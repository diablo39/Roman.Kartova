===== /me/permissions per role =====
--- OrgAdmin (admin@orga.kartova.local) ---
  GET http://localhost:8080/api/v1/organizations/me/permissions => 200
    body: {"role":"OrgAdmin","permissions":["catalog.applications.register","catalog.applications.lifecycle.forward","catalog.applications.lifecycle.reverse","catalog.applications.edit-metadata","catalog.read"]
--- Member (member@orga.kartova.local) ---
  GET http://localhost:8080/api/v1/organizations/me/permissions => 200
    body: {"role":"Member","permissions":["catalog.applications.register","catalog.applications.lifecycle.forward","catalog.applications.edit-metadata","catalog.read"]}
--- TeamAdmin (team-admin@orga.kartova.local) ---
  GET http://localhost:8080/api/v1/organizations/me/permissions => 200
    body: {"role":"TeamAdmin","permissions":["catalog.applications.register","catalog.applications.lifecycle.forward","catalog.applications.edit-metadata","catalog.read"]}
--- Viewer (viewer@orga.kartova.local) ---
  GET http://localhost:8080/api/v1/organizations/me/permissions => 200
    body: {"role":"Viewer","permissions":["catalog.read"]}

===== POST /applications per role (Member+ → 201, Viewer → 403) =====
--- OrgAdmin (admin@orga.kartova.local) ---
  POST http://localhost:8080/api/v1/catalog/applications => 201
    body: {"id":"02f3d4eb-1627-49e7-b3de-afbcb9c91a2c","tenantId":"11111111-1111-1111-1111-111111111111","name":"smoke-1779480459-31321","displayName":"Smoke","description":"Smoke test.","ownerUserId":"77819a33
--- Member (member@orga.kartova.local) ---
  POST http://localhost:8080/api/v1/catalog/applications => 201
    body: {"id":"b86cdf7a-d207-4a2f-b727-e103caf676f9","tenantId":"11111111-1111-1111-1111-111111111111","name":"smoke-1779480460-13312","displayName":"Smoke","description":"Smoke test.","ownerUserId":"358213ed
--- TeamAdmin (team-admin@orga.kartova.local) ---
  POST http://localhost:8080/api/v1/catalog/applications => 201
    body: {"id":"51bf8519-39b8-4a0d-9951-353fd52620f2","tenantId":"11111111-1111-1111-1111-111111111111","name":"smoke-1779480461-18206","displayName":"Smoke","description":"Smoke test.","ownerUserId":"44b63b96
--- Viewer (viewer@orga.kartova.local) ---
  POST http://localhost:8080/api/v1/catalog/applications => 403
    body: 

===== POST /reactivate as Member (expect 403) =====
  POST http://localhost:8080/api/v1/catalog/applications/00000000-0000-0000-0000-000000000099/reactivate => 403
    body: 

===== POST /reactivate as OrgAdmin (expect 404 — random GUID) =====
  POST http://localhost:8080/api/v1/catalog/applications/00000000-0000-0000-0000-000000000099/reactivate => 404
    body: {"type":"https://kartova.io/problems/resource-not-found","title":"Application not found","status":404,"detail":"No application with that id is visible in the current tenant.","traceId":"00-bbce0cb93b0

===== POST /un-decommission as OrgAdmin with valid body (expect 404 — random GUID) =====
  POST http://localhost:8080/api/v1/catalog/applications/00000000-0000-0000-0000-000000000099/un-decommission => 404
    body: {"type":"https://kartova.io/problems/resource-not-found","title":"Application not found","status":404,"detail":"No application with that id is visible in the current tenant.","traceId":"00-2be52d0c240

Smoke complete.
