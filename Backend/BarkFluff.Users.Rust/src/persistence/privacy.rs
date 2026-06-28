//! PrivacyStorage — зеркало `Persistence/Services/PrivacyStorage.cs`.
//! Дефолты: профиль виден, Email=None(2), остальное All(0)/true.

use crate::domain::{Privacy, VISIBILITY_ALL, VISIBILITY_NONE};
use crate::errors::AppResult;
use sqlx::PgPool;

const COLS: &str = r#""Id","UserId","ProfileVisibleOnSite","AvatarVisibility","BioVisibility","EmailVisibility","OnlineVisibility","SearchVisible""#;

pub async fn get(pool: &PgPool, user_id: i64) -> AppResult<Option<Privacy>> {
    let sql = format!(r#"SELECT {COLS} FROM "Privacies" WHERE "UserId" = $1"#);
    Ok(sqlx::query_as::<_, Privacy>(&sql)
        .bind(user_id)
        .fetch_optional(pool)
        .await?)
}

pub async fn create(pool: &PgPool, user_id: i64) -> AppResult<Privacy> {
    let sql = format!(
        r#"INSERT INTO "Privacies" ("UserId","ProfileVisibleOnSite","AvatarVisibility","BioVisibility","EmailVisibility","OnlineVisibility","SearchVisible")
           VALUES ($1, true, $2, $2, $3, $2, true) RETURNING {COLS}"#
    );
    Ok(sqlx::query_as::<_, Privacy>(&sql)
        .bind(user_id)
        .bind(VISIBILITY_ALL)
        .bind(VISIBILITY_NONE)
        .fetch_one(pool)
        .await?)
}

pub async fn get_or_create(pool: &PgPool, user_id: i64) -> AppResult<Privacy> {
    match get(pool, user_id).await? {
        Some(p) => Ok(p),
        None => create(pool, user_id).await,
    }
}

#[allow(clippy::too_many_arguments)]
pub async fn update(
    pool: &PgPool,
    user_id: i64,
    profile_visible_on_site: bool,
    avatar_visibility: i32,
    bio_visibility: i32,
    email_visibility: i32,
    search_visible: bool,
    online_visibility: i32,
) -> AppResult<Privacy> {
    // GetOrCreate гарантирует наличие строки.
    get_or_create(pool, user_id).await?;
    let sql = format!(
        r#"UPDATE "Privacies" SET "ProfileVisibleOnSite" = $2, "AvatarVisibility" = $3, "BioVisibility" = $4,
               "EmailVisibility" = $5, "SearchVisible" = $6, "OnlineVisibility" = $7
           WHERE "UserId" = $1 RETURNING {COLS}"#
    );
    Ok(sqlx::query_as::<_, Privacy>(&sql)
        .bind(user_id)
        .bind(profile_visible_on_site)
        .bind(avatar_visibility)
        .bind(bio_visibility)
        .bind(email_visibility)
        .bind(search_visible)
        .bind(online_visibility)
        .fetch_one(pool)
        .await?)
}
