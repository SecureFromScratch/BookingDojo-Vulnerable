import { useState, useEffect } from 'react'
import { api, type Hotel, type Partner, type CreateHotelRequest } from '../api/client'
import { useAuth } from '../contexts/AuthContext'

export default function HotelsPage() {
  const { user } = useAuth()
  const [hotels, setHotels] = useState<Hotel[]>([])
  const [partners, setPartners] = useState<Partner[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [formError, setFormError] = useState('')
  const [formSuccess, setFormSuccess] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const [form, setForm] = useState<CreateHotelRequest>({
    name: '',
    location: '',
    description: '',
    pricePerNight: 0,
  })

  useEffect(() => {
    api.getHotels()
      .then(setHotels)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))

    if (user?.role === 'AdminUser') {
      api.getPartners()
        .then(setPartners)
        .catch(err => setFormError(`Failed to load partners: ${err.message}`))
    }
  }, [user?.role])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setFormError('')
    setFormSuccess('')
    setSubmitting(true)
    try {
      const hotel = await api.createHotel(form)
      setHotels(prev => [hotel, ...prev])
      setForm({ name: '', location: '', description: '', pricePerNight: 0, partnerId: undefined })
      setShowForm(false)
      setFormSuccess(`Hotel "${hotel.name}" created successfully.`)
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to create hotel')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', marginBottom: '1.5rem' }}>
        <h1 className="page-title" style={{ marginBottom: 0, flex: 1 }}>Hotels</h1>
        {user?.role === 'AdminUser' && (
          <button className="btn-primary" onClick={() => setShowForm(!showForm)}>
            {showForm ? 'Cancel' : '+ Add Hotel'}
          </button>
        )}
      </div>

      {formSuccess && <div className="success-msg">{formSuccess}</div>}

      {showForm && (
        <div className="card">
          <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Add New Hotel</h2>
          <div className="xss-hint">
            <strong>Workshop tip:</strong> The hotel description is stored and displayed to other users.
            Try adding HTML content in the description — then check the Audit Logs page.
            Payload example: <code>&lt;img src=x onerror="alert('XSS from ' + document.cookie)"&gt;</code>
          </div>
          {formError && <div className="error-msg">{formError}</div>}
          <form onSubmit={handleSubmit}>
            <div className="form-group">
              <label>Hotel Name</label>
              <input
                type="text"
                value={form.name}
                onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                required
              />
            </div>
            <div className="form-group">
              <label>Location</label>
              <input
                type="text"
                value={form.location}
                onChange={e => setForm(f => ({ ...f, location: e.target.value }))}
                required
              />
            </div>
            <div className="form-group">
              <label>Description</label>
              <textarea
                rows={3}
                value={form.description}
                onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                required
              />
            </div>
            <div className="form-group">
              <label>Price per night (USD)</label>
              <input
                type="number"
                min="1"
                step="0.01"
                value={form.pricePerNight || ''}
                onChange={e => setForm(f => ({ ...f, pricePerNight: parseFloat(e.target.value) || 0 }))}
                required
              />
            </div>
            <div className="form-group">
              <label>Partner</label>
              <select
                value={form.partnerId ?? ''}
                onChange={e => setForm(f => ({ ...f, partnerId: e.target.value || undefined }))}
                required
              >
                <option value="">Select a partner...</option>
                {partners.map(p => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </div>
            <button type="submit" className="btn-primary" disabled={submitting}>
              {submitting ? 'Creating...' : 'Create Hotel'}
            </button>
          </form>
        </div>
      )}

      {loading && <p>Loading hotels...</p>}
      {error && <div className="error-msg">{error}</div>}

      {!loading && hotels.length === 0 && (
        <div className="card" style={{ textAlign: 'center', color: '#888' }}>No hotels found.</div>
      )}

      {hotels.length > 0 && (
        <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
          <table>
            <thead>
              <tr>
                <th>Name</th>
                <th>Location</th>
                <th>Partner</th>
                <th>Price/Night</th>
                <th>Description</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {hotels.map(hotel => (
                <tr key={hotel.id}>
                  <td><strong>{hotel.name}</strong></td>
                  <td>{hotel.location}</td>
                  <td>{hotel.partnerName}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>${hotel.pricePerNight.toFixed(2)}</td>
                  <td style={{ maxWidth: 300 }}>{hotel.description}</td>
                  <td style={{ whiteSpace: 'nowrap' }}>{new Date(hotel.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
