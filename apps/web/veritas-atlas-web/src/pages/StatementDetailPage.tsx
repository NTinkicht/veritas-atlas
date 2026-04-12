import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function StatementDetailPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ta te me nt De ta il"
      subtitle="Stable production shell."
      links={links}
    />
  );
}