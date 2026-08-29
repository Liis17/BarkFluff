import { useState, useEffect, useRef } from 'react';
import type { ProtoFile as ProtoFileType } from '../../api/client';
import { getProtoFileContent } from '../../api/client';
import { parseSectionData } from './sectionData';

interface Props {
  protoFile: ProtoFileType;
  token: string;
}

export function ProtoFileSection({ protoFile, token }: Props) {
  const [content, setContent] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [isVisible, setIsVisible] = useState(false);
  const sectionRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const element = sectionRef.current;
    if (!element) return;
    if (!('IntersectionObserver' in window)) {
      setIsVisible(true);
      return;
    }

    const observer = new IntersectionObserver(
      entries => {
        if (entries.some(entry => entry.isIntersecting)) {
          setIsVisible(true);
          observer.disconnect();
        }
      },
      { rootMargin: '600px 0px' },
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!isVisible) return;

    let cancelled = false;
    setLoading(true);
    setError('');
    getProtoFileContent(token, protoFile.fileName)
      .then(data => {
        if (!cancelled) setContent(data.content);
      })
      .catch(e => {
        if (cancelled) return;
        setContent(null);
        setError(e instanceof Error ? e.message : 'Не удалось загрузить файл');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [isVisible, protoFile.fileName, token]);

  const handleCopy = () => {
    if (!content) return;
    navigator.clipboard.writeText(content).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  const handleDownload = () => {
    if (!content) return;
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([content], { type: 'text/plain' }));
    a.download = protoFile.fileName;
    a.click();
    URL.revokeObjectURL(a.href);
  };

  return (
    <section ref={sectionRef} className="proto-section doc-section" id={protoFile.slug} data-section={protoFile.slug}>
      <div className="section-eyebrow">API Reference</div>
      <h2 className="section-title">{protoFile.displayName}</h2>

      <div className="proto-header">
        <div className="proto-icon">
          <ProtoIcon />
        </div>
        <div className="proto-title-block">
          <div className="proto-filename">{protoFile.fileName}</div>
          <div className="proto-desc">{getDescription()}</div>
        </div>
        <div className="proto-actions">
          <button className="proto-btn" onClick={handleCopy} disabled={loading}>
            {copied ? '✓ Скопировано' : 'Копировать'}
          </button>
          <button className="proto-btn" onClick={handleDownload} disabled={loading || !content}>Скачать</button>
        </div>
      </div>

      <div className="proto-code-wrap">
        <div className="proto-code-bar">
          <div className="proto-code-bar-label">
            <div className="proto-code-bar-dots"><span /><span /><span /></div>
            {protoFile.fileName}
          </div>
        </div>
        <pre className="proto-code">
          {!isVisible ? (
            <span style={{ color: 'var(--ink-mute)', fontStyle: 'italic' }}>Загрузка при прокрутке...</span>
          ) : loading ? (
            <span style={{ color: 'var(--ink-mute)', fontStyle: 'italic' }}>Загрузка...</span>
          ) : error ? (
            <span style={{ color: '#f87171' }}>Ошибка: {error}</span>
          ) : content ? (
            <span dangerouslySetInnerHTML={{ __html: highlightProto(content) }} />
          ) : (
            <span style={{ color: 'var(--ink-mute)' }}>Файл не найден</span>
          )}
        </pre>
      </div>

      {protoFile.rpcDescriptions && renderMetadata()}
    </section>
  );

  function getDescription(): string {
    return parseSectionData(protoFile.rpcDescriptions)?.description ?? '';
  }

  function renderMetadata() {
    const meta = parseSectionData(protoFile.rpcDescriptions);
    if (!meta) return null;

    return (
      <>
        {meta.info && (
          <div className="info-box">
            <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20A10 10 0 0 0 12 2zm0 9v5m0-8v1" /></svg>
            <div>{meta.info}</div>
          </div>
        )}

        {meta.warning && (
          <div className="warn-box">
            <svg viewBox="0 0 24 24"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01" /></svg>
            <div>{meta.warning}</div>
          </div>
        )}

        {meta.rpcs && meta.rpcs.length > 0 && (
          <>
            <h3 className="sub-heading">RPC методы</h3>
            <div className="doc-table-wrap">
              <table className="doc-table">
                <thead><tr><th>Метод</th><th>Тип</th><th>Request</th><th>Response</th><th>Описание</th></tr></thead>
                <tbody>
                  {meta.rpcs.map((rpc: { name: string; req: string; resp: string; stream: boolean; description: string }) => (
                    <tr key={rpc.name}>
                      <td className="td-rpc">{rpc.name}</td>
                      <td><span className={`td-badge ${rpc.stream ? 'badge-stream' : 'badge-unary'}`}>{rpc.stream ? 'stream' : 'unary'}</span></td>
                      <td className="td-type">{rpc.req}</td>
                      <td className="td-type">{rpc.resp}</td>
                      <td>{rpc.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}

        {meta.subsections && meta.subsections.map((sub: { title: string; type: string; items: { name: string; type: string; description: string; num?: string }[] }) => (
          <div key={sub.title}>
            <h3 className="sub-heading">{sub.title}</h3>
            <div className="doc-table-wrap">
              <table className="doc-table">
                <thead>
                  <tr>
                    {sub.type === 'enum'
                      ? <><th>Значение</th><th>Число</th><th>Описание</th></>
                      : <><th>Поле</th><th>Тип</th><th>Описание</th></>
                    }
                  </tr>
                </thead>
                <tbody>
                  {sub.items.map((item: { name: string; type: string; description: string; num?: string }) => (
                    <tr key={item.name}>
                      <td className="td-name">{item.name}</td>
                      {sub.type === 'enum'
                        ? <td className="td-type">{item.num}</td>
                        : <td className="td-type">{item.type}</td>
                      }
                      <td>{item.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))}
      </>
    );
  }
}

function ProtoIcon() {
  return (
    <svg viewBox="0 0 24 24"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" /></svg>
  );
}

function highlightProto(raw: string): string {
  const escaped = raw.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

  return escaped.replace(
    /(\/\/[^\n]*)|("(?:[^"\\]|\\.)*")|(\b(?:syntax|service|rpc|message|enum|returns|stream|repeated|optional|oneof|import|package|option|map)\b)|(\b(?:string|int32|int64|uint32|uint64|sint32|sint64|fixed32|fixed64|sfixed32|sfixed64|bool|bytes|double|float)\b)|(\bgoogle\.protobuf\.\w+\b)|(\b\d+\b)/g,
    (_m, comment, str, kw, type, gtype, num) => {
      if (comment) return `<span class="pc">${comment}</span>`;
      if (str) return `<span class="ps">${str}</span>`;
      if (kw) return `<span class="pk">${kw}</span>`;
      if (type) return `<span class="pt">${type}</span>`;
      if (gtype) return `<span class="pt">${gtype}</span>`;
      if (num) return `<span class="pn">${num}</span>`;
      return _m;
    },
  );
}
