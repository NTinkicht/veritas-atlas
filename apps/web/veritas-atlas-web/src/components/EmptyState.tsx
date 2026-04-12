type EmptyStateProps = {
  title?: string;
  message: string;
};

export function EmptyState({ title = "Nothing to show", message }: EmptyStateProps) {
  return (
    <div className="empty-card">
      <strong style={{ display: "block", marginBottom: 8 }}>{title}</strong>
      <span>{message}</span>
    </div>
  );
}