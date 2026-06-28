//! Прикладные сервисы: валидатор формата username и реестр зарезервированных имён.

use once_cell::sync::Lazy;
use regex::Regex;
use std::collections::HashSet;

static USERNAME_RE: Lazy<Regex> =
    Lazy::new(|| Regex::new(r"^[a-zA-Z0-9_]{3,32}$").expect("valid regex"));

/// Эквивалент `UsernameFormatValidator.IsValid`.
pub fn username_format_valid(username: &str) -> bool {
    !username.trim().is_empty() && USERNAME_RE.is_match(username)
}

/// Эквивалент `ReservedUsernamesService` (Singleton).
pub struct ReservedUsernames {
    reserved: HashSet<String>,
}

impl ReservedUsernames {
    pub fn from_csv(csv: &str) -> Self {
        let reserved = csv
            .split(',')
            .map(|s| s.trim())
            .filter(|s| !s.is_empty())
            .map(|s| s.to_lowercase())
            .collect();
        Self { reserved }
    }

    pub fn is_reserved(&self, username: &str) -> bool {
        if username.trim().is_empty() {
            return false;
        }
        self.reserved.contains(&username.trim().to_lowercase())
    }
}
