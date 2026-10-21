# 样板树木尺寸协调

树种基准与外围一致，等比例缩放 0.82～1.14 倍。原 15～21 米范围已移除。

|树|树种|实际高度 m|基准 m|原位置与树种保留|
|---|---|---:|---:|---|
|RainforestTree_0|tree03|8.10|8.0|True|
|RainforestTree_1|tree02|9.82|9.0|True|
|RainforestTree_2|Pachira_B|6.09|6.0|True|
|RainforestTree_3|Pachira_B|5.26|6.0|True|
|RainforestTree_4|tree01|9.15|10.0|True|
|RainforestTree_5|Pachira_B|5.88|6.0|True|
|RainforestTree_6|Pachira_B|6.64|6.0|True|
|RainforestTree_7|tree02|9.77|9.0|True|
|RainforestTree_8|Pachira_B|5.91|6.0|True|
|RainforestTree_9|tree01|10.90|10.0|True|
|RainforestTree_10|tree03|7.01|8.0|True|
|RainforestTree_11|Pachira_B|6.00|6.0|True|
|RainforestTree_12|Pachira_B|5.54|6.0|True|
|RainforestTree_13|tree01|11.20|10.0|True|
|RainforestTree_14|tree03|7.26|8.0|True|
|RainforestTree_15|tree04|5.91|6.0|True|
|RainforestTree_16|Pachira_B|6.71|6.0|True|
|RainforestTree_17|tree03|7.69|8.0|True|
|RainforestTree_18|tree02|10.20|9.0|True|
|RainforestTree_19|tree03|7.44|8.0|True|
|RainforestTree_20|Pachira_B|5.54|6.0|True|
|RainforestTree_21|tree01|11.36|10.0|True|
|RainforestTree_22|Pachira_B|5.50|6.0|True|
|RainforestTree_23|Pachira_B|5.22|6.0|True|
|RainforestTree_24|Pachira_B|4.93|6.0|True|
|RainforestTree_25|tree04|6.30|6.0|True|
|RainforestTree_26|tree02|9.19|9.0|True|
|RainforestTree_27|tree03|8.70|8.0|False|
|RainforestTree_28|Pachira_B|5.33|6.0|False|

原种植记录 29 棵；保留位置与树种 27 棵。冲突候选继续经过溪流、路线、出生、硬枝及互动空间检查。

本次保留既有样板分区及其面积记录，不按当前更细的动画分块重新缩小林隙或增种。种植记录同时保存原有林隙、连接通道及密度统计；未扩大排除区以提高密度比例。

Build and Verify 同时检查两次生成一致、撤销、Bake 接入和外围对象及 Terrain 数据不变。固定入口/林内/侧面机位对照：size-before-0..2.png、size-after-0..2.png。运动与全场景检查结果见 TreeSizingVerification.md，不能仅凭构建通过推断普通 Play 全部通过。

