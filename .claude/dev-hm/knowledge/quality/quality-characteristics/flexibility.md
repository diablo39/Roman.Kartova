# Quality characteristics in review (ISO/IEC 25010:2023) — Flexibility

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics (2023): adaptability, installability, replaceability, scalability. Review
questions: does the change hardcode environment specifics (paths, hosts, regions) that belong in
config (QUA-051 documents them)? Can the component be deployed/scaled without code edits? Would
swapping the underlying engine (database, broker) stay contained behind the existing boundary
(QUA-020, QUA-026)? Scalability claims follow QUA-073 — measured or labeled as expectations.
