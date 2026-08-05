# Control-test patterns: browser protections

Runnable patterns for the browser-facing control families — protective headers, CSP, request
forgery, cookie and token storage, cache hygiene, and subresource integrity. Each section names
the control our code provides, then a test asserting the protective behavior holds. The control
design behind every pattern here is `knowledge/security/browser-protections.md`; the mandate,
marker convention, and fails-when-removed spot-check are in
`knowledge/security/control-verification-tests.md`. Access-control families:
`knowledge/security/control-test-patterns-access.md`; data-flow families (including the
markup-renders-as-text encoding pattern these headers back up):
`knowledge/security/control-test-patterns-dataflow.md`.

## Harness

Two levels, used deliberately:

- Header-level tests run in-process through the harness table in
  `knowledge/security/control-test-patterns-access/harness-per-ecosystem.md` — fast, on every
  build, and sufficient wherever the control is a response header our middleware sets.
- Browser-level tests run a real browser (Playwright-class tooling) and assert what the browser
  refused — needed only where the control's effect exists in the browser: CSP blocking
  execution, cookie invisibility to script, storage contents. Keep these few; a control whose
  only test is browser-level runs less often than the code changes under it.

One fixture rule keeps header tests honest: assert on the route class through the real
middleware stack (an authenticated HTML route, an authenticated API route, an error response),
not on one hand-picked URL — headers set per-handler instead of per-layer are exactly the
regression these tests exist to catch.

## Protective header set: snapshot per route class

Our middleware sets the full protective header set on every HTML response; one snapshot test
per route class makes removing or weakening any header a failing diff.

```ts
it("control: HTML routes carry the protective header set", async () => {
  for (const path of HTML_ROUTE_CLASS) {              // includes an error route on purpose
    const res = await request(app).get(path).set(auth(token));
    expect(res.headers["strict-transport-security"]).toMatch(/max-age=\d{7,}/);
    expect(res.headers["x-content-type-options"]).toBe("nosniff");
    expect(res.headers["referrer-policy"]).toBe("strict-origin-when-cross-origin");
    expect(res.headers["content-security-policy"]).toContain("frame-ancestors 'none'");
  }
});
```

Include an error route in the class: error paths bypass middleware more often than success
paths, and HSTS on error responses is part of the control. The fails-when-removed lever is
direct — detaching the header middleware in a scratch run fails the snapshot.

## CSP: policy shape and refused execution

Two tests, two layers. The shape test pins what the policy must never contain; the execution
test proves the policy acts.

```python
@pytest.mark.control
def test_csp_shape_on_html_routes(client):
    policy = parse_csp(client.get("/dashboard").headers["Content-Security-Policy"])
    script_src = policy["script-src"]
    assert "'unsafe-inline'" not in script_src and "*" not in script_src
    assert "'none'" in policy["object-src"] and "'none'" in policy["base-uri"]
    assert re.search(r"'nonce-[A-Za-z0-9+/=_-]{16,}'", " ".join(script_src))

def test_csp_nonce_is_per_response(client):
    n1, n2 = (nonce_of(client.get("/dashboard")) for _ in range(2))
    assert n1 != n2                       # a build-time constant nonce is no nonce
```

```ts
test("control: a non-nonced inline script does not execute", async ({ page }) => {
  await page.goto(appUrl("/dashboard"));
  await page.evaluate(() => {
    const s = document.createElement("script");
    s.textContent = "window.__probe = true";          // no nonce attribute
    document.body.appendChild(s);
  });
  expect(await page.evaluate(() => (window as any).__probe)).toBeUndefined();
});
```

Keep report-only and enforced policies as separate assertions during rollout — a test that
passes on the `Report-Only` header proves reporting, not refusal.

## Request forgery: cross-site refused, same-origin allowed

Both directions, so the gate neither vanishes nor overblocks
(`knowledge/security/browser-protections/request-forgery-defenses.md`):

