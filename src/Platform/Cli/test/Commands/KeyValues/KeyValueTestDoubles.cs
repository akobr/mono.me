using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using _42.Platform.Cli.Commands;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Sharprompt;

namespace _42.Platform.Cli.UnitTests.Commands.KeyValues;

// The context of every key/value command test: org1.proj1 / default.
internal static class KeyValueTestContext
{
    public static Mock<ICommandContext> Create(string view = "default")
    {
        var context = new Mock<ICommandContext>();
        context.SetupGet(c => c.OrganizationName).Returns("org1");
        context.SetupGet(c => c.ProjectName).Returns("proj1");
        context.SetupGet(c => c.ViewName).Returns(view);
        return context;
    }

    public static KeyValue Pair(string key, KeyValueProtection protection, string? value, long version = 1) => new()
    {
        Key = key,
        Protection = protection,
        Value = value,
        Version = version,
        Author = "account: ada",
        CreatedBy = "account: ada",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    public static ApiException NotFound(string key) => Error(HttpStatusCode.NotFound, $"The key '{key}' does not exist.", "KeyValueNotFound");

    public static ApiException Error(HttpStatusCode status, string message, string? errorCode) => new(
        "The HTTP status code of the response was not expected.",
        (int)status,
        errorCode is null ? message : $$"""{"Message":"{{message}}","ErrorCode":"{{errorCode}}"}""",
        new Dictionary<string, IEnumerable<string>>(),
        null);

    public static KeyValueBulkResult Bulk(params (string Key, KeyValueBulkItemResultOutcome Outcome)[] items) => new()
    {
        Items = items.Select(item => new KeyValueBulkItemResult { Key = item.Key, Outcome = item.Outcome }).ToList(),
    };

    public static KeyValueImportResult Import(bool dryRun, bool complete, params (string Key, KeyValueImportItemResultAction Action)[] items) => new()
    {
        IsDryRun = dryRun,
        IsComplete = complete,
        Items = items.Select(item => new KeyValueImportItemResult { Key = item.Key, Action = item.Action, Protection = KeyValueImportItemResultProtection.Public }).ToList(),
    };

    // A file system with the given text files.
    public static IFileSystem Files(params (string Path, string Content)[] files)
    {
        var file = new Mock<IFile>();

        foreach (var (path, content) in files)
        {
            file.Setup(f => f.Exists(path)).Returns(true);
            file.Setup(f => f.ReadAllTextAsync(path, It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(content);
            file.Setup(f => f.ReadAllText(path)).Returns(content);
        }

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.SetupGet(fs => fs.File).Returns(file.Object);
        return fileSystem.Object;
    }
}

// A recipient with a key pair, for the client-side encryption tests.
internal sealed class TestRecipient : IDisposable
{
    public TestRecipient(string id)
    {
        Id = id;
        Rsa = RSA.Create(3072);
        var jwk = RsaPublicJwk.FromRsa(Rsa);
        Model = new ClientEncryptionRecipient
        {
            Id = id,
            Name = id,
            IsEnabled = true,
            PublicKey = new RsaJsonWebKey { Kty = "RSA", N = jwk.N, E = jwk.E },
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "test",
        };
    }

    public string Id { get; }

    public RSA Rsa { get; }

    public ClientEncryptionRecipient Model { get; }

    public string PrivateKeyPem => Rsa.ExportPkcs8PrivateKeyPem();

    public string Decrypt(string document) => ClientSecretDecryptor.Decrypt(document, Id, Rsa);

    public void Dispose() => Rsa.Dispose();
}

internal static class ConsolePrompts
{
    public static void Password(this RecordingConsole console, string value)
    {
        console.Mock.Setup(c => c.Password(It.IsAny<PasswordOptions>())).Returns(value);
    }

    public static void Input(this RecordingConsole console, string value)
    {
        console.Mock.Setup(c => c.Input(It.IsAny<InputOptions<string>>())).Returns(value);
    }

    public static void Confirm(this RecordingConsole console, bool value)
    {
        console.Mock.Setup(c => c.Confirm(It.IsAny<ConfirmOptions>())).Returns(value);
    }
}
