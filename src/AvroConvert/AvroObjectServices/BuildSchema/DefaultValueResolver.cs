﻿// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License.  You may obtain a copy
// of the License at http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED
// WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
// 
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

/** Modifications copyright(C) 2020 Adrian Strugała **/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SolTechnology.Avro.AvroObjectServices.Schemas;
using SolTechnology.Avro.AvroObjectServices.Schemas.Abstract;

namespace SolTechnology.Avro.AvroObjectServices.BuildSchema;

/// <summary>
/// This class is responsible for parsing a JSON string according to a given JSON
/// schema and returning the corresponding C# value as object.
/// </summary>
internal sealed class DefaultValueResolver
{
    private readonly Dictionary<Type, Func<string, object>> parsersWithoutSchema;
    private readonly Dictionary<Type, Func<TypeSchema, string, object>> parsersWithSchema;

    internal DefaultValueResolver()
    {
        this.parsersWithoutSchema = new Dictionary<Type, Func<string, object>>
        {
            { typeof(BooleanSchema), json => ConvertTo<bool>(json) },
            { typeof(IntSchema), json => ConvertTo<int>(json) },
            { typeof(LongSchema), json => ConvertTo<long>(json) },
            { typeof(FloatSchema), json => ConvertTo<float>(json) },
            { typeof(DoubleSchema), json => ConvertTo<double>(json) },
            { typeof(StringSchema), json => json },
            { typeof(BytesSchema), ConvertToBytes },
            { typeof(NullSchema), this.ParseNull },
            { typeof(UuidSchema), this.ParseUuid },
        };

        this.parsersWithSchema = new Dictionary<Type, Func<TypeSchema, string, object>>
        {
            { typeof(EnumSchema), this.ParseEnum },
            { typeof(ArraySchema), this.ParseArray },
            { typeof(UnionSchema), this.ParseUnion },
            { typeof(MapSchema), this.ParseMap },
            { typeof(RecordSchema), this.ParseRecord },
            { typeof(FixedSchema), this.ParseFixed }
        };
    }

    /// <summary>
    /// Parses a JSON string according to given schema and returns the corresponding object.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="json">The JSON object.</param>
    /// <returns>The object.</returns>
    internal object Parse(TypeSchema schema, string json)
    {
        if (this.parsersWithoutSchema.ContainsKey(schema.GetType()))
        {
            return this.parsersWithoutSchema[schema.GetType()](json);
        }

        if (this.parsersWithSchema.ContainsKey(schema.GetType()))
        {
            return this.parsersWithSchema[schema.GetType()](schema, json);
        }

        throw new SerializationException(
            string.Format(CultureInfo.InvariantCulture, "Unknown schema type '{0}'.", schema.GetType()));
    }

    private object ParseNull(string json)
    {
        if (!string.IsNullOrEmpty(json))
        {
            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture, "'{0}' is not valid. Null is expected.", json));
        }
        return null;
    }

    private object ParseEnum(TypeSchema schema, string jsonObject)
    {
        var enumSchema = (EnumSchema)schema;
        string enumValue;

        if (int.TryParse(jsonObject, out var index))
        {
            enumValue = enumSchema.GetSymbolByValue(index);
        }
        else if (!enumSchema.Symbols.Contains(jsonObject))
        {
            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid enum value.", jsonObject));
        }
        else
        {
            enumValue = jsonObject;
        }

        return enumValue;
    }

    private object[] ParseArray(TypeSchema schema, string jsonObject)
    {
        var arraySchema = (ArraySchema)schema;
        return JArray
            .Parse(jsonObject)
            .Select(i => this.Parse(arraySchema.ItemSchema, i.ToString()))
            .ToArray();
    }

    private object ParseUnion(TypeSchema schema, string jsonObject)
    {
        var unionSchema = (UnionSchema)schema;
        return this.Parse(unionSchema.Schemas[0], jsonObject);
    }

    private Dictionary<string, object> ParseMap(TypeSchema schema, string jsonObject)
    {
        var mapSchema = (MapSchema)schema;
        return JsonConvert
            .DeserializeObject<Dictionary<string, JToken>>(jsonObject)
            .Select(d => new { d.Key, Value = this.Parse(mapSchema.ValueSchema, d.Value.ToString()) })
            .ToDictionary(o => o.Key, o => o.Value);
    }

    private Dictionary<string, object> ParseRecord(TypeSchema schema, string jsonObject)
    {
        var recordSchema = (RecordSchema)schema;
        var result = new Dictionary<string, object>();

        var data = JsonConvert.DeserializeObject<Dictionary<string, JToken>>(jsonObject);

        foreach (var datum in data)
        {
            var matchedRecord = recordSchema.Fields.FirstOrDefault(field => field.Name == datum.Key);
            if (matchedRecord == null)
            {
                throw new SerializationException(
                    string.Format(CultureInfo.InvariantCulture,
                                  "Could not set default value because JSON object contains fields that do not exist in the schema."));
            }
            result[matchedRecord.Name] = this.Parse(matchedRecord.TypeSchema, datum.Value.ToString());
        }

        return result;
    }

    private byte[] ParseFixed(TypeSchema schema, string jsonObject)
    {
        var fixedSchema = (FixedSchema)schema;
        var result = ConvertToBytes(jsonObject);

        if (result.Length != fixedSchema.Size)
        {
            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture, "'{0}' size does not match the size of fixed schema node.", jsonObject));
        }

        return result;
    }

    private object ParseUuid(string json)
    {
        if (Guid.TryParse(json, out var guid))
        {
            return guid;
        }

        throw new SerializationException(
            string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid GUID", json));
    }

    private static byte[] ConvertToBytes(string jsonObject)
    {
        var result = new List<byte>();

        for (var i = 0; i < jsonObject.Length; i += char.IsSurrogatePair(jsonObject, i) ? 2 : 1)
        {
            var codepoint = char.ConvertToUtf32(jsonObject, i);

            if (codepoint > 255)
            {
                throw new SerializationException(string.Format(CultureInfo.InvariantCulture, "'{0}' contains invalid characters.", jsonObject));
            }

            result.Add((byte)codepoint);
        }

        return result.ToArray();
    }

    private static T ConvertTo<T>(string jsonObject)
    {
        // https://github.com/dotnet/corefx/pull/8093
        var converter = TypeDescriptor.GetConverter(typeof(T));
        try
        {
            return (T)converter.ConvertFromString(jsonObject);
        }
        catch (Exception e)
        {
            throw new SerializationException(
                string.Format(CultureInfo.InvariantCulture, "Could not parse '{0}' as '{1}'.", jsonObject, typeof(T)),
                e);
        }
    }
}
