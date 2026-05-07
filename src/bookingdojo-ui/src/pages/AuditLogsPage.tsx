import { useState, useEffect } from 'react'
import { api, type AuditLog } from '../api/client'
export default function AuditLogsPage() {
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [deleteError, setDeleteError] = useState('')

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

  const handleDelete = async (id: string) => {
    setDeleteError('')
    try {
      await api.deleteAuditLog(id)
      setLogs(prev => prev.filter(l => l.id !== id))
    } catch (err) {
      setDeleteError(err instanceof Error ? err.message : 'Failed to delete entry')
    }
  }

  return (
    <div>
      <h1 className="page-title">Audit Logs</h1>

      {/* Lab 11a — Log Injection */}
      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <h2 style={{ marginBottom: '0.5rem', fontSize: '1.1rem' }}>Log Injection (CRLF)</h2>
        <p style={{ color: '#666', fontSize: '0.9rem', marginBottom: '0.75rem' }}>
          In <strong>Vulnerable</strong> mode, user-controlled values are string-interpolated
          directly into <code>ILogger</code> messages. A newline character inside a username
          creates a fake line in the server's application log that looks like a legitimate entry
          to anyone reading a terminal or log file.
        </p>
        <div className="xss-hint">
          <strong>Attack (run in your terminal):</strong>
          <pre style={{ marginTop: '0.5rem', marginBottom: 0, fontSize: '0.8rem', whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>{`# Login with a crafted username containing a newline + fake log line
curl -s -X POST http://localhost:5000/api/auth/login \\
  -H "Content-Type: application/json" \\
  -d $'{"username":"alice\\n[CRITICAL] 2026-05-05 admin ROLE_CHANGED to SuperAdmin","password":"x"}'

# Then watch the server console — you will see a fake [CRITICAL] line
# mixed in with real log output. In Fixed mode the \\n is escaped to \\\\n.`}
          </pre>
        </div>
      </div>

      {/* Lab 11b — Audit Log Deletion */}
      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <h2 style={{ marginBottom: '0.5rem', fontSize: '1.1rem' }}>Audit Log Deletion</h2>
        <p style={{ color: '#666', fontSize: '0.9rem', marginBottom: '0.5rem' }}>
          In <strong>Vulnerable</strong> mode both <code>AdminUser</code> and <code>SupportUser</code>
          can delete any entry — a SupportUser can silently erase evidence of their own actions.
          No secondary log is created, so the deletion leaves no trace.
        </p>
        <p style={{ color: '#666', fontSize: '0.9rem' }}>
          In <strong>Fixed</strong> mode only <code>AdminUser</code> may delete, and every deletion
          creates an immutable <code>LOG_ENTRY_DELETED</code> record so erasure is itself audited.
        </p>
      </div>

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
      {deleteError && <div className="error-msg">{deleteError}</div>}

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
                <th></th>
              </tr>
            </thead>
            <tbody>
              {logs.map(log => (
                <tr key={log.id}>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {new Date(log.timestamp).toLocaleString()}
                  </td>
                  {/* white-space: pre makes injected newlines visible in the cell */}
                  <td style={{ fontFamily: 'monospace', fontSize: '0.85rem', whiteSpace: 'pre' }}>{log.username}</td>
                  <td>
                    <code style={{ fontSize: '0.8rem', background: '#f5f5f5', padding: '2px 6px', borderRadius: 3 }}>
                      {log.action}
                    </code>
                  </td>
                  {/* WORKSHOP: VULNERABLE — dangerouslySetInnerHTML renders stored HTML/JS (Lab 01 XSS) */}
                  <td dangerouslySetInnerHTML={{ __html: log.details }} />
                  <td>
                    <button
                      onClick={() => handleDelete(log.id)}
                      style={{
                        color: '#dc2626', background: 'none', border: 'none',
                        cursor: 'pointer', fontSize: '0.85rem', whiteSpace: 'nowrap',
                      }}
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
