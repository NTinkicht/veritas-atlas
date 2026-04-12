import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase15OperationsDashboardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se15 Op er at io ns Da sh bo ar d"
      subtitle="Stable production shell."
      links={links}
    />
  );
}