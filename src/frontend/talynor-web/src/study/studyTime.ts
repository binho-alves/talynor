export type ServerClockAnchor = { serverMs: number; receivedAt: number }
export function elapsedSeconds(startedAtUtc: string, anchor: ServerClockAnchor, localNow: number): number {
  return Math.max(0, (anchor.serverMs + localNow - anchor.receivedAt - Date.parse(startedAtUtc)) / 1000)
}
export function formatStudyDuration(seconds: number): string {
  const whole = Math.floor(Math.max(0, seconds))
  if (seconds > 0 && seconds < 1) return '< 1s'
  if (whole < 60) return `${whole}s`
  const minutes = Math.floor(whole / 60), rest = whole % 60
  if (minutes < 60) return `${minutes}m ${String(rest).padStart(2, '0')}s`
  return `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m ${String(rest).padStart(2, '0')}s`
}
