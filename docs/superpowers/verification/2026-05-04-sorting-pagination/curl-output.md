# Live-stack curl evidence — 2026-05-04

## docker compose ps

```
NAME                      IMAGE                            COMMAND                  SERVICE       CREATED       STATUS                 PORTS
romangig2-keycloak-1      quay.io/keycloak/keycloak:26.1   "/opt/keycloak/bin/…"   keycloak      2 hours ago   Up 2 hours (healthy)   0.0.0.0:8180->8080/tcp, [::]:8180->8080/tcp
romangig2-keycloak-db-1   postgres:16-alpine               "docker-entrypoint.s…"   keycloak-db   2 hours ago   Up 2 hours (healthy)   5432/tcp
romangig2-postgres-1      postgres:16-alpine               "docker-entrypoint.s…"   postgres      2 hours ago   Up 2 hours (healthy)   0.0.0.0:5432->5432/tcp, [::]:5432->5432/tcp
```

## API

Port: `http://localhost:5021`

Health check:

```
command:  curl -i http://localhost:5021/health/live
status:   HTTP/1.1 200 OK
body:     Healthy
```

Auth: JWT obtained via Keycloak resource-owner-password grant:

```
curl -s -X POST "http://localhost:8180/realms/kartova/protocol/openid-connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=kartova-api&username=admin@orga.kartova.local&password=dev_pass"
```

User: `admin@orga.kartova.local` · tenant_id: `11111111-1111-1111-1111-111111111111` (SeededOrgs.OrgA) · role: `OrgAdmin`

---

## Happy path — default request (limit=3)

```
command:  curl -i -H "Authorization: Bearer $TOKEN" \
            "http://localhost:5021/api/v1/catalog/applications?limit=3"
status:   HTTP/1.1 200 OK
headers:  Content-Type: application/json; charset=utf-8
body:
{
  "items": [
    {
      "id": "6b9976f6-1fb9-4300-8fd6-e79f6c68f708",
      "tenantId": "11111111-1111-1111-1111-111111111111",
      "name": "a-app-119",
      "displayName": "A App 119",
      "description": "Seeded application #120",
      "ownerUserId": "33cbd720-7e1b-406f-9e74-b2655c2ac6b7",
      "createdAt": "2026-05-05T06:42:08.232132+00:00"
    },
    {
      "id": "0df8fbae-fa70-471b-9ec9-d9d454e476ec",
      "tenantId": "11111111-1111-1111-1111-111111111111",
      "name": "b-app-118",
      "displayName": "B App 118",
      "description": "Seeded application #119",
      "ownerUserId": "43eb350b-5754-49e6-8c99-7479844e7f08",
      "createdAt": "2026-05-05T06:41:08.232132+00:00"
    },
    {
      "id": "c8effcc0-5a58-457b-a6c0-728df9519ed0",
      "tenantId": "11111111-1111-1111-1111-111111111111",
      "name": "c-app-117",
      "displayName": "C App 117",
      "description": "Seeded application #118",
      "ownerUserId": "320f5d61-805d-483d-a7a8-0a4f5329b48a",
      "createdAt": "2026-05-05T06:40:08.232132+00:00"
    }
  ],
  "nextCursor": "eyJzIjoiMjAyNi0wNS0wNVQwNjo0MDowOC4yMzIxMzIwWiIsImkiOiJjOGVmZmNjMC01YTU4LTQ1N2ItYTZjMC03MjhkZjk1MTllZDAiLCJkIjoiZGVzYyJ9",
  "prevCursor": null
}
```

Confirms: wire envelope `{items, nextCursor, prevCursor}`, tenant isolation (all OrgA items), descending sort by `createdAt` (default), opaque base64url cursor present.

---

## Negative path 1 — invalid sort field

```
command:  curl -i -H "Authorization: Bearer $TOKEN" \
            "http://localhost:5021/api/v1/catalog/applications?sortBy=garbage"
status:   HTTP/1.1 400 Bad Request
headers:  Content-Type: application/problem+json
body:
{
  "type": "https://kartova.io/problems/invalid-sort-field",
  "title": "Invalid sort field",
  "status": 400,
  "detail": "Sort field 'garbage' is not allowed. Allowed: createdAt, name.",
  "instance": "/api/v1/catalog/applications",
  "fieldName": "garbage",
  "allowedFields": ["createdAt", "name"],
  "traceId": "00-ceb02d44f31ad74911f8000c0dc284ff-051f19199f153856-00"
}
```

Confirms: `type` = `https://kartova.io/problems/invalid-sort-field`, `allowedFields` = `["createdAt","name"]`, `traceId` present.

---

## Negative path 2 — tampered cursor

```
command:  curl -i -H "Authorization: Bearer $TOKEN" \
            "http://localhost:5021/api/v1/catalog/applications?cursor=not-a-valid-cursor!!!"
status:   HTTP/1.1 400 Bad Request
headers:  Content-Type: application/problem+json
body:
{
  "type": "https://kartova.io/problems/invalid-cursor",
  "title": "Invalid cursor",
  "status": 400,
  "detail": "Cursor is not valid base64url.",
  "instance": "/api/v1/catalog/applications",
  "traceId": "00-eef8a5a3a35e97a13c8569befb5a5412-ff99392e04d63b5a-00"
}
```

Confirms: `type` = `https://kartova.io/problems/invalid-cursor`, `traceId` present.

---

## Summary

All three paths verified against a live stack (Postgres + Keycloak in Docker Compose, `dotnet run` API on port 5021).
Branch: `feat/sorting-pagination` · Date: 2026-05-04
