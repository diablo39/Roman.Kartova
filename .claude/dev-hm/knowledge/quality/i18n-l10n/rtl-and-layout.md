# Internationalization and localization — RTL and layout

Section of `knowledge/quality/i18n-l10n.md`.


- Write direction-agnostic layout: logical properties and start/end alignment (CSS logical
  properties, Android `start`/`end` attributes, Flutter `EdgeInsetsDirectional` and
  `Alignment*Directional`) instead of left/right. A layout authored in physical coordinates
  needs a mirrored twin; one authored logically mirrors itself.
- Mirror what means direction, not what means content: navigation chevrons and progress
  indicators flip; logos, media controls tied to timeline convention, and numerals do not.
- Isolate user-generated text embedded in UI strings (bidi isolation — FSI/PDI controls or the
  platform's isolation API): an RTL username inside an LTR sentence otherwise reorders the
  sentence visually, which is both a rendering bug and a spoofing vector for displayed
  identifiers like file names and URLs.
- Numbers, ranges, and units in RTL locales come out of the formatting APIs already correctly
  ordered — another reason hand-assembled strings fail.
- Test with a real RTL locale or the RTL pseudo-locale on the critical screens; layout
  mirroring cannot be verified from code review alone.
