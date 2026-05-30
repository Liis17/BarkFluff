//! Бизнес-логика (эквивалент MediatR-хендлеров). Функции принимают `&AppState`
//! и (где нужно) `&UserContext`; возвращают `AppResult<ProtoResponse>`.

use crate::app::AppState;
use crate::auth::UserContext;
use crate::errors::{AppError, AppResult};
use crate::mapping;
use crate::persistence::{chat_folders, devices, personalization, prekeys, privacy, users};
use crate::proto::barkfluff::files::UploadFileType;
use crate::proto::barkfluff::users as pb;
use crate::services::username_format_valid;
use serde_json::json;
use uuid::Uuid;

fn client_err(metric: &str, state: &AppState, e: tonic::Status) -> AppError {
    state.metrics.increment(metric);
    AppError::System(format!("внешний сервис: {}", e.message()))
}

fn ts_to_string(ts: &Option<prost_types::Timestamp>) -> String {
    match ts {
        Some(t) => chrono::DateTime::from_timestamp(t.seconds, t.nanos.max(0) as u32)
            .map(|d| d.to_rfc3339())
            .unwrap_or_default(),
        None => String::new(),
    }
}

// ===== Пользователи =========================================================

pub async fn get_user(
    state: &AppState,
    ctx: &UserContext,
    user_id: Option<i64>,
) -> AppResult<pb::GetUserResponse> {
    let uid = user_id.unwrap_or(ctx.user_id);
    let user = users::get_by_id(&state.pool, uid)
        .await?
        .ok_or(AppError::UserNotFound)?;
    let personalization = personalization::get(&state.pool, uid).await?;
    let mut g = mapping::user_to_proto(&user);
    g.profile_poster_file_id = personalization
        .and_then(|p| p.profile_poster_file_id)
        .unwrap_or_default();
    Ok(pb::GetUserResponse { user: Some(g) })
}

pub async fn find_by_login(
    state: &AppState,
    username: Option<String>,
    email: Option<String>,
) -> AppResult<pb::FindByLoginResponse> {
    let mut user = None;
    if let Some(u) = username.as_deref().filter(|s| !s.is_empty()) {
        user = users::get_user_by_username(&state.pool, u).await?;
    }
    if let Some(e) = email.as_deref().filter(|s| !s.is_empty()) {
        user = users::get_user_by_email(&state.pool, e).await?;
    }
    let user = user.ok_or(AppError::UserNotFound)?;
    Ok(pb::FindByLoginResponse {
        user: Some(mapping::user_to_proto(&user)),
    })
}

pub async fn list_by_ids(state: &AppState, ids: Vec<i64>) -> AppResult<pb::ListByIdsResponse> {
    let users = users::get_by_ids(&state.pool, &ids).await?;
    Ok(pb::ListByIdsResponse {
        users: users.iter().map(mapping::user_to_proto).collect(),
    })
}

pub async fn get_user_contacts(
    state: &AppState,
    user_id: i64,
) -> AppResult<pb::GetUserContactsResponse> {
    let user = users::get_by_id(&state.pool, user_id)
        .await?
        .ok_or(AppError::UserNotFound)?;
    let email = users::get_contact_email(&state.pool, user_id)
        .await?
        .unwrap_or_default();
    Ok(pb::GetUserContactsResponse {
        user: Some(mapping::user_to_proto(&user)),
        contact: Some(pb::UserContact { email }),
    })
}

pub async fn check_exist_username(
    state: &AppState,
    username: &str,
) -> AppResult<pb::CheckExistResponse> {
    let username = username.trim();
    if state.reserved.is_reserved(username) {
        return Ok(pb::CheckExistResponse { exist: true });
    }
    let exist = matches!(
        users::get_user_by_username(&state.pool, username).await?,
        Some(u) if !u.is_draft
    );
    Ok(pb::CheckExistResponse { exist })
}

pub async fn check_exist_email(state: &AppState, email: &str) -> AppResult<pb::CheckExistResponse> {
    let exist = matches!(
        users::get_user_by_email(&state.pool, email.trim()).await?,
        Some(u) if !u.is_draft
    );
    Ok(pb::CheckExistResponse { exist })
}

pub async fn add_draft_user(
    state: &AppState,
    username: &str,
    email: &str,
    first_name: &str,
    last_name: &str,
) -> AppResult<pb::AddDraftUserResponse> {
    let username = username.trim();
    let email = email.trim();
    let first_name = first_name.trim();
    let last_name = last_name.trim();

    if !username_format_valid(username) {
        return Err(AppError::UsernameInvalidFormat);
    }

    if let Some(u) = users::get_user_by_email(&state.pool, email).await? {
        state.metrics.increment("users_email_conflicts");
        return Err(if u.is_draft {
            AppError::UserIsDraft
        } else {
            AppError::EmailExist
        });
    }

    if state.reserved.is_reserved(username) {
        state.metrics.increment("users_reserved_username_blocked");
        return Err(AppError::UsernameReserved);
    }

    if let Some(u) = users::get_user_by_username(&state.pool, username).await? {
        state.metrics.increment("users_username_conflicts");
        return Err(if u.is_draft {
            AppError::UserIsDraft
        } else {
            AppError::UsernameExist
        });
    }

    let user = users::create_user(&state.pool, username, first_name, last_name, email).await?;
    Ok(pb::AddDraftUserResponse { user_id: user.id })
}

