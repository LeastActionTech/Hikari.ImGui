// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

using System;
using System.Diagnostics;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Hikari.ImGui.Demo;

/// <summary>
/// Hikari.ImGui hardware demo host application.
/// Instantiates Silk.NET window with OpenGL or Vulkan backend, binds interactive mouse and keyboard
/// input context events, runs the canonical ImGui demo window loop, and renders via the active backend.
/// </summary>
public static class Program
{
    #region Demo state

    private static IWindow? _window;
    private static IInputContext? _inputContext;
    private static GL? _gl;
    private static OpenGlRenderer? _glRenderer;
    private static VulkanWindowContext? _vkContext;
    private static VulkanRenderer? _vkRenderer;
    private static ImFontAtlas? _fontAtlas;

    private static string _backend = "opengl";
    private static int _maxFrames = 0; // 0 = infinite / interactive
    private static int _frameCount = 0;
    private static double _lastTime = 0.0;
    private static bool _showDemoWindow = true;

    #endregion

    #region Entry point

    /// <summary>
    /// Parses the demo command line and runs the requested renderer backend.
    /// </summary>
    /// <param name="args">Command-line arguments; supports <c>--backend opengl|vulkan</c>, <c>--frames N</c> and <c>--headless</c>.</param>
    /// <returns>Zero on success, or a non-zero process exit code when the backend cannot start.</returns>
    public static int Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("Hikari.ImGui Silk.NET Demo Host");
        Console.WriteLine("==================================================");

