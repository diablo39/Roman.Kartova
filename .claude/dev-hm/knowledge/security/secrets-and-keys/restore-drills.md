# Secrets and key management — Restore drills

Section of `knowledge/security/secrets-and-keys.md`.


A backup that has never been restored is a hypothesis. On a schedule — and after any change to
key topology, KMS configuration, or escrow custody — a drill restores the encrypted backup into
an isolated environment and proves the whole chain:

1. Reconstruct key access the way the disaster would require it: the replica region's key, or
   the escrow quorum — with the people and permissions the runbook names, not an admin
   shortcut.
2. Restore the data backup and decrypt a known sample; verify key IDs referenced by the
   ciphertext resolve (`#key-lifecycle` — the `kid` on every blob is what makes this checkable).
3. Record the drill output — what was restored, which key versions served, time taken — as the
   evidence a gate can verify, same standard as any control-verification record.

The drill is also where retired-key handling proves itself: a backup written before the last
rotation restores only if retired key versions remain available for decrypt-only use. A failed
drill is a finding at the severity of the data it strands.
