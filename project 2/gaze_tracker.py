from __future__ import annotations


class GazeTracker:
    def __init__(self):
        self.hit_counts = {
            "left": 0,
            "center": 0,
            "right": 0,
        }

    def register(self, gaze_zone: str) -> dict:
        zone = str(gaze_zone).strip().lower()
        if zone not in self.hit_counts:
            zone = "center"
        self.hit_counts[zone] += 1
        return {
            "zone": zone,
            "hit_counts": dict(self.hit_counts),
        }

    def top_zone(self) -> str:
        return max(self.hit_counts, key=self.hit_counts.get)
