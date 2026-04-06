import { useState, type FormEvent, type CSSProperties } from "react";
import { Link, useParams } from "react-router-dom";
import { usePersonDetail } from "../hooks/usePersonDetail";
import { useAddAlias } from "../hooks/useAddAlias";

export function PersonDetailPage() {
  const { id } = useParams<{ id: string }>();
  const personQuery = usePersonDetail(id);
  const addAliasMutation = useAddAlias();

  const [alias, setAlias] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!id || !alias.trim()) {
      return;
    }

    addAliasMutation.mutate(
      {
        personId: id,
        alias: alias.trim(),
      },
      {
        onSuccess: () => {
          setAlias("");
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Person Detail</h1>
        <p style={{ color: "#555" }}>Live person detail from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>

      {!id && <p style={{ color: "crimson" }}>No person id provided.</p>}

      {personQuery.isLoading && <p>Loading person detail...</p>}

      {personQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load person detail: {(personQuery.error as Error).message}
        </p>
      )}

      {addAliasMutation.isError && (
        <p style={{ color: "crimson" }}>
          Failed to add alias: {(addAliasMutation.error as Error).message}
        </p>
      )}

      {personQuery.isSuccess && (
        <div
          style={{
            border: "1px solid #ddd",
            borderRadius: "12px",
            padding: "20px",
            maxWidth: "900px",
          }}
        >
          <h2 style={{ marginTop: 0 }}>{personQuery.data.displayName}</h2>

          <div style={rowStyle}>
            <strong>Id</strong>
            <span>{personQuery.data.id}</span>
          </div>

          <div style={rowStyle}>
            <strong>Description</strong>
            <span>{personQuery.data.description ?? "N/A"}</span>
          </div>

          <div style={rowStyle}>
            <strong>Nationality</strong>
            <span>{personQuery.data.nationality ?? "N/A"}</span>
          </div>

          <div style={rowStyle}>
            <strong>Created</strong>
            <span>{formatDate(personQuery.data.createdAtUtc)}</span>
          </div>

          <div style={rowStyle}>
            <strong>Updated</strong>
            <span>{formatDate(personQuery.data.updatedAtUtc)}</span>
          </div>

          <div style={{ marginTop: "20px" }}>
            <strong>Aliases</strong>
            {personQuery.data.aliases.length === 0 ? (
              <p style={{ color: "#555" }}>No aliases yet.</p>
            ) : (
              <ul>
                {personQuery.data.aliases.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            )}
          </div>

          <div style={{ marginTop: "24px" }}>
            <Link to={`/cases/new?personId=${personQuery.data.id}`} style={actionLinkStyle}>
              Create Case For This Person
            </Link>
          </div>

          <form
            onSubmit={handleSubmit}
            style={{
              marginTop: "24px",
              display: "grid",
              gap: "12px",
              maxWidth: "420px",
            }}
          >
            <div>
              <label style={labelStyle} htmlFor="alias">Add Alias</label>
              <input
                id="alias"
                type="text"
                value={alias}
                onChange={(e) => setAlias(e.target.value)}
                style={inputStyle}
                placeholder="Enter alias"
              />
            </div>

            <button
              type="submit"
              disabled={addAliasMutation.isPending}
              style={buttonStyle}
            >
              {addAliasMutation.isPending ? "Adding..." : "Add Alias"}
            </button>
          </form>
        </div>
      )}
    </div>
  );
}

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

const rowStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "180px 1fr",
  gap: "12px",
  padding: "10px 0",
  borderBottom: "1px solid #eee",
};

const labelStyle: CSSProperties = {
  display: "block",
  marginBottom: "8px",
  fontWeight: 600,
};

const inputStyle: CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const buttonStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  cursor: "pointer",
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
