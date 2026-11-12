# Dinosaur size adjustment

All existing Main ground dinosaurs and the T-Rex interaction test use 1.5 times their previous visual scale. Ground walk/approach speeds and animation reference speeds use the same factor; clip timing, contact curves and bone-local sole offsets are preserved. Logic roots retain unit scale. Continuous steering radii and lookahead/probe distances also grow by 1.5, so increased travel speed does not sharpen turns.

The edit-mode migration is `JurassicWorld.Editor.DinosaurSizeAdjustment.Apply` (menu: Jurassic World/Scene/Enlarge All Dinosaurs 1.5x). `Assets/Editor/DinosaurSizeBaseline.json` records the original dimensional metrics and completion state. Re-running a completed migration does not rescale anything. Do not delete the baseline to rerun the migration. An interrupted derived-data bake can resume without rescaling the scene.

Generators use the same factor. World-space interaction trajectories, plant-contact radii and collision/focus dimensions are updated. T-Rex torso/head obstacle bands are re-sampled at the new size. The flyer uses a unit visual pivot, with scaling inside it, so pivot-local bounds used by the airspace baker include the new wingspan.

Player collision pose bands use encoded bone/group IDs (`bone * 1000 + originalHeightLayer + 100`), unlike obstacle bands' world-height layers. Scale each player's bound once while preserving its ID; never expand it into one copy per new height layer. The player pose cache was repaired from commit `d371764` at 1.5x, retaining its original 114,275 bounds and 983 pose times. Obstacle envelopes and foot/root samples were preserved.

Sauropelta needs an additional 0.02 m downward body correction after scaling to keep turn stance error below the existing 0.02 m limit. Its contact phase, speed and turn rate remain unchanged.

Dinosaurs with calibrated gait profiles also retain their calibrated turn-rate limit and forward stride during emergency escape. The legacy escape connector turned at 120 degrees/second while translating directly toward the goal; that sideways motion could exceed a planted leg's reach at the enlarged size. They restore normal obstacle checks as soon as they reach verified safe ground, avoiding circles around a nearby target. Main Play regression replays spawn seed 244401562, which exposed this case.

Validation commands:

- `python Tools/verify_dinosaur_size.py`: original versus baked metrics and unchanged gait contact/timing data.
- Unity executeMethod `JurassicWorld.Editor.DinosaurGaitAcceptance.VerifyEnlarged`: 30/60/144 FPS straight, turning, stop/start and blocked gait cases, retaining the original 2 cm stance/reach limit. Videos and reports: `Reports/DinosaurSize/`.
- Unity executeMethod `JurassicWorld.Editor.DinosaurSizeAdjustment.VerifyRepeatAndFlight`: repeat-migration guard and existing flight clearance acceptance.
- Unity executeMethod `JurassicWorld.Editor.DinosaurAnimationTimingAcceptance.Run`: T-Rex interaction timing/foot-lock check in Play mode (omit `-quit`).

The timing test checks the current animated torso/head probe rather than the neutral walk envelope, matching production pose checks during turns. No collision tolerances are relaxed.
