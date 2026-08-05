# TypeScript / web security patterns

Stack-specific security for TypeScript, React, and Node. Cross-language vulnerability taxonomy and
secure defaults live in `knowledge/security/`; this file is the TS/web manifestation and the
remediation target for the SEC-TS entries in `oracles/addenda/typescript.md`. Severity tiers are in
`knowledge/shared/severity-tiers.md`.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Boundary validation with a schema {#validation} | `knowledge/typescript/security/validation.md` |
| Cross-site scripting (XSS) {#xss} | `knowledge/typescript/security/xss.md` |
| Client-side messaging and navigation targets {#messaging} | `knowledge/typescript/security/messaging.md` |
| Authentication token storage {#token-storage} | `knowledge/typescript/security/token-storage.md` |
| Credential and secret comparison {#credentials} | `knowledge/typescript/security/credentials.md` |
| Cross-site request forgery (CSRF) {#csrf} | `knowledge/typescript/security/csrf.md` |
| Secrets and environment exposure {#secrets} | `knowledge/typescript/security/secrets.md` |
| SSRF and open redirects {#ssrf} | `knowledge/typescript/security/ssrf.md` |
| Prototype pollution {#prototype-pollution} | `knowledge/typescript/security/prototype-pollution.md` |
| Regular expression denial of service (ReDoS) {#redos} | `knowledge/typescript/security/redos.md` |
| File upload handling {#file-upload} | `knowledge/typescript/security/file-upload.md` |
| Dependencies {#deps} | `knowledge/typescript/security/deps.md` |
| Node transport and headers {#node} | `knowledge/typescript/security/node.md` |
