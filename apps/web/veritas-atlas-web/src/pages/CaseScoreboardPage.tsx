import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function CaseScoreboardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="C as eS co re bo ar d"
      subtitle="Stable production shell."
      links={links}
    />
  );
}