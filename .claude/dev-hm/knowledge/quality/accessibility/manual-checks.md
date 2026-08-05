# Accessibility — Manual checks

Section of `knowledge/quality/accessibility.md`.


The short pass for any UI-touching diff — minutes per flow, not an audit. It covers what
automation cannot judge: meaning, order, and experience.

1. Keyboard-only walkthrough of the changed flow: reach everything, operate everything, see the
   focus indicator the whole way, escape from everything you enter.
2. Screen-reader spot check with the platform's own reader — NVDA on Windows, VoiceOver on
   macOS/iOS, TalkBack on Android: do the changed elements announce a sensible name, role, and
   state, in a sensible order?
3. Zoom and reflow: 200% zoom and a narrow viewport — nothing disappears, nothing requires
   two-dimensional scrolling to read text.
4. Meaning check: labels say what the control does, alt text says what the image means here,
   error messages say how to fix the problem.
5. Color-off check: apply a grayscale filter — every status, selection, and validation state
   must survive it.

Record the pass in the handoff like any verification run: which flow was walked, on which stack,
with which reader. Findings outside the automated subset are reported as interaction-capability
findings with severities per `knowledge/shared/severity-tiers.md`, tagged as described in
`knowledge/quality/quality-characteristics.md#using-the-characteristics-in-reports`.
