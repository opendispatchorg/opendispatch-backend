/**
 * A sample client (Document 3, step 52's "a sample client import compiles against the
 * package"). Both imports below resolve through @opendispatch/contracts' own package.json —
 * its `exports` map and `types` field — rather than a relative path into contracts/src, the way
 * `make gen-contracts`' own `tsc contracts/src/index.ts contracts/src/rest.ts` invocation
 * compiles them. That direct-file check cannot catch a broken `exports` entry or a missing
 * `types` field; a real importer, resolved the way a client repository actually resolves it,
 * can.
 *
 * Not a test runner: nothing here asserts anything at runtime. Compiling *is* the assertion —
 * see the Makefile's `check-contracts-sample` target and tools/README.md.
 */

import { BoardEvents, canTransition, type AssignmentUpdated, type JobUpdated } from '@opendispatch/contracts';
import type { components, paths } from '@opendispatch/contracts/rest';

// The SignalR half: a board client subscribing to a job update exactly as DispatchHubFlowTests
// (step 51) drives one over a real connection.
declare function onBoardEvent(name: string, handler: (payload: JobUpdated) => void): void;
onBoardEvent(BoardEvents.JobUpdated, (event) => {
  console.log(`job ${event.jobId} is now ${event.status}, offering: ${canTransition(event.status, 'Cancelled')}`);
});

const sampleAssignment: AssignmentUpdated = {
  assignmentId: 'a1',
  jobId: 'j1',
  technicianId: 't1',
  sequence: 0,
  scheduledStart: new Date().toISOString(),
  travelMin: 12,
  version: 1,
};
void sampleAssignment;

// The REST half: the request body POST /customers actually binds, and the response
// GET /customers actually returns — both read through `paths`, not restated by hand.
type CreateCustomerBody = paths['/customers']['post']['requestBody']['content']['application/json'];
type CustomerSummary = components['schemas']['CustomerSummaryResponse'];

async function createCustomer(body: CreateCustomerBody): Promise<CustomerSummary> {
  const response = await fetch('/customers', { method: 'POST', body: JSON.stringify(body) });

  return (await response.json()) as CustomerSummary;
}

void createCustomer;
