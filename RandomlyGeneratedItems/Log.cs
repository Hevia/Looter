using BepInEx.Logging;
using System;
using System.Collections.Generic;

namespace RandomlyGeneratedItems
{
    internal static class Log
    {
        private static ManualLogSource _logSource;

        internal static void Init(ManualLogSource logSource)
        {
            _logSource = logSource;
        }

        internal static void Debug(object data) => _logSource.LogDebug(data);
        internal static void Error(object data) => _logSource.LogError(data);
        internal static void Fatal(object data) => _logSource.LogFatal(data);
        internal static void Info(object data) => _logSource.LogInfo(data);
        internal static void Message(object data) => _logSource.LogMessage(data);
        internal static void Warning(object data) => _logSource.LogWarning(data);

        public static Dictionary<string, object> GetProperties(object obj)
        {
            var properties = new Dictionary<string, object>();

            // Get the type of the object
            Type type = obj.GetType();

            // Get all the properties of the object
            var objProperties = type.GetProperties();

            // Iterate over each property and add its key-value pair to the dictionary
            foreach (var propertyInfo in objProperties)
            {
                var propertyName = propertyInfo.Name;
                var propertyValue = propertyInfo.GetValue(obj);

                properties.Add(propertyName, propertyValue);
            }

            return properties;
        }
    }
}