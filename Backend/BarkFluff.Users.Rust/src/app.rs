//! Общее состояние приложения (зависимости), доступное хост-сервисам и фичам.

use crate::clients::ServiceClients;
use crate::metrics::MetricsCollector;
use crate::queue::EventPublisher;
use crate::services::ReservedUsernames;
use sqlx::PgPool;
use std::sync::Arc;

pub struct AppState {
    pub pool: PgPool,
    pub metrics: Arc<MetricsCollector>,
    pub reserved: ReservedUsernames,
    pub publisher: Arc<EventPublisher>,
    pub clients: ServiceClients,
}
