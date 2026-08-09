using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Technicians;

/// <summary>
/// A technician: who they are, what they can work on, when they are available, and where
/// their day starts and ends.
/// </summary>
/// <remarks>
/// This is the resource side of scheduling, and it carries exactly what the engine needs to
/// place work — skills for the hard skill-match constraint, a shift for the hard
/// fits-in-the-day constraint, and a home base to measure the first and last drive from.
/// Anything about a technician that the scheduler does not consume does not belong here.
/// </remarks>
public sealed class Technician : AggregateRoot
{
    /// <summary>
    /// Skills are matched without regard to case. They are free text typed on two different
    /// screens by two different people — the admin setting up a technician and the
    /// dispatcher booking a job — and "HVAC" failing to match "hvac" would not surface as
    /// an error. It would surface as a job that mysteriously never gets assigned, which is
    /// exactly the failure mode the soft-lateness design exists to avoid elsewhere.
    /// </summary>
    /// <remarks>
    /// The original casing is kept rather than folded, so a skill displays the way someone
    /// typed it. Only comparison ignores case.
    /// </remarks>
    private static readonly StringComparer SkillComparer = StringComparer.OrdinalIgnoreCase;

    private readonly HashSet<string> _skills;

    private Technician(
        TechnicianId id,
        OrgId orgId,
        string name,
        IEnumerable<string> skills,
        TimeWindow shift,
        GeoPoint homeBase)
    {
        Id = id;
        OrgId = orgId;
        Name = name;
        _skills = new HashSet<string>(skills, SkillComparer);
        Shift = shift;
        HomeBase = homeBase;
    }

    /// <summary>This technician's identity.</summary>
    public TechnicianId Id { get; private set; }

    /// <summary>The tenant they work for.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>Their name, as it appears on the dispatch board.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// What they are qualified to work on. Case-insensitive: adding "HVAC" to a technician
    /// who already has "hvac" changes nothing.
    /// </summary>
    public IReadOnlySet<string> Skills => _skills;

    /// <summary>
    /// The hours they are available over the planning horizon. A job must fit inside this —
    /// a hard constraint, unlike the customer's promised window.
    /// </summary>
    public TimeWindow Shift { get; private set; }

    /// <summary>Where their day starts and ends; the origin of the first drive and the destination of the last.</summary>
    public GeoPoint HomeBase { get; private set; }

    /// <summary>
    /// Takes on a new technician. A technician with no skills is allowed — a trainee simply
    /// matches no skilled job.
    /// </summary>
    /// <exception cref="DomainException">The technician has no name, or a skill is blank.</exception>
    public static Technician Create(
        OrgId orgId,
        string name,
        IEnumerable<string> skills,
        TimeWindow shift,
        GeoPoint homeBase)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A technician must have a name.");
        }

        var normalized = skills.Select(NormalizeSkill).ToList();

        return new Technician(TechnicianId.New(), orgId, name.Trim(), normalized, shift, homeBase);
    }

    /// <summary>
    /// Qualifies the technician for a kind of work. Adding a skill they already have,
    /// however it is cased, does nothing.
    /// </summary>
    /// <exception cref="DomainException">The skill is blank.</exception>
    public void AddSkill(string skill) => _skills.Add(NormalizeSkill(skill));

    /// <summary>
    /// Takes a skill away. Removing one they do not have does nothing — this is how a
    /// re-sent or replayed update stays harmless.
    /// </summary>
    /// <exception cref="DomainException">The skill is blank.</exception>
    public void RemoveSkill(string skill) => _skills.Remove(NormalizeSkill(skill));

    /// <summary>Replaces the hours they are available over the planning horizon.</summary>
    public void SetShift(TimeWindow shift) => Shift = shift;

    /// <summary>
    /// Whether they can take work of this kind. The hard constraint the scheduler checks
    /// before it will put a job on this technician's day.
    /// </summary>
    public bool HasSkill(string skill) => _skills.Contains(skill.Trim());

    private static string NormalizeSkill(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            throw new DomainException("A skill must be named.");
        }

        return skill.Trim();
    }
}
