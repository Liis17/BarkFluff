use std::time::{SystemTime, UNIX_EPOCH};

use libsignal_protocol::*;
use rand::{Rng, TryRngCore, rngs::OsRng};
use serde::{Deserialize, Serialize};

use crate::{
    Bytes, Error,
    stores::{SNAPSHOT_VERSION, State},
};

#[derive(Deserialize)]
#[serde(
    tag = "operation",
    rename_all = "camelCase",
    rename_all_fields = "camelCase"
)]
pub enum Operation {
    Create,
    GeneratePreKey {
        id: u32,
    },
    GenerateSignedPreKey {
        id: u32,
    },
    GenerateKyberPreKey {
        id: u32,
        last_resort: bool,
    },
    GetSignedPreKey {
        id: u32,
    },
    GetKyberPreKey {
        id: u32,
    },
    RemoveSignedPreKey {
        id: u32,
    },
    RemoveKyberPreKey {
        id: u32,
    },
    DeleteSession {
        peer: String,
    },
    ProcessBundle {
        peer: String,
        bundle: Bundle,
    },
    Encrypt {
        peer: String,
        plaintext: Bytes,
    },
    Decrypt {
        peer: String,
        kind: u8,
        ciphertext: Bytes,
    },
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PreKey {
    id: u32,
    public_key: Bytes,
}

#[derive(Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SignedKey {
    id: u32,
    public_key: Bytes,
    signature: Bytes,
    #[serde(skip_deserializing)]
    timestamp: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Bundle {
    registration_id: u32,
    identity_key: Bytes,
    pre_key: Option<PreKey>,
    signed_pre_key: SignedKey,
    kyber_pre_key: SignedKey,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Device {
    registration_id: u32,
    identity_key: Bytes,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Encrypted {
    kind: u8,
    ciphertext: Bytes,
}

#[derive(Serialize)]
#[serde(untagged)]
pub enum Output {
    Device(Device),
    PreKey(PreKey),
    SignedKey(SignedKey),
    Encrypted(Encrypted),
    Plaintext(Bytes),
    Empty,
}

fn address(peer: String) -> std::result::Result<ProtocolAddress, Error> {
    if peer.is_empty() {
        return Err(Error::Input);
    }
    // Android использует строковый ID устройства как имя адреса, с deviceId = 1.
    Ok(ProtocolAddress::new(
        peer,
        DeviceId::new(1).expect("valid device id"),
    ))
}

fn timestamp(now: SystemTime) -> std::result::Result<Timestamp, Error> {
    let millis = now
        .duration_since(UNIX_EPOCH)
        .map_err(|_| Error::Input)?
        .as_millis();
    Ok(Timestamp::from_epoch_millis(
        millis.try_into().map_err(|_| Error::Input)?,
    ))
}

pub async fn execute(
    previous: &[u8],
    expected_generation: u64,
    operation: Operation,
) -> std::result::Result<(State, Output), Error> {
    // API libsignal принимает CryptoRng. Сбой системного RNG прерывает
    // операцию через catch_unwind на границе FFI.
    let mut rng = OsRng.unwrap_err();
    if matches!(operation, Operation::Create) {
        if !previous.is_empty() {
            return Err(Error::Input);
        }
        if expected_generation != 0 {
            return Err(Error::GenerationMismatch);
        }
        let identity = IdentityKeyPair::generate(&mut rng);
        let registration_id = rng.random_range(1..=16_380);
        let state = State::new(&identity, registration_id);
        return Ok((
            state,
            Output::Device(Device {
                registration_id,
                identity_key: identity.identity_key().serialize().into_vec().into(),
            }),
        ));
    }

    #[derive(Deserialize)]
    struct SnapshotVersion {
        version: u32,
    }
    if serde_json::from_slice::<SnapshotVersion>(previous)?.version != SNAPSHOT_VERSION {
        return Err(Error::UnsupportedSnapshot);
    }
    let mut state: State = serde_json::from_slice(previous)?;
    if state.generation != expected_generation {
        return Err(Error::GenerationMismatch);
    }
    if !matches!(
        operation,
        Operation::GetSignedPreKey { .. } | Operation::GetKyberPreKey { .. }
    ) {
        state.generation = state.generation.checked_add(1).ok_or(Error::Input)?;
    }
    state.restore_identity()?;
    let now = SystemTime::now();
    let output = match operation {
        Operation::Create => unreachable!(),
        Operation::GeneratePreKey { id } => {
            if id == 0 || state.pre_keys.0.contains_key(&id) {
                return Err(Error::Input);
            }
            let pair = KeyPair::generate(&mut rng);
            let record = PreKeyRecord::new(id.into(), &pair);
            state.pre_keys.save_pre_key(id.into(), &record).await?;
            Output::PreKey(PreKey {
                id,
                public_key: pair.public_key.serialize().into_vec().into(),
            })
        }
        Operation::GenerateSignedPreKey { id } => {
            if id == 0 || state.signed_keys.0.contains_key(&id) {
                return Err(Error::Input);
            }
            let pair = KeyPair::generate(&mut rng);
            let public_key = pair.public_key.serialize().into_vec();
            let identity = state.identities.get_identity_key_pair().await?;
            let signature = identity
                .private_key()
                .calculate_signature(&public_key, &mut rng)
                .map_err(SignalProtocolError::from)?;
            let timestamp = timestamp(now)?;
            let record = SignedPreKeyRecord::new(id.into(), timestamp, &pair, &signature);
            state
                .signed_keys
                .save_signed_pre_key(id.into(), &record)
                .await?;
            Output::SignedKey(SignedKey {
                id,
                public_key: public_key.into(),
                signature: signature.into_vec().into(),
                timestamp: timestamp.epoch_millis(),
            })
        }
        Operation::GenerateKyberPreKey { id, last_resort } => {
            if id == 0 || state.kyber_keys.contains(id) {
                return Err(Error::Input);
            }
            let pair = kem::KeyPair::generate(kem::KeyType::Kyber1024, &mut rng);
            let public_key = pair.public_key.serialize().into_vec();
            let identity = state.identities.get_identity_key_pair().await?;
            let signature = identity
                .private_key()
                .calculate_signature(&public_key, &mut rng)
                .map_err(SignalProtocolError::from)?;
            let timestamp = timestamp(now)?;
            let record = KyberPreKeyRecord::new(id.into(), timestamp, &pair, &signature);
            state.kyber_keys.insert(id, &record, last_resort)?;
            Output::SignedKey(SignedKey {
                id,
                public_key: public_key.into(),
                signature: signature.into_vec().into(),
                timestamp: timestamp.epoch_millis(),
            })
        }
        Operation::GetSignedPreKey { id } => {
            let record = state.signed_keys.get_signed_pre_key(id.into()).await?;
            Output::SignedKey(SignedKey {
                id,
                public_key: record.public_key()?.serialize().into_vec().into(),
                signature: record.signature()?.into(),
                timestamp: record.timestamp()?.epoch_millis(),
            })
        }
        Operation::GetKyberPreKey { id } => {
            let record = state.kyber_keys.get_kyber_pre_key(id.into()).await?;
            Output::SignedKey(SignedKey {
                id,
                public_key: record.public_key()?.serialize().into_vec().into(),
                signature: record.signature()?.into(),
                timestamp: record.timestamp()?.epoch_millis(),
            })
        }
        Operation::RemoveSignedPreKey { id } => {
            state.signed_keys.0.remove(&id);
            Output::Empty
        }
        Operation::RemoveKyberPreKey { id } => {
            state.kyber_keys.remove(id);
            Output::Empty
        }
        Operation::DeleteSession { peer } => {
            state.sessions.delete(&address(peer)?);
            Output::Empty
        }
        Operation::ProcessBundle { peer, bundle } => {
            let address = address(peer)?;
            let one_time = match bundle.pre_key {
                Some(key) => {
                    if key.id == 0 {
                        return Err(Error::Input);
                    }
                    Some((
                        key.id.into(),
                        PublicKey::deserialize(key.public_key.as_ref())
                            .map_err(SignalProtocolError::from)?,
                    ))
                }
                None => None,
            };
            let signed = bundle.signed_pre_key;
            let kyber = bundle.kyber_pre_key;
            if bundle.registration_id == 0 || signed.id == 0 || kyber.id == 0 {
                return Err(Error::Input);
            }
            let kyber_public = kem::PublicKey::deserialize(kyber.public_key.as_ref())?;
            if kyber_public.key_type() != kem::KeyType::Kyber1024 {
                return Err(Error::Input);
            }
            let bundle = PreKeyBundle::new(
                bundle.registration_id,
                address.device_id(),
                one_time,
                signed.id.into(),
                PublicKey::deserialize(signed.public_key.as_ref())
                    .map_err(SignalProtocolError::from)?,
                signed.signature.as_ref().to_vec(),
                kyber.id.into(),
                kyber_public,
                kyber.signature.as_ref().to_vec(),
                IdentityKey::decode(bundle.identity_key.as_ref())?,
            )?;
            process_prekey_bundle(
                &address,
                &mut state.sessions,
                &mut state.identities,
                &bundle,
                now,
                &mut rng,
            )
            .await?;
            Output::Empty
        }
        Operation::Encrypt { peer, plaintext } => {
            let address = address(peer)?;
            let message = message_encrypt(
                plaintext.as_ref(),
                &address,
                &mut state.sessions,
                &mut state.identities,
                now,
                &mut rng,
            )
            .await?;
            Output::Encrypted(Encrypted {
                kind: message.message_type() as u8,
                ciphertext: message.serialize().to_vec().into(),
            })
        }
        Operation::Decrypt {
            peer,
            kind,
            ciphertext,
        } => {
            let address = address(peer)?;
            let message = match kind {
                2 => {
                    CiphertextMessage::SignalMessage(SignalMessage::try_from(ciphertext.as_ref())?)
                }
                3 => {
                    let message = PreKeySignalMessage::try_from(ciphertext.as_ref())?;
                    if message.kyber_pre_key_id().is_none() {
                        return Err(Error::Input);
                    }
                    CiphertextMessage::PreKeySignalMessage(message)
                }
                _ => return Err(Error::Input),
            };
            let plaintext = message_decrypt(
                &message,
                &address,
                &mut state.sessions,
                &mut state.identities,
                &mut state.pre_keys,
                &state.signed_keys,
                &mut state.kyber_keys,
                &mut rng,
            )
            .await?;
            Output::Plaintext(plaintext.into())
        }
    };
    Ok((state, output))
}
