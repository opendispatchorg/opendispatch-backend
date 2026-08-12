namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// A job, inside a <see cref="SyncChange"/> whose <c>Entity</c> is <c>"job"</c>.
/// </summary>
/// <remarks>
/// Typed, unlike <see cref="SyncOp.Payload"/> beside it — and the asymmetry is deliberate, not an
/// inconsistency. A pushed op's payload is client-authored: a phone a release behind or ahead of
/// the server must still be able to send one, so it stays an opaque <c>JsonElement</c> the handler
/// reads defensively (Document 3, step 42). A pulled change's payload is server-authored: this
/// server always knows its own current shape, so there is nothing an opaque envelope would protect
/// against. <c>JobId</c> and <c>Version</c> are not repeated here — they already ride on the
/// <see cref="SyncChange"/> envelope, and a value carried twice in one message is one a future
/// change could make disagree with itself.
/// </remarks>
/// <param name="Status">How far through its life it is — which buttons the app offers.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="RequiredSkill">What it takes to do it.</param>
/// <param name="WindowStart">When the promised window opens.</param>
/// <param name="WindowEnd">When the promised window closes.</param>
/// <param name="EstimatedDuration">How long the work should take.</param>
/// <param name="Latitude">Where it is, for navigation.</param>
/// <param name="Longitude">Where it is, for navigation.</param>
/// <param name="CustomerName">Who it is for.</param>
/// <param name="Address">Where it is, for a human.</param>
/// <param name="Notes">What has been written about it, or <see langword="null"/> if nothing has.</param>
/// <param name="NotesRecordedAt">
/// When the notes were written. The device needs it to know whether its own unsent note would
/// win — the same comparison the server will make.
/// </param>
/// <param name="Lines">What the work has taken so far, in the order it was recorded.</param>
public sealed record SyncJobPayload(
    JobStatus Status,
    JobPriority Priority,
    string RequiredSkill,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    TimeSpan EstimatedDuration,
    double Latitude,
    double Longitude,
    string CustomerName,
    string Address,
    string? Notes,
    DateTimeOffset? NotesRecordedAt,
    IReadOnlyList<SyncJobLinePayload> Lines);

/// <summary>One line of what a job's work has taken, inside a <see cref="SyncJobPayload"/>.</summary>
/// <param name="Id">The line's identity within its job.</param>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it was.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">What one costs, in dollars.</param>
public sealed record SyncJobLinePayload(
    Guid Id,
    LineItemKind Kind,
    string Description,
    decimal Quantity,
    decimal UnitPrice);
