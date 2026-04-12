import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CaseExplorerPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C as eE xp lo re r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}