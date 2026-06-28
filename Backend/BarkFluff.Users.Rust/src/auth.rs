//! XAuth/JWT: валидация токена из заголовка `x-auth-token` (HS256),
//! `UserContext`, политики User/Service, `TokenRevocationCache`,
//! `RequestContext` (Base64-метадата). Эквивалент `BarkFluff.GrpcServer/XAuth`
//! + `JwtBearer` настроек + `RequestContextInterceptor`.

use base64::engine::general_purpose::{STANDARD, URL_SAFE_NO_PAD};
use base64::Engine;
use chrono::{DateTime, Utc};
use dashmap::DashMap;
use hmac::{Hmac, Mac};
use sha2::Sha256;
use std::sync::Arc;
use tonic::{Request, Status};

// Имена claim'ов — как в BarkFluff.Shared.Identity.IdentityClaims.
const CLAIM_USER_ID: &str = "x-user-id";
const CLAIM_TOKEN_TYPE: &str = "x-token-type";
const CLAIM_DEVICE_ID: &str = "x-device-id";

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum TokenType {
    #[default]
    Unknown,
    User,
    Service,
}

impl TokenType {
    fn parse(value: &str) -> Self {
        match value {
            "User" => TokenType::User,
            "Service" => TokenType::Service,
            _ => TokenType::Unknown,
        }
    }
}

/// Контекст текущего вызова (эквивалент XAuth `UserContext`).
#[derive(Clone, Debug, Default)]
pub struct UserContext {
    pub user_id: i64,
    pub token_type: TokenType,
    pub device_id: Option<String>,
    pub authenticated: bool,
}

impl UserContext {
    /// Политика `nameof(TokenType.User)` — токен User ИЛИ Service.
    pub fn require_user(&self) -> Result<(), Status> {
        if !self.authenticated {
            return Err(Status::unauthenticated("Требуется авторизация"));
        }
        match self.token_type {
            TokenType::User | TokenType::Service => Ok(()),
            _ => Err(Status::permission_denied("Недостаточно прав")),
        }
    }

    /// Политика `nameof(TokenType.Service)` — только Service-токен.
    pub fn require_service(&self) -> Result<(), Status> {
        if !self.authenticated {
            return Err(Status::unauthenticated("Требуется авторизация"));
        }
        match self.token_type {
            TokenType::Service => Ok(()),
            _ => Err(Status::permission_denied("Требуется сервисный токен")),
        }
    }
}

/// Метадата запроса (Base64-заголовки), эквивалент `RequestContext`.
/// Поля метадаты доступны хендлерам, но Users-логика их не читает (как в .NET).
#[allow(dead_code)]
#[derive(Clone, Debug, Default)]
pub struct RequestContext {
    pub device_name: Option<String>,
    pub operation_system: Option<String>,
    pub app_name: Option<String>,
    pub app_version: Option<String>,
    pub ip_address: Option<String>,
    pub device_id: Option<String>,
}

/// In-memory кэш отозванных сессий (эквивалент `TokenRevocationCache`).
#[derive(Default)]
pub struct TokenRevocationCache {
    revoked: DashMap<String, DateTime<Utc>>,
}

impl TokenRevocationCache {
    pub fn new() -> Arc<Self> {
        Arc::new(Self::default())
    }

    fn key(user_id: i64, device_id: &str) -> String {
        format!("{user_id}:{device_id}")
    }

    pub fn revoke(&self, user_id: i64, device_id: &str, expires_at: DateTime<Utc>) {
        self.revoked.insert(Self::key(user_id, device_id), expires_at);
    }

    pub fn is_revoked(&self, user_id: i64, device_id: &str) -> bool {
        self.revoked.contains_key(&Self::key(user_id, device_id))
    }

    /// Чистка протухших записей (фоновый аналог TokenRevocationCleanupService).
    pub fn cleanup(&self) {
        let now = Utc::now();
        self.revoked.retain(|_, exp| *exp >= now);
    }
}

/// Настройки валидации JWT.
#[derive(Clone)]
pub struct AuthState {
    secret: Vec<u8>,
    issuer: String,
    audience: String,
    pub revocation: Arc<TokenRevocationCache>,
}

impl AuthState {
    pub fn new(secret: String, issuer: String, audience: String, revocation: Arc<TokenRevocationCache>) -> Self {
        Self {
            secret: secret.into_bytes(),
            issuer,
            audience,
            revocation,
        }
    }

