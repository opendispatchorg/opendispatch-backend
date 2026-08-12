using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.ValueObjects;

/// <summary>
/// Overlap is the rule the scheduler leans on to keep two stops off one technician at the
/// same time, so the boundary cases — back-to-back windows, containment, differing UTC
/// offsets — are the ones worth pinning down.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class TimeWindowTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RejectsAWindowThatEndsBeforeItStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeWindow(Midnight.AddHours(11), Midnight.AddHours(9)));
    }

    [Fact]
    public void AllowsAZeroLengthWindow()
    {
        var instant = Hours(9, 9);

        Assert.Equal(TimeSpan.Zero, instant.Duration);
    }

    [Fact]
    public void DurationIsTheSpanBetweenStartAndEnd()
    {
        Assert.Equal(TimeSpan.FromHours(2.5), Hours(9, 11.5).Duration);
    }

    [Theory]
    [InlineData(9d, 11d, 9d, 11d, true)]    // identical
    [InlineData(9d, 11d, 10d, 12d, true)]   // the other one starts inside this one
    [InlineData(10d, 12d, 9d, 11d, true)]   // the other one ends inside this one
    [InlineData(9d, 17d, 12d, 13d, true)]   // fully contained
    [InlineData(9d, 11d, 11d, 13d, false)]  // back to back: a valid route, not a conflict
    [InlineData(9d, 11d, 13d, 15d, false)]  // disjoint
    public void OverlapsOnlyWhenTheWindowsShareAnInstant(
        double aStart, double aEnd, double bStart, double bEnd, bool expected)
    {
        var a = Hours(aStart, aEnd);
        var b = Hours(bStart, bEnd);

        Assert.Equal(expected, a.Overlaps(b));
        Assert.Equal(expected, b.Overlaps(a));
    }

    /// <summary>
    /// The rule two things depend on: the engine charges for lateness in its objective, and the
    /// board colours a block by it. Stated once here so a stop the optimiser priced as on time
    /// cannot be drawn late.
    /// </summary>
    [Theory]
    [InlineData(9d, 11d, 8d, 0d)]     // before the window even opens: early, not late
    [InlineData(9d, 11d, 9d, 0d)]     // exactly on the promise
    [InlineData(9d, 11d, 10.5d, 0d)]  // inside it
    [InlineData(9d, 11d, 11d, 0d)]    // half-open: beginning as it closes is late by nothing
    [InlineData(9d, 11d, 11.5d, 30d)] // half an hour past
    [InlineData(9d, 11d, 15d, 240d)]  // four hours past
    public void LatenessIsMeasuredFromTheCloseOfTheWindow(
        double start, double end, double began, double expectedMinutes)
    {
        var promised = Hours(start, end);

        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            promised.LatenessOf(Hours(began, began).Start));
    }

    /// <summary>
    /// A job that begins inside its window and overruns has kept the promise: the customer was
    /// told when somebody would turn up, not when they would leave.
    /// </summary>
    [Fact]
    public void WorkThatBeginsInsideTheWindowIsNeverLateHoweverLongItRuns()
    {
        var promised = Hours(9, 11);

        // Started at 10:55 and still going at midnight.
        Assert.Equal(TimeSpan.Zero, promised.LatenessOf(Hours(10.9166667, 11).Start));
    }

    [Fact]
    public void OverlapsComparesAbsoluteInstantsNotWallClockTime()
    {
        // 09:00-11:00 in Berlin is 07:00-09:00 UTC.
        var berlinMorning = new TimeWindow(
            new DateTimeOffset(2026, 8, 9, 9, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 8, 9, 11, 0, 0, TimeSpan.FromHours(2)));

        Assert.True(berlinMorning.Overlaps(Hours(8, 10)));

        // Same wall-clock numbers as the Berlin window, two hours later in real time —
        // it starts exactly as the Berlin window closes.
        Assert.False(berlinMorning.Overlaps(Hours(9, 11)));
    }

    private static TimeWindow Hours(double start, double end) =>
        new(Midnight.AddHours(start), Midnight.AddHours(end));
}
