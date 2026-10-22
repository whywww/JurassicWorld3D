# Baurusuchus Main runtime / 144 FPS only

Production controller, navigation, body sweeps, static peers and safety distances remain enabled. Other animal AI is held still for reproducibility.

Normal roaming: travel=6.023m, goal handoffs=1, idle frames=0.
Curved turn: forward motion retained while heading changed by at least 5 degrees.
Blocked enclosure: travel=0.000m, stationary frames=432, no idle rest state.
Unblocked: forward movement resumed after 0.0069s. Total travel=10.841m; maximum planted error=0.00000m.

PASS MAIN_CONTINUOUS_144
