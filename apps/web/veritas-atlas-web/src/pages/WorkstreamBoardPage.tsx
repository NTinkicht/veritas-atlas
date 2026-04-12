import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function WorkstreamBoardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="W or ks tr ea mB oa rd"
      subtitle="Stable production shell."
      links={links}
    />
  );
}