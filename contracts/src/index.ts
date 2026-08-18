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
  /** Carries an `AssignmentUpdated` payload — a stop newly planned, or one that moved. */
  AssignmentUpdated: 'assignment.updated',
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

// ---------------------------------------------------------------------------------------------
// OpenDispatch.Contracts.Sync
// ---------------------------------------------------------------------------------------------

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
