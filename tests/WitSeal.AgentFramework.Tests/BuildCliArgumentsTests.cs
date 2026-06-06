// SPDX-License-Identifier: Apache-2.0
// WitSealBridge.BuildCliArguments is the internal seam that builds the exact argv
// passed to Node (everything after the node executable):
//   CliEntry --data-dir DataDir --segment Segment exec --mode gate|witness
//   [--cwd Cwd] -- /bin/sh -c command
// Covered here: gate vs witness mode string; --cwd present only when Cwd is set;
// the trailing "-- /bin/sh -c <command>"; and the leading fixed shape.
// Reached via [assembly: InternalsVisibleTo("WitSeal.AgentFramework.Tests")].

using Xunit;

namespace WitSeal.AgentFramework.Tests;

public sealed class BuildCliArgumentsTests
{
    private const string Cli = "/opt/witseal/dist/src/cli/index.js";
    private const string DataDir = "/var/lib/witseal-data";

    private static WitSealBridge BridgeWith(
        WitSealMode mode = WitSealMode.Gate,
        string? cwd = null,
        string segment = "default")
    {
        var options = new WitSealBridgeOptions
        {
            CliEntry = Cli,
            DataDir = DataDir,
            Node = "node",
            Mode = mode,
            Segment = segment,
            Cwd = cwd,
        };
        return new WitSealBridge(options);
    }

    [Fact]
    public void Gate_mode_emits_gate_string_and_full_fixed_shape()
    {
        var args = BridgeWith(WitSealMode.Gate).BuildCliArguments("echo hi");

        Assert.Equal(new[]
        {
            Cli,
            "--data-dir", DataDir,
            "--segment", "default",
            "exec",
            "--mode", "gate",
            "--", "/bin/sh", "-c", "echo hi",
        }, args);
    }

    [Fact]
    public void Witness_mode_emits_witness_string()
    {
        var args = BridgeWith(WitSealMode.Witness).BuildCliArguments("ls");

        Assert.Equal(new[]
        {
            Cli,
            "--data-dir", DataDir,
            "--segment", "default",
            "exec",
            "--mode", "witness",
            "--", "/bin/sh", "-c", "ls",
        }, args);
    }

    [Fact]
    public void Cwd_absent_means_no_cwd_flag()
    {
        var args = BridgeWith(cwd: null).BuildCliArguments("pwd");

        Assert.DoesNotContain("--cwd", args);
    }

    [Fact]
    public void Empty_cwd_means_no_cwd_flag()
    {
        var args = BridgeWith(cwd: "").BuildCliArguments("pwd");

        Assert.DoesNotContain("--cwd", args);
    }

    [Fact]
    public void Cwd_present_inserts_cwd_flag_before_the_separator()
    {
        var args = BridgeWith(cwd: "/work/dir").BuildCliArguments("pwd");

        Assert.Equal(new[]
        {
            Cli,
            "--data-dir", DataDir,
            "--segment", "default",
            "exec",
            "--mode", "gate",
            "--cwd", "/work/dir",
            "--", "/bin/sh", "-c", "pwd",
        }, args);

        // --cwd <dir> sits immediately before the -- separator.
        var cwdIdx = args.ToList().IndexOf("--cwd");
        var sepIdx = args.ToList().IndexOf("--");
        Assert.Equal("/work/dir", args[cwdIdx + 1]);
        Assert.Equal(sepIdx, cwdIdx + 2);
    }

    [Fact]
    public void Trailing_four_are_separator_sh_dashc_and_verbatim_command()
    {
        const string command = "grep -R 'needle && other' .";
        var args = BridgeWith().BuildCliArguments(command);

        // The command is passed verbatim as the single final argument.
        Assert.Equal(command, args[^1]);
        Assert.Equal("-c", args[^2]);
        Assert.Equal("/bin/sh", args[^3]);
        Assert.Equal("--", args[^4]);
    }

    [Fact]
    public void Segment_value_is_passed_through()
    {
        var args = BridgeWith(segment: "release-2").BuildCliArguments("true");

        var segIdx = args.ToList().IndexOf("--segment");
        Assert.Equal("release-2", args[segIdx + 1]);
    }

    [Fact]
    public void First_argument_is_the_cli_entry()
    {
        var args = BridgeWith().BuildCliArguments("true");

        Assert.Equal(Cli, args[0]);
    }
}
