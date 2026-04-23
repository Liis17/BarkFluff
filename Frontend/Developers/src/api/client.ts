import { createClient } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';
import { DevelopersApi } from '../gen/developers_api_connect';
import {
  GetDocumentationSectionsRequest,
  GetProtoFilesRequest,
  GetProtoFileContentRequest,
  GetErrorCodesRequest,
} from '../gen/developers_api_pb';
import { getDeviceId } from '../App';

const developerTransport = createGrpcWebTransport({ baseUrl: '/grpc' });
const developerClient = createClient(DevelopersApi, developerTransport);

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

function buildHeaders(token: string): Headers {
  return new Headers({
    'x-auth-token': token,
    'x-device-id': btoa(getDeviceId()),
    'x-device-name': btoa(getBrowserName()),
    'x-os-name': btoa(getOsName()),
    'x-app-name': btoa('BarkFluff Developers'),
    'x-app-version': btoa('1.0.0'),
    'x-ip-address': btoa('0.0.0.0'),
  });
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

export async function getDocumentationSections(token: string): Promise<DocSection[]> {
  const resp = await developerClient.getDocumentationSections(
    new GetDocumentationSectionsRequest(),
    { headers: buildHeaders(token) },
  );
  return resp.sections.map(s => ({
    key: s.key,
    title: s.title,
    type: s.type,
    order: s.order,
    content: s.content,
  }));
}

export async function getProtoFiles(token: string): Promise<ProtoFile[]> {
  const resp = await developerClient.getProtoFiles(
    new GetProtoFilesRequest(),
    { headers: buildHeaders(token) },
  );
  return resp.files.map(f => ({
    fileName: f.fileName,
    displayName: f.displayName,
    slug: f.slug,
    order: f.order,
    rpcDescriptions: f.rpcDescriptions,
  }));
}

export async function getProtoFileContent(token: string, fileName: string): Promise<{ content: string; metadata?: ProtoFile }> {
  const resp = await developerClient.getProtoFileContent(
    new GetProtoFileContentRequest({ fileName }),
    { headers: buildHeaders(token) },
  );
  return {
    content: resp.content,
    metadata: resp.metadata ? {
      fileName: resp.metadata.fileName,
      displayName: resp.metadata.displayName,
      slug: resp.metadata.slug,
      order: resp.metadata.order,
      rpcDescriptions: resp.metadata.rpcDescriptions,
    } : undefined,
  };
}

export async function getErrorCodes(token: string): Promise<ErrorCode[]> {
  const resp = await developerClient.getErrorCodes(
    new GetErrorCodesRequest(),
    { headers: buildHeaders(token) },
  );
  return resp.errorCodes.map(e => ({
    code: e.code,
    exceptionName: e.exceptionName,
    description: e.description,
    domain: e.domain,
  }));
}
