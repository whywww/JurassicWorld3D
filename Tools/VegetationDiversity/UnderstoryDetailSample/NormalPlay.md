# Normal Play regression — runtime collision policy

Understory sample regression: 32 spawn seeds, 30/60/144 FPS each up to 60 seconds. Failed cases stop early and remain failures; this is not the previous ten-minute acceptance.

Births and ordinary travel use the runtime collision envelope. Special performances may overlap static scenery; peer body checks remain active. Recovery permits only unretired original overlaps and inward boundary/navigation movement. Rendered-mesh overlaps are visual diagnostics, not collision failures. Historical strict-mesh results: NormalPlay.BeforeCollisionPolicy.md.

All agents, navigation, skeletal animation, foot locking and foliage interaction remain active. First minute is rendered continuously and recorded; remaining movement frames run with AlwaysAnimate and camera renders at 30-second checkpoints. GPU/CPU rendering cost is measured separately in Performance.md.

- VISUAL_ONLY spawn seed 731716: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=104, firstVertex=(-9.320, -0.312, 46.049), wood=(-9.424, 45.939), radius=0.2769
- VISUAL_ONLY spawn seed 731716: dinosaur=蜥结龙, seed=731716, stage=Ordinary travel / idle, overlappingVertices=373, firstVertex=(-81.055, 10.121, 7.591), wood=(-80.995, 7.701), radius=0.7384
- VISUAL_ONLY spawn seed 731717: dinosaur=包鲁鳄, seed=731717, stage=Ordinary travel / idle, overlappingVertices=196, firstVertex=(-39.548, 1.132, 55.665), wood=(-39.444, 55.593), radius=0.2491
- VISUAL_ONLY spawn seed 731717: dinosaur=蜥结龙, seed=731717, stage=Ordinary travel / idle, overlappingVertices=319, firstVertex=(17.139, 2.970, -28.838), wood=(16.614, -28.685), radius=0.5834
- VISUAL_ONLY spawn seed 731718: dinosaur=包鲁鳄, seed=731718, stage=Ordinary travel / idle, overlappingVertices=852, firstVertex=(70.783, 5.857, 0.012), wood=(70.617, 0.509), radius=0.7828
- VISUAL_ONLY spawn seed 731718: dinosaur=蜥结龙, seed=731718, stage=Ordinary travel / idle, overlappingVertices=171, firstVertex=(-5.018, 5.987, 43.307), wood=(-4.989, 43.428), radius=0.1679
- VISUAL_ONLY spawn seed 731719: dinosaur=包鲁鳄, seed=731719, stage=Ordinary travel / idle, overlappingVertices=467, firstVertex=(58.071, 0.318, 61.617), wood=(58.112, 61.459), radius=0.2382
- VISUAL_ONLY spawn seed 731719: dinosaur=蜥结龙, seed=731719, stage=Ordinary travel / idle, overlappingVertices=235, firstVertex=(23.318, 3.141, 36.207), wood=(22.877, 36.324), radius=0.6284
- VISUAL_ONLY spawn seed 731720: dinosaur=包鲁鳄, seed=731720, stage=Ordinary travel / idle, overlappingVertices=225, firstVertex=(-47.859, 0.634, 89.191), wood=(-47.953, 89.436), radius=0.5047
- VISUAL_ONLY spawn seed 731720: dinosaur=蜥结龙, seed=731720, stage=Ordinary travel / idle, overlappingVertices=355, firstVertex=(-54.044, 10.061, 28.240), wood=(-54.580, 28.019), radius=0.7000
- VISUAL_ONLY spawn seed 731721: dinosaur=包鲁鳄, seed=731721, stage=Ordinary travel / idle, overlappingVertices=1905, firstVertex=(41.880, 0.063, -68.640), wood=(41.699, -68.303), radius=0.5157
- VISUAL_ONLY spawn seed 731721: dinosaur=蜥结龙, seed=731721, stage=Ordinary travel / idle, overlappingVertices=299, firstVertex=(-73.874, 4.238, -28.781), wood=(-73.993, -29.010), radius=0.6748
- VISUAL_ONLY spawn seed 731722: dinosaur=包鲁鳄, seed=731722, stage=Ordinary travel / idle, overlappingVertices=127, firstVertex=(38.315, 4.681, 35.886), wood=(38.360, 35.926), radius=0.1474
- VISUAL_ONLY spawn seed 731722: dinosaur=蜥结龙, seed=731722, stage=Ordinary travel / idle, overlappingVertices=23, firstVertex=(20.181, 4.980, -13.070), wood=(20.162, -13.022), radius=0.2872
- VISUAL_ONLY spawn seed 731722: dinosaur=霸王龙, seed=731722, stage=Ordinary travel / idle, overlappingVertices=1, firstVertex=(-10.980, 3.103, 42.480), wood=(-10.727, 42.113), radius=0.5321
- VISUAL_ONLY spawn seed 731723: dinosaur=包鲁鳄, seed=731723, stage=Ordinary travel / idle, overlappingVertices=358, firstVertex=(-56.770, 4.484, 98.870), wood=(-57.250, 98.775), radius=0.7000
- VISUAL_ONLY spawn seed 731723: dinosaur=蜥结龙, seed=731723, stage=Ordinary travel / idle, overlappingVertices=306, firstVertex=(-54.772, 2.602, -2.920), wood=(-54.199, -2.693), radius=0.6555
- VISUAL_ONLY spawn seed 731724: dinosaur=包鲁鳄, seed=731724, stage=Ordinary travel / idle, overlappingVertices=933, firstVertex=(0.510, 1.621, -75.527), wood=(0.889, -75.125), radius=0.5840
- VISUAL_ONLY spawn seed 731724: dinosaur=蜥结龙, seed=731724, stage=Ordinary travel / idle, overlappingVertices=613, firstVertex=(50.255, 2.707, 47.546), wood=(50.188, 47.461), radius=0.5055
- VISUAL_ONLY spawn seed 731725: dinosaur=包鲁鳄, seed=731725, stage=Ordinary travel / idle, overlappingVertices=386, firstVertex=(-30.794, 2.613, 98.510), wood=(-31.185, 98.141), radius=0.6290
- VISUAL_ONLY spawn seed 731725: dinosaur=蜥结龙, seed=731725, stage=Ordinary travel / idle, overlappingVertices=520, firstVertex=(80.709, 9.907, 82.159), wood=(80.621, 81.889), radius=0.5687
- VISUAL_ONLY spawn seed 731726: dinosaur=包鲁鳄, seed=731726, stage=Ordinary travel / idle, overlappingVertices=822, firstVertex=(20.061, 1.113, 37.995), wood=(20.176, 37.683), radius=0.5735
- VISUAL_ONLY spawn seed 731726: dinosaur=蜥结龙, seed=731726, stage=Ordinary travel / idle, overlappingVertices=142, firstVertex=(30.035, 3.892, 75.387), wood=(29.381, 75.124), radius=0.9573
- VISUAL_ONLY spawn seed 731726: dinosaur=霸王龙, seed=731726, stage=Ordinary travel / idle, overlappingVertices=7, firstVertex=(7.742, 2.492, 1.392), wood=(7.698, 1.179), radius=0.2582
- VISUAL_ONLY spawn seed 731727: dinosaur=包鲁鳄, seed=731727, stage=Ordinary travel / idle, overlappingVertices=324, firstVertex=(49.390, 2.366, 20.899), wood=(49.053, 20.826), radius=0.4187
- VISUAL_ONLY spawn seed 731727: dinosaur=蜥结龙, seed=731727, stage=Ordinary travel / idle, overlappingVertices=676, firstVertex=(-20.344, 4.806, 73.554), wood=(-21.095, 73.665), radius=0.8229
- VISUAL_ONLY spawn seed 731728: dinosaur=包鲁鳄, seed=731728, stage=Ordinary travel / idle, overlappingVertices=365, firstVertex=(20.218, 2.024, -8.174), wood=(20.001, -8.067), radius=0.3119
- VISUAL_ONLY spawn seed 731728: dinosaur=蜥结龙, seed=731728, stage=Ordinary travel / idle, overlappingVertices=201, firstVertex=(27.268, 2.690, 36.970), wood=(27.310, 37.002), radius=0.1466
- VISUAL_ONLY spawn seed 731729: dinosaur=包鲁鳄, seed=731729, stage=Ordinary travel / idle, overlappingVertices=869, firstVertex=(-53.401, 0.712, 62.331), wood=(-53.505, 62.291), radius=0.2912
- VISUAL_ONLY spawn seed 731729: dinosaur=蜥结龙, seed=731729, stage=Ordinary travel / idle, overlappingVertices=734, firstVertex=(-9.138, 4.533, 49.808), wood=(-9.151, 50.141), radius=0.4335
- VISUAL_ONLY spawn seed 731729: dinosaur=霸王龙, seed=731729, stage=Ordinary travel / idle, overlappingVertices=22, firstVertex=(59.216, 2.519, 89.835), wood=(59.656, 90.059), radius=0.6647
- VISUAL_ONLY spawn seed 731730: dinosaur=包鲁鳄, seed=731730, stage=Ordinary travel / idle, overlappingVertices=1105, firstVertex=(56.933, 5.124, 89.517), wood=(56.661, 90.065), radius=0.7513
- VISUAL_ONLY spawn seed 731730: dinosaur=蜥结龙, seed=731730, stage=Ordinary travel / idle, overlappingVertices=317, firstVertex=(13.226, 2.950, -6.211), wood=(13.206, -6.100), radius=0.4789
- VISUAL_ONLY spawn seed 731731: dinosaur=包鲁鳄, seed=731731, stage=Ordinary travel / idle, overlappingVertices=134, firstVertex=(11.420, 1.830, -39.571), wood=(11.409, -39.798), radius=0.3009
- VISUAL_ONLY spawn seed 731731: dinosaur=蜥结龙, seed=731731, stage=Ordinary travel / idle, overlappingVertices=46, firstVertex=(83.931, 8.202, 76.526), wood=(84.358, 76.991), radius=0.7976
- VISUAL_ONLY spawn seed 731732: dinosaur=包鲁鳄, seed=731732, stage=Ordinary travel / idle, overlappingVertices=321, firstVertex=(75.177, 1.241, 88.751), wood=(75.153, 88.428), radius=0.3696
- VISUAL_ONLY spawn seed 731732: dinosaur=蜥结龙, seed=731732, stage=Ordinary travel / idle, overlappingVertices=828, firstVertex=(-42.546, 4.301, 96.918), wood=(-42.833, 96.715), radius=0.6909
- VISUAL_ONLY spawn seed 731733: dinosaur=包鲁鳄, seed=731733, stage=Ordinary travel / idle, overlappingVertices=352, firstVertex=(29.246, 1.230, 49.345), wood=(29.105, 49.124), radius=0.2947
- VISUAL_ONLY spawn seed 731733: dinosaur=蜥结龙, seed=731733, stage=Ordinary travel / idle, overlappingVertices=254, firstVertex=(47.770, 2.862, -34.196), wood=(47.429, -34.103), radius=0.7092
- VISUAL_ONLY spawn seed 731733: dinosaur=霸王龙, seed=731733, stage=Ordinary travel / idle, overlappingVertices=30, firstVertex=(5.825, 2.518, 30.610), wood=(5.493, 30.507), radius=0.4032
- VISUAL_ONLY spawn seed 731734: dinosaur=包鲁鳄, seed=731734, stage=Ordinary travel / idle, overlappingVertices=80, firstVertex=(-59.756, 2.178, 26.554), wood=(-59.328, 26.482), radius=0.5796
- VISUAL_ONLY spawn seed 731734: dinosaur=蜥结龙, seed=731734, stage=Ordinary travel / idle, overlappingVertices=257, firstVertex=(7.443, 2.918, 40.405), wood=(7.456, 40.147), radius=0.3783
- VISUAL_ONLY spawn seed 731735: dinosaur=包鲁鳄, seed=731735, stage=Ordinary travel / idle, overlappingVertices=420, firstVertex=(58.373, 1.579, 37.070), wood=(58.087, 37.658), radius=0.7047
- VISUAL_ONLY spawn seed 731735: dinosaur=蜥结龙, seed=731735, stage=Ordinary travel / idle, overlappingVertices=238, firstVertex=(-3.036, 4.117, -30.108), wood=(-3.144, -30.060), radius=0.1549
- VISUAL_ONLY spawn seed 731735: dinosaur=霸王龙, seed=731735, stage=Ordinary travel / idle, overlappingVertices=18, firstVertex=(52.303, 0.341, 34.449), wood=(52.268, 34.517), radius=0.1252
- VISUAL_ONLY spawn seed 731736: dinosaur=包鲁鳄, seed=731736, stage=Ordinary travel / idle, overlappingVertices=326, firstVertex=(-65.267, 4.858, -10.178), wood=(-65.500, -9.500), radius=0.7500
- VISUAL_ONLY spawn seed 731736: dinosaur=蜥结龙, seed=731736, stage=Ordinary travel / idle, overlappingVertices=104, firstVertex=(-13.401, 7.807, 29.826), wood=(-13.498, 30.024), radius=0.5690
- VISUAL_ONLY spawn seed 731737: dinosaur=包鲁鳄, seed=731737, stage=Ordinary travel / idle, overlappingVertices=215, firstVertex=(34.054, 1.149, -2.557), wood=(34.114, -2.656), radius=0.1855
- VISUAL_ONLY spawn seed 731737: dinosaur=蜥结龙, seed=731737, stage=Ordinary travel / idle, overlappingVertices=149, firstVertex=(-8.351, 4.931, 47.004), wood=(-8.271, 47.011), radius=0.1113
- VISUAL_ONLY spawn seed 731738: dinosaur=包鲁鳄, seed=731738, stage=Ordinary travel / idle, overlappingVertices=2913, firstVertex=(-79.583, 0.311, -49.250), wood=(-79.916, -49.057), radius=0.4436
- VISUAL_ONLY spawn seed 731738: dinosaur=蜥结龙, seed=731738, stage=Ordinary travel / idle, overlappingVertices=535, firstVertex=(-19.562, 3.068, -46.011), wood=(-19.744, -46.139), radius=0.6948
- VISUAL_ONLY spawn seed 731738: dinosaur=霸王龙, seed=731738, stage=Ordinary travel / idle, overlappingVertices=45, firstVertex=(27.676, 2.518, -41.175), wood=(27.459, -41.633), radius=0.7131
- VISUAL_ONLY spawn seed 731739: dinosaur=包鲁鳄, seed=731739, stage=Ordinary travel / idle, overlappingVertices=73, firstVertex=(5.015, 1.089, 13.419), wood=(5.212, 13.299), radius=0.3278
- VISUAL_ONLY spawn seed 731739: dinosaur=蜥结龙, seed=731739, stage=Ordinary travel / idle, overlappingVertices=134, firstVertex=(-26.276, 2.802, 8.518), wood=(-26.370, 8.532), radius=0.2688
- VISUAL_ONLY spawn seed 731740: dinosaur=包鲁鳄, seed=731740, stage=Ordinary travel / idle, overlappingVertices=171, firstVertex=(23.167, 1.043, -2.638), wood=(23.344, -2.676), radius=0.2310
- VISUAL_ONLY spawn seed 731740: dinosaur=蜥结龙, seed=731740, stage=Ordinary travel / idle, overlappingVertices=165, firstVertex=(27.244, 2.957, -21.243), wood=(27.278, -21.496), radius=0.5338
- VISUAL_ONLY spawn seed 731741: dinosaur=包鲁鳄, seed=731741, stage=Ordinary travel / idle, overlappingVertices=505, firstVertex=(28.431, 1.007, 95.732), wood=(28.385, 95.888), radius=0.4963
- VISUAL_ONLY spawn seed 731741: dinosaur=蜥结龙, seed=731741, stage=Ordinary travel / idle, overlappingVertices=142, firstVertex=(-32.984, 2.970, -15.505), wood=(-32.981, -15.637), radius=0.2044
- VISUAL_ONLY spawn seed 731742: dinosaur=包鲁鳄, seed=731742, stage=Ordinary travel / idle, overlappingVertices=830, firstVertex=(-54.769, 1.411, -66.406), wood=(-54.987, -66.712), radius=0.4716
- VISUAL_ONLY spawn seed 731742: dinosaur=蜥结龙, seed=731742, stage=Ordinary travel / idle, overlappingVertices=280, firstVertex=(23.649, 3.257, 39.222), wood=(23.396, 39.234), radius=0.5340
- VISUAL_ONLY spawn seed 731743: dinosaur=包鲁鳄, seed=731743, stage=Ordinary travel / idle, overlappingVertices=148, firstVertex=(53.530, 5.191, -8.363), wood=(54.000, -7.500), radius=1.1000
- VISUAL_ONLY spawn seed 731743: dinosaur=蜥结龙, seed=731743, stage=Ordinary travel / idle, overlappingVertices=327, firstVertex=(-27.452, 10.121, 72.459), wood=(-27.643, 72.410), radius=1.1000
- VISUAL_ONLY spawn seed 731744: dinosaur=包鲁鳄, seed=731744, stage=Ordinary travel / idle, overlappingVertices=86, firstVertex=(40.868, 2.586, 66.048), wood=(40.639, 66.209), radius=0.4412
- VISUAL_ONLY spawn seed 731744: dinosaur=蜥结龙, seed=731744, stage=Ordinary travel / idle, overlappingVertices=359, firstVertex=(16.288, 1.986, -2.340), wood=(16.292, -2.389), radius=0.4185
- VISUAL_ONLY spawn seed 731744: dinosaur=霸王龙, seed=731744, stage=Ordinary travel / idle, overlappingVertices=45, firstVertex=(56.840, 2.877, 43.821), wood=(56.419, 43.910), radius=0.5108
- VISUAL_ONLY spawn seed 731745: dinosaur=包鲁鳄, seed=731745, stage=Ordinary travel / idle, overlappingVertices=179, firstVertex=(14.304, 5.468, 96.876), wood=(14.060, 96.219), radius=0.8486
- VISUAL_ONLY spawn seed 731745: dinosaur=蜥结龙, seed=731745, stage=Ordinary travel / idle, overlappingVertices=37, firstVertex=(-5.148, 4.907, 48.778), wood=(-5.238, 49.011), radius=0.4585
- VISUAL_ONLY spawn seed 731746: dinosaur=包鲁鳄, seed=731746, stage=Ordinary travel / idle, overlappingVertices=406, firstVertex=(50.316, -0.495, 75.817), wood=(50.348, 75.754), radius=0.1988
- VISUAL_ONLY spawn seed 731746: dinosaur=蜥结龙, seed=731746, stage=Ordinary travel / idle, overlappingVertices=617, firstVertex=(-57.730, 2.565, -49.762), wood=(-57.567, -49.780), radius=0.5200
- VISUAL_ONLY spawn seed 731747: dinosaur=包鲁鳄, seed=731747, stage=Ordinary travel / idle, overlappingVertices=875, firstVertex=(-49.612, 0.871, -69.461), wood=(-49.933, -69.523), radius=0.4875
- VISUAL_ONLY spawn seed 731747: dinosaur=蜥结龙, seed=731747, stage=Ordinary travel / idle, overlappingVertices=510, firstVertex=(36.061, 3.119, -40.530), wood=(35.908, -41.157), radius=0.7526
- VISUAL_ONLY spawn seed 731747: dinosaur=霸王龙, seed=731747, stage=Ordinary travel / idle, overlappingVertices=2, firstVertex=(27.249, 3.126, -21.869), wood=(27.157, -21.849), radius=0.1250
32 fixed seeds: runtime body-envelope, navigation, camera/peer spacing and six-metre exit contracts passed for both active species; visible mesh results are reported separately.
- VISUAL_ONLY checkpoint 30 FPS / 30.0s: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=292, firstVertex=(34.114, 6.294, 47.229), wood=(34.273, 47.200), radius=0.2796
- VISUAL_ONLY checkpoint 30 FPS / 30.0s: dinosaur=蜥结龙, seed=731716, stage=Ordinary travel / idle, overlappingVertices=240, firstVertex=(-37.326, 4.803, 10.159), wood=(-37.390, 10.203), radius=0.2481
- VISUAL_ONLY checkpoint 30 FPS / 60.0s: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=69, firstVertex=(55.903, 2.852, 48.837), wood=(55.316, 49.404), radius=0.8934
- VISUAL_ONLY checkpoint 30 FPS / 60.0s: dinosaur=蜥结龙, seed=731716, stage=Emergency escape (obstacles ignored), overlappingVertices=307, firstVertex=(-76.297, 2.798, 14.093), wood=(-75.818, 14.019), radius=0.5135
- 30 FPS / 60s / 包鲁鳄: 36.14m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 30 FPS / 60s / 厚头龙: 39.67m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 30 FPS / 60s / 原角龙: 16.89m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 30 FPS / 60s / 蜥结龙: 19.76m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 30 FPS / 60s / 霸王龙: 62.77m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 30 FPS / 60s / 伶盗龙: 64.88m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
Maximum planted-foot error at 30 FPS: 0.0023m (limit 0.25m); PASS / 0 failing animal frames.

