import { useRef, useState, type FormEvent } from "react";
import { useLocation } from "react-router-dom";
import {
  classifyFailure,
  MAX_QUERY_LENGTH,
  searchEvidence,
  type EvidenceHit,
  type FreshnessLabel,
} from "../api/evidenceSearch";
import { AppSurface } from "../components/AppSurface";
import { CitationInspector, EvidenceResults, type SearchView } from "../components/EvidenceSearchView";

const PAGE_SIZE = 20;
const FRESHNESS_OPTIONS: FreshnessLabel[] = ["CURRENT", "STALE", "SUPERSEDED", "UNKNOWN"];

type Submitted = { q: string; freshness: FreshnessLabel | ""; jurisdiction: string; type: string };

export function EvidenceSearchPage() {
  const location = useLocation();
  const [q, setQ] = useState("");
  const [freshness, setFreshness] = useState<FreshnessLabel | "">("");
  const [jurisdiction, setJurisdiction] = useState("");
  const [type, setType] = useState("");
  const [view, setView] = useState<SearchView>({ kind: "idle" });
  const [selected, setSelected] = useState<EvidenceHit | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const latest = useRef(0);
  const submitted = useRef<Submitted | null>(null);

  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  async function runSearch(event: FormEvent) {
    event.preventDefault();
    const trimmed = q.trim();
    if (trimmed.length === 0 || trimmed.length > MAX_QUERY_LENGTH) {
      setFormError(`Enter a query of 1 to ${MAX_QUERY_LENGTH} characters.`);
      return;
    }
    setFormError(null);
    const criteria: Submitted = { q: trimmed, freshness, jurisdiction, type };
    submitted.current = criteria;
    const ticket = ++latest.current;
    setSelected(null);
    setView({ kind: "loading" });
    try {
      const page = await searchEvidence({ ...criteria, limit: PAGE_SIZE });
      if (ticket !== latest.current) return;
      setView({ kind: "loaded", hits: page.hits, nextCursor: page.nextCursor, loadingMore: false, moreFailure: null });
    } catch (error) {
      if (ticket !== latest.current) return;
      setView({ kind: "error", failure: classifyFailure(error, navigator.onLine) });
    }
  }

  async function loadMore() {
    const criteria = submitted.current;
    if (!criteria || view.kind !== "loaded" || !view.nextCursor || view.loadingMore) return;
    const ticket = ++latest.current;
    const { hits, nextCursor } = view;
    setView({ kind: "loaded", hits, nextCursor, loadingMore: true, moreFailure: null });
    try {
      const page = await searchEvidence({ ...criteria, limit: PAGE_SIZE, cursor: nextCursor });
      if (ticket !== latest.current) return;
      const known = new Set(hits.map((hit) => hit.evidenceRef));
      const fresh = page.hits.filter((hit) => !known.has(hit.evidenceRef));
      setView({
        kind: "loaded",
        hits: [...hits, ...fresh],
        nextCursor: page.nextCursor,
        loadingMore: false,
        moreFailure: null,
      });
    } catch (error) {
      if (ticket !== latest.current) return;
      setView({
        kind: "loaded",
        hits,
        nextCursor,
        loadingMore: false,
        moreFailure: classifyFailure(error, navigator.onLine),
      });
    }
  }

  return (
    <AppSurface
      title="Evidence search"
      subtitle="Search verified evidence for your organization and inspect where each result comes from."
    >
      <form className="panel es-form" role="search" aria-label="Evidence search" onSubmit={(event) => void runSearch(event)}>
        <div className="es-field es-field-wide">
          <label htmlFor="es-q">Query</label>
          <input
            id="es-q"
            name="q"
            type="search"
            value={q}
            maxLength={MAX_QUERY_LENGTH}
            onChange={(event) => setQ(event.target.value)}
            aria-describedby={formError ? "es-form-error" : undefined}
            aria-invalid={formError ? true : undefined}
            autoComplete="off"
          />
        </div>
        <div className="es-field">
          <label htmlFor="es-freshness">Freshness</label>
          <select
            id="es-freshness"
            value={freshness}
            onChange={(event) => setFreshness(event.target.value as FreshnessLabel | "")}
          >
            <option value="">Any (each result labelled)</option>
            {FRESHNESS_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option.charAt(0) + option.slice(1).toLowerCase()}
              </option>
            ))}
          </select>
        </div>
        <div className="es-field">
          <label htmlFor="es-jurisdiction">Jurisdiction</label>
          <input id="es-jurisdiction" value={jurisdiction} maxLength={128} onChange={(event) => setJurisdiction(event.target.value)} />
        </div>
        <div className="es-field">
          <label htmlFor="es-type">Source type</label>
          <input id="es-type" value={type} maxLength={128} onChange={(event) => setType(event.target.value)} />
        </div>
        <div className="es-field es-field-action">
          <button type="submit" disabled={view.kind === "loading"}>
            Search
          </button>
        </div>
        {formError ? (
          <p id="es-form-error" className="notice-card notice-danger" role="alert">
            {formError}
          </p>
        ) : null}
      </form>

      <div className="es-layout">
        <section aria-labelledby="es-results-title">
          <h2 id="es-results-title" className="panel-title">
            Results
          </h2>
          <EvidenceResults
            view={view}
            selectedRef={selected?.evidenceRef ?? null}
            onSelect={setSelected}
            onMore={() => void loadMore()}
            signInHref={signInHref}
          />
        </section>
        <CitationInspector hit={selected} />
      </div>
    </AppSurface>
  );
}
