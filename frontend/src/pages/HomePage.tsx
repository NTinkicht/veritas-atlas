export default function HomePage() {
  return (
    <div>
      <h1>Dashboard</h1>

      <div style={{
        display:'grid',
        gridTemplateColumns:'repeat(4,1fr)',
        gap:20
      }}>
        <Stat title="Total Cases" value="124"/>
        <Stat title="Active Reviews" value="18"/>
        <Stat title="Agent Runs" value="342"/>
        <Stat title="Published" value="76"/>
      </div>
    </div>
  )
}

function Stat({title, value}) {
  return (
    <div style={{
      background:'#111827',
      padding:20,
      border:'1px solid #1f2937',
      borderRadius:8
    }}>
      <div style={{color:'#9ca3af'}}>{title}</div>
      <div style={{fontSize:24}}>{value}</div>
    </div>
  )
}
