import { useState, type FormEvent } from "react";
import { isContentDigest } from "../api/publicationRevisions";

// VER-P0-17: start the publication lifecycle. The server alone decides whether the caller may
// author a draft; this form only refuses input that can never be valid, and reports success
// only after the server accepted the draft. Public display is opt-in and defaults to off.

type Props = {
  busy: boolean;
  onCreate: (digest: string, policyPermitsPublicDisplay: boolean) => void;
};

export function PublicationDraftForm({ busy, onCreate }: Props) {
  const [digest, setDigest] = useState("");
  const [permits, setPermits] = useState(false);
  const [invalid, setInvalid] = useState(false);

  function submit(event: FormEvent) {
    event.preventDefault();
    const value = digest.trim();
    if (!isContentDigest(value)) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    onCreate(value, permits);
  }

  return (
    <form onSubmit={submit} aria-label="Create publication draft">
      <h3>Create a draft</h3>
      <label>
        Content digest (SHA-256, 64 lowercase hex characters){" "}
        <input value={digest} onChange={(e) => setDigest(e.target.value)} spellCheck={false} autoComplete="off" aria-invalid={invalid} />
      </label>{" "}
      <label>
        <input type="checkbox" checked={permits} onChange={(e) => setPermits(e.target.checked)} /> Policy permits public display
      </label>{" "}
      <button type="submit" disabled={busy}>Create draft</button>
      {invalid ? (
        <p className="notice-card notice-danger" role="alert">
          The content digest must be exactly 64 lowercase hexadecimal characters. Nothing was sent.
        </p>
      ) : null}
    </form>
  );
}
