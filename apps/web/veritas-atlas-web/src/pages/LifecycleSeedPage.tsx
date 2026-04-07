import { SeedScenarioPanel } from "../components/SeedScenarioPanel";
import { useWorkflowSeed } from "../hooks/useWorkflowSeed";

export function LifecycleSeedPage() {
  const seedMutation = useWorkflowSeed();

  const message = seedMutation.data
    ? `Case ${seedMutation.data.caseId} seeded with contradiction ${seedMutation.data.contradictionId}.`
    : undefined;

  return (
    <div style={{ fontFamily: "Arial, sans-serif", padding: 24 }}>
      <h1 style={{ marginTop: 0 }}>Lifecycle Seed</h1>
      <p style={{ color: "#555" }}>
        Seed a minimal end-to-end workflow scenario for validation and UI interaction.
      </p>

      <SeedScenarioPanel
        onSeed={() => seedMutation.mutate()}
        isPending={seedMutation.isPending}
        message={message}
      />
    </div>
  );
}