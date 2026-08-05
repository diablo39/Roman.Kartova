# Deployment hardening — Verification tests

Section of `knowledge/security/deployment-hardening.md`.


Deployment controls are configuration, so their tests are policy tests plus a small number of
behavioral probes — same mandate and marker convention as
`knowledge/security/control-verification-tests.md`:

- Admission refusal: in a test cluster or the policy engine's offline test harness, an unsigned
  image, an image from an unlisted registry, and a privileged pod are each refused by the
  admission layer — asserted as the deny verdict, with the allow-path fixture (a signed,
  compliant workload) accepted alongside. Lever: switching the policy to audit-only in a
  scratch run makes the deny assertions fail.
- Manifest floor: a policy test asserts every production workload manifest carries the
  restricted-profile context fields above; adding a workload without them is a failing diff.
  Lever: deleting `runAsNonRoot` from a fixture manifest must fail the test, not slide through
  a default.
- Secret absence: a repository scan asserts no secret-shaped literals in manifests or IaC
  (SEC-020), and an image-history check asserts no layer carries env files or key material.
- Egress bite: where the test environment can exercise it, a workload probe asserts a
  connection to a non-allowlisted destination fails at the network layer while the declared
  destination succeeds — the recorder pattern from
  `knowledge/security/control-test-patterns-access.md` pointed at the platform control.
- Identity scope: a policy test over IaC asserts no wildcard IAM actions and no default
  service-account grants; widening a grant is a reviewed, failing diff until declared.

Record the policy-engine and scanner outputs with the change (SEC-062's evidence rule); a
deployment control asserted only in prose is an unverified control, reported per
`knowledge/security/control-verification-tests.md#mandate`.
