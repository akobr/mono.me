# Tool Call Deadline of the MCP Server

Linear: [424-301](https://linear.app/424fun/issue/424-301/qa-mcp-servers-api-call-timeout-5-min-is-longer-than-the-240-s).

## Overview

This review covers all of [the spec](../2026-11-22%20Tool%20Call%20Deadline%20of%20the%20MCP%20Server%20below%20the%20Container%20Apps%20Ingress%20Limit.md). The MCP server now ends a tool call after 200 seconds with an answer of its own. The Container Apps ingress drops a request after 240 seconds in which nothing was sent, and the API's front end answers `502` after 230 seconds. The deadline covers the token exchange and every call of the API together. When it fires, the call that is still running is stopped and the tool says the call may have completed and names the read that checks it. The timeout of a single API call is now the same 200 seconds instead of five minutes. A response body that breaks while it is read now gets that answer too. Before, the client got a dropped connection or the MCP SDK's generic error. Four independent reviewers (cancellation, tests, docs, logging safety) checked the branch, and a skeptic verified each finding. Three findings changed the code or the docs (sections 4, 5 and 6), and the fourth was this review.

## What Was Done

### 1. A deadline for the whole tool call

`Mcp/src/Tools/StorytellerCallFilter.cs`:

- `public static readonly TimeSpan ToolCallTimeout = TimeSpan.FromSeconds(200)`. Its comment gives the budget: the 240 seconds of the ingress, the start of a replica included, and the API front end's 230 seconds. That leaves 40 seconds for the start of a replica, the authentication and the result.
- `Create` starts `deadline = new CancellationTokenSource(ToolCallTimeout, timeProvider)` before the token exchange. `timeProvider` is the host's `TimeProvider`: `AddStorytellerTokenExchange` registers `TimeProvider.System`, and tests replace it. `call` is a CTS linked to the request's token and the deadline. Both are disposed when the filter returns.
- `call.Token` goes to `IStorytellerTokenProvider.GetTokenAsync` and to `next`. The exception filters still test the caller's own token. So a cancellation by the deadline counts as an unfinished call, and the caller's cancellation stays a cancellation.
- In the catch around `next`, a fired deadline logs `Tool {Tool}: stopped at the deadline of a tool call, {Seconds} seconds ({ExceptionType}).` and returns `ToolCallTimeoutMessage`. Any other unfinished call logs and answers as before.
- The catch around the exchange is unchanged: a deadline there would be a sign-in that is temporarily unavailable. It can't fire there while `ExchangeTimeout` (30 seconds) is shorter.
- The class comment says the deadline exists and that it stops the running call because every tool passes its token to the SDK.

`Task.WhenAll` in the Understanding tools waits for all its reads, so no read outlives the filter's `using` of the two sources.

### 2. The answer at the deadline

`ToolCallTimeoutMessage` is a `static readonly string`, because it interpolates `ToolCallTimeout`. It reads "The tool call was stopped after 200 seconds, the longest the MCP server lets a tool call run." It then says that a running call may have completed, names the matching reads for a write, and advises repeating a read with a narrower request. The sentence about lost one-time credentials, which `UnfinishedCallMessage` and the new message share, is now the private const `LostCredentials`. `UnfinishedCallMessage` reads exactly as before.

### 3. The timeout of an API call follows the deadline

`McpHostExtensions.ApiCallTimeout = StorytellerCallFilter.ToolCallTimeout`, with a comment that one call may use the whole budget. The comment over `AddStorytellerClients` explains why the value is below the ingress and below the API's front end. `AttemptTimeout`, `TotalRequestTimeout` and `CircuitBreaker.SamplingDuration` (now 400 seconds) keep their formulas.

### 4. A broken response body is an unfinished call

This section goes further than the spec, which added only `HttpIOException`:

- **The SDK wraps the failure.** The first test of a `create_machine` whose `200` body breaks showed that the generated SDK does not let the transport exception through. Newtonsoft wraps it in a `JsonSerializationException` ("Error setting value in extension data", for a member the type does not know). The SDK's `ReadObjectResponseAsync` then wraps that in an `ApiException` with the status of the answer. The filter turned that into "Storyteller API answered 200: The API refused the request." `IsUnfinishedCall` now also matches an `Sdk.ApiException` whose inner chain holds the transport failure (`FindBrokenBody`). The filter checks this before its `catch (Sdk.ApiException)`.
- **A reset is a plain `IOException`.** The logging-safety reviewer showed, with a probe on .NET 10, that a connection reset (RST) while the body is read throws a plain `IOException` with an inner `SocketException`. Only an early clean end throws `HttpIOException`. So `IsUnfinishedCall` and `FindBrokenBody` match `IOException`, which also covers `HttpIOException` and `HttpProtocolException`. No tool reads a file between the exchange and the result. `StorytellerTokenExchangeProvider` already treats `IOException` as a transport failure.
- **The log names the transport failure.** The warning of an unfinished call logs the type of that failure (`IOException`, `HttpIOException`), not `ApiException`. As before, it never logs an exception's message, which for an `ApiException` holds the response body.
- **A step-up or `ActorRestricted` answer still reaches `ApiErrors`.** Only an answer whose body could not be read is taken as an unfinished call.

Polly's `BrokenCircuitException` and `RateLimiterRejectedException` stay unmapped, as planned.

### 5. Tests

- **Test support.**
  - `McpTestHost` takes an optional `clock` (a `TimeProvider`) and registers it over the default.
  - `RecordingApiHandler.RespondAsync(request, cancellationToken)` wins over `Respond`.
  - `StubTokenProvider.Exchanging` runs as an exchange starts. The spec did not list it. A test uses it to move the clock by a second, so the API call's own timeouts start after the deadline, as in production. Without it, both run out at the same fake instant, and a test can't tell which one fired.
  - The test project references `Microsoft.Extensions.TimeProvider.Testing`.
- **`ToolCallDeadlineTests`** run on a `FakeTimeProvider` started at the current time. Real-time waits of 30 seconds guard against a hang.
  - `CallThatDoesNotAnswer_EndsAtTheDeadline` covers `get_annotation` and `create_machine`. The clock moves exactly to the deadline, before the call's own timeouts. The test checks the message, that the API's request saw its cancellation, that there is one request (the GET is not retried), and that the log line has no exception text.
  - `CallsThatEachFit_EndAtTheDeadlineOfTheWholeToolCall` runs `set_configuration`, with 120 seconds for the check and 120 for the write. The deadline stops the second call, so the API sees two requests.
  - `ToolCall_ThatEndsBeforeTheDeadline_Answers` answers a second before the deadline. It also checks that the call's token was not cancelled. The tests reviewer showed that without this check a deadline anywhere from about 121 to 200 seconds went unnoticed.
  - `Message_SaysTheLimitAndNamesTheMatchingReads` checks the message text.
- **`UnfinishedCallTests`.**
  - The theory adds a body that ends early (`HttpIOException`) and a reset (`IOException` with `SocketException`).
  - `CreateMachine_WhoseAnswerBreaksWhileItIsRead_SaysItMayHaveCompleted` covers both. Its `200` body stream starts the JSON and then fails, and the test checks the message, one request, the logged type and that the marker is in no log.
- **`HostDefaultsTests.ToolCallDeadline_EndsBeforeTheIngressAndTheApisFrontEnd`** checks that `ApiCallTimeout` equals `ToolCallTimeout`, which is below 230 seconds and at most 240 − 40. It also checks that `ExchangeTimeout` is shorter.
- **Mutation checks.** The changes were run by hand, then reverted:
  - passing the request's token to `next` instead of `call.Token` fails all three deadline tests;
  - a 150-second deadline fails `ToolCall_ThatEndsBeforeTheDeadline_Answers`.
- **Runs.** The MCP unit tests pass: 786 tests. The deadline, unfinished-call and host-default tests passed 15 runs in a row before the review fixes and 10 after them.

### 6. Documentation

- `docs/Platform/Storyteller/mcp.md`:
  - "Limits of the hosting" says the ingress's 240 seconds are an idle timeout that the start of a replica counts toward. The Consumption profile can't change it; only premium ingress, on its own nodes, raises it. It adds the API front end's 230 seconds and the server's 200 seconds.
  - Safety: a call of the API, and a whole tool call, take at most 200 seconds. A body that breaks off is an unfinished call. A tool call stopped at the deadline says so.
  - Limits: a tool call ends after 200 seconds in every hosting.
- `docs/Platform/Deployment/runbook-mcp-server.md`, phase 9: the same, in one paragraph.
- `docs/Platform/Deployment/decisions.md`, "Azure Container Apps, scaled to zero":
  - It gives the idle timeout and the price of premium ingress (at most 30 minutes, at least two dedicated nodes, about 520 USD a month) as the reason it isn't used.
  - The ingress bounds the whole tool call, and the API front end's 230 seconds bound each call of the API.
  - The docs reviewer pointed out that a first wording ("the API's front end gives up after 230 seconds anyway") tied the deadline to the per-call limit.
- `src/Platform/Deployment/modules/mcp.bicep`: the ingress comment names the idle timeout and `StorytellerCallFilter.ToolCallTimeout`. `az bicep build` of `main.bicep` passes.

### 7. Not changed

The items of the spec's section 7 stay as planned: premium ingress, keep-alives on the SSE stream, the mapping of a front end's `502`/`504` without the API's error body, a body that stalls after its headers, and the JWT bearer's metadata fetch. Of these, a follow-up could map the `502`/`504`: the deadline now ends a call before the front end's 230 seconds, so only a front end that fails for another reason still answers "Try again later" to a write.
