import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function QualityRadarPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="Q ua li ty Ra da r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}