using System.ComponentModel;

namespace Domain.Common.Extensions
{
    public static class GenericTypeExtensions
    {
        public static string GetGenericTypeName(this Type type)
        {
            if (type.IsGenericType)
            {
                var genericTypes = string.Join(",", type.GetGenericArguments().Select(t => t.Name).ToArray());
                return $"{type.Name.Remove(type.Name.IndexOf('`'))}<{genericTypes}>";
            }
            else
            {
                return type.Name;
            }
        }

        public static string GetGenericTypeName(this object @object)
        {
            return @object.GetType().GetGenericTypeName();
        }

        /// <summary>
        /// Converts the string representation of this type to its actual type.
        /// A return value indicates whether the conversion succeeded.
        /// </summary>
        /// <param name="type">Type to convert string to.</param>
        /// <param name="s">A string containing a value to convert.</param>
        /// <param name="result">Converted value if successful.</param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParse(this Type type, string? s, out object? result)
        {
            result = null;
            if (s is null)
            {
                if (type == typeof(string) || Nullable.GetUnderlyingType(type) is not null)
                {
                    return true;
                }
            }
            else
            {
                if (type.IsEnum && Enum.TryParse(type, s, true, out result) && Enum.IsDefined(type, result!))
                {
                    return true;
                }
                var converter = TypeDescriptor.GetConverter(type);
                if (converter.IsValid(s))
                {
                    result = converter.ConvertFromString(s);
                    return true;
                }
            }
            return false;
        }
    }
}
