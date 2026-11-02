# Hikari.ImGui

A 100% pure C# re-implementation of **[Dear ImGui](https://github.com/ocornut/imgui)** targeting version **v1.93.0-docking** with **zero native dependencies**.

[![NuGet](https://img.shields.io/badge/nuget-v1.93.0-blue.svg)](https://www.nuget.org/packages/Hikari.ImGui)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## Highlights

- **Pure C# .NET 10**: Zero native binaries, C/C++ runtimes, P/Invokes, or platform-specific shims. Runs natively wherever .NET 10 runs (Linux, macOS, Windows, FreeBSD, ARM64, x64).
- **v1.93.0-docking Parity**: Built and validated directly against canonical Dear ImGui `v1.93.0-docking` (commit `b916186e66aa3c02b72c6ca17455516512ed8049`).
- **Complete Subsystem Coverage**: 100% feature coverage across all 18 core Dear ImGui subsystems:
  - **Docking**: Dock nodes, dockspaces, tab bars, window splitting, and persistence.
  - **Modern Vector Tessellation**: Full v1.93 stroke positioning modes (`StrokeCenter`, `StrokeInside`, `StrokeOutside`, `StrokeLegacy`), line caps (`CapSquare`, `JoinMiter`), anti-aliased line ends (`AALineEnds`), draw flag scoping stack (`PushDrawFlag`/`PopDrawFlag`), and integer snap fast-paths (`AddLineH`/`AddLineV`).
  - **Multi-Select & Mixed Values**: Tri-state checkboxes, radio buttons, sliders, and color edits supporting mixed-value multi-selection states (`ImGuiItemFlags.MixedValue`).
  - **Tables & Layouts**: Multi-column sorting, freezing, clipping, and auto-sizing.
  - **Popups, Modals & Menus**: Full popup and focus stacks (`OpenPopupStack`, `BeginPopupStack`).
  - **Trees, Tooltips & Drag-and-Drop**: Complete interactive widget suite.
- **Backend-Agnostic**: Generates standard `ImDrawData` mesh lists (vertices and 16/32-bit indices) ready for any graphics API (OpenGL, Vulkan, DirectX, Metal, WebGPU).

---

## Installation

Install via NuGet:

```bash
dotnet add package Hikari.ImGui --version 1.93.0
```

Or via Package Manager in `.csproj`:

```xml
<PackageReference Include="Hikari.ImGui" Version="1.93.0" />
```

---

## Quick Start

```csharp
using Hikari.ImGui;

// 1. Initialize context during application startup
ImGuiContext ctx = ImGui.CreateContext();
ImGui.SetCurrentContext(ctx);

// Optional: Apply style palette
ImGui.StyleColorsDark();

// 2. Configure display size and delta time per frame
ImGuiIO io = ImGui.GetIO();
io.DisplaySize = new ImVec2(1920, 1080);
io.DeltaTime = 1.0f / 60.0f;

// 3. Begin frame
ImGui.NewFrame();

// 4. Submit immediate-mode UI
ImGui.Begin("Hello, Pure C# ImGui!");
ImGui.Text("Welcome to Hikari.ImGui targeting v1.93.0-docking.");

if (ImGui.Button("Click Me"))
{
    Console.WriteLine("Button clicked!");
}

ImGui.End();

// 5. Finalize frame and retrieve draw geometry
ImGui.Render();
ImDrawData drawData = ImGui.GetDrawData();

// 6. Pass drawData to your graphics backend (e.g. OpenGL, Vulkan, Silk.NET)
// myRenderer.Render(drawData);
```

---

## Parity Verification & Test Suite

The pure C# implementation includes an automated parity verification suite checking all symbols, structs, and methods against canonical Dear ImGui headers:

- **Misplaced Symbols**: 0 (100% compliant)
- **Enums**: 87 / 87 (100% parity)
- **Enum Members**: 1,097 / 1,097 (100% parity)
- **Unit Test Suite**: 465 / 465 passing unit tests covering interactive widgets, vector tessellation, table layouts, clipping, docking, and nullability safety.

---

## Authors & Attribution

- **Dear ImGui Design & Architecture**: Copyright (c) 2014-2026 [Omar Cornut](https://github.com/ocornut) and Dear ImGui contributors.
- **Pure C# Port & Modern .NET Implementation**: Copyright (c) 2026 [Least Action Technologies Inc.](https://github.com/akshaym)

---

## License

This project is licensed under the **MIT License**. See [LICENSE.md](LICENSE.md) for details.
