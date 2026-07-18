using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Utils.Common.Utils
{
    public static class JsonUtil
    {
        public static Stream? GetJsonStream(this string json, string partitionKeyPath, string partitionKey, string? id = null, string? eTag = null)
        {
            if (!string.IsNullOrEmpty(json))
            {
                JsonNode jsonNode = JsonNode.Parse(json);
                if (jsonNode != null)
                {
                    jsonNode["id"] = id ?? Guid.NewGuid().ToString();
                    jsonNode[partitionKeyPath] = partitionKey;
                    if (!string.IsNullOrEmpty(eTag))
                    {
                        jsonNode["_etag"] = eTag;
                    }

                    MemoryStream memoryStream = new MemoryStream();
                    using Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter((Stream)memoryStream, default(JsonWriterOptions));
                    jsonNode.WriteTo(utf8JsonWriter);
                    utf8JsonWriter.Flush();
                    return memoryStream;
                }
            }

            return null;
        }

        public static MemoryStream SerializeJsonIntoStream(object value)
        {
            MemoryStream memoryStream = new MemoryStream();
            JsonSerializer.Serialize(memoryStream, value, value.GetType(), new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            return memoryStream;
        }

        public static T? TryParseTo<T>(this string s)
        {
            T result = default(T);
            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    result = JsonSerializer.Deserialize<T>(s, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return result;
                }
                catch
                {
                }
            }

            return result;
        }

        public static T? ParseJsonFile<T>(string path)
        {
            T result = default(T);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }

            return result;
        }

        public static string Serialize<T>(this T o)
        {
            return JsonSerializer.Serialize(o, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }

        public static Dictionary<string, object?>? ToDictionary<T>(this T o)
        {
            string text = o.Serialize();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            return JsonSerializer.Deserialize<Dictionary<string, object>>(text, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
        }
    }
}
