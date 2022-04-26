using System.IO;
using c0ded0c.Core;
using Microsoft.Extensions.FileProviders;

namespace c0ded0c.Cli
{
    public class PhysicalStorer : IStorer
    {
        private string workingPath = string.Empty;

        public void SetWorkingPath(string workingPath)
        {
            this.workingPath = workingPath;
        }

        public Stream GetOrCreateStream(string path)
        {
            path = Path.Combine(workingPath, path);

            if (!File.Exists(path))
            {
                string? directory = Path.GetDirectoryName(path);
                Directory.CreateDirectory(directory);
            }

            return new FileStream(path, FileMode.Create, FileAccess.Write);
        }

        public Stream? GetStream(string path)
        {
            path = Path.Combine(workingPath, path);

            if (!File.Exists(path))
            {
                return null;
            }

            return File.OpenRead(path);
        }

        public IFileProvider OpenDirectory(string path)
        {
            path = Path.Combine(workingPath, path);
            return new PhysicalFileProvider(Path.GetFullPath(path));
        }
    }
}
