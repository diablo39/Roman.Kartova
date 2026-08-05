# Accessibility — Focus management

Section of `knowledge/quality/accessibility.md`.


Where focus goes when the UI changes is designed behavior, not whatever the framework happened
to do.

- Opening a dialog or drawer moves focus into it (the first sensible control, or the container
  itself); focus cycles inside while it is open; closing returns focus to the element that
  opened it.
- Route changes in a single-page app move focus to the new view's heading or container and make
  the change perceivable — screen readers do not detect virtual navigation on their own, so the
  new context is announced (a focus target with an accessible name, or a live region).
- Removing the focused element never drops focus to the document body: after deleting a list
  item, focus lands on the next item or the list's container.
- Focus is not obscured: sticky headers, footers, and overlays must not cover the focused
  element — scroll offsets account for them.
- Async updates that matter are announced through a polite live region (spinners resolving,
  validation results, background saves); silent success is indistinguishable from silent failure.

Flutter: standard routes and dialogs manage focus correctly; custom overlays own the job —
manage a `FocusNode` for entry and restore, and announce context changes through the semantics
API.
