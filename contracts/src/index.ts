/**
 * @opendispatch/contracts — the wire shapes an OpenAPI document cannot describe:
 * SignalR board events, the offline-sync payloads, and the enums both halves share.
 *
 * Generated from the backend's `src/Contracts` by `make gen-contracts`. Do not edit —
 * change the C# and regenerate, which is what keeps a backend change a client compile
 * error rather than a runtime surprise.
 *
 * A C# `long` arrives as `number`: version stamps and cursors stay far inside the range
 * a double holds exactly. A `Guid` and a `DateTimeOffset` both arrive as `string`, the
 * latter in ISO 8601 with its offset.
 */

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts
// ---------------------------------------------------------------------------------------------

/** What a technician captured, as the clients see it. */
export const AttachmentKind = {
  /** A picture of the site, the fault, or the finished work. */
  Photo: 'Photo',
  /** The customer's signature, captured on the technician's screen. */
  Signature: 'Signature',
} as const;

export type AttachmentKind = (typeof AttachmentKind)[keyof typeof AttachmentKind];

/** Whether an invoice has been settled, as the clients see it. */
export const InvoiceStatus = {
  /** Raised but not settled. */
  Draft: 'Draft',
  /** Settled. Terminal. */
  Paid: 'Paid',
} as const;

export type InvoiceStatus = (typeof InvoiceStatus)[keyof typeof InvoiceStatus];

/** How badly a job needs doing, as the clients see it. */
export const JobPriority = {
  /** Can wait. Slipping it to another day costs little. */
  Low: 'Low',
  /** Ordinary booked work. */
  Normal: 'Normal',
  /** Wants doing today. */
  High: 'High',
  /** No heat, no water, no power. Bump whatever it takes. */
  Emergency: 'Emergency',
} as const;

export type JobPriority = (typeof JobPriority)[keyof typeof JobPriority];

/** Where a job has got to in its life, as the clients see it. */
export const JobStatus = {
  /** Booked, but nobody is going to it yet. */
  Unscheduled: 'Unscheduled',
  /** A technician and a time have been planned for it. */
  Scheduled: 'Scheduled',
  /** Sent to the technician's phone; it is on their day. */
  Dispatched: 'Dispatched',
  /** The technician is travelling to it. */
  EnRoute: 'EnRoute',
  /** The technician is on site doing the work. */
  InProgress: 'InProgress',
  /** The work is done. Nothing further happens to the job in the field. */
  Completed: 'Completed',
  /** The completed work has been turned into an invoice. */
  Invoiced: 'Invoiced',
  /** The invoice has been settled. Terminal. */
  Paid: 'Paid',
  /** Called off before the work was finished. Terminal. */
  Cancelled: 'Cancelled',
} as const;

export type JobStatus = (typeof JobStatus)[keyof typeof JobStatus];

/** What a billed line is for, as the clients see it. */
export const LineItemKind = {
  /** Time on the job, billed by the hour. */
  Labor: 'Labor',
  /** Something fitted or supplied, billed by the unit. */
  Part: 'Part',
} as const;

export type LineItemKind = (typeof LineItemKind)[keyof typeof LineItemKind];

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Attachments
// ---------------------------------------------------------------------------------------------

/**
 * The metadata half of `POST /jobs/{id}/attachments` — the multipart form's fields beside the
 * binary itself.
 */
export interface UploadAttachmentRequest {
  /** The id the device generated for the capture — the idempotency key. */
  readonly attachmentId: string;
  /**
   * The job it was captured against. Carried here as the build text names it, but the route's
   * own `{id}` is what the endpoint actually acts on — the same convention every other
   * `/jobs/{id}/...` route in this API already follows, of not trusting a body to restate an id
   * the URL already carries. The two are checked against each other; a mismatch is refused
   * rather than silently resolved one way.
   */
  readonly jobId: string;
  /** Photograph or signature. */
  readonly kind: AttachmentKind;
}

