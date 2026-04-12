import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function IntegrationTestCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="I nt eg ra ti on Te st Ce nt er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}