using OpenDispatch.Scheduling.Model;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Scheduling.Tests.Model;

/// <summary>
/// The prices the search compares every candidate schedule by.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class ObjectiveTests
{
    [Theory]
    [InlineData(-1d, 1d, 1d, 1d)]
    [InlineData(1d, -1d, 1d, 1d)]
    [InlineData(1d, 1d, -1d, 1d)]
    [InlineData(1d, 1d, 1d, -1d)]
    [InlineData(double.NaN, 1d, 1d, 1d)]
    [InlineData(1d, double.PositiveInfinity, 1d, 1d)]
    public void RefusesAPriceThatWouldBreakTheSearch(
        double travel, double lateness, double overtime, double unassigned)
    {
        // A negative weight pays the search to do the thing it is meant to avoid; an infinite
        // one is a hard constraint in disguise; a NaN makes every schedule incomparable to
        // every other. None of the three would throw later — they would just produce a
        // confidently wrong day.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Objective(travel, lateness, overtime, unassigned));
    }

    [Fact]
    public void AllowsAWeightOfZeroSoATermCanBeSwitchedOff()
    {
        var weights = new Objective(Travel: 1d, Lateness: 0d, Overtime: 0d, Unassigned: 0d);

        Assert.Equal(0d, weights.Lateness);
    }

    [Fact]
    public void TheDefaultPricesLatenessAboveDrivingAndDroppingAJobAboveEverything()
    {
        // Not a restatement of the numbers — the ordering is the design. Lateness has to hurt
        // more than a detour or the engine runs the whole day late for a shorter drive, and
        // dropping a job has to hurt more than any plausible amount of either.
        var weights = Objective.Default;

        Assert.True(weights.Lateness > weights.Travel);
        Assert.True(weights.Overtime > weights.Travel);
        Assert.True(weights.Unassigned > weights.Lateness * 60d);
    }
}
