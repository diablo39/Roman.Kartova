# Secure code review by vulnerability class — Output encoding and XSS

Section of `knowledge/security/secure-coding-review.md`.


Supports SEC-003, SEC-043. XSS is CWE-79, ranked #1 in the CWE Top 25 2025. Framework
auto-escaping is the control; the review looks for the places it is switched off.

```tsx
// fail SEC-003: raw-HTML sink with external input
<div dangerouslySetInnerHTML={{ __html: comment.body }} />
// pass: default escaping, or sanitize when HTML is a requirement
<div>{comment.body}</div>
// pass: sanitizer for rich text
<div dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(comment.body) }} />
```

Grep the diff for the raw sinks of the stack (`innerHTML`, `dangerouslySetInnerHTML`, `v-html`,
`| safe`, `Html.Raw`, `mark_safe`) and for template-engine autoescape toggles. Encoding is
context-specific: a value safe in HTML text is not safe in an attribute, URL, or inline script.
For SEC-043, check non-HTML downstream contexts: CR/LF stripped from response-header values,
`= + - @` prefixes escaped in CSV exports, correct quoting when writing into JSON or YAML.
