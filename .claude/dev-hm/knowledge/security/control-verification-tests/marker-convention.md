# Control-verification tests — Marker convention

Section of `knowledge/security/control-verification-tests.md`.


Durability checks need to know which tests are control tests, so every new control test carries
the project's control-test marker. One convention per repository — stated where the repository
documents testing. The convention is required, not optional, once a control test exists: it is
established in the first diff that ships one, because a marker that stays a proposal leaves the
durability check unable to see what it protects. For control tests that predate the marker, the
tests named in prior gate records' control-test maps are the protected set the durability check
defends. Idiomatic choices:

| Stack | Marker |
|---|---|
| Python | `@pytest.mark.control`, registered in the project's pytest config |
| TypeScript | file suffix `*.control.test.ts`, or a `control:` prefix in the describe block |
| Java | JUnit `@Tag("control")` |
| C# | MSTest `[TestCategory("Control")]` |
| Rust | tests in a `control` module or named with a `control_` prefix |
| C++ | GoogleTest: `Control` prefix on the test-suite name, e.g. `TEST(ControlAuthz, ...)`; Catch2 (secondary): tag `[control]` |
| Flutter / Dart | `tags: ['control']`, registered in `dart_test.yaml` |

The marker makes the set mechanically listable (`pytest -m control --collect-only -q`,
`--gtest_filter=Control* --gtest_list_tests`, `cargo test control_ -- --list`, name or tag
filters in the other runners), which is how a gate compares
the control-test set before and after a change without reading every test file.
