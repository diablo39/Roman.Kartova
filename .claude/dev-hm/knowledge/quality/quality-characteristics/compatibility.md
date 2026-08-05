# Quality characteristics in review (ISO/IEC 25010:2023) — Compatibility

Section of `knowledge/quality/quality-characteristics.md`.


Sub-characteristics: co-existence, interoperability. Review questions: does the change alter a
wire contract, schema, or file format consumed by others? Is the change backward compatible, or
is the break versioned and coordinated? Do error responses keep their documented shape
(QUA-034)? Contract tests (see `knowledge/quality/test-strategy.md#what-to-test-at-which-level`)
are the enforcement mechanism — QUA-017 requires a contract change to update its test in the
same diff (`knowledge/quality/test-adequacy.md#contract-tests`); a silently changed contract is
an S1 finding even when all local tests pass. Schema changes coexist with the running version
per QUA-090 (`knowledge/quality/data-migration-safety.md#expand-contract`). The breaking-change
taxonomy, versioning and deprecation policy, and N-1 client testing (QUA-113) live in
`knowledge/quality/api-compatibility.md`.
