import { formatInstant } from "../api/evidenceSearch";
import {
  ANNOTATION_MAX_TEXT_LENGTH,
  INVESTIGATION_MAX_TITLE_LENGTH,
  isValidAnnotationText,
  isValidInvestigationTitle,
} from "../api/investigations";
import type {
  CollectedEvidenceItem,
  EvidenceAnnotation,
  ComparedEvidence,
  EvidenceComparison as EvidenceComparisonData,
  Investigation,
  InvestigationFailure,
} from "../api/investigations";

// Presentational pieces of the investigation board (VER-P0-17). All API text (titles, notes,
// subjects) is rendered as React text children, never as HTML.

export type Loadable<T> =
  | { kind: "loading" }
  | { kind: "error"; failure: InvestigationFailure }
  | { kind: "loaded"; data: T };

export function FailureNotice({ failure, signInHref }: { failure: InvestigationFailure; signInHref: string }) {
  return (
    <p className="notice-card notice-danger" role="alert">
      {failure.message}
      {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
    </p>
  );
}

type CreateProps = {
  title: string;
  submitting: boolean;
  notice: InvestigationFailure | null;
  onTitleChange: (value: string) => void;
  onSubmit: () => void;
  signInHref: string;
};

export function CreateInvestigationForm({ title, submitting, notice, onTitleChange, onSubmit, signInHref }: CreateProps) {
  const valid = isValidInvestigationTitle(title);
  return (
    <form
      aria-label="Open an investigation"
      onSubmit={(event) => {
        event.preventDefault();
        if (valid && !submitting) onSubmit();
      }}
    >
      <label htmlFor="new-investigation-title">New investigation title</label>
      <input
        id="new-investigation-title"
        type="text"
        value={title}
        maxLength={INVESTIGATION_MAX_TITLE_LENGTH}
        aria-describedby="new-investigation-hint"
        onChange={(event) => onTitleChange(event.target.value)}
      />
      <p id="new-investigation-hint" className="muted">
        1-{INVESTIGATION_MAX_TITLE_LENGTH} characters, no leading or trailing spaces. {title.length}/{INVESTIGATION_MAX_TITLE_LENGTH}
      </p>
      <button type="submit" disabled={!valid || submitting}>
        {submitting ? "Opening..." : "Open investigation"}
      </button>
      {notice ? <FailureNotice failure={notice} signInHref={signInHref} /> : null}
    </form>
  );
}

type ListProps = {
  view: Loadable<Investigation[]>;
  selectedId: string | null;
  onSelect: (investigation: Investigation) => void;
  signInHref: string;
};

export function InvestigationList({ view, selectedId, onSelect, signInHref }: ListProps) {
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading investigations...</p>;
  if (view.kind === "error") return <FailureNotice failure={view.failure} signInHref={signInHref} />;
  if (view.data.length === 0) return <p className="empty-card">No investigations are visible to you yet.</p>;
  return (
    <ul className="es-results" aria-label="Investigations">
      {view.data.map((investigation) => (
        <li key={investigation.id}>
          <button type="button" aria-pressed={investigation.id === selectedId} onClick={() => onSelect(investigation)}>
            {investigation.title}
          </button>
          <span className="muted"> opened {formatInstant(investigation.createdAtUtc)} by {investigation.createdBySubject}</span>
        </li>
      ))}
    </ul>
  );
}

type BoardProps = {
  investigation: Investigation;
  view: Loadable<CollectedEvidenceItem[]>;
  signInHref: string;
  /** Item ids picked for comparison (at most two). Omit to hide the comparison controls. */
  pickedIds?: readonly string[];
  onTogglePick?: (itemId: string) => void;
  /** Item ids the caller has bookmarked. Omit together with onToggleBookmark to hide bookmark controls. */
  bookmarkedIds?: readonly string[];
  onToggleBookmark?: (itemId: string, bookmarked: boolean) => void;
  /** Item ids with a bookmark request in flight. */
  busyIds?: readonly string[];
  bookmarkNotice?: InvestigationFailure | null;
  /** False until the caller's bookmarks were read successfully: mutations stay disabled (fail closed). */
  bookmarksReady?: boolean;
  /** Annotations: omit onToggleAnnotations to hide the controls. */
  annotations?: AnnotationsState | null;
  onToggleAnnotations?: (itemId: string) => void;
};

export type AnnotationsState = {
  itemId: string;
  view: Loadable<EvidenceAnnotation[]>;
  draft: string;
  submitting: boolean;
  notice: InvestigationFailure | null;
  onDraftChange: (value: string) => void;
  onSubmit: () => void;
};

export function EvidenceBoard({
  investigation,
  view,
  signInHref,
  pickedIds,
  onTogglePick,
  bookmarkedIds,
  onToggleBookmark,
  busyIds,
  bookmarkNotice,
  bookmarksReady = true,
  annotations,
  onToggleAnnotations,
}: BoardProps) {
  return (
    <section className="panel" aria-label={`Collected evidence for ${investigation.title}`}>
      <h2>{investigation.title}</h2>
      {view.kind === "loading" ? <p className="notice-card" role="status">Loading collected evidence...</p> : null}
      {view.kind === "error" ? <FailureNotice failure={view.failure} signInHref={signInHref} /> : null}
      {view.kind === "loaded" && view.data.length === 0 ? (
        <p className="empty-card">No evidence has been collected into this investigation yet.</p>
      ) : null}
      {bookmarkNotice ? <FailureNotice failure={bookmarkNotice} signInHref={signInHref} /> : null}
      {view.kind === "loaded" && view.data.length > 0 ? (
        <ul aria-label="Collected evidence">
          {view.data.map((item) => (
            <li key={item.id}>
              <strong>Verified evidence {item.evidenceVerificationId}</strong>
              <div className="muted">collected {formatInstant(item.createdAtUtc)} by {item.collectedBySubject}</div>
              {item.note ? <p>{item.note}</p> : null}
              {onToggleBookmark ? (
                <BookmarkControl
                  bookmarked={bookmarkedIds?.includes(item.id) ?? false}
                  busy={!bookmarksReady || (busyIds?.includes(item.id) ?? false)}
                  onToggle={(next) => onToggleBookmark(item.id, next)}
                />
              ) : null}
              {onToggleAnnotations ? (
                <div>
                  <button
                    type="button"
                    aria-expanded={annotations?.itemId === item.id}
                    onClick={() => onToggleAnnotations(item.id)}
                  >
                    {annotations?.itemId === item.id ? "Hide annotations" : "Annotations"}
                  </button>
                  {annotations?.itemId === item.id ? <AnnotationPanel state={annotations} signInHref={signInHref} /> : null}
                </div>
              ) : null}
              {onTogglePick ? (
                <label>
                  <input
                    type="checkbox"
                    checked={pickedIds?.includes(item.id) ?? false}
                    disabled={!(pickedIds?.includes(item.id) ?? false) && (pickedIds?.length ?? 0) >= 2}
                    onChange={() => onTogglePick(item.id)}
                  />{" "}
                  Compare this item
                </label>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

export function AnnotationPanel({ state, signInHref }: { state: AnnotationsState; signInHref: string }) {
  const { view, draft, submitting, notice } = state;
  const canSubmit = !submitting && view.kind === "loaded" && isValidAnnotationText(draft);
  return (
    <section aria-label="Annotations">
      {view.kind === "loading" ? <p className="notice-card" role="status">Loading annotations...</p> : null}
      {view.kind === "error" ? <FailureNotice failure={view.failure} signInHref={signInHref} /> : null}
      {view.kind === "loaded" && view.data.length === 0 ? <p className="empty-card">No annotations yet.</p> : null}
      {view.kind === "loaded" && view.data.length > 0 ? (
        <ul aria-label="Annotation list">
          {view.data.map((a) => (
            <li key={a.id}>
              <p style={{ whiteSpace: "pre-wrap" }}>{a.text}</p>
              <div className="muted">{a.authorSubject} at {formatInstant(a.createdAtUtc)}</div>
            </li>
          ))}
        </ul>
      ) : null}
      {notice ? <FailureNotice failure={notice} signInHref={signInHref} /> : null}
      {view.kind === "loaded" ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (canSubmit) state.onSubmit();
          }}
        >
          <label>
            Add an annotation (append-only, cannot be edited later)
            <textarea
              value={draft}
              maxLength={ANNOTATION_MAX_TEXT_LENGTH}
              onChange={(event) => state.onDraftChange(event.target.value)}
            />
          </label>
          <button type="submit" disabled={!canSubmit}>Add annotation</button>
        </form>
      ) : null}
    </section>
  );
}

function BookmarkControl({ bookmarked, busy, onToggle }: { bookmarked: boolean; busy: boolean; onToggle: (next: boolean) => void }) {
  return (
    <div>
      {bookmarked ? <span className="muted">Bookmarked </span> : null}
      <button type="button" disabled={busy} aria-pressed={bookmarked} onClick={() => onToggle(!bookmarked)}>
        {bookmarked ? "Remove bookmark" : "Bookmark"}
      </button>
    </div>
  );
}

function ComparedSide({ label, side }: { label: string; side: ComparedEvidence }) {
  return (
    <section aria-label={`${label} evidence`}>
      <h3>{label}</h3>
      <dl>
        <dt>Evidence reference</dt>
        <dd>{side.parentEvidenceRef}</dd>
        <dt>Raw retrieval</dt>
        <dd>{side.rawRetrievalId}</dd>
        <dt>Attestation</dt>
        <dd>{side.attestationId}</dd>
        <dt>Parse run</dt>
        <dd>{side.parseRunId}</dd>
        <dt>Verified</dt>
        <dd>{formatInstant(side.verifiedAtUtc)}</dd>
        <dt>Note</dt>
        <dd>{side.note ?? "No note"}</dd>
      </dl>
    </section>
  );
}

const yesNo = (same: boolean) => (same ? "Same" : "Different");

export function EvidenceComparison({ view, signInHref }: { view: Loadable<EvidenceComparisonData>; signInHref: string }) {
  if (view.kind === "loading") return <p className="notice-card" role="status">Loading comparison...</p>;
  if (view.kind === "error") return <FailureNotice failure={view.failure} signInHref={signInHref} />;
  const c = view.data;
  return (
    <section className="panel" aria-label="Evidence comparison">
      <h3>Comparison</h3>
      <ul aria-label="Comparison summary">
        <li>Evidence reference: {yesNo(c.sameEvidenceRef)}</li>
        <li>Raw retrieval: {yesNo(c.sameRawRetrieval)}</li>
        <li>Attestation: {yesNo(c.sameAttestation)}</li>
        <li>Parse run: {yesNo(c.sameParseRun)}</li>
      </ul>
      <ComparedSide label="Left" side={c.left} />
      <ComparedSide label="Right" side={c.right} />
    </section>
  );
}
