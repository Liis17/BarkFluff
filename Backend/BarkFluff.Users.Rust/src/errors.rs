//! Ошибки приложения и их маппинг в gRPC `Status`.
//!
//! Эквивалент `ServerExceptionInterceptor` (.NET):
//! - бизнес-ошибка → `FailedPrecondition` + trailer `x-error-code: <GUID>`;
//! - системная ошибка → `Unknown` + trailer с базовым GUID.
//!
//! GUID кодов перенесены 1:1 из `BarkFluff.Shared.Exceptions`.

use tonic::{Code, Status};

pub const ERR_BASE: &str = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";

#[derive(Debug, thiserror::Error)]
pub enum AppError {
    #[error("Пользователь не найден")]
    UserNotFound,
    #[error("Пользователь с таким емейлом зарегристирован")]
    EmailExist,
    #[error("Пользователь с таким именем пользователя существует")]
    UsernameExist,
    #[error("Имя пользователя имеет недопустимый формат: разрешены латинские буквы, цифры и подчёркивание, длина от 3 до 32 символов")]
    UsernameInvalidFormat,
    #[error("Это имя пользователя зарезервировано и не может быть использовано")]
    UsernameReserved,
    #[error("Пользователь с такими параметрами есть, но он не подтвержден")]
    UserIsDraft,
    #[error("Био не должно превышать 200 символов")]
    BioTooLong,
    #[error("Папка чатов не найдена")]
    ChatFolderNotFound,
    #[error("Название папки не должно быть пустым и не должно превышать 64 символа")]
    ChatFolderInvalidName,
    #[error("Переданный file-id содержит файл не с типом Изображение профиля пользователя.")]
    ProfilePictureHasNotValidType,
    #[error("Был передан невалидный chat id. Он должен быть guid")]
    ChatIdNotValid,

    /// Системная (не бизнес) ошибка → Unknown + базовый код.
    #[error("{0}")]
    System(String),
}

impl AppError {
    /// Для бизнес-ошибок возвращает `(error_code, message)`; для системных — None.
    fn business(&self) -> Option<(&'static str, String)> {
        let code = match self {
            AppError::UserNotFound => "A4DAB334-1067-4838-A782-C4257DC838F7",
            AppError::EmailExist => "7599F3F1-C2EC-4D05-BF38-A1A60D40BA4E",
            AppError::UsernameExist => "DB157CD8-98A3-4A35-9857-33821813D422",
            AppError::UsernameInvalidFormat => "E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47",
            AppError::UsernameReserved => "A3F1B2C4-7D8E-4F5A-9B6C-1E2D3F4A5B6C",
            AppError::UserIsDraft => "91D75288-8314-4658-AA0E-EC1D01779D58",
            AppError::BioTooLong => "1A652492-87A4-4B8B-B758-E7FBE1F39DDF",
            AppError::ChatFolderNotFound => "5F0B7B2E-3F6E-4D2B-9B9E-9B7E7C2B9D8A",
            AppError::ChatFolderInvalidName => "8C1A6F4D-1B22-4E1B-8E4D-7E9A5B6C2A11",
            AppError::ProfilePictureHasNotValidType => "7097703F-977C-4E28-8C85-1A287B3FF8AD",
            AppError::ChatIdNotValid => "91CB4758-151F-4BF7-8C6D-435923CDF1AF",
            AppError::System(_) => return None,
        };
        Some((code, self.to_string()))
    }
}

impl From<sqlx::Error> for AppError {
    fn from(e: sqlx::Error) -> Self {
        AppError::System(format!("db error: {e}"))
    }
}

impl From<anyhow::Error> for AppError {
    fn from(e: anyhow::Error) -> Self {
        AppError::System(e.to_string())
    }
}

impl From<AppError> for Status {
    fn from(e: AppError) -> Self {
        match e.business() {
            Some((code, msg)) => {
                let mut s = Status::new(Code::FailedPrecondition, msg);
                if let Ok(v) = code.parse() {
                    s.metadata_mut().insert("x-error-code", v);
                }
                s
            }
            None => {
                let mut s = Status::new(Code::Unknown, e.to_string());
                if let Ok(v) = ERR_BASE.parse() {
                    s.metadata_mut().insert("x-error-code", v);
                }
                s
            }
        }
    }
}

pub type AppResult<T> = Result<T, AppError>;
