# TypeScript / web security patterns — Cross-site scripting (XSS) {#xss}

Section of `knowledge/typescript/security.md`.


React escapes text in JSX by default; the escape hatches are where XSS enters.

- `dangerouslySetInnerHTML` with anything but a constant or sanitizer output is an injection sink.
  Sanitize with a maintained library (DOMPurify) and prefer rendering as text.
- `href`/`src`/`formAction` built from user input can carry `javascript:` URLs — allowlist the
  scheme (`https:`, `mailto:`, relative) before rendering.
- Passing user input to `eval`, `new Function`, `setTimeout(string)`, or
  `element.innerHTML`/`document.write` outside React is a sink regardless of framework.
- On the server, set a Content-Security-Policy that forbids inline script as defence in depth; it
  limits the blast radius of any sink that slips through.

```tsx
// Sink: renders attacker HTML/script
<div dangerouslySetInnerHTML={{ __html: comment.body }} />
// Fixed: sanitize, or render as text
import DOMPurify from "dompurify";
<div dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(comment.body) }} />
<div>{comment.body}</div>
```
