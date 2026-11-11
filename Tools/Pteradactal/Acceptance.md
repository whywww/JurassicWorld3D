> 历史记录：以下为此前 2 倍测试结果；当前已恢复至 1.5 倍。最新结果见 Tools/DinosaurSizeAcceptance.md。

# Pteradactal acceptance

PASS

- Import and scene integration repeated twice; reload contains exactly one flyer.
- Ground dinosaurs preserved: 6.
- Clip: 10.6917s, Legacy, loop.
- Animated bounds: (10.07, 5.74, 4.16); start/end maximum vertex difference: 0.0228m.
- Five simulated minutes, 50Hz: travelled 1500.01m; maximum absolute X/Z 15.00m.
- Height: 13.15..13.85m; local mesh clearance passed; lowest sampled wing/body vertex 10.15m; front passes 5.
- Shader compiles and textures resolve.
- Captures: model-start.png, model-quarter.png, player-view.png.

## Level camera flybys

- Saved camera faces dense forest: no safe 30–45m low flyby fits; defer instead of flying through trees.
- Open view 1: camera=(-8.00, 1.70, -31.00), yaw=0°, pitch=0°; first visible=0.02s; continuous clear visibility=3.02s.
- Open view 2: camera=(-8.00, 1.70, 39.00), yaw=180°, pitch=0°; first visible=0.02s; continuous clear visibility=3.02s.
