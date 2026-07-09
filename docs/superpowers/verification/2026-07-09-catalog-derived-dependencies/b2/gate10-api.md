# Gate 10 — Derived Service Dependencies (B2) — Live API Verification

Date: 2026-07-09
Stack: full `docker compose up -d` (api, web, postgres, keycloak, keycloak-db, migrator)
Tenant: org-a (`tenantId 11111111-1111-1111-1111-111111111111`)
Auth: Resource-Owner-Password-Credentials grant, KeyCloak realm `kartova`, client `kartova-api` (public, `directAccessGrantsEnabled: true`), user `admin@orga.kartova.local` / `<dev password (see local setup)>` (role `OrgAdmin`).

## Seeded entities

| Entity | Id |
|---|---|
| Team (existing, reused) | `dddddddd-0001-0001-0001-000000000001` ("Demo Team") |
| Application "B2 Verify Provider App" | `7abf7672-048f-4a11-af4d-b0c39b8d162a` |
| Service "B2 Verify Provider" (provider T) | `96d200e4-7dcc-417a-9845-36b818389c57` |
| Service "B2 Verify Consumer" (consumer S) | `7e9c5aad-1350-4f71-b477-9d07b38f3867` |
| Api "B2 Verify Orders API" | `802fc7fb-c1da-4a9c-973e-c72ac3dc3263` |

Relationships created:
- `service:B2 Verify Provider` --`instanceOf`--> `application:B2 Verify Provider App` (relationship id `5400ef1d-6083-479c-8a56-63f3a6dc787f`)
- `application:B2 Verify Provider App` --`providesApiFor`--> `api:B2 Verify Orders API` (relationship id `063432d9-c2e3-479d-a36a-71adcb8080fa`)
- `service:B2 Verify Consumer` --`consumesApiFrom`--> `api:B2 Verify Orders API` (relationship id `4655c679-a9c3-4192-b54b-7d414ccc97bc`)

## Request 1 — derived-dependencies for the consumer (S)

```
curl -s "http://localhost:8080/api/v1/catalog/derived-dependencies?entityId=7e9c5aad-1350-4f71-b477-9d07b38f3867" \
  -H "Authorization: Bearer <REDACTED>"
```

Response:

```json
{
  "dependencies": [
    {
      "serviceId": "96d200e4-7dcc-417a-9845-36b818389c57",
      "displayName": "B2 Verify Provider",
      "teamId": "dddddddd-0001-0001-0001-000000000001",
      "paths": [
        {
          "apiId": "802fc7fb-c1da-4a9c-973e-c72ac3dc3263",
          "apiName": "B2 Verify Orders API",
          "viaApplicationId": "7abf7672-048f-4a11-af4d-b0c39b8d162a",
          "viaApplicationDisplayName": "B2 Verify Provider App"
        }
      ]
    }
  ],
  "dependents": []
}
```

Confirms: consumer S's `dependencies` contains provider T, with provenance `apiName: "B2 Verify Orders API"` and `viaApplicationDisplayName: "B2 Verify Provider App"` — matches expected shape exactly.

## Request 2 — derived-dependencies for the provider (T)

```
curl -s "http://localhost:8080/api/v1/catalog/derived-dependencies?entityId=96d200e4-7dcc-417a-9845-36b818389c57" \
  -H "Authorization: Bearer <REDACTED>"
```

Response:

```json
{
  "dependencies": [],
  "dependents": [
    {
      "serviceId": "7e9c5aad-1350-4f71-b477-9d07b38f3867",
      "displayName": "B2 Verify Consumer",
      "teamId": "dddddddd-0001-0001-0001-000000000001",
      "paths": [
        {
          "apiId": "802fc7fb-c1da-4a9c-973e-c72ac3dc3263",
          "apiName": "B2 Verify Orders API",
          "viaApplicationId": "7abf7672-048f-4a11-af4d-b0c39b8d162a",
          "viaApplicationDisplayName": "B2 Verify Provider App"
        }
      ]
    }
  ]
}
```

Confirms: provider T's `dependents` contains consumer S, with the same provenance path (mirror of Request 1). Both directions of the derived edge are correctly materialized.

## SPA base URL for follow-up Playwright pass

`web` container is serving on `http://localhost:4173` (`romangig2-web-1`, image `kartova/web:dev`, port mapping `4173->8080`). The Vite dev server (5173) was **not** started per task constraints.
