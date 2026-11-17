import { useEffect, useRef, useState } from "react";
import { useLocation } from "react-router-dom";
import { classifyTenantSourcesFailure, listTenantSources } from "../api/tenantSources";
import { AppSurface } from "../components/AppSurface";
import { TenantSourcesListView, type SourcesView } from "../components/TenantSourcesView";

export function SourcesPage() {
  const location = useLocation();
  const [view, setView] = useState<SourcesView>({ kind: "loading" });
  const alive = useRef(true);
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

  return (
    <AppSurface title="Sources" subtitle="The public-record sources registered for your organization. Read-only.">
      <TenantSourcesListView view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
