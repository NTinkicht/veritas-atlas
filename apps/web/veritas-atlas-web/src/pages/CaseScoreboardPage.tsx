import { useCallback, useEffect, useState } from "react";
import { apiGet } from "../api/http";
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

type CasesResponse = {
  totalCount: number;
};

type Score = {
  status: "Open" | "Approved" | "Rejected";
  count: number;
};

const STATUSES: Score["status"][] = ["Open", "Approved", "Rejected"];

export function CaseScoreboardPage() {
  const links = useFrontendNav();
  const [scores, setScores] = useState<Score[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadScores = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const nextScores = await Promise.all(
        STATUSES.map(async (status) => {
          const query = new URLSearchParams({ page: "1", pageSize: "1", status });
          const data = await apiGet<CasesResponse>(
            `/api/v1/cases?${query.toString()}`,
            false,
          );
          return { status, count: Number.isFinite(data.totalCount) ? data.totalCount : 0 };
        }),
      );
      setScores(nextScores);
    } catch (cause) {
      setScores([]);
      setError(cause instanceof Error ? cause.message : "Unable to load case scoreboard");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadScores();
  }, [loadScores]);

  const total = scores.reduce((sum, score) => sum + score.count, 0);

  return (
    <AppSurface
      title="Case Scoreboard"
      subtitle="Live case status totals from the case service."
      links={links}
    >
      <section aria-labelledby="case-scoreboard-heading">
        <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
          <h2 id="case-scoreboard-heading">Current case status</h2>
          <button type="button" onClick={() => void loadScores()} disabled={loading}>
            {loading ? "Refreshing…" : "Refresh"}
          </button>
        </div>

        <p aria-live="polite">
          {loading ? "Loading case totals…" : error ? "Case totals unavailable." : `${total} cases across tracked statuses.`}
        </p>
        {error && <p role="alert">{error}</p>}

        {!loading && !error && (
          <dl style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(160px, 1fr))", gap: 16 }}>
            {scores.map(({ status, count }) => (
              <div key={status} style={{ border: "1px solid currentColor", borderRadius: 8, padding: 16 }}>
                <dt>{status}</dt>
                <dd style={{ fontSize: "2rem", margin: "0.25rem 0 0" }}>{count}</dd>
              </div>
            ))}
          </dl>
        )}
      </section>
    </AppSurface>
  );
}