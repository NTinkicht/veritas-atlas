import Sidebar from './Sidebar'

export default function MainLayout({ children }) {
  return (
    <div style={{display:'flex'}}>
      <Sidebar />
      <div style={{flex:1, padding:20}}>
        {children}
      </div>
    </div>
  )
}
