import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function AgentRunsBoardPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="A ge nt Ru ns Bo ar d"
      subtitle="Stable production shell."
      links={links}
    />
  );
}