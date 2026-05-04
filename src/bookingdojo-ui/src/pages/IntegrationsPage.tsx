import { useState } from 'react'
import { api } from '../api/client'

export default function IntegrationsPage() {
  const [webhookUrl, setWebhookUrl] = useState('')
  const [webhookResult, setWebhookResult] = useState<object | null>(null)
  const [webhookError, setWebhookError] = useState('')
  const [webhookLoading, setWebhookLoading] = useState(false)

  const handleWebhookTest = async (e: React.FormEvent) => {
    e.preventDefault()
    setWebhookError('')
    setWebhookResult(null)
    setWebhookLoading(true)
    try {
      const res = await api.testWebhook(webhookUrl)
      setWebhookResult(res)
    } catch (err) {
      setWebhookError(err instanceof Error ? err.message : 'Request failed')
    } finally {
      setWebhookLoading(false)
    }
  }

  return (
    <div>
      <h1 className="page-title">Integrations</h1>

      <div className="card">
        <h2 style={{ marginBottom: '0.5rem', fontSize: '1.1rem' }}>Webhook Test</h2>
        <p style={{ color: '#666', fontSize: '0.9rem', marginBottom: '1rem' }}>
          Send a test event to your webhook endpoint to verify it is reachable and responding.
          The server posts a sample <code>booking.created</code> payload to the URL you supply.
        </p>
        <div className="xss-hint">
          <strong>Workshop tip:</strong> Try internal URLs such as{' '}
          <code>http://169.254.169.254/latest/meta-data/</code> (AWS metadata),{' '}
          <code>http://localhost:5432</code> (database), or{' '}
          <code>http://localhost:5000/api/auth/login</code> (internal API).
          In Vulnerable mode the server fetches these on your behalf.
        </div>

        {webhookError && <div className="error-msg" style={{ marginTop: '0.75rem' }}>{webhookError}</div>}

        <form onSubmit={handleWebhookTest} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end', marginTop: '1rem' }}>
          <div className="form-group" style={{ flex: 1, margin: 0 }}>
            <label>Webhook URL</label>
            <input
              type="text"
              placeholder="https://your-server.example.com/webhook"
              value={webhookUrl}
              onChange={e => setWebhookUrl(e.target.value)}
              required
            />
          </div>
          <button type="submit" className="btn-primary" disabled={webhookLoading}>
            {webhookLoading ? 'Testing...' : 'Test Webhook'}
          </button>
        </form>

        {webhookResult && (
          <div style={{ marginTop: '1rem' }}>
            <label style={{ fontSize: '0.85rem', color: '#666' }}>Server response:</label>
            <pre style={{
              background: '#1e1e1e', color: '#d4d4d4', padding: '1rem',
              borderRadius: 6, fontSize: '0.8rem', overflowX: 'auto',
              marginTop: '0.5rem', maxHeight: 300, overflowY: 'auto'
            }}>
              {JSON.stringify(webhookResult, null, 2)}
            </pre>
          </div>
        )}
      </div>
    </div>
  )
}
