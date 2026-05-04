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
  const [formSuccess, setFormSuccess] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const [form, setForm] = useState({
    hotelId: '',
    checkIn: '',
    checkOut: '',
    cardLastFour: '',
    specialRequests: '',
  })

  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<Booking[] | null>(null)
  const [searchTruncated, setSearchTruncated] = useState(false)
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState('')

  const [couponCode, setCouponCode] = useState('')
  const [couponResult, setCouponResult] = useState<{ discountPercent: number; message: string } | null>(null)
  const [couponError, setCouponError] = useState('')
  const [couponSubmitting, setCouponSubmitting] = useState(false)


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
    setFormSuccess('')
    setSubmitting(true)
    try {
      const booking = await api.createBooking(form)
      setForm({ hotelId: '', checkIn: '', checkOut: '', cardLastFour: '', specialRequests: '' })
      setFormSuccess(`Booking #${booking.id} confirmed.`)
      loadBookings(1)
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

  const handleRedeemCoupon = async (e: React.FormEvent) => {
    e.preventDefault()
    setCouponError('')
    setCouponResult(null)
    setCouponSubmitting(true)
    try {
      const res = await api.redeemCoupon(couponCode)
      setCouponResult(res)
      setCouponCode('')
    } catch (err) {
      setCouponError(err instanceof Error ? err.message : 'Redemption failed')
    } finally {
      setCouponSubmitting(false)
    }
  }

  return (
    <div>
      <h1 className="page-title">Bookings</h1>

      {/* Create booking form */}
      <div className="card">
        <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>New Booking</h2>
        {formSuccess && <div className="success-msg">{formSuccess}</div>}
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
            <label>Card last 4 digits</label>
            <input
              type="text" maxLength={4} pattern="[0-9]{4}" placeholder="e.g. 1234"
              value={form.cardLastFour}
              onChange={e => setForm(f => ({ ...f, cardLastFour: e.target.value }))}
              required
            />
          </div>
          <div className="form-group">
            <label>Special requests</label>
            <textarea rows={2} value={form.specialRequests}
              onChange={e => setForm(f => ({ ...f, specialRequests: e.target.value }))} />
          </div>
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

      {/* Coupon redemption */}
      <div className="card" style={{ marginTop: '1.5rem' }}>
        <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Redeem Coupon</h2>
        {couponError && <div className="error-msg">{couponError}</div>}
        {couponResult && (
          <div className="success-msg">
            {couponResult.message} — <strong>{couponResult.discountPercent}% off</strong> your next booking
          </div>
        )}
        <form onSubmit={handleRedeemCoupon} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end' }}>
          <div className="form-group" style={{ flex: 1, margin: 0 }}>
            <input
              type="text"
              placeholder="Coupon code..."
              value={couponCode}
              onChange={e => setCouponCode(e.target.value)}
              required
            />
          </div>
          <button type="submit" className="btn-primary" disabled={couponSubmitting}>
            {couponSubmitting ? 'Applying...' : 'Apply'}
          </button>
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
                        <td style={{ fontFamily: 'monospace' }}>**** **** **** {b.cardLastFour}</td>
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
