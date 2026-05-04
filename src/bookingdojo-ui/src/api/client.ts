export interface LoginRequest {
  username: string
  password: string
}

export interface User {
  username: string
  role: string
  partnerId?: string
}

export interface Hotel {
  id: string
  name: string
  location: string
  description: string
  partnerName: string
  createdAt: string
}

export interface CreateHotelRequest {
  name: string
  location: string
  description: string
  partnerId?: string
}

export interface Partner {
  id: string
  name: string
}

export interface Booking {
  id: number
  userId: string
  username: string
  hotelId: string
  hotelName: string
  checkIn: string
  checkOut: string
  cardLastFour: string
  cardNumber: string | null
  cardToken: string | null
  specialRequests: string
  createdAt: string
}

export interface CreateBookingRequest {
  hotelId: string
  checkIn: string
  checkOut: string
  cardNumber: string
  specialRequests: string
}

export interface AuditLog {
  id: string
  timestamp: string
  username: string
  action: string
  details: string
}

export interface CartItem {
  id: number
  hotelId: string
  hotelName: string
  checkIn: string
  checkOut: string
  cardLastFour: string
  cardNumber: string | null
  specialRequests: string
}

export interface Cart {
  id: number
  items: CartItem[]
}

export interface CheckoutResult {
  bookings: Booking[]
  discountPercent: number | null
  couponMessage: string | null
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...options?.headers,
    },
    ...options,
  })

  if (!response.ok) {
    const body = await response.json().catch(() => ({ message: response.statusText }))
    throw new Error(body.message || `HTTP ${response.status}`)
  }

  if (response.status === 204) return undefined as T
  return response.json()
}

export const api = {
  login: (data: LoginRequest) =>
    request<User>('/bff/auth/login', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  me: () => request<User>('/bff/auth/me'),

  logout: () =>
    request<void>('/bff/auth/logout', { method: 'DELETE' }),

  getHotels: () => request<Hotel[]>('/bff/hotels'),

  getAvailableHotels: () => request<Hotel[]>('/bff/hotels/available'),

  createHotel: (data: CreateHotelRequest) =>
    request<Hotel>('/bff/hotels', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  getPartners: () => request<Partner[]>('/bff/partners'),

  getMyBookings: (page = 1) =>
    request<{ results: Booking[]; total: number; page: number; pageSize: number; totalPages: number }>(
      `/bff/bookings?page=${page}`
    ),

  // Client always sends pageSize=50. In Vulnerable mode the server honours whatever
  // the caller provides, so an attacker can bypass this by sending a larger value directly.
  searchBookings: (q: string) =>
    request<{ results: Booking[]; truncated: boolean }>(
      `/bff/bookings/search?q=${encodeURIComponent(q)}&pageSize=10`
    ),

  createBooking: (data: CreateBookingRequest) =>
    request<Booking>('/bff/bookings', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  getBookingById: (id: number) => request<Booking>(`/bff/bookings/${id}`),

  getAuditLogs: () => request<AuditLog[]>('/bff/audit-logs'),

  getCart: () => request<Cart>('/bff/cart'),

  addToCart: (data: { hotelId: string; checkIn: string; checkOut: string; cardNumber: string; specialRequests: string }) =>
    request<CartItem>('/bff/cart/items', { method: 'POST', body: JSON.stringify(data) }),

  removeFromCart: (itemId: number) =>
    request<void>(`/bff/cart/items/${itemId}`, { method: 'DELETE' }),

  checkout: (couponCode?: string) =>
    request<CheckoutResult>('/bff/cart/checkout', {
      method: 'POST',
      body: JSON.stringify({ couponCode: couponCode || null }),
    }),

  mfaChallenge: () =>
    request<{ expiresAt: string; ttlMinutes: number }>('/bff/auth/mfa/challenge', { method: 'POST' }),

  mfaGetOtp: () =>
    request<{ code: string; expiresAt: string; attemptsRemaining: number | null; workshopNote: string }>('/bff/auth/mfa/otp'),

  mfaVerify: (code: string) =>
    request<{ verified: boolean; username: string; userId: string; verifiedAt: string }>('/bff/auth/mfa/verify', {
      method: 'POST',
      body: JSON.stringify({ code }),
    }),

  forgotPassword: (username: string) =>
    request<{ message: string; resetToken: string; expiresAt: string }>('/bff/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ username }),
    }),

  resetPassword: (token: string, newPassword: string) =>
    request<{ message: string }>('/bff/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ token, newPassword }),
    }),

  testWebhook: (url: string) =>
    request<{ url: string; statusCode?: number; body?: string; error?: string }>('/bff/webhooks/test', {
      method: 'POST',
      body: JSON.stringify({ url }),
    }),

  triggerError: () =>
    fetch('/bff/debug/throw', { credentials: 'include' })
      .then(res => res.json().catch(() => ({ error: 'Non-JSON response' }))),
}
