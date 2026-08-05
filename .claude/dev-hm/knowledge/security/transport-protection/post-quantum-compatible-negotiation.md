# Transport protection — Post-quantum compatible negotiation

Section of `knowledge/security/transport-protection.md`.


Hybrid post-quantum key exchange is negotiated at the library and platform layer; the deployment
state and the algorithms involved are pinned in `knowledge/shared/versions.md` (networking
product-state pins). The control our code provides is, again, restraint:

- TLS named-group configuration stays at library defaults unless there is a recorded reason.
  Defaults on current runtimes negotiate hybrid key exchange when both ends support it.
- No pinned classical-only key-exchange lists: an explicit group list copied from an older
  hardening guide silently excludes the hybrid groups and freezes the connection out of the
  post-quantum transition.

Design depth — which data needs quantum-resistant protection first, signature readiness, and
crypto agility — is in `knowledge/security/cryptography-lifecycle.md#post-quantum`. The
verification here is a configuration-fixture check: no named-group pin in the diff excludes the
platform's hybrid defaults, or the configuration is absent and defaults apply.
