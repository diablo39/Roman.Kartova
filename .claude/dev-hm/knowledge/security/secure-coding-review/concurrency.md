# Secure code review by vulnerability class — Concurrency

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-110 – SEC-112. Map every shared mutable datum the diff touches to exactly one
guarding discipline — one named lock, atomics, or channel/actor confinement; mixed
guarded/unguarded access to the same field fails SEC-110. Check-then-act must be atomic
(SEC-111): exclusive-create file APIs instead of exists-then-open (TOCTOU, CWE-367),
compare-and-set instead of read-then-write, unique constraints instead of check-then-insert.
When the diff acquires more than one lock, the order is documented and identical at every call
site (SEC-112). Data races are security bugs, not just correctness bugs: a race on an
authorization cache or a balance check is an exploit primitive.
