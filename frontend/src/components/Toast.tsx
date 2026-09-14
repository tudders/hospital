import { useCallback, useState, type ReactNode } from 'react'
import { ToastContext } from './toast-context'

type Toast = {
  id: number
  message: string
}

let nextToastId = 0

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const dismiss = useCallback((id: number) => {
    setToasts(current => current.filter(toast => toast.id !== id))
  }, [])

  const showToast = useCallback((message: string) => {
    const id = ++nextToastId
    setToasts(current => [...current, { id, message }])
    window.setTimeout(() => dismiss(id), 4000)
  }, [dismiss])

  return <ToastContext.Provider value={{ showToast }}>
    {children}
    <div className="toast-region" aria-live="polite" aria-atomic="false">
      {toasts.map(toast => (
        <div className="toast" role="status" key={toast.id}>
          <span>{toast.message}</span>
          <button type="button" aria-label="Dismiss notification" onClick={() => dismiss(toast.id)}>×</button>
        </div>
      ))}
    </div>
  </ToastContext.Provider>
}
