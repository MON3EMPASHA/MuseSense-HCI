# MuseSense C# Form Documentation

## Scope

This document explains how the C# desktop side works, focused on:

- Solution entry and project structure
- Form behavior and rendering flow
- 3D model loading and textured software rendering
- How to add new museum models safely
- The key files TUIO_CSHARP.sln and TuioDemo.cs

---

## 1) Solution File: TUIO_CSHARP.sln

File: [main_csharp/TUIO11_NET-master/TUIO_CSHARP.sln](../main_csharp/TUIO11_NET-master/TUIO_CSHARP.sln)

This solution contains 3 C# projects:

- TUIO_DEMO: main WinForms app that users run
- TUIO_DUMP: TUIO debug/inspection utility
- TUIO_LIB: TUIO core library classes

Important notes:

- Debug and Release configurations are defined for Any CPU.
- The app logic you are currently customizing is inside TUIO_DEMO and mainly in TuioDemo.cs.

---

## 2) Main Form File: TuioDemo.cs

File: [main_csharp/TUIO11_NET-master/TuioDemo.cs](../main_csharp/TUIO11_NET-master/TuioDemo.cs)

The class TuioDemo is both:

- a WinForms Form
- a TuioListener (receives marker/cursor/blob callbacks)

### 2.1 High-Level Responsibilities

TuioDemo handles:

- Window lifecycle and UI labels
- TUIO object tracking (add/update/remove)
- Persistent selected artifact logic
- OBJ/MTL parsing and texture loading
- Software 3D rendering to the form
- Socket communication with Python side (face/gesture status)

---

## 3) Runtime Architecture

### 3.1 Startup Flow

Main method location:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L1313](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L1313)

Flow:

1. Parse optional TUIO port argument, default 3333.
2. Create TuioDemo.
3. Start WinForms loop with Application.Run.

Constructor location:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L78](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L78)

Constructor does:

1. Sets form properties, enables double buffering, maximizes window.
2. Creates UI labels.
3. Initializes dictionaries for objects, cursors, blobs.
4. Connects TuioClient and registers this form as listener.
5. Starts socket thread to receive messages from Python backend.

### 3.2 Layout and Resizing

Key methods:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L112](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L112)
- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L117](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L117)

Behavior:

- width and height track current client area.
- Top labels and bottom status are repositioned during resize.

---

## 4) TUIO Event Handling

Key methods:

- add object: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L893](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L893)
- update object: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L904](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L904)
- remove object: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L911](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L911)

Current behavior:

- IDs 0, 1, 2 are treated as artifact markers.
- When marker 0/1/2 is seen, it pins the selected model in persistentSymbolID.
- The model remains visible even after marker disappears.
- If another valid marker is seen, selection switches to that new model.

This gives a stable museum presentation instead of forcing users to keep marker visible.

---

## 5) Marker-to-Model Mapping

Mapping function:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L259](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L259)

Current mapping:

- 0 -> Mask of Tutankhamun
- 1 -> Ramses II statue at the Grand Egyptian Museum
- 2 -> King Senwosret III (1836-1818 BC)

Display title function:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L203](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L203)

---

## 6) 3D Model Pipeline (OBJ + MTL + Texture)

### 6.1 Asset Resolution

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L298](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L298)

The loader tries these path styles:

- Relative path
- Relative to Application.StartupPath
- Relative to AppDomain base directory

This avoids path issues when launching from IDE vs executable folder.

### 6.2 OBJ Parsing

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L317](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L317)

What is parsed:

- v lines: vertex positions
- vt lines: UV coordinates
- f lines: faces with vertex and UV indices
- usemtl: per-face material assignment
- mtllib: linked material library files

Faces are triangulated using fan triangulation if polygons have more than 3 points.

### 6.3 MTL Parsing and Texture Loading

MTL parse method:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L487](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L487)

What is parsed:

- newmtl names
- Kd diffuse fallback color (if present)
- map_Kd texture path (primary color source)

Texture loading and caching:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L448](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L448)

### 6.4 Software Rendering

Render method:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L688](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L688)

Rasterizer method:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L618](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L618)

Rendering steps:

