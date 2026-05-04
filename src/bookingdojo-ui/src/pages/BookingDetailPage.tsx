import { useState, useEffect } from 'react'
import { useParams, Link } from 'react-router-dom'
import { api, type Booking } from '../api/client'

export default function BookingDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [booking, setBooking] = useState<Booking | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    api.getBookingById(Number(id))
      .then(setBooking)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [id])

  return (
    <div>
      <div style={{ marginBottom: '1.5rem' }}>
        <Link to="/bookings" style={{ color: '#888', fontSize: '0.9rem' }}>← My Bookings</Link>
      </div>

      <h1 className="page-title">Booking #{id}</h1>

      {loading && <p>Loading...</p>}

      {error && (
        <div className="error-msg">
          {error === 'Forbidden' ? 'You do not have permission to view this booking.' : error}
        </div>
      )}

      {booking && (
        <div className="card" style={{ maxWidth: 520 }}>
          <dl style={{ display: 'grid', gridTemplateColumns: '140px 1fr', gap: '0.75rem 1rem', margin: 0 }}>
            <dt style={{ color: '#888', fontSize: '0.9rem' }}>Booking ID</dt>
            <dd style={{ fontFamily: 'monospace', fontWeight: 600 }}>#{booking.id}</dd>

            <dt style={{ color: '#888', fontSize: '0.9rem' }}>Hotel</dt>
            <dd>{booking.hotelName}</dd>

            <dt style={{ color: '#888', fontSize: '0.9rem' }}>Check-in</dt>
            <dd>{new Date(booking.checkIn).toLocaleDateString()}</dd>

            <dt style={{ color: '#888', fontSize: '0.9rem' }}>Check-out</dt>
            <dd>{new Date(booking.checkOut).toLocaleDateString()}</dd>

            <dt style={{ color: '#888', fontSize: '0.9rem' }}>Card</dt>
            <dd style={{ fontFamily: 'monospace' }}>**** **** **** {booking.cardLastFour}</dd>

            {booking.specialRequests && (
              <>
                <dt style={{ color: '#888', fontSize: '0.9rem' }}>Special requests</dt>
                <dd style={{ color: '#666' }}>{booking.specialRequests}</dd>
              </>
            )}
          </dl>
        </div>
      )}
    </div>
  )
}
