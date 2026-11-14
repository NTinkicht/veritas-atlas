import type { LifecycleAction, PublicationRevision, RevisionFailure } from "../api/publicationRevisions";
import { LIFECYCLE_ACTIONS } from "../api/publicationRevisions";

// Presentational pieces of the publication revision desk (VER-P0-17). All API text is rendered
// as React text children, never as HTML. Every action is offered; the server decides.

export type Loadable<T> =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: RevisionFailure }
  | { kind: "loaded"; data: T };

export function RevisionFailureNotice({ failure, signInHref }: { failure: RevisionFailure; signInHref: string }) {
  return (
    <p className="notice-card notice-danger" role="alert">
      {failure.message}
      {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
    </p>
  );
}

type Props = {
  view: Loadable<PublicationRevision>;
  busy: boolean;
  notice: string | null;
  actionFailure: RevisionFailure | null;
  onAction: (action: LifecycleAction) => void;
  signInHref: string;
};

export function RevisionPanel({ view, busy, notice, actionFailure, onAction, signInHref }: Props) {
  if (view.kind === "idle") return <p className="empty-card">Enter a publication revision id to open it.</p>;
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading publication revision...</p>;
  if (view.kind === "error") return <RevisionFailureNotice failure={view.failure} signInHref={signInHref} />;
  const revision = view.data;
  return (
    <section className="panel" aria-label="Publication revision">
      <h2>Revision {revision.id}</h2>
      <dl>
        <dt>State</dt>
        <dd>{revision.state} (version {revision.version})</dd>
        <dt>Author</dt>
        <dd>{revision.authorId}</dd>
        <dt>Content digest</dt>
        <dd>{revision.contentDigest}</dd>
        <dt>Approved digest</dt>
        <dd>{revision.approvedDigest ?? "none yet"}</dd>
        <dt>Policy permits public display</dt>
        <dd>{revision.policyPermitsPublicDisplay ? "yes" : "no"}</dd>
      </dl>
      <p className="muted">
        Actions are checked by the server against your role, authorship and the revision version. A refused action changes nothing.
      </p>
      <div role="group" aria-label="Lifecycle actions">
        {LIFECYCLE_ACTIONS.map((action) => (
          <button key={action} type="button" disabled={busy} onClick={() => onAction(action)}>
            {action}
          </button>
        ))}
      </div>
      {notice ? <p className="notice-card" role="status">{notice}</p> : null}
      {actionFailure ? <RevisionFailureNotice failure={actionFailure} signInHref={signInHref} /> : null}
    </section>
  );
}
