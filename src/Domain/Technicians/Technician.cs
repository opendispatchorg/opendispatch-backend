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

    // Materialisation constructor — see the note on Job. The set is built with the comparer here
    // as well as in the mapping, so a technician cannot hold one that matches case-sensitively
    // even for the instant before the row is read into it.
    private Technician()
    {
        Name = string.Empty;
        _skills = new HashSet<string>(SkillComparer);
    }

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
    /// When they were retired, or <see langword="null"/> while they are still current.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Retiring is not deleting, and this system has no delete.</strong> A technician's name is on every stop they ever drove and every audit entry they wrote; a customer's is on their jobs and their invoices. A hard
    /// delete would orphan those references and there is nothing built to handle that, so what a
    /// shop actually needs — "stop offering them, keep the history" — is a flag and a filtered read.
    /// </para>
    /// <para>
    /// It is not erasure either. An erasure destroys what says who somebody is and is irreversible;
    /// this hides a record from the lists people pick from and can be undone tomorrow.
    /// </para>
    /// </remarks>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Whether they are still on the books.</summary>
    public bool IsActive => RetiredAt is null;

    /// <summary>Retires them: still in the history, no longer offered.</summary>
    /// <param name="at">When.</param>
    /// <remarks>Retiring somebody already retired does nothing, so a repeated click is harmless.</remarks>
    public void Retire(DateTimeOffset at)
    {
        RetiredAt ??= at;
    }

    /// <summary>Puts them back on the books.</summary>
    public void Reinstate()
    {
        RetiredAt = null;
    }

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

    /// <summary>
    /// Replaces what they are qualified to work on, wholesale. Passing nothing makes them a
    /// trainee again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The set-shaped counterpart to <see cref="AddSkill"/> and <see cref="RemoveSkill"/>, and it
    /// exists so nothing outside this aggregate has to work out the difference between the skills
    /// they have and the skills they should have. That difference is not arithmetic: two skills
    /// are the same skill when they differ only in case or surrounding whitespace, and a caller
    /// computing it with the wrong comparer would churn "HVAC" over "hvac" — or, worse, decide
    /// they are two skills. The rule about what makes two skills one lives here, with the set
    /// that enforces it.
    /// </para>
    /// <para>
    /// All or nothing: every skill is normalised before any of them is applied, so a blank one
    /// halfway down a list leaves the technician exactly as they were rather than holding half of
    /// an update.
    /// </para>
    /// </remarks>
    /// <exception cref="DomainException">A skill is blank.</exception>
    public void SetSkills(IEnumerable<string> skills)
    {
        var replacement = skills.Select(NormalizeSkill).ToList();

        _skills.Clear();
        _skills.UnionWith(replacement);
    }

    /// <summary>Replaces the hours they are available over the planning horizon.</summary>
    public void SetShift(TimeWindow shift) => Shift = shift;

    /// <summary>Corrects their name.</summary>
    /// <exception cref="DomainException">The technician would have no name.</exception>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A technician must have a name.");
        }

        Name = name.Trim();
    }

    /// <summary>Moves where their day starts and ends.</summary>
    public void SetHomeBase(GeoPoint homeBase) => HomeBase = homeBase;

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
