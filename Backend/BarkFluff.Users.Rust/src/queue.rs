//! RabbitMQ в формате MassTransit: публикация UserChanged* и потребление
//! SessionRevokedEvent. Зеркало `Infrastructure/UserInfoQueueSender.cs` +
//! `Consumers/SessionRevokedConsumer.cs`.
//!
//! Совместимость с MassTransit:
//! - exchange на тип сообщения: `{Namespace}:{TypeName}` (fanout, durable);
//! - тело — конверт `{ messageId, messageType:[urn:message:Ns:Type], message:{...}, ... }`,
//!   content-type `application/vnd.masstransit+json`;
//! - receive endpoint `session-revoked-users`: одноимённый fanout-exchange + очередь,
//!   привязка source-exchange `BarkFluff.Shared.Queue.Identity:SessionRevokedEvent`.

use crate::auth::TokenRevocationCache;
use crate::metrics::MetricsCollector;
use chrono::{DateTime, Utc};
use futures_util::StreamExt;
use lapin::options::{
    BasicAckOptions, BasicConsumeOptions, BasicPublishOptions, ExchangeBindOptions,
    ExchangeDeclareOptions, QueueBindOptions, QueueDeclareOptions,
};
use lapin::types::FieldTable;
use lapin::{BasicProperties, Channel, Connection, ConnectionProperties, ExchangeKind};
use serde_json::json;
use std::sync::Arc;
use uuid::Uuid;

const NS_USERS: &str = "BarkFluff.Shared.Queue.Users";
const SESSION_REVOKED_EXCHANGE: &str = "BarkFluff.Shared.Queue.Identity:SessionRevokedEvent";
const SESSION_REVOKED_QUEUE: &str = "session-revoked-users";

/// Строит AMQP URI (vhost "/" → %2F) из host/user/pass конфига RabbitMQ.
pub fn build_amqp_uri(host: &str, user: &str, pass: &str) -> String {
    let hostport = if host.contains(':') {
        host.to_string()
    } else {
        format!("{host}:5672")
    };
    format!("amqp://{user}:{pass}@{hostport}/%2F")
}

async fn connect(uri: &str) -> anyhow::Result<Connection> {
    // Executor — tokio (наш runtime); reactor оставляем дефолтным lapin (async-io),
    // он сам поднимает поток и работает внутри tokio.
    let props =
        ConnectionProperties::default().with_executor(tokio_executor_trait::Tokio::current());
    Ok(Connection::connect(uri, props).await?)
}

fn envelope(urn: &str, message: serde_json::Value, exchange: &str) -> (String, String) {
    let message_id = Uuid::new_v4().to_string();
    let env = json!({
        "messageId": message_id,
        "conversationId": Uuid::new_v4().to_string(),
        "sourceAddress": "rabbitmq://localhost/BarkFluff.Users.Rust",
        "destinationAddress": format!("rabbitmq://localhost/{exchange}"),
        "messageType": [format!("urn:message:{urn}")],
        "message": message,
        "sentTime": Utc::now().to_rfc3339(),
        "headers": {},
        "host": { "machineName": "barkfluff-users-rust" }
    });
    (env.to_string(), message_id)
}

/// Публикатор событий профиля (эквивалент UserInfoQueueSender).
pub struct EventPublisher {
    channel: Channel,
    metrics: Arc<MetricsCollector>,
    #[allow(dead_code)] // удерживаем соединение живым (RAII)
    connection: Connection,
}

impl EventPublisher {
    pub async fn connect(uri: &str, metrics: Arc<MetricsCollector>) -> anyhow::Result<Arc<Self>> {
        let connection = connect(uri).await?;
        let channel = connection.create_channel().await?;
        Ok(Arc::new(Self {
            channel,
            metrics,
            connection,
        }))
    }

    async fn publish(&self, type_name: &str, message: serde_json::Value) -> anyhow::Result<()> {
        let exchange = format!("{NS_USERS}:{type_name}");
        let urn = format!("{NS_USERS}:{type_name}");
        self.channel
            .exchange_declare(
                &exchange,
                ExchangeKind::Fanout,
                ExchangeDeclareOptions {
                    durable: true,
                    ..Default::default()
                },
                FieldTable::default(),
            )
            .await?;

        let (body, message_id) = envelope(&urn, message, &exchange);
        self.channel
            .basic_publish(
                &exchange,
                "",
                BasicPublishOptions::default(),
                body.as_bytes(),
                BasicProperties::default()
                    .with_content_type("application/vnd.masstransit+json".into())
                    .with_message_id(message_id.into())
                    .with_delivery_mode(2),
            )
            .await?
            .await?;
        Ok(())
    }

    pub async fn name_changed(&self, user_id: i64, first_name: &str, last_name: &str) {
        let _ = self
            .publish(
                "UserChangedName",
                json!({ "userId": user_id, "newFirstName": first_name, "newLastName": last_name }),
            )
            .await;
        self.metrics.increment("user_events_published");
        self.metrics.increment("user_name_changed_published");
    }

