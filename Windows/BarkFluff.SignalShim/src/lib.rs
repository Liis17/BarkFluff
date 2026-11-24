mod operations;
mod stores;

use std::{
    ffi::c_void,
    panic::catch_unwind,
    ptr, slice,
    sync::{
        Once,
        atomic::{AtomicUsize, Ordering},
    },
};

use base64::{Engine, engine::general_purpose::STANDARD};
use libsignal_protocol::SignalProtocolError;
use serde::{Deserialize, Deserializer, Serialize, Serializer, de::Error as _};
use zeroize::Zeroizing;

struct Bytes(Zeroizing<Vec<u8>>);

impl From<Vec<u8>> for Bytes {
    fn from(bytes: Vec<u8>) -> Self {
        Self(Zeroizing::new(bytes))
    }
}

impl AsRef<[u8]> for Bytes {
    fn as_ref(&self) -> &[u8] {
        &self.0
    }
}

impl Serialize for Bytes {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        let encoded = Zeroizing::new(STANDARD.encode(self.as_ref()));
        serializer.serialize_str(&encoded)
    }
}

impl<'de> Deserialize<'de> for Bytes {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let encoded = Zeroizing::new(String::deserialize(deserializer)?);
        STANDARD
            .decode(&*encoded)
            .map(Self::from)
            .map_err(D::Error::custom)
    }
}

#[repr(i32)]
pub enum Error {
    Input = 1,
    Protocol = 2,
    Panic = 3,
    UntrustedIdentity = 4,
    DuplicateMessage = 5,
    SessionNotFound = 6,
    GenerationMismatch = 7,
    UnsupportedSnapshot = 8,
}

impl From<serde_json::Error> for Error {
    fn from(_: serde_json::Error) -> Self {
        Self::Input
    }
}

impl From<SignalProtocolError> for Error {
    fn from(error: SignalProtocolError) -> Self {
        match error {
            SignalProtocolError::UntrustedIdentity(_) => Self::UntrustedIdentity,
            SignalProtocolError::DuplicatedMessage(..) => Self::DuplicateMessage,
            SignalProtocolError::InvalidMessage(
                libsignal_protocol::CiphertextMessageType::PreKey,
                stores::REUSED_BASE_KEY,
            ) => Self::DuplicateMessage,
            SignalProtocolError::SessionNotFound(_) => Self::SessionNotFound,
            _ => Self::Protocol,
        }
    }
}

// Снимок включает ключи, сессии и маркеры повтора всего устройства.
// Ограничиваем суммарную память на FFI и оставляем место для тысяч сессий.
const MAX_SNAPSHOT_LEN: usize = 64 * 1024 * 1024;
// Запрос содержит один bundle или envelope, включая накладные расходы JSON/base64.
const MAX_REQUEST_LEN: usize = 1024 * 1024;

struct ResponseBuffer {
    state: Bytes,
    output: Bytes,
    generation: u64,
}

static LIVE_RESPONSES: AtomicUsize = AtomicUsize::new(0);

#[repr(C)]
pub struct ResponseView {
    generation: u64,
    state: *const u8,
    state_len: usize,
    output: *const u8,
    output_len: usize,
}

fn execute(
    state: &[u8],
    expected_generation: u64,
    request: &[u8],
) -> Result<Box<ResponseBuffer>, Error> {
    let operation = serde_json::from_slice(request)?;
    let (state, output) =
        futures::executor::block_on(operations::execute(state, expected_generation, operation))?;
    let snapshot = Bytes::from(serde_json::to_vec(&state)?);
    if snapshot.as_ref().len() > MAX_SNAPSHOT_LEN {
        return Err(Error::Input);
    }
    Ok(Box::new(ResponseBuffer {
        state: snapshot,
        output: serde_json::to_vec(&output)?.into(),
        generation: state.generation,
    }))
}

unsafe fn input<'a>(data: *const u8, len: usize, limit: usize) -> Result<&'a [u8], Error> {
    if len == 0 {
        return Ok(&[]);
    }
    if data.is_null() || len > limit {
        return Err(Error::Input);
    }
    Ok(unsafe { slice::from_raw_parts(data, len) })
}

