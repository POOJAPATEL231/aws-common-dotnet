using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Persistence.Common.AWS.EntityManagement
{
    public static class InMemoryPropertyMetadata
    {
        private static readonly ConcurrentDictionary<(Type?, string), Func<object, object?>> _getterCache = new();
        private static readonly ConcurrentDictionary<(Type?, string), Action<object, object?>> _setterCache = new();

        public static Func<object, object?> GetGetter(PropertyInfo prop)
        {
            var key = (prop.DeclaringType, prop.Name);
            if (!_getterCache.TryGetValue(key, out var getter))
            {
                getter = CreateGetterDelegate(prop);
                _getterCache[key] = getter;
            }
            return getter;
        }

        public static Action<object, object?>? GetSetter(PropertyInfo prop)
        {
            var key = (prop.DeclaringType, prop.Name);
            if (!_setterCache.TryGetValue(key, out var setter))
            {
                setter = CreateSetterDelegate(prop);
                if (setter != null)
                {
                    _setterCache[key] = setter;
                }
            }
            return setter;
        }

        private static Func<object, object?> CreateGetterDelegate(PropertyInfo prop)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var convertedInstance = Expression.Convert(instance, prop.DeclaringType!);
            var propertyAccess = Expression.Property(convertedInstance, prop);
            var convertResult = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<object, object?>>(convertResult, instance).Compile();
        }

        private static Action<object, object?>? CreateSetterDelegate(PropertyInfo prop)
        {
            if (!prop.CanWrite)
            {
                return null;
            }

            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(object), "value");
            var convertedInstance = Expression.Convert(instance, prop.DeclaringType!);
            var convertedValue = Expression.Convert(value, prop.PropertyType);

            var setterCall = Expression.Call(convertedInstance, prop.GetSetMethod(true)!, convertedValue);
            return Expression.Lambda<Action<object, object?>>(setterCall, instance, value).Compile();
        }
    }
}
