import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CreateCasePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C re at eC as e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}