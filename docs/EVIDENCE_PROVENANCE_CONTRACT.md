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
    "source_type": "court_record|statute|regulation|register|filing|other_public_record",
    "classification_proof": "trusted-classifier-or-source-registry-attestation"
  },
  "retrieval": {
    "retrieved_at": "RFC3339 UTC timestamp",
    "content_sha256": "64 lowercase hex characters",
    "attestation": "trusted-retrieval-attestation"
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

## Trusted retrieval and source-authentication boundary

Digest consistency alone is insufficient provenance. Before an item becomes verified/answerable evidence, the system MUST establish both **who/what performed the retrieval** and **that the retrieved bytes are authentically attributable to the authoritative source**.

The normal accepted path is HTTPS retrieval by a trusted adapter or trusted Veritas Atlas server-side retriever with successful certificate/hostname validation. Its signed or otherwise authenticated attestation MUST bind the normalized final URI, exact `content_sha256`, `retrieved_at`, retriever identity, and source-authentication result.

Plain HTTP MUST NOT become verified evidence merely because a trusted adapter fetched and hashed it. HTTP is eligible only when the exact source bytes carry an independent authenticity mechanism that the verifier validates, such as an authoritative detached signature or a trusted source-published digest obtained through a separately authenticated channel. That proof and its verification result MUST be bound into the retrieval attestation. Without such independent authentication, HTTP evidence remains unverified and ineligible for answer generation.

Caller-supplied URI/content without one of these trusted proofs MUST remain unverified and MUST NOT become answerable evidence, even when its digest and `evidence_ref` recompute successfully. Credentials, authorization headers and secrets MUST never be embedded in the provenance envelope.

### Independent-fetch network policy

An independent server-side fetch is a privileged verification operation, not a generic URL fetcher. Before every connection and after every redirect it MUST:

- require the destination to match an allowlisted/registered public-record source policy for the expected jurisdiction/source type;
- resolve the destination using the trusted resolver and reject loopback, link-local, private, multicast, metadata-service and otherwise forbidden address ranges unless an explicit separately reviewed source policy authorizes that network;
- bind the connection to the validated resolved destination and re-check policy on DNS changes/retries;
- allow only the source policy's approved HTTP(S) schemes and ports;
- re-validate every redirect hop, with a bounded redirect count and no credential forwarding across authority changes;
- record the final authoritative URI and a privacy-safe redirect/audit trace.

URI normalization is an identity rule and MUST NOT be treated as SSRF or destination-policy enforcement.

## Trusted classification boundary

`jurisdiction` and `source_type` are security-relevant provenance metadata. They MUST NOT be accepted merely because they are syntactically valid controlled-vocabulary strings.

Each value MUST be derived from either (a) a trusted source registry keyed by the authoritative source/record, or (b) authenticated source metadata covered by a trusted classification attestation. `source.classification_proof` MUST bind at least the evidence reference inputs or resulting `evidence_ref`, normalized jurisdiction, normalized source type, classifier/registry identity, and classification-policy version. Caller/model-supplied classification without trusted derivation is unverified and ineligible for answer generation.

Classification fields intentionally remain outside `evidence_ref` identity so a metadata correction does not masquerade as changed source bytes. A correction MUST nevertheless be an authenticated, append-only audit event that records prior value, new value, actor/registry identity, reason, policy version and timestamp. Downstream use MUST resolve the latest authorized classification revision and MUST reject conflicting/unverified revisions.

## Validation rules

1. Missing or empty required fields fail closed; an item without complete provenance is not eligible evidence for an AI answer.
2. `content_sha256` MUST equal SHA-256 over the exact bytes produced by the trusted retrieval boundary before parsing or normalization. Parsed text may additionally carry its own digest but cannot replace the retrieval digest.
3. `retrieved_at` MUST be an offset-aware RFC3339 timestamp normalized to UTC for canonical serialization.
4. `uri` MUST be the normalized final authoritative/public source URI defined above, not a search-results page, caller assertion, or AI-generated redirect.
5. `record_id` MUST use a stable source-native identifier when available. If the source genuinely provides none, the ingestion adapter must derive and explicitly namespace a deterministic identifier rather than leaving the field blank.
6. `jurisdiction` and `source_type` MUST come from controlled normalized vocabularies **and** pass the trusted-classification boundary above. Unknown values must be represented explicitly as controlled `unknown`/`other` values according to the implementation schema, never guessed by the model.
7. Parser identity MUST be immutable enough to reproduce interpretation of the bytes. A parser behavior change requires a new version.
8. Verification MUST validate trusted-retrieval evidence, source authentication, classification proof, recompute the content digest, normalize the URI, and recompute `evidence_ref`; any mismatch rejects the evidence item.
9. Downstream chunks/claims MUST retain the parent immutable `evidence_ref`. Chunk-local identifiers may supplement it but cannot sever the parent provenance chain.
10. An AI answer that asserts record-grounded facts MUST reference only verified evidence items; missing/unverified provenance is an evidence insufficiency condition, not permission to answer from orphaned text.
11. Verification is a publication gate: ingestion, retry, recovery, reparse, cache restore and answer assembly MUST NOT expose an item as verified if the current provenance/authentication checks have not succeeded atomically for that item/revision.

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

RFC 8785 is normative: implementations MUST NOT substitute serializer-specific key ordering, whitespace, Unicode escaping, slash escaping, or number formatting rules. The public reference is `ev1:` followed by the lowercase hexadecimal digest. Jurisdiction, source type, retrieval time, retrieval attestation and classification proof remain mandatory provenance metadata but are intentionally excluded from identity so authorized metadata/audit corrections do not masquerade as new source bytes. Any implementation that changes these identity semantics requires a schema/reference-version change.

## Required verification tests

- Same canonical envelope identity fields produce the same `evidence_ref` across processes and conforming RFC 8785 implementations, including non-ASCII and escapable characters.
- Equivalent URI spellings normalize identically for scheme/host case, default ports, unreserved percent escapes, fragments and dot segments.
- Query order/multiplicity and meaningful trailing-slash differences are preserved; final redirect URI, not requested URI, is canonical.
- Changed source bytes change both `content_sha256` and `evidence_ref`.
- Changed parser version changes `evidence_ref` even for identical bytes.
- Missing URI, record ID, retrieval timestamp, digest, trusted-retrieval attestation, jurisdiction, source type, classification proof, parser name or parser version is rejected.
- Caller-supplied bytes with a self-consistent digest/reference but no trusted retrieval proof are rejected as unverified.
- HTTPS hostname/certificate authentication failure rejects evidence even when bytes/hash are internally consistent.
- Plain HTTP with no independently verified source signature/digest rejects evidence; a configured HTTP exception passes only when its independent authenticity proof validates the exact bytes.
- Independent fetch rejects forbidden resolved addresses and revalidates every redirect hop; redirect-to-private/metadata destinations fail closed.
- Caller/model-supplied jurisdiction/source type without trusted classification proof is rejected; an authorized correction retains the same `evidence_ref` but creates a new authenticated metadata audit revision.
- Malformed timestamps and non-SHA-256 digests are rejected.
- Digest mismatch against trusted retrieved bytes is rejected.
- Tampered `evidence_ref` is rejected by recomputation.
- Derived chunks preserve the parent reference.
- Retry/recovery paths cannot publish an item when current provenance verification fails.
- Answer-evidence assembly rejects orphaned or untrusted text and reports insufficient verified evidence rather than silently accepting it.

## Security and privacy boundary

The envelope records public-source provenance, not credentials, access tokens, private operator metadata or sensitive user data. Retrieval adapters must not persist authorization headers or secrets into provenance fields. This contract does not authorize new data sources, scraping policy changes, deployment, or production mutation.
