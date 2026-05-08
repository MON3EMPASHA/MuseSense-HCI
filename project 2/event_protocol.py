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


def to_pretty_json(data: object) -> str:
    """Pretty JSON intended for console logs (not for socket payload framing)."""
    return json.dumps(data, ensure_ascii=True, indent=2, sort_keys=True)


def event_to_console(event: dict) -> str:
    """Human-friendly event output for terminal debugging."""
    event_type = event.get("type", "unknown")
    payload = event.get("payload") or {}
    user = payload.get("user")

    summary_bits: list[str] = [f"type={event_type}"]
    if user:
        summary_bits.append(f"user={user}")

    if event_type == "expression_gaze_update":
        expr = payload.get("expression") or {}
        rec = payload.get("recommended") or {}
        if expr.get("emotion") is not None:
            summary_bits.append(f"emotion={expr.get('emotion')}")
        if expr.get("gaze_zone") is not None:
            summary_bits.append(f"gaze={expr.get('gaze_zone')}")
        if expr.get("valence") is not None:
            try:
                summary_bits.append(f"valence={float(expr.get('valence')):.2f}")
            except (TypeError, ValueError):
                summary_bits.append(f"valence={expr.get('valence')}")
        if rec.get("category") is not None:
            summary_bits.append(f"recommended={rec.get('category')}")

    if event_type == "object_tracking":
        obj = payload.get("object") or {}
        if obj.get("label") is not None:
            summary_bits.append(f"label={obj.get('label')}")
        if obj.get("confidence") is not None:
            try:
                summary_bits.append(f"conf={float(obj.get('confidence')):.2f}")
            except (TypeError, ValueError):
                summary_bits.append(f"conf={obj.get('confidence')}")

    summary = "[EVENT] " + " ".join(summary_bits)
    return summary + "\n" + to_pretty_json(event)
