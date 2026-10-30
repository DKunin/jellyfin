import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from trailer_proxy import (
    StreamResolver,
    is_allowed_stream_url,
    is_valid_range,
    load_proxy_url,
    open_socks_connection,
    parse_connect_target,
)


class TrailerProxyTests(unittest.TestCase):
    def test_loads_only_one_socks_proxy_url(self):
        with tempfile.TemporaryDirectory() as directory:
            secret = Path(directory) / "proxy"
            secret.write_text("socks5h://user:password@proxy.example:1080\n", encoding="utf-8")
            self.assertEqual(
                load_proxy_url(str(secret)),
                "socks5h://user:password@proxy.example:1080",
            )
            secret.write_text("https://proxy.example:443\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                load_proxy_url(str(secret))

    def test_allows_only_googlevideo_https_streams(self):
        self.assertTrue(is_allowed_stream_url("https://rr1---sn.example.googlevideo.com/videoplayback?id=1"))
        self.assertFalse(is_allowed_stream_url("http://rr1---sn.example.googlevideo.com/video"))
        self.assertFalse(is_allowed_stream_url("https://googlevideo.com.evil.example/video"))
        self.assertFalse(is_allowed_stream_url("https://user@googlevideo.com/video"))

    def test_accepts_only_one_http_byte_range(self):
        self.assertTrue(is_valid_range(None))
        self.assertTrue(is_valid_range("bytes=0-1023"))
        self.assertTrue(is_valid_range("bytes=1024-"))
        self.assertTrue(is_valid_range("bytes=-1024"))
        self.assertFalse(is_valid_range("bytes=0-1,4-5"))
        self.assertFalse(is_valid_range("items=0-1"))

    def test_allows_only_public_dns_hosts_over_tls(self):
        self.assertEqual(parse_connect_target("api.themoviedb.org:443"), ("api.themoviedb.org", 443))
        self.assertEqual(parse_connect_target("www.omdbapi.com:443"), ("www.omdbapi.com", 443))
        self.assertEqual(parse_connect_target("image.tmdb.org:443"), ("image.tmdb.org", 443))
        self.assertIsNone(parse_connect_target("api.themoviedb.org:80"))
        self.assertIsNone(parse_connect_target("user@api.themoviedb.org:443"))
        self.assertIsNone(parse_connect_target("127.0.0.1:443"))
        self.assertIsNone(parse_connect_target("service.internal:443"))
        self.assertIsNone(parse_connect_target("trailer-proxy:443"))

    @patch("trailer_proxy.socks.create_connection")
    def test_opens_metadata_connection_through_socks_secret(self, create_connection):
        target = ("www.omdbapi.com", 443)
        open_socks_connection("socks5://user:p%40ss@proxy.example:1080", target)
        create_connection.assert_called_once_with(
            target,
            timeout=15,
            proxy_type=2,
            proxy_addr="proxy.example",
            proxy_port=1080,
            proxy_rdns=True,
            proxy_username="user",
            proxy_password="p@ss",
        )

    @patch("trailer_proxy.subprocess.run")
    def test_resolves_android_client_stream_through_proxy_and_caches_it(self, run):
        run.return_value = subprocess.CompletedProcess(
            args=[],
            returncode=0,
            stdout=json.dumps(
                {
                    "url": "https://rr1---sn.example.googlevideo.com/videoplayback?id=1",
                    "http_headers": {
                        "User-Agent": "yt-dlp-agent",
                        "Referer": "https://www.youtube.com/",
                        "X-Ignored": "secret",
                    },
                }
            ),
            stderr="",
        )
        resolver = StreamResolver("socks5h://proxy.example:1080", cache_seconds=300)
        first = resolver.resolve("8g18jFHCLXk")
        second = resolver.resolve("8g18jFHCLXk")
        self.assertEqual(first, second)
        self.assertEqual(first.headers, {"User-Agent": "yt-dlp-agent", "Referer": "https://www.youtube.com/"})
        self.assertEqual(run.call_count, 1)
        command = run.call_args.args[0]
        self.assertIn("youtube:player_client=android", command)
        self.assertIn("socks5h://proxy.example:1080", command)


if __name__ == "__main__":
    unittest.main()
