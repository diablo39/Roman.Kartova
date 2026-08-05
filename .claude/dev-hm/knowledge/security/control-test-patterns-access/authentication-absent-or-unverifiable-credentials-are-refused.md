# Control-test patterns: transport, authentication, session, authorization — Authentication: absent or unverifiable credentials are refused

Section of `knowledge/security/control-test-patterns-access.md`.


Our authentication layer refuses requests whose credentials do not meet our verification
policy — missing, expired, signed with a key we do not hold, or requesting an algorithm we did
not pin — before any handler logic runs.

```rust
#[tokio::test]
async fn control_request_without_token_is_refused() {
    let app = build_app(test_state());
    let res = app.oneshot(Request::get("/api/orders").body(Body::empty()).unwrap())
        .await.unwrap();
    assert_eq!(res.status(), StatusCode::UNAUTHORIZED);
}

#[tokio::test]
async fn control_expired_token_is_refused() {
    let app = build_app(test_state_with_clock(AFTER_EXPIRY)); // injected clock, no sleeps
    let res = app.oneshot(request_with(VALID_BUT_EXPIRED_TOKEN)).await.unwrap();
    assert_eq!(res.status(), StatusCode::UNAUTHORIZED);
}
```

Expiry is tested with an injected clock (`knowledge/quality/test-strategy.md`), never by
sleeping past a deadline. For token verification policy (pinned algorithm, required claims),
one test per rejected class: the fixture mints a token violating exactly one rule and asserts
refusal — this keeps the verification call's options pinned by behavior, not by code comment.
