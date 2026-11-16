import { useEffect, useRef, useState } from "react";
import {
  SHARE_MAX_GRANTEE_LENGTH,
  classifyInvestigationFailure,
  grantShare,
  isValidGrantee,
  listShares,
  revokeShare,
  type Investigation,
  type InvestigationFailure,
  type InvestigationShare,
} from "../api/investigations";
import { formatInstant } from "../api/evidenceSearch";
import { FailureNotice } from "./InvestigationBoardView";

// VER-P0-17 / VER-P0-10: manage who an investigation is shared with. The server is the authority
// (only the creator may manage shares; a closed investigation refuses changes): the list changes
// only from a server success, and a refusal is shown, never papered over.

type Props = { investigation: Investigation; signInHref: string };

// Keyed by investigation id so every piece of state starts fresh when another investigation is opened.
export function InvestigationSharesPanel(props: Props) {
  return <SharesPanelBody key={props.investigation.id} {...props} />;
}

function SharesPanelBody({ investigation, signInHref }: Props) {
  const [shares, setShares] = useState<InvestigationShare[] | null>(null);
  const [loadFailure, setLoadFailure] = useState<InvestigationFailure | null>(null);
  const [grantee, setGrantee] = useState("");
  const [busy, setBusy] = useState<string[]>([]);
  const [notice, setNotice] = useState<InvestigationFailure | null>(null);
  const latest = useRef(0);
  const id = investigation.id;
  const closed = investigation.state === "closed";

  useEffect(() => {
    const ticket = ++latest.current;
    listShares(id).then(
      (data) => ticket === latest.current && setShares(data),
      (error) => ticket === latest.current && setLoadFailure(classifyInvestigationFailure(error, navigator.onLine)),
    );
  }, [id]);

  function grant() {
    const submitted = grantee;
    if (shares === null || closed || !isValidGrantee(submitted) || busy.includes(submitted)) return;
    const ticket = latest.current;
    setBusy((current) => [...current, submitted]);
    setNotice(null);
    grantShare(id, submitted).then(
      (created) => {
        if (ticket !== latest.current) return;
        // A replayed answer returns the stored row: never show it twice.
        setShares((current) => [...(current ?? []).filter((s) => s.granteeSubject !== created.granteeSubject), created]);
        setGrantee((now) => (now === submitted ? "" : now));
        setBusy((current) => current.filter((g) => g !== submitted));
      },
      (error) => {
        if (ticket !== latest.current) return;
        setNotice(classifyInvestigationFailure(error, navigator.onLine));
        setBusy((current) => current.filter((g) => g !== submitted));
      },
    );
  }

  function revoke(target: string) {
    if (shares === null || closed || busy.includes(target)) return;
    const ticket = latest.current;
    setBusy((current) => [...current, target]);
    setNotice(null);
    revokeShare(id, target).then(
      () => {
        if (ticket !== latest.current) return;
        setShares((current) => (current ?? []).filter((s) => s.granteeSubject !== target));
        setBusy((current) => current.filter((g) => g !== target));
      },
      (error) => {
        if (ticket !== latest.current) return;
        setNotice(classifyInvestigationFailure(error, navigator.onLine));
        setBusy((current) => current.filter((g) => g !== target));
      },
    );
  }

  const valid = isValidGrantee(grantee);
  return (
    <section aria-label="Investigation sharing">
      <h3>Shared with</h3>
      {loadFailure ? (
        <FailureNotice failure={loadFailure} signInHref={signInHref} />
      ) : shares === null ? (
        <p className="muted" role="status">Loading shares...</p>
      ) : shares.length === 0 ? (
        <p className="muted">This investigation is not shared with anyone.</p>
      ) : (
        <ul>
          {shares.map((s) => (
            <li key={s.granteeSubject}>
              <span>{s.granteeSubject}</span>{" "}
              <span className="muted">granted by {s.grantedBySubject} at {formatInstant(s.grantedAtUtc)}</span>{" "}
              <button
                type="button"
                disabled={closed || busy.includes(s.granteeSubject)}
                aria-label={`Revoke access for ${s.granteeSubject}`}
                onClick={() => revoke(s.granteeSubject)}
              >
                Revoke
              </button>
            </li>
          ))}
        </ul>
      )}
      {notice ? <FailureNotice failure={notice} signInHref={signInHref} /> : null}
      {closed ? <p className="muted">Reopen the investigation to change who it is shared with.</p> : null}
      <form
        aria-label="Share this investigation"
        onSubmit={(event) => {
          event.preventDefault();
          grant();
        }}
      >
        <label htmlFor="share-grantee">Share with (account name)</label>
        <input
          id="share-grantee"
          type="text"
          value={grantee}
          maxLength={SHARE_MAX_GRANTEE_LENGTH}
          onChange={(event) => setGrantee(event.target.value)}
        />
        <button type="submit" disabled={shares === null || closed || !valid || busy.includes(grantee)}>
          Share
        </button>
      </form>
    </section>
  );
}
