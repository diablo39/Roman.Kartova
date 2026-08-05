# Control-test patterns: transport, authentication, session, authorization — Keeping fixtures honest

Section of `knowledge/security/control-test-patterns-access.md`.


The refusal tests above depend on the client staying at verifying defaults, so the positive-path
tests must never be "fixed" by switching verification off. The fixture generates its own CA and
issues the test server's certificate from it; the allow-path test trusts that CA explicitly
through the client's trust-store configuration (a constructor parameter, not a global toggle),
while the refusal test simply omits the trust. Certificate-generation helpers exist per stack
(for example trustme for Python, rcgen for Rust, the dev-certificate tooling in .NET); a
verification-disabled flag anywhere in test-adjacent code is the failure mode the crypto table
in `knowledge/security/secure-coding-review.md` flags at S0.
