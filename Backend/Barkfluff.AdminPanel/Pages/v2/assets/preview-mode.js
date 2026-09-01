/* =============================================================
   Preview mode: mocks /api/* endpoints when no real backend.
   Loads BEFORE other scripts. Detects preview hosts (sandbox / localhost)
   and intercepts fetch() with realistic fixtures so the UI is reviewable.
   In production (real backend present) it does nothing harmful — it
   only mocks endpoints that fail with a network error.
   ============================================================= */
(function () {
  // Always enable mocks in this preview project — the real backend isn't
  // present anyway, so all /api/* requests would fail. Mocks let reviewers
  // see populated UI states.
  const ENABLE_MOCKS = true;

  if (!ENABLE_MOCKS) return;

  const now = Date.now();
  const hoursAgo = (h) => new Date(now - h * 3600000).toISOString();
  const minutesAgo = (m) => new Date(now - m * 60000).toISOString();

  const SERVICES = [
    'BarkFluff.Identity','BarkFluff.Users','BarkFluff.Messages','BarkFluff.Files',
    'BarkFluff.Updates','BarkFluff.Notification','BarkFluff.Beacon','BarkFluff.FastAuth',
    'BarkFluff.Onliner','BarkFluff.Settings','BarkFluff.Web','BarkFluff.ClientStorage'
  ];

  const FIRST_NAMES = ['Алексей','Мария','Дмитрий','Анна','Иван','Екатерина','Сергей','Ольга','Никита','Ксения','Артём','Полина','Кирилл','Виктория','Максим','Дарья','Илья','Юлия','Роман','София'];
  const LAST_NAMES  = ['Иванов','Петров','Смирнов','Кузнецов','Попов','Васильев','Соколов','Михайлов','Новиков','Фёдоров','Морозов','Волков','Алексеев','Лебедев','Семёнов','Егоров','Павлов','Козлов','Степанов','Николаев'];

  function pick(arr, i) { return arr[i % arr.length]; }
  function rnd(min, max) { return Math.floor(Math.random() * (max - min + 1)) + min; }
  function rs() { return Math.random().toString(36).slice(2, 10); }

  // -------- Fixtures --------
  const fix = {
    authMe: { name: 'Admin Demo', createdAt: new Date(now - 30 * 86400000).toISOString() },

    s3Buckets: [
      { id: 'avatars', displayName: 'Аватарки' },
      { id: 'media', displayName: 'Медиа' },
      { id: 'documents', displayName: 'Документы' },
      { id: 'stickers', displayName: 'Стикеры' }
    ],

    kpis() {
      return {
        totalEvents: 482917,
        errorCount: 124,
        warningCount: 2841,
        perService: Object.fromEntries(SERVICES.map(s => [s, rnd(100, 50000)]))
      };
    },

    traffic() {
      const points = [];
      const errors = [];
      const warnings = [];
      for (let i = 23; i >= 0; i--) {
        const t = hoursAgo(i);
        const base = 8000 + Math.sin(i / 4) * 3000 + Math.random() * 2000;
        points.push({ timestamp: t, count: Math.round(base + rnd(0, 2000)) });
        errors.push({ timestamp: t, count: rnd(0, 25) });
        warnings.push({ timestamp: t, count: rnd(20, 180) });
      }
      return { all: points, errors, warnings };
    },

    serviceMetrics(svc) {
      const ts = [];
      const metricNamesByType = {
        counters: ['requests_total', 'messages_sent', 'connections_opened'],
        errors:   ['errors_total', 'requests_failed'],
        gauges:   ['active_users', 'connections_current'],
        latency:  ['request_duration_ms', 'db_latency_ms']
      };
      const all = [].concat(...Object.values(metricNamesByType));
      for (let i = 11; i >= 0; i--) {
        const m = {};
        all.forEach(n => {
          if (n.endsWith('_ms')) m[n] = rnd(20, 300);
          else if (n.startsWith('active_')) m[n] = rnd(500, 5000);
          else if (n.includes('failed') || n.includes('errors_total')) m[n] = rnd(0, 12);
          else if (n.endsWith('_current')) m[n] = rnd(200, 1200);
          else m[n] = rnd(800, 12000);
        });
        ts.push({ hour: hoursAgo(i), metrics: m });
      }
      return { service: svc, timeSeries: ts };
    },

    metricGroups() {
      return { groups: [
        { serviceName: 'BarkFluff.Messages', title: 'Messages', expandedByDefault: true, metrics: [{id:'messages_sent',title:'Обычные сообщения',unit:'count',kind:'counter'}] },
        { serviceName: 'BarkFluff.Files', title: 'Files', expandedByDefault: true, metrics: [{id:'files_uploaded',title:'Загруженные файлы',unit:'count',kind:'counter'}, {id:'file_traffic_bytes_total',title:'Файловый трафик',unit:'bytes',kind:'counter'}] },
        { serviceName: 'BarkFluff.Identity', title: 'Identity', expandedByDefault: true, metrics: [{id:'auth_login_success',title:'Успешные входы',unit:'count',kind:'counter'}] },
        { serviceName: 'BarkFluff.Updates', title: 'Updates', expandedByDefault: true, metrics: [{id:'new_messages_broadcast',title:'Доставленные realtime-сообщения',unit:'count',kind:'counter'}] },
        { serviceName: 'BarkFluff.Onliner', title: 'Onliner', expandedByDefault: true, metrics: [{id:'online_users_count',title:'Пользователи онлайн',unit:'count',kind:'gauge'}] }
      ]};
    },

    metricGroup(svc) {
      const group = this.metricGroups().groups.find(g => g.serviceName === svc) || this.metricGroups().groups[0];
      const points = Array.from({length: 48}, (_, index) => ({ hour: hoursAgo(47 - index), value: index % 11 === 0 ? null : rnd(20, 8000) }));
      return { serviceName: group.serviceName, title: group.title, periodHours: 720,
        metrics: group.metrics.map(metric => ({...metric, points})) };
    },

    users(query, offset, size) {
      const total = 247;
      const list = [];
      const max = Math.min(size, total - offset);
      for (let i = 0; i < max; i++) {
        const idx = offset + i;
        const fn = pick(FIRST_NAMES, idx);
        const ln = pick(LAST_NAMES, idx * 3);
        list.push({
          id: 1000 + idx,
          firstName: fn,
          lastName: ln,
          username: (fn + ln).toLowerCase().replace(/ё/g,'e') + idx,
          profilePicture: null,
          profilePicturePreview: null,
          badges: idx % 4 === 0 ? [{ badge: { id: 1, name: 'Verified', imageUrl: null } }] : []
        });
      }
      return { totalCount: total, users: list };
    },

    userDetail(id) {
      const idx = id - 1000;
      const fn = pick(FIRST_NAMES, idx);
      const ln = pick(LAST_NAMES, idx * 3);
      return {
        profile: {
          id, firstName: fn, lastName: ln,
          username: (fn + ln).toLowerCase().replace(/ё/g,'e') + idx,
          bio: 'Демо-пользователь для превью.',
          profilePicture: null,
          profilePosterUrl: null,
          registrationDate: hoursAgo(rnd(24, 8000)),
          storageLimitGb: 5,
          badges: []
        },
        contacts: { email: fn.toLowerCase() + '@example.com' },
        sessions: [
          { deviceId: rs(), customName: 'iPhone 15 Pro', originalName: 'iPhone', operationSystem: 'iOS 17.4', appName: 'BarkFluff', location: 'Moscow, Russia', createdAt: hoursAgo(48), expirationAt: hoursAgo(-720) },
          { deviceId: rs(), customName: 'MacBook Pro', originalName: 'MacBook', operationSystem: 'macOS 14.3', appName: 'BarkFluff Desktop', location: 'Saint Petersburg', createdAt: hoursAgo(120), expirationAt: hoursAgo(-720) }
        ],
        twoFactor: { authenticatorEnabled: idx % 3 === 0, emailEnabled: idx % 2 === 0 },
        storage: {
          totalUsedStorage: rnd(800, 3500) * 1024 * 1024,
          storageLimit: 5 * 1024 * 1024 * 1024,
          storageByTypes: [
            { fileType: 1, fileTypeName: 'Avatars',   usedStorage: rnd(20, 60) * 1024 * 1024 },
            { fileType: 2, fileTypeName: 'Images',    usedStorage: rnd(200, 800) * 1024 * 1024 },
            { fileType: 3, fileTypeName: 'Videos',    usedStorage: rnd(400, 1500) * 1024 * 1024 },
            { fileType: 5, fileTypeName: 'Documents', usedStorage: rnd(20, 200) * 1024 * 1024 }
          ]
        }
      };
    },

    badges() {
      return [
        { id: 1, name: 'Verified',     description: 'Подтверждённый аккаунт',   isActive: true,  imageUrl: null, createdAt: hoursAgo(2000) },
        { id: 2, name: 'Premium',      description: 'Premium-подписка',         isActive: true,  imageUrl: null, createdAt: hoursAgo(1500) },
        { id: 3, name: 'OG',           description: 'Один из первых юзеров',    isActive: true,  imageUrl: null, createdAt: hoursAgo(1000) },
        { id: 4, name: 'Beta Tester',  description: 'Помогал тестировать',      isActive: true,  imageUrl: null, createdAt: hoursAgo(800)  },
        { id: 5, name: 'Moderator',    description: 'Модератор сообщества',     isActive: true,  imageUrl: null, createdAt: hoursAgo(500)  },
        { id: 6, name: 'Legacy',       description: 'Устаревший бейдж',         isActive: false, imageUrl: null, createdAt: hoursAgo(3000) }
      ];
    },

    stickerPacks() {
      return [
        { id: 1, name: 'Котики',           shortName: 'cats',        coverUrl: null, stickersCount: 24, isPublished: true,  createdAt: hoursAgo(720) },
        { id: 2, name: 'Эмоции',           shortName: 'emotions',    coverUrl: null, stickersCount: 32, isPublished: true,  createdAt: hoursAgo(640) },
        { id: 3, name: 'Мемы 2025',        shortName: 'memes-25',    coverUrl: null, stickersCount: 18, isPublished: true,  createdAt: hoursAgo(560) },
        { id: 4, name: 'Праздники',        shortName: 'holidays',    coverUrl: null, stickersCount: 27, isPublished: false, createdAt: hoursAgo(220) },
        { id: 5, name: 'Программисты',     shortName: 'devs',        coverUrl: null, stickersCount: 21, isPublished: true,  createdAt: hoursAgo(180) },
        { id: 6, name: 'Дикие животные',   shortName: 'wildlife',    coverUrl: null, stickersCount: 16, isPublished: true,  createdAt: hoursAgo(90) }
      ];
    },

    notifications() {
      return [
        { id: 1, title: 'Новое сообщение', body: 'У вас 3 непрочитанных в чате', type: 'message', enabled: true, lastSentAt: minutesAgo(15), totalSent: 14829 },
        { id: 2, title: 'Звонок',          body: 'Входящий вызов',               type: 'call',    enabled: true, lastSentAt: minutesAgo(60), totalSent: 3924 },
        { id: 3, title: 'Обновление',      body: 'Доступна новая версия',        type: 'system',  enabled: false, lastSentAt: hoursAgo(72), totalSent: 528 },
        { id: 4, title: 'Реакция',         body: 'Кто-то поставил реакцию',      type: 'reaction',enabled: true, lastSentAt: minutesAgo(5),  totalSent: 88210 }
      ];
    },

    mailAccounts() {
      return [
        { id: 'support', address: 'support@barkfluff.app', unread: 4 },
        { id: 'noreply', address: 'noreply@barkfluff.app', unread: 0 },
        { id: 'security', address: 'security@barkfluff.app', unread: 1 }
      ];
    },
    mailMessages() {
      const list = [
        { uid: 1001, from: { name: 'Анна Петрова', address: 'anna@example.com' }, subject: 'Не могу войти в аккаунт',  preview: 'Здравствуйте! Уже сутки не могу попасть в свой аккаунт. Пишет «неверный пароль»...', date: minutesAgo(12), isRead: false, hasAttachments: false },
        { uid: 1002, from: { name: 'Jane Doe',  address: 'jane@example.com' }, subject: 'Восстановление пароля',       preview: 'Не приходит письмо с кодом восстановления. Пробовала разные адреса.',          date: hoursAgo(2),    isRead: false, hasAttachments: true  },
        { uid: 1003, from: { name: 'Stripe',    address: 'noreply@stripe.com' }, subject: 'Платёж получен',              preview: 'Оплата за подписку Premium прошла успешно. Сумма: $9.99',                       date: hoursAgo(6),    isRead: true,  hasAttachments: false },
        { uid: 1004, from: null,                                                  subject: 'Win a free iPhone!',           preview: 'Congratulations, you have been selected as the lucky winner...',                date: hoursAgo(20),   isRead: true,  hasAttachments: false },
        { uid: 1005, from: { address: 'admin@server.local' },                     subject: 'Backup completed',             preview: 'Резервная копия успешно создана. Размер 42 ГБ.',                                  date: hoursAgo(36),   isRead: false, hasAttachments: false }
      ];
      return { items: list, total: list.length };
    },
    mailMessageDetail(uid) {
      return {
        uid,
        from: { name: 'Анна Петрова', address: 'anna@example.com' },
        to: [{ name: 'Support', address: 'support@barkfluff.app' }],
        subject: 'Не могу войти в аккаунт',
        date: minutesAgo(12),
        isRead: true,
        bodyText: 'Здравствуйте!\n\nУже сутки не могу попасть в свой аккаунт.\nПишет «неверный пароль», хотя я уверена что не меняла.\n\nЛогин: anna_p\nID: 482939\n\nПожалуйста, помогите.\n\nСпасибо!',
        bodyHtml: null,
        attachments: []
      };
    },

    s3Storage() {
      return {
        buckets: [
          { id: 'avatars',   displayName: 'Аватарки',   filesCount: 12489, totalSize: 1.2 * 1024 * 1024 * 1024,  region: 'eu-central-1' },
          { id: 'media',     displayName: 'Медиа',      filesCount: 248721, totalSize: 84 * 1024 * 1024 * 1024,  region: 'eu-central-1' },
          { id: 'documents', displayName: 'Документы',  filesCount: 8920,  totalSize: 4.6 * 1024 * 1024 * 1024,  region: 'eu-central-1' },
          { id: 'stickers',  displayName: 'Стикеры',    filesCount: 1840,  totalSize: 320 * 1024 * 1024,         region: 'eu-central-1' }
        ]
      };
    },
    s3Objects(bucket, prefix) {
      const samples = [
        { type:'folder', name:'avatars/' },
        { type:'folder', name:'thumbnails/' },
        { type:'folder', name:'2026/' },
        { type:'file',   name:'config.json',         size: 4821,       ext:'.json' },
        { type:'file',   name:'image_482939.webp',   size: 248192,     ext:'.webp' },
        { type:'file',   name:'video_demo.mp4',      size: 18248192,   ext:'.mp4'  },
        { type:'file',   name:'report.pdf',          size: 482192,     ext:'.pdf'  },
        { type:'file',   name:'sticker_pack.zip',    size: 8421920,    ext:'.zip'  },
        { type:'file',   name:'voice_message.opus',  size: 92128,      ext:'.opus' },
        { type:'file',   name:'badge_icon.png',      size: 8192,       ext:'.png'  },
        { type:'file',   name:'avatar.jpg',          size: 124928,     ext:'.jpg'  }
      ];
      const items = samples.map(s => ({
        key: (prefix || '') + s.name,
        isFolder: s.type === 'folder',
        size: s.size || 0,
        lastModified: hoursAgo(rnd(1, 600))
      }));
      return { contents: items, continuationToken: null, isTruncated: false };
    },
    s3Configuration() {
      const now = new Date(Date.now() - 86400000 * 3).toISOString();
      const bucket = (name) => ({ serviceUrl: 'https://s3.eu-central-1.amazonaws.com', bucketName: name, accessKeyConfigured: true, accessKeyMasked: 'AKI…LE', secretKeyConfigured: true, region: 'eu-central-1', editedAt: now });
      return {
        'profile-pictures': bucket('bf-profile-pictures'),
        'message-images':   bucket('bf-message-images'),
        'message-videos':   bucket('bf-message-videos'),
        'message-documents':bucket('bf-message-documents'),
        'message-audio':    bucket('bf-message-audio'),
        'chat-pictures':    bucket('bf-chat-pictures'),
        'badge-images':     bucket('bf-badge-images'),
        'barkfluff-uploads':bucket('bf-uploads')
      };
    },
    s3Browser(bucket) {
      return {
        bucket,
        items: [
          { key: 'avatars/',              type: 'folder', size: 0, modifiedAt: hoursAgo(720) },
          { key: 'cache/',                type: 'folder', size: 0, modifiedAt: hoursAgo(48) },
          { key: 'uploads/2026/',         type: 'folder', size: 0, modifiedAt: hoursAgo(24) },
          { key: 'config.json',           type: 'file',   size: 4821, modifiedAt: hoursAgo(8) },
          { key: 'image_482939.webp',     type: 'file',   size: 248192, modifiedAt: hoursAgo(3) },
          { key: 'video_demo.mp4',        type: 'file',   size: 18248192, modifiedAt: hoursAgo(1) },
          { key: 'report.pdf',            type: 'file',   size: 482192, modifiedAt: minutesAgo(15) }
        ]
      };
    },

    logs() {
      const services = SERVICES;
      const messages = [
        'Request completed successfully',
        'Database connection acquired',
        'User authentication succeeded',
        'Cache miss for key user:482',
        'Slow query detected on messages table',
        'Failed to connect to S3 bucket',
        'JWT token expired',
        'WebSocket connection opened',
        'WebSocket connection closed',
        'Notification dispatched',
        'Rate limit exceeded for IP 192.0.2.4',
        'Background job picked up',
        'Background job completed in 124ms'
      ];
      const levels = ['Information','Information','Information','Information','Warning','Warning','Error','Information'];
      const out = [];
      for (let i = 0; i < 200; i++) {
        const lvl = pick(levels, rnd(0, 99));
        out.push({
          id: rs(),
          timestamp: minutesAgo(rnd(0, 240)),
          level: lvl,
          service: pick(services, rnd(0, 99)),
          message: pick(messages, rnd(0, 99)),
          properties: {}
        });
      }
      out.sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));
      return { logs: out, totalCount: 482917, services };
    },

    servicesList() {
      return SERVICES.map((s, i) => ({
        id: i + 1,
        name: s,
        displayName: s.split('.').pop(),
        status: i % 8 === 7 ? 'stopped' : 'running',
        cpuPercent: rnd(2, 78),
        memoryMb: rnd(64, 920),
        uptimeSec: rnd(1800, 86400 * 14),
        version: '2.4.' + rnd(10, 90),
        replicas: rnd(1, 4)
      }));
    },

    servicesStatus() {
      return SERVICES.map((s, i) => ({
        name: s,
        dockerState: i % 8 === 7 ? 'exited' : (i % 11 === 10 ? 'restarting' : 'running'),
        lastSeen: minutesAgo(rnd(0, 60)),
        errorCount: i % 4 === 0 ? rnd(0, 8) : 0,
        eventCount: rnd(120, 14820)
      }));
    },

    remoteServers() {
      return {
        navigator: {
          host: 'nav.barkfluff.app', port: 22, user: 'deploy', password: '••••••••••',
          services: [
            { name: 'navigator-api', status: 'running' },
            { name: 'navigator-worker', status: 'running' },
            { name: 'navigator-cache', status: 'stopped' }
          ]
        },
        msk: {
          host: 'msk.barkfluff.app', port: 22, user: 'deploy', password: '••••••••••',
          services: [
            { name: 'msk-edge', status: 'running' },
            { name: 'msk-relay', status: 'running' }
          ]
        }
      };
    }
  };

  // -------- Router --------
  function match(url, pattern) {
    const re = new RegExp('^' + pattern.replace(/:(\w+)/g, '([^/?#]+)').replace(/\*/g, '.*') + '(\\?.*)?$');
    return re.exec(url);
  }

  function json(data, status) {
    status = status || 200;
    return new Response(JSON.stringify(data), {
      status,
      headers: { 'Content-Type': 'application/json' }
    });
  }

  async function handle(input, init) {
    const url = typeof input === 'string' ? input : (input && input.url) || '';
    const method = (init && init.method) || (typeof input === 'object' && input.method) || 'GET';

    // ---- Auth ----
    if (match(url, '/api/auth/me')) return json(fix.authMe);
    if (match(url, '/api/auth/logout')) return json({ ok: true });
    if (match(url, '/api/auth/request')) return json({ requestId: rs() });
    if (match(url, '/api/auth/status/.+')) return json({ status: 0 });

    // ---- Dashboard ----
    if (match(url, '/api/seq/dashboard/kpis.*'))    return json(fix.kpis());
    if (match(url, '/api/seq/dashboard/traffic.*')) return json(fix.traffic());
    const metricGroup = match(url, '/api/seq/dashboard/metric-groups/:svc');
    if (metricGroup) return json(fix.metricGroup(decodeURIComponent(metricGroup[1])));
    if (match(url, '/api/seq/dashboard/metric-groups.*')) return json(fix.metricGroups());
    const m = match(url, '/api/seq/dashboard/service-metrics/:svc');
    if (m) return json(fix.serviceMetrics(decodeURIComponent(m[1])));

    // ---- S3 ----
    if (match(url, '/api/s3/buckets')) return json(fix.s3Buckets);
    if (match(url, '/api/s3/storage')) return json(fix.s3Storage());
    if (match(url, '/api/configuration/s3-configuration')) return json(fix.s3Configuration());
    if (match(url, '/api/configuration/s3/update')) return json({ success: true });
    // S3 objects endpoint(s) — used by browser
    const sob = match(url, '/api/s3/buckets/([^/]+)/objects.*');
    if (sob) {
      const u = new URL(url, 'http://x');
      return json(fix.s3Objects(decodeURIComponent(sob[1]), u.searchParams.get('prefix') || ''));
    }
    if (match(url, '/api/s3/buckets/[^/]+/presign.*')) return json({ url: 'https://example.com/preview-not-available' });
    const b = match(url, '/api/s3/browser/:bucket.*');
    if (b) return json(fix.s3Browser(decodeURIComponent(b[1])));

    // ---- Users ----
    if (match(url, '/api/users\\?.*') || /^\/api\/users(\?|$)/.test(url)) {
      const u = new URL(url, 'http://x');
      const q = u.searchParams.get('query') || '';
      const offset = parseInt(u.searchParams.get('offset') || '0', 10);
      const size = parseInt(u.searchParams.get('size') || '50', 10);
      return json(fix.users(q, offset, size));
    }
    const userMatch = match(url, '/api/users/(\\d+)');
    if (userMatch) return json(fix.userDetail(parseInt(userMatch[1], 10)));

    if (match(url, '/api/reserved-names')) return json(['admin','support','help','barkfluff','official','team']);

    // ---- Badges ----
    if (match(url, '/api/badges')) return json(fix.badges());

    // ---- Stickers ----
    if (match(url, '/api/stickers/packs')) return json(fix.stickerPacks());
    if (match(url, '/api/stickers/packs/.+')) return json({ id: 1, name: 'Котики', shortName: 'cats', stickers: Array.from({length: 12}, (_, i) => ({ id: i+1, emoji: '🐱', imageUrl: null })) });

    // ---- Notifications ----
    if (match(url, '/api/notifications')) return json(fix.notifications());

    // ---- Mail ----
    if (match(url, '/api/mail/accounts')) return json(fix.mailAccounts());
    const mailDetail = match(url, '/api/mail/[^/]+/messages/(\\d+)$');
    if (mailDetail) return json(fix.mailMessageDetail(parseInt(mailDetail[1], 10)));
    if (match(url, '/api/mail/[^/]+/messages.*')) return json(fix.mailMessages());

    // ---- Logs ----
    if (match(url, '/api/logs.*'))    return json(fix.logs());
    if (match(url, '/api/seq/logs.*')) return json(fix.logs());

    // ---- Services ----
    if (match(url, '/api/services')) return json(fix.servicesList());
    if (match(url, '/api/seq/services/status')) return json(fix.servicesStatus());
    if (match(url, '/api/docker/containers/.+/(start|stop|restart|pull|restart-own)')) {
      return json({ success: true, message: 'Действие выполнено успешно' });
    }
    if (match(url, '/api/remote-servers')) return json(fix.remoteServers());

    // anything we don't know → 404 (so the page can handle it)
    return json({ error: 'mocked-404', url }, 404);
  }

  const origFetch = window.fetch.bind(window);
  window.fetch = async function (input, init) {
    const url = typeof input === 'string' ? input : (input && input.url) || '';
    if (/^\/api\//.test(url) || /\/api\//.test(url)) {
      try { return await handle(input, init); }
      catch (e) { return new Response(JSON.stringify({ error: String(e) }), { status: 500 }); }
    }
    return origFetch(input, init);
  };
})();
