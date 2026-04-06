import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useCreateSource } from "../hooks/useCreateSource";

export function CreateSourcePage() {
  const mutation = useCreateSource();
  const navigate = useNavigate();

  const [name, setName] = useState("");
  const [type, setType] = useState("Article");
  const [reference, setReference] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    mutation.mutate(
      {
        name,
        type,
        reference: reference || undefined,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (source) => {
          navigate("/documents/new", {
            state: {
              sourceId: source.id,
            },
          });
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Source</h1>
        <p style={{ color: "#555" }}>Start the ingestion chain in Veritas Atlas</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/sources/new">New Source</Link>
          <Link to="/documents/new">New Document</Link>
          <Link to="/evidence/new">New Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
          <Link to="/cases">Cases</Link>
        </nav>
      </header>

      {mutation.isError && (
        <p style={{ color: "crimson" }}>
          Failed to create source: {(mutation.error as Error).message}
        </p>
      )}

      {mutation.isSuccess && (
        <p style={{ color: "green" }}>
          Source created: {mutation.data.id}
        </p>
      )}

      <form onSubmit={handleSubmit} style={formStyle}>
        <div>
          <label style={labelStyle} htmlFor="name">Name</label>
          <input id="name" value={name} onChange={(e) => setName(e.target.value)} required style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="type">Type</label>
          <select id="type" value={type} onChange={(e) => setType(e.target.value)} style={inputStyle}>
            <option value="Article">Article</option>
            <option value="Interview">Interview</option>
            <option value="Report">Report</option>
            <option value="Speech">Speech</option>
            <option value="SocialPost">SocialPost</option>
            <option value="Other">Other</option>
          </select>
        </div>

        <div>
          <label style={labelStyle} htmlFor="reference">Reference</label>
          <input id="reference" value={reference} onChange={(e) => setReference(e.target.value)} style={inputStyle} />
        </div>

        <button type="submit" disabled={mutation.isPending} style={buttonStyle}>
          {mutation.isPending ? "Creating..." : "Create Source"}
        </button>
      </form>

      <div style={{ marginTop: "24px", color: "#666" }}>
        Next step after success: create a document for this source.
      </div>
    </div>
  );
}

const formStyle: React.CSSProperties = {
  display: "grid",
  gap: "16px",
  maxWidth: "720px",
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "20px",
};

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

const buttonStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  cursor: "pointer",
};
