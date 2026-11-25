use std::collections::BTreeMap;

use async_trait::async_trait;
use libsignal_protocol::*;
use serde::{Deserialize, Serialize};

use crate::Bytes;

type Result<T> = std::result::Result<T, SignalProtocolError>;
pub const SNAPSHOT_VERSION: u32 = 1;
pub const REUSED_BASE_KEY: &str = "reused base key";

#[derive(Deserialize, Serialize)]
pub struct State {
    pub version: u32,
    #[serde(default)]
    pub generation: u64,
    pub identity: Bytes,
    pub registration_id: u32,
    pub identities: Identities,
    pub sessions: Sessions,
    pub pre_keys: PreKeys,
    pub signed_keys: SignedKeys,
    pub kyber_keys: KyberKeys,
}

impl State {
    pub fn new(identity: &IdentityKeyPair, registration_id: u32) -> Self {
        Self {
            version: SNAPSHOT_VERSION,
            generation: 1,
            identity: identity.serialize().into_vec().into(),
            registration_id,
            identities: Identities {
                local: Some((*identity, registration_id)),
                peers: BTreeMap::new(),
            },
            sessions: Sessions::default(),
            pre_keys: PreKeys::default(),
            signed_keys: SignedKeys::default(),
            kyber_keys: KyberKeys::default(),
        }
    }

    pub fn restore_identity(&mut self) -> Result<()> {
        self.identities.local = Some((
            IdentityKeyPair::try_from(self.identity.as_ref())?,
            self.registration_id,
        ));
        Ok(())
    }
}

#[derive(Deserialize, Serialize)]
pub struct Identities {
    #[serde(skip)]
    local: Option<(IdentityKeyPair, u32)>,
    peers: BTreeMap<String, Bytes>,
}

#[async_trait(?Send)]
impl IdentityKeyStore for Identities {
    async fn get_identity_key_pair(&self) -> Result<IdentityKeyPair> {
        self.local
            .map(|(key, _)| key)
            .ok_or_else(|| SignalProtocolError::InvalidArgument("missing identity".into()))
    }

    async fn get_local_registration_id(&self) -> Result<u32> {
        self.local
            .map(|(_, id)| id)
            .ok_or_else(|| SignalProtocolError::InvalidArgument("missing identity".into()))
    }

    async fn save_identity(
        &mut self,
        address: &ProtocolAddress,
        key: &IdentityKey,
    ) -> Result<IdentityChange> {
        let changed = self
            .get_identity(address)
            .await?
            .is_some_and(|old| old != *key);
        self.peers
            .insert(address.name().into(), key.serialize().into_vec().into());
        Ok(IdentityChange::from_changed(changed))
    }

    async fn is_trusted_identity(
        &self,
        address: &ProtocolAddress,
        key: &IdentityKey,
        _: Direction,
    ) -> Result<bool> {
        Ok(self
            .get_identity(address)
            .await?
            .is_none_or(|old| old == *key))
    }

    async fn get_identity(&self, address: &ProtocolAddress) -> Result<Option<IdentityKey>> {
        self.peers
            .get(address.name())
            .map(|key| IdentityKey::decode(key.as_ref()))
            .transpose()
    }
}

#[derive(Default, Deserialize, Serialize)]
pub struct Sessions(BTreeMap<String, Bytes>);

impl Sessions {
    pub fn delete(&mut self, address: &ProtocolAddress) {
        self.0.remove(address.name());
    }
}

#[async_trait(?Send)]
impl SessionStore for Sessions {
    async fn load_session(&self, address: &ProtocolAddress) -> Result<Option<SessionRecord>> {
        self.0
            .get(address.name())
            .map(|record| SessionRecord::deserialize(record.as_ref()))
            .transpose()
    }

    async fn store_session(
        &mut self,
        address: &ProtocolAddress,
        record: &SessionRecord,
    ) -> Result<()> {
        self.0
            .insert(address.name().into(), record.serialize()?.into());
        Ok(())
    }
}

#[derive(Default, Deserialize, Serialize)]
pub struct PreKeys(pub BTreeMap<u32, Bytes>);

