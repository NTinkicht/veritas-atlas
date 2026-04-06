import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { useCreateStatement } from "../hooks/useCreateStatement";

export function CreateStatementPage() {
  const createStatementMutation = useCreateStatement();

  const [evidenceId, setEvidenceId] = useState("");
  const [text, setText] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    createStatementMutation.mutate({
      evidenceId,
      text,
      createdBy: "frontend-user",
    });
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Statement</h1>
        <p style={{ color: "#555" }}>Create a statement in Veritas Atlas</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/reviews">Reviews</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>

      {createStatementMutation.isError && (
        <p style={{ color: "crimson" }}>
          Failed to create statement: {(createStatementMutation.error as Error).message}
        </p>
      )}

      {createStatementMutation.isSuccess && (
        <p style={{ color: "green" }}>
          Statement created: {createStatementMutation.data.id}
        </p>
      )}

      <form
        onSubmit={handleSubmit}
        style={{
          display: "grid",
          gap: "16px",
          maxWidth: "720px",
          border: "1px solid #ddd",
          borderRadius: "12px",
          padding: "20px",
        }}
      >
        <div>
          <label style={labelStyle} htmlFor="evidenceId">Evidence Id</label>
          <input
            id="evidenceId"
            type="text"
            value={evidenceId}
            onChange={(e) => setEvidenceId(e.target.value)}
            required
            style={inputStyle}
            placeholder="Enter evidence Guid"
          />
        </div>

        <div>
          <label style={labelStyle} htmlFor="text">Statement Text</label>
          <textarea
            id="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            required
            style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }}
            placeholder="Enter statement text"
          />
        </div>

        <div style={{ display: "flex", gap: "12px" }}>
          <button type="submit" disabled={createStatementMutation.isPending} style={primaryButtonStyle}>
            {createStatementMutation.isPending ? "Creating..." : "Create Statement"}
          </button>
        </div>
      </form>
    </div>
  );
}

const labelStyle: React.CSSProperties = {
  display: "block",
  marginBottom: "8px",
  fontWeight: 600,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const primaryButtonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  cursor: "pointer",
};
