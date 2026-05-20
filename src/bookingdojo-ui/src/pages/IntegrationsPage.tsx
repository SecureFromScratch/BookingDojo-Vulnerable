import { useEffect, useState } from 'react'
import { api } from '../api/client'

interface Webhook {
  id: string
  url: string
  createdAt: string
}

export default function IntegrationsPage() {
  const [webhooks, setWebhooks] = useState<Webhook[]>([])
  const [newUrl, setNewUrl] = useState('')
  const [registerResult, setRegisterResult] = useState<object | null>(null)
  const [registerError, setRegisterError] = useState('')
  const [registerLoading, setRegisterLoading] = useState(false)

  const [testUrl, setTestUrl] = useState('')
  const [testResult, setTestResult] = useState<object | null>(null)
  const [testError, setTestError] = useState('')
  const [testLoading, setTestLoading] = useState(false)

  useEffect(() => {
    api.getWebhooks().then(setWebhooks).catch(() => {})
  }, [])

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault()
    setRegisterError('')
    setRegisterResult(null)
    setRegisterLoading(true)
    try {
      const res = await api.registerWebhook(newUrl)
      setWebhooks(prev => [res.webhook, ...prev])
      setRegisterResult({ pingStatusCode: res.pingStatusCode, pingBody: res.pingBody, pingError: res.pingError })
      setNewUrl('')
    } catch (err) {
      setRegisterError(err instanceof Error ? err.message : 'Request failed')
    } finally {
      setRegisterLoading(false)
    }
  }

  const handleDelete = async (id: string) => {
    try {
      await api.deleteWebhook(id)
      setWebhooks(prev => prev.filter(w => w.id !== id))
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Delete failed')
    }
  }

  const handleTest = async (e: React.FormEvent) => {
    e.preventDefault()
    setTestError('')
    setTestResult(null)
    setTestLoading(true)
    try {
      const res = await api.testWebhook(testUrl)
      setTestResult(res)
    } catch (err) {
      setTestError(err instanceof Error ? err.message : 'Request failed')
    } finally {
      setTestLoading(false)
    }
  }

  return (
    <div>
      <h1 className="page-title">Integrations</h1>

      {/* ── Registered webhooks ── */}
      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <h2 style={{ marginBottom: '0.5rem', fontSize: '1.1rem' }}>Webhooks</h2>
        <p style={{ color: '#666', fontSize: '0.9rem', marginBottom: '1rem' }}>
          Register a URL to receive a <code>booking.created</code> event whenever a new booking is made.
          The server sends a verification ping immediately on registration.
        </p>
        <div className="xss-hint">
          <strong>Workshop tip:</strong> In Vulnerable mode the server pings any URL you register —
          including internal services. Try{' '}
          <code>http://localhost:8888/api/internal/secret</code> to retrieve credentials the server
          can reach but the browser cannot.
        </div>

        {registerError && <div className="error-msg" style={{ marginTop: '0.75rem' }}>{registerError}</div>}

        <form onSubmit={handleRegister} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end', marginTop: '1rem' }}>
          <div className="form-group" style={{ flex: 1, margin: 0 }}>
            <label>Webhook URL</label>
            <input
              type="text"
              placeholder="https://your-server.example.com/webhook"
              value={newUrl}
              onChange={e => setNewUrl(e.target.value)}
              required
            />
          </div>
          <button type="submit" className="btn-primary" disabled={registerLoading}>
            {registerLoading ? 'Registering...' : 'Register'}
          </button>
        </form>

        {registerResult && (
          <div style={{ marginTop: '1rem' }}>
            <label style={{ fontSize: '0.85rem', color: '#666' }}>Ping result:</label>
            <pre style={{
              background: '#1e1e1e', color: '#d4d4d4', padding: '1rem',
              borderRadius: 6, fontSize: '0.8rem', overflowX: 'auto',
              marginTop: '0.5rem', maxHeight: 300, overflowY: 'auto'
            }}>
              {JSON.stringify(registerResult, null, 2)}
            </pre>
          </div>
        )}

        {webhooks.length > 0 && (
          <table style={{ width: '100%', marginTop: '1.25rem', borderCollapse: 'collapse', fontSize: '0.9rem' }}>
            <thead>
              <tr style={{ borderBottom: '1px solid #e5e7eb' }}>
                <th style={{ textAlign: 'left', padding: '0.5rem 0.75rem', color: '#6b7280' }}>URL</th>
                <th style={{ textAlign: 'left', padding: '0.5rem 0.75rem', color: '#6b7280' }}>Registered</th>
                <th style={{ padding: '0.5rem 0.75rem' }}></th>
              </tr>
            </thead>
            <tbody>
              {webhooks.map(w => (
                <tr key={w.id} style={{ borderBottom: '1px solid #f3f4f6' }}>
                  <td style={{ padding: '0.5rem 0.75rem', wordBreak: 'break-all' }}>{w.url}</td>
                  <td style={{ padding: '0.5rem 0.75rem', color: '#6b7280', whiteSpace: 'nowrap' }}>
                    {new Date(w.createdAt).toLocaleDateString()}
                  </td>
                  <td style={{ padding: '0.5rem 0.75rem', textAlign: 'right' }}>
                    <button
                      onClick={() => handleDelete(w.id)}
                      style={{ background: 'none', border: 'none', color: '#ef4444', cursor: 'pointer', fontSize: '0.85rem' }}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* ── Ad-hoc test ── */}
      <div className="card">
        <h2 style={{ marginBottom: '0.5rem', fontSize: '1.1rem' }}>Test a URL</h2>
        <p style={{ color: '#666', fontSize: '0.9rem', marginBottom: '1rem' }}>
          Send a one-off test ping without saving the URL.
        </p>

        {testError && <div className="error-msg" style={{ marginTop: '0.75rem' }}>{testError}</div>}

        <form onSubmit={handleTest} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end' }}>
          <div className="form-group" style={{ flex: 1, margin: 0 }}>
            <label>URL</label>
            <input
              type="text"
              placeholder="https://your-server.example.com/webhook"
              value={testUrl}
              onChange={e => setTestUrl(e.target.value)}
              required
            />
          </div>
          <button type="submit" className="btn-primary" disabled={testLoading}>
            {testLoading ? 'Sending...' : 'Send Test'}
          </button>
        </form>

        {testResult && (
          <div style={{ marginTop: '1rem' }}>
            <label style={{ fontSize: '0.85rem', color: '#666' }}>Server response:</label>
            <pre style={{
              background: '#1e1e1e', color: '#d4d4d4', padding: '1rem',
              borderRadius: 6, fontSize: '0.8rem', overflowX: 'auto',
              marginTop: '0.5rem', maxHeight: 300, overflowY: 'auto'
            }}>
              {JSON.stringify(testResult, null, 2)}
            </pre>
          </div>
        )}
      </div>
    </div>
  )
}
