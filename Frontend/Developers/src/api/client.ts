import { getDeviceId } from '../App';

const API_BASE = '/grpc';

function getBrowserName(): string {
  const ua = navigator.userAgent;
  if (ua.includes('Firefox')) return 'Firefox';
  if (ua.includes('Edg')) return 'Edge';
  if (ua.includes('Chrome')) return 'Chrome';
  if (ua.includes('Safari')) return 'Safari';
  if (ua.includes('Opera') || ua.includes('OPR')) return 'Opera';
  return 'Unknown';
}

function getOsName(): string {
  const ua = navigator.userAgent;
  if (ua.includes('Win')) return 'Windows';
  if (ua.includes('Mac')) return 'macOS';
  if (ua.includes('Linux')) return 'Linux';
  if (ua.includes('Android')) return 'Android';
  if (ua.includes('iOS') || ua.includes('iPhone')) return 'iOS';
  return 'Unknown';
}

function buildHeaders(token: string): Record<string, string> {
  return {
    'Content-Type': 'application/json',
    'Connect-Protocol-Version': '1',
    'x-auth-token': token,
    'x-device-id': btoa(getDeviceId()),
    'x-device-name': btoa(getBrowserName()),
    'x-os-name': btoa(getOsName()),
    'x-app-name': btoa('BarkFluff Developers'),
    'x-app-version': btoa('1.0.0'),
    'x-ip-address': btoa('0.0.0.0'),
  };
}

export interface DocSection {
  key: string;
  title: string;
  type: string;
  order: number;
  content: string;
}

export interface ProtoFile {
  fileName: string;
  displayName: string;
  slug: string;
  order: number;
  rpcDescriptions: string;
}

export interface ErrorCode {
  code: string;
  exceptionName: string;
  description: string;
  domain: string;
}

async function grpcCall<T>(service: string, method: string, body: unknown, token: string): Promise<T> {
  const resp = await fetch(`${API_BASE}/${service}/${method}`, {
    method: 'POST',
    headers: buildHeaders(token),
    body: JSON.stringify(body),
  });

  if (!resp.ok) {
    const errorCode = resp.headers.get('x-error-code');
    throw new Error(errorCode ?? `gRPC call failed: ${resp.status}`);
  }

  return resp.json();
}

export async function getDocumentationSections(token: string): Promise<DocSection[]> {
  const data = await grpcCall<{ sections: DocSection[] }>(
    'barkfluff.developers.DevelopersApi',
    'GetDocumentationSections',
    {},
    token,
  );
  return data.sections ?? [];
}

export async function getProtoFiles(token: string): Promise<ProtoFile[]> {
  const data = await grpcCall<{ files: ProtoFile[] }>(
    'barkfluff.developers.DevelopersApi',
    'GetProtoFiles',
    {},
    token,
  );
  return data.files ?? [];
}

export async function getProtoFileContent(token: string, fileName: string): Promise<{ content: string; metadata?: ProtoFile }> {
  return grpcCall(
    'barkfluff.developers.DevelopersApi',
    'GetProtoFileContent',
    { fileName },
    token,
  );
}

export async function getErrorCodes(token: string): Promise<ErrorCode[]> {
  const data = await grpcCall<{ errorCodes: ErrorCode[] }>(
    'barkfluff.developers.DevelopersApi',
    'GetErrorCodes',
    {},
    token,
  );
  return data.errorCodes ?? [];
}
