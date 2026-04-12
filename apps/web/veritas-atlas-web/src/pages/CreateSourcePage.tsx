import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CreateSourcePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C re at eS ou rc e"
      subtitle="Stable production shell."
      links={links}
    />
  );
}