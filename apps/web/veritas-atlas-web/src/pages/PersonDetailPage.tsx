import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function PersonDetailPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="P er so nD et ai l"
      subtitle="Stable production shell."
      links={links}
    />
  );
}