// Идентификация устройства и определение браузера/ОС. Порт wwwroot/js/app/device.js.

const DEVICE_ID_KEY = 'barkfluff_device_id';

export function getDeviceId(): string {
  let id = localStorage.getItem(DEVICE_ID_KEY);
  if (!id) {
    id = crypto.randomUUID();
    localStorage.setItem(DEVICE_ID_KEY, id);
  }
  return id;
}

export function getBrowserName(): string {
  const ua = navigator.userAgent;
  if (ua.includes('Firefox')) return 'Firefox';
  if (ua.includes('Edg/')) return 'Microsoft Edge';
  if (ua.includes('OPR/') || ua.includes('Opera')) return 'Opera';
  if (ua.includes('YaBrowser')) return 'Yandex Browser';
  if (ua.includes('Chrome')) return 'Chrome';
  if (ua.includes('Safari')) return 'Safari';
  return 'Unknown';
}

export function getOsName(): string {
  const ua = navigator.userAgent;
  if (ua.includes('Windows NT 10')) return 'Windows 10/11';
  if (ua.includes('Windows')) return 'Windows';
  if (ua.includes('Mac OS X')) return 'macOS';
  if (ua.includes('Android')) return 'Android';
  if (ua.includes('iPhone') || ua.includes('iPad')) return 'iOS';
  if (ua.includes('Linux')) return 'Linux';
  return 'Unknown';
}

export const getAppName = () => 'BarkFluff Web';
export const getAppVersion = () => '1.0.0';
