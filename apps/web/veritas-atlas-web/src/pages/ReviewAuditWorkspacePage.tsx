import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReviewAuditWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ev ie wA ud it Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}