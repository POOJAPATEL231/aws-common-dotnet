using System.Reflection;

namespace Persistence.Common.AWS.EntityManagement
{
    public class PropertyMetadata
    {
        public string Name { get; }
        public Type PropertyType { get; }
        public Func<object, object?> GetValue { get; }
        public Action<object, object?>? SetValue { get; }
        public bool IsSimpleType { get; }

        private PropertyMetadata(string name, Type propertyType, Func<object, object?> getValue, Action<object, object?>? setValue)
        {
            Name = name;
            PropertyType = propertyType;
            GetValue = getValue;
            SetValue = setValue;
            IsSimpleType = IsSimpleTypeCheck(propertyType);
        }

        public static PropertyMetadata Create(PropertyInfo prop)
        {
            return new PropertyMetadata(
                prop.Name,
                prop.PropertyType,
                InMemoryPropertyMetadata.GetGetter(prop),
                InMemoryPropertyMetadata.GetSetter(prop)
            );
        }

        // Method to check if a type is simple
        public static bool IsSimpleTypeCheck(Type type)
        {
            // List of predefined simple types
            Type[] simpleTypes = {
                typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
                typeof(TimeSpan), typeof(Guid), typeof(bool), typeof(byte), typeof(sbyte),
                typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
                typeof(ulong), typeof(char), typeof(float), typeof(double)
            };

            if (type.IsPrimitive || type.IsEnum || simpleTypes.Contains(type))
            {
                return true;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return IsSimpleTypeCheck(type.GetGenericArguments()[0]);
            }

            return false;
        }
    }
}
