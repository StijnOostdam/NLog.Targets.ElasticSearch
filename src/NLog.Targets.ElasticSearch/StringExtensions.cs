using System;
#if NET45
using System.Configuration;
#else
using System.IO;
using Microsoft.Extensions.Configuration;
#endif

namespace NLog.Targets.ElasticSearch
{
    internal static class StringExtensions
    {
        public static string GetConnectionString(this string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var value = name.GetEnvironmentVariable();
            if (!string.IsNullOrEmpty(value))
                return value;

#if NET45
            var connectionString = ConfigurationManager.ConnectionStrings[name];
            return connectionString?.ConnectionString;
#else
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", true, true);

            var configuration = builder.Build();

            return configuration.GetConnectionString(name);
#endif
        }

        private static string GetEnvironmentVariable(this string name)
        {
            return string.IsNullOrEmpty(name) ? null : Environment.GetEnvironmentVariable(name);
        }
    }
}