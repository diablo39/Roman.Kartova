# Browser protections — Safe rendering

Section of `knowledge/security/browser-protections.md`.


Every value our templates and components interpolate into HTML is encoded for its rendering
context by default: the framework's auto-escaping stays on, globally, and is never disabled to
"fix" display of a value. Raw-HTML sinks (`innerHTML`, `dangerouslySetInnerHTML`, `v-html`,
template filters that mark strings safe) are exceptions, not tools: each use is individually
justified, fed only through a maintained HTML sanitizer, and never by string concatenation.
Values landing in URL positions (`href`, `src`, redirect targets) are validated to navigable
web schemes before rendering, so a stored string cannot become a script-scheme link. Where the
platform supports Trusted Types, enforcing `require-trusted-types-for 'script'` in the CSP
turns any unreviewed DOM-sink write into a runtime refusal — treat it as hardening on top of
encoding, not a replacement for it.

Verification tests: the encoding round-trip pattern in
`knowledge/security/control-test-patterns-dataflow.md` plus the sanitizer-config snapshot in
`knowledge/security/control-test-patterns-browser.md#rendering-and-sanitizer-snapshots`.
