# @opendispatch/contracts

The API contract the OpenDispatch backend owns and the web and mobile clients consume.

**Generated. Do not edit.** Everything in this directory is produced by
`make gen-contracts` in the backend repository. Change the C# or an endpoint, regenerate,
and commit — that is what turns a backend change into a client compile error rather than a
runtime surprise.

| Import | Generated from | What it holds |
| --- | --- | --- |
| `@opendispatch/contracts` | `src/Contracts` (C#) | SignalR board events, offline-sync payloads, shared enums |
| `@opendispatch/contracts/rest` | `openapi.json` | Request and response types for the REST endpoints |

The split is not cosmetic. OpenAPI cannot describe a SignalR message or a sync batch, so
those shapes are written once in C# and emitted here; the REST half is generated from the
OpenAPI document the API exports. `openapi.json` ships alongside for anything that would
rather generate its own client.

```ts
import { BoardEvents, type JobUpdated } from '@opendispatch/contracts';

connection.on(BoardEvents.JobUpdated, (event: JobUpdated) => board.apply(event));
```
