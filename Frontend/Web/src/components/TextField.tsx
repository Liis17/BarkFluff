import { useId, type InputHTMLAttributes } from 'react';
import './TextField.css';

interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'> {
  label: string;
  error?: string;
  /** ведущая иконка Material Symbols */
  leadingIcon?: string;
  /** замыкающий элемент (например, кнопка показа пароля) */
  trailing?: React.ReactNode;
}

// M3 Outlined text field с плавающим лейблом.
export function TextField({
  label,
  error,
  leadingIcon,
  trailing,
  id,
  className = '',
  ...rest
}: TextFieldProps) {
  const autoId = useId();
  const inputId = id ?? autoId;
  return (
    <div className={`bf-tf ${error ? 'bf-tf--error' : ''} ${className}`}>
      <div className="bf-tf__box">
        {leadingIcon && (
          <span className="material-symbols-rounded bf-tf__leading">{leadingIcon}</span>
        )}
        <input id={inputId} className="bf-tf__input" placeholder=" " {...rest} />
        <label htmlFor={inputId} className="bf-tf__label">
          {label}
        </label>
        {trailing && <span className="bf-tf__trailing">{trailing}</span>}
      </div>
      {error && <span className="bf-tf__error">{error}</span>}
    </div>
  );
}
