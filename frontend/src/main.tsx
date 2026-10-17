import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

// Placeholder shell: the real screens come with the frontend PRD.
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <h1>Tewe Diary</h1>
    <p>API: {import.meta.env.VITE_API_URL}</p>
  </StrictMode>,
)
