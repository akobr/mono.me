using System.IO;
using Microsoft.Extensions.FileProviders;

namespace c0ded0c.Core
{
    public interface IStorer
    {
        void SetWorkingPath(string workingPath);

        Stream? GetStream(string path);

        Stream GetOrCreateStream(string path);

        IFileProvider OpenDirectory(string path);
    }
}
