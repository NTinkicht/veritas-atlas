import { useRef, useState } from "react";
import {
  EVIDENCE_NOTE_MAX_LENGTH,
  classifyInvestigationFailure,
  collectEvidence,
  isValidEvidenceNote,
  isValidVerificationId,
  type CollectedEvidenceItem,
  type Investigation,
  type InvestigationFailure,
} from "../api/investigations";
import { FailureNotice } from "./InvestigationBoardView";

// VER-P0-17 / VER-P0-10: collect one verified evidence record into the open investigation. The server
// is the authority (author role, share authorization, closed investigation, unknown or foreign id): the
// board changes only from a server success, and a refusal is shown, never papered over.

type Props = {
  investigation: Investigation;
  /** False until the collected-evidence list was read, so a collection is never lost behind a pending read. */
  ready: boolean;
  signInHref: string; onCollected: (item: CollectedEvidenceItem) => void;
};

// Keyed by investigation id so every piece of state starts fresh when another investigation is opened.
export function CollectEvidenceForm(props: Props) {
  return <CollectEvidenceBody key={props.investigation.id} {...props} />;
}

function CollectEvidenceBody({ investigation, ready, signInHref, onCollected }: Props) {
  const [verificationId, setVerificationId] = useState("");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<InvestigationFailure | null>(null);
  const inFlight = useRef(false);
  const closed = investigation.state === "closed";
  const id = verificationId.trim();
  const valid = isValidVerificationId(id) && isValidEvidenceNote(note);

  function submit() {
    if (closed || !ready || busy || inFlight.current || !valid) return;
    inFlight.current = true;
    const submittedId = verificationId;
    const submittedNote = note;
    setBusy(true);
    setNotice(null);
    collectEvidence(investigation.id, id, note).then(
      (item) => {
        inFlight.current = false;
        setBusy(false);
        setVerificationId((now) => (now === submittedId ? "" : now));
        setNote((now) => (now === submittedNote ? "" : now));
        onCollected(item);
      },
      (error) => {
        inFlight.current = false;
        setBusy(false);
        setNotice(classifyInvestigationFailure(error, navigator.onLine));
      },
    );
  }

  return (
    <form
      aria-label="Collect verified evidence"
      onSubmit={(event) => {
        event.preventDefault();
        submit();
      }}
    >
      <h3>Collect verified evidence</h3>
      {closed ? <p className="muted">Reopen the investigation to collect evidence.</p> : null}
      {!ready && !closed ? <p className="muted" role="status">Collecting is available once the collected evidence has loaded.</p> : null}
      <label htmlFor="collect-verification-id">Verified evidence id</label>
      <input
        id="collect-verification-id"
        type="text"
        value={verificationId}
        maxLength={36}
        spellCheck={false}
        onChange={(event) => setVerificationId(event.target.value)}
      />
      <label htmlFor="collect-note">Note (optional)</label>
      <input
        id="collect-note"
        type="text"
        value={note}
        maxLength={EVIDENCE_NOTE_MAX_LENGTH}
        onChange={(event) => setNote(event.target.value)}
      />
      <button type="submit" disabled={closed || !ready || busy || !valid}>
        {busy ? "Collecting..." : "Collect evidence"}
      </button>
      {notice ? <FailureNotice failure={notice} signInHref={signInHref} /> : null}
    </form>
  );
}
