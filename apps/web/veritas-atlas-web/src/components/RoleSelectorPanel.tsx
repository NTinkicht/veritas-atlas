import { useState } from "react";
import { getWorkflowRole, setWorkflowRole } from "../api/workflowRoleContext";

const roles = ["operator", "reviewer", "publisher", "admin"];

export function RoleSelectorPanel() {
  const [role, setRole] = useState(getWorkflowRole);

  const onRoleChange = (value: string) => {
    setRole(value);
    setWorkflowRole(value);
  };

  return (
    <div style={panelStyle}>
      <strong>Workflow Role</strong>
      <select value={role} onChange={(e) => onRoleChange(e.target.value)} style={selectStyle}>
        {roles.map((item) => (
          <option key={item} value={item}>{item}</option>
        ))}
      </select>
    </div>
  );
}

const panelStyle: React.CSSProperties = {
  border: "1px solid #ddd",
  borderRadius: 12,
  padding: 12,
  display: "flex",
  gap: 12,
  alignItems: "center",
};

const selectStyle: React.CSSProperties = {
  border: "1px solid #ccc",
  borderRadius: 8,
  padding: "8px 10px",
  font: "inherit",
};