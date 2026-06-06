// SPDX-License-Identifier: Apache-2.0
// Serializes every test that mutates process-level WITSEAL_* environment
// variables. xUnit runs distinct test classes in parallel by default; the
// FromEnvironment tests read and write shared process state, so they must not
// overlap. Classes that touch env vars carry [Collection(EnvVarCollection.Name)].

using Xunit;

namespace WitSeal.AgentFramework.Tests;

[CollectionDefinition(EnvVarCollection.Name, DisableParallelization = true)]
public sealed class EnvVarCollection
{
    public const string Name = "WitSeal env-var (serialized)";
}

/// <summary>
/// Saves the four WITSEAL_* environment variables on construction and restores
/// them on dispose, so a test can set them freely and leave the process clean.
/// </summary>
internal sealed class WitSealEnvScope : IDisposable
{
    private static readonly string[] Keys =
    {
        "WITSEAL_CLI_ENTRY",
        "WITSEAL_DATA_DIR",
        "WITSEAL_MODE",
        "WITSEAL_NODE",
    };

    private readonly Dictionary<string, string?> _saved = new();

    public WitSealEnvScope()
    {
        foreach (var key in Keys)
        {
            _saved[key] = Environment.GetEnvironmentVariable(key);
            // Start every scope from a clean slate so defaults are exercised
            // unless the test explicitly sets a value.
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    public static void Set(string key, string? value)
        => Environment.SetEnvironmentVariable(key, value);

    public void Dispose()
    {
        foreach (var key in Keys)
        {
            Environment.SetEnvironmentVariable(key, _saved[key]);
        }
    }
}
