import './Switch.css';

interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  'aria-label'?: string;
}

// M3 Switch.
export function Switch({ checked, onChange, disabled, ...rest }: SwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={rest['aria-label']}
      disabled={disabled}
      className={`bf-switch ${checked ? 'bf-switch--on' : ''}`}
      onClick={() => onChange(!checked)}
    >
      <span className="bf-switch__thumb" />
    </button>
  );
}
