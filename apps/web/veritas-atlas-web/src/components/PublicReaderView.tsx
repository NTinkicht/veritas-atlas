import type { PublicPublicationResult } from "../api/publicPublication";

// VER-P0-17 (#204): anonymous public-reader view. Renders only the server's allow-listed projection as text
// (React escapes it); there is deliberately no navigation, link or control that points at staff areas.

export const HIDDEN_MESSAGE = "This publication is not available.";
export const ERROR_MESSAGE = "This publication could not be loaded. Please try again later.";

// Keep the source-local calendar date: take it from the ISO string, never via a UTC conversion.
const fmt = (iso: string | null) => (iso && /^\d{4}-\d{2}-\d{2}/.test(iso) ? iso.slice(0, 10) : "not stated");

export type PublicReaderState = PublicPublicationResult | { kind: "loading" };

export function PublicReaderView({ state }: { state: PublicReaderState }) {
  if (state.kind === "loading") return <p role="status">Loading…</p>;
  if (state.kind === "hidden") return <p role="status">{HIDDEN_MESSAGE}</p>;
  if (state.kind === "error") return <p role="alert">{ERROR_MESSAGE}</p>;
  const { publication } = state;
  return (
    <article aria-label="Public publication">
      <p>Freshness: {publication.freshnessLabel}</p>
      {publication.citations.map((c, i) => (
        <section key={i} aria-label={c.citationLabel}>
          <h2>{c.title}</h2>
          <p>
            <strong>{c.citationLabel}</strong> · {c.sourceName}
          </p>
          <blockquote>{c.quote}</blockquote>
          <p>
            As of {fmt(c.asOf)} · Effective {fmt(c.effectiveDate)} · {c.freshnessLabel}
          </p>
        </section>
      ))}
    </article>
  );
}
