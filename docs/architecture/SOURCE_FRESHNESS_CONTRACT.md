# Public-Record Source Freshness Contract

Issue: #45.

Freshness is explicit evidence state, never an inference from successful retrieval alone.

## States

Each evidence item has one of: `CURRENT`, `STALE`, `UNKNOWN`, or `SUPERSEDED`.

- `CURRENT`: source policy confirms the evidence is inside its allowed freshness window and no newer authoritative revision is known.
- `STALE`: the evidence exceeded the configured freshness window.
- `UNKNOWN`: freshness cannot be established from available source metadata.
- `SUPERSEDED`: a newer authoritative source revision replaces this evidence for current-state claims.

## Rules

1. Freshness policy is source-specific and versioned.
2. Retrieval timestamp alone does not prove the upstream record itself is current.
3. A new source revision creates a new immutable evidence reference; prior evidence is never rewritten.
4. Current-state claims may use only `CURRENT` evidence when the source contract requires freshness.
5. Historical claims may reference stale/superseded evidence only when the answer clearly preserves the historical time context.
6. `UNKNOWN` fails closed for freshness-required claims.
7. Answer generation must not silently replace a cited evidence reference with a newer record.

## Revalidation triggers

Revalidate on scheduled source refresh, explicit source revision change, parser/source connector upgrade that affects version interpretation, and before serving a current-state claim whose evidence freshness window expired.

## Acceptance cases

- evidence inside the allowed window with unchanged source revision is `CURRENT`;
- expired evidence becomes `STALE` even if its content digest still matches;
- an authoritative replacement marks prior evidence `SUPERSEDED` for current-state use;
- unavailable source-version metadata produces `UNKNOWN` where freshness cannot otherwise be established;
- current-state answer validation rejects `STALE`, `UNKNOWN`, and `SUPERSEDED` evidence when freshness is mandatory.