pub async fn override_draft_user(
    state: &AppState,
    username: &str,
    email: &str,
    first_name: &str,
    last_name: &str,
) -> AppResult<pb::AddDraftUserResponse> {
    let user_id = users::override_draft_user(
        &state.pool,
        username.trim(),
        email.trim(),
        first_name.trim(),
        last_name.trim(),
    )
    .await?;
    Ok(pb::AddDraftUserResponse { user_id })
}

pub async fn confirm_user(state: &AppState, user_id: i64) -> AppResult<()> {
    users::get_by_id(&state.pool, user_id)
        .await?
        .ok_or(AppError::UserNotFound)?;
    users::change_draft_status(&state.pool, user_id, false).await?;
    privacy::get_or_create(&state.pool, user_id).await?;
    Ok(())
}

pub async fn change_name(
    state: &AppState,
    ctx: &UserContext,
    first_name: &str,
    last_name: &str,
) -> AppResult<()> {
    users::change_name(&state.pool, ctx.user_id, first_name.trim(), last_name.trim()).await?;
    state
        .publisher
        .name_changed(ctx.user_id, first_name.trim(), last_name.trim())
        .await;
    Ok(())
}

pub async fn change_username(state: &AppState, ctx: &UserContext, username: &str) -> AppResult<()> {
    let username = username.trim();
    if !username_format_valid(username) {
        return Err(AppError::UsernameInvalidFormat);
    }
    if state.reserved.is_reserved(username) {
        return Err(AppError::UsernameReserved);
    }
    if let Some(existing) = users::get_user_by_username(&state.pool, username).await? {
        if existing.id != ctx.user_id {
            return Err(AppError::UsernameExist);
        }
    }
    users::change_username(&state.pool, ctx.user_id, username).await?;
    state.publisher.username_changed(ctx.user_id, username).await;
    Ok(())
}

pub async fn change_bio(state: &AppState, ctx: &UserContext, bio: &str) -> AppResult<()> {
    if bio.chars().count() > 200 {
        return Err(AppError::BioTooLong);
    }
    users::change_bio(&state.pool, ctx.user_id, bio).await?;
    state.publisher.bio_changed(ctx.user_id, bio).await;
    Ok(())
}

pub async fn set_profile_picture(
    state: &AppState,
    ctx: &UserContext,
    file_id: Option<Uuid>,
) -> AppResult<()> {
    let mut file_url = String::new();
    let mut preview_url = String::new();

    if let Some(fid) = file_id {
        let resp = state
            .clients
            .get_file_data(&fid.to_string())
            .await
            .map_err(|e| client_err("files_fetch_errors", state, e))?;
        state.metrics.increment("files_fetch_success");

        let info = resp.file_info.unwrap_or_default();
        if info.r#type != UploadFileType::UserAvatar as i32 {
            return Err(AppError::ProfilePictureHasNotValidType);
        }
        file_url = info.file_url;
        preview_url = info.preview_url;
    }

    users::update_profile_picture(&state.pool, ctx.user_id, &file_url, &preview_url).await?;
    state
        .publisher
        .avatar_changed(ctx.user_id, &file_url, &preview_url)
        .await;
    Ok(())
}

pub async fn set_profile_picture_server(
    state: &AppState,
    user_id: i64,
    url: &str,
    preview: &str,
) -> AppResult<()> {
    users::update_profile_picture(&state.pool, user_id, url, preview).await?;
    state.publisher.avatar_changed(user_id, url, preview).await;
    Ok(())
}

pub async fn update_storage_limit(
    state: &AppState,
    user_id: i64,
    storage_limit_gb: i32,
) -> AppResult<pb::UpdateStorageLimitResponse> {
    users::update_storage_limit_gb(&state.pool, user_id, storage_limit_gb).await?;
    let user = users::get_by_id(&state.pool, user_id)
        .await?
        .ok_or(AppError::UserNotFound)?;
    Ok(pb::UpdateStorageLimitResponse {
        user: Some(mapping::user_to_proto(&user)),
    })
}

pub async fn update_profile_server(
    state: &AppState,
    user_id: i64,
    first_name: &str,
    last_name: &str,
    bio: &str,
    username: &str,
) -> AppResult<()> {
    let current = users::get_by_id(&state.pool, user_id)
        .await?
        .ok_or_else(|| AppError::System(format!("Пользователь {user_id} не найден")))?;

    let first_name = first_name.trim();
    let last_name = last_name.trim();
    let username = username.trim();
    let bio = bio.trim();

    let name_changed =
        !first_name.is_empty() && (first_name != current.first_name || last_name != current.last_name);
    let username_changed = !username.is_empty() && username != current.username;
    let bio_changed = bio != current.bio.clone().unwrap_or_default();

    if name_changed {
        users::change_name(&state.pool, user_id, first_name, last_name).await?;
        state.publisher.name_changed(user_id, first_name, last_name).await;
    }
    if username_changed {
        users::change_username(&state.pool, user_id, username).await?;
        state.publisher.username_changed(user_id, username).await;
    }
    if bio_changed {
        users::change_bio(&state.pool, user_id, bio).await?;
        state.publisher.bio_changed(user_id, bio).await;
    }
    Ok(())
}

