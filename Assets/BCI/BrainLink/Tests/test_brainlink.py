"""Run with the prepared Python 3.11 environment and --sdk-dir Builds/BrainLinkTools/sdk."""
import argparse
import hashlib
import json
import pathlib
import socket
import subprocess
import sys
import time
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[4]
BRIDGE = pathlib.Path(__file__).resolve().parents[1] / "Python" / "brainlink_bridge.py"
sys.dont_write_bytecode = True  # Do not create importable Unity assets under Assets/Python/__pycache__.
sys.path.insert(0, str(BRIDGE.parent))
from brainlink_bridge import frame, summary


class SdkTests(unittest.TestCase):
    def parser(self):
        result, raw = [], []
        from BrainLinkParser import BrainLinkParser
        parser = BrainLinkParser(lambda d: result.append((d.attention, d.signal)), None, None, None, raw.append)
        return parser, result, raw

    def test_pinned_binary(self):
        binary = pathlib.Path(SDK_DIR) / "BrainLinkParser.pyd"
        self.assertEqual(hashlib.sha256(binary.read_bytes()).hexdigest(),
                         "d9340d07031993cca7b68e6bd720cb6cc8ac4e795c26d93b18c7202a8d835319")

    def test_attention_and_signal(self):
        parser, result, _ = self.parser()
        values = [(a, 0) for a in (0, 39, 40, 70, 100)] + [(70, 200), (70, 0)]
        for attention, signal in values:
            parser.parse(summary(attention, signal))
        self.assertEqual(result, values)

    def test_all_fragment_boundaries(self):
        packet = summary(70, 0)
        for split in range(1, len(packet)):
            with self.subTest(split=split):
                parser, result, _ = self.parser()
                parser.parse(packet[:split])
                self.assertEqual(result, [])
                parser.parse(packet[split:])
                self.assertEqual(result, [(70, 0)])

    def test_bytewise_coalesced_and_resync(self):
        parser, result, _ = self.parser()
        for byte in summary(70, 0):
            parser.parse(bytes([byte]))
        parser.parse(b"noise" + summary(39, 0) + summary(100, 0))
        self.assertEqual(result, [(70, 0), (39, 0), (100, 0)])

    def test_checksum_and_raw_not_attention(self):
        parser, result, raw = self.parser()
        packet = summary(70, 0)
        parser.parse(packet[:-1] + bytes([packet[-1] ^ 1]))
        parser.parse(frame([0x80, 2, 0, 123]))
        parser.parse(frame([2, 0]))
        parser.parse(frame([4, 100]))
        self.assertEqual(result, [])
        self.assertEqual(raw, [123])
        parser.parse(summary(70, 0))
        self.assertEqual(result, [(70, 0)])


class BridgeTests(unittest.TestCase):
    def setUp(self):
        with socket.socket() as free:
            free.bind(("127.0.0.1", 0))
            self.port = free.getsockname()[1]
        self.process = subprocess.Popen([sys.executable, str(BRIDGE), "--sdk-dir", SDK_DIR,
                                         "--port", str(self.port), "--duration", "30"],
                                        stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                                        text=True)
        self.peer = self.connect()
        self.pending = b""

    def connect(self):
        for _ in range(80):
            try:
                return socket.create_connection(("127.0.0.1", self.port), timeout=0.2)
            except OSError:
                time.sleep(0.05)
        self.fail("bridge did not start")

    def tearDown(self):
        self.peer.close()
        if self.process.poll() is None:
            self.command("quit")
        _, errors = self.process.communicate(timeout=5)
        self.assertEqual(self.process.returncode, 0, errors)

    def command(self, text):
        self.process.stdin.write(text + "\n")
        self.process.stdin.flush()

    def packet(self):
        self.peer.settimeout(3)
        while b"\n" not in self.pending:
            chunk = self.peer.recv(4096)
            if not chunk:
                self.fail("unexpected EOF")
            self.pending += chunk
        line, self.pending = self.pending.split(b"\n", 1)
        return json.loads(line)

    def test_sdk_to_tcp_commands_and_reconnect(self):
        self.assertEqual(self.packet(), {"attention": 70, "signal": 0, "mode": "simulate"})
        self.command("a 100")
        self.assertEqual(self.packet()["attention"], 100)
        self.command("s 200")
        self.assertEqual(self.packet()["signal"], 200)
        self.command("pause")
        time.sleep(0.15)
        self.peer.settimeout(1.2)
        with self.assertRaises(socket.timeout):
            self.peer.recv(4096)
        self.command("drop")
        self.peer.settimeout(2)
        self.assertEqual(self.peer.recv(4096), b"")
        self.peer.close()
        self.peer = self.connect()
        self.pending = b""
        self.command("s 0")
        self.command("a 70")
        self.command("resume")
        self.assertEqual(self.packet(), {"attention": 70, "signal": 0, "mode": "simulate"})


if __name__ == "__main__":
    arguments = argparse.ArgumentParser()
    arguments.add_argument("--sdk-dir", default=str(ROOT / "Builds" / "BrainLinkTools" / "sdk"))
    args, remaining = arguments.parse_known_args()
    SDK_DIR = str(pathlib.Path(args.sdk_dir).resolve())
    sys.path.insert(0, SDK_DIR)
    unittest.main(argv=[sys.argv[0]] + remaining, verbosity=2)
