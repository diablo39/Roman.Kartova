# Cryptography lifecycle — In transit

Section of `knowledge/security/cryptography-lifecycle.md`.


Transport policy — protocol floor, cipher policy, certificate and hostname verification, peer
identity on internal calls, refusal on verification failure — lives in
`knowledge/security/transport-protection.md`. The lifecycle contribution here is agility and
effectiveness: TLS settings are configuration-as-code reviewed like code, and an integration test
asserts the effective negotiated state (protocol at or above the floor, strong cipher, verified
peer), not merely the declared one, so a dependency upgrade or platform change that silently
weakens defaults fails the test instead of shipping.
