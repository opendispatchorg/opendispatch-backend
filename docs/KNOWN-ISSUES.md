# Known issues and accepted limitations

What is wrong with this build, and what is deliberately not in it. Written for somebody deciding
whether to run OpenDispatch for a real business, and what they are taking on by doing so.

Two categories, kept apart on purpose. **Defects** are things that should be fixed and are not
finished. **Accepted limitations** are decisions — scope this version does not cover, each with the
reasoning that put it out of scope. A limitation you know about is a constraint; one you find out
about in month two is a betrayal, so both lists are here rather than in a commit message.

Every claim below names the file it lives in. If you are evaluating this, read the code rather than
taking this document's word for it.

*Current as of the `last-mile` branch. The [README](../README.md) says what the system is; the
[runbook](RUNBOOK.md) says what to do to it when something is wrong.*

---

## The honest frame: none of this has run outside a laptop

Before any specific defect, the thing that matters most:

- **Continuous integration has never executed a single run.** The workflow file was not valid YAML
  from the day it was written — one `run:` line contained a colon followed by a space, which
  terminates a plain YAML scalar. An unparseable workflow does not fail loudly; it does not run at
  all. It is fixed, and `make check-ci` now guards it, but the fix itself has not been exercised:
  the triggers are `main`, tags, and pull requests, so the first real run happens when a PR is
  opened.
- **Nothing has been deployed.** The Render blueprint (`render.yaml`) is written from the platform's
  documentation, not from a deployment. The `preDeployCommand`, the managed-Postgres wiring and the
  cron job are untested against the real platform.
- **The restore drill has never run against a deployed system.** `make restore-drill` passes locally
  and found two defects in the shipped image the first time it ran. It has not been pointed at a
  real deployment, so the disaster-recovery procedure is proven on containers and not in place.

Four properties are structurally untestable in-process and stay unverified until a deployment
exists: HSTS over real TLS, the 26 MB request-body limit (`WebApplicationFactory` runs `TestServer`,
which is not Kestrel, so `KestrelServerOptions.Limits` is inert), a notification actually arriving,
and two browsers repainting on one optimise.

The test suite is dense — 950-odd tests, exhaustive where behaviour is hard — but it has only ever
run on one machine.

---

## Open defects

Nothing here blocks a first deployment. All three are small, and all three are known rather than
suspected.

| | Severity | Blocks a deploy? |
|---|---|---|
| No `includeRetired` filter on the list endpoints | Low | No |
| A single enormous sync transaction is still sent whole | Low | No |
| Orphaned attachment blobs are never swept | Low | No |

### Retired records cannot be listed, only fetched

`GET /customers` and `GET /technicians` exclude retired records, and there is no flag to include
them. `retiredAt` is on both response shapes, so a client holding an id can see the state and
reinstate — and `GET /export` carries everyone — but there is no way to browse "who did we retire".

A shop that retires somebody and later wants them back needs the id from the export or from a job
that predates the retirement. Fix is an `includeRetired` query parameter threaded through the list
queries and their repositories.

### A single enormous sync transaction is sent whole

`GET /sync/pull` bounds a page by transactions *and* by rows (`Sync:PullPageTransactions`,
`Sync:PullPageRows`), but a transaction is never split — the first one is always taken whole. One
re-optimise of a very large day therefore overruns the page budget in a single response.

The invariant is the right one (a stamp bigger than the budget must not become a wall a device can
never get past), and the load pass measured a worst case of 2.3 MB. It becomes a problem only for a
shop writing transactions larger than that.

### Orphaned attachment blobs are never swept

`UploadAttachmentHandler` writes the bytes before committing the metadata row, deliberately: a
request that dies in between leaves a file nothing points at, which is invisible and harmless — the
other order leaves a row pointing at content that was never written, which a device can reach and
cannot understand.

On a disk this is invisible. In a bucket it is a line on a bill. Nothing sweeps them and nothing
alerts on them; a lifecycle rule on the bucket is the practical answer.

---

## Accepted limitations

These are scope decisions, not defects. Each is out of this version deliberately.

### Travel times are straight-line, not road-network

**This is the largest gap between what the product promises and what it does.**
`HaversineTravelTimeProvider` estimates driving time as great-circle distance at an assumed 40 km/h.
Document 2 §6 describes an OSRM adapter behind `ITravelTimeProvider`; it does not exist.

The estimate is honestly wrong in a known direction — roads are longer than straight lines — so it
under-estimates every drive by roughly the same factor, which leaves the *relative* ordering of
candidate routes largely intact. That is what the search consumes, which is why it is a defensible
default rather than a placeholder. But **every arrival time promised to a customer derives from
it**, and a shop will notice as technicians running late in a pattern rather than at random.

The correction available today is configuration, not code: a lower assumed speed already carries the
detour, the traffic and the parking. The real fix is an `OsrmTravelTimeProvider` in Infrastructure —
one class behind the existing port, and its own piece of work, because it needs a routing service,
a matrix API, caching and a fallback path.

