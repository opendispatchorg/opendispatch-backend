# @opendispatch/contracts

The API contract the OpenDispatch backend owns and the web and mobile clients consume.

**Generated. Do not edit.** Everything in this directory is produced by
`make gen-contracts` in the backend repository. Change the C# or an endpoint, regenerate,
and commit — that is what turns a backend change into a client compile error rather than a
runtime surprise. The backend's CI regenerates and diffs on every push, so a stale copy
fails a build there before it reaches anyone here.

| Import | Generated from | What it holds |
| --- | --- | --- |
| `@opendispatch/contracts` | `src/Contracts` (C#) | SignalR board events, offline-sync payloads, shared enums |
| `@opendispatch/contracts/rest` | `openapi.json` | Request and response types for the REST endpoints |

The split is not cosmetic. OpenAPI cannot describe a SignalR message or a sync batch, so
those shapes are written once in C# and emitted here; the REST half is generated from the
OpenAPI document the API exports. `openapi.json` ships alongside for anything that would
rather generate its own client.

```ts
import { BoardEvents, canTransition, type JobUpdated } from '@opendispatch/contracts';

connection.on(BoardEvents.JobUpdated, (event: JobUpdated) => board.apply(event));

// The job state machine, exported from the same table the server enforces — so an
// offline device offers the actions the server will actually accept.
canTransition(job.status, 'InProgress');
```

## Depending on it

Published under this name — no path or git reference needed:

```json
{
  "dependencies": {
    "@opendispatch/contracts": "^0.1.0"
  }
}
```

The package ships TypeScript source rather than compiled output, which most of it being
types makes reasonable — but `BoardEvents`, `jobTransitions` and `canTransition` are real
values, so a bundler that does not transform linked dependencies needs telling
(`transpilePackages` in Next, `optimizeDeps` in Vite).
