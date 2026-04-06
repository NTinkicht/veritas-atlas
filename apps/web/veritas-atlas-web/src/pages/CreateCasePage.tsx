import { useState, type FormEvent, useEffect } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useCreateCase } from "../hooks/useCreateCase";
import { usePersons } from "../hooks/usePersons";

export function CreateCasePage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const prefilledPersonId = searchParams.get("personId") ?? "";

  const createCaseMutation = useCreateCase();
  const personsQuery = usePersons();

  const [title, setTitle] = useState("");
  const [summary, setSummary] = useState("");
  const [selectedPersonId, setSelectedPersonId] = useState(prefilledPersonId);
  const [manualPersonId, setManualPersonId] = useState("");

  useEffect(() => {
    if (prefilledPersonId) {
      setSelectedPersonId(prefilledPersonId);
    }
  }, [prefilledPersonId]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const resolvedSubjectPersonId =
      selectedPersonId || manualPersonId || null;

    createCaseMutation.mutate(
      {
        title,
        summary: summary || undefined,
        subjectPersonId: resolvedSubjectPersonId,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (createdCase) => {
          navigate(`/cases/${createdCase.id}`);
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Case</h1>
        <p style={{ color: "#555" }}>Create a new case in Veritas Atlas</p>
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

      {createCaseMutation.isError && (
        <p style={{ color: "crimson" }}>
          Failed to create case: {(createCaseMutation.error as Error).message}
        </p>
      )}

      {personsQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load persons: {(personsQuery.error as Error).message}
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
          <label style={labelStyle} htmlFor="title">Title</label>
          <input
            id="title"
            type="text"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            required
            style={inputStyle}
            placeholder="Enter case title"
          />
        </div>

        <div>
          <label style={labelStyle} htmlFor="summary">Summary</label>
          <textarea
            id="summary"
            value={summary}
            onChange={(e) => setSummary(e.target.value)}
            style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }}
            placeholder="Enter case summary"
          />
        </div>

        <div>
          <label style={labelStyle} htmlFor="subjectPersonSelect">
            Subject Person
          </label>

          {personsQuery.isLoading ? (
            <p>Loading persons...</p>
          ) : (
            <select
              id="subjectPersonSelect"
              value={selectedPersonId}
              onChange={(e) => setSelectedPersonId(e.target.value)}
              style={inputStyle}
            >
              <option value="">No selected person</option>
              {personsQuery.data?.items.map((person) => (
                <option key={person.id} value={person.id}>
                  {person.displayName} ({person.id})
                </option>
              ))}
            </select>
          )}
        </div>

        <div>
          <Link to="/persons/new" style={inlineLinkStyle}>
            Create a new person first
          </Link>
        </div>

        <div>
          <label style={labelStyle} htmlFor="manualPersonId">
            Manual Subject Person Id (optional override)
          </label>
          <input
            id="manualPersonId"
            type="text"
            value={manualPersonId}
            onChange={(e) => setManualPersonId(e.target.value)}
            style={inputStyle}
            placeholder="Optional Guid"
          />
          <p style={{ color: "#666", marginTop: "8px", marginBottom: 0 }}>
            If you provide a manual Guid, it is used only when no dropdown selection is chosen.
          </p>
        </div>

        <div style={{ display: "flex", gap: "12px" }}>
          <button type="submit" disabled={createCaseMutation.isPending} style={primaryButtonStyle}>
            {createCaseMutation.isPending ? "Creating..." : "Create Case"}
          </button>

          <Link to="/cases" style={secondaryLinkStyle}>
            Cancel
          </Link>
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

const secondaryLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};

const inlineLinkStyle: React.CSSProperties = {
  textDecoration: "underline",
};
