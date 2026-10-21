# Normal Play regression

All agents, navigation, skeletal animation, foot locking and foliage interaction remain active. First minute is rendered continuously and recorded; remaining movement frames run with AlwaysAnimate and camera renders at 30-second checkpoints. GPU/CPU rendering cost is measured separately in Performance.md.


FAIL

System.InvalidOperationException: Rendered mesh inside hard wood at (8.51, 2.59, 41.45), wood=(8.67, 41.77), radius=0.4252203: 霸王龙, seed=731725, elapsed=0.00, fps=30, position=(11.22, 0.00, 37.30), yaw=157.49, state=Idle, timer=4.665013, enabled=True, interaction=Inactive, front=False/False, turn=False, turnTime=0.000000, path=False, goal=(13.52, 0.01, 31.76), blocked=0, reason=, avoidance=Inactive
  at JurassicWorld.Editor.ForestPlayAcceptance.CheckRenderedBody (JurassicWorld.DinosaurAgent agent) [0x0009f] in E:\Unity\JurassicWorld\Assets\Editor\ForestPlayAcceptance.cs:209 
  at JurassicWorld.Editor.ForestPlayAcceptance.Begin () [0x000d8] in E:\Unity\JurassicWorld\Assets\Editor\ForestPlayAcceptance.cs:100 
