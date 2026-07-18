using System.Text;
using System.Text.Json;

namespace Utils.Common.Utils
{
    /// <summary>
    /// Generic object &lt;-&gt; byte[] serialization helpers used by the cache implementations.
    /// Objects are round-tripped as UTF-8 JSON; strings and raw byte arrays pass through unchanged.
    /// </summary>
    public static class ByteArrayUtil
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static byte[] ToByteArray<T>(this T item)
        {
            return item switch
            {
                null => Array.Empty<byte>(),
                byte[] bytes => bytes,
                string s => Encoding.UTF8.GetBytes(s),
                _ => JsonSerializer.SerializeToUtf8Bytes(item, _options)
            };
        }

        public static T? FromByteArray<T>(this byte[] data)
        {
            if (data.Length == 0)
            {
                return default;
            }

            if (typeof(T) == typeof(byte[]))
            {
                return (T)(object)data;
            }

            if (typeof(T) == typeof(string))
            {
                return (T)(object)Encoding.UTF8.GetString(data);
            }

            return JsonSerializer.Deserialize<T>(data, _options);
        }
    }
}
