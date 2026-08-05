# Accessibility — Labels

Section of `knowledge/quality/accessibility.md`.


The control: every interactive element and every meaning-bearing image has an accessible name
that says what the thing is or does. This is the most automatable control in the file and the
most common failure found in the wild.

Web:

- Form fields get a programmatic label: a `label` element (preferred — it also enlarges the
  click target), or `aria-label`/`aria-labelledby` where visible text exists elsewhere.
  Placeholder text is not a label; it vanishes on input.
- Icon-only buttons get `aria-label`. A button whose name is its glyph announces as "button".
- Images: meaning-bearing images get `alt` text describing their meaning in context; decorative
  images get `alt=""` so they are skipped. A missing `alt` is worse than an empty one — the
  screen reader falls back to reading the file name.
- Link text names the destination; "click here" and "read more" are indistinguishable in the
  rollup screen-reader users navigate by.
- Error messages are programmatically associated with their field (`aria-describedby`) and do
  not rely on color alone to mark the invalid input.

Flutter:

- Text-bearing widgets get names for free. Icon-only controls need one: `IconButton(tooltip:)`
  doubles as the semantic label; otherwise wrap in `Semantics(label:)`.
- `Image(semanticLabel:)` for meaning-bearing images; `excludeFromSemantics: true` for
  decorative ones.
- Text fields use `InputDecoration(labelText:)` — a floating label that persists — rather than
  `hintText` alone, which is the placeholder problem again.

Automation flags missing names; only the manual pass judges whether a name is right — "Submit"
on three different buttons passes every scanner and helps nobody.
