import { ActivityTimelinePanel } from "../components/ActivityTimelinePanel";

export function ObservabilityDashboardPage() {
  const activity = [
    { id: "1", text: "Claim created" },
    { id: "2", text: "Contradiction detected" },
    { id: "3", text: "Review submitted" },
  ];

  return (
    <div style={{ padding: 24 }}>
      <h1>Observability Dashboard</h1>
      <ActivityTimelinePanel items={activity} />
    </div>
  );
}