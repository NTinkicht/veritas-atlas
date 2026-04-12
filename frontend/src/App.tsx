import { BrowserRouter, Routes, Route } from 'react-router-dom'
import MainLayout from './layout/MainLayout'
import HomePage from './pages/HomePage'
import CasesPage from './pages/CasesPage'
import LoginPage from './pages/LoginPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>

        <Route path="/login" element={<LoginPage />} />

        <Route path="/" element={
          <MainLayout>
            <HomePage />
          </MainLayout>
        } />

        <Route path="/cases" element={
          <MainLayout>
            <CasesPage />
          </MainLayout>
        } />

      </Routes>
    </BrowserRouter>
  )
}