/** The response from `POST /jobs/{id}/attachments`. */
export interface UploadAttachmentResponse {
  /**
   * The handle the server holds this content under. Opaque — a client stores it and does not
   * parse it, the same register as a sync cursor.
   */
  readonly serverId: string;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Auth
// ---------------------------------------------------------------------------------------------

/** The body of `POST /auth/login`. */
export interface LoginRequest {
  /** Whoever is asking. */
  readonly username: string;
  /** Proof it is them. */
  readonly password: string;
}

/** What a successful `POST /auth/login` returns. */
export interface LoginResponse {
  /** The bearer token — send it as `Authorization: Bearer {Token}`. */
  readonly token: string;
  /** When the token stops being accepted. */
  readonly expiresAt: string;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Board
// ---------------------------------------------------------------------------------------------

/**
 * A stop was planned, moved within a day, or handed to another technician. Pushed to the org's
 * board group under `BoardEvents.AssignmentUpdated`.
 */
export interface AssignmentUpdated {
  /** Which stop. */
  readonly assignmentId: string;
  /** The job being planned — the block's contents. */
  readonly jobId: string;
  /** Whose lane it is now on. */
  readonly technicianId: string;
  /** Where it falls in that technician's run, counting from zero. */
  readonly sequence: number;
  /** When the technician is planned to arrive. */
  readonly scheduledStart: string;
  /** Minutes of driving to reach it from the previous stop — the gap before the block. */
  readonly travelMin: number;
  /**
   * The assignment's concurrency stamp after the change, so a client can discard an event older
   * than what it has already applied.
   */
  readonly version: number;
}

/**
 * The names the board's messages arrive under. A payload nobody can subscribe to is half a
 * contract, so the names live beside the shapes rather than as a literal in the hub and a
 * second, hopefully identical, literal in each client.
 */
export const BoardEvents = {
  /** Carries a `JobUpdated` payload. */
  JobUpdated: 'job.updated',
  /** Carries an `AssignmentUpdated` payload. */
  AssignmentUpdated: 'assignment.updated',
  /** Carries a `TechnicianMoved` payload. */
  TechnicianMoved: 'technician.moved',
} as const;

/** A job as the board shows it, over REST — the snapshot half of `JobUpdated`'s delta. */
export interface BoardJobResponse {
  /** Which job. */
  readonly jobId: string;
  /** How far through its life it is — what colours the block. */
  readonly status: JobStatus;
  /** How badly it needs doing. */
  readonly priority: JobPriority;
  /** What a technician needs to take it. */
  readonly requiredSkill: string;
  /** When the promised window opens. */
  readonly windowStart: string;
  /** When the promised window closes. */
  readonly windowEnd: string;
  /** How long the work should take — the width of the block. */
  readonly estimatedDuration: string;
  /** Where it is, in decimal degrees. */
  readonly latitude: number;
  /** Where it is, in decimal degrees. */
  readonly longitude: number;
  /** Who it is for. */
  readonly customerName: string;
  /** Where it is, for a human. */
  readonly address: string;
}

/** One technician's day, as it appears on the board over REST: a lane and a line on the map. */
export interface BoardRouteResponse {
  /** Whose day it is. */
  readonly technicianId: string;
  /** Their name, as the lane is labelled. */
  readonly name: string;
  /**
   * What they are qualified for. Here for the same reason it is on the projection this mirrors:
   * a manual assignment may put work on somebody who does not hold the job's skill, and the
   * board is what shows the mismatch.
   */
  readonly skills: readonly string[];
  /** When their working hours begin. */
  readonly shiftStart: string;
  /** When their working hours end. */
  readonly shiftEnd: string;
  /** Where the day starts and ends, in decimal degrees. */
  readonly homeLat: number;
  /** Where the day starts and ends, in decimal degrees. */
  readonly homeLng: number;
  /** Their run, in sequence order. */
  readonly stops: readonly BoardStopResponse[];
}

/** A planned visit, as it appears on a technician's lane over REST. */
export interface BoardStopResponse {
  /** The stop's own identity — what a drag on the board reschedules. */
  readonly assignmentId: string;
  /** Where it falls in the technician's run, counting from zero. */
  readonly sequence: number;
  /** When the technician is planned to start work. */
  readonly scheduledStart: string;
  /** Minutes of driving to get here from the previous stop. */
  readonly travelMin: number;
  /**
   * How far past the promised window the work is planned to begin, or zero when it begins
   * inside it.
   */
  readonly lateBy: string;
  /** What the visit is for. */
  readonly job: BoardJobResponse;
}

/** The response from `GET /dispatch/board?day=`: a snapshot of the day. */
export interface DispatchBoardResponse {
  /** The instant the requested day opens, UTC. */
  readonly dayStart: string;
  /** The instant the requested day closes, UTC. */
  readonly dayEnd: string;
  /**
   * One run per technician, each ordered by sequence. Technicians with nothing on are present
   * and empty.
   */
  readonly routes: readonly BoardRouteResponse[];
  /** Jobs promised inside the day that no technician has been given. */
  readonly unassigned: readonly BoardJobResponse[];
}

/**
 * A job moved through its lifecycle. Pushed to the org's board group under
 * `BoardEvents.JobUpdated`.
 */
export interface JobUpdated {
  /** Which job the client should apply this to. */
  readonly jobId: string;
  /** What the job's status now is. */
  readonly status: JobStatus;
  /**
   * The job's concurrency stamp after the change. Two dispatchers acting at once can have their
   * events arrive out of order; a client that keeps the highest version it has seen never
   * applies the older of the two.
   */
  readonly version: number;
}

/**
 * A technician is somewhere new — the pin on the board's map. Pushed to the org's board group
 * under `BoardEvents.TechnicianMoved`.
 */
export interface TechnicianMoved {
  /** Whose pin moves. */
  readonly technicianId: string;
  /** Latitude in degrees. */
  readonly lat: number;
  /** Longitude in degrees. */
  readonly lng: number;
  /**
   * When the device was there, not when the message was sent. A phone that was out of signal
   * reports a position several minutes old, and a board that showed it as current would be
   * confidently wrong.
   */
  readonly at: string;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Customers
// ---------------------------------------------------------------------------------------------

/** The body of `POST /customers`. */
export interface CreateCustomerRequest {
  /** Their name, personal or trading. */
  readonly name: string;
  /** An email address, or `null` if there isn't one. */
  readonly email: string | null;
  /** A phone number, or `null` if there isn't one. */
  readonly phone: string | null;
}

/**
 * One customer, as the clients see them: who they are, how to reach them, and where they want
 * work done.
 */
export interface CustomerResponse {
  /** Their identity. */
  readonly id: string;
  /** Their name, personal or trading. */
  readonly name: string;
  /** Their email address, or `null` if there isn't one. */
  readonly email: string | null;
  /** Their phone number, or `null` if there isn't one. */
  readonly phone: string | null;
  /** The places they want work done, in the order they were added. */
  readonly locations: readonly ServiceLocationResponse[];
}

/**
 * A customer as `GET /customers` lists them: enough to recognise and to reach, and nothing
 * else.
 */
export interface CustomerSummaryResponse {
  /** Their identity, which is what a caller picks them by. */
  readonly id: string;
  /** Their name, personal or trading. */
  readonly name: string;
  /** Their email address, or `null` if there isn't one. */
  readonly email: string | null;
  /** Their phone number, or `null` if there isn't one. */
  readonly phone: string | null;
}

/** The body of `POST /customers/{id}/locations` and `PUT .../locations/{locationId}`. */
export interface ServiceLocationRequest {
  /** What the customer calls it — "Home", "Unit 4", "the Croydon branch". */
  readonly label: string;
  /** The postal address a technician would be given. */
  readonly address: string;
  /** Where it is, in decimal degrees between -90 and 90. */
  readonly latitude: number;
  /** Where it is, in decimal degrees between -180 and 180. */
  readonly longitude: number;
}

/** One of a customer's service locations, as the clients see it. */
export interface ServiceLocationResponse {
  /** The identity a job points at. */
  readonly id: string;
  /** What the customer calls it. */
  readonly label: string;
  /** The postal address a technician would be given. */
  readonly address: string;
  /** Where it is, in decimal degrees. */
  readonly latitude: number;
  /** Where it is, in decimal degrees. */
  readonly longitude: number;
}

/** The body of `PUT /customers/{id}`. */
export interface UpdateCustomerRequest {
  /** Their name, personal or trading. */
  readonly name: string;
  /** An email address, or `null` if there isn't one. */
  readonly email: string | null;
  /** A phone number, or `null` if there isn't one. */
  readonly phone: string | null;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Export
// ---------------------------------------------------------------------------------------------

/** One stop, as it appears in `GET /export`. */
export interface AssignmentExport {
  /** The stop's identity. */
  readonly id: string;
  /** The job being planned. */
  readonly jobId: string;
  /** Whose day it sits on. */
  readonly technicianId: string;
  /** Where it falls in the technician's run, counting from zero. */
  readonly sequence: number;
  /** When the technician is planned to start work. */
  readonly scheduledStart: string;
  /** Minutes of driving to reach this stop from the previous one. */
  readonly travelMin: number;
}

/** One captured photo or signature's metadata, as it appears in `GET /export`. */
export interface AttachmentExport {
  /** The device's own id for the capture. */
  readonly id: string;
  /** The job it was captured against. */
  readonly jobId: string;
  /** Photograph or signature. */
  readonly kind: AttachmentKind;
  /** The handle its bytes are stored under. */
  readonly serverId: string;
  /** When it was captured, by the device's clock. */
  readonly createdAt: string;
}

/**
 * The whole of a tenant's business data — the response from `GET /export`, Document 1's
 * anti-lock-in feature: "your customers, your jobs, your data — on software you control."
 */
export interface ExportResponse {
  /** Every customer, with their service locations. */
  readonly customers: readonly CustomerResponse[];
  /** Every job, whatever its status. */
  readonly jobs: readonly JobResponse[];
  /** The plan: every stop, whichever technician it is on. */
  readonly assignments: readonly AssignmentExport[];
  /** Every bill raised, with its lines and whether it is settled. */
  readonly invoices: readonly InvoiceResponse[];
  /** Every photo and signature captured, by metadata — not their bytes. */
  readonly attachments: readonly AttachmentExport[];
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Invoicing
// ---------------------------------------------------------------------------------------------

/** The body of `POST /jobs/{id}/invoice`. The job comes from the route. */
export interface CreateInvoiceRequest {
  /** What to charge for — time on the job, and parts fitted. */
  readonly lines: readonly InvoiceLineRequest[];
}

/** One line to bill, as submitted to `POST /jobs/{id}/invoice`. */
export interface InvoiceLineRequest {
  /** Labour or a part. */
  readonly kind: LineItemKind;
  /** What it says on the invoice. */
  readonly description: string;
  /** How many — hours for labour, units for parts. Fractions are ordinary. */
  readonly quantity: number;
  /** The price of one, in whole currency units (dollars, not cents). Negative for a discount. */
  readonly unitPrice: number;
}

/** One billed line, as the clients see it. */
export interface InvoiceLineResponse {
  /** Labour or a part. */
  readonly kind: LineItemKind;
  /** What it says on the invoice. */
  readonly description: string;
  /** How many — hours for labour, units for parts. */
  readonly quantity: number;
  /** The price of one, in dollars. Negative for a discount. */
  readonly unitPrice: number;
  /** What this line adds to the invoice, in dollars. */
  readonly lineTotal: number;
}

/**
 * One invoice, as the clients see it — the response from `POST /jobs/{id}/invoice`, and the
 * shape every invoice takes in `GET /export`.
 */
export interface InvoiceResponse {
  /** Its identity. */
  readonly id: string;
  /** The job it bills. */
  readonly jobId: string;
  /** Whether it has been settled. */
  readonly status: InvoiceStatus;
  /** When it was raised. */
  readonly issued: string;
  /** What it bills for, in the order the lines were added. */
  readonly lines: readonly InvoiceLineResponse[];
  /** What is owed, in dollars. */
  readonly total: number;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Jobs
// ---------------------------------------------------------------------------------------------

/** The body of `POST /jobs/{id}/assign`. */
export interface AssignJobRequest {
  /** Whose day it goes on. */
  readonly technicianId: string;
  /** When they are planned to arrive. */
  readonly scheduledStart: string;
}

/** What a successful `POST /jobs/{id}/assign` returns. */
export interface AssignJobResponse {
  /** The identity of the stop it created or moved — the board has just drawn it. */
  readonly assignmentId: string;
}

/** The body of `POST /jobs/{id}/status`. */
export interface ChangeJobStatusRequest {
  /** Where the job should be. */
  readonly status: JobStatus;
  /**
   * When the work actually finished. Only read when `status` is `JobStatus.Completed`; when it
   * is omitted the server asks its own clock.
   */
  readonly completedAt: string | null;
}

/** The body of `POST /jobs`. */
export interface CreateJobRequest {
  /** Whose work it is. */
  readonly customerId: string;
  /** Which of that customer's service locations it happens at. */
  readonly locationId: string;
  /** The skill a technician must have to take it. */
  readonly requiredSkill: string;
  /** How badly it needs doing. */
  readonly priority: JobPriority;
  /** When the promised window opens. */
  readonly windowStart: string;
  /** When the promised window closes. */
  readonly windowEnd: string;
  /** How long the work should take once a technician is on site. */
  readonly estimatedDuration: string;
}

/** One job, as the clients see it: the demand, and nothing about the plan. */
export interface JobResponse {
  /** Its identity. */
  readonly id: string;
  /** Whose work it is. */
  readonly customerId: string;
  /** Which of that customer's service locations it happens at. */
  readonly locationId: string;
  /** Where it is, in decimal degrees. */
  readonly latitude: number;
  /** Where it is, in decimal degrees. */
  readonly longitude: number;
  /** The skill a technician must have to take it. */
  readonly requiredSkill: string;
  /** How badly it needs doing. */
  readonly priority: JobPriority;
  /** When the promised window opens. */
  readonly windowStart: string;
  /** When the promised window closes. */
  readonly windowEnd: string;
  /** How long the work should take once a technician is on site. */
  readonly estimatedDuration: string;
  /** How far through its life the job is. */
  readonly status: JobStatus;
  /** What a technician wrote about it, or `null` if nobody has. */
  readonly notes: string | null;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Schedule
// ---------------------------------------------------------------------------------------------

/** The body of `POST /schedule/insert`: an emergency, named by the job it is for. */
export interface InsertJobRequest {
  /** The work that has just come in. */
  readonly jobId: string;
}

/** The response from `POST /schedule/insert`: where the emergency went. */
export interface InsertJobResponse {
  /** The stop that now exists for it. */
  readonly assignmentId: string;
  /** Whose day it landed on. */
  readonly technicianId: string;
  /** When the work is planned to start. */
  readonly scheduledStart: string;
  /** Where it falls in that technician's run, counting from zero. */
  readonly sequence: number;
  /**
   * How many of that technician's other stops had to move along to make room. Zero when the job
   * went on the end of a day.
   */
  readonly displaced: number;
}

/** What a good schedule is worth, as a caller states it. Optional on `OptimizeScheduleRequest`. */
export interface ObjectiveWeightsRequest {
  /** Cost of one minute of driving. The unit the other three are quoted in. */
  readonly travel: number;
  /** Cost of one minute past a job's promised window. */
  readonly lateness: number;
  /** Cost of one minute worked past the end of a technician's shift. */
  readonly overtime: number;
  /** Cost of leaving a job undone, per unit of its priority. */
  readonly unassigned: number;
}

/** The body of `POST /schedule/optimize`. */
export interface OptimizeScheduleRequest {
  /** When the horizon to re-plan opens. */
  readonly from: string;
  /** When it closes. Never earlier than `from`. */
  readonly to: string;
  /** What a good schedule is worth, or `null` for the engine's defaults. */
  readonly weights: ObjectiveWeightsRequest | null;
}

/** The response from `POST /schedule/optimize`: what the optimiser did. */
export interface OptimizeScheduleResponse {
  /** How many stops the day now has. */
  readonly planned: number;
  /** The jobs no technician could take. Not a failure — see the remarks on `OptimizedDay`. */
  readonly unassigned: readonly string[];
  /**
   * What the plan scores under the weights it was given. Only comparable against another
   * response for the same horizon under the same weights.
   */
  readonly cost: number;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Sync
// ---------------------------------------------------------------------------------------------

/** One entity as the server currently holds it, sent to a device that is behind. */
export interface SyncChange {
  /** What kind of thing this is — the same vocabulary an operation uses. */
  readonly entity: string;
  /** Which one. */
  readonly entityId: string;
  /** Its concurrency stamp as the server holds it. */
  readonly version: number;
  /**
   * The entity as the client should now hold it, or `null` when `deleted` is set — the two
   * always agree.
   */
  readonly state: unknown;
  /**
   * The entity is gone from this device's world: cancelled, or a stop re-optimised onto
   * somebody else's day. Removal has to be sayable, because a phone that is never told simply
   * keeps the stop and drives to it.
   */
  readonly deleted: boolean;
}

/** An operation the server would not apply, and why. */
export interface SyncConflict {
  /** Which operation, by the id the device gave it. */
  readonly opId: string;
  /** What kind of refusal it was, for the client to branch on. */
  readonly reason: SyncConflictReason;
  /** Why, in terms fit to show the technician holding the phone. */
  readonly message: string;
}

/** The kinds of refusal a pushed operation can meet. */
export const SyncConflictReason = {
  /**
   * The status change is not one the job's state machine allows from where the job actually is.
   * Legality is the rule, not who wrote last — a stale phone cannot push a cancelled job back
   * into progress by being the most recent writer.
   */
  IllegalTransition: 'IllegalTransition',
  /**
   * The entity has moved on since the version the device based its operation on. What the
   * server holds stands, and the device rebases onto it.
   */
  VersionConflict: 'VersionConflict',
  /**
   * The server cannot apply this operation at all: it does not know that kind of operation, or
   * cannot read its payload, or does not have the thing it names.
   */
  Unsupported: 'Unsupported',
} as const;

export type SyncConflictReason = (typeof SyncConflictReason)[keyof typeof SyncConflictReason];

/** One line of what a job's work has taken, inside a `SyncJobPayload`. */
export interface SyncJobLinePayload {
  /** The line's identity within its job. */
  readonly id: string;
  /** Labour or a part. */
  readonly kind: LineItemKind;
  /** What it was. */
  readonly description: string;
  /** How many. */
  readonly quantity: number;
  /** What one costs, in dollars. */
  readonly unitPrice: number;
}

/** A job, inside a `SyncChange` whose `Entity` is `"job"`. */
export interface SyncJobPayload {
  /** How far through its life it is — which buttons the app offers. */
  readonly status: JobStatus;
  /** How badly it needs doing. */
  readonly priority: JobPriority;
  /** What it takes to do it. */
  readonly requiredSkill: string;
  /** When the promised window opens. */
  readonly windowStart: string;
  /** When the promised window closes. */
  readonly windowEnd: string;
  /** How long the work should take. */
  readonly estimatedDuration: string;
  /** Where it is, for navigation. */
  readonly latitude: number;
  /** Where it is, for navigation. */
  readonly longitude: number;
  /** Who it is for. */
  readonly customerName: string;
  /** Where it is, for a human. */
  readonly address: string;
  /** What has been written about it, or `null` if nothing has. */
  readonly notes: string | null;
  /**
   * When the notes were written. The device needs it to know whether its own unsent note would
   * win — the same comparison the server will make.
   */
  readonly notesRecordedAt: string | null;
  /** What the work has taken so far, in the order it was recorded. */
  readonly lines: readonly SyncJobLinePayload[];
}

/**
 * One thing a technician did in the field: started a job, added a note, added a part, finished.
 * Queued on the device and pushed in batches (Document 2 §10).
 */
export interface SyncOp {
  /**
   * The device's own identifier for this operation, and the idempotency key. A phone that
   * pushes, loses signal, and pushes again sends the same id, which is how the server knows not
   * to apply it twice.
   */
  readonly id: string;
  /** What kind of thing it happened to — `"job"`, `"line_item"`. */
  readonly entity: string;
  /** Which one. */
  readonly entityId: string;
  /** What was done — `"status_change"`, `"add_note"`. */
  readonly type: string;
  /** The operation's own data, shaped by `type`. */
  readonly payload: unknown;
  /**
   * The version of the entity the device was looking at when it acted. What a stale write is
   * judged against; the server's answer is authoritative either way.
   */
  readonly baseVersion: number;
  /**
   * When it happened on the device, which is not when it arrived. The ordering a technician
   * would recognise, and what last-write-wins on free text is decided by.
   */
  readonly clientTs: string;
}

/**
 * Everything that changed in a device's world since the cursor it asked with, and where it now
 * stands. The body of `GET /sync/pull?since={cursor}`.
 */
export interface SyncPullResponse {
  /** The entities that moved, as the server now holds them. */
  readonly changes: readonly SyncChange[];
  /**
   * The watermark to ask with next time. Opaque — the shape of the server's bookmark is not
   * part of the contract.
   */
  readonly cursor: string;
}

/**
 * A device emptying its queue: everything it did since it last got through, in the order it did
 * it. The body of `POST /sync/push`.
 */
export interface SyncPushRequest {
  /** The operations, oldest first. */
  readonly ops: readonly SyncOp[];
}

/**
 * What the server made of a pushed batch. Authoritative: the device rebases onto this rather
 * than keeping its own opinion (Document 2 §10).
 */
export interface SyncPushResponse {
  /**
   * The ops that landed, by id. An op the server had already applied is reported here too: from
   * the device's point of view a re-sent operation that is already in effect succeeded, and
   * telling it otherwise would leave it queueing the op forever.
   */
  readonly applied: readonly string[];
  /** The ops that were refused, each with a reason fit to show a technician. */
  readonly conflicts: readonly SyncConflict[];
  /**
   * Where the device now stands in the change stream. Opaque — it is the server's watermark,
   * and a client that parses it is reading something it was not promised.
   */
  readonly cursor: string;
}

/**
 * A planned stop, inside a `SyncChange` whose `Entity` is `"assignment"` — the same name a
 * removed stop uses, so a client groups both under one vocabulary rather than switching between
 * "stop" and "assignment" depending on whether the row still exists.
 */
export interface SyncStopPayload {
  /** The work it is for. The device joins this to a `SyncJobPayload`. */
  readonly jobId: string;
  /** Where it falls in the day, counting from zero. */
  readonly sequence: number;
  /** When the technician is planned to start work. */
  readonly scheduledStart: string;
  /** Minutes of driving to get here from the previous stop. */
  readonly travelMin: number;
}

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Technicians
// ---------------------------------------------------------------------------------------------

/** The body of `POST /technicians`. */
export interface CreateTechnicianRequest {
  /** Their name, as it appears on the dispatch board. */
  readonly name: string;
  /**
   * What they are qualified to work on. Empty is allowed — a trainee simply matches no skilled
   * job.
   */
  readonly skills: readonly string[];
  /** When their working hours open. */
  readonly shiftStart: string;
  /** When their working hours close. */
  readonly shiftEnd: string;
  /** Their home base, in decimal degrees between -90 and 90. */
  readonly latitude: number;
  /** Their home base, in decimal degrees between -180 and 180. */
  readonly longitude: number;
}

/** The body of `PUT /technicians/{id}/shift`. */
export interface SetShiftRequest {
  /** When their working hours open. */
  readonly start: string;
  /** When their working hours close. */
  readonly end: string;
}

/** The body of `PUT /technicians/{id}/skills`. */
export interface SetSkillsRequest {
  /** The complete list they should have afterwards. Empty makes them a trainee again. */
  readonly skills: readonly string[];
}

/** One technician, as the clients see them. */
export interface TechnicianResponse {
  /** Their identity. */
  readonly id: string;
  /** Their name, as it appears on the dispatch board. */
  readonly name: string;
  /** What they are qualified to work on, alphabetically. */
  readonly skills: readonly string[];
  /** When their working hours open. */
  readonly shiftStart: string;
  /** When their working hours close. */
  readonly shiftEnd: string;
  /** Their home base, in decimal degrees. */
  readonly latitude: number;
  /** Their home base, in decimal degrees. */
  readonly longitude: number;
}

/**
 * The body of `PUT /technicians/{id}` — everything `SetSkillsRequest` and `SetShiftRequest` do
 * not own.
 */
export interface UpdateTechnicianRequest {
  /** Their name, as it appears on the dispatch board. */
  readonly name: string;
  /** Their home base, in decimal degrees between -90 and 90. */
  readonly latitude: number;
  /** Their home base, in decimal degrees between -180 and 180. */
  readonly longitude: number;
}

// ---------------------------------------------------------------------------------------------
// Job state machine — generated from the domain transition table
// ---------------------------------------------------------------------------------------------

/**
 * What may follow what. The same table the server enforces, so a client deciding which action
 * to offer is reading the rule rather than guessing at it. A status with no successors is
 * terminal.
 */
export const jobTransitions: Readonly<Record<JobStatus, readonly JobStatus[]>> = {
  Unscheduled: ['Scheduled', 'Cancelled'],
  Scheduled: ['Unscheduled', 'Dispatched', 'Cancelled'],
  Dispatched: ['Unscheduled', 'EnRoute', 'Cancelled'],
  EnRoute: ['InProgress', 'Cancelled'],
  InProgress: ['Completed', 'Cancelled'],
  Completed: ['Invoiced'],
  Invoiced: ['Paid'],
  Paid: [],
  Cancelled: [],
};

/**
 * Whether a job may move from one status to the next. The client-side half of the state
 * machine: the server still refuses an illegal move, and a device that has been offline long
 * enough may be asking about a job that has already moved on.
 */
export function canTransition(from: JobStatus, to: JobStatus): boolean {
  return jobTransitions[from].includes(to);
}
