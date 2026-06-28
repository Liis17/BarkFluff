//! UsersApi (клиентский) — зеркало `Host/UsersApiService.cs`.
//! Политика User (кроме CheckExist* — анонимные). Метрики 1:1 со .NET.

use crate::app::AppState;
use crate::errors::AppError;
use crate::proto::barkfluff::users as pb;
use crate::proto::barkfluff::users::users_api_server::UsersApi;
use crate::{auth, features};
use std::sync::Arc;
use std::time::Instant;
use tonic::{Request, Response, Status};
use uuid::Uuid;

pub struct UsersApiService {
    pub state: Arc<AppState>,
}

fn now_unix() -> i64 {
    chrono::Utc::now().timestamp()
}

#[tonic::async_trait]
impl UsersApi for UsersApiService {
    async fn get_user(
        &self,
        request: Request<pb::GetUserRequest>,
    ) -> Result<Response<pb::GetUserResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("user_lookups");
        let req = request.into_inner();
        let user_id = if req.user_id == 0 { None } else { Some(req.user_id) };
        features::get_user(&self.state, &ctx, user_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn set_profile_picture(
        &self,
        request: Request<pb::SetProfilePictureRequest>,
    ) -> Result<Response<pb::SetProfilePictureResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        let req = request.into_inner();
        if req.file_id.is_empty() {
            self.state.metrics.increment("profile_avatar_removals");
        } else {
            self.state.metrics.increment("profile_avatar_updates");
        }
        let file_id = if req.file_id.is_empty() {
            None
        } else {
            Some(
                Uuid::parse_str(&req.file_id)
                    .map_err(|_| Status::from(AppError::System("invalid file id".into())))?,
            )
        };
        features::set_profile_picture(&self.state, &ctx, file_id).await?;
        Ok(Response::new(pb::SetProfilePictureResponse {}))
    }

    async fn check_exist_username(
        &self,
        request: Request<pb::CheckExistUsernameRequest>,
    ) -> Result<Response<pb::CheckExistResponse>, Status> {
        self.state.metrics.increment("existence_checks");
        let req = request.into_inner();
        features::check_exist_username(&self.state, req.username.trim())
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn check_exist_email(
        &self,
        request: Request<pb::CheckExistEmailRequest>,
    ) -> Result<Response<pb::CheckExistResponse>, Status> {
        self.state.metrics.increment("existence_checks");
        let req = request.into_inner();
        features::check_exist_email(&self.state, req.email.trim())
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn change_name(
        &self,
        request: Request<pb::ChangeNameRequest>,
    ) -> Result<Response<pb::ChangeNameResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("profile_name_updates");
        let req = request.into_inner();
        features::change_name(&self.state, &ctx, &req.first_name, &req.last_name).await?;
        Ok(Response::new(pb::ChangeNameResponse {}))
    }

    async fn change_username(
        &self,
        request: Request<pb::ChangeUsernameRequest>,
    ) -> Result<Response<pb::ChangeUsernameResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("profile_username_updates");
        let req = request.into_inner();
        features::change_username(&self.state, &ctx, &req.username).await?;
        Ok(Response::new(pb::ChangeUsernameResponse {}))
    }

