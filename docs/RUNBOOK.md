# OpenDispatch runbook

For whoever is on the end of the phone when a shop cannot dispatch. It assumes you can reach the
host and the database and nothing else; every command here has been run.

The [README](../README.md) says what the system *is* and how to configure it. This says what to do
to it.

---

## The shape of a deployment

| Piece | What it is | What happens without it |
|---|---|---|
| **The API** | One stateless process. `dotnet OpenDispatch.Api.dll`, or the image with no arguments. | Nothing serves. Nothing is lost. |
| **Postgres** (with PostGIS) | Every business record: customers, jobs, the plan, invoices, the audit trail, the sync log. | The API answers 503 on `/health/ready` and 500 on everything else. Nothing is lost. |
| **The attachment store** | Photographs and signatures — the **only** data not in Postgres. A volume (`Attachments__Root`) or an S3-compatible bucket (`Attachments__Bucket`). | Uploads fail; existing photographs 404. `pg_dump` will not bring them back. On an ephemeral container filesystem, a volume is not a store at all — the next deploy takes it. |
| **Redis** (optional) | The SignalR backplane, for more than one API instance. | With one instance: nothing. With two and no Redis: the live board silently shows one instance's changes only. |
| **An SMTP server** (optional) | Customer notifications: technician on the way, invoice settled. | Nothing is sent and nothing fails. With one configured but unreachable: an error log line per message, and the message is gone. |

Everything else — the outbox sweep, the demo seeder, the CLI verbs — runs inside the API process or
as the same image with a different argument.

---

## Deploying a new version

Order matters, and it is the one thing here that cannot be improvised.

```bash
# 1. Apply the schema. One-shot, before the new version serves, and only once —
#    two replicas rolling out together must not both run this.
docker run --rm \
  -e Database__ConnectionString="$CONNECTION" -e Jwt__SigningKey="$KEY" \
  opendispatch-api:<new> migrate

# 2. Roll the API.
docker compose up -d api        # or your orchestrator's equivalent
```

`migrate` is idempotent, says whether it applied anything, and exits non-zero if it could not — so a
deployment pipeline can gate on it. The host deliberately **does not** migrate itself at startup:
two replicas starting together would race.

**In-flight requests drain.** A `SIGTERM` (`docker stop`, a rolling update) lets requests already
being handled finish before the process exits — verified: a `/schedule/optimize` call issued 0.2s
before the stop returned 200 with a complete body, and the container took 0.58s to exit rather than
dying at once.

**Board clients reconnect by themselves.** A restart invalidates the SignalR connection — the old
connection id answers 404 afterwards — and the client's automatic reconnect negotiates a new one.
Nothing has to re-subscribe by hand: `DispatchHub.OnConnectedAsync` puts the connection back into
its tenant's group, so a reconnected board resumes receiving without the client doing anything.
Updates made *during* the gap are missed; the board's next full read is what fills them in.

### Rolling back

Roll the **image** back and leave the schema alone. Migrations here are forward-only in practice:
every one so far is additive (new tables, new nullable columns), so an older image runs against a
newer schema — it simply ignores what it does not know about. That is the property to preserve when
writing a migration, and the reason a release that must be reversible should not drop or rename in
the same deploy as the code that stops using the column.

If a migration must be undone, it is `dotnet ef migrations script <to> <from>` from a working tree
with the SDK, reviewed by a person, and applied by hand. There is no automatic down-migration path
in the image, deliberately: an automated rollback of a schema is how data goes missing quietly.

---

## Backup

Two commands, because a shop's data is in two places. Both, together, every time — an invoice whose
photograph is gone is half a record.

```bash
# The database. -Fc is the custom format: compressed, and restorable selectively.
docker exec <postgres> pg_dump -U opendispatch -d opendispatch -Fc > opendispatch-$(date +%F).dump

# The attachments, on a volume. Whatever holds it — this is the Docker case.
docker run --rm -v opendispatch-attachments:/data:ro -v "$PWD:/backup" busybox \
  tar czf /backup/attachments-$(date +%F).tar.gz -C /data .

# The attachments, in a bucket. Same artifact, different shelf.
aws s3 sync s3://opendispatch-attachments ./attachments-$(date +%F)/ --endpoint-url "$STORE"
```

Do **not** narrow the dump with `-n public`, however tidy it looks: schema filtering drops the
`CREATE EXTENSION postgis` along with everything else, and the restore then fails on the first
`geography` column. A backup that appears to work and cannot be restored is worse than none.

Keep them together and treat them as one artifact. Test them with the drill below.

**Backups and erasure.** A customer erased today is still in yesterday's backup, and nothing here can
reach into it. That is a retention-policy decision, not a software one: decide how long backups live,
write it down, and know that restoring an old one re-creates data somebody asked you to forget.

---

## Restore

Into an **empty** database, never over a live one.

