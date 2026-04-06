import { Link } from "react-router-dom";

type CatalogItem = {
  label: string;
  route: string;
  description: string;
};

export function AppSurfaceCatalogPanel({
  items,
}: {
  items: CatalogItem[];
}) {
  return (
    <div style={panelStyle}>
      <h3 style={{ marginTop: 0 }}>App Surface Catalog</h3>
      <div style={gridStyle}>
        {items.map((item) => (
          <Link key={item.route} to={item.route} style={cardStyle}>
            <strong>{item.label}</strong>
            <span>{item.route}</span>
            <p style={{ margin: 0, color: "#555" }}>{item.description}</p>
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

const gridStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
  gap: 12,
};

const cardStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: 12,
  padding: 14,
  display: "grid",
  gap: 6,
  textDecoration: "none",
  color: "inherit",
};