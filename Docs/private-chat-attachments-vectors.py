"""Deterministic fixtures only. The keys and nonces below are public."""

import argparse
import hashlib
import io
import json
from importlib.metadata import version
from pathlib import Path
from tempfile import TemporaryDirectory

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF


SEGMENT = 4096
HEADER = 40
TAG = 16
CHAT_KEY = bytes(range(32))
FILE_KEY = bytes(range(32, 64))
SALT = bytes(range(64, 96))
PREFIX = bytes(range(96, 103))
MESSAGE_NONCE = bytes(range(12))
CHAT_ID = b"00000000-0000-4000-8000-000000000001"
LEGACY_AAD = b"barkfluff:private:" + CHAT_ID
PAYLOAD_AAD = b"barkfluff:private:attachments:v1:" + CHAT_ID
FILE_INFO = b"barkfluff:private:attachments:v1:file"
EXPECTED = {
    "legacy": "979d079b155d12a95df446099014095cd3c45d993355a113b05f1c5664",
    "payload": (
        "1949b8c6c44e653b0317753341e713434b2d0eba9a5dea817a15346a86cf5b7d6d88b"
        "db9fd83a6753e38132adfb0917207fa1fb5ec00f9c17a11747cba9145ab2c06e3852f"
        "aa25040ac1c18aea5099ff05807df3a0a84cd7e316b775bda3130ea05387e685ef739"
        "dd98c6f1058d057729a00053c384423e5183511bf2d51785c9b73ce34fe3452bbe258"
        "554651f9f91717aa4cd31a1aa39d2873bb8078c0491c812bc2b20b1ed410a0acf417"
        "5881c04b92adc0d60f36ff9fb3208b7d6dcfe12894326a27ecc751c443d75f302382"
        "c60806a30f19926f7c8ba5cc33a1da3cd66c6b81c1eff3d67112f83dd23fc68e15"
        "aed581bad58a0128d4d0ec9d09d32bcb9ed4e48dfe9d4514773210a12081015170d"
        "5b133ff7c73fcfaaf6dac4a8504253907361b0495351705a7c1eedc0f78"
    ),
    "one": "28404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465661b18b845f6416256cb2dd5951b327972446d64",
    "empty": "28404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f606162636465664aa2f5703b4e35e7af23d9fde509dead",
    # SHA-256 of the complete multi-segment ciphertext (including header and all tags).
    "multi": "8e558c43e4918e1701e342191cdc69d60b49c249d39725a5faadb227728119ee",
}


def derive_key(key, salt, info):
    return HKDF(algorithm=hashes.SHA256(), length=32, salt=salt, info=info).derive(key)


def plaintext_segment_sizes(size):
    first = min(size, SEGMENT - HEADER - TAG)
    yield first
    remaining = size - first
    while remaining:
        length = min(remaining, SEGMENT - TAG)
        yield length
        remaining -= length


def nonce(prefix, index, final):
    return prefix + index.to_bytes(4, "big") + bytes([final])


def encrypt_file(plaintext):
    aead = AESGCM(derive_key(FILE_KEY, SALT, FILE_INFO))
    parts = [bytes([HEADER]) + SALT + PREFIX]
    offset = 0
    for index, length in enumerate(plaintext_segment_sizes(len(plaintext))):
        end = offset + length
        parts.append(aead.encrypt(nonce(PREFIX, index, end == len(plaintext)),
                                  plaintext[offset:end], None))
        offset = end
    return b"".join(parts)


def decrypt_file(ciphertext, size, expected_hash):
    lengths = list(plaintext_segment_sizes(size))
    if len(ciphertext) != HEADER + size + TAG * len(lengths):
        raise ValueError("ciphertext length mismatch")
    if ciphertext[0] != HEADER:
        raise ValueError("invalid header length")

    aead = AESGCM(derive_key(FILE_KEY, ciphertext[1:33], FILE_INFO))
    prefix = ciphertext[33:HEADER]
    parts = []
    offset = HEADER
    for index, length in enumerate(lengths):
        end = offset + length + TAG
        try:
            parts.append(aead.decrypt(nonce(prefix, index, index == len(lengths) - 1),
                                      ciphertext[offset:end], None))
        except InvalidTag as exc:
            raise InvalidTag(f"file segment {index}") from exc
        offset = end
    plaintext = b"".join(parts)
    if hashlib.sha256(plaintext).digest() != expected_hash:
        raise ValueError("plaintext hash mismatch")
    return plaintext