pub async fn search_users(
    state: &AppState,
    ctx: &UserContext,
    query: &str,
    skip: i32,
    size: i32,
) -> AppResult<pb::SearchUsersResponse> {
    if size == 0 {
        return Ok(pb::SearchUsersResponse::default());
    }
    let size = if size > 50 { 50 } else { size };
    let (found, total) = users::search_users_by_trigram(
        &state.pool,
        query,
        skip,
        size,
        0.3,
        Some(ctx.user_id),
        true,
    )
    .await?;
    Ok(pb::SearchUsersResponse {
        users: found.iter().map(mapping::user_to_proto).collect(),
        total_count: total.max(0),
    })
}

pub async fn search_users_server(
    state: &AppState,
    query: &str,
    offset: i32,
    size: i32,
) -> AppResult<pb::SearchUsersServerResponse> {
    if size <= 0 {
        return Ok(pb::SearchUsersServerResponse::default());
    }
    let size = if size > 50 { 50 } else { size };

    let (found, total) = if query.trim().is_empty() {
        users::get_all_users_descending(&state.pool, offset, size).await?
    } else if let Ok(uid) = query.trim().parse::<i64>() {
        match users::get_by_id(&state.pool, uid).await? {
            Some(u) if !u.is_draft => (vec![u], 1),
            _ => return Ok(pb::SearchUsersServerResponse { users: vec![], total_count: 0 }),
        }
    } else {
        let (u, t) =
            users::search_users_by_trigram(&state.pool, query, offset, size, 0.3, None, false)
                .await?;
        (u, t.max(0))
    };

    let ids: Vec<i64> = found.iter().map(|u| u.id).collect();
    let badges_by_user = users::get_badges_for_users(&state.pool, &ids).await?;

    let proto_users = found
        .iter()
        .map(|u| {
            let mut pu = mapping::user_to_proto(u);
            if let Some(badges) = badges_by_user.get(&u.id) {
                pu.badges = badges.iter().map(mapping::user_badge_to_proto).collect();
            }
            pu
        })
        .collect();

    Ok(pb::SearchUsersServerResponse {
        users: proto_users,
        total_count: total,
    })
}

pub async fn get_user_by_username(
    state: &AppState,
    username: &str,
) -> AppResult<pb::GetUserByUsernameResponse> {
    let user = match users::get_user_by_username(&state.pool, username.trim()).await? {
        Some(u) if !u.is_draft => u,
        _ => {
            state.metrics.increment("public_profile_not_found");
            return Ok(pb::GetUserByUsernameResponse {
                found: false,
                ..Default::default()
            });
        }
    };

    let p = privacy::get_or_create(&state.pool, user.id).await?;
    if !p.profile_visible_on_site {
        state.metrics.increment("public_profile_hidden");
        return Ok(pb::GetUserByUsernameResponse {
            found: false,
            ..Default::default()
        });
    }

    let bio = if p.bio_visibility == crate::domain::VISIBILITY_ALL {
        user.bio.clone().unwrap_or_default()
    } else {
        String::new()
    };
    let avatar = if p.avatar_visibility == crate::domain::VISIBILITY_ALL {
        user.profile_picture.clone().unwrap_or_default()
    } else {
        String::new()
    };

    let mut poster_url = String::new();
    if let Some(pers) = personalization::get(&state.pool, user.id).await? {
        if let Some(poster_id) = pers.profile_poster_file_id.filter(|s| !s.is_empty()) {
            match state.clients.get_file_data(&poster_id).await {
                Ok(resp) => {
                    poster_url = resp.file_info.map(|i| i.file_url).unwrap_or_default();
                    state.metrics.increment("files_fetch_success");
                }
                Err(_) => {
                    state.metrics.increment("files_fetch_errors");
                }
            }
        }
    }

    Ok(pb::GetUserByUsernameResponse {
        found: true,
        first_name: user.first_name,
        last_name: user.last_name,
        username: user.username,
        bio,
        profile_picture: avatar,
        profile_poster_url: poster_url,
    })
}