```bash
# 1. An empty database, from template0 rather than the image's initialised one.
docker exec <postgres> psql -U opendispatch -d postgres -c 'DROP DATABASE opendispatch'
docker exec <postgres> createdb -U opendispatch -T template0 opendispatch

# 2. The dump. --clean --if-exists so the restore owns every object it creates;
#    --exit-on-error so a partial restore is a failure rather than a warning.
docker cp opendispatch-2026-08-16.dump <postgres>:/tmp/restore.dump
docker exec <postgres> pg_restore -U opendispatch -d opendispatch \
  --no-owner --clean --if-exists --exit-on-error /tmp/restore.dump

# 3. The attachments.
docker run --rm -v opendispatch-attachments:/data -v "$PWD:/backup:ro" busybox \
  tar xzf /backup/attachments-2026-08-16.tar.gz -C /data

# 4. Start the API. No `migrate` — the dump carries the schema and the migration history,
#    and the API refuses to serve against a schema it does not recognise, which is the check.
docker compose up -d api
```

Then confirm it by reading business back: sign in, open a job you know, and **fetch one photograph**
— `GET /attachments/{id}/content`. The photograph is the half a database-only backup silently loses,
so it is the half worth checking by hand.

### The drill

```bash
make restore-drill
```

Stands up a deployment, does a shop's work through the API (customer, job, visit, photograph,
invoice), backs both halves up, restores them into scratch containers, and reads the same business
back out — including the photograph, compared byte for byte. Everything it creates is named
`opendispatch-drill-*` and removed on exit.

**Run it after any change to the image, the schema, or the backup procedure, and once a quarter
regardless.** It is the only thing in this repository that tests the parts of a deployment no unit
test can see; the first time it ran it found two defects in the shipped image.

### Knowing whether it is still fast

```bash
dotnet run --project src/Api -- seed --scale big     # a year of history, on a scratch database
scripts/measure.sh                                   # the four paths somebody waits on
```

The budgets are in the README, measured rather than guessed. Run this after a schema change, after a
query change, and before telling a shop it can grow — it is what makes "it feels slower" a number.
**Never against production data**: the seed writes tens of thousands of rows under the demo
organization, and `measure.sh` re-plans whatever day it is run on.

---

## Routine operations

| Task | Command |
|---|---|
| **Create a login** | `<image> create-user --username <name> --org "<Organization>" --role <Admin\|Dispatcher\|Technician> [--technician <guid>]` — the password is read from stdin if `--password` is not given, which is how to keep it out of your shell history. Running it again for an existing username **replaces** that login: the only password reset there is. |
| **Somebody leaves** | `<image> disable-user --username <name>`. The login stops working; the user row stays, so the audit trail can still say what they did. |
| **Somebody returns** | `create-user` again with the same username: it switches the login back on with a new password. |
| **Erase a customer** | `POST /customers/{id}/erase` as an admin. See the README for exactly what goes and what stays. It is irreversible and it deletes the photographs' bytes from whichever store this host has. |
| **Prune protocol tables** | `<image> prune --days 30`, from cron. Deletes the sync op log and removal notes older than the window — bookkeeping, not business records. Nothing else in this system is ever pruned. |

Every one of these except `prune` shows up in the audit trail or the logs with a name attached.

### Rotating the signing key

`Jwt:SigningKey` signs every token. Changing it **invalidates every token in the field at once**:
every dispatcher and every phone is signed out and must sign in again. There is no second-key
overlap window — one key, one moment.

So: rotate at a quiet hour, tell people first, and roll all instances together (an instance still
holding the old key would accept tokens the new ones reject, which is worse than a clean cutover).
Rotate immediately and without ceremony if the key has leaked — a leaked signing key is somebody
able to mint an admin token for any organization.

---

## Alerts

Nothing here assumes a particular monitoring stack. Set `Otel:Endpoint` and a collector receives the
`OpenDispatch` meter, ASP.NET Core's HTTP metrics, .NET runtime metrics and traces; the queries below
are written in PromQL because that is the most common thing on the far side of a collector, and they
translate.

**Five alerts. They are few on purpose** — a page that fires weekly is a page nobody reads.

