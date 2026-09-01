import type { DocSection, ErrorCode } from '../../api/client';
import { InvalidSectionContent, parseSectionData } from './sectionData';

interface Props {
  section: DocSection;
  errorCodes: ErrorCode[];
}

export function ErrorCodes({ section, errorCodes }: Props) {
  const data = parseSectionData(section.content);
  if (!data) return <InvalidSectionContent section={section} />;

  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="section-eyebrow">{data.eyebrow}</div>
      <h2 className="section-title">{section.title}</h2>
      <p className="section-lead">{data.lead}</p>

      <div className="doc-table-wrap error-table">
        <table className="doc-table">
          <thead><tr><th>GUID (x-error-code)</th><th>Исключение</th><th>Домен</th><th>Описание</th></tr></thead>
          <tbody>
            {errorCodes.map(ec => (
              <tr key={ec.code}>
                <td className="td-type guid">{ec.code}</td>
                <td className="td-name">{ec.exceptionName}</td>
                <td>{ec.domain}</td>
                <td>{ec.description}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {data.trailerNote && (
        <div className="info-box">
          <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20A10 10 0 0 0 12 2zm0 9v5m0-8v1" /></svg>
          <div>{data.trailerNote}</div>
        </div>
      )}
    </section>
  );
}
