import { initials } from '../utils/format';
import './Avatar.css';

interface AvatarProps {
  name: string;
  /** URL картинки (если есть) */
  src?: string;
  size?: number;
}

// Детерминированный цвет фона по имени.
function hueFor(name: string): number {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360;
  return h;
}

export function Avatar({ name, src, size = 48 }: AvatarProps) {
  const style = { width: size, height: size, fontSize: size * 0.4 };
  if (src) {
    return <img className="bf-avatar" style={style} src={src} alt={name} loading="lazy" />;
  }
  const hue = hueFor(name);
  return (
    <span
      className="bf-avatar bf-avatar--fallback"
      style={{ ...style, background: `hsl(${hue} 45% 45%)` }}
      aria-label={name}
    >
      {initials(name)}
    </span>
  );
}
