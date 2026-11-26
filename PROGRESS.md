# Implementation and verification

REAL Capture X is a native .NET 10 and WinUI 3 desktop application. These notes distinguish the Windows configurations exercised during development from the work that still needs hardware or compatibility testing.

## Capture and annotation

| Area | Observed behavior | Open work |
| --- | --- | --- |
| Region, window and display | Captured a 528 × 276 region on a monitor to the left of the primary display, a 666 × 463 window and a 1920 × 1080 display. Escape restored the preview. | Selections spanning multiple monitors, HDR and mixed DPI. |
| Capture toolbar | Six direct modes, repeat-region capture, keyboard navigation and square selection with Shift. The region overlay shows a magnifier, dimensions and the color under the pointer. | Numeric region dimensions and broader multi-monitor coverage. |
| Quick Access | Preview, restore, auto-hide, copy and export were exercised. The exported PNG matched the original SHA-256. Hover reveals the controls, and the card supports file dragging. | Drop targets in other applications and card placement on mixed-DPI monitors. |
| Annotate | Drew and selected marks, moved a rectangle, checked resize handles, undo, redo and zoom. Copy produced a 1400 × 850 image. A PNG export changed only the pixels around the test arrow. The editor also provides inline text, numbering, highlight, blur, pixelation, redaction and crop. | Editable project files. |
| Background | Solid color, padding, corner radius and aspect ratio are available. Enabling the background changed the exported dimensions as expected. | Custom background images, saved presets, background shadows and automatic balancing. |
| Scrolling capture | The floating indicator and vertical stitching produced a 695 × 1079 PNG. Horizontal stitching uses the same algorithm on transposed frames. | Live coverage of horizontal scrolling, automatic scrolling and pages with sticky elements. |
| OCR | Bundled Tesseract data covers English and Czech. Copying recognized text worked on a synthetic sample. | Broader language coverage and QR recognition. |
| Pinned images | Opened a separate floating reference from Quick Access. Move and resize controls are implemented. | Click-through and opacity controls. |

## Recording and desktop integration

| Area | Observed behavior | Open work |
| --- | --- | --- |
| Recording | Captured a 1920 × 1080 H.264 video at 30 fps (193 frames) and a 698 × 456 region. The compact controls showed elapsed time. Setup exposes microphone, system audio, webcam and smart zoom options. | Physical input/output devices and extended multi-source sessions. |
| Studio | A recording opened automatically. A 6.433-second H.264 clip exported to 3.000 seconds at 1920 × 1080 and 30 fps. A separate trim in the redesigned UI measured 1.380 seconds with ffprobe. Volume is adjustable in Studio. | Post-capture editing of zoom, cursor and camera effects, and multitrack editing. |
| History | Screenshots and recordings have separate lists, previews and actions to open Annotate or Studio. | Search and retention controls. |
| Tray and shortcuts | Registered and saved a replacement global shortcut. The tray and toolbar menus share keyboard navigation and shortcut hints. | Broader testing of hotkey conflicts with other utilities. |
| Appearance | System, Light and Dark provide live previews. A saved theme survived a restart. Reduced motion and translucency preferences are implemented. | Visual inspection of capture-excluded surfaces on physical displays at additional scales. |

## Release checks

- **0.2.1 regressions.** With the floating toolbar alive, repeated Dark, Light and System selections in one Windows process. The settings surface visibly changed each time the effective theme changed. At 100% scaling the recording setup window measured 424 × 292 physical pixels including its shadow margins. The entire 38-pixel Start recording button was visible, with 7 pixels below it inside the panel.
- **Debug self-check.** The Debug executable accepts `--self-check` and verifies pixel copying, duplicate-frame rejection, stitching, transposition and virtual-key mapping.
- **Portable packaging.** The 0.2.0 self-contained x64 build was launched with the WinUI resource index included. The build workflow checks the application, native libraries, runtime, OCR data and third-party notices before packaging.
- **Visual QA.** Three passes in the running application covered menu density, contrast in both themes, window layering, player height and transitions. Product screenshots and animations use an isolated profile and synthetic content.
- **Dependency history.** The 0.1.0 audit reported no known vulnerable packages at the time. Version 0.2.0 retained those dependencies.

## Design references

The interface uses native Windows controls and materials. The original design study used public CleanShot X product material. Access to its X profile returned HTTP 403 during that study.

[Product demo](https://cleanshot.com/) · [Screenshots](https://cleanshot.com/screenshots) · [Features](https://cleanshot.com/features) · [Changelog](https://cleanshot.com/changelog) · [Quick Access demonstration](https://cleanshot.com/video/home/quickaccess.mp4)
