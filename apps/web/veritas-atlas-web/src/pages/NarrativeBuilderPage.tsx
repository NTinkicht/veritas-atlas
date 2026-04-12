import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function NarrativeBuilderPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="N ar ra ti ve Bu il de r"
      subtitle="Stable production shell."
      links={links}
    />
  );
}