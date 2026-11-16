import { REVISION_STATES, type PublicationRevision, type RevisionFailure, type RevisionState } from "../api/publicationRevisions";
import { RevisionFailureNotice } from "./PublicationRevisionView";

// Review queue of the publication revision desk (VER-P0-17). Presentational: API text is rendered
// as React text children only. The list is whatever the server returned for the caller's tenant.

export type QueueView = {
  items: PublicationRevision[];
  nextAfterId: string | null;
  loading: boolean;
  failure: RevisionFailure | null;
  loaded: boolean;
};

type Props = {
  queue: QueueView;
  state: RevisionState | null;
  onStateChange: (state: RevisionState | null) => void;
  onOpen: (id: string) => void;
  onLoadMore: () => void;
  signInHref: string;
};

export function RevisionQueue({ queue, state, onStateChange, onOpen, onLoadMore, signInHref }: Props) {
  return (
    <section className="panel" aria-label="Publication review queue">
      <h2>Review queue</h2>
      <label>
        State{" "}
        <select
          value={state ?? ""}
          onChange={(e) => onStateChange(e.target.value === "" ? null : (e.target.value as RevisionState))}
        >
          <option value="">All states</option>
          {REVISION_STATES.map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </select>
      </label>
      {queue.failure ? <RevisionFailureNotice failure={queue.failure} signInHref={signInHref} /> : null}
      {queue.loading && queue.items.length === 0 ? <p className="notice-card" role="status">Loading review queue...</p> : null}
      {queue.loaded && !queue.failure && queue.items.length === 0 ? <p className="empty-card">No publication revisions match.</p> : null}
      {queue.items.length > 0 ? (
        <ul>
          {queue.items.map((item) => (
            <li key={item.id}>
              {item.state} (version {item.version}) by {item.authorId} {" "}
              <button type="button" onClick={() => onOpen(item.id)}>Open {item.id}</button>
            </li>
          ))}
        </ul>
      ) : null}
      {queue.nextAfterId !== null ? (
        <button type="button" disabled={queue.loading} onClick={onLoadMore}>Load more</button>
      ) : null}
    </section>
  );
}
