//! Слой доступа к данным (sqlx + PostgreSQL).
//! Работает с СУЩЕСТВУЮЩЕЙ схемой (создаётся .NET-сервисом), поэтому миграции
//! не выполняются, а запросы используют точные PascalCase-идентификаторы EF Core.

pub mod chat_folders;
pub mod devices;
pub mod personalization;
pub mod prekeys;
pub mod privacy;
pub mod users;

use sqlx::postgres::{PgConnectOptions, PgPoolOptions};
use sqlx::PgPool;

/// Парсит Npgsql key-value строку подключения (.NET) в `PgConnectOptions`.
/// sqlx не понимает формат `Host=...;Database=...;Username=...;Password=...`.
fn parse_npgsql(conn: &str) -> PgConnectOptions {
    let mut opts = PgConnectOptions::new();
    for part in conn.split(';') {
        let part = part.trim();
        if part.is_empty() {
            continue;
        }
        let Some((key, value)) = part.split_once('=') else {
            continue;
        };
        let key = key.trim().to_ascii_lowercase();
        let value = value.trim().to_string();
        match key.as_str() {
            "host" | "server" => opts = opts.host(&value),
            "port" => {
                if let Ok(p) = value.parse::<u16>() {
                    opts = opts.port(p);
                }
            }
            "database" | "db" => opts = opts.database(&value),
            "username" | "user id" | "userid" | "user" => opts = opts.username(&value),
            "password" | "pwd" => opts = opts.password(&value),
            _ => {} // прочие ключи (Ssl Mode, Pooling, ...) для dev игнорируем
        }
    }
    opts
}

/// Создаёт пул соединений из Npgsql-строки `UsersDb`.
pub async fn create_pool(connection_string: &str) -> anyhow::Result<PgPool> {
    let opts = parse_npgsql(connection_string);
    let pool = PgPoolOptions::new()
        .max_connections(20)
        .connect_with(opts)
        .await?;
    Ok(pool)
}
