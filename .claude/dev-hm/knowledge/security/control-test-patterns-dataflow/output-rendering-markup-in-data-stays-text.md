# Control-test patterns: parameterized statements, encoding, parsing, telemetry, limits — Output rendering: markup in data stays text

Section of `knowledge/security/control-test-patterns-dataflow.md`.


Our rendering layer context-encodes values by default, so data containing markup renders as
visible text — no element, attribute, or template expression is created from a value. Unit
level: render our component or template with a markup probe and assert the output treats it as
text. The template-expression probes (`{{7*7}}`, `${7*7}`) must come out literally, never as 49.

```tsx
it("control: markup in a display name renders as text", () => {
  render(<ProfileCard name="<b>probe</b>" />);
  expect(screen.getByText("<b>probe</b>")).toBeInTheDocument(); // visible as literal text
  expect(document.querySelector("b")).toBeNull();               // no element was created
});
```

```python
def test_markup_in_data_renders_as_text(render_template):
    html = render_template("profile.html", name="<b>probe</b>")
    assert "&lt;b&gt;probe&lt;/b&gt;" in html
    assert "<b>probe</b>" not in html            # nothing from data became markup
```

| Ecosystem | Encoding default under test | Escape hatch the test guards |
|---|---|---|
| TypeScript / React | JSX text interpolation | `dangerouslySetInnerHTML` |
| Python | Jinja2/Django autoescape | the `safe` filter, `Markup()` |
| Java | Thymeleaf `th:text`, JSP `c:out` | `th:utext`, unescaped EL |
| C# | Razor `@` expression | `Html.Raw` |
| Rust | askama/maud auto-escaping | raw or pre-escaped markers |
| C++ | the project's encoder applied at every markup sink | raw concatenation into markup |
| Flutter / Dart | `Text` renders data as glyphs, never markup | feeding data to an HTML-rendering or WebView widget |

The tripwire property: switching one binding to the raw form makes the no-element assertion
fail. Flutter's default widget tree is inert to markup; the control point is any HTML-rendering
widget, and the test asserts our code encodes or strips markup before it receives the value.
