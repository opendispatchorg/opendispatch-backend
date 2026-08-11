using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Dispatch.GetBoard;

/// <summary>
/// The dispatch board for a stretch of time: every technician's lane, and the pile nobody is going
/// to.
/// </summary>
/// <param name="From">When the day opens.</param>
/// <param name="To">When it closes. Never earlier than <paramref name="From"/>.</param>
/// <remarks>
/// <para>
/// A day rather than a date, for the third time in this phase and the same reason: a date is not an
/// instant until somebody names a timezone, and the only thing that knows which one is the request.
/// </para>
/// <para>
/// It returns the projection the read port produces and does not reshape it. A second shape here
/// would be a second thing to keep in step with the SignalR board events, which are deltas against
/// exactly this.
/// </para>
/// </remarks>
public sealed record GetBoardQuery(DateTimeOffset From, DateTimeOffset To) : IQuery<DispatchBoard>;
