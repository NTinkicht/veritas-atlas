import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useCreatePerson } from "../hooks/useCreatePerson";

export function CreatePersonPage() {
  const navigate = useNavigate();
  const createPersonMutation = useCreatePerson();

  const [displayName, setDisplayName] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    createPersonMutation.mutate(
      {
        displayName,
        createdBy: "frontend-user",
      },
      {
        onSuccess: (createdPerson) => {
          navigate(`/cases/new?personId=${createdPerson.id}`);
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Create Person</h1>
        <p style={{ color: "#555" }}>Create a new person in Veritas Atlas</p>
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

      {createPersonMutation.isError && (
        <p style={{ color: "crimson" }}>
          Failed to create person: {(createPersonMutation.error as Error).message}
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
          <label style={labelStyle} htmlFor="displayName">Display Name</label>
          <input
            id="displayName"
            type="text"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
            style={inputStyle}
            placeholder="Enter person display name"
          />
        </div>

        <div style={{ display: "flex", gap: "12px" }}>
          <button type="submit" disabled={createPersonMutation.isPending} style={primaryButtonStyle}>
            {createPersonMutation.isPending ? "Creating..." : "Create Person"}
          </button>

          <Link to="/persons" style={secondaryLinkStyle}>
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
