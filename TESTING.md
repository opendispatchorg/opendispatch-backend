# Testing philosophy

Tests here are **proportional to complexity**, not one-per-class. The suite is meant to be
dense where behaviour is hard to get right and absent where it is trivial. A test that
mirrors the implementation line-for-line costs maintenance forever and catches nothing.

## Test exhaustively

These are cheap to write, load-bearing, and the places bugs actually hide:

- **The scheduling engine.** Constraint satisfaction, the objective function, determinism
  under a seed, and the soft-lateness behaviour. Edge cases are the point here.
- **The sync and conflict logic.** Idempotent replay of ops, state-machine legality on
  status changes, last-write-wins on free text, and the server response being authoritative.
- **Domain rules.** State-machine transitions, invariants that must be unrepresentable when
  violated, and money math.

## Cover incidentally

Exercise these through a handful of real end-to-end flows, not a unit test per item:

- CRUD
- Repositories
- Controllers and endpoints

One test that creates a customer, schedules a job, completes it and invoices it is worth
more than thirty per-endpoint tests, and it breaks for real reasons.

## Do not test

- Getters and setters
- DTO mapping
- Framework behaviour — EF saving, MediatR dispatching, model binding, routing
- Anything that restates the implementation rather than asserting a behaviour

## Coverage

Coverage is meaningful **only for the `Scheduling` and `Sync` packages**. Ignore it
everywhere else, and do not add a solution-wide coverage gate. A number that counts DTO
constructors tells you nothing about whether the scheduler is correct.

## Categories

Every test carries exactly one category trait, so the two halves run independently:

```csharp
[Trait(TestCategories.Name, TestCategories.Unit)]         // pure, fast, no I/O
[Trait(TestCategories.Name, TestCategories.Integration)]  // real Postgres, real host
```

`TestCategories` lives in `tests/TestSupport`, which every test project references.

| Category | What belongs there | Runner |
|---|---|---|
| `Unit` | Domain, Scheduling, Application-with-fakes. No I/O, no container, no host. | `make test-fast` |
| `Integration` | PostGIS via Testcontainers, the real host via `WebApplicationFactory`. | `make test` |

An uncategorised test still runs under `make test` but is invisible to `make test-fast`,
so it silently drops out of the fast feedback loop. Always add the trait.

## Running them

```bash
make test-fast     # Unit only - the loop you keep open while coding
make test          # everything; needs Docker running for the container
make test-watch    # re-runs the Unit category on every save
```

`make test-watch` defaults to `tests/Domain.Tests`; point it elsewhere with
`make test-watch WATCH_PROJECT=tests/Scheduling.Tests`.

CI runs `make test`.

### Why test-fast uses a solution filter

`make test-fast` runs `OpenDispatch.Unit.slnf`, not the full solution. The filter contains
only the three pure test projects, so MSBuild never builds `Api`/`Infrastructure` and
VSTest never launches the integration assembly just to discover nothing in it. That is
roughly 3s versus 3.5s today, and the gap widens as the integration suite grows.

**Adding a new pure test project means adding it to `OpenDispatch.Unit.slnf`**, or its
tests silently never run in the fast loop.

Note that most of those 3s is MSBuild and VSTest startup — the tests themselves execute in
single-digit milliseconds. `make test-watch` is what makes the loop feel immediate, because
it keeps the build warm between saves.

## Shared harness

Reuse these instead of rolling your own setup:

- **`PostgresFixture`** (`tests/Api.IntegrationTests/Fixtures`) — starts **one** PostGIS
  container for the whole run, enables the extension, and hands the same instance to every
  class marked `[Collection(PostgresCollectionDefinition.Name)]`. Containers cost seconds to
  start; never start one per test class.
- **`ApiFactory`** (same folder) — boots the real Api host in-process. Endpoint tests go
  through this rather than `WebApplicationFactory` directly, so host-level test
  configuration lives in one place.
- **`TestSupport`** (`tests/TestSupport`) — category traits today; the home for
  object-mother / builder helpers as the aggregates land.

### Builders

Aggregates get an object-mother builder each, so setup reads as intent and a new required
field is a one-line change instead of a sweep through every test:

```csharp
var job = JobBuilder.Any().WithSkill("hvac").InWindow(morning).Build();
```

These are added **with the aggregates that need them** — `JobBuilder` arrives with `Job`,
`SchedulingProblemBuilder` with `SchedulingProblem` — rather than being written speculatively
against types that do not exist yet.

## Writing a new test

1. Pick the category and add the trait.
2. If it needs a database, join the Postgres collection — do not start a container.
3. Arrange–Act–Assert, and name it for the behaviour under test
   (`RejectsIllegalTransition`, not `TestTransition2`).
4. Ask whether it would fail for a real reason. If the only way to break it is to delete
   the code it mirrors, do not write it.
