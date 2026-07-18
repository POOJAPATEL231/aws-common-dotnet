using System.Text.Json;

namespace Domain.Common.Extensions
{
    public static class DictionaryExtensions
    {
        public static T? GetValueOrDefault<T>(this Dictionary<string, object?> dictionary, string key)
        {
            if (dictionary.TryGetValue(key, out var value) && value is not null)
            {
                if (value is string stringValue)
                {
                    if (typeof(T).TryParse(stringValue, out var result))
                    {
                        return (T?)result;
                    }
                    return default;
                }
                else if (value is JsonElement element)
                {
                    return element.Deserialize<T>();
                }
                return (T?)value;
            }
            return default;
        }

        public static Dictionary<TKey, TValue> TryAdd<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, Dictionary<TKey, TValue> items)
            where TKey : notnull
        {
            foreach (var item in items)
            {
                dictionary.TryAdd(item.Key, item.Value);
            }
            return dictionary;
        }

        public static void AddOrReplace<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
            where TKey : notnull
        {
            if (!dictionary.TryAdd(key, value))
            {
                dictionary[key] = value;
            }
        }

        public static Dictionary<TKey, TValue> AddRange<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, Dictionary<TKey, TValue> items)
            where TKey : notnull
        {
            foreach (var item in items)
            {
                dictionary.Add(item.Key, item.Value);
            }
            return dictionary;
        }
    }
}
