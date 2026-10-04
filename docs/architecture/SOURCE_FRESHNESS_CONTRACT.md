# Public-Record Source Freshness Contract

Issue: #45.
Contract version: `source-freshness/v1`.

Freshness is explicit evidence state, never an inference from successful retrieval alone. Classification is a pure function of a verified evidence item, an exact supported source-policy version, authoritative revision evidence, and a trusted evaluation timestamp.

## States and deterministic precedence

Each evidence item has exactly one state: `CURRENT`, `STALE`, `UNKNOWN`, or `SUPERSEDED`.

Classification MUST use this precedence, from highest to lowest:

1. `SUPERSEDED` — a trusted replacement authority proves that a newer authoritative source revision replaces this evidence for current-state claims. This wins even when the older evidence is also past its age window.
2. `UNKNOWN` — no superseding revision is proven, but the exact applicable policy/version or any required freshness input cannot be verified. This wins over an apparent age calculation because an incomplete policy/input set cannot establish `STALE` or `CURRENT` safely.
3. `STALE` — the exact policy is supported, no authoritative replacement is known, all required inputs are valid, and retrieval age is **greater than** the policy's maximum retrieval age.
4. `CURRENT` — the exact policy is supported, no authoritative replacement is known, all required inputs are valid, and retrieval age is **less than or equal to** the policy's maximum retrieval age.

The states are therefore mutually exclusive. Age and supersession may both be factual properties, but the externally exposed freshness state follows the precedence above; supplementary audit metadata may retain both facts.

## Versioned source-policy shape

Every verified evidence item MUST resolve to exactly one supported source policy using trusted source-registry metadata, never a model/caller-selected fallback. A policy contains at least:

```json
{
  "policy_id": "public-record-source-policy",
  "policy_version": "1",
  "source_match": "trusted-registry-key-or-authority",
  "freshness_required_for_current_claims": true,
  "max_retrieval_age_seconds": 86400,
  "revision_signal": "etag|source-version-id|authoritative-index|signed-manifest|none",
  "revision_signal_required": true,
  "replacement_authority": "trusted-registry-or-source-adapter-id"
}
```

Rules:

- `policy_id` + `policy_version` are exact-match identifiers. Unknown, missing, disabled, or unsupported versions MUST NOT fall back to a default or previous version.
- `max_retrieval_age_seconds` is a non-negative integer fixed by the reviewed source policy. An absent/invalid value makes freshness `UNKNOWN` when freshness is required.
- `revision_signal_required=true` means the configured authoritative revision signal must be present and successfully verified. Missing/unverifiable revision metadata makes freshness `UNKNOWN` unless an authoritative replacement has already been positively proven, in which case precedence yields `SUPERSEDED`.
- `replacement_authority` identifies who is trusted to assert that a new source revision supersedes an old one. Caller/model assertions never establish supersession.
- A policy with `freshness_required_for_current_claims=false` may still classify evidence for presentation/audit, but cannot be substituted for a policy that the claim/source registry says requires freshness.

## Timestamp anchors and age calculation

Freshness evaluation uses two stored/trusted absolute timestamps:

- `retrieved_at`: the UTC RFC3339 completion timestamp of the verified authoritative retrieval whose exact bytes are bound to the evidence reference. It is written by the trusted retrieval boundary after the source response has been authenticated/verified.
- `evaluated_at`: the UTC RFC3339 timestamp captured by the trusted server-side freshness evaluation/revalidation operation.

Both timestamps MUST be offset-aware, canonicalized to UTC, and parse to real instants. `retrieved_at` MUST NOT be in the future relative to `evaluated_at`.

`retrieval_age_seconds = floor((evaluated_at - retrieved_at) / 1 second)`.

Boundary semantics are exact:

- `retrieval_age_seconds <= max_retrieval_age_seconds` is inside the age window;
- `retrieval_age_seconds > max_retrieval_age_seconds` is expired.

Missing, malformed, non-canonical, future `retrieved_at`, missing/invalid `evaluated_at`, negative age, or numeric overflow makes freshness `UNKNOWN` unless a trusted authoritative replacement already establishes `SUPERSEDED` by the precedence rule.

A source publication/effective timestamp may be retained as additional provenance and may be required by a source-specific policy for separate legal/effective-date validation, but it does not silently replace `retrieved_at` as the retrieval-age anchor in `source-freshness/v1`.

## Rules

1. Freshness policy is source-specific, versioned, and selected only through the trusted source registry.
2. Retrieval age is mandatory input to `source-freshness/v1`; successful retrieval alone does not prove currentness.
3. When the policy requires a revision signal, authoritative source-version verification is also mandatory for `CURRENT`/`STALE`; missing verification produces `UNKNOWN`.
4. A new source revision creates a new immutable evidence reference; prior evidence is never rewritten.
5. A positively verified authoritative replacement classifies the prior evidence `SUPERSEDED` regardless of whether its age window has also expired.
6. Current-state claims may use only `CURRENT` evidence when the applicable policy requires freshness.
7. `STALE`, `UNKNOWN`, and `SUPERSEDED` fail closed for freshness-required current-state claims.
8. Historical claims may reference non-current evidence only when the claim policy explicitly permits historical evidence and the answer preserves the historical time context; the evidence state remains visible in metadata.
9. Answer generation must not silently replace a cited evidence reference with a newer record.
10. Retry/recovery/revalidation must use an explicit `evaluated_at`; replaying a historical decision uses its recorded evaluation timestamp rather than the ambient process clock.

## Revalidation triggers

Revalidate on scheduled source refresh, explicit source revision change, source-policy version change, parser/source connector upgrade that affects revision interpretation, and before serving a current-state claim whose evidence age window has expired or whose exact policy/revision evidence is not already verified for the decision.

## Directly testable acceptance cases

Assume policy `P1` is supported, requires freshness and an authoritative revision signal, and has `max_retrieval_age_seconds=86400`.

- `retrieved_at=2026-10-01T00:00:00Z`, `evaluated_at=2026-10-02T00:00:00Z`, unchanged verified revision: age is exactly 86400, therefore `CURRENT`.
- Same input with `evaluated_at=2026-10-02T00:00:01Z`: age is 86401, therefore `STALE`.
- Evidence is age 90000 **and** a trusted replacement authority proves a newer revision: `SUPERSEDED`, not `STALE`.
- Exact policy id/version cannot be resolved: `UNKNOWN`; freshness-required current-state use is rejected.
- Policy is known but required authoritative revision metadata is unavailable: `UNKNOWN`; freshness-required current-state use is rejected.
- `retrieved_at` is missing, malformed, or later than `evaluated_at`: `UNKNOWN`; freshness-required current-state use is rejected.
- An authoritative replacement is proven while other freshness inputs are missing: `SUPERSEDED` by precedence; current-state use is rejected.
- Evidence is `CURRENT` under a policy/version different from the exact registry-selected policy version: that result is invalid; re-evaluate with the exact supported version or classify `UNKNOWN` if it is unavailable.
- Current-state answer validation rejects `STALE`, `UNKNOWN`, and `SUPERSEDED` evidence when freshness is mandatory.

These cases are normative and must map one-to-one to automated assertions; implementations may not choose alternative states for the same verified inputs.
