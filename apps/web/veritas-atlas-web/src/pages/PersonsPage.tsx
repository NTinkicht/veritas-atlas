import { Link } from "react-router-dom";
import type { CSSProperties } from "react";
import { usePersons } from "../hooks/usePersons";
import type { PersonItem } from "../api/persons";

export function PersonsPage() {
  const personsQuery = usePersons();

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Persons</h1>
        <p style={{ color: "#555" }}>Live people directory from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to="/persons/new" style={actionLinkStyle}>Create New Person</Link>
        <Link to="/cases/new" style={actionLinkStyle}>Create New Case</Link>
      </div>

      {personsQuery.isLoading && <p>Loading persons...</p>}

      {personsQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load persons: {(personsQuery.error as Error).message}
        </p>
      )}

      {personsQuery.isSuccess && (
        <>
          <p>Total persons: {personsQuery.data.totalCount}</p>

          {personsQuery.data.items.length === 0 ? (
            <p>No persons found.</p>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table
                style={{
                  width: "100%",
                  borderCollapse: "collapse",
                  marginTop: "16px",
                }}
              >
                <thead>
                  <tr>
                    <th style={thStyle}>Display Name</th>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Aliases</th>
                    <th style={thStyle}>Nationality</th>
                    <th style={thStyle}>Created</th>
                    <th style={thStyle}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {personsQuery.data.items.map((person: PersonItem) => (
                    <tr key={person.id}>
                      <td style={tdStyle}>{person.displayName}</td>
                      <td style={tdStyle}>{person.id}</td>
                      <td style={tdStyle}>
                        {person.aliases.length > 0 ? person.aliases.join(", ") : "N/A"}
                      </td>
                      <td style={tdStyle}>{person.nationality ?? "N/A"}</td>
                      <td style={tdStyle}>{formatDate(person.createdAtUtc)}</td>
                      <td style={tdStyle}>
                        <div style={actionsStyle}>
                          <Link to={`/cases/new?personId=${person.id}`} style={inlineActionLinkStyle}>
                            Create Case
                          </Link>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const thStyle: CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const actionsStyle: CSSProperties = {
  display: "flex",
  gap: "8px",
  flexWrap: "wrap",
};

const actionLinkStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const inlineActionLinkStyle: CSSProperties = {
  textDecoration: "underline",
};
