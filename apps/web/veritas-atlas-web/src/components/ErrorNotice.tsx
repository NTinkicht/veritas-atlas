type ErrorNoticeProps = {
  message?: string | null;
};

export function ErrorNotice({ message }: ErrorNoticeProps) {
  if (!message) return null;
  return (
    <div style={{ border: "1px solid #fecaca", background: "#fef2f2", color: "#991b1b", padding: 12, borderRadius: 10 }}>
      {message}
    </div>
  );
}