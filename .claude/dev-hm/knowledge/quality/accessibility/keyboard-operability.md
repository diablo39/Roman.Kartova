# Accessibility — Keyboard operability

Section of `knowledge/quality/accessibility.md`.


Everything operable by pointer is operable by keyboard, in an order that makes sense.

- Tab order follows the visual and logical order. No positive `tabindex` values — they create a
  parallel order that must be maintained forever; `tabindex="0"` joins the natural order and
  `tabindex="-1"` enables programmatic focus, and those two are all that is needed.
- No keyboard traps: anything focus can enter, focus can leave. Dialogs close on Escape.
- Custom widgets implement the keyboard pattern users expect from the native equivalent (the
  ARIA Authoring Practices patterns): arrow keys move within menus, tab lists, and grids;
  Enter/Space activates; Home/End jump. If implementing that sounds expensive, that is the
  argument for the native element.
- The focus indicator stays visible. Removing the outline without an equally visible replacement
  disables navigation for sighted keyboard users; style it, never suppress it.
- Pointer targets meet the WCAG minimum target size, and the platform conventions above it
  (44 pt on iOS, 48 dp on Android and Material) where the stack sets them.

Flutter: keyboard support on desktop and web comes from `Shortcuts` and `Actions`, with
`FocusTraversalGroup` ordering traversal; Material widgets meet the tap-target floor by default,
and the guideline tests below verify it stays met.