    async fn change_bio(
        &self,
        request: Request<pb::ChangeBioRequest>,
    ) -> Result<Response<pb::ChangeBioResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("profile_bio_updates");
        let req = request.into_inner();
        let bio = if req.bio.trim().is_empty() {
            String::new()
        } else {
            req.bio.trim().to_string()
        };
        features::change_bio(&self.state, &ctx, &bio).await?;
        Ok(Response::new(pb::ChangeBioResponse {}))
    }

    async fn search_users(
        &self,
        request: Request<pb::SearchUsersRequest>,
    ) -> Result<Response<pb::SearchUsersResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("user_searches");
        let req = request.into_inner();
        let (size, offset) = req
            .pagination
            .map(|p| (p.size, p.offset))
            .unwrap_or((10, 0));
        let start = Instant::now();
        match features::search_users(&self.state, &ctx, &req.query, offset, size).await {
            Ok(resp) => {
                self.state
                    .metrics
                    .add("user_search_duration_ms_total", start.elapsed().as_millis() as i64);
                self.state.metrics.set("last_user_search_unix", now_unix());
                Ok(Response::new(resp))
            }
            Err(e) => {
                self.state.metrics.increment("user_search_errors");
                Err(e.into())
            }
        }
    }

    async fn get_user_badges(
        &self,
        request: Request<pb::GetUserBadgesRequest>,
    ) -> Result<Response<pb::GetUserBadgesResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("badge_lookups");
        let req = request.into_inner();
        // proto3 optional → .NET берёт 0 при отсутствии (Take(0)).
        let limit = Some(req.limit.unwrap_or(0));
        features::get_user_badges(&self.state, req.user_id, limit)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn get_devices(
        &self,
        request: Request<pb::GetDevicesRequest>,
    ) -> Result<Response<pb::GetDevicesResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("device_lookups");
        features::get_devices(&self.state, &ctx)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn get_current_device(
        &self,
        request: Request<pb::GetCurrentDeviceRequest>,
    ) -> Result<Response<pb::GetCurrentDeviceResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("device_lookups");
        features::get_current_device(&self.state, &ctx)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn rename_device(
        &self,
        request: Request<pb::RenameDeviceRequest>,
    ) -> Result<Response<pb::RenameDeviceResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("device_renames");
        let req = request.into_inner();
        let device_id = Uuid::parse_str(&req.device_id)
            .map_err(|_| Status::from(AppError::System("invalid device id".into())))?;
        features::rename_device(&self.state, &ctx, device_id, &req.custom_name).await?;
        Ok(Response::new(pb::RenameDeviceResponse {}))
    }

    async fn set_firebase_token(
        &self,
        request: Request<pb::SetFirebaseTokenRequest>,
    ) -> Result<Response<pb::SetFirebaseTokenResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("firebase_token_updates");
        let req = request.into_inner();
        features::set_firebase_token(&self.state, &ctx, &req.firebase_token).await?;
        Ok(Response::new(pb::SetFirebaseTokenResponse {}))
    }

    async fn set_notifications_enabled(
        &self,
        request: Request<pb::SetNotificationsEnabledRequest>,
    ) -> Result<Response<pb::SetNotificationsEnabledResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("notifications_toggles");
        let req = request.into_inner();
        features::set_notifications_enabled(&self.state, &ctx, req.enabled).await?;
        Ok(Response::new(pb::SetNotificationsEnabledResponse {}))
    }

    async fn get_privacy_settings(
        &self,
        request: Request<pb::GetPrivacySettingsRequest>,
    ) -> Result<Response<pb::GetPrivacySettingsResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        features::get_privacy_settings(&self.state, &ctx)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_privacy_settings(
        &self,
        request: Request<pb::UpdatePrivacySettingsRequest>,
    ) -> Result<Response<pb::UpdatePrivacySettingsResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("privacy_updates");
        let req = request.into_inner();
        features::update_privacy_settings(&self.state, &ctx, req.settings).await?;
        Ok(Response::new(pb::UpdatePrivacySettingsResponse {}))
    }

    async fn get_personalization(
        &self,
        request: Request<pb::GetPersonalizationRequest>,
    ) -> Result<Response<pb::GetPersonalizationResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        features::get_personalization(&self.state, &ctx)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_personalization(
        &self,
        request: Request<pb::UpdatePersonalizationRequest>,
    ) -> Result<Response<pb::UpdatePersonalizationResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("personalization_updates");
        let req = request.into_inner();
        features::update_personalization(&self.state, &ctx, req.personalization).await?;
        Ok(Response::new(pb::UpdatePersonalizationResponse {}))
    }

    async fn get_profile_poster(
        &self,
        request: Request<pb::GetProfilePosterRequest>,
    ) -> Result<Response<pb::GetProfilePosterResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        features::get_profile_poster(&self.state, &ctx)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn set_profile_poster(
        &self,
        request: Request<pb::SetProfilePosterRequest>,
    ) -> Result<Response<pb::SetProfilePosterResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("profile_poster_updates");
        let req = request.into_inner();
        let file_id = if req.profile_poster_file_id.is_empty() {
            None
        } else {
            Some(req.profile_poster_file_id)
        };
        features::set_profile_poster(&self.state, &ctx, file_id).await?;
        Ok(Response::new(pb::SetProfilePosterResponse {}))
    }

    async fn get_chat_folders(
        &self,
        request: Request<pb::GetChatFoldersRequest>,
    ) -> Result<Response<pb::GetChatFoldersResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_lookups");
        features::get_chat_folders(&self.state, &ctx)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn create_chat_folder(
        &self,
        request: Request<pb::CreateChatFolderRequest>,
    ) -> Result<Response<pb::CreateChatFolderResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_creates");
        let req = request.into_inner();
        features::create_chat_folder(&self.state, &ctx, &req.folder_name, &req.folder_icon)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_chat_folder(
        &self,
        request: Request<pb::UpdateChatFolderRequest>,
    ) -> Result<Response<pb::UpdateChatFolderResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_updates");
        let req = request.into_inner();
        features::update_chat_folder(
            &self.state,
            &ctx,
            &req.folder_id,
            req.folder_name,
            req.folder_icon,
            req.has_chat_list_update,
            req.chat_list,
        )
        .await
        .map(Response::new)
        .map_err(Into::into)
    }

    async fn delete_chat_folder(
        &self,
        request: Request<pb::DeleteChatFolderRequest>,
    ) -> Result<Response<pb::DeleteChatFolderResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_deletes");
        let req = request.into_inner();
        features::delete_chat_folder(&self.state, &ctx, &req.folder_id).await?;
        Ok(Response::new(pb::DeleteChatFolderResponse {}))
    }

    async fn add_chat_to_folder(
        &self,
        request: Request<pb::AddChatToFolderRequest>,
    ) -> Result<Response<pb::AddChatToFolderResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_chat_adds");
        let req = request.into_inner();
        features::add_chat_to_folder(&self.state, &ctx, &req.folder_id, &req.chat_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn remove_chat_from_folder(
        &self,
        request: Request<pb::RemoveChatFromFolderRequest>,
    ) -> Result<Response<pb::RemoveChatFromFolderResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_chat_removes");
        let req = request.into_inner();
        features::remove_chat_from_folder(&self.state, &ctx, &req.folder_id, &req.chat_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn reorder_chat_folders(
        &self,
        request: Request<pb::ReorderChatFoldersRequest>,
    ) -> Result<Response<pb::ReorderChatFoldersResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("chat_folder_reorders");
        let req = request.into_inner();
        features::reorder_chat_folders(&self.state, &ctx, req.orders).await?;
        Ok(Response::new(pb::ReorderChatFoldersResponse {}))
    }

    async fn register_prekey_bundle(
        &self,
        request: Request<pb::RegisterPrekeyBundleRequest>,
    ) -> Result<Response<pb::RegisterPrekeyBundleResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("prekey_bundle_registrations");
        let req = request.into_inner();
        features::register_prekey_bundle(&self.state, &ctx, req).await?;
        Ok(Response::new(pb::RegisterPrekeyBundleResponse {}))
    }

    async fn fetch_prekey_bundle(
        &self,
        request: Request<pb::FetchPrekeyBundleRequest>,
    ) -> Result<Response<pb::FetchPrekeyBundleResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("prekey_bundle_fetches");
        let req = request.into_inner();
        features::fetch_prekey_bundle(&self.state, req.user_id, &req.device_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn list_peer_devices(
        &self,
        request: Request<pb::ListPeerDevicesRequest>,
    ) -> Result<Response<pb::ListPeerDevicesResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("peer_device_listings");
        let req = request.into_inner();
        features::list_peer_devices(&self.state, req.user_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn replenish_one_time_prekeys(
        &self,
        request: Request<pb::ReplenishOneTimePrekeysRequest>,
    ) -> Result<Response<pb::ReplenishOneTimePrekeysResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("one_time_prekey_replenishments");
        let req = request.into_inner();
        features::replenish_one_time_prekeys(&self.state, &ctx, req)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn rotate_signed_prekey(
        &self,
        request: Request<pb::RotateSignedPrekeyRequest>,
    ) -> Result<Response<pb::RotateSignedPrekeyResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_user()?;
        self.state.metrics.increment("signed_prekey_rotations");
        let req = request.into_inner();
        features::rotate_signed_prekey(&self.state, &ctx, req).await?;
        Ok(Response::new(pb::RotateSignedPrekeyResponse {}))
    }
}
