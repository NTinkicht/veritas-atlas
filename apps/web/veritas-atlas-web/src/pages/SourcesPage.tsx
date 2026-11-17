import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { classifyFetchAttemptsFailure, listFetchAttempts } from "../api/tenantFetchAttempts";
import { classifyRawRetrievalsFailure, listRawRetrievals } from "../api/tenantRawRetrievals";
import { classifyTenantSourcesFailure, listTenantSources, type TenantSource } from "../api/tenantSources";
import { AppSurface } from "../components/AppSurface";
import { FetchAttemptsPanel, type AttemptsView } from "../components/FetchAttemptsView";
import { RawRetrievalsPanel, type RetrievalsView } from "../components/RawRetrievalsView";
import { TenantSourcesListView, type SourcesView } from "../components/TenantSourcesView";

export function SourcesPage() {
  const location = useLocation();
  const [view, setView] = useState<SourcesView>({ kind: "loading" });
  const [attempts, setAttempts] = useState<AttemptsView>({ kind: "idle" });
  const [retrievals, setRetrievals] = useState<RetrievalsView>({ kind: "idle" });
  const alive = useRef(true);
  const latest = useRef(0);
  const latestRetrievals = useRef(0);
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  useEffect(() => {
    alive.current = true;
    (async () => {
      try {
        const sources = await listTenantSources();
        if (alive.current) setView({ kind: "loaded", sources });
      } catch (error) {
        if (alive.current) setView({ kind: "error", failure: classifyTenantSourcesFailure(error, navigator.onLine) });
      }
    })();
    return () => {
      alive.current = false;
    };
  }, []);

  const viewAttempts = async (source: TenantSource) => {
    // Only the newest request may write: a slow answer for a previously selected source is dropped.
    const ticket = ++latest.current;
    setAttempts({ kind: "loading", sourceKey: source.canonicalKey });
    try {
      const rows = await listFetchAttempts(source.id);
      if (alive.current && ticket === latest.current) setAttempts({ kind: "loaded", sourceKey: source.canonicalKey, attempts: rows });
    } catch (error) {
      if (alive.current && ticket === latest.current) {
        setAttempts({ kind: "error", sourceKey: source.canonicalKey, failure: classifyFetchAttemptsFailure(error, navigator.onLine) });
      }
    }
  };

  const viewRetrievals = async (source: TenantSource) => {
    const ticket = ++latestRetrievals.current;
    setRetrievals({ kind: "loading", sourceKey: source.canonicalKey });
    try {
      const rows = await listRawRetrievals(source.id);
      if (alive.current && ticket === latestRetrievals.current) setRetrievals({ kind: "loaded", sourceKey: source.canonicalKey, retrievals: rows });
    } catch (error) {
      if (alive.current && ticket === latestRetrievals.current) {
        setRetrievals({ kind: "error", sourceKey: source.canonicalKey, failure: classifyRawRetrievalsFailure(error, navigator.onLine) });
      }
    }
  };

  return (
    <AppSurface title="Sources" subtitle="The public-record sources registered for your organization. Read-only.">
      <TenantSourcesListView view={view} signInHref={signInHref} onViewAttempts={viewAttempts} onViewRetrievals={viewRetrievals} />
      <FetchAttemptsPanel view={attempts} signInHref={signInHref} />
      <RawRetrievalsPanel view={retrievals} signInHref={signInHref} />
    </AppSurface>
  );
}
