# Transport protection — Certificate and hostname validation

Section of `knowledge/security/transport-protection.md`.


Our transport clients verify both halves of server identity on every connection: the certificate
chains to a root we trust, and the presented identity matches the host we intended to reach.
Both checks stay enabled everywhere (SEC-031 holds this at S0); a chain check without a hostname
check accepts any certificate our trust store ever issued, which is not verification.

- Public peers use the platform trust store. Internal CAs are trusted by adding the CA to the
  client's trust configuration — a constructor-scoped bundle or SSL context parameter — never by
  switching verification off. The difference is the control surviving: explicit trust still
  refuses every other unverifiable peer.
- Test helpers that relax verification must not be importable from production code. The test
  fixtures generate their own CA and trust it explicitly, so even tests run with verification on
  (`knowledge/security/control-test-patterns-access.md` shows the fixture shape).
- An expiring certificate is an operational problem with an operational fix — rotation, ideally
  automated. A verification-disabled flag as the mitigation converts a scheduled outage into a
  permanent identity blind spot.
- Per-leaf certificate pinning is a last resort: it couples deploys to certificate rotation and
  fails closed at the worst time. Prefer trust anchoring on a private CA; pin only with a
  recorded rotation story and a backup pin.

```csharp
// fail: chain accepted regardless — hostname and chain checks both gone
handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

// pass: defaults verify chain and hostname; internal CA added to the trust store explicitly
var handler = new SocketsHttpHandler();
handler.SslOptions.CertificateChainPolicy = policyTrustingInternalCa;
```

The control test starts a local TLS endpoint whose certificate the client's trust store does not
include, connects our production client code, and asserts a verification error with zero
requests received — the fails-when-removed tripwire for any disable flag introduced later.
