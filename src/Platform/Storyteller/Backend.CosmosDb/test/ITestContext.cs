using System;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public interface ITestContext
{
    IConfiguration Configuration { get; }

    IServiceProvider Services { get; }

    // The emulator of the run, for the tests that only an emulator like the service answers right.
    CosmosTestEmulator Emulator { get; }
}
