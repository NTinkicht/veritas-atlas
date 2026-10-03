# Evidence Provenance Contract

Issue: #43.

Every public-record fragment admitted into retrieval or answer generation must carry a deterministic provenance envelope. Missing provenance is a hard validation failure, not an optional metadata warning.

## Required fields

- canonical source URI or authoritative record identifier
- source type and jurisdiction
- retrieval timestamp in UTC
- content SHA-256 digest
- parser/extractor name and version
- immutable evidence reference used by downstream claims
- source version/revision when the upstream system exposes one

## Invariants

1. The content digest is computed over the exact normalized bytes used by the parser.
2. The immutable evidence reference binds source identity, digest, parser version and retrieval event.
3. A downstream claim may reference only evidence whose provenance envelope is complete and schema-valid.
4. A later retrieval never mutates prior evidence in place; it creates a new evidence reference.
5. Parser upgrades create new evidence references even when source bytes are unchanged.
6. Missing jurisdiction/source type is represented explicitly as unknown only when the source contract permits it; required fields otherwise fail closed.

## Verification

A verifier must be able to recompute the evidence reference from persisted provenance fields and reject digest, parser-version or source-identity mismatches before the evidence is eligible for answer generation.

## Acceptance cases

- identical source bytes + same parser/version produce the same normalized content digest;
- changed bytes produce a different evidence reference;
- same bytes parsed by a new parser version produce a distinct evidence reference;
- missing digest, source identity or parser version is rejected;
- a claim pointing at a non-existent evidence reference is rejected.
