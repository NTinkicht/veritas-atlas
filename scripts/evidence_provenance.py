#!/usr/bin/env python3
"""Deterministic, offline verifier for Veritas Atlas evidence-provenance v1."""
from __future__ import annotations

import hashlib
import json
import re
from datetime import datetime, timezone
from urllib.parse import urlsplit, urlunsplit

UNRESERVED = frozenset("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
HEX = frozenset("0123456789abcdefABCDEF")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
SECRET_QUERY_RE = re.compile(
    r"(?:^|[?&;])[^=&]*(?:password|passwd|secret|token|api[_-]?key|authorization|credential)[^=&]*=",
    re.IGNORECASE,
)


class ProvenanceError(ValueError):
    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


def _normalize_percent(value: str) -> str:
    out: list[str] = []
    index = 0
    while index < len(value):
        if value[index] != "%":
            out.append(value[index])
            index += 1
            continue
        if index + 2 >= len(value) or value[index + 1] not in HEX or value[index + 2] not in HEX:
            raise ProvenanceError("malformed_percent_escape")
        octet = int(value[index + 1 : index + 3], 16)
        char = chr(octet)
        if char in UNRESERVED:
            out.append(char)
        else:
            out.append(f"%{octet:02X}")
        index += 3
    return "".join(out)


def _remove_dot_segments(path: str) -> str:
    leading = path.startswith("/")
    trailing = path.endswith("/") and path != "/"
    segments: list[str] = []
    for segment in path.split("/"):
        if segment in {"", "."}:
            continue
        if segment == "..":
            if segments:
                segments.pop()
            continue
        segments.append(segment)
    normalized = ("/" if leading else "") + "/".join(segments)
    if not normalized:
        normalized = "/"
    elif trailing and not normalized.endswith("/"):
        normalized += "/"
    return normalized


def normalize_source_uri(raw: str) -> str:
    if not isinstance(raw, str) or not raw:
        raise ProvenanceError("missing_source_uri")
    try:
        parts = urlsplit(raw)
        port = parts.port
    except ValueError as exc:
        raise ProvenanceError("malformed_source_uri") from exc
    scheme = parts.scheme.lower()
    if scheme not in {"http", "https"} or not parts.hostname:
        raise ProvenanceError("malformed_source_uri")
    if parts.username is not None or parts.password is not None:
        raise ProvenanceError("source_uri_userinfo_forbidden")
    host = parts.hostname
    if any(ord(char) > 127 for char in host):
        # The contract pins Unicode 15.1 UTS #46 non-transitional processing.
        # stdlib Python cannot prove that exact profile, so fail closed.
        raise ProvenanceError("unsupported_idna_profile")
    host = host.lower()
    if ":" in host and not host.startswith("["):
        host = f"[{host}]"
    if port is not None and not ((scheme == "http" and port == 80) or (scheme == "https" and port == 443)):
        authority = f"{host}:{port}"
    else:
        authority = host
    path = _remove_dot_segments(_normalize_percent(parts.path or "/"))
    query = _normalize_percent(parts.query)
    if SECRET_QUERY_RE.search("?" + query if query else ""):
        raise ProvenanceError("secret_bearing_query")
    return urlunsplit((scheme, authority, path, query, ""))


def _require_text(envelope: dict, key: str, code: str) -> str:
    value = envelope.get(key)
    if not isinstance(value, str) or not value:
        raise ProvenanceError(code)
    return value


def _validate_retrieved_at(value: str) -> None:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise ProvenanceError("invalid_retrieved_at") from exc
    if parsed.tzinfo is None or parsed.utcoffset() != timezone.utc.utcoffset(parsed):
        raise ProvenanceError("invalid_retrieved_at")


def derive_evidence_ref(
    *, schema_version: str, source_uri: str, record_id: str, content_sha256: str,
    parser_name: str, parser_version: str,
) -> str:
    identity = {
        "content_sha256": content_sha256,
        "parser_name": parser_name,
        "parser_version": parser_version,
        "record_id": record_id,
        "schema_version": schema_version,
        "source_uri": source_uri,
    }
    # This identity contains only JSON strings. sort_keys + compact UTF-8 output is
    # RFC 8785-equivalent for this bounded v1 value shape.
    canonical = json.dumps(identity, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return "ev1:" + hashlib.sha256(canonical).hexdigest()


def verify_envelope(
    envelope: dict,
    *, retrieval_trusted: bool, classification_trusted: bool, digest_verified: bool,
) -> dict:
    try:
        schema_version = _require_text(envelope, "schema_version", "missing_schema_version")
        if schema_version != "1.0":
            raise ProvenanceError("unsupported_schema_version")
        normalized_uri = normalize_source_uri(_require_text(envelope, "source_uri", "missing_source_uri"))
        record_id = _require_text(envelope, "record_id", "missing_record_id")
        digest = _require_text(envelope, "content_sha256", "missing_content_digest")
        if not SHA256_RE.fullmatch(digest):
            raise ProvenanceError("invalid_sha256")
        if not digest_verified:
            raise ProvenanceError("content_digest_unverified")
        parser_name = _require_text(envelope, "parser_name", "missing_parser_name")
        parser_version = _require_text(envelope, "parser_version", "missing_parser_version")
        retrieved_at = _require_text(envelope, "retrieved_at", "missing_retrieved_at")
        _validate_retrieved_at(retrieved_at)
        _require_text(envelope, "retrieval_attestation", "missing_trusted_retrieval_attestation")
        if not retrieval_trusted:
            raise ProvenanceError("untrusted_retrieval")
        _require_text(envelope, "jurisdiction", "missing_jurisdiction")
        _require_text(envelope, "source_type", "missing_source_type")
        _require_text(envelope, "classification_proof", "missing_classification_proof")
        if not classification_trusted:
            raise ProvenanceError("untrusted_classification")
        derived = derive_evidence_ref(
            schema_version=schema_version,
            source_uri=normalized_uri,
            record_id=record_id,
            content_sha256=digest,
            parser_name=parser_name,
            parser_version=parser_version,
        )
        supplied = envelope.get("evidence_ref")
        if supplied is not None and supplied != derived:
            raise ProvenanceError("evidence_ref_mismatch")
        return {"accepted": True, "reason": None, "normalized_source_uri": normalized_uri, "evidence_ref": derived}
    except ProvenanceError as exc:
        return {"accepted": False, "reason": exc.code, "normalized_source_uri": None, "evidence_ref": None}
