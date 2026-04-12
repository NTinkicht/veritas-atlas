export default function CasesPage() {
  return (
    <div>
      <h1>Cases</h1>

      <table style={{width:'100%', borderCollapse:'collapse'}}>
        <thead>
          <tr>
            <th>ID</th>
            <th>Status</th>
            <th>Created By</th>
            <th>Created At</th>
          </tr>
        </thead>

        <tbody>
          <tr>
            <td>1</td>
            <td>Open</td>
            <td>Admin</td>
            <td>Today</td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}
