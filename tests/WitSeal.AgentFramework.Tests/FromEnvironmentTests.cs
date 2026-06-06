// SPDX-License-Identifier: Apache-2.0
// WitSealBridgeOptions.FromEnvironment() reads four WITSEAL_* variables. These
// tests cover: the required CliEntry (missing -> InvalidOperationException),
// the DataDir / Node / Mode defaults, and WITSEAL_MODE mapping
// (witness -> Witness; gate or anything else -> Gate).
//
// All of these mutate process-level environment variables, so the class joins the
// serialized env-var collection and each test brackets its mutations with a
// WitSealEnvScope that saves and restores prior values.

using Xunit;

namespace WitSeal.AgentFramework.Tests;

[Collection(EnvVarCollection.Name)]
public sealed class FromEnvironmentTests
{
    private static string ExpectedDefaultDataDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".witseal");

    [Fact]
    public void Missing_CliEntry_throws_InvalidOperationException()
    {
        using var _ = new WitSealEnvScope();
        // WITSEAL_CLI_ENTRY is cleared by the scope.

        var ex = Assert.Throws<InvalidOperationException>(
            () => WitSealBridgeOptions.FromEnvironment());
        Assert.Contains("WITSEAL_CLI_ENTRY", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_CliEntry_throws_InvalidOperationException(string blank)
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", blank);

        Assert.Throws<InvalidOperationException>(
            () => WitSealBridgeOptions.FromEnvironment());
    }

    [Fact]
    public void Only_CliEntry_set_yields_documented_defaults()
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", "/opt/witseal/dist/src/cli/index.js");

        var options = WitSealBridgeOptions.FromEnvironment();

        Assert.Equal("/opt/witseal/dist/src/cli/index.js", options.CliEntry);
        Assert.Equal(ExpectedDefaultDataDir, options.DataDir);
        Assert.Equal("node", options.Node);
        Assert.Equal(WitSealMode.Gate, options.Mode);
    }

    [Fact]
    public void DataDir_and_Node_are_read_when_set()
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", "/opt/witseal/dist/src/cli/index.js");
        WitSealEnvScope.Set("WITSEAL_DATA_DIR", "/var/lib/witseal-data");
        WitSealEnvScope.Set("WITSEAL_NODE", "/usr/local/bin/node20");

        var options = WitSealBridgeOptions.FromEnvironment();

        Assert.Equal("/var/lib/witseal-data", options.DataDir);
        Assert.Equal("/usr/local/bin/node20", options.Node);
    }

    [Fact]
    public void Blank_DataDir_and_Node_fall_back_to_defaults()
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", "/opt/witseal/dist/src/cli/index.js");
        WitSealEnvScope.Set("WITSEAL_DATA_DIR", "   ");
        WitSealEnvScope.Set("WITSEAL_NODE", "");

        var options = WitSealBridgeOptions.FromEnvironment();

        Assert.Equal(ExpectedDefaultDataDir, options.DataDir);
        Assert.Equal("node", options.Node);
    }

    [Fact]
    public void Mode_witness_maps_to_Witness_case_insensitively()
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", "/opt/witseal/dist/src/cli/index.js");
        WitSealEnvScope.Set("WITSEAL_MODE", "WiTnEsS");

        var options = WitSealBridgeOptions.FromEnvironment();

        Assert.Equal(WitSealMode.Witness, options.Mode);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("GATE")]
    [InlineData("observe")]
    [InlineData("anything-else")]
    [InlineData("")]
    public void Mode_gate_or_other_maps_to_Gate(string modeValue)
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", "/opt/witseal/dist/src/cli/index.js");
        WitSealEnvScope.Set("WITSEAL_MODE", modeValue);

        var options = WitSealBridgeOptions.FromEnvironment();

        Assert.Equal(WitSealMode.Gate, options.Mode);
    }

    [Fact]
    public void Mode_unset_defaults_to_Gate()
    {
        using var _ = new WitSealEnvScope();
        WitSealEnvScope.Set("WITSEAL_CLI_ENTRY", "/opt/witseal/dist/src/cli/index.js");
        // WITSEAL_MODE left unset by the scope.

        var options = WitSealBridgeOptions.FromEnvironment();

        Assert.Equal(WitSealMode.Gate, options.Mode);
    }
}
