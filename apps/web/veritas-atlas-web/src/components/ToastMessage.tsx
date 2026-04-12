type ToastMessageProps = {
  type: "success" | "error";
  message: string;
};

export function ToastMessage({ type, message }: ToastMessageProps) {
  return (
    <div
      className={`notice-card ${type === "success" ? "notice-success" : "notice-danger"}`}
      style={{
        position: "fixed",
        top: 20,
        right: 20,
        zIndex: 1000,
        minWidth: 320,
        maxWidth: 420,
      }}
    >
      {message}
    </div>
  );
}