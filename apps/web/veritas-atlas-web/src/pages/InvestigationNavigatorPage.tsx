import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function InvestigationNavigatorPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="I nv es ti ga ti on Na vi ga to r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}