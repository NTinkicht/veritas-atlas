import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function ReviewsPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="R ev ie ws"
      subtitle="Stable production shell."
      links={links}
    />
  );
}