- VISUAL_ONLY checkpoint 60 FPS / 30.0s: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=312, firstVertex=(34.117, 6.298, 47.239), wood=(34.273, 47.200), radius=0.2796
- VISUAL_ONLY checkpoint 60 FPS / 30.0s: dinosaur=蜥结龙, seed=731716, stage=Ordinary travel / idle, overlappingVertices=335, firstVertex=(-30.970, 2.642, 2.414), wood=(-31.342, 2.317), radius=0.4335
- VISUAL_ONLY checkpoint 60 FPS / 60.0s: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=71, firstVertex=(55.883, 2.860, 48.835), wood=(55.316, 49.404), radius=0.8934
- VISUAL_ONLY checkpoint 60 FPS / 60.0s: dinosaur=蜥结龙, seed=731716, stage=Emergency escape (obstacles ignored), overlappingVertices=443, firstVertex=(-79.616, 10.777, 18.908), wood=(-80.000, 18.843), radius=0.4872
- 60 FPS / 60s / 包鲁鳄: 36.13m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 60 FPS / 60s / 厚头龙: 39.58m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 60 FPS / 60s / 原角龙: 16.89m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 60 FPS / 60s / 蜥结龙: 19.69m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 60 FPS / 60s / 霸王龙: 63.03m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 60 FPS / 60s / 伶盗龙: 65.27m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
Maximum planted-foot error at 60 FPS: 0.0038m (limit 0.25m); PASS / 0 failing animal frames.

