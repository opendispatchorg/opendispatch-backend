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
make migrate        # apply EF migrations to it
make seed           # load the demo dataset - Development hosts only
make run            # serve the Api on http://localhost:5141
make test-fast      # the Unit category only - pure, no I/O
make test           # everything, including container-backed integration tests
make gen-contracts  # rebuild contracts/ - the @opendispatch/contracts package
```

## Deploying it

A deployment is the same application with different arguments, in a container. It has no SDK, no
tool manifest and no working tree, which is why the schema and the first login are verbs on the
host rather than `make` targets:

```bash
make image                                   # build opendispatch-api:local (what CI builds too)

docker run --rm \
  -e Database__ConnectionString="Host=…;Database=opendispatch;Username=…;Password=…" \
  -e Jwt__SigningKey="a real key, at least 32 bytes of it" \
  opendispatch-api:local migrate             # apply the schema; exits non-zero if it cannot

docker run --rm -it \
  -e Database__ConnectionString="…" -e Jwt__SigningKey="…" \
  opendispatch-api:local create-user \
    --username ada@yourshop.example --org "Your Shop" --role Admin
                                             # asks for a password on stdin; registers the
                                             # organization if it does not exist yet

docker run -d -p 8080:8080 \
  -e Database__ConnectionString="…" -e Jwt__SigningKey="…" \
  -e Cors__Origins__0="https://board.yourshop.example" \
  -v opendispatch-attachments:/var/lib/opendispatch/attachments \
  opendispatch-api:local                     # serve
```

To see the whole shape locally instead, `make up-app` runs the database, the migration and the API
in containers on `http://localhost:8080` (as `Development`, using the credentials this repository
commits), and `make down-app` stops it.

Four things a real deployment owns:

| | |
|---|---|
| **Migrations** | Run `migrate` as a one-shot **before** the new version serves, and only once — the host deliberately does not migrate itself on startup, because two replicas rolling out together would race. It is idempotent and says whether it applied anything. |
| **The first login** | Nothing creates users over HTTP, by design. `create-user` is the only way in, and running it again for an existing username **replaces** that login — which is also the only password reset this system has. |
| **Somebody leaving** | `disable-user --username <name>` switches a login off; the user row stays, so the audit trail can still say what they did, and `create-user` for the same name switches it back on with a new password. **It does not revoke the token they already hold:** authorization is a signed JWT and nothing reads the user store per request, so a disabled person can keep calling until that token expires — bounded by `Jwt:ExpiryMinutes` (`720` — twelve hours — by default) and no longer. Set that to the longest window you are willing to have; closing the gap entirely needs short tokens plus refresh, which this does not have. |
| **Attachments** | Photographs and signatures are files, under `Attachments__Root` (`/var/lib/opendispatch/attachments` in the image). Mount a volume, back it up with the database, and note that two API instances need *shared* storage — the local-disk adapter is one machine's disk until an object-store adapter replaces it. |
| **TLS** | Terminated by a proxy in front; the container serves plain HTTP on 8080. Set `ReverseProxy:Enabled` so the host believes the forwarded address, and only when it is unreachable except through that proxy. |
| **More than one instance** | Set `SignalR:Redis`. SignalR keeps its groups in the memory of the process holding the connection, so **without a backplane the live board is correct only while there is exactly one API instance** — a dispatcher connected to one would never see a change made through another, silently. One instance is a supported way to run this; two without Redis is not. The host says which it is in its startup log. |

Back up the database and the attachment volume together: an invoice whose photograph is missing is
half a record, and Document 1's promise is that the business owns all of it.

### Erasing a customer

`POST /customers/{id}/erase`, admin only, is how a shop answers a request to be forgotten. It is
**not** a delete: the customer's row, their jobs, their stops and their invoices stay, dated and
still totalling what they totalled, because a shop is required to keep its financial records. What
goes is everything that says who they were — their name, their contact details, the label, address
and coordinates of every site, the coordinates each job carries its own copy of, whatever the
technician wrote about the visit, and the photographs, whose **bytes are deleted from the attachment
store** and not merely unlinked.

After it, nothing can write them back: the office cannot rename them or add a site, no new job can be
booked for them, and a phone that was out of signal when the erasure ran has its late note or
photograph refused rather than applied. The tombstoned record reads `[erased]`, and `erasedAt` on
the customer and on each job — in the API and in `GET /export` — says it was an answered request
rather than a row nobody filled in.

The one thing it cannot reach is a backup taken before it ran. Erasure is a live-system operation;
what your retention policy does about older backups is a decision this software cannot make.

One thing to schedule, from cron or its equivalent:

```bash
docker run --rm -e Database__ConnectionString="…" -e Jwt__SigningKey="…" \
  opendispatch-api:local prune --days 30
```

`prune` deletes the two tables nothing else ever deletes from — the sync op log, which recognises a
re-sent operation, and the removal notes, which tell a device a stop is gone. Both are protocol
bookkeeping rather than business records, and both grow forever without this. The window is how far
behind a device may be and still be told about a deletion individually; one further behind gets the
whole truth on a full resync instead. Nothing else in this system is ever pruned: the jobs, the
invoices and the photographs are the shop's.

## Operating it

Three things a deployment needs to watch this service, none of which assume a particular
monitoring stack:

