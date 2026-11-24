import assert from 'node:assert/strict';
import { createInterface } from 'node:readline';
import * as signal from '@signalapp/libsignal-client';

const encode = bytes => Buffer.from(bytes).toString('base64');
const decode = text => Buffer.from(text, 'base64');
const peer = signal.ProtocolAddress.new('windows-device', 1);

class Sessions extends signal.SessionStore {
    record = null;
    async saveSession(_, record) { this.record = record; }
    async getSession() { return this.record; }
    async getExistingSessions() { return this.record ? [this.record] : []; }
}

class Identities extends signal.IdentityKeyStore {
    local = signal.PrivateKey.generate();
    remote = null;
    async getIdentityKey() { return this.local; }
    async getLocalRegistrationId() { return 42; }
    async getIdentity() { return this.remote; }
    async isTrustedIdentity(_, key) {
        return !this.remote || encode(this.remote.serialize()) === encode(key.serialize());
    }
    async saveIdentity(_, key) {
        const trusted = await this.isTrustedIdentity(_, key);
        this.remote = key;
        return trusted ? signal.IdentityChange.NewOrUnchanged : signal.IdentityChange.ReplacedExisting;
    }
}

class PreKeys extends signal.PreKeyStore {
    record = null;
    async savePreKey(_, record) { this.record = record; }
    async getPreKey(id) { assert.equal(this.record?.id(), id); return this.record; }
    async removePreKey(id) { assert.equal(this.record?.id(), id); this.record = null; }
}

class SignedKeys extends signal.SignedPreKeyStore {
    record = null;
    async saveSignedPreKey(_, record) { this.record = record; }
    async getSignedPreKey(id) { assert.equal(this.record?.id(), id); return this.record; }
}

class KyberKeys extends signal.KyberPreKeyStore {
    record = null;
    async saveKyberPreKey(_, record) { this.record = record; }
    async getKyberPreKey(id) { assert.equal(this.record?.id(), id); return this.record; }
    async markKyberPreKeyUsed(id) { assert.equal(this.record?.id(), id); this.record = null; }
}

const sessions = new Sessions();
const identities = new Identities();
const preKeys = new PreKeys();
const signedKeys = new SignedKeys();
const kyberKeys = new KyberKeys();

async function run(command) {
    switch (command.operation) {
        case 'bundle': {
            const ec = signal.PrivateKey.generate();
            const signed = signal.PrivateKey.generate();
            const kyber = signal.KEMKeyPair.generate();
            const sign = key => identities.local.sign(key.serialize());
            const signedPublic = signed.getPublicKey();
            const kyberPublic = kyber.getPublicKey();
            const now = Date.now();
            preKeys.record = signal.PreKeyRecord.new(1, ec.getPublicKey(), ec);
            signedKeys.record = signal.SignedPreKeyRecord.new(2, now, signedPublic, signed, sign(signedPublic));
            kyberKeys.record = signal.KyberPreKeyRecord.new(3, now, kyber, sign(kyberPublic));
            const publicKey = record => ({ id: record.id(), publicKey: encode(record.publicKey().serialize()) });
            const signedKey = record => ({ ...publicKey(record), signature: encode(record.signature()) });
            return {
                registrationId: await identities.getLocalRegistrationId(),
                identityKey: encode(identities.local.getPublicKey().serialize()),
                preKey: publicKey(preKeys.record),
                signedPreKey: signedKey(signedKeys.record),
                kyberPreKey: signedKey(kyberKeys.record),
            };
        }
        case 'processBundle': {
            const { registrationId, identityKey, preKey, signedPreKey, kyberPreKey } = command.bundle;
            const publicKey = value => signal.PublicKey.deserialize(decode(value));
            const bundle = signal.PreKeyBundle.new(registrationId, 1,
                preKey?.id ?? null, preKey ? publicKey(preKey.publicKey) : null,
                signedPreKey.id, publicKey(signedPreKey.publicKey), decode(signedPreKey.signature),
                publicKey(identityKey),
                kyberPreKey.id, signal.KEMPublicKey.deserialize(decode(kyberPreKey.publicKey)),
                decode(kyberPreKey.signature));
            await signal.processPreKeyBundle(bundle, peer, sessions, identities);
            return true;
        }
        case 'encrypt': {
            const message = await signal.signalEncrypt(decode(command.plaintext), peer, sessions, identities);
            return { kind: message.type(), ciphertext: encode(message.serialize()) };
        }
        case 'decrypt': {
            const ciphertext = decode(command.message.ciphertext);
            if (command.message.kind === signal.CiphertextMessageType.PreKey) {
                const message = signal.PreKeySignalMessage.deserialize(ciphertext);
                assert.equal(message.version(), 4);
                return encode(await signal.signalDecryptPreKey(
                    message, peer, sessions, identities, preKeys, signedKeys, kyberKeys));
            }
            assert.equal(command.message.kind, signal.CiphertextMessageType.Whisper);
            return encode(await signal.signalDecrypt(
                signal.SignalMessage.deserialize(ciphertext), peer, sessions, identities));
        }
        default:
            throw new Error('Unknown test operation');
    }
}

for await (const line of createInterface({ input: process.stdin })) {
    try {
        process.stdout.write(JSON.stringify(await run(JSON.parse(line))) + '\n');
    } catch (error) {
        process.stderr.write(error.stack + '\n');
        process.exitCode = 1;
        break;
    }
}
