using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Scheduling;
using OpenDispatch.Application.Scheduling.InsertJob;
using OpenDispatch.Application.Scheduling.NormalizeDay;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Scheduling;

/// <summary>
/// The way back from a day a dispatcher has dragged into a shape no van could drive.
/// </summary>
/// <remarks>
/// <para>
/// The manual path is an instruction and is carried out literally: two stops can end up on the same
/// hour, and from that moment the emergency-insert path refuses every job for that technician,
/// because there is no run to insert into. Until this slice existed the only answer was to
/// re-optimise, which rewrites everybody's day to fix one.
/// </para>
/// <para>
/// So what these are about is the repair keeping every decision it is not entitled to change — who
/// does the work, in what order, and no earlier than the customer was told — while making the day
/// drivable again.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class NormalizeDayTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private static readonly GeoPoint Depot = new(51.5074d, -0.1278d);
    private static readonly GeoPoint Slough = new(51.5107d, -0.5950d);
    private static readonly GeoPoint Croydon = new(51.3762d, -0.0982d);

    /// <summary>
    /// The whole point, end to end: a day that refuses an emergency accepts one after the repair.
    /// </summary>
    [Fact]
    public async Task TurnsADayThatRefusesEmergenciesBackIntoOneThatTakesThem()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnicianAsync(slice);

        var morning = await ABookedJobAsync(slice, Slough);
        var alsoMorning = await ABookedJobAsync(slice, Croydon);

        // Both dropped on the same hour by hand, which no van can do.
        await slice.Send(new AssignJobCommand(morning, technician, MondayMorning.AddHours(1)));
        await slice.Send(new AssignJobCommand(alsoMorning, technician, MondayMorning.AddHours(1)));

        var emergency = await ABookedJobAsync(slice, Depot, JobPriority.Emergency);
        var refused = await slice.Send(new InsertJobCommand(emergency));
        Assert.Equal(SchedulingErrors.OverlappingDayCode, refused.Error!.Code);

        var repaired = await slice.Send(new NormalizeDayCommand(technician, Day.Start, Day.End));

        Assert.True(repaired.IsSuccess);
        Assert.Equal(2, repaired.Value.Stops);
        Assert.Equal(1, repaired.Value.Moved);

        // And the emergency now has somewhere to go.
        var inserted = await slice.Send(new InsertJobCommand(emergency));
        Assert.True(inserted.IsSuccess);
    }

    /// <summary>
    /// The repair changes the clock and nothing else: the same technician, the same work, in the
    /// same order — and no stop pulled earlier than the customer was told.
    /// </summary>
    [Fact]
    public async Task KeepsTheOrderAndNeverMovesAStopEarlier()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnicianAsync(slice);

        var first = await ABookedJobAsync(slice, Slough);
        var second = await ABookedJobAsync(slice, Croydon);

        // Overlapping but ordered: an hour of work each, dropped fifteen minutes apart. Two stops
        // dropped on the *same* instant would be a fair thing to repair and a bad thing to assert
        // about — with nothing between them the order falls to the tie-break, which is the
        // assignment id, which is a fresh guid per run.
        var promised = MondayMorning.AddHours(1);
        await slice.Send(new AssignJobCommand(first, technician, promised));
        await slice.Send(new AssignJobCommand(second, technician, promised.AddMinutes(15)));

        await slice.Send(new NormalizeDayCommand(technician, Day.Start, Day.End));

        var day = slice.Store<Assignment>().Saved
            .OrderBy(stop => stop.Sequence)
            .ToList();

        Assert.Equal([first, second], day.Select(stop => stop.JobId).ToList());
        Assert.All(day, stop => Assert.Equal(technician, stop.TechnicianId));
        Assert.All(day, stop => Assert.True(
            stop.ScheduledStart >= promised,
            $"a stop moved earlier than the {promised:t} it was promised for"));

        // An hour of work each, so the second cannot begin until the first is done and driven from.
        Assert.True(day[1].ScheduledStart >= day[0].ScheduledStart.AddHours(1));
        Assert.Equal([0, 1], day.Select(stop => stop.Sequence));
    }

    /// <summary>A day that was already drivable is left exactly as it was.</summary>
    [Fact]
    public async Task ChangesNothingAboutADayThatIsAlreadyARoute()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnicianAsync(slice);

        var first = await ABookedJobAsync(slice, Slough);
        var second = await ABookedJobAsync(slice, Croydon);

        await slice.Send(new AssignJobCommand(first, technician, MondayMorning.AddHours(1)));
        await slice.Send(new AssignJobCommand(second, technician, MondayMorning.AddHours(5)));

        var before = slice.Store<Assignment>().Saved
            .ToDictionary(stop => stop.JobId, stop => stop.ScheduledStart);

        var repaired = await slice.Send(new NormalizeDayCommand(technician, Day.Start, Day.End));

        Assert.Equal(0, repaired.Value.Moved);
        Assert.All(
            slice.Store<Assignment>().Saved,
            stop => Assert.Equal(before[stop.JobId], stop.ScheduledStart));
    }

    /// <summary>
    /// Re-timing cannot make a day shorter. Work that only fits when it overlaps is refused, because
    /// deciding what comes off the day belongs to a dispatcher.
    /// </summary>
    [Fact]
    public async Task RefusesADayThatDoesNotFitTheShiftHoweverItIsTimed()
    {
        await using var slice = SliceHost.Dispatching();

        // A half day, and three hours of work dropped into it on top of each other.
        var technician = await ATechnicianAsync(slice, shift: TimeSpan.FromHours(2));

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var job = await ABookedJobAsync(slice, Slough);
            await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddMinutes(15)));
        }

        var repaired = await slice.Send(new NormalizeDayCommand(technician, Day.Start, Day.End));

        Assert.Equal(SchedulingErrors.UndrivableDayCode, repaired.Error!.Code);
    }

    /// <summary>A technician with nothing on is not a failure — an empty day is a drivable day.</summary>
    [Fact]
    public async Task ReportsNothingToDoForATechnicianWithNoStops()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnicianAsync(slice);

        var repaired = await slice.Send(new NormalizeDayCommand(technician, Day.Start, Day.End));

        Assert.True(repaired.IsSuccess);
        Assert.Equal(0, repaired.Value.Stops);
        Assert.Null(repaired.Value.FirstStart);
    }

    [Fact]
    public async Task RefusesATechnicianThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Dispatching();

        var repaired = await slice.Send(new NormalizeDayCommand(TechnicianId.New(), Day.Start, Day.End));

        Assert.True(repaired.IsFailure);
    }

    private static async Task<TechnicianId> ATechnicianAsync(SliceHost slice, TimeSpan? shift = null)
    {
        var technician = await slice.Send(new CreateTechnicianCommand(
            "Sam Rivera",
            ["hvac"],
            Day.Start,
            Day.Start + (shift ?? TimeSpan.FromHours(9)),
            Depot.Lat,
            Depot.Lng));

        return technician.Value;
    }

    private static async Task<JobId> ABookedJobAsync(
        SliceHost slice,
        GeoPoint where,
        JobPriority priority = JobPriority.Normal)
    {
        var customer = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await slice.Send(new AddServiceLocationCommand(
            customer.Value, "Site", "12 Bath Road", where.Lat, where.Lng));

        var job = await slice.Send(new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            priority,
            Day.Start,
            Day.End,
            TimeSpan.FromHours(1)));

        return job.Value;
    }
}
