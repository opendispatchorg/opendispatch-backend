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

    [Fact]
    public void CreatingAnAssignmentAnnouncesNothingByItself()
    {
        var assignment = AssignmentBuilder.Any().Build();

        Assert.Empty(assignment.DomainEvents);
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
        var assignment = AssignmentBuilder.Any().ForJob(job).ForTechnician(technician).Build();

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
        var assignment = AssignmentBuilder.Any().AtSequence(1).StartingAt(Midday).AfterTravel(5d).Build();

        Assert.Throws<DomainException>(() => assignment.Reschedule(Midday.AddHours(2), sequence, travelMin));

        Assert.Equal(Midday, assignment.ScheduledStart);
        Assert.Equal(1, assignment.Sequence);
        Assert.Equal(5d, assignment.TravelMin);
        Assert.Empty(assignment.DomainEvents);
    }

    [Fact]
    public void ReassigningHandsTheStopOverAndAnnouncesTheNewOwner()
    {
        var assignment = AssignmentBuilder.Any().Build();
        var cover = TechnicianId.New();

        assignment.Reassign(cover);

        Assert.Equal(cover, assignment.TechnicianId);

        var changed = Assert.IsType<AssignmentChanged>(Assert.Single(assignment.DomainEvents));
        Assert.Equal(cover, changed.TechnicianId);
    }

    [Fact]
    public void EveryRewriteIsAnnouncedSoTheBoardNeverMissesOne()
    {
        var assignment = AssignmentBuilder.Any().Build();

        assignment.Reassign(TechnicianId.New());
        assignment.Reschedule(Midday, sequence: 0, travelMin: 0d);

        Assert.Collection(
            assignment.DomainEvents,
            e => Assert.IsType<AssignmentChanged>(e),
            e => Assert.IsType<AssignmentChanged>(e));
    }
}
