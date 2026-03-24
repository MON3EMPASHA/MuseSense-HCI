# Project 2 Setup (Team Guide)

This folder is an independent Python project.
Use Python 3.11. Python 3.12 is not the supported target for this setup and can break the Bluetooth dependency install.

## 1) Prerequisites

- Windows
- Python 3.11 installed (check with `py -0p`)

## 2) Create and activate local venv

Run these commands from the repository root:

```powershell
py -3.11 -m venv .venv
.\.venv\Scripts\Activate.ps1
```

## 3) Install dependencies

```powershell
python -m pip install --upgrade pip
python -m pip install -r "project 2\requirements.txt"
```

## 4) Register Jupyter kernel (one time)

```powershell
python -m pip install ipykernel
python -m ipykernel install --user --name project2-py311 --display-name "Project 2 (py311 venv)"
```

## 5) Open notebook and select kernel

Notebook file:

- `constantly_run_in_background.ipynb`

In VS Code, select kernel:

- `Project 2 (py311 venv)`

If it still shows another Python, change kernel manually from the top-right kernel picker.

## 6) Quick verification cell

Run this in a notebook cell:

```python
import sys, numpy as np, mediapipe as mp, cv2
print(sys.version)
print("numpy", np.__version__)
print("mediapipe", mp.__version__)
print("opencv", cv2.__version__)
```

Expected:

- Python 3.11.x
- NumPy 1.26.4
- MediaPipe 0.10.14

## 7) Run order

- Run Cell 3 first (version check)
- Then run Cell 1 or Cell 2

## Notes

- Do not commit `venv` to git.
- Each teammate creates their own local `venv`.
- Share code and `requirements.txt`, not virtual environments.
