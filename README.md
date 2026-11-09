# Hikari.ImGui

A pure, standalone C# re-implementation of the much-beloved and widely used immediate-mode GUI for 3D applications, **[Dear ImGui](https://github.com/ocornut/imgui)**, targeting version **v1.93.0-docking** with zero native dependencies.

---

## Repository Structure

```
Hikari.ImGui/
├── Hikari.ImGui.sln            # Visual Studio / dotnet solution file
├── Hikari.ImGui/               # Core pure C# Dear ImGui library (NuGet package)
├── Hikari.ImGui.Demo/          # Interactive Silk.NET OpenGL / Vulkan demo host
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

### Immediate-Mode UI Loop

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

The included demo host renders the canonical Dear ImGui demo window using Silk.NET and OpenGL and Vulkan:

```bash
dotnet run --project Hikari.ImGui.Demo -c Release
```

```bash
dotnet run --project Hikari.ImGui.Demo -c Release --backend vulkan
```

---

## Running the Test Suite

```bash
dotnet test Hikari.ImGui.Test -c Release
```
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
- **C# Port & .NET Implementation, Hikari.ImGui.Test project and parts of Hikari.ImGui.Demo project**: Copyright (c) 2026 [Least Action Technologies Inc.](https://github.com/LeastActionTech)

---

## License

This software is released under the **MIT License**. See [LICENSE.md](LICENSE.md) for details.
