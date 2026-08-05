# ASP.NET Core security wiring

Defensive configuration of ASP.NET Core's built-in security machinery. The controls themselves —
what to enforce and how to verify it — are the `knowledge/security/` cluster; this file maps those
controls onto ASP.NET Core APIs and flags the framework-specific misconfigurations. Language-level
rules (parameterization, crypto, secrets literals) are `knowledge/csharp/review-checklist.md`
and the SEC-CS-* oracle entries.

## Authentication wiring {#authentication}

Token-authenticated APIs use the JWT bearer handler with validation pinned explicitly
(`knowledge/security/authentication-sessions/token-lifecycle.md` for the rules):

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];   // keys via OIDC discovery
        o.TokenValidationParameters = new()
        {
            ValidAudience = builder.Configuration["Auth:Audience"],
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
        };
    });
```

- Audience and issuer validation stay on; a token minted for another API must not work here.
  Never accept the `alg` the token claims — the handler pins algorithms from the discovered keys.
- Browser-facing apps: cookie authentication (or OIDC code flow via the OpenIdConnect handler with
  a BFF) with `HttpOnly`, `Secure`, and an appropriate `SameSite` on the auth cookie; session
  lifecycle rules per `knowledge/security/authentication-sessions/session-lifecycle.md`.
- Middleware order is load-bearing: `UseAuthentication()` before `UseAuthorization()`, both before
  the endpoints they protect.
- Password storage, when the service owns credentials at all, follows
  `knowledge/csharp/review-checklist/crypto.md` (argon2id/scrypt; Identity's hasher only for
  existing Identity stores).

## Authorization: deny by default {#authorization}

Make protection opt-out, not opt-in, so the next endpoint added without an attribute is closed
(`knowledge/security/authorization-design/deny-by-default.md`):

```csharp
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()      // applies where nothing else is stated
        .RequireAuthenticatedUser()
        .Build();
});
```

- Public endpoints declare themselves: `[AllowAnonymous]` / `.AllowAnonymous()` — a reviewable,
  greppable exemption list. An `[AllowAnonymous]` on a controller silences every `[Authorize]`
  below it; keep exemptions on the smallest scope.
- Role/permission checks are named policies (`AddPolicy` with `RequireRole`/`RequireClaim` or
  requirement handlers), applied via `[Authorize(Policy = ...)]` or
  `.RequireAuthorization("policy")` on minimal-API groups.
- Object-level ownership cannot be expressed by an attribute: resolve the resource, then call
  `IAuthorizationService.AuthorizeAsync(user, resource, policy)` in the handler — the
  fetch-then-check pattern of `knowledge/security/authorization-design/object-level-ownership.md`;
  return 404 (not 403) where existence itself is sensitive.
- Every protected endpoint carries 401/403/wrong-owner tests against real tokens from a
  containerized identity provider — `knowledge/csharp/aspnet-integration-testing/auth.md`.

## Data Protection {#data-protection}

The Data Protection stack encrypts the framework's own artifacts — auth cookies, antiforgery
tokens, TempData, password-reset tokens. Defaults work on one machine; multi-instance deployments
must configure the key ring or every deploy/instance-switch silently invalidates cookies and
tokens:

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("orders")                             // stable across instances & deploys
    .PersistKeysToAzureBlobStorage(blobUri)                   // shared, durable key ring
    .ProtectKeysWithAzureKeyVault(keyUri, credential);        // keys encrypted at rest
```

Rules: persist the ring to shared durable storage; protect it at rest (Key Vault/KMS/DPAPI) —
a plaintext key ring on disk is a secrets finding; same `SetApplicationName` for apps that must
read each other's cookies, different names to isolate. Keys auto-rotate (90-day default) with
old keys retained for decryption — key lifecycle per `knowledge/security/secrets-and-keys/key-lifecycle.md`.
Data Protection is for these framework payloads, not general document encryption
(`knowledge/security/cryptography-lifecycle.md` for that).

## Rate limiting and resource protection {#rate-limiting}

The built-in middleware implements the quotas of
`knowledge/security/resource-protection/per-principal-fairness.md`:

```csharp
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-user", ctx => RateLimitPartition.GetTokenBucketLimiter(
        ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress!.ToString(),
        _ => new TokenBucketRateLimiterOptions { TokenLimit = 100, /* refill options */ }));
});
app.UseRateLimiter();
apiGroup.RequireRateLimiting("per-user");
```

- Partition by authenticated principal first, IP only as the anonymous fallback (IPs are shared
  and spoofable behind proxies).
- Limiter choice: token bucket for sustained-rate-with-burst (default pick), sliding window for
  smooth quotas, concurrency limiter for expensive endpoints where in-flight count is the real
  resource; fixed window only where its boundary burst is acceptable.
- Queueing (`QueueLimit > 0`) trades latency for rejections — keep queues at 0 for auth endpoints
  and other abuse targets so probing gets a fast 429.
- Body/size bounds complement rates: Kestrel `MaxRequestBodySize`, form limits, and
  `RequestSizeLimit` per endpoint (`knowledge/security/resource-protection/input-size-limits.md`);
  timeouts via the request-timeouts middleware
  (`knowledge/security/resource-protection/timeouts.md`).

## Transport, proxies, and browser-facing headers {#transport}

- `UseHsts()` and `UseHttpsRedirection()` in every internet-facing profile; TLS floor policy and
  cert validation rules are `knowledge/security/transport-protection.md` (and never a
  `ServerCertificateCustomValidationCallback` that returns true — SEC-CS-006).
- Behind a proxy/load balancer, configure `ForwardedHeadersOptions` with explicit `KnownProxies`/
  `KnownNetworks`. Trusting `X-Forwarded-For`/`-Proto` from anywhere lets clients spoof their IP
  (defeating the rate limiter's partitions) and scheme.
- Browser responses: CSP, `X-Content-Type-Options`, frame-ancestors, and cache hygiene per
  `knowledge/security/browser-protections.md` — set once in middleware, not per controller.
- CORS: explicit origin allowlist; never combine `AllowAnyOrigin` with credentials. CORS is not an
  authorization mechanism — it only governs what browsers will read.

## Antiforgery {#antiforgery}

Cookie-authenticated state-changing requests need antiforgery
(`knowledge/security/browser-protections/request-forgery-defenses.md`). Razor form helpers emit
tokens automatically; minimal APIs validate form posts when antiforgery services and
`UseAntiforgery()` are registered. Token-authenticated APIs (no auth cookie) are not CSRF-forgeable
and do not need it — do not weaken cookie `SameSite` to make cross-site posting work.

## Secrets and configuration {#secrets}

Secrets reach the app through configuration providers only: user-secrets in development,
environment/managed vault (Key Vault + managed identity or equivalent) in deployment — never
`appsettings.json` in the repo (SEC-CS-002). Bind through the options pattern with
`ValidateOnStart` so a missing secret fails deployment, not the first request
(`knowledge/csharp/platform.md#options-pattern-ioptions-family`); one access path, secret-typed
fields, redaction rules per `knowledge/security/secrets-and-keys/one-access-path.md`. Error
responses use ProblemDetails without stack traces or internals
(`knowledge/csharp/review-checklist/error-handling.md`); the developer exception page stays
development-only.
