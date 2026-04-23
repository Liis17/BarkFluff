import { useState, useEffect } from 'react';
import { useAuth } from '../App';
import { getDocumentationSections, getProtoFiles, getErrorCodes, type DocSection, type ProtoFile as ProtoFileType, type ErrorCode } from '../api/client';
import { Sidebar } from './Layout/Sidebar';
import { Header } from './Layout/Header';
import { Overview } from './Sections/Overview';
import { Quickstart } from './Sections/Quickstart';
import { Implementation } from './Sections/Implementation';
import { AuthHeaders } from './Sections/AuthHeaders';
import { ConnectionFlow } from './Sections/ConnectionFlow';
import { ErrorCodes } from './Sections/ErrorCodes';
import { ProtoFileSection } from './Sections/ProtoFile';

export function DocsPage() {
  const { auth, logout } = useAuth();
  const token = auth?.accessToken ?? '';
  const [sections, setSections] = useState<DocSection[]>([]);
  const [protoFiles, setProtoFiles] = useState<ProtoFileType[]>([]);
  const [errorCodes, setErrorCodes] = useState<ErrorCode[]>([]);
  const [activeSection, setActiveSection] = useState('overview');
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [loadError, setLoadError] = useState('');

  useEffect(() => {
    if (!token) return;
    Promise.all([
      getDocumentationSections(token),
      getProtoFiles(token),
      getErrorCodes(token),
    ])
      .then(([s, pf, ec]) => {
        setSections(s);
        setProtoFiles(pf);
        setErrorCodes(ec);
      })
      .catch(e => setLoadError(e.message));
  }, [token]);

  const renderSection = (section: DocSection) => {
    switch (section.type) {
      case 'overview': return <Overview key={section.key} section={section} />;
      case 'quickstart': return <Quickstart key={section.key} section={section} />;
      case 'implementation': return <Implementation key={section.key} section={section} />;
      case 'auth-headers': return <AuthHeaders key={section.key} section={section} />;
      case 'connection-flow': return <ConnectionFlow key={section.key} section={section} />;
      case 'error-codes': return <ErrorCodes key={section.key} section={section} errorCodes={errorCodes} />;
      default: return null;
    }
  };

  if (loadError) {
    return (
      <div style={{ padding: 48, textAlign: 'center', color: '#f87171' }}>
        <h2>Ошибка загрузки</h2>
        <p>{loadError}</p>
        <button onClick={logout} style={{ marginTop: 16, padding: '8px 24px', cursor: 'pointer' }}>Выйти</button>
      </div>
    );
  }

  return (
    <>
      <Header onMenuToggle={() => setSidebarOpen(!sidebarOpen)} onLogout={logout} />
      <div className="layout">
        <Sidebar
          sections={sections}
          protoFiles={protoFiles}
          activeSection={activeSection}
          onNavigate={(key) => {
            setActiveSection(key);
            setSidebarOpen(false);
            document.getElementById(key)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
          }}
          isOpen={sidebarOpen}
        />
        <main className="main-content">
          {sections.map(renderSection)}
          {protoFiles.map(pf => (
            <ProtoFileSection key={pf.slug} protoFile={pf} token={token} />
          ))}
        </main>
      </div>
      <footer className="site-footer">
        <span>&copy; 2026 Barkfluff — Developer Portal</span>
        <span><a href="https://barkfluff.com" target="_blank" rel="noreferrer">barkfluff.com</a></span>
      </footer>
    </>
  );
}
