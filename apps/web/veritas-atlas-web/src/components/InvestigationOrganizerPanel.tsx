import { useEffect, useRef, useState } from "react";
import {
  FOLDER_MAX_LENGTH,
  TAG_MAX_LENGTH,
  addTag,
  classifyOrganizerFailure,
  clearFolder,
  getFolder,
  listTags,
  normalizeFolderName,
  normalizeTag,
  removeTag,
  setFolder,
  type OrganizerFailure,
} from "../api/investigationOrganizer";
import type { Investigation } from "../api/investigations";

// VER-P1-01: the caller's private tags and folder for one investigation. The server is the authority
// (author roles, sharing, closed state, tag limit): the view changes only from a server success, and a
// refusal is shown, never papered over. Tags and folder are private to the caller and never shared.

type Props = { investigation: Investigation; signInHref: string };

function Notice({ failure, signInHref }: { failure: OrganizerFailure; signInHref: string }) {
  return (
    <p className="notice-card notice-danger" role="alert">
      {failure.message}
      {failure.kind === "unauthenticated" ? <> <a href={signInHref}>Sign in</a></> : null}
    </p>
  );
}

type Loaded = { tags: string[]; folder: string | null };

// Keyed by investigation id so every piece of state starts fresh when another investigation is opened.
export function InvestigationOrganizerPanel(props: Props) {
  return <OrganizerBody key={props.investigation.id} {...props} />;
}

function OrganizerBody({ investigation, signInHref }: Props) {
  const [data, setData] = useState<Loaded | null>(null);
  const [loadFailure, setLoadFailure] = useState<OrganizerFailure | null>(null);
  const [notice, setNotice] = useState<OrganizerFailure | null>(null);
  const [tagDraft, setTagDraft] = useState("");
  const [folderDraft, setFolderDraft] = useState("");
  const [busy, setBusy] = useState<string[]>([]);
  const [reloads, setReloads] = useState(0);
  const latest = useRef(0);
  const id = investigation.id;
  const closed = investigation.state === "closed";

  useEffect(() => {
    const ticket = ++latest.current;
    Promise.all([listTags(id), getFolder(id)]).then(
      ([tags, folder]) => ticket === latest.current && setData({ tags, folder }),
      (error) => ticket === latest.current && setLoadFailure(classifyOrganizerFailure(error, navigator.onLine)),
    );
  }, [id, reloads]);

  function retry() {
    setLoadFailure(null);
    setReloads((n) => n + 1);
  }

  // One in-flight change per key; a late answer after the user moved on is ignored by the ticket check.
  function run<T>(key: string, work: () => Promise<T>, apply: (result: T) => void) {
    if (data === null || closed || busy.includes(key)) return;
    const ticket = latest.current;
    setBusy((b) => [...b, key]);
    setNotice(null);
    work().then(
      (result) => {
        if (ticket !== latest.current) return;
        apply(result);
        setBusy((b) => b.filter((k) => k !== key));
      },
      (error) => {
        if (ticket !== latest.current) return;
        setNotice(classifyOrganizerFailure(error, navigator.onLine));
        setBusy((b) => b.filter((k) => k !== key));
      },
    );
  }

  const normalizedTag = normalizeTag(tagDraft);
  const normalizedFolder = normalizeFolderName(folderDraft);

  function submitTag() {
    const tag = normalizedTag;
    if (tag === null) return;
    run(`tag:${tag}`, () => addTag(id, tag), (added) => {
      // A replayed answer returns the stored row: never show it twice.
      setData((d) => (d === null ? d : { ...d, tags: [...d.tags.filter((t) => t !== added), added].sort() }));
      setTagDraft((now) => (now === tagDraft ? "" : now));
    });
  }

  function dropTag(tag: string) {
    run(`tag:${tag}`, () => removeTag(id, tag), () =>
      setData((d) => (d === null ? d : { ...d, tags: d.tags.filter((t) => t !== tag) })),
    );
  }

  function submitFolder() {
    const name = normalizedFolder;
    if (name === null) return;
    run("folder", () => setFolder(id, name), (saved) => {
      setData((d) => (d === null ? d : { ...d, folder: saved }));
      // Keep a newer draft typed while this save was pending (same rule as the tag draft).
      setFolderDraft((now) => (now === folderDraft ? "" : now));
    });
  }

  function dropFolder() {
    run("folder", () => clearFolder(id), () => setData((d) => (d === null ? d : { ...d, folder: null })));
  }

  const folderBusy = busy.includes("folder");
  return (
    <section aria-label="Investigation tags and folder">
      <h3>Your tags and folder</h3>
      <p className="muted">Private to you: other people with access do not see them.</p>
      {loadFailure ? (
        <>
          <Notice failure={loadFailure} signInHref={signInHref} />
          <button type="button" onClick={retry}>Reload tags and folder</button>
        </>
      ) : data === null ? (
        <p className="muted" role="status">Loading tags and folder...</p>
      ) : (
        <>
          <p>
            Folder: {data.folder === null ? <span className="muted">Not filed in a folder.</span> : <strong>{data.folder}</strong>}{" "}
            {data.folder !== null ? (
              <button type="button" disabled={closed || folderBusy} aria-label={`Remove from folder ${data.folder}`} onClick={dropFolder}>
                Clear folder
              </button>
            ) : null}
          </p>
          {data.tags.length === 0 ? (
            <p className="muted">You have not tagged this investigation.</p>
          ) : (
            <ul aria-label="Tags">
              {data.tags.map((tag) => (
                <li key={tag}>
                  <span>{tag}</span>{" "}
                  <button type="button" disabled={closed || busy.includes(`tag:${tag}`)} aria-label={`Remove tag ${tag}`} onClick={() => dropTag(tag)}>
                    Remove
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
      {notice ? <Notice failure={notice} signInHref={signInHref} /> : null}
      {closed ? <p className="muted">This investigation is closed; reopen it to change its tags or folder.</p> : null}
      <form
        aria-label="Add a tag"
        onSubmit={(event) => {
          event.preventDefault();
          if (!closed) submitTag();
        }}
      >
        <label htmlFor="org-tag">Tag (lowercase letters, digits, _ . -)</label>
        <input id="org-tag" type="text" value={tagDraft} maxLength={TAG_MAX_LENGTH} onChange={(e) => setTagDraft(e.target.value)} />
        <button type="submit" disabled={data === null || closed || normalizedTag === null || busy.includes(`tag:${normalizedTag}`)}>
          Add tag
        </button>
      </form>
      <form
        aria-label="File in a folder"
        onSubmit={(event) => {
          event.preventDefault();
          if (!closed) submitFolder();
        }}
      >
        <label htmlFor="org-folder">Folder name</label>
        <input id="org-folder" type="text" value={folderDraft} maxLength={FOLDER_MAX_LENGTH} onChange={(e) => setFolderDraft(e.target.value)} />
        <button type="submit" disabled={data === null || closed || normalizedFolder === null || folderBusy}>
          Set folder
        </button>
      </form>
    </section>
  );
}
