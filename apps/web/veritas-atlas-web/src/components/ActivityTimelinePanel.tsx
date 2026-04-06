export function ActivityTimelinePanel({
  items,
}: {
  items: { id: string; text: string }[];
}) {
  return (
    <div style={{ border: "1px solid #ddd", padding: 16, borderRadius: 12 }}>
      <h3>Activity Timeline</h3>
      <ul>
        {items.map((i) => (
          <li key={i.id}>{i.text}</li>
        ))}
      </ul>
    </div>
  );
}