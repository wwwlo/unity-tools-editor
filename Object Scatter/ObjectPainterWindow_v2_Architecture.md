# ObjectPainterWindow_v2 — Architecture Reference

## Overview

`ObjectPainterWindow_v2` is a Unity Editor tool (`EditorWindow`) for painting, erasing, and scaling prefab objects directly onto scene geometry via brush-based interaction in the Scene View. It supports both standard `GameObject` instantiation and Nature Renderer's `NatureInstance` component for GPU-instanced vegetation.

---

## Class Structure

```
ObjectPainterWindow_v2 : EditorWindow
├── PrefabSettings (nested struct)
├── State fields
├── OnGUI()
├── Scene View hooks (OnEnable / OnDisable / OnDestroy)
├── OnSceneGUI()
│   ├── HandleBrushSizeChange()
│   ├── HandlePainting()
│   ├── HandleErasing()
│   ├── HandleScaling()
│   └── DrawBrushPreview()
├── Helpers
│   ├── SetTemporaryLayer() / RestoreOriginalLayer()
│   ├── CanPaint()
│   └── CalculateScaleFromScreenSpace()
└── Operations
    ├── EraseGameObjects()
    ├── ScaleGameObjects()
    └── ClearPaintedObjects()
```

---

## PrefabSettings Struct

Serializable per-prefab configuration stored in a `List<PrefabSettings>`.

| Field | Type | Purpose |
|---|---|---|
| `prefab` | `GameObject` | Source prefab to paint |
| `useNatureInstance` | `bool` | Route through `NatureInstance` instead of `PrefabUtility` |
| `initialRotation` | `Vector3` | Base rotation applied to every placed object |
| `randomRotation` | `Vector3` | Per-axis random rotation range |
| `minPaintAngle` / `maxPaintAngle` | `float` | Surface normal angle gate |
| `rotateToNormal` | `bool` | Align Y-axis to surface normal |
| `minTextureScale` / `maxTextureScale` | `float` | Scale range driven by noise texture sampling |
| `density` | `int` | Objects spawned per brush stroke tick (0-300) |
| `brushCenterScale` / `brushEdgeScale` | `float` | Scale gradient from brush center to edge |
| `brushScaleExponent` | `float` | Power curve applied to the center-to-edge lerp |

---

## State Fields

| Field | Purpose |
|---|---|
| `surfaceToPaintOn` | Target `GameObject` raycasts are tested against |
| `parentObject` | Hierarchy parent for spawned objects |
| `textureForScaling` | Noise `Texture2D` for screen-space scale sampling |
| `prefabs` | List of active `PrefabSettings` |
| `brushSize` | World-space brush radius (scroll wheel adjustable) |
| `timeBetweenPaints` | Minimum seconds between paint ticks |
| `randomScaleOffset` | Additional random scale noise |
| `isPainting` | Whether a stroke is in progress |
| `isErasing` / `isScaling` | Active tool mode flags (mutually exclusive) |
| `eraserProbability` | 0-1 chance each object in range is erased |
| `scaleMultiplier` | Per-tick scale factor in scaling mode |
| `lastPaintPosition` / `lastPaintTime` | Throttle controls for stroke frequency |

---

## Lifecycle

```
OnEnable  -> subscribes OnSceneGUI to SceneView.duringSceneGui
OnDisable -> unsubscribes
OnDestroy -> unsubscribes (guard against force-close)
```

The default noise texture (`Assets/Textures/Def_Noise.tga`) is auto-loaded in `OnEnable` if `textureForScaling` is null.

---

## Input Model

All three modes share the same gesture:

- `Ctrl + Shift + LMB Down` — begin stroke
- `Ctrl + Shift + LMB Drag` — continuous tick (throttled by `timeBetweenPaints` and `MIN_MOUSE_MOVEMENT`)
- `LMB Up` — end stroke
- `Scroll Wheel` — adjust `brushSize` (0.1-50 world units)

