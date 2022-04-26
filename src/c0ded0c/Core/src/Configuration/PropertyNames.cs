namespace c0ded0c.Core.Configuration
{
    public static class PropertyNames
    {
        public const string WorkingDirectory = nameof(IToolConfiguration.WorkingDirectory);
        public const string OutputDirectory = nameof(IToolConfiguration.OutputDirectory);
        public const string RunName = nameof(IToolConfiguration.RunName);
        public const string Version = nameof(IToolConfiguration.Version);
        public const string IsPacked = nameof(IToolConfiguration.IsPacked);
        public const string IsIncremental = nameof(IToolConfiguration.IsIncremental);
        public const string Modules = nameof(IToolConfiguration.Modules);
        public const string Plugins = nameof(IToolConfiguration.Plugins);
        public const string InputPaths = nameof(IToolConfiguration.InputPaths);
    }
}
