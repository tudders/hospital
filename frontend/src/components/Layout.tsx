import { type ReactNode } from 'react';
import { Header } from './Header';
import { Sidenav } from './Sidenav';
import type { Me } from '../lib/auth';

interface LayoutProps {
  me: Me;
  currentPage: 'hospital' | 'patients';
  onNavigate: (page: 'hospital' | 'patients') => void;
  onLogout: () => void;
  children: ReactNode;
}

export function Layout({ me, currentPage, onNavigate, onLogout, children }: LayoutProps) {
  return (
    <div className="app-layout">
      <Header me={me} onLogout={onLogout} />
      <div className="app-container">
        <Sidenav currentPage={currentPage} onNavigate={onNavigate} />
        <main className="main" data-testid="main">
          {children}
        </main>
      </div>
    </div>
  );
}
