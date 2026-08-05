# Accessibility — Semantic structure

Section of `knowledge/quality/accessibility.md`.


The structure assistive technology navigates by is the one our markup or widget tree declares,
not the one the pixels suggest.

Web:

- Native elements first: `button`, `a`, `nav`, `main`, `label`, `table`, `select` carry their
  role, state, and keyboard behavior for free. ARIA is for what native elements cannot express;
  a `div` with `role="button"` and hand-wired keys is a rebuild of something the browser ships.
- Heading levels form an outline without skips; screen-reader users navigate by it. One `h1` per
  page, sections introduced by the next level down.
- Landmarks (`header`, `nav`, `main`, `footer`, or their ARIA equivalents) partition the page so
  "skip to content" is a jump, not a scroll.
- DOM order matches reading order. CSS that reorders visually (`order`, positioned layouts)
  leaves the underlying sequence — and therefore the screen-reader and tab experience — behind.
- Data tables use `th` with `scope`; layout does not use tables. Lists of things are `ul`/`ol`.
- The document declares its language (`lang`), and so do inline foreign-language passages.

Flutter: the widget tree generates the semantics tree. Standard Material and Cupertino widgets
carry correct semantics; custom-painted or gesture-driven widgets declare theirs with `Semantics`
explicitly. `MergeSemantics` groups a composite (icon plus label) into one announcement;
`ExcludeSemantics` removes purely decorative subtrees from the traversal.
