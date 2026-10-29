#!/usr/bin/env python3
"""Resolve and stream validated YouTube trailers through one SOCKS proxy."""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import re
import subprocess
import threading
import time
from urllib.parse import urljoin, urlsplit

import requests


VIDEO_ID_PATTERN = re.compile(r"^[A-Za-z0-9_-]{11}$")
RANGE_PATTERN = re.compile(r"^bytes=(?:\d+-\d*|-\d+)$")
STREAM_HEADER_NAMES = ("Content-Type", "Content-Length", "Content-Range", "Accept-Ranges")
FORWARDED_REQUEST_HEADERS = ("User-Agent", "Referer", "Origin")
FORMAT_SELECTOR = (
    "b[ext=mp4][vcodec^=avc1][acodec^=mp4a][protocol^=http]/"
    "b[ext=mp4][vcodec!=none][acodec!=none][protocol^=http]/"
    "b[vcodec!=none][acodec!=none][protocol^=http]"
)


@dataclass(frozen=True)
class ResolvedStream:
    url: str
    headers: dict[str, str]
    expires_at: float


def load_proxy_url(path: str) -> str:
    secret_path = Path(path)
    if not secret_path.is_file() or secret_path.stat().st_size > 2048:
        raise ValueError("SOCKS proxy secret is missing or oversized")
    value = secret_path.read_text(encoding="utf-8").strip()
    parsed = urlsplit(value)
    try:
        valid = (
            parsed.scheme in {"socks5", "socks5h"}
            and bool(parsed.hostname)
            and parsed.port is not None
            and parsed.path in {"", "/"}
            and not parsed.query
            and not parsed.fragment
            and not any(character.isspace() for character in value)
        )
    except ValueError:
        valid = False
    if not valid:
        raise ValueError("SOCKS proxy secret must contain one socks5 URL")
    return value


def is_allowed_stream_url(value: str) -> bool:
    try:
        parsed = urlsplit(value)
        hostname = (parsed.hostname or "").lower()
        return (
            parsed.scheme == "https"
            and parsed.port in {None, 443}
            and not parsed.username
            and not parsed.password
            and (hostname == "googlevideo.com" or hostname.endswith(".googlevideo.com"))
        )
    except ValueError:
        return False


def is_valid_range(value: str | None) -> bool:
    return value is None or bool(RANGE_PATTERN.fullmatch(value))


class StreamResolver:
    def __init__(self, proxy_url: str, *, executable: str = "yt-dlp", cache_seconds: int = 300):
        self.proxy_url = proxy_url
        self.executable = executable
        self.cache_seconds = cache_seconds
        self._cache: dict[str, ResolvedStream] = {}
        self._lock = threading.Lock()

    def resolve(self, video_id: str, *, force: bool = False) -> ResolvedStream:
        if not VIDEO_ID_PATTERN.fullmatch(video_id):
            raise ValueError("Invalid YouTube video identifier")
        now = time.monotonic()
        with self._lock:
            cached = self._cache.get(video_id)
            if not force and cached and cached.expires_at > now:
                return cached
            result = subprocess.run(
                [
                    self.executable,
                    "--no-warnings",
                    "--no-playlist",
                    "--socket-timeout",
                    "15",
                    "--proxy",
                    self.proxy_url,
                    "--extractor-args",
                    "youtube:player_client=android",
                    "--format",
                    FORMAT_SELECTOR,
                    "--dump-single-json",
                    f"https://www.youtube.com/watch?v={video_id}",
                ],
                check=False,
                capture_output=True,
                text=True,
                timeout=45,
            )
            if result.returncode != 0:
                raise RuntimeError("YouTube trailer resolution failed")
            try:
                payload = json.loads(result.stdout)
                stream_url = payload["url"]
                source_headers = payload.get("http_headers") or {}
            except (KeyError, TypeError, json.JSONDecodeError) as exception:
                raise RuntimeError("YouTube trailer metadata was malformed") from exception
            if not is_allowed_stream_url(stream_url):
                raise RuntimeError("YouTube returned an untrusted stream URL")
            headers = {
                name: str(source_headers[name])
                for name in FORWARDED_REQUEST_HEADERS
                if source_headers.get(name)
            }
            resolved = ResolvedStream(stream_url, headers, now + self.cache_seconds)
            self._cache[video_id] = resolved
            return resolved


