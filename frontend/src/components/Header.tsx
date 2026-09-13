import { sessionId } from '../lib/telemetry';
import type { Me } from '../lib/auth';

interface HeaderProps {
  me: Me;
  onLogout: () => void;
}

export function Header({ me, onLogout }: HeaderProps) {
  return (
    <header className="header" data-testid="header">
      <h1>Alcidion patient flow</h1>
      <div className="who">
        {me.name} · {me.roles.join(', ') || 'read-only'} · session <code>{sessionId.slice(0, 8)}</code>{' '}
        <button className="btn ghost sm" type="button" data-track="sign-out" onClick={onLogout}>
          Sign out
        </button>
      </div>
    </header>
  );
}
