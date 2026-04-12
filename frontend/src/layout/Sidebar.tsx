import { Link } from 'react-router-dom'

export default function Sidebar() {
  return (
    <div style={{
      width: 240,
      height: '100vh',
      background: '#111827',
      borderRight: '1px solid #1f2937',
      padding: 20
    }}>
      <h2 style={{color:'#6366f1'}}>Veritas Atlas</h2>

      <nav style={{display:'flex', flexDirection:'column', gap:12}}>
        <Link to="/">Dashboard</Link>
        <Link to="/cases">Cases</Link>
      </nav>
    </div>
  )
}
