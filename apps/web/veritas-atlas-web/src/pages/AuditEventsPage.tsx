import { useEffect, useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import {
  classifyAuditEventsFailure,
  listAuditEvents,
  type AuditEvent,
} from "../api/tenantAuditEvents";
import { AppSurface } from "../components/AppSurface";
import {
  AuditEventsResult,
  type AuditEventsState,
} from "../components/AuditEventsView";

export function AuditEventsPage() {
  const location = useLocation();
  const [entityType, setEntityType] = useState("");
  const [entityId, setEntityId] = useState("");
  const [state, setState] = useState<AuditEventsState>({ kind: "loading" });
  const latest = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  function show(request: Promise<AuditEvent[]>, ticket: number) {
    request.then(
      (events) =>
        ticket === latest.current && setState({ kind: "loaded", events }),
      (error) =>
        ticket === latest.current &&
        setState({
          kind: "error",
          failure: classifyAuditEventsFailure(error, navigator.onLine),
        }),
    );
  }

  useEffect(() => {
    show(listAuditEvents({ entityType: "", entityId: "" }), ++latest.current);
    return () => {
      latest.current++;
    };
  }, []);

  function submit(event: FormEvent) {
    event.preventDefault();
    const ticket = ++latest.current;
    setState({ kind: "loading" });
    show(listAuditEvents({ entityType, entityId }), ticket);
  }

  return (
    <AppSurface
      title="Audit events"
      subtitle="Read-only view of the organization's append-only audit ledger. Administrators only."
    >
      <form onSubmit={submit}>
        <p>
          <label>
            Entity type{" "}
            <input
              value={entityType}
              onChange={(e) => setEntityType(e.target.value)}
              spellCheck={false}
              autoComplete="off"
            />
          </label>
        </p>
        <p>
          <label>
            Entity id{" "}
            <input
              value={entityId}
              onChange={(e) => setEntityId(e.target.value)}
              spellCheck={false}
              autoComplete="off"
            />
          </label>
        </p>
        <button type="submit">Filter audit events</button>
      </form>
      <AuditEventsResult state={state} signInHref={signInHref} />
    </AppSurface>
  );
}
