//! PrekeyStorage (X3DH) — зеркало `Persistence/Services/PrekeyStorage.cs`.

use crate::domain::{DevicePrekeyBundle, OneTimePrekey, UserDevice};
use crate::errors::{AppError, AppResult};
use chrono::Utc;
use sqlx::{PgPool, Row};
use uuid::Uuid;

const BUNDLE_COLS: &str = r#""DeviceId","RegistrationId","IdentityPubkey","SignedPrekeyId","SignedPrekeyPublic","SignedPrekeySignature","SignedPrekeyRotatedAt","CreatedAt""#;
const PREKEY_COLS: &str = r#""Id","DeviceId","PrekeyId","PublicKey","CreatedAt""#;
const DEVICE_COLS: &str = r#""Id","UserId","OriginalName","CustomName","AuthorizedAt","AppName","OperationSystem","Location","FirebaseDeviceToken","NotificationsEnabled""#;

async fn device_exists(pool: &PgPool, device_id: Uuid, user_id: i64) -> AppResult<bool> {
    let exists: Option<i64> =
        sqlx::query_scalar(r#"SELECT 1 FROM "UserDevices" WHERE "Id" = $1 AND "UserId" = $2"#)
            .bind(device_id)
            .bind(user_id)
            .fetch_optional(pool)
            .await?;
    Ok(exists.is_some())
}

async fn get_bundle(pool: &PgPool, device_id: Uuid) -> AppResult<Option<DevicePrekeyBundle>> {
    let sql = format!(r#"SELECT {BUNDLE_COLS} FROM "DevicePrekeyBundles" WHERE "DeviceId" = $1"#);
    Ok(sqlx::query_as::<_, DevicePrekeyBundle>(&sql)
        .bind(device_id)
        .fetch_optional(pool)
        .await?)
}

async fn existing_prekey_ids(pool: &PgPool, device_id: Uuid) -> AppResult<std::collections::HashSet<i64>> {
    let rows = sqlx::query(r#"SELECT "PrekeyId" FROM "OneTimePrekeys" WHERE "DeviceId" = $1"#)
        .bind(device_id)
        .fetch_all(pool)
        .await?;
    Ok(rows.into_iter().map(|r| r.get::<i64, _>("PrekeyId")).collect())
}

#[allow(clippy::too_many_arguments)]
pub async fn register_bundle(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    registration_id: i64,
    identity_pubkey: &[u8],
    signed_prekey_id: i64,
    signed_prekey_public: &[u8],
    signed_prekey_signature: &[u8],
    one_time_prekeys: &[(i64, Vec<u8>)],
) -> AppResult<DevicePrekeyBundle> {
    if !device_exists(pool, device_id, user_id).await? {
        return Err(AppError::System("Устройство не найдено".into()));
    }
    let now = Utc::now();

    if get_bundle(pool, device_id).await?.is_some() {
        sqlx::query(
            r#"UPDATE "DevicePrekeyBundles" SET "RegistrationId" = $2, "IdentityPubkey" = $3, "SignedPrekeyId" = $4, "SignedPrekeyPublic" = $5, "SignedPrekeySignature" = $6, "SignedPrekeyRotatedAt" = $7 WHERE "DeviceId" = $1"#,
        )
        .bind(device_id)
        .bind(registration_id)
        .bind(identity_pubkey)
        .bind(signed_prekey_id)
        .bind(signed_prekey_public)
        .bind(signed_prekey_signature)
        .bind(now)
        .execute(pool)
        .await?;
    } else {
        sqlx::query(
            r#"INSERT INTO "DevicePrekeyBundles" ("DeviceId","RegistrationId","IdentityPubkey","SignedPrekeyId","SignedPrekeyPublic","SignedPrekeySignature","SignedPrekeyRotatedAt","CreatedAt")
               VALUES ($1,$2,$3,$4,$5,$6,$7,$8)"#,
        )
        .bind(device_id)
        .bind(registration_id)
        .bind(identity_pubkey)
        .bind(signed_prekey_id)
        .bind(signed_prekey_public)
        .bind(signed_prekey_signature)
        .bind(now)
        .bind(now)
        .execute(pool)
        .await?;
    }

    if !one_time_prekeys.is_empty() {
        let existing = existing_prekey_ids(pool, device_id).await?;
        for (prekey_id, public_key) in one_time_prekeys {
            if existing.contains(prekey_id) {
                continue;
            }
            sqlx::query(
                r#"INSERT INTO "OneTimePrekeys" ("DeviceId","PrekeyId","PublicKey","CreatedAt") VALUES ($1,$2,$3,$4)"#,
            )
            .bind(device_id)
            .bind(prekey_id)
            .bind(public_key)
            .bind(now)
            .execute(pool)
            .await?;
        }
    }

    get_bundle(pool, device_id)
        .await?
        .ok_or_else(|| AppError::System("bundle отсутствует после регистрации".into()))
}

pub async fn rotate_signed_prekey(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    signed_prekey_id: i64,
    signed_prekey_public: &[u8],
    signed_prekey_signature: &[u8],
) -> AppResult<()> {
    let res = sqlx::query(
        r#"UPDATE "DevicePrekeyBundles" b
           SET "SignedPrekeyId" = $3, "SignedPrekeyPublic" = $4, "SignedPrekeySignature" = $5, "SignedPrekeyRotatedAt" = $6
           FROM "UserDevices" d
           WHERE b."DeviceId" = $1 AND d."Id" = b."DeviceId" AND d."UserId" = $2"#,
    )
    .bind(device_id)
    .bind(user_id)
    .bind(signed_prekey_id)
    .bind(signed_prekey_public)
    .bind(signed_prekey_signature)
    .bind(Utc::now())
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::System("Bundle устройства не зарегистрирован".into()));
    }
    Ok(())
}

pub async fn replenish_one_time_prekeys(
    pool: &PgPool,
    device_id: Uuid,
    user_id: i64,
    prekeys: &[(i64, Vec<u8>)],
) -> AppResult<i32> {
    if !device_exists(pool, device_id, user_id).await? {
        return Err(AppError::System("Устройство не найдено".into()));
    }
    if !prekeys.is_empty() {
        let existing = existing_prekey_ids(pool, device_id).await?;
        let now = Utc::now();
        for (prekey_id, public_key) in prekeys {
            if existing.contains(prekey_id) {
                continue;
            }
            sqlx::query(
                r#"INSERT INTO "OneTimePrekeys" ("DeviceId","PrekeyId","PublicKey","CreatedAt") VALUES ($1,$2,$3,$4)"#,
            )
            .bind(device_id)
            .bind(prekey_id)
            .bind(public_key)
            .bind(now)
            .execute(pool)
            .await?;
        }
    }
    let count: i64 =
        sqlx::query_scalar(r#"SELECT COUNT(*) FROM "OneTimePrekeys" WHERE "DeviceId" = $1"#)
            .bind(device_id)
            .fetch_one(pool)
            .await?;
    Ok(count as i32)
}

/// Атомарно получает bundle устройства собеседника и расходует одну one-time prekey
/// через `DELETE ... FOR UPDATE SKIP LOCKED`.
pub async fn fetch_bundle(
    pool: &PgPool,
    peer_user_id: i64,
    peer_device_id: Uuid,
) -> AppResult<Option<(DevicePrekeyBundle, Option<OneTimePrekey>, i32)>> {
    // bundle где DeviceId И принадлежит peer_user_id.
    let bundle_sql = format!(
        r#"SELECT {BUNDLE_COLS} FROM "DevicePrekeyBundles" b
           JOIN "UserDevices" d ON d."Id" = b."DeviceId"
           WHERE b."DeviceId" = $1 AND d."UserId" = $2"#
    );
    let bundle = sqlx::query_as::<_, DevicePrekeyBundle>(&bundle_sql)
        .bind(peer_device_id)
        .bind(peer_user_id)
        .fetch_optional(pool)
        .await?;
    let Some(bundle) = bundle else {
        return Ok(None);
    };

    let claim_sql = format!(
        r#"DELETE FROM "OneTimePrekeys"
           WHERE "Id" = (
               SELECT "Id" FROM "OneTimePrekeys"
               WHERE "DeviceId" = $1
               ORDER BY "Id"
               LIMIT 1
               FOR UPDATE SKIP LOCKED
           )
           RETURNING {PREKEY_COLS}"#
    );
    let prekey = sqlx::query_as::<_, OneTimePrekey>(&claim_sql)
        .bind(peer_device_id)
        .fetch_optional(pool)
        .await?;

    let remaining: i64 =
        sqlx::query_scalar(r#"SELECT COUNT(*) FROM "OneTimePrekeys" WHERE "DeviceId" = $1"#)
            .bind(peer_device_id)
            .fetch_one(pool)
            .await?;

    Ok(Some((bundle, prekey, remaining as i32)))
}

pub async fn list_peer_devices(
    pool: &PgPool,
    peer_user_id: i64,
) -> AppResult<Vec<(UserDevice, bool)>> {
    let dev_sql = format!(
        r#"SELECT {DEVICE_COLS} FROM "UserDevices" WHERE "UserId" = $1 ORDER BY "AuthorizedAt" DESC"#
    );
    let devices = sqlx::query_as::<_, UserDevice>(&dev_sql)
        .bind(peer_user_id)
        .fetch_all(pool)
        .await?;
    if devices.is_empty() {
        return Ok(Vec::new());
    }

    let device_ids: Vec<Uuid> = devices.iter().map(|d| d.id).collect();
    let bundle_rows = sqlx::query(r#"SELECT "DeviceId" FROM "DevicePrekeyBundles" WHERE "DeviceId" = ANY($1)"#)
        .bind(&device_ids)
        .fetch_all(pool)
        .await?;
    let with_bundle: std::collections::HashSet<Uuid> =
        bundle_rows.into_iter().map(|r| r.get::<Uuid, _>("DeviceId")).collect();

    Ok(devices
        .into_iter()
        .map(|d| {
            let has = with_bundle.contains(&d.id);
            (d, has)
        })
        .collect())
}
