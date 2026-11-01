# First Permitted Source Policy (VER-P0-01, #155)

This document records the first public-record source Veritas Atlas may retrieve from, and the
policy parameters that bind it. It serves PRODUCT.md row VER-P0-01 ("document permitted sources,
jurisdictions and freshness policy"). It contains no connector, fetcher or parser; those are
later rows (VER-P0-06/07/08). The machine-readable entry is
`tests/fixtures/first_source_policy_v1.json` and is pinned by
`FirstSourcePolicyTests`.

## Decision

Decided autonomously under the owner's standing delegation (issue #155).

| Option | Source | Outcome |
|---|---|---|
| A | UK legislation (legislation.gov.uk) | **Chosen** |
| B | US Federal Register | Rejected for now |
| C | An Algerian official gazette | Deferred (later connector, VER-P2-01) |

Why A: it publishes versioned legislation with point-in-time and in-force views, which
exercises the freshness and supersession contract (`SOURCE_FRESHNESS_CONTRACT.md`) better than
the alternatives; its stated licence is open with an attribution duty; no credentials and no
cost are involved. C matters to the owner but its terms and formats are unverified here.

Override path: the owner replies on issue #155 with a comment starting `OWNER OVERRIDE:`. A
different first source then requires a new policy entry and a new pinned fixture; this one is
not edited in place without updating the pin test.

## Policy

- Source id `uk-legislation-gov-uk`; jurisdiction `uk`; source type `legislation`; policy
  version `uk-legislation-v1`.
- Authority and classification: official UK legislation publisher. UNVERIFIED: the exact
  publisher/authority wording must be confirmed from the site's own documentation before a
  connector is admitted.
- Allowed hosts, exact list, no wildcards: `www.legislation.gov.uk`, `legislation.gov.uk`.
  Every addition is a reviewed change to the fixture and to the pin test.
- Schemes: `https` only; port 443 only; no plaintext-http exception; at most 3 redirects, each
  re-checked against this list (see `FetchDestinationPolicy`).
- Freshness (parameters of `SOURCE_FRESHNESS_CONTRACT.md`): `freshness_required_for_current_claims`
  true; `max_retrieval_age_seconds` 86400; `revision_signal` `source-version-id`, required;
  `replacement_authority` `uk-legislation-gov-uk-source-adapter`. These values are design
  choices for this slice, not facts about the source, and are UNVERIFIED against how the source
  actually exposes revisions. An unverifiable revision signal makes freshness `UNKNOWN`.
- Historical (point-in-time) versions may be retrieved only as non-current evidence carrying
  their as-of/effective date.

## Licence and attribution

Believed to be the Open Government Licence v3.0 with attribution required. UNVERIFIED: licence
terms, any content excluded from the licence, and the exact attribution text were not
confirmed from official documentation in this slice. The fixture carries the standard OGL
attribution sentence and the test requires it to be non-empty; a connector must not be
admitted until the terms are checked.

## Forbidden uses

No credentials or API keys, no fetching from hosts outside the list, no following redirects
off-list, no use of retrieved text before parser identity and provenance are verified, and no
live network access from tests or CI.

## UNVERIFIED summary

Licence terms and attribution text; publisher authority wording; that both hostnames serve
the same content; the revision signal and its name; available endpoints and formats; rate
limits and terms of use. Nothing above was verified against live documentation.
