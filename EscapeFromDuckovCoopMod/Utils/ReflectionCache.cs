using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace EscapeFromDuckovCoopMod.Utils
{
    public static class ReflectionCache
    {
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new();
        private static readonly Dictionary<string, MethodInfo> _methodCache = new();
        private static readonly Dictionary<string, PropertyInfo> _propertyCache = new();
        private static readonly Dictionary<string, Delegate> _delegateCache = new();
        
        public static FieldInfo GetField(Type type, string fieldName)
        {
            string key = $"{type.FullName}.{fieldName}";
            if (_fieldCache.TryGetValue(key, out var field))
                return field;
            
            field = AccessTools.Field(type, fieldName);
            if (field != null)
                _fieldCache[key] = field;
            
            return field;
        }
        
        public static MethodInfo GetMethod(Type type, string methodName, Type[] parameterTypes = null)
        {
            string key = $"{type.FullName}.{methodName}";
            if (parameterTypes != null)
            {
                key += $"({string.Join(",", Array.ConvertAll(parameterTypes, t => t.Name))})";
            }
            
            if (_methodCache.TryGetValue(key, out var method))
                return method;
            
            method = parameterTypes == null 
                ? AccessTools.Method(type, methodName) 
                : AccessTools.Method(type, methodName, parameterTypes);
                
            if (method != null)
                _methodCache[key] = method;
            
            return method;
        }
        
        public static PropertyInfo GetProperty(Type type, string propertyName)
        {
            string key = $"{type.FullName}.{propertyName}";
            if (_propertyCache.TryGetValue(key, out var property))
                return property;
            
            property = AccessTools.Property(type, propertyName);
            if (property != null)
                _propertyCache[key] = property;
            
            return property;
        }
        
        public static T GetFieldValue<T>(object instance, string fieldName)
        {
            if (instance == null) return default;
            
            var field = GetField(instance.GetType(), fieldName);
            if (field == null) return default;
            
            return (T)field.GetValue(instance);
        }
        
        public static void SetFieldValue(object instance, string fieldName, object value)
        {
            if (instance == null) return;
            
            var field = GetField(instance.GetType(), fieldName);
            field?.SetValue(instance, value);
        }
        
        public static T InvokeMethod<T>(object instance, string methodName, params object[] parameters)
        {
            if (instance == null) return default;
            
            Type[] paramTypes = parameters != null && parameters.Length > 0
                ? Array.ConvertAll(parameters, p => p?.GetType() ?? typeof(object))
                : null;
            
            var method = GetMethod(instance.GetType(), methodName, paramTypes);
            if (method == null) return default;
            
            return (T)method.Invoke(instance, parameters);
        }
        
        public static void InvokeMethod(object instance, string methodName, params object[] parameters)
        {
            if (instance == null) return;
            
            Type[] paramTypes = parameters != null && parameters.Length > 0
                ? Array.ConvertAll(parameters, p => p?.GetType() ?? typeof(object))
                : null;
            
            var method = GetMethod(instance.GetType(), methodName, paramTypes);
            method?.Invoke(instance, parameters);
        }
        
        public static Action<T> CreateSetter<T>(Type type, string fieldName)
        {
            string key = $"Setter_{type.FullName}.{fieldName}";
            
            if (_delegateCache.TryGetValue(key, out var cached))
                return (Action<T>)cached;
            
            var field = GetField(type, fieldName);
            if (field == null) return null;
            
            var setter = AccessTools.FieldRefAccess<T>(type, fieldName);
            if (setter != null)
            {
                Action<T> action = (value) => setter(null) = value;
                _delegateCache[key] = action;
                return action;
            }
            
            return null;
        }
        
        public static Func<T> CreateGetter<T>(Type type, string fieldName)
        {
            string key = $"Getter_{type.FullName}.{fieldName}";
            
            if (_delegateCache.TryGetValue(key, out var cached))
                return (Func<T>)cached;
            
            var field = GetField(type, fieldName);
            if (field == null) return null;
            
            var getter = AccessTools.FieldRefAccess<T>(type, fieldName);
            if (getter != null)
            {
                Func<T> func = () => getter(null);
                _delegateCache[key] = func;
                return func;
            }
            
            return null;
        }
        
        public static void ClearCache()
        {
            _fieldCache.Clear();
            _methodCache.Clear();
            _propertyCache.Clear();
            _delegateCache.Clear();
        }
    }
}

