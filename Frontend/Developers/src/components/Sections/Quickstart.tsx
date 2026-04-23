import type { DocSection } from '../../api/client';

interface Props {
  section: DocSection;
}

export function Quickstart({ section }: Props) {
  const data = JSON.parse(section.content);

  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="section-eyebrow">{data.eyebrow}</div>
      <h2 className="section-title">{section.title}</h2>
      <p className="section-lead">{data.lead}</p>

      <div className="code-block">
        <div className="code-block-head">{data.code.language}</div>
        <pre className="code-sample">
          {data.code.lines.map((line: { text: string; type: string }, i: number) => (
            <span key={i} className={lineClass(line.type)}>{line.text}{'\n'}</span>
          ))}
        </pre>
      </div>

      {data.infoBox && (
        <div className="info-box">
          <svg viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 0 20A10 10 0 0 0 12 2zm0 9v5m0-8v1" /></svg>
          <div>{data.infoBox}</div>
        </div>
      )}
    </section>
  );
}

function lineClass(type: string): string {
  switch (type) {
    case 'comment': return 'code-comment';
    case 'kw': return 'code-kw';
    case 'fn': return 'code-fn';
    case 'str': return 'code-str';
    default: return '';
  }
}
