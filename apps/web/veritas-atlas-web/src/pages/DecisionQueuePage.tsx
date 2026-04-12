import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function DecisionQueuePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="D ec is io nQ ue ue"
      subtitle="Stable production shell."
      links={links}
    />
  );
}