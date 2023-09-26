using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowFlyer2
{
    using Newtonsoft.Json;
    using System;
    using System.Windows.Forms;

    public class HexConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(int);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Integer)
            {
                long vIn = (long)reader.Value;
                uint vOut = Convert.ToUInt32(vIn);
                Keys key = (Keys)vOut;

                return key;
            }
            return 0;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Keys keysValue)
            {
                writer.WriteValue((uint)keysValue);
            }
        }
    }
}

