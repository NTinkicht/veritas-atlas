export type QuickLinkItem = {
  href: string;
  label: string;
  description?: string;
};

export function QuickLinkGrid(_props: { items?: QuickLinkItem[] }) {
  return <div>Quick Links</div>;
}