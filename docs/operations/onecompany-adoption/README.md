# Project-owned OneCompany adoption evidence

This directory contains Veritas Atlas-specific snapshots, writer inventories,
release runbooks, and staging decisions transferred from the OneCompany
**development** workstream. OneCompany itself is a reusable project-independent
product; it must not ship named-target evidence, provider IDs or target-specific
release instructions. The evidence below remains scoped to this project only.

## Authority and freshness

- Historical snapshots dated 18–19 September 2026 are immutable observations,
  not evidence of the current environment unless reverified at a new boundary.
- H7/H8 status remains **blocked** until their respective exit criteria are met.
- An approved GitHub PR is not Render release authority, a schema-migration
  authorization, an autonomous merge delegation, or OneCompany installation.
- Render and Neon values in these documents identify **Veritas Atlas Staging
  only**; unrelated projects, including Project HUMAN, are out of scope.
- No credential values should be committed; previously disclosed Neon and
  staging bootstrap credentials must have independently verifiable rotation
  before staging user data.

These files retain the original relative hierarchy under this directory to
preserve audit provenance. Existing links targeting their former location in
OneCompany are historical and should not be treated as operational links.

For current release decisions, reconcile this project's latest GitHub main,
Render deploy IDs, protected-main admin settings, Neon migration history,
credential rotation, and current dependency audit against the runbook.
