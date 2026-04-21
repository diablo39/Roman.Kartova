.PHONY: up down rebuild test archtest web logs clean

# Start backend services (postgres + migrator + api)
up:
	docker compose up -d postgres
	docker compose up migrator
	docker compose up -d api
	@echo ""
	@echo "API running at http://localhost:8080"
	@echo "Health: curl http://localhost:8080/health/ready"
	@echo "Version: curl http://localhost:8080/api/v1/version"

# Stop and remove all services + volumes
down:
	docker compose down -v

# Rebuild images and restart
rebuild:
	docker compose build --no-cache
	$(MAKE) down
	$(MAKE) up

# Run full test suite (arch + unit + integration)
test:
	cmd /c dotnet test Kartova.sln --configuration Release

# Run only architecture tests (fast fail-early gate)
archtest:
	cmd /c dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --configuration Release

# Start frontend dev server
web:
	cd web && npm run dev

# Tail logs of all services
logs:
	docker compose logs -f

# Full cleanup: down + remove build artifacts
clean: down
	cmd /c dotnet clean Kartova.sln
	rm -rf web/node_modules web/dist
