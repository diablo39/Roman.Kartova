# Email and notification safety — Header injection refusal

Section of `knowledge/security/email-notification-safety.md`.


Message headers are a CR/LF-delimited downstream context (SEC-043): a line break inside a value
our code places into a header ends that header and starts another — an injected `Bcc`, a
rewritten `Subject`, an altered envelope. The control is structural: build messages through the
mail library's typed API — address objects, header setters that refuse or encode line breaks —
and never by concatenating strings into raw message source.

```python
# fail SEC-043: display name concatenated into raw headers — a CR/LF in it forges headers
msg = f"From: noreply@example.com\r\nTo: {display_name} <{addr}>\r\nSubject: {subject}\r\n\r\n{body}"

# pass: typed message API; the library encodes or refuses control characters in each field
msg = EmailMessage()
msg["To"] = Address(display_name, addr_spec=addr)   # value is one header, structurally
msg["Subject"] = subject
```

On top of the structural control: recipient addresses are validated to address shape at the
boundary (SEC-040), and any external value destined for a header is refused if it contains
CR, LF, or other control characters — a defense that stays even if someone later swaps the mail
library. Recipient lists come from our data, never parsed out of a request field that could
smuggle separators.
