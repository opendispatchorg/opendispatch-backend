using System.Text.Json;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync.PushOps;

/// <summary>
/// A device emptying its queue: everything a technician did while it could not reach the server.
/// </summary>
/// <param name="TechnicianId">Whose work this is.</param>
/// <param name="Ops">The operations, oldest first.</param>
/// <remarks>
/// <para>
/// <paramref name="Ops"/> order is meaningful and the handler keeps it. A technician who set off,
/// arrived, worked and finished pushes four operations that are only legal in that sequence, so
/// replaying them as they happened is what lets a whole morning land in one request.
/// </para>
/// <para>
/// The technician is on the command where the tenant deliberately is not, and the difference is
/// that nothing resolves a technician yet. <c>ITenantContext</c> makes the organization ambient
/// because a handler that trusted a request's <c>OrgId</c> could file a row under the wrong
/// tenant; there is no equivalent for the person, because there is no user-to-technician mapping
/// until auth lands. Step 50 supplies it from the authenticated principal, and if a second slice
/// ever needs it, that is an <c>ITechnicianContext</c> rather than a second parameter.
/// </para>
/// </remarks>
public sealed record PushOpsCommand(TechnicianId TechnicianId, IReadOnlyList<PushedOp> Ops)
    : ICommand<PushedBatch>
{
    /// <summary>
    /// The most operations one push may carry.
    /// </summary>
    /// <remarks>
    /// An invented number, like the bounds in <c>GenerateInvoiceValidator</c>, guarding the same
    /// kind of thing: the batch is applied in one transaction, so an unbounded batch is an
    /// unbounded transaction. A technician's day is tens of operations; a thousand is a device with
    /// a bug, and telling it so is kinder than timing out. It is on the command rather than in the
    /// validator because a device has to be told the limit it is being held to.
    /// </remarks>
    public const int MaxOps = 1_000;
}

/// <summary>
/// One operation as it arrives from a device.
/// </summary>
/// <param name="Id">
/// The device's own id for the operation, and the idempotency key. A phone that pushes, loses
/// signal on the response and pushes again sends the same id.
/// </param>
/// <param name="Entity">What kind of thing it was done to. See <see cref="FieldOps"/>.</param>
/// <param name="EntityId">
/// Which one. A bare <see cref="Guid"/> because what it identifies depends on
/// <paramref name="Entity"/> — it becomes a <c>JobId</c> at the moment the entity turns out to be
/// a job, and not before.
/// </param>
/// <param name="Type">What was done. See <see cref="FieldOps"/>.</param>
/// <param name="Payload">
/// The operation's own data, shaped by <paramref name="Type"/> and opaque until the handler knows
/// which kind it is holding.
/// </param>
/// <param name="BaseVersion">The version of the entity the device was looking at when it acted.</param>
/// <param name="ClientTs">
/// When it happened on the device, which is not when it arrived. This is what last-write-wins on
/// free text is decided by, and what the recorded work is timed with.
/// </param>
/// <remarks>
/// The application's own shape rather than the wire's <c>SyncOp</c>: <c>Contracts</c> is not on
/// this layer's reference list (Document 2 §2), so the endpoint maps one to the other. They are
/// deliberately the same fields — a difference between them would be a translation somebody has to
/// keep right.
/// </remarks>
public sealed record PushedOp(
    SyncOpId Id,
    string Entity,
    Guid EntityId,
    string Type,
    JsonElement Payload,
    long BaseVersion,
    DateTimeOffset ClientTs);
