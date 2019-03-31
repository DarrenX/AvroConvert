﻿namespace AvroConvert
{
    using System.Runtime.Serialization;
    using Avro;
    using Avro.File;
    using Avro.Generic;
    using Microsoft.Hadoop.Avro;

    public static partial class AvroConvert
    {
        public static string Serialize(object obj)
        {
            string result = "";

            Dupa dupa = new Dupa
            {
                cycek = new Cycki
                {
                    lewy = "spoko",
                    prawy = 2137
                },
                numebr = 111111
            };

            Dupa dupa2 = new Dupa
            {
                cycek = new Cycki
                {
                    lewy = "loko",
                    prawy = 2137
                },
                numebr = 2135
            };

            var xd = AvroSerializer.Create<Dupa>().WriterSchema.ToString();
            

            var writer = DataFileWriter<Dupa>.OpenWriter(new GenericDatumWriter<Dupa>(Schema.Parse(xd)), "result.avro");
            writer.Append(dupa);
            writer.Append(dupa2);
            writer.Close();
            

            return result;
        }
    }

    [DataContract(Name = "Demo", Namespace = "pubsub.demo")]
    public class Dupa
    {
        [DataMember(Name = "value")]
        public Cycki cycek { get; set; }

        [DataMember(Name = "d")]
        public int numebr { get; set; }
    }

    [DataContract(Name = "Cycek", Namespace = "pubsub.demo")]
    public class Cycki
    {
        [DataMember(Name = "value")]
        public string lewy { get; set; }
        [DataMember(Name = "lol")]
        public long prawy { get; set; }
    }
}
