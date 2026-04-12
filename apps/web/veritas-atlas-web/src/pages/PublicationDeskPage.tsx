import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PublicationDeskPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ub li ca ti on De sk"
      subtitle="Stable production shell."
      links={links}
    />
  );
}