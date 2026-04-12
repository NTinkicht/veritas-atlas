import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ContradictionsWorkspacePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C on tr ad ic ti on sW or ks pa ce"
      subtitle="Stable production shell."
      links={links}
    />
  );
}