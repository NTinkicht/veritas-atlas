import { useState, type FormEvent, useEffect } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useCreateDocument } from "../hooks/useCreateDocument";

type LocationState = {
  sourceId?: string;
};

export function CreateDocumentPage() {
  const mutation = useCreateDocument();
  const navigate = useNavigate();
  const location = useLocation();
  const state = (location.state as LocationState | null) ?? null;

  const [sourceId, setSourceId] = useState(state?.sourceId ?? "");
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  const [language, setLanguage] = useState("");
  const [externalReference, setExternalReference] = useState("");

  useEffect(() => {
    if (state?.sourceId) {
      setSourceId(state.sourceId);
    }
  }, [state?.sourceId]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    mutation.mutate(
      {
        sourceId,
        title,
        content,
        language: language || undefined,
        externalReference: externalReference || undefined,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (document) => {
          navigate("/evidence/new", {
            state: {
              documentId: document.id,
            },
          });
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Document</h1>
        <p style={{ color: "#555" }}>Create a document in Veritas Atlas</p>
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
          Failed to create document: {(mutation.error as Error).message}
        </p>
      )}

      {mutation.isSuccess && (
        <p style={{ color: "green" }}>
          Document created: {mutation.data.id}
        </p>
      )}

      <form onSubmit={handleSubmit} style={formStyle}>
        <div>
          <label style={labelStyle} htmlFor="sourceId">Source Id</label>
          <input id="sourceId" value={sourceId} onChange={(e) => setSourceId(e.target.value)} required style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="title">Title</label>
          <input id="title" value={title} onChange={(e) => setTitle(e.target.value)} required style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="content">Content</label>
          <textarea id="content" value={content} onChange={(e) => setContent(e.target.value)} required style={{ ...inputStyle, minHeight: "140px", resize: "vertical" }} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="language">Language</label>
          <input id="language" value={language} onChange={(e) => setLanguage(e.target.value)} style={inputStyle} />
        </div>

        <div>
          <label style={labelStyle} htmlFor="externalReference">External Reference</label>
          <input id="externalReference" value={externalReference} onChange={(e) => setExternalReference(e.target.value)} style={inputStyle} />
        </div>

        <button type="submit" disabled={mutation.isPending} style={buttonStyle}>
          {mutation.isPending ? "Creating..." : "Create Document"}
        </button>
      </form>

      <div style={{ marginTop: "24px", color: "#666" }}>
        Next step after success: create evidence for this document.
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
