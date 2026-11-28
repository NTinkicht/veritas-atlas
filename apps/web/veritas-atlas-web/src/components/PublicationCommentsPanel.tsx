import { useEffect, useRef, useState } from "react";
import {
  COMMENT_MAX_BODY,
  addComment,
  classifyCommentFailure,
  listComments,
  normalizeCommentBody,
  type CommentFailure,
  type RevisionComment,
} from "../api/publicationComments";

// VER-P1-02 (#400): editorial comment history of one publication revision. The list changes only from a
// server success. A retry of the same unsent draft reuses its clientKey so it can never be stored twice.
// Bodies render as text. Mount with key={revisionId} so a late response for another revision is dropped.

type Props = { revisionId: string; signInHref: string };

function Notice({ failure, signInHref }: { failure: CommentFailure; signInHref: string }) {
  return (
    <p className="notice-card notice-danger" role="alert">
      {failure.message}
      {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
    </p>
  );
}

export function PublicationCommentsPanel({ revisionId, signInHref }: Props) {
  const [items, setItems] = useState<RevisionComment[] | null>(null);
  const [loadFailure, setLoadFailure] = useState<CommentFailure | null>(null);
  const [notice, setNotice] = useState<CommentFailure | null>(null);
  const [draft, setDraft] = useState("");
  const [saving, setSaving] = useState(false);
  const [reloads, setReloads] = useState(0);
  const pending = useRef<{ key: string; body: string } | null>(null);
  const active = useRef(true);

  useEffect(() => {
    active.current = true;
    listComments(revisionId).then(
      (data) => active.current && setItems(data),
      (error) => active.current && setLoadFailure(classifyCommentFailure(error, navigator.onLine)),
    );
    return () => {
      active.current = false;
    };
  }, [revisionId, reloads]);

  const body = normalizeCommentBody(draft);

  function send() {
    if (items === null || saving || body === null) return;
    // Same unsent text keeps its key (idempotent retry); changed text gets a new one.
    if (pending.current === null || pending.current.body !== body) pending.current = { key: crypto.randomUUID(), body };
    const { key } = pending.current;
    setSaving(true);
    setNotice(null);
    addComment(revisionId, key, body).then(
      (created) => {
        if (!active.current) return;
        setItems((list) => [...(list ?? []).filter((c) => c.id !== created.id), created]);
        pending.current = null;
        setDraft("");
        setSaving(false);
      },
      (error) => {
        if (!active.current) return;
        setNotice(classifyCommentFailure(error, navigator.onLine));
        setSaving(false);
      },
    );
  }

  return (
    <section className="panel" aria-label="Editorial comments">
      <h2 className="panel-title">Editorial comments</h2>
      {loadFailure ? (
        <>
          <Notice failure={loadFailure} signInHref={signInHref} />
          <button type="button" onClick={() => { setLoadFailure(null); setReloads((n) => n + 1); }}>Reload comments</button>
        </>
      ) : items === null ? (
        <p className="muted" role="status">Loading comments...</p>
      ) : items.length === 0 ? (
        <p className="muted">This revision has no comments.</p>
      ) : (
        <ul>
          {items.map((c) => (
            <li key={c.id}>
              <strong>{c.author}</strong> <time dateTime={c.createdAtUtc}>{c.createdAtUtc}</time>
              <p style={{ whiteSpace: "pre-wrap" }}>{c.body}</p>
            </li>
          ))}
        </ul>
      )}
      {notice ? <Notice failure={notice} signInHref={signInHref} /> : null}
      <form aria-label="Add comment" onSubmit={(event) => { event.preventDefault(); send(); }}>
        <label htmlFor="rc-body">New comment</label>
        <textarea id="rc-body" value={draft} maxLength={COMMENT_MAX_BODY * 2} onChange={(event) => setDraft(event.target.value)} />
        <button type="submit" disabled={items === null || saving || body === null}>Add comment</button>
        {draft.length > 0 && body === null ? (
          <p className="muted">A comment is 1 to {COMMENT_MAX_BODY} characters without control characters.</p>
        ) : null}
      </form>
    </section>
  );
}
