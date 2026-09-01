import type { DocSection } from '../../api/client';

export type SectionData = Record<string, any>;

export function parseSectionData(content: string): SectionData | null {
  try {
    const value: unknown = JSON.parse(content);
    return value && typeof value === 'object' && !Array.isArray(value)
      ? value as SectionData
      : null;
  } catch {
    return null;
  }
}

export function InvalidSectionContent({ section }: { section: DocSection }) {
  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="section-eyebrow">{section.title}</div>
      <h2 className="section-title">Раздел временно недоступен</h2>
      <div className="warn-box">
        <div>Содержимое раздела не удалось безопасно прочитать.</div>
      </div>
    </section>
  );
}
