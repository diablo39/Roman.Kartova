# Browser protections — Response cache hygiene

Section of `knowledge/security/browser-protections.md`.


Responses carrying authenticated content or Tier-3 data
(`knowledge/security/data-classification.md#tiers`) declare that no cache may keep a copy:
`Cache-Control: no-store`. Without the directive, a shared proxy or CDN can serve one user's
response to another, and browser caches retain readable copies on shared machines after logout.
The split is by content, not by effort: authenticated and Tier-3 responses opt out of caching;
genuinely public, static responses stay aggressively cacheable as a deliberate opt-in. Set the
directive where the authentication layer lives — middleware or gateway — so every new
authenticated route inherits it instead of each handler remembering it; the handoff names the
owning layer.

Verification tests: the `no-store` route-class assertion and the two-principal cache probe in
`knowledge/security/control-test-patterns-browser.md#cache-hygiene`.
