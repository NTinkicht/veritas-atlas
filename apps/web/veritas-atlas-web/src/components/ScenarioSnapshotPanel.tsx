import type { ScenarioSnapshotResponse } from "../api/contracts";

export function ScenarioSnapshotPanel({
  snapshot,
}: {
  snapshot: ScenarioSnapshotResponse;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Scenario Snapshot</h3>
      {!snapshot.exists && <p style={{ marginBottom: 0 }}>No snapshot found.</p>}
      {snapshot.exists && (
        <ul style={{ marginBottom: 0 }}>
          <li>Case Id: {snapshot.caseId}</li>
          <li>Claim A Id: {snapshot.claimAId}</li>
          <li>Claim B Id: {snapshot.claimBId}</li>
          <li>Contradiction Id: {snapshot.contradictionId}</li>
          <li>Created: {snapshot.createdAtUtc}</li>
          <li>Case Status: {snapshot.caseStatus}</li>
          <li>Contradiction Status: {snapshot.contradictionStatus}</li>
        </ul>
      )}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};