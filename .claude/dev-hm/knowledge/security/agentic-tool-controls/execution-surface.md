# Agentic tool controls — Execution surface

Section of `knowledge/security/agentic-tool-controls.md`.


Features that do not ship code execution keep every execution sink unreachable from model output
(`knowledge/security/llm-feature-controls.md#output-handling`). Where executing model-written
code is the product — analysis sandboxes, code assistants — the interpreter runs isolated from
the service: no ambient credentials in its environment, no network egress beyond declared hosts,
CPU/memory/time ceilings, and an ephemeral filesystem discarded per session. The sandbox boundary
is itself a protective control: a test asserts egress to an undeclared host is refused from
inside the sandbox, and the sandbox environment contains none of the service's secrets.
