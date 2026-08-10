CONFIGURATION ?= Debug
WATCH_PROJECT ?= tests/Domain.Tests

# The generated @opendispatch/contracts package. Everything under it is output: it is deleted
# and rebuilt by gen-contracts, and committed so the clients can consume it by path or git
# reference until publishing is configured.
CONTRACTS := contracts

# Where the build-time OpenAPI export lands. The file name comes from the project name, which
# the generation targets do not let us set — hence the copy rather than a direct write.
OPENAPI_EXPORT := src/Api/obj/openapi/Api.json

# EF Core's design-time tools read the model from Infrastructure and the connection string from
# the Api host's configuration, so every `dotnet ef` command needs both projects.
EF := dotnet ef --project src/Infrastructure --startup-project src/Api

.PHONY: up run migrate migration test test-fast test-watch gen-contracts check-contracts

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
migration:
	@test -n "$(NAME)" || { echo "usage: make migration NAME=AddSomething" >&2; exit 1; }
	dotnet tool restore
	$(EF) migrations add $(NAME) --output-dir Persistence/Migrations

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
gen-contracts:
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
