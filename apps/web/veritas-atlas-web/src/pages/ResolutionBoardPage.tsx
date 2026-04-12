import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ResolutionBoardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R es ol ut io nB oa rd"
      subtitle="Stable production shell."
      links={links}
    />
  );
}