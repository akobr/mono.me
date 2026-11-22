# Tool Call Deadline of the MCP Server below the Container Apps Ingress Limit

Linear: [424-301](https://linear.app/424fun/issue/424-301/qa-mcp-servers-api-call-timeout-5-min-is-longer-than-the-240-s), found by the QA review of PR #210 (424-287, the MCP server on Azure Container Apps).

Line numbers refer to the tree this plan was written against (`20ef5b5a2`). No application code is changed by this document. Before it was written, three independent readers checked the hosting limits (Microsoft docs, Microsoft staff answers in `microsoft/azure-container-apps`, Envoy), the cancellation and error flow of the MCP C# SDK 2.3.0 (its source at the tag `v2.3.0`, commit `9a327829`), and every tool of the server. Their findings are in Current State.

## Problem

The MCP server gives a call of the Storyteller API up to five minutes. In production a request to the server passes the Container Apps ingress, which drops it after 240 seconds in which nothing was sent. A tool call that waits longer for the API therefore never gets its own answer to the client. The client sees a `504` with the body `stream timeout`, or a stream that breaks, instead of the server's isError result that says the call may have completed and names the read that checks it.

A shorter per-call timeout alone does not fix this. Several tools call the API more than once in a row (pages of a list, a check before a write, the reads of `explain_configuration`), and each call would get the whole per-call time again.

## Current State

### The MCP server

- `McpHostExtensions.ApiCallTimeout` is `TimeSpan.FromMinutes(5)` (`Mcp/src/Hosting/McpHostExtensions.cs:19`). `AddStorytellerClients` (lines 126-155) removes the defaults' resilience handler, sets `HttpClient.Timeout` to infinite (line 144) and adds the standard handler with retries off for unsafe methods (line 147), `AttemptTimeout` and `TotalRequestTimeout` at `ApiCallTimeout` (lines 148-149) and `CircuitBreaker.SamplingDuration` at twice that (line 150). GETs keep the standard retry (3 retries, exponential from 2 seconds) inside the total timeout.
- `StorytellerCallFilter.Create` (`Mcp/src/Tools/StorytellerCallFilter.cs:33-97`) gets the person's Storyteller token (`IStorytellerTokenProvider.GetTokenAsync`, line 54), then runs the tool with the request's token unchanged (`next(request, cancellationToken)`, line 74). There is no deadline for a tool call.
- `IsUnfinishedCall` (lines 109-117) treats `TimeoutRejectedException`, `HttpRequestException` and a cancellation the caller did not ask for as a call that did not finish; the tool then answers `UnfinishedCallMessage` (lines 27-31). During the token exchange (lines 52-61) the same exceptions become `StorytellerSignInUnavailableException`.
- The token exchange has its own budget: `StorytellerTokenExchangeProvider.ExchangeTimeout` is 30 seconds (`Mcp/src/Authentication/StorytellerTokenExchangeProvider.cs:52`, the source at line 232). Its answers are cached, so most tool calls make no exchange.
- Every one of the 119 SDK calls in `Mcp/src/Tools` and `Mcp/src/Understanding` passes the tool's `CancellationToken`, and no catch block of a tool catches `OperationCanceledException`, `TimeoutRejectedException` or `HttpRequestException`: they all reach the filter.
- Tools with several sequential API calls: the list tools up to five pages (`ApiPageReader.MaxEmptyPages`, `Mcp/src/Tools/ApiPageReader.cs:9`), `replace_configuration` and `patch_configuration` four, `set_annotation` up to 21, `create_annotations` a lookup per name that breaks the naming rule, `search_configurations` up to 58 rounds of reads, `explain_configuration` up to 16, `suggest_configuration_placement` up to 14.
- The SDK reads a body after the headers (`HttpCompletionOption.ResponseHeadersRead`) and deserializes a stream synchronously (`Sdk.NSwag/src/ApiSdk.g.cs:6235-6240`). A body read that breaks throws `HttpIOException`, which derives from `IOException`, not `HttpRequestException`: `IsUnfinishedCall` misses it, and the MCP SDK turns it into its generic "An error occurred invoking '<tool>'." The one-time credentials of `create_machine` and `reset_machine` arrive in such a body.

### The MCP C# SDK 2.3.0

- The token a call-tool filter passes to `next` is the `CancellationToken` parameter of the tool method: `AIFunctionMcpServerTool.InvokeAsync` passes it to `AIFunction.InvokeAsync`, which binds every `CancellationToken` parameter to it (`Microsoft.Extensions.AI.Abstractions` `AIFunctionFactory.cs:920-925`). So a filter can impose a deadline with a linked token.
- In stateless Streamable HTTP the filter's token is cancelled when the HTTP request aborts (the per-POST session is disposed). When the ingress drops the request, the tool's token is cancelled as the caller's cancellation, `IsUnfinishedCall` rightly answers false, and the SDK writes nothing: the POST transport is already gone.
- A `tools/call` POST is answered as `text/event-stream`. For protocol revisions before `2026-07-28` the SDK sends the headers at once; from `2026-07-28` on (the default of the 2.3.0 client) it sends nothing until the result. The SDK has no keep-alive.
- An exception that escapes the filter becomes a generic isError result ("An error occurred invoking '<tool>'.") and is logged by the SDK at `Error` with the exception object.

### The hosting

- **The ingress:** Microsoft's page says "Request time out is 240 seconds"; Microsoft staff (`microsoft/azure-container-apps#597`) and the environment's `ingressConfiguration.requestIdleTimeout` ("before idle requests are timed out", 4 to 30 minutes, default 4) show that it is an idle timeout of Envoy: no bytes in either direction for 240 seconds. Before any response header the client gets `504` `stream timeout`; after them a broken stream. It cannot be changed on the Consumption profile: premium ingress needs a dedicated workload profile of at least two nodes (about 520 USD a month in Sweden Central), and the app has no per-app timeout. The start of a replica after scale to zero very probably counts toward it (`#1637`); the startup probe of `mcp.bicep` (lines 192-203) allows about 31 seconds.
- **The API:** an HTTP-triggered function on any plan, Flex Consumption included, has at most 230 seconds to respond (the Azure Load Balancer's idle timeout); then the caller gets `502` and the function keeps running, so a write may still complete. The MCP server's filter maps a `502` to "The Storyteller API failed. Try again later." (`Mcp/src/Tools/ApiErrors.cs:14, 97-101`), which misleads for a write.

### Documentation and tests

- `docs/Platform/Storyteller/mcp.md:231` ("the ingress ends a request after 240 seconds, before the server's own five minutes"), `:414` ("A call may take up to five minutes."), `:423`; `docs/Platform/Deployment/runbook-mcp-server.md:256`; `docs/Platform/Deployment/decisions.md:111`; the comment at `src/Platform/Deployment/modules/mcp.bicep:165`. They word the 240 seconds as a total limit and the five minutes as the server's.
- `Mcp/test/HostDefaultsTests.cs:23-39` pins the handler's timeouts to `ApiCallTimeout`; `Mcp/test/UnfinishedCallTests.cs` covers the unfinished-call mapping; `Mcp/test/Support/RecordingApiHandler.cs` answers synchronously only, and `McpTestHost` keeps the system clock.

## Proposed Changes

### 1. A deadline for the whole tool call

In `StorytellerCallFilter`:

```csharp
// The longest a tool call may take: the token exchange and every call of the API. The Container Apps ingress drops a
// request after 240 seconds in which nothing was sent, the start of a replica included, and the API's front end answers
// 502 after 230 seconds. 40 seconds stay for the start of a replica, the authentication and the result.
public static readonly TimeSpan ToolCallTimeout = TimeSpan.FromSeconds(200);
```

`Create` starts the deadline before the token exchange, on the host's `TimeProvider` (registered by `AddStorytellerTokenExchange`, `TimeProvider.System` by default), and links it to the request's token:

```csharp
var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
using var deadline = new CancellationTokenSource(ToolCallTimeout, timeProvider);
using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
```

`call.Token` goes to `GetTokenAsync` and to `next`. The exception filters keep testing the caller's own token (`IsUnfinishedCall(exception, cancellationToken)`), so a cancellation by the deadline is an unfinished call and the caller's cancellation stays a cancellation. In the catch of the tool's calls, a fired deadline (`deadline.IsCancellationRequested`) logs `Tool {Tool}: stopped at the deadline of a tool call, {Seconds} seconds ({ExceptionType}).` (the type only, as every log of the filter) and returns `ToolCallTimeoutMessage`; otherwise the catch is as before. The catch of the exchange is unchanged: a deadline there (not reachable while `ExchangeTimeout` is shorter) is a sign-in that is temporarily unavailable.

The deadline is cooperative: it stops what observes the token, which every SDK call does. A body that stalls after its headers is read synchronously and is not stopped (see 7).

### 2. The answer at the deadline

```csharp
public static readonly string ToolCallTimeoutMessage =
    $"The tool call was stopped after {ToolCallTimeout.TotalSeconds:0} seconds, the longest the MCP server lets a tool "
    + "call run. A call to the Storyteller API that was still running may have completed on the server, so check with "
    + "the matching read (list_machines, get_annotation, get_configuration, get_key_value) before repeating a write; "
    + "repeat a read with a narrower request (a smaller limit, fewer keys). The one-time credentials of a completed "
    + "create_machine or reset_machine cannot be read again: reset the machine, or delete and recreate a Certificate "
    + "machine.";
```

`UnfinishedCallMessage` stays as it is for a single call that times out or breaks.

### 3. The timeout of an API call follows the deadline

`McpHostExtensions.ApiCallTimeout = StorytellerCallFilter.ToolCallTimeout` (200 seconds). One call may use the whole budget of a tool call, and the deadline ends what several calls add up to. `AttemptTimeout`, `TotalRequestTimeout` and `CircuitBreaker.SamplingDuration` (400 seconds) keep their formulas; the validator's rule (sampling at least twice the attempt timeout) holds. The comment over `AddStorytellerClients` says why the value is below the ingress and the API's front end.

### 4. A broken response body is an unfinished call

`IsUnfinishedCall` adds `HttpIOException` (`System.Net.Http`): a body read that breaks is a call that did not finish, which may have completed on the server. Polly's `BrokenCircuitException` and `RateLimiterRejectedException` stay unmapped (the call never started; not this issue).

### 5. Tests

- `Support/McpTestHost`: an optional `TimeProvider` (registered over the default) so a test moves the clock; `Support/RecordingApiHandler`: an optional asynchronous responder `RespondAsync(request, cancellationToken)` that wins over `Respond`.
- New `ToolCallDeadlineTests` on a `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, already in `Directory.Packages.props`) started at the current time:
  - a single call that does not answer, for a read (`get_annotation`) and a write (`create_machine`): the clock moves past `ToolCallTimeout`, the tool answers `ToolCallTimeoutMessage` as isError, the API's request saw its cancellation, the GET is not retried, and the log has the tool and the exception type, never the exception's message;
  - two calls that each fit (`set_configuration`: the annotation check and the write, 120 seconds each): the deadline stops the second, two requests;
  - a tool call that ends before the deadline answers normally.
- `UnfinishedCallTests`: `HttpIOException` as a failure of the theory, and `create_machine` whose `200` body breaks while it is read.
- `HostDefaultsTests`: `ApiCallTimeout` equals `ToolCallTimeout`, which is below the API front end's 230 seconds and leaves at least 40 seconds of the ingress's 240.

### 6. Documentation

- `docs/Platform/Storyteller/mcp.md`: the limits of the hosting (`:231`: an idle timeout of 240 seconds that the start of a replica counts toward and the Consumption profile cannot change, the API's 230 seconds, the server's 200 seconds for a tool call), the calls of the API (`:414`: a call and a whole tool call take at most 200 seconds, then the tool says so), and the limits (`:423`).
- `docs/Platform/Deployment/runbook-mcp-server.md:256`, `docs/Platform/Deployment/decisions.md:111` (with the price of premium ingress as the reason it is not used) and the comment at `src/Platform/Deployment/modules/mcp.bicep:165`.

### 7. Not changed

- **Premium ingress** would raise the idle timeout to 30 minutes for about 520 USD a month; the server runs inside the free grant. The API's 230 seconds would still apply.
- **Keep-alives or progress notifications** on the SSE stream: the SDK has none, a client on `2026-07-28` gets no headers before the result, and the API's 230 seconds per call would still apply.
- **A `502` or `504` of the API's front end** without the API's error body would better answer "may have completed" than "Try again later" for a write. The deadline now ends a call before the front end's 230 seconds, so the case is left to a follow-up.
- **A body that stalls after its headers** is deserialized synchronously by the generated SDK and is not stopped by the deadline. The API sends whole JSON bodies, so this is not expected.
- **The JWT bearer's metadata fetch** (default backchannel timeout of one minute, on a cold replica) runs before the filter and ends as `401`, not as a tool result.