pub async fn export_data(state: &AppState, user_id: i64) -> AppResult<pb::ExportDataResponse> {
    let mut files: Vec<pb::JsonFile> = Vec::new();

    let user = match users::get_by_id(&state.pool, user_id).await? {
        Some(u) => u,
        None => return Ok(pb::ExportDataResponse { files }),
    };

    // 1. profile.json
    let profile = json!({
        "id": user.id,
        "username": user.username,
        "firstName": user.first_name,
        "lastName": user.last_name,
        "registrationDate": user.registration_date.to_rfc3339(),
        "profilePicture": user.profile_picture,
        "bio": user.bio,
        "storageLimitGb": user.storage_limit_gb,
    });
    files.push(pb::JsonFile {
        filename: "profile.json".into(),
        content: serde_json::to_string_pretty(&profile).unwrap_or_default(),
    });

    // 2. messages.json
    let messages_response = match state.clients.get_user_all_messages(user_id).await {
        Ok(resp) => {
            state.metrics.increment("messages_fetch_success");
            let data = json!({
                "messages": resp.messages.iter().map(|m| json!({
                    "id": m.id,
                    "chatId": m.chat_id,
                    "senderId": m.sender_id,
                    "sentAt": ts_to_string(&m.sent_at),
                    "text": m.text,
                    "contentType": m.content_type,
                    "readBy": m.read_by,
                    "attachments": m.attachments.iter().map(|a| json!({
                        "id": a.id,
                        "type": a.r#type,
                        "fileId": a.file_id,
                        "previewUrl": a.preview_url,
                        "fileSize": a.attachment_size,
                        "previewFileId": a.preview_file_id,
                        "fileName": a.file_name,
                    })).collect::<Vec<_>>(),
                })).collect::<Vec<_>>(),
                "chats": resp.chats.iter().map(|c| json!({
                    "id": c.id,
                    "title": c.title,
                    "isGroupChat": c.is_group_chat,
                    "memberIds": c.member_ids,
                })).collect::<Vec<_>>(),
            });
            files.push(pb::JsonFile {
                filename: "messages.json".into(),
                content: serde_json::to_string_pretty(&data).unwrap_or_default(),
            });
            Some(resp)
        }
        Err(e) => {
            state.metrics.increment("messages_fetch_errors");
            files.push(pb::JsonFile {
                filename: "messages_error.json".into(),
                content: serde_json::to_string_pretty(&json!({ "error": e.message() }))
                    .unwrap_or_default(),
            });
            None
        }
    };

    // 3. files.json
    let mut file_ids: std::collections::HashSet<String> = std::collections::HashSet::new();
    if let Some(resp) = &messages_response {
        for m in &resp.messages {
            for a in &m.attachments {
                if !a.file_id.is_empty() {
                    file_ids.insert(a.file_id.clone());
                }
                if !a.preview_file_id.is_empty() {
                    file_ids.insert(a.preview_file_id.clone());
                }
            }
        }
    }
    if let Some(pp) = user.profile_picture.as_deref().filter(|s| !s.is_empty()) {
        file_ids.insert(pp.to_string());
    }
    if let Some(pv) = user
        .profile_picture_preview_url
        .as_deref()
        .filter(|s| !s.is_empty())
    {
        file_ids.insert(pv.to_string());
    }

    if file_ids.is_empty() {
        files.push(pb::JsonFile {
            filename: "files.json".into(),
            content: serde_json::to_string_pretty(&json!({ "files": [] })).unwrap_or_default(),
        });
    } else {
        match state
            .clients
            .get_files_data(file_ids.into_iter().collect())
            .await
        {
            Ok(resp) => {
                state.metrics.increment("files_fetch_success");
                let data = json!({
                    "files": resp.files_infos.iter().map(|f| json!({
                        "id": f.id,
                        "fileName": f.file_name,
                        "fileSize": f.file_size,
                        "type": f.r#type,
                        "createdAt": ts_to_string(&f.created_at),
                        "uploadedAt": ts_to_string(&f.uploaded_at),
                        "etag": f.etag,
                        "fileUrl": f.file_url,
                        "previewUrl": f.preview_url,
                        "previewFileId": f.preview_file_id,
                        "uploaders": f.uploaders,
                    })).collect::<Vec<_>>(),
                });
                files.push(pb::JsonFile {
                    filename: "files.json".into(),
                    content: serde_json::to_string_pretty(&data).unwrap_or_default(),
                });
            }
            Err(e) => {
                state.metrics.increment("files_fetch_errors");
                files.push(pb::JsonFile {
                    filename: "files_error.json".into(),
                    content: serde_json::to_string_pretty(&json!({ "error": e.message() }))
                        .unwrap_or_default(),
                });
            }
        }
    }

    Ok(pb::ExportDataResponse { files })
}

// ===== Устройства ===========================================================

pub async fn register_device(
    state: &AppState,
    device_id: Uuid,
    user_id: i64,
    original_name: &str,
    app_name: &str,
    operation_system: &str,
    location: &str,
) -> AppResult<pb::RegisterDeviceResponse> {
    let opt = |s: &str| if s.is_empty() { None } else { Some(s.to_string()) };
    let device = devices::register_or_update_device(
        &state.pool,
        device_id,
        user_id,
        original_name,
        opt(app_name).as_deref(),
        opt(operation_system).as_deref(),
        opt(location).as_deref(),
    )
    .await?;
    Ok(pb::RegisterDeviceResponse {
        device: Some(mapping::device_to_proto(&device, false)),
    })
}

