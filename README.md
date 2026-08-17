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
| **Attachments** | Photographs and signatures are the only data not in Postgres, and a deployment picks where they live: a **disk** (`Attachments__Root`, `/var/lib/opendispatch/attachments` in the image) or an **S3-compatible bucket** (`Attachments__Bucket`). Choose by what your platform's filesystem is, not by scale — see [Where the photographs live](#where-the-photographs-live) below. |
| **TLS** | Terminated by a proxy in front; the container serves plain HTTP on 8080. Set `ReverseProxy:Enabled` so the host believes the forwarded address, and only when it is unreachable except through that proxy. **`Strict-Transport-Security` is only sent once the host can see the request arrived over HTTPS**, which behind a proxy means this flag is on — otherwise a browser would be told never to use plain HTTP for this origin again, which no server-side change undoes. |
| **Connection pool** | Sized in the connection string, not in code, so it can match the database you actually have: `Maximum Pool Size` (Npgsql's default is 100) should be at or below what Postgres will grant this deployment across every instance, `Minimum Pool Size=2` keeps a connection warm, and `Timeout=15` bounds waiting for one. A single command is capped at 30 seconds regardless. |
| **More than one instance** | Set `SignalR:Redis`. SignalR keeps its groups in the memory of the process holding the connection, so **without a backplane the live board is correct only while there is exactly one API instance** — a dispatcher connected to one would never see a change made through another, silently. One instance is a supported way to run this; two without Redis is not. The host says which it is in its startup log. |

Back up the database and the attachment store together: an invoice whose photograph is missing is
half a record, and Document 1's promise is that the business owns all of it. The exact commands, and
the restore that goes with them, are in [docs/RUNBOOK.md](docs/RUNBOOK.md) — and `make restore-drill`
runs the whole cycle against scratch containers, so the procedure is one that has been executed
rather than one that has been written down.

### Where the photographs live

Attachment content is the one thing this system writes outside Postgres, so it is the one thing a
database backup does not carry and the one thing a container filesystem can quietly take with it.
There are two adapters behind a single port, and the host says which one it has in its startup log.

**A disk**, the default. `Attachments__Root` names a directory; mount a volume over it and back it up
with the database. A **named** volume is handled by the image; a **bind** mount keeps the host
directory's ownership, so `chown -R 1654:1654` it or the first upload of the day answers 500. This
is the whole answer for a shop running OpenDispatch on a machine it owns.

**A bucket**, when the filesystem is not yours to keep. Set `Attachments__Bucket` and the content
goes to any S3-compatible object store — AWS S3, Cloudflare R2, Backblaze B2, MinIO — addressed
path-style, so no per-bucket DNS is needed.

```bash
-e Attachments__Bucket=opendispatch-attachments \
-e Attachments__ServiceUrl=https://<accountid>.r2.cloudflarestorage.com \
-e Attachments__Region=auto \
-e AWS_ACCESS_KEY_ID=… -e AWS_SECRET_ACCESS_KEY=…
```

`ServiceUrl` is the store's endpoint; leave it unset for Amazon S3 itself and give
`Attachments__Region` instead. **Credentials are read from the environment only** — there is
deliberately no configuration key for an access key, so there is nowhere for one to be typed into
`appsettings.json` and committed. A host that names a bucket needs no `Attachments__Root`, and a
host that names neither refuses to start.

Two things to get right on the bucket itself, because the code cannot check them for you:

- **It must already exist.** The adapter never creates a bucket; that is an account-level act with
  its own policy and lifecycle.
- **Versioning and object-lock retention must be off.** Erasure *deletes* a photograph, and a bucket
  configured to keep previous versions answers a delete by hiding the object rather than removing
  it — which would leave a picture of somebody's home recoverable after they were told it was gone.

**Pick the bucket if your platform's container filesystem is ephemeral.** On Render, Fly, Cloud Run
and anything else that hands each release a fresh disk, the local-disk adapter does not degrade
gracefully — the next deploy destroys every photograph and signature captured since the last one,
silently, with a perfectly healthy-looking host on the far side. The same applies to running two API
instances without shared storage: one would store a photograph the other 404s. The startup log's
disk line says so, every boot.

### What the edge enforces

Four bounds, none of them configurable, all of them chosen far above every measured path — they
exist to stop a request running or growing without end, not to shape ordinary traffic.

| | |
|---|---|
| **Request timeout** | 30 seconds by default; **5 minutes** for `GET /export`, which streams a whole tenant's history and is the one request whose honest duration grows with the shop. A request that hits it gets a 503. The SignalR hub is exempt — its fallback transports hold a request open on purpose. |
| **Command timeout** | 30 seconds on any single database command, so a lock nobody meant to hold cannot pin a connection until the caller gives up. |
| **Request body** | 26 MB, refused by the server before a handler sees it. That is the 25 MB attachment cap plus a megabyte for multipart framing — the framework's own default sat *above* the cap, so oversized uploads were read in full and only then rejected. |
| **Response headers** | `nosniff`, `X-Frame-Options: DENY` with `frame-ancestors 'none'`, `Referrer-Policy: no-referrer` and a minimal `Permissions-Policy` on every response, including failures. HSTS only over HTTPS — see the TLS row above. |

`GET /health/ready` checks the database, the attachment store, and the SignalR backplane when one is
configured. An unreachable store or backplane reports **degraded** rather than unhealthy — a shop
can still dispatch and invoice without either — and readiness still answers 503, because a load
balancer is asking a yes/no question. The body names which check failed.

### Telling customers

Set `Mail:Host` and this system emails a customer at the two moments a customer actually wants to
hear from a service business:

| When | What |
|---|---|
| The technician goes **en route** | "Your technician is on the way." |
| Their invoice is **marked paid** | A receipt naming the amount. |

```bash
-e Mail__Host=smtp.resend.com -e Mail__Port=587 \
-e Mail__From=dispatch@yourshop.example -e Mail__FromName="Your Shop" \
-e Mail__Username=resend -e Mail__Password=…
```

SMTP, because every provider speaks it — Resend, SES, Postmark, or the shop's own mailbox are all a
host, a port and a login, so nothing here picks a vendor for you.

Four things this does deliberately, each of which is the sort of thing that is embarrassing when it
is wrong:

- **Leave `Mail:Host` unset and nothing is sent and nothing fails.** A shop that does not want
  automated email is not a misconfigured deployment. The host says which it is in its startup log,
  and no adapter is registered at all — there is no no-op quietly reporting success for messages
  nobody sent.
- **An erased customer is never emailed.** `POST /customers/{id}/erase` removes their contact
  details, and the notification path checks the erasure itself rather than trusting the field to be
  empty. Writing to whoever holds that address now, about work done for somebody who asked to be
  forgotten, is the failure this prevents.
- **A customer with no email address is not an error.** Plenty have a phone number and nothing else,
  and their jobs run exactly as everybody else's.
- **A mail server that is down does not fail the dispatcher's action.** The status change has already
  committed by the time anything is sent, so a send that throws is logged at error level and the
  request succeeds. **The message is not retried** — the log line is the only record. See the
  runbook's known limits.

Messages can arrive twice. Domain-event delivery is at-least-once, so a "your technician is on the
way" that the outbox re-delivers is sent again. That is tolerable for email and is exactly why email
is the first channel; it is also why nothing that costs money is triggered this way.

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

The one thing it cannot reach is a copy taken before it ran — a backup, or a previous object version
in a bucket configured to keep them. Erasure is a live-system operation; what your retention policy
does about older copies is a decision this software cannot make, which is why the bucket must not be
versioned.

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
| `RateLimit:*` | Three caps. Sign-in attempts per address per window (`20` per `300`s) — raise it for an office behind one NAT address. `PushesPerMinute` per technician (`60`) and `OptimizationsPerMinute` per organization (`10`), which are not about attackers: a phone stuck in a retry loop and a browser with a wedged refresh are ordinary accidents, and both can spend a shop's database. `Enabled` turns all three off. |
| `ReverseProxy:Enabled` | Read `X-Forwarded-For`/`-Proto`. Turn it on **only** when this host is unreachable except through the proxy, or narrow it with `KnownProxies`/`KnownNetworks`. |
| `Attachments:Bucket` | An S3-compatible bucket for photographs and signatures, instead of `Attachments:Root` on a disk. With it, `Attachments:ServiceUrl` (the store's endpoint; unset means Amazon S3) and `Attachments:Region` (required for Amazon, conventionally `auto` or `us-east-1` elsewhere). Access keys come from `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY` or a platform role and have no configuration key at all. **Required, not optional, on any platform whose container filesystem is ephemeral** — see "Where the photographs live" above. |
| `Mail:*` | An SMTP server, and customers get told two things: their technician is on the way, and their invoice has been settled. `Mail:Host` is what turns it on; with it, `Mail:From` is required, and `Mail:Port` (587), `Mail:FromName`, `Mail:Username` and `Mail:Password` fill in the rest. Supply the password through the environment (`Mail__Password`). Unset means **nothing is sent and nothing fails** — see "Telling customers" below. |
| `SignalR:Redis` | A StackExchange connection string for the dispatch board's backplane. Unset means in-process, which is correct for exactly one instance — see "More than one instance" above. |
| `Outbox:*` | How the delivery sweep behaves — `Enabled` (default true), `IntervalSeconds` (10), `GraceSeconds` (30), `BatchSize` (50). Turning it off is an incident measure while a poison message is dealt with, not a configuration: with it off, a reaction lost to a failure or a restart stays lost. |
| `Otel:Endpoint` | An OTLP collector to export metrics and traces to — `http://collector:4317`. Unset means the instruments are published in-process and sent nowhere, which is what `dotnet-counters` reads. `Otel:Protocol` picks `grpc` (default, usually port 4317) or `http/protobuf` (usually 4318); `Otel:ServiceName` names this deployment in the collector's feed, which is how staging and production are told apart when they share one. A collector that is down costs nothing — the exporter drops what it cannot deliver and never blocks a request. |
| `Logging:Json` | Write newline-delimited JSON to the console (Serilog's compact format) instead of the human-readable template. Set it wherever the logs are shipped to something that parses them: the correlation id, the request path and every structured property survive as fields rather than being flattened into a sentence. |
| `Sync:PullPageTransactions` | How much of a technician's change stream one `GET /sync/pull` may carry, counted in transactions (default `200`). Lower it for a fleet on poor connections; a device simply pulls more often, because a capped page says `hasMore`. |
| `Sync:PullPageRows` | The second bound on the same page, roughly in rows (default `2000`). Transactions alone bound the wrong thing — one transaction can write a whole day's plan, or a whole import — and the load pass found a first sync answering with 12,884 changes in one 2.3 MB response while obeying the transaction cap perfectly. A transaction is still never split, so a single enormous one is sent whole. |

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
field operation was refused, which is the one failure this API answers with a 200).

Set `Otel:Endpoint` and those instruments go to a collector over OTLP, together with ASP.NET Core's
HTTP metrics, .NET runtime metrics, and traces — including Npgsql's spans, so a slow request shows
*which query* it waited on rather than only that it was slow. Leave it unset and nothing is
exported; `dotnet-counters` against the meter name still works. The alerts worth having, with
thresholds and what to do when each fires, are in [docs/RUNBOOK.md](docs/RUNBOOK.md#alerts).

The schema lives in `src/Infrastructure/Persistence/Migrations` and is generated with
`make migration NAME=AddSomething`; it is *applied* by `make migrate` on a developer machine and by
the `migrate` verb in a deployment. Integration tests apply the same migrations to a throwaway
container, so a migration that will not apply from scratch fails the suite rather than a
deployment.

What to do when something is wrong — deploy, roll back, restore, rotate the signing key, and what
each alert means — is in [docs/RUNBOOK.md](docs/RUNBOOK.md).

Testing conventions and the shared harness are described in [TESTING.md](TESTING.md).

## What it costs

Measured, not estimated, against a shop with a year behind it: `seed --scale big` writes 12,000 jobs
across 300 trading days — 424 customers, 11,407 stops, 10,251 invoices, a 30-day sync log, 84 MB of
database — and `scripts/measure.sh` then times the four paths somebody waits on. Both are repeatable
commands; the numbers below are from a 2026-era laptop running the API, Postgres **and** the load
generator together, which is a harsher arrangement than any deployment.

| Path | p95, idle | p95, 10 phones polling | Budget |
|---|---|---|---|
| `GET /sync/pull` (routine) | 20–29 ms | 71–305 ms | **< 300 ms** |
| `GET /sync/pull` (first sync, `since=0`) | 23–40 ms | 78–133 ms | **< 300 ms** |
| `GET /dispatch/board` (a day) | 6–12 ms | 37–52 ms | **< 150 ms** |
| `POST /schedule/optimize` (40 jobs, 8 technicians) | 88–196 ms | 1.2–1.4 s | **< 2 s** |
| `GET /export` (the whole business) | 11 MB in 0.2–0.7 s | — | **< 5 s** |

The load column is ten clients pulling **continuously with no pause** — a fleet of phones polling
every thirty seconds is nowhere near it. Read it as a floor under a bad moment rather than as normal
operation.

**A first sync arrives in pages.** Against that year of history a wiped phone catches up in 8 pulls
of roughly 300 KB, not one response of 2.3 MB; see `Sync:PullPageRows` above for why that bound
exists and what it cost to find.

**Memory.** The API settles around 200 MB and grows to roughly 500 MB after repeated exports, where
it levels off — .NET's server garbage collector keeping the heap it has grown rather than a leak
(with `DOTNET_gcServer=0` the same run stays near 300 MB, at 20–40% more latency under load). A shop
runs comfortably on 2 vCPU and 2 GB for the API with another 2 GB for Postgres.

## Reading lists

`GET /customers` and `GET /jobs` are paged: `?page=1&pageSize=50` by default, `pageSize` capped at
200, and the body carries `items`, `total`, `page` and `pageSize` so a caller can tell whether there
is more. They used to answer with the whole table, which is fine for a demo and is a page nobody can
draw for a shop with ten years of history.

`GET /technicians` is deliberately **not** paged: the scheduler must consider every technician to
place a job, so the port behind it is unpaged by design, and a crew is a crew rather than a
database.

`POST /schedule/normalize` is the way back from a day a dispatcher has dragged into overlapping
itself. A manual assignment is an instruction and is carried out literally — nothing re-times the
run around it — so two stops can end up on the same hour, and from then on the emergency-insert path
refuses that technician's work because there is no run to insert into. Normalising re-times **one**
technician's day: same work, same order, stops pushed later where they must be and never pulled
earlier than the customer was told. A re-plan would fix it too, by rewriting everybody's day to
repair one.

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
  shapes are written once in `src/Contracts` and emitted to `contracts/src/index.ts`. The board
  sends two events, `job.updated` and `assignment.updated`; the latter covers a stop newly planned
  as well as one that moved, because a board draws where a stop is and has no separate rendering for
  an arrival. **`technician.moved` was removed**: it was published as a name and a payload shape for
  a message nothing has ever sent — nothing models a technician's live position — and a contract
  that promises a message the server cannot send is one a client writes a handler against and waits
  forever on. It comes back the day a real location source does.
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
