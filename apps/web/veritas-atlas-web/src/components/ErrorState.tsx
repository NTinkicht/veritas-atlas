type ErrorStateProps = {
  message: string;
};

export function ErrorState({ message }: ErrorStateProps) {
  return <div className="notice-card notice-danger">{message}</div>;
}