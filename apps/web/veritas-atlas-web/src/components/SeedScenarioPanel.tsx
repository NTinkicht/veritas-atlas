export function SeedScenarioPanel({
  onSeed,
  isPending,
  message,
}: {
  onSeed: () => void;
  isPending: boolean;
  message?: string;
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Seed Scenario</h3>
      <button onClick={onSeed} disabled={isPending} style={buttonStyle}>
        {isPending ? "Seeding..." : "Seed Lifecycle Scenario"}
      </button>
      {message && <p style={{ marginTop: 12, marginBottom: 0 }}>{message}</p>}
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};

const buttonStyle: React.CSSProperties = {
  border: "1px solid #bbb",
  borderRadius: 10,
  padding: "10px 14px",
  background: "white",
  cursor: "pointer",
  font: "inherit",
};