# C# review checklist — Secrets {#secrets}

Section of `knowledge/csharp/review-checklist.md`.


Secrets come from configuration providers (environment, Key Vault, user-secrets in dev), never from
string literals or checked-in `appsettings.json`. A literal that looks like a key, token, password,
or connection string with credentials is S0. See `knowledge/security/secure-coding-review.md`.
