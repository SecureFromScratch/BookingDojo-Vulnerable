import { Link } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function Navbar() {
  const { user, logout } = useAuth()

  const handleLogout = async () => {
    await logout()
  }

  return (
    <nav className="navbar">
      <Link to="/bookings" className="navbar-brand">BookingDojo</Link>
      <Link to="/bookings">Bookings</Link>
      {user?.role === 'AdminUser' && <Link to="/hotels">Hotels</Link>}
      <Link to="/cart">Cart</Link>
      <Link to="/mfa">MFA Demo</Link>
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
