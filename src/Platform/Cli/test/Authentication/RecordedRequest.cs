using System;
using System.Collections.Generic;
using System.Net.Http;

namespace _42.Platform.Cli.UnitTests.Authentication;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, Dictionary<string, string> Form);
