import { useState, useEffect, type ReactNode } from 'react';
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
import { ErrorBoundary } from './ErrorBoundary';

export function DocsPage() {
  const { auth, logout } = useAuth();
  const token = auth?.accessToken ?? '';
  const [sections, setSections] = useState<DocSection[]>([]);
  const [protoFiles, setProtoFiles] = useState<ProtoFileType[]>([]);
  const [errorCodes, setErrorCodes] = useState<ErrorCode[]>([]);
  const [activeSection, setActiveSection] = useState('overview');
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [loadError, setLoadError] = useState('');
  const [catalogWarnings, setCatalogWarnings] = useState<string[]>([]);

  useEffect(() => {
    if (!token) return;

    let cancelled = false;
    setLoadError('');
    setCatalogWarnings([]);
    setSections([]);
    setProtoFiles([]);
    setErrorCodes([]);

    Promise.allSettled([
      getDocumentationSections(token),
      getProtoFiles(token),
      getErrorCodes(token),
    ]).then(([sectionsResult, protoFilesResult, errorCodesResult]) => {
      if (cancelled) return;

      const warnings: string[] = [];
      if (sectionsResult.status === 'fulfilled') {
        setSections(sectionsResult.value);
      } else {
        setSections([]);
        warnings.push('Основные разделы документации временно недоступны.');
      }

      if (protoFilesResult.status === 'fulfilled') {
        setProtoFiles(protoFilesResult.value);
      } else {
        setProtoFiles([]);
        warnings.push('Каталог proto временно недоступен.');
      }

      if (errorCodesResult.status === 'fulfilled') {
        setErrorCodes(errorCodesResult.value);
      } else {
        setErrorCodes([]);
        warnings.push('Каталог кодов ошибок временно недоступен.');
      }

      if (warnings.length === 3) {
        setLoadError('Сервисы Developer Portal временно недоступны.');
      } else {
        setCatalogWarnings(warnings);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [token]);

  const renderSection = (section: DocSection) => {
    let content: ReactNode;
    switch (section.type) {
      case 'overview': content = <Overview section={section} />; break;
      case 'quickstart': content = <Quickstart section={section} />; break;
      case 'implementation': content = <Implementation section={section} />; break;
      case 'auth-headers': content = <AuthHeaders section={section} />; break;
      case 'connection-flow': content = <ConnectionFlow section={section} />; break;
      case 'error-codes': content = <ErrorCodes section={section} errorCodes={errorCodes} />; break;
      default: content = null;
    }

    return (
      <ErrorBoundary key={section.key}>
        {content}
      </ErrorBoundary>
    );
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
      {catalogWarnings.length > 0 && (
        <div className="warn-box" style={{ margin: '16px 32px 0' }} role="status">
          {catalogWarnings.map(warning => <div key={warning}>{warning}</div>)}
        </div>
      )}
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
            <ErrorBoundary key={pf.slug}>
              <ProtoFileSection protoFile={pf} token={token} />
            </ErrorBoundary>
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
