# Claim-to-Evidence Integrity Matrix

Issue: #44.
Contract version: `citation-integrity/v1`.

Every generated factual claim that requires evidence must resolve to one or more immutable, verified evidence references. Validation is deterministic and fail-closed: each case below has one explicit policy input and one expected result.

## Canonical test fixtures

Unless a row overrides them, automated tests use these immutable fixtures:

- `E1`: verified evidence for parser `public-record/v1`, digest `D1`, immutable reference `R1` recomputed from that exact parser/version and provenance;
- `E2`: the same authoritative source bytes and source identity parsed by `public-record/v2`, with immutable reference `R2 != R1`;
- `E3`: verified evidence with reference `R3` that is explicitly `STALE` under a source policy whose claim class requires current evidence;
- `E4`: verified evidence with reference `R4` whose applicable source policy allows historical/non-current evidence for the tested historical claim.

A validator must verify the reference against the parser name/version recorded in the referenced evidence fixture. It must never substitute the currently deployed parser version for the version bound into the immutable evidence record.

| Case | Explicit input | Expected result |
| --- | --- | --- |
| exact immutable evidence match | claim references `R1`; stored bytes digest is `D1`; parser is exactly `public-record/v1`; provenance verifies | **valid** |
| evidence id/reference does not exist | claim references unknown `R_missing` | **reject claim: EVIDENCE_NOT_FOUND** |
| stored digest differs | claim references `R1`, but exact retrieved bytes recompute to a digest other than `D1` | **reject claim: EVIDENCE_DIGEST_MISMATCH** |
| source record was re-fetched with changed bytes | old claim still references old verified `R1`; new verified retrieval has a different immutable reference | **old claim remains bound to `R1`; validator must not silently substitute the new reference** |
| parser/version exact match | evidence is `E1` and reference recomputation uses `public-record/v1` | **valid** |
| parser drift / mismatched reference | fixture bytes/source identity are interpreted as `public-record/v2`, but claim supplies `R1` from `public-record/v1` (or `R2` is paired with a record declaring v1) | **reject claim: EVIDENCE_REFERENCE_PARSER_MISMATCH** |
| multiple evidence items | claim references `R1` and another verified in-scope item | **valid only if every referenced item independently validates; one invalid item rejects the claim** |
| factual assertion with no qualifying evidence | answer policy classifies the assertion as evidence-required and the claim has no evidence reference | **reject claim: EVIDENCE_REQUIRED** |
| provenance incomplete | referenced item lacks any mandatory trusted provenance field/proof | **reject evidence before claim validation: PROVENANCE_INCOMPLETE** |
| isolation/scope mismatch | reference resolves to evidence outside the permitted tenant/scope where isolation applies | **reject claim: EVIDENCE_SCOPE_MISMATCH** |
| freshness required and evidence stale | claim class/source policy requires current evidence; claim references `E3` | **reject claim: EVIDENCE_STALE** |
| freshness required and freshness unknown | claim class/source policy requires current evidence; referenced item's freshness cannot be established | **reject claim: EVIDENCE_FRESHNESS_UNKNOWN** |
| historical claim explicitly permits non-current evidence | applicable versioned source/claim policy explicitly permits historical evidence; claim references verified `E4` | **valid with the historical evidence status retained in answer metadata; it must not be represented as current** |

## Test shape

Automated tests MUST construct immutable evidence fixtures and mutate exactly one provenance/policy component per negative case. The parser-drift test must create both the v1 and v2 reference identities and deliberately cross-bind one version/reference pair so the failure is unambiguous.

For policy-dependent behavior, the applicable policy/version is part of the test input; the expected result is never expressed as an alternative. Unsupported evidence-required claims are always rejected. Stale/unknown evidence is always rejected when the supplied policy requires freshness.

A passing validator must never repair, rewrite, refresh, reparse, or silently substitute evidence during claim validation. Validation output should identify the failed invariant with stable error codes without exposing sensitive source payloads.
