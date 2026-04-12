import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OperationalPortfolioPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O pe ra ti on al Po rt fo li o"
      subtitle="Stable production shell."
      links={links}
    />
  );
}