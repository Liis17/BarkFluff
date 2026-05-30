// Управление server-streaming подпиской: переподключение с backoff, forced refresh
// при auth-ошибке, resync при переоткрытии. Порт логики wwwroot/js/app/realtime.js.
import { Code, ConnectError } from '@connectrpc/connect';
import { getValidToken, refreshToken } from '../api/refresh';
import { buildHeaders } from '../api/metadata';

const BACKOFF_MIN = 2000;
const BACKOFF_MAX = 30000;

export interface StreamHandle {
  stop: () => void;
}

interface RunStreamOpts<T> {
  label: string;
  open: (req: Record<string, never>, options: { signal: AbortSignal; headers: Headers }) => AsyncIterable<T>;
  onEvent: (event: T) => void;
  onReopen?: () => void; // вызывается при ЛЮБОМ переоткрытии (кроме первого) — для resync
}

export function runStream<T>(opts: RunStreamOpts<T>): StreamHandle {
  let stopped = false;
  let attempt = 0;
  let firstOpen = true;
  let controller: AbortController | null = null;
  let sleepTimer: ReturnType<typeof setTimeout> | null = null;

  const sleep = (ms: number) =>
    new Promise<void>((resolve) => {
      sleepTimer = setTimeout(resolve, ms);
    });

  async function loop() {
    while (!stopped) {
      controller = new AbortController();
      try {
        const token = await getValidToken();
        const headers = buildHeaders(token);
        if (!firstOpen) opts.onReopen?.();
        firstOpen = false;

        for await (const event of opts.open({}, { signal: controller.signal, headers })) {
          attempt = 0; // активность → сбрасываем backoff
          opts.onEvent(event);
        }
        // стрим завершился штатно — переоткрываем
      } catch (err) {
        if (stopped) break;
        if (err instanceof ConnectError && err.code === Code.Unauthenticated) {
          await refreshToken();
        }
      }
      if (stopped) break;
      attempt++;
      const delay = Math.min(BACKOFF_MIN * 2 ** (attempt - 1), BACKOFF_MAX);
      await sleep(delay);
    }
  }

  void loop();

  return {
    stop() {
      stopped = true;
      if (sleepTimer) clearTimeout(sleepTimer);
      controller?.abort();
    },
  };
}
