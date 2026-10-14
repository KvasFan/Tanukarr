using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Qualities
{
    [System.Text.Json.Serialization.JsonConverter(typeof(CodecMultiplierOverridesConverter))]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonsoftCodecMultiplierOverridesConverter))]
    public class CodecMultiplierOverrides : Dictionary<string, decimal>, IEmbeddedDocument
    {
        public CodecMultiplierOverrides()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public CodecMultiplierOverrides(IDictionary<string, decimal> values)
            : this()
        {
            foreach (var pair in values)
            {
                var name = CodecSizeNormalizer.DefaultMultipliers.Keys.FirstOrDefault(key => string.Equals(key, pair.Key, StringComparison.OrdinalIgnoreCase)) ?? pair.Key;
                Add(name, pair.Value);
            }
        }
    }

    public class CodecMultiplierOverridesConverter : System.Text.Json.Serialization.JsonConverter<CodecMultiplierOverrides>
    {
        public override CodecMultiplierOverrides Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(ref reader, options);
            return values == null ? null : new CodecMultiplierOverrides(values);
        }

        public override void Write(Utf8JsonWriter writer, CodecMultiplierOverrides value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var pair in value)
            {
                writer.WriteNumber(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
        }
    }

    public class NewtonsoftCodecMultiplierOverridesConverter : Newtonsoft.Json.JsonConverter<CodecMultiplierOverrides>
    {
        public override CodecMultiplierOverrides ReadJson(JsonReader reader, Type objectType, CodecMultiplierOverrides existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            var values = serializer.Deserialize<Dictionary<string, decimal>>(reader);
            return values == null ? null : new CodecMultiplierOverrides(values);
        }

        public override void WriteJson(JsonWriter writer, CodecMultiplierOverrides value, Newtonsoft.Json.JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            foreach (var pair in value)
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteValue(pair.Value);
            }

            writer.WriteEndObject();
        }
    }
}
