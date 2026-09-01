import type { DocSection } from '../../api/client';
import { InvalidSectionContent, parseSectionData } from './sectionData';

interface Props {
  section: DocSection;
}

export function Overview({ section }: Props) {
  const data = parseSectionData(section.content);
  if (!data) return <InvalidSectionContent section={section} />;

  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="hero-block">
        <div className="section-eyebrow">{data.hero.eyebrow}</div>
        <h1 className="hero-title">
          {data.hero.title}<br />
          <span className="accent">{data.hero.titleAccent}</span>
        </h1>
        <p className="hero-sub">{data.hero.subtitle}</p>
        <div className="hero-pills">
          {data.hero.pills.map((p: string) => <span key={p} className="pill">{p}</span>)}
        </div>
      </div>

      <div className="cards-grid">
        {data.cards.map((card: { title: string; description: string; icon: string }) => (
          <div key={card.title} className="feat-card">
            <div className="f-icon">
              <CardIcon name={card.icon} />
            </div>
            <div className="f-title">{card.title}</div>
            <div className="f-body">{card.description}</div>
          </div>
        ))}
      </div>
    </section>
  );
}

function CardIcon({ name }: { name: string }) {
  const icons: Record<string, string> = {
    shield: 'M12 1l9 4v6c0 5.5-3.8 10.7-9 12-5.2-1.3-9-6.5-9-12V5l9-4z',
    auth: 'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zm8 4l2 2 4-4',
    bolt: 'M13 10V3L4 14h7v7l9-11h-7z',
    file: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6z',
    devices: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6z',
    globe: 'M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10z',
  };
  return <svg viewBox="0 0 24 24"><path d={icons[name] ?? icons.shield} /></svg>;
}
