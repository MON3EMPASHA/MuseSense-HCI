import json
import time


EVENT_PROTOCOL_VERSION = "v1"


def build_event(event_type: str, payload: dict) -> dict:
    return {
        "version": EVENT_PROTOCOL_VERSION,
        "timestamp": int(time.time()),
        "type": event_type,
        "payload": payload,
    }


def event_to_line(event: dict) -> str:
    return json.dumps(event, ensure_ascii=True)
