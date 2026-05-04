import { useState, useEffect } from 'react'
import { api, type Cart, type CheckoutResult } from '../api/client'

export default function CartPage() {
  const [cart, setCart] = useState<Cart | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [couponCode, setCouponCode] = useState('')
  const [checkoutResult, setCheckoutResult] = useState<CheckoutResult | null>(null)
  const [checkoutError, setCheckoutError] = useState('')
  const [checkingOut, setCheckingOut] = useState(false)

  useEffect(() => {
    api.getCart()
      .then(setCart)
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  const handleRemove = async (itemId: number) => {
    try {
      await api.removeFromCart(itemId)
      setCart(prev => prev ? { ...prev, items: prev.items.filter(i => i.id !== itemId) } : prev)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove item')
    }
  }

  const handleCheckout = async (e: React.FormEvent) => {
    e.preventDefault()
    setCheckoutError('')
    setCheckingOut(true)
    try {
      const result = await api.checkout(couponCode || undefined)
      setCheckoutResult(result)
      setCart(prev => prev ? { ...prev, items: [] } : prev)
      setCouponCode('')
    } catch (err) {
      setCheckoutError(err instanceof Error ? err.message : 'Checkout failed')
    } finally {
      setCheckingOut(false)
    }
  }

  if (loading) return <p>Loading cart...</p>

  return (
    <div>
      <h1 className="page-title">Cart</h1>

      {error && <div className="error-msg">{error}</div>}

      {checkoutResult && (
        <div className="card" style={{ marginBottom: '1.5rem' }}>
          <div className="success-msg" style={{ marginBottom: '0.5rem' }}>
            Booking confirmed — {checkoutResult.bookings.length} booking{checkoutResult.bookings.length !== 1 ? 's' : ''} created.
            {checkoutResult.couponMessage && <> {checkoutResult.couponMessage}.</>}
          </div>
          <table>
            <thead>
              <tr><th>Booking #</th><th>Hotel</th><th>Check-in</th><th>Check-out</th><th>Card</th></tr>
            </thead>
            <tbody>
              {checkoutResult.bookings.map(b => (
                <tr key={b.id}>
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
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {cart && cart.items.length === 0 && !checkoutResult && (
        <div className="card" style={{ textAlign: 'center', color: '#888' }}>
          Your cart is empty. Go to <a href="/hotels">Hotels</a> to add items.
        </div>
      )}

      {cart && cart.items.length > 0 && (
        <>
          <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
            <table>
              <thead>
                <tr>
                  <th>Hotel</th>
                  <th>Check-in</th>
                  <th>Check-out</th>
                  <th>Card</th>
                  <th>Special Requests</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {cart.items.map(item => (
                  <tr key={item.id}>
                    <td><strong>{item.hotelName}</strong></td>
                    <td>{new Date(item.checkIn).toLocaleDateString()}</td>
                    <td>{new Date(item.checkOut).toLocaleDateString()}</td>
                    <td style={{ fontFamily: 'monospace' }}>
                      {item.cardNumber
                        ? <span style={{ color: '#dc2626' }}>{item.cardNumber}</span>
                        : `**** **** **** ${item.cardLastFour}`}
                    </td>
                    <td style={{ color: '#666', fontSize: '0.9rem' }}>{item.specialRequests}</td>
                    <td>
                      <button onClick={() => handleRemove(item.id)}
                        style={{ color: '#dc2626', background: 'none', border: 'none', cursor: 'pointer', fontSize: '0.85rem' }}>
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="card" style={{ marginTop: '1.5rem' }}>
            <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Checkout</h2>
            <div className="xss-hint" style={{ marginBottom: '0.75rem' }}>
              <strong>Workshop tip:</strong> Apply coupon <code>SAVE10</code> (1 use) or <code>SUMMER20</code> (3 uses).
              In <em>Vulnerable</em> mode, fire two concurrent checkout requests with the same coupon — both succeed and the coupon is over-redeemed.
              In <em>Fixed</em> mode the server uses an atomic SQL UPDATE, so only one request wins.
            </div>
            {checkoutError && <div className="error-msg" style={{ marginBottom: '0.75rem' }}>{checkoutError}</div>}
            <form onSubmit={handleCheckout} style={{ display: 'flex', gap: '0.75rem', alignItems: 'flex-end' }}>
              <div className="form-group" style={{ flex: 1, margin: 0 }}>
                <label>Coupon code (optional)</label>
                <div style={{ display: 'flex', gap: '0.5rem' }}>
                  <input
                    type="text"
                    placeholder="e.g. SAVE10"
                    value={couponCode}
                    onChange={e => setCouponCode(e.target.value)}
                    style={{ flex: 1 }}
                  />
                  {couponCode && (
                    <button
                      type="button"
                      onClick={() => setCouponCode('')}
                      style={{ padding: '0.4rem 0.75rem', background: 'none', border: '1px solid #ccc', borderRadius: '4px', cursor: 'pointer', color: '#666' }}
                    >
                      Clear
                    </button>
                  )}
                </div>
              </div>
              <button type="submit" className="btn-primary" disabled={checkingOut}>
                {checkingOut ? 'Processing...' : 'Checkout'}
              </button>
            </form>
          </div>
        </>
      )}
    </div>
  )
}
