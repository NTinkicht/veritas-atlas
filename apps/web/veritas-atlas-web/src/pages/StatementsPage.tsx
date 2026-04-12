import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function StatementsPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="S ta te me nt s"
      subtitle="Stable production shell."
      links={links}
    />
  );
}