pub async fn get_devices(state: &AppState, ctx: &UserContext) -> AppResult<pb::GetDevicesResponse> {
    let list = devices::get_devices_by_user_id(&state.pool, ctx.user_id).await?;
    Ok(pb::GetDevicesResponse {
        devices: list.iter().map(|d| mapping::device_to_proto(d, false)).collect(),
    })
}

pub async fn get_current_device(
    state: &AppState,
    ctx: &UserContext,
) -> AppResult<pb::GetCurrentDeviceResponse> {
    let Some(device_guid) = ctx.device_id.as_deref().and_then(|d| Uuid::parse_str(d).ok()) else {
        return Ok(pb::GetCurrentDeviceResponse::default());
    };
    match devices::get_device_by_id(&state.pool, device_guid, ctx.user_id).await? {
        Some(d) => Ok(pb::GetCurrentDeviceResponse {
            device: Some(mapping::device_to_proto(&d, true)),
        }),
        None => Ok(pb::GetCurrentDeviceResponse::default()),
    }
}

pub async fn get_user_devices(
    state: &AppState,
    user_id: i64,
) -> AppResult<pb::GetUserDevicesResponse> {
    let list = devices::get_devices_by_user_id(&state.pool, user_id).await?;
    Ok(pb::GetUserDevicesResponse {
        devices: list.iter().map(|d| mapping::device_to_proto(d, false)).collect(),
    })
}

pub async fn rename_device(
    state: &AppState,
    ctx: &UserContext,
    device_id: Uuid,
    custom_name: &str,
) -> AppResult<()> {
    devices::rename_device(&state.pool, device_id, ctx.user_id, custom_name).await
}

pub async fn delete_user_device(
    state: &AppState,
    device_id: Uuid,
    user_id: i64,
) -> AppResult<()> {
    devices::delete_device(&state.pool, device_id, user_id).await
}

pub async fn set_firebase_token(
    state: &AppState,
    ctx: &UserContext,
    firebase_token: &str,
) -> AppResult<()> {
    let Some(device_guid) = ctx.device_id.as_deref().and_then(|d| Uuid::parse_str(d).ok()) else {
        return Ok(());
    };
    if firebase_token.trim().is_empty() || firebase_token.chars().count() > 256 {
        return Ok(());
    }
    devices::set_firebase_token(&state.pool, device_guid, ctx.user_id, firebase_token).await
}

pub async fn set_notifications_enabled(
    state: &AppState,
    ctx: &UserContext,
    enabled: bool,
) -> AppResult<()> {
    let Some(device_guid) = ctx.device_id.as_deref().and_then(|d| Uuid::parse_str(d).ok()) else {
        return Ok(());
    };
    devices::set_notifications_enabled(&state.pool, device_guid, ctx.user_id, enabled).await
}

fn firebase_tokens_to_proto(
    tokens: Vec<devices::FirebaseToken>,
) -> pb::GetDevicesWithFirebaseTokensResponse {
    pb::GetDevicesWithFirebaseTokensResponse {
        tokens: tokens
            .into_iter()
            .map(|(user_id, device_id, firebase_token)| pb::DeviceFirebaseToken {
                user_id,
                device_id,
                firebase_token,
            })
            .collect(),
    }
}

pub async fn get_devices_with_firebase_tokens(
    state: &AppState,
    user_ids: Vec<i64>,
) -> AppResult<pb::GetDevicesWithFirebaseTokensResponse> {
    let tokens = devices::get_devices_with_firebase_tokens(&state.pool, &user_ids).await?;
    Ok(firebase_tokens_to_proto(tokens))
}

pub async fn get_devices_with_firebase_tokens_by_device_ids(
    state: &AppState,
    device_ids: Vec<Uuid>,
) -> AppResult<pb::GetDevicesWithFirebaseTokensResponse> {
    let tokens =
        devices::get_devices_with_firebase_tokens_by_device_ids(&state.pool, &device_ids).await?;
    Ok(firebase_tokens_to_proto(tokens))
}

pub async fn get_all_devices_with_firebase_tokens(
    state: &AppState,
) -> AppResult<pb::GetDevicesWithFirebaseTokensResponse> {
    let tokens = devices::get_all_devices_with_firebase_tokens(&state.pool).await?;
    Ok(firebase_tokens_to_proto(tokens))
}

// ===== Бейджи ===============================================================

pub async fn get_user_badges(
    state: &AppState,
    user_id: i64,
    limit: Option<i32>,
) -> AppResult<pb::GetUserBadgesResponse> {
    let badges = users::get_user_badges(&state.pool, user_id, limit).await?;
    Ok(pb::GetUserBadgesResponse {
        badges: badges.iter().map(mapping::user_badge_to_proto).collect(),
    })
}

pub async fn assign_user_badge(
    state: &AppState,
    user_id: i64,
    badge_id: i32,
    priority: Option<i32>,
) -> AppResult<pb::AssignUserBadgeResponse> {
    let priority = priority.unwrap_or(1000);
    let ub = users::assign_badge(&state.pool, user_id, badge_id, priority).await?;
    Ok(pb::AssignUserBadgeResponse {
        user_badge: Some(mapping::user_badge_to_proto(&ub)),
    })
}

