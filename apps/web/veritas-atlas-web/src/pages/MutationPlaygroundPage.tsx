import { AppSurface } from "../components/AppSurface";
import { useFrontendNav } from "../hooks/useFrontendNav";

export function MutationPlaygroundPage() {
  const links = useFrontendNav();

  return (
    <AppSurface
      title="M ut at io nP la yg ro un d"
      subtitle="Stable production shell."
      links={links}
    />
  );
}