| Surface | Where | What it says |
|---|---|---|
| Liveness | `GET /health`, `GET /health/live` | The process is up and serving. Checks nothing else, so a failure here means restart. |
| Readiness | `GET /health/ready` | Every registered health check, database connectivity among them. 503 means take this host out of rotation and leave it alone — it recovers on its own. |
| Correlation id | `X-Correlation-ID` request/response header | Sent by the caller or minted by the server; the same value lands in `traceId` on every error body and in every log line the request produces. |

### Configuration a deployment must set

`appsettings.json` ships development values so a clone runs with no setup. Outside `Development`
the host **refuses to start** while it still carries the two that are credentials:

| Key | Why |
|---|---|
| `Jwt:SigningKey` | The committed key is public; every token signed with it is forgeable. |
| `Database:ConnectionString` | The committed password is the compose one, published here and in `docker-compose.yml`. |

Optional, and doing nothing until set:

| Key | Effect |
|---|---|
| `Cors:Origins` | Browser origins allowed to call the API and the hub. Empty means no browser client can call it — set it to your dispatch board and technician app origins. |
| `RateLimit:*` | Sign-in attempts per address per window (`20` per `300`s). Raise it for an office behind one NAT address. |
| `ReverseProxy:Enabled` | Read `X-Forwarded-For`/`-Proto`. Turn it on **only** when this host is unreachable except through the proxy, or narrow it with `KnownProxies`/`KnownNetworks`. |
| `SignalR:Redis` | A StackExchange connection string for the dispatch board's backplane. Unset means in-process, which is correct for exactly one instance — see "More than one instance" above. |
| `Outbox:*` | How the delivery sweep behaves — `Enabled` (default true), `IntervalSeconds` (10), `GraceSeconds` (30), `BatchSize` (50). Turning it off is an incident measure while a poison message is dealt with, not a configuration: with it off, a reaction lost to a failure or a restart stays lost. |
| `Sync:PullPageTransactions` | How much of a technician's change stream one `GET /sync/pull` may carry, counted in transactions (default `200`). Lower it for a fleet on poor connections; a device simply pulls more often, because a capped page says `hasMore`. |

Every command that succeeds writes one row to `audit_entries` — who did it, in which organization,
what the act was, the ids it named, and when — inside the same transaction as the work, so an audit
entry cannot survive a command that rolled back. **It records the act, never the values:** no names,
no addresses, no note text. That is what keeps the trail and erasure compatible — a trail holding
personal data would be a second, append-only copy of exactly what an erasure has to remove. Nothing
prunes this table; it is a business record, not bookkeeping.

Domain-event reactions are delivered through an outbox: the event is written in the same
transaction as the work that raised it, published in-process the moment that commits, and swept up
afterwards if that publish never happened. **A row that stays in `outbox_messages` is the system
telling you something could not be delivered** — it carries the error and the attempt count, and the
size of that table is the alert worth having.

Metrics are published on the `OpenDispatch` meter (`System.Diagnostics.Metrics`) — optimize
latency as `opendispatch.scheduling.optimize.duration`, and sync as
`opendispatch.sync.ops.applied` / `opendispatch.sync.ops.conflicted` (tagged with the reason a
field operation was refused, which is the one failure this API answers with a 200). Point an
OpenTelemetry exporter or `dotnet-counters` at that meter name; no exporter is registered here,
because which one to use is the deployment's decision.

The schema lives in `src/Infrastructure/Persistence/Migrations` and is generated with
`make migration NAME=AddSomething`; it is *applied* by `make migrate` on a developer machine and by
the `migrate` verb in a deployment. Integration tests apply the same migrations to a throwaway
container, so a migration that will not apply from scratch fails the suite rather than a
deployment.

Testing conventions and the shared harness are described in [TESTING.md](TESTING.md).

## Reading lists

`GET /customers` and `GET /jobs` are paged: `?page=1&pageSize=50` by default, `pageSize` capped at
200, and the body carries `items`, `total`, `page` and `pageSize` so a caller can tell whether there
is more. They used to answer with the whole table, which is fine for a demo and is a page nobody can
draw for a shop with ten years of history.

`GET /technicians` is deliberately **not** paged: the scheduler must consider every technician to
place a job, so the port behind it is unpaged by design, and a crew is a crew rather than a
database.

`GET /export` is unpaged and always will be — it is the whole-business dump Document 1 promises —
but it is streamed rather than assembled, so its cost to this host does not grow with the shop.

Field captures are read back through two routes, open to all three roles: `GET
/jobs/{id}/attachments` lists what was captured against a job (id, kind, content type, size, and the
URL for the bytes), and `GET /attachments/{id}/content` streams one of them. Uploads are restricted
to the types the field app captures — JPEG, PNG, WebP, HEIC — and content is served as a download
with `X-Content-Type-Options: nosniff`, never as a page in this API's origin.

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

CI enforces that loop: a `Contract drift` job regenerates the package and fails if the result
differs from what is committed. Reproduce it in one command:

```bash
make check-contracts   # regenerate, type-check, and diff against the checkout
```

Clients depend on `contracts/` by path until publishing is configured (step 52); the generated
[contracts/README.md](contracts/README.md) says how.

Docker Compose, the `Makefile` targets (`make up`, `make run`, `make test`, …), and EF
migrations come online as the build plan progresses — check the repo root for what currently
exists.
