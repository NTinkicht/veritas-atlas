type GovernanceCheck = {
  label: string;
  status: string;
};

export function PublicationGovernancePanel({
  checks,
}: {
  checks: GovernanceCheck[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Publication Governance</h3>
      <ul style={{ marginBottom: 0 }}>
        {checks.map((check) => (
          <li key={check.label}>
            {check.label} - {check.status}
          </li>
        ))}
      </ul>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};