PASS BODY_HEAD_ENVELOPES

# Body/head envelope acceptance

- PASS: all 26 Main motions have ordered pose caches; all parts belong to torso, hips, neck or head; height-layer IDs are valid.
- PASS: Spine hard-trunk overlap rejected, obstacle name retained.
- PASS: Neck hard-trunk overlap rejected, obstacle name retained.
- PASS: Head hard-trunk overlap rejected, obstacle name retained.
- PASS: a hard trunk in the original tail geometry does not block the body/head pose.
- PASS: overlapping grass/fern collider does not enter hard-obstacle checks.
- PASS: walking pose and peer probe accept clearing 0 (bounds, trunks, solids, NavMesh).
- PASS: walking pose and peer probe accept clearing 1 (bounds, trunks, solids, NavMesh).
