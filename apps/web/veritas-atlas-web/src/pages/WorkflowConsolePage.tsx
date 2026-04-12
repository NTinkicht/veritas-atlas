import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function WorkflowConsolePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="W or kf lo wC on so le"
      subtitle="Stable production shell."
      links={links}
    />
  );
}