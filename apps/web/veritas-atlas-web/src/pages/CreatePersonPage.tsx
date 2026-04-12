import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CreatePersonPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C re at eP er so n"
      subtitle="Stable production shell."
      links={links}
    />
  );
}