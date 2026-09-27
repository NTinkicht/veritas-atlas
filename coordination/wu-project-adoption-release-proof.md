# WU: Project-owned adoption and release proof

Canonical issue: #12.

This WU implements only repository-verifiable, non-production validation. It does **not** deploy, rotate credentials, mutate Neon/Render, authorize a migration, clear H7/H8, or make the owner staging go/no-go decision.

Implemented:
- deterministic validation of the transferred H7 writer inventory and H8 release evidence;
- fail-closed checks that H7/H8 remain blocked and cannot silently acquire deployment, migration, cutover, budget, merge, or autonomy authority;
- Render/Neon target consistency checks;
- required unresolved real-world blocker checks;
- rejection of secret-bearing evidence fields;
- CI regression tests that prove historical evidence remains planning-only.

Remaining owner/external evidence includes actual GitHub administration protection, complete external writer inventory, non-secret credential rotation/revocation receipts, authenticated DB-backed staging smoke, applicable dependency-advisory disposition, recovery drill, immutable artifact/config revision, and a separate staging release go/no-go.

Material-Author: chatgpt