    /// Валидирует JWT (HS256, iss/aud/exp, ClockSkew=0) и возвращает UserContext.
    fn validate(&self, token: &str) -> Option<UserContext> {
        let parts: Vec<&str> = token.split('.').collect();
        if parts.len() != 3 {
            return None;
        }

        // header.alg == HS256
        let header_bytes = URL_SAFE_NO_PAD.decode(parts[0]).ok()?;
        let header: serde_json::Value = serde_json::from_slice(&header_bytes).ok()?;
        if header.get("alg").and_then(|a| a.as_str()) != Some("HS256") {
            return None;
        }

        // подпись HMAC-SHA256 над "header.payload"
        let signing_input = format!("{}.{}", parts[0], parts[1]);
        let signature = URL_SAFE_NO_PAD.decode(parts[2]).ok()?;
        let mut mac = Hmac::<Sha256>::new_from_slice(&self.secret).ok()?;
        mac.update(signing_input.as_bytes());
        mac.verify_slice(&signature).ok()?; // constant-time

        // payload
        let payload_bytes = URL_SAFE_NO_PAD.decode(parts[1]).ok()?;
        let claims: serde_json::Value = serde_json::from_slice(&payload_bytes).ok()?;

        // exp (ClockSkew = 0)
        let exp = claims.get("exp").and_then(|e| e.as_i64())?;
        if Utc::now().timestamp() >= exp {
            return None;
        }

        // iss
        if claims.get("iss").and_then(|i| i.as_str()) != Some(self.issuer.as_str()) {
            return None;
        }

        // aud (строка или массив)
        let aud_ok = match claims.get("aud") {
            Some(serde_json::Value::String(s)) => s == &self.audience,
            Some(serde_json::Value::Array(arr)) => {
                arr.iter().any(|v| v.as_str() == Some(self.audience.as_str()))
            }
            _ => false,
        };
        if !aud_ok {
            return None;
        }

        let user_id = match claims.get(CLAIM_USER_ID) {
            Some(serde_json::Value::String(s)) => s.parse::<i64>().ok()?,
            Some(serde_json::Value::Number(n)) => n.as_i64()?,
            _ => 0,
        };
        let token_type = claims
            .get(CLAIM_TOKEN_TYPE)
            .and_then(|t| t.as_str())
            .map(TokenType::parse)
            .unwrap_or(TokenType::Unknown);
        let device_id = claims
            .get(CLAIM_DEVICE_ID)
            .and_then(|d| d.as_str())
            .filter(|s| !s.is_empty())
            .map(|s| s.to_string());

        // Проверка отзыва сессии (для User-токенов с device_id) — как OnTokenValidated.
        if token_type == TokenType::User {
            if let Some(ref dev) = device_id {
                if self.revocation.is_revoked(user_id, dev) {
                    return None;
                }
            }
        }

        Some(UserContext {
            user_id,
            token_type,
            device_id,
            authenticated: true,
        })
    }
}

fn decode_b64_header(req: &Request<()>, key: &str) -> Option<String> {
    let raw = req.metadata().get(key)?.to_str().ok()?;
    if raw.is_empty() {
        return None;
    }
    let bytes = STANDARD.decode(raw).ok()?;
    String::from_utf8(bytes).ok()
}

/// gRPC-интерцептор: валидирует токен и кладёт UserContext + RequestContext
/// в extensions запроса. Политики проверяются в самих хендлерах
/// (`ctx.require_user()` / `require_service()`), как `[Authorize]` в .NET.
#[derive(Clone)]
pub struct AuthInterceptor {
    state: Arc<AuthState>,
}

impl AuthInterceptor {
    pub fn new(state: Arc<AuthState>) -> Self {
        Self { state }
    }
}

impl tonic::service::Interceptor for AuthInterceptor {
    fn call(&mut self, mut req: Request<()>) -> Result<Request<()>, Status> {
        let ctx = req
            .metadata()
            .get("x-auth-token")
            .and_then(|t| t.to_str().ok())
            .and_then(|t| self.state.validate(t))
            .unwrap_or_default();

        let rc = RequestContext {
            device_name: decode_b64_header(&req, "x-device-name"),
            operation_system: decode_b64_header(&req, "x-os-name"),
            app_name: decode_b64_header(&req, "x-app-name"),
            app_version: decode_b64_header(&req, "x-app-version"),
            ip_address: decode_b64_header(&req, "x-ip-address"),
            device_id: decode_b64_header(&req, "x-device-id"),
        };

        req.extensions_mut().insert(ctx);
        req.extensions_mut().insert(rc);
        Ok(req)
    }
}

/// Достаёт UserContext из extensions запроса (дефолт — неаутентифицированный).
pub fn user_context<T>(req: &Request<T>) -> UserContext {
    req.extensions().get::<UserContext>().cloned().unwrap_or_default()
}
