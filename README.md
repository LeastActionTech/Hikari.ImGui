# Hikari.ImGui

A pure, standalone C# re-implementation of **[Dear ImGui](https://github.com/ocornut/imgui)** targeting version **v1.93.0-docking** with **zero native dependencies**.

[![Build and Test](https://github.com/akshaym/Hikari.ImGui/actions/workflows/ci.yml/badge.svg)](https://github.com/akshaym/Hikari.ImGui/actions)
[![NuGet](https://img.shields.io/badge/nuget-v1.93.0-blue.svg)](https://www.nuget.org/packages/Hikari.ImGui)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## Architecture & Design

- **100% Managed .NET 10**: Zero native binaries, C/C++ runtimes, P/Invokes, or platform-specific shims. Runs natively across Linux, macOS, Windows, FreeBSD, ARM64, and x64.
- **Canonical Parity**: Built against canonical Dear ImGui `v1.93.0-docking` (commit `b916186e66aa3c02b72c6ca17455516512ed8049`).
- **Complete Subsystem Coverage**: 100% feature coverage across all 18 core Dear ImGui subsystems:
  - **Docking**: Dock nodes, dockspaces, tab bars, window splitting, and persistence.
  - **Modern Vector Tessellation**: Full v1.93 stroke positioning modes (`StrokeCenter`, `StrokeInside`, `StrokeOutside`, `StrokeLegacy`), line caps (`CapSquare`, `JoinMiter`), anti-aliased line ends (`AALineEnds`), draw flag scoping stack (`PushDrawFlag`/`PopDrawFlag`), and integer snap fast-paths (`AddLineH`/`AddLineV`).
  - **Multi-Select & Mixed Values**: Tri-state checkboxes, radio buttons, sliders, and color edits supporting mixed-value multi-selection states (`ImGuiItemFlags.MixedValue`).
  - **Tables & Layouts**: Multi-column sorting, freezing, clipping, and auto-sizing.
  - **Popups, Modals & Menus**: Full popup and focus stacks (`OpenPopupStack`, `BeginPopupStack`).
  - **Trees, Tooltips & Drag-and-Drop**: Complete interactive widget suite.
- **Backend-Agnostic**: Generates standard `ImDrawData` mesh lists (vertices and 16/32-bit indices) ready for any graphics API (OpenGL, Vulkan, DirectX, Metal, WebGPU).

---

## Repository Structure

```
Hikari.ImGui/
├── Hikari.ImGui.sln            # Visual Studio / dotnet solution file
├── Hikari.ImGui/               # Core pure C# Dear ImGui library (NuGet package)
├── Hikari.ImGui.Demo/          # Interactive Silk.NET OpenGL demo host
├── Hikari.ImGui.Test/          # Regression & parity unit test suite (465 tests)
├── Tools/                      # Canonical reference sync and parity audit tools
│   ├── sync_imgui_reference.py # Fetches canonical Dear ImGui C++ headers
│   ├── verify_imgui_parity.py  # Audits C# symbols against C++ canonical headers
│   └── test_verify_imgui_parity.py
├── PARITY_COMPLIANCE.md        # Comprehensive canonical parity verification record
└── LICENSE.md                  # MIT License
```

---

## Quick Start

### 1. Add NuGet Package

```bash
dotnet add package Hikari.ImGui --version 1.93.0
```

### 2. Immediate-Mode UI Loop

```csharp
using Hikari.ImGui;

// Initialize context
ImGuiContext ctx = ImGui.CreateContext();
ImGui.SetCurrentContext(ctx);
ImGui.StyleColorsDark();

// Setup display metrics each frame
ImGuiIO io = ImGui.GetIO();
io.DisplaySize = new ImVec2(1920, 1080);
io.DeltaTime = 1.0f / 60.0f;

// Frame logic
ImGui.NewFrame();
ImGui.Begin("Hello, Pure C# ImGui!");
ImGui.Text("Welcome to Hikari.ImGui v1.93.0!");
if (ImGui.Button("Click Me"))
{
    Console.WriteLine("Button clicked!");
}
ImGui.End();

// Render and get mesh data
ImGui.Render();
ImDrawData drawData = ImGui.GetDrawData();

// Dispatch drawData to your renderer (OpenGL, Vulkan, Silk.NET, etc.)
```

---

## Running the Interactive Demo

The included demo host renders the canonical Dear ImGui demo window using Silk.NET and OpenGL:

```bash
dotnet run --project Hikari.ImGui.Demo -c Release
```

---

## Running the Test Suite

```bash
dotnet test Hikari.ImGui.Test -c Release
```

All 465 unit tests validate parity across widgets, docking, tables, vector drawing, styling, and interactivity.

---

## Canonical Parity Verification Tooling

To fetch the canonical Dear ImGui reference checkout and run automated parity verification:

```bash
# 1. Fetch canonical Dear ImGui headers
python3 Tools/sync_imgui_reference.py --commit v1.93.0-docking --destination ThirdParty/imgui

# 2. Run symbol and structure parity audit
python3 Tools/verify_imgui_parity.py --json Tools/imgui_parity_v1.93.0.json
```

---

## Authors & Attribution

- **Dear ImGui Design & Architecture**: Copyright (c) 2014-2026 [Omar Cornut](https://github.com/ocornut) and Dear ImGui contributors.
- **Pure C# Port & Modern .NET Implementation**: Copyright (c) 2026 [Least Action Technologies Inc.](https://github.com/akshaym)

---

## License

This software is released under the **MIT License**. See [LICENSE.md](LICENSE.md) for details.
