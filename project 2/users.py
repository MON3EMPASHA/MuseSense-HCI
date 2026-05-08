from pathlib import Path
import json
from typing import Dict


def normalize_mac(mac: str) -> str:
    return mac.strip().upper().replace("-", ":")


def load_users_by_mac(json_path: Path) -> Dict[str, dict]:
    if not json_path.exists():
        print(f"[USERS] users.json not found at {json_path}")
        return {}

    try:
        with json_path.open("r", encoding="utf-8") as json_file:
            users_data = json.load(json_file)
    except Exception as e:
        print(f"[USERS] Failed to read users.json: {e}")
        return {}

    users_by_mac: dict[str, dict] = {}
    if isinstance(users_data, list):
        for user in users_data:
            if not isinstance(user, dict):
                continue
            name = user.get("name")
            mac_field = user.get("mac")

            if not name or not mac_field:
                continue

            if isinstance(mac_field, list):
                mac_values = [str(mac).strip() for mac in mac_field if str(mac).strip()]
            else:
                mac_values = [str(mac_field).strip()]

            normalized_macs = [normalize_mac(mac) for mac in mac_values if mac]

            for normalized_mac in normalized_macs:
                users_by_mac[normalized_mac] = {
                    "type": "user_login",
                    "name": str(name).strip(),
                    "age": str(user.get("age", "")).strip(),
                    "gender": str(user.get("gender", "")).strip(),
                    "mac": normalized_mac,
                    "Profile": str(user.get("Profile", "")).strip(),
                    "themeMode": str(user.get("themeMode", "light")).strip() or "light",
                }

    return users_by_mac
