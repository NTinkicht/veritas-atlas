type LoadingStateProps = {
  message?: string;
};

export function LoadingState({ message = "Loading..." }: LoadingStateProps) {
  return <div className="notice-card" role="status" aria-live="polite">{message}</div>;
}