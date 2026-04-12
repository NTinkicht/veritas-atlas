import { AppSurface } from "../components/AppSurface";
import { SeededWorkflowHarness } from "../components/SeededWorkflowHarness";

export function OperationalActionsPage() {
  return (
    <AppSurface
      title="Operational Actions"
      subtitle="Run the known-good seeded workflow test path and validate the operational controls."
    >
      <SeededWorkflowHarness />
    </AppSurface>
  );
}