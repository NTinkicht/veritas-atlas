# Veritas Atlas Work Queue

GitHub Issues and live PR/CI/review state are authoritative. This file is the canonical ordered backlog snapshot for L5 replenishment.

Controller rule: when there is no open production PR, take the first dependency-ready item in **NEXT**, verify it is not already satisfied on `main`, and implement it. If it is already satisfied, move/reconcile it under **DONE / RECONCILED** and continue to the next item in the same run. **Do not reinterpret release readiness trackers as automatically authorized development work units.**

## ACTIVE

_None. This reconciliation PR updates the backlog snapshot only._

## NEXT

_No independently admitted, dependency-ready implementation work unit at this checkpoint. Do not replay #57 or manufacture a substitute._

## BLOCKED / SEPARATE PROJECT RELEASE GATES

- **#10 - project-owned adoption and staging release readiness.** Requires verified H1 independent-review enforcement, H7 writer/service inventory, H8 rotation and rollback evidence, and project-scoped release authority before staging deployment or controller adoption. This tracker is not executable authorization.
- **#12 - scoped CompanyOS onboarding and release proof.** Requires non-secret operator proof and separately authorized staging smoke, then a fresh target-specific C2b decision. A GitHub merge does not imply deployment.
- New implementation WUs must be explicitly described, dependency-checked and admitted on GitHub before entering **NEXT**. Do not silently turn #10 or #12 into unreviewed deploy work.

## DONE / RECONCILED

- **#43 WU-L5-PROVENANCE** - provenance contract and implementation present on main.
- **#44 WU-L5-CITATION-INTEGRITY** - citation-integrity matrix present on main.
- **#45 WU-L5-FRESHNESS** - source-freshness contract present on main.
- **#50 WU-L5-PROVENANCE-VECTORS** - vector artifact and `tests/test_evidence_provenance_vectors.py` present on main.
- **#52 WU-L5-CITATION-VECTORS** - citation vectors and `tests/test_citation_integrity_vectors.py` present on main.
- **#57 WU-L5-FRESHNESS-VECTORS** - executable versioned freshness vectors and deterministic tests delivered through independently reviewed and merged PR #59; issue #57 is closed.

Snapshot reconciled against GitHub main `3644ee7f21f22b4c928c432b8e784480737b74b2` and issues #10, #12 and #57 on 2026-10-10. No staging release or production migration is authorized by this queue file.
