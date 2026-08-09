CONFIGURATION ?= Debug
WATCH_PROJECT ?= tests/Domain.Tests

# The generated @opendispatch/contracts package. Everything under it is output: it is deleted
# and rebuilt by gen-contracts, and committed so the clients can consume it by path or git
# reference until publishing is configured.
CONTRACTS := contracts

# Where the build-time OpenAPI export lands. The file name comes from the project name, which
# the generation targets do not let us set — hence the copy rather than a direct write.
OPENAPI_EXPORT := src/Api/obj/openapi/Api.json

.PHONY: up run test test-fast test-watch gen-contracts

## up: start Postgres/PostGIS via docker compose
up:
	docker compose up -d

## run: run the Api host
run:
	dotnet run --project src/Api

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
