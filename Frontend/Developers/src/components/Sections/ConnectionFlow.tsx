import type { DocSection } from '../../api/client';

interface Props {
  section: DocSection;
}

export function ConnectionFlow({ section }: Props) {
  const data = JSON.parse(section.content);

  return (
    <section className="doc-section" id={section.key} data-section={section.key}>
      <div className="section-eyebrow">{data.eyebrow}</div>
      <h2 className="section-title">{section.title}</h2>
      <p className="section-lead">{data.lead}</p>

      <div className="steps">
        {data.steps.map((step: { title: string; description: string }, i: number) => (
          <div key={i} className="step">
            <div className="step-num">{i + 1}</div>
            {i < data.steps.length - 1 && <div className="step-line" />}
            <div className="step-body">
              <h4>{step.title}</h4>
              <p>{step.description}</p>
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
