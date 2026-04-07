import { IntegrationChecklistPanel } from "../components/IntegrationChecklistPanel";

export function IntegrationTestCenterPage() {
  const items = [
    "Seed lifecycle scenario",
    "Submit seeded case",
    "Prepare publication",
    "Resolve contradiction",
    "Publish case",
    "Verify refreshed lists and detail surfaces",
    "Capture diagnostics report"
  ];

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Integration Test Center</h1>
      <p style={{ color: "#555" }}>
        Frontend reference center for the seeded workflow integration path.
      </p>
      <IntegrationChecklistPanel items={items} />
    </div>
  );
}