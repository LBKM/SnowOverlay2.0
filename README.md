# ❄ SnowOverlay

A lightweight Windows desktop overlay that makes snow fall over your screen and pile up into soft drifts on top of your open windows and the taskbar. Inspired by the "Snow for you" TikTok app.

<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/e160df50-f635-4d1f-92ac-d12a8af4eb4f" />


Built with C# / WPF on .NET 8. No third-party libraries, no bitmaps or sprites: every snowflake and drift is a vector shape.

## Features

- **Click-through overlay**: transparent, borderless, always on top. Mouse clicks pass straight through it. Hidden from the taskbar and Alt+Tab.
- **Vector snow**: white circles, 2–6 px, with varying opacity for depth. Gentle sine-wave sway plus a slowly changing wind.
- **Drifts on windows**: the top edge of every visible window (and the taskbar) collects snow into smooth hills with rounded ends, a maximum height, and slow melting of old layers.
- **Dynamic behavior**
  - Move, minimize or close a window and its snow slides off and falls down.
  - Resize a window and the snow stays put (the left edge is fixed, the right side is just cropped or extended).
  - Windows in front hide the drifts of windows behind them, including maximized windows.
- **Fullscreen aware**: the overlay pauses completely (0% CPU, no drawing) while a fullscreen app, game or presentation is active.
- **Tray widget**: Windows 11 style flyout with an animated **ON / OFF** switch (snow stops, drifts melt away smoothly), a graphics selector, and an **Exit** button. Right-click the tray icon for a quick Exit menu.
- **Graphics modes**

  | Mode | Frame rate | Window polling | Extras |
  |------|-----------|----------------|--------|
  | Normal | 30 FPS | every 50 ms | Economical |
  | Ultra | 60 FPS | every 16 ms | 1.5x snowflakes, soft glow, landing splashes, ice glints on drifts, wind gusts, large foreground bokeh flakes |

- **Idle is free**: when snow is off and everything has melted, the app stops polling windows and drawing.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (the published single-file `.exe` is self-contained)

## Build and run

```bash
# run from source
dotnet run -c Release

# publish a single self-contained .exe
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable is written to `bin/Release/net8.0-windows/win-x64/publish/`.

You can also open `SnowOverlay.csproj` in Visual Studio 2022 and press F5.

> Close any running instance (tray icon, then Exit) before rebuilding, otherwise the `.exe` is locked.

## Usage

1. Start the app. A snowflake icon appears in the system tray and snow starts falling.
2. **Left-click** the tray icon to open the widget.
3. Use the switch to turn snow on or off, and pick **Normal** or **Ultra** graphics.
4. Press **Exit** (or right-click the tray icon, then Exit) to quit.

## Project structure

| File | Purpose |
|------|---------|
| `App.xaml` / `App.xaml.cs` | Startup, tray icon, widget wiring |
| `MainWindow.xaml` / `.cs` | Transparent click-through overlay window, polling timer, fullscreen detection |
| `SnowEngine.cs` | Snowflake physics, drift simulation, vector rendering with a single `DrawingVisual` |
| `WindowTracker.cs` | WinAPI interop (`EnumWindows`, `DwmGetWindowAttribute`, taskbar and fullscreen detection) |
| `TrayWidget.xaml` / `.cs` | Tray flyout with toggle switch, quality selector and Exit |

## How it works

- The overlay is a full-screen WPF window with `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- A timer snapshots visible top-level windows via `EnumWindows`, skipping minimized, cloaked, tool and click-through windows. Exact bounds come from `DWMWA_EXTENDED_FRAME_BOUNDS`.
- Each window top edge becomes a *surface*: an array of 4 px columns holding drift heights. Landing flakes add height with a small kernel, a slope-relaxation pass smooths the profile, and a per-column cap rounds the ends.
- Maximized windows (and windows touching the top of the screen) are *occluders*: they carry no snow but hide drifts behind them.
- The taskbar top (`Shell_TrayWnd`) acts as the ground.

## Configuration

Tweak the constants at the top of `SnowEngine.cs`:

| Setting | Default | Meaning |
|---------|---------|---------|
| `Target` | 240 | Snowflakes on screen (x1.5 in Ultra) |
| `WindowDriftMax` | 18 | Max drift height on windows, px |
| `GroundDriftMax` | 34 | Max drift height on the taskbar, px |
| `MeltOn` | 0.04 | Melt speed while snowing, px/s |
| `MeltOff` | 9.0 | Melt speed after switching off, px/s |

## Known limitations

- Only the primary monitor is covered.
- The overlay uses a WPF transparent window (`AllowsTransparency`), which is not fully GPU-direct. On low-end machines, use Normal mode or lower `Target`.
- Settings (on/off, graphics mode) are not saved between launches yet.

## Ideas / roadmap

- Multi-monitor and per-monitor DPI support
- Persist settings
- Frost on window corners, garland lights along window edges
- Blizzard mode, small snowman on the biggest drift

## License

Add the license of your choice (for example MIT) as a `LICENSE` file.
