import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PublicationPipelinePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P ub li ca ti on Pi pe li ne"
      subtitle="Stable production shell."
      links={links}
    />
  );
}