import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CreateEvidencePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C re at eE vi de nc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}