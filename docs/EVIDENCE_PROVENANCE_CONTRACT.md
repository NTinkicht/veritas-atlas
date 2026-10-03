# Public-record evidence provenance contract

Status: bounded engineering contract for WU-L5-PROVENANCE (#43).

Veritas Atlas must not treat fetched public-record text as answerable evidence unless it carries a deterministic provenance envelope. The envelope follows the evidence from trusted retrieval through parsing/chunking and into downstream AI citations or answer evidence references.

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
    "content_sha256": "64 lowercase hex characters",
    "attestation": "trusted-adapter-or-independent-fetch-evidence"
  },
  "parser": {
    "name": "stable-parser-name",
    "version": "immutable parser/version identifier"
  }
}
```

`evidence_ref` is immutable and MUST be derived deterministically from the canonical identity-bearing fields: schema version, normalized source URI, source-native record identifier, retrieval content digest, parser name and parser version. A later trusted retrieval with changed bytes therefore creates a different evidence reference even when the source URI is unchanged.

## Normative source-URI normalization

The identity-bearing `source.uri` is the **final authoritative URI after trusted redirect resolution**, not the originally requested URI. Redirect history may be retained separately as non-identity audit metadata. Before reference derivation, normalize the final URI as follows:

1. parse it as an absolute HTTP or HTTPS URI; reject userinfo and malformed/relative values;
2. lowercase the scheme and DNS host and normalize an internationalized host to its ASCII IDNA form;
3. remove the default port (`:80` for HTTP, `:443` for HTTPS) and preserve any non-default port;
4. remove the fragment entirely;
5. use `/` for an empty path; preserve path segment and trailing-slash semantics otherwise;
6. normalize percent escapes to uppercase hex and decode percent-encoded **unreserved** characters only; never decode reserved delimiters;
7. remove dot segments according to RFC 3986 section 5.2.4;
8. preserve query parameter order and multiplicity exactly as present in the final authoritative URI, while applying the same percent-escape normalization to each query component. Do not sort query parameters because order can be source-significant.

Adapters MUST emit the same normalized URI for equivalent URI spellings covered by these rules. A redirect target that differs materially from the requested URI is canonical because it is the resource actually retrieved.

## Trusted retrieval boundary

Digest consistency alone is insufficient provenance. Before an item becomes verified/answerable evidence, the system MUST establish that the bytes were obtained through a trusted retrieval boundary. The accepted mechanism is either (a) an authenticated trusted-adapter attestation bound to the normalized final URI, exact content digest and retrieval time, or (b) an independent server-side fetch performed by a trusted Veritas Atlas retrieval component that records the same facts. Caller-supplied URI/content without one of these trusted proofs MUST remain unverified and MUST NOT become answerable evidence, even when its digest and `evidence_ref` recompute successfully.

The attestation format may be implemented separately, but verification MUST bind it to at least the normalized final URI, `content_sha256`, retrieval timestamp, and trusted adapter/retriever identity. Credentials, authorization headers and secrets MUST never be embedded in the provenance envelope.

## Validation rules

1. Missing or empty required fields fail closed; an item without complete provenance is not eligible evidence for an AI answer.
2. `content_sha256` MUST equal SHA-256 over the exact bytes produced by the trusted retrieval boundary before parsing or normalization. Parsed text may additionally carry its own digest but cannot replace the retrieval digest.
3. `retrieved_at` MUST be an offset-aware RFC3339 timestamp normalized to UTC for canonical serialization.
4. `uri` MUST be the normalized final authoritative/public source URI defined above, not a search-results page, caller assertion, or AI-generated redirect.
5. `record_id` MUST use a stable source-native identifier when available. If the source genuinely provides none, the ingestion adapter must derive and explicitly namespace a deterministic identifier rather than leaving the field blank.
6. `jurisdiction` and `source_type` MUST come from controlled normalized vocabularies. Unknown values must be represented explicitly as controlled `unknown`/`other` values according to the implementation schema, never guessed by the model.
7. Parser identity MUST be immutable enough to reproduce interpretation of the bytes. A parser behavior change requires a new version.
8. Verification MUST validate trusted-retrieval evidence, recompute the content digest, normalize the URI, and recompute `evidence_ref`; any mismatch rejects the evidence item.
9. Downstream chunks/claims MUST retain the parent immutable `evidence_ref`. Chunk-local identifiers may supplement it but cannot sever the parent provenance chain.
10. An AI answer that asserts record-grounded facts MUST reference only verified evidence items; missing/unverified provenance is an evidence insufficiency condition, not permission to answer from orphaned text.

## Canonical reference derivation

Construct the following JSON value from already-normalized identity fields and serialize it using **RFC 8785 JSON Canonicalization Scheme (JCS)**. Hash the exact UTF-8 bytes produced by RFC 8785 with SHA-256:

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

RFC 8785 is normative: implementations MUST NOT substitute serializer-specific key ordering, whitespace, Unicode escaping, slash escaping, or number formatting rules. The public reference is `ev1:` followed by the lowercase hexadecimal digest. Jurisdiction, source type, retrieval time and retrieval attestation remain mandatory provenance metadata but are intentionally excluded from identity so metadata/audit corrections do not masquerade as new source bytes. Any implementation that changes these identity semantics requires a schema/reference-version change.

## Required verification tests

- Same canonical envelope identity fields produce the same `evidence_ref` across processes and conforming RFC 8785 implementations, including non-ASCII and escapable characters.
- Equivalent URI spellings normalize identically for scheme/host case, default ports, unreserved percent escapes, fragments and dot segments.
- Query order/multiplicity and meaningful trailing-slash differences are preserved; final redirect URI, not requested URI, is canonical.
- Changed source bytes change both `content_sha256` and `evidence_ref`.
- Changed parser version changes `evidence_ref` even for identical bytes.
- Missing URI, record ID, retrieval timestamp, digest, trusted-retrieval attestation, jurisdiction, source type, parser name or parser version is rejected.
- Caller-supplied bytes with a self-consistent digest/reference but no trusted retrieval proof are rejected as unverified.
- Malformed timestamps and non-SHA-256 digests are rejected.
- Digest mismatch against trusted retrieved bytes is rejected.
- Tampered `evidence_ref` is rejected by recomputation.
- Derived chunks preserve the parent reference.
- Answer-evidence assembly rejects orphaned or untrusted text and reports insufficient verified evidence rather than silently accepting it.

## Security and privacy boundary

The envelope records public-source provenance, not credentials, access tokens, private operator metadata or sensitive user data. Retrieval adapters must not persist authorization headers or secrets into provenance fields. This contract does not authorize new data sources, scraping policy changes, deployment, or production mutation.