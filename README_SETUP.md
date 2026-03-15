# MuseSense - Setup and Running Guide

## Improvements Made to C# GUI
### inital steps to run this project:
make sure to download the python venv from: https://drive.google.com/drive/folders/1-ELzpc3rc3XIZVDMCU7gUKK2pYLnFa56?usp=sharing
### 1. **Face Detection Display**

- Added visual indicator (green/red circle) in top-right corner
- Face status label shows "✓ Face Detected" or "Face: Not Detected"
- Non-blocking UI - no more MessageBox popups

### 2. **TUIO Marker Detection Display**

- Added marker info label in top-left corner
- Shows artifact name and description when marker is placed
- Yellow glow effect around detected markers
- Displays marker ID on each marker with dark background for visibility

### 3. **Status Information**

- Bottom status bar shows connection status and keyboard shortcuts
- Real-time updates without freezing the GUI

### 4. **Improved Rendering**

- Proper thread-safe UI updates using Invoke/BeginInvoke
- Smooth visual feedback for all interactions
- Error handling for missing image files (shows colored rectangles as fallback)
- Different colors for different marker IDs

## How to Run

### Step 1: Start Python Face Detection Server

```powershell
cd "a:\CS MSA\Year 4 Term 2\HCI\project"
jupyter notebook constantly_run_in_background.ipynb
```

Run all cells to start the socket server on port 5000.

### Step 2: Start reacTIVision (TUIO Tracker)

```powershell
cd "a:\CS MSA\Year 4 Term 2\HCI\project\reacTIVision-1.5.1-win64\reacTIVision-1.5.1-win64"
.\reacTIVision.exe
```

This will start tracking fiducial markers from your camera.

### Step 3: Build and Run C# Application

```powershell
cd "a:\CS MSA\Year 4 Term 2\HCI\project\main_csharp\TUIO11_NET-master"
msbuild TUIO_CSHARP.sln
.\bin\Debug\TuioDemo.exe
```

Or open TUIO_CSHARP.sln in Visual Studio and run the TuioDemo project.

## Keyboard Controls

- **F1**: Toggle fullscreen mode
- **ESC**: Exit application
- **V**: Toggle verbose console output

## Features Demonstration

### Face Detection

When your Python script sends "face:detected", you'll see:

- Green indicator circle in top-right
- Text changes to "✓ Face Detected" in green
- Status updates without blocking the GUI

### TUIO Marker Detection

When you place a marker on the tracking surface:

- Marker info appears in top-left corner
- Yellow glow effect appears around the marker
- Shows artifact name (e.g., "Ancient Pottery Collection")
- Rotating the marker rotates the visual on screen

### Marker IDs

- **Marker 0**: Ancient Pottery Collection (Brown/Orange)
- **Marker 1**: Medieval Manuscript (Blue)
- **Marker 2**: Renaissance Painting (Purple)
- **Other IDs**: Dynamically colored based on ID number

## Troubleshooting

### "Python connection failed"

- Make sure the Python Jupyter notebook is running first
- Verify the socket server is listening on localhost:5000

### "Images not found"

- The app will show colored rectangles if background1.png, obj1.png, etc. are missing
- This is normal and the app will still work

### No markers detected

- Ensure reacTIVision is running and detecting your camera
- Print TUIO markers (amoeba markers work best)
- Adjust camera angle and lighting in reacTIVision configuration

## Next Steps

To fully integrate with your MuseSense project:

1. Update Python script to send proper face and gesture events
2. Add image files (background1.png, obj1.png, etc.) for each artifact
3. Extend the UpdateMarkerInfoInternal() method with your artifact database
4. Add gesture handling to the stream() method
