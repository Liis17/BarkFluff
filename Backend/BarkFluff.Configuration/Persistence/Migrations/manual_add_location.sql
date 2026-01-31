-- Ручная вставка поля Location для ServerProps в базу данных Configurations
-- Выполните этот скрипт, если миграция не применилась автоматически

-- Проверяем, существует ли уже запись
SELECT * FROM "Configurations"
WHERE "Section" = 'ServerProps'
  AND "Key" = 'Location'
  AND "ServiceId" = 3;

-- Если запись не существует, вставляем её
INSERT INTO "Configurations" ("Section", "Key", "Value", "EditedAt", "EditedBy", "EditedFrom", "ServiceId")
SELECT 'ServerProps', 'Location', '', NOW(), 'System', 'ManualScript', 3
WHERE NOT EXISTS (
    SELECT 1 FROM "Configurations"
    WHERE "Section" = 'ServerProps'
      AND "Key" = 'Location'
      AND "ServiceId" = 3
);

-- Проверяем результат
SELECT * FROM "Configurations"
WHERE "ServiceId" = 3
  AND "Section" = 'ServerProps'
ORDER BY "Key";

-- Для обновления значения Location (пример):
-- UPDATE "Configurations"
-- SET "Value" = 'Moscow, Russia', "EditedAt" = NOW(), "EditedBy" = 'Admin', "EditedFrom" = 'Manual'
-- WHERE "Section" = 'ServerProps'
--   AND "Key" = 'Location'
--   AND "ServiceId" = 3;
