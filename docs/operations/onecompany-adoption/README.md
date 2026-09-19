# Project-owned adoption evidence

This directory contains project-specific historical snapshots, operator runbooks,
and staging decisions transferred out of the generic OneCompany source.
The OneCompany product must not ship target identifiers, credentials, service IDs,
deployment instructions, or client-specific Work Unit state.

## Scope and freshness

- All recorded snapshots are time-bound; reverify them before acting.
- H7/H8 remain blocked unless their actual exit conditions have been proved.
- A reviewed merge does not authorize a hosting deployment, schema migration,
  automatic release, or OneCompany cutover.
- All Neon and Render identifiers in the imported files refer to this project's
  isolated staging estate, never any unrelated project.
- Do not commit secrets or expose bearer tokens. Verify rotation of historically
  exposed staging credentials using non-secret operator evidence.

The original directory layout is retained below to preserve provenance. Original
relative links pointing into the former OneCompany source are historical, not
current operating links. Current-state records must be regenerated from the live
target and its connected hosting/database administration.