        // Parse CLI args
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--backend", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _backend = args[++i].ToLowerInvariant();
            }
            else if (args[i].Equals("--frames", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int frames))
                {
                    _maxFrames = frames;
                }
            }
            else if (args[i].Equals("--headless", StringComparison.OrdinalIgnoreCase))
            {
                _maxFrames = 10;
            }
        }

        Console.WriteLine($"Selected backend: {_backend}");
        if (_maxFrames > 0)
        {
            Console.WriteLine($"Running smoke test for {_maxFrames} frames...");
        }

        // Initialize ImFontAtlas and ImGuiContext.
        _fontAtlas = new ImFontAtlas();
        _fontAtlas.AddFontDefault();
        _fontAtlas.Build();

        ImGui.CreateContext(_fontAtlas);

        // Configure default IO capabilities
        ImGuiIO io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

        if (_backend == "vulkan")
        {
            return RunVulkanDemo();
        }
        else
        {
            return RunOpenGlDemo();
        }
    }

    #endregion

    #region Input handling

    private static void SetupInput(IWindow window)
    {
        _inputContext = window.CreateInput();
        for (int i = 0; i < _inputContext.Keyboards.Count; i++)
        {
            var keyboard = _inputContext.Keyboards[i];
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        for (int i = 0; i < _inputContext.Mice.Count; i++)
        {
            var mouse = _inputContext.Mice[i];
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnMouseScroll;
        }

        // Configure platform capabilities and clipboard hooks.
        ImGuiIO io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        ImGuiPlatformIO platformIo = ImGui.GetPlatformIO();
        platformIo.Platform_SetClipboardTextFn = (ctx, text) =>
        {
            if (_inputContext != null && _inputContext.Keyboards.Count > 0)
            {
                try
                {
                    _inputContext.Keyboards[0].ClipboardText = text;
                }
                catch
                {
                    // Fall back to context in-memory buffer if platform clipboard write fails.
                }
            }
        };
        platformIo.Platform_GetClipboardTextFn = (ctx) =>
        {
            if (_inputContext != null && _inputContext.Keyboards.Count > 0)
            {
                try
                {
                    return _inputContext.Keyboards[0].ClipboardText ?? string.Empty;
                }
                catch
                {
                    // Fall back to context in-memory buffer if platform clipboard read fails.
                }
            }
            return string.Empty;
        };
    }

    private static void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        ImGuiKey imguiKey = TranslateKey(key);
        if (imguiKey != ImGuiKey.None)
            ImGui.GetIO().AddKeyEvent(imguiKey, true);
    }

    private static void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
    {
        ImGuiKey imguiKey = TranslateKey(key);
        if (imguiKey != ImGuiKey.None)
            ImGui.GetIO().AddKeyEvent(imguiKey, false);
    }

    /// <summary>
    /// Forwards a host character event. Silk.NET delivers UTF-16 code units, so surrogate halves are routed
    /// through <c>AddInputCharacterUTF16</c>, which joins them into one code point
    /// </summary>
    private static void OnKeyChar(IKeyboard keyboard, char character)
    {
        ImGui.GetIO().AddInputCharacterUTF16((ushort)character);
    }

    private static void OnMouseMove(IMouse mouse, Vector2 position)
    {
        ImGui.GetIO().AddMousePosEvent(position.X, position.Y);
    }

    private static void OnMouseDown(IMouse mouse, MouseButton button)
    {
        int index = TranslateMouseButton(button);
        if (index >= 0)
            ImGui.GetIO().AddMouseButtonEvent(index, true);
    }

    private static void OnMouseUp(IMouse mouse, MouseButton button)
    {
        int index = TranslateMouseButton(button);
        if (index >= 0)
            ImGui.GetIO().AddMouseButtonEvent(index, false);
    }

    private static void OnMouseScroll(IMouse mouse, ScrollWheel wheel)
    {
        ImGui.GetIO().AddMouseWheelEvent(wheel.X, wheel.Y);
    }

    internal static int TranslateMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            MouseButton.Button4 => 3,
            MouseButton.Button5 => 4,
            _ => -1
        };
    }

    internal static ImGuiKey TranslateKey(Key key)
    {
        return key switch
        {
            Key.Right => ImGuiKey.RightArrow,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.AltRight => ImGuiKey.RightAlt,
            Key.SuperLeft => ImGuiKey.LeftSuper,
            Key.SuperRight => ImGuiKey.RightSuper,
            Key.Tab => ImGuiKey.Tab,
            Key.Enter or Key.KeypadEnter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Space => ImGuiKey.Space,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Delete => ImGuiKey.Delete,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.BackSlash => ImGuiKey.Backslash,
            Key.Number0 or Key.D0 => ImGuiKey._0,
            Key.Number1 => ImGuiKey._1,
            Key.Number2 => ImGuiKey._2,
            Key.Number3 => ImGuiKey._3,
            Key.Number4 => ImGuiKey._4,
            Key.Number5 => ImGuiKey._5,
            Key.Number6 => ImGuiKey._6,
            Key.Number7 => ImGuiKey._7,
            Key.Number8 => ImGuiKey._8,
            Key.Number9 => ImGuiKey._9,
            _ => Enum.TryParse(key.ToString(), out ImGuiKey parsed) ? parsed : ImGuiKey.None
        };
    }

    #endregion

    #region Frame preparation

    // Algorithmic rationale: Display coordinate mapping and aspect transform.
    // We derive DisplayFramebufferScale by comparing physical framebuffer dimensions against virtual window
    // coordinate dimensions. This threshold and transform ratio is required because high-DPI displays decouple
    // window logical units from backing physical render coordinates.
    private static void BeginImGuiFrame(double delta)
    {
        if (_window == null) return;

        ImGuiIO io = ImGui.GetIO();
        int sizeX = _window.Size.X;
        int sizeY = _window.Size.Y;
        int fbX = _window.FramebufferSize.X;
        int fbY = _window.FramebufferSize.Y;

        io.DisplaySize = new ImVec2(sizeX, sizeY);
        io.DisplayFramebufferScale = sizeX > 0 && sizeY > 0
            ? new ImVec2((float)fbX / sizeX, (float)fbY / sizeY)
            : new ImVec2(1f, 1f);

        double currentTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        float dt = _lastTime > 0.0 ? (float)(currentTime - _lastTime) : (float)delta;
        io.DeltaTime = dt > 0f ? dt : 1f / 60f;
        _lastTime = currentTime;

        ImGui.NewFrame();

        // Render Dear ImGui demo window
        if (_showDemoWindow)
        {
            ImGuiDemo.ShowDemoWindow(ref _showDemoWindow);
        }

        ImGui.Render();
        UpdateMouseCursor();
    }

    private static ImGuiMouseCursor _lastMouseCursor = (ImGuiMouseCursor)(-2);

    /// <summary>
    /// Synchronizes the active Dear ImGui mouse cursor request to the host OS cursor via Silk.NET.
    /// Canvases canonical <c>ImGui_ImplGlfw_UpdateMouseCursor</c>.
    /// </summary>
    private static void UpdateMouseCursor()
    {
        if (_inputContext == null || _inputContext.Mice.Count == 0)
            return;

        ImGuiIO io = ImGui.GetIO();
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
            return;

        ImGuiMouseCursor cursor = ImGui.GetMouseCursor();
        if (_lastMouseCursor == cursor)
            return;

        _lastMouseCursor = cursor;

        IMouse mouse = _inputContext.Mice[0];
        if (cursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
        {
            mouse.Cursor.CursorMode = CursorMode.Hidden;
        }
        else
        {
            mouse.Cursor.CursorMode = CursorMode.Normal;
            mouse.Cursor.StandardCursor = cursor switch
            {
                ImGuiMouseCursor.Arrow => StandardCursor.Default,
                ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
                ImGuiMouseCursor.ResizeNS => StandardCursor.VResize,
                ImGuiMouseCursor.ResizeEW => StandardCursor.HResize,
                ImGuiMouseCursor.ResizeNESW => StandardCursor.NeswResize,
                ImGuiMouseCursor.ResizeNWSE => StandardCursor.NwseResize,
                ImGuiMouseCursor.Hand => StandardCursor.Hand,
                _ => StandardCursor.Default
            };
        }
    }

    #endregion

    #region OpenGL host

    private static int RunOpenGlDemo()
    {
        WindowOptions options = WindowOptions.Default;
        options.Title = "Hikari.ImGui Demo - OpenGL 3.3";
        options.Size = new Vector2D<int>(1280, 720);
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));
        options.VSync = false;

        _window = Window.Create(options);

        _window.Load += OnOpenGlLoad;
        _window.Render += OnOpenGlRender;
        _window.Closing += OnOpenGlClosing;

        try
        {
            _window.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OpenGL Demo Error]: {ex}");
            return 1;
        }
        finally
        {
            _window.Dispose();
        }
    }

    private static void OnOpenGlLoad()
    {
        if (_window == null) return;

        SetupInput(_window);

        _gl = GL.GetApi(_window);
        _glRenderer = new OpenGlRenderer(_gl);

        if (_fontAtlas != null)
        {
            _glRenderer.CreateFontTexture(_fontAtlas);
        }

        Console.WriteLine("[OpenGL] Initialized successfully.");
    }

    private static void OnOpenGlRender(double delta)
    {
        if (_window == null || _gl == null || _glRenderer == null || _fontAtlas == null)
            return;

        if (_window.Size.X <= 0 || _window.Size.Y <= 0 || _window.FramebufferSize.X <= 0 || _window.FramebufferSize.Y <= 0)
            return;

        // Clear background
        _gl.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        BeginImGuiFrame(delta);

        ImDrawData drawData = ImGui.GetDrawData();
        _glRenderer.Render(drawData);

        _frameCount++;
        if (_maxFrames > 0 && _frameCount >= _maxFrames)
        {
            Console.WriteLine($"[OpenGL] Completed {_frameCount} frames. Exiting.");
            _window.Close();
        }
    }

    private static void OnOpenGlClosing()
    {
        _inputContext?.Dispose();
        _glRenderer?.Dispose();
        _gl?.Dispose();
        ImGui.DestroyContext();
    }

    #endregion

    #region Vulkan host

    private static int RunVulkanDemo()
    {
        Console.WriteLine("[Vulkan] Checking Vulkan availability...");
        if (!VulkanRenderer.IsVulkanAvailable())
        {
            Console.WriteLine("[Vulkan] Vulkan API is not available on this host. Falling back to OpenGL.");
            _backend = "opengl";
            return RunOpenGlDemo();
        }

        Console.WriteLine("[Vulkan] Creating physical Silk.NET window with Vulkan swapchain presentation...");

        WindowOptions options = WindowOptions.Default;
        options.Title = "Hikari.ImGui Demo - Vulkan";
        options.Size = new Vector2D<int>(1280, 720);
        options.API = GraphicsAPI.None; // Tell Silk.NET to leave the context for Vulkan
        options.VSync = false;

        _window = Window.Create(options);

        _window.Load += OnVulkanLoad;
        _window.Render += OnVulkanRender;
        _window.Closing += OnVulkanClosing;

        try
        {
            _window.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Vulkan Window Error]: {ex}");
            return 1;
        }
        finally
        {
            _window.Dispose();
        }
    }

    private static void OnVulkanLoad()
    {
        if (_window == null) return;

        SetupInput(_window);

        Console.WriteLine("[Vulkan] Initializing Vulkan swapchain context...");
        _vkContext = new VulkanWindowContext(_window);

        Console.WriteLine("[Vulkan] Initializing Vulkan ImGui renderer pipeline...");
        _vkRenderer = new VulkanRenderer(
            _vkContext.Vk,
            _vkContext.Device,
            _vkContext.PhysicalDevice,
            _vkContext.GraphicsQueue,
            _vkContext.CommandPool,
            _vkContext.RenderPass
        );

        if (_fontAtlas != null)
        {
            _vkRenderer.CreateFontTexture(_fontAtlas);
        }

        Console.WriteLine($"[Vulkan] Initialized successfully. Swapchain extent: {_vkContext.SwapchainExtent.Width}x{_vkContext.SwapchainExtent.Height}.");
    }

    private static void OnVulkanRender(double delta)
    {
        if (_window == null || _vkContext == null || _vkRenderer == null || _fontAtlas == null)
            return;

        if (_window.Size.X <= 0 || _window.Size.Y <= 0 || _window.FramebufferSize.X <= 0 || _window.FramebufferSize.Y <= 0)
            return;

        if (!_vkContext.BeginFrame(out var cmdBuffer, out var imageIndex))
            return;

        BeginImGuiFrame(delta);

        ImDrawData drawData = ImGui.GetDrawData();

        // Record draw commands into active render pass
        _vkRenderer.Render(cmdBuffer, drawData, (int)_vkContext.CurrentFrame);

        // End render pass, submit to queue, and present to swapchain
        _vkContext.EndFrameAndPresent();

        _frameCount++;
        if (_maxFrames > 0 && _frameCount >= _maxFrames)
        {
            Console.WriteLine($"[Vulkan] Completed {_frameCount} frames. Exiting.");
            _window.Close();
        }
    }

    private static void OnVulkanClosing()
    {
        _inputContext?.Dispose();
        _vkRenderer?.Dispose();
        _vkContext?.Dispose();
        ImGui.DestroyContext();
    }

    #endregion
}
