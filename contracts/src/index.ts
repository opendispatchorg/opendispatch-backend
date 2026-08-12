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
