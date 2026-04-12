import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AuditPersistencePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A ud it Pe rs is te nc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}