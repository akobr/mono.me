using System.IO.Abstractions;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// A data set in a folder of its own below the temporary folder, written through the real file system (as a --path
// folder is read) and deleted with the test.
public sealed class TestSetFolder : IDisposable
{
    private static readonly IFileSystem FileSystem = new FileSystem();

    public TestSetFolder(string name)
    {
        Name = name;
        Path = FileSystem.Path.Combine(FileSystem.Path.GetTempPath(), $"dbcreator-it-{Guid.NewGuid():N}", name);
        FileSystem.Directory.CreateDirectory(Path);
    }

    public string Name { get; }

    public string Path { get; }

    // The set the seed tests share: annotation files of every type, two schemas, a template and three configurations,
    // and annotations.jsonl with a literal line and three generators below the files' responsibility. The target comes
    // from the request: the manifest names none.
    public static TestSetFolder Accounting()
    {
        return new TestSetFolder("accounting")
            .Write("set.json", """
                {
                  "Name": "accounting",
                  "Description": "A set of the integration tests of DbCreator.",
                  "ProjectDescription": "Seeded by the integration tests of DbCreator.",
                  "ViewDescription": "The view of the seed."
                }
                """)
            .Write("annotations/rst.billing.json", """{ "Title": "Billing", "Labels": [ "core" ] }""")
            .Write("annotations/unt.billing.invoice.json", """{ "Title": "Invoice", "UnitType": "file", "UnitDefinition": "invoice.pdf" }""")
            .Write("annotations/sbt.acme.json", """{ "Title": "Acme", "Values": { "tier": "gold", "since": "2024-01-01" } }""")
            .Write("annotations/cnt.acme.prod.json", "{}")
            .Write("annotations/usg.acme.billing.json", """{ "Values": { "plan": "standard" } }""")
            .Write("annotations/exe.acme.billing.prod.json", "{}")
            .Write("annotations/uxe.acme.billing.prod.invoice.json", "{}")
            .Write("schemas/exe.schema.json", """{ "type": "object", "properties": { "replicas": { "type": "integer", "minimum": 1 } } }""")
            .Write("schemas/rst.billing.usg.schema.json", """{ "type": "object", "properties": { "plan": { "enum": [ "standard", "premium" ] } } }""")
            .Write("templates/exe.template.json", """{ "replicas": 1 }""")
            .Write("configurations/rst.billing.conf.json", """{ "currency": "EUR", "retries": 3 }""")
            .Write("configurations/usg.acme.billing.conf.json", """{ "plan": "standard" }""")
            .Write("configurations/exe.acme.billing.prod.conf.json", """{ "replicas": 2 }""")
            .Write("annotations.jsonl", """
                // a customer without anything below it, then three generated ones that use the files' responsibility
                {"AnnotationKey":"sbt.globex","Title":"Globex"}
                {"$count":3,"AnnotationKey":"sbt.customer-{00}","Title":"Customer {n}","Labels":["generated"]}
                {"$count":3,"AnnotationKey":"cnt.customer-{00}.prod"}
                {"$count":3,"AnnotationKey":"usg.customer-{00}.billing"}
                """);
    }

    public TestSetFolder Write(string relativePath, string content)
    {
        var path = FileSystem.Path.Combine(Path, relativePath);
        FileSystem.Directory.CreateDirectory(FileSystem.Path.GetDirectoryName(path)!);
        FileSystem.File.WriteAllText(path, content);
        return this;
    }

    public void Dispose()
    {
        var root = FileSystem.Path.GetDirectoryName(Path)!;

        try
        {
            FileSystem.Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // a file still open on Windows: the temporary folder is cleaned up later
        }
    }
}
