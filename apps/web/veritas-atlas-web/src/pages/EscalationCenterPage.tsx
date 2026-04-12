import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function EscalationCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="E sc al at io nC en te r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}