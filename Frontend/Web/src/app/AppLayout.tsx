import { Outlet } from 'react-router-dom';
import { NavRail } from '../components/NavRail';
import './AppLayout.css';

export function AppLayout() {
  return (
    <div className="bf-layout">
      <NavRail />
      <main className="bf-layout__content">
        <Outlet />
      </main>
    </div>
  );
}
