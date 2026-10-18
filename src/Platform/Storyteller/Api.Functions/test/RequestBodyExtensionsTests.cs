using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class RequestBodyExtensionsTests
{
    private const string Description = "an assignment";

    // The worker's [FromBody] binding fails these bodies with an exception before the function runs (500); the
    // reader gives no value, for the handler to name.
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n")]
    [InlineData("null")]
    public async Task ReadJsonBody_EmptyOrNull_IsNoValueAndNoError(string body)
    {
        var (value, error) = await ReadAsync<Assignment>(body);

        value.ShouldBeNull();
        error.ShouldBeNull();
    }

    [Theory]
    [InlineData("{", "The request body is not an assignment (at $).")]
    [InlineData("[1, 2]", "The request body is not an assignment (at $).")]
    [InlineData("{ \"Account\": \"ada\", \"Role\": \"Overlord\" }", "The request body is not an assignment (at $.Role).")]
    public async Task ReadJsonBody_JsonThatDoesNotFit_IsBadRequestWithThePath(string body, string message)
    {
        var (value, error) = await ReadAsync<Assignment>(body);

        value.ShouldBeNull();
        var response = error.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        response.Message.ShouldBe(message);
        response.ErrorCode.ShouldBeNull();
    }

    // System.Text.Json refuses a body without a C# required member, even one the handler would set itself.
    [Fact]
    public async Task ReadJsonBody_WithoutARequiredMember_IsBadRequestAtTheRoot()
    {
        var (value, error) = await ReadAsync<Assignment>("""{ "Role": "Reader" }""");

        value.ShouldBeNull();
        error.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>()
            .Message.ShouldBe("The request body is not an assignment (at $).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ErrorCodes.InvalidKey)]
    public async Task ReadJsonBody_ErrorCode_IsSetOnlyWhenGiven(string? errorCode)
    {
        var (_, error) = await ReadAsync<Assignment>("[1, 2]", errorCode: errorCode);

        error.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(errorCode);
    }

    [Fact]
    public async Task ReadJsonBody_ReadsTheApiJson_WithEnumNamesAnyCaseAndTrailingCommas()
    {
        var (value, error) = await ReadAsync<Assignment>("""{ "account": "ada", "ROLE": "contributor", }""");

        error.ShouldBeNull();
        value.ShouldBe(new Assignment { Account = "ada", Role = AccountRole.Contributor });
    }

    // The worker's [FromBody] binding refuses a body without a content type; the reader does not look at it.
    [Fact]
    public async Task ReadJsonBody_WithoutAContentType_IsRead()
    {
        var (value, error) = await ReadAsync<Assignment>("""{ "Account": "ada", "Role": "Administrator" }""", contentType: false);

        error.ShouldBeNull();
        value.ShouldBe(new Assignment { Account = "ada", Role = AccountRole.Administrator });
    }

    [Fact]
    public async Task ReadJsonBody_AList_IsRead()
    {
        List<Assignment> assignments =
        [
            new() { Account = "ada", Role = AccountRole.Owner },
            new() { Account = "bob" },
        ];

        var (value, error) = await ReadAsync<List<Assignment>>(FunctionTestDoubles.ToJsonBody(assignments));

        error.ShouldBeNull();
        value.ShouldBe(assignments);
    }

    // Decoding to a string first would replace the bytes that are not UTF-8 and store what the client never sent.
    [Fact]
    public async Task ReadJsonBody_NotUtf8_IsBadRequest()
    {
        // "café" in Latin-1.
        byte[] body = [.. "{ \"Account\": \"caf"u8, 0xE9, .. "\" }"u8];

        var (value, error) = await ReadBytesAsync<Assignment>(body);

        value.ShouldBeNull();
        error.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>()
            .Message.ShouldBe("The request body is not an assignment (at $.Account).");
    }

    [Theory]
    [InlineData("""{ "Account": "ada" }""")]
    [InlineData("")]
    public async Task ReadJsonBody_WithAByteOrderMark_IsReadAfterIt(string json)
    {
        byte[] body = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(json)];

        var (value, error) = await ReadBytesAsync<Assignment>(body);

        error.ShouldBeNull();
        value.ShouldBe(json.Length == 0 ? null : new Assignment { Account = "ada" });
    }

    [Fact]
    public async Task ReadJsonBody_CanceledRequest_Throws()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, FunctionTestDoubles.JsonContentType(), "POST", """{ "Account": "ada" }""");
        context.Token = new CancellationToken(canceled: true);

        await Should.ThrowAsync<OperationCanceledException>(
            () => request.ReadJsonBodyAsync<Assignment>(FunctionTestDoubles.ApiJsonSerializerOptions, Description));
    }

    private static async Task<(T? Value, IActionResult? Error)> ReadAsync<T>(string body, bool contentType = true, string? errorCode = null)
        where T : class
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (_, request) = FunctionTestDoubles.CreateRequest(services, headers, "POST", body);

        return await request.ReadJsonBodyAsync<T>(FunctionTestDoubles.ApiJsonSerializerOptions, Description, errorCode);
    }

    private static async Task<(T? Value, IActionResult? Error)> ReadBytesAsync<T>(byte[] body)
        where T : class
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var request = new TestHttpRequestData(new TestFunctionContext(services), FunctionTestDoubles.JsonContentType(), "POST", bodyBytes: body);

        return await request.ReadJsonBodyAsync<T>(FunctionTestDoubles.ApiJsonSerializerOptions, Description);
    }

    private sealed record Assignment
    {
        public required string Account { get; init; }

        public AccountRole Role { get; init; }
    }
}
