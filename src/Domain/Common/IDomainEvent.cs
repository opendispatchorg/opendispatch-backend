namespace OpenDispatch.Domain.Common;

/// <summary>
/// Something that happened in the domain, described in the past tense.
/// </summary>
/// <remarks>
/// <para>
/// Domain events are the extension seam of the whole system. An aggregate raises one to
/// say what it did; anything that needs to react subscribes. That is what lets invoicing,
/// notifications and inventory arrive later as new handlers rather than as edits to the
/// code that completes a job.
/// </para>
/// <para>
/// Implementations are records: an event is a value describing a fact, and facts do not
/// change once stated. The catalog of concrete events is frozen in step 13.
/// </para>
/// </remarks>
public interface IDomainEvent
{
    /// <summary>
    /// When the thing being described happened. Stamped by the event itself where the
    /// moment of raising is the moment it happened, and passed in where the domain knows
    /// better — a job completed in a basement is reported when the phone next has signal.
    /// </summary>
    DateTimeOffset OccurredAt { get; }
}
