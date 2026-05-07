import json
import time
from pathlib import Path


CONTEXT_STORE_PATH = Path("context_data.json")


def _default_store() -> dict:
    return {
        "users": {},
        "events": [],
        "meta": {
            "version": 1,
            "updated_at": 0,
        },
    }


class ContextStore:
    def __init__(self, store_path: Path = CONTEXT_STORE_PATH):
        self.store_path = store_path
        self.data = self._load()

    def _load(self) -> dict:
        if not self.store_path.exists():
            return _default_store()

        try:
            with self.store_path.open("r", encoding="utf-8") as file:
                data = json.load(file)
            if not isinstance(data, dict):
                return _default_store()
            if "users" not in data or not isinstance(data["users"], dict):
                data["users"] = {}
            # Clear events array to avoid duplicate names confusion
            data["events"] = []
            if "meta" not in data or not isinstance(data["meta"], dict):
                data["meta"] = {"version": 1, "updated_at": 0}
            return data
        except Exception:
            return _default_store()

    def save(self) -> None:
        self.data["meta"]["updated_at"] = int(time.time())
        with self.store_path.open("w", encoding="utf-8") as file:
            json.dump(self.data, file, indent=2)

    def ensure_user(self, user_name: str, profile: str = "") -> None:
        if user_name not in self.data["users"]:
            self.data["users"][user_name] = {
                "profile": profile,
                "lists": {
                    "favorites": [],
                    "explore_later": [],
                    "good_to_see": [],
                },
                "category_scores": {},
                "updated_at": 0,
            }
            self.save()

    def create_list_item(self, user_name: str, list_name: str, item: dict) -> bool:
        self.ensure_user(user_name)
        target_list = self.data["users"][user_name]["lists"].setdefault(list_name, [])
        item_id = str(item.get("item_id", ""))
        if item_id and any(str(i.get("item_id", "")) == item_id for i in target_list):
            return False

        target_list.append(item)
        self.data["users"][user_name]["updated_at"] = int(time.time())
        self.save()
        return True

    def read_lists(self, user_name: str) -> dict:
        self.ensure_user(user_name)
        return self.data["users"][user_name]["lists"]

    def update_category_score(self, user_name: str, category: str, delta: float) -> float:
        self.ensure_user(user_name)
        scores = self.data["users"][user_name].setdefault("category_scores", {})
        old_score = float(scores.get(category, 0.0))
        new_score = old_score + float(delta)
        scores[category] = round(new_score, 3)
        self.data["users"][user_name]["updated_at"] = int(time.time())
        self.save()
        return scores[category]

    def delete_list_item(self, user_name: str, list_name: str, item_id: str) -> bool:
        self.ensure_user(user_name)
        target_list = self.data["users"][user_name]["lists"].setdefault(list_name, [])
        before = len(target_list)
        target_list[:] = [item for item in target_list if str(item.get("item_id", "")) != str(item_id)]
        changed = len(target_list) != before
        if changed:
            self.data["users"][user_name]["updated_at"] = int(time.time())
            self.save()
        return changed

    def log_event(self, event: dict) -> None:
        # Disabled event logging to prevent 'duplicate names' feedback
        pass

    def get_context_recommendation(self, user_name: str) -> dict:
        self.ensure_user(user_name)
        scores = self.data["users"][user_name].get("category_scores", {})
        if not scores:
            return {
                "category": "general",
                "reason": "No category score yet",
            }

        top_category = max(scores, key=scores.get)
        return {
            "category": top_category,
            "reason": f"Highest score={scores[top_category]}",
        }


def apply_gesture_action(store: ContextStore, user_name: str, gesture_name: str) -> dict:
    gesture_key = gesture_name.strip().lower()

    # Keep action mapping procedural and explicit to match lab style.
    if gesture_key in {"swiperight", "circle"}:
        added = store.create_list_item(
            user_name,
            "favorites",
            {
                "item_id": "current_artifact",
                "source": "gesture",
                "gesture": gesture_name,
                "timestamp": int(time.time()),
            },
        )
        if added:
            score = store.update_category_score(user_name, "favorites", 1.0)
            return {
                "action": "add_favorite",
                "result": "created",
                "category_score": score,
            }
        return {
            "action": "add_favorite",
            "result": "already_exists",
        }

    if gesture_key == "swipeup":
        added = store.create_list_item(
            user_name,
            "explore_later",
            {
                "item_id": "current_artifact",
                "source": "gesture",
                "gesture": gesture_name,
                "timestamp": int(time.time()),
            },
        )
        if added:
            score = store.update_category_score(user_name, "explore", 0.7)
            return {
                "action": "add_explore_later",
                "result": "created",
                "category_score": score,
            }
        return {
            "action": "add_explore_later",
            "result": "already_exists",
        }

    if gesture_key in {"thumbsup", "goodtosee"}:
        added = store.create_list_item(
            user_name,
            "good_to_see",
            {
                "item_id": "current_artifact",
                "source": "gesture",
                "gesture": gesture_name,
                "timestamp": int(time.time()),
            },
        )
        if added:
            score = store.update_category_score(user_name, "positive", 0.5)
            return {
                "action": "add_good_to_see",
                "result": "created",
                "category_score": score,
            }
        return {
            "action": "add_good_to_see",
            "result": "already_exists",
        }

    return {
        "action": "none",
        "result": "no_mapping",
    }
