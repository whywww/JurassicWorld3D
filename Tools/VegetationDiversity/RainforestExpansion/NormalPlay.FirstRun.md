# Normal Play regression — runtime collision policy

Births and ordinary travel use the runtime collision envelope. Special performances may overlap static scenery; peer body checks remain active. Recovery permits only unretired original overlaps and inward boundary/navigation movement. Rendered-mesh overlaps are visual diagnostics, not collision failures. Historical strict-mesh results: NormalPlay.BeforeCollisionPolicy.md.

All agents, navigation, skeletal animation, foot locking and foliage interaction remain active. First minute is rendered continuously and recorded; remaining movement frames run with AlwaysAnimate and camera renders at 30-second checkpoints. GPU/CPU rendering cost is measured separately in Performance.md.

- VISUAL_ONLY spawn seed 731722: dinosaur=霸王龙, seed=731722, stage=Ordinary travel / idle, overlappingVertices=1, firstVertex=(-10.980, 3.103, 42.480), wood=(-10.727, 42.113), radius=0.5321
- VISUAL_ONLY spawn seed 731726: dinosaur=霸王龙, seed=731726, stage=Ordinary travel / idle, overlappingVertices=7, firstVertex=(7.742, 2.492, 1.392), wood=(7.698, 1.179), radius=0.2582
- VISUAL_ONLY spawn seed 731729: dinosaur=霸王龙, seed=731729, stage=Ordinary travel / idle, overlappingVertices=22, firstVertex=(59.216, 2.519, 89.835), wood=(59.656, 90.059), radius=0.6647
- VISUAL_ONLY spawn seed 731731: dinosaur=霸王龙, seed=731731, stage=Ordinary travel / idle, overlappingVertices=38, firstVertex=(23.606, 2.554, 73.659), wood=(23.757, 74.129), radius=0.7178
- VISUAL_ONLY spawn seed 731738: dinosaur=霸王龙, seed=731738, stage=Ordinary travel / idle, overlappingVertices=45, firstVertex=(27.676, 2.518, -41.175), wood=(27.459, -41.633), radius=0.7131
- VISUAL_ONLY spawn seed 731742: dinosaur=霸王龙, seed=731742, stage=Ordinary travel / idle, overlappingVertices=11, firstVertex=(-70.279, 2.768, -4.025), wood=(-69.697, -4.520), radius=0.7920
- VISUAL_ONLY spawn seed 731744: dinosaur=霸王龙, seed=731744, stage=Ordinary travel / idle, overlappingVertices=45, firstVertex=(56.840, 2.877, 43.821), wood=(56.419, 43.910), radius=0.5108
32 fixed seeds: runtime body-envelope, navigation, camera/peer spacing and six-metre exit contracts passed for both active species; visible mesh results are reported separately.

Visible-mesh diagnostics: 72 animal samples; 7 samples contained overlaps; 169 overlapping vertices total (vertices may recur across samples). These counts do not determine collision-contract success.

Runtime collision contract: FAIL

System.InvalidOperationException: Runtime collision contract: Tree, 霸王龙, collisionStage=Interaction travel / wait, seed=731716, elapsed=147.90, fps=30, position=(63.63, 0.00, -6.79), yaw=31.61, state=RandomWalk, timer=-0.01231093, enabled=True, interaction=FrontSequence, front=True/False, turn=True, turnTime=0.000000, path=True, goal=(61.36, 0.01, 10.80), blocked=0, reason=, avoidance=Inactive
  at JurassicWorld.Editor.ForestPlayAcceptance.CheckPose (JurassicWorld.DinosaurAgent agent, System.Boolean spawning) [0x00117] in E:\Unity\JurassicWorld\Assets\Editor\ForestPlayAcceptance.cs:226 
  at JurassicWorld.Editor.ForestPlayAcceptance.Tick () [0x00053] in E:\Unity\JurassicWorld\Assets\Editor\ForestPlayAcceptance.cs:333 
