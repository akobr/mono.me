using System;

namespace _42.Monorepo.Texo.Core.Markdown
{
    public interface ILink
    {
        string Title { get; }

        Uri Address { get; }
    }
}
