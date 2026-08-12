namespace OpenDispatch.Application.Sync;

/// <summary>
/// The operations a technician's device can push, and what their payloads have to say.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary lives in one place because two things read it and would otherwise disagree: the
/// handler that applies an operation, and every test and client that names one. They are strings
/// on the wire on purpose (see <c>SyncOp</c> in Contracts) — a phone a month behind must still be
/// able to push what it recorded, and a phone a release ahead must be able to name something this
/// server has never heard of and be told so, one operation at a time.
/// </para>
/// <para>
/// Everything here is about a job, which is not a simplification: the field workflow in this
/// version is a technician moving one job through its day, writing down what they found, and
/// recording what the work took. Attachments arrive separately (step 50b) because a photo is a
/// blob rather than an operation.
/// </para>
/// <para>
/// The payload shapes are documented here rather than declared in <c>Contracts</c>. Step 21 made
/// <c>SyncOp.Payload</c> deliberately opaque so that an unknown operation cannot break
/// deserialization for the whole batch, and typing the payloads on the wire would undo that. What
/// the technician app should send is stated below; whether that becomes a generated type is the
/// endpoint's decision (step 50), when Documents 6–7 say what the app records.
/// </para>
/// </remarks>
public static class FieldOps
{
    /// <summary>The only kind of entity a field operation names in this version.</summary>
    /// <remarks>
    /// The operation's <c>EntityId</c> is then a job id. It is a bare <see cref="Guid"/> until
    /// this has been checked, which is the moment the protocol's "some entity" becomes the
    /// domain's "a job".
    /// </remarks>
    public const string JobEntity = "job";

    /// <summary>
    /// Moves the job through its life. Payload: <c>{ "status": "InProgress" }</c>, or
    /// <c>{ "status": "Completed", "completedAt": "2026-08-10T14:05:00Z" }</c>.
    /// </summary>
    /// <remarks>
    /// The status travels as its name, exactly as <c>JobStatus</c> does everywhere else on the
    /// wire. <c>completedAt</c> is optional and only read for a completion — a job finished in a
    /// basement at two and synced at six finished at two.
    /// </remarks>
    public const string StatusChange = "status_change";

    /// <summary>
    /// Records what the technician found. Payload: <c>{ "text": "Meter behind the boiler." }</c>.
    /// </summary>
    /// <remarks>
    /// Last-write-wins, decided by the operation's <c>ClientTs</c> rather than by which push
    /// arrived first — see <c>Job.CanRecordNotes</c>.
    /// </remarks>
    public const string AddNote = "add_note";

    /// <summary>
    /// Records what the work took. Payload:
    /// <c>{ "kind": "Part", "description": "Run capacitor", "quantity": 2, "unitPriceCents": 2850 }</c>.
    /// </summary>
    /// <remarks>
    /// The price is in cents because <c>Money</c> is, and a JSON number of dollars is a rounding
    /// argument waiting to happen. Appends rather than replaces, so it cannot conflict: two
    /// technicians recording two parts recorded two parts.
    /// </remarks>
    public const string AddLineItem = "add_line_item";
}
