# Test adequacy — Contract tests

Section of `knowledge/quality/test-adequacy.md`.


Inside one deployable, integration tests catch interface drift. At boundaries where the two
sides deploy independently — HTTP APIs, published events and messages, exported file formats,
a datastore read by another service — neither side's own suite can see a divergence, so the
wire contract needs its own pinned test. Current practice offers two complementary forms:

- Consumer-driven contracts (the Pact family): each consumer records the requests it makes
  and the response fields it actually reads; the provider replays and verifies those
  expectations in its own build. Highest fidelity, and evolution-friendly by design — the
  provider can add or reorder anything no consumer asserts on.
- Schema-based verification: the provider publishes its schema (OpenAPI, protobuf, Avro,
  JSON Schema) and tooling checks compatibility — breaking-change linters for protobuf,
  compatibility modes in schema registries for events, spec-comparison brokers
  (bi-directional contract testing) matching consumer expectations against the provider spec
  without provider-side test code. Cheaper to adopt where schema discipline already exists.

A workable default: schema-based verification for every independently-deployed boundary,
consumer-driven contracts reserved for the highest-criticality pairs. Either way, the rule
the oracle enforces is the same: a diff that changes a wire contract updates the
corresponding contract or schema test in the same change. A contract change with no matching
test change means the contract either was never pinned or is no longer pinned — both are
findings. What makes a contract change breaking (the full taxonomy), versioning and deprecation
policy, and compatibility testing against deployed N-1 clients are split out to
`knowledge/quality/api-compatibility.md` (QUA-113 is its enforcement hook); fixtures that
encode contract shapes follow `knowledge/quality/test-data-management.md#fixture-drift`.
