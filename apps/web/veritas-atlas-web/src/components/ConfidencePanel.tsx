export function ConfidencePanel({
  score,
}: {
  score: number;
}) {
  return (
    <div style={{ border: "1px solid #ddd", padding: 16, borderRadius: 12 }}>
      <h3>Confidence Score</h3>
      <p style={{ fontSize: 28, margin: 0 }}>{score}%</p>
    </div>
  );
}