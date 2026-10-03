# Claim-to-Evidence Integrity Matrix

Issue: #44.

Every generated factual claim that requires evidence must resolve to one or more immutable evidence references. Validation is deterministic and fail-closed.

| Case | Expected result |
| --- | --- |
| claim references an existing evidence id with matching digest | valid |
| evidence id does not exist | reject claim |
| evidence id exists but stored digest differs | reject claim |
| source record was re-fetched and replaced | old claim remains bound to old immutable evidence; new claims use new evidence |
| parser version changed | require the evidence reference produced by that parser version |
| claim uses multiple evidence items | every referenced item must independently validate |
| claim contains a factual assertion with no evidence reference | unsupported/reject according to answer policy |
| evidence provenance is incomplete | reject evidence before claim validation |
| evidence reference points to another tenant/scope where isolation applies | reject |
| evidence is marked stale/unknown freshness and source policy requires freshness | reject or surface as explicitly stale, never silently treat as current |

## Test shape

Automated tests should construct immutable evidence fixtures and mutate exactly one provenance component per negative case. A passing validator must never repair, rewrite or silently substitute evidence. Validation output should identify the failed invariant without exposing sensitive source payloads.
