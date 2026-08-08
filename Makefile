.PHONY: up run

## up: start Postgres/PostGIS via docker compose
up:
	docker compose up -d

## run: run the Api host
run:
	dotnet run --project src/Api
