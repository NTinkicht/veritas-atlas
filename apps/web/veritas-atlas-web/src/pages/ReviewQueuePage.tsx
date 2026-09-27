import { useCallback, useEffect, useState } from "react";
import { apiGet } from "../api/http";
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

type CasesResponse = { totalCount: number };
type ReviewBucket = { label: string; status: "InReview" | "OnHold"; count: number };

const REVIEW_BUCKETS: Array<Omit<ReviewBucket, "count">> = [
  { label: "In review", status: "InReview" },
  { label: "On hold", status: "OnHold" },
];

export function ReviewQueuePage() {
  const links = useFrontendNav();
  const [buckets, setBuckets] = useState<ReviewBucket[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadQueue = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await Promise.all(
        REVIEW_BUCKETS.map(async (bucket) => {
          const query = new URLSearchParams({ page: "1", pageSize: "1", status: bucket.status });
          const data = await apiGet<CasesResponse>(`/api/v1/cases?${query.toString()}`, false);
          return { ...bucket, count: Number.isFinite(data.totalCount) ? data.totalCount : 0 };
        }),
      );
      setBuckets(next);
    } catch (cause) {
      setBuckets([]);
      setError(cause instanceof Error ? cause.message : "Unable to load review queue");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadQueue(); }, [loadQueue]);

  const total = buckets.reduce((sum, bucket) => sum + bucket.count, 0);

  return (
    <AppSurface title="Review Queue" subtitle="Live review workload from the case service." links={links}>
      <section aria-labelledby="review-queue-heading">
        <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
          <h2 id="review-queue-heading">Current review workload</h2>
          <button type="button" onClick={() => void loadQueue()} disabled={loading}>
            {loading ? "Refreshing…" : "Refresh"}
          </button>
        </div>
        <p aria-live="polite">
          {loading ? "Loading review workload…" : error ? "Review workload unavailable." : `${total} cases currently require review attention.`}
        </p>
        {error && <p role="alert">{error}</p>}
        {!loading && !error && (
          <dl style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 16 }}>
            {buckets.map(({ label, status, count }) => (
              <div key={status} style={{ border: "1px solid currentColor", borderRadius: 8, padding: 16 }}>
                <dt>{label}</dt>
                <dd style={{ fontSize: "2rem", margin: "0.25rem 0 0" }}>{count}</dd>
              </div>
            ))}
          </dl>
        )}
      </section>
    </AppSurface>
  );
}