Mode is selected via the `isErasing` / `isScaling` toggles in the GUI (mutually exclusive).

---

## Raycast Strategy

To avoid hitting unintended geometry, the tool temporarily moves `surfaceToPaintOn` to the `Ignore Raycast` layer, fires rays against that layer mask only, then restores the original layer via `SetTemporaryLayer()` / `RestoreOriginalLayer()`.

Two rays are used during painting:
1. Primary ray from mouse position — finds the brush center hit point.
2. Secondary downward ray from `hit.point + Vector3.up * 1f` — finds the exact surface depth for each randomly offset spawn position.

---

## Painting Pipeline

```
MouseDrag tick
└── Raycast to surfaceToPaintOn
    └── Surface angle check (minPaintAngle / maxPaintAngle)
        └── for i in density:
            ├── Random offset within brushSize circle
            ├── Depth raycast at offset position
            ├── Compute objectScale
            │   ├── brushScale = lerp(centerScale, edgeScale, pow(dist, exponent)) +/- randomScaleOffset
            │   └── textureScale = CalculateScaleFromScreenSpace() [if texture assigned]
            ├── Spawn object
            │   ├── NatureInstance: new GameObject -> AddComponent<NatureInstance> -> set Prefab -> Refresh()
            │   └── Standard: PrefabUtility.InstantiatePrefab -> auto-add MeshCollider to LOD0
            └── Apply rotation + scale, register Undo
```

### LOD0 Auto-Collider

In standard mode, after instantiation the tool searches for a child named `<prefabName>_LOD0`. If found and it lacks a `MeshCollider`, one is added automatically using `MeshFilter.sharedMesh`.

---

## Erasing Pipeline

```
MouseDrag tick
└── Raycast to surfaceToPaintOn
    └── Collect children of parentTransform within brushSize
        └── Filter by eraserProbability (Random.value)
            └── Undo.DestroyObjectImmediate per object
```

Two-pass approach (collect then destroy) prevents hierarchy mutation during iteration.

---

## Scaling Pipeline

```
MouseDrag tick
└── Raycast to surfaceToPaintOn
    └── Collect children of parentTransform within brushSize
        └── Undo.RecordObject -> localScale *= scaleMultiplier
            └── Clamp: magnitude <= 10, minimum 0.1
```

Same two-pass pattern as erasing.

---

## Scale Calculation

`CalculateScaleFromScreenSpace(worldPosition, prefabSettings)` projects a world point to screen UV, samples `textureForScaling` with `GetPixelBilinear`, and lerps between `minTextureScale` and `maxTextureScale` using the pixel's grayscale value. Falls back to `minTextureScale` if no camera or texture is available.

---

## Surface Auto-Detection

When the user assigns a new object to "Surface to Paint On", the tool checks for a child named `<objectName>_LOD0`. If found, that child becomes the actual paint surface and a `MeshCollider` is added if missing. This ensures raycasts hit the highest-detail mesh.

---

## Undo Support

| Operation | Undo label |
|---|---|
| NatureInstance spawn | `"Create Nature Instance"` |
| Prefab spawn | `"Instantiate Object"` |
| MeshCollider add | `"Add Mesh Collider"` |
| Object erase | `Undo.DestroyObjectImmediate` |
| Object scale | `"Scale Object"` |
| Clear all | `Undo.DestroyObjectImmediate` per child |

---

## Constants

| Constant | Value | Purpose |
|---|---|---|
| `MAX_RAYCAST_DISTANCE` | `20f` | Max depth for secondary downward raycasts |
| `MIN_MOUSE_MOVEMENT` | `1f` | Minimum pixel movement to trigger a paint tick |

---

## GUI Notes

`OnGUI` wraps everything in `try/finally` around `GUILayout.BeginScrollView` / `EndScrollView` to guarantee the scroll view closes even on exceptions.

Prefab removal is deferred via an `indexToRemove` flag and followed by `GUIUtility.ExitGUI()` to prevent layout errors from mid-loop list mutation.
