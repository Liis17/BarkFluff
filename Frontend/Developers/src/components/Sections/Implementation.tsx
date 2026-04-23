import type { DocSection } from '../../api/client';

interface Props {
  section: DocSection;
}

export function Implementation({ section }: Props) {
  const data = JSON.parse(section.content);

  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="section-eyebrow">{data.eyebrow}</div>
      <h2 className="section-title">{section.title}</h2>
      <p className="section-lead">{data.lead}</p>

      <div className="impl-cards">
        {data.cards.map((card: { title: string; subtitle: string; icon: string; code: { text: string; type: string }[] }) => (
          <div key={card.title} className="impl-card">
            <div className="impl-card-head">
              <div className="impl-card-icon"><CardIcon name={card.icon} /></div>
              <div>
                <div className="impl-card-title">{card.title}</div>
                <div className="impl-card-sub">{card.subtitle}</div>
              </div>
            </div>
            <pre className="impl-code">
              {card.code.map((line, i) => (
                <span key={i} className={lineClass(line.type)}>{line.text}{'\n'}</span>
              ))}
            </pre>
          </div>
        ))}
      </div>
    </section>
  );
}

const iconPaths: Record<string, string> = {
  channels: 'M22 12h-4l-3 9L9 3l-3 9H2',
  tokens: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z',
  streaming: 'M18 8h1a4 4 0 0 1 0 8h-1M2 8h16v9a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8zm4-6v4M10 2v4M14 2v4',
  upload: 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8 12 3 7 8M12 3v12',
  shield: 'M12 1l9 4v6c0 5.5-3.8 10.7-9 12-5.2-1.3-9-6.5-9-12V5l9-4z',
  bolt: 'M13 10V3L4 14h7v7l9-11h-7z',
  auth: 'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm8 4l2 2 4-4',
  file: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6z',
  devices: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6z',
  globe: 'M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10z',
};

function CardIcon({ name }: { name: string }) {
  return <svg viewBox="0 0 24 24"><path d={iconPaths[name] ?? iconPaths.shield} /></svg>;
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
