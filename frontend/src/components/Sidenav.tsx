interface SidenavProps {
  currentPage: 'hospital' | 'patients';
  onNavigate: (page: 'hospital' | 'patients') => void;
}

export function Sidenav({ currentPage, onNavigate }: SidenavProps) {
  return (
    <nav className="sidenav" data-testid="sidenav" data-region="navigation" aria-label="Application navigation">
      <button
        data-track="navigate-hospital"
        aria-current={currentPage === 'hospital' ? 'page' : undefined}
        onClick={() => onNavigate('hospital')}
        className={currentPage === 'hospital' ? 'active' : ''}
      >
        ▥ Hospital overview
      </button>
      <button
        data-track="navigate-patients"
        aria-current={currentPage === 'patients' ? 'page' : undefined}
        onClick={() => onNavigate('patients')}
        className={currentPage === 'patients' ? 'active' : ''}
      >
        Patient flow
      </button>
    </nav>
  );
}
