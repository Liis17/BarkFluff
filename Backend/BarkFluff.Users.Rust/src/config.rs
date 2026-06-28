//! Загрузка конфигурации из Configuration-сервиса по gRPC.
//! Эквивалент `WebApplicationBuilderExtensions.LoadConfiguration(ServiceId.Users)`.

use crate::proto::barkfluff::configuration::{
    configuration_api_client::ConfigurationApiClient, GetConfigurationRequest,
};
use std::collections::HashMap;

pub const SERVICE_ID_USERS: i32 = 2; // BarkFluff.Shared.Identity.ServiceId.Users

pub struct Config {
    map: HashMap<String, String>,
}

impl Config {
    /// Грузит конфиг: локальные дефолты + значения из Configuration-сервиса.
    pub async fn load() -> anyhow::Result<Self> {
        let mut map = HashMap::new();
        // Локальные дефолты (appsettings.json у .NET).
        map.insert("RunSettings:Port".to_string(), "7001".to_string());

        let addr = std::env::var("CONFIGURATION_SERVICE_URL")
            .ok()
            .or_else(|| std::env::var("ConfigurationServiceAddr").ok())
            .unwrap_or_else(|| "http://localhost:7003".to_string());
        map.insert("ConfigurationServiceAddr".to_string(), addr.clone());

        let mut client = ConfigurationApiClient::connect(addr).await?;
        let response = client
            .get_configuration(GetConfigurationRequest {
                service_id: SERVICE_ID_USERS,
            })
            .await?
            .into_inner();

        for item in response.configurations {
            let key = if item.key.is_empty() {
                item.section
            } else {
                format!("{}:{}", item.section, item.key)
            };
            map.insert(key, item.value);
        }

        Ok(Self { map })
    }

    pub fn get(&self, key: &str) -> Option<&str> {
        self.map.get(key).map(|s| s.as_str())
    }

    pub fn require(&self, key: &str) -> anyhow::Result<String> {
        self.get(key)
            .map(|s| s.to_string())
            .ok_or_else(|| anyhow::anyhow!("отсутствует ключ конфигурации: {key}"))
    }

    pub fn get_or(&self, key: &str, default: &str) -> String {
        self.get(key).unwrap_or(default).to_string()
    }

    pub fn port(&self) -> u16 {
        self.get("RunSettings:Port")
            .and_then(|p| p.parse().ok())
            .unwrap_or(7001)
    }
}
