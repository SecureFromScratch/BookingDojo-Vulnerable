import { useState, useEffect } from 'react'
import { api, type AuditLog } from '../api/client'

export default function AuditLogsPage() {
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [errorResult, setErrorResult] = useState<object | null>(null)
  const [errorTriggerLoading, setErrorTriggerLoading] = useState(false)

  useEffect(() => {
    api.getAuditLogs()
      .then(setLogs)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  const handleTriggerError = async () => {
    setErrorTriggerLoading(true)
    setErrorResult(null)
    try {
      const result = await api.triggerError()
      setErrorResult(result)
    } finally {
      setErrorTriggerLoading(false)
    }
  }

  return (
    <div>
      <h1 className="page-title">Audit Logs</h1>

      {/* Lab 09 — Exception detail disclosure trigger */}
      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <h2 style={{ marginBottom: '0.5rem', fontSize: '1.1rem' }}>Exception Disclosure Test</h2>
        <p style={{ color: '#666', fontSize: '0.9rem', marginBottom: '1rem' }}>
          Trigger a server-side exception and observe what the API returns.
          In <strong>Vulnerable</strong> mode the full exception message, type, and stack trace are exposed.
          In <strong>Fixed</strong> mode only a generic message is returned.
        </p>
        <button className="btn-primary" onClick={handleTriggerError} disabled={errorTriggerLoading}>
          {errorTriggerLoading ? 'Triggering...' : 'Trigger Server Error'}
        </button>
        {errorResult && (
          <div style={{ marginTop: '1rem' }}>
            <label style={{ fontSize: '0.85rem', color: '#666' }}>Server response (HTTP 500):</label>
            <pre style={{
              background: '#1e1e1e', color: '#d4d4d4', padding: '1rem',
              borderRadius: 6, fontSize: '0.8rem', overflowX: 'auto',
              marginTop: '0.5rem', maxHeight: 400, overflowY: 'auto'
            }}>
              {JSON.stringify(errorResult, null, 2)}
            </pre>
          </div>
        )}
      </div>

      {loading && <p>Loading audit logs...</p>}
      {error && <div className="error-msg">{error}</div>}

      {!loading && logs.length === 0 && (
        <div className="card" style={{ textAlign: 'center', color: '#888' }}>No audit logs found.</div>
      )}

      {logs.length > 0 && (
        <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
          <table>
            <thead>
              <tr>
                <th>Timestamp</th>
                <th>User</th>
                <th>Action</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {logs.map(log => (
                <tr key={log.id}>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {new Date(log.timestamp).toLocaleString()}
                  </td>
                  <td>{log.username}</td>
                  <td>
                    <code style={{ fontSize: '0.8rem', background: '#f5f5f5', padding: '2px 6px', borderRadius: 3 }}>
                      {log.action}
                    </code>
                  </td>
                  {/* WORKSHOP: VULNERABLE — dangerouslySetInnerHTML renders stored HTML/JS from the server */}
                  <td dangerouslySetInnerHTML={{ __html: log.details }} />
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
