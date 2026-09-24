import { type FormEvent, useCallback, useEffect, useState } from "react";
import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

type CaseItem = {
  id: string;
  title: string;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

type CasesResponse = {
  items: CaseItem[];
  page: number;
  totalCount: number;
  totalPages: number;
};

const PAGE_SIZE = 20;

export function CaseExplorerPage() {
  const links = useFrontendNav();
  const [cases, setCases] = useState<CaseItem[]>([]);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadCases = useCallback(
    async (
      requestedPage: number,
      requestedSearch = search,
      requestedStatus = status,
    ) => {
      setLoading(true);
      setError(null);
      const query = new URLSearchParams({
        page: String(requestedPage),
        pageSize: String(PAGE_SIZE),
        sortBy: "UpdatedAt",
        sortDirection: "desc",
      });
      if (requestedSearch.trim()) query.set("search", requestedSearch.trim());
      if (requestedStatus) query.set("status", requestedStatus);

      try {
        const response = await fetch(`/api/v1/cases?${query.toString()}`, {
          credentials: "same-origin",
          headers: { Accept: "application/json" },
        });
        if (!response.ok) {
          throw new Error(`Cases request failed (${response.status})`);
        }
        const data = (await response.json()) as CasesResponse;
        setCases(data.items ?? []);
        setPage(data.page);
        setTotalPages(Math.max(1, data.totalPages));
        setTotalCount(data.totalCount);
      } catch (caught) {
        setCases([]);
        setError(caught instanceof Error ? caught.message : "Unable to load cases");
      } finally {
        setLoading(false);
      }
    },
    [search, status],
  );

  useEffect(() => {
    void loadCases(1, "", "");
  }, [loadCases]);

  function applyFilters(event: FormEvent) {
    event.preventDefault();
    void loadCases(1);
  }

  function clearFilters() {
    setSearch("");
    setStatus("");
    void loadCases(1, "", "");
  }

  return (
    <AppSurface
      title="Case Explorer"
      subtitle="Search and review the latest case activity from the live case service."
      links={links}
    >
      <form onSubmit={applyFilters} aria-label="Case filters">
        <label>
          Search
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Title or case text"
            type="search"
          />
        </label>
        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="">All statuses</option>
            <option value="Open">Open</option>
            <option value="Approved">Approved</option>
            <option value="Rejected">Rejected</option>
          </select>
        </label>
        <button type="submit" disabled={loading}>Apply</button>
        <button type="button" onClick={clearFilters} disabled={loading && !search && !status}>Clear</button>
      </form>

      <p aria-live="polite">
        {loading ? "Loading cases…" : `${totalCount} case${totalCount === 1 ? "" : "s"} found`}
      </p>
      {error ? <p role="alert">{error}</p> : null}

      {!loading && !error && cases.length === 0 ? (
        <p>No cases match these filters.</p>
      ) : (
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                <th scope="col">Case</th>
                <th scope="col">Status</th>
                <th scope="col">Created</th>
                <th scope="col">Updated</th>
              </tr>
            </thead>
            <tbody>
              {cases.map((item) => (
                <tr key={item.id}>
                  <td><strong>{item.title}</strong><br /><small>{item.id}</small></td>
                  <td>{item.status}</td>
                  <td>{formatDate(item.createdAtUtc)}</td>
                  <td>{formatDate(item.updatedAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <nav aria-label="Case pages">
        <button type="button" disabled={loading || page <= 1} onClick={() => void loadCases(page - 1)}>Previous</button>
        <span>Page {page} of {totalPages}</span>
        <button type="button" disabled={loading || page >= totalPages} onClick={() => void loadCases(page + 1)}>Next</button>
      </nav>
    </AppSurface>
  );
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
