import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function KnowledgeGraphHubPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="K no wl ed ge Gr ap hH ub"
      subtitle="Stable production shell."
      links={links}
    />
  );
}