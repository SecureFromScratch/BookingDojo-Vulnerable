import { useState } from 'react'
import { api } from '../api/client'

export default function MfaPage() {
  const [challenged, setChallenged] = useState(false)
  const [expiresAt, setExpiresAt] = useState<string | null>(null)
  const [otp, setOtp] = useState<string | null>(null)
  const [attemptsRemaining, setAttemptsRemaining] = useState<number | null>(null)
  const [code, setCode] = useState('')
  const [result, setResult] = useState<{ username: string; verifiedAt: string } | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const handleChallenge = async () => {
    setError('')
    setOtp(null)
    setResult(null)
    setCode('')
    setAttemptsRemaining(null)
    setLoading(true)
    try {
      const r = await api.mfaChallenge()
      setExpiresAt(r.expiresAt)
      setChallenged(true)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to request OTP')
    } finally {
      setLoading(false)
    }
  }

  const handleGetOtp = async () => {
    setError('')
    setLoading(true)
    try {
      const r = await api.mfaGetOtp()
      setOtp(r.code)
      setAttemptsRemaining(r.attemptsRemaining)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to retrieve OTP')
    } finally {
      setLoading(false)
    }
  }

  const handleVerify = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const r = await api.mfaVerify(code)
      setResult({ username: r.username, verifiedAt: r.verifiedAt })
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Verification failed'
      setError(msg)
      // Refresh attempts remaining after a failed attempt
      try {
        const otp = await api.mfaGetOtp()
        setAttemptsRemaining(otp.attemptsRemaining)
      } catch { /* challenge may be invalidated */ }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div>
      <h1 className="page-title">MFA Demo</h1>

      <div className="xss-hint" style={{ marginBottom: '1.5rem' }}>
        <strong>Workshop – Lab 11: Brute Force MFA Protection.</strong><br />
        In <em>Vulnerable</em> mode there is no attempt limit — a 4-digit OTP (0000–9999) can be
        enumerated with a simple loop. In <em>Fixed</em> mode the challenge is invalidated after
        5 wrong guesses and the endpoint returns <code>429 Too Many Requests</code>.
      </div>

      {/* Step 1 – Request a challenge */}
      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <h2 style={{ fontSize: '1.1rem', marginBottom: '0.75rem' }}>Step 1 — Request OTP</h2>
        <p style={{ color: '#555', marginBottom: '1rem', fontSize: '0.95rem' }}>
          Generates a fresh 4-digit code valid for 10 minutes.
          Any previous active challenge is invalidated.
        </p>
        <button className="btn-primary" onClick={handleChallenge} disabled={loading}>
          {loading ? 'Requesting…' : challenged ? 'Request New OTP' : 'Request OTP'}
        </button>
        {expiresAt && (
          <p style={{ marginTop: '0.5rem', fontSize: '0.85rem', color: '#666' }}>
            Expires at: {new Date(expiresAt).toLocaleTimeString()}
          </p>
        )}
      </div>

      {challenged && (
        <>
          {/* Step 2 – Workshop delivery */}
          <div className="card" style={{ marginBottom: '1.5rem' }}>
            <h2 style={{ fontSize: '1.1rem', marginBottom: '0.75rem' }}>Step 2 — Retrieve OTP (workshop only)</h2>
            <p style={{ color: '#555', marginBottom: '1rem', fontSize: '0.95rem' }}>
              In a real system this code would arrive via SMS or email.
              This endpoint simulates that out-of-band delivery for the workshop.
            </p>
            <button onClick={handleGetOtp} disabled={loading}
              style={{ padding: '0.5rem 1.25rem', border: '1px solid #1d4ed8', borderRadius: '6px', background: 'white', color: '#1d4ed8', cursor: 'pointer', fontWeight: 500 }}>
              {loading ? '…' : 'Show My OTP'}
            </button>
            {otp && (
              <div style={{ marginTop: '0.75rem' }}>
                <span style={{ fontFamily: 'monospace', fontSize: '2rem', fontWeight: 700, letterSpacing: '0.4rem', color: '#1d4ed8' }}>
                  {otp}
                </span>
                {attemptsRemaining !== null && (
                  <span style={{ marginLeft: '1rem', fontSize: '0.85rem', color: '#666' }}>
                    {attemptsRemaining} attempt{attemptsRemaining !== 1 ? 's' : ''} remaining
                  </span>
                )}
              </div>
            )}
          </div>

          {/* Step 3 – Verify */}
          {!result && (
            <div className="card" style={{ marginBottom: '1.5rem' }}>
              <h2 style={{ fontSize: '1.1rem', marginBottom: '0.75rem' }}>Step 3 — Verify OTP</h2>
              {error && <div className="error-msg" style={{ marginBottom: '0.75rem' }}>{error}</div>}
              <form onSubmit={handleVerify} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end' }}>
                <div className="form-group" style={{ margin: 0 }}>
                  <label>Enter 4-digit code</label>
                  <input
                    type="text"
                    maxLength={4}
                    pattern="[0-9]{4}"
                    placeholder="0000"
                    value={code}
                    onChange={e => setCode(e.target.value)}
                    style={{ fontFamily: 'monospace', fontSize: '1.2rem', letterSpacing: '0.3rem', width: '7rem', textAlign: 'center' }}
                    required
                  />
                </div>
                <button type="submit" className="btn-primary" disabled={loading}>
                  {loading ? 'Verifying…' : 'Verify'}
                </button>
              </form>
            </div>
          )}

          {/* Success */}
          {result && (
            <div className="card">
              <div className="success-msg" style={{ marginBottom: '0.75rem' }}>
                Identity verified for <strong>{result.username}</strong> at {new Date(result.verifiedAt).toLocaleTimeString()}.
              </div>
              <p style={{ color: '#555', fontSize: '0.9rem' }}>
                In a real application, step-up authentication like this would gate a sensitive action:
                fund transfer, account deletion, viewing PAN data, or exporting personal data.
              </p>
              <button onClick={handleChallenge} disabled={loading}
                style={{ marginTop: '0.75rem', padding: '0.4rem 1rem', border: '1px solid #ccc', borderRadius: '6px', background: 'white', cursor: 'pointer' }}>
                Start Over
              </button>
            </div>
          )}
        </>
      )}
    </div>
  )
}
