# Email and notification safety — HTML email encoding

Section of `knowledge/security/email-notification-safety.md`.


An HTML email body is an HTML rendering context, reviewed exactly like a page (SEC-003):
template auto-escaping on, every interpolated value encoded for its context, no raw-HTML
insertion of external input. Two things make email stricter than the browser surface:

- No script, no CSP: mail clients vary wildly in what they execute or block, and none of our
  browser-side protections (`knowledge/security/browser-protections.md`) travel with the
  message. The template is fully static structure plus encoded data — nothing dynamic enough to
  need a sanitizer is allowed into a mail template.
- User-to-user delivery: notifications routinely render one user's input (a comment, a display
  name) into another user's mailbox. That is stored XSS with delivery, and the encoding test
  below treats it as such.

Every HTML message carries a `text/plain` alternative built from the same data — plain text is
both an accessibility and a safety floor. URLs interpolated into `href` positions are validated
to `https` before rendering, so a stored value cannot become a script-scheme or download link.
