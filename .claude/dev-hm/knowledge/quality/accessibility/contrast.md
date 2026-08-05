# Accessibility — Contrast

Section of `knowledge/quality/accessibility.md`.


- Text contrasts with its background at 4.5:1 or better (3:1 for large text). This is measured
  on the rendered result — text over images and gradients is judged at its worst point.
- Non-text essentials — icons that carry meaning, input borders, focus indicators, chart series
  that must be told apart — need 3:1 against their adjacent colors.
- Color is never the only carrier of meaning: pair it with an icon, text, or pattern. Red/green
  status dots alone exclude the most common color-vision deficiencies and every grayscale print.
- Put the guarantee in the design tokens: publish foreground/background pairs that are validated
  once, so screen-level checks reduce to "used a sanctioned pair". Both themes are validated —
  a pair that passes in light mode can fail in dark mode, and each theme is checked as rendered.
