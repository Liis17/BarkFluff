import type { ButtonHTMLAttributes, ReactNode } from 'react';
import './Button.css';

type Variant = 'filled' | 'tonal' | 'outlined' | 'text' | 'elevated';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  /** иконка Material Symbols (имя) перед текстом */
  icon?: string;
  loading?: boolean;
  children?: ReactNode;
}

export function Button({
  variant = 'filled',
  icon,
  loading = false,
  children,
  className = '',
  disabled,
  ...rest
}: ButtonProps) {
  return (
    <button
      className={`bf-btn bf-btn--${variant} ${className}`}
      disabled={disabled || loading}
      {...rest}
    >
      <span className="bf-btn__layer" aria-hidden="true" />
      {loading ? (
        <span className="bf-btn__spinner" aria-hidden="true" />
      ) : (
        icon && <span className="material-symbols-rounded bf-btn__icon">{icon}</span>
      )}
      {children && <span className="bf-btn__label">{children}</span>}
    </button>
  );
}
