-- DATA FIX: обновление типа файлов-постеров профиля
-- Постеры загружались с типом MessageAttachmentImage (2), но должны иметь тип UserProfilePoster (10)
-- 
-- Логика: файл является постером если его ID находится в таблице UserPersonalizations
-- базы данных users. Выполнять вручную после деплоя сервиса Files с новым типом.
--
-- ВАЖНО: поменяйте имена БД/схем если они отличаются от дефолтных

-- Files DB: обновляем тип файлов чьи ID сохранены как постеры в Users DB
UPDATE "UploadedFiles"
SET "Type" = 10  -- UserProfilePoster
WHERE "Id"::text IN (
	SELECT "ProfilePosterFileId"
	FROM dblink(
		'dbname=users_db host=localhost user=postgres password=YOUR_PASSWORD',
		'SELECT "ProfilePosterFileId" FROM "UserPersonalizations" WHERE "ProfilePosterFileId" IS NOT NULL'
	) AS t("ProfilePosterFileId" text)
)
AND "Type" = 2;  -- только те, что были загружены как MessageAttachmentImage

-- Если dblink не настроен — используйте альтернативный вариант с явными ID:
-- UPDATE "UploadedFiles" SET "Type" = 10
-- WHERE "Id" IN ('uuid1', 'uuid2', ...);  -- список ID из таблицы UserPersonalizations
