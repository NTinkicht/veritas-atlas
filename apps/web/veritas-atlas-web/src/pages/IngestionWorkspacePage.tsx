import { useState, type CSSProperties, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { useCreateSource } from "../hooks/useCreateSource";
import { useCreateDocument } from "../hooks/useCreateDocument";
import { useCreateEvidence } from "../hooks/useCreateEvidence";
import { useCreateStatement } from "../hooks/useCreateStatement";

export function IngestionWorkspacePage() {
  const createSourceMutation = useCreateSource();
  const createDocumentMutation = useCreateDocument();
  const createEvidenceMutation = useCreateEvidence();
  const createStatementMutation = useCreateStatement();

  const [sourceId, setSourceId] = useState("");
  const [documentId, setDocumentId] = useState("");
  const [evidenceId, setEvidenceId] = useState("");

  const [sourceName, setSourceName] = useState("");
  const [sourceType, setSourceType] = useState("Article");
  const [sourceReference, setSourceReference] = useState("");

  const [documentTitle, setDocumentTitle] = useState("");
  const [documentContent, setDocumentContent] = useState("");
  const [documentLanguage, setDocumentLanguage] = useState("");
  const [documentExternalReference, setDocumentExternalReference] = useState("");

  const [evidenceQuote, setEvidenceQuote] = useState("");
  const [evidenceNotes, setEvidenceNotes] = useState("");

  const [statementText, setStatementText] = useState("");

  function handleCreateSource(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    createSourceMutation.mutate(
      {
        name: sourceName,
        type: sourceType,
        reference: sourceReference || undefined,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (source) => {
          setSourceId(source.id);
        },
      }
    );
  }

  function handleCreateDocument(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    createDocumentMutation.mutate(
      {
        sourceId,
        title: documentTitle,
        content: documentContent,
        language: documentLanguage || undefined,
        externalReference: documentExternalReference || undefined,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (document) => {
          setDocumentId(document.id);
        },
      }
    );
  }

  function handleCreateEvidence(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    createEvidenceMutation.mutate(
      {
        documentId,
        quote: evidenceQuote,
        notes: evidenceNotes || undefined,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (evidence) => {
          setEvidenceId(evidence.id);
        },
      }
    );
  }

  function handleCreateStatement(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!evidenceId) {
      return;
    }

    createStatementMutation.mutate({
      evidenceId,
      text: statementText,
      createdBy: "frontend-user",
    });
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Ingestion Workspace</h1>
        <p style={{ color: "#555" }}>
          One operational workspace for source, document, evidence, and statement creation
        </p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources/new">New Source</Link>
          <Link to="/documents/new">New Document</Link>
          <Link to="/evidence/new">New Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
          <Link to="/cases">Cases</Link>
        </nav>
      </header>

      <section style={statusPanelStyle}>
        <h2 style={{ marginTop: 0 }}>Chain Status</h2>
        <div style={statusGridStyle}>
          <StatusItem label="Source Id" value={sourceId} />
          <StatusItem label="Document Id" value={documentId} />
          <StatusItem label="Evidence Id" value={evidenceId} />
          <StatusItem label="Statement Status" value={createStatementMutation.isSuccess ? "Created" : "Pending"} />
        </div>
      </section>

      <div style={workspaceGridStyle}>
        <section style={cardStyle}>
          <h2>Create Source</h2>

          {createSourceMutation.isError && (
            <p style={errorStyle}>
              Failed to create source: {(createSourceMutation.error as Error).message}
            </p>
          )}

          {createSourceMutation.isSuccess && (
            <p style={successStyle}>Created source: {createSourceMutation.data.id}</p>
          )}

          <form onSubmit={handleCreateSource} style={formStyle}>
            <div>
              <label style={labelStyle} htmlFor="sourceName">Name</label>
              <input id="sourceName" value={sourceName} onChange={(e) => setSourceName(e.target.value)} required style={inputStyle} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="sourceType">Type</label>
              <select id="sourceType" value={sourceType} onChange={(e) => setSourceType(e.target.value)} style={inputStyle}>
                <option value="Article">Article</option>
                <option value="Interview">Interview</option>
                <option value="Report">Report</option>
                <option value="Speech">Speech</option>
                <option value="SocialPost">SocialPost</option>
                <option value="Other">Other</option>
              </select>
            </div>

            <div>
              <label style={labelStyle} htmlFor="sourceReference">Reference</label>
              <input id="sourceReference" value={sourceReference} onChange={(e) => setSourceReference(e.target.value)} style={inputStyle} />
            </div>

            <button type="submit" disabled={createSourceMutation.isPending} style={buttonStyle}>
              {createSourceMutation.isPending ? "Creating..." : "Create Source"}
            </button>
          </form>
        </section>

        <section style={cardStyle}>
          <h2>Create Document</h2>

          {createDocumentMutation.isError && (
            <p style={errorStyle}>
              Failed to create document: {(createDocumentMutation.error as Error).message}
            </p>
          )}

          {createDocumentMutation.isSuccess && (
            <p style={successStyle}>Created document: {createDocumentMutation.data.id}</p>
          )}

          <form onSubmit={handleCreateDocument} style={formStyle}>
            <div>
              <label style={labelStyle} htmlFor="documentSourceId">Source Id</label>
              <input id="documentSourceId" value={sourceId} onChange={(e) => setSourceId(e.target.value)} required style={inputStyle} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="documentTitle">Title</label>
              <input id="documentTitle" value={documentTitle} onChange={(e) => setDocumentTitle(e.target.value)} required style={inputStyle} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="documentContent">Content</label>
              <textarea id="documentContent" value={documentContent} onChange={(e) => setDocumentContent(e.target.value)} required style={{ ...inputStyle, minHeight: "130px", resize: "vertical" }} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="documentLanguage">Language</label>
              <input id="documentLanguage" value={documentLanguage} onChange={(e) => setDocumentLanguage(e.target.value)} style={inputStyle} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="documentExternalReference">External Reference</label>
              <input id="documentExternalReference" value={documentExternalReference} onChange={(e) => setDocumentExternalReference(e.target.value)} style={inputStyle} />
            </div>

            <button type="submit" disabled={createDocumentMutation.isPending} style={buttonStyle}>
              {createDocumentMutation.isPending ? "Creating..." : "Create Document"}
            </button>
          </form>
        </section>

        <section style={cardStyle}>
          <h2>Create Evidence</h2>

          {createEvidenceMutation.isError && (
            <p style={errorStyle}>
              Failed to create evidence: {(createEvidenceMutation.error as Error).message}
            </p>
          )}

          {createEvidenceMutation.isSuccess && (
            <p style={successStyle}>Created evidence: {createEvidenceMutation.data.id}</p>
          )}

          <form onSubmit={handleCreateEvidence} style={formStyle}>
            <div>
              <label style={labelStyle} htmlFor="evidenceDocumentId">Document Id</label>
              <input id="evidenceDocumentId" value={documentId} onChange={(e) => setDocumentId(e.target.value)} required style={inputStyle} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="evidenceQuote">Quote</label>
              <textarea id="evidenceQuote" value={evidenceQuote} onChange={(e) => setEvidenceQuote(e.target.value)} required style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="evidenceNotes">Notes</label>
              <textarea id="evidenceNotes" value={evidenceNotes} onChange={(e) => setEvidenceNotes(e.target.value)} style={{ ...inputStyle, minHeight: "100px", resize: "vertical" }} />
            </div>

            <button type="submit" disabled={createEvidenceMutation.isPending} style={buttonStyle}>
              {createEvidenceMutation.isPending ? "Creating..." : "Create Evidence"}
            </button>
          </form>
        </section>

        <section style={cardStyle}>
          <h2>Create Statement</h2>

          {createStatementMutation.isError && (
            <p style={errorStyle}>
              Failed to create statement: {(createStatementMutation.error as Error).message}
            </p>
          )}

          {createStatementMutation.isSuccess && (
            <p style={successStyle}>Created statement: {createStatementMutation.data.id}</p>
          )}

          <form onSubmit={handleCreateStatement} style={formStyle}>
            <div>
              <label style={labelStyle} htmlFor="statementEvidenceId">Evidence Id</label>
              <input id="statementEvidenceId" value={evidenceId} onChange={(e) => setEvidenceId(e.target.value)} required style={inputStyle} />
            </div>

            <div>
              <label style={labelStyle} htmlFor="statementText">Statement Text</label>
              <textarea id="statementText" value={statementText} onChange={(e) => setStatementText(e.target.value)} required style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }} />
            </div>

            <button type="submit" disabled={createStatementMutation.isPending} style={buttonStyle}>
              {createStatementMutation.isPending ? "Creating..." : "Create Statement"}
            </button>
          </form>
        </section>
      </div>
    </div>
  );
}

function StatusItem({ label, value }: { label: string; value: string }) {
  return (
    <div style={statusItemStyle}>
      <div style={{ color: "#666", marginBottom: "6px" }}>{label}</div>
      <div style={{ fontWeight: 700 }}>{value || "â€”"}</div>
    </div>
  );
}

const workspaceGridStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(360px, 1fr))",
  gap: "18px",
};

const statusPanelStyle: CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "18px",
  marginBottom: "24px",
};

const statusGridStyle: CSSProperties = {
  display: "grid",
  gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
  gap: "12px",
};

const statusItemStyle: CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "10px",
  padding: "12px",
};

const cardStyle: CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "20px",
};

const formStyle: CSSProperties = {
  display: "grid",
  gap: "14px",
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

const errorStyle: CSSProperties = {
  color: "crimson",
};

const successStyle: CSSProperties = {
  color: "green",
};
