# Secrets and key management — One access path

Section of `knowledge/security/secrets-and-keys.md`.


Supports SEC-023. One configuration module is the only place that reads the environment or calls
the secret store; everything else receives typed values by injection. This gives review a single
surface, gives redaction a single choke point, and turns "where could a secret enter the code?"
into a one-file question.

```python
# fail: business logic reads the environment directly — unvalidated, unredactable, untestable
stripe.api_key = os.environ["STRIPE_KEY"]

# pass: one settings module, validated at startup, secret-typed fields
class Settings(BaseSettings):
    stripe_key: SecretStr          # repr/str prints '**********'
    database_url: SecretStr
settings = Settings()              # raises at boot when a required secret is missing
```

The startup behavior is itself a control: a missing required secret refuses to boot rather than
continuing with an empty string or a development default. Fail closed at process start, where the
operator is watching, not at first use in production traffic.
