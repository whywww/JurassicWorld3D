# 扩展后路线验收

PASS：三组路线，每组600秒模拟时间；霸王龙与伶盗龙启用。保留原有根跳变、同伴间距、普通行走碰撞和持续停滞断言。转身沿用当前允许静态重叠的规则。

DINOSAUR_ROUTE_GEOMETRY_OK: 32 seeds; all loops, determinism, bounds, self-intersection, seams, full model clearance.
ROUTE_MOVEMENT 霸王龙 route=0 travelled=1213.9m
ROUTE_MOVEMENT 伶盗龙 route=4 travelled=1414.4m
ROUTE_MOVEMENT 霸王龙 route=1 travelled=1181.8m
ROUTE_MOVEMENT 伶盗龙 route=2 travelled=1453.1m
ROUTE_MOVEMENT 霸王龙 route=3 travelled=1126.5m
ROUTE_MOVEMENT 伶盗龙 route=5 travelled=1095.6m
DINOSAUR_ROUTE_MOVEMENT_OK: requested route pairs completed; full routes, continuous movement, peer clearance, no stuck agents. Unrestricted turn frames outside old corridor=106.

日志：Logs/expansion-routes.log。此结果不替代普通自由漫游和支撑脚验收。
