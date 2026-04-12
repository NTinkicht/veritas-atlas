import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ContradictionResolutionWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C on tr ad ic ti on Re so lu ti on Wo rk sp ac e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}