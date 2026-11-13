"""Генератор общих тест-векторов BFTv1 (см. Docs/BFTv1.md).

Независимая от .NET- и Android-библиотек реализация. Требует пакет `cryptography`.

    py -3 Docs/bft/generate_vectors.py                      # перезаписать bftv1-vectors.json
    py -3 Docs/bft/generate_vectors.py --cacophony FILE     # дополнительно сверить Noise NK
                                                            # с векторами cacophony (vectors/cacophony.txt)
"""
import base64
import hashlib
import hmac
import ipaddress
import json
import struct
import sys
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey, X25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import ChaCha20Poly1305

MAX_P, MAX_PAD = 16400, 1024
REKEY_INTERVAL = 65536


def aead(key, n, ad, pt):
    return ChaCha20Poly1305(key).encrypt(b"\0\0\0\0" + struct.pack("<Q", n), pt, ad)


def hmac256(key, data):
    return hmac.new(key, data, hashlib.sha256).digest()


def hkdf_extract(salt, ikm):
    return hmac256(salt, ikm)


def hkdf_expand32(prk, info):
    return hmac256(prk, info + b"\x01")


def x_pub(priv):
    return X25519PrivateKey.from_private_bytes(priv).public_key().public_bytes(
        serialization.Encoding.Raw, serialization.PublicFormat.Raw)


def x_dh(priv, pub):
    if pub == bytes(32):
        return bytes(32)  # точка малого порядка: результат нулевой
    return X25519PrivateKey.from_private_bytes(priv).exchange(X25519PublicKey.from_public_bytes(pub))


# ---------- слой записей ----------

def obfs_keys(obfs_key, salt):
    prk = hkdf_extract(salt, obfs_key)
    return {name: hkdf_expand32(prk, b"BFTv1 obfs " + name.encode())
            for name in ("c2s len", "c2s data", "s2c len", "s2c data")}


def record(k_len, k_dat, n, payload, pad_len=0, *, l_override=None, p_override=None, pad_byte=0):
    p = len(payload) if p_override is None else p_override
    inner = struct.pack(">H", p) + payload + bytes([pad_byte]) * pad_len
    body = aead(k_dat, n, b"", inner)
    length = len(body) if l_override is None else l_override
    return aead(k_len, n, b"", struct.pack(">H", length)) + body


# ---------- Noise_NK_25519_ChaChaPoly_SHA256 ----------

class Noise:
    def __init__(self, prologue, rs_pub):
        self.h = b"Noise_NK_25519_ChaChaPoly_SHA256"  # ровно 32 байта
        self.ck = self.h
        self.k, self.n = None, 0
        self.mix_hash(prologue)
        self.mix_hash(rs_pub)

    def mix_hash(self, data):
        self.h = hashlib.sha256(self.h + data).digest()

    def mix_key(self, ikm):
        temp = hmac256(self.ck, ikm)
        self.ck = hmac256(temp, b"\x01")
        self.k, self.n = hmac256(temp, self.ck + b"\x02"), 0

    def encrypt_and_hash(self, pt):
        ct = aead(self.k, self.n, self.h, pt)
        self.n += 1
        self.mix_hash(ct)
        return ct

    def split(self):
        temp = hmac256(self.ck, b"")
        k1 = hmac256(temp, b"\x01")
        return k1, hmac256(temp, k1 + b"\x02")


def noise_handshake(prologue, rs_priv, e_priv, re_priv, payload1=b"", payload2=b"", e_pub=None):
    """Возвращает (msg1, msg2, handshake_hash, k_c2s, k_s2c). e_pub задаётся только для вектора малого порядка."""
    rs_pub = x_pub(rs_priv)
    e_pub = x_pub(e_priv) if e_pub is None else e_pub
    s = Noise(prologue, rs_pub)
    s.mix_hash(e_pub)
    s.mix_key(x_dh(rs_priv, e_pub))  # es
    msg1 = e_pub + s.encrypt_and_hash(payload1)
    re_pub = x_pub(re_priv)
    s.mix_hash(re_pub)
    s.mix_key(x_dh(re_priv, e_pub))  # ee
    msg2 = re_pub + s.encrypt_and_hash(payload2)
    k1, k2 = s.split()
    return msg1, msg2, s.h, k1, k2


