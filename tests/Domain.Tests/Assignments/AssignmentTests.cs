using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Assignments;

/// <summary>
/// An assignment is rewritten on every drag and every optimiser run, so what matters is
/// that a rewrite either lands completely and announces itself, or is refused and changes
/// nothing at all. A half-applied reschedule would put a technician somewhere the board
/// never heard about.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AssignmentTests
{
    private static readonly DateTimeOffset Midday = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The assertion that had to change, and the defect it was quietly protecting: nothing
    /// announced a stop coming into existence, so optimising a day — which creates stops and moves
    /// none — repainted no board at all. The aggregate says it itself now, because the two slices
    /// that plan work both go through this factory and neither remembered to.
    /// </summary>
    [Fact]
    public void PlanningAStopAnnouncesItOnce()
    {
        var assignment = AssignmentBuilder.Any().Build();

        var planned = Assert.IsType<AssignmentPlanned>(Assert.Single(assignment.DomainEvents));

        Assert.Equal(assignment.Id, planned.AssignmentId);
        Assert.Equal(assignment.JobId, planned.JobId);
        Assert.Equal(assignment.TechnicianId, planned.TechnicianId);
    }

    /// <summary>
    /// A stop appearing and a stop moving stay different facts, even though the board draws them
    /// the same way — a subscriber that wants "this work has just been booked" must not have to
    /// guess from a change event whether it was the first one.
    /// </summary>
    [Fact]
    public void APlannedStopIsNotAChangedOne()
    {
        var assignment = AssignmentBuilder.Any().Build();

        Assert.IsNotType<AssignmentChanged>(Assert.Single(assignment.DomainEvents));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsAStopBeforeTheStartOfTheDay(int sequence)
    {
        Assert.Throws<DomainException>(() => AssignmentBuilder.Any().AtSequence(sequence).Build());
    }

    [Theory]
    [InlineData(-0.1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsTravelThatIsNotAFiniteNonNegativeNumberOfMinutes(double travelMin)
    {
        Assert.Throws<DomainException>(() => AssignmentBuilder.Any().AfterTravel(travelMin).Build());
    }

    [Fact]
    public void ReschedulingMovesTheWholePlanAndAnnouncesIt()
    {
        var job = JobId.New();
        var technician = TechnicianId.New();
        var assignment = Planned(AssignmentBuilder.Any().ForJob(job).ForTechnician(technician).Build());

        assignment.Reschedule(Midday, sequence: 3, travelMin: 22.5d);

        Assert.Equal(Midday, assignment.ScheduledStart);
        Assert.Equal(3, assignment.Sequence);
        Assert.Equal(22.5d, assignment.TravelMin);

        var changed = Assert.IsType<AssignmentChanged>(Assert.Single(assignment.DomainEvents));
        Assert.Equal(assignment.Id, changed.AssignmentId);
        Assert.Equal(job, changed.JobId);
        Assert.Equal(technician, changed.TechnicianId);
    }

    [Theory]
    [InlineData(-1, 10d)]
    [InlineData(0, -10d)]
    [InlineData(0, double.NaN)]
    public void ARefusedRescheduleLeavesThePlanExactlyAsItWas(int sequence, double travelMin)
    {
        var assignment = Planned(AssignmentBuilder.Any().AtSequence(1).StartingAt(Midday).AfterTravel(5d).Build());

        Assert.Throws<DomainException>(() => assignment.Reschedule(Midday.AddHours(2), sequence, travelMin));

        Assert.Equal(Midday, assignment.ScheduledStart);
        Assert.Equal(1, assignment.Sequence);
        Assert.Equal(5d, assignment.TravelMin);
        Assert.Empty(assignment.DomainEvents);
    }

    [Fact]
    public void ReassigningHandsTheStopOverAndAnnouncesTheNewOwner()
    {
        var assignment = Planned(AssignmentBuilder.Any().Build());
        var cover = TechnicianId.New();

        assignment.Reassign(cover);

        Assert.Equal(cover, assignment.TechnicianId);

        var changed = Assert.IsType<AssignmentChanged>(Assert.Single(assignment.DomainEvents));
        Assert.Equal(cover, changed.TechnicianId);
    }

    [Fact]
    public void EveryRewriteIsAnnouncedSoTheBoardNeverMissesOne()
    {
        var assignment = Planned(AssignmentBuilder.Any().Build());

        assignment.Reassign(TechnicianId.New());
        assignment.Reschedule(Midday, sequence: 0, travelMin: 0d);

        Assert.Collection(
            assignment.DomainEvents,
            e => Assert.IsType<AssignmentChanged>(e),
            e => Assert.IsType<AssignmentChanged>(e));
    }

    /// <summary>
    /// A stop as it exists once it has been saved: planned, and its creation already announced.
    /// </summary>
    /// <remarks>
    /// The clear is what a real save does — the <c>SaveChanges</c> interceptor collects and clears
    /// each aggregate's events after committing — so a test about what a <em>rewrite</em> announces
    /// starts where the next request starts. Without it every such test would assert on the
    /// creation event as well, which is a different claim and has its own case above.
    /// </remarks>
    private static Assignment Planned(Assignment assignment)
    {
        assignment.ClearDomainEvents();

        return assignment;
    }
}
