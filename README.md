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
  Domain.Tests  Scheduling.Tests  Application.Tests  Api.IntegrationTests
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
Contracts.CodeGen -> Contracts
Contracts       -> (nothing)
Domain          -> (nothing)
```

`Api -> Infrastructure` is deliberate and confined to DI registration at startup; controllers
depend only on Application abstractions. See [Document 2 §2](claude%20docs/OpenDispatch-02-backend-architecture.md).

## Building

```bash
dotnet build OpenDispatch.sln
dotnet test  OpenDispatch.sln
```

Docker Compose, the `Makefile` targets (`make up`, `make run`, `make test`, …), and EF
migrations come online as the build plan progresses — check the repo root for what currently
exists.
