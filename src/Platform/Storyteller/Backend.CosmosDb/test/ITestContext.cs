using System;
using Microsoft.Extensions.Configuration;
using Testcontainers.CosmosDb;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public interface ITestContext
{
    IConfiguration Configuration { get; }

    IServiceProvider Services { get; }

    CosmosDbContainer DbContainer { get; }
}
