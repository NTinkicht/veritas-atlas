import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ObservabilityDashboardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O bs er va bi li ty Da sh bo ar d"
      subtitle="Stable production shell."
      links={links}
    />
  );
}