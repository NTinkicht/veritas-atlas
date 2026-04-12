type LoadingBlockProps = {
  label?: string;
};

export function LoadingBlock({ label = "Loading..." }: LoadingBlockProps) {
  return (
    <div style={{ border: "1px solid #e5e7eb", background: "#f9fafb", color: "#374151", padding: 12, borderRadius: 10 }}>
      {label}
    </div>
  );
}