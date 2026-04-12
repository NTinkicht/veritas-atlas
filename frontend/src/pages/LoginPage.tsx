import { useState } from 'react'

export default function LoginPage() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  return (
    <div style={{
      display:'flex',
      height:'100vh',
      alignItems:'center',
      justifyContent:'center'
    }}>
      <div style={{
        width:320,
        padding:30,
        background:'#111827',
        border:'1px solid #1f2937',
        borderRadius:8
      }}>
        <h2>Login</h2>

        <input
          placeholder="Email"
          value={email}
          onChange={e=>setEmail(e.target.value)}
          style={{width:'100%', marginBottom:10}}
        />

        <input
          placeholder="Password"
          type="password"
          value={password}
          onChange={e=>setPassword(e.target.value)}
          style={{width:'100%', marginBottom:20}}
        />

        <button style={{
          width:'100%',
          background:'#6366f1',
          border:'none',
          padding:10,
          color:'white'
        }}>
          Login
        </button>
      </div>
    </div>
  )
}
