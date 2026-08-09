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
