import { AuthStatusPanel } from "../components/AuthStatusPanel";
import { useCurrentUser } from "../hooks/useAuth";

export function AuthMePage() {
  const query = useCurrentUser();

  if (query.isLoading) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>Loading current user...</div>;
  }

  if (query.isError) {
    return <div style={{ fontFamily: "Arial, sans-serif", padding: 24, color: "crimson" }}>Failed to load current user.</div>;
  }

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Current User</h1>
      <AuthStatusPanel user={query.data ?? null} />
    </div>
  );
}