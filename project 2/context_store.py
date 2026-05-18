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
                "artifact_scores": {},
                "opened_artifacts": {},
                "context": {
                    "current_artifact": "",
                    "current_category": "",
                    "last_object": "",
                    "last_gesture": "",
                    "last_emotion": "",
                    "last_gaze": "",
                },
                "updated_at": 0,
            }
            self.save()
            return

        context = self.data["users"][user_name].setdefault("context", {})
        context.setdefault("current_artifact", "")
        context.setdefault("current_category", "")
        context.setdefault("last_object", "")
        context.setdefault("last_gesture", "")
        context.setdefault("last_emotion", "")
        context.setdefault("last_gaze", "")

        self.data["users"][user_name].setdefault("artifact_scores", {})
        self.data["users"][user_name].setdefault("opened_artifacts", {})

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

    def update_context(self, user_name: str, **updates) -> dict:
        self.ensure_user(user_name)
        context = self.data["users"][user_name].setdefault("context", {})
        for key, value in updates.items():
            if value is not None:
                context[key] = value
        self.data["users"][user_name]["updated_at"] = int(time.time())
        self.save()
        return context

    def get_context_snapshot(self, user_name: str) -> dict:
        self.ensure_user(user_name)
        return dict(self.data["users"][user_name].get("context", {}))

    def update_category_score(
        self, user_name: str, category: str, delta: float
    ) -> float:
        self.ensure_user(user_name)
        scores = self.data["users"][user_name].setdefault("category_scores", {})
        old_score = float(scores.get(category, 0.0))
        new_score = old_score + float(delta)
        scores[category] = round(new_score, 3)
        self.data["users"][user_name]["updated_at"] = int(time.time())
        self.save()
        return scores[category]

    def update_artifact_score(
        self, user_name: str, artifact_name: str, delta: float
    ) -> float:
        self.ensure_user(user_name)
        scores = self.data["users"][user_name].setdefault("artifact_scores", {})
        key = str(artifact_name).strip()
        if not key:
            return 0.0
        old_score = float(scores.get(key, 0.0))
        new_score = old_score + float(delta)
        scores[key] = round(new_score, 3)
        self.data["users"][user_name]["updated_at"] = int(time.time())
        self.save()
        return scores[key]

    def record_artifact_opened(
        self, user_name: str, artifact_name: str, opened_at: float | None = None
    ) -> float:
        self.ensure_user(user_name)
        key = str(artifact_name).strip().lower()
        if not key:
            return 0.0
        timestamps = self.data["users"][user_name].setdefault("opened_artifacts", {})
        opened_time = float(opened_at if opened_at is not None else time.time())
        timestamps[key] = opened_time
        self.data["users"][user_name]["updated_at"] = int(time.time())
        self.save()
        return opened_time

    def delete_list_item(self, user_name: str, list_name: str, item_id: str) -> bool:
        self.ensure_user(user_name)
        target_list = self.data["users"][user_name]["lists"].setdefault(list_name, [])
        before = len(target_list)
        target_list[:] = [
            item for item in target_list if str(item.get("item_id", "")) != str(item_id)
        ]
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
        context = self.data["users"][user_name].get("context", {})
        current_category = context.get("current_category") or context.get("last_object")
        scores = self.data["users"][user_name].get("category_scores", {})
        if not scores:
            if current_category:
                return {
                    "category": current_category,
                    "reason": "Using current artifact category",
                }
            return {
                "category": "general",
                "reason": "No category score yet",
            }

        top_category = max(scores, key=scores.get)
        if current_category:
            top_score = float(scores.get(top_category, 0.0))
            current_score = float(scores.get(current_category, 0.0))
            if current_score >= top_score * 0.8:
                return {
                    "category": current_category,
                    "reason": "Current focus is close to top score",
                }
        return {
            "category": top_category,
            "reason": f"Highest score={scores[top_category]}",
        }


def apply_gesture_action(
    store: ContextStore,
    user_name: str,
    gesture_name: str,
    item_id: str | None = None,
    category: str | None = None,
) -> dict:
    gesture_key = gesture_name.strip().lower()
    resolved_item_id = item_id or "current_artifact"
    resolved_category = category or "general"

    # Keep action mapping procedural and explicit to match lab style.
    if gesture_key in {"create_artifact", "admincreateartifact"}:
        return {
            "action": "open_create_artifact",
            "result": "opened",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    if gesture_key in {"edit_artifact", "admineditartifact"}:
        return {
            "action": "open_edit_artifact",
            "result": "opened",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    if gesture_key in {"delete_artifact", "admindeleteartifact", "delete", "mute"}:
        return {
            "action": "open_delete_artifact",
            "result": "opened",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    if gesture_key in {"next_artifact", "adminnextartifact"}:
        return {
            "action": "open_next_artifact",
            "result": "opened",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    if gesture_key in {"prev_artifact", "adminprevartifact"}:
        return {
            "action": "open_prev_artifact",
            "result": "opened",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    if gesture_key in {"circle"}:
        return toggle_favorite(
            store,
            user_name,
            resolved_item_id,
            resolved_category,
            source="gesture",
            gesture=gesture_name,
        )

    if gesture_key == "swipeup":
        added = store.create_list_item(
            user_name,
            "explore_later",
            {
                "item_id": resolved_item_id,
                "category": resolved_category,
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
                "item_id": resolved_item_id,
                "category": resolved_category,
                "category_score": score,
            }
        return {
            "action": "add_explore_later",
            "result": "already_exists",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    if gesture_key in {"thumbsup", "goodtosee"}:
        added = store.create_list_item(
            user_name,
            "good_to_see",
            {
                "item_id": resolved_item_id,
                "category": resolved_category,
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
                "item_id": resolved_item_id,
                "category": resolved_category,
                "category_score": score,
            }
        return {
            "action": "add_good_to_see",
            "result": "already_exists",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }


def toggle_favorite(
    store: ContextStore,
    user_name: str,
    item_id: str | None,
    category: str | None,
    source: str,
    gesture: str | None = None,
) -> dict:
    resolved_item_id = str(item_id or "").strip()
    resolved_category = str(category or "general").strip() or "general"
    if not resolved_item_id:
        return {
            "action": "toggle_favorite",
            "result": "missing_item",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    store.ensure_user(user_name)
    target_list = store.data["users"][user_name]["lists"].setdefault("favorites", [])
    existing = any(
        str(item.get("item_id", "")).strip() == resolved_item_id for item in target_list
    )
    if existing:
        removed = store.delete_list_item(user_name, "favorites", resolved_item_id)
        if removed:
            return {
                "action": "remove_favorite",
                "result": "removed",
                "item_id": resolved_item_id,
                "category": resolved_category,
            }
        return {
            "action": "remove_favorite",
            "result": "not_found",
            "item_id": resolved_item_id,
            "category": resolved_category,
        }

    payload = {
        "item_id": resolved_item_id,
        "category": resolved_category,
        "source": source,
        "timestamp": int(time.time()),
    }
    if gesture:
        payload["gesture"] = gesture

    added = store.create_list_item(user_name, "favorites", payload)
    if added:
        score = store.update_category_score(user_name, "favorites", 1.0)
        return {
            "action": "add_favorite",
            "result": "created",
            "item_id": resolved_item_id,
            "category": resolved_category,
            "category_score": score,
        }
    return {
        "action": "add_favorite",
        "result": "already_exists",
        "item_id": resolved_item_id,
        "category": resolved_category,
    }
