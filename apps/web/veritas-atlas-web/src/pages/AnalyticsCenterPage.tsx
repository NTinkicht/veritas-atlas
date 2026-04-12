import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AnalyticsCenterPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A na ly ti cs Ce nt er"
      subtitle="Stable production shell."
      links={links}
    />
  );
}