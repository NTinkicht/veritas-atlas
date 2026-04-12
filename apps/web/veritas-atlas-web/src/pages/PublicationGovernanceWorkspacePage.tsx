import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PublicationGovernanceWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ub li ca ti on Go ve rn an ce Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}