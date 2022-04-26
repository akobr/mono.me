using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace c0ded0c.Core.Configuration
{
    public abstract class ConfigurationSegmentReader
    {
        private readonly IReadOnlyDictionary<string, string> properties;

        protected ConfigurationSegmentReader(IReadOnlyDictionary<string, string> configuration)
        {
            properties = configuration;
        }

        protected string Get([CallerMemberName]string propertyName = "", string defaultValue = "")
        {
            return properties.TryGetValue(propertyName, out string? value)
                ? value
                : defaultValue;
        }

        protected IEnumerable<string> GetEnumerable([CallerMemberName] string propertyName = "")
        {
            return Get(propertyName).Split(Constants.LIST_SEPARATOR, StringSplitOptions.RemoveEmptyEntries);
        }

        protected int GetInt([CallerMemberName] string propertyName = "", int defaultValue = default)
        {
            return int.TryParse(Get(propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : defaultValue;
        }

        protected double GetDouble([CallerMemberName] string propertyName = "", double defaultValue = default)
        {
            return double.TryParse(Get(propertyName), NumberStyles.Number, CultureInfo.InvariantCulture, out double value)
                ? value
                : defaultValue;
        }

        protected bool GetBoolean([CallerMemberName] string propertyName = "", bool defaultValue = default)
        {
            return bool.TryParse(Get(propertyName), out bool value)
                ? value
                : defaultValue;
        }

        protected static IImmutableDictionary<string, string> SetTo<TConfiguration>(
            IImmutableDictionary<string, string> properties,
            TConfiguration configuration)
        {
            Type configurationType = typeof(TConfiguration);

            foreach (PropertyInfo property in configurationType.GetProperties())
            {
                object? value = property.GetValue(configuration);

                if (value != null)
                {
                    properties = properties.SetItem(property.Name, GetTextValue(value));
                }
            }

            return properties;
        }

        private static string GetTextValue(object value)
        {
            switch (value)
            {
                case IEnumerable<string> enumerable:
                    return string.Join(Constants.DEFAULT_LIST_SEPARATOR, enumerable);

                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);

                default:
                    return value.ToString() ?? string.Empty;
            }
        }
    }
}
