# Seeded Claim IDs Fix

Applied:
- after Seed Lifecycle, the harness now reads the persistence snapshot
- if claimAId or claimBId are missing from the seed response, they are filled from the snapshot
- added N/A fallback display instead of silent blank values