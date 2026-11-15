import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import { VERIFICATION_STATES, classifyComposeFailure, composeReport, type VerificationState } from "../api/answerReport";
import { AppSurface } from "../components/AppSurface";
import { ReportResult, type ReportView } from "../components/AnswerReportView";

export function AnswerReportPage() {
  const location = useLocation();
  const [claimId, setClaimId] = useState("");
  const [text, setText] = useState("");
  const [currentState, setCurrentState] = useState(true);
  const [historicalAsOf, setHistoricalAsOf] = useState("");
  const [citationLines, setCitationLines] = useState("");
  const [humanVerification, setHumanVerification] = useState<VerificationState>("unreviewed");
  const [view, setView] = useState<ReportView>({ kind: "idle" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    composeReport({ claimId, text, currentState, historicalAsOf, citationLines, humanVerification }).then(
      (result) => ticket === latest.current && setView({ kind: "loaded", result }),
      (error) => ticket === latest.current && setView({ kind: "error", failure: classifyComposeFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Source-backed report" subtitle="Compose one claim with its cited evidence. The server re-checks every citation and freshness before it will serve the answer.">
      <form onSubmit={submit}>
        <p>
          <label>Claim id <input value={claimId} onChange={(e) => setClaimId(e.target.value)} spellCheck={false} autoComplete="off" /></label>
        </p>
        <p>
          <label>Claim text <textarea value={text} onChange={(e) => setText(e.target.value)} rows={3} /></label>
        </p>
        <p>
          <label>
            <input type="checkbox" checked={currentState} onChange={(e) => setCurrentState(e.target.checked)} /> Current-state claim
          </label>
        </p>
        <p>
          <label>
            Historical as-of date (YYYY-MM-DD){" "}
            <input value={historicalAsOf} onChange={(e) => setHistoricalAsOf(e.target.value)} disabled={currentState} autoComplete="off" />
          </label>
        </p>
        <p>
          <label>
            Citations, one per line: reference | parser name | parser version | optional scope GUID
            <textarea value={citationLines} onChange={(e) => setCitationLines(e.target.value)} rows={4} spellCheck={false} />
          </label>
        </p>
        <p>
          <label>
            Human verification{" "}
            <select value={humanVerification} onChange={(e) => setHumanVerification(e.target.value as VerificationState)}>
              {VERIFICATION_STATES.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </label>
        </p>
        <button type="submit">Compose report</button>
      </form>
      <ReportResult view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