1. Rotate and project vertices to 2D.
2. For each triangle, compute barycentric interpolation.
3. Use z-buffer for visibility.
4. Sample texture using interpolated UV.
5. Apply lighting and exposure.
6. Write ARGB pixel buffer and blit to form.

This is a CPU software renderer (not GPU OpenGL/DirectX).

### 6.5 Cleanup

On close, all texture bitmaps in cached models are disposed.

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L875](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L875)

---

## 7) UI and Paint Loop

Main paint method:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L1152](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L1152)

Draw order:

1. Gradient background and spotlight
2. UI readability bars
3. Face indicator and status overlays
4. Cursor path and blobs
5. Marker handling logic
6. Persistent selected model at static center location

Top info text format is updated by:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L1138](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L1138)

---

## 8) Python Socket Integration (C# Side)

Socket thread method:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L998](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L998)

It listens on localhost:5000 messages and updates UI for:

- face detected / lost
- optional gesture messages

This is independent from TUIO marker transport and only affects status indicators/messages.

---

## 9) How To Add A New 3D Model

Assume you want marker ID 3 to show a new artifact.

### 9.1 Put Files In Correct Location

Place model assets under:

- main_csharp/TUIO11_NET-master/bin/Debug/3d models/Your Model Name/

Required minimum:

- Your Model Name.obj
- YourMaterial.mtl
- textures/... (if map_Kd is used)

### 9.2 Verify OBJ/MTL Content

OBJ should contain:

- mtllib ...
- usemtl ...
- v ...
- vt ...
- f ... with UV references

MTL should contain:

- newmtl ...
- map_Kd ...

Kd is optional but useful as fallback.

### 9.3 Add Marker Name

Update artifact name switch:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L203](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L203)

Add:

- case 3: return "Your Artifact Name";

### 9.4 Add OBJ Path Mapping

Update model path switch:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L259](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L259)

Add case 3 with Path.Combine to your .obj.

### 9.5 Allow New ID In Renderable Filter

Update renderable range logic in IsRenderableSymbol:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L211](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L211)

Change from 0..2 to include 3.

### 9.6 Optional Fallback Color

If desired, add fallback color in:

- [main_csharp/TUIO11_NET-master/TuioDemo.cs#L590](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L590)

### 9.7 Run and Test

Test checklist:

1. Present marker ID 3.
2. Confirm top text updates to your artifact.
3. Confirm model appears at static center.
4. Remove marker and ensure model persists.
5. Present another marker and ensure model switches.

---

## 10) Common Issues and Fixes

### Model does not show

- Check GetModelObjPath mapping.
- Confirm .obj filename exact spaces/case.
- Confirm objDirectory-relative map_Kd paths are valid.

### Model is dark

- Check map_Kd texture exists and loads.
- Lighting/exposure is applied in rasterizer.
- If needed, tune lightFactor and exposure in RasterizeTriangle.

### Model has holes or broken patches

- Verify OBJ face winding consistency.
- Verify UV indices and no out-of-range references.
- Keep z-buffer logic intact.

### Build from terminal fails with msbuild not found

- Build from Visual Studio Developer Command Prompt, or from Visual Studio IDE directly.

---

## 11) Fast Reference: Key Methods

- Startup constructor: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L78](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L78)
- Marker add/update/remove: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L893](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L893), [main_csharp/TUIO11_NET-master/TuioDemo.cs#L904](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L904), [main_csharp/TUIO11_NET-master/TuioDemo.cs#L911](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L911)
- OBJ path mapping: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L259](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L259)
- OBJ load: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L317](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L317)
- MTL parse: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L487](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L487)
- Rasterizer: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L618](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L618)
- Render method: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L688](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L688)
- Paint loop: [main_csharp/TUIO11_NET-master/TuioDemo.cs#L1152](../main_csharp/TUIO11_NET-master/TuioDemo.cs#L1152)
- Solution file: [main_csharp/TUIO11_NET-master/TUIO_CSHARP.sln](../main_csharp/TUIO11_NET-master/TUIO_CSHARP.sln)

---

## 12) Recommended Future Improvement

For higher performance and exact material fidelity, migrate rendering to GPU (OpenTK or WPF 3D pipeline) while keeping the same TUIO event and model-selection architecture.
