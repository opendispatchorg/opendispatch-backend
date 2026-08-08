CONFIGURATION ?= Debug
WATCH_PROJECT ?= tests/Domain.Tests

.PHONY: up run test test-fast test-watch

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
