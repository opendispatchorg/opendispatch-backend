namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// What time it is.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than a convenience: handlers that stamp an invoice or judge whether a
/// technician is running late are testable only if the present is something a test can
/// state. Code that calls <c>DateTimeOffset.UtcNow</c> directly cannot be tested without
/// waiting.
/// </para>
/// <para>
/// UTC only, deliberately. Every instant in the system is a <see cref="DateTimeOffset"/>, so
/// local time is a display concern the clients own; a <c>Now</c> here would invite handlers
/// to reason in whatever timezone the server happens to be in.
/// </para>
/// <para>
/// Note that the aggregates take the instant as a parameter — <c>Job.MarkCompleted(at)</c>,
/// <c>Invoice.CreateFromJob(..., issued)</c> — rather than reading a clock themselves. Work
/// finished offline happened before it was reported, so the domain must be told when, and
/// this port is what the handler consults when nobody has told it anything better.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
