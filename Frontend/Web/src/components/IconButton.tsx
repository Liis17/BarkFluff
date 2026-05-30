import type { ButtonHTMLAttributes } from 'react';
import './IconButton.css';

interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  icon: string;
  /** заполненная иконка (FILL 1) */
  filled?: boolean;
  selected?: boolean;
}

export function IconButton({
  icon,
  filled = false,
  selected = false,
  className = '',
  ...rest
}: IconButtonProps) {
  return (
    <button
      className={`bf-iconbtn ${selected ? 'bf-iconbtn--selected' : ''} ${className}`}
      {...rest}
    >
      <span className="bf-iconbtn__layer" aria-hidden="true" />
      <span
        className="material-symbols-rounded bf-iconbtn__icon"
        style={filled || selected ? { fontVariationSettings: "'FILL' 1" } : undefined}
      >
        {icon}
      </span>
    </button>
  );
}
