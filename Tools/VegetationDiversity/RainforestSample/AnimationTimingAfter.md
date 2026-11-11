> 历史记录：以下为此前 2 倍测试结果；当前已恢复至 1.5 倍。最新结果见 Tools/DinosaurSizeAcceptance.md。

PASS ANIMATION_TIMING

# Animation timing after

Controlled Main clearing; original collision checks, sampled root motion and foot IK. Simulated FPS, not hardware performance.

|FPS|Action|Expected s|Actual s|Max planted-foot error m|Result|
|---|---|---:|---:|---:|---|
|30|turn 90 left|1.7000|1.7000|0.0039|PASS|
|30|turn 90 right|1.7000|1.7000|0.0029|PASS|
|30|bite close left|2.6667|2.6667|0.0051|PASS|
|30|bite close right|2.2000|2.2000|0.0035|PASS|
|60|turn 90 left|1.7000|1.7000|0.0051|PASS|
|60|turn 90 right|1.7000|1.7000|0.0043|PASS|
|60|bite close left|2.6667|2.6667|0.0052|PASS|
|60|bite close right|2.2000|2.2000|0.0654|PASS|
|144|turn 90 left|1.7000|1.7014|0.0046|PASS|
|144|turn 90 right|1.7000|1.7014|0.0053|PASS|
|144|bite close left|2.6667|2.6667|0.0052|PASS|
|144|bite close right|2.2000|2.2014|0.0452|PASS|