def check_json_fields(payload):
    if payload.startswith(b"\xef\xbb\xbf"):
        raise ValueError("UTF-8 BOM")

    def unique_object(pairs):
        fields = {}
        for name, value in pairs:
            if name in fields:
                raise ValueError(f"duplicate JSON field {name}")
            fields[name] = value
        return fields

    def check_fields(obj, required, optional=()):
        if not isinstance(obj, dict) or not required <= obj.keys():
            raise ValueError("missing required JSON fields")
        for name in obj.keys() - required - set(optional):
            raise ValueError(f"unknown JSON field {name}")

    manifest = json.loads(payload.decode("utf-8"), object_pairs_hook=unique_object)
    check_fields(manifest, {"text", "attachments"})
    descriptor_fields = {"fileId", "name", "mime", "size", "sha256", "key"}
    for attachment in manifest["attachments"]:
        check_fields(attachment, descriptor_fields, ("preview",))
        if "preview" in attachment:
            check_fields(attachment["preview"], descriptor_fields)
    return manifest


def compare_with_tink(files, fixtures):
    try:
        import tink
    except ModuleNotFoundError as exc:
        if exc.name != "tink":
            raise
        print("SKIP Tink: optional tink package is not installed")
        return

    from tink import secret_key_access, streaming_aead
    from tink.proto import aes_gcm_hkdf_streaming_pb2, tink_pb2

    streaming_aead.register()
    template = streaming_aead.streaming_aead_key_templates.AES256_GCM_HKDF_4KB
    key_format = aes_gcm_hkdf_streaming_pb2.AesGcmHkdfStreamingKeyFormat.FromString(
        template.value)
    key = aes_gcm_hkdf_streaming_pb2.AesGcmHkdfStreamingKey(
        version=0, params=key_format.params, key_value=FILE_KEY)
    keyset = tink_pb2.Keyset(primary_key_id=1, key=[tink_pb2.Keyset.Key(
        key_id=1, status=tink_pb2.ENABLED, output_prefix_type=template.output_prefix_type,
        key_data=tink_pb2.KeyData(type_url=template.type_url, value=key.SerializeToString(),
                                  key_material_type=tink_pb2.KeyData.SYMMETRIC))])
    handle = tink.proto_keyset_format.parse(keyset.SerializeToString(), secret_key_access.TOKEN)
    stream_aead = handle.primitive(streaming_aead.StreamingAead)

    with TemporaryDirectory() as temporary_directory:
        ciphertext_path = Path(temporary_directory) / "tink-ciphertext"
        for name, plaintext in files.items():
            with stream_aead.new_decrypting_stream(io.BytesIO(fixtures[name]), FILE_INFO) as stream:
                require(stream.read() == plaintext, f"Tink decrypts {name} fixture")
            with ciphertext_path.open("wb") as output:
                with stream_aead.new_encrypting_stream(output, FILE_INFO) as stream:
                    stream.write(plaintext)
            require(decrypt_file(ciphertext_path.read_bytes(), len(plaintext),
                                 hashlib.sha256(plaintext).digest()) == plaintext,
                    f"fixture reader decrypts Tink {name} ciphertext")
    print(f"PASS Tink {version('tink')}: both directions for {', '.join(files)}")


def require(condition, description):
    if not condition:
        raise AssertionError(description)


