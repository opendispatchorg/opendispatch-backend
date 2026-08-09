# OpenDispatch — backend

The .NET backend for OpenDispatch, an open-source, self-hostable field-service dispatch
platform. This repo owns all business logic, the database, the scheduling engine, real-time
push, and the offline-sync endpoints, and it defines the API contract the web and mobile
repos consume.

**Stack:** ASP.NET Core · Entity Framework Core · SignalR · MediatR · FluentValidation ·
PostgreSQL/PostGIS. One deployable process, built as a modular monolith with Clean
Architecture.

## Solution layout

```
src/
  Domain             entities, value objects, domain events, the job state machine
  Scheduling         the optimization engine as a pure library
  Application        vertical feature slices, orchestration, port interfaces
  Contracts          wire types shared verbatim with the TypeScript clients
  Infrastructure     EF Core, repositories, port adapters, SignalR, event dispatch
  Api                thin controllers, auth, tenancy, OpenAPI; the composition root
  Contracts.CodeGen  generates the shared client types from C#
tests/
  Domain.Tests  Scheduling.Tests  Contracts.Tests  Application.Tests  Api.IntegrationTests
contracts/           generated: the @opendispatch/contracts npm package (do not edit)
tools/               the node half of `make gen-contracts`
```

Directories are unprefixed; each project sets `RootNamespace` and `AssemblyName` to
`OpenDispatch.<Name>`, so namespaces and assemblies stay fully qualified
(`src/Domain` builds `OpenDispatch.Domain.dll`) and match the project names in
Document 2 §2.

## Dependency rule

Dependencies point inward and this direction is enforced by the project references. Domain
depends on nothing; nothing inner depends on EF Core, ASP.NET, or any external system.

```
Api             -> Application, Infrastructure, Domain, Contracts   (composition root)
Infrastructure  -> Application, Domain, Scheduling
Application     -> Domain, Scheduling
Scheduling      -> Domain
Contracts.CodeGen -> Contracts, Domain   (build-time tool; ships nowhere)
Contracts       -> (nothing)
Domain          -> (nothing)
```

`Contracts.CodeGen` is the only project that sees both Contracts and Domain, deliberately: the
job transition table is domain data, `Contracts` depends on nothing, and the generated package
carries the rule so the clients need no copy of it.

`Api -> Infrastructure` is deliberate and confined to DI registration at startup; controllers
depend only on Application abstractions. See [Document 2 §2](claude%20docs/OpenDispatch-02-backend-architecture.md).

## Building

```bash
make up             # start Postgres/PostGIS
make run            # serve the Api on http://localhost:5141
make test-fast      # the Unit category only - pure, no I/O
make test           # everything, including container-backed integration tests
make gen-contracts  # rebuild contracts/ - the @opendispatch/contracts package
```

Testing conventions and the shared harness are described in [TESTING.md](TESTING.md).

## The API contract

One authoritative contract holds the three repos together, so a backend change surfaces as a
client compile error rather than a runtime surprise ([Document 2 §11](claude%20docs/OpenDispatch-02-backend-architecture.md)).
It has two halves and both land in `contracts/`:

- **REST** — the Api exports an OpenAPI document at build time; `contracts/src/rest.ts` is
  generated from it.
- **Real-time and sync** — OpenAPI cannot describe a SignalR message or a sync batch, so those
  shapes are written once in `src/Contracts` and emitted to `contracts/src/index.ts`.
- **The job state machine** — the transition table is read out of the `Job` aggregate and
  emitted alongside `JobStatus` with a `canTransition(from, to)` helper, so the offline
  technician app decides which action to offer from the rule the server enforces.

Change a `Contracts` type or an endpoint → `make gen-contracts` → commit. Nothing under
`contracts/` is edited by hand; the whole directory is output.

Docker Compose, the `Makefile` targets (`make up`, `make run`, `make test`, …), and EF
migrations come online as the build plan progresses — check the repo root for what currently
exists.