class TrailerProxyHandler(BaseHTTPRequestHandler):
    resolver: StreamResolver
    proxy_url: str
    server_version = "KuniniTrailerProxy/1.0"

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler contract
        stream_started = False
        parsed = urlsplit(self.path)
        if parsed.path == "/health" and not parsed.query:
            self._send_text(200, "ok\n")
            return
        prefix = "/stream/"
        if not parsed.path.startswith(prefix) or parsed.query:
            self._send_text(404, "not found\n")
            return
        video_id = parsed.path[len(prefix):]
        range_header = self.headers.get("Range")
        if not VIDEO_ID_PATTERN.fullmatch(video_id) or not is_valid_range(range_header):
            self._send_text(416, "invalid range or trailer id\n")
            return

        try:
            upstream = self._open_upstream(video_id, range_header, force=False)
            if upstream.status_code == 403:
                upstream.close()
                upstream = self._open_upstream(video_id, range_header, force=True)
            with upstream:
                if upstream.status_code not in {200, 206, 416}:
                    self._send_text(502, "trailer upstream unavailable\n")
                    return
                stream_started = True
                self.send_response(upstream.status_code)
                for name in STREAM_HEADER_NAMES:
                    if upstream.headers.get(name):
                        self.send_header(name, upstream.headers[name])
                self.send_header("Cache-Control", "private, no-store")
                self.send_header("X-Content-Type-Options", "nosniff")
                self.end_headers()
                if upstream.status_code == 416:
                    return
                for chunk in upstream.iter_content(chunk_size=64 * 1024):
                    if chunk:
                        self.wfile.write(chunk)
        except (BrokenPipeError, ConnectionResetError):
            return
        except (requests.RequestException, RuntimeError, subprocess.TimeoutExpired, ValueError):
            if not stream_started:
                self._send_text(502, "trailer proxy unavailable\n")

    def _open_upstream(self, video_id: str, range_header: str | None, *, force: bool):
        resolved = self.resolver.resolve(video_id, force=force)
        headers = dict(resolved.headers)
        if range_header:
            headers["Range"] = range_header
        stream_url = resolved.url
        proxies = {"http": self.proxy_url, "https": self.proxy_url}
        for _ in range(4):
            response = requests.get(
                stream_url,
                headers=headers,
                proxies=proxies,
                allow_redirects=False,
                stream=True,
                timeout=(15, 30),
            )
            if response.status_code not in {301, 302, 303, 307, 308}:
                return response
            location = response.headers.get("Location", "")
            response.close()
            stream_url = urljoin(stream_url, location)
            if not is_allowed_stream_url(stream_url):
                raise RuntimeError("YouTube redirected to an untrusted stream URL")
        raise RuntimeError("YouTube trailer redirected too many times")

    def _send_text(self, status: int, value: str) -> None:
        if self.wfile.closed:
            return
        payload = value.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(payload)

    def log_message(self, format: str, *args: object) -> None:
        return


def main() -> None:
    proxy_url = load_proxy_url(os.environ.get("SOCKS_PROXY_FILE", "/run/secrets/socks-proxy-url"))
    TrailerProxyHandler.proxy_url = proxy_url
    TrailerProxyHandler.resolver = StreamResolver(proxy_url)
    server = ThreadingHTTPServer(("0.0.0.0", 8080), TrailerProxyHandler)
    server.daemon_threads = True
    server.serve_forever()


if __name__ == "__main__":
    main()