// Входные буферы заимствуются до возврата. Дескриптор результата принадлежит
// вызывающей стороне, а указатель на данные действителен до освобождения дескриптора.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn barkfluff_signal_execute(
    state: *const u8,
    state_len: usize,
    expected_generation: u64,
    request: *const u8,
    request_len: usize,
    handle: *mut *mut c_void,
    view: *mut ResponseView,
) -> i32 {
    if handle.is_null() || view.is_null() {
        return Error::Input as i32;
    }
    unsafe {
        handle.write(ptr::null_mut());
        view.write(ResponseView {
            generation: 0,
            state: ptr::null(),
            state_len: 0,
            output: ptr::null(),
            output_len: 0,
        });
    }
    let result = catch_unwind(|| {
        // Стандартная библиотека статически связана с DLL. Не печатаем payload
        // паники из shim или libsignal в stderr приложения.
        static PANIC_HOOK: Once = Once::new();
        PANIC_HOOK.call_once(|| std::panic::set_hook(Box::new(|_| {})));
        let state = unsafe { input(state, state_len, MAX_SNAPSHOT_LEN)? };
        let request = unsafe { input(request, request_len, MAX_REQUEST_LEN)? };
        execute(state, expected_generation, request)
    });
    match result {
        Ok(Ok(response)) => {
            LIVE_RESPONSES.fetch_add(1, Ordering::Relaxed);
            unsafe {
                view.write(ResponseView {
                    generation: response.generation,
                    state: response.state.as_ref().as_ptr(),
                    state_len: response.state.as_ref().len(),
                    output: response.output.as_ref().as_ptr(),
                    output_len: response.output.as_ref().len(),
                });
                handle.write(Box::into_raw(response).cast());
            }
            0
        }
        Ok(Err(error)) => error as i32,
        Err(payload) => {
            // Произвольный payload паники может сам запаниковать при освобождении.
            // Здесь libsignal и shim используют стандартные строковые payload.
            if let Err(payload) = catch_unwind(std::panic::AssertUnwindSafe(|| drop(payload))) {
                std::mem::forget(payload);
            }
            Error::Panic as i32
        }
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn barkfluff_signal_free(handle: *mut c_void) {
    if !handle.is_null() {
        drop(unsafe { Box::from_raw(handle.cast::<ResponseBuffer>()) });
        LIVE_RESPONSES.fetch_sub(1, Ordering::Relaxed);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn barkfluff_signal_live_responses() -> usize {
    LIVE_RESPONSES.load(Ordering::Relaxed)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ffi_releases_each_response_and_errors_own_no_buffer() {
        for _ in 0..32 {
            let mut handle = ptr::null_mut();
            let mut view = std::mem::MaybeUninit::uninit();
            let request = br#"{"operation":"create"}"#;
            let status = unsafe {
                barkfluff_signal_execute(
                    ptr::null(),
                    0,
                    0,
                    request.as_ptr(),
                    request.len(),
                    &mut handle,
                    view.as_mut_ptr(),
                )
            };
            assert_eq!(status, 0);
            assert!(!handle.is_null());
            let response = unsafe { view.assume_init_read() };
            assert!(!response.state.is_null() && response.state_len > 0);
            let snapshot = unsafe { slice::from_raw_parts(response.state, response.state_len) };
            assert_eq!(
                serde_json::from_slice::<stores::State>(snapshot)
                    .unwrap()
                    .generation,
                1
            );
            assert_eq!(barkfluff_signal_live_responses(), 1);
            unsafe { barkfluff_signal_free(handle) };
            assert_eq!(barkfluff_signal_live_responses(), 0);

            let status = unsafe {
                barkfluff_signal_execute(
                    ptr::null(),
                    0,
                    0,
                    ptr::null(),
                    1,
                    &mut handle,
                    view.as_mut_ptr(),
                )
            };
            assert_eq!(status, Error::Input as i32);
            let response = unsafe { view.assume_init_read() };
            assert!(handle.is_null() && response.state.is_null() && response.output.is_null());
            unsafe { barkfluff_signal_free(handle) };
            assert_eq!(barkfluff_signal_live_responses(), 0);
        }
    }

    #[test]
    fn rejects_snapshot_larger_than_limit() {
        let mut snapshot = vec![b' '; MAX_SNAPSHOT_LEN + 1];
        snapshot[..13].copy_from_slice(br#"{"version":2}"#);
        let request = br#"{"operation":"generatePreKey","id":1}"#;
        let mut handle = ptr::null_mut();
        let mut view = std::mem::MaybeUninit::uninit();
        let status = unsafe {
            barkfluff_signal_execute(
                snapshot.as_ptr(),
                snapshot.len(),
                0,
                request.as_ptr(),
                request.len(),
                &mut handle,
                view.as_mut_ptr(),
            )
        };
        assert_eq!(status, Error::Input as i32);
        assert!(handle.is_null());
    }
}
