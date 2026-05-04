import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { api } from '../api/client'

type Mode = 'login' | 'forgot' | 'reset'

export default function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [mode, setMode] = useState<Mode>('login')
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [loading, setLoading] = useState(false)

  // Login form
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')

  // Forgot password form
  const [forgotUsername, setForgotUsername] = useState('')
  const [resetToken, setResetToken] = useState('')

  // Reset password form
  const [resetTokenInput, setResetTokenInput] = useState('')
  const [newPassword, setNewPassword] = useState('')

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      await login(username, password)
      navigate('/hotels')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Login failed')
    } finally {
      setLoading(false)
    }
  }

  const handleForgot = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setSuccess('')
    setLoading(true)
    try {
      const res = await api.forgotPassword(forgotUsername)
      setResetToken(res.resetToken)
      setSuccess('Token issued. In production this would be emailed — the workshop returns it directly.')
      setResetTokenInput(res.resetToken)
      setMode('reset')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Request failed')
    } finally {
      setLoading(false)
    }
  }

  const handleReset = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setSuccess('')
    setLoading(true)
    try {
      await api.resetPassword(resetTokenInput, newPassword)
      setSuccess('Password reset successfully. You can now log in.')
      setMode('login')
      setUsername(forgotUsername)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reset failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="login-container">
      <div className="login-title">BookingDojo</div>
      <div className="login-subtitle">Secure Coding Workshop Platform</div>

      {mode === 'login' && (
        <div className="card">
          <form onSubmit={handleLogin}>
            {error && <div className="error-msg">{error}</div>}
            {success && <div className="success-msg">{success}</div>}
            <div className="form-group">
              <label htmlFor="username">Username</label>
              <input
                id="username" type="text" value={username}
                onChange={e => setUsername(e.target.value)}
                required autoComplete="username"
              />
            </div>
            <div className="form-group">
              <label htmlFor="password">Password</label>
              <input
                id="password" type="password" value={password}
                onChange={e => setPassword(e.target.value)}
                required autoComplete="current-password"
              />
            </div>
            <button type="submit" className="btn-primary" disabled={loading}>
              {loading ? 'Signing in...' : 'Sign In'}
            </button>
          </form>
          <div style={{ marginTop: '1rem', textAlign: 'center' }}>
            <button type="button" onClick={() => { setMode('forgot'); setError(''); setSuccess('') }}
              style={{ background: 'none', border: 'none', color: '#2563eb', cursor: 'pointer', fontSize: '0.9rem' }}>
              Forgot password?
            </button>
          </div>
          <div style={{ marginTop: '1.5rem', fontSize: '0.8rem', color: '#888' }}>
            <strong>Workshop accounts:</strong><br />
            admin / Admin1234! &nbsp;|&nbsp; partner / Partner1234! &nbsp;|&nbsp; support / Support1234!
          </div>
        </div>
      )}

      {mode === 'forgot' && (
        <div className="card">
          <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Forgot Password</h2>
          {error && <div className="error-msg">{error}</div>}
          {success && <div className="success-msg">{success}</div>}
          <form onSubmit={handleForgot}>
            <div className="form-group">
              <label>Username</label>
              <input type="text" value={forgotUsername}
                onChange={e => setForgotUsername(e.target.value)} required />
            </div>
            <button type="submit" className="btn-primary" disabled={loading}>
              {loading ? 'Sending...' : 'Request Reset Token'}
            </button>
          </form>
          {resetToken && (
            <div style={{ marginTop: '1rem', padding: '0.75rem', background: '#fef9c3', borderRadius: 6, fontSize: '0.85rem' }}>
              <strong>Reset token (workshop only):</strong><br />
              <code style={{ wordBreak: 'break-all' }}>{resetToken}</code>
            </div>
          )}
          <button type="button" onClick={() => { setMode('login'); setError(''); setSuccess('') }}
            style={{ marginTop: '1rem', background: 'none', border: 'none', color: '#666', cursor: 'pointer' }}>
            ← Back to login
          </button>
        </div>
      )}

      {mode === 'reset' && (
        <div className="card">
          <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Reset Password</h2>
          {error && <div className="error-msg">{error}</div>}
          {success && <div className="success-msg">{success}</div>}
          <form onSubmit={handleReset}>
            <div className="form-group">
              <label>Reset Token</label>
              <input type="text" value={resetTokenInput}
                onChange={e => setResetTokenInput(e.target.value)} required />
            </div>
            <div className="form-group">
              <label>New Password</label>
              <input type="password" value={newPassword}
                onChange={e => setNewPassword(e.target.value)}
                required minLength={8} />
            </div>
            <button type="submit" className="btn-primary" disabled={loading}>
              {loading ? 'Resetting...' : 'Set New Password'}
            </button>
          </form>
          <button type="button" onClick={() => { setMode('login'); setError(''); setSuccess('') }}
            style={{ marginTop: '1rem', background: 'none', border: 'none', color: '#666', cursor: 'pointer' }}>
            ← Back to login
          </button>
        </div>
      )}
    </div>
  )
}
