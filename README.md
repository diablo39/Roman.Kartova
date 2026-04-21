# Kartova

SaaS service catalog and developer portal (Backstage + Compass + Statuspage hybrid).

## Stack

.NET 10 LTS · React 19 · PostgreSQL 16 · Wolverine · KafkaFlow · Kubernetes.

See [CLAUDE.md](CLAUDE.md) for full architecture, key decisions, and working agreements.

## Local development

Prerequisites: Docker Desktop (or equivalent), .NET 10 SDK, Node 20 LTS, `make`.

```bash
make up        # start postgres + migrator + api
make web       # start frontend dev server (in a second terminal)
make test      # run full test suite
make archtest  # run architecture tests only (fast)
make down      # stop everything, remove volumes
```

## Documentation

- [ADR library](docs/architecture/decisions/README.md) — 88 accepted decisions with keyword index
- [Product requirements](docs/product/PRODUCT-REQUIREMENTS.md)
- [Backlog](docs/product/EPICS-AND-STORIES.md) — 30 epics, 73 features, 209 stories
- [Progress](docs/product/CHECKLIST.md)
- [Design system](docs/design/DESIGN.md)

## Repository layout

See `CLAUDE.md` "Where to find things" section and
[ADR-0082](docs/architecture/decisions/ADR-0082-modular-monolith-architecture.md) for the modular monolith structure.
