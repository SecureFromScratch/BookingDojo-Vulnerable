import { useEffect, useState } from 'react'
import { api } from '../api/client'

type Profile = {
  username: string
  role: string
  displayName?: string
  bio?: string
  avatarUrl?: string
}

type FetchResult = {
  url: string
  statusCode?: number
  body?: string
  error?: string
}

export default function ProfilePage() {
  const [profile, setProfile] = useState<Profile | null>(null)
  const [displayName, setDisplayName] = useState('')
  const [bio, setBio] = useState('')
  const [saving, setSaving] = useState(false)
  const [saveMsg, setSaveMsg] = useState('')

  const [avatarTab, setAvatarTab] = useState<'file' | 'url'>('file')
  const [avatarUrl, setAvatarUrl] = useState('')
  const [fetchResult, setFetchResult] = useState<FetchResult | null>(null)
  const [fetchLoading, setFetchLoading] = useState(false)
  const [uploadMsg, setUploadMsg] = useState('')

  useEffect(() => {
    api.getProfile().then(p => {
      setProfile(p)
      setDisplayName(p.displayName ?? '')
      setBio(p.bio ?? '')
    })
  }, [])

  const handleSaveProfile = async () => {
    setSaving(true)
    setSaveMsg('')
    try {
      const updated = await api.updateProfile({ displayName: displayName || undefined, bio: bio || undefined })
      setProfile(updated)
      setSaveMsg('Profile saved.')
    } catch (e: any) {
      setSaveMsg('Error: ' + e.message)
    } finally {
      setSaving(false)
    }
  }

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    setUploadMsg('Uploading…')
    try {
      const result = await api.uploadAvatar(file)
      if (result.avatarUrl) {
        setProfile(p => p ? { ...p, avatarUrl: result.avatarUrl } : p)
        setUploadMsg('Avatar updated.')
      } else {
        setUploadMsg(result.message ?? 'Upload failed')
      }
    } catch (e: any) {
      setUploadMsg('Error: ' + e.message)
    }
  }

  const handleFetchUrl = async () => {
    if (!avatarUrl.trim()) return
    setFetchLoading(true)
    setFetchResult(null)
    try {
      const result = await api.setAvatarFromUrl(avatarUrl.trim())
      setFetchResult(result as FetchResult)
      if (result.avatarUrl || result.url) {
        setProfile(p => p ? { ...p, avatarUrl: result.avatarUrl ?? result.url } : p)
      }
    } catch (e: any) {
      setFetchResult({ url: avatarUrl, error: e.message })
    } finally {
      setFetchLoading(false)
    }
  }

  const isJson = (s: string) => {
    try { JSON.parse(s); return true } catch { return false }
  }

  if (!profile) return <div className="loading">Loading…</div>

  return (
    <div style={{ maxWidth: 680, margin: '2rem auto', padding: '0 1rem' }}>
      <h1>My Profile</h1>

      <div style={{ display: 'flex', gap: '2rem', alignItems: 'flex-start', marginBottom: '2rem' }}>
        <div>
          {profile.avatarUrl && !profile.avatarUrl.startsWith('http') ? (
            <img
              src={profile.avatarUrl}
              alt="avatar"
              style={{ width: 100, height: 100, borderRadius: '50%', objectFit: 'cover', border: '2px solid #444' }}
            />
          ) : profile.avatarUrl ? (
            <div style={{ width: 100, height: 100, borderRadius: '50%', background: '#333', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 12, color: '#aaa', textAlign: 'center', padding: 4 }}>
              external URL
            </div>
          ) : (
            <div style={{ width: 100, height: 100, borderRadius: '50%', background: '#333', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 32 }}>
              {profile.username[0].toUpperCase()}
            </div>
          )}
        </div>
        <div>
          <p style={{ margin: 0, fontSize: '1.2rem', fontWeight: 'bold' }}>{profile.displayName || profile.username}</p>
          <p style={{ margin: '0.25rem 0', color: '#aaa', fontSize: '0.9rem' }}>@{profile.username}</p>
          <span className={`role-badge ${profile.role}`}>{profile.role}</span>
          {profile.bio && <p style={{ marginTop: '0.5rem', color: '#ccc' }}>{profile.bio}</p>}
        </div>
      </div>

      <section style={{ background: '#1e1e2e', padding: '1.5rem', borderRadius: 8, marginBottom: '1.5rem' }}>
        <h2 style={{ marginTop: 0 }}>Edit Profile</h2>
        <label style={{ display: 'block', marginBottom: '0.5rem' }}>Display Name</label>
        <input
          value={displayName}
          onChange={e => setDisplayName(e.target.value)}
          placeholder={profile.username}
          style={{ width: '100%', padding: '0.5rem', marginBottom: '1rem', background: '#2a2a3e', border: '1px solid #444', borderRadius: 4, color: '#fff' }}
        />
        <label style={{ display: 'block', marginBottom: '0.5rem' }}>Bio</label>
        <textarea
          value={bio}
          onChange={e => setBio(e.target.value)}
          rows={3}
          placeholder="Tell us about yourself…"
          style={{ width: '100%', padding: '0.5rem', marginBottom: '1rem', background: '#2a2a3e', border: '1px solid #444', borderRadius: 4, color: '#fff', resize: 'vertical' }}
        />
        <button onClick={handleSaveProfile} disabled={saving} className="btn-primary">
          {saving ? 'Saving…' : 'Save Profile'}
        </button>
        {saveMsg && <span style={{ marginLeft: '1rem', color: '#aaa' }}>{saveMsg}</span>}
      </section>

      <section style={{ background: '#1e1e2e', padding: '1.5rem', borderRadius: 8 }}>
        <h2 style={{ marginTop: 0 }}>Profile Picture</h2>

        <div style={{ display: 'flex', gap: '1rem', marginBottom: '1rem' }}>
          <button
            onClick={() => setAvatarTab('file')}
            style={{ padding: '0.4rem 1rem', background: avatarTab === 'file' ? '#6c63ff' : '#2a2a3e', border: '1px solid #444', borderRadius: 4, color: '#fff', cursor: 'pointer' }}
          >
            Upload File
          </button>
          <button
            onClick={() => setAvatarTab('url')}
            style={{ padding: '0.4rem 1rem', background: avatarTab === 'url' ? '#6c63ff' : '#2a2a3e', border: '1px solid #444', borderRadius: 4, color: '#fff', cursor: 'pointer' }}
          >
            Fetch from URL
          </button>
        </div>

        {avatarTab === 'file' && (
          <div>
            <p style={{ color: '#aaa', fontSize: '0.9rem' }}>Upload an image (max 512 KB). Stored securely on the server.</p>
            <input type="file" accept="image/*" onChange={handleFileUpload} />
            {uploadMsg && <p style={{ color: '#aaa', marginTop: '0.5rem' }}>{uploadMsg}</p>}
          </div>
        )}

        {avatarTab === 'url' && (
          <div>
            <p style={{ color: '#aaa', fontSize: '0.9rem' }}>
              The server will fetch the image from this URL to preview and validate it, then store the URL as your avatar.
            </p>
            <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '1rem' }}>
              <input
                value={avatarUrl}
                onChange={e => setAvatarUrl(e.target.value)}
                placeholder="https://example.com/my-photo.jpg"
                style={{ flex: 1, padding: '0.5rem', background: '#2a2a3e', border: '1px solid #444', borderRadius: 4, color: '#fff' }}
              />
              <button onClick={handleFetchUrl} disabled={fetchLoading} className="btn-primary">
                {fetchLoading ? 'Fetching…' : 'Set Avatar'}
              </button>
            </div>

            {fetchResult && (
              <div style={{ background: '#0d1117', border: '1px solid #30363d', borderRadius: 6, padding: '1rem' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '0.5rem' }}>
                  <span style={{ color: '#aaa', fontSize: '0.85rem' }}>Server response</span>
                  {fetchResult.statusCode && (
                    <span style={{ color: fetchResult.statusCode < 300 ? '#3fb950' : '#f85149', fontSize: '0.85rem' }}>
                      HTTP {fetchResult.statusCode}
                    </span>
                  )}
                  {fetchResult.error && <span style={{ color: '#f85149', fontSize: '0.85rem' }}>{fetchResult.error}</span>}
                </div>
                {fetchResult.body && (
                  <pre style={{
                    margin: 0,
                    fontSize: '0.8rem',
                    color: isJson(fetchResult.body) ? '#79c0ff' : '#e6edf3',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-all',
                    maxHeight: 400,
                    overflowY: 'auto'
                  }}>
                    {isJson(fetchResult.body)
                      ? JSON.stringify(JSON.parse(fetchResult.body), null, 2)
                      : fetchResult.body}
                  </pre>
                )}
              </div>
            )}
          </div>
        )}
      </section>
    </div>
  )
}
