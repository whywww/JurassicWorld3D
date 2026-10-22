"""Compare the preserved 8.5s timing captures with the new 1.7s Unity captures."""
from pathlib import Path
import cv2
import numpy as np

folder = Path(__file__).resolve().parent / "VegetationDiversity" / "RainforestSample"
output = folder / "turn-sync-comparison.mp4"
writer = cv2.VideoWriter(str(output), cv2.CAP_MSMF,
                         cv2.VideoWriter_fourcc(*"H264"), 60, (1280, 400))
if not writer.isOpened():
    raise RuntimeError("Cannot open H264 comparison encoder")

for direction in ("left", "right"):
    before = cv2.VideoCapture(str(folder / f"turn-sync-before-{direction}.mp4"))
    after = cv2.VideoCapture(str(folder / f"timing-after-Turn{direction.title()}.mp4"))
    if not before.isOpened() or not after.isOpened():
        raise RuntimeError(f"Missing {direction} capture")
    count = int(before.get(cv2.CAP_PROP_FRAME_COUNT))
    last = None
    for index in range(count):
        ok, old = before.read()
        if not ok:
            raise RuntimeError("Truncated baseline capture")
        ok, new = after.read()
        if ok:
            last = new
        if last is None:
            raise RuntimeError("Empty new capture")
        canvas = np.zeros((400, 1280, 3), dtype=np.uint8)
        canvas[40:, :640] = cv2.resize(old, (640, 360))
        canvas[40:, 640:] = cv2.resize(last, (640, 360))
        cv2.putText(canvas, f"Before: {direction} / 8.5s (preserved prior capture)",
                    (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .62, (255, 255, 255), 1, cv2.LINE_AA)
        status = "complete; holding final frame" if not ok else f"t={index / 60:.2f}s"
        cv2.putText(canvas, f"After: {direction} / 1.7s / {status}",
                    (652, 26), cv2.FONT_HERSHEY_SIMPLEX, .62, (255, 255, 255), 1, cv2.LINE_AA)
        writer.write(canvas)
    before.release()
    after.release()
writer.release()

# A contact sheet of the second consecutive left turn, at its original speed.
repeat = cv2.VideoCapture(str(folder / "turn-sync-repeat.mp4"))
sheet = np.zeros((600, 960, 3), dtype=np.uint8)
for index, seconds in enumerate((1.7, 2.0, 2.3, 2.6, 2.9, 3.2)):
    repeat.set(cv2.CAP_PROP_POS_MSEC, seconds * 1000)
    ok, capture = repeat.read()
    if not ok:
        raise RuntimeError("Missing repeated turn capture")
    row, column = divmod(index, 3)
    tile = cv2.resize(capture, (320, 180))
    sheet[row * 300 + 40:row * 300 + 220, column * 320:(column + 1) * 320] = tile
    cv2.putText(sheet, f"second left: t={seconds - 1.7:.1f}s",
                (column * 320 + 8, row * 300 + 27), cv2.FONT_HERSHEY_SIMPLEX,
                .55, (255, 255, 255), 1, cv2.LINE_AA)
repeat.release()
cv2.imwrite(str(folder / "turn-sync-repeat.png"), sheet)
print(output)
