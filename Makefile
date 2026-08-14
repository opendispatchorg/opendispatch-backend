CONFIGURATION ?= Debug
WATCH_PROJECT ?= tests/Domain.Tests

# The generated @opendispatch/contracts package. Everything under it is output: it is deleted
# and rebuilt by gen-contracts, and committed both so it can be diffed for drift and because the
# published tarball (npm publish, from this same directory) is exactly what is checked out here
# — nothing is assembled specially for a release that CI has not already verified.
CONTRACTS := contracts

# A throwaway npm project that depends on $(CONTRACTS) the way a real client repository would —
# through its own package.json, not a relative path into its src/. Proves the package's own
# `exports`/`types` fields resolve, which compiling contracts/src/*.ts directly cannot.
SAMPLE_CLIENT := tools/sample-client

# Where the build-time OpenAPI export lands. The file name comes from the project name, which
# the generation targets do not let us set — hence the copy rather than a direct write.
OPENAPI_EXPORT := src/Api/obj/openapi/Api.json

# EF Core's design-time tools read the model from Infrastructure and the connection string from
# the Api host's configuration, so every `dotnet ef` command needs both projects.
EF := dotnet ef --project src/Infrastructure --startup-project src/Api

.PHONY: up run migrate migration seed test test-fast test-watch gen-contracts check-contracts \
	check-contracts-sample publish-contracts

## up: start Postgres/PostGIS via docker compose
up:
	docker compose up -d

## run: run the Api host
run:
	dotnet run --project src/Api

## migrate: apply EF migrations to the compose database (needs `make up` first)
migrate:
	dotnet tool restore
	$(EF) database update

## migration: scaffold a new migration - `make migration NAME=AddSomething`
##
## The composite index on jobs(org_id, status, window_start) is hand-written in the initial
## migration because EF cannot declare an index over a complex type's member. Regenerating that
## migration from scratch would drop it; adding a new one on top will not.
##
## A migration that adds a table also needs its stamp_change_seq trigger, hand-written the way
## the SyncOpLog migration writes them: the change_seq column arrives on the model by itself,
## the trigger that keeps it current does not. SchemaTests fails if one is missing.
migration:
	@test -n "$(NAME)" || { echo "usage: make migration NAME=AddSomething" >&2; exit 1; }
	dotnet tool restore
	$(EF) migrations add $(NAME) --output-dir Persistence/Migrations

## seed: load the dev demo dataset (needs `make up` and `make migrate` first)
##
## Re-runnable: it finds the demo organization by name and rewrites everything under it, so a demo
## driven into a state you would rather undo costs one command rather than a dropped database.
##
## ASPNETCORE_ENVIRONMENT is deliberately not set here, and that is worth being precise about:
## `dotnet run` applies src/Api/Properties/launchSettings.json, whose profile sets Development and
## *overrides* any ambient value — so this target is always a development host, which is correct,
## because a machine running `dotnet run` out of this working tree is one. launchSettings.json is
## not part of publish output, so a deployed host has whatever its environment says, and there the
## seeder refuses (exit 1, having written nothing). Setting the variable here would replace that
## with a claim this Makefile made on the host's behalf.
seed:
	dotnet run --project src/Api -- seed

## test: everything - unit + integration (integration needs Docker running)
test:
	dotnet test OpenDispatch.sln --configuration $(CONFIGURATION)

## test-fast: only the Unit category, via the pure-test-project solution filter
test-fast:
	dotnet test OpenDispatch.Unit.slnf --configuration $(CONFIGURATION) --filter Category=Unit

## test-watch: re-run the Unit category on every save (override WATCH_PROJECT=<path>)
test-watch:
	dotnet watch --project $(WATCH_PROJECT) test --filter Category=Unit

## gen-contracts: rebuild @opendispatch/contracts from the C# types and the OpenAPI document
##
## ASPNETCORE_ENVIRONMENT is set here and only here. The export works by building the real host in
## a child process to read its endpoints, and that process has no environment of its own — so it
## lands outside Development, where the startup guards refuse the development signing key and
## database password appsettings.json commits (see DevelopmentDefaults). Generating a document is a
## development-time act on a working tree, so it says so; nothing about the guards is relaxed.
gen-contracts:
	ASPNETCORE_ENVIRONMENT=Development \
		dotnet build src/Api --configuration $(CONFIGURATION) -p:OpenApiGenerateDocuments=true
	mkdir -p $(CONTRACTS)/src
	cp $(OPENAPI_EXPORT) $(CONTRACTS)/openapi.json
	npm ci --prefix tools --silent
	tools/node_modules/.bin/openapi-typescript $(CONTRACTS)/openapi.json --output $(CONTRACTS)/src/rest.ts
	dotnet run --project src/Contracts.CodeGen --configuration $(CONFIGURATION) -- --output $(CONTRACTS)
	@for file in package.json README.md openapi.json src/index.ts src/rest.ts; do \
		test -s $(CONTRACTS)/$$file \
			|| { echo "gen-contracts: $(CONTRACTS)/$$file is missing or empty" >&2; exit 1; }; \
	done
	tools/node_modules/.bin/tsc --noEmit --strict --target es2022 --module preserve \
		--moduleResolution bundler $(CONTRACTS)/src/index.ts $(CONTRACTS)/src/rest.ts
	@echo "gen-contracts: $(CONTRACTS)/ regenerated and type-checks - commit it if it changed"

## check-contracts: fail if the committed contracts/ is not what the sources generate
##
## Regenerate-and-diff. What CI runs, and what a developer runs to reproduce a red build:
## a drift check nobody can reproduce locally is one people learn to click past. The
## comparison is against what is staged, so regenerating and staging before committing is
## not reported as drift, while a stale commit and a generated file nobody added both are.
check-contracts: gen-contracts
	@drifted="$$(git diff --name-only -- $(CONTRACTS); \
		git ls-files --others --exclude-standard -- $(CONTRACTS))"; \
	if [ -n "$$drifted" ]; then \
		echo "" >&2; \
		echo "Contract drift. $(CONTRACTS)/ is not what the current C# and endpoints generate:" >&2; \
		echo "$$drifted" | sed 's/^/  /' >&2; \
		echo "" >&2; \
		git --no-pager diff -- $(CONTRACTS) >&2; \
		echo "Run 'make gen-contracts' and commit the result." >&2; \
		exit 1; \
	fi
	@echo "check-contracts: $(CONTRACTS)/ matches the sources it is generated from"

## check-contracts-sample: prove a real importer compiles against $(CONTRACTS) (Document 3, step 52)
##
## `npm install` on a file: dependency resolves through the package's own package.json —
## its `exports` map and `types` field — the same mechanism a registry install uses, just
## without a registry. gen-contracts' own tsc invocation compiles contracts/src/*.ts directly
## and cannot catch a broken exports entry or a missing types field; this can.
check-contracts-sample: gen-contracts
	npm install --prefix $(SAMPLE_CLIENT) --no-audit --no-fund
	npm exec --prefix $(SAMPLE_CLIENT) -- tsc --noEmit -p $(SAMPLE_CLIENT)/tsconfig.json
	@echo "check-contracts-sample: a sample client import compiles against $(CONTRACTS)"

## publish-contracts: publish $(CONTRACTS) to the npm registry (Document 3, step 52)
##
## Needs registry auth this repository does not itself hold — an .npmrc or NPM_TOKEN a release
## actually has. Refuses to publish anything gen-contracts has not just regenerated and
## check-contracts has not just confirmed matches what is committed, so a version can never ship
## something other than what CI already reviewed.
publish-contracts: check-contracts
	npm publish $(CONTRACTS)
