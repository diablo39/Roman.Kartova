# Browser protections — Subresource integrity

Section of `knowledge/security/browser-protections.md`.


Script we execute is script we reviewed. First preference is bundling and self-hosting
third-party code through the locked dependency chain (`knowledge/security/supply-chain.md`).
Where a script or stylesheet must load from a third-party origin, the tag carries an
`integrity` hash and `crossorigin` attribute, and the URL is version-pinned — never a
"latest" channel, which would change content under an unchanged review. A changed upstream
file then fails closed: the browser refuses to execute what no longer matches the hash.

Verification tests: the integrity-attribute build check and pinned-origin snapshot in
`knowledge/security/control-test-patterns-browser.md#subresource-integrity-and-pinned-origins`.

Every control above is a protective control in the sense of
`knowledge/security/control-verification-tests.md`: its test must fail when the control is
removed, and refusals — CSP violation reports, request-forgery rejections — emit their security
event (`knowledge/security/security-logging-detection.md#event-codes`).
