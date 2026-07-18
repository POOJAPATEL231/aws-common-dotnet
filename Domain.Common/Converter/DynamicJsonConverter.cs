using System.Text.Json.Serialization;
using System.Text.Json;

namespace Domain.Common.Converter
{
    public class DynamicJsonConverter : JsonConverter<dynamic>
    {
        public override dynamic? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetByte(out var valueAsByte))
                {
                    return valueAsByte;
                }
                if (reader.TryGetInt16(out var valueAsShort))
                {
                    return valueAsShort;
                }
                if (reader.TryGetInt32(out var valueAsInt))
                {
                    return valueAsInt;
                }
                if (reader.TryGetInt64(out var valueAsLong))
                {
                    return valueAsLong;
                }
                if (reader.TryGetDecimal(out var valueAsDecimal))
                {
                    return valueAsDecimal;
                }
            }
            else if (reader.TokenType == JsonTokenType.True)
            {
                return true;
            }
            else if (reader.TokenType == JsonTokenType.False)
            {
                return false;
            }
            return JsonDocument.ParseValue(ref reader).RootElement.Clone();
        }

        public override void Write(Utf8JsonWriter writer, dynamic value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
