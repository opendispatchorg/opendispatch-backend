using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests.Model;

/// <summary>
/// The engine's answer. The guards here catch the solver bugs that would otherwise leave the
/// building as a schedule: a job promised to two technicians, a job both booked and reported
/// undone, a technician in two places at once.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SolutionTests
{
    private static readonly TechnicianId Sam = SmallCity.Sam.Id;
    private static readonly TechnicianId Alex = SmallCity.Alex.Id;

    [Fact]
    public void RefusesAJobPlacedOnTwoTechnicians()
    {
        var job = JobId.New();

        Assert.Throws<ArgumentException>(() => new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>
            {
                [Sam] = [StopFor(job, 9)],
                [Alex] = [StopFor(job, 13)],
            },
            [],
            0d));
    }

    [Fact]
    public void RefusesAJobPlacedTwiceOnOneTechnician()
    {
        var job = JobId.New();

        Assert.Throws<ArgumentException>(() => new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>> { [Sam] = [StopFor(job, 9), StopFor(job, 13)] },
            [],
            0d));
    }

    [Fact]
    public void RefusesAJobThatIsBothPlacedAndReportedUndone()
    {
        // The dispatch board reads both halves of the answer. A job in both would appear on
        // someone's timeline and in the unassigned pile at the same time.
        var job = JobId.New();

        Assert.Throws<ArgumentException>(() => new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>> { [Sam] = [StopFor(job, 9)] },
            [job],
            0d));
    }

    [Fact]
    public void RefusesATechnicianBeingInTwoPlacesAtOnce()
    {
        // Overlapping stops are the hard constraint that has no penalty attached, because a
        // schedule containing them is not a worse plan — it is not a plan.
        Assert.Throws<ArgumentException>(() => new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>
            {
                [Sam] =
                [
                    new Stop(JobId.New(), SmallCity.At(9), SmallCity.At(9), SmallCity.At(11), 0d),
                    new Stop(JobId.New(), SmallCity.At(10), SmallCity.At(10), SmallCity.At(12), 0d),
                ],
            },
            [],
            0d));
    }

    [Fact]
    public void AllowsBackToBackStopsWithNoDriveBetweenThem()
    {
        // Two jobs at the same address, or a drive short enough to round to nothing. Touching
        // is not overlapping.
        var solution = new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>
            {
                [Sam] =
                [
                    new Stop(JobId.New(), SmallCity.At(9), SmallCity.At(9), SmallCity.At(10), 0d),
                    new Stop(JobId.New(), SmallCity.At(10), SmallCity.At(10), SmallCity.At(11), 0d),
                ],
            },
            [],
            0d);

        Assert.Equal(2, solution.RouteFor(Sam).Length);
    }

    [Fact]
    public void RefusesACostThatCannotBeCompared()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>(), [], double.NaN));
    }

    [Fact]
    public void RefusesTheSameJobReportedUndoneTwice()
    {
        var job = JobId.New();

        Assert.Throws<ArgumentException>(() => new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>(), [job, job], 0d));
    }

    [Fact]
    public void ATechnicianWithNothingToDoHasAnEmptyDayRatherThanNoDay()
    {
        // So the caller writing assignments, and the board rendering the timeline, never have
        // to ask whether a technician is in the dictionary at all.
        var solution = new Solution(new Dictionary<TechnicianId, ImmutableArray<Stop>>(), [], 0d);

        Assert.Empty(solution.RouteFor(Sam));
    }

    private static Stop StopFor(JobId job, int hour) =>
        new(job, SmallCity.At(hour), SmallCity.At(hour), SmallCity.At(hour + 1), 10d);
}
