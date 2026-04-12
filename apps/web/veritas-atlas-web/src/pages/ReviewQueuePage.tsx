import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReviewQueuePage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ev ie wQ ue ue"
      subtitle="Stable production shell."
      links={links}
    />
  );
}