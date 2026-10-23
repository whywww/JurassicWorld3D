"""Probe the supplied native SDK's file mode without changing its binaries or Unity.

Requires 64-bit Python on Windows. This is an SDK capability check, not a runtime backend.
"""
import argparse
import ctypes
import json
from pathlib import Path
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sdk", type=Path, default=Path(__file__).resolve().parents[4] / "Windows-Developer-Tools-3.2.zip")
    parser.add_argument("--work-dir", type=Path, help="Optional writable scratch directory")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="MindWave-SDK-probe-", dir=args.work_dir) as directory:
        root = Path(directory)
        with zipfile.ZipFile(args.sdk) as archive:
            name = "Windows Developer Tools 3.2/Stream SDK for PC/libs/x64/thinkgear64.dll"
            library = root / "thinkgear64.dll"
            library.write_bytes(archive.read(name))
        sdk = ctypes.CDLL(str(library))
        sdk.TG_Connect.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_int]
        sdk.TG_GetValue.argtypes = [ctypes.c_int, ctypes.c_int]
        sdk.TG_GetValue.restype = ctypes.c_float
        packet = bytes([0xAA, 0xAA, 4, 2, 0, 4, 70, (~76) & 255])
        cases = {"binary": packet, "hex": (" ".join(f"{v:02X}" for v in packet) + "\n").encode(),
                 "stream_log": ("1467805045.562:  " + " ".join(f"{v:02X}" for v in packet) + "\n").encode()}
        results = {"sdkVersion": sdk.TG_GetVersion(), "cases": []}
        for label, content in cases.items():
            path = root / (label + ".dat")
            path.write_bytes(content)
            handle = sdk.TG_GetNewConnectionId()
            try:
                connected = sdk.TG_Connect(handle, str(path).encode("mbcs"), 57600, 2)
                result = {"format": label, "connectResult": connected}
                if connected == 0:
                    result["readResult"] = sdk.TG_ReadPackets(handle, 1)
                    result["attentionUpdated"] = sdk.TG_GetValueStatus(handle, 2)
                    result["attention"] = sdk.TG_GetValue(handle, 2)
                results["cases"].append(result)
            finally:
                sdk.TG_FreeConnection(handle)
        # Release the loaded DLL before TemporaryDirectory removes it on Windows.
        ctypes.windll.kernel32.FreeLibrary.argtypes = [ctypes.c_void_p]
        ctypes.windll.kernel32.FreeLibrary(sdk._handle)
        print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
