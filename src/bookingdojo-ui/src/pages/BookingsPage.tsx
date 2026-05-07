import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, type Booking, type Hotel } from '../api/client'
import Pagination from '../components/Pagination'

export default function BookingsPage() {
  const navigate = useNavigate()
  const [bookings, setBookings] = useState<Booking[]>([])
  const [bookingsPage, setBookingsPage] = useState(1)
  const [bookingsTotalPages, setBookingsTotalPages] = useState(1)
  const [bookingsTotal, setBookingsTotal] = useState(0)
  const [hotels, setHotels] = useState<Hotel[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [formError, setFormError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const [form, setForm] = useState({
    hotelId: '',
    checkIn: '',
    checkOut: '',
    cardNumber: '',
    specialRequests: '',
  })

  const selectedHotel = hotels.find(h => h.id === form.hotelId)
  const nights = form.checkIn && form.checkOut
    ? Math.max(0, Math.round((new Date(form.checkOut).getTime() - new Date(form.checkIn).getTime()) / 86400000))
    : 0
  const estimatedTotal = selectedHotel && nights > 0 ? selectedHotel.pricePerNight * nights : null

  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<Booking[] | null>(null)
  const [searchTruncated, setSearchTruncated] = useState(false)
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState('')



  const loadBookings = (page: number) => {
    setLoading(true)
    setError('')
    api.getMyBookings(page)
      .then(data => {
        setBookings(data.results)
        setBookingsPage(data.page)
        setBookingsTotalPages(data.totalPages)
        setBookingsTotal(data.total)
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    loadBookings(1)

    api.getAvailableHotels()
      .then(h => setHotels(h))
      .catch(() => {/* hotels dropdown silently empty if unavailable */})
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setFormError('')
    setSubmitting(true)
    try {
      await api.addToCart({
        hotelId: form.hotelId,
        checkIn: form.checkIn,
        checkOut: form.checkOut,
        cardNumber: form.cardNumber,
        specialRequests: form.specialRequests,
      })
      setForm({ hotelId: '', checkIn: '', checkOut: '', cardNumber: '', specialRequests: '' })
      navigate('/cart')
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'Failed to create booking')
    } finally {
      setSubmitting(false)
    }
  }

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault()
    setSearchError('')
    setSearching(true)
    try {
      const res = await api.searchBookings(searchQuery)
      setSearchResults(res.results)
      setSearchTruncated(res.truncated)
    } catch (err) {
      setSearchError(err instanceof Error ? err.message : 'Search failed')
    } finally {
      setSearching(false)
    }
  }

  const clearSearch = () => {
    setSearchResults(null)
    setSearchTruncated(false)
    setSearchQuery('')
    setSearchError('')
  }

  return (
    <div>
      <h1 className="page-title">Bookings</h1>

      {/* Create booking form */}
      <div className="card">
        <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>New Booking</h2>
        {formError && <div className="error-msg">{formError}</div>}
        <form onSubmit={handleSubmit}>
          <div className="form-group">
            <label>Hotel</label>
            <select
              value={form.hotelId}
              onChange={e => setForm(f => ({ ...f, hotelId: e.target.value }))}
              required
            >
              <option value="">Select a hotel...</option>
              {hotels.map(h => <option key={h.id} value={h.id}>{h.name} — {h.location}</option>)}
            </select>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
            <div className="form-group">
              <label>Check-in</label>
              <input type="date" value={form.checkIn}
                onChange={e => setForm(f => ({ ...f, checkIn: e.target.value }))} required />
            </div>
            <div className="form-group">
              <label>Check-out</label>
              <input type="date" value={form.checkOut}
                onChange={e => setForm(f => ({ ...f, checkOut: e.target.value }))} required />
            </div>
          </div>
          <div className="form-group">
            <label>Card number (16 digits)</label>
            <input
              type="text" maxLength={16} pattern="[0-9]{13,19}" placeholder="e.g. 4111111111111234"
              value={form.cardNumber}
              onChange={e => setForm(f => ({ ...f, cardNumber: e.target.value }))}
              required
            />
          </div>
          <div className="form-group">
            <label>Special requests</label>
            <textarea rows={2} value={form.specialRequests}
              onChange={e => setForm(f => ({ ...f, specialRequests: e.target.value }))} />
          </div>
          {estimatedTotal !== null && (
            <div style={{ marginBottom: '1rem', padding: '0.75rem 1rem', background: '#f0f9ff', border: '1px solid #bae6fd', borderRadius: '6px', fontSize: '0.95rem' }}>
              <span style={{ color: '#0369a1' }}>
                {nights} night{nights !== 1 ? 's' : ''} × ${selectedHotel!.pricePerNight.toFixed(2)} = <strong>${estimatedTotal.toFixed(2)}</strong>
              </span>
            </div>
          )}
          <button type="submit" className="btn-primary" disabled={submitting}>
            {submitting ? 'Booking...' : 'Book Now'}
          </button>
        </form>
      </div>

      {/* Search bookings */}
      <div className="card" style={{ marginTop: '1.5rem' }}>
        <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Search Bookings by Hotel</h2>
        {searchError && <div className="error-msg">{searchError}</div>}
        <div className="xss-hint" style={{ marginBottom: '0.75rem' }}>
          <strong>Workshop tip:</strong> This form always sends <code>pageSize=10</code>.
          In <em>Vulnerable</em> mode the server trusts that value but applies no cap of its
          own — bypass the UI directly: <code>curl … "…/search?q=&amp;pageSize=500"</code>.
          In <em>Fixed</em> mode the server ignores <code>pageSize</code> entirely and always
          caps at 10.
        </div>
        <form onSubmit={handleSearch} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end' }}>
          <div className="form-group" style={{ flex: 1, margin: 0 }}>
            <input
              type="text"
              placeholder="Hotel name..."
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
            />
          </div>
          <button type="submit" className="btn-primary" disabled={searching}>
            {searching ? 'Searching...' : 'Search'}
          </button>
          {searchResults !== null && (
            <button type="button" onClick={clearSearch}
              style={{ padding: '0.5rem 1rem', cursor: 'pointer' }}>
              Clear
            </button>
          )}
        </form>
      </div>

      {/* Search results */}
      {searchResults !== null && (
        <>
          <h2 style={{ margin: '1.5rem 0 0.75rem', fontSize: '1.1rem' }}>
            Search Results ({searchResults.length}){searchTruncated && (
              <span style={{ marginLeft: '0.75rem', fontSize: '0.8rem', color: '#b45309', fontWeight: 400 }}>
                — showing first 10 results
              </span>
            )}
          </h2>
          {searchResults.length === 0 && (
            <div className="card" style={{ textAlign: 'center', color: '#888' }}>No results.</div>
          )}
          {searchResults.length > 0 && (
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
              <table>
                <thead>
                  <tr>
                    <th>ID</th>
                    <th>User</th>
                    <th>Hotel</th>
                    <th>Check-in</th>
                    <th>Check-out</th>
                    <th>Card</th>
                  </tr>
                </thead>
                <tbody>
                  {searchResults.map(b => (
                    <tr key={b.id} onClick={() => navigate(`/bookings/${b.id}`)}
                      style={{ cursor: 'pointer' }}>
                      <td><strong style={{ fontFamily: 'monospace' }}>#{b.id}</strong></td>
                      <td style={{ color: '#666', fontSize: '0.9rem' }}>{b.username}</td>
                      <td>{b.hotelName}</td>
                      <td>{new Date(b.checkIn).toLocaleDateString()}</td>
                      <td>{new Date(b.checkOut).toLocaleDateString()}</td>
                      <td style={{ fontFamily: 'monospace' }}>**** **** **** {b.cardLastFour}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      {/* My bookings */}
      {searchResults === null && (
        <>
          <h2 style={{ margin: '1.5rem 0 0.75rem', fontSize: '1.1rem' }}>
            My Bookings
            {bookingsTotal > 0 && (
              <span style={{ marginLeft: '0.75rem', fontSize: '0.8rem', color: '#666', fontWeight: 400 }}>
                ({bookingsTotal} total)
              </span>
            )}
          </h2>
          {loading && <p>Loading...</p>}
          {error && <div className="error-msg">{error}</div>}
          {!loading && bookings.length === 0 && (
            <div className="card" style={{ textAlign: 'center', color: '#888' }}>No bookings yet.</div>
          )}
          {bookings.length > 0 && (
            <>
              <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
                <table>
                  <thead>
                    <tr>
                      <th>ID</th>
                      <th>Hotel</th>
                      <th>Check-in</th>
                      <th>Check-out</th>
                      <th>Card</th>
                      <th>Special Requests</th>
                    </tr>
                  </thead>
                  <tbody>
                    {bookings.map(b => (
                      <tr key={b.id} onClick={() => navigate(`/bookings/${b.id}`)}
                        style={{ cursor: 'pointer' }}>
                        <td><strong style={{ fontFamily: 'monospace' }}>#{b.id}</strong></td>
                        <td>{b.hotelName}</td>
                        <td>{new Date(b.checkIn).toLocaleDateString()}</td>
                        <td>{new Date(b.checkOut).toLocaleDateString()}</td>
                        <td style={{ fontFamily: 'monospace' }}>
                          {b.cardNumber
                            ? <span style={{ color: '#dc2626' }}>{b.cardNumber}</span>
                            : b.cardToken
                              ? <>{b.cardLastFour.padStart(16, '*')} <span style={{ color: '#16a34a', fontSize: '0.8rem' }}>{b.cardToken}</span></>
                              : `**** **** **** ${b.cardLastFour}`}
                        </td>
                        <td style={{ color: '#666', fontSize: '0.9rem' }}>{b.specialRequests}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {bookingsTotalPages > 1 && (
                <Pagination
                  page={bookingsPage}
                  totalPages={bookingsTotalPages}
                  disabled={loading}
                  onPageChange={loadBookings}
                />
              )}
            </>
          )}
        </>
      )}

    </div>
  )
}
