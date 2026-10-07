# Veritas Atlas Work Queue

GitHub Issues and live PR/CI/review state are authoritative. This file is the canonical ordered backlog snapshot for L5 replenishment.

Controller rule: when there is no open production PR, take the first dependency-ready item in **NEXT**, verify it is not already satisfied on `main`, and implement it. If it is already satisfied, move/reconcile it under **DONE / RECONCILED** and continue to the next item in the same run.

## ACTIVE

_None._

## NEXT

1. **#57 - WU-L5-FRESHNESS-VECTORS: executable source freshness cases**  
   Add machine-readable freshness vectors plus deterministic tests that consume them directly.

## BLOCKED

_None for #57 after reconciliation._

## DONE / RECONCILED

- **#43 WU-L5-PROVENANCE** - provenance contract and implementation present on main.
- **#44 WU-L5-CITATION-INTEGRITY** - citation-integrity matrix present on main.
- **#45 WU-L5-FRESHNESS** - source-freshness contract present on main.
- **#50 WU-L5-PROVENANCE-VECTORS** - vector artifact and `tests/test_evidence_provenance_vectors.py` present on main.
- **#52 WU-L5-CITATION-VECTORS** - citation vectors and `tests/test_citation_integrity_vectors.py` present on main.

Snapshot refreshed: 2026-10-07.
