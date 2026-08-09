using System.Collections.Frozen;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// A technician as the scheduler sees them: who they are, what they may be given, the hours
/// they are available, and where their day starts and ends.
/// </summary>
/// <remarks>
/// <para>
/// The resource side of the problem, and the mirror of <see cref="SchedJob"/> — only the
/// facts that constrain where work can go.
/// </para>
/// <para>
/// The skill set is rebuilt here with a case-insensitive comparer rather than trusting
/// whatever the caller hands over. That is the point of taking an <see cref="IEnumerable{T}"/>
/// rather than a set: a plain <c>HashSet&lt;string&gt;</c> arriving from a mapper that forgot
/// the comparer would fail case-sensitively, and the symptom is not an error — it is a job
/// nobody can be assigned to and no explanation of why.
/// </para>
/// <para>
/// A class rather than a record: the skills are a collection, and a record's generated
/// equality would compare them by reference while reading like value equality.
/// </para>
/// </remarks>
public sealed class TechPlan
{
    private static readonly StringComparer SkillComparer = StringComparer.OrdinalIgnoreCase;

    private readonly FrozenSet<string> _skills;

    /// <summary>Describes a technician to the engine.</summary>
    /// <param name="id">Which technician this is.</param>
    /// <param name="skills">
    /// What they are qualified for. May be empty — a trainee simply matches no skilled job.
    /// Copied, so a later change to the caller's collection cannot move a technician's
    /// qualifications out from under a solve in progress.
    /// </param>
    /// <param name="shift">The hours they are available. A hard constraint.</param>
    /// <param name="homeBase">Where their day starts and ends.</param>
    /// <exception cref="ArgumentException">A skill is blank.</exception>
    public TechPlan(TechnicianId id, IEnumerable<string> skills, TimeWindow shift, GeoPoint homeBase)
    {
        ArgumentNullException.ThrowIfNull(skills);

        Id = id;
        Shift = shift;
        HomeBase = homeBase;
        _skills = skills.Select(NormalizeSkill).ToFrozenSet(SkillComparer);
    }

    /// <summary>Which technician this plan is for.</summary>
    public TechnicianId Id { get; }

    /// <summary>
    /// What they are qualified to work on. Looks up without regard to case, so "HVAC" finds
    /// "hvac".
    /// </summary>
    public IReadOnlySet<string> Skills => _skills;

    /// <summary>
    /// The hours they are available. Work must fit inside this — a hard constraint, unlike
    /// the customer's promised window.
    /// </summary>
    public TimeWindow Shift { get; }

    /// <summary>The origin of the first drive of the day and the destination of the last.</summary>
    public GeoPoint HomeBase { get; }

    /// <summary>
    /// Whether they can take this kind of work. The hard constraint checked before a job may
    /// be placed on their day.
    /// </summary>
    public bool HasSkill(string skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        return _skills.Contains(skill.Trim());
    }

    private static string NormalizeSkill(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill))
        {
            throw new ArgumentException("A skill must be named.", nameof(skill));
        }

        return skill.Trim();
    }
}