### Nothing charges a card

`POST /invoices/{id}/pay` is bookkeeping. `FakePaymentGateway` always succeeds and is registered in
every environment; the "payment refused" path has never run, and the gateway's transaction reference
is discarded, so there is no column to reconcile against a bank statement.

Document 1 puts real payment processing out of scope. See the README's *Getting paid* section for
what to do about it — the column before the adapter.

### Signing somebody out is not immediate

`disable-user` stops the next sign-in. The token they already hold keeps working until it expires,
because nothing reads the user store per request. The window is `Jwt:ExpiryMinutes`, **720 minutes
by default** — a shift plus room either side, chosen because a technician in a basement with no
signal cannot log in again mid-job.

The only *immediate* lever is rotating `Jwt:SigningKey`, which signs out everybody at once with no
overlap window. Closing the gap properly needs refresh tokens, which this does not have. There is no
password self-service, no complexity policy, no lockout, and no MFA.

**One measurable side-channel:** an unknown username returns without hashing, while a wrong password
hashes at 100,000 iterations, so the two are distinguishable by timing despite returning an
identical error. Closing it needs a decoy hash.

### Within one organization, everybody can see everything

A technician can act on any job in their organization and read any attachment in it. The tenant
boundary is the one this system enforces; inside it, field workers cover for each other, and a
technician taking over a job needs the photographs the last one took. Every action is recorded
against whoever did it.

**If you run subcontractors on one tenant, this is not the isolation you want.** That needs a policy
on the push path and on attachment reads, and it is not here.

### Rate limits are per process

All three caps — sign-in attempts per address, pushes per technician, optimisations per organization
— live in each instance's memory. Two instances mean twice every limit. There is no distributed
limiter. `render.yaml` pins `numInstances: 1` partly for this reason.

### The live board needs Redis to be scaled

SignalR keeps its groups in the memory of the process holding the connection, so without
`SignalR:Redis` a dispatcher connected to one instance never sees a change made through another —
no error, nothing logged. One instance is a supported way to run this; two without Redis is not.
The host says which it has in its startup log.

### A versioned bucket makes erasure a lie

If `Attachments:Bucket` points at a store with versioning or object-lock retention enabled, a delete
hides the object rather than removing it — so a photograph somebody asked you to erase stays
recoverable. Nothing in this process can check that without bucket-administration permissions it
should not hold, so the host warns about it on every boot instead.

### Erasure cannot reach a backup

A customer erased today is still in yesterday's backup. That is a retention-policy decision, not a
software one: decide how long backups live, write it down, and know that restoring an old one
re-creates data somebody asked you to forget.

### No live technician position

`technician.moved` was removed from the published contract. Nothing models a technician's location
and nothing on the roadmap adds one; a contract promising a message the server cannot send is one a
client writes a handler against and waits forever on. It returns when a real location source does.

### The contracts package cannot be published

`publish-contracts` is configured and gated on a `contracts-v*` tag, but it needs an npm token that
does not exist and a decision between the `@opendispatch` and `@opendispatchorg` scopes. Clients
consume `contracts/` by path until then. The version is hand-bumped in `PackageManifest.cs`, so
nothing derives semver from source — a breaking wire change can ship under a patch bump.

### Nothing is multi-region and nothing fails over

One database, one API deployment. Recovery from losing the database is the restore procedure in the
runbook, and its speed is whatever your backup schedule says it is.

### The audit trail records acts, never values

"Who cancelled job X at 14:02" is answerable; "what was the phone number before it changed" is not.
A trail holding personal data would be a second, append-only copy of exactly what an erasure has to
remove. Some regulated contexts require before-and-after values; this cannot give them.

---

## Recently fixed, for context

These were real and are closed, each with a test that fails without its fix. Listed because they
show the shape of what this codebase gets wrong, which is useful if you are auditing it: quiet
failures in paths nothing exercised.

- **The transactional outbox could not deliver anything in a deployed host.** The background sweep
  ran with no tenant resolved, so every subscriber threw and every message became a permanent poison
  row. Invisible in normal operation, because the in-request path works — it broke precisely in the
  situation the outbox exists for. `outbox_messages` now carries an `org_id` and the sweep resolves
  a tenant per organization.
- **One organization's commit deleted another's undelivered outbox rows.** The delete matched on
  event type and `OccurredAt >= oldest` against the one table with no tenant filter.
- **`seed --scale big` threw on the first stop it wrote**, once assignments started announcing
  their own creation — the same class of bug as the outbox one, a save with no tenant reaching a
  subscriber that needs one. Nothing covered the big-seed path; something does now.
- **A retired technician could still be handed work by hand**, and a retired customer could still be
  booked. Retirement removed them from the lists and the optimiser but not from the two write paths.
- **`create-user` could move a person between tenants** on a mistyped `--org`, reporting "replaced".
- **Render's health check pointed at `/health/ready`**, which answers 503 for *degraded* — so a
  transient object-store blip would have pulled the whole instance and could have failed a deploy.