pub async fn remove_user_badge(
    state: &AppState,
    user_id: i64,
    badge_id: i32,
) -> AppResult<pb::RemoveUserBadgeResponse> {
    let success = users::remove_badge(&state.pool, user_id, badge_id).await?;
    Ok(pb::RemoveUserBadgeResponse { success })
}

pub async fn update_user_badge_priority(
    state: &AppState,
    user_id: i64,
    badge_id: i32,
    new_priority: i32,
) -> AppResult<pb::UpdateUserBadgePriorityResponse> {
    let ub = users::update_user_badge_priority(&state.pool, user_id, badge_id, new_priority)
        .await?
        .ok_or_else(|| {
            AppError::System(format!(
                "User badge not found for User {user_id} and Badge {badge_id}"
            ))
        })?;
    Ok(pb::UpdateUserBadgePriorityResponse {
        user_badge: Some(mapping::user_badge_to_proto(&ub)),
    })
}

pub async fn create_badge(
    state: &AppState,
    name: &str,
    description: &str,
    image_url: &str,
) -> AppResult<pb::CreateBadgeResponse> {
    let badge = users::create_badge(&state.pool, name, Some(description), image_url).await?;
    Ok(pb::CreateBadgeResponse {
        badge: Some(mapping::badge_to_proto(&badge)),
    })
}

pub async fn get_all_badges(
    state: &AppState,
    include_inactive: bool,
) -> AppResult<pb::GetAllBadgesResponse> {
    let badges = users::get_all_badges(&state.pool, include_inactive).await?;
    Ok(pb::GetAllBadgesResponse {
        badges: badges.iter().map(mapping::badge_to_proto).collect(),
    })
}

pub async fn update_badge(
    state: &AppState,
    id: i32,
    name: &str,
    description: &str,
    image_url: &str,
    is_active: bool,
) -> AppResult<pb::UpdateBadgeResponse> {
    let badge = users::update_badge(&state.pool, id, name, Some(description), image_url, is_active)
        .await?
        .ok_or_else(|| AppError::System(format!("Badge with id {id} not found")))?;
    Ok(pb::UpdateBadgeResponse {
        badge: Some(mapping::badge_to_proto(&badge)),
    })
}

pub async fn delete_badge(state: &AppState, id: i32) -> AppResult<pb::DeleteBadgeResponse> {
    let success = users::delete_badge(&state.pool, id).await?;
    Ok(pb::DeleteBadgeResponse { success })
}

// ===== Приватность ==========================================================

pub async fn get_privacy_settings(
    state: &AppState,
    ctx: &UserContext,
) -> AppResult<pb::GetPrivacySettingsResponse> {
    let p = privacy::get_or_create(&state.pool, ctx.user_id).await?;
    Ok(pb::GetPrivacySettingsResponse {
        settings: Some(mapping::privacy_to_proto(&p)),
    })
}

pub async fn update_privacy_settings(
    state: &AppState,
    ctx: &UserContext,
    settings: Option<pb::PrivacySettings>,
) -> AppResult<()> {
    let s = settings.unwrap_or_default();
    privacy::update(
        &state.pool,
        ctx.user_id,
        s.profile_visible_on_site,
        s.avatar_visibility,
        s.bio_visibility,
        s.email_visibility,
        s.search_visible,
        s.online_visibility,
    )
    .await?;
    Ok(())
}

pub async fn get_user_privacy_server(
    state: &AppState,
    user_id: i64,
) -> AppResult<pb::GetUserPrivacyResponse> {
    let p = privacy::get_or_create(&state.pool, user_id).await?;
    Ok(pb::GetUserPrivacyResponse {
        settings: Some(mapping::privacy_to_proto(&p)),
    })
}

// ===== Персонализация =======================================================

pub async fn get_personalization(
    state: &AppState,
    ctx: &UserContext,
) -> AppResult<pb::GetPersonalizationResponse> {
    let p = personalization::get_or_create(&state.pool, ctx.user_id).await?;
    Ok(pb::GetPersonalizationResponse {
        personalization: Some(mapping::personalization_to_proto(&p)),
    })
}

pub async fn update_personalization(
    state: &AppState,
    ctx: &UserContext,
    data: Option<pb::UserPersonalizationData>,
) -> AppResult<()> {
    let d = data.unwrap_or_default();
    let poster = if d.profile_poster_file_id.is_empty() {
        None
    } else {
        Some(d.profile_poster_file_id.as_str())
    };
    personalization::update(&state.pool, ctx.user_id, poster, &d.chat_background_file_ids).await?;
    Ok(())
}

pub async fn get_profile_poster(
    state: &AppState,
    ctx: &UserContext,
) -> AppResult<pb::GetProfilePosterResponse> {
    let p = personalization::get(&state.pool, ctx.user_id).await?;
    Ok(pb::GetProfilePosterResponse {
        profile_poster_file_id: p.and_then(|x| x.profile_poster_file_id).unwrap_or_default(),
    })
}

