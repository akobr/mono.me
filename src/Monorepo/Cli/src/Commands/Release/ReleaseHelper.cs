using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Monorepo.Cli.ConventionalCommits;
using _42.Monorepo.Texo.Core.Markdown.Builder;

namespace _42.Monorepo.Cli.Commands.Release
{
    public static class ReleaseHelper
    {
        public static bool UpdateVersionFile(string version, string versionFilePath)
        {
            JsonNode? versionNode;
            using (var versionFileStream = new FileStream(versionFilePath, FileMode.Open, FileAccess.Read))
            {
                versionNode = JsonNode.Parse(versionFileStream, documentOptions: new JsonDocumentOptions() { CommentHandling = JsonCommentHandling.Skip });
            }

            if (versionNode is null)
            {
                return false;
            }

            var rootObject = versionNode.AsObject();
            var versionProperty = rootObject["version"];

            if (versionProperty is null)
            {
                rootObject.Add("version", version);
            }
            else
            {
                rootObject["version"] = version;
            }

            using (var versionFileStream = new FileStream(versionFilePath, FileMode.Create, FileAccess.Write))
            {
                using (var writer = new Utf8JsonWriter(versionFileStream, new JsonWriterOptions() { Indented = true }))
                {
                    versionNode.WriteTo(writer, new JsonSerializerOptions { WriteIndented = true });
                }

                versionFileStream.Write(Encoding.UTF8.GetBytes(Environment.NewLine));
            }

            return true;
        }

        public static IMarkdownBuilder BuildReleaseNotes(ReleasePreview preview)
        {
            var breakChanges = preview.MajorChanges;
            var unknownChanges = preview.UnknownChanges;

            var features = preview.MinorChanges
                .Where(m => m.Type is "feat" or ":sparkles:")
                .ToList();

            var minorChanges = preview.MinorChanges
                .Where(m => m.Type is not "feat" or ":sparkles:")
                .Concat(preview.PathChanges)
                .ToList();

            var markdownBuilder = new MarkdownBuilder();
            markdownBuilder.Header($"Release v.{preview.Version}");
            markdownBuilder.Bullet($"Tagged as `{preview.Tag}`");
            markdownBuilder.Bullet($"At {DateTime.Today.ToShortDateString()} {DateTime.Now.ToLongTimeString()}");

            if (breakChanges.Count > 0)
            {
                markdownBuilder.WriteLine();
                markdownBuilder.Header("Breaking changes", 2);

                foreach (var change in breakChanges)
                {
                    markdownBuilder.Bullet($"{change.Type}: {change.Description}");
                    WriteLinks(markdownBuilder, change);
                }
            }

            if (features.Count > 0)
            {
                markdownBuilder.WriteLine();
                markdownBuilder.Header("New features", 2);

                foreach (var feature in features)
                {
                    markdownBuilder.Bullet(feature.Description);
                    WriteLinks(markdownBuilder, feature);
                }
            }

            if (minorChanges.Count > 0)
            {
                markdownBuilder.WriteLine();
                markdownBuilder.Header("Minor changes", 2);

                foreach (var change in minorChanges)
                {
                    markdownBuilder.Bullet($"{change.Type}: {change.Description}");
                    WriteLinks(markdownBuilder, change);
                }
            }

            if (unknownChanges.Count > 0)
            {
                markdownBuilder.WriteLine();
                markdownBuilder.Header("Unknown changes", 2);

                foreach (var unknown in unknownChanges)
                {
                    markdownBuilder.Bullet($"{unknown.Sha[..7]}: {unknown.MessageShort}");
                }
            }

            return markdownBuilder;
        }

        private static void WriteLinks(MarkdownBuilder markdownBuilder, IConventionalMessage change)
        {
            foreach (string urlLink in change.Links)
            {
                markdownBuilder.Bullet(1);
                markdownBuilder.Link(urlLink, urlLink);
                markdownBuilder.WriteLine();
            }
        }
    }
}
