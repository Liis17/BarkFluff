//! UsersStorage — зеркало `Persistence/Services/UsersStorage.cs`.

use crate::domain::{Badge, User, UserBadge};
use crate::errors::{AppError, AppResult};
use chrono::{DateTime, Utc};
use sqlx::{FromRow, PgPool, Row};

const USER_COLS: &str = r#""Id","Username","FirstName","LastName","RegistrationDate","IsDraft","ProfilePicture","ProfilePicturePreviewUrl","Bio","StorageLimitGb""#;

// Те же колонки, но с префиксом таблицы `u.` (для JOIN-запросов).
const USER_COLS_U: &str = r#"u."Id",u."Username",u."FirstName",u."LastName",u."RegistrationDate",u."IsDraft",u."ProfilePicture",u."ProfilePicturePreviewUrl",u."Bio",u."StorageLimitGb""#;

pub async fn get_user_by_username(pool: &PgPool, username: &str) -> AppResult<Option<User>> {
    let sql = format!(r#"SELECT {USER_COLS} FROM "Users" WHERE LOWER("Username") = LOWER($1)"#);
    Ok(sqlx::query_as::<_, User>(&sql)
        .bind(username)
        .fetch_optional(pool)
        .await?)
}

pub async fn get_user_by_email(pool: &PgPool, email: &str) -> AppResult<Option<User>> {
    let sql = format!(
        r#"SELECT {USER_COLS_U} FROM "Users" u JOIN "UserContacts" uc ON u."Id" = uc."UserId" WHERE LOWER(uc."Email") = LOWER($1)"#
    );
    Ok(sqlx::query_as::<_, User>(&sql)
        .bind(email)
        .fetch_optional(pool)
        .await?)
}

pub async fn get_by_id(pool: &PgPool, id: i64) -> AppResult<Option<User>> {
    let sql = format!(r#"SELECT {USER_COLS} FROM "Users" WHERE "Id" = $1"#);
    Ok(sqlx::query_as::<_, User>(&sql)
        .bind(id)
        .fetch_optional(pool)
        .await?)
}

pub async fn get_by_ids(pool: &PgPool, ids: &[i64]) -> AppResult<Vec<User>> {
    let sql = format!(r#"SELECT {USER_COLS} FROM "Users" WHERE "Id" = ANY($1)"#);
    Ok(sqlx::query_as::<_, User>(&sql)
        .bind(ids)
        .fetch_all(pool)
        .await?)
}

pub async fn get_contact_email(pool: &PgPool, user_id: i64) -> AppResult<Option<String>> {
    let row = sqlx::query(r#"SELECT "Email" FROM "UserContacts" WHERE "UserId" = $1"#)
        .bind(user_id)
        .fetch_optional(pool)
        .await?;
    Ok(row.map(|r| r.get::<String, _>("Email")))
}

#[derive(FromRow)]
struct TrigramRow {
    #[sqlx(flatten)]
    user: User,
    total_count: i64,
}

/// Trigram-поиск (pg_trgm). Эквивалент `SearchUsersByTrigram`.
/// Возвращает (пользователи, общий счёт) одним запросом через COUNT(*) OVER().
pub async fn search_users_by_trigram(
    pool: &PgPool,
    search_term: &str,
    skip: i32,
    page_size: i32,
    similarity_threshold: f64,
    current_user_id: Option<i64>,
    respect_search_visibility: bool,
) -> AppResult<(Vec<User>, i32)> {
    if search_term.trim().is_empty() {
        return Ok((Vec::new(), 0));
    }
    let term = search_term.trim();

    let privacy_filter = if respect_search_visibility {
        r#" AND (p."SearchVisible" IS NULL OR p."SearchVisible" = true OR u."Id" = $5)"#
    } else {
        ""
    };

    let sql = format!(
        r#"
        SELECT u."Id", u."FirstName", u."LastName", u."Username", u."RegistrationDate",
               u."ProfilePicture", u."ProfilePicturePreviewUrl", u."Bio", u."IsDraft", u."StorageLimitGb",
               COUNT(*) OVER() AS total_count
        FROM "Users" u
        LEFT JOIN "UserContacts" uc ON u."Id" = uc."UserId"
        LEFT JOIN "Privacies" p ON u."Id" = p."UserId"
        WHERE (similarity(u."FirstName", $1) > $2
           OR similarity(u."LastName", $1) > $2
           OR similarity(u."Username", $1) > $2)
        AND u."IsDraft" = false{privacy_filter}
        ORDER BY GREATEST(
            similarity(u."FirstName", $1),
            similarity(u."LastName", $1),
            similarity(u."Username", $1)
        ) DESC
        LIMIT $3 OFFSET $4
    "#
    );

    let mut q = sqlx::query_as::<_, TrigramRow>(&sql)
        .bind(term)
        .bind(similarity_threshold)
        .bind(page_size as i64)
        .bind(skip as i64);
    if respect_search_visibility {
        q = q.bind(current_user_id);
    }
    let rows = q.fetch_all(pool).await?;

    let total = rows.first().map(|r| r.total_count as i32).unwrap_or(0);
    let users = rows.into_iter().map(|r| r.user).collect();
    Ok((users, total))
}

/// Все неподтверждённые исключаются; сортировка по Id убыванию. (`GetAllUsersDescending`)
pub async fn get_all_users_descending(
    pool: &PgPool,
    offset: i32,
    size: i32,
) -> AppResult<(Vec<User>, i32)> {
    let total: i64 = sqlx::query_scalar(r#"SELECT COUNT(*) FROM "Users" WHERE "IsDraft" = false"#)
        .fetch_one(pool)
        .await?;
    let sql = format!(
        r#"SELECT {USER_COLS} FROM "Users" WHERE "IsDraft" = false ORDER BY "Id" DESC LIMIT $1 OFFSET $2"#
    );
    let users = sqlx::query_as::<_, User>(&sql)
        .bind(size as i64)
        .bind(offset as i64)
        .fetch_all(pool)
        .await?;
    Ok((users, total as i32))
}

/// Создаёт draft-пользователя + контакт. Id = UnixTimeMilliseconds.
/// 23505 по индексу LOWER(Email)/LOWER(Username) → EmailExist/UsernameExist.
pub async fn create_user(
    pool: &PgPool,
    username: &str,
    first_name: &str,
    last_name: &str,
    email: &str,
) -> AppResult<User> {
    let id = Utc::now().timestamp_millis();
    let now: DateTime<Utc> = Utc::now();

    let mut tx = pool.begin().await?;

    let insert = async {
        sqlx::query(
            r#"INSERT INTO "Users" ("Id","Username","FirstName","LastName","RegistrationDate","IsDraft","StorageLimitGb")
               VALUES ($1,$2,$3,$4,$5,true,5)"#,
        )
        .bind(id)
        .bind(username)
        .bind(first_name)
        .bind(last_name)
        .bind(now)
        .execute(&mut *tx)
        .await?;

        sqlx::query(r#"INSERT INTO "UserContacts" ("Email","UserId") VALUES ($1,$2)"#)
            .bind(email)
            .bind(id)
            .execute(&mut *tx)
            .await?;
        Ok::<(), sqlx::Error>(())
    }
    .await;

    match insert {
        Ok(()) => {
            tx.commit().await?;
            Ok(User {
                id,
                username: username.to_string(),
                first_name: first_name.to_string(),
                last_name: last_name.to_string(),
                registration_date: now,
                is_draft: true,
                profile_picture: None,
                profile_picture_preview_url: None,
                bio: None,
                storage_limit_gb: 5,
            })
        }
        Err(sqlx::Error::Database(db)) if db.code().as_deref() == Some("23505") => {
            let _ = tx.rollback().await;
            let constraint = db.constraint().unwrap_or("").to_ascii_lowercase();
            if constraint.contains("email") {
                Err(AppError::EmailExist)
            } else if constraint.contains("username") {
                Err(AppError::UsernameExist)
            } else {
                Err(AppError::System(format!("unique violation: {constraint}")))
            }
        }
        Err(e) => {
            let _ = tx.rollback().await;
            Err(e.into())
        }
    }
}

/// OverrideDraftUser: сбрасывает ProfilePicture, IsDraft=true, RegistrationDate=now;
/// обновляет имя/фамилию/username/email. Возвращает user_id или UserNotFound.
pub async fn override_draft_user(
    pool: &PgPool,
    username: &str,
    email: &str,
    first_name: &str,
    last_name: &str,
) -> AppResult<i64> {
    // Поиск по email, затем по username (как в OverrideDraftUserCommandHandler).
    let user = match get_user_by_email(pool, email).await? {
        Some(u) => Some(u),
        None => get_user_by_username(pool, username).await?,
    };
    let Some(user) = user else {
        return Err(AppError::UserNotFound);
    };

    let now = Utc::now();
    let mut tx = pool.begin().await?;
    sqlx::query(
        r#"UPDATE "Users" SET "FirstName" = $2, "LastName" = $3, "Username" = $4, "ProfilePicture" = NULL, "RegistrationDate" = $5, "IsDraft" = true WHERE "Id" = $1"#,
    )
    .bind(user.id)
    .bind(first_name)
    .bind(last_name)
    .bind(username)
    .bind(now)
    .execute(&mut *tx)
    .await?;
    sqlx::query(r#"UPDATE "UserContacts" SET "Email" = $2 WHERE "UserId" = $1"#)
        .bind(user.id)
        .bind(email)
        .execute(&mut *tx)
        .await?;
    tx.commit().await?;

    Ok(user.id)
}

pub async fn change_draft_status(pool: &PgPool, user_id: i64, is_draft: bool) -> AppResult<()> {
    let res = sqlx::query(r#"UPDATE "Users" SET "IsDraft" = $2 WHERE "Id" = $1"#)
        .bind(user_id)
        .bind(is_draft)
        .execute(pool)
        .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::UserNotFound);
    }
    Ok(())
}

pub async fn update_profile_picture(
    pool: &PgPool,
    user_id: i64,
    url: &str,
    preview: &str,
) -> AppResult<()> {
    let res = sqlx::query(
        r#"UPDATE "Users" SET "ProfilePicture" = $2, "ProfilePicturePreviewUrl" = $3 WHERE "Id" = $1"#,
    )
    .bind(user_id)
    .bind(url)
    .bind(preview)
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::UserNotFound);
    }
    Ok(())
}

pub async fn change_name(pool: &PgPool, user_id: i64, first: &str, last: &str) -> AppResult<()> {
    let res = sqlx::query(r#"UPDATE "Users" SET "FirstName" = $2, "LastName" = $3 WHERE "Id" = $1"#)
        .bind(user_id)
        .bind(first)
        .bind(last)
        .execute(pool)
        .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::UserNotFound);
    }
    Ok(())
}

pub async fn change_username(pool: &PgPool, user_id: i64, username: &str) -> AppResult<()> {
    let res = sqlx::query(r#"UPDATE "Users" SET "Username" = $2 WHERE "Id" = $1"#)
        .bind(user_id)
        .bind(username)
        .execute(pool)
        .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::UserNotFound);
    }
    Ok(())
}

pub async fn change_bio(pool: &PgPool, user_id: i64, bio: &str) -> AppResult<()> {
    let res = sqlx::query(r#"UPDATE "Users" SET "Bio" = $2 WHERE "Id" = $1"#)
        .bind(user_id)
        .bind(bio)
        .execute(pool)
        .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::UserNotFound);
    }
    Ok(())
}

pub async fn update_storage_limit_gb(pool: &PgPool, user_id: i64, limit_gb: i32) -> AppResult<()> {
    let res = sqlx::query(r#"UPDATE "Users" SET "StorageLimitGb" = $2 WHERE "Id" = $1"#)
        .bind(user_id)
        .bind(limit_gb)
        .execute(pool)
        .await?;
    if res.rows_affected() == 0 {
        return Err(AppError::UserNotFound);
    }
    Ok(())
}

// UpdateProfileServer реализован в features через условные change_name/username/bio
// (как в .NET-хендлере), поэтому отдельного bulk-метода в storage нет.

// ---- Бейджи -----------------------------------------------------------------

#[derive(FromRow)]
struct UserBadgeRow {
    #[sqlx(rename = "Id")]
    id: i32,
    #[sqlx(rename = "Name")]
    name: String,
    #[sqlx(rename = "Description")]
    description: Option<String>,
    #[sqlx(rename = "ImageUrl")]
    image_url: String,
    #[sqlx(rename = "CreatedDate")]
    created_date: DateTime<Utc>,
    #[sqlx(rename = "IsActive")]
    is_active: bool,
    #[sqlx(rename = "Priority")]
    priority: i32,
    #[sqlx(rename = "AssignedDate")]
    assigned_date: DateTime<Utc>,
    #[sqlx(rename = "OwnerId")]
    owner_id: i64,
}

impl UserBadgeRow {
    fn into_domain(self) -> (i64, UserBadge) {
        (
            self.owner_id,
            UserBadge {
                badge: Badge {
                    id: self.id,
                    name: self.name,
                    description: self.description,
                    image_url: self.image_url,
                    created_date: self.created_date,
                    is_active: self.is_active,
                },
                priority: self.priority,
                assigned_date: self.assigned_date,
            },
        )
    }
}

const USER_BADGE_SELECT: &str = r#"
    SELECT b."Id", b."Name", b."Description", b."ImageUrl", b."CreatedDate", b."IsActive",
           ub."Priority", ub."AssignedDate", ub."UserId" AS "OwnerId"
    FROM "UserBadges" ub
    JOIN "Badges" b ON ub."BadgeId" = b."Id"
"#;

pub async fn get_user_badges(
    pool: &PgPool,
    user_id: i64,
    limit: Option<i32>,
) -> AppResult<Vec<UserBadge>> {
    let mut sql = format!(
        r#"{USER_BADGE_SELECT} WHERE ub."UserId" = $1 AND b."IsActive" = true ORDER BY ub."Priority", ub."AssignedDate""#
    );
    if limit.is_some() {
        sql.push_str(" LIMIT $2");
    }
    let mut q = sqlx::query_as::<_, UserBadgeRow>(&sql).bind(user_id);
    if let Some(l) = limit {
        q = q.bind(l as i64);
    }
    let rows = q.fetch_all(pool).await?;
    Ok(rows.into_iter().map(|r| r.into_domain().1).collect())
}

pub async fn get_badges_for_users(
    pool: &PgPool,
    user_ids: &[i64],
) -> AppResult<std::collections::HashMap<i64, Vec<UserBadge>>> {
    let mut map: std::collections::HashMap<i64, Vec<UserBadge>> = std::collections::HashMap::new();
    if user_ids.is_empty() {
        return Ok(map);
    }
    let sql = format!(
        r#"{USER_BADGE_SELECT} WHERE ub."UserId" = ANY($1) AND b."IsActive" = true ORDER BY ub."Priority", ub."AssignedDate""#
    );
    let rows = sqlx::query_as::<_, UserBadgeRow>(&sql)
        .bind(user_ids)
        .fetch_all(pool)
        .await?;
    for row in rows {
        let (owner, ub) = row.into_domain();
        map.entry(owner).or_default().push(ub);
    }
    Ok(map)
}

pub async fn assign_badge(
    pool: &PgPool,
    user_id: i64,
    badge_id: i32,
    priority: i32,
) -> AppResult<UserBadge> {
    let now = Utc::now();
    sqlx::query(
        r#"INSERT INTO "UserBadges" ("UserId","BadgeId","Priority","AssignedDate") VALUES ($1,$2,$3,$4)"#,
    )
    .bind(user_id)
    .bind(badge_id)
    .bind(priority)
    .bind(now)
    .execute(pool)
    .await?;

    let badge = get_badge_by_id(pool, badge_id)
        .await?
        .ok_or_else(|| AppError::System("badge not found after assign".into()))?;
    Ok(UserBadge {
        badge,
        priority,
        assigned_date: now,
    })
}

pub async fn remove_badge(pool: &PgPool, user_id: i64, badge_id: i32) -> AppResult<bool> {
    let res = sqlx::query(r#"DELETE FROM "UserBadges" WHERE "UserId" = $1 AND "BadgeId" = $2"#)
        .bind(user_id)
        .bind(badge_id)
        .execute(pool)
        .await?;
    Ok(res.rows_affected() > 0)
}

pub async fn update_user_badge_priority(
    pool: &PgPool,
    user_id: i64,
    badge_id: i32,
    new_priority: i32,
) -> AppResult<Option<UserBadge>> {
    let res = sqlx::query(
        r#"UPDATE "UserBadges" SET "Priority" = $3 WHERE "UserId" = $1 AND "BadgeId" = $2"#,
    )
    .bind(user_id)
    .bind(badge_id)
    .bind(new_priority)
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Ok(None);
    }
    // Загружаем актуальные priority/assigned_date.
    let sql = format!(
        r#"{USER_BADGE_SELECT} WHERE ub."UserId" = $1 AND ub."BadgeId" = $2"#
    );
    let row = sqlx::query_as::<_, UserBadgeRow>(&sql)
        .bind(user_id)
        .bind(badge_id)
        .fetch_optional(pool)
        .await?;
    Ok(row.map(|r| r.into_domain().1))
}

async fn get_badge_by_id(pool: &PgPool, id: i32) -> AppResult<Option<Badge>> {
    Ok(sqlx::query_as::<_, Badge>(
        r#"SELECT "Id","Name","Description","ImageUrl","CreatedDate","IsActive" FROM "Badges" WHERE "Id" = $1"#,
    )
    .bind(id)
    .fetch_optional(pool)
    .await?)
}

pub async fn create_badge(
    pool: &PgPool,
    name: &str,
    description: Option<&str>,
    image_url: &str,
) -> AppResult<Badge> {
    let now = Utc::now();
    let id: i32 = sqlx::query_scalar(
        r#"INSERT INTO "Badges" ("Name","Description","ImageUrl","CreatedDate","IsActive")
           VALUES ($1,$2,$3,$4,true) RETURNING "Id""#,
    )
    .bind(name)
    .bind(description)
    .bind(image_url)
    .bind(now)
    .fetch_one(pool)
    .await?;
    Ok(Badge {
        id,
        name: name.to_string(),
        description: description.map(|s| s.to_string()),
        image_url: image_url.to_string(),
        created_date: now,
        is_active: true,
    })
}

pub async fn get_all_badges(pool: &PgPool, include_inactive: bool) -> AppResult<Vec<Badge>> {
    let sql = if include_inactive {
        r#"SELECT "Id","Name","Description","ImageUrl","CreatedDate","IsActive" FROM "Badges" ORDER BY "Name""#
    } else {
        r#"SELECT "Id","Name","Description","ImageUrl","CreatedDate","IsActive" FROM "Badges" WHERE "IsActive" = true ORDER BY "Name""#
    };
    Ok(sqlx::query_as::<_, Badge>(sql).fetch_all(pool).await?)
}

pub async fn update_badge(
    pool: &PgPool,
    id: i32,
    name: &str,
    description: Option<&str>,
    image_url: &str,
    is_active: bool,
) -> AppResult<Option<Badge>> {
    let res = sqlx::query(
        r#"UPDATE "Badges" SET "Name" = $2, "Description" = $3, "ImageUrl" = $4, "IsActive" = $5 WHERE "Id" = $1"#,
    )
    .bind(id)
    .bind(name)
    .bind(description)
    .bind(image_url)
    .bind(is_active)
    .execute(pool)
    .await?;
    if res.rows_affected() == 0 {
        return Ok(None);
    }
    get_badge_by_id(pool, id).await
}

pub async fn delete_badge(pool: &PgPool, id: i32) -> AppResult<bool> {
    let res = sqlx::query(r#"DELETE FROM "Badges" WHERE "Id" = $1"#)
        .bind(id)
        .execute(pool)
        .await?;
    Ok(res.rows_affected() > 0)
}
