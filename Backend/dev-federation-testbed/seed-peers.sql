-- Ручной сид KnownServers/KnownServerKeys для двух-нодового стенда (этап 1.3 — плейнтекст,
-- TlsSpkiSha256 пуст; SPKI-пиннинг через nginx/self-signed — этап 1.6, там сид обновляется).
--
-- Шаблон — подставь реальные значения перед запуском (см. README.md этой папки, раздел
-- "Как получить публичные ключи"):
--   {{NODE1_SERVER_NAME}}, {{NODE2_SERVER_NAME}} — Federation:ServerName каждой ноды
--   {{NODE1_PUBLIC_KEY_BASE64}}, {{NODE2_PUBLIC_KEY_BASE64}} — публичный ключ ed25519:1
--     (SELECT "KeyId", encode("PublicKey", 'base64') FROM "SigningKeys" в БД federation каждой ноды)
--
-- Секция A — выполнить в БД federation НОДЫ 1: добавляет ноду 2 как известного пира.
INSERT INTO "KnownServers" ("ServerName", "FederationEndpoint", "TlsSpkiSha256", "Source", "Status",
                            "FirstSeenAt", "LastSeenAt", "LastKeyRefreshAt", "ProtocolVersion")
VALUES ('{{NODE2_SERVER_NAME}}', 'http://federation2:7030', ARRAY[]::text[], 'Manual', 'Active',
        NOW(), NOW(), NOW(), 1)
ON CONFLICT ("ServerName") DO NOTHING;

INSERT INTO "KnownServerKeys" ("ServerName", "KeyId", "PublicKey", "ExpiredAt", "RevokedAt")
VALUES ('{{NODE2_SERVER_NAME}}', 'ed25519:1', decode('{{NODE2_PUBLIC_KEY_BASE64}}', 'base64'), NULL, NULL)
ON CONFLICT ("ServerName", "KeyId") DO NOTHING;

-- Секция B — выполнить в БД federation НОДЫ 2: добавляет ноду 1 как известного пира.
INSERT INTO "KnownServers" ("ServerName", "FederationEndpoint", "TlsSpkiSha256", "Source", "Status",
                            "FirstSeenAt", "LastSeenAt", "LastKeyRefreshAt", "ProtocolVersion")
VALUES ('{{NODE1_SERVER_NAME}}', 'http://federation:7030', ARRAY[]::text[], 'Manual', 'Active',
        NOW(), NOW(), NOW(), 1)
ON CONFLICT ("ServerName") DO NOTHING;

INSERT INTO "KnownServerKeys" ("ServerName", "KeyId", "PublicKey", "ExpiredAt", "RevokedAt")
VALUES ('{{NODE1_SERVER_NAME}}', 'ed25519:1', decode('{{NODE1_PUBLIC_KEY_BASE64}}', 'base64'), NULL, NULL)
ON CONFLICT ("ServerName", "KeyId") DO NOTHING;
