import { Link } from "react-router-dom";
import type { CSSProperties } from "react";
import { useReviews } from "../hooks/useReviews";
import type { ReviewsListItem } from "../api/reviews";
import {
  useApproveReview,
  useRejectReview,
  useRequestReviewChanges,
} from "../hooks/useReviewActions";

export function ReviewsPage() {
  const reviewsQuery = useReviews();
  const approveMutation = useApproveReview();
  const rejectMutation = useRejectReview();
  const needsChangesMutation = useRequestReviewChanges();

  const isBusy =
    approveMutation.isPending ||
    rejectMutation.isPending ||
    needsChangesMutation.isPending;

  const mutationError =
    (approveMutation.error as Error | null) ||
    (rejectMutation.error as Error | null) ||
    (needsChangesMutation.error as Error | null);

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: "24px" }}>
      <header style={{ marginBottom: "24px" }}>
        <h1 style={{ margin: 0 }}>Reviews</h1>
        <p style={{ color: "#555" }}>Live review workload from Veritas Atlas API</p>
        <nav style={{ display: "flex", gap: "16px", marginTop: "12px", flexWrap: "wrap" }}>
          <Link to="/">Home</Link>
          <Link to="/dashboard">Dashboard</Link>
          <Link to="/health">Health</Link>
          <Link to="/cases">Cases</Link>
          <Link to="/cases/new">New Case</Link>
          <Link to="/reviews">Reviews</Link>
        </nav>
      </header>

      {reviewsQuery.isLoading && <p>Loading reviews...</p>}

      {reviewsQuery.isError && (
        <p style={{ color: "crimson" }}>
          Failed to load reviews: {(reviewsQuery.error as Error).message}
        </p>
      )}

      {mutationError && (
        <p style={{ color: "crimson" }}>
          Review action failed: {mutationError.message}
        </p>
      )}

      {reviewsQuery.isSuccess && (
        <>
          <p>Total reviews: {reviewsQuery.data.totalCount}</p>

          {reviewsQuery.data.items.length === 0 ? (
            <p>No reviews found.</p>
          ) : (
            <div style={{ overflowX: "auto" }}>
              <table
                style={{
                  width: "100%",
                  borderCollapse: "collapse",
                  marginTop: "16px",
                }}
              >
                <thead>
                  <tr>
                    <th style={thStyle}>Id</th>
                    <th style={thStyle}>Case Id</th>
                    <th style={thStyle}>Type</th>
                    <th style={thStyle}>Status</th>
                    <th style={thStyle}>Decision</th>
                    <th style={thStyle}>Reviewer</th>
                    <th style={thStyle}>Created</th>
                    <th style={thStyle}>Reviewed</th>
                    <th style={thStyle}>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {reviewsQuery.data.items.map((item: ReviewsListItem) => (
                    <tr key={item.id}>
                      <td style={tdStyle}>{item.id}</td>
                      <td style={tdStyle}>
                        <Link to={`/cases/${item.caseId}`}>{item.caseId}</Link>
                      </td>
                      <td style={tdStyle}>{item.type}</td>
                      <td style={tdStyle}>{item.status}</td>
                      <td style={tdStyle}>{item.decision}</td>
                      <td style={tdStyle}>{item.reviewer}</td>
                      <td style={tdStyle}>{formatDate(item.createdAtUtc)}</td>
                      <td style={tdStyle}>{item.reviewedAtUtc ? formatDate(item.reviewedAtUtc) : "N/A"}</td>
                      <td style={tdStyle}>
                        <div style={actionsStyle}>
                          <button
                            style={buttonStyle}
                            disabled={isBusy}
                            onClick={() => approveMutation.mutate(item.id)}
                          >
                            Approve
                          </button>
                          <button
                            style={buttonStyle}
                            disabled={isBusy}
                            onClick={() => rejectMutation.mutate(item.id)}
                          >
                            Reject
                          </button>
                          <button
                            style={buttonStyle}
                            disabled={isBusy}
                            onClick={() => needsChangesMutation.mutate(item.id)}
                          >
                            Needs Changes
                          </button>
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

function formatDate(value: string) {
  return new Date(value).toLocaleString();
}

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

const actionsStyle: CSSProperties = {
  display: "flex",
  flexDirection: "column",
  gap: "8px",
  minWidth: "130px",
};

const buttonStyle: CSSProperties = {
  padding: "8px 10px",
  borderRadius: "8px",
  border: "1px solid #ccc",
  cursor: "pointer",
};
