import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import { classifyClassificationFailure, classifyStatement } from "../api/statementClassification";
import { AppSurface } from "../components/AppSurface";
import { ClassificationResultView, type ClassificationView } from "../components/StatementClassificationView";

export function StatementClassificationPage() {
  const location = useLocation();
  const [evidenceRef, setEvidenceRef] = useState("");
  const [statement, setStatement] = useState("");
  const [view, setView] = useState<ClassificationView>({ kind: "idle" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setView({ kind: "loading" });
    classifyStatement({ evidenceRef, statement }).then(
      (result) => ticket === latest.current && setView({ kind: "loaded", result }),
      (error) => ticket === latest.current && setView({ kind: "error", failure: classifyClassificationFailure(error, navigator.onLine) }),
    );
  }

  return (
    <AppSurface title="Statement classification" subtitle="Ask whether a statement is a quotation, an inference, unresolved or unsupported by one verified evidence item. Nothing is saved.">
      <form onSubmit={submit}>
        <p>
          <label>Evidence reference <input value={evidenceRef} onChange={(e) => setEvidenceRef(e.target.value)} spellCheck={false} autoComplete="off" /></label>
        </p>
        <p>
          <label>Statement <textarea value={statement} onChange={(e) => setStatement(e.target.value)} rows={4} /></label>
        </p>
        <button type="submit">Classify statement</button>
      </form>
      <ClassificationResultView view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
