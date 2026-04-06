export function AgentActivityPanel() {
  return (
    <div style={{ border: "1px solid #ccc", padding: 12, borderRadius: 8 }}>
      <h3>Agent Activity</h3>
      <ul>
        <li>Extraction Agent - idle</li>
        <li>Contradiction Agent - running</li>
        <li>Confidence Agent - idle</li>
      </ul>
    </div>
  );
}