using System;
using System.IO.Abstractions;
using System.Reflection;

namespace _42.Platform.Storyteller.DbCreator.Configuration;

public class LoggingOptions
{
    public const string SECTION = "logging";

    private static readonly IFileSystem FileSystem = new FileSystem();

    public string LogFilePath { get; set; } = "logs/cli.log";

    public string? SentryDsn { get; set; }

    public string GetTemplateLogFullPath()
    {
        var directory = FileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory;
        return FileSystem.Path.Combine(directory, LogFilePath);
    }

    public string GetTodayLogFullPath()
    {
        var fullPath = GetTemplateLogFullPath();
        var directory = FileSystem.Path.GetDirectoryName(fullPath);
        var prefix = FileSystem.Path.GetFileNameWithoutExtension(fullPath);
        var extension = FileSystem.Path.GetExtension(fullPath);
        return $"{directory}{FileSystem.Path.DirectorySeparatorChar}{prefix}{DateTime.Now:yyyyMMdd}{extension}";
    }
}
