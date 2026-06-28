//! In-memory сборщик метрик + фоновый репортер.
//!
//! Эквивалент `MetricsCollector` + `MetricsReporterService` (.NET):
//! counters сбрасываются при snapshot, gauges живут; каждые 5с пишется
//! структурный лог `ServiceMetrics` (в .NET → Seq). В простое — heartbeat
//! не чаще раза в 5 минут (60 тиков по 5с).

use dashmap::DashMap;
use std::collections::BTreeMap;
use std::sync::Arc;
use std::time::Duration;

#[derive(Default)]
pub struct MetricsCollector {
    counters: DashMap<String, i64>,
    gauges: DashMap<String, i64>,
}

impl MetricsCollector {
    pub fn new() -> Arc<Self> {
        Arc::new(Self::default())
    }

    /// Увеличивает счётчик на 1.
    pub fn increment(&self, name: &str) {
        *self.counters.entry(name.to_string()).or_insert(0) += 1;
    }

    /// Добавляет значение к счётчику.
    pub fn add(&self, name: &str, value: i64) {
        *self.counters.entry(name.to_string()).or_insert(0) += value;
    }

    /// Устанавливает значение gauge (не сбрасывается при snapshot).
    pub fn set(&self, name: &str, value: i64) {
        self.gauges.insert(name.to_string(), value);
    }

    /// Снимок всех метрик + сброс счётчиков (gauges сохраняются).
    /// Возвращает `(snapshot, had_counter_activity)`.
    pub fn snapshot_and_reset(&self) -> (BTreeMap<String, i64>, bool) {
        let mut snapshot = BTreeMap::new();
        let mut had_counter_activity = false;

        let keys: Vec<String> = self.counters.iter().map(|e| e.key().clone()).collect();
        for key in keys {
            if let Some((_, value)) = self.counters.remove(&key) {
                if value != 0 {
                    snapshot.insert(key, value);
                    had_counter_activity = true;
                }
            }
        }

        for entry in self.gauges.iter() {
            snapshot.insert(entry.key().clone(), *entry.value());
        }

        (snapshot, had_counter_activity)
    }
}

/// Запускает фоновую задачу-репортер (аналог `MetricsReporterService.ExecuteAsync`).
pub fn spawn_reporter(metrics: Arc<MetricsCollector>, service_name: String) {
    const IDLE_HEARTBEAT_EVERY_TICKS: i32 = 60; // 60 * 5с = 5 минут
    tokio::spawn(async move {
        let mut idle_ticks = 0i32;
        loop {
            tokio::time::sleep(Duration::from_secs(5)).await;
            let (snapshot, had_counter_activity) = metrics.snapshot_and_reset();

            let should_report = if had_counter_activity {
                idle_ticks = 0;
                true
            } else {
                idle_ticks += 1;
                let report = !snapshot.is_empty() && idle_ticks >= IDLE_HEARTBEAT_EVERY_TICKS;
                if report {
                    idle_ticks = 0;
                }
                report
            };

            if should_report {
                let metrics_json = serde_json::to_value(&snapshot).unwrap_or_default();
                let timestamp = chrono::Utc::now().to_rfc3339();
                // Формат, эквивалентный Serilog `ServiceMetrics {@Metrics}`.
                tracing::info!(
                    service_name = %service_name,
                    timestamp = %timestamp,
                    metrics = %metrics_json,
                    "ServiceMetrics"
                );
            }
        }
    });
}
