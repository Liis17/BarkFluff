//! Маппинг domain → proto. Зеркало `Mapping/*.cs` (+ inline-маппинг устройств).

use crate::domain;
use crate::proto::barkfluff::users as pb;
use chrono::{DateTime, Utc};

/// DateTime<Utc> → prost_types::Timestamp.
pub fn to_timestamp(dt: DateTime<Utc>) -> prost_types::Timestamp {
    prost_types::Timestamp {
        seconds: dt.timestamp(),
        nanos: dt.timestamp_subsec_nanos() as i32,
    }
}

pub fn user_to_proto(u: &domain::User) -> pb::User {
    pb::User {
        id: u.id,
        first_name: u.first_name.clone(),
        last_name: u.last_name.clone(),
        username: u.username.clone(),
        registration_date: Some(to_timestamp(u.registration_date)),
        profile_picture: u.profile_picture.clone().unwrap_or_default(),
        profile_picture_preview: u.profile_picture_preview_url.clone().unwrap_or_default(),
        bio: u.bio.clone().unwrap_or_default(),
        badges: Vec::new(),
        storage_limit_gb: u.storage_limit_gb,
        profile_poster_file_id: String::new(),
    }
}

pub fn badge_to_proto(b: &domain::Badge) -> pb::Badge {
    pb::Badge {
        id: b.id,
        name: b.name.clone(),
        description: b.description.clone().unwrap_or_default(),
        image_url: b.image_url.clone(),
        created_date: Some(to_timestamp(b.created_date)),
        is_active: b.is_active,
    }
}

pub fn user_badge_to_proto(ub: &domain::UserBadge) -> pb::UserBadge {
    pb::UserBadge {
        badge: Some(badge_to_proto(&ub.badge)),
        priority: ub.priority,
        assigned_date: Some(to_timestamp(ub.assigned_date)),
    }
}

pub fn privacy_to_proto(p: &domain::Privacy) -> pb::PrivacySettings {
    pb::PrivacySettings {
        profile_visible_on_site: p.profile_visible_on_site,
        avatar_visibility: p.avatar_visibility,
        bio_visibility: p.bio_visibility,
        email_visibility: p.email_visibility,
        search_visible: p.search_visible,
        online_visibility: p.online_visibility,
    }
}

pub fn personalization_to_proto(p: &domain::UserPersonalization) -> pb::UserPersonalizationData {
    pb::UserPersonalizationData {
        profile_poster_file_id: p.profile_poster_file_id.clone().unwrap_or_default(),
        chat_background_file_ids: p.chat_background_file_ids.clone(),
    }
}

pub fn chat_folder_to_proto(f: &domain::ChatFolder) -> pb::ChatFolderData {
    pb::ChatFolderData {
        folder_id: f.folder_id.to_string(),
        folder_name: f.folder_name.clone(),
        folder_icon: f.folder_icon.clone().unwrap_or_default(),
        chat_list: f.chat_list.iter().map(|id| id.to_string()).collect(),
        sort_order: f.sort_order,
    }
}

/// UserDevice → proto Device. `include_notifications` повторяет различие .NET:
/// GetCurrentDevice выставляет notifications_enabled, остальные — нет (default false).
pub fn device_to_proto(d: &domain::UserDevice, include_notifications: bool) -> pb::Device {
    pb::Device {
        device_id: d.id.to_string(),
        user_id: d.user_id,
        original_name: d.original_name.clone(),
        custom_name: d.custom_name.clone().unwrap_or_default(),
        authorized_at: Some(to_timestamp(d.authorized_at)),
        app_name: d.app_name.clone().unwrap_or_default(),
        operation_system: d.operation_system.clone().unwrap_or_default(),
        location: d.location.clone().unwrap_or_default(),
        notifications_enabled: if include_notifications {
            d.notifications_enabled
        } else {
            false
        },
    }
}

pub fn signed_prekey_to_proto(b: &domain::DevicePrekeyBundle) -> pb::SignedPreKey {
    pb::SignedPreKey {
        prekey_id: b.signed_prekey_id as u32,
        public_key: b.signed_prekey_public.clone(),
        signature: b.signed_prekey_signature.clone(),
    }
}

pub fn one_time_prekey_to_proto(p: &domain::OneTimePrekey) -> pb::OneTimePreKey {
    pb::OneTimePreKey {
        prekey_id: p.prekey_id as u32,
        public_key: p.public_key.clone(),
    }
}

pub fn prekey_bundle_to_proto(
    b: &domain::DevicePrekeyBundle,
    one_time: Option<&domain::OneTimePrekey>,
) -> pb::PrekeyBundle {
    pb::PrekeyBundle {
        device_id: b.device_id.to_string(),
        registration_id: b.registration_id as u32,
        identity_pubkey: b.identity_pubkey.clone(),
        signed_prekey: Some(signed_prekey_to_proto(b)),
        one_time_prekey: one_time.map(one_time_prekey_to_proto),
        has_one_time_prekey: one_time.is_some(),
    }
}

pub fn peer_device_to_proto(d: &domain::UserDevice, has_bundle: bool) -> pb::PeerDeviceInfo {
    let display_name = match &d.custom_name {
        Some(c) if !c.is_empty() => c.clone(),
        _ => d.original_name.clone(),
    };
    pb::PeerDeviceInfo {
        device_id: d.id.to_string(),
        display_name,
        has_bundle,
        last_seen_at: Some(to_timestamp(d.authorized_at)),
    }
}