def reject(operation, error, description, detail=None):
    try:
        operation()
    except error as exc:
        require(detail is None or str(exc) == detail, f"{description}: unexpected {exc!r}")
        check = f"{type(exc).__name__}: {exc}" if str(exc) else type(exc).__name__
        print(f"PASS reject {description} -> {check}")
        return
    raise AssertionError(f"accepted {description}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--emit", action="store_true", help="print full fixture bytes in hex")
    args = parser.parse_args()

    legacy = "Привет!".encode("utf-8")
    legacy_ciphertext = AESGCM(CHAT_KEY).encrypt(MESSAGE_NONCE, legacy, LEGACY_AAD)
    files = {"one": b"abc", "multi": bytes(i % 251 for i in range(12201)), "empty": b""}
    manifest = {
        "text": "Привет!",
        "attachments": [{
            "fileId": "00000000-0000-4000-8000-000000000002",
            "name": "note.txt",
            "mime": "text/plain",
            "size": len(files["one"]),
            "sha256": hashlib.sha256(files["one"]).hexdigest(),
            "key": FILE_KEY.hex(),
        }],
    }
    payload = json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    payload_aead = AESGCM(derive_key(CHAT_KEY, bytes(32), PAYLOAD_AAD))
    payload_ciphertext = payload_aead.encrypt(MESSAGE_NONCE, payload, PAYLOAD_AAD)
    fixtures = {"legacy": legacy_ciphertext, "payload": payload_ciphertext}
    fixtures.update({name: encrypt_file(plaintext) for name, plaintext in files.items()})
    if args.emit:
        print(json.dumps({
            "legacyPlaintext": legacy.hex(),
            "payloadPlaintext": payload.hex(),
            **{name: blob.hex() for name, blob in fixtures.items()},
        }, indent=2), flush=True)

    require(legacy.hex() == "d09fd180d0b8d0b2d0b5d18221", "legacy UTF-8 bytes")
    require(payload_aead.decrypt(MESSAGE_NONCE, payload_ciphertext, PAYLOAD_AAD) == payload,
            "payload round trip")
    require(check_json_fields(payload) == manifest, "payload JSON fields")
    reject(lambda: AESGCM(CHAT_KEY).decrypt(MESSAGE_NONCE, payload_ciphertext, PAYLOAD_AAD),
           InvalidTag, "v1 payload with legacy key")
    wrong_aad = PAYLOAD_AAD[:-1] + b"2"
    reject(lambda: payload_aead.decrypt(MESSAGE_NONCE, payload_ciphertext, wrong_aad),
           InvalidTag, "v1 payload with another chat AAD (same derived key)")
    for name, bad, detail in [
        ("duplicate key", b'{"te\\u0078t":"duplicate",' + payload[1:],
         "duplicate JSON field text"),
        ("extra field", payload[:-1] + b',"extra":null}', "unknown JSON field extra"),
        ("BOM", b"\xef\xbb\xbf" + payload, "UTF-8 BOM"),
    ]:
        reject(lambda: check_json_fields(bad), ValueError, f"JSON {name}", detail)
    for name, plaintext in files.items():
        require(decrypt_file(fixtures[name], len(plaintext), hashlib.sha256(plaintext).digest())
                == plaintext, f"{name} round trip")

    multi = fixtures["multi"]
    altered = multi[:HEADER] + bytes([multi[HEADER] ^ 1]) + multi[HEADER + 1:]
    swapped = (multi[:SEGMENT] + multi[2 * SEGMENT:3 * SEGMENT]
               + multi[SEGMENT:2 * SEGMENT] + multi[3 * SEGMENT:])
    prefix_plaintext = files["multi"][:2 * SEGMENT - HEADER - 2 * TAG]
    prefix_hash = hashlib.sha256(prefix_plaintext).digest()
    prefix_final = encrypt_file(prefix_plaintext)
    spliced = prefix_final + multi[2 * SEGMENT:]
    bad_salt = multi[:1] + bytes([multi[1] ^ 1]) + multi[2:]
    multi_hash = hashlib.sha256(files["multi"]).digest()
    for name, bad, error, detail in [
        ("byte substitution", altered, InvalidTag, "file segment 0"),
        ("truncation", multi[:-1], ValueError, "ciphertext length mismatch"),
        ("segment reordering", swapped, InvalidTag, "file segment 1"),
        ("extra segment", multi + fixtures["empty"][HEADER:], ValueError,
         "ciphertext length mismatch"),
        ("final segment followed by more segments", spliced, InvalidTag, "file segment 1"),
        ("altered header salt", bad_salt, InvalidTag, "file segment 0"),
    ]:
        reject(lambda: decrypt_file(bad, len(files["multi"]), multi_hash), error, name, detail)
    reject(lambda: decrypt_file(multi[:2 * SEGMENT], len(prefix_plaintext), prefix_hash),
           InvalidTag, "truncation with matching plaintext size", "file segment 1")

    for name, blob in fixtures.items():
        actual = hashlib.sha256(blob).hexdigest() if name == "multi" else blob.hex()
        require(EXPECTED[name] == actual, f"{name} expected ciphertext")
    compare_with_tink({**files, "exact boundary": prefix_plaintext},
                      {**fixtures, "exact boundary": prefix_final})
    print("PASS: legacy text, v1 payload, 3 fixed file vectors, 12 rejection vectors")


if __name__ == "__main__":
    main()