| Alert | Condition | Why this threshold | First thing to do |
|---|---|---|---|
| **The host is not serving** | `GET /health/ready` != 200 for 2 minutes, from outside the host | Under two minutes is a deploy or a database blip, and the API recovers from both by itself. Two minutes of 503 is an outage a shop is feeling. | [`/health/ready` is 503](#healthready-is-503) below. Check the database before the API. |
| **Requests are failing** | `sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m])) / sum(rate(http_server_request_duration_seconds_count[5m])) > 0.02` for 10 minutes | 5xx is *this system's* fault by definition — a refusal is a 4xx and a sync conflict is a 200. Two percent over ten minutes is a bug affecting real requests rather than one bad client. | Find the `traceId` in the logs and read the exception; one endpoint or all of them is the first question. |
| **Reactions are not being delivered** | `SELECT count(*) FROM outbox_messages WHERE occurred_at < now() - interval '5 minutes'` > 0 for 15 minutes | The sweep clears an ordinary backlog within a minute (10s interval, 30s grace). Anything older than five minutes and still there has failed repeatedly. | [Rows accumulating in `outbox_messages`](#rows-are-accumulating-in-outbox_messages) below. Every row is work a shop believes happened. |
| **Planning a day has got slow** | `histogram_quantile(0.95, sum by (le) (rate(opendispatch_scheduling_optimize_duration_milliseconds_bucket[15m]))) > 5000` for 30 minutes | A dispatcher presses this button and waits at their desk; five seconds is the edge of tolerable, and the p95 rather than the max so one enormous day does not page anybody. | Compare against the day's size. A shop that has grown needs the budget re-measured, not the alert raised. |
| **Somebody is guessing passwords** | `sum(rate(aspnetcore_rate_limiting_requests_rejected_total[5m])) > 1` for 10 minutes | The limiter only guards `/auth/login` (20 attempts per address per 300s). Sustained rejections are either an attack or an office behind one NAT address. | If it is one address from outside, the limiter is doing its job and there is nothing to do. If it is the shop, raise `RateLimit:PermitLimit`. |

Two things worth watching without paging anybody:

- **`opendispatch.sync.ops.conflicted`, by its `reason` tag.** A steady trickle is the protocol
  working. A step change in one reason is a client release behaving differently — the shape that
  matters is `sync.unsupportedOperation` appearing at all, which means a phone is newer than this
  host. Graph it beside `opendispatch.sync.ops.applied`.
- **`opendispatch.scheduling.optimize.duration` as a trend.** It grows with the shop, and it is the
  number that says when the box needs to be bigger — long before the alert above fires.

---

## When something is wrong

### Start here

```bash
curl -s http://<host>:8080/health/ready   # 200 = serving, 503 = take it out of rotation
docker logs --since 15m <api> | tail -50
```

Every log line and every error body carries the same `traceId` (`X-Correlation-ID`). If a shop can
give you one from an error message, it is the fastest way into the logs.

### `/health/ready` is 503

**Read the body first — it names the check that failed**, and the three mean different things:

```json
{"status":"degraded","service":"opendispatch-api",
 "checks":{"database":"healthy","attachments":"degraded","board-backplane":"healthy"}}
```

| Check | What it means | What to do |
|---|---|---|
| `database` **unhealthy** | The API cannot reach Postgres. Nothing works. | Check the database first (`pg_isready`, disk, connection count). It recovers on its own when the database comes back — **do not restart the API**; a restart loses nothing but tells you nothing either. The API retries transient failures three times over five seconds before answering at all, so this means longer than a blip. |
| `attachments` **degraded** | The volume is not mounted, or the bucket is unreachable with the credentials this host holds. Dispatch, invoicing and sync are all fine; **uploads and downloads 500**. | On a disk: check the mount and its ownership (`chown -R 1654:1654`). On a bucket: check `Attachments__Bucket`, the endpoint, and that the access key still works. This is the one that used to answer "healthy" while every technician's photograph failed. |
| `board-backplane` **degraded** | Redis is configured and not answering. Everything works except the live board across instances. | Check Redis. With more than one API instance running, dispatchers are now seeing only their own host's changes — the same silent failure the backplane exists to prevent. |

A degraded host answers 503 deliberately: readiness is a yes/no question for a load balancer, and
the body is where the nuance lives.

### Rows are accumulating in `outbox_messages`

```sql
SELECT type, count(*), min(occurred_at), max(attempts), left(last_error, 200)
FROM outbox_messages GROUP BY type, left(last_error, 200) ORDER BY 2 DESC;
```

Every row is a reaction that could not be delivered — a stop that should have been withdrawn, a
board that was not told. A handful that clear within a minute are normal (the sweep runs every ten
seconds after a thirty-second grace). Rows that stay, with a rising `attempts` and a `last_error`,
are a poison message: read the error, fix the cause, and the next sweep delivers them.

**The one shape that never clears by itself** is a payload naming an event type this build does not
have — a row written before a rename. Those need the old build, or a deliberate delete once somebody
has decided the reaction no longer matters. Deleting from this table is destroying evidence of work
that did not happen; do it consciously.

Turning the sweep off (`Outbox:Enabled=false`) is an incident measure while dealing with a poison
message, not a setting. With it off, a reaction lost to a failure stays lost.

### A technician's phone says its work was refused

Conflicts are answered with a 200 and a per-operation reason — that is the protocol, not a failure.
Look at `opendispatch.sync.ops.conflicted`, tagged with the reason:

| Tag (the error code) | What it means | What to do |
|---|---|---|
| `sync.notesSuperseded` | The office wrote a newer note. Last-write-wins by the writer's clock. | Nothing. Working as designed. |
| `job.illegalTransition` | The phone asked for a status the job cannot move to — usually a stale copy. | Nothing; the phone finds out by pulling. |
| `job.erased` | The customer was erased while the phone was offline. | Nothing. The refusal is what keeps the erasure honest. |
| `sync.unsupportedOperation` / `sync.malformedOperation` | A client sent something this build does not know, or could not read. | A client/server version mismatch. Check what is deployed. |
| `job.notFound` | The job is not this tenant's, or no longer exists. | A phone signed in as the wrong organization, or a very stale device. |

A phone whose clock is fast is the one that produces conflicts nobody can explain. The raw client
timestamp is kept on the op-log row exactly for this:

```sql
SELECT technician_id, type, client_ts, applied_at, applied_at - client_ts AS skew
FROM sync_ops ORDER BY applied_at DESC LIMIT 50;
```

### Photograph uploads fail

The attachment volume is not writable by the API's user (uid 1654). With a **named** volume this is
handled by the image; with a **bind** mount the host directory's ownership wins:

```bash
chown -R 1654:1654 /path/to/attachments
```

An unwritable volume does not stop the host from starting — the directory exists, so the startup
check passes — and shows up as a 500 on the first upload of the day.

### A dispatcher cannot slot in an emergency

`schedule.overlappingDay` means somebody has dragged two of that technician's stops onto the same
hour, so their day is not a route and there is nothing to insert into. The repair is
`POST /schedule/normalize` with that technician and the day — it re-times their run and touches
nobody else's. `schedule.undrivableDay` back from *that* means the day genuinely does not fit their
shift however it is timed: work has to come off it, which is a dispatcher's decision rather than
something the system should make for them.

### The board is not updating for some people

Two instances without `SignalR:Redis`. Each holds its own connections, so a change made through one
never reaches a client connected to the other, and nothing errors. The startup log says which mode
the host is in. Set `SignalR:Redis` and roll.

### Sign-ins are being refused in bulk

Check the login rate limiter (`RateLimit:*`, 20 attempts per address per 300s by default). An office
behind one NAT address can exhaust it legitimately — raise it. A single address exhausting it
repeatedly from outside is an attack, and the limiter is doing its job.

---

## Known limits, so you find them here rather than at 2am

- **The disk adapter is one machine's disk, and on some platforms it is nobody's.** Two API instances
  without shared storage will store a photograph on one and 404 it from the other, silently; a
  platform with an ephemeral container filesystem destroys the lot on the next deploy, which is
  worse and quieter. Both are answered by `Attachments__Bucket` — the object-store adapter — and the
  startup log says which store this host has.
- **A versioned bucket makes erasure a lie.** If `Attachments__Bucket` points at a store with
  versioning or object-lock retention turned on, a delete hides the object instead of removing it,
  and a photograph somebody asked you to erase stays recoverable. Nothing in the software can check
  this; check it on the bucket.
- **A request is cut off at 30 seconds, and `GET /export` at five minutes.** Both answer 503. A shop
  large enough to hit the export ceiling has outgrown a single streamed dump, and the answer is a
  paged export rather than a larger number — but the number is in `RequestLimits` if you disagree at
  two in the morning.
- **A customer notification that fails is not retried.** A send throws, it is logged at error level
  ("A customer notification (…) could not be delivered and will not be retried") and the request it
  was raised by still succeeds — because the alternative is a dispatcher getting a 500 for a status
  change that already happened, and the board's own repaint failing with it. A mail outage therefore
  costs exactly the messages that fell inside it. Alert on that log line if a shop cares; a durable
  queue for outbound messages is the thing that would buy the retry back, and it does not exist.
- **Disabling a user does not revoke the token they hold.** Up to `Jwt:ExpiryMinutes` (720 by
  default) of continued access. Lower it if that is unacceptable; rotate the signing key if it is
  urgent, accepting that everybody else is signed out too.
- **The API grows to about half a gigabyte and stays there.** .NET's server garbage collector keeps
  the heap it has grown; repeated exports take a fresh process from ~200 MB to ~500 MB, where it
  levels off. That is not a leak, and the fix if a box is tight is `DOTNET_gcServer=0`, which held
  the same run near 300 MB at 20–40% more latency under load. Size for 2 GB and it is a non-event.
- **Nothing here is multi-region and nothing fails over automatically.** One database, one API
  deployment. Recovery from losing the database is the restore procedure above, and its speed is
  whatever your backup schedule and the drill say it is.
- **The audit trail records who did what, never the values.** "Who cancelled job X at 14:02" is
  answerable; "what was the phone number before it changed" is not, deliberately — see the README.
