//! ChatFolderStorage — зеркало `Persistence/Services/ChatFolderStorage.cs`.
//! Все методы фильтруют по OwnerUserId (чужие папки невидимы/неизменяемы).

use crate::domain::ChatFolder;
use crate::errors::AppResult;
use sqlx::PgPool;
use uuid::Uuid;

const COLS: &str = r#""Id","OwnerUserId","FolderId","FolderName","FolderIcon","ChatList","SortOrder""#;

pub async fn get_by_owner(pool: &PgPool, user_id: i64) -> AppResult<Vec<ChatFolder>> {
    let sql = format!(
        r#"SELECT {COLS} FROM "ChatFolders" WHERE "OwnerUserId" = $1 ORDER BY "SortOrder", "Id""#
    );
    Ok(sqlx::query_as::<_, ChatFolder>(&sql)
        .bind(user_id)
        .fetch_all(pool)
        .await?)
}

pub async fn get_by_folder_id(
    pool: &PgPool,
    user_id: i64,
    folder_id: Uuid,
) -> AppResult<Option<ChatFolder>> {
    let sql = format!(
        r#"SELECT {COLS} FROM "ChatFolders" WHERE "OwnerUserId" = $1 AND "FolderId" = $2"#
    );
    Ok(sqlx::query_as::<_, ChatFolder>(&sql)
        .bind(user_id)
        .bind(folder_id)
        .fetch_optional(pool)
        .await?)
}

pub async fn create(
    pool: &PgPool,
    user_id: i64,
    folder_name: &str,
    folder_icon: Option<&str>,
) -> AppResult<ChatFolder> {
    let max_sort: Option<i32> =
        sqlx::query_scalar(r#"SELECT MAX("SortOrder") FROM "ChatFolders" WHERE "OwnerUserId" = $1"#)
            .bind(user_id)
            .fetch_one(pool)
            .await?;
    let sort_order = max_sort.unwrap_or(-1) + 1;
    let folder_id = Uuid::new_v4();
    let empty: Vec<Uuid> = Vec::new();

    let sql = format!(
        r#"INSERT INTO "ChatFolders" ("OwnerUserId","FolderId","FolderName","FolderIcon","ChatList","SortOrder")
           VALUES ($1,$2,$3,$4,$5,$6) RETURNING {COLS}"#
    );
    Ok(sqlx::query_as::<_, ChatFolder>(&sql)
        .bind(user_id)
        .bind(folder_id)
        .bind(folder_name)
        .bind(folder_icon)
        .bind(&empty)
        .bind(sort_order)
        .fetch_one(pool)
        .await?)
}

#[allow(clippy::too_many_arguments)]
pub async fn update(
    pool: &PgPool,
    user_id: i64,
    folder_id: Uuid,
    folder_name: Option<&str>,
    update_icon: bool,
    folder_icon: Option<&str>,
    update_chat_list: bool,
    chat_list: Option<&[Uuid]>,
) -> AppResult<Option<ChatFolder>> {
    let Some(existing) = get_by_folder_id(pool, user_id, folder_id).await? else {
        return Ok(None);
    };

    let new_name = folder_name.unwrap_or(&existing.folder_name).to_string();
    let new_icon: Option<String> = if update_icon {
        match folder_icon {
            Some(i) if !i.is_empty() => Some(i.to_string()),
            _ => None,
        }
    } else {
        existing.folder_icon.clone()
    };
    let new_list: Vec<Uuid> = if update_chat_list {
        chat_list.map(|c| c.to_vec()).unwrap_or_default()
    } else {
        existing.chat_list.clone()
    };

    let sql = format!(
        r#"UPDATE "ChatFolders" SET "FolderName" = $3, "FolderIcon" = $4, "ChatList" = $5 WHERE "OwnerUserId" = $1 AND "FolderId" = $2 RETURNING {COLS}"#
    );
    Ok(Some(
        sqlx::query_as::<_, ChatFolder>(&sql)
            .bind(user_id)
            .bind(folder_id)
            .bind(new_name)
            .bind(new_icon)
            .bind(&new_list)
            .fetch_one(pool)
            .await?,
    ))
}

pub async fn delete(pool: &PgPool, user_id: i64, folder_id: Uuid) -> AppResult<bool> {
    let res = sqlx::query(r#"DELETE FROM "ChatFolders" WHERE "OwnerUserId" = $1 AND "FolderId" = $2"#)
        .bind(user_id)
        .bind(folder_id)
        .execute(pool)
        .await?;
    Ok(res.rows_affected() > 0)
}

pub async fn add_chat(
    pool: &PgPool,
    user_id: i64,
    folder_id: Uuid,
    chat_id: Uuid,
) -> AppResult<Option<ChatFolder>> {
    let Some(mut folder) = get_by_folder_id(pool, user_id, folder_id).await? else {
        return Ok(None);
    };
    if !folder.chat_list.contains(&chat_id) {
        folder.chat_list.push(chat_id);
        sqlx::query(r#"UPDATE "ChatFolders" SET "ChatList" = $3 WHERE "OwnerUserId" = $1 AND "FolderId" = $2"#)
            .bind(user_id)
            .bind(folder_id)
            .bind(&folder.chat_list)
            .execute(pool)
            .await?;
    }
    Ok(Some(folder))
}

pub async fn remove_chat(
    pool: &PgPool,
    user_id: i64,
    folder_id: Uuid,
    chat_id: Uuid,
) -> AppResult<Option<ChatFolder>> {
    let Some(mut folder) = get_by_folder_id(pool, user_id, folder_id).await? else {
        return Ok(None);
    };
    if folder.chat_list.contains(&chat_id) {
        folder.chat_list.retain(|id| *id != chat_id);
        sqlx::query(r#"UPDATE "ChatFolders" SET "ChatList" = $3 WHERE "OwnerUserId" = $1 AND "FolderId" = $2"#)
            .bind(user_id)
            .bind(folder_id)
            .bind(&folder.chat_list)
            .execute(pool)
            .await?;
    }
    Ok(Some(folder))
}

pub async fn reorder(pool: &PgPool, user_id: i64, orders: &[(Uuid, i32)]) -> AppResult<()> {
    if orders.is_empty() {
        return Ok(());
    }
    // Чужие folder_id молча игнорируются (WHERE OwnerUserId).
    for (folder_id, sort_order) in orders {
        sqlx::query(
            r#"UPDATE "ChatFolders" SET "SortOrder" = $3 WHERE "OwnerUserId" = $1 AND "FolderId" = $2"#,
        )
        .bind(user_id)
        .bind(folder_id)
        .bind(sort_order)
        .execute(pool)
        .await?;
    }
    Ok(())
}
