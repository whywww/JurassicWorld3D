"""Official BrainLink SDK -> localhost JSON. Synthetic EEG is test data, not human EEG."""
import argparse
import json
import queue
import socket
import sys
import threading
import time


def frame(payload):
    return bytes((0xAA, 0xAA, len(payload))) + bytes(payload) + bytes((~sum(payload) & 255,))


def summary(attention, signal):
    payload = [2, signal, 4, attention, 5, 50, 0x83, 24]
    for power in range(1000, 1800, 100):
        payload.extend(power.to_bytes(3, "big"))
    return frame(payload)


class Bridge:
    def __init__(self, parser_type, mode, port):
        self.parser_type = parser_type
        self.mode = mode
        self.peer = None
        self.listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        # Do not reuse an occupied port: two bridges must not masquerade as one source.
        self.listener.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        self.listener.bind(("127.0.0.1", port))
        self.listener.listen(1)
        self.listener.setblocking(False)
        self.reset_parser()

    def reset_parser(self):
        self.parser = self.parser_type(self.on_eeg, None, None, None, None)

    def on_eeg(self, data):
        # Called only by the real SDK. No cache/replay when a new Unity client connects.
        packet = {"attention": int(data.attention), "signal": int(data.signal), "mode": self.mode}
        print(json.dumps(packet), flush=True)
        if self.peer is not None:
            try:
                self.peer.sendall((json.dumps(packet, separators=(",", ":")) + "\n").encode("ascii"))
            except (OSError, TimeoutError):
                self.drop()

    def accept(self):
        try:
            peer, _ = self.listener.accept()
        except BlockingIOError:
            return
        self.drop()
        self.peer = peer
        self.peer.settimeout(0.25)
        self.peer.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        print("Unity connected; waiting for a new SDK callback", flush=True)

    def drop(self):
        if self.peer is not None:
            self.peer.close()
            self.peer = None

    def close(self):
        self.drop()
        self.listener.close()


def read_commands(commands):
    for line in sys.stdin:
        commands.put(line.strip())


def run(args, parser_type):
    if args.mode == "serial" and not args.serial_port:
        raise ValueError("--serial-port COMx is required in serial mode")
    bridge = Bridge(parser_type, args.mode, args.port)
    commands = queue.Queue()
    threading.Thread(target=read_commands, args=(commands,), daemon=True).start()
    serial_device = None
    began = time.monotonic()
    auto_began = began
    next_sample = next_serial_attempt = began
    attention, signal = args.attention, args.signal
    paused, automatic, previous_stage = False, args.auto, -1
    print(f"BrainLink {args.mode} -> 127.0.0.1:{args.port}; official SDK required", flush=True)
    print("Commands: a 70 | s 200 | pause | resume | drop | auto | manual | quit", flush=True)
    try:
        while args.duration <= 0 or time.monotonic() - began < args.duration:
            now = time.monotonic()
            bridge.accept()
            while not commands.empty():
                parts = commands.get_nowait().lower().split()
                try:
                    if not parts:
                        continue
                    if parts[0] == "quit":
                        return
                    if parts[0] in ("a", "s") and len(parts) == 2:
                        value = int(parts[1])
                        maximum = 100 if parts[0] == "a" else 200
                        if not 0 <= value <= maximum:
                            raise ValueError(f"range is 0..{maximum}")
                        if args.mode != "simulate":
                            raise ValueError("a/s commands are only for simulation")
                        if parts[0] == "a": attention = value
                        else: signal = value
                    elif parts[0] == "pause": paused = True
                    elif parts[0] == "resume":
                        paused = False
                        bridge.reset_parser()
                    elif parts[0] == "drop": bridge.drop()
                    elif parts[0] == "auto" and args.mode == "simulate":
                        automatic = True
                        auto_began, previous_stage = now, -1
                    elif parts[0] == "manual": automatic, paused = False, False
                    else: raise ValueError("unknown command")
                except ValueError as error:
                    print("Command rejected:", error, flush=True)
            if automatic and args.mode == "simulate":
                stage = int((now - auto_began) / 4) % 9
                if stage != previous_stage:
                    previous_stage = stage
                    attention = [39, 40, 70, 100, 0, 70, 70, 70, 70][stage]
                    signal, paused = (200 if stage == 5 else 0), stage == 7
                    if stage == 8: bridge.drop()
                    print(f"Auto stage {stage}: attention={attention} signal={signal} paused={paused}", flush=True)
            if args.mode == "simulate":
                if now >= next_sample:
                    next_sample = now + 1
                    if not paused:
                        bridge.parser.parse(summary(attention, signal))
            else:
                if serial_device is None and now >= next_serial_attempt:
                    import serial
                    try:
                        serial_device = serial.Serial(args.serial_port, 115200, timeout=0.05)
                        bridge.reset_parser()
                        print("Serial connected:", args.serial_port, flush=True)
                    except (OSError, serial.SerialException) as error:
                        bridge.drop()
                        print("Serial unavailable:", type(error).__name__, flush=True)
                        next_serial_attempt = now + 2
                if serial_device is not None:
                    try:
                        chunk = serial_device.read(max(1, min(4096, serial_device.in_waiting)))
                        if not paused and chunk:
                            bridge.parser.parse(chunk)
                    except (OSError, serial.SerialException) as error:
                        serial_device.close()
                        serial_device = None
                        bridge.drop()
                        bridge.reset_parser()
                        next_serial_attempt = now + 2
                        print("Serial disconnected:", type(error).__name__, flush=True)
            time.sleep(0.01)
    finally:
        if serial_device is not None:
            serial_device.close()
        bridge.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sdk-dir", required=True)
    parser.add_argument("--mode", choices=("simulate", "serial"), default="simulate")
    parser.add_argument("--serial-port")
    parser.add_argument("--port", type=int, default=13856)
    parser.add_argument("--attention", type=int, default=70)
    parser.add_argument("--signal", type=int, default=0)
    parser.add_argument("--auto", action="store_true")
    parser.add_argument("--duration", type=float, default=0)
    args = parser.parse_args()
    if not (0 <= args.attention <= 100 and 0 <= args.signal <= 200 and 1 <= args.port <= 65535):
        parser.error("attention 0..100; signal 0..200; port 1..65535")
    if args.auto and args.mode != "simulate":
        parser.error("--auto is only for simulation")
    if sys.version_info[:2] != (3, 11):
        parser.error("The official .pyd requires Python 3.11 x64; run Setup-BrainLink.ps1")
    sys.path.insert(0, args.sdk_dir)
    from BrainLinkParser import BrainLinkParser
    try:
        run(args, BrainLinkParser)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
