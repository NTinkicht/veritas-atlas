param(
    [string]$RootDir = "C:\Projects\veritas-atlas"
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Ensure-Directory received an empty path."
    }

    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [string]$Path,
        [string]$Content
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Write-Utf8File received an empty path."
    }

    $parent = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Could not resolve parent directory for path: $Path"
    }

    Ensure-Directory $parent
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Wrote: $Path" -ForegroundColor Green
}

$solutionPath = Join-Path $RootDir "VeritasAtlas.slnx"
$webRoot = Join-Path $RootDir "apps\web\veritas-atlas-web"

$claimsPagePath = Join-Path $webRoot "src\pages\ClaimsPage.tsx"
$claimDetailPagePath = Join-Path $webRoot "src\pages\ClaimDetailPage.tsx"
$claimsWorkspacePagePath = Join-Path $webRoot "src\pages\ClaimsWorkspacePage.tsx"
$statementDetailPagePath = Join-Path $webRoot "src\pages\StatementDetailPage.tsx"
$caseDetailPagePath = Join-Path $webRoot "src\pages\CaseDetailPage.tsx"
$contradictionsWorkspacePagePath = Join-Path $webRoot "src\pages\ContradictionsWorkspacePage.tsx"
$mainTsxPath = Join-Path $webRoot "src\main.tsx"

Push-Location $RootDir

Write-Host ""
Write-Host "Checkpointing current code with git..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "git add failed."
}

$commitMessage = "checkpoint before phase 6.14 - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
git commit -m $commitMessage 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "No new commit created. Continuing with phase 6.14." -ForegroundColor Yellow
}
else {
    Write-Host "Created git commit: $commitMessage" -ForegroundColor Green
}

Pop-Location

$claimsPageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useMemo } from "react";
import { useClaims } from "../hooks/useClaims";

