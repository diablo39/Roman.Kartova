# File upload handling — Verification tests

Section of `knowledge/security/file-upload-handling.md`.


Control tests per `knowledge/security/control-verification-tests.md`, with the
fails-when-removed lever named. Harness table:
`knowledge/security/control-test-patterns-access.md#harness-per-ecosystem`.

- Size gate: an upload one byte past the configured cap is refused with the limit status and no
  stored object; one at the cap succeeds. Lever: raising the cap in a scratch run flips the
  just-outside probe.
- Type agreement: a file whose content bytes do not match its extension and declared type is
  refused, and nothing is stored — the probe is an allowlisted extension over non-matching
  content. Lever: skipping the magic-byte check in a scratch run makes this pass the gate and
  the test fail.
- Re-encode inertness: an image carrying a marker string in a metadata segment and trailing
  bytes is accepted, and the stored object contains neither marker — decode–re-encode removed
  them. Lever: switching to store-original makes the marker assertion fail.
- Filename as display data: an upload named with traversal segments and markup characters is
  stored under a generated name (assert the stored path is inside the base and contains no
  client bytes), and the original name renders encoded in listings (the encoding pattern in
  `knowledge/security/control-test-patterns-dataflow.md`).
- Authorization both directions: principal A cannot attach to B's resource, and cannot fetch
  B's object — refusal shape plus no side effect, per the ownership pattern in
  `knowledge/security/control-test-patterns-access.md`. Where signed URLs are used, an expired
  and a tampered URL are both refused.
- Active-content rule: an SVG containing a script element is refused (default posture) or, in
  the sanitize carve-out, the stored result contains no script and the serving response carries
  the sandboxing headers — assert the headers, not just the body.
- Archive containment and bounds: an archive with a traversal entry is refused with zero files
  written outside the root (assert the outside path does not exist); an archive one entry or
  one byte past each cap is refused mid-extraction with partial output cleaned up.
- Download headers: a header snapshot on the download route class asserts the stored
  content type, `nosniff`, and the disposition policy, so header regressions are failing diffs.

Every refusal above emits its `upload_validation` event with actor and reason — asserted in the
same tests, per `knowledge/security/security-logging-detection.md#verification-tests`.