    pub async fn username_changed(&self, user_id: i64, username: &str) {
        let _ = self
            .publish(
                "UserChangedUsername",
                json!({ "userId": user_id, "newUsername": username }),
            )
            .await;
        self.metrics.increment("user_events_published");
        self.metrics.increment("user_username_changed_published");
    }

    pub async fn avatar_changed(&self, user_id: i64, url: &str, preview: &str) {
        let _ = self
            .publish(
                "UserChangedAvatar",
                json!({ "userId": user_id, "profilePictureUrl": url, "profilePictureUrlPreview": preview }),
            )
            .await;
        self.metrics.increment("user_events_published");
        self.metrics.increment("user_avatar_changed_published");
    }

    // UserChangedPassword публикуется Identity, не Users — метод для паритета API.
    #[allow(dead_code)]
    pub async fn password_changed(&self, user_id: i64) {
        let _ = self
            .publish("UserChangedPassword", json!({ "userId": user_id }))
            .await;
        self.metrics.increment("user_events_published");
        self.metrics.increment("user_password_changed_published");
    }

    pub async fn bio_changed(&self, user_id: i64, bio: &str) {
        let _ = self
            .publish("UserChangedBio", json!({ "userId": user_id, "newBio": bio }))
            .await;
        self.metrics.increment("user_events_published");
        self.metrics.increment("user_bio_changed_published");
    }
}

/// Запускает фоновый consumer SessionRevokedEvent (эквивалент SessionRevokedConsumer).
pub async fn run_session_revoked_consumer(
    uri: String,
    cache: Arc<TokenRevocationCache>,
    metrics: Arc<MetricsCollector>,
) -> anyhow::Result<()> {
    let connection = connect(&uri).await?;
    let channel = connection.create_channel().await?;

    // Топология как у MassTransit receive endpoint "session-revoked-users".
    channel
        .exchange_declare(
            SESSION_REVOKED_QUEUE,
            ExchangeKind::Fanout,
            ExchangeDeclareOptions {
                durable: true,
                ..Default::default()
            },
            FieldTable::default(),
        )
        .await?;
    channel
        .queue_declare(
            SESSION_REVOKED_QUEUE,
            QueueDeclareOptions {
                durable: true,
                ..Default::default()
            },
            FieldTable::default(),
        )
        .await?;
    channel
        .queue_bind(
            SESSION_REVOKED_QUEUE,
            SESSION_REVOKED_QUEUE,
            "",
            QueueBindOptions::default(),
            FieldTable::default(),
        )
        .await?;
    channel
        .exchange_declare(
            SESSION_REVOKED_EXCHANGE,
            ExchangeKind::Fanout,
            ExchangeDeclareOptions {
                durable: true,
                ..Default::default()
            },
            FieldTable::default(),
        )
        .await?;
    channel
        .exchange_bind(
            SESSION_REVOKED_QUEUE,
            SESSION_REVOKED_EXCHANGE,
            "",
            ExchangeBindOptions::default(),
            FieldTable::default(),
        )
        .await?;

    let mut consumer = channel
        .basic_consume(
            SESSION_REVOKED_QUEUE,
            "barkfluff-users-rust",
            BasicConsumeOptions::default(),
            FieldTable::default(),
        )
        .await?;

    tracing::info!("SessionRevoked consumer запущен (очередь {SESSION_REVOKED_QUEUE})");

    while let Some(delivery) = consumer.next().await {
        let delivery = match delivery {
            Ok(d) => d,
            Err(e) => {
                tracing::warn!("Ошибка доставки RabbitMQ: {e}");
                continue;
            }
        };

        if let Ok(env) = serde_json::from_slice::<serde_json::Value>(&delivery.data) {
            if let Some(msg) = env.get("message") {
                let user_id = field_i64(msg, "userId", "UserId");
                let device_id = field_str(msg, "deviceId", "DeviceId");
                let expires = field_str(msg, "accessTokenExpiresAt", "AccessTokenExpiresAt")
                    .and_then(|s| DateTime::parse_from_rfc3339(&s).ok())
                    .map(|d| d.with_timezone(&Utc))
                    .unwrap_or_else(Utc::now);

                if let (Some(uid), Some(dev)) = (user_id, device_id) {
                    metrics.increment("session_revoked_received");
                    tracing::info!("Получено событие отзыва сессии: UserId={uid}, DeviceId={dev}");
                    cache.revoke(uid, &dev, expires);
                    metrics.set("last_session_revoked_unix", Utc::now().timestamp());
                }
            }
        }

        let _ = delivery.ack(BasicAckOptions::default()).await;
    }

    // Соединение удерживается до конца цикла.
    drop(connection);
    Ok(())
}

fn field_i64(v: &serde_json::Value, a: &str, b: &str) -> Option<i64> {
    let f = v.get(a).or_else(|| v.get(b))?;
    f.as_i64().or_else(|| f.as_str().and_then(|s| s.parse().ok()))
}

fn field_str(v: &serde_json::Value, a: &str, b: &str) -> Option<String> {
    v.get(a)
        .or_else(|| v.get(b))?
        .as_str()
        .map(|s| s.to_string())
}
