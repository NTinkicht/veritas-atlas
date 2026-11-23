import { useEffect, useRef, useState } from "react";
import {
  SAVED_SEARCH_MAX_NAME,
  classifySavedSearchFailure,
  createSavedSearch,
  deleteSavedSearch,
  isValidSavedSearchName,
  listSavedSearches,
  type SavedSearch,
  type SavedSearchDraft,
  type SavedSearchFailure,
} from "../api/savedSearches";

// VER-P1-01 (#394): the caller's private saved searches. The server is the authority: the list changes
// only from a server success. Applying a saved search only fills the form; running it is the normal
// search, which re-evaluates freshness at that request.

type Props = {
  current: SavedSearchDraft;
  onApply: (search: SavedSearch) => void;
  signInHref: string;
};

function Notice({ failure, signInHref }: { failure: SavedSearchFailure; signInHref: string }) {
  return (
    <p className="notice-card notice-danger" role="alert">
      {failure.message}
      {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
    </p>
  );
}

export function SavedSearchesPanel({ current, onApply, signInHref }: Props) {
  const [items, setItems] = useState<SavedSearch[] | null>(null);
  const [loadFailure, setLoadFailure] = useState<SavedSearchFailure | null>(null);
  const [notice, setNotice] = useState<SavedSearchFailure | null>(null);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [reloads, setReloads] = useState(0);
  const latest = useRef(0);

  useEffect(() => {
    const ticket = ++latest.current;
    listSavedSearches().then(
      (data) => ticket === latest.current && setItems(data),
      (error) => ticket === latest.current && setLoadFailure(classifySavedSearchFailure(error, navigator.onLine)),
    );
  }, [reloads]);

  function retry() {
    setLoadFailure(null);
    setReloads((n) => n + 1);
  }

  const queryOk = current.q.trim().length > 0;
  const nameOk = isValidSavedSearchName(name);

  function save() {
    if (items === null || saving || !nameOk || !queryOk) return;
    setSaving(true);
    setNotice(null);
    createSavedSearch({ ...current, name }).then(
      (created) => {
        setItems((list) => [...(list ?? []).filter((s) => s.id !== created.id), created]);
        setName("");
        setSaving(false);
      },
      (error) => {
        setNotice(classifySavedSearchFailure(error, navigator.onLine));
        setSaving(false);
      },
    );
  }

  function remove(id: string) {
    if (items === null || busy.includes(id)) return;
    setBusy((b) => [...b, id]);
    setNotice(null);
    deleteSavedSearch(id).then(
      () => {
        setItems((list) => (list ?? []).filter((s) => s.id !== id));
        setBusy((b) => b.filter((x) => x !== id));
      },
      (error) => {
        setNotice(classifySavedSearchFailure(error, navigator.onLine));
        setBusy((b) => b.filter((x) => x !== id));
      },
    );
  }

  return (
    <section className="panel" aria-label="Saved searches">
      <h2 className="panel-title">Saved searches</h2>
      {loadFailure ? (
        <>
          <Notice failure={loadFailure} signInHref={signInHref} />
          <button type="button" onClick={retry}>Reload saved searches</button>
        </>
      ) : items === null ? (
        <p className="muted" role="status">Loading saved searches...</p>
      ) : items.length === 0 ? (
        <p className="muted">You have no saved searches.</p>
      ) : (
        <ul>
          {items.map((s) => (
            <li key={s.id}>
              <strong>{s.name}</strong> <span className="muted">{s.query}</span>{" "}
              {s.hasUnsupportedCriteria ? (
                <span className="muted">Uses criteria this page cannot apply.</span>
              ) : (
                <button type="button" aria-label={`Apply saved search ${s.name}`} onClick={() => onApply(s)}>
                  Apply
                </button>
              )}{" "}
              <button
                type="button"
                disabled={busy.includes(s.id)}
                aria-label={`Delete saved search ${s.name}`}
                onClick={() => remove(s.id)}
              >
                Delete
              </button>
            </li>
          ))}
        </ul>
      )}
      {notice ? <Notice failure={notice} signInHref={signInHref} /> : null}
      <form
        aria-label="Save current search"
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
      >
        <label htmlFor="ss-name">Name for the current search</label>
        <input
          id="ss-name"
          type="text"
          value={name}
          maxLength={SAVED_SEARCH_MAX_NAME}
          onChange={(event) => setName(event.target.value)}
        />
        <button type="submit" disabled={items === null || saving || !nameOk || !queryOk}>
          Save search
        </button>
        {!queryOk ? <p className="muted">Enter a query to save a search.</p> : null}
      </form>
    </section>
  );
}
