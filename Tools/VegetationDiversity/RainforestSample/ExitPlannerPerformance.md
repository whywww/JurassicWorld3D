# Historical trail and return cost

PASS HISTORICAL_RETURN_COST

Isolated Main actor/profile in empty flat navigation at a fixed 60 FPS. The recorded follow trail is the only return route; no exit is searched during follow or return. This measures the cost of trail recording and reversed walking, not a planner.

|Metric|Value|
|---|---:|
|Follow time|10.00 s|
|Trail points|52|
|Return walked|8.00 s|
|Stepping turns|3|
|Path queries|40 (at follow start 1)|
|Searches / reviews|0 / 0|
|Per-frame GC during return median/P95/max|4468/8980/57396 bytes|

Searches stay zero: an unobstructed follow and return never replan an exit. Frame GC covers the whole fixture (animation, editor and return), not only the trail. Stepping turns keep the calibrated pose playback, so fixture time can exceed the accumulated walking time; every turn is checked to keep advancing.
