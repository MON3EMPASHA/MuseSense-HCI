import json
import math
import os

HAND_SHAPES_FILE = "hand_shapes.json"

def normalize_landmarks(landmarks):
    """
    Takes Mediapipe hand landmarks (21 points) and normalizes them:
    1. Shifts origin to the wrist.
    2. Scales by the maximum distance from wrist to any finger tip.
    Returns a flat list of 42 floats [x0, y0, x1, y1, ...].
    """
    if not landmarks or len(landmarks.landmark) != 21:
        return None
    
    # 1. Get wrist coordinates as origin
    wrist_x = landmarks.landmark[0].x
    wrist_y = landmarks.landmark[0].y
    
    # 2. Shift all points relative to wrist
    shifted = []
    max_dist = 0.0
    
    for lm in landmarks.landmark:
        nx = lm.x - wrist_x
        ny = lm.y - wrist_y
        dist = math.sqrt(nx*nx + ny*ny)
        if dist > max_dist:
            max_dist = dist
        shifted.append((nx, ny))
        
    if max_dist == 0:
        return None
        
    # 3. Scale by max_dist to normalize hand size
    normalized = []
    for nx, ny in shifted:
        normalized.append(nx / max_dist)
        normalized.append(ny / max_dist)
        
    return normalized

def save_hand_shape(name, normalized_points, filename=HAND_SHAPES_FILE):
    templates = load_hand_shapes(filename)
    templates[name] = normalized_points
    with open(filename, 'w') as f:
        json.dump(templates, f, indent=4)

def load_hand_shapes(filename=HAND_SHAPES_FILE):
    if os.path.exists(filename):
        try:
            with open(filename, 'r') as f:
                return json.load(f)
        except:
            return {}
    return {}

def recognize_hand_shape(normalized_points, templates, threshold=0.5):
    """
    Compares normalized points against saved templates using Euclidean distance.
    Returns (best_match_name, confidence_score) or (None, 0.0)
    """
    if not normalized_points or not templates:
        return None, 0.0
        
    best_match = None
    best_dist = float('inf')
    
    for name, template_points in templates.items():
        if len(template_points) != len(normalized_points):
            continue
            
        dist = 0.0
        for p1, p2 in zip(normalized_points, template_points):
            dist += (p1 - p2) ** 2
        dist = math.sqrt(dist)
        
        if dist < best_dist:
            best_dist = dist
            best_match = name
            
    # If the distance is below our threshold, it's a match!
    if best_dist < threshold:
        # Convert distance to a 0.0 - 1.0 confidence score
        score = max(0.0, 1.0 - (best_dist / threshold))
        return best_match, score
        
    return None, 0.0
