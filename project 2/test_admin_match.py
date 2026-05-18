import json, pathlib, random
from hand_shape_recognizer import recognize_hand_shape

p = pathlib.Path(__file__).parent / 'admin_hand_shapes.json'
bank = json.loads(p.read_text())
print(f"Loaded {len(bank)} templates: {list(bank.keys())}")

for name, pts in bank.items():
    match, score = recognize_hand_shape(pts, bank, threshold=0.45)
    print(f"{name} self-match -> {match} score={score:.3f}")
    for noise in (0.01, 0.02, 0.05):
        noisy = [p + random.uniform(-noise, noise) for p in pts]
        m2, s2 = recognize_hand_shape(noisy, bank, threshold=0.45)
        print(f"  noisy {noise:.3f} -> {m2} score={s2:.3f}")

# Also test mirrored variant
for name, pts in bank.items():
    mirrored = []
    for i in range(0, len(pts), 2):
        mirrored.extend([-pts[i], pts[i+1]])
    m, s = recognize_hand_shape(mirrored, bank, threshold=0.45)
    print(f"{name} mirrored -> {m} score={s:.3f}")