pub async fn set_profile_poster(
    state: &AppState,
    ctx: &UserContext,
    profile_poster_file_id: Option<String>,
) -> AppResult<()> {
    personalization::update_poster(&state.pool, ctx.user_id, profile_poster_file_id.as_deref())
        .await
}

pub async fn set_profile_poster_server(
    state: &AppState,
    user_id: i64,
    poster_file_id: Option<String>,
) -> AppResult<()> {
    personalization::update_poster(&state.pool, user_id, poster_file_id.as_deref()).await
}

pub async fn get_profile_poster_server(
    state: &AppState,
    user_id: i64,
) -> AppResult<pb::GetProfilePosterServerResponse> {
    let pers = personalization::get(&state.pool, user_id).await?;
    let poster_id = pers.and_then(|p| p.profile_poster_file_id);
    let Some(poster_id) = poster_id.filter(|s| !s.is_empty()) else {
        return Ok(pb::GetProfilePosterServerResponse {
            poster_url: String::new(),
        });
    };
    let poster_url = match state.clients.get_file_data(&poster_id).await {
        Ok(resp) => resp.file_info.map(|i| i.file_url).unwrap_or_default(),
        Err(_) => String::new(),
    };
    Ok(pb::GetProfilePosterServerResponse { poster_url })
}

// ===== Папки чатов ==========================================================

fn parse_folder_id(s: &str) -> Result<Uuid, AppError> {
    Uuid::parse_str(s).map_err(|_| AppError::ChatFolderNotFound)
}

fn parse_chat_id(s: &str) -> Result<Uuid, AppError> {
    Uuid::parse_str(s).map_err(|_| AppError::ChatIdNotValid)
}

pub async fn get_chat_folders(
    state: &AppState,
    ctx: &UserContext,
) -> AppResult<pb::GetChatFoldersResponse> {
    let folders = chat_folders::get_by_owner(&state.pool, ctx.user_id).await?;
    Ok(pb::GetChatFoldersResponse {
        folders: folders.iter().map(mapping::chat_folder_to_proto).collect(),
    })
}

pub async fn create_chat_folder(
    state: &AppState,
    ctx: &UserContext,
    folder_name: &str,
    folder_icon: &str,
) -> AppResult<pb::CreateChatFolderResponse> {
    let name = folder_name.trim();
    if name.is_empty() || name.chars().count() > 64 {
        return Err(AppError::ChatFolderInvalidName);
    }
    let icon = {
        let t = folder_icon.trim();
        if t.is_empty() {
            None
        } else {
            Some(t)
        }
    };
    let folder = chat_folders::create(&state.pool, ctx.user_id, name, icon).await?;
    Ok(pb::CreateChatFolderResponse {
        folder: Some(mapping::chat_folder_to_proto(&folder)),
    })
}

pub async fn update_chat_folder(
    state: &AppState,
    ctx: &UserContext,
    folder_id: &str,
    folder_name: Option<String>,
    folder_icon: Option<String>,
    has_chat_list_update: bool,
    chat_list: Vec<String>,
) -> AppResult<pb::UpdateChatFolderResponse> {
    let folder_id = parse_folder_id(folder_id)?;

    let new_name = match folder_name {
        Some(n) => {
            let t = n.trim().to_string();
            if t.is_empty() || t.chars().count() > 64 {
                return Err(AppError::ChatFolderInvalidName);
            }
            Some(t)
        }
        None => None,
    };
    let update_icon = folder_icon.is_some();
    let icon = folder_icon;

    let parsed_list: Option<Vec<Uuid>> = if has_chat_list_update {
        let mut v = Vec::with_capacity(chat_list.len());
        for c in &chat_list {
            v.push(parse_chat_id(c)?);
        }
        Some(v)
    } else {
        None
    };

    let folder = chat_folders::update(
        &state.pool,
        ctx.user_id,
        folder_id,
        new_name.as_deref(),
        update_icon,
        icon.as_deref(),
        has_chat_list_update,
        parsed_list.as_deref(),
    )
    .await?
    .ok_or(AppError::ChatFolderNotFound)?;

    Ok(pb::UpdateChatFolderResponse {
        folder: Some(mapping::chat_folder_to_proto(&folder)),
    })
}

pub async fn delete_chat_folder(
    state: &AppState,
    ctx: &UserContext,
    folder_id: &str,
) -> AppResult<()> {
    let folder_id = parse_folder_id(folder_id)?;
    let deleted = chat_folders::delete(&state.pool, ctx.user_id, folder_id).await?;
    if !deleted {
        return Err(AppError::ChatFolderNotFound);
    }
    Ok(())
}

pub async fn add_chat_to_folder(
    state: &AppState,
    ctx: &UserContext,
    folder_id: &str,
    chat_id: &str,
) -> AppResult<pb::AddChatToFolderResponse> {
    let folder_id = parse_folder_id(folder_id)?;
    let chat_id = parse_chat_id(chat_id)?;
    let folder = chat_folders::add_chat(&state.pool, ctx.user_id, folder_id, chat_id)
        .await?
        .ok_or(AppError::ChatFolderNotFound)?;
    Ok(pb::AddChatToFolderResponse {
        folder: Some(mapping::chat_folder_to_proto(&folder)),
    })
}

