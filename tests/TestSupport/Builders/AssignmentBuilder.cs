using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="Assignment"/>s for tests, so a test states only the part of the plan
/// it is about.
/// </summary>
/// <example>
/// <code>
/// var stop = AssignmentBuilder.Any().ForTechnician(alice).AtSequence(2).Build();
/// </code>
/// </example>
/// <remarks>
/// Unlike <see cref="JobBuilder"/> this does not clear domain events, because creating an
/// assignment raises none — a test can assert that directly on a built one.
/// </remarks>
public sealed record AssignmentBuilder
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private OrgId Org { get; init; } = OrgId.New();

    private JobId Job { get; init; } = JobId.New();

    private TechnicianId Technician { get; init; } = TechnicianId.New();

    private int Sequence { get; init; }

    private DateTimeOffset ScheduledStart { get; init; } = MorningOf;

    private double TravelMin { get; init; } = 15d;

    /// <summary>An ordinary first stop of the morning.</summary>
    public static AssignmentBuilder Any() => new();

    /// <summary>Puts the assignment in a particular tenant.</summary>
    public AssignmentBuilder ForOrg(OrgId org) => this with { Org = org };

    /// <summary>Points the assignment at a particular job.</summary>
    public AssignmentBuilder ForJob(JobId job) => this with { Job = job };

    /// <summary>Puts the stop on a particular technician's day.</summary>
    public AssignmentBuilder ForTechnician(TechnicianId technician) => this with { Technician = technician };

    /// <summary>Places the stop at a position in the run.</summary>
    public AssignmentBuilder AtSequence(int sequence) => this with { Sequence = sequence };

    /// <summary>Sets when the technician is planned to arrive.</summary>
    public AssignmentBuilder StartingAt(DateTimeOffset start) => this with { ScheduledStart = start };

    /// <summary>Sets the drive to this stop, in minutes.</summary>
    public AssignmentBuilder AfterTravel(double travelMin) => this with { TravelMin = travelMin };

    /// <summary>Creates the assignment.</summary>
    public Assignment Build() =>
        Assignment.Create(Org, Job, Technician, Sequence, ScheduledStart, TravelMin);
}