def noise_rekey(k):
    return aead(k, 2 ** 64 - 1, b"", bytes(32))[:32]


# ---------- кадры и преамбула ----------

def frame(ftype, stream_id, body=b""):
    return struct.pack(">BIH", ftype, stream_id, len(body)) + body


def relay_preamble(relay_key, salt, relay_nonce, ip, port, timestamp, relay_id):
    k = hkdf_expand32(hkdf_extract(salt, relay_key), b"BFTv1 relay preamble" + relay_nonce)
    addr = ipaddress.ip_address(ip)
    ip16 = addr.packed if addr.version == 6 else bytes(10) + b"\xff\xff" + addr.packed
    plain = ip16 + struct.pack(">HQ", port, timestamp) + relay_id.encode().ljust(16, b"\0")
    return relay_nonce + aead(k, 0, b"", plain)


def b64u(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def flip(data, index):
    out = bytearray(data)
    out[index] ^= 0x01
    return bytes(out)


def hx(value):
    if isinstance(value, bytes):
        return value.hex()
    if isinstance(value, dict):
        return {k: hx(v) for k, v in value.items()}
    if isinstance(value, list):
        return [hx(v) for v in value]
    return value


# ---------- сборка векторов ----------

def build():
    obfs_key = bytes(range(0x20, 0x40))
    salt = bytes(range(0x40, 0x60))
    exit_priv = bytes(range(0x01, 0x21))
    client_e = bytes(range(0x61, 0x81))
    server_e = bytes(range(0x81, 0xA1))
    relay_key = bytes(range(0xA1, 0xC1))
    timestamp = 1_800_000_000
    tls_spki_sha256 = bytes(range(32))
    exit_pub = x_pub(exit_priv)
    keys = obfs_keys(obfs_key, salt)
    c_len, c_dat, s_len, s_dat = keys["c2s len"], keys["c2s data"], keys["s2c len"], keys["s2c data"]

    def client_flight(*, okeys=keys, rs_priv=exit_priv, version=1, wrapper=0, ts=timestamp, e_pub=None, pad=5):
        header = struct.pack(">BBQ", version, wrapper, ts)
        msg1, msg2, hh, k1, k2 = noise_handshake(b"BFTv1" + salt + header, rs_priv, client_e, server_e, e_pub=e_pub)
        return salt + record(okeys["c2s len"], okeys["c2s data"], 0, header + msg1, pad), msg2, hh, k1, k2

    flight, msg2, hh, k_c2s, k_s2c = client_flight()
    server_first = record(s_len, s_dat, 0, msg2, 7)

    # транспорт: запись обфускации n = номер сообщения Noise + 1 (запись 0 — хендшейк)
    def hello(window, services=bytes([1, 4, 5])):
        return frame(0x02, 0, bytes([4]) + b"lv-1" + struct.pack(">IH", window, 256)
                     + tls_spki_sha256 + bytes([len(services)]) + services)

    frames = {
        "AUTH": frame(0x01, 0, b"token-1"),
        "HELLO": hello(262144),
        "OPEN": frame(0x03, 1, bytes([4])),
        "DATA": frame(0x04, 1, b"hello"),
        "WINDOW_UPDATE": frame(0x05, 1, struct.pack(">I", 65536)),
        "CLOSE": frame(0x06, 1),
        "RESET": frame(0x07, 1, bytes([1])),
        "PING": frame(0x08, 0, bytes(range(8))),
        "PONG": frame(0x09, 0, bytes(range(8))),
        "GOAWAY": frame(0x0A, 0, struct.pack(">IB", 3, 0)),
    }

    def transport(direction, noise_n, plaintext, pad, key=None):
        k_noise = key or (k_c2s if direction == "c2s" else k_s2c)
        kl, kd = (c_len, c_dat) if direction == "c2s" else (s_len, s_dat)
        msg = aead(k_noise, noise_n, b"", plaintext)
        return {"direction": direction, "noiseN": noise_n, "recordN": noise_n + 1, "plaintext": plaintext,
                "padLen": pad, "record": record(kl, kd, noise_n + 1, msg, pad)}

    k_c2s_rekeyed = noise_rekey(k_c2s)
    transport_vectors = [
        transport("c2s", 0, frames["AUTH"], 0),
        transport("s2c", 0, frames["HELLO"], 3),
        transport("c2s", 1, frames["OPEN"] + frames["DATA"], 0),
        transport("s2c", 1, b"", 16),
        transport("c2s", REKEY_INTERVAL - 1, frames["PING"], 0),
        transport("c2s", REKEY_INTERVAL, frames["PING"], 0, key=k_c2s_rekeyed),
    ]

    rec0 = record(c_len, c_dat, 0, b"first")
    rec1 = record(c_len, c_dat, 1, b"second", 4)

    def rec_neg(name, stream, why):
        return {"name": name, "keys": "c2s", "startN": 0, "stream": stream, "why": why}

    record_negative = [
        rec_neg("len-ciphertext-bit", flip(rec0, 0), "изменён бит зашифрованной длины"),
        rec_neg("len-tag-bit", flip(rec0, 17), "изменён бит тега длины"),
        rec_neg("body-bit", flip(rec0, 18), "изменён бит тела"),
        rec_neg("body-tag-bit", flip(rec0, len(rec0) - 1), "изменён бит тега тела"),
        rec_neg("length-too-small", record(c_len, c_dat, 0, b"", l_override=17) + bytes(17), "L = 17"),
        rec_neg("length-too-large", record(c_len, c_dat, 0, b"", l_override=17443), "L = 17443"),
        rec_neg("payload-longer-than-body", record(c_len, c_dat, 0, b"abc", p_override=4), "P больше тела"),
        rec_neg("nonzero-padding", record(c_len, c_dat, 0, b"abc", 4, pad_byte=1), "ненулевой паддинг"),
        rec_neg("replay", rec0 + rec0, "вторая запись — повтор первой"),
        rec_neg("reorder", rec1 + rec0, "записи переставлены"),
        rec_neg("truncated", rec0[:-1], "обрыв посреди записи"),
    ]

    other_obfs = obfs_keys(bytes(32), salt)
    other_exit = bytes(range(0x02, 0x22))
    handshake_negative = [
        {"name": "wrong-obfs-key", "now": timestamp, "flight": client_flight(okeys=other_obfs)[0]},
        {"name": "wrong-exit-static-key", "now": timestamp, "flight": client_flight(rs_priv=other_exit)[0]},
        {"name": "low-order-ephemeral", "now": timestamp, "flight": client_flight(e_pub=bytes(32))[0]},
        {"name": "bad-version", "now": timestamp, "flight": client_flight(version=2)[0]},
        {"name": "bad-wrapper", "now": timestamp, "flight": client_flight(wrapper=1)[0]},
        {"name": "timestamp-too-old", "now": timestamp + 121, "flight": flight},
        {"name": "timestamp-in-future", "now": timestamp - 121, "flight": flight},
        {"name": "salt-replay", "now": timestamp, "flight": flight, "precondition": "salt уже в кеше"},
    ]

    frame_negative = [
        {"name": "unknown-type", "bytes": frame(0x0B, 0)},
        {"name": "length-beyond-message", "bytes": struct.pack(">BIH", 0x04, 1, 10) + b"short"},
        {"name": "truncated-header", "bytes": frame(0x04, 1, b"x")[:5]},
        {"name": "data-on-stream-0", "bytes": frame(0x04, 0, b"x")},
        {"name": "empty-data", "bytes": frame(0x04, 1)},
        {"name": "ping-on-stream", "bytes": frame(0x08, 1, bytes(8))},
        {"name": "ping-wrong-length", "bytes": frame(0x08, 0, bytes(7))},
        {"name": "window-update-zero", "bytes": frame(0x05, 1, struct.pack(">I", 0))},
        {"name": "open-without-service", "bytes": frame(0x03, 1)},
        {"name": "close-with-body", "bytes": frame(0x06, 1, b"x")},
        {"name": "hello-window-zero", "bytes": hello(0)},
        {"name": "hello-window-too-large", "bytes": hello(2 ** 31)},
        {"name": "hello-duplicate-service", "bytes": hello(262144, services=bytes([1, 1]))},
    ]

    # получатель обрабатывает кадры по порядку; expect — действие на последний кадр
    f = frames
    open2 = frame(0x03, 2, bytes([4]))
    mux_sequences = [
        {"name": "open-skips-id", "side": "exit", "frames": [open2], "expect": "protocol-error"},
        {"name": "open-repeats-id", "side": "exit", "frames": [f["OPEN"], f["OPEN"]], "expect": "protocol-error"},
        {"name": "open-closed-id", "side": "exit", "frames": [f["OPEN"], f["RESET"], f["OPEN"]],
         "expect": "protocol-error"},
        {"name": "data-on-idle-stream", "side": "exit", "frames": [f["DATA"]], "expect": "protocol-error"},
        {"name": "reset-on-idle-stream", "side": "exit", "frames": [f["RESET"]], "expect": "protocol-error"},
        {"name": "data-after-close", "side": "exit", "frames": [f["OPEN"], f["CLOSE"], f["DATA"]],
         "expect": "reset:7"},
        {"name": "close-after-close", "side": "exit", "frames": [f["OPEN"], f["CLOSE"], f["CLOSE"]],
         "expect": "reset:7"},
        {"name": "window-update-after-close", "side": "exit",
         "frames": [f["OPEN"], f["CLOSE"], f["WINDOW_UPDATE"]], "expect": "process"},
        {"name": "data-after-reset", "side": "exit", "frames": [f["OPEN"], f["RESET"], f["DATA"]],
         "expect": "ignore"},
        {"name": "reset-after-reset", "side": "exit", "frames": [f["OPEN"], f["RESET"], f["RESET"]],
         "expect": "ignore"},
        {"name": "data-beyond-window", "side": "exit", "initialWindow": 16384,
         "frames": [f["OPEN"], frame(0x04, 1, bytes(16377)), frame(0x04, 1, bytes(8))], "expect": "reset:3"},
        {"name": "open-after-goaway-sent", "side": "exit", "sentGoawayLastStreamId": 1,
         "frames": [f["OPEN"], open2, frame(0x04, 2, b"x")], "expect": "ignore"},
        {"name": "goaway-from-client", "side": "exit", "frames": [f["GOAWAY"]], "expect": "protocol-error"},
        {"name": "open-from-exit", "side": "client", "frames": [f["HELLO"], f["OPEN"]], "expect": "protocol-error"},
        {"name": "second-hello", "side": "client", "frames": [f["HELLO"], f["HELLO"]], "expect": "protocol-error"},
        {"name": "data-on-unopened-stream", "side": "client", "frames": [f["HELLO"], f["DATA"]],
         "expect": "protocol-error"},
    ]

    def preamble(ip, port, ts=timestamp, relay_id="ru-1", psalt=salt, nonce=bytes(range(0xC1, 0xD1))):
        return {"relayNonce": nonce, "clientIp": ip, "clientPort": port, "timestamp": ts, "relayId": relay_id,
                "preamble": relay_preamble(relay_key, psalt, nonce, ip, port, ts, relay_id)}

    p4 = preamble("198.51.100.7", 51234)
    relay_negative = [
        {"name": "other-salt", "now": timestamp, "salt": salt,
         "preamble": preamble("198.51.100.7", 51234, psalt=bytes(32))["preamble"]},
        {"name": "nonce-bit-flip", "now": timestamp, "salt": salt, "preamble": flip(p4["preamble"], 3)},
        {"name": "ciphertext-bit-flip", "now": timestamp, "salt": salt, "preamble": flip(p4["preamble"], 20)},
        {"name": "expired", "now": timestamp + 121, "salt": salt, "preamble": p4["preamble"]},
    ]

    token = "t0ken/+ 1"
    return hx({
        "version": 1,
        "connectionString": {
            "string": f"bft://{b64u(exit_pub)}.{b64u(obfs_key)}@proxy.example.org:8443?token=t0ken%2F%2B%201",
            "staticPublicKey": exit_pub, "obfsKey": obfs_key, "host": "proxy.example.org", "port": 8443,
            "token": token,
            "invalid": [
                f"bft://{b64u(exit_pub)}.{b64u(obfs_key)}@proxy.example.org",
                f"bft://{b64u(exit_pub)[:-1]}.{b64u(obfs_key)}@proxy.example.org:8443",
                f"bft://{b64u(exit_pub)}@proxy.example.org:8443",
                f"bft://{b64u(exit_pub)}.{b64u(obfs_key)}@proxy.example.org:8443?other=1",
                f"http://{b64u(exit_pub)}.{b64u(obfs_key)}@proxy.example.org:8443",
            ],
        },
        "obfsKeys": {"obfsKey": obfs_key, "salt": salt, "c2sLen": c_len, "c2sData": c_dat,
                     "s2cLen": s_len, "s2cData": s_dat},
        "records": [
            {"keys": "c2s", "n": 0, "payload": b"first", "padLen": 0, "record": rec0},
            {"keys": "c2s", "n": 1, "payload": b"second", "padLen": 4, "record": rec1},
            {"keys": "s2c", "n": 0, "payload": b"", "padLen": MAX_PAD, "record": record(s_len, s_dat, 0, b"", MAX_PAD)},
            {"keys": "s2c", "n": 1, "payload": bytes(MAX_P), "padLen": 0, "record": record(s_len, s_dat, 1, bytes(MAX_P))},
        ],
        "handshake": {
            "exitStaticPrivate": exit_priv, "exitStaticPublic": exit_pub,
            "clientEphemeralPrivate": client_e, "serverEphemeralPrivate": server_e,
            "timestamp": timestamp, "clientPadLen": 5, "serverPadLen": 7,
            "clientFlight": flight, "serverFirstRecord": server_first,
            "handshakeHash": hh, "c2sKey": k_c2s, "s2cKey": k_s2c, "c2sKeyAfterRekey": k_c2s_rekeyed,
        },
        "transport": transport_vectors,
        "frames": frames,
        "tlsSpkiSha256": tls_spki_sha256,
        "muxSequences": mux_sequences,
        "relayPreamble": {"relayKey": relay_key, "salt": salt,
                          "valid": [p4, preamble("2001:db8::1", 443, relay_id="relay-0123456789",
                                                  nonce=bytes(range(0xD1, 0xE1)))]},
        "negative": {"records": record_negative, "handshake": handshake_negative,
                     "frames": frame_negative, "relayPreamble": relay_negative},
    })


def check_cacophony(path):
    vectors = json.loads(Path(path).read_text(encoding="utf-8"))["vectors"]
    checked = 0
    for v in vectors:
        name = v.get("protocol_name") or v.get("name")
        if name != "Noise_NK_25519_ChaChaPoly_SHA256":
            continue
        m = v["messages"]
        msg1, msg2, hh, k1, k2 = noise_handshake(
            bytes.fromhex(v["init_prologue"]), bytes.fromhex(v["resp_static"]),
            bytes.fromhex(v["init_ephemeral"]), bytes.fromhex(v["resp_ephemeral"]),
            bytes.fromhex(m[0]["payload"]), bytes.fromhex(m[1]["payload"]))
        assert msg1.hex() == m[0]["ciphertext"], "msg1"
        assert msg2.hex() == m[1]["ciphertext"], "msg2"
        if "handshake_hash" in v:
            assert hh.hex() == v["handshake_hash"], "handshake hash"
        counters = [0, 0]
        for i, msg in enumerate(m[2:]):
            side = i % 2  # сообщение 3 шлёт инициатор, далее по очереди
            ct = aead((k1, k2)[side], counters[side], b"", bytes.fromhex(msg["payload"]))
            counters[side] += 1
            assert ct.hex() == msg["ciphertext"], f"transport {i}"
        checked += 1
    assert checked, "в файле нет векторов Noise_NK_25519_ChaChaPoly_SHA256"
    print(f"cacophony: OK, векторов сверено: {checked}")


if __name__ == "__main__":
    if "--cacophony" in sys.argv:
        check_cacophony(sys.argv[sys.argv.index("--cacophony") + 1])
    out = Path(__file__).with_name("bftv1-vectors.json")
    out.write_text(json.dumps(build(), indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"written {out}")
