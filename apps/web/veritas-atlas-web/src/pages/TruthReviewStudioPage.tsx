import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function TruthReviewStudioPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="T ru th Re vi ew St ud io"
      subtitle="Stable production shell."
      links={links}
    />
  );
}