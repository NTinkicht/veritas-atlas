import { useState, type FormEvent, useEffect } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useCreateEvidence } from "../hooks/useCreateEvidence";

type LocationState = {
  documentId?: string;
};

export function CreateEvidencePage() {
  const mutation = useCreateEvidence();
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state as LocationState | null) ?? null;

  const [documentId, setDocumentId] = useState(state?.documentId ?? "");
  const [quote, setQuote] = useState("");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (state?.documentId) {
      setDocumentId(state.documentId);
    }
  }, [state?.documentId]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    mutation.mutate(
      {
        documentId,
        quote,
        notes: notes || undefined,
        createdBy: "frontend-user",
      },
      {
        onSuccess: () => {
          navigate("/statements/new");
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Evidence</h1>
        <p style={{ color: "#555" }}>Create evidence in Veritas Atlas</p>
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
          Failed to create evidence: {(mutation.error as Error).message}
        </p>
      )}

      {mutation.isSuccess && (
        <p style={{ color: "green" }}>
          Evidence created: {mutation.data.id}
        </p>
      )}

      <form onSubmit={handleSubmit} style={formStyle}>
        <div>
          <label style={labelStyle} htmlFor="documentId">Document Id</label>
          <input id="documentId" value={documentId} onChange={(e) => setDocumentId(e.target.value)} required style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="quote">Quote</label>
          <textarea id="quote" value={quote} onChange={(e) => setQuote(e.target.value)} required style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="notes">Notes</label>
          <textarea id="notes" value={notes} onChange={(e) => setNotes(e.target.value)} style={{ ...inputStyle, minHeight: "100px", resize: "vertical" }} />
        </div>

        <button type="submit" disabled={mutation.isPending} style={buttonStyle}>
          {mutation.isPending ? "Creating..." : "Create Evidence"}
        </button>
      </form>

      <div style={{ marginTop: "24px", color: "#666" }}>
        Next step after success: create a statement from the evidence.
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
