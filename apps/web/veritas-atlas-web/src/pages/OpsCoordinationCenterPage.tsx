import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function OpsCoordinationCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="O ps Co or di na ti on Ce nt er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}