pub async fn remove_chat_from_folder(
    state: &AppState,
    ctx: &UserContext,
    folder_id: &str,
    chat_id: &str,
) -> AppResult<pb::RemoveChatFromFolderResponse> {
    let folder_id = parse_folder_id(folder_id)?;
    let chat_id = parse_chat_id(chat_id)?;
    let folder = chat_folders::remove_chat(&state.pool, ctx.user_id, folder_id, chat_id)
        .await?
        .ok_or(AppError::ChatFolderNotFound)?;
    Ok(pb::RemoveChatFromFolderResponse {
        folder: Some(mapping::chat_folder_to_proto(&folder)),
    })
}

pub async fn reorder_chat_folders(
    state: &AppState,
    ctx: &UserContext,
    orders: Vec<pb::ChatFolderOrder>,
) -> AppResult<()> {
    // Невалидные folder_id молча пропускаются (как в .NET).
    let parsed: Vec<(Uuid, i32)> = orders
        .into_iter()
        .filter_map(|o| Uuid::parse_str(&o.folder_id).ok().map(|id| (id, o.sort_order)))
        .collect();
    chat_folders::reorder(&state.pool, ctx.user_id, &parsed).await
}

// ===== Prekeys (X3DH) =======================================================

pub async fn register_prekey_bundle(
    state: &AppState,
    ctx: &UserContext,
    request: pb::RegisterPrekeyBundleRequest,
) -> AppResult<()> {
    let Some(device_guid) = ctx.device_id.as_deref().and_then(|d| Uuid::parse_str(d).ok()) else {
        return Ok(());
    };
    let signed = request
        .signed_prekey
        .ok_or_else(|| AppError::System("SignedPrekey обязателен".into()))?;
    let one_time: Vec<(i64, Vec<u8>)> = request
        .one_time_prekeys
        .into_iter()
        .map(|p| (p.prekey_id as i64, p.public_key))
        .collect();
    prekeys::register_bundle(
        &state.pool,
        device_guid,
        ctx.user_id,
        request.registration_id as i64,
        &request.identity_pubkey,
        signed.prekey_id as i64,
        &signed.public_key,
        &signed.signature,
        &one_time,
    )
    .await?;
    Ok(())
}

pub async fn fetch_prekey_bundle(
    state: &AppState,
    user_id: i64,
    device_id: &str,
) -> AppResult<pb::FetchPrekeyBundleResponse> {
    let device_guid =
        Uuid::parse_str(device_id).map_err(|_| AppError::System("Некорректный DeviceId".into()))?;
    let result = prekeys::fetch_bundle(&state.pool, user_id, device_guid)
        .await?
        .ok_or_else(|| AppError::System("Bundle устройства не найден".into()))?;
    let (bundle, prekey, remaining) = result;
    Ok(pb::FetchPrekeyBundleResponse {
        bundle: Some(mapping::prekey_bundle_to_proto(&bundle, prekey.as_ref())),
        remaining_one_time_prekeys: remaining,
    })
}

pub async fn list_peer_devices(
    state: &AppState,
    user_id: i64,
) -> AppResult<pb::ListPeerDevicesResponse> {
    let devices = prekeys::list_peer_devices(&state.pool, user_id).await?;
    Ok(pb::ListPeerDevicesResponse {
        devices: devices
            .iter()
            .map(|(d, has)| mapping::peer_device_to_proto(d, *has))
            .collect(),
    })
}

pub async fn replenish_one_time_prekeys(
    state: &AppState,
    ctx: &UserContext,
    request: pb::ReplenishOneTimePrekeysRequest,
) -> AppResult<pb::ReplenishOneTimePrekeysResponse> {
    let Some(device_guid) = ctx.device_id.as_deref().and_then(|d| Uuid::parse_str(d).ok()) else {
        return Ok(pb::ReplenishOneTimePrekeysResponse::default());
    };
    let prekeys_vec: Vec<(i64, Vec<u8>)> = request
        .prekeys
        .into_iter()
        .map(|p| (p.prekey_id as i64, p.public_key))
        .collect();
    let total =
        prekeys::replenish_one_time_prekeys(&state.pool, device_guid, ctx.user_id, &prekeys_vec)
            .await?;
    Ok(pb::ReplenishOneTimePrekeysResponse {
        total_one_time_prekeys: total,
    })
}

pub async fn rotate_signed_prekey(
    state: &AppState,
    ctx: &UserContext,
    request: pb::RotateSignedPrekeyRequest,
) -> AppResult<()> {
    let Some(device_guid) = ctx.device_id.as_deref().and_then(|d| Uuid::parse_str(d).ok()) else {
        return Ok(());
    };
    let signed = request
        .signed_prekey
        .ok_or_else(|| AppError::System("SignedPrekey обязателен".into()))?;
    prekeys::rotate_signed_prekey(
        &state.pool,
        device_guid,
        ctx.user_id,
        signed.prekey_id as i64,
        &signed.public_key,
        &signed.signature,
    )
    .await?;
    Ok(())
}
