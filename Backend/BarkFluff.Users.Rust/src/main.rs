//! BarkFluff.Users.Rust — drop-in порт микросервиса BarkFluff.Users на Rust.
//! Bootstrap: конфиг (Configuration-сервис) → пул БД → метрики → RabbitMQ →
//! gRPC-сервер (UsersApi + UsersServerApi) с XAuth-интерцептором.

mod app;
mod auth;
mod clients;
mod config;
mod domain;
mod errors;
mod features;
mod host;
mod mapping;
mod metrics;
mod persistence;
mod proto;
mod queue;
mod services;

use crate::app::AppState;
use crate::auth::{AuthInterceptor, AuthState, TokenRevocationCache};
use crate::clients::ServiceClients;
use crate::config::Config;
use crate::host::client_api::UsersApiService;
use crate::host::server_api::UsersServerApiService;
use crate::metrics::MetricsCollector;
use crate::proto::barkfluff::users::users_api_server::UsersApiServer;
use crate::proto::barkfluff::users::users_server_api_server::UsersServerApiServer;
use crate::queue::EventPublisher;
use crate::services::ReservedUsernames;
use std::sync::Arc;
use std::time::Duration;
use tonic::transport::Server;

const SERVICE_NAME: &str = "BarkFluff.Users";

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info")),
        )
        .init();

    // 1. Конфигурация из Configuration-сервиса.
    let config = Config::load().await?;
    let port = config.port();
    tracing::info!("Конфигурация загружена. Порт: {port}");

    // 2. Метрики + репортер (gauges старта).
    let metrics = MetricsCollector::new();
    metrics::spawn_reporter(metrics.clone(), SERVICE_NAME.to_string());
    metrics.set("service_started_unix", chrono::Utc::now().timestamp());
    metrics.set("db_migration_healthy", 0);

    // 3. Пул БД (схема уже создана .NET-сервисом — миграции не выполняем).
    let users_db = config.require("UsersDb")?;
    let pool = persistence::create_pool(&users_db).await?;
    metrics.set("db_migration_healthy", 1);
    tracing::info!("Пул PostgreSQL подключён");

    // 4. Зарезервированные имена.
    let reserved = ReservedUsernames::from_csv(&config.get_or("ReservedNames:Usernames", ""));

    // 5. Кэш отозванных сессий + фоновая чистка.
    let revocation = TokenRevocationCache::new();
    {
        let cache = revocation.clone();
        tokio::spawn(async move {
            loop {
                tokio::time::sleep(Duration::from_secs(60)).await;
                cache.cleanup();
            }
        });
    }

    // 6. RabbitMQ publisher + consumer (MassTransit-совместимо).
    let amqp_uri = queue::build_amqp_uri(
        &config.require("RabbitMQ:Host")?,
        &config.require("RabbitMQ:Username")?,
        &config.require("RabbitMQ:Password")?,
    );
    let publisher = EventPublisher::connect(&amqp_uri, metrics.clone()).await?;
    {
        let uri = amqp_uri.clone();
        let cache = revocation.clone();
        let m = metrics.clone();
        tokio::spawn(async move {
            if let Err(e) = queue::run_session_revoked_consumer(uri, cache, m).await {
                tracing::error!("SessionRevoked consumer завершился с ошибкой: {e}");
            }
        });
    }

    // 7. gRPC-клиенты Files/Messages.
    let clients = ServiceClients::new(
        config.require("FilesService:Host")?,
        config.require("FilesService:Token")?,
        config.require("MessagesService:Host")?,
        config.require("MessagesService:Token")?,
    )?;

    // 8. Состояние приложения.
    let state = Arc::new(AppState {
        pool,
        metrics: metrics.clone(),
        reserved,
        publisher,
        clients,
    });

    // 9. XAuth-интерцептор.
    let auth_state = Arc::new(AuthState::new(
        config.require("JwtSettings:SecretKey")?,
        config.require("JwtSettings:Issuer")?,
        config.require("JwtSettings:Audience")?,
        revocation,
    ));
    let interceptor = AuthInterceptor::new(auth_state);

    // 10. Reflection.
    let reflection = tonic_reflection::server::Builder::configure()
        .register_encoded_file_descriptor_set(proto::FILE_DESCRIPTOR_SET)
        .build_v1()?;

    let client_svc = UsersApiServer::with_interceptor(
        UsersApiService { state: state.clone() },
        interceptor.clone(),
    );
    let server_svc = UsersServerApiServer::with_interceptor(
        UsersServerApiService { state: state.clone() },
        interceptor,
    );

    let addr = format!("0.0.0.0:{port}").parse()?;
    tracing::info!("gRPC-сервер слушает на {addr}");

    Server::builder()
        .add_service(reflection)
        .add_service(server_svc)
        .add_service(client_svc)
        .serve(addr)
        .await?;

    Ok(())
}
