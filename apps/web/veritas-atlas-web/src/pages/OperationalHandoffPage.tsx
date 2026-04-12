import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OperationalHandoffPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O pe ra ti on al Ha nd of f"
      subtitle="Stable production shell."
      links={links}
    />
  );
}