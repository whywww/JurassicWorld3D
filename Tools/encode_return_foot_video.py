"""Encode the actual Unity camera captures at half speed; retain CSV traces alongside."""
from pathlib import Path
import csv
import cv2
import json

root = Path(__file__).resolve().parents[1]
folder = root / "Logs" / "ReturnFootVideo"
count = int((folder / "capture-count.txt").read_text().strip())
for view in ("side", "feet"):
    target = folder / f"{view}-slow.mp4"
    writer = cv2.VideoWriter(str(target), cv2.CAP_MSMF, cv2.VideoWriter_fourcc(*"H264"), 15, (640, 480))
    if not writer.isOpened():
        raise RuntimeError(f"Cannot encode {target}")
    for index in range(count):
        frame = cv2.imread(str(folder / view / f"{index:04d}.png"))
        if frame is None:
            raise RuntimeError(f"Missing {view} frame {index}")
        cv2.rectangle(frame, (0, 0), (640, 32), (24, 24, 24), -1)
        cv2.putText(frame, f"170-deg turn -> first two footfalls | {view} | 0.5x", (9, 22),
                    cv2.FONT_HERSHEY_SIMPLEX, .52, (245, 245, 245), 1, cv2.LINE_AA)
        writer.write(frame)
    writer.release()
    print(f"{target}: {count} frames, {count / 15:.2f}s")

rows = list(csv.DictReader((root / "Logs" / "trex-return-foot-audit.csv").open()))
returns = [row for row in rows if row["stage"] != "TurnBack"]
summary = {
    "captured_footfalls": int(rows[-1]["landings"]),
    "max_left_stance_error_m": max(float(row["leftError"]) for row in returns),
    "max_right_stance_error_m": max(float(row["rightError"]) for row in returns),
    "added_sole_order_reversals": sum(float(row["ikLX"]) >= float(row["ikRX"])
                                      and float(row["rawLX"]) < float(row["rawRX"]) for row in returns),
    "added_knee_order_reversals": sum(float(row["ikLKX"]) >= float(row["ikRKX"])
                                      and float(row["rawLKX"]) < float(row["rawRKX"]) for row in returns),
    "scope": "turn end, entry blend, two actual footfalls; includes early return-to-roam handoff",
}
(folder / "measurements.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
