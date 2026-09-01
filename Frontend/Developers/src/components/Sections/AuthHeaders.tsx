import type { DocSection } from '../../api/client';
import { InvalidSectionContent, parseSectionData } from './sectionData';

interface Props {
  section: DocSection;
}

export function AuthHeaders({ section }: Props) {
  const data = parseSectionData(section.content);
  if (!data) return <InvalidSectionContent section={section} />;

  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="section-eyebrow">{data.eyebrow}</div>
      <h2 className="section-title">{section.title}</h2>
      <p className="section-lead">{data.lead}</p>

      {data.encodingNote && (
        <div className="info-box" style={{ marginBottom: 24 }}>
          <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20A10 10 0 0 0 12 2zm0 9v5m0-8v1" /></svg>
          <div><strong style={{ color: 'var(--ink)' }}>Кодирование:</strong> {data.encodingNote}</div>
        </div>
      )}

      <h3 className="sub-heading">Обязательные заголовки устройства (все запросы)</h3>
      <div className="doc-table-wrap">
        <table className="doc-table">
          <thead><tr><th>Заголовок</th><th>Формат</th><th>Описание</th><th>Пример</th></tr></thead>
          <tbody>
            {data.deviceHeaders.map((h: { name: string; format: string; description: string; example: string }) => (
              <tr key={h.name}>
                <td className="td-name">{h.name}</td>
                <td className="td-type">{h.format}</td>
                <td>{h.description}</td>
                <td className="td-type" style={{ fontSize: 10 }}>{h.example}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <h3 className="sub-heading">Заголовок авторизации (защищённые методы)</h3>
      <div className="doc-table-wrap">
        <table className="doc-table">
          <thead><tr><th>Заголовок</th><th>Формат</th><th>Описание</th></tr></thead>
          <tbody>
            <tr>
              <td className="td-name">{data.authHeader.name}</td>
              <td className="td-type">{data.authHeader.format}</td>
              <td>{data.authHeader.description}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {data.kotlinExample && (
        <h3 className="sub-heading">Пример (Kotlin / Android)</h3>
      )}
      {data.kotlinExample && (
        <div className="code-block" style={{ marginBottom: 0 }}>
          <pre><code>
            {data.kotlinExample.map((line: { text: string; type: string }, i: number) => (
              <span key={i} className={line.type === 'comment' ? 'code-comment' : line.type === 'kw' ? 'code-kw' : line.type === 'fn' ? 'code-fn' : ''}>{line.text}{'\n'}</span>
            ))}
          </code></pre>
        </div>
      )}

      {data.serverApiNote && (
        <div className="info-box">
          <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20A10 10 0 0 0 12 2zm0 9v5m0-8v1" /></svg>
          <div>{data.serverApiNote}</div>
        </div>
      )}
    </section>
  );
}
