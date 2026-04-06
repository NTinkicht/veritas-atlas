import { useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { useCreateStatement } from "../hooks/useCreateStatement";

export function CreateStatementPage() {
  const [params] = useSearchParams();
  const initialEvidenceId = params.get("evidenceId") ?? "";

  const [evidenceId, setEvidenceId] = useState(initialEvidenceId);
  const [text, setText] = useState("");
  const [createdBy, setCreatedBy] = useState("");

  const mutation = useCreateStatement();

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    await mutation.mutateAsync({
      evidenceId,
      text,
      createdBy: createdBy || undefined,
    });

    setText("");
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "760px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Statement</h1>
        <p style={{ color: "#555" }}>Extract a statement from an evidence item.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
        </nav>
      </header>

      <form onSubmit={submit} style={{ display: "grid", gap: "16px" }}>
        <label style={labelStyle}>
          <span>Evidence Id</span>
          <input
            value={evidenceId}
            onChange={(e) => setEvidenceId(e.target.value)}
            style={inputStyle}
            placeholder="Enter evidence id"
            required
          />
        </label>

        <label style={labelStyle}>
          <span>Statement Text</span>
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            style={{ ...inputStyle, minHeight: "140px", resize: "vertical" }}
            placeholder="Enter extracted statement text"
            required
          />
        </label>

        <label style={labelStyle}>
          <span>Created By</span>
          <input
            value={createdBy}
            onChange={(e) => setCreatedBy(e.target.value)}
            style={inputStyle}
            placeholder="Optional creator"
          />
        </label>

        <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
          <button type="submit" style={buttonStyle} disabled={mutation.isPending}>
            {mutation.isPending ? "Creating..." : "Create Statement"}
          </button>
          <Link to="/statements" style={linkButtonStyle}>Open Statements</Link>
        </div>
      </form>

      {mutation.isError && (
        <p style={{ color: "crimson", marginTop: "16px" }}>
          Failed to create statement: {(mutation.error as Error).message}
        </p>
      )}

      {mutation.isSuccess && (
        <div style={successCardStyle}>
          <p style={{ marginTop: 0 }}><strong>Statement created successfully.</strong></p>
          <p style={{ marginBottom: "8px" }}>New statement id: {mutation.data.id}</p>
          <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
            <Link to={`/statements/${mutation.data.id}`}>Open Statement Detail</Link>
            {mutation.data.evidenceId && <Link to={`/evidence/${mutation.data.evidenceId}`}>Back to Evidence</Link>}
          </div>
        </div>
      )}
    </div>
  );
}

const labelStyle: React.CSSProperties = {
  display: "grid",
  gap: "8px",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
};

const buttonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  background: "#1976d2",
  color: "white",
  cursor: "pointer",
};

const linkButtonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const successCardStyle: React.CSSProperties = {
  marginTop: "20px",
  padding: "16px",
  border: "1px solid #d7e8d7",
  borderRadius: "12px",
  background: "#f8fff8",
};
