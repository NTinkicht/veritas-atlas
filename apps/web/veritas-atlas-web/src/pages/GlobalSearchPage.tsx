import { useState } from "react";

export function GlobalSearchPage() {
  const [query, setQuery] = useState("");

  return (
    <div style={{ padding: 24 }}>
      <h1>Global Search</h1>

      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search claims, contradictions, evidence..."
        style={{ width: "100%", padding: 10, marginBottom: 20 }}
      />

      <div>
        <p>Search query: {query}</p>
        <p>Results coming from unified index (future backend integration)</p>
      </div>
    </div>
  );
}