# Public-record evidence provenance contract

Status: bounded engineering contract for WU-L5-PROVENANCE (#43).

Veritas Atlas must not treat fetched public-record text as answerable evidence unless it carries a deterministic provenance envelope. The envelope follows the evidence from retrieval through parsing/chunking and into downstream AI citations or answer evidence references.

## Envelope v1

Every evidence item MUST contain:

```json
{
  "schema_version": "1.0",
  "evidence_ref": "ev1:<sha256>",
  "source": {
    "uri": "https://authoritative.example/record/123",
    "record_id": "source-native-stable-id",
    "jurisdiction": "normalized-jurisdiction",
    "source_type": "court_record|statute|regulation|register|filing|other_public_record"
  },
  "retrieval": {
    "retrieved_at": "RFC3339 UTC timestamp",
    "content_sha256": "64 lowercase hex characters"
  },
  "parser": {
    "name": "stable-parser-name",
    "version": "immutable parser/version identifier"
  }
}
```

`evidence_ref` is immutable and MUST be derived deterministically from a canonical serialization of the identity-bearing fields: schema version, normalized source URI, source-native record identifier, retrieval content digest, parser name and parser version. A later retrieval with changed bytes therefore creates a different evidence reference even when the source URI is unchanged.

## Validation rules

1. Missing or empty required fields fail closed; an item without complete provenance is not eligible evidence for an AI answer.
2. `content_sha256` MUST equal SHA-256 over the exact retrieved byte payload before parsing or normalization. Parsed text may additionally carry its own digest but cannot replace the retrieval digest.
3. `retrieved_at` MUST be an offset-aware RFC3339 timestamp normalized to UTC for canonical serialization.
4. `uri` MUST identify the actual authoritative/public source fetched, not a search-results page or an AI-generated redirect.
5. `record_id` MUST use a stable source-native identifier when available. If the source genuinely provides none, the ingestion adapter must derive and explicitly namespace a deterministic identifier rather than leaving the field blank.
6. `jurisdiction` and `source_type` MUST come from controlled normalized vocabularies. Unknown values must be represented explicitly as controlled `unknown`/`other` values according to the implementation schema, never guessed by the model.
7. Parser identity MUST be immutable enough to reproduce interpretation of the bytes. A parser behavior change requires a new version.
8. Verification MUST recompute the content digest and `evidence_ref`; a mismatch rejects the evidence item.
9. Downstream chunks/claims MUST retain the parent immutable `evidence_ref`. Chunk-local identifiers may supplement it but cannot sever the parent provenance chain.
10. An AI answer that asserts record-grounded facts MUST reference only verified evidence items; missing/unverified provenance is an evidence insufficiency condition, not permission to answer from the orphaned text.

## Canonical reference derivation

The implementation should serialize the following ordered object as UTF-8 canonical JSON (sorted keys, no insignificant whitespace) and compute SHA-256:

```json
{
  "content_sha256": "...",
  "parser_name": "...",
  "parser_version": "...",
  "record_id": "...",
  "schema_version": "1.0",
  "source_uri": "..."
}
```

The public reference is `ev1:` followed by the lowercase hexadecimal digest. Jurisdiction, source type and retrieval time remain mandatory provenance metadata but are intentionally excluded from identity so metadata normalization corrections do not masquerade as new source bytes. Any implementation that changes these identity semantics requires a schema/reference-version change.

## Required verification tests

- Same canonical envelope identity fields produce the same `evidence_ref` across processes.
- Changed source bytes change both `content_sha256` and `evidence_ref`.
- Changed parser version changes `evidence_ref` even for identical bytes.
- Missing URI, record ID, retrieval timestamp, digest, jurisdiction, source type, parser name or parser version is rejected.
- Malformed timestamps and non-SHA-256 digests are rejected.
- Digest mismatch against retrieved bytes is rejected.
- Tampered `evidence_ref` is rejected by recomputation.
- Derived chunks preserve the parent reference.
- Answer-evidence assembly rejects orphaned text and reports insufficient verified evidence rather than silently accepting it.

## Security and privacy boundary

The envelope records public-source provenance, not credentials, access tokens, private operator metadata or sensitive user data. Retrieval adapters must not persist authorization headers or secrets into provenance fields. This contract does not authorize new data sources, scraping policy changes, deployment, or production mutation.