import type { AuditEvent, AuditEventsFailure } from "../api/tenantAuditEvents";

// All API text is rendered as React text children, never as HTML.

export type AuditEventsState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "error"; failure: AuditEventsFailure }
  | { kind: "loaded"; events: AuditEvent[] };

export function AuditEventsResult({
  state,
  signInHref,
}: {
  state: AuditEventsState;
  signInHref: string;
}) {
  if (state.kind === "idle") return null;
  if (state.kind === "loading")
    return (
      <p className="notice-card" role="status">
        Loading audit events...
      </p>
    );
  if (state.kind === "error") {
    return (
      <p className="notice-card notice-danger" role="alert">
        {state.failure.message}
        {state.failure.kind === "unauthenticated" ? (
          <>
            {" "}
            <a href={signInHref}>Sign in</a>
          </>
        ) : null}
      </p>
    );
  }
  if (state.events.length === 0)
    return (
      <p className="notice-card" role="status">
        No audit events match.
      </p>
    );
  return (
    <section className="panel" aria-label="Audit events">
      <table>
        <caption>Newest 200 events at most, oldest first</caption>
        <thead>
          <tr>
            <th scope="col">When (UTC)</th>
            <th scope="col">Action</th>
            <th scope="col">Entity</th>
            <th scope="col">Actor</th>
          </tr>
        </thead>
        <tbody>
          {state.events.map((e) => (
            <tr key={e.id}>
              <td>
                <time dateTime={e.occurredAtUtc}>{e.occurredAtUtc}</time>
              </td>
              <td>{e.action}</td>
              <td>
                {e.entityType} <code>{e.entityId}</code>
              </td>
              <td>{e.actorSubject}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="notice-card" role="note">
        Read-only. The audit ledger is append-only and cannot be changed from
        here.
      </p>
    </section>
  );
}
