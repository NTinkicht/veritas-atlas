import { ScenarioSnapshotPanel } from "../components/ScenarioSnapshotPanel";
import { useResetScenarioState, useScenarioSnapshot } from "../hooks/usePersistence";

export function PersistenceConsolePage() {
  const snapshotQuery = useScenarioSnapshot();
  const resetMutation = useResetScenarioState();

  if (snapshotQuery.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading persistence console...</div>;
  }

  if (snapshotQuery.isError || !snapshotQuery.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load persistence console.</div>;
  }

  const error = (resetMutation.error as Error | null)?.message;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Persistence Console</h1>
      <p style={{ color: "#555" }}>
        Snapshot and reset surface for the persisted seeded lifecycle scenario.
      </p>

      <div style={{ display: "grid", gap: 16 }}>
        <ScenarioSnapshotPanel snapshot={snapshotQuery.data} />

        <div style={panelStyle}>
          <button
            onClick={() => resetMutation.mutate()}
            disabled={resetMutation.isPending}
            style={buttonStyle}
          >
            {resetMutation.isPending ? "Resetting..." : "Reset Scenario State"}
          </button>

          {resetMutation.data && <p style={{ margin: 0 }}>{resetMutation.data.message}</p>}
          {error && <p style={{ margin: 0, color: "crimson" }}>{error}</p>}
        </div>
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
  display: "grid",
  gap: 12,
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};