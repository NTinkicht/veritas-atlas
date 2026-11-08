# Authorization matrix (VER-P0-01)

Status: characterization of the **current** behavior, pinned by
`tests/VeritasAtlas.Api.Tests/AuthorizationMatrixTests.cs`. It does not grant
or change any permission. Changing a role set or adding an endpoint fails that
test until the pinned matrix and this document are updated together.

## Roles and trust model

- Roles in tokens: `operator`, `analyst`, `reviewer`, `publisher`, `admin`.
  Any other role value is unknown and must not be trusted.
- Authentication: deny by default (fallback policy requires an authenticated
  principal; VER-P0-02, `DefaultAuthenticatedEndpointTests`). The anonymous
  surface is exactly: `/health`, `/health/live`, `/health/ready`,
  `GET auth-diagnostics/public`, `POST auth/login`, and the anonymous
  public-reader route `GET public/publications/{id}`.
- Role checks are imperative inside controllers (`HasExactlyOneRoleIn`), not
  declarative attributes, so they are verified by calling the real endpoints.
- Tenant scope (owner decision A, #80): the tenant is a signed `tenant` claim.
  `tenant-*` endpoints filter by the caller's tenant; a cross-tenant id answers
  404. Legacy non-tenant endpoints (`cases`, `claims`, `sources`, ...) predate
  the tenant model and stay unscoped until VER-P0-18.

## Product workflow and who may act

query -> evidence -> claims -> review -> publication:

- Investigations, collected evidence, annotations, bookmarks: writes are
  not denied to `operator` and `analyst`; reviewers, publishers and admins are denied.
- Publication revisions: creation is not denied to `operator`/`analyst`; transitions are denied to
  `admin`; visibility changes are denied to everyone except `publisher`; anonymous readers see only explicitly PUBLISHED,
  public-scope revisions.
- Tenant source registry writes (create, fetch policy): denied to every role except `admin`.
- Legacy destructive `POST persistence/reset` and `POST workflow-audit/clear`: denied to every role except `admin`.

## Pinned matrix (endpoints where at least one role is denied with 403)

Every other protected endpoint denies **no** role at the authorization gate.
Source of truth is the test; this table is generated from it.

| Endpoint (under `api/v1/`) | Roles answered with 403 |
|---|---|
| `DELETE tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/bookmark` | reviewer, publisher, admin, intruder |
| `GET tenant-investigations` | intruder |
| `GET tenant-investigations/{id:guid}` | intruder |
| `GET tenant-investigations/{investigationId:guid}/bookmarks` | intruder |
| `GET tenant-investigations/{investigationId:guid}/evidence-items` | intruder |
| `GET tenant-investigations/{investigationId:guid}/evidence-items/comparison` | intruder |
| `GET tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/annotations` | intruder |
| `GET tenant-publication-revisions/{id:guid}` | admin, intruder |
| `POST actions/seed/lifecycle` | analyst, intruder |
| `POST persistence/reset` | operator, analyst, reviewer, publisher, intruder |
| `POST tenant-answers/compose` | intruder |
| `POST tenant-answers/serve-check` | intruder |
| `POST tenant-claims/confidence` | intruder |
| `POST tenant-claims/relation-graph` | intruder |
| `POST tenant-investigations` | reviewer, publisher, admin, intruder |
| `POST tenant-investigations/{investigationId:guid}/evidence-items` | reviewer, publisher, admin, intruder |
| `POST tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/annotations` | reviewer, publisher, admin, intruder |
| `POST tenant-publication-revisions` | reviewer, publisher, admin, intruder |
| `POST tenant-publication-revisions/{id:guid}/transitions` | admin, intruder |
| `POST tenant-sources` | operator, analyst, reviewer, publisher, intruder |
| `POST workflow-audit/clear` | operator, analyst, reviewer, publisher, intruder |
| `PUT tenant-investigations/{investigationId:guid}/evidence-items/{itemId:guid}/bookmark` | reviewer, publisher, admin, intruder |
| `PUT tenant-publication-revisions/{id:guid}/visibility` | operator, analyst, reviewer, admin, intruder |
| `PUT tenant-sources/{id:guid}/fetch-policy` | operator, analyst, reviewer, publisher, intruder |

## Known gaps found while pinning (not fixed here)

1. **Unknown roles pass on legacy endpoints.** An authenticated token with an
   unrecognized role is not denied by any of the legacy non-tenant endpoints
   (about 70 routes). The destructive `POST persistence/reset` and
   `POST workflow-audit/clear` are now admin-only (#237).
2. **Probe limitation.** The probe sends an empty JSON body. Endpoints that
   answer 400 from model binding do so before in-action role checks, so their
   role gating is not asserted by this matrix (rows with no denied role may
   still gate roles for valid bodies). A later slice should send valid bodies.
3. **Database-backed role checks** are now reached by a second test
   (`DatabaseBackedWorkflowRoleChecksArePinnedPerEndpoint`, #239) that seeds a
   case, claim, contradiction and review in a real PostgreSQL database
   (`VERITAS_TEST_POSTGRES`) and calls the 15 workflow action routes below as
   every role, from a valid starting status. Denied roles must not change the
   record; permitted roles must reach 200. Rows for these routes in the
   pinned table above show no denied role only because that probe uses an
   unreachable database; the real gating is:

   | Route (under `api/v1/`) | Policy action | Roles allowed (others denied) |
   |---|---|---|
   | `POST actions/cases/{id}/submit` | SubmitCase | operator, reviewer, publisher, admin |
   | `POST actions/cases/{id}/approve`, `reject` | ApproveCase, RejectCase | reviewer, publisher, admin |
   | `POST actions/cases/{id}/prepare`, `publish`, `hold` | PreparePublication, PublishCase, HoldCase | reviewer, publisher, admin |
   | `POST actions/contradictions/{id}/resolve` | ResolveContradiction | reviewer, publisher, admin |
   | `POST actions/reviews/{id}/complete` | CompleteReview | reviewer, publisher, admin |
   | `POST publication-workflow/cases/{id}/prepare`, `publish`, `hold` | PreparePublication, PublishCase, HoldCase | reviewer, publisher, admin |
   | `POST review-workflow/claims/{id}/send-to-review`, `return-for-edit` | SendClaimToReview, ReturnClaimForEdit | reviewer, publisher, admin |
   | `POST review-workflow/contradictions/{id}/escalate` | EscalateContradiction | reviewer, publisher, admin |
   | `POST review-workflow/reviews/{id}/reopen` | ReopenReview | reviewer, publisher, admin |

   `analyst` and unknown roles are denied everywhere; `operator` may only
   submit. Denied calls answer 403 with the role-policy message on every route, including
   the six `actions/cases/{id:guid}/...` routes (fixed in #241; an invalid
   transition answers 400). The record is unchanged on denial.
4. VER-P0-01 remains PLANNED: this slice adds the authorization matrix and
   negative vectors only. Requirement traceability for the remaining
   decisions is still open.
