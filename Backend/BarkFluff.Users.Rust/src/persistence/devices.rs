//! DevicesStorage — зеркало `Persistence/Services/DevicesStorage.cs`.

use crate::domain::UserDevice;
use crate::errors::{AppError, AppResult};
use chrono::Utc;
use sqlx::{PgPool, Row};
use uuid::Uuid;

const DEVICE_COLS: &str = r#""Id","UserId","OriginalName","CustomName","AuthorizedAt","AppName","OperationSystem","Location","FirebaseDeviceToken","NotificationsEnabled""#;

/// Firebase-токен устройства: (user_id, device_id-строка, token).
pub type FirebaseToken = (i64, String, String);

pub async fn get_device_by_id(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
) -> AppResult<Option<UserDevice>> {
    let sql = format!(r#"SELECT {DEVICE_COLS} FROM "UserDevices" WHERE "Id" = $1 AND "UserId" = $2"#);
    Ok(sqlx::query_as::<_, UserDevice>(&sql)
        .bind(device_id)
        .bind(user_id)
        .fetch_optional(pool)
        .await?)
}

pub async fn get_devices_by_user_id(pool: &PgPool, user_id: i64) -> AppResult<Vec<UserDevice>> {
    let sql = format!(
        r#"SELECT {DEVICE_COLS} FROM "UserDevices" WHERE "UserId" = $1 ORDER BY "AuthorizedAt" DESC"#
    );
    Ok(sqlx::query_as::<_, UserDevice>(&sql)
        .bind(user_id)
        .fetch_all(pool)
        .await?)
}

/// Upsert по (Id, UserId): обновляет OriginalName/AppName/OS/Location/AuthorizedAt,
/// не трогая CustomName/NotificationsEnabled/FirebaseToken; иначе создаёт.
pub async fn register_or_update_device(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    original_name: &str,
    app_name: Option<&str>,
    operation_system: Option<&str>,
    location: Option<&str>,
) -> AppResult<UserDevice> {
    let now = Utc::now();
    let existing = get_device_by_id(pool, device_id, user_id).await?;
    if existing.is_some() {
        sqlx::query(
            r#"UPDATE "UserDevices" SET "OriginalName" = $3, "AppName" = $4, "OperationSystem" = $5, "Location" = $6, "AuthorizedAt" = $7 WHERE "Id" = $1 AND "UserId" = $2"#,
        )
        .bind(device_id)
        .bind(user_id)
        .bind(original_name)
        .bind(app_name)
        .bind(operation_system)
        .bind(location)
        .bind(now)
        .execute(pool)
        .await?;
    } else {
        sqlx::query(
            r#"INSERT INTO "UserDevices" ("Id","UserId","OriginalName","AuthorizedAt","AppName","OperationSystem","Location","NotificationsEnabled")
               VALUES ($1,$2,$3,$4,$5,$6,$7,true)"#,
        )
        .bind(device_id)
        .bind(user_id)
        .bind(original_name)
        .bind(now)
        .bind(app_name)
        .bind(operation_system)
        .bind(location)
        .execute(pool)
        .await?;
    }

    get_device_by_id(pool, device_id, user_id)
        .await?
        .ok_or_else(|| AppError::System("устройство не найдено после upsert".into()))
}

pub async fn rename_device(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    custom_name: &str,
) -> AppResult<()> {
    let res = sqlx::query(
        r#"UPDATE "UserDevices" SET "CustomName" = $3 WHERE "Id" = $1 AND "UserId" = $2"#,
    )
    .bind(device_id)
    .bind(user_id)
    .bind(custom_name)
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::System("Устройство не найдено".into()));
    }
    Ok(())
}

pub async fn delete_device(pool: &PgPool, device_id: Uuid, user_id: i64) -> AppResult<()> {
    sqlx::query(r#"DELETE FROM "UserDevices" WHERE "Id" = $1 AND "UserId" = $2"#)
        .bind(device_id)
        .bind(user_id)
        .execute(pool)
        .await?;
    Ok(())
}

pub async fn set_firebase_token(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    token: &str,
) -> AppResult<()> {
    let res = sqlx::query(
        r#"UPDATE "UserDevices" SET "FirebaseDeviceToken" = $3 WHERE "Id" = $1 AND "UserId" = $2"#,
    )
    .bind(device_id)
    .bind(user_id)
    .bind(token)
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::System("Устройство не найдено".into()));
    }
    Ok(())
}

pub async fn set_notifications_enabled(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    enabled: bool,
) -> AppResult<()> {
    let res = sqlx::query(
        r#"UPDATE "UserDevices" SET "NotificationsEnabled" = $3 WHERE "Id" = $1 AND "UserId" = $2"#,
    )
    .bind(device_id)
    .bind(user_id)
    .bind(enabled)
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::System("Устройство не найдено".into()));
    }
    Ok(())
}

fn map_token_rows(rows: Vec<sqlx::postgres::PgRow>) -> Vec<FirebaseToken> {
    rows.into_iter()
        .map(|r| {
            let user_id: i64 = r.get("UserId");
            let id: Uuid = r.get("Id");
            let token: String = r.get("FirebaseDeviceToken");
            (user_id, id.to_string(), token)
        })
        .collect()
}

pub async fn get_devices_with_firebase_tokens(
    pool: &PgPool,
    user_ids: &[i64],
) -> AppResult<Vec<FirebaseToken>> {
    let rows = sqlx::query(
        r#"SELECT "UserId","Id","FirebaseDeviceToken" FROM "UserDevices" WHERE "UserId" = ANY($1) AND "FirebaseDeviceToken" IS NOT NULL AND "NotificationsEnabled" = true"#,
    )
    .bind(user_ids)
    .fetch_all(pool)
    .await?;
    Ok(map_token_rows(rows))
}

pub async fn get_devices_with_firebase_tokens_by_device_ids(
    pool: &PgPool,
    device_ids: &[Uuid],
) -> AppResult<Vec<FirebaseToken>> {
    let rows = sqlx::query(
        r#"SELECT "UserId","Id","FirebaseDeviceToken" FROM "UserDevices" WHERE "Id" = ANY($1) AND "FirebaseDeviceToken" IS NOT NULL AND "NotificationsEnabled" = true"#,
    )
    .bind(device_ids)
    .fetch_all(pool)
    .await?;
    Ok(map_token_rows(rows))
}

pub async fn get_all_devices_with_firebase_tokens(pool: &PgPool) -> AppResult<Vec<FirebaseToken>> {
    let rows = sqlx::query(
        r#"SELECT "UserId","Id","FirebaseDeviceToken" FROM "UserDevices" WHERE "FirebaseDeviceToken" IS NOT NULL AND "NotificationsEnabled" = true"#,
    )
    .fetch_all(pool)
    .await?;
    Ok(map_token_rows(rows))
}
