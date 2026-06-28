//! UsersServerApi (межсервисный) — зеркало `Host/UsersServerApiService.cs`.
//! Политика Service. Метрики 1:1 со .NET.

use crate::app::AppState;
use crate::errors::AppError;
use crate::proto::barkfluff::users as pb;
use crate::proto::barkfluff::users::users_server_api_server::UsersServerApi;
use crate::{auth, features};
use std::sync::Arc;
use std::time::Instant;
use tonic::{Request, Response, Status};
use uuid::Uuid;

pub struct UsersServerApiService {
    pub state: Arc<AppState>,
}

fn now_unix() -> i64 {
    chrono::Utc::now().timestamp()
}

#[tonic::async_trait]
impl UsersServerApi for UsersServerApiService {
    async fn find_by_login(
        &self,
        request: Request<pb::FindByLoginRequest>,
    ) -> Result<Response<pb::FindByLoginResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("login_lookups");
        let req = request.into_inner();
        // oneof login → username | email
        let (username, email) = match req.login {
            Some(pb::find_by_login_request::Login::Username(u)) => (Some(u), None),
            Some(pb::find_by_login_request::Login::Email(e)) => (None, Some(e)),
            None => (None, None),
        };
        features::find_by_login(&self.state, username, email)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn check_exist_username(
        &self,
        request: Request<pb::CheckExistUsernameRequest>,
    ) -> Result<Response<pb::CheckExistResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
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
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("existence_checks");
        let req = request.into_inner();
        features::check_exist_email(&self.state, req.email.trim())
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn add_draft_user(
        &self,
        request: Request<pb::AddDraftUserRequest>,
    ) -> Result<Response<pb::AddDraftUserResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("drafts_create_requests");
        let req = request.into_inner();
        match features::add_draft_user(&self.state, &req.username, &req.email, &req.first_name, &req.last_name).await {
            Ok(resp) => {
                self.state.metrics.increment("drafts_created");
                self.state.metrics.set("last_draft_created_unix", now_unix());
                Ok(Response::new(resp))
            }
            Err(e) => {
                self.state.metrics.increment("drafts_create_errors");
                Err(e.into())
            }
        }
    }

    async fn override_draft_user(
        &self,
        request: Request<pb::AddDraftUserRequest>,
    ) -> Result<Response<pb::AddDraftUserResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("drafts_overridden");
        let req = request.into_inner();
        features::override_draft_user(&self.state, &req.username, &req.email, &req.first_name, &req.last_name)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn confirm_user(
        &self,
        request: Request<pb::ConfirmUserRequest>,
    ) -> Result<Response<pb::ConfirmUserResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("users_confirm_requests");
        let req = request.into_inner();
        match features::confirm_user(&self.state, req.user_id).await {
            Ok(()) => {
                self.state.metrics.increment("users_confirmed");
                self.state.metrics.set("last_user_confirmed_unix", now_unix());
                Ok(Response::new(pb::ConfirmUserResponse {}))
            }
            Err(e) => {
                self.state.metrics.increment("users_confirm_errors");
                Err(e.into())
            }
        }
    }

    async fn get_by_id(
        &self,
        request: Request<pb::GetByIdRequest>,
    ) -> Result<Response<pb::GetByIdResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("user_lookups");
        let req = request.into_inner();
        let resp = features::get_user(&self.state, &ctx, Some(req.user_id)).await?;
        Ok(Response::new(pb::GetByIdResponse { user: resp.user }))
    }

    async fn get_user_contacts(
        &self,
        request: Request<pb::GetUserContactsRequest>,
    ) -> Result<Response<pb::GetUserContactsResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("contact_lookups");
        let req = request.into_inner();
        features::get_user_contacts(&self.state, req.user_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn list_by_ids(
        &self,
        request: Request<pb::ListByIdsRequest>,
    ) -> Result<Response<pb::ListByIdsResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("user_lookups");
        let req = request.into_inner();
        features::list_by_ids(&self.state, req.ids)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn assign_user_badge(
        &self,
        request: Request<pb::AssignUserBadgeRequest>,
    ) -> Result<Response<pb::AssignUserBadgeResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badges_assigned");
        let req = request.into_inner();
        let priority = Some(req.priority.unwrap_or(0));
        features::assign_user_badge(&self.state, req.user_id, req.badge_id, priority)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn remove_user_badge(
        &self,
        request: Request<pb::RemoveUserBadgeRequest>,
    ) -> Result<Response<pb::RemoveUserBadgeResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badges_removed");
        let req = request.into_inner();
        features::remove_user_badge(&self.state, req.user_id, req.badge_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_user_badge_priority(
        &self,
        request: Request<pb::UpdateUserBadgePriorityRequest>,
    ) -> Result<Response<pb::UpdateUserBadgePriorityResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badges_priority_updated");
        let req = request.into_inner();
        features::update_user_badge_priority(&self.state, req.user_id, req.badge_id, req.new_priority)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn create_badge(
        &self,
        request: Request<pb::CreateBadgeRequest>,
    ) -> Result<Response<pb::CreateBadgeResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badges_created");
        let req = request.into_inner();
        features::create_badge(&self.state, &req.name, &req.description, &req.image_url)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn get_all_badges(
        &self,
        request: Request<pb::GetAllBadgesRequest>,
    ) -> Result<Response<pb::GetAllBadgesResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badge_lookups");
        let req = request.into_inner();
        features::get_all_badges(&self.state, req.include_inactive)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_badge(
        &self,
        request: Request<pb::UpdateBadgeRequest>,
    ) -> Result<Response<pb::UpdateBadgeResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badges_updated");
        let req = request.into_inner();
        features::update_badge(&self.state, req.id, &req.name, &req.description, &req.image_url, req.is_active)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn delete_badge(
        &self,
        request: Request<pb::DeleteBadgeRequest>,
    ) -> Result<Response<pb::DeleteBadgeResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("badges_deleted");
        let req = request.into_inner();
        features::delete_badge(&self.state, req.id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn export_data(
        &self,
        request: Request<pb::ExportDataRequest>,
    ) -> Result<Response<pb::ExportDataResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("data_exports");
        let req = request.into_inner();
        let start = Instant::now();
        match features::export_data(&self.state, req.user_id).await {
            Ok(resp) => {
                self.state
                    .metrics
                    .add("data_export_duration_ms_total", start.elapsed().as_millis() as i64);
                self.state.metrics.set("last_data_export_unix", now_unix());
                Ok(Response::new(resp))
            }
            Err(e) => {
                self.state.metrics.increment("data_export_errors");
                Err(e.into())
            }
        }
    }

    async fn register_device(
        &self,
        request: Request<pb::RegisterDeviceRequest>,
    ) -> Result<Response<pb::RegisterDeviceResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("device_registrations");
        let req = request.into_inner();
        let device_id = Uuid::parse_str(&req.device_id)
            .map_err(|_| Status::from(AppError::System("invalid device id".into())))?;
        let resp = features::register_device(
            &self.state,
            device_id,
            req.user_id,
            &req.original_name,
            &req.app_name,
            &req.operation_system,
            &req.location,
        )
        .await?;
        self.state.metrics.set("last_device_registered_unix", now_unix());
        Ok(Response::new(resp))
    }

    async fn get_user_devices(
        &self,
        request: Request<pb::GetUserDevicesRequest>,
    ) -> Result<Response<pb::GetUserDevicesResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("device_lookups");
        let req = request.into_inner();
        features::get_user_devices(&self.state, req.user_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn delete_user_device(
        &self,
        request: Request<pb::DeleteUserDeviceRequest>,
    ) -> Result<Response<pb::DeleteUserDeviceResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("device_deletions");
        let req = request.into_inner();
        let device_id = Uuid::parse_str(&req.device_id)
            .map_err(|_| Status::from(AppError::System("invalid device id".into())))?;
        features::delete_user_device(&self.state, device_id, req.user_id).await?;
        Ok(Response::new(pb::DeleteUserDeviceResponse {}))
    }

    async fn get_user_by_username(
        &self,
        request: Request<pb::GetUserByUsernameRequest>,
    ) -> Result<Response<pb::GetUserByUsernameResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("public_profile_views");
        let req = request.into_inner();
        features::get_user_by_username(&self.state, &req.username)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn search_users_server(
        &self,
        request: Request<pb::SearchUsersServerRequest>,
    ) -> Result<Response<pb::SearchUsersServerResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("user_searches");
        let req = request.into_inner();
        features::search_users_server(&self.state, &req.query, req.offset, req.size)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_storage_limit(
        &self,
        request: Request<pb::UpdateStorageLimitRequest>,
    ) -> Result<Response<pb::UpdateStorageLimitResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("storage_limit_updates");
        let req = request.into_inner();
        features::update_storage_limit(&self.state, req.user_id, req.storage_limit_gb)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn set_profile_picture_server(
        &self,
        request: Request<pb::SetProfilePictureServerRequest>,
    ) -> Result<Response<pb::SetProfilePictureServerResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("profile_avatar_updates");
        let req = request.into_inner();
        features::set_profile_picture_server(
            &self.state,
            req.user_id,
            &req.profile_picture_url,
            &req.profile_picture_preview_url,
        )
        .await?;
        Ok(Response::new(pb::SetProfilePictureServerResponse {}))
    }

    async fn get_devices_with_firebase_tokens(
        &self,
        request: Request<pb::GetDevicesWithFirebaseTokensRequest>,
    ) -> Result<Response<pb::GetDevicesWithFirebaseTokensResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("device_lookups");
        let req = request.into_inner();
        features::get_devices_with_firebase_tokens(&self.state, req.user_ids)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn get_devices_with_firebase_tokens_by_device_ids(
        &self,
        request: Request<pb::GetDevicesWithFirebaseTokensByDeviceIdsRequest>,
    ) -> Result<Response<pb::GetDevicesWithFirebaseTokensResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("device_lookups_by_device_id");
        let req = request.into_inner();
        // Невалидные guid пропускаются (Guid.TryParse).
        let device_ids: Vec<Uuid> = req
            .device_ids
            .iter()
            .filter_map(|s| Uuid::parse_str(s).ok())
            .collect();
        features::get_devices_with_firebase_tokens_by_device_ids(&self.state, device_ids)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn get_all_devices_with_firebase_tokens(
        &self,
        request: Request<pb::GetAllDevicesWithFirebaseTokensRequest>,
    ) -> Result<Response<pb::GetDevicesWithFirebaseTokensResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("device_lookups_all");
        features::get_all_devices_with_firebase_tokens(&self.state)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn get_user_privacy(
        &self,
        request: Request<pb::GetUserPrivacyRequest>,
    ) -> Result<Response<pb::GetUserPrivacyResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        let req = request.into_inner();
        features::get_user_privacy_server(&self.state, req.user_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }

    async fn update_profile_server(
        &self,
        request: Request<pb::UpdateProfileServerRequest>,
    ) -> Result<Response<pb::UpdateProfileServerResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("profile_updates_server");
        let req = request.into_inner();
        features::update_profile_server(
            &self.state,
            req.user_id,
            &req.first_name,
            &req.last_name,
            &req.bio,
            &req.username,
        )
        .await?;
        Ok(Response::new(pb::UpdateProfileServerResponse {}))
    }

    async fn set_profile_poster_server(
        &self,
        request: Request<pb::SetProfilePosterServerRequest>,
    ) -> Result<Response<pb::SetProfilePosterServerResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("profile_poster_updates");
        let req = request.into_inner();
        let poster = if req.poster_file_id.is_empty() {
            None
        } else {
            Some(req.poster_file_id)
        };
        features::set_profile_poster_server(&self.state, req.user_id, poster).await?;
        Ok(Response::new(pb::SetProfilePosterServerResponse {}))
    }

    async fn get_profile_poster_server(
        &self,
        request: Request<pb::GetProfilePosterServerRequest>,
    ) -> Result<Response<pb::GetProfilePosterServerResponse>, Status> {
        let ctx = auth::user_context(&request);
        ctx.require_service()?;
        self.state.metrics.increment("profile_poster_lookups");
        let req = request.into_inner();
        features::get_profile_poster_server(&self.state, req.user_id)
            .await
            .map(Response::new)
            .map_err(Into::into)
    }
}
