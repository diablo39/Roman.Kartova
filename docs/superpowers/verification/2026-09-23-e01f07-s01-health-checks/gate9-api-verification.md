# Gate 9 — Live API Verification

**Date:** 2026-09-23 · **Commit:** `bb50aa5`
**Stack:** `docker compose up postgres keycloak migrator api` against the branch's own rebuilt images (`kartova/migrator:dev`, `kartova/api:dev`), remapped to host ports 15432/18180/18080 to avoid colliding with an already-running dev stack on the default ports (5432/8180/8080) on this host. Real Postgres, real KeyCloak (realm imported from `deploy/keycloak/kartova-realm.json`), real EF migrations applied by the real `Kartova.Migrator` container (exit 0).

## `/health/live` — anonymous

```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:18080/health/live
{"status":"Healthy","totalDuration":"00:00:00.0073183","entries":{"self":{"status":"Healthy","duration":"00:00:00.0005700","tags":["live"],"description":null}}}
HTTP_STATUS:200
```
Exactly the `{"self"}` entry set — live excludes dependencies.

## `/health/ready` — anonymous

```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:18080/health/ready
{"status":"Healthy","totalDuration":"00:00:00.1472363","entries":{"postgres":{"status":"Healthy","duration":"00:00:00.1465318","tags":["ready","startup"],"description":null},"keycloak":{"status":"Healthy","duration":"00:00:00.0632007","tags":["ready","startup"],"description":"KeyCloak discovery endpoint reachable"}}}
HTTP_STATUS:200
```
Real Postgres + real KeyCloak checks, both Healthy against the actual running dependencies.

## `/health/startup` — anonymous

```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:18080/health/startup
{"status":"Healthy","totalDuration":"00:00:00.8128442","entries":{"postgres":{"status":"Healthy","duration":"00:00:00.0086741","tags":["ready","startup"],"description":null},"keycloak":{"status":"Healthy","duration":"00:00:00.0096393","tags":["ready","startup"],"description":"KeyCloak discovery endpoint reachable"},"migrations":{"status":"Healthy","duration":"00:00:00.8119303","tags":["startup"],"description":"All modules up to date"}}}
HTTP_STATUS:200
```
`migrations` entry Healthy — confirms `ModuleMigrationsHealthCheck`'s `IModule.RegisterForMigrator`-based provider genuinely resolves and queries all 3 real module DbContexts against the real Postgres instance the real Migrator container just migrated.

## `/health/detailed` — anonymous (expect 401)

```
$ curl -s -o /dev/null -w "HTTP_STATUS:%{http_code}\n" http://localhost:18080/health/detailed
HTTP_STATUS:401
```

## `/health/detailed` — real platform-admin JWT (expect 200, full detail)

Token minted via a real password-grant against the real KeyCloak realm (`platform-admin@kartova.local`):
```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" -H "Authorization: Bearer $TOKEN" http://localhost:18080/health/detailed
{"status":"Healthy","totalDuration":"00:00:00.0115736","entries":{"self":{"status":"Healthy","duration":"00:00:00.0000595","tags":["live"],"description":null,"exception":null},"postgres":{"status":"Healthy","duration":"00:00:00.0008898","tags":["ready","startup"],"description":null,"exception":null},"keycloak":{"status":"Healthy","duration":"00:00:00.0047338","tags":["ready","startup"],"description":"KeyCloak discovery endpoint reachable","exception":null},"migrations":{"status":"Healthy","duration":"00:00:00.0103766","tags":["startup"],"description":"All modules up to date","exception":null}}}
HTTP_STATUS:200
```
All 4 checks present, `exception` key present (null) on every entry as the detailed writer's contract requires.

API container logs for this request confirm the real `DefaultHealthCheckService` pipeline ran (`Health check keycloak with status Healthy completed after 4.7338ms with message 'KeyCloak discovery endpoint reachable'`, etc.) — not a cached/synthetic response.

## Side note — image tag collision with the host's other running stack

This host already runs an independent `romangig2-*` docker-compose stack (a different project, from the main, non-worktree checkout) on the same default ports (5432/8080/8180) and reusing the same image tags (`kartova/api:dev`, `kartova/migrator:dev`). Gate 4's `docker compose build` (this branch) already overwrote those shared tags; the already-running `romangig2-*` containers were unaffected (a running container keeps its instantiated filesystem regardless of a later tag reassignment), but a future `docker compose up`/`restart` on that other stack will now pick up this branch's image content. Flagging this for the user's awareness — not a defect in this branch, just a shared-tag side effect of building on the same host as another running project.
