import { Link } from "react-router-dom";
import type { ContradictionItem } from "../api/contradictions";

export function ContradictionQueuePanel({ items }: { items: ContradictionItem[] }) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Contradiction Queue</h3>
      {items.length === 0 && <p>No contradiction items for this case.</p>}
      {items.length > 0 && (
        <ul style={{ marginBottom: 0 }}>
          {items.map((item) => (
            <li key={item.id}>
              <Link to={`/contradictions/${item.id}`}>{item.topic}</Link> - {item.severity} - {item.status}
            </li>
          ))}
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