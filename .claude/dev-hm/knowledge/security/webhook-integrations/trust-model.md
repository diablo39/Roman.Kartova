# Webhook integrations — Trust model

Section of `knowledge/security/webhook-integrations.md`.


A webhook receiver cannot use session authentication — the caller is a machine with a shared
contract, not a logged-in principal. Its authentication is the signature; everything else about
the endpoint is deny-by-default hardening. The receiver's registration outside the session
middleware is an explicit, declared opt-out (SEC-162), and the handoff names the signature
control as the compensating authentication.
