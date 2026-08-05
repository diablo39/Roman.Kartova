# Deployment hardening — Identity and network least privilege

Section of `knowledge/security/deployment-hardening.md`.


- One service account per workload, granted the verbs it uses and nothing more; the default
  account stays permissionless. API-server token automounting is off unless the workload calls
  the API, and that need is declared.
- Network deny-by-default: an ingress-and-egress default-deny policy per namespace, with named
  allowances per flow — the deployment counterpart of SEC-162. Egress allowances are the
  enforcement backstop for outbound-URL controls (SEC-090): a worker that can only reach its
  declared destinations cannot be steered elsewhere even by a validation bug.
- Cloud IAM roles attached to workloads follow the same shape: scoped to the resources the
  service touches, no wildcard actions, no account-wide grants for a single bucket's sake.
  IaC diffs that widen a grant are security-relevant lines, reviewed as such.
