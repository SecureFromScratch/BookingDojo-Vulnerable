import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function Navbar() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  const handleLogout = async () => {
    await logout()
    navigate('/login')
  }

  return (
    <nav className="navbar">
      <Link to="/bookings" className="navbar-brand">BookingDojo</Link>
      <Link to="/bookings">Bookings</Link>
      <Link to="/hotels">Hotels</Link>
      {(user?.role === 'AdminUser' || user?.role === 'SupportUser') && (
        <Link to="/audit-logs">Audit Logs</Link>
      )}
      {(user?.role === 'AdminUser' || user?.role === 'PartnerUser') && (
        <Link to="/integrations">Integrations</Link>
      )}
      <span className={`role-badge ${user?.role}`}>{user?.role}</span>
      <span style={{ color: '#aaa', fontSize: '0.85rem' }}>{user?.username}</span>
      <button className="btn-logout" onClick={handleLogout}>Logout</button>
    </nav>
  )
}
