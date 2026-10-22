# 树根重叠处理

按用户选择保留位置，撤下近乎重合的一棵；不清除树冠覆盖。判定为根部平面距离不足5cm。原场景树保持原父对象与变换，只停用；地形树完整实例字段另存，可恢复。

|类型|撤下的树|原X|原Z|恢复来源|
|---|---|---:|---:|---|
|Scene|tree01_0353|6.0207|-25.3442|Assets/NatureStarterKit2/Nature/tree01.prefab|
|Scene|tree01_0370|6.2424|-25.7523|Assets/NatureStarterKit2/Nature/tree01.prefab|
|Scene|tree04_0198|12.8916|67.9447|Assets/NatureStarterKit2/Nature/tree04.prefab|
|Scene|FabTree_1_0253|5.5139|-24.6227|Assets/VegetationDiversity/Prefabs/FabTree_1.prefab|
|Scene|FabTree_1_0379|6.2095|-24.6952|Assets/VegetationDiversity/Prefabs/FabTree_1.prefab|
|Scene|FabTree_5_0312|-47.4643|32.8868|Assets/VegetationDiversity/Prefabs/FabTree_5.prefab|
|Terrain|tree01|-53.2391|-63.4796|Assets/NatureStarterKit2/Nature/tree01.prefab|

累计撤下7棵（本次操作实际撤下0棵），其余树的位置、树种和数量不变。该数量变化是用户对原计划保持棵数要求的明确修订。记录：Assets/Editor/TreeOverlapWithdrawals.json。


已通过撤下、恢复身份/尺寸一致及重放一致性验证。

