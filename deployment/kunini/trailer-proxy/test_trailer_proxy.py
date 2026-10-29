import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from trailer_proxy import StreamResolver, is_allowed_stream_url, is_valid_range, load_proxy_url


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
