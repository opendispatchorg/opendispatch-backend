using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests.Model;

/// <summary>
/// The whole input to the engine. What it refuses are the shapes no schedule could honour.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SchedulingProblemTests
{
    [Fact]
    public void RefusesTheSameTechnicianTwice()
    {
        // Routes are keyed by technician, so a duplicate would silently become one route and
        // one of the two shifts would be forgotten.
        var id = TechnicianId.New();

        Assert.Throws<ArgumentException>(
            () => SchedulingProblemBuilder.Any()
                .Staffed(TechPlanBuilder.Any().WithId(id).Build(), TechPlanBuilder.Any().WithId(id).Build())
                .Build());
    }

    [Fact]
    public void RefusesTheSameJobTwice()
    {
        // A job can be placed once. Booked twice, it would be either double-scheduled or
        // half-reported, and the caller writing assignments would not be able to tell which.
        var id = JobId.New();

        Assert.Throws<ArgumentException>(
            () => SchedulingProblemBuilder.Any()
                .Booked(SchedJobBuilder.Any().WithId(id).Build(), SchedJobBuilder.Any().WithId(id).Build())
                .Build());
    }

    [Fact]
    public void AcceptsAJobPromisedOutsideTheHorizon()
    {
        // Deliberate: a window is a promise, not a boundary. Clipping the problem to its
        // horizon would turn soft lateness into a hard constraint by the back door, and
        // produce exactly the mysteriously unschedulable job the design rejects.
        var tomorrow = new TimeWindow(SmallCity.Day.End, SmallCity.Day.End.AddHours(2));

        var problem = SchedulingProblemBuilder.Any()
            .Over(SmallCity.Day)
            .Booked(SchedJobBuilder.Any().InWindow(tomorrow).Build())
            .Build();

        Assert.Single(problem.Jobs);
    }

    [Fact]
    public void AnEmptyDayIsAValidProblem()
    {
        var problem = SchedulingProblemBuilder.Any().Build();

        Assert.Empty(problem.Technicians);
        Assert.Empty(problem.Jobs);
    }
}