```ts
it("control: a cross-site-shaped state change is refused", async () => {
  const cookie = await login(app, user);
  const res = await request(app).post("/api/profile/email")
    .set("Cookie", cookie).set("Sec-Fetch-Site", "cross-site")
    .send({ email: "probe@example.com" });            // token deliberately absent
  expect(res.status).toBe(403);
  expect((await profile(user)).email).not.toBe("probe@example.com"); // state unchanged
});

it("control: the same request shaped same-origin with a token succeeds", async () => {
  const { cookie, csrf } = await loginWithToken(app, user);
  await request(app).post("/api/profile/email")
    .set("Cookie", cookie).set("Sec-Fetch-Site", "same-origin")
    .set("X-CSRF-Token", csrf).send({ email: "new@example.com" }).expect(200);
});
```

Run the refusal against each state-changing route class. A companion test pins the invariant
`SameSite=Lax` depends on: safe methods mutate nothing — drive every GET route in the class and
assert zero writes recorded.

## Cookies and token storage

The attribute snapshot runs at header level; the visibility assertions need the browser.

```python
@pytest.mark.control
def test_session_cookie_attributes(client):
    cookie = parse_set_cookie(login(client))           # parse the raw Set-Cookie header
    assert cookie.name.startswith("__Host-")
    assert cookie.secure and cookie.httponly
    assert cookie.samesite in ("Lax", "Strict")
```

```ts
test("control: the session is invisible to script and absent from storage", async ({ page }) => {
  await loginThroughUi(page);
  expect(await page.evaluate(() => document.cookie)).not.toContain(sessionCookieName);
  const stored = await page.evaluate(() =>
    JSON.stringify([localStorage, sessionStorage].map(s => Object.entries(s))));
  expect(stored).not.toMatch(TOKEN_SHAPES);           // project regex: JWT and API-key shapes
});
```

Where web storage is the declared decision, the storage assertion inverts into pinning the
declared mitigations instead — short expiry asserted with an injected clock, and the CSP shape
test above required on the same routes.

## Cache hygiene

Authenticated and Tier-3 responses opt out of caching
(`knowledge/security/browser-protections/response-cache-hygiene.md`):

```python
@pytest.mark.control
def test_authenticated_routes_set_no_store(client, token):
    for path in AUTHENTICATED_ROUTE_CLASS:
        assert "no-store" in client.get(path, headers=auth(token)).headers["Cache-Control"]
```

Where a caching proxy sits in the test stack, add the two-principal probe: request as A through
the cache, request as B, assert B never receives A's body — the cache test that catches a
missing directive the header snapshot cannot see (a layer that ignores `no-store`).

## Subresource integrity and pinned origins

Build-time checks, run as tests so removal is a failing diff:

```ts
it("control: external scripts carry integrity and come from pinned origins", () => {
  for (const tag of externalScriptAndLinkTags(builtHtml())) {
    expect(tag.attrs.integrity).toMatch(/^sha(256|384|512)-/);
    expect(tag.attrs.crossorigin).toBeDefined();
    expect(PINNED_ORIGINS).toContain(new URL(tag.attrs.src ?? tag.attrs.href).origin);
  }
});
```

The pinned-origin set is a committed constant, so a new external origin is a reviewed diff, and
an upstream file changed under an unchanged review fails closed in the browser via the hash.

## Rendering and sanitizer snapshots

The markup-renders-as-text pattern lives in
`knowledge/security/control-test-patterns-dataflow/output-rendering-markup-in-data-stays-text.md`
and applies unchanged to browser code. Add here only the sanitizer-config snapshot: each
raw-HTML sink's sanitizer configuration (allowed tags and attributes) is serialized into a
snapshot test, so a widened allowlist is a reviewed diff, not a silent policy change — the same
discipline as the event-code registry snapshot in
`knowledge/security/security-logging-detection.md#verification-tests`.
