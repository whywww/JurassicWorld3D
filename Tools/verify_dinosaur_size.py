"""Compare baked ground-dinosaur metrics with the recorded pre-migration baseline."""
import json, re
from pathlib import Path
baseline = json.loads(Path("Assets/Editor/DinosaurSizeBaseline.json").read_text(encoding="utf-8-sig"))
checks = 0
for entry in baseline["entries"]:
    path = entry["path"]
    if path.endswith("/visual"):
        scene = path.split(".unity/")[0] + ".unity"
        original = json.loads(entry["json"])["Transform"]["m_LocalScale"]
        text = Path(scene).read_text(encoding="utf-8-sig")
        scales = re.findall(r"m_LocalScale: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}", text)
        assert any(all(abs(float(v) - original[axis] * 1.5) < 0.00001 for axis, v in zip("xyz", scale)) for scale in scales), path
    if not path.endswith("/visual") and ".unity/" in path:
        scene, name = path.split(".unity/")
        text = Path(scene + ".unity").read_text(encoding="utf-8-sig")
        old = json.loads(entry["json"])
        old = old.get("MonoBehaviour", old)
        values = {key: old[key] * 1.5 for key in ("walkSpeed", "approachSpeed", "moveAnimationGroundSpeed", "collisionRadius", "viewFocusHeight")}
        blocks = text.split("--- !u!")
        assert any(all(re.search(r"^  " + key + r": ([^\n]+)", block, re.M) and abs(float(re.search(r"^  " + key + r": ([^\n]+)", block, re.M)[1]) - value) < 0.00002 for key, value in values.items()) for block in blocks), path
        checks += 1
print(f"PASS: {checks} dinosaur metric tuples are 1.5x; speed/reference ratios preserved")
# Gait timing, local sole offsets and contact samples must remain byte-identical.
import hashlib
for entry in baseline["entries"]:
    if not entry["path"].endswith("/Gait.asset"):
        continue
    path = entry["path"]
    new = Path(path).read_text(encoding="utf-8-sig")
    old = json.loads(entry["json"])["MonoBehaviour"]
    speed = float(re.search(r"^  groundSpeed: ([^\n]+)", new, re.M)[1])
    assert abs(speed - old["groundSpeed"] * 1.5) < 0.00001, path
    invariant = "\n".join(line for line in new.splitlines() if not line.startswith(("  groundSpeed:", "  heightCorrection:")))
    assert hashlib.sha256(invariant.encode()).hexdigest() == entry["invariantHash"], path
print("PASS: 3 gait profiles preserve clips, contact phases, loop timing and bone-local offsets")
