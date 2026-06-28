//! Доменные структуры — зеркало сущностей `BarkFluff.Users/Domain`.
//! Имена колонок (PascalCase EF Core) задаются через `#[sqlx(rename = ...)]`,
//! что централизует маппинг столбцов существующей схемы.
//!
//! Часть полей читается из БД через FromRow, но не маппится в proto (id/user_id и пр.) —
//! структуры намеренно зеркалят полную строку таблицы.
#![allow(dead_code)]

use chrono::{DateTime, Utc};
use sqlx::FromRow;
use uuid::Uuid;

/// `ProfileFieldVisibility`: All=0, Friends=1, None=2.
pub const VISIBILITY_ALL: i32 = 0;
pub const VISIBILITY_FRIENDS: i32 = 1;
pub const VISIBILITY_NONE: i32 = 2;

#[derive(Debug, Clone, FromRow)]
pub struct User {
    #[sqlx(rename = "Id")]
    pub id: i64,
    #[sqlx(rename = "Username")]
    pub username: String,
    #[sqlx(rename = "FirstName")]
    pub first_name: String,
    #[sqlx(rename = "LastName")]
    pub last_name: String,
    #[sqlx(rename = "RegistrationDate")]
    pub registration_date: DateTime<Utc>,
    #[sqlx(rename = "IsDraft")]
    pub is_draft: bool,
    #[sqlx(rename = "ProfilePicture")]
    pub profile_picture: Option<String>,
    #[sqlx(rename = "ProfilePicturePreviewUrl")]
    pub profile_picture_preview_url: Option<String>,
    #[sqlx(rename = "Bio")]
    pub bio: Option<String>,
    #[sqlx(rename = "StorageLimitGb")]
    pub storage_limit_gb: i32,
}

#[derive(Debug, Clone, FromRow)]
pub struct UserDevice {
    #[sqlx(rename = "Id")]
    pub id: Uuid,
    #[sqlx(rename = "UserId")]
    pub user_id: i64,
    #[sqlx(rename = "OriginalName")]
    pub original_name: String,
    #[sqlx(rename = "CustomName")]
    pub custom_name: Option<String>,
    #[sqlx(rename = "AuthorizedAt")]
    pub authorized_at: DateTime<Utc>,
    #[sqlx(rename = "AppName")]
    pub app_name: Option<String>,
    #[sqlx(rename = "OperationSystem")]
    pub operation_system: Option<String>,
    #[sqlx(rename = "Location")]
    pub location: Option<String>,
    #[sqlx(rename = "FirebaseDeviceToken")]
    pub firebase_device_token: Option<String>,
    #[sqlx(rename = "NotificationsEnabled")]
    pub notifications_enabled: bool,
}

#[derive(Debug, Clone, FromRow)]
pub struct Privacy {
    #[sqlx(rename = "Id")]
    pub id: i64,
    #[sqlx(rename = "UserId")]
    pub user_id: i64,
    #[sqlx(rename = "ProfileVisibleOnSite")]
    pub profile_visible_on_site: bool,
    #[sqlx(rename = "AvatarVisibility")]
    pub avatar_visibility: i32,
    #[sqlx(rename = "BioVisibility")]
    pub bio_visibility: i32,
    #[sqlx(rename = "EmailVisibility")]
    pub email_visibility: i32,
    #[sqlx(rename = "OnlineVisibility")]
    pub online_visibility: i32,
    #[sqlx(rename = "SearchVisible")]
    pub search_visible: bool,
}

#[derive(Debug, Clone, FromRow)]
pub struct Badge {
    #[sqlx(rename = "Id")]
    pub id: i32,
    #[sqlx(rename = "Name")]
    pub name: String,
    #[sqlx(rename = "Description")]
    pub description: Option<String>,
    #[sqlx(rename = "ImageUrl")]
    pub image_url: String,
    #[sqlx(rename = "CreatedDate")]
    pub created_date: DateTime<Utc>,
    #[sqlx(rename = "IsActive")]
    pub is_active: bool,
}

/// UserBadge вместе с присоединённым Badge (для маппинга в proto).
#[derive(Debug, Clone)]
pub struct UserBadge {
    pub badge: Badge,
    pub priority: i32,
    pub assigned_date: DateTime<Utc>,
}

#[derive(Debug, Clone, FromRow)]
pub struct UserPersonalization {
    #[sqlx(rename = "Id")]
    pub id: i64,
    #[sqlx(rename = "UserId")]
    pub user_id: i64,
    #[sqlx(rename = "ProfilePosterFileId")]
    pub profile_poster_file_id: Option<String>,
    #[sqlx(rename = "ChatBackgroundFileIds")]
    pub chat_background_file_ids: Vec<String>,
}

#[derive(Debug, Clone, FromRow)]
pub struct ChatFolder {
    #[sqlx(rename = "Id")]
    pub id: i64,
    #[sqlx(rename = "OwnerUserId")]
    pub owner_user_id: i64,
    #[sqlx(rename = "FolderId")]
    pub folder_id: Uuid,
    #[sqlx(rename = "FolderName")]
    pub folder_name: String,
    #[sqlx(rename = "FolderIcon")]
    pub folder_icon: Option<String>,
    #[sqlx(rename = "ChatList")]
    pub chat_list: Vec<Uuid>,
    #[sqlx(rename = "SortOrder")]
    pub sort_order: i32,
}

#[derive(Debug, Clone, FromRow)]
pub struct DevicePrekeyBundle {
    #[sqlx(rename = "DeviceId")]
    pub device_id: Uuid,
    #[sqlx(rename = "RegistrationId")]
    pub registration_id: i64,
    #[sqlx(rename = "IdentityPubkey")]
    pub identity_pubkey: Vec<u8>,
    #[sqlx(rename = "SignedPrekeyId")]
    pub signed_prekey_id: i64,
    #[sqlx(rename = "SignedPrekeyPublic")]
    pub signed_prekey_public: Vec<u8>,
    #[sqlx(rename = "SignedPrekeySignature")]
    pub signed_prekey_signature: Vec<u8>,
    #[sqlx(rename = "SignedPrekeyRotatedAt")]
    pub signed_prekey_rotated_at: DateTime<Utc>,
    #[sqlx(rename = "CreatedAt")]
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, FromRow)]
pub struct OneTimePrekey {
    #[sqlx(rename = "Id")]
    pub id: i64,
    #[sqlx(rename = "DeviceId")]
    pub device_id: Uuid,
    #[sqlx(rename = "PrekeyId")]
    pub prekey_id: i64,
    #[sqlx(rename = "PublicKey")]
    pub public_key: Vec<u8>,
    #[sqlx(rename = "CreatedAt")]
    pub created_at: DateTime<Utc>,
}
