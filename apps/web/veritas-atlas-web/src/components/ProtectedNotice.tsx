import { Link } from "react-router-dom";

type ProtectedNoticeProps = {
  message?: string;
};

export function ProtectedNotice({ message = "This section requires a fresh login." }: ProtectedNoticeProps) {
  return (
    <div className="notice-card notice-danger">
      {message} <Link to="/login">Open login</Link>
    </div>
  );
}