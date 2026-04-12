import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function Phase7CloseoutPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ha se7 Cl os eo ut"
      subtitle="Stable production shell."
      links={links}
    />
  );
}