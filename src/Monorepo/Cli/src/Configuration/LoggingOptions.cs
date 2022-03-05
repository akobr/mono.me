using System;
using System.IO;
using System.Reflection;

namespace _42.Monorepo.Cli.Configuration
{
    public class LoggingOptions
    {
        public string LogFilePath { get; set; } = "logs/cli.log";

        public string? SentryDsn { get; set; }

        public string GetTemplateLogFullPath()
        {
            var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
            return Path.Combine(directory, LogFilePath);
        }

        public string GetTodayLogFullPath()
        {
            var fullPath = GetTemplateLogFullPath();
            var directory = Path.GetDirectoryName(fullPath);
            var prefix = Path.GetFileNameWithoutExtension(fullPath);
            var extension = Path.GetExtension(fullPath);
            return $"{directory}{Path.DirectorySeparatorChar}{prefix}{DateTime.Now:yyyyMMdd}{extension}";
        }
    }
}
