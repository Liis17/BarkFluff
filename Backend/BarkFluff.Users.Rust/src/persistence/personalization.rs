//! PersonalizationStorage — зеркало `Persistence/Services/PersonalizationStorage.cs`.

use crate::domain::UserPersonalization;
use crate::errors::AppResult;
use sqlx::PgPool;

const COLS: &str = r#""Id","UserId","ProfilePosterFileId","ChatBackgroundFileIds""#;

pub async fn get(pool: &PgPool, user_id: i64) -> AppResult<Option<UserPersonalization>> {
    let sql = format!(r#"SELECT {COLS} FROM "UserPersonalizations" WHERE "UserId" = $1"#);
    Ok(sqlx::query_as::<_, UserPersonalization>(&sql)
        .bind(user_id)
        .fetch_optional(pool)
        .await?)
}

pub async fn create(pool: &PgPool, user_id: i64) -> AppResult<UserPersonalization> {
    let empty: Vec<String> = Vec::new();
    let sql = format!(
        r#"INSERT INTO "UserPersonalizations" ("UserId","ChatBackgroundFileIds") VALUES ($1, $2) RETURNING {COLS}"#
    );
    Ok(sqlx::query_as::<_, UserPersonalization>(&sql)
        .bind(user_id)
        .bind(&empty)
        .fetch_one(pool)
        .await?)
}

pub async fn get_or_create(pool: &PgPool, user_id: i64) -> AppResult<UserPersonalization> {
    match get(pool, user_id).await? {
        Some(p) => Ok(p),
        None => create(pool, user_id).await,
    }
}

pub async fn update(
    pool: &PgPool,
    user_id: i64,
    profile_poster_file_id: Option<&str>,
    chat_background_file_ids: &[String],
) -> AppResult<UserPersonalization> {
    get_or_create(pool, user_id).await?;
    let sql = format!(
        r#"UPDATE "UserPersonalizations" SET "ProfilePosterFileId" = $2, "ChatBackgroundFileIds" = $3 WHERE "UserId" = $1 RETURNING {COLS}"#
    );
    Ok(sqlx::query_as::<_, UserPersonalization>(&sql)
        .bind(user_id)
        .bind(profile_poster_file_id)
        .bind(chat_background_file_ids)
        .fetch_one(pool)
        .await?)
}

pub async fn update_poster(
    pool: &PgPool,
    user_id: i64,
    profile_poster_file_id: Option<&str>,
) -> AppResult<()> {
    get_or_create(pool, user_id).await?;
    sqlx::query(r#"UPDATE "UserPersonalizations" SET "ProfilePosterFileId" = $2 WHERE "UserId" = $1"#)
        .bind(user_id)
        .bind(profile_poster_file_id)
        .execute(pool)
        .await?;
    Ok(())
}
