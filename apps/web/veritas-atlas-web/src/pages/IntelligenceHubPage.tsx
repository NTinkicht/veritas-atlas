import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function IntelligenceHubPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="I nt el li ge nc eH ub"
      subtitle="Stable production shell."
      links={links}
    />
  );
}