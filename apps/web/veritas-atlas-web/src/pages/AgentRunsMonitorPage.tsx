export function AgentRunsMonitorPage() {
  return (
    <div style={{ padding: 24 }}>
      <h1>Agent Runs Monitor</h1>
      <ul>
        <li>Extraction Agent: running</li>
        <li>Contradiction Agent: idle</li>
        <li>Review Agent: pending</li>
      </ul>
    </div>
  );
}