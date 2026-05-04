interface PaginationProps {
  page: number
  totalPages: number
  disabled?: boolean
  onPageChange: (page: number) => void
}

export default function Pagination({ page, totalPages, disabled, onPageChange }: PaginationProps) {
  const pages = buildPageList(page, totalPages)

  const btnStyle = (active: boolean, clickable: boolean): React.CSSProperties => ({
    minWidth: '2rem',
    padding: '0.35rem 0.6rem',
    cursor: clickable && !disabled ? 'pointer' : 'default',
    fontWeight: active ? 700 : 400,
    background: active ? '#2563eb' : '#fff',
    color: active ? '#fff' : '#374151',
    border: '1px solid #d1d5db',
    borderRadius: '0.375rem',
    opacity: disabled ? 0.6 : 1,
  })

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: '0.3rem', marginTop: '0.75rem', flexWrap: 'wrap' }}>
      <button
        onClick={() => !disabled && page > 1 && onPageChange(page - 1)}
        disabled={page <= 1 || disabled}
        style={btnStyle(false, page > 1)}
      >
        ←
      </button>

      {pages.map((p, i) =>
        p === '…' ? (
          <span key={`ellipsis-${i}`} style={{ padding: '0.35rem 0.4rem', color: '#9ca3af' }}>…</span>
        ) : (
          <button
            key={p}
            onClick={() => !disabled && p !== page && onPageChange(p as number)}
            disabled={disabled}
            style={btnStyle(p === page, p !== page)}
          >
            {p}
          </button>
        )
      )}

      <button
        onClick={() => !disabled && page < totalPages && onPageChange(page + 1)}
        disabled={page >= totalPages || disabled}
        style={btnStyle(false, page < totalPages)}
      >
        →
      </button>

      <span style={{ marginLeft: '0.5rem', fontSize: '0.82rem', color: '#6b7280' }}>
        Page {page} of {totalPages}
      </span>
    </div>
  )
}

function buildPageList(current: number, total: number): (number | '…')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)

  const pages: (number | '…')[] = [1]

  if (current > 3) pages.push('…')

  const start = Math.max(2, current - 1)
  const end = Math.min(total - 1, current + 1)
  for (let i = start; i <= end; i++) pages.push(i)

  if (current < total - 2) pages.push('…')

  pages.push(total)
  return pages
}