- VISUAL_ONLY checkpoint 144 FPS / 30.0s: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=318, firstVertex=(34.097, 6.302, 47.237), wood=(34.273, 47.200), radius=0.2796
- VISUAL_ONLY checkpoint 144 FPS / 30.0s: dinosaur=蜥结龙, seed=731716, stage=Ordinary travel / idle, overlappingVertices=323, firstVertex=(-32.791, 2.855, 4.005), wood=(-33.204, 3.939), radius=0.4572
- VISUAL_ONLY checkpoint 144 FPS / 60.0s: dinosaur=包鲁鳄, seed=731716, stage=Ordinary travel / idle, overlappingVertices=75, firstVertex=(55.321, 3.081, 48.677), wood=(55.316, 49.404), radius=0.8934
- VISUAL_ONLY checkpoint 144 FPS / 60.0s: dinosaur=蜥结龙, seed=731716, stage=Ordinary travel / idle, overlappingVertices=370, firstVertex=(-79.205, 3.255, 9.934), wood=(-79.373, 9.545), radius=0.6889
- 144 FPS / 60s / 包鲁鳄: 36.14m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 144 FPS / 60s / 厚头龙: 39.57m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 144 FPS / 60s / 原角龙: 16.89m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 144 FPS / 60s / 蜥结龙: 19.72m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 144 FPS / 60s / 霸王龙: 63.26m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
- 144 FPS / 60s / 伶盗龙: 65.08m; runtime collision contract passed; no root jumps or prolonged static stall. Static overlaps permitted only by the active policy.
Maximum planted-foot error at 144 FPS: 0.0040m (limit 0.25m); PASS / 0 failing animal frames.


Visible-mesh diagnostics: 228 animal samples; 84 samples contained overlaps; 30663 overlapping vertices total (vertices may recur across samples). These counts do not determine collision-contract success.

Runtime collision contract: PASS
