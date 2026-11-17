import { useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import { classifyDetailFailure, getEvidenceDetail } from "../api/evidenceDetail";
import { AppSurface } from "../components/AppSurface";
import { EvidenceDetailBody, type DetailView } from "../components/EvidenceDetailView";

export function EvidenceDetailPage() {
  const { id } = useParams();
  const location = useLocation();
  const [view, setView] = useState<DetailView>({ kind: "loading" });
  const signInHref = `/login?returnTo=${encodeURIComponent(`${location.pathname}${location.search}`)}`;

  useEffect(() => {
    let alive = true;
    setView({ kind: "loading" });
    (async () => {
      try {
        if (!id) throw Object.assign(new Error("missing ref"), { status: 400 });
        const detail = await getEvidenceDetail(id);
        if (alive) setView({ kind: "loaded", detail });
      } catch (error) {
        if (alive) setView({ kind: "error", failure: classifyDetailFailure(error, navigator.onLine) });
      }
    })();
    return () => {
      alive = false;
    };
  }, [id]);

  return (
    <AppSurface title="Evidence detail" subtitle="Provenance, freshness and verified text of one evidence item for your organization.">
      <p>
        <Link to="/evidence-search">Back to evidence search</Link>
      </p>
      <EvidenceDetailBody view={view} signInHref={signInHref} />
    </AppSurface>
  );
}
