# Quality characteristics in review (ISO/IEC 25010:2023) — Security

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics: confidentiality, integrity, non-repudiation, accountability, authenticity,
resistance. Enforced by the security oracle (`oracles/security-oracle.md`) and gated by
senior-security-engineer; the quality gate does not duplicate it. The one overlap kept on the
quality side is operational: QUA-063 (generated files not hand-edited) protects integrity of
build inputs.
