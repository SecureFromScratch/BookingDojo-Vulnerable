import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function Navbar() {
  const { user, logout } = useAuth()

  const handleLogout = async () => {
    await logout()
  }

  const linkClassName = ({ isActive }: { isActive: boolean }) =>
    isActive ? 'navbar-link active' : 'navbar-link'

  return (
    <nav className="navbar" aria-label="Main navigation">
      <div className="navbar-inner">
        <Link to="/bookings" className="navbar-brand" aria-label="BookingDojo home">
          <span className="navbar-logo-mark" aria-hidden="true">✦</span>
          <span className="navbar-brand-copy">
            <span>BookingDojo</span>            
          </span>
        </Link>

        <div className="navbar-links">
          <NavLink to="/bookings" className={linkClassName}>Bookings</NavLink>
          {user?.role === 'AdminUser' && <NavLink to="/hotels" className={linkClassName}>Hotels</NavLink>}
          <NavLink to="/cart" className={linkClassName}>Cart</NavLink>
          <NavLink to="/mfa" className={linkClassName}>MFA Demo</NavLink>
          {(user?.role === 'AdminUser' || user?.role === 'SupportUser') && (
            <NavLink to="/audit-logs" className={linkClassName}>Audit Logs</NavLink>
          )}
          {(user?.role === 'AdminUser' || user?.role === 'PartnerUser') && (
            <NavLink to="/integrations" className={linkClassName}>Integrations</NavLink>
          )}
          <NavLink to="/profile" className={linkClassName}>Profile</NavLink>
        </div>

        <div className="navbar-user">
          <span className={`role-badge ${user?.role}`}>{user?.role}</span>
          <span className="navbar-username">{user?.username}</span>
          <button className="btn-logout" onClick={handleLogout}>Logout</button>
        </div>
      </div>
    </nav>
  )
}
