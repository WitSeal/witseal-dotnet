// SPDX-License-Identifier: Apache-2.0
using Xunit;

namespace WitSeal.AgentFramework.Tests;

public sealed class WitSealToolRenderTests
{
    [Fact]
    public void Executed_command_exit_100_does_not_become_a_non_execution_claim()
    {
        var result = new WitSealRunResult
        {
            ExitCode = 100,
            Stdout = "command side effect completed",
            Stderr = "",
            ReceiptId = "rcpt_test100",
            EventId = "evt_test100",
        };
        var rendered = WitSealTool.Render(result);
        Assert.Contains(result.Stdout, rendered);
        Assert.Contains(result.ReceiptId, rendered);
        Assert.Contains("exit=100", rendered);
        Assert.DoesNotContain("did not run", rendered);
        Assert.DoesNotContain("DENIED by policy", rendered);
    }

    [Fact]
    public void Missing_receipt_does_not_become_a_recording_claim()
    {
        var rendered = WitSealTool.Render(new WitSealRunResult
        {
            ExitCode = 1,
            Stdout = "",
            Stderr = "CLI entry missing",
        });
        Assert.Contains("receipt=(none)", rendered);
        Assert.Contains("evidence status not established", rendered);
        Assert.DoesNotContain("receipt recorded", rendered);
    }

    [Fact]
    public void Reported_receipt_is_not_presented_as_completed_verification()
    {
        var rendered = WitSealTool.Render(new WitSealRunResult
        {
            ExitCode = 0,
            Stdout = "ok",
            Stderr = "",
            ReceiptId = "rcpt_test0",
            EventId = "evt_test0",
        });
        Assert.Contains("receipt=rcpt_test0 event=evt_test0 exit=0", rendered);
        Assert.Contains("reported by CLI", rendered);
        Assert.DoesNotContain("independently verified", rendered);
    }
}
