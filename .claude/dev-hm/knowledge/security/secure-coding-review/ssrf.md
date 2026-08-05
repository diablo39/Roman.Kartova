# Secure code review by vulnerability class — SSRF

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-090, SEC-091. When a server-side request's target derives from external input,
validate scheme and host against an allowlist and block link-local/metadata addresses
(169.254.169.254) and private ranges unless explicitly intended. Compare the parsed host, never
the raw string:

```python
# fail SEC-091: substring check — http://evil.com/?x=.internal.example.com passes
if ".internal.example.com" in url: fetch(url)
# pass: parse, then exact-match the host
host = urllib.parse.urlsplit(url).hostname
if host in ALLOWED_HOSTS: fetch(url)
```

Redirects re-open the question: an allowlisted host can 302 to the metadata endpoint, so
disable auto-redirects or re-validate each hop (SEC-090). DNS names that resolve to private
addresses defeat host allowlists; where the risk warrants it, resolve then check the IP.
