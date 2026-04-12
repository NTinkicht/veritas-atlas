import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function WorkflowDiagnosticsPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="W or kf lo wD ia gn os ti cs"
      subtitle="Stable production shell."
      links={links}
    />
  );
}