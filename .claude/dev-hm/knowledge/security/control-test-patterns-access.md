# Control-test patterns: transport, authentication, session, authorization

Runnable patterns for the access-control families. Each section names the control our code
provides, then shows a test asserting the protective behavior holds — always exercised through
the layer that enforces the control, phrased as behavior of our own code. The mandate, marker
convention, and fails-when-removed spot-check are defined in
`knowledge/security/control-verification-tests.md`; control design depth lives in
`knowledge/security/authorization-design.md`, `knowledge/security/authentication-sessions.md`,
and `knowledge/security/transport-protection.md`. Data-flow families are in
`knowledge/security/control-test-patterns-dataflow.md`; browser families in
`knowledge/security/control-test-patterns-browser.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Harness per ecosystem | `knowledge/security/control-test-patterns-access/harness-per-ecosystem.md` |
| Authorization: the wrong role is refused | `knowledge/security/control-test-patterns-access/authorization-the-wrong-role-is-refused.md` |
| Authorization: another principal's object is refused | `knowledge/security/control-test-patterns-access/authorization-another-principals-object-is-refused.md` |
| Authentication: absent or unverifiable credentials are refused | `knowledge/security/control-test-patterns-access/authentication-absent-or-unverifiable-credentials-are-refused.md` |
| Session: a fresh identifier is issued after authentication | `knowledge/security/control-test-patterns-access/session-a-fresh-identifier-is-issued-after-authentication.md` |
| Session and tokens: revocation is honored server-side | `knowledge/security/control-test-patterns-access/session-and-tokens-revocation-is-honored-server-side.md` |
| Transport: an unverifiable peer is a refused connection | `knowledge/security/control-test-patterns-access/transport-an-unverifiable-peer-is-a-refused-connection.md` |
| Transport: no fallback below the verified channel | `knowledge/security/control-test-patterns-access/transport-no-fallback-below-the-verified-channel.md` |
| Keeping fixtures honest | `knowledge/security/control-test-patterns-access/keeping-fixtures-honest.md` |
