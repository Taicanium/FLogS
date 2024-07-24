using System;
using System.Globalization;
using System.Reflection;

namespace FLogS
{
    public class ContextSettings
    {
        private readonly string _versionString = "FLogS — Version " + Assembly.GetExecutingAssembly().GetName().Version + " © Taica, " + GetBuildYear(Assembly.GetExecutingAssembly());

        public string VersionString => _versionString;

        private static int GetBuildYear(Assembly assembly)
        {
            const string BuildVersionMetadataPrefix = "+build";
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            if (attribute?.InformationalVersion != null)
            {
                string value = attribute.InformationalVersion;
                int index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0 && DateTime.TryParseExact(value[(index + BuildVersionMetadataPrefix.Length)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                    return result.Year;
            }

            return default;
        }
    }
}
