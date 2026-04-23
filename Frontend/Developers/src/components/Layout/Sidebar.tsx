import { useState } from 'react';
import type { DocSection, ProtoFile } from '../../api/client';

interface SidebarProps {
  sections: DocSection[];
  protoFiles: ProtoFile[];
  activeSection: string;
  onNavigate: (key: string) => void;
  isOpen: boolean;
}

interface NavGroup {
  id: string;
  title: string;
  items: { key: string; label: string }[];
}

export function Sidebar({ sections, protoFiles, activeSection, onNavigate, isOpen }: SidebarProps) {
  const startGroup: NavGroup = {
    id: 'ng-start',
    title: 'Начало работы',
    items: sections.filter(s => ['overview', 'quickstart', 'implementation'].includes(s.type)).map(s => ({ key: s.key, label: s.title })),
  };

  const authGroup: NavGroup = {
    id: 'ng-auth',
    title: 'Аутентификация',
    items: sections.filter(s => ['auth-headers', 'connection-flow', 'error-codes'].includes(s.type)).map(s => ({ key: s.key, label: s.title })),
  };

  const protoGroup: NavGroup = {
    id: 'ng-api',
    title: 'API Reference',
    items: protoFiles.map(pf => ({ key: pf.slug, label: pf.displayName || pf.fileName })),
  };

  const groups = [startGroup, authGroup, protoGroup];
  const [openGroups, setOpenGroups] = useState<Set<string>>(new Set(groups.map(g => g.id)));

  const toggleGroup = (id: string) => {
    setOpenGroups(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return (
    <aside className={`sidebar${isOpen ? ' open' : ''}`}>
      {groups.map(group => (
        <div key={group.id} className={`nav-group${openGroups.has(group.id) ? ' open' : ''}`}>
          <button className="nav-group-toggle" onClick={() => toggleGroup(group.id)}>
            {group.title}
            <svg className="chevron" viewBox="0 0 24 24"><polyline points="6 9 12 15 18 9" /></svg>
          </button>
          <div className="nav-items">
            {group.items.map(item => (
              <a
                key={item.key}
                className={`nav-link${activeSection === item.key ? ' active' : ''}`}
                onClick={() => onNavigate(item.key)}
              >
                <span className="dot" />
                {item.label}
              </a>
            ))}
          </div>
        </div>
      ))}

      {groups.indexOf(startGroup) < groups.indexOf(authGroup) && (
        <div className="nav-separator" />
      )}
    </aside>
  );
}
