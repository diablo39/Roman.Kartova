# Deployment hardening — IaC discipline

Section of `knowledge/security/deployment-hardening.md`.


- Reviewed like code, scanned like code: policy-as-code checks (misconfiguration scanners) run
  in CI over manifests and IaC with a recorded output, same evidence rule as SEC-062.
- Plan output is part of the diff review for infrastructure changes: what will change is
  reviewed, not inferred from the source delta.
- State files and plan artifacts contain resolved secret values and are treated as
  secret-bearing: stored encrypted, access-controlled, never committed (SEC-022).
- Drift between declared and running configuration is a finding — an undeclared runtime change
  is either an incident or a bypass, and both are reportable.
