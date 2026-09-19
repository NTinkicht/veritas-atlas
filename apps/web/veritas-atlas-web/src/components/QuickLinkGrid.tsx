export type QuickLinkItem = {
  href: string;
  label: string;
  description?: string;
};

export function QuickLinkGrid(_props: { items?: QuickLinkItem[] }) {
  void _props;
  return <div>Quick Links</div>;
}