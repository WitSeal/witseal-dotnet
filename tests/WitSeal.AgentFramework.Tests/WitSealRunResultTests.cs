// SPDX-License-Identifier: Apache-2.0
// WitSealRunResult.DeniedExitCode is WitSeal's reserved Gate-denial exit code.
// VERIFIED FACT: a WitSeal Gate denial exits 100 (witseal src/cli/exec.ts
// EXIT_DENIED = 100; live: explicit default_decision:deny and fails-closed
// no-policy both exit 100). Denied is true exactly when ExitCode == 100.

using Xunit;

namespace WitSeal.AgentFramework.Tests;

public sealed class WitSealRunResultTests
{
    [Fact]
    public void DeniedExitCode_is_100()
    {
        Assert.Equal(100, WitSealRunResult.DeniedExitCode);
    }

    [Fact]
    public void Denied_is_true_when_exit_is_100()
    {
        var result = new WitSealRunResult
        {
            ExitCode = 100,
            Stdout = "",
            Stderr = "",
        };

        Assert.True(result.Denied);
    }

    [Fact]
    public void Denied_is_false_when_exit_is_0()
    {
        var result = new WitSealRunResult
        {
            ExitCode = 0,
            Stdout = "",
            Stderr = "",
        };

        Assert.False(result.Denied);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(101)]
    [InlineData(127)]
    public void Denied_is_false_for_any_non_100_exit(int exitCode)
    {
        var result = new WitSealRunResult
        {
            ExitCode = exitCode,
            Stdout = "",
            Stderr = "",
        };

        Assert.False(result.Denied);
    }

    [Fact]
    public void Receipt_and_event_ids_round_trip()
    {
        var result = new WitSealRunResult
        {
            ExitCode = 0,
            Stdout = "ok",
            Stderr = "footer",
            ReceiptId = "rcpt_abc123",
            EventId = "evt_def456",
        };

        Assert.Equal("rcpt_abc123", result.ReceiptId);
        Assert.Equal("evt_def456", result.EventId);
    }
}
