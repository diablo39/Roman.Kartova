# Secure code review by vulnerability class — Cross-origin and request forgery

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-046, SEC-047. For CSRF: any new state-changing endpoint authenticated by cookies
needs the framework's CSRF token mechanism, or SameSite=Lax/Strict cookies plus a guarantee that
safe methods (GET/HEAD) never mutate state. Token-in-header APIs (Bearer) are structurally
immune — record that as the n/a reason. For CORS, the failing pattern is reflection:

```js
// fail SEC-047: reflects any origin with credentials
res.setHeader("Access-Control-Allow-Origin", req.headers.origin);
res.setHeader("Access-Control-Allow-Credentials", "true");
// pass: explicit origin list
const allowed = new Set(["https://app.example.com"]);
if (allowed.has(req.headers.origin)) { /* set headers */ }
```

`Access-Control-Allow-Origin: *` with credentials is the same failure; browsers reject the
combination but permissive middleware often emulates it via reflection.
