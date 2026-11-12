# All-tier Player performance

Visible 1920x1080 non-Development Players; 15s warmup, 60s samples, three repeats. Median = median of run medians; P95/P99 = worst repeat; average FPS includes long frames. Dinosaur close-up follows an active animal, so dynamic visibility varies.

The shipped final defaults use native 1080p in every tier. Native optimization and final results are therefore identical. Scaled candidates (Low 75%, Medium 85%, High 100%) were measured separately and are not selected defaults.

|Tier|View|Before median ms|Final native ms|Scaled candidate ms|Before / final average FPS|Candidate average FPS|Before / final P95 ms|Final P99 ms|
|---|---|---:|---:|---:|---:|---:|---:|---:|
|Low|forest|8.04|5.27|5.74|46.7 / 114.4|105.9|137.56 / 27.14|49.74|
|Low|creek|26.29|8.57|8.37|21.4 / 69.5|89.5|239.13 / 36.97|65.75|
|Low|dinosaur-close|9.78|8.30|7.40|25.2 / 75.2|64.6|176.59 / 47.43|78.21|
|Low|route|7.95|5.85|4.81|48.9 / 119.2|149.3|231.87 / 24.60|69.85|
|Medium|forest|54.56|7.08|7.51|14.7 / 72.4|72.6|216.88 / 39.46|93.09|
|Medium|creek|51.77|12.76|12.87|16.4 / 48.6|51.6|186.93 / 74.91|116.60|
|Medium|dinosaur-close|47.57|10.84|11.72|18.6 / 63.9|59.3|125.55 / 42.79|69.13|
|Medium|route|76.52|6.56|7.69|10.5 / 91.4|79.4|264.05 / 53.74|93.50|
|High|forest|69.86|8.72|8.72|12.4 / 78.9|78.9|186.35 / 32.70|48.80|
|High|creek|82.34|12.27|12.27|9.7 / 55.8|55.8|332.63 / 50.84|75.74|
|High|dinosaur-close|117.27|12.80|12.80|7.6 / 63.8|63.8|300.83 / 41.50|63.79|
|High|route|75.18|8.08|8.08|12.1 / 78.4|78.4|233.80 / 48.82|76.08|

Low sources: before / native / scaled — 原始采样日志已随本机 `Logs/` 清理。

Medium sources: before / native / scaled — 原始采样日志已随本机 `Logs/` 清理。

High sources: before / native / scaled — 原始采样日志已随本机 `Logs/` 清理。

No fixed FPS target was imposed. Frame-time regressions and unsupported counters must be reviewed in the raw reports.
