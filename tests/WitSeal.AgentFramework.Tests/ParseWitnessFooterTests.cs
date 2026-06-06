// SPDX-License-Identifier: Apache-2.0
// WitSealBridge.ParseWitnessFooter is the internal seam over the two footer
// regexes (receipt=(rcpt_…) / event=(evt_…)). It parses the witness footer the
// CLI prints on stderr; each component is null when its token is absent.
// Reached via [assembly: InternalsVisibleTo("WitSeal.AgentFramework.Tests")].

using Xunit;

namespace WitSeal.AgentFramework.Tests;

public sealed class ParseWitnessFooterTests
{
    [Fact]
    public void Parses_both_ids_from_a_full_footer()
    {
        const string stderr = "[witseal: event=evt_01HZX receipt=rcpt_9Q2 risk=C3 outcome=allow]";

        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(stderr);

        Assert.Equal("rcpt_9Q2", receiptId);
        Assert.Equal("evt_01HZX", eventId);
    }

    [Fact]
    public void Parses_ids_regardless_of_token_order()
    {
        const string stderr = "receipt=rcpt_AAA before event=evt_BBB after";

        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(stderr);

        Assert.Equal("rcpt_AAA", receiptId);
        Assert.Equal("evt_BBB", eventId);
    }

    [Fact]
    public void Returns_null_for_both_when_footer_absent()
    {
        const string stderr = "some unrelated diagnostic line with no footer\n";

        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(stderr);

        Assert.Null(receiptId);
        Assert.Null(eventId);
    }

    [Fact]
    public void Returns_null_for_both_on_empty_stderr()
    {
        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(string.Empty);

        Assert.Null(receiptId);
        Assert.Null(eventId);
    }

    [Fact]
    public void Receipt_present_event_absent()
    {
        const string stderr = "receipt=rcpt_only here, no event token";

        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(stderr);

        Assert.Equal("rcpt_only", receiptId);
        Assert.Null(eventId);
    }

    [Fact]
    public void Event_present_receipt_absent()
    {
        const string stderr = "event=evt_only here, no receipt token";

        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(stderr);

        Assert.Null(receiptId);
        Assert.Equal("evt_only", eventId);
    }

    [Fact]
    public void Footer_id_stops_at_non_alphanumeric()
    {
        // rcpt_/evt_ ids are [A-Za-z0-9]+; the match must stop at the first
        // non-alphanumeric character (here the closing bracket / space).
        const string stderr = "event=evt_123ABC] receipt=rcpt_456def ";

        var (receiptId, eventId) = WitSealBridge.ParseWitnessFooter(stderr);

        Assert.Equal("rcpt_456def", receiptId);
        Assert.Equal("evt_123ABC", eventId);
    }
}
