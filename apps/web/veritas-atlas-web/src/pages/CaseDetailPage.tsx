import { Link, useParams } from "react-router-dom";
import { useState, type FormEvent, type CSSProperties } from "react";
import { useCaseDetail } from "../hooks/useCaseDetail";
import { useApproveCase, useRejectCase } from "../hooks/useCaseActions";
import { useCreateClaim } from "../hooks/useCreateClaim";
import { useCreateContradiction } from "../hooks/useCreateContradiction";

export function CaseDetailPage() {
  const { id } = useParams<{ id: string }>();
  const caseQuery = useCaseDetail(id);
  const approveMutation = useApproveCase();
  const rejectMutation = useRejectCase();
  const createClaimMutation = useCreateClaim();
  const createContradictionMutation = useCreateContradiction();

  const [statementId, setStatementId] = useState("");
  const [claimText, setClaimText] = useState("");
  const [claimPersonId, setClaimPersonId] = useState("");

  const [leftClaimId, setLeftClaimId] = useState("");
  const [rightClaimId, setRightClaimId] = useState("");
  const [contradictionType, setContradictionType] = useState("Direct");
  const [contradictionSummary, setContradictionSummary] = useState("");
  const [contradictionRationale, setContradictionRationale] = useState("");

  const isBusy =
    approveMutation.isPending ||
    rejectMutation.isPending ||
    createClaimMutation.isPending ||
    createContradictionMutation.isPending;

  function handleCreateClaim(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    createClaimMutation.mutate(
      {
        statementId,
        text: claimText,
        personId: claimPersonId || null,
        createdBy: "frontend-user",
      },
      {
        onSuccess: () => {
          setStatementId("");
          setClaimText("");
          setClaimPersonId("");
        },
      }
    );
  }

  function handleCreateContradiction(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!id) {
      return;
    }

    createContradictionMutation.mutate(
      {
        caseId: id,
        leftClaimId,
        rightClaimId,
        type: contradictionType,
        summary: contradictionSummary,
        rationale: contradictionRationale || null,
        createdBy: "frontend-user",
      },
      {
        onSuccess: () => {
          setLeftClaimId("");
          setRightClaimId("");
          setContradictionSummary("");
          setContradictionRationale("");
        },
      }
    );
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Case Detail</h1>
        <p style={{ color: "#555" }}>Live case detail from Veritas Atlas API</p>
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

      {!id && <p style={{ color: "crimson" }}>No case id provided.</p>}

      {caseQuery.isLoading && <p>Loading case detail...</p>}

      {caseQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load case detail: {(caseQuery.error as Error).message}
        </p>
      )}

      {(approveMutation.isError || rejectMutation.isError || createClaimMutation.isError || createContradictionMutation.isError) && (
        <p style={{ color: "crimson" }}>
          Action failed.
        </p>
      )}

      {caseQuery.isSuccess && (
        <div
          style={{
            border: "1px solid #ddd",
            borderRadius: "12px",
            padding: "20px",
            maxWidth: "1100px",
          }}
        >
          <h2 style={{ marginTop: 0 }}>{caseQuery.data.title}</h2>

          <div style={actionsStyle}>
            <button
              style={approveButtonStyle}
              onClick={() => id && approveMutation.mutate(id)}
              disabled={isBusy}
            >
              {approveMutation.isPending ? "Approving..." : "Approve"}
            </button>

            <button
              style={rejectButtonStyle}
              onClick={() => id && rejectMutation.mutate(id)}
              disabled={isBusy}
            >
              {rejectMutation.isPending ? "Rejecting..." : "Reject"}
            </button>
          </div>

          <div style={rowStyle}>
            <strong>Id</strong>
            <span>{caseQuery.data.id}</span>
          </div>

          <div style={rowStyle}>
            <strong>Status</strong>
            <span>{caseQuery.data.status}</span>
          </div>

          <div style={rowStyle}>
            <strong>Subject Person Id</strong>
            <span>{caseQuery.data.subjectPersonId ?? "N/A"}</span>
          </div>

          <div style={rowStyle}>
            <strong>Created</strong>
            <span>{formatDate(caseQuery.data.createdAtUtc)}</span>
          </div>

          <div style={rowStyle}>
            <strong>Updated</strong>
            <span>{formatDate(caseQuery.data.updatedAtUtc)}</span>
          </div>

          <div style={{ marginTop: "20px" }}>
            <strong>Summary</strong>
            <p style={{ marginTop: "8px", color: "#555" }}>
              {caseQuery.data.summary ?? "No summary provided."}
            </p>
          </div>

          <section style={sectionStyle}>
            <h3>Create Claim</h3>
            <form onSubmit={handleCreateClaim} style={formGridStyle}>
              <input
                value={statementId}
                onChange={(e) => setStatementId(e.target.value)}
                placeholder="Statement Id"
                style={inputStyle}
                required
              />
              <input
                value={claimText}
                onChange={(e) => setClaimText(e.target.value)}
                placeholder="Claim text"
                style={inputStyle}
                required
              />
              <input
                value={claimPersonId}
                onChange={(e) => setClaimPersonId(e.target.value)}
                placeholder="Person Id (optional)"
                style={inputStyle}
              />
              <button type="submit" disabled={createClaimMutation.isPending} style={buttonStyle}>
                {createClaimMutation.isPending ? "Creating..." : "Create Claim"}
              </button>
            </form>
          </section>

          <section style={sectionStyle}>
            <h3>Claims</h3>
            {caseQuery.data.claims.length === 0 ? (
              <p>No claims attached.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Material</th>
                  </tr>
                </thead>
                <tbody>
                  {caseQuery.data.claims.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.topic ?? "N/A"}</td>
                      <td style={tdStyle}>{item.isMaterial ? "Yes" : "No"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <section style={sectionStyle}>
            <h3>Create Contradiction</h3>
            <form onSubmit={handleCreateContradiction} style={formGridStyle}>
              <input
                value={leftClaimId}
                onChange={(e) => setLeftClaimId(e.target.value)}
                placeholder="Left Claim Id"
                style={inputStyle}
                required
              />
              <input
                value={rightClaimId}
                onChange={(e) => setRightClaimId(e.target.value)}
                placeholder="Right Claim Id"
                style={inputStyle}
                required
              />
              <input
                value={contradictionType}
                onChange={(e) => setContradictionType(e.target.value)}
                placeholder="Type"
                style={inputStyle}
                required
              />
              <input
                value={contradictionSummary}
                onChange={(e) => setContradictionSummary(e.target.value)}
                placeholder="Summary"
                style={inputStyle}
                required
              />
              <input
                value={contradictionRationale}
                onChange={(e) => setContradictionRationale(e.target.value)}
                placeholder="Rationale (optional)"
                style={inputStyle}
              />
              <button type="submit" disabled={createContradictionMutation.isPending} style={buttonStyle}>
                {createContradictionMutation.isPending ? "Creating..." : "Create Contradiction"}
              </button>
            </form>
          </section>

          <section style={sectionStyle}>
            <h3>Contradictions</h3>
            {caseQuery.data.contradictions.length === 0 ? (
              <p>No contradictions attached.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Severity</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Summary</th>
                  </tr>
                </thead>
                <tbody>
                  {caseQuery.data.contradictions.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.severity}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.summary}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <section style={sectionStyle}>
            <h3>Reviews</h3>
            {caseQuery.data.reviews.length === 0 ? (
              <p>No reviews attached.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Decision</th>
                    <th style={thStyle}>Reviewer</th>
                  </tr>
                </thead>
                <tbody>
                  {caseQuery.data.reviews.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.decision}</td>
                      <td style={tdStyle}>{item.reviewer}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <section style={sectionStyle}>
            <h3>Publications</h3>
            {caseQuery.data.publications.length === 0 ? (
              <p>No publications attached.</p>
            ) : (
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Channel</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Title</th>
                    <th style={thStyle}>Slug</th>
                  </tr>
                </thead>
                <tbody>
                  {caseQuery.data.publications.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>{item.channel}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.title}</td>
                      <td style={tdStyle}>{item.slug}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
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

const actionsStyle: CSSProperties = {
  display: "flex",
  gap: "12px",
  marginBottom: "20px",
};

const approveButtonStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #2e7d32",
  cursor: "pointer",
};

const rejectButtonStyle: CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #c62828",
  cursor: "pointer",
};

const sectionStyle: CSSProperties = {
  marginTop: "28px",
};

const tableStyle: CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "12px",
};

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

const formGridStyle: CSSProperties = {
  display: "grid",
  gap: "12px",
  maxWidth: "720px",
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
