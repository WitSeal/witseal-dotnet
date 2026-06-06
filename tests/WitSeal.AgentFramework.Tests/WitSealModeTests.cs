// SPDX-License-Identifier: Apache-2.0
// The WitSealMode enum is the gate|witness selector. These tests pin its members
// and the value the bridge maps each to on the command line is covered separately
// in BuildCliArgumentsTests.

using Xunit;

namespace WitSeal.AgentFramework.Tests;

public sealed class WitSealModeTests
{
    [Fact]
    public void Enum_has_exactly_gate_and_witness()
    {
        var names = Enum.GetNames<WitSealMode>();
        Assert.Equal(2, names.Length);
        Assert.Contains(nameof(WitSealMode.Gate), names);
        Assert.Contains(nameof(WitSealMode.Witness), names);
    }

    [Fact]
    public void Gate_is_the_default_underlying_value()
    {
        // default(WitSealMode) must be Gate — deny-by-default is the safe default
        // and several option defaults rely on it.
        Assert.Equal(WitSealMode.Gate, default(WitSealMode));
        Assert.Equal(0, (int)WitSealMode.Gate);
    }

    [Theory]
    [InlineData(WitSealMode.Gate)]
    [InlineData(WitSealMode.Witness)]
    public void Members_are_defined(WitSealMode mode)
    {
        Assert.True(Enum.IsDefined(mode));
    }
}