#[async_trait(?Send)]
impl PreKeyStore for PreKeys {
    async fn get_pre_key(&self, id: PreKeyId) -> Result<PreKeyRecord> {
        let record = self
            .0
            .get(&id.into())
            .ok_or(SignalProtocolError::InvalidPreKeyId)?;
        PreKeyRecord::deserialize(record.as_ref())
    }

    async fn save_pre_key(&mut self, id: PreKeyId, record: &PreKeyRecord) -> Result<()> {
        self.0.insert(id.into(), record.serialize()?.into());
        Ok(())
    }

    async fn remove_pre_key(&mut self, id: PreKeyId) -> Result<()> {
        self.0.remove(&id.into());
        Ok(())
    }
}

#[derive(Default, Deserialize, Serialize)]
pub struct SignedKeys(pub BTreeMap<u32, Bytes>);

#[async_trait(?Send)]
impl SignedPreKeyStore for SignedKeys {
    async fn get_signed_pre_key(&self, id: SignedPreKeyId) -> Result<SignedPreKeyRecord> {
        let record = self
            .0
            .get(&id.into())
            .ok_or(SignalProtocolError::InvalidSignedPreKeyId)?;
        SignedPreKeyRecord::deserialize(record.as_ref())
    }

    async fn save_signed_pre_key(
        &mut self,
        id: SignedPreKeyId,
        record: &SignedPreKeyRecord,
    ) -> Result<()> {
        self.0.insert(id.into(), record.serialize()?.into());
        Ok(())
    }
}

#[derive(Deserialize, Serialize)]
pub struct KyberEntry {
    record: Bytes,
    // Наличие карты означает last-resort. Ключи повтора хранятся вместе с записью.
    seen: Option<BTreeMap<u32, Vec<Bytes>>>,
}

#[derive(Default, Deserialize, Serialize)]
pub struct KyberKeys(BTreeMap<u32, KyberEntry>);

impl KyberKeys {
    pub fn contains(&self, id: u32) -> bool {
        self.0.contains_key(&id)
    }

    pub fn remove(&mut self, id: u32) {
        self.0.remove(&id);
    }

    pub fn insert(&mut self, id: u32, record: &KyberPreKeyRecord, last_resort: bool) -> Result<()> {
        self.0.insert(
            id,
            KyberEntry {
                record: record.serialize()?.into(),
                seen: last_resort.then(BTreeMap::new),
            },
        );
        Ok(())
    }
}

#[async_trait(?Send)]
impl KyberPreKeyStore for KyberKeys {
    async fn get_kyber_pre_key(&self, id: KyberPreKeyId) -> Result<KyberPreKeyRecord> {
        let entry = self
            .0
            .get(&id.into())
            .ok_or(SignalProtocolError::InvalidKyberPreKeyId)?;
        KyberPreKeyRecord::deserialize(entry.record.as_ref())
    }

    async fn save_kyber_pre_key(
        &mut self,
        id: KyberPreKeyId,
        record: &KyberPreKeyRecord,
    ) -> Result<()> {
        let id = id.into();
        if let Some(entry) = self.0.get_mut(&id) {
            entry.record = record.serialize()?.into();
            return Ok(());
        }
        self.insert(id, record, false)
    }

    async fn mark_kyber_pre_key_used(
        &mut self,
        id: KyberPreKeyId,
        signed_id: SignedPreKeyId,
        base_key: &PublicKey,
    ) -> Result<()> {
        let id = id.into();
        let entry = self
            .0
            .get_mut(&id)
            .ok_or(SignalProtocolError::InvalidKyberPreKeyId)?;
        if let Some(seen) = &mut entry.seen {
            // Как в Android, маркер живёт столько же, сколько last-resort ключ.
            // При удалении ключа удаляется вся его история. Общий лимит снимка
            // ограничивает рост без вытеснения маркеров для действующих ключей.
            let keys = seen.entry(signed_id.into()).or_default();
            let key = base_key.serialize();
            if keys.iter().any(|old| old.as_ref() == key.as_ref()) {
                return Err(SignalProtocolError::InvalidMessage(
                    CiphertextMessageType::PreKey,
                    REUSED_BASE_KEY,
                ));
            }
            keys.push(key.into_vec().into());
        } else {
            self.remove(id);
        }
        Ok(())
    }
}