export function ClaimsPage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? undefined;
  const topicFilter = params.get("topic") ?? "";
  const typeFilter = params.get("type") ?? "All";
  const materialFilter = params.get("material") ?? "All";

  const query = useClaims(statementId);
  const items = query.data?.items ?? [];

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      const matchesTopic =
        topicFilter.length === 0 ||
        item.topic.toLowerCase().includes(topicFilter.toLowerCase()) ||
        item.normalizedText.toLowerCase().includes(topicFilter.toLowerCase());

      const matchesType =
        typeFilter === "All" || item.type === typeFilter;

      const matchesMaterial =
        materialFilter === "All" ||
        (materialFilter === "Material" && item.isMaterial) ||
        (materialFilter === "NonMaterial" && !item.isMaterial);

      return matchesTopic && matchesType && matchesMaterial;
    });
  }, [items, topicFilter, typeFilter, materialFilter]);

  const types = useMemo(
    () => ["All", ...Array.from(new Set(items.map((x) => x.type))).sort()],
    [items]
  );

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims</h1>
        <p style={{ color: "#555" }}>Live claim catalog from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
        </nav>
      </header>

      <div style={{ marginBottom: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={statementId ? `/claims/workspace?statementId=${statementId}` : "/claims/workspace"} style={actionLinkStyle}>Open Claims Workspace</Link>
        <Link to="/contradictions/workspace" style={actionLinkStyle}>Open Contradictions Workspace</Link>
      </div>

      {statementId && (
        <div style={hintCardStyle}>
          <strong>Statement scope:</strong> {statementId}
        </div>
      )}

      <section style={filterPanelStyle}>
        <input value={topicFilter} readOnly style={inputStyle} placeholder="Use URL ?topic=... for deep link filtering" />
        <select value={typeFilter} disabled style={selectStyle}>
          {types.map((value) => (
            <option key={value} value={value}>{value}</option>
          ))}
        </select>
        <select value={materialFilter} disabled style={selectStyle}>
          <option value="All">All</option>
          <option value="Material">Material</option>
          <option value="NonMaterial">Non-material</option>
        </select>
      </section>

      {query.isLoading && <p>Loading claims...</p>}

      {query.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load claims: {(query.error as Error).message}
        </p>
      )}

      {query.isSuccess && (
        <>
          <p>Showing {filteredItems.length} of {query.data.totalCount} claims</p>

          {filteredItems.length === 0 ? (
            <div style={emptyStateStyle}>
              <p style={{ margin: 0 }}>No claims found.</p>
            </div>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table style={tableStyle}>
                <thead>
                  <tr>
                    <th style={thStyle}>Topic</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Material</th>
                    <th style={thStyle}>Statement</th>
                    <th style={thStyle}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredItems.map((item) => (
                    <tr key={item.id}>
                      <td style={tdStyle}><Link to={`/claims/${item.id}`}>{item.topic}</Link></td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.isMaterial ? "Yes" : "No"}</td>
                      <td style={tdStyle}><Link to={`/statements/${item.statementId}`}>{item.statementId}</Link></td>
                      <td style={tdStyle}>
                        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                          <Link to={`/claims/${item.id}`}>Detail</Link>
                          <Link to={`/claims/workspace?statementId=${item.statementId}`}>Workspace</Link>
                          <Link to={`/contradictions/workspace?claimId=${item.id}&statementId=${item.statementId}`}>Contradictions</Link>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

const tableStyle: React.CSSProperties = {
  width: "100%",
  borderCollapse: "collapse",
  marginTop: "16px",
};

const thStyle: React.CSSProperties = {
  textAlign: "left",
  borderBottom: "1px solid #ccc",
  padding: "10px",
};

const tdStyle: React.CSSProperties = {
  borderBottom: "1px solid #eee",
  padding: "10px",
  verticalAlign: "top",
};

const emptyStateStyle: React.CSSProperties = {
  border: "1px solid #eee",
  borderRadius: "12px",
  padding: "20px",
  color: "#666",
};

const hintCardStyle: React.CSSProperties = {
  marginBottom: "16px",
  padding: "12px 14px",
  border: "1px solid #d9e6ff",
  borderRadius: "12px",
  background: "#f8fbff",
};

const filterPanelStyle: React.CSSProperties = {
  display: "grid",
  gridTemplateColumns: "minmax(260px, 1fr) 180px 180px",
  gap: "12px",
  marginBottom: "16px",
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
  background: "#fafafa",
};

const selectStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  padding: "10px 12px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  font: "inherit",
  background: "#fafafa",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
  display: "inline-flex",
  alignItems: "center",
};
'@

$claimDetailPageContent = @'
import { Link, useParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";

export function ClaimDetailPage() {
  const { id } = useParams();
  const query = useClaimDetail(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading claim...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load claim: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Claim not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/claims">Back to Claims</Link>
        <Link to={`/statements/${item.statementId}`}>Statement</Link>
        {item.caseId && <Link to={`/cases/${item.caseId}`}>Case</Link>}
        <Link to={`/contradictions/workspace?claimId=${item.id}&statementId=${item.statementId}`}>Contradictions Workspace</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Claim</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Statement Id" value={item.statementId} />
        <Row label="Topic" value={item.topic} />
        <Row label="Normalized Text" value={item.normalizedText} />
        <Row label="Type" value={item.type} />
        <Row label="Status" value={item.status} />
        <Row label="Material" value={item.isMaterial ? "Yes" : "No"} />
        <Row label="Person Id" value={item.personId ?? "N/A"} />
        <Row label="Case Id" value={item.caseId ?? "N/A"} />
        <Row label="Created" value={new Date(item.createdAtUtc).toLocaleString()} />
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        <Link to={`/statements/${item.statementId}`} style={actionLinkStyle}>Open Statement</Link>
        <Link to={`/claims?statementId=${item.statementId}`} style={actionLinkStyle}>More Claims for Statement</Link>
        <Link to={`/contradictions/workspace?claimId=${item.id}&statementId=${item.statementId}`} style={actionLinkStyle}>Open Contradictions Workspace</Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$claimsWorkspacePageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useEffect, useState } from "react";
import { useStatementDetail } from "../hooks/useStatementDetail";
import { useClaims } from "../hooks/useClaims";
import { useCreateClaim } from "../hooks/useCreateClaim";

export function ClaimsWorkspacePage() {
  const [params] = useSearchParams();
  const statementId = params.get("statementId") ?? "";

  const statementQuery = useStatementDetail(statementId || undefined);
  const claimsQuery = useClaims(statementId || undefined);
  const createClaimMutation = useCreateClaim();

  const [topic, setTopic] = useState("");
  const [normalizedText, setNormalizedText] = useState("");
  const [type, setType] = useState("Factual");
  const [isMaterial, setIsMaterial] = useState(true);

  useEffect(() => {
    if (statementQuery.isSuccess && statementQuery.data) {
      setNormalizedText((current) => current || statementQuery.data.text);
      setTopic((current) => current || statementQuery.data.topic || "general");
    }
  }, [statementQuery.isSuccess, statementQuery.data]);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    await createClaimMutation.mutateAsync({
      statementId,
      topic,
      normalizedText,
      type,
      isMaterial,
    });
  };

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "980px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Claims Workspace</h1>
        <p style={{ color: "#555" }}>Create and inspect claims for a statement.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Statement Id" value={statementId || "N/A"} />
      </div>

      {statementId.length > 0 && statementQuery.isSuccess && statementQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Statement Context</h3>
          <Row label="Text" value={statementQuery.data.text} />
          <Row label="Topic" value={statementQuery.data.topic ?? "N/A"} />
          <Row label="Polarity" value={statementQuery.data.polarity} />
          <Row label="Status" value={statementQuery.data.status} />
          <div style={{ display: "flex", gap: "12px", flexWrap: "wrap", marginTop: "12px" }}>
            {statementQuery.data.evidenceId && (
              <Link to={`/evidence/${statementQuery.data.evidenceId}`} style={actionLinkStyle}>Open Evidence</Link>
            )}
            <Link to={`/claims?statementId=${statementQuery.data.id}`} style={actionLinkStyle}>Open Claims for Statement</Link>
            <Link to={`/contradictions/workspace?statementId=${statementQuery.data.id}`} style={actionLinkStyle}>Prepare Contradiction Review</Link>
          </div>
        </div>
      )}

      {statementId.length > 0 && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Create Claim</h3>
          <form onSubmit={submit} style={{ display: "grid", gap: "16px" }}>
            <label style={labelStyle}>
              <span>Topic</span>
              <input value={topic} onChange={(e) => setTopic(e.target.value)} style={inputStyle} required />
            </label>

            <label style={labelStyle}>
              <span>Normalized Text</span>
              <textarea value={normalizedText} onChange={(e) => setNormalizedText(e.target.value)} style={{ ...inputStyle, minHeight: "120px", resize: "vertical" }} required />
            </label>

            <label style={labelStyle}>
              <span>Type</span>
              <select value={type} onChange={(e) => setType(e.target.value)} style={inputStyle}>
                <option value="Factual">Factual</option>
                <option value="Temporal">Temporal</option>
                <option value="Quantitative">Quantitative</option>
                <option value="Categorical">Categorical</option>
                <option value="Attribution">Attribution</option>
              </select>
            </label>

            <label style={{ display: "flex", gap: "10px", alignItems: "center" }}>
              <input type="checkbox" checked={isMaterial} onChange={(e) => setIsMaterial(e.target.checked)} />
              <span>Material claim</span>
            </label>

            <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
              <button type="submit" style={buttonStyle} disabled={createClaimMutation.isPending}>
                {createClaimMutation.isPending ? "Creating..." : "Create Claim"}
              </button>
              <Link to={statementId ? `/claims?statementId=${statementId}` : "/claims"} style={linkButtonStyle}>Open Claims</Link>
            </div>
          </form>

          {createClaimMutation.isError && (
            <p style={{ color: "crimson", marginTop: "16px" }}>
              Failed to create claim: {(createClaimMutation.error as Error).message}
            </p>
          )}

          {createClaimMutation.isSuccess && (
            <div style={successCardStyle}>
              <p style={{ marginTop: 0 }}><strong>Claim created successfully.</strong></p>
              <p style={{ marginBottom: "8px" }}>New claim id: {createClaimMutation.data.id}</p>
              <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
                <Link to={`/claims/${createClaimMutation.data.id}`}>Open Claim Detail</Link>
                <Link to={`/claims?statementId=${createClaimMutation.data.statementId}`}>Open Claim List</Link>
                <Link to={`/contradictions/workspace?claimId=${createClaimMutation.data.id}&statementId=${createClaimMutation.data.statementId}`}>Open Contradictions Workspace</Link>
              </div>
            </div>
          )}
        </div>
      )}

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Claims for this Statement</h3>

        {claimsQuery.isLoading && <p>Loading claims...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims: {(claimsQuery.error as Error).message}</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length === 0 && <p>No claims yet.</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length > 0 && (
          <ul>
            {claimsQuery.data.items.map((claim) => (
              <li key={claim.id}>
                <Link to={`/claims/${claim.id}`}>{claim.topic}</Link> - {claim.type} - {claim.status}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

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

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};

const successCardStyle: React.CSSProperties = {
  marginTop: "20px",
  padding: "16px",
  border: "1px solid #d7e8d7",
  borderRadius: "12px",
  background: "#f8fff8",
};
'@

$statementDetailPageContent = @'
import { Link, useParams } from "react-router-dom";
import { useStatementDetail } from "../hooks/useStatementDetail";
import { useClaims } from "../hooks/useClaims";

export function StatementDetailPage() {
  const { id } = useParams();
  const query = useStatementDetail(id);
  const claimsQuery = useClaims(id);

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Loading statement...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", color: "crimson" }}>Failed to load statement: {(query.error as Error).message}</div>;
  }

  if (!query.data) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>Statement not found.</div>;
  }

  const item = query.data;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <nav style={{ display: "flex", gap: "16px", marginBottom: "20px", flexWrap: "wrap" }}>
        <Link to="/statements">Back to Statements</Link>
        {item.evidenceId && <Link to={`/evidence/${item.evidenceId}`}>Evidence</Link>}
        <Link to={`/claims/workspace?statementId=${item.id}`}>Claims Workspace</Link>
        <Link to={`/contradictions/workspace?statementId=${item.id}`}>Contradictions Workspace</Link>
      </nav>

      <h1 style={{ marginTop: 0 }}>Statement</h1>

      <div style={cardStyle}>
        <Row label="Id" value={item.id} />
        <Row label="Text" value={item.text} />
        <Row label="Topic" value={item.topic ?? "N/A"} />
        <Row label="Predicate" value={item.predicate ?? "N/A"} />
        <Row label="Object" value={item.object ?? "N/A"} />
        <Row label="Polarity" value={item.polarity} />
        <Row label="Status" value={item.status} />
        <Row label="Evidence Id" value={item.evidenceId ?? "N/A"} />
        <Row label="Person Id" value={item.personId ?? "N/A"} />
        <Row label="Created" value={new Date(item.createdAt).toLocaleString()} />
      </div>

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Claims</h3>
        <div style={{ display: "flex", gap: "12px", flexWrap: "wrap", marginBottom: "12px" }}>
          <Link to={`/claims/workspace?statementId=${item.id}`} style={actionLinkStyle}>Create Claim</Link>
          <Link to={`/claims?statementId=${item.id}`} style={actionLinkStyle}>Open Claims for Statement</Link>
          <Link to={`/contradictions/workspace?statementId=${item.id}`} style={actionLinkStyle}>Prepare Contradiction Review</Link>
        </div>

        {claimsQuery.isLoading && <p>Loading claims...</p>}
        {claimsQuery.isError && <p style={{ color: "crimson" }}>Failed to load claims: {(claimsQuery.error as Error).message}</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length === 0 && <p>No claims linked to this statement yet.</p>}
        {claimsQuery.isSuccess && claimsQuery.data.items.length > 0 && (
          <ul>
            {claimsQuery.data.items.map((claim) => (
              <li key={claim.id}>
                <Link to={`/claims/${claim.id}`}>{claim.topic}</Link> - {claim.type} - {claim.status}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$contradictionsWorkspacePageContent = @'
import { Link, useSearchParams } from "react-router-dom";
import { useClaimDetail } from "../hooks/useClaimDetail";
import { useStatementDetail } from "../hooks/useStatementDetail";

export function ContradictionsWorkspacePage() {
  const [params] = useSearchParams();
  const claimId = params.get("claimId") ?? "";
  const statementId = params.get("statementId") ?? "";

  const claimQuery = useClaimDetail(claimId || undefined);
  const statementQuery = useStatementDetail(statementId || undefined);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px", maxWidth: "980px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Contradictions Workspace</h1>
        <p style={{ color: "#555" }}>Operational preparation space for the contradiction slice.</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/statements">Statements</Link>
          {claimId && <Link to={`/claims/${claimId}`}>Claim</Link>}
          {statementId && <Link to={`/statements/${statementId}`}>Statement</Link>}
        </nav>
      </header>

      <div style={cardStyle}>
        <Row label="Claim Id" value={claimId || "N/A"} />
        <Row label="Statement Id" value={statementId || "N/A"} />
        <Row label="Workspace Status" value="Ready for contradiction implementation" />
      </div>

      {claimId && claimQuery.isSuccess && claimQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Claim Context</h3>
          <Row label="Topic" value={claimQuery.data.topic} />
          <Row label="Type" value={claimQuery.data.type} />
          <Row label="Status" value={claimQuery.data.status} />
          <Row label="Normalized Text" value={claimQuery.data.normalizedText} />
        </div>
      )}

      {statementId && statementQuery.isSuccess && statementQuery.data && (
        <div style={cardStyle}>
          <h3 style={{ marginTop: 0 }}>Statement Context</h3>
          <Row label="Text" value={statementQuery.data.text} />
          <Row label="Topic" value={statementQuery.data.topic ?? "N/A"} />
          <Row label="Polarity" value={statementQuery.data.polarity} />
          <Row label="Status" value={statementQuery.data.status} />
        </div>
      )}

      <div style={cardStyle}>
        <h3 style={{ marginTop: 0 }}>Planned contradiction actions</h3>
        <ul style={{ marginBottom: 0 }}>
          <li>Compare one claim against alternate claims and statements</li>
          <li>Classify contradiction type and severity</li>
          <li>Link contradiction results to case review workflow</li>
        </ul>
      </div>

      <div style={{ display: "flex", gap: "12px", flexWrap: "wrap" }}>
        {claimId && <Link to={`/claims/${claimId}`} style={actionLinkStyle}>Back to Claim</Link>}
        {statementId && <Link to={`/claims/workspace?statementId=${statementId}`} style={actionLinkStyle}>Open Claims Workspace</Link>}
        <Link to="/claims" style={actionLinkStyle}>Open Claims</Link>
      </div>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "180px 1fr", gap: "12px", padding: "6px 0" }}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  );
}

const cardStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: "12px",
  padding: "16px",
  marginBottom: "16px",
};

const actionLinkStyle: React.CSSProperties = {
  padding: "10px 16px",
  borderRadius: "8px",
  border: "1px solid #1976d2",
  textDecoration: "none",
  color: "inherit",
};
'@

$mainTsxContent = @'
import React from "react";
import ReactDOM from "react-dom/client";
import {
  createBrowserRouter,
  RouterProvider,
  Link,
} from "react-router-dom";
import {
  QueryClient,
  QueryClientProvider,
  useQuery,
} from "@tanstack/react-query";
import { getDatabaseHealth, getHealth } from "./api/health";
import { PersonsPage } from "./pages/PersonsPage";
import { PersonDetailPage } from "./pages/PersonDetailPage";
import { CasesPage } from "./pages/CasesPage";
import { CreateCasePage } from "./pages/CreateCasePage";
import { CreatePersonPage } from "./pages/CreatePersonPage";
import { CreateStatementPage } from "./pages/CreateStatementPage";
import { CreateDocumentPage } from "./pages/CreateDocumentPage";
import { CreateEvidencePage } from "./pages/CreateEvidencePage";
import { CreateSourcePage } from "./pages/CreateSourcePage";
import { IngestionWorkspacePage } from "./pages/IngestionWorkspacePage";
import { SourcesPage } from "./pages/SourcesPage";
import { SourceDetailPage } from "./pages/SourceDetailPage";
import { DocumentsPage } from "./pages/DocumentsPage";
import { DocumentDetailPage } from "./pages/DocumentDetailPage";
import { EvidencePage } from "./pages/EvidencePage";
import { EvidenceDetailPage } from "./pages/EvidenceDetailPage";
import { StatementsPage } from "./pages/StatementsPage";
import { StatementDetailPage } from "./pages/StatementDetailPage";
import { ClaimsPage } from "./pages/ClaimsPage";
import { ClaimDetailPage } from "./pages/ClaimDetailPage";
import { ClaimsWorkspacePage } from "./pages/ClaimsWorkspacePage";
import { ContradictionsWorkspacePage } from "./pages/ContradictionsWorkspacePage";
import { CaseDetailPage } from "./pages/CaseDetailPage";
import { ReviewsPage } from "./pages/ReviewsPage";
import { DashboardPage } from "./pages/DashboardPage";
import "./index.css";

const queryClient = new QueryClient();

function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Veritas Atlas</h1>
        <p style={{ color: "#555" }}>Frontend connected to live API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/persons">Persons</Link>
          <Link to="/persons/new">New Person</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
          <Link to="/ingestion">Ingestion</Link>
          <Link to="/sources">Sources</Link>
          <Link to="/documents">Documents</Link>
          <Link to="/evidence">Evidence</Link>
          <Link to="/statements">Statements</Link>
          <Link to="/claims">Claims</Link>
          <Link to="/claims/workspace">Claims Workspace</Link>
          <Link to="/contradictions/workspace">Contradictions Workspace</Link>
          <Link to="/sources/new">New Source</Link>
          <Link to="/documents/new">New Document</Link>
          <Link to="/evidence/new">New Evidence</Link>
          <Link to="/statements/new">New Statement</Link>
        </nav>
      </header>
      <main>{children}</main>
    </div>
  );
}

function HomePage() {
  return (
    <Layout>
      <h2>Home</h2>
      <p>Claim operational UX pack is now available.</p>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "16px", marginTop: "20px" }}>
        <QuickCard title="Statements" text="Inspect extracted statements and linked claims." to="/statements" />
        <QuickCard title="Claims" text="Browse, filter, and inspect claims." to="/claims" />
        <QuickCard title="Claims Workspace" text="Create claims from statement context." to="/claims/workspace" />
        <QuickCard title="Contradictions Workspace" text="Prepare contradiction review from statements and claims." to="/contradictions/workspace" />
      </div>
    </Layout>
  );
}

function QuickCard({ title, text, to }: { title: string; text: string; to: string }) {
  return (
    <Link
      to={to}
      style={{
        border: "1px solid #ddd",
        borderRadius: "14px",
        padding: "16px",
        textDecoration: "none",
        color: "inherit",
        display: "block",
      }}
    >
      <strong style={{ display: "block", marginBottom: "8px" }}>{title}</strong>
      <span>{text}</span>
    </Link>
  );
}

function HealthPage() {
  const healthQuery = useQuery({
    queryKey: ["health"],
    queryFn: getHealth,
  });

  const dbHealthQuery = useQuery({
    queryKey: ["health-db"],
    queryFn: getDatabaseHealth,
  });

  return (
    <Layout>
      <h2>Health</h2>

      <section style={{ marginBottom: "24px" }}>
        <h3>API</h3>
        {healthQuery.isLoading && <p>Loading API health...</p>}
        {healthQuery.isError && (
          <p style={{ color: "crimson" }}>
            API health failed: {(healthQuery.error as Error).message}
          </p>
        )}
        {healthQuery.isSuccess && (
          <pre>{JSON.stringify(healthQuery.data, null, 2)}</pre>
        )}
      </section>

      <section>
        <h3>Database</h3>
        {dbHealthQuery.isLoading && <p>Loading DB health...</p>}
        {dbHealthQuery.isError && (
          <p style={{ color: "crimson" }}>
            DB health failed: {(dbHealthQuery.error as Error).message}
          </p>
        )}
        {dbHealthQuery.isSuccess && (
          <pre>{JSON.stringify(dbHealthQuery.data, null, 2)}</pre>
        )}
      </section>
    </Layout>
  );
}

const router = createBrowserRouter([
  { path: "/", element: <HomePage /> },
  { path: "/dashboard", element: <DashboardPage /> },
  { path: "/health", element: <HealthPage /> },
  { path: "/persons", element: <PersonsPage /> },
  { path: "/persons/new", element: <CreatePersonPage /> },
  { path: "/persons/:id", element: <PersonDetailPage /> },
  { path: "/cases", element: <CasesPage /> },
  { path: "/cases/new", element: <CreateCasePage /> },
  { path: "/cases/:id", element: <CaseDetailPage /> },
  { path: "/reviews", element: <ReviewsPage /> },
  { path: "/ingestion", element: <IngestionWorkspacePage /> },
  { path: "/sources", element: <SourcesPage /> },
  { path: "/sources/new", element: <CreateSourcePage /> },
  { path: "/sources/:id", element: <SourceDetailPage /> },
  { path: "/documents", element: <DocumentsPage /> },
  { path: "/documents/new", element: <CreateDocumentPage /> },
  { path: "/documents/:id", element: <DocumentDetailPage /> },
  { path: "/evidence", element: <EvidencePage /> },
  { path: "/evidence/new", element: <CreateEvidencePage /> },
  { path: "/evidence/:id", element: <EvidenceDetailPage /> },
  { path: "/statements", element: <StatementsPage /> },
  { path: "/statements/new", element: <CreateStatementPage /> },
  { path: "/statements/:id", element: <StatementDetailPage /> },
  { path: "/claims", element: <ClaimsPage /> },
  { path: "/claims/:id", element: <ClaimDetailPage /> },
  { path: "/claims/workspace", element: <ClaimsWorkspacePage /> },
  { path: "/contradictions/workspace", element: <ContradictionsWorkspacePage /> },
]);

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </React.StrictMode>
);
'@

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$diagDir = Join-Path $RootDir "_diagnostics\phase-6-14-$timestamp"
Ensure-Directory $diagDir
$reportPath = Join-Path $diagDir "phase-6-14-report.txt"

Set-Content -Path $reportPath -Value "Phase 6.14 claim to contradiction operational pack`r`nGenerated: $(Get-Date -Format s)`r`nRoot: $RootDir" -Encoding UTF8

Write-Host ""
Write-Host "Applying Phase 6.14..." -ForegroundColor Cyan

Write-Utf8File -Path $claimsPagePath -Content $claimsPageContent
Write-Utf8File -Path $claimDetailPagePath -Content $claimDetailPageContent
Write-Utf8File -Path $claimsWorkspacePagePath -Content $claimsWorkspacePageContent
Write-Utf8File -Path $statementDetailPagePath -Content $statementDetailPageContent
Write-Utf8File -Path $contradictionsWorkspacePagePath -Content $contradictionsWorkspacePageContent
Write-Utf8File -Path $caseDetailPagePath -Content $caseDetailUpdated
Write-Utf8File -Path $mainTsxPath -Content $mainTsxContent

Add-Section -OutputPath $reportPath -Title "Updated files" -Content @"
$claimsPagePath
$claimDetailPagePath
$claimsWorkspacePagePath
$statementDetailPagePath
$contradictionsWorkspacePagePath
$caseDetailPagePath
$mainTsxPath
"@

Push-Location $RootDir

Write-Host ""
Write-Host "Building backend..." -ForegroundColor Cyan
dotnet build $solutionPath
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "dotnet build failed."
}
Pop-Location

Push-Location $webRoot
Write-Host ""
Write-Host "Building frontend..." -ForegroundColor Cyan
npm run build
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    throw "npm run build failed."
}
Pop-Location

Write-Host ""
Write-Host "Phase 6.14 completed successfully." -ForegroundColor Green
Write-Host "Report: $reportPath" -ForegroundColor Cyan
