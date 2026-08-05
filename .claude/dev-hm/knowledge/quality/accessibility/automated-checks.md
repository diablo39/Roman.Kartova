# Accessibility — Automated checks

Section of `knowledge/quality/accessibility.md`.


Automation decides the deterministic subset — missing names and labels, contrast of rendered
text, missing document language, duplicate IDs, invalid ARIA, tap-target sizes. That is roughly
a third to half of the target's criteria, which is exactly why it is wired as a gate and exactly
why it is not the whole story.

| Stack | In the linter | In component tests | In end-to-end / CI |
|---|---|---|---|
| Web (TypeScript/React) | eslint-plugin-jsx-a11y as part of the standard lint run | axe-core against rendered components (jest-axe / vitest-axe); the Storybook a11y addon where stories exist | @axe-core/playwright on key pages and states; Lighthouse or pa11y-ci with a no-new-errors budget |
| Flutter | analyzer defaults | `flutter_test` accessibility guidelines via `tester.ensureSemantics()`: `textContrastGuideline`, `labeledTapTargetGuideline`, `androidTapTargetGuideline`, `iOSTapTargetGuideline` | the same guideline expectations in integration tests on real screens |

Wiring rules that keep the checks honest:

- The gate condition is zero new errors on changed UI files — the same ratchet as lint.
- Scanners judge rendered states, so run them with the interesting states reached: dialog open,
  menu expanded, validation errors showing, both themes. A scan of the initial render certifies
  the initial render.
- Suppressing a rule at a call site carries an inline justification, exactly like any other
  threshold exception; a bare suppression is a silent waiver nobody granted.
- Component-level axe runs belong next to the component's other tests so the failure arrives
  with the diff that caused it, not in a nightly report three days later.
