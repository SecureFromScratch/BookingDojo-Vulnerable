import { useState, useEffect } from 'react'
import { api, type Cart, type CheckoutResult } from '../api/client'

export default function CartPage() {
  const [cart, setCart] = useState<Cart | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [couponInput, setCouponInput] = useState('')
  const [appliedCoupon, setAppliedCoupon] = useState<{ code: string; discountPercent: number; count: number } | null>(null)
  const [couponError, setCouponError] = useState('')
  const [applyingCoupon, setApplyingCoupon] = useState(false)
  const [checkoutResult, setCheckoutResult] = useState<CheckoutResult | null>(null)
  const [checkoutError, setCheckoutError] = useState('')
  const [checkingOut, setCheckingOut] = useState(false)
  const [mfaRequired, setMfaRequired] = useState(false)
  const [mfaCode, setMfaCode] = useState('')
  const [mfaError, setMfaError] = useState('')
  const [mfaLoading, setMfaLoading] = useState(false)
  const [pendingCoupon, setPendingCoupon] = useState<string | undefined>(undefined)

  useEffect(() => {
    api.getCart()
      .then(cart => {
        setCart(cart)
        if (cart.appliedCouponCode && cart.appliedCouponDiscountPercent != null && cart.appliedCouponCount > 0) {
          setAppliedCoupon({ code: cart.appliedCouponCode, discountPercent: cart.appliedCouponDiscountPercent, count: cart.appliedCouponCount })
        }
      })
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

  const handleApplyCoupon = async (e: React.FormEvent) => {
    e.preventDefault()
    setCouponError('')
    setApplyingCoupon(true)
    try {
      const code = couponInput.trim()
      const result = await api.redeemCoupon(code)
      // Re-fetch cart so count is server-authoritative
      const updatedCart = await api.getCart()
      setCart(updatedCart)
      if (updatedCart.appliedCouponCode && updatedCart.appliedCouponDiscountPercent != null) {
        setAppliedCoupon({ code: updatedCart.appliedCouponCode, discountPercent: updatedCart.appliedCouponDiscountPercent, count: updatedCart.appliedCouponCount })
      } else {
        setAppliedCoupon({ code, discountPercent: result.discountPercent, count: 1 })
      }
      setCouponInput('')
    } catch (err) {
      setCouponError(err instanceof Error ? err.message : 'Invalid coupon')
    } finally {
      setApplyingCoupon(false)
    }
  }

  const handleRemoveCoupon = async () => {
    if (!appliedCoupon) return
    try {
      await api.cancelCoupon(appliedCoupon.code)
    } catch {
      // best-effort — clear from UI regardless
    }
    setAppliedCoupon(null)
    setCouponError('')
  }

  const handleCheckout = async (e: React.FormEvent) => {
    e.preventDefault()
    setCheckoutError('')
    setCheckingOut(true)
    try {
      const result = await api.checkout(appliedCoupon?.code)
      setCheckoutResult(result)
      setCart(prev => prev ? { ...prev, items: [] } : prev)
      setAppliedCoupon(null)
    } catch (err: any) {
      if (err.requiresMfa) {
        setPendingCoupon(appliedCoupon?.code)
        setMfaRequired(true)
        setMfaError('')
        api.mfaChallenge().catch(() => {})
      } else {
        setCheckoutError(err instanceof Error ? err.message : 'Checkout failed')
      }
    } finally {
      setCheckingOut(false)
    }
  }

  const handleMfaVerify = async (e: React.FormEvent) => {
    e.preventDefault()
    setMfaLoading(true)
    setMfaError('')
    try {
      await api.mfaVerify(mfaCode)
      setMfaRequired(false)
      setMfaCode('')
      setCheckingOut(true)
      try {
        const result = await api.checkout(pendingCoupon)
        setCheckoutResult(result)
        setCart(prev => prev ? { ...prev, items: [] } : prev)
        setAppliedCoupon(null)
      } catch (err: any) {
        setCheckoutError(err instanceof Error ? err.message : 'Checkout failed')
      } finally {
        setCheckingOut(false)
      }
    } catch (err: any) {
      setMfaError(err.message || 'Incorrect code')
    } finally {
      setMfaLoading(false)
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
              <tr><th>Booking #</th><th>Hotel</th><th>Check-in</th><th>Check-out</th><th>Total Paid</th><th>Card</th></tr>
            </thead>
            <tbody>
              {checkoutResult.bookings.map(b => (
                <tr key={b.id}>
                  <td><strong style={{ fontFamily: 'monospace' }}>#{b.id}</strong></td>
                  <td>{b.hotelName}</td>
                  <td>{new Date(b.checkIn).toLocaleDateString()}</td>
                  <td>{new Date(b.checkOut).toLocaleDateString()}</td>
                  <td style={{ fontFamily: 'monospace', whiteSpace: 'nowrap' }}>${b.totalPrice.toFixed(2)}</td>
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
                  <th>Total</th>
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
                    <td style={{ fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
                      {appliedCoupon ? (
                        <>
                          <span style={{ textDecoration: 'line-through', color: '#9ca3af', marginRight: '0.4rem' }}>
                            ${item.totalPrice.toFixed(2)}
                          </span>
                          <span style={{ color: '#16a34a' }}>
                            ${(item.totalPrice * Math.pow(1 - appliedCoupon.discountPercent / 100, appliedCoupon.count)).toFixed(2)}
                          </span>
                        </>
                      ) : (
                        `$${item.totalPrice.toFixed(2)}`
                      )}
                    </td>
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

          {(() => {
            const subtotal = cart.items.reduce((sum, i) => sum + i.totalPrice, 0)
            const factor = appliedCoupon ? Math.pow(1 - appliedCoupon.discountPercent / 100, appliedCoupon.count) : 1
            const total = subtotal * factor
            const discount = subtotal - total
            return (
              <div style={{ textAlign: 'right', padding: '0.75rem 1rem', fontSize: '0.95rem', lineHeight: '1.8' }}>
                <div>Subtotal: ${subtotal.toFixed(2)}</div>
                {appliedCoupon && (
                  <div style={{ color: '#16a34a' }}>
                    {appliedCoupon.count > 1
                      ? `Discount (${appliedCoupon.code} × ${appliedCoupon.count} — ${appliedCoupon.discountPercent}% each, compounded): −$${discount.toFixed(2)}`
                      : `Discount (${appliedCoupon.code} — ${appliedCoupon.discountPercent}% off): −$${discount.toFixed(2)}`}
                  </div>
                )}
                <div style={{ fontWeight: 700, fontSize: '1.05rem' }}>Total: ${total.toFixed(2)}</div>
              </div>
            )
          })()}

          <div className="card" style={{ marginTop: '1.5rem' }}>
            <h2 style={{ marginBottom: '1rem', fontSize: '1.1rem' }}>Checkout</h2>
            <div className="xss-hint" style={{ marginBottom: '0.75rem' }}>
              <strong>Workshop tip (Race Condition — TOCTOU):</strong> Coupon <code>SAVE10</code> allows only 1 use.
              In <em>Vulnerable</em> mode, send two concurrent <code>POST /api/coupons/redeem</code> requests before either completes —
              both pass the "uses remaining" check before either writes, so both return <code>200 OK</code> and the use count exceeds the limit.
              The second request should have returned <code>409 Conflict</code>.
              In <em>Fixed</em> mode the server uses an atomic SQL UPDATE, so only one request wins and the second correctly gets <code>409</code>.
            </div>

            {appliedCoupon ? (
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '1rem', padding: '0.6rem 1rem', background: '#f0fdf4', border: '1px solid #86efac', borderRadius: '6px' }}>
                <span style={{ color: '#16a34a', fontWeight: 600, flex: 1 }}>
                  {appliedCoupon.code} — {appliedCoupon.discountPercent}% off
                  {appliedCoupon.count > 1 && <span style={{ color: '#dc2626' }}> × {appliedCoupon.count} (race condition!)</span>}
                </span>
                <button type="button" onClick={handleRemoveCoupon}
                  style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#6b7280', fontSize: '0.85rem', textDecoration: 'underline' }}>
                  Remove
                </button>
              </div>
            ) : (
              <form onSubmit={handleApplyCoupon} style={{ display: 'flex', gap: '0.5rem', alignItems: 'flex-end', marginBottom: '1rem' }}>
                <div className="form-group" style={{ flex: 1, margin: 0 }}>
                  <label>Coupon code (optional)</label>
                  <input
                    type="text"
                    placeholder="e.g. SAVE10"
                    value={couponInput}
                    onChange={e => { setCouponInput(e.target.value); setCouponError('') }}
                  />
                </div>
                <button type="submit" disabled={applyingCoupon || !couponInput.trim()}
                  style={{ padding: '0.5rem 1.25rem', border: '1px solid #1d4ed8', borderRadius: '6px', background: 'white', color: '#1d4ed8', cursor: 'pointer', fontWeight: 500, whiteSpace: 'nowrap' }}>
                  {applyingCoupon ? 'Checking...' : 'Apply'}
                </button>
              </form>
            )}
            {couponError && <div className="error-msg" style={{ marginBottom: '0.75rem' }}>{couponError}</div>}

            {checkoutError && <div className="error-msg" style={{ marginBottom: '0.75rem' }}>{checkoutError}</div>}
            {!mfaRequired && (
              <form onSubmit={handleCheckout}>
                <button type="submit" className="btn-primary" disabled={checkingOut}>
                  {checkingOut ? 'Processing...' : 'Checkout'}
                </button>
              </form>
            )}

            {mfaRequired && (
              <div style={{ marginTop: '1rem', background: '#0f172a', border: '1px solid #334155', borderRadius: 8, padding: '1.25rem' }}>
                <h3 style={{ margin: '0 0 0.5rem', fontSize: '1rem' }}>Identity verification required</h3>
                <p style={{ color: '#94a3b8', margin: '0 0 1rem', fontSize: '0.875rem' }}>
                  A one-time code has been sent to your registered device. Enter it below to complete your purchase.
                </p>
                <form onSubmit={handleMfaVerify} style={{ display: 'flex', gap: '0.5rem', alignItems: 'flex-end' }}>
                  <div className="form-group" style={{ flex: 1, margin: 0 }}>
                    <label>One-time code</label>
                    <input
                      type="text"
                      inputMode="numeric"
                      maxLength={4}
                      placeholder="0000"
                      value={mfaCode}
                      onChange={e => { setMfaCode(e.target.value.replace(/\D/g, '')); setMfaError('') }}
                      style={{ letterSpacing: '0.4em', fontFamily: 'monospace', fontSize: '1.2rem', textAlign: 'center' }}
                      autoFocus
                    />
                  </div>
                  <button type="submit" className="btn-primary" disabled={mfaLoading || mfaCode.length !== 4}>
                    {mfaLoading ? 'Verifying…' : 'Verify & Pay'}
                  </button>
                </form>
                {mfaError && <div className="error-msg" style={{ marginTop: '0.5rem' }}>{mfaError}</div>}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  )
}
