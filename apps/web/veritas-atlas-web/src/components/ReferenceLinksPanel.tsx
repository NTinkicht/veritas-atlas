import { Link } from "react-router-dom";

type RefItem = {
  label: string;
  route: string;
};

export function ReferenceLinksPanel({
  items,
}: {
  items: RefItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>Reference Links</h3>
      <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
        {items.map((item) => (
          <Link key={item.route} to={item.route}>
            {item.label}
          </Link>
        ))}
      </div>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 14,
  padding: 16,
};