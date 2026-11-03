// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Hikari.ImGui;

/// <summary>
/// Provides immediate-mode GUI operations, context and frame lifecycle management, windowing,
/// layout, input routing, styling, widgets, tables, and docking facilities.
/// </summary>
public static partial class ImGui
{

    #region SECTION: CONTEXT, FRAME LIFECYCLE, STYLING, ERRORS, LOCALIZATION

    #region [SECTION] INITIALIZATION, SHUTDOWN

    /// <summary>
    /// Message reported when a member that requires a current context is invoked without one. The
    /// canonical library dereferences its global <c>GImGui</c> pointer in this situation and therefore
    /// faults; this port reports the same programmer error through <see cref="ImAssert.Check(bool, string)"/>
    /// and then throws <see cref="InvalidOperationException"/> so the failure is deterministic and
    /// diagnosable.
    /// </summary>
    private const string NoContextMessage =
        "No current context. Did you call ImGui.CreateContext() and ImGui.SetCurrentContext()?";

    /// <summary>
    /// Version string compiled into this port, mirroring canonical <c>IMGUI_VERSION</c>
    /// </summary>
    private const string ImGuiVersion = "1.93.0";

    /// <summary>
    /// Numeric version value mirrored from canonical <c>IMGUI_VERSION_NUM</c>
    ///., used by the localized version string and by callers that
    /// report the engine version, such as the demo's About window.
    /// </summary>
    public const int ImGuiVersionNum = 19300;

    /// <summary>
    /// Default base font size applied when a font carries no explicit legacy size, mirroring canonical
    /// <c>FONT_DEFAULT_SIZE_BASE</c>.
    /// </summary>
    private const float FontDefaultSizeBase = 20.0f;

    /// <summary>
    /// Upper bound applied to the resolved font size, mirroring canonical <c>IMGUI_FONT_SIZE_MAX</c>
    /// </summary>
    private const float FontSizeMax = 512.0f;

    /// <summary>
    /// Length of the canonical temporary formatting buffer, in bytes: <c>1024 * 3 + 1</c>
    /// </summary>
    private const int TempBufferSize = 1024 * 3 + 1;

    /// <summary>
    /// Current context for the calling thread. Canonical keeps a single process-wide <c>GImGui</c>
    /// pointer.; this port stores one context per thread,
    /// matching the threading contract documented for the assembled module. A context is therefore
    /// only reachable from the thread that created or adopted it.
    /// </summary>
    [ThreadStatic]
    private static ImGuiContext? _currentContext;

    /// <summary>
    /// Returns the current context, asserting and throwing when none is installed. Canonical callers
    /// dereference <c>GImGui</c> unconditionally; this helper is the single guarded realization of
    /// that requirement and keeps the ported method bodies reading like the canonical ones.
    /// </summary>
    /// <remarks>
    /// Realizes canonical <c>GetCurrentContext()</c>. Canonical
    /// also exposes a <c>GImGui</c> macro that skips the null check; every ported body routes through this
    /// helper instead so a missing context fails deterministically.
    /// </remarks>
    /// <returns>The non-null current context.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    private static ImGuiContext GetContext()
    {
        ImGuiContext? ctx = _currentContext;
        ImAssert.Check(ctx != null, NoContextMessage);
        if (ctx == null)
            throw new InvalidOperationException(NoContextMessage);
        return ctx;
    }

    /// <summary>
    /// Creates a context, makes it current, runs one-time initialization, and restores the previously
    /// current context. Mirrors canonical <c>ImGui::CreateContext</c>
    /// </summary>
    /// <param name="sharedFontAtlas">
    /// Optional font atlas to share between contexts. Canonical creates an atlas owned by the new
    /// context when this is null; this port's <see cref="ImGuiIO"/> always constructs an atlas, and the
    /// supplied atlas replaces it.
    /// </param>
    /// <returns>The newly created context. It remains current when no context was current before.</returns>
    /// <remarks>
    /// The canonical context constructor fills roughly two hundred fields. This port's
    /// <see cref="ImGuiContext"/> realizes those defaults through property initializers; this method
    /// applies only the defaults that depend on other state: the input/output back-reference, the
    /// text-editor back-reference, the Ctrl+Tab chords, the initial IME position, and the debug break
    /// chord. Font-atlas ownership tracking (<c>ImFontAtlas::OwnerContext</c>, <c>RefCount</c>) does
    /// not exist in this port, so the context neither adopts nor deletes the atlas.
    /// </remarks>
    public static ImGuiContext CreateContext(ImFontAtlas? sharedFontAtlas = null)
    {
        ImGuiContext? prevCtx = GetCurrentContext();
        ImGuiContext ctx = new ImGuiContext();

        IO_GetCtx(ctx).Ctx = ctx;
        ctx.InputTextState.Ctx = ctx;
        if (sharedFontAtlas != null)
            ctx.IO.Fonts = sharedFontAtlas;

        // Canonical ImGuiContext::ImGuiContext().
        ctx.ConfigNavWindowingKeyNext = ctx.IO.ConfigMacOSXBehaviors
            ? ImGuiKey.ImGuiMod_Super | ImGuiKey.Tab
            : ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.Tab;
        ctx.ConfigNavWindowingKeyPrev = ctx.IO.ConfigMacOSXBehaviors
            ? ImGuiKey.ImGuiMod_Super | ImGuiKey.ImGuiMod_Shift | ImGuiKey.Tab
            : ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift | ImGuiKey.Tab;
        ctx.PlatformImeDataPrev.InputPos = new ImVec2(-1.0f, -1.0f); // Different to ensure initial submission
        ctx.DebugBreakKeyChord = ImGuiKey.Pause;

        SetCurrentContext(ctx);
        Initialize();
        if (prevCtx != null)
            SetCurrentContext(prevCtx); // Restore previous context if any, else keep new one.
        return ctx;
    }

    /// <summary>
    /// Returns the input/output record of a context. Used only while the context is being constructed,
    /// before it is installed as the current context.
    /// </summary>
    /// <remarks>
    /// Mirrors canonical <c>GetIO(ImGuiContext* ctx)</c>., which
    /// exists precisely so context construction and destruction need not touch the ambient context.
    /// </remarks>
    /// <param name="ctx">Context whose input/output record is required.</param>
    /// <returns>The context's input/output record.</returns>
    private static ImGuiIO IO_GetCtx(ImGuiContext ctx) => ctx.IO;

    /// <summary>
    /// Destroys a context and its owned resources. Mirrors canonical <c>ImGui::DestroyContext</c>
    /// </summary>
    /// <param name="ctx">Context to destroy, or null to destroy the current context.</param>
    /// <remarks>
    /// Canonical calls <c>IM_DELETE(ctx)</c> after shutting down; this port relies on the garbage
    /// collector, so <see cref="Shutdown"/> is the only explicit teardown. The call is a no-op when no
    /// context is given and none is current, whereas canonical would fault.
    /// </remarks>
    public static void DestroyContext(ImGuiContext? ctx = null)
    {
        ImGuiContext? prevCtx = GetCurrentContext();
        ImGuiContext? target = ctx ?? prevCtx;
        if (target == null)
            return;
        SetCurrentContext(target);
        Shutdown();
        SetCurrentContext(prevCtx != target ? prevCtx : null);
    }

    /// <summary>
    /// Returns the current context, or null when none is installed. Mirrors canonical
    /// <c>ImGui::GetCurrentContext</c>.
    /// </summary>
    /// <returns>The current context or null.</returns>
    public static ImGuiContext? GetCurrentContext()
    {
        return _currentContext;
    }

    /// <summary>
    /// Canonical global context pointer accessor (<c>GImGui</c>).
    /// </summary>
    internal static ImGuiContext? GImGui => _currentContext;

    /// <summary>
    /// Installs a context as the current context for the calling thread. Mirrors canonical
    /// <c>ImGui::SetCurrentContext</c>.
    /// </summary>
    /// <param name="ctx">Context to install, or null to clear the current context.</param>
    public static void SetCurrentContext(ImGuiContext? ctx)
    {
        _currentContext = ctx;
    }

    /// <summary>
    /// Performs the context's one-time initialization: registers the English localization table,
    /// creates the main viewport, sizes the temporary buffer, builds the character-input key table,
    /// and marks the context initialized. Mirrors canonical <c>ImGui::Initialize</c>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The following canonical steps are owned by other chunks and are deliberately inert here:
    /// settings handlers (chunk 13, settings-file I/O) and the docking context (chunk 6). The platform
    /// interface is created with the context, its session date is recorded here, and its handler slots
    /// start empty because this port installs no native defaults.
    /// </para>
    /// <para>
    /// Canonical registers the atlas through <c>RegisterFontAtlas</c>, which links the atlas to the
    /// shared draw-list data. This port's <see cref="ImDrawList"/> is constructed with its atlas and
    /// refreshes its own atlas tables, so registration only records the atlas in
    /// <see cref="ImGuiContext.FontAtlases"/>. Canonical also stores the context on the shared draw-list
    /// data (<c>g.DrawListSharedData.Context = &amp;g</c>); this port's
    /// <see cref="ImDrawListSharedData"/> has no <c>Context</c> member and draw lists receive their
    /// context through construction instead.
    /// </para>
    /// </remarks>
    private static void Initialize()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(!g.Initialized && !g.SettingsLoaded);

        // Setup default localization table.
        LocalizeRegisterEntries(GLocalizationEntriesEnUS, GLocalizationEntriesEnUS.Length);

        // Register the "Window" and "Table" settings handlers
        // DockContextInitialize(), which keeps the canonical Window, Table, Docking section order.
        ImGuiSettingsHandler windowSettingsHandler = new()
        {
            TypeName = "Window",
            TypeHash = ImHashStr("Window"),
            ClearAllFn = WindowSettingsHandler_ClearAll,
            CleanupFn = WindowSettingsHandler_Cleanup,
            ReadOpenFn = WindowSettingsHandler_ReadOpen,
            ReadLineFn = WindowSettingsHandler_ReadLine,
            ApplyAllFn = WindowSettingsHandler_ApplyAll,
            WriteAllFn = WindowSettingsHandler_WriteAll,
        };
        AddSettingsHandler(windowSettingsHandler);
        TableSettingsAddSettingsHandler();

        // Record the session starting date once, in the packed YYYYMMDD form the settings layer stores
        // handlers here; this port leaves both null so callers can detect that no host handler exists.
        DateTime sessionStart = DateTime.Now;
        g.PlatformIO.Platform_SessionDate = (sessionStart.Year * 10000) + (sessionStart.Month * 100) + sessionStart.Day;

        // Create default viewport.
        ImGuiViewportP viewport = new ImGuiViewportP();
        viewport.ID = ImGuiInternalConstants.ViewportDefaultId;
        viewport.Idx = 0;
        viewport.PlatformWindowCreated = true;
        viewport.Flags = ImGuiViewportFlags.OwnedByApp;
        g.Viewports.Add(viewport);

        // The platform-facing viewport list is rebuilt by UpdateViewportsEndFrame() each frame.

        // Setup temporary formatting buffer.
        g.TempBuffer.Clear();
        g.TempBuffer.Capacity = Math.Max(g.TempBuffer.Capacity, TempBufferSize);
        for (int n = 0; n < TempBufferSize; n++)
            g.TempBuffer.Add(0);

        g.ViewportCreatedCount++;

        // Build KeysMayBeCharInput[] lookup table, one bit per named key
        for (ImGuiKey key = ImGuiKey.NamedKey_BEGIN; key < ImGuiKey.NamedKey_END; key = (ImGuiKey)((int)key + 1))
        {
            if ((key >= ImGuiKey._0 && key <= ImGuiKey._9)
                || (key >= ImGuiKey.A && key <= ImGuiKey.Z)
                || (key >= ImGuiKey.Keypad0 && key <= ImGuiKey.Keypad9)
                || key == ImGuiKey.Tab || key == ImGuiKey.Space || key == ImGuiKey.Apostrophe
                || key == ImGuiKey.Comma || key == ImGuiKey.Minus || key == ImGuiKey.Period
                || key == ImGuiKey.Slash || key == ImGuiKey.Semicolon || key == ImGuiKey.Equal
                || key == ImGuiKey.LeftBracket || key == ImGuiKey.RightBracket || key == ImGuiKey.GraveAccent
                || key == ImGuiKey.KeypadDecimal || key == ImGuiKey.KeypadDivide || key == ImGuiKey.KeypadMultiply
                || key == ImGuiKey.KeypadSubtract || key == ImGuiKey.KeypadAdd || key == ImGuiKey.KeypadEqual)
            {
                g.KeysMayBeCharInput.SetBit(key);
            }
        }

        RegisterFontAtlas(g.IO.Fonts);
        DockContextInitialize(g);

        // Setup default ImGuiPlatformIO shell handler.
        g.PlatformIO.Platform_OpenInShellFn = Platform_OpenInShellDefault;

        g.Initialized = true;
    }

    /// <summary>
    /// Records a font atlas as used by the context. Mirrors the list bookkeeping of canonical
    /// <c>ImGui::RegisterFontAtlas</c>.
    /// </summary>
    /// <param name="atlas">Atlas to register.</param>
    /// <remarks>
    /// Canonical increments <c>ImFontAtlas::RefCount</c>, links the atlas to the shared draw-list data,
    /// and pushes it onto <see cref="ImGuiContext.FontAtlases"/>. This port's <see cref="ImFontAtlas"/>
    /// has no reference count and its draw lists bind their atlas at construction, so registration is
    /// idempotent insertion into the list only.
    /// </remarks>
    private static void RegisterFontAtlas(ImFontAtlas atlas)
    {
        ImGuiContext g = GetContext();
        if (g.FontAtlases.Count == 0)
            ImAssert.Check(atlas == g.IO.Fonts, "The first registered atlas must be the input/output atlas.");
        if (!g.FontAtlases.Contains(atlas))
            g.FontAtlases.Add(atlas);
    }

    /// <summary>
    /// Removes a font atlas from the context's atlas list. Mirrors the list bookkeeping of canonical
    /// <c>ImGui::UnregisterFontAtlas</c>.
    /// </summary>
    /// <param name="atlas">Atlas to remove.</param>
    private static void UnregisterFontAtlas(ImFontAtlas atlas)
    {
        ImGuiContext g = GetContext();
        g.FontAtlases.Remove(atlas);
    }

    /// <summary>
    /// Registers a user-created texture so the context tracks its lifetime. Mirrors canonical
    /// <c>ImGui::RegisterUserTexture</c>.
    /// </summary>
    /// <param name="texture">The texture to register.</param>
    /// <remarks>
    /// <para>
    /// Registration increments the texture's reference count and appends it to
    /// <see cref="ImGuiContext.UserTextures"/>. The pixel data is queued through
    /// <see cref="ImTextureData.QueueUpload"/> and consumed by the backend on the next frame, so the
    /// caller must not release its own reference to the texture before unregistering it.
    /// </para>
    /// <para>
    /// The texture must not already be owned by a font atlas; the font atlas path maintains its own
    /// registrations.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="texture"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    public static void RegisterUserTexture(ImTextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ImGuiContext g = GetContext();
        texture.RefCount++;
        g.UserTextures.Add(texture);
    }

    /// <summary>
    /// Unregisters a texture previously passed to <see cref="RegisterUserTexture"/>. Mirrors canonical
    /// <c>ImGui::UnregisterUserTexture</c>.
    /// </summary>
    /// <param name="texture">The texture to unregister.</param>
    /// <remarks>
    /// The reference count is decremented and the first matching entry is removed from
    /// <see cref="ImGuiContext.UserTextures"/>. Unregistering a texture whose reference count is
    /// already zero is a precondition violation and is asserted.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="texture"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    public static void UnregisterUserTexture(ImTextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ImGuiContext g = GetContext();
        ImAssert.Check(texture.RefCount > 0, "Unregistering a user texture requires a positive reference count.");
        texture.RefCount--;
        int index = g.UserTextures.IndexOf(texture);
        if (index >= 0)
            g.UserTextures.RemoveAt(index);
    }

    /// <summary>
    /// Releases the context's heap allocations and clears its runtime state. Mirrors canonical
    /// <c>ImGui::Shutdown</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Canonical asserts that the platform and renderer backends have been detached, unregisters and
    /// deletes context-owned atlases, saves the settings file, shuts the docking context down, and
    /// clears tables, tab bars, multi-select storage and settings handlers. The settings file
    /// (chunk 13) and docking (chunk 6) are owned by later chunks; the platform output lists are
    /// cleared with the rest of the context state, and every runtime collection that exists on this
    /// port's <see cref="ImGuiContext"/> is cleared.
    /// </para>
    /// <para>
    /// This port's <see cref="ImFontAtlas"/> carries no owner-context flag and no reference count, so
    /// canonical's conditional deletion of an atlas owned by this context is replaced by unregistering
    /// and unlocking each atlas and letting the garbage collector reclaim it.
    /// </para>
    /// </remarks>
    public static void Shutdown()
    {
        ImGuiContext g = GetContext();
        AssertUserError(g.IO.BackendPlatformUserData == null, "Forgot to shutdown Platform backend?");
        AssertUserError(g.IO.BackendRendererUserData == null, "Forgot to shutdown Renderer backend?");
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            AssertUserError(
                viewport.RendererUserData == IntPtr.Zero
                && viewport.PlatformUserData == IntPtr.Zero
                && viewport.PlatformHandle == IntPtr.Zero,
                "Backend or app forgot to call DestroyPlatformWindows()?");
        }

        // The font atlases can be used prior to calling NewFrame(), so they are released even when the
        // context was never initialized. Canonical also deletes an
        // atlas whose OwnerContext is this context once its reference count reaches zero; neither member
        // exists on this port's ImFontAtlas, so the atlas is unregistered and unlocked and then left to
        // the garbage collector. The loop walks backwards because UnregisterFontAtlas removes the entry.
        for (int n = g.FontAtlases.Count - 1; n >= 0; n--)
        {
            ImFontAtlas atlas = g.FontAtlases[n];
            UnregisterFontAtlas(atlas);
            atlas.Locked = false;
        }
        ImAssert.Check(g.FontAtlases.Count == 0, "Every font atlas must be unregistered during shutdown.");

        // Canonical also clears the path-point scratch vector held by the shared draw-list data
        // member, so that line has no counterpart; ImGuiContext.TempBuffer is a different, text-only
        // scratch buffer and is not cleared here.

        // Cleanup of other data is conditional on actually having initialized Dear ImGui.
        if (!g.Initialized)
            return;

        // SaveIniSettingsToDisk(): flush the pending layout before the settings state is torn down
        if (g.IO.IniFilename != null)
            SaveIniSettingsToDisk(g.IO.IniFilename);
        // DockContextShutdown(): chunk 6 (docking).

        CallContextHooks(g, ImGuiContextHookType.Shutdown);

        // Clear everything else.
        g.Windows.Clear();
        g.WindowsFocusOrder.Clear();
        g.WindowsTempSortBuffer.Clear();
        g.CurrentWindow = null;
        g.CurrentWindowStack.Clear();
        g.WindowsById.Clear();
        g.NavWindow = null;
        g.HoveredWindow = null;
        g.HoveredWindowUnderMovingWindow = null;
        g.ActiveIdWindow = null;
        g.MovingWindow = null;

        g.KeysRoutingTable.Clear();

        g.ColorStack.Clear();
        g.StyleVarStack.Clear();
        g.FontStack.Clear();
        g.OpenPopupStack.Clear();
        g.BeginPopupStack.Clear();
        g.TreeNodeStack.Clear();

        g.CurrentViewport = null;
        g.MouseViewport = null;
        g.MouseLastHoveredViewport = null;
        g.Viewports.Clear();

        // Collections shared with the tab-bar and draw-channel chunks are cleared here in canonical
        // order.
        g.CurrentTabBarStack.Clear();
        g.TabBars.Clear();
        g.ShrinkWidthBuffer.Clear();

        // Clipper temporary data is released before the table pool, in canonical order
        // that the "stacked index fits the pool" invariant holds if the context is initialized again.
        g.ClipperTempData.Clear();
        g.ClipperTempDataStacked = 0;

        g.Tables.Clear();
        g.TablesTempData.Clear();
        g.TablesLastTimeActive.Clear();
        g.CurrentTable = null;

        g.DrawChannelsTempMergeBuffer.Clear();

        g.MultiSelectStorage.Clear();
        g.MultiSelectTempData.Clear();

        g.ClipboardHandlerData.Clear();
        g.MenusIdSubmittedThisFrame.Clear();
        g.InputTextState.ClearFreeMemory();
        g.InputTextLineIndex.Clear();
        g.InputTextDeactivatedState.ClearFreeMemory();

        g.SettingsWindows.Clear();
        g.SettingsHandlers.Clear();
        DockContextShutdown(g);

        if (g.LogFile != null)
        {
            if (g.LogFile != Console.Out)
                g.LogFile.Dispose();
            g.LogFile = null;
        }
        g.LogBuffer.Clear();
        g.DebugLogBuf.Clear();
        g.DebugLogIndex.Clear();

        g.Initialized = false;
    }

    #endregion

    #region [SECTION] LOCALIZATION

    /// <summary>
    /// Canonical English localization table, mirroring <c>GLocalizationEntriesEnUS</c>
    ///. Interactive entries keep their fixed
    /// <c>###</c> suffix because automation relies on an identical identifier in every language.
    /// </summary>
    private static readonly ImGuiLocEntry[] GLocalizationEntriesEnUS =
    {
        new ImGuiLocEntry { Key = ImGuiLocKey.VersionStr, Text = "Dear ImGui " + ImGuiVersion + " (" + ImGuiVersionNum + ")" },
        new ImGuiLocEntry { Key = ImGuiLocKey.TableSizeOne, Text = "Size column to fit###SizeOne" },
        new ImGuiLocEntry { Key = ImGuiLocKey.TableSizeAllFit, Text = "Size all columns to fit###SizeAll" },
        new ImGuiLocEntry { Key = ImGuiLocKey.TableSizeAllDefault, Text = "Size all columns to default###SizeAll" },
        new ImGuiLocEntry { Key = ImGuiLocKey.TableReset, Text = "Reset" },
        new ImGuiLocEntry { Key = ImGuiLocKey.TableResetOrder, Text = "Reset order###ResetOrder" },
        new ImGuiLocEntry { Key = ImGuiLocKey.TableResetVisibility, Text = "Reset visibility###ResetVisibility" },
        new ImGuiLocEntry { Key = ImGuiLocKey.WindowingMainMenuBar, Text = "(Main menu bar)" },
        new ImGuiLocEntry { Key = ImGuiLocKey.WindowingPopup, Text = "(Popup)" },
        new ImGuiLocEntry { Key = ImGuiLocKey.WindowingUntitled, Text = "(Untitled)" },
        new ImGuiLocEntry { Key = ImGuiLocKey.OpenLink_s, Text = "Open '%s'" },
        new ImGuiLocEntry { Key = ImGuiLocKey.CopyLink, Text = "Copy Link###CopyLink" },
        new ImGuiLocEntry { Key = ImGuiLocKey.DockingHideTabBar, Text = "Hide tab bar###HideTabBar" },
        new ImGuiLocEntry { Key = ImGuiLocKey.DockingHoldShiftToDock, Text = "Hold SHIFT to enable Docking window." },
        new ImGuiLocEntry { Key = ImGuiLocKey.DockingDragToUndockOrMoveNode, Text = "Click and drag to move or undock whole node." },
    };

    /// <summary>
    /// Registers localization entries into the current context's table. Mirrors canonical
    /// <c>ImGui::LocalizeRegisterEntries</c>.
    /// </summary>
    /// <param name="entries">Entries to register, indexed by <see cref="ImGuiLocEntry.Key"/>.</param>
    /// <param name="count">Number of leading entries in <paramref name="entries"/> to register.</param>
    internal static void LocalizeRegisterEntries(ImGuiLocEntry[] entries, int count)
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < count; n++)
            g.LocalizationTable[(int)entries[n].Key] = entries[n].Text;
    }

    /// <summary>
    /// Returns the localized text for a key. Mirrors canonical <c>LocalizeGetMsg</c>
    ///., which substitutes
    /// <c>"*Missing Text*"</c> when no entry is registered for the key.
    /// </summary>
    /// <param name="key">Localization key to resolve.</param>
    /// <returns>The registered text, or the canonical missing-text placeholder.</returns>
    internal static string LocalizeGetMsg(ImGuiLocKey key)
    {
        ImGuiContext g = GetContext();
        string? msg = g.LocalizationTable[(int)key];
        return msg ?? "*Missing Text*";
    }

    #endregion

    #region [SECTION] STYLING

    /// <summary>
    /// Returns the current context's style. Mirrors canonical <c>ImGui::GetStyle</c>
    ///. The returned instance is the context's live
    /// style object, so mutations are visible to subsequent drawing.
    /// </summary>
    /// <returns>The current style.</returns>
    public static ImGuiStyle GetStyle()
    {
        return GetContext().Style;
    }

    /// <summary>
    /// Applies the default dark color theme to a style. Mirrors canonical
    /// <c>ImGui::StyleColorsDark</c>.
    /// </summary>
    /// <param name="dst">Target style, or <see langword="null"/> to modify the current context's style.</param>
    /// <remarks>
    /// The color values are owned by <see cref="ImGuiStyle.SetDarkTheme"/>, which the port's
    /// <see cref="ImGuiStyle"/> constructor also applies; this entry point supplies the canonical free
    /// function and its destination parameter.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="dst"/> is null and no
    /// current context is installed.</exception>
    public static void StyleColorsDark(ImGuiStyle? dst = null)
    {
        (dst ?? GetStyle()).SetDarkTheme();
    }

    /// <summary>
    /// Applies the classic color theme to a style. Mirrors canonical
    /// <c>ImGui::StyleColorsClassic</c>.
    /// </summary>
    /// <param name="dst">Target style, or <see langword="null"/> to modify the current context's style.</param>
    /// <remarks>
    /// Derived slots use the canonical interpolation expressions against the base colors assigned
    /// earlier in the table rather than hard-coded values.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="dst"/> is null and no
    /// current context is installed.</exception>
    public static void StyleColorsClassic(ImGuiStyle? dst = null)
    {
        ImVec4[] colors = (dst ?? GetStyle()).Colors;

        colors[(int)ImGuiCol.Text] = new(0.90f, 0.90f, 0.90f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new(0.60f, 0.60f, 0.60f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new(0.00f, 0.00f, 0.00f, 0.85f);
        colors[(int)ImGuiCol.ChildBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.PopupBg] = new(0.11f, 0.11f, 0.14f, 0.92f);
        colors[(int)ImGuiCol.Border] = new(0.50f, 0.50f, 0.50f, 0.50f);
        colors[(int)ImGuiCol.BorderShadow] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.FrameBg] = new(0.43f, 0.43f, 0.43f, 0.39f);
        colors[(int)ImGuiCol.FrameBgHovered] = new(0.47f, 0.47f, 0.69f, 0.40f);
        colors[(int)ImGuiCol.FrameBgActive] = new(0.42f, 0.41f, 0.64f, 0.69f);
        colors[(int)ImGuiCol.TitleBg] = new(0.27f, 0.27f, 0.54f, 0.83f);
        colors[(int)ImGuiCol.TitleBgActive] = new(0.32f, 0.32f, 0.63f, 0.87f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new(0.40f, 0.40f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.MenuBarBg] = new(0.40f, 0.40f, 0.55f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarBg] = new(0.20f, 0.25f, 0.30f, 0.60f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new(0.40f, 0.40f, 0.80f, 0.30f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new(0.40f, 0.40f, 0.80f, 0.40f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new(0.41f, 0.39f, 0.80f, 0.60f);
        colors[(int)ImGuiCol.CheckMark] = new(0.90f, 0.90f, 0.90f, 0.50f);
        colors[(int)ImGuiCol.CheckboxSelectedBg] = StyleColorLerp(colors[(int)ImGuiCol.FrameBg], colors[(int)ImGuiCol.FrameBgActive], 0.65f);
        colors[(int)ImGuiCol.SliderGrab] = new(1.00f, 1.00f, 1.00f, 0.30f);
        colors[(int)ImGuiCol.SliderGrabActive] = new(0.41f, 0.39f, 0.80f, 0.60f);
        colors[(int)ImGuiCol.Button] = new(0.35f, 0.40f, 0.61f, 0.62f);
        colors[(int)ImGuiCol.ButtonHovered] = new(0.40f, 0.48f, 0.71f, 0.79f);
        colors[(int)ImGuiCol.ButtonActive] = new(0.46f, 0.54f, 0.80f, 1.00f);
        colors[(int)ImGuiCol.Header] = new(0.40f, 0.40f, 0.90f, 0.45f);
        colors[(int)ImGuiCol.HeaderHovered] = new(0.45f, 0.45f, 0.90f, 0.80f);
        colors[(int)ImGuiCol.HeaderActive] = new(0.53f, 0.53f, 0.87f, 0.80f);
        colors[(int)ImGuiCol.Separator] = new(0.50f, 0.50f, 0.50f, 0.60f);
        colors[(int)ImGuiCol.SeparatorHovered] = new(0.60f, 0.60f, 0.70f, 1.00f);
        colors[(int)ImGuiCol.SeparatorActive] = new(0.70f, 0.70f, 0.90f, 1.00f);
        colors[(int)ImGuiCol.ResizeGrip] = new(1.00f, 1.00f, 1.00f, 0.10f);
        colors[(int)ImGuiCol.ResizeGripHovered] = new(0.78f, 0.82f, 1.00f, 0.60f);
        colors[(int)ImGuiCol.ResizeGripActive] = new(0.78f, 0.82f, 1.00f, 0.90f);
        colors[(int)ImGuiCol.InputTextCursor] = colors[(int)ImGuiCol.Text];
        colors[(int)ImGuiCol.TabHovered] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.Tab] = StyleColorLerp(colors[(int)ImGuiCol.Header], colors[(int)ImGuiCol.TitleBgActive], 0.80f);
        colors[(int)ImGuiCol.TabSelected] = StyleColorLerp(colors[(int)ImGuiCol.HeaderActive], colors[(int)ImGuiCol.TitleBgActive], 0.60f);
        colors[(int)ImGuiCol.TabSelectedOverline] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TabDimmed] = StyleColorLerp(colors[(int)ImGuiCol.Tab], colors[(int)ImGuiCol.TitleBg], 0.80f);
        colors[(int)ImGuiCol.TabDimmedSelected] = StyleColorLerp(colors[(int)ImGuiCol.TabSelected], colors[(int)ImGuiCol.TitleBg], 0.40f);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new(0.53f, 0.53f, 0.87f, 0.00f);
        colors[(int)ImGuiCol.DockingPreview] = new(
            colors[(int)ImGuiCol.Header].X,
            colors[(int)ImGuiCol.Header].Y,
            colors[(int)ImGuiCol.Header].Z,
            colors[(int)ImGuiCol.Header].W * 0.7f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new(0.20f, 0.20f, 0.20f, 1.00f);
        colors[(int)ImGuiCol.PlotLines] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new(0.90f, 0.70f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new(0.90f, 0.70f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new(1.00f, 0.60f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new(0.27f, 0.27f, 0.38f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new(0.31f, 0.31f, 0.45f, 1.00f);
        colors[(int)ImGuiCol.TableBorderLight] = new(0.26f, 0.26f, 0.28f, 1.00f);
        colors[(int)ImGuiCol.TableRowBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TableRowBgAlt] = new(1.00f, 1.00f, 1.00f, 0.07f);
        colors[(int)ImGuiCol.TextLink] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TextSelectedBg] = new(0.00f, 0.00f, 1.00f, 0.35f);
        colors[(int)ImGuiCol.TreeLines] = colors[(int)ImGuiCol.Border];
        colors[(int)ImGuiCol.DragDropTarget] = new(1.00f, 1.00f, 0.00f, 0.90f);
        colors[(int)ImGuiCol.DragDropTargetBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.UnsavedMarker] = new(0.90f, 0.90f, 0.90f, 1.00f);
        colors[(int)ImGuiCol.NavCursor] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.NavWindowingHighlight] = new(1.00f, 1.00f, 1.00f, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.80f, 0.80f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new(0.20f, 0.20f, 0.20f, 0.35f);
    }

    /// <summary>
    /// Applies the light color theme to a style. Mirrors canonical
    /// <c>ImGui::StyleColorsLight</c>.
    /// </summary>
    /// <param name="dst">Target style, or <see langword="null"/> to modify the current context's style.</param>
    /// <remarks>
    /// Derived slots use the canonical interpolation expressions against the base colors assigned
    /// earlier in the table rather than hard-coded values.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="dst"/> is null and no
    /// current context is installed.</exception>
    public static void StyleColorsLight(ImGuiStyle? dst = null)
    {
        ImVec4[] colors = (dst ?? GetStyle()).Colors;

        colors[(int)ImGuiCol.Text] = new(0.00f, 0.00f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new(0.60f, 0.60f, 0.60f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new(0.94f, 0.94f, 0.94f, 1.00f);
        colors[(int)ImGuiCol.ChildBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.PopupBg] = new(1.00f, 1.00f, 1.00f, 0.98f);
        colors[(int)ImGuiCol.Border] = new(0.00f, 0.00f, 0.00f, 0.30f);
        colors[(int)ImGuiCol.BorderShadow] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.FrameBg] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new(0.26f, 0.59f, 0.98f, 0.40f);
        colors[(int)ImGuiCol.FrameBgActive] = new(0.26f, 0.59f, 0.98f, 0.67f);
        colors[(int)ImGuiCol.TitleBg] = new(0.96f, 0.96f, 0.96f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new(0.82f, 0.82f, 0.82f, 1.00f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new(1.00f, 1.00f, 1.00f, 0.51f);
        colors[(int)ImGuiCol.MenuBarBg] = new(0.86f, 0.86f, 0.86f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new(0.98f, 0.98f, 0.98f, 0.53f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new(0.69f, 0.69f, 0.69f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new(0.49f, 0.49f, 0.49f, 0.80f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new(0.49f, 0.49f, 0.49f, 1.00f);
        colors[(int)ImGuiCol.CheckMark] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.CheckboxSelectedBg] = new(0.95f, 0.97f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.SliderGrab] = new(0.26f, 0.59f, 0.98f, 0.78f);
        colors[(int)ImGuiCol.SliderGrabActive] = new(0.46f, 0.54f, 0.80f, 0.60f);
        colors[(int)ImGuiCol.Button] = new(0.26f, 0.59f, 0.98f, 0.40f);
        colors[(int)ImGuiCol.ButtonHovered] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = new(0.06f, 0.53f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.Header] = new(0.26f, 0.59f, 0.98f, 0.31f);
        colors[(int)ImGuiCol.HeaderHovered] = new(0.26f, 0.59f, 0.98f, 0.80f);
        colors[(int)ImGuiCol.HeaderActive] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.Separator] = new(0.39f, 0.39f, 0.39f, 0.62f);
        colors[(int)ImGuiCol.SeparatorHovered] = new(0.14f, 0.44f, 0.80f, 0.78f);
        colors[(int)ImGuiCol.SeparatorActive] = new(0.14f, 0.44f, 0.80f, 1.00f);
        colors[(int)ImGuiCol.ResizeGrip] = new(0.35f, 0.35f, 0.35f, 0.17f);
        colors[(int)ImGuiCol.ResizeGripHovered] = new(0.26f, 0.59f, 0.98f, 0.67f);
        colors[(int)ImGuiCol.ResizeGripActive] = new(0.26f, 0.59f, 0.98f, 0.95f);
        colors[(int)ImGuiCol.InputTextCursor] = colors[(int)ImGuiCol.Text];
        colors[(int)ImGuiCol.TabHovered] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.Tab] = StyleColorLerp(colors[(int)ImGuiCol.Header], colors[(int)ImGuiCol.TitleBgActive], 0.90f);
        colors[(int)ImGuiCol.TabSelected] = StyleColorLerp(colors[(int)ImGuiCol.HeaderActive], colors[(int)ImGuiCol.TitleBgActive], 0.60f);
        colors[(int)ImGuiCol.TabSelectedOverline] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TabDimmed] = StyleColorLerp(colors[(int)ImGuiCol.Tab], colors[(int)ImGuiCol.TitleBg], 0.80f);
        colors[(int)ImGuiCol.TabDimmedSelected] = StyleColorLerp(colors[(int)ImGuiCol.TabSelected], colors[(int)ImGuiCol.TitleBg], 0.40f);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new(0.26f, 0.59f, 1.00f, 0.00f);
        colors[(int)ImGuiCol.DockingPreview] = new(
            colors[(int)ImGuiCol.Header].X,
            colors[(int)ImGuiCol.Header].Y,
            colors[(int)ImGuiCol.Header].Z,
            colors[(int)ImGuiCol.Header].W * 0.7f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new(0.20f, 0.20f, 0.20f, 1.00f);
        colors[(int)ImGuiCol.PlotLines] = new(0.39f, 0.39f, 0.39f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new(1.00f, 0.43f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new(0.90f, 0.70f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new(1.00f, 0.45f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new(0.78f, 0.87f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new(0.57f, 0.57f, 0.64f, 1.00f);
        colors[(int)ImGuiCol.TableBorderLight] = new(0.68f, 0.68f, 0.74f, 1.00f);
        colors[(int)ImGuiCol.TableRowBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TableRowBgAlt] = new(0.30f, 0.30f, 0.30f, 0.09f);
        colors[(int)ImGuiCol.TextLink] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TextSelectedBg] = new(0.26f, 0.59f, 0.98f, 0.35f);
        colors[(int)ImGuiCol.TreeLines] = colors[(int)ImGuiCol.Border];
        colors[(int)ImGuiCol.DragDropTarget] = new(0.26f, 0.59f, 0.98f, 0.95f);
        colors[(int)ImGuiCol.DragDropTargetBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.UnsavedMarker] = new(0.00f, 0.00f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.NavCursor] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.NavWindowingHighlight] = new(0.70f, 0.70f, 0.70f, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.20f, 0.20f, 0.20f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new(0.20f, 0.20f, 0.20f, 0.35f);
    }

    /// <summary>
    /// Linearly interpolates two style colors. This mirrors the file-local <c>ImLerp</c> overload used
    /// by the canonical style color tables.
    /// </summary>
    /// <param name="a">Color returned when the factor is zero.</param>
    /// <param name="b">Color returned when the factor is one.</param>
    /// <param name="t">Interpolation factor.</param>
    /// <returns>The component-wise interpolated color.</returns>
    private static ImVec4 StyleColorLerp(ImVec4 a, ImVec4 b, float t)
    {
        return new ImVec4(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t);
    }

    /// <summary>
    /// Index of the style most recently selected through <see cref="ShowStyleSelector"/>, or -1 before
    /// any selection. Mirrors the function-local <c>static int style_idx</c> in canonical
    /// <c>ImGui::ShowStyleSelector</c>.
    /// </summary>
    private static int StyleSelectorIndex = -1;

    /// <summary>
    /// Submits a combo box that switches between the dark, light, and classic color themes. Mirrors
    /// canonical <c>ImGui::ShowStyleSelector</c>.
    /// </summary>
    /// <param name="label">The combo label; the canonical demo passes <c>"Colors##Selector"</c>.</param>
    /// <returns><see langword="true"/> when the user selected a theme.</returns>
    /// <remarks>
    /// Canonical keeps no registration of which style function is active, so the most recently
    /// selected index is remembered in <see cref="StyleSelectorIndex"/> and used as the preview and
    /// selection state. Selecting an entry applies the corresponding <c>StyleColors*</c> function; the
    /// currently selected entry receives the default focus so a keyboard selection survives the next
    /// frame.
    /// </remarks>
    public static bool ShowStyleSelector(string label)
    {
        string[] styleNames = { "Dark", "Light", "Classic" };
        bool ret = false;
        string preview = StyleSelectorIndex >= 0 && StyleSelectorIndex < styleNames.Length
            ? styleNames[StyleSelectorIndex]
            : string.Empty;
        if (BeginCombo(label, preview))
        {
            for (int n = 0; n < styleNames.Length; n++)
            {
                if (Selectable(styleNames[n], StyleSelectorIndex == n, ImGuiSelectableFlags.SelectOnNav))
                {
                    StyleSelectorIndex = n;
                    ret = true;
                    switch (StyleSelectorIndex)
                    {
                        case 0: StyleColorsDark(); break;
                        case 1: StyleColorsLight(); break;
                        case 2: StyleColorsClassic(); break;
                    }
                }
                else if (StyleSelectorIndex == n)
                {
                    SetItemDefaultFocus();
                }
            }
            EndCombo();
        }
        return ret;
    }

    /// <summary>
    /// Returns a style color with the global alpha and an extra multiplier applied, packed as a
    /// 32-bit value. Mirrors canonical <c>ImGui::GetColorU32(ImGuiCol, float)</c>
    /// </summary>
    /// <param name="idx">Style color to read.</param>
    /// <param name="alphaMul">Extra alpha multiplier applied on top of <see cref="ImGuiStyle.Alpha"/>.</param>
    /// <returns>The packed color value.</returns>
    public static uint GetColorU32(ImGuiCol idx, float alphaMul = 1.0f)
    {
        ImGuiStyle style = GetContext().Style;
        ImVec4 c = style.Colors[(int)idx];
        c.W *= style.Alpha * alphaMul;
        return PackColor(c);
    }

    /// <summary>
    /// Returns a color with the global alpha applied, packed as a 32-bit value. Mirrors canonical
    /// <c>ImGui::GetColorU32(const ImVec4&amp;)</c>.
    /// </summary>
    /// <param name="col">Color to convert.</param>
    /// <returns>The packed color value.</returns>
    public static uint GetColorU32(ImVec4 col)
    {
        ImGuiStyle style = GetContext().Style;
        ImVec4 c = col;
        c.W *= style.Alpha;
        return PackColor(c);
    }

    /// <summary>
    /// Returns a style color as stored in the style, without alpha adjustment. Mirrors canonical
    /// <c>ImGui::GetStyleColorVec4</c>.
    /// </summary>
    /// <param name="idx">Style color to read.</param>
    /// <returns>The stored color value.</returns>
    public static ImVec4 GetStyleColorVec4(ImGuiCol idx)
    {
        return GetContext().Style.Colors[(int)idx];
    }

    /// <summary>
    /// Returns a packed color with the global alpha and an extra multiplier applied. Mirrors canonical
    /// <c>ImGui::GetColorU32(ImU32, float)</c>.
    /// </summary>
    /// <param name="col">Packed color to adjust.</param>
    /// <param name="alphaMul">Extra alpha multiplier applied on top of <see cref="ImGuiStyle.Alpha"/>.</param>
    /// <returns>The adjusted packed color value.</returns>
    public static uint GetColorU32(uint col, float alphaMul = 1.0f)
    {
        ImGuiStyle style = GetContext().Style;
        alphaMul *= style.Alpha;
        if (alphaMul >= 1.0f)
            return col;
        uint a = (col & 0xFF000000u) >> 24;
        a = (uint)(a * alphaMul); // Clamping is unnecessary because alpha is in the 0.1 range.
        return (col & ~0xFF000000u) | (a << 24);
    }

    /// <summary>
    /// Pushes a packed style color and applies it unless the debug flash tool owns that slot. Mirrors
    /// canonical <c>ImGui::PushStyleColor(ImGuiCol, ImU32)</c>
    /// </summary>
    /// <param name="idx">Style color slot to modify.</param>
    /// <param name="col">Packed replacement color.</param>
    public static void PushStyleColor(ImGuiCol idx, uint col)
    {
        ImGuiContext g = GetContext();
        ImGuiColorMod backup = new ImGuiColorMod
        {
            Col = idx,
            BackupValue = g.Style.Colors[(int)idx],
        };
        g.ColorStack.Add(backup);
        if (g.DebugFlashStyleColorIdx != idx)
            g.Style.Colors[(int)idx] = new ImColor(col).Value;
    }

    /// <summary>
    /// Pushes a floating-point style color and applies it unless the debug flash tool owns that slot.
    /// Mirrors canonical <c>ImGui::PushStyleColor(ImGuiCol, const ImVec4&amp;)</c>
    /// </summary>
    /// <param name="idx">Style color slot to modify.</param>
    /// <param name="col">Replacement color.</param>
    public static void PushStyleColor(ImGuiCol idx, ImVec4 col)
    {
        ImGuiContext g = GetContext();
        ImGuiColorMod backup = new ImGuiColorMod
        {
            Col = idx,
            BackupValue = g.Style.Colors[(int)idx],
        };
        g.ColorStack.Add(backup);
        if (g.DebugFlashStyleColorIdx != idx)
            g.Style.Colors[(int)idx] = col;
    }

    /// <summary>
    /// Restores style colors pushed by <see cref="PushStyleColor(ImGuiCol, ImVec4)"/>. Mirrors canonical
    /// <c>ImGui::PopStyleColor</c>.
    /// </summary>
    /// <param name="count">Number of entries to pop.</param>
    /// <remarks>
    /// Popping more entries than were pushed is a recoverable user error: the canonical
    /// <c>IM_ASSERT_USER_ERROR</c> is realized through <see cref="ErrorLog"/> and the count is clamped
    /// to the stack size.
    /// </remarks>
    public static void PopStyleColor(int count = 1)
    {
        ImGuiContext g = GetContext();
        if (g.ColorStack.Count < count)
        {
            AssertUserError(false, "Calling PopStyleColor() too many times!");
            count = g.ColorStack.Count;
        }
        while (count > 0)
        {
            ImGuiColorMod backup = g.ColorStack[g.ColorStack.Count - 1];
            g.Style.Colors[(int)backup.Col] = backup.BackupValue;
            g.ColorStack.RemoveAt(g.ColorStack.Count - 1);
            count--;
        }
    }

    /// <summary>
    /// Style-variable description table, mirroring <c>GStyleVarsInfo</c>
    ///. in <see cref="ImGuiStyleVar"/> order.
    /// </summary>
    /// <remarks>
    /// Canonical stores the byte offset of each member inside <c>ImGuiStyle</c> and resolves it with
    /// <c>GetVarPtr()</c>. Managed auto-properties expose no stable field offsets, so
    /// <see cref="ImGuiStyleVarInfo.Offset"/> is left at zero here and the variables are read and
    /// written by the <c>GetStyleVarValue</c>/<c>SetStyleVarValue</c> switch pair instead.
    /// </remarks>
    private static readonly ImGuiStyleVarInfo[] GStyleVarsInfo =
    {
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // Alpha
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // DisabledAlpha
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // WindowPadding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // WindowRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // WindowBorderSize
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // WindowMinSize
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // WindowTitleAlign
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ChildRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ChildBorderSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // PopupRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // PopupBorderSize
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // FramePadding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // FrameRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // FrameBorderSize
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // ItemSpacing
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // ItemInnerSpacing
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // IndentSpacing
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // CellPadding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ScrollbarSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ScrollbarRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ScrollbarPadding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // GrabMinSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // GrabRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ImageRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // ImageBorderSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TabRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TabBorderSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TabMinWidthBase
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TabMinWidthShrink
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TabBarBorderSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TabBarOverlineSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TableAngledHeadersAngle
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // TableAngledHeadersTextAlign
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TreeLinesSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // TreeLinesRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // MenuItemRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // SelectableRounding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // DragDropTargetRounding
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // ButtonTextAlign
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // SelectableTextAlign
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // SeparatorSize
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // SeparatorTextBorderSize
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // SeparatorTextAlign
        new ImGuiStyleVarInfo { Count = 2, DataType = ImGuiDataType.Float }, // SeparatorTextPadding
        new ImGuiStyleVarInfo { Count = 1, DataType = ImGuiDataType.Float }, // DockingSeparatorSize
    };

    /// <summary>
    /// Returns the description of a style variable. Mirrors canonical <c>ImGui::GetStyleVarInfo</c>
    /// </summary>
    /// <param name="idx">Style variable to describe.</param>
    /// <returns>
    /// The component count and data type of the variable. <see cref="ImGuiStyleVarInfo.Offset"/> is not
    /// a usable byte offset in this port; see <see cref="GStyleVarsInfo"/>.
    /// </returns>
    internal static ImGuiStyleVarInfo GetStyleVarInfo(ImGuiStyleVar idx)
    {
        int index = (int)idx;
        ImAssert.Check(index >= 0 && index < GStyleVarsInfo.Length);
        if (index < 0 || index >= GStyleVarsInfo.Length)
            return default;
        return GStyleVarsInfo[index];
    }

    /// <summary>
    /// Reads the two scalar components of a style variable. The second component is zero for variables
    /// that hold a single float.
    /// </summary>
    /// <remarks>
    /// Replaces canonical <c>ImGuiStyleVarInfo</c> descriptor reads with a switch, because managed auto-properties expose no stable field
    /// offsets for the canonical <c>Offset</c>-based access performed by <c>PushStyleVar</c>/<c>PopStyleVar</c>.
    /// The accepted members and their scalar/vector kinds follow canonical behavior exactly.
    /// </remarks>
    /// <param name="style">Style to read from. The caller owns the instance.</param>
    /// <param name="idx">Style variable to read.</param>
    /// <param name="x">Receives the first component.</param>
    /// <param name="y">Receives the second component, or zero for scalar variables.</param>
    private static void GetStyleVarValue(ImGuiStyle style, ImGuiStyleVar idx, out float x, out float y)
    {
        x = 0.0f;
        y = 0.0f;
        switch (idx)
        {
            case ImGuiStyleVar.Alpha: x = style.Alpha; break;
            case ImGuiStyleVar.DisabledAlpha: x = style.DisabledAlpha; break;
            case ImGuiStyleVar.WindowPadding: x = style.WindowPadding.X; y = style.WindowPadding.Y; break;
            case ImGuiStyleVar.WindowRounding: x = style.WindowRounding; break;
            case ImGuiStyleVar.WindowBorderSize: x = style.WindowBorderSize; break;
            case ImGuiStyleVar.WindowMinSize: x = style.WindowMinSize.X; y = style.WindowMinSize.Y; break;
            case ImGuiStyleVar.WindowTitleAlign: x = style.WindowTitleAlign.X; y = style.WindowTitleAlign.Y; break;
            case ImGuiStyleVar.ChildRounding: x = style.ChildRounding; break;
            case ImGuiStyleVar.ChildBorderSize: x = style.ChildBorderSize; break;
            case ImGuiStyleVar.PopupRounding: x = style.PopupRounding; break;
            case ImGuiStyleVar.PopupBorderSize: x = style.PopupBorderSize; break;
            case ImGuiStyleVar.FramePadding: x = style.FramePadding.X; y = style.FramePadding.Y; break;
            case ImGuiStyleVar.FrameRounding: x = style.FrameRounding; break;
            case ImGuiStyleVar.FrameBorderSize: x = style.FrameBorderSize; break;
            case ImGuiStyleVar.ItemSpacing: x = style.ItemSpacing.X; y = style.ItemSpacing.Y; break;
            case ImGuiStyleVar.ItemInnerSpacing: x = style.ItemInnerSpacing.X; y = style.ItemInnerSpacing.Y; break;
            case ImGuiStyleVar.IndentSpacing: x = style.IndentSpacing; break;
            case ImGuiStyleVar.CellPadding: x = style.CellPadding.X; y = style.CellPadding.Y; break;
            case ImGuiStyleVar.ScrollbarSize: x = style.ScrollbarSize; break;
            case ImGuiStyleVar.ScrollbarRounding: x = style.ScrollbarRounding; break;
            case ImGuiStyleVar.ScrollbarPadding: x = style.ScrollbarPadding; break;
            case ImGuiStyleVar.GrabMinSize: x = style.GrabMinSize; break;
            case ImGuiStyleVar.GrabRounding: x = style.GrabRounding; break;
            case ImGuiStyleVar.ImageRounding: x = style.ImageRounding; break;
            case ImGuiStyleVar.ImageBorderSize: x = style.ImageBorderSize; break;
            case ImGuiStyleVar.TabRounding: x = style.TabRounding; break;
            case ImGuiStyleVar.TabBorderSize: x = style.TabBorderSize; break;
            case ImGuiStyleVar.TabMinWidthBase: x = style.TabMinWidthBase; break;
            case ImGuiStyleVar.TabMinWidthShrink: x = style.TabMinWidthShrink; break;
            case ImGuiStyleVar.TabBarBorderSize: x = style.TabBarBorderSize; break;
            case ImGuiStyleVar.TabBarOverlineSize: x = style.TabBarOverlineSize; break;
            case ImGuiStyleVar.TableAngledHeadersAngle: x = style.TableAngledHeadersAngle; break;
            case ImGuiStyleVar.TableAngledHeadersTextAlign: x = style.TableAngledHeadersTextAlign.X; y = style.TableAngledHeadersTextAlign.Y; break;
            case ImGuiStyleVar.TreeLinesSize: x = style.TreeLinesSize; break;
            case ImGuiStyleVar.TreeLinesRounding: x = style.TreeLinesRounding; break;
            case ImGuiStyleVar.MenuItemRounding: x = style.MenuItemRounding; break;
            case ImGuiStyleVar.SelectableRounding: x = style.SelectableRounding; break;
            case ImGuiStyleVar.DragDropTargetRounding: x = style.DragDropTargetRounding; break;
            case ImGuiStyleVar.ButtonTextAlign: x = style.ButtonTextAlign.X; y = style.ButtonTextAlign.Y; break;
            case ImGuiStyleVar.SelectableTextAlign: x = style.SelectableTextAlign.X; y = style.SelectableTextAlign.Y; break;
            case ImGuiStyleVar.SeparatorSize: x = style.SeparatorSize; break;
            case ImGuiStyleVar.SeparatorTextBorderSize: x = style.SeparatorTextBorderSize; break;
            case ImGuiStyleVar.SeparatorTextAlign: x = style.SeparatorTextAlign.X; y = style.SeparatorTextAlign.Y; break;
            case ImGuiStyleVar.SeparatorTextPadding: x = style.SeparatorTextPadding.X; y = style.SeparatorTextPadding.Y; break;
            case ImGuiStyleVar.DockingSeparatorSize: x = style.DockingSeparatorSize; break;
            default: break;
        }
    }

    /// <summary>
    /// Writes the two scalar components of a style variable. The second component is ignored for
    /// variables that hold a single float.
    /// </summary>
    /// <remarks>
    /// The write-side counterpart of <see cref="GetStyleVarValue"/>; see that member for the canonical
    /// citation and the rationale for replacing descriptor-offset writes with a switch. The canonical
    /// write path is <c>PopStyleVar</c>.
    /// </remarks>
    /// <param name="style">Style to write to. The caller owns the instance.</param>
    /// <param name="idx">Style variable to write.</param>
    /// <param name="x">Value of the first component.</param>
    /// <param name="y">Value of the second component.</param>
    private static void SetStyleVarValue(ImGuiStyle style, ImGuiStyleVar idx, float x, float y)
    {
        switch (idx)
        {
            case ImGuiStyleVar.Alpha: style.Alpha = x; break;
            case ImGuiStyleVar.DisabledAlpha: style.DisabledAlpha = x; break;
            case ImGuiStyleVar.WindowPadding: style.WindowPadding = new ImVec2(x, y); break;
            case ImGuiStyleVar.WindowRounding: style.WindowRounding = x; break;
            case ImGuiStyleVar.WindowBorderSize: style.WindowBorderSize = x; break;
            case ImGuiStyleVar.WindowMinSize: style.WindowMinSize = new ImVec2(x, y); break;
            case ImGuiStyleVar.WindowTitleAlign: style.WindowTitleAlign = new ImVec2(x, y); break;
            case ImGuiStyleVar.ChildRounding: style.ChildRounding = x; break;
            case ImGuiStyleVar.ChildBorderSize: style.ChildBorderSize = x; break;
            case ImGuiStyleVar.PopupRounding: style.PopupRounding = x; break;
            case ImGuiStyleVar.PopupBorderSize: style.PopupBorderSize = x; break;
            case ImGuiStyleVar.FramePadding: style.FramePadding = new ImVec2(x, y); break;
            case ImGuiStyleVar.FrameRounding: style.FrameRounding = x; break;
            case ImGuiStyleVar.FrameBorderSize: style.FrameBorderSize = x; break;
            case ImGuiStyleVar.ItemSpacing: style.ItemSpacing = new ImVec2(x, y); break;
            case ImGuiStyleVar.ItemInnerSpacing: style.ItemInnerSpacing = new ImVec2(x, y); break;
            case ImGuiStyleVar.IndentSpacing: style.IndentSpacing = x; break;
            case ImGuiStyleVar.CellPadding: style.CellPadding = new ImVec2(x, y); break;
            case ImGuiStyleVar.ScrollbarSize: style.ScrollbarSize = x; break;
            case ImGuiStyleVar.ScrollbarRounding: style.ScrollbarRounding = x; break;
            case ImGuiStyleVar.ScrollbarPadding: style.ScrollbarPadding = x; break;
            case ImGuiStyleVar.GrabMinSize: style.GrabMinSize = x; break;
            case ImGuiStyleVar.GrabRounding: style.GrabRounding = x; break;
            case ImGuiStyleVar.ImageRounding: style.ImageRounding = x; break;
            case ImGuiStyleVar.ImageBorderSize: style.ImageBorderSize = x; break;
            case ImGuiStyleVar.TabRounding: style.TabRounding = x; break;
            case ImGuiStyleVar.TabBorderSize: style.TabBorderSize = x; break;
            case ImGuiStyleVar.TabMinWidthBase: style.TabMinWidthBase = x; break;
            case ImGuiStyleVar.TabMinWidthShrink: style.TabMinWidthShrink = x; break;
            case ImGuiStyleVar.TabBarBorderSize: style.TabBarBorderSize = x; break;
            case ImGuiStyleVar.TabBarOverlineSize: style.TabBarOverlineSize = x; break;
            case ImGuiStyleVar.TableAngledHeadersAngle: style.TableAngledHeadersAngle = x; break;
            case ImGuiStyleVar.TableAngledHeadersTextAlign: style.TableAngledHeadersTextAlign = new ImVec2(x, y); break;
            case ImGuiStyleVar.TreeLinesSize: style.TreeLinesSize = x; break;
            case ImGuiStyleVar.TreeLinesRounding: style.TreeLinesRounding = x; break;
            case ImGuiStyleVar.MenuItemRounding: style.MenuItemRounding = x; break;
            case ImGuiStyleVar.SelectableRounding: style.SelectableRounding = x; break;
            case ImGuiStyleVar.DragDropTargetRounding: style.DragDropTargetRounding = x; break;
            case ImGuiStyleVar.ButtonTextAlign: style.ButtonTextAlign = new ImVec2(x, y); break;
            case ImGuiStyleVar.SelectableTextAlign: style.SelectableTextAlign = new ImVec2(x, y); break;
            case ImGuiStyleVar.SeparatorSize: style.SeparatorSize = x; break;
            case ImGuiStyleVar.SeparatorTextBorderSize: style.SeparatorTextBorderSize = x; break;
            case ImGuiStyleVar.SeparatorTextAlign: style.SeparatorTextAlign = new ImVec2(x, y); break;
            case ImGuiStyleVar.SeparatorTextPadding: style.SeparatorTextPadding = new ImVec2(x, y); break;
            case ImGuiStyleVar.DockingSeparatorSize: style.DockingSeparatorSize = x; break;
            default: break;
        }
    }

    /// <summary>
    /// Pushes a scalar style variable. Mirrors canonical <c>ImGui::PushStyleVar(ImGuiStyleVar, float)</c>
    /// </summary>
    /// <param name="idx">Style variable to modify; it must hold a single float.</param>
    /// <param name="val">Replacement value.</param>
    /// <remarks>
    /// Calling the scalar overload for a two-component variable is a recoverable user error: the
    /// canonical <c>IM_ASSERT_USER_ERROR_RET</c> is realized through <see cref="ErrorLog"/> and the
    /// call returns without modifying the stack.
    /// </remarks>
    public static void PushStyleVar(ImGuiStyleVar idx, float val)
    {
        ImGuiContext g = GetContext();
        ImGuiStyleVarInfo varInfo = GetStyleVarInfo(idx);
        if (!(varInfo.DataType == ImGuiDataType.Float && varInfo.Count == 1))
        {
            AssertUserError(false, "Calling PushStyleVar() variant with wrong type!");
            return;
        }
        GetStyleVarValue(g.Style, idx, out float x, out float y);
        ImGuiStyleMod backup = new ImGuiStyleMod { VarIdx = idx };
        backup.BackupFloat[0] = x;
        backup.BackupFloat[1] = y;
        g.StyleVarStack.Add(backup);
        SetStyleVarValue(g.Style, idx, val, y);
    }

    /// <summary>
    /// Pushes a two-component style variable, replacing only its X component. Mirrors canonical
    /// <c>ImGui::PushStyleVarX</c>.
    /// </summary>
    /// <param name="idx">Style variable to modify; it must hold two floats.</param>
    /// <param name="valX">Replacement X component.</param>
    public static void PushStyleVarX(ImGuiStyleVar idx, float valX)
    {
        ImGuiContext g = GetContext();
        ImGuiStyleVarInfo varInfo = GetStyleVarInfo(idx);
        if (!(varInfo.DataType == ImGuiDataType.Float && varInfo.Count == 2))
        {
            AssertUserError(false, "Calling PushStyleVar() variant with wrong type!");
            return;
        }
        GetStyleVarValue(g.Style, idx, out float x, out float y);
        ImGuiStyleMod backup = new ImGuiStyleMod { VarIdx = idx };
        backup.BackupFloat[0] = x;
        backup.BackupFloat[1] = y;
        g.StyleVarStack.Add(backup);
        SetStyleVarValue(g.Style, idx, valX, y);
    }

    /// <summary>
    /// Pushes a two-component style variable, replacing only its Y component. Mirrors canonical
    /// <c>ImGui::PushStyleVarY</c>.
    /// </summary>
    /// <param name="idx">Style variable to modify; it must hold two floats.</param>
    /// <param name="valY">Replacement Y component.</param>
    public static void PushStyleVarY(ImGuiStyleVar idx, float valY)
    {
        ImGuiContext g = GetContext();
        ImGuiStyleVarInfo varInfo = GetStyleVarInfo(idx);
        if (!(varInfo.DataType == ImGuiDataType.Float && varInfo.Count == 2))
        {
            AssertUserError(false, "Calling PushStyleVar() variant with wrong type!");
            return;
        }
        GetStyleVarValue(g.Style, idx, out float x, out float y);
        ImGuiStyleMod backup = new ImGuiStyleMod { VarIdx = idx };
        backup.BackupFloat[0] = x;
        backup.BackupFloat[1] = y;
        g.StyleVarStack.Add(backup);
        SetStyleVarValue(g.Style, idx, x, valY);
    }

    /// <summary>
    /// Pushes a two-component style variable. Mirrors canonical
    /// <c>ImGui::PushStyleVar(ImGuiStyleVar, const ImVec2&amp;)</c>
    /// </summary>
    /// <param name="idx">Style variable to modify; it must hold two floats.</param>
    /// <param name="val">Replacement value.</param>
    public static void PushStyleVar(ImGuiStyleVar idx, ImVec2 val)
    {
        ImGuiContext g = GetContext();
        ImGuiStyleVarInfo varInfo = GetStyleVarInfo(idx);
        if (!(varInfo.DataType == ImGuiDataType.Float && varInfo.Count == 2))
        {
            AssertUserError(false, "Calling PushStyleVar() variant with wrong type!");
            return;
        }
        GetStyleVarValue(g.Style, idx, out float x, out float y);
        ImGuiStyleMod backup = new ImGuiStyleMod { VarIdx = idx };
        backup.BackupFloat[0] = x;
        backup.BackupFloat[1] = y;
        g.StyleVarStack.Add(backup);
        SetStyleVarValue(g.Style, idx, val.X, val.Y);
    }

    /// <summary>
    /// Restores style variables pushed by the <c>PushStyleVar</c> family. Mirrors canonical
    /// <c>ImGui::PopStyleVar</c>.
    /// </summary>
    /// <param name="count">Number of entries to pop.</param>
    /// <remarks>
    /// Popping more entries than were pushed is a recoverable user error: the canonical
    /// <c>IM_ASSERT_USER_ERROR</c> is realized through <see cref="ErrorLog"/> and the count is clamped
    /// to the stack size.
    /// </remarks>
    public static void PopStyleVar(int count = 1)
    {
        ImGuiContext g = GetContext();
        if (g.StyleVarStack.Count < count)
        {
            AssertUserError(false, "Calling PopStyleVar() too many times!");
            count = g.StyleVarStack.Count;
        }
        while (count > 0)
        {
            ImGuiStyleMod backup = g.StyleVarStack[g.StyleVarStack.Count - 1];
            ImGuiStyleVarInfo varInfo = GetStyleVarInfo(backup.VarIdx);
            if (varInfo.DataType == ImGuiDataType.Float && varInfo.Count == 1)
                SetStyleVarValue(g.Style, backup.VarIdx, backup.BackupFloat[0], 0.0f);
            else if (varInfo.DataType == ImGuiDataType.Float && varInfo.Count == 2)
                SetStyleVarValue(g.Style, backup.VarIdx, backup.BackupFloat[0], backup.BackupFloat[1]);
            g.StyleVarStack.RemoveAt(g.StyleVarStack.Count - 1);
            count--;
        }
    }

    /// <summary>
    /// Returns the canonical name of a style color. Mirrors canonical
    /// <c>ImGui::GetStyleColorName</c>.
    /// </summary>
    /// <param name="idx">Style color to name.</param>
    /// <returns>The color name, or <c>"Unknown"</c> for a value outside the enumeration.</returns>
    public static string GetStyleColorName(ImGuiCol idx)
    {
        switch (idx)
        {
            case ImGuiCol.Text: return "Text";
            case ImGuiCol.TextDisabled: return "TextDisabled";
            case ImGuiCol.WindowBg: return "WindowBg";
            case ImGuiCol.ChildBg: return "ChildBg";
            case ImGuiCol.PopupBg: return "PopupBg";
            case ImGuiCol.Border: return "Border";
            case ImGuiCol.BorderShadow: return "BorderShadow";
            case ImGuiCol.FrameBg: return "FrameBg";
            case ImGuiCol.FrameBgHovered: return "FrameBgHovered";
            case ImGuiCol.FrameBgActive: return "FrameBgActive";
            case ImGuiCol.TitleBg: return "TitleBg";
            case ImGuiCol.TitleBgActive: return "TitleBgActive";
            case ImGuiCol.TitleBgCollapsed: return "TitleBgCollapsed";
            case ImGuiCol.MenuBarBg: return "MenuBarBg";
            case ImGuiCol.ScrollbarBg: return "ScrollbarBg";
            case ImGuiCol.ScrollbarGrab: return "ScrollbarGrab";
            case ImGuiCol.ScrollbarGrabHovered: return "ScrollbarGrabHovered";
            case ImGuiCol.ScrollbarGrabActive: return "ScrollbarGrabActive";
            case ImGuiCol.CheckMark: return "CheckMark";
            case ImGuiCol.CheckboxSelectedBg: return "CheckboxSelectedBg";
            case ImGuiCol.SliderGrab: return "SliderGrab";
            case ImGuiCol.SliderGrabActive: return "SliderGrabActive";
            case ImGuiCol.Button: return "Button";
            case ImGuiCol.ButtonHovered: return "ButtonHovered";
            case ImGuiCol.ButtonActive: return "ButtonActive";
            case ImGuiCol.Header: return "Header";
            case ImGuiCol.HeaderHovered: return "HeaderHovered";
            case ImGuiCol.HeaderActive: return "HeaderActive";
            case ImGuiCol.Separator: return "Separator";
            case ImGuiCol.SeparatorHovered: return "SeparatorHovered";
            case ImGuiCol.SeparatorActive: return "SeparatorActive";
            case ImGuiCol.ResizeGrip: return "ResizeGrip";
            case ImGuiCol.ResizeGripHovered: return "ResizeGripHovered";
            case ImGuiCol.ResizeGripActive: return "ResizeGripActive";
            case ImGuiCol.InputTextCursor: return "InputTextCursor";
            case ImGuiCol.TabHovered: return "TabHovered";
            case ImGuiCol.Tab: return "Tab";
            case ImGuiCol.TabSelected: return "TabSelected";
            case ImGuiCol.TabSelectedOverline: return "TabSelectedOverline";
            case ImGuiCol.TabDimmed: return "TabDimmed";
            case ImGuiCol.TabDimmedSelected: return "TabDimmedSelected";
            case ImGuiCol.TabDimmedSelectedOverline: return "TabDimmedSelectedOverline";
            case ImGuiCol.DockingPreview: return "DockingPreview";
            case ImGuiCol.DockingEmptyBg: return "DockingEmptyBg";
            case ImGuiCol.PlotLines: return "PlotLines";
            case ImGuiCol.PlotLinesHovered: return "PlotLinesHovered";
            case ImGuiCol.PlotHistogram: return "PlotHistogram";
            case ImGuiCol.PlotHistogramHovered: return "PlotHistogramHovered";
            case ImGuiCol.TableHeaderBg: return "TableHeaderBg";
            case ImGuiCol.TableBorderStrong: return "TableBorderStrong";
            case ImGuiCol.TableBorderLight: return "TableBorderLight";
            case ImGuiCol.TableRowBg: return "TableRowBg";
            case ImGuiCol.TableRowBgAlt: return "TableRowBgAlt";
            case ImGuiCol.TextLink: return "TextLink";
            case ImGuiCol.TextSelectedBg: return "TextSelectedBg";
            case ImGuiCol.TreeLines: return "TreeLines";
            case ImGuiCol.DragDropTarget: return "DragDropTarget";
            case ImGuiCol.DragDropTargetBg: return "DragDropTargetBg";
            case ImGuiCol.UnsavedMarker: return "UnsavedMarker";
            case ImGuiCol.NavCursor: return "NavCursor";
            case ImGuiCol.NavWindowingHighlight: return "NavWindowingHighlight";
            case ImGuiCol.NavWindowingDimBg: return "NavWindowingDimBg";
            case ImGuiCol.ModalWindowDimBg: return "ModalWindowDimBg";
            default: break;
        }
        ImAssert.Check(false, "Invalid ImGuiCol value.");
        return "Unknown";
    }

    /// <summary>
    /// Packs a floating-point color into a 32-bit value, mirroring canonical
    /// <c>ColorConvertFloat4ToU32</c>. The
    /// implementation reuses the existing <see cref="ImColor.Pack(float, float, float, float)"/>
    /// conversion so the rounding rule stays in one place.
    /// </summary>
    /// <param name="col">Color components in the 0.1 range.</param>
    /// <returns>The packed color value.</returns>
    private static uint PackColor(ImVec4 col)
    {
        return ImColor.Pack(col.X, col.Y, col.Z, col.W);
    }

    #endregion

    #region [SECTION] FONT ATLAS / FONT STACK

    /// <summary>
    /// Returns the currently bound font. Mirrors canonical <c>ImGui::GetFont</c>
    /// </summary>
    /// <returns>The bound font, or null before the first frame.</returns>
    public static ImFont? GetFont()
    {
        return GetContext().Font;
    }

    /// <summary>
    /// Returns the baked font bound at the current size. Mirrors canonical
    /// <c>ImGui::GetFontBaked</c>.
    /// </summary>
    /// <returns>The baked font, or null when no window was current when the size was resolved.</returns>
    public static ImFontBaked? GetFontBaked()
    {
        return GetContext().FontBaked;
    }

    /// <summary>
    /// Returns the current scaled font size in pixels. Mirrors canonical <c>ImGui::GetFontSize</c>
    /// </summary>
    /// <returns>
    /// <c>style.FontSizeBase</c> multiplied by the global scale factors. The value must not be passed
    /// back to <see cref="PushFont(ImFont?, float)"/>; use <see cref="ImGuiStyle.FontSizeBase"/>.
    /// </returns>
    public static float GetFontSize()
    {
        return GetContext().FontSize;
    }

    /// <summary>
    /// Returns the texture coordinate of the font atlas's white pixel. Mirrors canonical
    /// <c>ImGui::GetFontTexUvWhitePixel</c>.
    /// </summary>
    /// <returns>The UV coordinate at the center of the atlas's reserved white texel.</returns>
    /// <remarks>
    /// <para>
    /// Canonical reads the copy published into the context's shared draw-list data
    ///., which the atlas refreshes whenever it re-derives
    /// its white texel (,
    /// <c>ImFontAtlasUpdateDrawListsSharedData</c>). This port binds a single atlas to the context
    /// through <see cref="ImGuiIO.Fonts"/> and pushes the same refresh into each registered
    /// <see cref="ImDrawList"/>, so the atlas is the single maintained source of the value; reading it
    /// directly keeps the result correct even when a lazy atlas build happens mid-frame.
    /// </para>
    /// <para>
    /// Until the first build reserves the white block the returned coordinate is
    /// <see cref="ImVec2.Zero"/>.
    /// </para>
    /// </remarks>
    public static ImVec2 GetFontTexUvWhitePixel()
    {
        return GetContext().IO.Fonts.TexUvWhitePixel;
    }

    /// <summary>
    /// Selects the font and size used at the start of a frame and pushes the initial font-stack entry.
    /// Mirrors canonical <c>ImGui::UpdateFontsNewFrame</c>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Canonical locks every atlas for the whole frame when the backend cannot consume texture updates
    /// and only then resolves the default font, which may build the atlas.
    /// This port's <see cref="ImFontAtlas.Build"/> refuses to run while
    /// <see cref="ImFontAtlas.Locked"/> is set, so the default font is resolved first and the lock is
    /// applied immediately afterwards; the end state for the frame is identical because
    /// <see cref="EndFrame"/> unlocks the atlases.
    /// </para>
    /// <para>
    /// The canonical <c>IM_ASSERT(g.Font-&gt;IsLoaded())</c> has no counterpart here: this port's
    /// <see cref="ImFont.GetFontBaked(float, float)"/> bakes glyphs on demand, so there is no separate
    /// "loaded" state to assert.
    /// </para>
    /// </remarks>
    private static void UpdateFontsNewFrame()
    {
        ImGuiContext g = GetContext();

        if (g.Style._NextFrameFontSizeBase != 0.0f)
        {
            g.Style.FontSizeBase = g.Style._NextFrameFontSizeBase;
            g.Style._NextFrameFontSizeBase = 0.0f;
        }

        // Apply default font size the first time.
        ImFont font = GetDefaultFont();
        if ((g.IO.BackendFlags & ImGuiBackendFlags.RendererHasTextures) == 0)
        {
            for (int n = 0; n < g.FontAtlases.Count; n++)
                g.FontAtlases[n].Locked = true;
        }
        if (g.Style.FontSizeBase <= 0.0f)
            g.Style.FontSizeBase = font.LegacySize > 0.0f ? font.LegacySize : FontDefaultSizeBase;

        // Set initial font.
        g.Font = font;
        g.FontSizeBase = g.Style.FontSizeBase;
        g.FontSize = 0.0f;
        ImFontStackData fontStackData = new ImFontStackData
        {
            Font = font,
            FontSizeBeforeScaling = g.Style.FontSizeBase,
            FontSizeAfterScaling = g.Style.FontSizeBase, // Will restore FontSize
        };
        SetCurrentFont(fontStackData.Font, fontStackData.FontSizeBeforeScaling, 0.0f); // But use 0.0f to enable scale
        g.FontStack.Add(fontStackData);
    }

    /// <summary>
    /// Pops the initial font-stack entry pushed by <see cref="UpdateFontsNewFrame"/>. Mirrors canonical
    /// <c>ImGui::UpdateFontsEndFrame</c>.
    /// </summary>
    private static void UpdateFontsEndFrame()
    {
        PopFont();
    }

    /// <summary>
    /// Returns the font used at the start of a frame, building the atlas if it has not been built yet.
    /// Mirrors canonical <c>ImGui::GetDefaultFont</c>.
    /// </summary>
    /// <returns><see cref="ImGuiIO.FontDefault"/> when set, otherwise the atlas's first font.</returns>
    /// <remarks>
    /// The canonical condition is <c>atlas-&gt;Builder == NULL || atlas-&gt;Fonts.Size == 0</c>. This
    /// port has no builder pointer, so an atlas that has not produced a texture yet
    /// (<see cref="ImFontAtlas.TexIsBuilt"/>) or that holds no fonts is built through
    /// <see cref="ImFontAtlas.Build"/>.
    /// </remarks>
    internal static ImFont GetDefaultFont()
    {
        ImGuiContext g = GetContext();
        ImFontAtlas atlas = g.IO.Fonts;
        if (!atlas.TexIsBuilt || atlas.Fonts.Count == 0)
            atlas.Build();
        return g.IO.FontDefault ?? atlas.Fonts[0];
    }

    /// <summary>
    /// Binds a font at a given size and refreshes the derived size state. Mirrors canonical
    /// <c>ImGui::SetCurrentFont</c>.
    /// </summary>
    /// <param name="font">Font to bind, or null to clear the binding.</param>
    /// <param name="fontSizeBeforeScaling">Base size before global scale factors are applied.</param>
    /// <param name="fontSizeAfterScaling">Already-scaled size to restore, or 0 to recompute from the scale factors.</param>
    /// <remarks>
    /// Canonical additionally stores the atlas in the shared draw-list data, refreshes every registered
    /// draw list's atlas tables, and re-binds the texture on the current window's draw list. This port's
    /// <see cref="ImDrawList"/> is constructed with its atlas and owns that binding, so only the shared
    /// font pointer is refreshed here.
    /// </remarks>
    internal static void SetCurrentFont(ImFont? font, float fontSizeBeforeScaling, float fontSizeAfterScaling)
    {
        ImGuiContext g = GetContext();
        g.Font = font;
        g.FontSizeBase = fontSizeBeforeScaling;
        UpdateCurrentFontSize(fontSizeAfterScaling);

        if (font != null)
        {
            ImAssert.Check(font.OwnerAtlas != null, "Font atlas not created.");
            g.DrawListSharedData.Font = font;
        }
    }

    /// <summary>
    /// Recomputes the bound font size from the base size and the global, window and legacy scale
    /// factors, then re-bakes the font. Mirrors canonical <c>ImGui::UpdateCurrentFontSize</c>
    /// </summary>
    /// <param name="restoreFontSizeAfterScaling">
    /// Already-scaled size restored by <see cref="PopFont"/>, or 0 to recompute the size from the scale
    /// factors.
    /// </param>
    /// <remarks>
    /// The canonical table exemption
    /// (<c>table-&gt;CurrentColumn != -1 &amp;&amp; !Columns[.].IsSkipItems</c>) is owned by chunk 5;
    /// while <c>ImGuiContext.CurrentTable</c> is absent the collapsed-window early-out always
    /// applies. Canonical also publishes <c>DrawListSharedData.FontScale</c>; this port's
    /// <see cref="ImDrawListSharedData"/> does not declare that field.
    /// </remarks>
    internal static void UpdateCurrentFontSize(float restoreFontSizeAfterScaling)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow? window = g.CurrentWindow;

        g.Style.FontSizeBase = g.FontSizeBase;

        // Restoring is pretty much only used by PopFont().
        float finalSize = restoreFontSizeAfterScaling > 0.0f ? restoreFontSizeAfterScaling : 0.0f;
        if (finalSize == 0.0f)
        {
            finalSize = g.FontSizeBase;
            finalSize *= g.Style.FontScaleMain;
            finalSize *= g.Style.FontScaleDpi;
            if (window != null)
                finalSize *= window.FontWindowScale;
            finalSize *= g.IO.FontGlobalScale; // Obsolete; use style.FontScaleMain instead.
            if (g.Font != null)
                finalSize *= g.Font.Scale;
        }

        // Round font size: canonical GetRoundedFontSize() is IM_ROUND(), i.e. floor(x + 0.5f)
        finalSize = MathF.Floor(finalSize + 0.5f);
        finalSize = Math.Clamp(finalSize, 1.0f, FontSizeMax);
        if (g.Font != null && (g.IO.BackendFlags & ImGuiBackendFlags.RendererHasTextures) != 0)
            g.Font.CurrentRasterizerDensity = g.FontRasterizerDensity;

        g.FontSize = finalSize;
        g.DrawListSharedData.FontSize = g.FontSize;

        // Early out to avoid a hidden window keeping bakes referenced and out of garbage-collection
        // reach.
        if (window != null && window.SkipItems)
            return;

        g.FontBaked = (g.Font != null && window != null) ? g.Font.GetFontBaked(finalSize) : null;
        g.FontBakedScale = g.FontBaked != null ? g.FontSize / g.FontBaked.Size : 0.0f;
    }

    /// <summary>
    /// Overrides the font rasterizer / pixel density used for subsequent dynamic font bakes. Mirrors canonical
    /// <c>ImGui::SetPixelDensity</c>.
    /// </summary>
    /// <param name="pixelDensity">The density multiplier applied when rasterizing glyphs.</param>
    /// <remarks>
    /// <para>
    /// The density is exposed so an application can override the value the frame pipeline would
    /// otherwise derive from the viewport framebuffer scale. Setting it re-derives the current font
    /// size (<see cref="UpdateCurrentFontSize"/>) so the bound baked font reflects the new density
    /// immediately. Assigning the value already in effect is a no-op.
    /// </para>
    /// <para>
    /// The canonical renderer-capability assertion is preserved: the density only has meaning when the
    /// backend consumes texture data and can therefore respond to a density change.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    public static void SetPixelDensity(float pixelDensity)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((g.IO.BackendFlags & ImGuiBackendFlags.RendererHasTextures) != 0,
            "SetPixelDensity() requires a backend with the RendererHasTextures flag.");
        if (g.CurrentPixelDensity == pixelDensity)
            return;
        g.CurrentPixelDensity = pixelDensity;
        UpdateCurrentFontSize(0.0f);
    }

    /// <summary>
    /// Obsolete alias for <see cref="SetPixelDensity"/>.
    /// </summary>
    [Obsolete("Use SetPixelDensity instead.")]
    public static void SetFontRasterizerDensity(float rasterizerDensity)
    {
        SetPixelDensity(rasterizerDensity);
    }

    /// <summary>
    /// Pushes a font and a base size, or changes only the size when the font is null. Mirrors canonical
    /// <c>ImGui::PushFont</c>.
    /// </summary>
    /// <param name="font">Font to bind, or null to keep the current font.</param>
    /// <param name="fontSizeBase">
    /// Base size before global scale factors, or 0 to keep the current base size.
    /// </param>
    /// <remarks>
    /// The value passed here is unscaled. <see cref="GetFontSize"/> returns the scaled size and must not
    /// be fed back into this method.
    /// </remarks>
    public static void PushFont(ImFont? font, float fontSizeBase = 0.0f)
    {
        ImGuiContext g = GetContext();
        if (font == null) // Historically, PushFont(NULL) was equivalent to PushFont(GetDefaultFont()).
            font = g.Font;
        ImAssert.Check(font != null, "PushFont() requires a font when none is bound yet.");
        ImAssert.Check(fontSizeBase >= 0.0f, "Font size base must not be negative.");

        g.FontStack.Add(new ImFontStackData
        {
            Font = g.Font,
            FontSizeBeforeScaling = g.FontSizeBase,
            FontSizeAfterScaling = g.FontSize,
        });
        if (fontSizeBase == 0.0f)
            fontSizeBase = g.FontSizeBase; // Keep current font size
        SetCurrentFont(font, fontSizeBase, 0.0f);
    }

    /// <summary>
    /// Restores the font binding saved by <see cref="PushFont(ImFont?, float)"/>. Mirrors canonical
    /// <c>ImGui::PopFont</c>.
    /// </summary>
    /// <remarks>
    /// Popping an empty font stack is a recoverable user error: the canonical
    /// <c>IM_ASSERT_USER_ERROR_RET</c> is realized through <see cref="ErrorLog"/> and the call returns.
    /// </remarks>
    public static void PopFont()
    {
        ImGuiContext g = GetContext();
        if (g.FontStack.Count <= 0)
        {
            AssertUserError(false, "Calling PopFont() too many times!");
            return;
        }
        ImFontStackData fontStackData = g.FontStack[g.FontStack.Count - 1];
        SetCurrentFont(fontStackData.Font, fontStackData.FontSizeBeforeScaling, fontStackData.FontSizeAfterScaling);
        g.FontStack.RemoveAt(g.FontStack.Count - 1);
    }

    #endregion

    #region [SECTION] CONTEXT AND USER HOOKS

    /// <summary>
    /// Registers a context hook and returns its identifier. Mirrors canonical
    /// <c>ImGui::AddContextHook</c>.
    /// </summary>
    /// <param name="ctx">Context that owns the hook.</param>
    /// <param name="hook">
    /// Hook to register. Its <see cref="ImGuiContextHook.Callback"/> must be set,
    /// <see cref="ImGuiContextHook.HookId"/> must be zero, and
    /// <see cref="ImGuiContextHook.Type"/> must not be <see cref="ImGuiContextHookType.PendingRemoval_"/>.
    /// </param>
    /// <returns>The assigned hook identifier.</returns>
    /// <remarks>
    /// Canonical returns <c>ImGuiID</c> internally; this port
    /// returns the identifier as <see cref="uint"/>. <c>CONTRACT.md</c> §5 specifies a <c>void</c> return
    /// for this member, which contradicts canonical <c>AddContextHook</c>;
    /// the canonical identifier return is retained and the
    /// discrepancy is reported to the caller. There is no ordering or dependency support between hooks.
    /// </remarks>
    public static uint AddContextHook(ImGuiContext ctx, ImGuiContextHook hook)
    {
        ImAssert.Check(hook.Callback != null, "A context hook requires a callback.");
        ImAssert.Check(hook.HookId == 0, "A context hook must be registered with HookId zero.");
        ImAssert.Check(hook.Type != ImGuiContextHookType.PendingRemoval_, "Cannot register a pending-removal hook.");
        ctx.Hooks.Add(hook);
        ctx.Hooks[ctx.Hooks.Count - 1].HookId = ++ctx.HookIdNext;
        return ctx.HookIdNext;
    }

    /// <summary>
    /// Marks a context hook for deferred removal. Mirrors canonical
    /// <c>ImGui::RemoveContextHook</c>.
    /// </summary>
    /// <param name="ctx">Context that owns the hook.</param>
    /// <param name="hookToRemove">Identifier returned by <see cref="AddContextHook"/>.</param>
    /// <remarks>
    /// Removal is deferred so the hook list is not mutated while it is being iterated; the entry is
    /// erased at the top of the next <see cref="NewFrame"/>.
    /// </remarks>
    public static void RemoveContextHook(ImGuiContext ctx, uint hookToRemove)
    {
        ImAssert.Check(hookToRemove != 0, "A hook identifier of zero is never valid.");
        for (int n = 0; n < ctx.Hooks.Count; n++)
        {
            if (ctx.Hooks[n].HookId == hookToRemove)
                ctx.Hooks[n].Type = ImGuiContextHookType.PendingRemoval_;
        }
    }

    /// <summary>
    /// Invokes every hook registered for one lifecycle event. Mirrors canonical
    /// <c>ImGui::CallContextHooks</c>.
    /// </summary>
    /// <param name="ctx">Context whose hooks run.</param>
    /// <param name="type">Lifecycle event being dispatched.</param>
    /// <remarks>
    /// The canonical implementation assumes a small number of hooks and iterates the single array
    /// without sorting, which is reproduced here. Hooks added or removed while the list is iterated
    /// take effect on the next dispatch because removal is deferred.
    /// </remarks>
    public static void CallContextHooks(ImGuiContext ctx, ImGuiContextHookType type)
    {
        for (int n = 0; n < ctx.Hooks.Count; n++)
        {
            ImGuiContextHook hook = ctx.Hooks[n];
            if (hook.Type == type)
                hook.Callback?.Invoke(ctx, hook);
        }
    }

    #endregion

    #region [SECTION] ERROR HANDLING

    /// <summary>
    /// Reports a recoverable user error and returns whether the caller should assert. Mirrors
    /// canonical <c>ImGui::ErrorLog</c>.
    /// </summary>
    /// <param name="message">Error text describing the misuse.</param>
    /// <returns>
    /// True when <see cref="ImGuiIO.ConfigErrorRecoveryEnableAssert"/> requests an assertion.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The message is written to the debug log when
    /// <see cref="ImGuiIO.ConfigErrorRecoveryEnableDebugLog"/> is set, and into the error tooltip when
    /// <see cref="ImGuiIO.ConfigErrorRecoveryEnableTooltip"/> is set. The first error of a frame is
    /// preceded by a banner that records the three recovery settings, so a maintainer reading the log can
    /// tell what the caller had requested. The error callback is notified regardless of those settings.
    /// </para>
    /// <para>
    /// Tooltip output is limited to the first twenty errors of a frame; the twenty-first appends a single
    /// "(and more errors)" line. This bound exists because a single misuse inside a loop can otherwise
    /// produce thousands of lines in one frame.
    /// </para>
    /// <para>
    /// The tooltip is only submitted while a frame is in scope, because the tooltip window needs the frame
    /// pipeline to be running.
    /// </para>
    /// <para>
    /// Canonical returns <c>bool</c>., which the
    /// <c>IM_ASSERT_USER_ERROR</c> family relies on to decide whether to assert after a recoverable error.
    /// <c>CONTRACT.md</c> §5 specifies a <c>void</c> return for this member, which contradicts canonical;
    /// the canonical <c>bool</c> return is retained so <see cref="AssertUserError"/> matches the canonical
    /// macro expansion, and the discrepancy is reported to the caller. The member is public per §5.
    /// </para>
    /// </remarks>
    public static bool ErrorLog(string message)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow? window = g.CurrentWindow;

        if (g.IO.ConfigErrorRecoveryEnableDebugLog)
        {
            if (g.ErrorFirst)
            {
                DebugLogError(
                    "[imgui-error] (current settings: Assert={0}, Log={1}, Tooltip={2})",
                    g.IO.ConfigErrorRecoveryEnableAssert,
                    g.IO.ConfigErrorRecoveryEnableDebugLog,
                    g.IO.ConfigErrorRecoveryEnableTooltip);
            }
            DebugLogError("[imgui-error] In window '{0}': {1}", window != null ? window.Name : "NULL", message);
        }
        g.ErrorFirst = false;

        if (g.IO.ConfigErrorRecoveryEnableTooltip)
        {
            if (g.WithinFrameScope && BeginErrorTooltip())
            {
                if (g.ErrorCountCurrentFrame < 20)
                {
                    Text("In window '{0}': {1}", window != null ? window.Name : "NULL", message);
                    // Outline the offending window so the maintainer can see which one reported the error.
                    // The implicit fallback window has no visible rectangle of its own until it is used.
                    if (window != null && (!window.IsFallbackWindow || window.WasActive))
                        GetForegroundDrawList(window).AddRect(window.Pos, window.Pos + window.Size, 0xFF0000FF);
                }
                if (g.ErrorCountCurrentFrame == 20)
                    Text("(and more errors)");
                // EndFrame() amends debug buttons to this window once all errors have been submitted.
                EndErrorTooltip();
            }
            g.ErrorCountCurrentFrame++;
        }

        if (g.ErrorCallback != null)
            g.ErrorCallback(g, g.ErrorCallbackUserData, message);

        // Return whether we should assert.
        return g.IO.ConfigErrorRecoveryEnableAssert;
    }

    /// <summary>
    /// Opens the pseudo-tooltip window that surfaces recoverable user errors.
    /// </summary>
    /// <returns>Whether the tooltip window is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// <para>
    /// The tooltip follows the mouse until Ctrl is held, at which point it locks to
    /// <see cref="ImGuiContext.ErrorTooltipLockedPos"/> so its buttons can be clicked. The popup background
    /// colour is pushed toward red before the window is submitted, which is what distinguishes this
    /// pseudo-tooltip from an ordinary one.
    /// </para>
    /// <para>
    /// On the first begin of the frame the header, display front, and focus front are applied and the
    /// locked position is refreshed; when the window is not visible the caller's matching <c>End()</c> is
    /// issued here so callers never need to know whether the window opened.
    /// </para>
    /// </remarks>
    internal static bool BeginErrorTooltip()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow? window = FindWindowByName("##Tooltip_Error");
        bool useLockedPos = g.IO.KeyCtrl && window != null && window.WasActive;
        PushStyleColor(
            ImGuiCol.PopupBg,
            LerpColor(GetStyleColorVec4(ImGuiCol.PopupBg), new ImVec4(1.0f, 0.0f, 0.0f, 1.0f), 0.15f));
        if (useLockedPos)
            SetNextWindowPos(g.ErrorTooltipLockedPos);
        bool isVisible = Begin(
            "##Tooltip_Error",
            ImGuiWindowFlags.Tooltip
            | ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.AlwaysAutoResize);
        PopStyleColor();
        if (isVisible && g.CurrentWindow!.BeginCount == 1)
        {
            SeparatorText("MESSAGE FROM DEAR IMGUI");
            BringWindowToDisplayFront(g.CurrentWindow!);
            BringWindowToFocusFront(g.CurrentWindow!);
            g.ErrorTooltipLockedPos = GetWindowPos();
        }
        else if (!isVisible)
        {
            End();
        }
        return isVisible;
    }

    /// <summary>
    /// Closes the pseudo-tooltip window opened by <see cref="BeginErrorTooltip"/>.
    /// </summary>
    internal static void EndErrorTooltip() => End();

    /// <summary>
    /// Realizes the canonical <c>IM_ASSERT_USER_ERROR</c> macro: reports a recoverable user error and
    /// asserts only when <see cref="ErrorLog"/> requests it. Mirrors
    /// </summary>
    /// <param name="condition">Condition that should hold; a false value is reported.</param>
    /// <param name="message">Error text describing the misuse.</param>
    /// <remarks>
    /// Internal rather than private because the canonical macro is also expanded by types outside this
    /// class, notably <see cref="ImDrawList.AddRect"/> and <see cref="ImDrawList.AddPolyline"/>.
    /// </remarks>
    internal static void AssertUserError(bool condition, string message)
    {
        if (!condition && ErrorLog(message))
            ImAssert.Check(condition, message);
    }

    /// <summary>
    /// Detects code that uses <c>SetCursorPos()</c> or <c>SetCursorScreenPos()</c> to extend a parent's
    /// boundaries and reports it as a user error. Mirrors canonical
    /// <c>ImGui::ErrorCheckUsingSetCursorPosToExtendParentBoundaries</c>
    /// </summary>
    /// <remarks>
    /// No caller expects <c>SetXXX(GetXXX())</c> to have a side effect, so the cursor position is not
    /// folded into <see cref="ImGuiWindowTempData.CursorMaxPos"/>; the caller must submit an item, for
    /// example <c>Dummy()</c>, to grow the boundaries.
    /// </remarks>
    internal static void ErrorCheckUsingSetCursorPosToExtendParentBoundaries()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow? window = g.CurrentWindow;
        ImAssert.Check(window != null, "A window must be current when checking cursor-position usage.");
        if (window == null)
            return;
        ImAssert.Check(window.DC.IsSetPos);
        window.DC.IsSetPos = false;
        if (window.DC.CursorPos.X <= window.DC.CursorMaxPos.X && window.DC.CursorPos.Y <= window.DC.CursorMaxPos.Y)
            return;
        if (window.SkipItems)
            return;
        AssertUserError(false,
            "Code uses SetCursorPos()/SetCursorScreenPos() to extend window/parent boundaries.\n"
            + "Please submit an item e.g. Dummy() afterwards in order to grow window/parent boundaries.");
    }

    /// <summary>
    /// Validates the input/output and style state at the start of a frame. Mirrors canonical
    /// <c>ImGui::ErrorCheckNewFrameSanityChecks</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Canonical opens with an <c>if (true) IM_ASSERT(1); else IM_ASSERT(0);</c> idiom
    /// that detects a host assert macro which is unsafe inside control-flow
    /// blocks. <see cref="ImAssert.Check(bool, string)"/> has no comparable misuse mode, so that
    /// self-check is omitted; every canonical assertion below is still evaluated in the same order.
    /// </para>
    /// <para>
    /// Canonical also tolerates a zero delta time on Emscripten; this port is not
    /// compiled for Emscripten and omits that branch.
    /// </para>
    /// <para>
    /// The legacy <c>NavEnableSetMousePos</c> flag does not exist in this port's
    /// <see cref="ImGuiConfigFlags"/>, so that remap is inert. The legacy clipboard handlers are not
    /// copied into <see cref="ImGuiPlatformIO"/> because the clipboard accessors read the platform
    /// handler first and fall back to the obsolete input/output callback, which is equivalent. The
    /// canonical recovery that clears <see cref="ImGuiConfigFlags.ViewportsEnable"/> when the backend
    /// cannot provide viewports, and the handler and monitor assertions that guard the multi-viewport
    /// path, are reproduced.
    /// </para>
    /// </remarks>
    private static void ErrorCheckNewFrameSanityChecks()
    {
        ImGuiContext g = GetContext();

        // Check user data.
        ImAssert.Check(g.Initialized, "Context is not initialized.");
        ImAssert.Check(g.IO.DeltaTime > 0.0f || g.FrameCount == 0, "Need a positive DeltaTime!");
        ImAssert.Check(g.FrameCount == 0 || g.FrameCountEnded == g.FrameCount,
            "Forgot to call Render() or EndFrame() at the end of the previous frame?");
        ImAssert.Check(g.IO.DisplaySize.X >= 0.0f && g.IO.DisplaySize.Y >= 0.0f, "Invalid DisplaySize value!");
        ImAssert.Check(g.Style.CurveTessellationTol > 0.0f, "Invalid style setting!");
        ImAssert.Check(g.Style.CurveTessellationMaxError > 0.0f, "Invalid style setting!");
        ImAssert.Check(g.Style.Alpha >= 0.0f && g.Style.Alpha <= 1.0f, "Invalid style setting!");
        ImAssert.Check(g.Style.WindowMinSize.X >= 1.0f && g.Style.WindowMinSize.Y >= 1.0f, "Invalid style setting!");
        ImAssert.Check(g.Style.WindowBorderHoverPadding > 0.0f, "Invalid style setting!");
        ImAssert.Check(
            g.Style.WindowMenuButtonPosition == ImGuiDir.None
            || g.Style.WindowMenuButtonPosition == ImGuiDir.Left
            || g.Style.WindowMenuButtonPosition == ImGuiDir.Right,
            "Invalid style setting!");
        ImAssert.Check(
            g.Style.ColorButtonPosition == ImGuiDir.Left || g.Style.ColorButtonPosition == ImGuiDir.Right,
            "Invalid style setting!");

        // Check that only one option is selected in each config color mask.
        int displayMask = (int)(g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.DisplayMask_);
        int dataTypeMask = (int)(g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.DataTypeMask_);
        int pickerMask = (int)(g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.PickerMask_);
        int inputMask = (int)(g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.InputMask_);
        ImAssert.Check(displayMask != 0 && (displayMask & (displayMask - 1)) == 0, "Invalid ConfigColorEditFlags.");
        ImAssert.Check(dataTypeMask != 0 && (dataTypeMask & (dataTypeMask - 1)) == 0, "Invalid ConfigColorEditFlags.");
        ImAssert.Check(pickerMask != 0 && (pickerMask & (pickerMask - 1)) == 0, "Invalid ConfigColorEditFlags.");
        ImAssert.Check(inputMask != 0 && (inputMask & (inputMask - 1)) == 0, "Invalid ConfigColorEditFlags.");

        ImAssert.Check(
            g.Style.TreeLinesFlags == ImGuiTreeNodeFlags.DrawLinesNone
            || g.Style.TreeLinesFlags == ImGuiTreeNodeFlags.DrawLinesFull
            || g.Style.TreeLinesFlags == ImGuiTreeNodeFlags.DrawLinesToNodes,
            "Invalid style setting!");
        ImAssert.Check(g.IO.MouseSingleClickDelay > g.IO.MouseDoubleClickTime, "Invalid input setting!");

        // Error handling: a fully silent recovery is not accepted.
        if (g.IO.ConfigErrorRecovery)
        {
            ImAssert.Check(
                g.IO.ConfigErrorRecoveryEnableAssert
                || g.IO.ConfigErrorRecoveryEnableDebugLog
                || g.IO.ConfigErrorRecoveryEnableTooltip
                || g.ErrorCallback != null,
                "Error recovery requires at least one output.");
        }

        // Remap legacy names.
        if (g.IO.FontGlobalScale > 1.0f)
            ImAssert.Check(g.Style.FontScaleMain == 1.0f, "Since 1.92: use style.FontScaleMain instead of g.IO.FontGlobalScale!");
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.NavNoCaptureKeyboard) != 0)
        {
            g.IO.ConfigNavCaptureKeyboard = false;
            g.IO.ConfigFlags &= ~ImGuiConfigFlags.NavNoCaptureKeyboard;
        }
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.DpiEnableScaleFonts) != 0)
        {
            g.IO.ConfigDpiScaleFonts = true;
            g.IO.ConfigFlags &= ~ImGuiConfigFlags.DpiEnableScaleFonts;
        }
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.DpiEnableScaleViewports) != 0)
        {
            g.IO.ConfigDpiScaleViewports = true;
            g.IO.ConfigFlags &= ~ImGuiConfigFlags.DpiEnableScaleViewports;
        }

        // Docking and Viewports must not be enabled exactly on frame 1
        if (g.FrameCount == 1
            && (g.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0
            && (g.ConfigFlagsLastFrame & ImGuiConfigFlags.DockingEnable) == 0)
        {
            ImAssert.Check(false, "Please set DockingEnable before the first call to NewFrame()! Otherwise you will lose your .ini settings!");
        }
        if (g.FrameCount == 1
            && (g.IO.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0
            && (g.ConfigFlagsLastFrame & ImGuiConfigFlags.ViewportsEnable) == 0)
        {
            ImAssert.Check(false, "Please set ViewportsEnable before the first call to NewFrame()! Otherwise you will lose your .ini settings!");
        }

        // Multi-viewport support checks. The legacy clipboard
        // handlers are not copied into PlatformIO because GetClipboardText/SetClipboardText read the
        // platform handler first and fall back to the obsolete IO callback, which is equivalent.
        if ((g.IO.ConfigFlags & ImGuiConfigFlags.ViewportsEnable) != 0)
        {
            bool platformHasViewports = (g.IO.BackendFlags & ImGuiBackendFlags.PlatformHasViewports) != 0;
            bool rendererHasViewports = (g.IO.BackendFlags & ImGuiBackendFlags.RendererHasViewports) != 0;
            if (platformHasViewports && rendererHasViewports)
            {
                ImAssert.Check(g.FrameCount == 0 || g.FrameCount == g.FrameCountPlatformEnded,
                    "Forgot to call UpdatePlatformWindows() in main loop after EndFrame()?");
                ImAssert.Check(g.PlatformIO.Platform_CreateWindow != null, "Platform init didn't install handlers?");
                ImAssert.Check(g.PlatformIO.Platform_DestroyWindow != null, "Platform init didn't install handlers?");
                ImAssert.Check(g.PlatformIO.Platform_GetWindowPos != null, "Platform init didn't install handlers?");
                ImAssert.Check(g.PlatformIO.Platform_SetWindowPos != null, "Platform init didn't install handlers?");
                ImAssert.Check(g.PlatformIO.Platform_GetWindowSize != null, "Platform init didn't install handlers?");
                ImAssert.Check(g.PlatformIO.Platform_SetWindowSize != null, "Platform init didn't install handlers?");
                ImAssert.Check(g.PlatformIO.Monitors.Count > 0, "Platform init didn't setup the monitor list?");
                ImAssert.Check(g.Viewports[0].PlatformUserData != IntPtr.Zero || g.Viewports[0].PlatformHandle != IntPtr.Zero,
                    "Platform init didn't setup the main viewport.");
                if (g.IO.ConfigDockingTransparentPayload && (g.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0)
                    ImAssert.Check(g.PlatformIO.Platform_SetWindowAlpha != null,
                        "Platform_SetWindowAlpha is required to use io.ConfigDockingTransparentPayload.");
            }
            else
            {
                g.IO.ConfigFlags &= ~ImGuiConfigFlags.ViewportsEnable; // Disable feature, our backends do not support it
            }

            // Validate the monitor rectangles and DPI scales supplied by the application or backend.
            for (int n = 0; n < g.PlatformIO.Monitors.Count; n++)
            {
                ImGuiPlatformMonitor monitor = g.PlatformIO.Monitors[n];
                ImAssert.Check(monitor.MainSize.X > 0.0f && monitor.MainSize.Y > 0.0f,
                    "Monitor main bounds not setup properly.");
                ImAssert.Check(new ImRect(monitor.MainPos, monitor.MainPos + monitor.MainSize)
                        .Contains(new ImRect(monitor.WorkPos, monitor.WorkPos + monitor.WorkSize)),
                    "Monitor work bounds not setup properly.");
                ImAssert.Check(monitor.DpiScale > 0.0f && monitor.DpiScale < 99.0f, "Monitor DpiScale is invalid.");
            }
        }
    }

    /// <summary>
    /// Validates the key-modifier and window-stack state at the end of a frame. Mirrors canonical
    /// <c>ImGui::ErrorCheckEndFrameSanityChecks</c>.
    /// </summary>
    /// <remarks>
    /// A backend that releases every modifier mid-frame is tolerated because
    /// <c>key_mods == 0</c> is exempt; mid-frame key presses still assert.
    /// </remarks>
    private static void ErrorCheckEndFrameSanityChecks()
    {
        ImGuiContext g = GetContext();

        // Verify that io.KeyXXX fields have not been tampered with between NewFrame() and EndFrame().
        ImGuiKey keyMods = GetMergedModsFromKeys();
        ImAssert.Check(
            keyMods == ImGuiKey.None || g.IO.KeyMods == keyMods,
            "Mismatching io.KeyCtrl/io.KeyShift/io.KeyAlt/io.KeySuper vs io.KeyMods");

        ImAssert.Check(g.CurrentWindowStack.Count == 1, "Unbalanced Begin()/End() calls.");
        if (g.CurrentWindowStack.Count > 0)
        {
            ImGuiWindow? fallback = g.CurrentWindowStack[0].Window;
            ImAssert.Check(fallback != null && fallback.IsFallbackWindow, "Implicit fallback window is missing.");
        }
    }

    /// <summary>
    /// Finalizes the duplicate-identifier error tooltip at the end of a frame. Mirrors the state
    /// bookkeeping of canonical <c>ImGui::ErrorCheckEndFrameFinalizeErrorTooltip</c>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent reports share the pseudo-tooltip. The first is the duplicate-identifier conflict
    /// report, which latches the conflict count while Ctrl is not held and offers the item picker when
    /// <see cref="ImGuiIO.ConfigDebugHighlightIdConflictsShowItemPicker"/> allows it. The second amends a
    /// tooltip that is already open because errors were reported this frame, adding the button that turns
    /// assertions on.
    /// </para>
    /// <para>
    /// The conflict count is latched rather than read live so that holding Ctrl freezes the number while
    /// the maintainer clicks through the offered actions.
    /// </para>
    /// </remarks>
    internal static void ErrorCheckEndFrameFinalizeErrorTooltip()
    {
        ImGuiContext g = GetContext();
        if (g.DebugDrawIdConflictsId != 0 && !g.IO.KeyCtrl)
            g.DebugDrawIdConflictsCount = g.HoveredIdPreviousFrameItemCount;

        if (g.DebugDrawIdConflictsId != 0 && !g.DebugItemPickerActive && BeginErrorTooltip())
        {
            Text("Programmer error: {0} visible items with conflicting ID!", g.DebugDrawIdConflictsCount);
            BulletText("Code should use PushID()/PopID() in loops, or append \"##xx\" to same-label identifiers!");
            BulletText("Empty label e.g. Button(\"\") == same ID as parent widget/node. Use Button(\"##xx\") instead!");
            BulletText("Set io.ConfigDebugHighlightIdConflicts=false to disable this warning in non-programmers builds.");
            Separator();
            if (g.IO.ConfigDebugHighlightIdConflictsShowItemPicker)
            {
                Text("(Hold Ctrl to: use ");
                SameLine(0.0f, 0.0f);
                if (SmallButton("Item Picker"))
                    DebugStartItemPicker();
                SameLine(0.0f, 0.0f);
                Text(" to break in item call-stack, or ");
            }
            else
            {
                Text("(Hold Ctrl to: ");
            }
            SameLine(0.0f, 0.0f);
            TextLinkOpenURL("read FAQ \"About ID Stack System\"", "https://github.com/ocornut/imgui/blob/master/docs/FAQ.md#qa-usage");
            SameLine(0.0f, 0.0f);
            Text(")");
            EndErrorTooltip();
        }

        if (g.ErrorCountCurrentFrame > 0 && BeginErrorTooltip()) // Amend at end of frame
        {
            Separator();
            Text("(Hold Ctrl to: ");
            SameLine(0.0f, 0.0f);
            if (SmallButton("Enable Asserts"))
                g.IO.ConfigErrorRecoveryEnableAssert = true;
            SameLine(0.0f, 0.0f);
            Text(")");
            EndErrorTooltip();
        }
    }

    /// <summary>
    /// Records the current stack sizes so a later frame can unwind a mismatched
    /// <c>Begin</c>/<c>End</c> or <c>Push</c>/<c>Pop</c> sequence. Mirrors canonical
    /// <c>ImGui::ErrorRecoveryStoreState</c>.
    /// </summary>
    /// <param name="stateOut">Record that receives the stack sizes.</param>
    /// <remarks>
    /// <see cref="ImGuiErrorRecoveryState.SizeOfTreeStack"/> records the current window's
    /// <see cref="ImGuiWindowTempData.TreeDepth"/>, not the partial tree-node stack size, exactly as
    /// canonical does.
    /// </remarks>
    public static void ErrorRecoveryStoreState(ImGuiErrorRecoveryState stateOut)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow? window = g.CurrentWindow;
        ImAssert.Check(window != null, "A window must be current when storing the recovery state.");
        stateOut.SizeOfWindowStack = (short)g.CurrentWindowStack.Count;
        stateOut.SizeOfIDStack = (short)(window != null ? window.IDStack.Count : 0);
        stateOut.SizeOfTreeStack = (short)(window != null ? window.DC.TreeDepth : 0);
        stateOut.SizeOfColorStack = (short)g.ColorStack.Count;
        stateOut.SizeOfStyleVarStack = (short)g.StyleVarStack.Count;
        stateOut.SizeOfFontStack = (short)g.FontStack.Count;
        stateOut.SizeOfFocusScopeStack = (short)g.FocusScopeStack.Count;
        stateOut.SizeOfGroupStack = (short)g.GroupStack.Count;
        stateOut.SizeOfItemFlagsStack = (short)g.ItemFlagsStack.Count;
        stateOut.SizeOfBeginPopupStack = (short)g.BeginPopupStack.Count;
        stateOut.SizeOfDisabledStack = g.DisabledStackSize;
    }

    /// <summary>
    /// Unwinds the window stack down to a recorded size and then recovers the innermost window state.
    /// Mirrors canonical <c>ImGui::ErrorRecoveryTryToRecoverState</c>
    /// </summary>
    /// <param name="state">Record produced by <see cref="ErrorRecoveryStoreState"/>.</param>
    /// <remarks>
    /// <para>
    /// Begin and BeginChild always require a matching End or EndChild even when they return false, so the
    /// unwind loop dispatches on the current window's flags rather than on a return value.
    /// </para>
    /// <para>
    /// A table is begun inside a child window that the table itself owns, so an unfinished table must be
    /// ended through <see cref="EndTable"/> instead of ending its child window directly; ending the child
    /// would leave the table's own state on the context. The remaining canonical branches call
    /// <see cref="EndChild"/> and <see cref="End"/>.
    /// </para>
    /// <para>
    /// <c>CONTRACT.md</c> §5 specifies a <c>bool</c> return for this member and a <c>public</c>
    /// accessibility; canonical returns <c>void</c>., so the
    /// canonical <c>void</c> shape is retained and the return-type discrepancy is reported to the caller.
    /// </para>
    /// </remarks>
    public static void ErrorRecoveryTryToRecoverState(ImGuiErrorRecoveryState state)
    {
        ImGuiContext g = GetContext();
        while (g.CurrentWindowStack.Count > state.SizeOfWindowStack)
        {
            ImGuiWindow? window = g.CurrentWindow;
            if (window == null)
                break;
            if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
            {
                if (g.CurrentTable != null && g.CurrentTable.InnerWindow == g.CurrentWindow)
                {
                    AssertUserError(false, "Missing EndTable()");
                    EndTable();
                }
                else
                {
                    AssertUserError(false, "Missing EndChild()");
                    EndChild();
                }
            }
            else
            {
                AssertUserError(false, "Missing End()");
                End();
            }
        }
        if (g.CurrentWindowStack.Count == state.SizeOfWindowStack)
            ErrorRecoveryTryToRecoverWindowState(state);
    }

    /// <summary>
    /// Unwinds the stacks owned by the current window down to a recorded size. Mirrors canonical
    /// <c>ImGui::ErrorRecoveryTryToRecoverWindowState</c>
    /// </summary>
    /// <param name="state">Record produced by <see cref="ErrorRecoveryStoreState"/>.</param>
    /// <remarks>
    /// <para>
    /// The stacks are unwound in the canonical order: tables, tab bars, multi-selections, the menu bar,
    /// tree depth, groups, identifiers, disabled scopes, colours, item flags, style variables, fonts, and
    /// finally focus scopes. The order is not arbitrary, because several pops consult state that an earlier
    /// pop establishes: ending a disabled scope needs the current item flags, and popping an identifier
    /// only makes sense once the tree and group stacks are back to their recorded depths.
    /// </para>
    /// <para>
    /// The canonical focus-scope assert is intentionally left commented out in the reference, so this port
    /// also performs no assertion there; a caller may legitimately end a window with a focus scope pushed
    /// by a nested scope that was never closed.
    /// </para>
    /// </remarks>
    public static void ErrorRecoveryTryToRecoverWindowState(ImGuiErrorRecoveryState state)
    {
        ImGuiContext g = GetContext();

        while (g.CurrentTable != null && g.CurrentTable.InnerWindow == g.CurrentWindow)
        {
            AssertUserError(false, "Missing EndTable()");
            EndTable();
        }

        ImGuiWindow? window = g.CurrentWindow;
        ImAssert.Check(window != null, "A window must be current when recovering window state.");
        if (window == null)
            return;

        // Recovery from inside a tab item is not possible yet, matching the canonical limitation.
        while (g.CurrentTabBar != null && g.CurrentTabBar.Window == window)
        {
            AssertUserError(false, "Missing EndTabBar()");
            EndTabBar();
        }
        while (g.CurrentMultiSelect != null && g.CurrentMultiSelect.Storage?.Window == window)
        {
            AssertUserError(false, "Missing EndMultiSelect()");
            EndMultiSelect();
        }
        if (window.DC.MenuBarAppending)
        {
            AssertUserError(false, "Missing EndMenuBar()");
            EndMenuBar();
        }
        while (window.DC.TreeDepth > state.SizeOfTreeStack)
        {
            AssertUserError(false, "Missing TreePop()");
            TreePop();
        }
        while (g.GroupStack.Count > state.SizeOfGroupStack)
        {
            AssertUserError(false, "Missing EndGroup()");
            EndGroup();
        }
        ImAssert.Check(g.GroupStack.Count == state.SizeOfGroupStack);
        while (window.IDStack.Count > state.SizeOfIDStack)
        {
            AssertUserError(false, "Missing PopID()");
            PopID();
        }
        while (g.DisabledStackSize > state.SizeOfDisabledStack)
        {
            AssertUserError(false, "Missing EndDisabled()");
            // A re-enabled override is a different scope shape from an ordinary disabled block, so the
            // matching end function depends on the flag state the scope was entered with.
            if ((g.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0)
            {
                EndDisabled();
            }
            else
            {
                EndDisabledOverrideReenable();
                g.CurrentWindowStack[^1].DisabledOverrideReenable = false;
            }
        }
        ImAssert.Check(g.DisabledStackSize == state.SizeOfDisabledStack);
        while (g.ColorStack.Count > state.SizeOfColorStack)
        {
            AssertUserError(false, "Missing PopStyleColor()");
            PopStyleColor();
        }
        while (g.ItemFlagsStack.Count > state.SizeOfItemFlagsStack)
        {
            AssertUserError(false, "Missing PopItemFlag()");
            PopItemFlag();
        }
        while (g.StyleVarStack.Count > state.SizeOfStyleVarStack)
        {
            AssertUserError(false, "Missing PopStyleVar()");
            PopStyleVar();
        }
        while (g.FontStack.Count > state.SizeOfFontStack)
        {
            AssertUserError(false, "Missing PopFont()");
            PopFont();
        }
        while (g.FocusScopeStack.Count > state.SizeOfFocusScopeStack)
        {
            AssertUserError(false, "Missing PopFocusScope()");
            PopFocusScope();
        }
    }

    #endregion

    #region [SECTION] FRAME LIFECYCLE

    /// <summary>
    /// Refreshes the shared draw-list data every frame: the fullscreen clip rectangle, tessellation
    /// tolerances and the initial draw-list flags. Mirrors canonical <c>SetupDrawListSharedData</c>
    /// </summary>
    /// <remarks>
    /// Canonical also stores <c>InitialFringeScale</c>, which this port's
    /// <see cref="ImDrawListSharedData"/> does not declare.
    /// </remarks>
    private static void SetupDrawListSharedData()
    {
        ImGuiContext g = GetContext();
        ImRect virtualSpace = new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);
        for (int n = 0; n < g.Viewports.Count; n++)
            virtualSpace.Add(g.Viewports[n].GetMainRect());
        g.DrawListSharedData.ClipRectFullscreen = virtualSpace;
        g.DrawListSharedData.CurveTessellationTol = g.Style.CurveTessellationTol;
        g.DrawListSharedData.CircleTessellationMaxError = g.Style.CurveTessellationMaxError;
        g.DrawListSharedData.InitialDrawFlags = ImDrawFlags.None;
        if ((g.IO.Fonts.Flags & ImFontAtlasFlags.NoBakedLines) != 0)
            g.Style.AntiAliasedLines = g.Style.AntiAliasedLineEnds = false;
        if (g.Style.AntiAliasedFill)
            g.DrawListSharedData.InitialDrawFlags |= ImDrawFlags.AAFill;
        if (g.Style.AntiAliasedLines)
            g.DrawListSharedData.InitialDrawFlags |= ImDrawFlags.AALines;
        if (g.Style.AntiAliasedLineEnds)
            g.DrawListSharedData.InitialDrawFlags |= ImDrawFlags.AALineEnds;
        if (g.IO.ConfigDebugDrawListDefaultsToStrokeLegacy)
            g.DrawListSharedData.InitialDrawFlags |= ImDrawFlags.StrokeLegacy;
        if ((g.IO.BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
            g.DrawListSharedData.InitialDrawFlags |= ImDrawFlags.UseVtxOffset;
    }

    /// <summary>
    /// Advances every registered font atlas to the current frame. Mirrors the atlas half of canonical
    /// <c>UpdateTexturesNewFrame</c>.
    /// </summary>
    /// <remarks>
    /// Canonical passes <c>RendererHasTextures</c> into <c>ImFontAtlasUpdateNewFrame</c> and also
    /// advances the user-texture list; this port's <see cref="ImFontAtlas.UpdateNewFrame(int)"/> needs
    /// only the frame counter, so the user-texture list is not advanced here. Every atlas in
    /// <see cref="ImGuiContext.FontAtlases"/> is treated as owned by this context because the port has
    /// no <c>ImFontAtlas::OwnerContext</c>.
    /// </remarks>
    private static void UpdateTexturesNewFrame()
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.FontAtlases.Count; n++)
            g.FontAtlases[n].UpdateNewFrame(g.FrameCount);
    }

    /// <summary>
    /// Finalizes texture bookkeeping at the end of a frame. Mirrors canonical
    /// <c>UpdateTexturesEndFrame</c>.
    /// </summary>
    /// <remarks>
    /// Canonical rebuilds <c>ImGuiPlatformIO.Textures</c> from the atlas texture lists and the user
    /// textures. This port does not assemble that list yet, so <c>PlatformIO.Textures</c> stays empty;
    /// the method is retained so the canonical <see cref="EndFrame"/> ordering remains visible.
    /// </remarks>
    private static void UpdateTexturesEndFrame()
    {
        // PlatformIO.Textures assembly is not implemented in this port.
    }

    /// <summary>
    /// Starts a frame: processes queued input, advances viewport, texture and font state, resets the
    /// per-frame bookkeeping, and opens the implicit fallback window. Mirrors canonical
    /// <c>ImGui::NewFrame</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Steps owned by other chunks are deliberately inert and named inline: settings load/save
    /// (chunk 13), docking (chunk 6), table transient garbage collection (chunk 5), the Escape
    /// drag-and-drop cancel route (input/drag-drop chunk) and the debug tools (chunk 12). The platform
    /// session date and the viewport lifecycle are wired here.
    /// </para>
    /// <para>
    /// The input queue is consumed through <see cref="UpdateInputEvents"/>, which part 02 implements;
    /// canonical trickling is driven by <see cref="ImGuiIO.ConfigInputTrickleEventQueue"/>.
    /// </para>
    /// </remarks>
    public static void NewFrame()
    {
        ImGuiContext g = GetContext();

        // Remove pending delete hooks before frame start. This deferred removal avoids issues of
        // removal while iterating the hook vector.
        for (int n = g.Hooks.Count - 1; n >= 0; n--)
        {
            if (g.Hooks[n].Type == ImGuiContextHookType.PendingRemoval_)
                g.Hooks.RemoveAt(n);
        }

        CallContextHooks(g, ImGuiContextHookType.NewFramePre);

        // Check and assert for various common IO and configuration mistakes.
        g.ConfigFlagsLastFrame = g.ConfigFlagsCurrFrame;
        ErrorCheckNewFrameSanityChecks();
        g.ConfigFlagsCurrFrame = g.IO.ConfigFlags;

        // UpdateSettings(): load on the first frame and flush after the dirty-delay elapses
        UpdateSettings();

        g.Time += g.IO.DeltaTime;
        g.FrameCount += 1;
        g.SessionDate = new ImGuiPackedDate((uint)g.PlatformIO.Platform_SessionDate);
        g.TooltipOverrideCount = 0;
        g.WindowsActiveCount = 0;
        g.MenusIdSubmittedThisFrame.Clear();

        // Calculate frame-rate for the user, as a purely luxurious feature
        g.FramerateSecPerFrameAccum += g.IO.DeltaTime - g.FramerateSecPerFrame[g.FramerateSecPerFrameIdx];
        g.FramerateSecPerFrame[g.FramerateSecPerFrameIdx] = g.IO.DeltaTime;
        g.FramerateSecPerFrameIdx = (g.FramerateSecPerFrameIdx + 1) % g.FramerateSecPerFrame.Length;
        g.FramerateSecPerFrameCount = Math.Min(g.FramerateSecPerFrameCount + 1, g.FramerateSecPerFrame.Length);
        g.IO.Framerate = g.FramerateSecPerFrameAccum > 0.0f
            ? 1.0f / (g.FramerateSecPerFrameAccum / g.FramerateSecPerFrameCount)
            : float.MaxValue;

        // Process input queue (trickle as many events as possible), turn events into writes to IO
        // structure.
        g.InputEventsTrail.Clear();
        UpdateInputEvents(g.IO.ConfigInputTrickleEventQueue);

        // Update viewports (after processing the input queue, so io.MouseHoveredViewport is set).
        UpdateViewportsNewFrame();

        // Update texture list (collect destroyed textures, etc.).
        UpdateTexturesNewFrame();

        // Setup current font and draw list shared data.
        SetupDrawListSharedData();
        UpdateFontsNewFrame();

        g.WithinFrameScope = true;

        // Mark rendering data as invalid to prevent a user who may have a handle on it from using it.
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            viewport.DrawData = null;
            viewport.DrawDataP.Valid = false;
        }

        // Drag and drop keeps the source ID alive so state stays consistent even if the source
        // disappears.
        if (g.DragDropActive && g.DragDropPayload.SourceId == g.ActiveId)
            KeepAliveID(g.DragDropPayload.SourceId);

        // [DEBUG] Identifier-conflict highlight.
        if (!g.IO.ConfigDebugHighlightIdConflicts || !g.IO.KeyCtrl) // Count is locked while holding Ctrl
            g.DebugDrawIdConflictsId = 0;
        if (g.IO.ConfigDebugHighlightIdConflicts && g.HoveredIdPreviousFrameItemCount > 1)
            g.DebugDrawIdConflictsId = g.HoveredIdPreviousFrame;

        // Update HoveredId data.
        if (g.HoveredIdPreviousFrame == 0)
            g.HoveredIdTimer = 0.0f;
        if (g.HoveredIdPreviousFrame == 0 || (g.HoveredId != 0 && g.ActiveId == g.HoveredId))
            g.HoveredIdNotActiveTimer = 0.0f;
        if (g.HoveredId != 0)
            g.HoveredIdTimer += g.IO.DeltaTime;
        if (g.HoveredId != 0 && g.ActiveId != g.HoveredId)
            g.HoveredIdNotActiveTimer += g.IO.DeltaTime;
        g.HoveredIdPreviousFrame = g.HoveredId;
        g.HoveredIdPreviousFrameItemCount = 0;
        g.HoveredId = 0;
        g.HoveredIdAllowOverlap = false;
        g.HoveredIdIsDisabled = false;

        // Clear ActiveID if the item is not alive anymore.
        if (g.ActiveId != 0 && g.ActiveIdIsAlive != g.ActiveId && g.ActiveIdPreviousFrame == g.ActiveId)
            ClearActiveID();

        // Update ActiveId data.
        if (g.ActiveId != 0)
            g.ActiveIdTimer += g.IO.DeltaTime;
        if (g.ActiveId != 0 && g.ActiveId == g.LastActiveId)
        {
            g.LastActiveIdWasSelected = g.ActiveIdWasSelected;
            g.LastActiveIdWasSoleSelected = g.ActiveIdWasSoleSelected;
        }
        g.LastActiveIdTimer += g.IO.DeltaTime;
        g.ActiveIdPreviousFrame = g.ActiveId;
        g.ActiveIdIsAlive = 0;
        g.AnyIdHasBeenEditedThisFrame = false;
        g.ActiveIdHasBeenEditedThisFrame = false;
        g.ActiveIdIsJustActivated = false;
        if (g.TempInputId != 0 && g.ActiveId != g.TempInputId)
            g.TempInputId = 0;
        if (g.InputTextReactivateId != 0 && g.InputTextReactivateId != g.DeactivatedItemData.ID)
            g.InputTextReactivateId = 0;
        if (g.ActiveId == 0)
        {
            g.ActiveIdUsingNavDirMask = 0x00;
            g.ActiveIdUsingAllKeyboardKeys = false;
        }
        if (g.DeactivatedItemData.ElapseFrame < g.FrameCount)
            g.DeactivatedItemData.ID = 0;
        g.DeactivatedItemData.IsAlive = false;
        if (g.InputTextDeactivatedState.ElapseFrame < g.FrameCount)
            g.InputTextDeactivatedState.ID = 0;

        // Record when the mouse has been stationary, as this state is preserved while over the same
        // item.
        if (g.HoverItemDelayId != 0 && g.MouseStationaryTimer >= g.Style.HoverStationaryDelay)
            g.HoverItemUnlockedStationaryId = g.HoverItemDelayId;
        else if (g.HoverItemDelayId == 0)
            g.HoverItemUnlockedStationaryId = 0;
        if (g.HoveredWindow != null && g.MouseStationaryTimer >= g.Style.HoverStationaryDelay)
            g.HoverWindowUnlockedStationaryId = g.HoveredWindow.ID;
        else if (g.HoveredWindow == null)
            g.HoverWindowUnlockedStationaryId = 0;

        // Update hover delay for IsItemHovered() with delays and tooltips
        g.HoverItemDelayIdPreviousFrame = g.HoverItemDelayId;
        if (g.HoverItemDelayId != 0)
        {
            g.HoverItemDelayTimer += g.IO.DeltaTime;
            g.HoverItemDelayClearTimer = 0.0f;
            g.HoverItemDelayId = 0;
        }
        else if (g.HoverItemDelayTimer > 0.0f)
        {
            // This gives a little leeway before clearing the hover timer, allowing the mouse to cross
            // gaps.
            g.HoverItemDelayClearTimer += g.IO.DeltaTime;
            if (g.HoverItemDelayClearTimer >= Math.Max(0.25f, g.IO.DeltaTime * 2.0f))
                g.HoverItemDelayTimer = g.HoverItemDelayClearTimer = 0.0f;
        }

        // Update keyboard input state.
        UpdateKeyboardInputs();

        // Drag and drop frame reset.
        g.DragDropAcceptIdPrev = g.DragDropAcceptIdCurr;
        g.DragDropAcceptIdCurr = 0;
        g.DragDropAcceptFlagsPrev = g.DragDropAcceptFlagsCurr;
        g.DragDropAcceptFlagsCurr = ImGuiDragDropFlags.None;
        g.DragDropAcceptIdCurrRectSurface = float.MaxValue;
        g.DragDropWithinSource = false;
        g.DragDropWithinTarget = false;
        g.DragDropHoldJustPressedId = 0;

        // Cancel an active drag with Escape. The route owner is
        // the drag source's active item, or a dedicated identifier when the payload outlives its item.
        if (g.DragDropActive)
        {
            uint dragCancelOwnerId = g.ActiveId != 0 ? g.ActiveId : ImHashStr("##DragDropCancelHandler");
            if (Shortcut(ImGuiKey.Escape, ImGuiInputFlags.RouteGlobal, dragCancelOwnerId))
            {
                ClearActiveID();
                ClearDragDrop();
            }
        }
        g.TooltipPreviousWindow = null;

        // Update keyboard/gamepad navigation.
        NavUpdate();

        // Update mouse input state.
        UpdateMouseInputs();

        DockContextNewFrameUpdateUndocking(g);

        // Mark all windows as not visible and compact unused memory
        ImAssert.Check(g.WindowsFocusOrder.Count <= g.Windows.Count, "Focus order list exceeds the window list.");
        bool gcAll = g.GcCompactAll || g.IO.ConfigMemoryCompactTimer < 0.0f;
        float memoryCompactStartTime = gcAll ? float.MaxValue : (float)g.Time - g.IO.ConfigMemoryCompactTimer;
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            window.WasActive = window.Active;
            window.Active = false;
            window.WriteAccessed = false;
            window.BeginCountPreviousFrame = window.BeginCount;
            window.BeginCount = 0;

            // Garbage collect transient buffers of recently unused windows.
            if ((!window.WasActive || gcAll) && !window.MemoryCompacted && window.LastTimeActive < memoryCompactStartTime)
                GcCompactTransientWindowBuffers(window);
        }

        // Find hovered window. This runs before UpdateMouseMovingWindowNewFrame() so
        // g.HoveredWindowUnderMovingWindow is filled on the mouse release frame, and after the
        // WasActive=Active loop because FindHoveredWindowEx reads window->Active
        UpdateHoveredWindowAndCaptureFlags(g.IO.MousePos);

        // Handle the user moving a window with the mouse at the beginning of the frame to avoid input
        // lag or sheering.
        UpdateMouseMovingWindowNewFrame();

        // Background darkening/whitening. The modal lookup is
        // inlined from GetTopMostPopupModal()., which is owned
        // by the popup chunk.
        bool hasPopupModal = false;
        for (int n = g.OpenPopupStack.Count - 1; n >= 0; n--)
        {
            ImGuiWindow? popup = g.OpenPopupStack[n].Window;
            if (popup != null && (popup.Flags & ImGuiWindowFlags.Modal) != 0)
            {
                hasPopupModal = true;
                break;
            }
        }
        if (hasPopupModal || (g.NavWindowingTarget != null && g.NavWindowingHighlightAlpha > 0.0f))
            g.DimBgRatio = Math.Min(g.DimBgRatio + g.IO.DeltaTime * 6.0f, 1.0f);
        else
            g.DimBgRatio = Math.Max(g.DimBgRatio - g.IO.DeltaTime * 10.0f, 0.0f);

        g.MouseCursor = ImGuiMouseCursor.Arrow;
        g.WantCaptureMouseNextFrame = g.WantCaptureKeyboardNextFrame = g.WantTextInputNextFrame = -1;

        // Platform IME data: reset for the frame.
        ImGuiPlatformImeData imeData = g.PlatformImeData;
        ImGuiPlatformImeData imeDataPrev = g.PlatformImeDataPrev;
        imeDataPrev.WantVisible = imeData.WantVisible;
        imeDataPrev.WantTextInput = imeData.WantTextInput;
        imeDataPrev.InputPos = imeData.InputPos;
        imeDataPrev.InputLineHeight = imeData.InputLineHeight;
        imeDataPrev.ViewportId = imeData.ViewportId;
        imeData.WantVisible = false;
        imeData.WantTextInput = false;

        // Mouse wheel scrolling, scale.
        UpdateMouseWheel();

        // Garbage collect transient buffers of recently unused tables: chunk 5 (tables).
        if (g.GcCompactAll)
            GcCompactTransientMiscBuffers();
        g.GcCompactAll = false;

        // Closing the focused window restores focus to the first active root window in descending
        // z-order.
        if (g.NavWindow != null && !g.NavWindow.WasActive)
            FocusTopMostWindowUnderOne(null!, null, null, ImGuiFocusRequestFlags.RestoreFocusedChild);

        // No window should be open at the beginning of the frame. An explicit clear allows the user to
        // call NewFrame() multiple times without calling Render()
        g.CurrentWindowStack.Clear();
        g.BeginPopupStack.Clear();
        g.ItemFlagsStack.Clear();
        // ImGuiItemFlags_Default_ == ImGuiItemFlags_AutoClosePopups | ImGuiItemFlags_LiveEditOnInputText
        // private alias, so the two members are written out explicitly.
        g.ItemFlagsStack.Add(ImGuiItemFlags.AutoClosePopups | ImGuiItemFlags.LiveEditOnInputText); // Default flags
        g.CurrentItemFlags = g.ItemFlagsStack[g.ItemFlagsStack.Count - 1];
        g.GroupStack.Clear();

        DockContextNewFrameUpdateDocking(g);

        // [DEBUG] Update debug features.
        UpdateDebugToolItemPicker();
        UpdateDebugToolFlashStyleColor();
        UpdateDebugToolItemPathQuery();
        if (g.DebugLocateFrames > 0 && --g.DebugLocateFrames == 0)
        {
            g.DebugLocateId = 0;
            g.DebugBreakInLocateId = false;
        }
        if (g.DebugLogAutoDisableFrames > 0 && --g.DebugLogAutoDisableFrames == 0)
        {
            DebugLog("(Debug Log: Auto-disabled some ImGuiDebugLogFlags after 2 frames)\n");
            g.DebugLogFlags &= ~g.DebugLogAutoDisableFlags;
            g.DebugLogAutoDisableFlags = ImGuiDebugLogFlags.None;
        }

        // Create the implicit/fallback window, which is only rendered if the user has added something
        // to it. It prevents ImGui calls from crashing without a user window
        g.WithinFrameScopeWithImplicitWindow = true;
        SetNextWindowSize(new ImVec2(400, 400), ImGuiCond.FirstUseEver);
        Begin("Debug##Default");
        ImAssert.Check(g.CurrentWindow != null && g.CurrentWindow.IsFallbackWindow);

        // Store stack sizes.
        g.ErrorCountCurrentFrame = 0;
        ErrorRecoveryStoreState(g.StackSizesInNewFrame);

        // [DEBUG] When ConfigDebugBeginReturnValueLoop is set, Begin()/BeginChild() return false at
        // different levels of the window stack.
        if (g.IO.ConfigDebugBeginReturnValueLoop)
        {
            g.DebugBeginReturnValueCullDepth = g.DebugBeginReturnValueCullDepth == -1
                ? (sbyte)0
                : (sbyte)((g.DebugBeginReturnValueCullDepth + ((g.FrameCount % 4) == 0 ? 1 : 0)) % 10);
        }
        else
        {
            g.DebugBeginReturnValueCullDepth = -1;
        }

        CallContextHooks(g, ImGuiContextHookType.NewFramePost);
    }

    /// <summary>
    /// Ends a frame: recovers from unbalanced stacks, finalizes navigation and drag-and-drop state,
    /// sorts the window list, unlocks the font atlases, and clears the per-frame input transients.
    /// Mirrors canonical <c>ImGui::EndFrame</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The canonical docking shutdown, drag-and-drop payload expiry and fallback tooltip, texture-list
    /// assembly and settings save depend on subsystems owned by later chunks (chunk 6 docking, the
    /// drag-and-drop chunk, chunk 13 settings); those steps are deliberately inert and named inline.
    /// The IME notification and the platform viewport list are wired here.
    /// </para>
    /// <para>
    /// The ordering below follows canonical exactly for the steps this chunk owns, including the
    /// call to <see cref="UpdateMouseMovingWindowEndFrame"/> (part 02) and
    /// <see cref="AddWindowToSortBuffer"/> (part 08).
    /// </para>
    /// </remarks>
    public static void EndFrame()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.Initialized, "Context is not initialized.");

        // Don't process EndFrame() multiple times.
        if (g.FrameCountEnded == g.FrameCount)
            return;
        if (!g.WithinFrameScope)
        {
            // IM_ASSERT_USER_ERROR_RET.
            AssertUserError(false, "Forgot to call ImGui::NewFrame()?");
            return;
        }

        CallContextHooks(g, ImGuiContextHookType.EndFramePre);

        // [EXPERIMENTAL] Recover from errors.
        if (g.IO.ConfigErrorRecovery)
            ErrorRecoveryTryToRecoverState(g.StackSizesInNewFrame);
        ErrorCheckEndFrameSanityChecks();
        ErrorCheckEndFrameFinalizeErrorTooltip();

        // Notify the platform when the Input Method Editor cursor has moved
        // forwards the data to the viewport that requested text input, falling back to the main
        // viewport when that identifier is unknown.
        ImGuiPlatformImeData imeData = g.PlatformImeData;
        if (g.PlatformIO.Platform_SetImeDataFn != null && !ImeDataEquals(imeData, g.PlatformImeDataPrev))
        {
            ImGuiViewport? imeViewport = FindViewportByID(imeData.ViewportId) ?? GetMainViewport();
            g.PlatformIO.Platform_SetImeDataFn(g, imeViewport, imeData);
        }
        g.WantTextInputNextFrame = imeData.WantTextInput ? 1 : 0;

        // Hide and unfocus the implicit/fallback "Debug" window if it hasn't been used
        g.WithinFrameScopeWithImplicitWindow = false;
        if (g.CurrentWindow != null && g.CurrentWindow.IsFallbackWindow && !g.CurrentWindow.WriteAccessed)
        {
            g.CurrentWindow.Active = false;
            if (g.NavWindow != null && g.NavWindow.RootWindow == g.CurrentWindow)
                FocusWindow(null);
        }
        End();

        // Update navigation: Ctrl+Tab, wrap-around requests.
        NavEndFrame();

        DockContextEndFrame(g);

        SetCurrentViewport(null, null);

        // Drag and drop: elapse the payload when it was delivered, or when the source stopped being
        // submitted. A source elapses when it opted into
        // PayloadAutoExpire, has no live mouse button, or its mouse button was released.
        if (g.DragDropActive)
        {
            bool isDelivered = g.DragDropPayload.Delivery;
            bool isElapsed = g.DragDropSourceFrameCount + 1 < g.FrameCount
                && ((g.DragDropSourceFlags & ImGuiDragDropFlags.PayloadAutoExpire) != 0
                    || g.DragDropMouseButton == -1
                    || !IsMouseDown((ImGuiMouseButton)g.DragDropMouseButton));
            if (isDelivered || isElapsed)
                ClearDragDrop();
        }

        // Drag and drop: fall back to a generic preview tooltip when the source stopped being
        // submitted without a description of its own.
        if (g.DragDropActive
            && g.DragDropSourceFrameCount + 1 < g.FrameCount
            && (g.DragDropSourceFlags & ImGuiDragDropFlags.SourceNoPreviewTooltip) == 0)
        {
            g.DragDropWithinSource = true;
            SetTooltip("...");
            g.DragDropWithinSource = false;
        }

        // End frame.
        g.WithinFrameScope = false;
        g.FrameCountEnded = g.FrameCount;
        UpdateFontsEndFrame();

        // Initiate moving window and handle left-click and right-click focus.
        UpdateMouseMovingWindowEndFrame();

        // Update user-facing viewport list.
        UpdateViewportsEndFrame();

        // Sort the window list so that all child windows are after their parent
        g.WindowsTempSortBuffer.Clear();
        g.WindowsTempSortBuffer.Capacity = Math.Max(g.WindowsTempSortBuffer.Capacity, g.Windows.Count);
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            if (window.Active && (window.Flags & ImGuiWindowFlags.ChildWindow) != 0) // If a child is active its parent will add it
                continue;
            AddWindowToSortBuffer(g.WindowsTempSortBuffer, window);
        }

        // This usually asserts if there is a mismatch between the ChildWindow/ParentWindow values and
        // DC.ChildWindows[] in parents.
        ImAssert.Check(g.Windows.Count == g.WindowsTempSortBuffer.Count, "Window sort buffer size mismatch.");
        g.Windows.Clear();
        g.Windows.AddRange(g.WindowsTempSortBuffer);
        g.IO.MetricsActiveWindows = g.WindowsActiveCount;

        UpdateTexturesEndFrame();

        // Unlock font atlases.
        for (int n = 0; n < g.FontAtlases.Count; n++)
            g.FontAtlases[n].Locked = false;

        // Clear input data for the next frame.
        g.IO.MousePosPrev = g.IO.MousePos;
        g.IO.AppFocusLost = false;
        g.IO.MouseWheel = g.IO.MouseWheelH = 0.0f;
        g.IO.InputQueueCharacters.Clear();

        CallContextHooks(g, ImGuiContextHookType.EndFramePost);
    }

    /// <summary>
    /// Renders the dimmed background behind a modal window or the Ctrl+Tab window list. Mirrors
    /// canonical <c>RenderDimmedBackgroundBehindWindow</c>
    /// </summary>
    /// <param name="window">Window whose background is dimmed.</param>
    /// <param name="col">Packed dim color; an alpha of zero skips the call.</param>
    /// <remarks>
    /// The canonical docking branch that paints over sibling nodes needs
    /// <c>ImGuiWindow::DockNode</c>, <c>FindFrontMostVisibleChildWindow</c> and
    /// <c>RenderRectFilledWithHole</c>, so it
    /// is deliberately inert.
    /// </remarks>
    private static void RenderDimmedBackgroundBehindWindow(ImGuiWindow window, uint col)
    {
        if ((col & 0xFF000000u) == 0)
            return;

        ImGuiViewportP? viewport = window.Viewport;
        ImAssert.Check(viewport != null, "A dimmed window must belong to a viewport.");
        if (viewport == null)
            return;
        ImRect viewportRect = viewport.GetMainRect();

        // Draw behind the window by moving the draw command to the FRONT of the draw list.
        ImGuiWindow? rootDockTree = window.RootWindowDockTree ?? window.RootWindow;
        if (rootDockTree == null)
            return;
        ImDrawList drawList = rootDockTree.DrawList;
        // Draw lists have been trimmed already, hence the explicit recreation of a draw command if
        // missing.
        drawList.ChannelsMerge();
        if (drawList.CmdBuffer.Count == 0)
            drawList.AddDrawCmd();
        drawList.PushClipRect(
            viewportRect.Min - new ImVec2(1, 1),
            viewportRect.Max + new ImVec2(1, 1),
            false);
        ImDrawCmd cmd = drawList.CmdBuffer[drawList.CmdBuffer.Count - 1];
        ImAssert.Check(cmd.ElemCount == 0);
        drawList.AddRectFilled(viewportRect.Min, viewportRect.Max, col);
        cmd = drawList.CmdBuffer[drawList.CmdBuffer.Count - 1];
        drawList.CmdBuffer.RemoveAt(drawList.CmdBuffer.Count - 1);
        drawList.CmdBuffer.Insert(0, cmd);
        // A new command is required because CmdBuffer[^1].IdxOffset would be wrong if geometry were
        // appended to the same command.
        drawList.AddDrawCmd();
        drawList.PopClipRect();

        // Draw over sibling docking nodes in the same docking tree: chunk 6 (docking).
    }

    /// <summary>
    /// Returns the bottom-most visible window that shares a begin stack with a parent window. Mirrors
    /// canonical <c>FindBottomMostVisibleWindowWithinBeginStack</c>
    /// </summary>
    /// <param name="parentWindow">Window whose begin stack is searched.</param>
    /// <returns>The bottom-most visible window of the begin stack, defaulting to the parent.</returns>
    public static ImGuiWindow FindBottomMostVisibleWindowWithinBeginStack(ImGuiWindow parentWindow)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow bottomMostVisibleWindow = parentWindow;
        for (int i = FindWindowDisplayIndex(parentWindow); i >= 0; i--)
        {
            ImGuiWindow window = g.Windows[i];
            if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
                continue;
            if (!IsWindowWithinBeginStackOf(window, parentWindow))
                break;
            if (IsWindowActiveAndVisible(window) && GetWindowDisplayLayer(window) <= GetWindowDisplayLayer(parentWindow))
                bottomMostVisibleWindow = window;
        }
        return bottomMostVisibleWindow;
    }

    /// <summary>
    /// Returns whether a window is active and not hidden. Mirrors the canonical file-static
    /// <c>IsWindowActiveAndVisible</c>.
    /// </summary>
    /// <param name="window">Window to test.</param>
    /// <returns>True when the window is active and visible.</returns>
    /// <remarks>
    /// The box-selection helper uses this predicate when it resolves the front-most visible child window
    /// to draw the selection rectangle on.
    /// </remarks>
    internal static bool IsWindowActiveAndVisible(ImGuiWindow window)
    {
        return window.Active && !window.Hidden;
    }

    /// <summary>
    /// Draws the modal and Ctrl+Tab dimmed backgrounds. Mirrors canonical
    /// <c>ImGui::RenderDimmedBackgrounds</c>.
    /// </summary>
    /// <remarks>
    /// The top-most visible modal is resolved with an inline scan equivalent to
    /// <c>GetTopMostAndVisiblePopupModal()</c>., whose
    /// owning member belongs to the popup chunk. This function is assigned to part 08 by
    /// <c>CONTRACT.md</c> §2 as well as to part 01; the frame-lifecycle render path requires it, so it
    /// is defined here and reported as a duplicate ownership entry.
    /// </remarks>
    internal static void RenderDimmedBackgrounds()
    {
        ImGuiContext g = GetContext();

        ImGuiWindow? modalWindow = null;
        for (int n = g.OpenPopupStack.Count - 1; n >= 0; n--)
        {
            ImGuiWindow? popup = g.OpenPopupStack[n].Window;
            if (popup != null && (popup.Flags & ImGuiWindowFlags.Modal) != 0 && IsWindowActiveAndVisible(popup))
            {
                modalWindow = popup;
                break;
            }
        }

        if (g.DimBgRatio <= 0.0f && g.NavWindowingHighlightAlpha <= 0.0f)
            return;
        bool dimBgForModal = modalWindow != null;
        bool dimBgForWindowList = g.NavWindowingTargetAnim != null && g.NavWindowingTargetAnim.Active;
        if (!dimBgForModal && !dimBgForWindowList)
            return;

        ImGuiViewport?[] viewportsAlreadyDimmed = new ImGuiViewport?[2];
        if (dimBgForModal)
        {
            // Draw dimming behind the modal, or behind a begin-stack child, whichever comes first in
            // draw order.
            ImGuiWindow dimBehindWindow = FindBottomMostVisibleWindowWithinBeginStack(modalWindow!);
            RenderDimmedBackgroundBehindWindow(
                dimBehindWindow,
                GetColorU32(modalWindow!.DC.ModalDimBgColor, g.DimBgRatio));
            viewportsAlreadyDimmed[0] = modalWindow.Viewport;
        }
        else if (dimBgForWindowList)
        {
            // Draw dimming behind the Ctrl+Tab target window and behind the Ctrl+Tab UI window.
            ImGuiWindow targetWindow = g.NavWindowingTargetAnim!;
            RenderDimmedBackgroundBehindWindow(
                targetWindow,
                GetColorU32(ImGuiCol.NavWindowingDimBg, g.DimBgRatio));
            viewportsAlreadyDimmed[0] = targetWindow.Viewport;
            if (g.NavWindowingListWindow != null
                && g.NavWindowingListWindow.Active
                && g.NavWindowingListWindow.Viewport != null
                && g.NavWindowingListWindow.Viewport != targetWindow.Viewport)
            {
                RenderDimmedBackgroundBehindWindow(
                    g.NavWindowingListWindow,
                    GetColorU32(ImGuiCol.NavWindowingDimBg, g.DimBgRatio));
                viewportsAlreadyDimmed[1] = g.NavWindowingListWindow.Viewport;
            }

            // Draw a border around the Ctrl+Tab target window.
            ImGuiViewport? viewport = targetWindow.Viewport;
            if (viewport != null)
            {
                float distance = g.FontSize;
                ImRect bb = targetWindow.Rect();
                bb.Expand(distance);
                if (bb.GetWidth() >= viewport.Size.X && bb.GetHeight() >= viewport.Size.Y)
                    bb.Expand(-distance - 1.0f); // If a window fits the entire viewport, adjust its highlight inward
                targetWindow.DrawList.ChannelsMerge();
                if (targetWindow.DrawList.CmdBuffer.Count == 0)
                    targetWindow.DrawList.AddDrawCmd();
                targetWindow.DrawList.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size);
                targetWindow.DrawList.AddRect(
                    bb.Min,
                    bb.Max,
                    GetColorU32(ImGuiCol.NavWindowingHighlight, g.NavWindowingHighlightAlpha),
                    targetWindow.WindowRounding,
                    3.0f);
                targetWindow.DrawList.PopClipRect();
            }
        }

        // Draw a dimming background on every other viewport.
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            if (viewport == viewportsAlreadyDimmed[0] || viewport == viewportsAlreadyDimmed[1])
                continue;
            if (modalWindow != null && viewport.Window != null && IsWindowAbove(viewport.Window, modalWindow))
                continue;
            ImDrawList drawList = GetForegroundDrawList(viewport);
            uint dimBgCol = GetColorU32(
                dimBgForModal ? ImGuiCol.ModalWindowDimBg : ImGuiCol.NavWindowingDimBg,
                g.DimBgRatio);
            drawList.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, dimBgCol);
        }
    }

    /// <summary>
    /// Prepares the draw data for the renderer. Mirrors canonical <c>ImGui::Render</c>
    /// </summary>
    /// <remarks>
    /// The software mouse cursor (<c>RenderMouseCursor</c>, 6482-6484) needs
    /// <c>ImFontAtlasGetMouseCursorTexData</c> and <c>ImDrawListSharedData.FontAtlas</c>, neither of
    /// which exists in this port, and the debug texture-request logging belongs to chunk 12; both are
    /// inert. Draw-data assembly itself is delegated to part 08
    /// (<see cref="InitViewportDrawData"/>, <see cref="AddDrawListToDrawDataEx"/>,
    /// <see cref="FlattenDrawDataIntoSingleLayer"/>, <see cref="AddRootWindowToDrawData"/>).
    /// </remarks>
    public static void Render()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.Initialized, "Context is not initialized.");

        if (g.FrameCountEnded != g.FrameCount)
            EndFrame();
        if (g.FrameCountRendered == g.FrameCount)
            return;
        g.FrameCountRendered = g.FrameCount;

        g.IO.MetricsRenderWindows = 0;
        CallContextHooks(g, ImGuiContextHookType.RenderPre);

        // Add the background draw list for each active viewport.
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            InitViewportDrawData(viewport);
            if (viewport.BgFgDrawLists[0] != null && viewport.BgFgDrawListsLastTimeActive[0] == (float)g.Time)
            {
                AddDrawListToDrawDataEx(
                    viewport.DrawDataP,
                    viewport.DrawDataBuilder.Layers[0]!,
                    GetBackgroundDrawList(viewport));
            }
        }

        // Draw modal/window whitening backgrounds.
        RenderDimmedBackgrounds();

        // Add ImDrawList to render.
        ImGuiWindow?[] windowsToRenderTopMost = new ImGuiWindow?[2];
        windowsToRenderTopMost[0] = g.NavWindowingTarget != null
            && (g.NavWindowingTarget.Flags & ImGuiWindowFlags.NoBringToFrontOnFocus) == 0
            ? g.NavWindowingTarget.RootWindowDockTree
            : null;
        windowsToRenderTopMost[1] = g.NavWindowingTarget != null ? g.NavWindowingListWindow : null;
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            if (IsWindowActiveAndVisible(window)
                && (window.Flags & ImGuiWindowFlags.ChildWindow) == 0
                && window != windowsToRenderTopMost[0]
                && window != windowsToRenderTopMost[1])
            {
                AddRootWindowToDrawData(window);
            }
        }
        for (int n = 0; n < windowsToRenderTopMost.Length; n++)
        {
            ImGuiWindow? topMost = windowsToRenderTopMost[n];
            if (topMost != null && IsWindowActiveAndVisible(topMost)) // NavWindowingTarget is always temporarily displayed as the top-most window
                AddRootWindowToDrawData(topMost);
        }

        // Draw the software mouse cursor if requested by io.MouseDrawCursor: deferred (see remarks).

        // Setup ImDrawData structures for the end user.
        g.IO.MetricsRenderVertices = 0;
        g.IO.MetricsRenderIndices = 0;
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            FlattenDrawDataIntoSingleLayer(viewport.DrawDataBuilder);

            // Add the foreground draw list for each active viewport.
            if (viewport.BgFgDrawLists[1] != null && viewport.BgFgDrawListsLastTimeActive[1] == (float)g.Time)
            {
                AddDrawListToDrawDataEx(
                    viewport.DrawDataP,
                    viewport.DrawDataBuilder.Layers[0]!,
                    GetForegroundDrawList(viewport));
            }

            // _PopUnusedDrawCmd() runs last because RenderDimmedBackgrounds() relies on a valid command
            // being present, especially in the docking branch.
            ImDrawData drawData = viewport.DrawDataP;
            for (int cmdListIndex = 0; cmdListIndex < drawData.CmdLists.Count; cmdListIndex++)
                drawData.CmdLists[cmdListIndex].PopUnusedDrawCmd();
            g.IO.MetricsRenderVertices += drawData.TotalVtxCount;
            g.IO.MetricsRenderIndices += drawData.TotalIdxCount;
        }

        // ImFontAtlasDebugLogTextureRequests: chunk 12 (debug tools).

        CallContextHooks(g, ImGuiContextHookType.RenderPost);
    }

    /// <summary>
    /// Returns the draw data produced by <see cref="Render"/>. Mirrors canonical
    /// <c>ImGui::GetDrawData</c>.
    /// </summary>
    /// <returns>
    /// The main viewport's draw data, or a null reference when no valid frame has been rendered yet.
    /// </returns>
    /// <remarks>
    /// Canonical returns <c>NULL</c> until <see cref="Render"/> has run and invalidates the data at the
    /// next <see cref="NewFrame"/>. This port keeps the signature non-nullable as required by
    /// <c>CONTRACT.md</c> §5 and returns <c>null!</c> in the invalid case so the canonical null-check
    /// remains meaningful; callers that must be null-safe should test the reference for null.
    /// </remarks>
    public static ImDrawData GetDrawData()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.Viewports.Count > 0, "Context has no main viewport.");
        ImGuiViewportP viewport = g.Viewports[0];
        return viewport.DrawDataP.Valid ? viewport.DrawDataP : null!;
    }

    #endregion

    #region [SECTION] VIEWPORTS, PLATFORM WINDOWS

    /// <summary>
    /// Returns the main (default) viewport, which is never null. Mirrors canonical
    /// <c>ImGui::GetMainViewport</c>.
    /// </summary>
    /// <returns>The main viewport.</returns>
    public static ImGuiViewport GetMainViewport()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.Viewports.Count > 0, "Context has no main viewport; was ImGui.CreateContext() called?");
        if (g.Viewports.Count == 0)
            throw new InvalidOperationException("Context has no main viewport; was ImGui.CreateContext() called?");
        return g.Viewports[0];
    }

    /// <summary>
    /// Makes a viewport current and updates the DPI scale it implies. Mirrors canonical
    /// <c>ImGui::SetCurrentViewport</c>.
    /// </summary>
    /// <param name="currentWindow">Window being entered; retained for signature parity and unused, as in canonical.</param>
    /// <param name="viewport">Viewport to make current, or null to clear it.</param>
    /// <remarks>
    /// When the current viewport actually changes and the backend installed
    /// <see cref="ImGuiPlatformIO.Platform_OnChangedViewport"/>, that handler is notified so it can swap
    /// fonts or style for the new DPI.
    /// </remarks>
    internal static void SetCurrentViewport(ImGuiWindow? currentWindow, ImGuiViewportP? viewport)
    {
        ImGuiContext g = GetContext();
        if (viewport != null)
            viewport.LastFrameActive = g.FrameCount;
        if (g.CurrentViewport == viewport)
            return;
        g.CurrentDpiScale = viewport != null ? viewport.DpiScale : 1.0f;
        g.CurrentViewport = viewport;
        ImAssert.Check(g.CurrentDpiScale > 0.0f && g.CurrentDpiScale < 99.0f, "Invalid DPI scale.");
        if (g.IO.ConfigDpiScaleFonts)
            g.Style.FontScaleDpi = g.CurrentDpiScale;

        // Notify the platform layer of viewport changes.
        if (g.CurrentViewport != null && g.PlatformIO.Platform_OnChangedViewport != null)
            g.PlatformIO.Platform_OnChangedViewport(g.CurrentViewport);
    }

    /// <summary>
    /// Refreshes the state of every viewport at the start of a frame, including position, size, work
    /// rectangle, monitor association, DPI scale and mouse routing. Mirrors canonical
    /// <c>ImGui::UpdateViewportsNewFrame</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="ImGuiConfigFlags.ViewportsEnable"/> is set, the platform is queried for window
    /// position, size, framebuffer density, minimized/focused state and work-area insets, and the mouse
    /// is routed between viewports. Otherwise the canonical single-viewport branch runs and only the
    /// main viewport is refreshed; <see cref="ErrorCheckNewFrameSanityChecks"/> has already cleared the
    /// flag when the backend cannot provide viewports, so the branch is stable for backends that do not
    /// advertise the feature.
    /// </para>
    /// <para>
    /// Canonical computes <c>main_viewport_framebuffer_scale</c> but consumes it only in
    /// the minimized multi-viewport branch; the main-viewport update helper does not copy
    /// it onto the viewport, so <c>InitViewportDrawData</c> falls back to
    /// <see cref="ImGuiIO.DisplayFramebufferScale"/>. That quirk is reproduced.
    /// </para>
    /// </remarks>
    private static void UpdateViewportsNewFrame()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.Viewports.Count > 0, "Context has no main viewport.");
        if (g.Viewports.Count == 0)
            return;

        ImAssert.Check(g.PlatformIO.Viewports.Count <= g.Viewports.Count,
            "The user-facing viewport list must not exceed the internal list.");

        // Update the minimized and focused state first, because the main viewport's position and size
        // are preserved while it is minimized.
        bool viewportsEnabled = (g.ConfigFlagsCurrFrame & ImGuiConfigFlags.ViewportsEnable) != 0;
        if (viewportsEnabled)
        {
            ImGuiViewportP? focusedViewport = null;
            for (int n = 0; n < g.Viewports.Count; n++)
            {
                ImGuiViewportP viewport = g.Viewports[n];
                bool platformFuncsAvailable = viewport.PlatformWindowCreated;
                if (g.PlatformIO.Platform_GetWindowMinimized != null && platformFuncsAvailable)
                {
                    bool isMinimized = g.PlatformIO.Platform_GetWindowMinimized(viewport);
                    if (isMinimized)
                        viewport.Flags |= ImGuiViewportFlags.IsMinimized;
                    else
                        viewport.Flags &= ~ImGuiViewportFlags.IsMinimized;
                }

                // Track the implicit z-order knowledge of platform windows, used when the backend
                // cannot report io.MouseHoveredViewport.
                if (g.PlatformIO.Platform_GetWindowFocus != null && platformFuncsAvailable)
                {
                    bool isFocused = g.PlatformIO.Platform_GetWindowFocus(viewport);
                    if (isFocused)
                        viewport.Flags |= ImGuiViewportFlags.IsFocused;
                    else
                        viewport.Flags &= ~ImGuiViewportFlags.IsFocused;
                    if (isFocused)
                        focusedViewport = viewport;
                }
            }

            // The focused viewport changed: update the z-order stamp and optionally move the ImGui
            // focus to the focused viewport's window.
            if (focusedViewport != null && g.PlatformLastFocusedViewportId != focusedViewport.ID)
            {
                ImGuiViewport? previousFocusedViewport = FindViewportByID(g.PlatformLastFocusedViewportId);
                bool previousFocusedHasBeenDestroyed = previousFocusedViewport == null || !previousFocusedViewport.PlatformWindowCreated;
                if (focusedViewport.LastFocusedStampCount != g.ViewportFocusedStampCount)
                    focusedViewport.LastFocusedStampCount = ++g.ViewportFocusedStampCount;
                g.PlatformLastFocusedViewportId = focusedViewport.ID;

                bool applyImGuiFocus = !IsAnyMouseDown() && !previousFocusedHasBeenDestroyed;
                if (applyImGuiFocus && g.IO.ConfigViewportsPlatformFocusSetsImGuiFocus)
                {
                    focusedViewport.LastFocusedHadNavWindow |= g.NavWindow != null && g.NavWindow.Viewport == focusedViewport;
                    ImGuiFocusRequestFlags focusRequestFlags =
                        ImGuiFocusRequestFlags.UnlessBelowModal | ImGuiFocusRequestFlags.RestoreFocusedChild;
                    if (focusedViewport.Window != null)
                        FocusWindow(focusedViewport.Window, focusRequestFlags);
                    else if (focusedViewport.LastFocusedHadNavWindow)
                        FocusTopMostWindowUnderOne(null, null, focusedViewport, focusRequestFlags);
                    else
                        FocusWindow(null, focusRequestFlags);
                }
            }
            if (focusedViewport != null)
                focusedViewport.LastFocusedHadNavWindow = g.NavWindow != null && g.NavWindow.Viewport == focusedViewport;
        }

        ImGuiViewportP mainViewport = g.Viewports[0];
        ImAssert.Check(mainViewport.ID == ImGuiInternalConstants.ViewportDefaultId, "Invalid main viewport identifier.");
        ImAssert.Check(mainViewport.Window == null, "The main viewport must not be owned by a window.");

        ImVec2 mainViewportPos = viewportsEnabled && g.PlatformIO.Platform_GetWindowPos != null
            ? g.PlatformIO.Platform_GetWindowPos(mainViewport)
            : new ImVec2(0.0f, 0.0f);
        ImVec2 mainViewportSize = g.IO.DisplaySize;
        if (viewportsEnabled && (mainViewport.Flags & ImGuiViewportFlags.IsMinimized) != 0)
        {
            // Preserve the last position and size while the main platform window is minimized; the
            // backend keeps reporting the pre-minimized rectangle.
            mainViewportPos = mainViewport.Pos;
            mainViewportSize = mainViewport.Size;
        }

        // AddUpdateViewport(NULL, IMGUI_VIEWPORT_DEFAULT_ID,.) update branch
        // position and size because it is driven by io.DisplaySize.
        ImVec2 previousPos = mainViewport.Pos;
        ImVec2 previousSize = mainViewport.Size;
        if (!mainViewport.PlatformRequestMove || mainViewport.ID == ImGuiInternalConstants.ViewportDefaultId)
            mainViewport.Pos = mainViewportPos;
        if (!mainViewport.PlatformRequestResize || mainViewport.ID == ImGuiInternalConstants.ViewportDefaultId)
            mainViewport.Size = mainViewportSize;
        mainViewport.Flags = ImGuiViewportFlags.OwnedByApp
            | ImGuiViewportFlags.CanHostOtherWindows
            | ImGuiViewportFlags.IsPlatformWindow
            | (mainViewport.Flags & (ImGuiViewportFlags.IsMinimized | ImGuiViewportFlags.IsFocused));
        if (previousPos != mainViewport.Pos || previousSize != mainViewport.Size)
            UpdateViewportPlatformMonitor(mainViewport);
        mainViewport.Window = null;
        mainViewport.LastFrameActive = g.FrameCount;
        mainViewport.UpdateWorkRect();

        bool gcAll = g.GcCompactAll || g.IO.ConfigMemoryCompactTimer < 0.0f;
        float memoryCompactStartTime = gcAll ? float.MaxValue : (float)g.Time - g.IO.ConfigMemoryCompactTimer;
        g.CurrentDpiScale = 0.0f;
        g.CurrentViewport = null;
        g.MouseViewport = null;
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            viewport.Idx = n;

            // Erase unused viewports; the main viewport is never erased
            if (n > 0 && viewport.LastFrameActive < g.FrameCount - 2)
            {
                DestroyViewport(viewport);
                n--;
                continue;
            }

            // Read the platform window position, size and framebuffer density early in the frame, so
            // OS-driven moves and resizes have no frame of lag
            bool platformFuncsAvailable = viewport.PlatformWindowCreated;
            if (viewportsEnabled)
            {
                if ((viewport.Flags & ImGuiViewportFlags.IsMinimized) == 0 && platformFuncsAvailable)
                {
                    if (viewport.PlatformRequestMove && g.PlatformIO.Platform_GetWindowPos != null)
                        viewport.Pos = viewport.LastPlatformPos = g.PlatformIO.Platform_GetWindowPos(viewport);
                    if (viewport.PlatformRequestResize && g.PlatformIO.Platform_GetWindowSize != null)
                        viewport.Size = viewport.LastPlatformSize = g.PlatformIO.Platform_GetWindowSize(viewport);
                    if (g.PlatformIO.Platform_GetWindowFramebufferScale != null)
                        viewport.FramebufferScale = g.PlatformIO.Platform_GetWindowFramebufferScale(viewport);
                }
            }

            // Update the monitor association from the viewport rectangle
            UpdateViewportPlatformMonitor(viewport);

            // Lock down the space taken by menu bars and status bars and query the initial insets from
            // the backend.
            viewport.WorkInsetMin = viewport.BuildWorkInsetMin;
            viewport.WorkInsetMax = viewport.BuildWorkInsetMax;
            viewport.BuildWorkInsetMin = new ImVec2(0.0f, 0.0f);
            viewport.BuildWorkInsetMax = new ImVec2(0.0f, 0.0f);
            if (g.PlatformIO.Platform_GetWindowWorkAreaInsets != null && platformFuncsAvailable)
            {
                ImVec4 insets = g.PlatformIO.Platform_GetWindowWorkAreaInsets(viewport);
                ImAssert.Check(insets.X >= 0.0f && insets.Y >= 0.0f && insets.Z >= 0.0f && insets.W >= 0.0f,
                    "Invalid viewport work-area insets.");
                viewport.BuildWorkInsetMin = new ImVec2(insets.X, insets.Y);
                viewport.BuildWorkInsetMax = new ImVec2(insets.Z, insets.W);
            }
            viewport.UpdateWorkRect();

            // Garbage collect transient buffers of recently used background/foreground draw lists
            for (int dl = 0; dl < viewport.BgFgDrawLists.Length; dl++)
            {
                if (viewport.BgFgDrawListsLastTimeActive[dl] < memoryCompactStartTime && viewport.BgFgDrawLists[dl] != null)
                    viewport.BgFgDrawLists[dl] = null;
            }

            // Reset alpha every frame. Users of transparency (docking) must request a lower alpha back.
            viewport.Alpha = 1.0f;

            // Translate Dear ImGui windows when a host viewport moves. This also keeps windows in the
            // same place when ImGuiConfigFlags.ViewportsEnable is toggled
            ImVec2 viewportDeltaPos = viewport.Pos - viewport.LastPos;
            if ((viewport.Flags & ImGuiViewportFlags.CanHostOtherWindows) != 0
                && (viewportDeltaPos.X != 0.0f || viewportDeltaPos.Y != 0.0f))
            {
                TranslateWindowsInViewport(viewport, viewport.LastPos, viewport.Pos, viewport.LastSize, viewport.Size);
            }

            // Update DPI scale.
            float newDpiScale;
            if (g.PlatformIO.Platform_GetWindowDpiScale != null && platformFuncsAvailable)
                newDpiScale = g.PlatformIO.Platform_GetWindowDpiScale(viewport);
            else if (viewport.PlatformMonitor != -1 && viewport.PlatformMonitor < g.PlatformIO.Monitors.Count)
                newDpiScale = g.PlatformIO.Monitors[viewport.PlatformMonitor].DpiScale;
            else
                newDpiScale = viewport.DpiScale != 0.0f ? viewport.DpiScale : 1.0f;
            ImAssert.Check(newDpiScale > 0.0f && newDpiScale < 99.0f, "Invalid DPI scale.");
            if (viewport.DpiScale != 0.0f && newDpiScale != viewport.DpiScale)
            {
                float scaleFactor = newDpiScale / viewport.DpiScale;
                if (g.IO.ConfigDpiScaleViewports)
                    ScaleWindowsInViewport(viewport, scaleFactor);
            }
            viewport.DpiScale = newDpiScale;
        }

        // Update the fallback monitor and the bounding box of every monitor
        g.PlatformMonitorsFullWorkRect = new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);
        if (g.PlatformIO.Monitors.Count == 0)
        {
            ImGuiPlatformMonitor fallback = g.FallbackMonitor;
            fallback.MainPos = mainViewport.Pos;
            fallback.MainSize = mainViewport.Size;
            fallback.WorkPos = mainViewport.WorkPos;
            fallback.WorkSize = mainViewport.WorkSize;
            fallback.DpiScale = mainViewport.DpiScale;
            g.PlatformMonitorsFullWorkRect.Add(fallback.WorkPos);
            g.PlatformMonitorsFullWorkRect.Add(fallback.WorkPos + fallback.WorkSize);
        }
        else
        {
            g.FallbackMonitor = g.PlatformIO.Monitors[0];
        }
        for (int monitorN = 0; monitorN < g.PlatformIO.Monitors.Count; monitorN++)
        {
            ImGuiPlatformMonitor monitor = g.PlatformIO.Monitors[monitorN];
            g.PlatformMonitorsFullWorkRect.Add(monitor.WorkPos);
            g.PlatformMonitorsFullWorkRect.Add(monitor.WorkPos + monitor.WorkSize);
        }

        if (!viewportsEnabled)
        {
            g.MouseViewport = mainViewport;
            return;
        }

        // Mouse handling: choose the mouse viewport for this frame between the active/focused viewport
        // and the hovered viewport.
        ImGuiViewportP? viewportHovered = null;
        if ((g.IO.BackendFlags & ImGuiBackendFlags.HasMouseHoveredViewport) != 0)
        {
            viewportHovered = g.IO.MouseHoveredViewport != 0
                ? FindViewportByID(g.IO.MouseHoveredViewport) as ImGuiViewportP
                : null;
            if (viewportHovered != null && (viewportHovered.Flags & ImGuiViewportFlags.NoInputs) != 0)
                viewportHovered = FindHoveredViewportFromPlatformWindowStack(g.IO.MousePos);
        }
        else
        {
            viewportHovered = FindHoveredViewportFromPlatformWindowStack(g.IO.MousePos);
        }
        if (viewportHovered != null)
            g.MouseLastHoveredViewport = viewportHovered;
        else if (g.MouseLastHoveredViewport == null)
            g.MouseLastHoveredViewport = g.Viewports[0];

        if (g.MovingWindow != null && g.MovingWindow.Viewport != null)
            g.MouseViewport = g.MovingWindow.Viewport;
        else
            g.MouseViewport = g.MouseLastHoveredViewport;

        bool isMouseDraggingWithExpectedDestination = g.DragDropActive;
        if (isMouseDraggingWithExpectedDestination && viewportHovered == null)
            viewportHovered = g.MouseLastHoveredViewport;
        if ((isMouseDraggingWithExpectedDestination || g.ActiveId == 0 || !IsAnyMouseDown())
            && viewportHovered != null
            && viewportHovered != g.MouseViewport
            && (viewportHovered.Flags & ImGuiViewportFlags.NoInputs) == 0)
        {
            g.MouseViewport = viewportHovered;
        }
        ImAssert.Check(g.MouseViewport != null, "A mouse viewport must be selected.");
    }

    /// <summary>
    /// Advances the end-of-frame viewport bookkeeping: records the last position and size, rebuilds the
    /// user-facing viewport list, and clears the main viewport's platform request flags. Mirrors
    /// canonical <c>ImGui::UpdateViewportsEndFrame</c>
    /// </summary>
    /// <remarks>
    /// The user-facing list filters out viewports that were not submitted this frame, have a zero-sized
    /// rectangle, or host a hidden window. The main viewport is always included.
    /// </remarks>
    private static void UpdateViewportsEndFrame()
    {
        ImGuiContext g = GetContext();
        g.PlatformIO.Viewports.Clear();
        for (int i = 0; i < g.Viewports.Count; i++)
        {
            ImGuiViewportP viewport = g.Viewports[i];
            viewport.LastPos = viewport.Pos;
            viewport.LastSize = viewport.Size;
            if (viewport.LastFrameActive < g.FrameCount || viewport.Size.X <= 0.0f || viewport.Size.Y <= 0.0f)
            {
                if (i > 0) // Always include the main viewport in the list.
                    continue;
            }
            if (viewport.Window != null && !IsWindowActiveAndVisible(viewport.Window))
                continue;
            if (i > 0)
                ImAssert.Check(viewport.Window != null, "Every secondary viewport must be owned by a window.");
            g.PlatformIO.Viewports.Add(viewport);
        }
        if (g.Viewports.Count > 0)
            g.Viewports[0].ClearRequestFlags(); // UpdatePlatformWindows() will not clear them and may not even be called
    }

    /// <summary>
    /// Finds a viewport by its identifier. Mirrors canonical <c>ImGui::FindViewportByID</c>
    /// </summary>
    /// <param name="viewportId">Identifier to search for.</param>
    /// <returns>The matching viewport, or <see langword="null"/> when no viewport has that identifier.</returns>
    public static ImGuiViewport? FindViewportByID(uint viewportId)
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            if (g.Viewports[n].ID == viewportId)
                return g.Viewports[n];
        }
        return null;
    }

    /// <summary>
    /// Finds a viewport by the platform handle the backend stored on it. Mirrors canonical
    /// <c>ImGui::FindViewportByPlatformHandle</c>.
    /// </summary>
    /// <param name="platformHandle">Platform handle to search for.</param>
    /// <returns>The matching viewport, or <see langword="null"/> when no viewport has that handle.</returns>
    /// <remarks>
    /// Canonical compares the handle directly, so on startup the main viewport with its default zero
    /// handle matches a zero query. Callers that treat zero as "no handle" must test it themselves.
    /// </remarks>
    public static ImGuiViewport? FindViewportByPlatformHandle(IntPtr platformHandle)
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            if (g.Viewports[n].PlatformHandle == platformHandle)
                return g.Viewports[n];
        }
        return null;
    }

    /// <summary>
    /// Finds the monitor with the largest intersection area with the given rectangle, defaulting to the
    /// first monitor. Mirrors canonical <c>ImGui::FindPlatformMonitorForRect</c>
    /// </summary>
    /// <param name="rect">Rectangle to place on a monitor.</param>
    /// <returns>The monitor index, or -1 when no monitor is registered.</returns>
    private static int FindPlatformMonitorForRect(ImRect rect)
    {
        ImGuiContext g = GetContext();
        int monitorCount = g.PlatformIO.Monitors.Count;
        if (monitorCount <= 1)
            return monitorCount - 1;

        // A minimum threshold of 1.0 prevents a zero-sized rectangle from falsely matching, which
        // matters for tooltips that resize down to zero at first.
        float surfaceThreshold = MathF.Max(rect.GetWidth() * rect.GetHeight() * 0.5f, 1.0f);
        int bestMonitorN = 0; // Default to the first monitor as a fallback.
        float bestMonitorSurface = 0.001f;
        for (int monitorN = 0; monitorN < monitorCount && bestMonitorSurface < surfaceThreshold; monitorN++)
        {
            ImGuiPlatformMonitor monitor = g.PlatformIO.Monitors[monitorN];
            ImRect monitorRect = new ImRect(monitor.MainPos, monitor.MainPos + monitor.MainSize);
            if (monitorRect.Contains(rect))
                return monitorN;
            ImRect overlappingRect = rect;
            overlappingRect.ClipWithFull(monitorRect);
            float overlappingSurface = overlappingRect.GetWidth() * overlappingRect.GetHeight();
            if (overlappingSurface < bestMonitorSurface)
                continue;
            bestMonitorSurface = overlappingSurface;
            bestMonitorN = monitorN;
        }
        return bestMonitorN;
    }

    /// <summary>
    /// Updates a viewport's monitor association from its main rectangle. Mirrors canonical
    /// <c>ImGui::UpdateViewportPlatformMonitor</c>.
    /// </summary>
    /// <param name="viewport">Viewport whose monitor association is refreshed.</param>
    private static void UpdateViewportPlatformMonitor(ImGuiViewportP viewport)
    {
        viewport.PlatformMonitor = (short)FindPlatformMonitorForRect(viewport.GetMainRect());
    }

    /// <summary>
    /// Removes a viewport from the internal list and releases its platform window. Mirrors canonical
    /// <c>ImGui::DestroyViewport</c>.
    /// </summary>
    /// <param name="viewport">Viewport to remove; it must be a secondary viewport.</param>
    /// <remarks>
    /// Windows that referenced the viewport fall back to the master viewport identifier, which is how
    /// canonical models their association after the pointer is cleared. The main viewport is never
    /// destroyed by this path because the caller only erases entries above index zero.
    /// </remarks>
    private static void DestroyViewport(ImGuiViewportP viewport)
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            if (window.Viewport != viewport)
                continue;
            window.Viewport = null;
            window.ViewportOwned = false;
        }
        if (ReferenceEquals(g.MouseLastHoveredViewport, viewport))
            g.MouseLastHoveredViewport = null;

        DestroyPlatformWindow(viewport);
        ImAssert.Check(!g.PlatformIO.Viewports.Contains(viewport), "The user-facing list must not retain a destroyed viewport.");
        ImAssert.Check(viewport.Idx >= 0 && viewport.Idx < g.Viewports.Count && ReferenceEquals(g.Viewports[viewport.Idx], viewport),
            "A destroyed viewport must occupy its recorded index.");
        g.Viewports.RemoveAt(viewport.Idx);
    }

    /// <summary>
    /// Translates the windows hosted by a moved host viewport. Mirrors canonical
    /// <c>ImGui::TranslateWindowsInViewport</c>.
    /// </summary>
    /// <param name="viewport">Host viewport that moved.</param>
    /// <param name="oldPos">Previous viewport position.</param>
    /// <param name="newPos">Current viewport position.</param>
    /// <param name="oldSize">Previous viewport size.</param>
    /// <param name="newSize">Current viewport size.</param>
    /// <remarks>
    /// Every window is translated when the multi-viewport flag was just toggled, so windows keep their
    /// absolute position while switching between OS-window-local and absolute coordinates. Otherwise a
    /// window is translated only when it belongs to the viewport and either the viewport size is
    /// unchanged or the window still fits in the previous rectangle.
    /// </remarks>
    internal static void TranslateWindowsInViewport(ImGuiViewportP viewport, ImVec2 oldPos, ImVec2 newPos, ImVec2 oldSize, ImVec2 newSize)
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(viewport.Window == null && (viewport.Flags & ImGuiViewportFlags.CanHostOtherWindows) != 0,
            "Only a host viewport translates the windows it owns.");

        bool translateAllWindows = (g.ConfigFlagsCurrFrame & ImGuiConfigFlags.ViewportsEnable)
            != (g.ConfigFlagsLastFrame & ImGuiConfigFlags.ViewportsEnable);
        ImRect testStillFitRect = new ImRect(oldPos, oldPos + oldSize);
        ImVec2 deltaPos = newPos - oldPos;
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            if (translateAllWindows
                || (window.Viewport == viewport
                    && (oldSize == newSize || testStillFitRect.Contains(window.Rect()))))
            {
                TranslateWindow(window, deltaPos);
            }
        }
    }

    /// <summary>
    /// Scales the windows hosted by a viewport. Mirrors canonical
    /// <c>ImGui::ScaleWindowsInViewport</c>.
    /// </summary>
    /// <param name="viewport">Viewport whose DPI scale changed.</param>
    /// <param name="scale">Multiplier applied to the hosted windows.</param>
    /// <remarks>
    /// Scaling is lossy because positions and sizes are truncated to whole pixels. A viewport owned by
    /// a single window scales only that window; a host viewport scales every window assigned to it.
    /// </remarks>
    internal static void ScaleWindowsInViewport(ImGuiViewportP viewport, float scale)
    {
        ImGuiContext g = GetContext();
        if (viewport.Window != null)
        {
            ScaleWindow(viewport.Window, scale);
        }
        else
        {
            for (int n = 0; n < g.Windows.Count; n++)
            {
                if (g.Windows[n].Viewport == viewport)
                    ScaleWindow(g.Windows[n], scale);
            }
        }
    }

    /// <summary>
    /// Translates a window and the layout rectangles that would otherwise lag. Mirrors canonical
    /// <c>TranslateWindow</c>.
    /// </summary>
    /// <param name="window">Window to translate.</param>
    /// <param name="delta">Offset applied to every translated member.</param>
    private static void TranslateWindow(ImGuiWindow window, ImVec2 delta)
    {
        window.Pos += delta;

        // ImRect is a value type in this port, so each rectangle is read, translated and written back
        // instead of canonical's in-place Translate call.
        ImRect clipRect = window.ClipRect;
        clipRect.Translate(delta);
        window.ClipRect = clipRect;
        ImRect outerRectClipped = window.OuterRectClipped;
        outerRectClipped.Translate(delta);
        window.OuterRectClipped = outerRectClipped;
        ImRect innerRect = window.InnerRect;
        innerRect.Translate(delta);
        window.InnerRect = innerRect;

        window.DC.CursorPos += delta;
        window.DC.CursorStartPos += delta;
        window.DC.CursorMaxPos += delta;
        window.DC.IdealMaxPos += delta;
    }

    /// <summary>
    /// Scales a window's position and sizes around its viewport origin. Mirrors canonical
    /// <c>ScaleWindow</c>.
    /// </summary>
    /// <param name="window">Window to scale.</param>
    /// <param name="scale">Multiplier applied around the owning viewport's origin.</param>
    private static void ScaleWindow(ImGuiWindow window, float scale)
    {
        ImVec2 origin = window.Viewport!.Pos;
        window.Pos = new ImVec2(
            MathF.Floor((window.Pos.X - origin.X) * scale + origin.X),
            MathF.Floor((window.Pos.Y - origin.Y) * scale + origin.Y));
        window.Size = TruncateVector(window.Size * scale);
        window.SizeFull = TruncateVector(window.SizeFull * scale);
        window.ContentSize = TruncateVector(window.ContentSize * scale);
    }

    /// <summary>
    /// Searches for the best hovered viewport from the platform window stack. Mirrors canonical
    /// <c>ImGui::FindHoveredViewportFromPlatformWindowStack</c>
    /// </summary>
    /// <param name="mousePlatformPos">Mouse position in desktop coordinates.</param>
    /// <returns>The best hovered viewport, or <see langword="null"/> when none matches.</returns>
    /// <remarks>
    /// A viewport is a candidate when it accepts inputs, is not minimized, contains the mouse position,
    /// and has a created platform window. The candidate with the highest focus stamp wins, which is the
    /// core's approximation of platform z-order.
    /// </remarks>
    internal static ImGuiViewportP? FindHoveredViewportFromPlatformWindowStack(ImVec2 mousePlatformPos)
    {
        ImGuiContext g = GetContext();
        ImGuiViewportP? bestCandidate = null;
        for (int n = 0; n < g.Viewports.Count; n++)
        {
            ImGuiViewportP viewport = g.Viewports[n];
            if ((viewport.Flags & (ImGuiViewportFlags.NoInputs | ImGuiViewportFlags.IsMinimized)) != 0)
                continue;
            if (!viewport.GetMainRect().Contains(mousePlatformPos))
                continue;
            if (bestCandidate == null || bestCandidate.LastFocusedStampCount < viewport.LastFocusedStampCount)
            {
                if (viewport.PlatformWindowCreated)
                    bestCandidate = viewport;
            }
        }
        return bestCandidate;
    }

    /// <summary>
    /// Returns the monitor that hosts a viewport, falling back to the fallback monitor. Mirrors
    /// canonical <c>ImGui::GetViewportPlatformMonitor</c>.
    /// </summary>
    /// <param name="viewport">Viewport whose monitor is queried.</param>
    /// <returns>
    /// The registered monitor at the viewport's monitor index, or <see cref="ImGuiContext.FallbackMonitor"/>
    /// when the viewport is not assigned to a registered monitor.
    /// </returns>
    /// <remarks>
    /// The returned instance is owned by the context and must not be held across frames, because the
    /// monitor list may be replaced by the application between frames.
    /// </remarks>
    public static ImGuiPlatformMonitor GetViewportPlatformMonitor(ImGuiViewport viewport)
    {
        ImGuiContext g = GetContext();
        ImGuiViewportP viewportP = viewport as ImGuiViewportP ?? g.Viewports[0];
        int monitorIndex = viewportP.PlatformMonitor;
        if (monitorIndex >= 0 && monitorIndex < g.PlatformIO.Monitors.Count)
            return g.PlatformIO.Monitors[monitorIndex];
        return g.FallbackMonitor;
    }

    /// <summary>
    /// Destroys the platform window of one viewport. Mirrors canonical
    /// <c>ImGui::DestroyPlatformWindow</c>.
    /// </summary>
    /// <param name="viewport">Viewport whose platform window is destroyed.</param>
    /// <remarks>
    /// The renderer window is destroyed before the platform window, matching the documented ordering.
    /// The main viewport keeps <see cref="ImGuiViewport.PlatformWindowCreated"/> set because it is
    /// established during initialization and is not recreated by the backend.
    /// </remarks>
    internal static void DestroyPlatformWindow(ImGuiViewportP viewport)
    {
        ImGuiContext g = GetContext();
        if (viewport.PlatformWindowCreated)
        {
            if (g.PlatformIO.Renderer_DestroyWindow != null)
                g.PlatformIO.Renderer_DestroyWindow(viewport);
            if (g.PlatformIO.Platform_DestroyWindow != null)
                g.PlatformIO.Platform_DestroyWindow(viewport);
            ImAssert.Check(viewport.RendererUserData == IntPtr.Zero && viewport.PlatformUserData == IntPtr.Zero,
                "The renderer and platform backends must clear their user data when destroying a window.");
            if (viewport.ID != ImGuiInternalConstants.ViewportDefaultId)
                viewport.PlatformWindowCreated = false;
        }
        else
        {
            ImAssert.Check(viewport.RendererUserData == IntPtr.Zero
                && viewport.PlatformUserData == IntPtr.Zero
                && viewport.PlatformHandle == IntPtr.Zero,
                "A viewport without a platform window must not retain backend handles.");
        }
        viewport.RendererUserData = viewport.PlatformUserData = IntPtr.Zero;
        viewport.PlatformHandle = IntPtr.Zero;
        viewport.ClearRequestFlags();
    }

    /// <summary>
    /// Destroys the platform windows of every viewport, including the main viewport. Mirrors canonical
    /// <c>ImGui::DestroyPlatformWindows</c>.
    /// </summary>
    /// <remarks>
    /// The main viewport is included so the backend can clear data it stored there. Backends are
    /// expected to tolerate a destroy call for a viewport they never created.
    /// </remarks>
    public static void DestroyPlatformWindows()
    {
        ImGuiContext g = GetContext();
        for (int n = 0; n < g.Viewports.Count; n++)
            DestroyPlatformWindow(g.Viewports[n]);
    }

    /// <summary>
    /// Creates, updates and destroys the platform windows of the secondary viewports after a frame has
    /// ended. Mirrors canonical <c>ImGui::UpdatePlatformWindows</c>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The main viewport (index zero) is skipped because the application owns it. The call must follow
    /// <see cref="EndFrame"/> or <see cref="Render"/>, and it is a no-op when multi-viewport support is
    /// disabled or the backend does not advertise both viewport backend flags.
    /// </para>
    /// <para>
    /// Each callback is invoked only when the corresponding handler is installed, so a partially
    /// implemented backend stays safe while the core still tracks request flags.
    /// </para>
    /// </remarks>
    public static void UpdatePlatformWindows()
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.FrameCountEnded == g.FrameCount, "Forgot to call Render() or EndFrame() before UpdatePlatformWindows()?");
        ImAssert.Check(g.FrameCountPlatformEnded < g.FrameCount, "UpdatePlatformWindows() must be called at most once per frame.");
        g.FrameCountPlatformEnded = g.FrameCount;

        bool platformHasViewports = (g.IO.BackendFlags & ImGuiBackendFlags.PlatformHasViewports) != 0;
        bool rendererHasViewports = (g.IO.BackendFlags & ImGuiBackendFlags.RendererHasViewports) != 0;
        if ((g.ConfigFlagsCurrFrame & ImGuiConfigFlags.ViewportsEnable) == 0 || !(platformHasViewports && rendererHasViewports))
            return;

        for (int i = 1; i < g.Viewports.Count; i++)
        {
            ImGuiViewportP viewport = g.Viewports[i];

            // Destroy the platform window when the viewport was not submitted recently or hosts a
            // hidden window.
            bool destroyPlatformWindow = viewport.LastFrameActive < g.FrameCount - 1;
            destroyPlatformWindow |= viewport.Window != null && !IsWindowActiveAndVisible(viewport.Window);
            if (destroyPlatformWindow)
            {
                DestroyPlatformWindow(viewport);
                continue;
            }

            // A window appearing directly in a new viewport has no size on its first frame.
            if (viewport.LastFrameActive < g.FrameCount || viewport.Size.X <= 0.0f || viewport.Size.Y <= 0.0f)
                continue;

            bool isNewPlatformWindow = !viewport.PlatformWindowCreated;
            if (isNewPlatformWindow)
            {
                g.PlatformIO.Platform_CreateWindow?.Invoke(viewport);
                g.PlatformIO.Renderer_CreateWindow?.Invoke(viewport);
                g.PlatformWindowsCreatedCount++;
                viewport.LastNameHash = 0;
                // Clearing these enforces a position and size call below, before the window is shown.
                viewport.LastPlatformPos = new ImVec2(float.MaxValue, float.MaxValue);
                viewport.LastPlatformSize = new ImVec2(float.MaxValue, float.MaxValue);
                // The renderer create handler is expected to have sized its resources already.
                viewport.LastRendererSize = viewport.Size;
                viewport.PlatformWindowCreated = true;
            }

            // Apply position and size from the core to the platform and renderer backends.
            if ((viewport.LastPlatformPos.X != viewport.Pos.X || viewport.LastPlatformPos.Y != viewport.Pos.Y)
                && !viewport.PlatformRequestMove)
            {
                g.PlatformIO.Platform_SetWindowPos?.Invoke(viewport, viewport.Pos);
            }
            if ((viewport.LastPlatformSize.X != viewport.Size.X || viewport.LastPlatformSize.Y != viewport.Size.Y)
                && !viewport.PlatformRequestResize)
            {
                g.PlatformIO.Platform_SetWindowSize?.Invoke(viewport, viewport.Size);
            }
            if ((viewport.LastRendererSize.X != viewport.Size.X || viewport.LastRendererSize.Y != viewport.Size.Y)
                && g.PlatformIO.Renderer_SetWindowSize != null)
            {
                g.PlatformIO.Renderer_SetWindowSize(viewport, viewport.Size);
            }
            viewport.LastPlatformPos = viewport.Pos;
            viewport.LastPlatformSize = viewport.LastRendererSize = viewport.Size;

            // Update the title when it changed.
            ImGuiWindow? windowForTitle = GetWindowForTitleDisplay(viewport.Window);
            if (windowForTitle != null)
            {
                string title = FindRenderedTextEnd(windowForTitle.Name);
                uint titleHash = ImHashStr(title);
                if (viewport.LastNameHash != titleHash)
                {
                    g.PlatformIO.Platform_SetWindowTitle?.Invoke(viewport, title);
                    viewport.LastNameHash = titleHash;
                }
            }

            // Update alpha when it changed.
            if (viewport.LastAlpha != viewport.Alpha)
                g.PlatformIO.Platform_SetWindowAlpha?.Invoke(viewport, viewport.Alpha);
            viewport.LastAlpha = viewport.Alpha;

            // Optional general-purpose per-frame hook for backend bookkeeping.
            g.PlatformIO.Platform_UpdateWindow?.Invoke(viewport);

            if (isNewPlatformWindow)
            {
                // Let the window settle before allowing it to steal focus on startup.
                if (g.FrameCount < 3)
                    viewport.Flags |= ImGuiViewportFlags.NoFocusOnAppearing;
                g.PlatformIO.Platform_ShowWindow?.Invoke(viewport);
            }

            viewport.ClearRequestFlags();
        }
    }

    /// <summary>
    /// Returns the window whose title is displayed for a viewport, resolving a dock-node host to its
    /// visible window. Mirrors canonical <c>GetWindowForTitleDisplay</c>
    /// </summary>
    /// <param name="window">Viewport window, or <see langword="null"/> for the main viewport.</param>
    /// <returns>The window whose title is used, or <see langword="null"/> when there is none.</returns>
    private static ImGuiWindow? GetWindowForTitleDisplay(ImGuiWindow? window)
    {
        if (window == null)
            return null;
        return window.DockNodeAsHost != null ? window.DockNodeAsHost.VisibleWindow : window;
    }

    /// <summary>
    /// Renders and presents the secondary platform windows through the configured renderer helpers.
    /// Mirrors canonical <c>ImGui::RenderPlatformWindowsDefault</c>
    /// </summary>
    /// <param name="platformRenderArg">Argument forwarded to the platform render and swap handlers.</param>
    /// <param name="rendererRenderArg">Argument forwarded to the renderer render and swap handlers.</param>
    /// <remarks>
    /// The main viewport is skipped because the application renders it directly. Minimized viewports are
    /// skipped, and all viewports are rendered before any is presented, which is the canonical ordering
    /// that lets a backend synchronize multiple swap chains.
    /// </remarks>
    public static void RenderPlatformWindowsDefault(IntPtr platformRenderArg = default, IntPtr rendererRenderArg = default)
    {
        ImGuiPlatformIO platformIo = GetPlatformIO();
        for (int i = 1; i < platformIo.Viewports.Count; i++)
        {
            ImGuiViewport viewport = platformIo.Viewports[i];
            if ((viewport.Flags & ImGuiViewportFlags.IsMinimized) != 0)
                continue;
            platformIo.Platform_RenderWindow?.Invoke(viewport, platformRenderArg);
            platformIo.Renderer_RenderWindow?.Invoke(viewport, rendererRenderArg);
        }
        for (int i = 1; i < platformIo.Viewports.Count; i++)
        {
            ImGuiViewport viewport = platformIo.Viewports[i];
            if ((viewport.Flags & ImGuiViewportFlags.IsMinimized) != 0)
                continue;
            platformIo.Platform_SwapBuffers?.Invoke(viewport, platformRenderArg);
            platformIo.Renderer_SwapBuffers?.Invoke(viewport, rendererRenderArg);
        }
    }

    /// <summary>
    /// Compares two IME data records field by field. Replaces canonical's
    /// <c>memcmp(&amp;ime_data, &amp;ime_data_prev, sizeof(ImGuiPlatformImeData))</c>
    ///. because the port models the record as a managed class.
    /// </summary>
    /// <param name="left">Current-frame IME data.</param>
    /// <param name="right">Previous-frame IME data.</param>
    /// <returns><see langword="true"/> when every field is equal.</returns>
    private static bool ImeDataEquals(ImGuiPlatformImeData left, ImGuiPlatformImeData right)
    {
        return left.WantVisible == right.WantVisible
            && left.WantTextInput == right.WantTextInput
            && left.InputPos == right.InputPos
            && left.InputLineHeight == right.InputLineHeight
            && left.ViewportId == right.ViewportId;
    }

    #endregion

    #region [SECTION] MISC HELPERS/UTILITIES

    /// <summary>
    /// Compacts miscellaneous transient buffers. Mirrors canonical
    /// <c>ImGui::GcCompactTransientMiscBuffers</c>.
    /// </summary>
    /// <remarks>
    /// Canonical also destroys the stacked multi-select temporary data and compacts table settings
    /// storage. The multi-select temporary-data list is cleared here so its retained request buffers are
    /// released; the table settings compaction (chunk 5) has no storage on this port's context yet and is
    /// inert. Every registered font atlas is compacted through <see cref="ImFontAtlas.CompactCache"/>.
    /// </remarks>
    internal static void GcCompactTransientMiscBuffers()
    {
        ImGuiContext g = GetContext();
        g.ItemFlagsStack.Clear();
        g.GroupStack.Clear();
        g.InputTextLineIndex.Clear();
        g.MultiSelectTempDataStacked = 0;
        g.MultiSelectTempData.Clear();
        // TableGcCompactSettings(): chunk 5.
        for (int n = 0; n < g.FontAtlases.Count; n++)
            g.FontAtlases[n].CompactCache();
    }

    /// <summary>
    /// Releases a window's transient buffers while retaining its persistent data. Mirrors canonical
    /// <c>ImGui::GcCompactTransientWindowBuffers</c>.
    /// </summary>
    /// <param name="window">Window whose transient buffers are released.</param>
    /// <remarks>
    /// <see cref="ImGuiWindow"/>, <see cref="ImGuiWindowSettings"/>, the window name, its state storage
    /// and its legacy column storage are deliberately retained because they may hold useful data. The
    /// draw-list buffer capacities are remembered so <see cref="GcAwakeTransientWindowBuffers"/> can
    /// restore them without repeated growth.
    /// </remarks>
    internal static void GcCompactTransientWindowBuffers(ImGuiWindow window)
    {
        window.MemoryCompacted = true;
        window.MemoryDrawListIdxCapacity = Math.Min(
            (int)(window.DrawList.IdxBuffer.Count * 1.05f),
            window.DrawList.IdxBuffer.Capacity);
        window.MemoryDrawListVtxCapacity = Math.Min(
            (int)(window.DrawList.VtxBuffer.Count * 1.05f),
            window.DrawList.VtxBuffer.Capacity);
        window.IDStack.Clear();
        window.DrawList.ClearFreeMemory();
        window.DC.ChildWindows.Clear();
        window.DC.ItemWidthStack.Clear();
        window.DC.TextWrapPosStack.Clear();
    }

    /// <summary>
    /// Restores the buffer capacities released by <see cref="GcCompactTransientWindowBuffers"/>.
    /// Mirrors canonical <c>ImGui::GcAwakeTransientWindowBuffers</c>
    /// </summary>
    /// <param name="window">Window whose transient buffers are restored.</param>
    internal static void GcAwakeTransientWindowBuffers(ImGuiWindow window)
    {
        // The stored capacity of the draw-list buffers reduces growth-caused allocation and copy when
        // the window awakens. The other buffers tend to amortize much faster.
        window.MemoryCompacted = false;
        window.DrawList.IdxBuffer.EnsureCapacity(window.MemoryDrawListIdxCapacity);
        window.DrawList.VtxBuffer.EnsureCapacity(window.MemoryDrawListVtxCapacity);
        window.MemoryDrawListIdxCapacity = 0;
        window.MemoryDrawListVtxCapacity = 0;
    }

    /// <summary>
    /// Returns the version string of this port. Mirrors canonical <c>ImGui::GetVersion</c>
    /// </summary>
    /// <returns>The canonical version string, for example the canonical release version.</returns>
    public static string GetVersion()
    {
        return ImGuiVersion;
    }

    /// <summary>
    /// Returns the current context's input/output record. Mirrors canonical <c>ImGui::GetIO</c>
    /// </summary>
    /// <returns>The live input/output record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    public static ImGuiIO GetIO()
    {
        return GetContext().IO;
    }

    /// <summary>
    /// Returns a specific context's input/output record. Mirrors canonical
    /// <c>ImGui::GetIO(ImGuiContext*)</c> (, declared in
    /// </summary>
    /// <param name="context">Context whose input/output record is returned.</param>
    /// <returns>The live input/output record owned by <paramref name="context"/>.</returns>
    /// <remarks>
    /// The context-parameter overload lets a backend bound to a context reach it without changing the
    /// current context, matching <see cref="GetPlatformIO(ImGuiContext)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public static ImGuiIO GetIO(ImGuiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.IO;
    }

    /// <summary>
    /// Returns the current context's platform and renderer interface. Mirrors canonical
    /// <c>ImGui::GetPlatformIO</c>.
    /// </summary>
    /// <returns>The live platform interface owned by the current context.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    public static ImGuiPlatformIO GetPlatformIO()
    {
        return GetContext().PlatformIO;
    }

    /// <summary>
    /// Returns a specific context's platform and renderer interface. Mirrors canonical
    /// <c>ImGui::GetPlatformIO(ImGuiContext*)</c>., which
    /// lets a backend bound to a context reach it without changing the current context.
    /// </summary>
    /// <param name="context">Context whose platform interface is returned.</param>
    /// <returns>The platform interface owned by <paramref name="context"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public static ImGuiPlatformIO GetPlatformIO(ImGuiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.PlatformIO;
    }

    /// <summary>
    /// Returns the time accumulated by <see cref="NewFrame"/>, in seconds. Mirrors canonical
    /// <c>ImGui::GetTime</c>.
    /// </summary>
    /// <returns>The accumulated time in seconds.</returns>
    public static double GetTime()
    {
        return GetContext().Time;
    }

    /// <summary>
    /// Returns the global frame counter, incremented once per <see cref="NewFrame"/>. Mirrors canonical
    /// <c>ImGui::GetFrameCount</c>.
    /// </summary>
    /// <returns>The number of frames started so far.</returns>
    public static int GetFrameCount()
    {
        return GetContext().FrameCount;
    }

    /// <summary>
    /// Returns the clipboard text provided by the host. Mirrors canonical
    /// <c>ImGui::GetClipboardText</c>.
    /// </summary>
    /// <returns>The clipboard text, or an empty string when no handler is installed or it fails.</returns>
    /// <remarks>
    /// Canonical reads <c>PlatformIO.Platform_GetClipboardTextFn</c> and returns <c>NULL</c> on failure.
    /// This port reads that handler first and falls back to the obsolete
    /// <see cref="ImGuiIO.GetClipboardTextFn"/> callback without copying it, because
    /// the two reads are equivalent and the fallback keeps the handler live if the host changes it.
    /// Canonical <c>NULL</c> maps to an empty string because <c>CONTRACT.md</c> §5 declares a
    /// non-nullable return type.
    /// </remarks>
    public static string GetClipboardText()
    {
        ImGuiContext g = GetContext();
        Func<ImGuiContext, string?>? platformGetter = g.PlatformIO.Platform_GetClipboardTextFn;
        if (platformGetter != null)
            return platformGetter(g) ?? string.Empty;
        Func<object?, string?>? getter = g.IO.GetClipboardTextFn;
        if (getter != null)
            return getter(g.IO.ClipboardUserData) ?? string.Empty;
        return Platform_GetClipboardTextDefault(g) ?? string.Empty;
    }

    /// <summary>
    /// Stores text into the host clipboard. Mirrors canonical
    /// <c>ImGui::SetClipboardText</c>.
    /// </summary>
    /// <param name="text">Text to store.</param>
    /// <remarks>
    /// Canonical writes through <c>PlatformIO.Platform_SetClipboardTextFn</c>. This port prefers that
    /// handler and falls back to the obsolete <see cref="ImGuiIO.SetClipboardTextFn"/> callback; see
    /// <see cref="GetClipboardText"/> for the rationale.
    /// </remarks>
    public static void SetClipboardText(string text)
    {
        ImGuiContext g = GetContext();
        if (g.PlatformIO.Platform_SetClipboardTextFn != null)
        {
            g.PlatformIO.Platform_SetClipboardTextFn(g, text);
            return;
        }
        if (g.IO.SetClipboardTextFn != null)
        {
            g.IO.SetClipboardTextFn(g.IO.ClipboardUserData, text);
            return;
        }
        Platform_SetClipboardTextDefault(g, text);
    }

    /// <summary>
    /// Default in-memory clipboard getter. Canvases canonical <c>Platform_GetClipboardTextFn_DefaultImpl</c>
    /// </summary>
    internal static string? Platform_GetClipboardTextDefault(ImGuiContext ctx)
    {
        if (ctx.ClipboardHandlerData.Count == 0)
            return null;
        return Encoding.UTF8.GetString(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ctx.ClipboardHandlerData));
    }

    /// <summary>
    /// Default in-memory clipboard setter. Canvases canonical <c>Platform_SetClipboardTextFn_DefaultImpl</c>
    /// </summary>
    internal static void Platform_SetClipboardTextDefault(ImGuiContext ctx, string text)
    {
        ctx.ClipboardHandlerData.Clear();
        if (string.IsNullOrEmpty(text))
            return;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        ctx.ClipboardHandlerData.AddRange(bytes);
    }

    /// <summary>
    /// Default shell execution handler that opens files or URLs in the default system browser or file manager.
    /// Canvases canonical <c>Platform_OpenInShellFn_DefaultImpl</c>.
    /// </summary>
    /// <param name="ctx">Context initiating the request.</param>
    /// <param name="path">URL or file path to open.</param>
    /// <returns><see langword="true"/> if the process was launched successfully; otherwise, <see langword="false"/>.</returns>
    internal static bool Platform_OpenInShellDefault(ImGuiContext ctx, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            ProcessStartInfo psi = new(path)
            {
                UseShellExecute = true
            };
            Process? proc = Process.Start(psi);
            return proc != null;
        }
        catch
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    ProcessStartInfo psi = new("xdg-open", path) { UseShellExecute = false };
                    Process? proc = Process.Start(psi);
                    return proc != null;
                }
                else if (OperatingSystem.IsMacOS())
                {
                    ProcessStartInfo psi = new("open", path) { UseShellExecute = false };
                    Process? proc = Process.Start(psi);
                    return proc != null;
                }
            }
            catch
            {
                // Silently swallow shell launch failures to mirror canonical exit(-1)/false behavior.
            }
            return false;
        }
    }

    /// <summary>
    /// Overrides the keyboard-capture flag reported on the next frame. Mirrors canonical
    /// <c>ImGui::SetNextFrameWantCaptureKeyboard</c>.
    /// </summary>
    /// <param name="wantCaptureKeyboard">Value to publish; negative values are not representable here.</param>
    public static void SetNextFrameWantCaptureKeyboard(bool wantCaptureKeyboard)
    {
        ImGuiContext g = GetContext();
        g.WantCaptureKeyboardNextFrame = wantCaptureKeyboard ? 1 : 0;
    }

    /// <summary>
    /// Overrides the mouse-capture flag reported on the next frame. Mirrors canonical
    /// <c>ImGui::SetNextFrameWantCaptureMouse</c>.
    /// </summary>
    /// <param name="wantCaptureMouse">Value to publish; negative values are not representable here.</param>
    public static void SetNextFrameWantCaptureMouse(bool wantCaptureMouse)
    {
        ImGuiContext g = GetContext();
        g.WantCaptureMouseNextFrame = wantCaptureMouse ? 1 : 0;
    }

    /// <summary>
    /// Computes the barycentric coordinates of a point relative to a triangle. Mirrors canonical
    /// <c>ImTriangleBarycentricCoords</c>.
    /// </summary>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="p">Point to express in barycentric coordinates.</param>
    /// <returns>
    /// The weights of vertices <paramref name="b"/> and <paramref name="c"/> in <c>X</c> and <c>Y</c>.
    /// The weight of <paramref name="a"/> is <c>1 - X - Y</c>, so the canonical three-output form is
    /// recoverable; <c>CONTRACT.md</c> §5 binds this two-component return type.
    /// </returns>
    /// <remarks>
    /// The result is undefined when the triangle is degenerate, that is when the canonical
    /// <c>denom</c> term is zero, exactly as in canonical.
    /// </remarks>
    internal static ImVec2 ImTriangleBarycentricCoords(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 p)
    {
        ImVec2 v0 = b - a;
        ImVec2 v1 = c - a;
        ImVec2 v2 = p - a;
        float denom = (v0.X * v1.Y) - (v1.X * v0.Y);
        float outV = ((v2.X * v1.Y) - (v1.X * v2.Y)) / denom;
        float outW = ((v0.X * v2.Y) - (v2.X * v0.Y)) / denom;
        return new ImVec2(outV, outW);
    }

    /// <summary>
    /// Formats arguments with a canonical printf-style format string. Mirrors the return contract of
    /// canonical <c>ImFormatString</c>.
    /// </summary>
    /// <param name="fmt">Format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns>The formatted text.</returns>
    /// <remarks>
    /// See <see cref="ImFormatStringV"/> for the supported conversions. Canonical writes into a
    /// caller-provided buffer and truncates to <c>buf_size - 1</c>; this port returns a managed string,
    /// so no truncation is applied and no output buffer size is required.
    /// </remarks>
    internal static string ImFormatString(string fmt, params object?[] args)
    {
        return ImFormatStringV(fmt, args);
    }

    /// <summary>
    /// Formats arguments with a canonical printf-style format string. Mirrors canonical
    /// <c>ImFormatStringV</c>.
    /// </summary>
    /// <param name="fmt">Format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns>The formatted text.</returns>
    /// <remarks>
    /// <para>
    /// Supported conversions: <c>%d</c>, <c>%i</c> (signed decimal), <c>%u</c> (unsigned decimal),
    /// <c>%x</c> and <c>%X</c> (hexadecimal), <c>%f</c> (fixed point with a default precision of six),
    /// <c>%.Nf</c> (fixed point with precision <c>N</c>), <c>%e</c> and <c>%E</c> (scientific notation,
    /// with the exponent padded to the C library's minimum of two digits), <c>%s</c> (string, truncated
    /// by an explicit precision), <c>%c</c> (single character) and <c>%%</c> (a literal percent sign).
    /// Flags <c>-</c>, <c>+</c>, <c> </c>, <c>0</c> and <c>#</c> are parsed; <c>#</c> is accepted and
    /// ignored, the others behave as in C. Width may be given literally or as <c>*</c>, in which case
    /// the next argument supplies it; a negative width selects left justification and its magnitude is
    /// used. Precision may be given literally, as <c>*</c>, or omitted.
    /// </para>
    /// <para>
    /// <b>Unsupported conversions are emitted as literal text</b>: the whole <c>%</c> sequence, for
    /// example <c>%p</c>, <c>%g</c> or <c>%o</c>, is copied verbatim and consumes no argument. Floating
    /// point output always uses the invariant culture, matching canonical
    /// <c>vsnprintf</c> behavior regardless of the host locale.
    /// </para>
    /// </remarks>
    internal static string ImFormatStringV(string fmt, object?[] args)
    {
        if (string.IsNullOrEmpty(fmt))
            return string.Empty;

        StringBuilder builder = new StringBuilder(fmt.Length + 16);
        int argIndex = 0;
        int n = 0;
        while (n < fmt.Length)
        {
            if (fmt[n] != '%')
            {
                builder.Append(fmt[n]);
                n++;
                continue;
            }

            int conversionStart = n;
            n++; // Consume '%'.

            // "%%" is a literal percent sign and consumes no argument.
            if (n < fmt.Length && fmt[n] == '%')
            {
                builder.Append('%');
                n++;
                continue;
            }

            // Flags.
            bool leftAlign = false;
            bool zeroPad = false;
            bool plusSign = false;
            bool spaceSign = false;
            while (n < fmt.Length)
            {
                char flag = fmt[n];
                if (flag == '-')
                {
                    leftAlign = true;
                    n++;
                }
                else if (flag == '0')
                {
                    zeroPad = true;
                    n++;
                }
                else if (flag == '+')
                {
                    plusSign = true;
                    n++;
                }
                else if (flag == ' ')
                {
                    spaceSign = true;
                    n++;
                }
                else if (flag == '#')
                {
                    n++; // Accepted and ignored; no alternative form is produced.
                }
                else
                {
                    break;
                }
            }

            // Width, either literal or taken from the next argument.
            int width = 0;
            if (n < fmt.Length && fmt[n] == '*')
            {
                width = ToFormatInt32(NextFormatArg(args, ref argIndex));
                n++;
                if (width < 0)
                {
                    leftAlign = true;
                    width = -width;
                }
            }
            else
            {
                while (n < fmt.Length && fmt[n] >= '0' && fmt[n] <= '9')
                {
                    width = (width * 10) + (fmt[n] - '0');
                    n++;
                }
            }

            // Precision, either literal, taken from the next argument, or absent.
            int precision = -1;
            if (n < fmt.Length && fmt[n] == '.')
            {
                n++;
                if (n < fmt.Length && fmt[n] == '*')
                {
                    precision = ToFormatInt32(NextFormatArg(args, ref argIndex));
                    n++;
                }
                else
                {
                    precision = 0;
                    while (n < fmt.Length && fmt[n] >= '0' && fmt[n] <= '9')
                    {
                        precision = (precision * 10) + (fmt[n] - '0');
                        n++;
                    }
                }
                if (precision < 0)
                    precision = -1; // A negative precision is treated as if it were omitted, as in C.
            }

            if (n >= fmt.Length)
            {
                // A trailing '%' has no conversion character; emit it literally.
                builder.Append(fmt, conversionStart, fmt.Length - conversionStart);
                break;
            }

            char conversion = fmt[n];
            n++;
            switch (conversion)
            {
                case 'd':
                case 'i':
                {
                    long value = ToFormatInt64(NextFormatArg(args, ref argIndex));
                    string sign = value < 0 ? "-" : (plusSign ? "+" : (spaceSign ? " " : string.Empty));
                    string digits = FormatUnsignedMagnitude(value < 0 ? (ulong)(-(value + 1)) + 1UL : (ulong)value, 10, false, precision);
                    AppendFormatted(builder, sign + digits, sign.Length, width, leftAlign, zeroPad);
                    break;
                }
                case 'u':
                {
                    ulong value = ToFormatUInt64(NextFormatArg(args, ref argIndex));
                    string digits = FormatUnsignedMagnitude(value, 10, false, precision);
                    AppendFormatted(builder, digits, 0, width, leftAlign, zeroPad);
                    break;
                }
                case 'x':
                case 'X':
                {
                    ulong value = ToFormatUInt64(NextFormatArg(args, ref argIndex));
                    string digits = FormatUnsignedMagnitude(value, 16, conversion == 'X', precision);
                    AppendFormatted(builder, digits, 0, width, leftAlign, zeroPad);
                    break;
                }
                case 'f':
                case 'F':
                {
                    double value = ToFormatDouble(NextFormatArg(args, ref argIndex));
                    int decimals = precision >= 0 ? precision : 6;
                    string text = value.ToString(
                        "F" + decimals.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (plusSign && value >= 0.0)
                        text = "+" + text;
                    else if (spaceSign && !plusSign && value >= 0.0)
                        text = " " + text;
                    int signLength = (text.Length > 0 && (text[0] == '-' || text[0] == '+' || text[0] == ' ')) ? 1 : 0;
                    AppendFormatted(builder, text, signLength, width, leftAlign, zeroPad);
                    break;
                }
                case 'e':
                case 'E':
                {
                    double value = ToFormatDouble(NextFormatArg(args, ref argIndex));
                    int decimals = precision >= 0 ? precision : 6;
                    string text = FormatExponent(value, decimals, conversion == 'E');
                    if (plusSign && value >= 0.0 && text[0] != '+')
                        text = "+" + text;
                    else if (spaceSign && !plusSign && value >= 0.0 && text[0] != '-' && text[0] != '+')
                        text = " " + text;
                    int signLength = (text.Length > 0 && (text[0] == '-' || text[0] == '+' || text[0] == ' ')) ? 1 : 0;
                    AppendFormatted(builder, text, signLength, width, leftAlign, zeroPad);
                    break;
                }
                case 's':
                {
                    object? raw = NextFormatArg(args, ref argIndex);
                    string text = raw as string ?? (raw == null ? "(null)" : raw.ToString() ?? string.Empty);
                    if (precision >= 0 && precision < text.Length)
                        text = text[..precision];
                    AppendFormatted(builder, text, -1, width, leftAlign, false);
                    break;
                }
                case 'c':
                {
                    object? raw = NextFormatArg(args, ref argIndex);
                    char text = raw switch
                    {
                        char c => c,
                        string s when s.Length > 0 => s[0],
                        null => '\0',
                        _ => (char)ToFormatInt32(raw),
                    };
                    AppendFormatted(builder, text.ToString(), -1, width, leftAlign, false);
                    break;
                }
                default:
                {
                    // Unsupported conversion: emit the whole sequence literally and consume no argument.
                    builder.Append(fmt, conversionStart, n - conversionStart);
                    break;
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats into a temporary buffer, mirroring canonical <c>ImFormatStringToTempBuffer</c>
    /// </summary>
    /// <param name="outBuf">Receives the formatted text.</param>
    /// <param name="outBufEnd">
    /// Receives the unconsumed tail of <paramref name="outBuf"/>. The formatted text is the prefix of
    /// <paramref name="outBuf"/> whose length is <c>outBuf.Length - outBufEnd.Length</c>; the tail is
    /// empty except for the <c>%.*s</c> fast path.
    /// </param>
    /// <param name="fmt">Format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>
    /// Canonical returns <c>const char*</c> begin/end pointers into <c>g.TempBuffer</c> and skips
    /// formatting for the exact strings <c>"%s"</c> and <c>"%.*s"</c>. This port represents the pointer
    /// pair as two managed strings, retaining the same slicing contract without exposing the reusable
    /// buffer: managed strings are immutable and independently valid for the caller's lifetime.
    /// </remarks>
    internal static void ImFormatStringToTempBuffer(out string outBuf, out string outBufEnd, string fmt, params object?[] args)
    {
        // Fast path: skip formatting when the format is exactly "%s".
        if (fmt == "%s")
        {
            object? first = args.Length > 0 ? args[0] : null;
            outBuf = first as string ?? (first == null ? "(null)" : first.ToString() ?? string.Empty);
            outBufEnd = string.Empty;
            return;
        }

        // Fast path: skip formatting when the format is exactly "%.*s".
        if (fmt == "%.*s")
        {
            int bufLen = args.Length > 0 ? ToFormatInt32(args[0]) : 0;
            object? raw = args.Length > 1 ? args[1] : null;
            string buf = raw as string ?? string.Empty;
            if (raw == null)
            {
                buf = "(null)";
                bufLen = Math.Min(bufLen, 6);
            }
            if (bufLen < 0)
                bufLen = 0;
            if (bufLen > buf.Length)
                bufLen = buf.Length;
            outBuf = buf;
            outBufEnd = buf[bufLen..]; // Disallow not passing 'outBufEnd' here. The user is expected to use it.
            return;
        }

        outBuf = ImFormatStringV(fmt, args);
        outBufEnd = string.Empty;
    }

    /// <summary>
    /// Returns the next formatting argument and advances the cursor.
    /// </summary>
    /// <remarks>
    /// Bookkeeping for the hand-written conversion engine that replaces canonical <c>vsnprintf</c> dispatch in
    /// <c>ImFormatStringV</c>. Canonical walks the argument list
    /// through <c>va_arg</c>; the port walks the boxed argument array in the same order, so an unsupported or
    /// mismatched conversion leaves the cursor exactly as canonical would.
    /// </remarks>
    /// <param name="args">Argument array.</param>
    /// <param name="argIndex">Zero-based cursor, advanced past the returned argument.</param>
    /// <returns>The argument, or null when the array is exhausted.</returns>
    private static object? NextFormatArg(object?[] args, ref int argIndex)
    {
        if (argIndex < 0 || argIndex >= args.Length)
            return null;
        object? value = args[argIndex];
        argIndex++;
        return value;
    }

    /// <summary>
    /// Builds the digit sequence of an unsigned value with an optional minimum digit count.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>.; it produces the
    /// same digits and padding that the C library conversion specifier would.
    /// </remarks>
    /// <param name="value">Value to render.</param>
    /// <param name="numberBase">Numeric base, either 10 or 16.</param>
    /// <param name="uppercase">True to render hexadecimal digits in upper case.</param>
    /// <param name="precision">Minimum number of digits, or a negative value for no minimum.</param>
    /// <returns>The digit sequence.</returns>
    private static string FormatUnsignedMagnitude(ulong value, int numberBase, bool uppercase, int precision)
    {
        string digits = numberBase == 16
            ? value.ToString(uppercase ? "X" : "x", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (precision > digits.Length)
            digits = new string('0', precision - digits.Length) + digits;
        return digits;
    }

    /// <summary>
    /// Renders one floating-point value in the canonical exponent form, for example
    /// <c>1.000000e+10</c>.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>. The host
    /// formatter already produces the mantissa and exponent, but its exponent digit count and case
    /// vary between runtimes; the exponent is normalized here to the C library's minimum of two
    /// digits so <c>%e</c>/<c>%E</c> render identically to <c>printf</c>.
    /// </remarks>
    /// <param name="value">Value to render.</param>
    /// <param name="decimals">Digits after the decimal point.</param>
    /// <param name="uppercase">True to render the <c>E</c> marker and match <c>%E</c>.</param>
    /// <returns>The formatted exponent form.</returns>
    private static string FormatExponent(double value, int decimals, bool uppercase)
    {
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        string formatted = value.ToString("E" + decimals.ToString(invariant), invariant);
        int marker = formatted.IndexOf('E');
        string mantissa = formatted[..marker];
        int exponent = int.Parse(formatted[(marker + 1)..], invariant);
        string digits = Math.Abs(exponent).ToString(invariant);
        if (digits.Length < 2)
            digits = "0" + digits;
        return mantissa + (uppercase ? "E" : "e") + (exponent < 0 ? "-" : "+") + digits;
    }

    /// <summary>
    /// Appends a formatted field with width padding and optional zero padding.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>.; it produces the
    /// same digits and padding that the C library conversion specifier would.
    /// </remarks>
    /// <param name="builder">Destination builder.</param>
    /// <param name="text">Field text, including any sign.</param>
    /// <param name="signLength">
    /// Number of leading characters that must remain before zero padding, or -1 when the field is not
    /// numeric and therefore never zero-padded.
    /// </param>
    /// <param name="width">Minimum field width; zero applies no padding.</param>
    /// <param name="leftAlign">True to pad on the right instead of the left.</param>
    /// <param name="zeroPad">True to pad with zeros instead of spaces when <paramref name="signLength"/> permits.</param>
    private static void AppendFormatted(StringBuilder builder, string text, int signLength, int width, bool leftAlign, bool zeroPad)
    {
        int padding = width - text.Length;
        if (padding <= 0)
        {
            builder.Append(text);
            return;
        }
        if (leftAlign)
        {
            builder.Append(text);
            builder.Append(' ', padding);
            return;
        }
        if (zeroPad && signLength >= 0)
        {
            builder.Append(text, 0, signLength);
            builder.Append('0', padding);
            builder.Append(text, signLength, text.Length - signLength);
            return;
        }
        builder.Append(' ', padding);
        builder.Append(text);
    }

    /// <summary>
    /// Converts a formatting argument to a signed 64-bit integer.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>.; it produces the
    /// same digits and padding that the C library conversion specifier would.
    /// </remarks>
    /// <param name="value">Argument to convert.</param>
    /// <returns>The converted value, or zero when the argument is null.</returns>
    /// <exception cref="FormatException">
    /// Thrown when the argument cannot be interpreted as an integer, mirroring the undefined behavior
    /// of passing a mismatched argument to canonical <c>vsnprintf</c>.
    /// </exception>
    private static long ToFormatInt64(object? value)
    {
        switch (value)
        {
            case null: return 0L;
            case int v: return v;
            case long v: return v;
            case uint v: return v;
            case short v: return v;
            case ushort v: return v;
            case byte v: return v;
            case sbyte v: return v;
            case ulong v: return (long)v;
            case float v: return (long)v;
            case double v: return (long)v;
            case bool v: return v ? 1L : 0L;
            default: return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Converts a formatting argument to an unsigned 64-bit integer.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>.; it produces the
    /// same digits and padding that the C library conversion specifier would.
    /// </remarks>
    /// <param name="value">Argument to convert.</param>
    /// <returns>The converted value, or zero when the argument is null.</returns>
    private static ulong ToFormatUInt64(object? value)
    {
        switch (value)
        {
            case null: return 0UL;
            case int v: return (ulong)v;
            case long v: return (ulong)v;
            case uint v: return v;
            case short v: return (ulong)v;
            case ushort v: return v;
            case byte v: return v;
            case sbyte v: return (ulong)v;
            case ulong v: return v;
            case float v: return (ulong)v;
            case double v: return (ulong)v;
            case bool v: return v ? 1UL : 0UL;
            default: return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Converts a formatting argument to a 32-bit integer, used for widths and precisions.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>.; it produces the
    /// same digits and padding that the C library conversion specifier would.
    /// </remarks>
    /// <param name="value">Argument to convert.</param>
    /// <returns>The converted value, or zero when the argument is null.</returns>
    private static int ToFormatInt32(object? value)
    {
        switch (value)
        {
            case null: return 0;
            case int v: return v;
            case long v: return (int)v;
            case uint v: return (int)v;
            case short v: return v;
            case ushort v: return v;
            case byte v: return v;
            case sbyte v: return v;
            case ulong v: return (int)v;
            case float v: return (int)v;
            case double v: return (int)v;
            case bool v: return v ? 1 : 0;
            default: return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Converts a formatting argument to a double-precision value.
    /// </summary>
    /// <remarks>
    /// Conversion helper for the formatting engine that replaces the <c>vsnprintf</c> call in
    /// canonical <c>ImFormatStringV</c>.; it produces the
    /// same digits and padding that the C library conversion specifier would.
    /// </remarks>
    /// <param name="value">Argument to convert.</param>
    /// <returns>The converted value, or zero when the argument is null.</returns>
    private static double ToFormatDouble(object? value)
    {
        switch (value)
        {
            case null: return 0.0;
            case double v: return v;
            case float v: return v;
            case int v: return v;
            case long v: return v;
            case uint v: return v;
            case ulong v: return v;
            case short v: return v;
            case ushort v: return v;
            case byte v: return v;
            case sbyte v: return v;
            case bool v: return v ? 1.0 : 0.0;
            default: return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    #endregion

    #endregion


    #region SECTION: INPUT PIPELINE AND SHORTCUT ROUTING

    #region Input event queue

    /// <summary>
    /// Consumes queued input events and applies them to the live <see cref="ImGuiIO"/> state.
    /// </summary>
    /// <param name="trickleFastInputs">
    /// When set, events are consumed one transition at a time so that fast input sequences are
    /// spread across frames; when clear, the whole queue is flattened in a single frame.
    /// </param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::UpdateInputEvents</c>.
    /// with the exact per-event consumption order and trickling rules. A repeated key event whose
    /// down-state equals the live state is skipped, analog-only key changes do not count as a key
    /// change, and unconsumed events remain queued for the next frame.
    /// </para>
    /// <para>
    /// Focus events may be queued as lost then regained in the same frame; the last one wins because
    /// the flag is overwritten. When the application lost focus at the end of the pass, all keyboard
    /// and mouse state is cleared.
    /// </para>
    /// <para>
    /// The canonical debug-trail print under <c>IMGUI_DISABLE_DEBUG_TOOLS</c> is omitted because the
    /// debug logging subsystem is owned by a later chunk; the consumed-event trail is still recorded
    /// in <see cref="ImGuiContext.InputEventsTrail"/>.
    /// </para>
    /// </remarks>
    internal static void UpdateInputEvents(bool trickleFastInputs)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;

        // Only trickle chars<>key when working with InputText()
        bool trickleInterleavedNoncharKeysAndText = trickleFastInputs && g.WantTextInputNextFrame == 1;

        bool mouseMoved = false;
        bool mouseWheeled = false;
        bool keyChanged = false;
        bool keyChangedNonchar = false;
        bool textInputted = false;
        int mouseButtonChanged = 0x00;
        ImBitArrayForNamedKeys keyChangedMask = new();

        int eventN = 0;
        for (; eventN < g.InputEventsQueue.Count; eventN++)
        {
            ImGuiInputEvent e = g.InputEventsQueue[eventN];
            if (e.Type == ImGuiInputEventType.MousePos)
            {
                if (io.WantSetMousePos)
                    continue;
                // Trickling Rule: stop processing queued events if a mouse button change was handled.
                ImVec2 eventPos = new(e.MousePos.PosX, e.MousePos.PosY);
                if (trickleFastInputs && (mouseButtonChanged != 0 || mouseWheeled || keyChanged || textInputted))
                    break;
                io.MousePos = eventPos;
                io.MouseSource = e.MousePos.MouseSource;
                mouseMoved = true;
            }
            else if (e.Type == ImGuiInputEventType.MouseButton)
            {
                // Trickling Rule: stop processing queued events if the same button acted twice.
                int button = e.MouseButton.Button;
                ImAssert.Check(button >= 0 && button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
                if (trickleFastInputs && ((mouseButtonChanged & (1 << button)) != 0 || mouseWheeled))
                    break;
                if (trickleFastInputs && e.MouseButton.MouseSource == ImGuiMouseSource.TouchScreen && mouseMoved)
                    break; // #2702: TouchScreen have no initial hover.
                io.MouseDown[button] = e.MouseButton.Down;
                io.MouseSource = e.MouseButton.MouseSource;
                mouseButtonChanged |= (1 << button);
            }
            else if (e.Type == ImGuiInputEventType.MouseWheel)
            {
                // Trickling Rule: stop processing queued events if a wheel or button change was handled.
                if (trickleFastInputs && (mouseMoved || mouseButtonChanged != 0))
                    break;
                io.MouseWheelH += e.MouseWheel.WheelX;
                io.MouseWheel += e.MouseWheel.WheelY;
                io.MouseSource = e.MouseWheel.MouseSource;
                mouseWheeled = true;
            }
            else if (e.Type == ImGuiInputEventType.MouseViewport)
            {
                io.MouseHoveredViewport = e.MouseViewport.HoveredViewportID;
            }
            else if (e.Type == ImGuiInputEventType.Key)
            {
                if ((io.ConfigFlags & ImGuiConfigFlags.NoKeyboard) != 0)
                    continue;
                ImGuiKey key = e.Key.Key;
                ImAssert.Check(key != ImGuiKey.None, "Queued key event with ImGuiKey_None.");
                // Modifier flags are stored in a dedicated key slot; resolve the slot and its index.
                ImGuiKey resolvedKey = (key & ImGuiKey.ImGuiMod_Mask_) != 0 ? ConvertSingleModFlagToKey(key) : key;
                ref ImGuiKeyData keyData = ref GetKeyData(key);
                if (trickleFastInputs && keyData.Down != e.Key.Down && (keyChangedMask.TestBit(resolvedKey) || mouseButtonChanged != 0))
                    break;

                bool keyIsPotentiallyForCharInput = IsKeyChordPotentiallyCharInput(GetMergedModsFromKeys() | key);
                if (trickleInterleavedNoncharKeysAndText && (textInputted && !keyIsPotentiallyForCharInput))
                    break;

                if (keyData.Down != e.Key.Down)
                {
                    // Analog change only do not trigger this, so it won't block further mouse pos events.
                    keyChanged = true;
                    keyChangedMask.SetBit(resolvedKey);
                    if (trickleInterleavedNoncharKeysAndText && !keyIsPotentiallyForCharInput)
                        keyChangedNonchar = true;
                }

                keyData.Down = e.Key.Down;
                keyData.AnalogValue = e.Key.AnalogValue;
            }
            else if (e.Type == ImGuiInputEventType.Text)
            {
                if ((io.ConfigFlags & ImGuiConfigFlags.NoKeyboard) != 0)
                    continue;
                // Trickling Rule: stop processing queued events if keys or mouse were interacted with.
                if (trickleFastInputs && (mouseButtonChanged != 0 || mouseMoved || mouseWheeled))
                    break;
                if (trickleInterleavedNoncharKeysAndText && keyChangedNonchar)
                    break;
                uint c = e.Text.Char;
                io.InputQueueCharacters.Add(c <= 0x10FFFFu ? c : 0xFFFDu);
                if (trickleInterleavedNoncharKeysAndText)
                    textInputted = true;
            }
            else if (e.Type == ImGuiInputEventType.Focus)
            {
                // Intentionally overwrite so a backend can queue AddFocusEvent(false) + (true) in one frame.
                io.AppFocusLost = !e.AppFocused.Focused;
            }
            else
            {
                ImAssert.Check(false, "Unknown input event type.");
            }
        }

        // Record trail for domain-specific applications wanting access to a precise trail.
        for (int n = 0; n < eventN; n++)
            g.InputEventsTrail.Add(g.InputEventsQueue[n]);

        // Remaining events will be processed on the next frame.
        if (eventN == g.InputEventsQueue.Count)
            g.InputEventsQueue.Clear();
        else
            g.InputEventsQueue.RemoveRange(0, eventN);

        // Clear button state when focus is lost, so releasing Alt after an Alt-Tab does not trigger
        // the Alt menu toggle. Applications may still observe io.AppFocusLost later in the frame.
        if (io.AppFocusLost)
        {
            io.ClearInputKeys();
            io.ClearInputMouse();
        }
    }

    #endregion

    #region Key helpers and routing

    /// <summary>
    /// Maps a left/right modifier key to its matching modifier flag.
    /// </summary>
    /// <param name="key">The left or right modifier key.</param>
    /// <returns>The matching modifier flag, or <see cref="ImGuiKey.None"/> for a non-modifier key.</returns>
    private static ImGuiKey GetModForLRModKey(ImGuiKey key)
    {
        if (key == ImGuiKey.LeftCtrl || key == ImGuiKey.RightCtrl)
            return ImGuiKey.ImGuiMod_Ctrl;
        if (key == ImGuiKey.LeftShift || key == ImGuiKey.RightShift)
            return ImGuiKey.ImGuiMod_Shift;
        if (key == ImGuiKey.LeftAlt || key == ImGuiKey.RightAlt)
            return ImGuiKey.ImGuiMod_Alt;
        if (key == ImGuiKey.LeftSuper || key == ImGuiKey.RightSuper)
            return ImGuiKey.ImGuiMod_Super;
        return ImGuiKey.None;
    }

    /// <summary>
    /// Maps a modifier flag to the reserved key slot that stores its live state.
    /// </summary>
    /// <param name="key">A key value that may be a modifier flag.</param>
    /// <returns>The reserved modifier key for a modifier flag, otherwise the input unchanged.</returns>
    internal static ImGuiKey ConvertSingleModFlagToKey(ImGuiKey key)
    {
        if (key == ImGuiKey.ImGuiMod_Ctrl)
            return ImGuiKey.ReservedForModCtrl;
        if (key == ImGuiKey.ImGuiMod_Shift)
            return ImGuiKey.ReservedForModShift;
        if (key == ImGuiKey.ImGuiMod_Alt)
            return ImGuiKey.ReservedForModAlt;
        if (key == ImGuiKey.ImGuiMod_Super)
            return ImGuiKey.ReservedForModSuper;
        return key;
    }

    /// <summary>
    /// Maps a mouse button index to its alias key.
    /// </summary>
    /// <param name="button">The mouse button index.</param>
    /// <returns>The alias key for the button.</returns>
    internal static ImGuiKey MouseButtonToKey(ImGuiMouseButton button)
    {
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        return (ImGuiKey)((int)ImGuiKey.MouseLeft + (int)button);
    }

    /// <summary>
    /// Adds the matching modifier flag when a chord names a left/right modifier key.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <returns>The chord with the matching modifier flag added.</returns>
    internal static ImGuiKey FixupKeyChord(ImGuiKey keyChord)
    {
        ImGuiKey key = keyChord & ~ImGuiKey.ImGuiMod_Mask_;
        if (IsLRModKey(key))
            keyChord |= GetModForLRModKey(key);
        return keyChord;
    }

    /// <summary>
    /// Returns the per-key ownership record for a named key or modifier flag.
    /// </summary>
    /// <param name="key">A named key or modifier flag.</param>
    /// <returns>A reference to the key's ownership record.</returns>
    /// <remarks>The caller must pass a named key or a modifier flag; modifier flags are resolved to their reserved slot.</remarks>
    private static ref ImGuiKeyOwnerData GetKeyOwnerData(ImGuiKey key)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((key & ImGuiKey.ImGuiMod_Mask_) != 0)
            key = ConvertSingleModFlagToKey(key);
        ImAssert.Check(IsNamedKey(key), "Key ownership requires a named key.");
        return ref g.KeysOwnerData[(int)key - (int)ImGuiKey.NamedKey_BEGIN];
    }

    /// <summary>
    /// Returns the live input state for a named key or modifier flag.
    /// </summary>
    /// <param name="key">A named key or modifier flag.</param>
    /// <returns>A reference to the key's live state record.</returns>
    /// <remarks>
    /// Canonical returns a pointer; this port returns a reference so callers can mutate the state in
    /// place. The signature listed in CONTRACT.md section 5 returns by value, which cannot express the
    /// canonical write-back path.
    /// </remarks>
    internal static ref ImGuiKeyData GetKeyData(ImGuiKey key)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((key & ImGuiKey.ImGuiMod_Mask_) != 0)
            key = ConvertSingleModFlagToKey(key);
        // Canonical asserts that only named keys and the modifier pseudo-keys reach this lookup,
        // because support for user key indices was dropped in favor of ImGuiKey
        // behavior the canonical pointer arithmetic would have had, so the release-build contract is
        // an explicit exception naming the rejected value instead of a raw index failure.
        ImAssert.Check(IsNamedKey(key), "Only named keys and modifier pseudo-keys have key state.");
        int index = (int)key - (int)ImGuiKey.NamedKey_BEGIN;
        if ((uint)index >= (uint)g.IO.KeysData.Length)
            throw new ArgumentOutOfRangeException(nameof(key), key, "Only named keys and modifier pseudo-keys have key state; legacy native key codes are rejected by ImGuiIO.AddKeyEvent.");
        return ref g.IO.KeysData[index];
    }

    /// <summary>
    /// Writes the pressed state and analog value of a mouse or wheel alias key.
    /// </summary>
    /// <param name="key">An alias key.</param>
    /// <param name="down">The pressed state to store.</param>
    /// <param name="analogValue">The analog value to store.</param>
    private static void UpdateAliasKey(ImGuiKey key, bool down, float analogValue)
    {
        ImAssert.Check(IsAliasKey(key), "UpdateAliasKey requires an alias key.");
        ref ImGuiKeyData keyData = ref GetKeyData(key);
        keyData.Down = down;
        keyData.AnalogValue = analogValue;
    }

    /// <summary>
    /// Derives the merged modifier flags from the live modifier key states.
    /// </summary>
    /// <returns>The OR of the modifier flags whose reserved keys are down.</returns>
    /// <remarks>
    /// Reads the key records directly instead of through <c>IsKeyDown</c>, so an explicit
    /// <c>ImGuiInputFlags_LockXXX</c> on a modifier key does not hide it from the merge.
    /// </remarks>
    private static ImGuiKey GetMergedModsFromKeys()
    {
        ImGuiKey mods = ImGuiKey.None;
        if (GetKeyData(ImGuiKey.ImGuiMod_Ctrl).Down)
            mods |= ImGuiKey.ImGuiMod_Ctrl;
        if (GetKeyData(ImGuiKey.ImGuiMod_Shift).Down)
            mods |= ImGuiKey.ImGuiMod_Shift;
        if (GetKeyData(ImGuiKey.ImGuiMod_Alt).Down)
            mods |= ImGuiKey.ImGuiMod_Alt;
        if (GetKeyData(ImGuiKey.ImGuiMod_Super).Down)
            mods |= ImGuiKey.ImGuiMod_Super;
        return mods;
    }

    /// <summary>
    /// Rebuilds the key routing table, promoting pending routes and dropping dead entries.
    /// </summary>
    /// <param name="rt">The routing table to rebuild.</param>
    /// <remarks>
    /// For each named key the previous frame's <c>RoutingNext</c> becomes <c>RoutingCurr</c>, entries
    /// with no owner are dropped, and the surviving entries are compacted into a contiguous, sorted
    /// list. An entry whose modifier mask matches the current frame's modifiers assigns ownership to
    /// the key when the key has no owner yet.
    /// </remarks>
    private static void UpdateKeyRoutingTable(ImGuiKeyRoutingTable rt)
    {
        ImGuiContext g = GetCurrentContext()!;
        rt.EntriesNext.Clear();
        for (int key = (int)ImGuiKey.NamedKey_BEGIN; key < (int)ImGuiKey.NamedKey_END; key++)
        {
            int newRoutingStartIdx = rt.EntriesNext.Count;
            int oldRoutingIdx = rt.Index[key - (int)ImGuiKey.NamedKey_BEGIN];
            while (oldRoutingIdx != -1)
            {
                ImGuiKeyRoutingData routingEntry = rt.Entries[oldRoutingIdx];
                routingEntry.RoutingCurrScore = routingEntry.RoutingNextScore;
                routingEntry.RoutingCurr = routingEntry.RoutingNext; // Update entry
                routingEntry.RoutingNext = ImGuiKeyOwner_NoOwner;
                routingEntry.RoutingNextScore = 0;
                if (routingEntry.RoutingCurr != ImGuiKeyOwner_NoOwner)
                {
                    rt.EntriesNext.Add(routingEntry); // Keep alive entries.

                    // Apply routing to owner if there is no owner already.
                    if (routingEntry.Mods == (ushort)g.IO.KeyMods)
                    {
                        ref ImGuiKeyOwnerData ownerData = ref GetKeyOwnerData((ImGuiKey)key);
                        if (ownerData.OwnerCurr == ImGuiKeyOwner_NoOwner)
                            ownerData.OwnerCurr = routingEntry.RoutingCurr;
                    }
                }
                oldRoutingIdx = routingEntry.NextEntryIndex;
            }

            // Rewrite the linked list.
            rt.Index[key - (int)ImGuiKey.NamedKey_BEGIN] = (short)(newRoutingStartIdx < rt.EntriesNext.Count ? newRoutingStartIdx : -1);
            for (int n = newRoutingStartIdx; n < rt.EntriesNext.Count; n++)
                rt.EntriesNext[n].NextEntryIndex = (short)((n + 1 < rt.EntriesNext.Count) ? n + 1 : -1);
        }

        // Canonical swaps the two vectors; both lists are exposed get-only, so the contents are swapped.
        rt.Entries.Clear();
        rt.Entries.AddRange(rt.EntriesNext);
        rt.EntriesNext.Clear();
    }

    /// <summary>
    /// The canonical printable names of all named keys, in enum order.
    /// </summary>
    private static readonly string[] GKeyNames =
    {
        "Tab", "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "PageUp", "PageDown", "Home",
        "End", "Insert", "Delete", "Backspace", "Space", "Enter", "Escape", "LeftCtrl",
        "LeftShift", "LeftAlt", "LeftSuper", "RightCtrl", "RightShift", "RightAlt", "RightSuper", "Menu",
        "0", "1", "2", "3", "4", "5", "6", "7",
        "8", "9", "A", "B", "C", "D", "E", "F",
        "G", "H", "I", "J", "K", "L", "M", "N",
        "O", "P", "Q", "R", "S", "T", "U", "V",
        "W", "X", "Y", "Z", "F1", "F2", "F3", "F4",
        "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20",
        "F21", "F22", "F23", "F24", "Apostrophe", "Comma", "Minus", "Period",
        "Slash", "Semicolon", "Equal", "LeftBracket", "Backslash", "RightBracket", "GraveAccent", "CapsLock",
        "ScrollLock", "NumLock", "PrintScreen", "Pause", "Keypad0", "Keypad1", "Keypad2", "Keypad3",
        "Keypad4", "Keypad5", "Keypad6", "Keypad7", "Keypad8", "Keypad9", "KeypadDecimal", "KeypadDivide",
        "KeypadMultiply", "KeypadSubtract", "KeypadAdd", "KeypadEnter", "KeypadEqual", "AppBack", "AppForward", "Oem102",
        "GamepadStart", "GamepadBack", "GamepadFaceLeft", "GamepadFaceRight", "GamepadFaceUp", "GamepadFaceDown", "GamepadDpadLeft", "GamepadDpadRight",
        "GamepadDpadUp", "GamepadDpadDown", "GamepadL1", "GamepadR1", "GamepadL2", "GamepadR2", "GamepadL3", "GamepadR3",
        "GamepadLStickLeft", "GamepadLStickRight", "GamepadLStickUp", "GamepadLStickDown", "GamepadRStickLeft", "GamepadRStickRight", "GamepadRStickUp", "GamepadRStickDown",
        "MouseLeft", "MouseRight", "MouseMiddle", "MouseX1", "MouseX2", "MouseWheelX", "MouseWheelY", "ModCtrl",
        "ModShift", "ModAlt", "ModSuper",
    };

    /// <summary>
    /// Gets the printable name of a named key or modifier flag.
    /// </summary>
    /// <param name="key">The key to name.</param>
    /// <returns>The canonical name, <c>"None"</c> for <see cref="ImGuiKey.None"/>, or <c>"Unknown"</c>.</returns>
    /// <remarks>The names are provided for debugging and are not meant to be persisted or compared.</remarks>
    public static string GetKeyName(ImGuiKey key)
    {
        if (key == ImGuiKey.None)
            return "None";
        ImAssert.Check(IsNamedKeyOrMod(key), "Support for user key indices was dropped in favor of ImGuiKey.");
        if ((key & ImGuiKey.ImGuiMod_Mask_) != 0)
            key = ConvertSingleModFlagToKey(key);
        if (!IsNamedKey(key))
            return "Unknown";

        return GKeyNames[(int)key - (int)ImGuiKey.NamedKey_BEGIN];
    }

    /// <summary>
    /// Builds the printable name of a key chord, such as <c>"Ctrl+LeftShift"</c>.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <returns>The chord name; the returned string is stable until the next call.</returns>
    /// <remarks>
    /// The modifier prefix is untranslated, so on macOS the Command key shows as <c>Ctrl</c> and the
    /// Control key shows as <c>Super</c>. A chord that names a left/right modifier key folds the
    /// matching modifier flag so the key is not repeated.
    /// </remarks>
    public static string GetKeyChordName(ImGuiKey keyChord)
    {
        ImGuiContext g = GetCurrentContext()!;

        ImGuiKey key = keyChord & ~ImGuiKey.ImGuiMod_Mask_;
        if (IsLRModKey(key))
            keyChord &= ~GetModForLRModKey(key); // Return "Ctrl+LeftShift" instead of "Ctrl+Shift+LeftShift".
        g.TempKeychordName = string.Concat(
            (keyChord & ImGuiKey.ImGuiMod_Ctrl) != 0 ? "Ctrl+" : "",
            (keyChord & ImGuiKey.ImGuiMod_Shift) != 0 ? "Shift+" : "",
            (keyChord & ImGuiKey.ImGuiMod_Alt) != 0 ? "Alt+" : "",
            (keyChord & ImGuiKey.ImGuiMod_Super) != 0 ? "Super+" : "",
            (key != ImGuiKey.None || keyChord == ImGuiKey.None) ? GetKeyName(key) : "");
        if (key == ImGuiKey.None && keyChord != (ImGuiKey)0)
        {
            int len = g.TempKeychordName.Length;
            if (len != 0) // Remove trailing '+'.
                g.TempKeychordName = g.TempKeychordName.Substring(0, len - 1);
        }
        return g.TempKeychordName;
    }

    /// <summary>
    /// Returns the typematic repeat delay and rate selected by the supplied repeat-rate flags.
    /// </summary>
    /// <param name="flags">Flags whose repeat-rate bits select a preset.</param>
    /// <param name="repeatDelay">Receives the delay before the first repeat, in seconds.</param>
    /// <param name="repeatRate">Receives the interval between repeats, in seconds.</param>
    internal static void GetTypematicRepeatRate(ImGuiInputFlags flags, out float repeatDelay, out float repeatRate)
    {
        ImGuiContext g = GetCurrentContext()!;
        switch ((ImGuiInputFlagsPrivate)((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatRateMask_))
        {
            case ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatRateNavMove:
                repeatDelay = g.IO.KeyRepeatDelay * 0.72f;
                repeatRate = g.IO.KeyRepeatRate * 0.80f;
                return;
            case ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatRateNavTweak:
                repeatDelay = g.IO.KeyRepeatDelay * 0.72f;
                repeatRate = g.IO.KeyRepeatRate * 0.30f;
                return;
            case ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatRateDefault:
            default:
                repeatDelay = g.IO.KeyRepeatDelay * 1.00f;
                repeatRate = g.IO.KeyRepeatRate * 1.00f;
                return;
        }
    }

    /// <summary>
    /// Counts the typematic repeat events triggered between two points on a key's hold timeline.
    /// </summary>
    /// <param name="t0">The previous duration, typically <c>DownDuration - DeltaTime</c>.</param>
    /// <param name="t1">The current duration.</param>
    /// <param name="repeatDelay">The delay before repeats begin, in seconds.</param>
    /// <param name="repeatRate">The interval between repeats, in seconds.</param>
    /// <returns>The number of repeat events in the interval.</returns>
    public static int CalcTypematicRepeatAmount(float t0, float t1, float repeatDelay, float repeatRate)
    {
        if (t1 == 0.0f)
            return 1;
        if (t0 >= t1)
            return 0;
        if (repeatRate <= 0.0f)
            return (t0 < repeatDelay && t1 >= repeatDelay) ? 1 : 0;
        int countT0 = (t0 < repeatDelay) ? -1 : (int)((t0 - repeatDelay) / repeatRate);
        int countT1 = (t1 < repeatDelay) ? -1 : (int)((t1 - repeatDelay) / repeatRate);
        return countT1 - countT0;
    }

    /// <summary>
    /// Returns the number of presses in the current repeat period for a key.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <param name="repeatDelay">The delay before repeats begin, in seconds.</param>
    /// <param name="repeatRate">The interval between repeats, in seconds.</param>
    /// <returns>A count, usually zero or one.</returns>
    public static int GetKeyPressedAmount(ImGuiKey key, float repeatDelay, float repeatRate)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiKeyData keyData = GetKeyData(key);
        if (!keyData.Down)
            return 0;
        float t = keyData.DownDuration;
        return CalcTypematicRepeatAmount(t - g.IO.DeltaTime, t, repeatDelay, repeatRate);
    }

    /// <summary>
    /// Combines four directional keys into a signed two-dimensional magnitude with analog support.
    /// </summary>
    /// <param name="keyLeft">The key contributing negative horizontal magnitude.</param>
    /// <param name="keyRight">The key contributing positive horizontal magnitude.</param>
    /// <param name="keyUp">The key contributing negative vertical magnitude.</param>
    /// <param name="keyDown">The key contributing positive vertical magnitude.</param>
    /// <returns>The combined magnitude vector.</returns>
    public static ImVec2 GetKeyMagnitude2d(ImGuiKey keyLeft, ImGuiKey keyRight, ImGuiKey keyUp, ImGuiKey keyDown)
    {
        return new ImVec2(
            GetKeyData(keyRight).AnalogValue - GetKeyData(keyLeft).AnalogValue,
            GetKeyData(keyDown).AnalogValue - GetKeyData(keyUp).AnalogValue);
    }

    /// <summary>
    /// Gets a value indicating whether a chord could generate a text character.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <returns><see langword="true"/> when the chord may produce a character input.</returns>
    /// <remarks>
    /// Control without Alt (or Control on macOS) suppresses character input, so those chords are never
    /// treated as potential character input. Used by the input trickling rules to avoid interleaving a
    /// lettered key with its own text event.
    /// </remarks>
    internal static bool IsKeyChordPotentiallyCharInput(ImGuiKey keyChord)
    {
        ImGuiContext g = GetCurrentContext()!;

        ImGuiKey mods = keyChord & ImGuiKey.ImGuiMod_Mask_;
        bool ignoreCharInputs = ((mods & ImGuiKey.ImGuiMod_Ctrl) != 0 && (mods & ImGuiKey.ImGuiMod_Alt) == 0)
            || (g.IO.ConfigMacOSXBehaviors && (mods & ImGuiKey.ImGuiMod_Ctrl) != 0);
        if (ignoreCharInputs)
            return false;

        ImGuiKey key = keyChord & ~ImGuiKey.ImGuiMod_Mask_;
        if (key == ImGuiKey.None)
            return false;
        return g.KeysMayBeCharInput.TestBit(key);
    }

    #endregion
    #region Shortcut routing

    /// <summary>
    /// Translates an owner identifier used by the public shortcut API into a routing identifier.
    /// </summary>
    /// <param name="ownerId">A concrete owner, <see cref="ImGuiKeyOwner_Any"/>, or <see cref="ImGuiKeyOwner_NoOwner"/>.</param>
    /// <returns>The concrete owner, or the current focus scope when the owner is unspecified.</returns>
    private static uint GetRoutingIdFromOwnerId(uint ownerId)
    {
        ImGuiContext g = GetCurrentContext()!;
        return (ownerId != ImGuiKeyOwner_NoOwner && ownerId != ImGuiKeyOwner_Any) ? ownerId : g.CurrentFocusScopeId;
    }

    /// <summary>
    /// Finds or creates the routing entry for a key chord.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <returns>The routing entry for the chord's key and modifier combination.</returns>
    /// <remarks>
    /// A chord accepts a single modifier flag with no key, but not multiple modifier flags. Newly
    /// created entries are initialized to no owner so that the route only becomes current on the
    /// following frame, matching the canonical constructor.
    /// </remarks>
    public static ImGuiKeyRoutingData GetShortcutRoutingData(ImGuiKey keyChord)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiKeyRoutingTable rt = g.KeysRoutingTable;
        ImGuiKey key = keyChord & ~ImGuiKey.ImGuiMod_Mask_;
        ImGuiKey mods = keyChord & ImGuiKey.ImGuiMod_Mask_;
        if (key == ImGuiKey.None)
            key = ConvertSingleModFlagToKey(mods);
        ImAssert.Check(IsNamedKey(key), "Shortcut routing requires a named key.");

        // Most shortcuts have a single entry for their key; the list is sorted and rebuilt each frame.
        int idx = rt.Index[(int)key - (int)ImGuiKey.NamedKey_BEGIN];
        while (idx != -1)
        {
            ImGuiKeyRoutingData routingData = rt.Entries[idx];
            if (routingData.Mods == (ushort)mods)
                return routingData;
            idx = routingData.NextEntryIndex;
        }

        // Add to the linked list for the key.
        short routingDataIdx = (short)rt.Entries.Count;
        rt.Entries.Add(new ImGuiKeyRoutingData());
        ImGuiKeyRoutingData newData = rt.Entries[routingDataIdx];
        newData.Mods = (ushort)mods;
        newData.NextEntryIndex = rt.Index[(int)key - (int)ImGuiKey.NamedKey_BEGIN];
        rt.Index[(int)key - (int)ImGuiKey.NamedKey_BEGIN] = routingDataIdx;
        // ImGuiKeyRoutingData has no constructor that seeds the owner fields, so seed them here to
        // reproduce the canonical (RoutingCurr = RoutingNext = ImGuiKeyOwner_NoOwner) initialization.
        newData.RoutingCurr = ImGuiKeyOwner_NoOwner;
        newData.RoutingNext = ImGuiKeyOwner_NoOwner;
        return newData;
    }

    /// <summary>
    /// Tests if a shortcut routing matches the specified owner ID.
    /// Ports canonical <c>ImGui::TestShortcutRouting</c>.
    /// </summary>
    public static bool TestShortcutRouting(ImGuiKey keyChord, uint ownerId)
    {
        uint routingId = GetRoutingIdFromOwnerId(ownerId);
        keyChord = FixupKeyChord(keyChord);
        ImGuiKeyRoutingData routingData = GetShortcutRoutingData(keyChord);
        return routingData.RoutingCurr == routingId;
    }

    /// <summary>
    /// Computes the routing priority for a request.
    /// </summary>
    /// <param name="focusScopeId">The focus scope the request is evaluated from.</param>
    /// <param name="ownerId">The owner submitting the request.</param>
    /// <param name="flags">The routing policy flags.</param>
    /// <returns>A score where lower is better and zero means the route can never be granted.</returns>
    /// <remarks>
    /// Focused routes score by distance from the focused window along <c>NavFocusRoute</c>; active
    /// routes score 300; global routes score 1, 200, 300, or 400 depending on the override flags.
    /// </remarks>
    private static int CalcRoutingScore(uint focusScopeId, uint ownerId, ImGuiInputFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((flags & ImGuiInputFlags.RouteFocused) != 0)
        {
            // The active item gets high priority.
            if (ownerId != 0 && g.ActiveId == ownerId)
                return 300;

            if (focusScopeId == 0)
                return 0;
            for (int indexInFocusPath = 0; indexInFocusPath < g.NavFocusRoute.Count; indexInFocusPath++)
                if (g.NavFocusRoute[indexInFocusPath].ID == focusScopeId)
                {
                    if ((flags & ImGuiInputFlags.RouteOverActive) != 0)
                        return 599 - indexInFocusPath;
                    else
                        return 199 - indexInFocusPath;
                }
            return 0;
        }
        else if ((flags & ImGuiInputFlags.RouteActive) != 0)
        {
            if (ownerId != 0 && g.ActiveId == ownerId)
                return 300;
            return 0;
        }
        else if ((flags & ImGuiInputFlags.RouteGlobal) != 0)
        {
            if ((flags & ImGuiInputFlags.RouteOverActive) != 0)
                return 400;
            if (ownerId != 0 && g.ActiveId == ownerId)
                return 300;
            if ((flags & ImGuiInputFlags.RouteOverFocused) != 0)
                return 200;
            return 1;
        }
        ImAssert.Check(false, "SetShortcutRouting requires an explicit routing policy.");
        return 0;
    }

    /// <summary>
    /// Submits a desired route for a key chord and reports whether it is available this frame.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <param name="flags">The routing policy flags.</param>
    /// <param name="ownerId">A concrete owner; <see cref="ImGuiKeyOwner_Any"/> and <see cref="ImGuiKeyOwner_NoOwner"/> are rejected.</param>
    /// <returns><see langword="true"/> when the route belongs to the owner this frame.</returns>
    /// <remarks>
    /// Routes are attributed at the beginning of the next frame from the best score and the current
    /// modifier state, so a newly submitted route is available one frame later. Lettered shortcuts
    /// that could be character input are culled while an item reads text, and the active item's
    /// all-keyboard-keys capture blocks routes that do not override it.
    /// </remarks>
    internal static bool SetShortcutRouting(ImGuiKey keyChord, ImGuiInputFlags flags, uint ownerId)
    {
        ImGuiContext g = GetCurrentContext()!;
        uint routeTypeMask = (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RouteTypeMask_;
        if (((uint)flags & routeTypeMask) == 0)
            flags |= ImGuiInputFlags.RouteGlobal | ImGuiInputFlags.RouteOverFocused | ImGuiInputFlags.RouteOverActive;
        else
        {
            uint routeType = (uint)flags & routeTypeMask;
            ImAssert.Check((routeType & (routeType - 1)) == 0, "Only one routing flag may be used.");
        }
        ImAssert.Check(ownerId != ImGuiKeyOwner_Any && ownerId != ImGuiKeyOwner_NoOwner,
            "SetShortcutRouting requires a concrete owner.");
        if ((flags & (ImGuiInputFlags.RouteOverFocused | ImGuiInputFlags.RouteUnlessBgFocused)) != 0)
            ImAssert.Check((flags & ImGuiInputFlags.RouteGlobal) != 0, "These routing options require a global route.");
        if ((flags & ImGuiInputFlags.RouteOverActive) != 0)
            ImAssert.Check((flags & (ImGuiInputFlags.RouteGlobal | ImGuiInputFlags.RouteFocused)) != 0,
                "RouteOverActive requires a global or focused route.");

        keyChord = FixupKeyChord(keyChord);

        // A user-requested debug break on this chord is owned by the debug-tools subsystem (a later
        // chunk) and is intentionally not ported here.

        if ((flags & ImGuiInputFlags.RouteUnlessBgFocused) != 0 && g.NavWindow == null)
            return false;

        // RouteAlways does not register a route and therefore does not set an owner.
        if ((flags & ImGuiInputFlags.RouteAlways) != 0)
            return true;

        // Specific culling while an item is active.
        if (g.ActiveId != 0 && g.ActiveId != ownerId)
        {
            if ((flags & ImGuiInputFlags.RouteActive) != 0)
                return false;

            // Cull shortcuts that could generate a character while text is being read.
            if (g.IO.WantTextInput && IsKeyChordPotentiallyCharInput(keyChord))
                return false;

            // The active item's all-keyboard-keys capture trumps everything except an over-active route.
            if ((flags & ImGuiInputFlags.RouteOverActive) == 0 && g.ActiveIdUsingAllKeyboardKeys)
            {
                ImGuiKey key = keyChord & ~ImGuiKey.ImGuiMod_Mask_;
                if (key == ImGuiKey.None)
                    key = ConvertSingleModFlagToKey(keyChord & ImGuiKey.ImGuiMod_Mask_);
                if (IsKeyboardKey(key))
                    return false;
            }
        }

        // Determine where the route is evaluated.
        uint focusScopeId = g.CurrentFocusScopeId;
        if ((flags & ImGuiInputFlags.RouteFromRootWindow) != 0)
            focusScopeId = g.CurrentWindow!.RootWindow!.ID;

        int score = CalcRoutingScore(focusScopeId, ownerId, flags);
        if (score == 0)
            return false;

        // Submit the route for the next frame when the score is sufficient, and report the route that
        // is current now.
        ImGuiKeyRoutingData routingData = GetShortcutRoutingData(keyChord);
        if (score > routingData.RoutingNextScore)
        {
            routingData.RoutingNext = ownerId;
            routingData.RoutingNextScore = (ushort)score;
        }
        return routingData.RoutingCurr == ownerId;
    }

    /// <summary>
    /// Queues a keyboard shortcut for the next submitted item.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <param name="flags">The shortcut flags, including the optional tooltip request.</param>
    /// <remarks>
    /// <para>
    /// The queued values are consumed by <c>ItemAdd</c> through its shortcut handling and surface on
    /// <see cref="ImGuiLastItemData.Shortcut"/> when the tooltip flag is set.
    /// </para>
    /// <para>
    /// The implementation plan assigned the shortcut surface to a later chunk, but <c>ItemAdd</c>
    /// consumes it, so it is realized here; the later chunk extends rather than redefines it.
    /// </para>
    /// </remarks>
    public static void SetNextItemShortcut(ImGuiKey keyChord, ImGuiInputFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasShortcut;
        g.NextItemData.Shortcut = keyChord;
        g.NextItemData.ShortcutFlags = flags;
    }

    /// <summary>
    /// Tests whether a keyboard shortcut is pressed and can be routed to the caller.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <param name="flags">The shortcut flags; a focused route is used when no route policy is set.</param>
    /// <returns><see langword="true"/> when the shortcut fired for this frame.</returns>
    public static bool Shortcut(ImGuiKey keyChord, ImGuiInputFlags flags = 0)
    {
        return Shortcut(keyChord, flags, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Tests whether a keyboard shortcut is pressed and can be routed to a specific owner.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <param name="flags">The shortcut flags; a focused route is used when no route policy is set.</param>
    /// <param name="ownerId">The owner identifier, or <see cref="ImGuiKeyOwner_Any"/> to derive one from the focus scope.</param>
    /// <returns><see langword="true"/> when the shortcut fired for this frame.</returns>
    /// <remarks>
    /// The shortcut is input-owner aware: an unspecified owner is resolved to the current focus
    /// scope. Repeat defaults to ending when the modifiers change, so releasing a modifier while the
    /// key is held does not keep firing the shortcut. On success the modifier flags are claimed for
    /// the owner.
    /// </remarks>
    public static bool Shortcut(ImGuiKey keyChord, ImGuiInputFlags flags, uint ownerId)
    {
        ImGuiContext g = GetCurrentContext()!;

        // When the owner is unspecified, routing uses the current focus scope; matching IsKeyPressed
        // with the same owner keeps the test consistent.
        if (((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RouteTypeMask_) == 0)
            flags |= ImGuiInputFlags.RouteFocused;

        // An unspecified owner is auto-assigned from the current focus scope so the shortcut is always
        // input-owner aware.
        if (ownerId == ImGuiKeyOwner_Any || ownerId == ImGuiKeyOwner_NoOwner)
            ownerId = GetRoutingIdFromOwnerId(ownerId);

        if ((g.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0)
            return false;

        // Submit the route.
        if (!SetShortcutRouting(keyChord, flags, ownerId))
            return false;

        // Default repeat behavior for Shortcut(): releasing a modifier while the key is held stops the
        // repeat.
        if ((flags & ImGuiInputFlags.Repeat) != 0
            && ((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilMask_) == 0)
            flags |= (ImGuiInputFlags)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilKeyModsChange;

        if (!IsKeyChordPressed(keyChord, flags, ownerId))
            return false;

        // Claim the modifiers during the press.
        SetKeyOwnersForKeyChord(keyChord & ImGuiKey.ImGuiMod_Mask_, ownerId);

        ImAssert.Check(((uint)flags & ~(uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedByShortcut) == 0,
            "Passing flags not supported by Shortcut.");
        return true;
    }

    #endregion



    #region Key ownership

    /// <summary>
    /// The canonical owner identifier meaning "do not test ownership" (<c>ImGuiKeyOwner_Any == 0</c>).
    /// </summary>
    internal const uint ImGuiKeyOwner_Any = 0;

    /// <summary>
    /// The canonical owner identifier meaning "the key must have no owner" (<c>ImGuiKeyOwner_NoOwner == (ImGuiID)-1</c>).
    /// </summary>
    internal const uint ImGuiKeyOwner_NoOwner = uint.MaxValue;

    /// <summary>
    /// Returns the current owner of a key, suppressed to no owner while keyboard input is captured.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <returns>The current owner identifier, or <see cref="ImGuiKeyOwner_NoOwner"/>.</returns>
    internal static uint GetKeyOwner(ImGuiKey key)
    {
        if (!IsNamedKeyOrMod(key))
            return ImGuiKeyOwner_NoOwner;

        ImGuiContext g = GetCurrentContext()!;
        ref ImGuiKeyOwnerData ownerData = ref GetKeyOwnerData(key);
        uint ownerId = ownerData.OwnerCurr;

        if (g.ActiveIdUsingAllKeyboardKeys && ownerId != g.ActiveId && ownerId != ImGuiKeyOwner_Any)
            if (IsKeyboardKey(key))
                return ImGuiKeyOwner_NoOwner;

        return ownerId;
    }

    /// <summary>
    /// Tests whether a key may be read by the supplied owner.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <param name="ownerId">The requesting owner, <see cref="ImGuiKeyOwner_Any"/>, or <see cref="ImGuiKeyOwner_NoOwner"/>.</param>
    /// <returns><see langword="true"/> when the owner may read the key.</returns>
    /// <remarks>
    /// <see cref="ImGuiKeyOwner_Any"/> accepts any owner unless the key is locked;
    /// <see cref="ImGuiKeyOwner_NoOwner"/> accepts only unowned, unlocked keys; a specific identifier
    /// accepts its own key or an unowned, unlocked key. Locked modifier and mouse keys are handled the
    /// same way because modifier flags resolve to reserved key slots.
    /// </remarks>
    internal static bool TestKeyOwner(ImGuiKey key, uint ownerId)
    {
        if (!IsNamedKeyOrMod(key))
            return true;

        ImGuiContext g = GetCurrentContext()!;
        if (g.ActiveIdUsingAllKeyboardKeys && ownerId != g.ActiveId && ownerId != ImGuiKeyOwner_Any)
            if (IsKeyboardKey(key))
                return false;

        ref ImGuiKeyOwnerData ownerData = ref GetKeyOwnerData(key);
        if (ownerId == ImGuiKeyOwner_Any)
            return ownerData.LockThisFrame == false;

        if (ownerData.OwnerCurr != ownerId)
        {
            if (ownerData.LockThisFrame)
                return false;
            if (ownerData.OwnerCurr != ImGuiKeyOwner_NoOwner)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Assigns an owner to a key and optionally locks it.
    /// </summary>
    /// <param name="key">The named key or modifier flag to assign.</param>
    /// <param name="ownerId">The owner identifier, or <see cref="ImGuiKeyOwner_Any"/> when locking.</param>
    /// <param name="flags">Zero, or the <c>LockThisFrame</c> / <c>LockUntilRelease</c> flags.</param>
    /// <remarks>
    /// Ownership is automatically released on the frame after the key is released by
    /// <c>UpdateKeyboardInputs</c>. Using <see cref="ImGuiKeyOwner_Any"/> requires a lock flag.
    /// </remarks>
    internal static void SetKeyOwner(ImGuiKey key, uint ownerId, ImGuiInputFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        uint lockFlags = (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_LockThisFrame
            | (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_LockUntilRelease;
        ImAssert.Check(IsNamedKeyOrMod(key) && (ownerId != ImGuiKeyOwner_Any || ((uint)flags & lockFlags) != 0),
            "Can only use ImGuiKeyOwner_Any with a lock flag.");
        ImAssert.Check(((uint)flags & ~(uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedBySetKeyOwner) == 0,
            "Passing flags not supported by SetKeyOwner.");

        ref ImGuiKeyOwnerData ownerData = ref GetKeyOwnerData(key);
        ownerData.OwnerCurr = ownerData.OwnerNext = ownerId;

        // Locking is not enabled by default because it would break legacy code. LockUntilRelease also
        // locks during the frame in which it was requested, even before the key is down.
        ownerData.LockUntilRelease = ((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_LockUntilRelease) != 0;
        ownerData.LockThisFrame = ((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_LockThisFrame) != 0
            || ownerData.LockUntilRelease;
    }

    /// <summary>
    /// Assigns an owner to every modifier and key named by a chord.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="flags">Zero, or the lock flags forwarded to <see cref="SetKeyOwner"/>.</param>
    internal static void SetKeyOwnersForKeyChord(ImGuiKey keyChord, uint ownerId, ImGuiInputFlags flags = 0)
    {
        if ((keyChord & ImGuiKey.ImGuiMod_Ctrl) != 0)
            SetKeyOwner(ImGuiKey.ImGuiMod_Ctrl, ownerId, flags);
        if ((keyChord & ImGuiKey.ImGuiMod_Shift) != 0)
            SetKeyOwner(ImGuiKey.ImGuiMod_Shift, ownerId, flags);
        if ((keyChord & ImGuiKey.ImGuiMod_Alt) != 0)
            SetKeyOwner(ImGuiKey.ImGuiMod_Alt, ownerId, flags);
        if ((keyChord & ImGuiKey.ImGuiMod_Super) != 0)
            SetKeyOwner(ImGuiKey.ImGuiMod_Super, ownerId, flags);
        if ((keyChord & ~ImGuiKey.ImGuiMod_Mask_) != 0)
            SetKeyOwner(keyChord & ~ImGuiKey.ImGuiMod_Mask_, ownerId, flags);
    }

    /// <summary>
    /// Routes a key to the identifier of the last submitted item when that item is hovered or active.
    /// Mirrors canonical <c>ImGui::SetItemKeyOwner(ImGuiKey, ImGuiInputFlags)</c>
    /// </summary>
    /// <param name="key">The key or modifier flag to route.</param>
    /// <param name="flags">
    /// Zero, or a combination of the private condition flags selecting whether the hovered or active
    /// state qualifies, and the private lock flags forwarded to <see cref="SetKeyOwner"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the item qualified, the key was readable by the item, and ownership
    /// was assigned; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The canonical function is a convenience for the common pattern
    /// <c>if (IsItemHovered() || IsItemActive()) SetKeyOwner(key, GetItemID())</c>. Only one item can
    /// be hovered and only one can be active, so no routing or priority computation is required.
    /// </para>
    /// <para>
    /// The private <c>CondHovered</c> and <c>CondActive</c> bits are exposed through
    /// <see cref="ImGuiInputFlagsPrivate"/>; a zero condition mask defaults to both bits, matching
    /// <c>ImGuiInputFlags_CondDefault_</c>. The condition bits are stripped before the lock flags are
    /// forwarded to <see cref="SetKeyOwner"/>.
    /// </para>
    /// </remarks>
    internal static bool SetItemKeyOwner(ImGuiKey key, ImGuiInputFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        uint id = g.LastItemData.ID;
        if (id == 0 || (g.HoveredId != id && g.ActiveId != id))
            return false;

        uint condMask = (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_CondMask_;
        if (((uint)flags & condMask) == 0)
            flags |= (ImGuiInputFlags)ImGuiInputFlagsPrivate.ImGuiInputFlags_CondDefault_;
        bool condHovered = (g.HoveredId == id && ((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_CondHovered) != 0);
        bool condActive = (g.ActiveId == id && ((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_CondActive) != 0);
        if (condHovered || condActive)
        {
            ImAssert.Check(((uint)flags & ~(uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedBySetItemKeyOwner) == 0,
                "Passing flags not supported by SetItemKeyOwner.");
            if (!TestKeyOwner(key, id))
                return false;
            SetKeyOwner(key, id, (ImGuiInputFlags)((uint)flags & ~condMask));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Routes a key to the identifier of the last submitted item when that item is hovered or active.
    /// Mirrors canonical <c>ImGui::SetItemKeyOwner(ImGuiKey)</c>
    ///., which supplies no condition or lock flags.
    /// </summary>
    /// <param name="key">The key or modifier flag to route.</param>
    /// <returns><see langword="true"/> when the last item qualified and the key ownership was assigned.</returns>
    public static bool SetItemKeyOwner(ImGuiKey key)
    {
        return SetItemKeyOwner(key, ImGuiInputFlags.None);
    }

    /// <summary>
    /// Marks the active item as consuming every keyboard key and cancels navigation moves.
    /// </summary>
    /// <remarks>
    /// Called when a drag or window move starts so that navigation keys do not compete with the active
    /// item. The active item must be non-zero.
    /// </remarks>
    internal static void SetActiveIdUsingAllKeyboardKeys()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.ActiveId != 0, "SetActiveIdUsingAllKeyboardKeys requires an active item.");
        g.ActiveIdUsingNavDirMask = (1u << (int)ImGuiDir.COUNT) - 1;
        g.ActiveIdUsingAllKeyboardKeys = true;
        NavMoveRequestCancel();
    }

    #endregion

    #region Keyboard input

    /// <summary>
    /// Advances the live keyboard state from the queued input applied earlier in the frame.
    /// </summary>
    /// <remarks>
    /// Refreshes the mouse and wheel alias keys, derives <c>KeyMods</c> and the convenience modifier
    /// booleans from the live modifier key states, clears gamepad state when the backend lacks gamepad
    /// support, advances each key's hold duration, updates key ownership, and finally rebuilds the
    /// shortcut routing table.
    /// </remarks>
    internal static void UpdateKeyboardInputs()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;

        if ((io.ConfigFlags & ImGuiConfigFlags.NoKeyboard) != 0)
            io.ClearInputKeys();

        // Update aliases.
        for (int n = 0; n < (int)ImGuiMouseButton.COUNT; n++)
            UpdateAliasKey(MouseButtonToKey((ImGuiMouseButton)n), io.MouseDown[n], io.MouseDown[n] ? 1.0f : 0.0f);
        UpdateAliasKey(ImGuiKey.MouseWheelX, io.MouseWheelH != 0.0f, io.MouseWheelH);
        UpdateAliasKey(ImGuiKey.MouseWheelY, io.MouseWheel != 0.0f, io.MouseWheel);

        // Synchronize io.KeyMods and io.KeyCtrl/io.KeyShift/etc. values from the key array.
        ImGuiKey prevKeyMods = io.KeyMods;
        io.KeyMods = GetMergedModsFromKeys();
        io.KeyCtrl = (io.KeyMods & ImGuiKey.ImGuiMod_Ctrl) != 0;
        io.KeyShift = (io.KeyMods & ImGuiKey.ImGuiMod_Shift) != 0;
        io.KeyAlt = (io.KeyMods & ImGuiKey.ImGuiMod_Alt) != 0;
        io.KeySuper = (io.KeyMods & ImGuiKey.ImGuiMod_Super) != 0;
        if (prevKeyMods != io.KeyMods)
            g.LastKeyModsChangeTime = g.Time;
        if (prevKeyMods != io.KeyMods && prevKeyMods == ImGuiKey.None)
            g.LastKeyModsChangeFromNoneTime = g.Time;

        // Clear gamepad data if disabled.
        if ((io.BackendFlags & ImGuiBackendFlags.HasGamepad) == 0)
        {
            for (int key = (int)ImGuiKey.GamepadStart; key < (int)ImGuiKey.GamepadRStickDown + 1; key++)
            {
                int index = key - (int)ImGuiKey.NamedKey_BEGIN;
                io.KeysData[index].Down = false;
                io.KeysData[index].AnalogValue = 0.0f;
            }
        }

        // Update key hold durations.
        for (int key = (int)ImGuiKey.NamedKey_BEGIN; key < (int)ImGuiKey.NamedKey_END; key++)
        {
            int index = key - (int)ImGuiKey.NamedKey_BEGIN;
            ref ImGuiKeyData keyData = ref io.KeysData[index];
            keyData.DownDurationPrev = keyData.DownDuration;
            keyData.DownDuration = keyData.Down ? (keyData.DownDuration < 0.0f ? 0.0f : keyData.DownDuration + io.DeltaTime) : -1.0f;
            if (keyData.DownDuration == 0.0f)
            {
                if (IsKeyboardKey((ImGuiKey)key))
                    g.LastKeyboardKeyPressTime = g.Time;
                else if (key == (int)ImGuiKey.ReservedForModCtrl || key == (int)ImGuiKey.ReservedForModShift
                    || key == (int)ImGuiKey.ReservedForModAlt || key == (int)ImGuiKey.ReservedForModSuper)
                    g.LastKeyboardKeyPressTime = g.Time;
            }
        }

        // Update key/input ownership (named keys only), one entry per key.
        for (int key = (int)ImGuiKey.NamedKey_BEGIN; key < (int)ImGuiKey.NamedKey_END; key++)
        {
            int index = key - (int)ImGuiKey.NamedKey_BEGIN;
            ImGuiKeyData keyData = io.KeysData[index];
            ImGuiKeyOwnerData ownerData = g.KeysOwnerData[index];
            ownerData.OwnerCurr = ownerData.OwnerNext;
            // Important: ownership is released on the frame after a release, so a
            // MouseDown -> CloseWindow -> MouseUp chain cannot hand the key to someone else early.
            if (!keyData.Down)
                ownerData.OwnerNext = ImGuiKeyOwner_NoOwner;
            // Clear LockUntilRelease when the key is no longer down.
            ownerData.LockThisFrame = ownerData.LockUntilRelease = ownerData.LockUntilRelease && keyData.Down;
        }

        // Update key routing (for example shortcuts).
        UpdateKeyRoutingTable(g.KeysRoutingTable);
    }

    /// <summary>
    /// Gets a value indicating whether a key is currently held and readable.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <returns><see langword="true"/> when the key is down and not filtered by key ownership.</returns>
    public static bool IsKeyDown(ImGuiKey key)
    {
        return IsKeyDown(key, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a key is currently held and readable by the supplied owner.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> when the key is down and the owner may read it.</returns>
    internal static bool IsKeyDown(ImGuiKey key, uint ownerId)
    {
        ImGuiKeyData keyData = GetKeyData(key);
        if (!keyData.Down)
            return false;
        if (!TestKeyOwner(key, ownerId))
            return false;
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether a key was pressed, optionally including typematic repeats.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <param name="repeat">Whether held keys should also report typematic repeats.</param>
    /// <returns><see langword="true"/> on the press edge, or on a repeat when requested.</returns>
    public static bool IsKeyPressed(ImGuiKey key, bool repeat = true)
    {
        return IsKeyPressed(key, repeat ? ImGuiInputFlags.Repeat : ImGuiInputFlags.None, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a key was pressed, with explicit flags and an owner.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <param name="flags">The supported repeat and repeat-rate flags.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> when the press satisfies the flags and the owner may read the key.</returns>
    /// <remarks>
    /// Any repeat-rate or repeat-until flag implies <c>Repeat</c>. Repeat-until filters cancel a repeat
    /// when a later modifier change or key press occurred after the key went down.
    /// </remarks>
    internal static bool IsKeyPressed(ImGuiKey key, ImGuiInputFlags flags, uint ownerId = ImGuiKeyOwner_Any)
    {
        ImGuiKeyData keyData = GetKeyData(key);
        if (!keyData.Down)
            return false;
        float t = keyData.DownDuration;
        if (t < 0.0f)
            return false;
        ImAssert.Check(((uint)flags & ~(uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedByIsKeyPressed) == 0,
            "Passing flags not supported by IsKeyPressed.");
        if (((uint)flags & ((uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatRateMask_
            | (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilMask_)) != 0)
            flags |= ImGuiInputFlags.Repeat;

        bool pressed = t == 0.0f;
        if (!pressed && (flags & ImGuiInputFlags.Repeat) != 0)
        {
            GetTypematicRepeatRate(flags, out float repeatDelay, out float repeatRate);
            pressed = t > repeatDelay && GetKeyPressedAmount(key, repeatDelay, repeatRate) > 0;
            if (pressed && ((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilMask_) != 0)
            {
                // Bias the pressed time slightly because DownDuration accumulates DeltaTime.
                ImGuiContext g = GetCurrentContext()!;
                double keyPressedTime = g.Time - t + 0.00001f;
                if (((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilKeyModsChange) != 0
                    && g.LastKeyModsChangeTime > keyPressedTime)
                    pressed = false;
                if (((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilKeyModsChangeFromNone) != 0
                    && g.LastKeyModsChangeFromNoneTime > keyPressedTime)
                    pressed = false;
                if (((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatUntilOtherKeyPress) != 0
                    && g.LastKeyboardKeyPressTime > keyPressedTime)
                    pressed = false;
            }
        }
        if (!pressed)
            return false;
        if (!TestKeyOwner(key, ownerId))
            return false;
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether a key was released this frame.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <returns><see langword="true"/> on the release edge.</returns>
    public static bool IsKeyReleased(ImGuiKey key)
    {
        return IsKeyReleased(key, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a key was released this frame and is readable by the owner.
    /// </summary>
    /// <param name="key">The key to query.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> on the release edge when the owner may read the key.</returns>
    internal static bool IsKeyReleased(ImGuiKey key, uint ownerId)
    {
        ImGuiKeyData keyData = GetKeyData(key);
        if (keyData.DownDurationPrev < 0.0f || keyData.Down)
            return false;
        if (!TestKeyOwner(key, ownerId))
            return false;
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether a key chord was pressed this frame.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <returns><see langword="true"/> when the chord's modifiers match and its base key was pressed.</returns>
    public static bool IsKeyChordPressed(ImGuiKey keyChord)
    {
        return IsKeyChordPressed(keyChord, ImGuiInputFlags.None, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a key chord was pressed this frame, with explicit flags and owner.
    /// </summary>
    /// <param name="keyChord">A key optionally OR-ed with modifier flags.</param>
    /// <param name="flags">The repeat flags forwarded to <see cref="IsKeyPressed(ImGuiKey, ImGuiInputFlags, uint)"/>.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> when the chord's modifiers match and its base key was pressed.</returns>
    internal static bool IsKeyChordPressed(ImGuiKey keyChord, ImGuiInputFlags flags, uint ownerId = ImGuiKeyOwner_Any)
    {
        ImGuiContext g = GetCurrentContext()!;
        keyChord = FixupKeyChord(keyChord);
        ImGuiKey mods = keyChord & ImGuiKey.ImGuiMod_Mask_;
        if (g.IO.KeyMods != mods)
            return false;

        ImGuiKey key = keyChord & ~ImGuiKey.ImGuiMod_Mask_;
        if (key == ImGuiKey.None)
            key = ConvertSingleModFlagToKey(mods);
        ImGuiInputFlags repeatFlags = (ImGuiInputFlags)((uint)flags & (uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_RepeatMask_);
        return IsKeyPressed(key, repeatFlags, ownerId);
    }

    #endregion

    #region Mouse input

    /// <summary>
    /// The sentinel below which a mouse coordinate is considered unavailable (<c>MOUSE_INVALID = -256000.0f</c>).
    /// </summary>
    private const float MouseInvalidPos = -256000.0f;

    /// <summary>
    /// Gets a value indicating whether the current mouse position is valid.
    /// </summary>
    /// <returns><see langword="true"/> when <see cref="ImGuiIO.MousePos"/> is not the invalid sentinel.</returns>
    public static bool IsMousePosValid()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.IO.MousePos.X >= MouseInvalidPos && g.IO.MousePos.Y >= MouseInvalidPos;
    }

    /// <summary>
    /// Gets a value indicating whether a supplied mouse position is valid.
    /// </summary>
    /// <param name="mousePos">The position to test.</param>
    /// <returns><see langword="true"/> when the position is not the invalid sentinel.</returns>
    public static bool IsMousePosValid(ImVec2 mousePos)
    {
        return mousePos.X >= MouseInvalidPos && mousePos.Y >= MouseInvalidPos;
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button is currently held.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> when the button is down and readable.</returns>
    public static bool IsMouseDown(ImGuiMouseButton button)
    {
        return IsMouseDown(button, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button is currently held and readable by an owner.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> when the button is down and the owner may read it.</returns>
    internal static bool IsMouseDown(ImGuiMouseButton button, uint ownerId)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        return g.IO.MouseDown[(int)button] && TestKeyOwner(MouseButtonToKey(button), ownerId);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was clicked, optionally repeating.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="repeat">Whether a held button should also report typematic repeats.</param>
    /// <returns><see langword="true"/> on the click edge, or on a repeat when requested.</returns>
    public static bool IsMouseClicked(ImGuiMouseButton button, bool repeat = false)
    {
        return IsMouseClicked(button, repeat ? ImGuiInputFlags.Repeat : ImGuiInputFlags.None, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was clicked, with explicit flags and owner.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="flags">The supported repeat flags.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> when the click satisfies the flags and the owner may read the button.</returns>
    internal static bool IsMouseClicked(ImGuiMouseButton button, ImGuiInputFlags flags, uint ownerId = ImGuiKeyOwner_Any)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        if (!g.IO.MouseDown[(int)button])
            return false;
        float t = g.IO.MouseDownDuration[(int)button];
        if (t < 0.0f)
            return false;
        ImAssert.Check(((uint)flags & ~(uint)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedByIsMouseClicked) == 0,
            "Passing flags not supported by IsMouseClicked.");

        bool repeat = (flags & ImGuiInputFlags.Repeat) != 0;
        bool pressed = t == 0.0f
            || (repeat && t > g.IO.KeyRepeatDelay
                && CalcTypematicRepeatAmount(t - g.IO.DeltaTime, t, g.IO.KeyRepeatDelay, g.IO.KeyRepeatRate) > 0);
        if (!pressed)
            return false;
        if (!TestKeyOwner(MouseButtonToKey(button), ownerId))
            return false;
        return true;
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was released this frame.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> on the release edge.</returns>
    public static bool IsMouseReleased(ImGuiMouseButton button)
    {
        return IsMouseReleased(button, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was released this frame and is readable by an owner.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> on the release edge when the owner may read the button.</returns>
    internal static bool IsMouseReleased(ImGuiMouseButton button, uint ownerId)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        return g.IO.MouseReleased[(int)button] && TestKeyOwner(MouseButtonToKey(button), ownerId);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was released at least <paramref name="delay"/>
    /// seconds ago, without regard to the current frame's release edge. Mirrors canonical
    /// <c>ImGui::IsMouseReleasedWithDelay</c>.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="delay">
    /// The required delay in seconds since release, or a negative value to use
    /// <see cref="ImGuiIO.MouseSingleClickDelay"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the button is up and the release happened within the frame that
    /// crosses the delay boundary.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The canonical helper distinguishes a single click from the start of a double click by waiting
    /// out <c>IO.MouseSingleClickDelay</c>, which is always greater than
    /// <c>IO.MouseDoubleClickTime</c>. Callers typically combine the result with a test of
    /// <c>IO.MouseClickedLastCount == 1</c>.
    /// </para>
    /// <para>
    /// The window is the single frame on which the elapsed time first reaches the delay: the upper
    /// bound rejects any later frame. <see cref="ImGuiIO.DeltaTime"/> is the duration of the frame in
    /// which the release occurred, so it is subtracted from the elapsed time to recover the elapsed
    /// time at the start of that frame.
    /// </para>
    /// </remarks>
    public static bool IsMouseReleasedWithDelay(ImGuiMouseButton button, float delay)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        if (IsMouseDown(button))
            return false;
        if (delay < 0.0f)
            delay = g.IO.MouseSingleClickDelay;
        float timeSinceRelease = (float)(g.Time - g.IO.MouseReleasedTime[(int)button]);
        return (timeSinceRelease - g.IO.DeltaTime < delay) && (timeSinceRelease >= delay);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was double-clicked.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns><see langword="true"/> when the click count reached two.</returns>
    public static bool IsMouseDoubleClicked(ImGuiMouseButton button)
    {
        return IsMouseDoubleClicked(button, ImGuiKeyOwner_Any);
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button was double-clicked and is readable by an owner.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="ownerId">The requesting owner.</param>
    /// <returns><see langword="true"/> when the click count reached two and the owner may read the button.</returns>
    internal static bool IsMouseDoubleClicked(ImGuiMouseButton button, uint ownerId)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        return g.IO.MouseClickedCount[(int)button] == 2 && TestKeyOwner(MouseButtonToKey(button), ownerId);
    }

    /// <summary>
    /// Returns how many successive clicks the last click on a mouse button was part of. Mirrors
    /// canonical <c>ImGui::GetMouseClickedCount</c>.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <returns>The recorded click count: one for a single click and two or more for a repeated click.</returns>
    /// <remarks>
    /// <see cref="ImGuiIO.MouseClickedCount"/> is refreshed by the mouse update pass each time a button
    /// goes down; it is not reset on release.
    /// </remarks>
    public static int GetMouseClickedCount(ImGuiMouseButton button)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        return g.IO.MouseClickedCount[(int)button];
    }

    /// <summary>
    /// Resolves the active overlay identifier for an item that has no identifier of its own. Mirrors
    /// the canonical file-local helper <c>LastItemOverlayButtonForNullId</c>
    /// </summary>
    /// <param name="mouseButton">The mouse button the interaction is tested against.</param>
    /// <returns>The generated identifier, which is the active identifier while the button is held.</returns>
    /// <remarks>
    /// A null item identifier is used by overlay-style widgets that draw outside the normal item
    /// submission path. The identifier is derived from the last item's rectangle so repeated frames
    /// produce the same value, and it is made active only while the mouse is hovering with the button
    /// newly pressed.
    /// </remarks>
    private static uint LastItemOverlayButtonForNullId(ImGuiMouseButton mouseButton)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.LastItemData.ID == 0, "The overlay fallback identifier requires a null last-item identifier.");
        ImGuiWindow window = g.CurrentWindow!;
        uint id = GetIDFromRectangle(g.LastItemData.Rect);
        if (g.IO.MouseClicked[(int)mouseButton] && ItemHoverable(g.LastItemData.Rect, id, g.LastItemData.ItemFlags))
        {
            SetActiveID(id, window);
            FocusWindow(window);
        }
        else if (g.ActiveId == id)
        {
            KeepAliveID(id);
            if (!g.IO.MouseDown[(int)mouseButton])
                ClearActiveID();
        }
        return id;
    }

    /// <summary>
    /// Returns the disambiguated click count for the last submitted item, delaying a single click so it
    /// can be told apart from the first half of a double click. Mirrors canonical
    /// <c>ImGui::GetItemClickedCountWithSingleClickDelay</c>
    /// </summary>
    /// <param name="mouseButton">The mouse button whose clicks are counted.</param>
    /// <param name="delay">
    /// The single-click delay in seconds. A negative value uses the larger of
    /// <see cref="ImGuiIO.MouseSingleClickDelay"/> and <c>IO.MouseDoubleClickTime + 0.01f</c>.
    /// </param>
    /// <returns>
    /// Zero when neither branch applies, one after the delayed single-click delay elapses, or two and
    /// above for a double click or a subsequent repeated click.
    /// </returns>
    /// <remarks>
    /// The item is identified by <see cref="ImGuiLastItemData.ID"/>, or by an overlay rectangle
    /// identifier derived through <see cref="LastItemOverlayButtonForNullId"/> when it is zero. The
    /// delayed single-click branch additionally requires that the item is the last active item and that
    /// <see cref="ImGuiIO.MouseClickedLastCount"/> is one.
    /// </remarks>
    public static int GetItemClickedCountWithSingleClickDelay(ImGuiMouseButton mouseButton = ImGuiMouseButton.Left, float delay = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;

        // A double click or a later repeated click is reported immediately.
        if (g.IO.MouseClickedCount[(int)mouseButton] >= 2 && IsItemClicked(mouseButton))
            return g.IO.MouseClickedCount[(int)mouseButton];

        // A single click is reported only after the delay has elapsed.
        uint id = g.LastItemData.ID;
        if (id == 0)
            id = LastItemOverlayButtonForNullId(mouseButton);
        if (g.LastActiveId == id)
        {
            if (delay >= 0.0f)
                delay = MathF.Max(delay, g.IO.MouseDoubleClickTime + 0.01f);
            if (IsMouseReleasedWithDelay(mouseButton, delay) && g.IO.MouseClickedLastCount[(int)mouseButton] == 1)
                return 1;
        }
        return 0;
    }

    /// <summary>
    /// Gets a value indicating whether the mouse is hovering a rectangle, optionally clipped.
    /// </summary>
    /// <param name="rMin">The rectangle's upper-left corner.</param>
    /// <param name="rMax">The rectangle's lower-right corner.</param>
    /// <param name="clip">Whether to clip the rectangle against the current window's clip rectangle.</param>
    /// <returns><see langword="true"/> when the expanded, clipped rectangle contains the mouse and overlaps the viewport.</returns>
    /// <remarks>The rectangle is expanded by <c>Style.TouchExtraPadding</c> to be generous on imprecise input systems.</remarks>
    public static bool IsMouseHoveringRect(ImVec2 rMin, ImVec2 rMax, bool clip = true)
    {
        ImGuiContext g = GetCurrentContext()!;

        ImRect rectClipped = new(rMin, rMax);
        if (clip)
            rectClipped.ClipWith(g.CurrentWindow!.ClipRect);

        if (!RectContainsWithPad(rectClipped, g.IO.MousePos, g.Style.TouchExtraPadding))
            return false;
        if (!g.MouseViewport!.GetMainRect().Overlaps(rectClipped))
            return false;
        return true;
    }

    /// <summary>
    /// Tests whether a point lies inside a rectangle expanded by a padding vector.
    ///
    /// </summary>
    /// <param name="rect">The rectangle to test.</param>
    /// <param name="p">The point to test.</param>
    /// <param name="pad">The padding added on every side.</param>
    /// <returns><see langword="true"/> when the padded rectangle contains the point.</returns>
    private static bool RectContainsWithPad(ImRect rect, ImVec2 p, ImVec2 pad)
    {
        return p.X >= rect.Min.X - pad.X && p.Y >= rect.Min.Y - pad.Y
            && p.X < rect.Max.X + pad.X && p.Y < rect.Max.Y + pad.Y;
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button traveled past the drag threshold.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="lockThreshold">The threshold distance; a negative value selects <c>IO.MouseDragThreshold</c>.</param>
    /// <returns><see langword="true"/> when the maximum recorded drag distance reached the threshold.</returns>
    /// <remarks>This does not test whether the button is pressed, so it is valid during the release frame.</remarks>
    internal static bool IsMouseDragPastThreshold(ImGuiMouseButton button, float lockThreshold = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        if (lockThreshold < 0.0f)
            lockThreshold = g.IO.MouseDragThreshold;
        return g.IO.MouseDragMaxDistanceSqr[(int)button] >= lockThreshold * lockThreshold;
    }

    /// <summary>
    /// Gets a value indicating whether a mouse button is dragging past the drag threshold.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="lockThreshold">The threshold distance; a negative value selects <c>IO.MouseDragThreshold</c>.</param>
    /// <returns><see langword="true"/> when the button is down and dragging past the threshold.</returns>
    public static bool IsMouseDragging(ImGuiMouseButton button, float lockThreshold = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        if (!g.IO.MouseDown[(int)button])
            return false;
        return IsMouseDragPastThreshold(button, lockThreshold);
    }

    /// <summary>
    /// Gets the current mouse position.
    /// </summary>
    /// <returns>The current <c>IO.MousePos</c>.</returns>
    public static ImVec2 GetMousePos()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.IO.MousePos;
    }

    /// <summary>
    /// Moves the mouse position as if the backend had reported it, clearing the frame's delta.
    /// </summary>
    /// <param name="pos">The new mouse position.</param>
    /// <remarks>
    /// This is named "Teleport" rather than "Set" because writing <c>MousePosPrev</c> clears
    /// <c>MouseDelta</c>. It is expected to be called only when the backend supports
    /// <c>ImGuiBackendFlags_HasSetMousePos</c>; <c>io.WantSetMousePos</c> is set so the backend can
    /// apply the move.
    /// </remarks>
    internal static void TeleportMousePos(ImVec2 pos)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.IO.MousePos = g.IO.MousePosPrev = pos;
        g.IO.MouseDelta = new ImVec2(0.0f, 0.0f);
        g.IO.WantSetMousePos = true;
    }

    /// <summary>
    /// Gets the mouse position captured when the current popup was opened.
    /// </summary>
    /// <returns>The popup's opening mouse position, or the current mouse position when no popup is open.</returns>
    public static ImVec2 GetMousePosOnOpeningCurrentPopup()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.BeginPopupStack.Count > 0)
            return g.OpenPopupStack[g.BeginPopupStack.Count - 1].OpenMousePos;
        return g.IO.MousePos;
    }

    /// <summary>
    /// Gets a value indicating whether any mouse button is held.
    /// </summary>
    /// <returns><see langword="true"/> when any button is down.</returns>
    public static bool IsAnyMouseDown()
    {
        ImGuiContext g = GetCurrentContext()!;
        for (int n = 0; n < g.IO.MouseDown.Length; n++)
            if (g.IO.MouseDown[n])
                return true;
        return false;
    }

    /// <summary>
    /// Gets the delta from the initial clicking position while a button is held or was just released.
    /// </summary>
    /// <param name="button">The mouse button to query.</param>
    /// <param name="lockThreshold">The threshold distance; a negative value selects <c>IO.MouseDragThreshold</c>.</param>
    /// <returns>The drag delta, or zero until the drag passed the threshold.</returns>
    public static ImVec2 GetMouseDragDelta(ImGuiMouseButton button = ImGuiMouseButton.Left, float lockThreshold = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        if (lockThreshold < 0.0f)
            lockThreshold = g.IO.MouseDragThreshold;
        if (g.IO.MouseDown[(int)button] || g.IO.MouseReleased[(int)button])
            if (g.IO.MouseDragMaxDistanceSqr[(int)button] >= lockThreshold * lockThreshold)
                if (IsMousePosValid(g.IO.MousePos) && IsMousePosValid(g.IO.MouseClickedPos[(int)button]))
                    return g.IO.MousePos - g.IO.MouseClickedPos[(int)button];
        return new ImVec2(0.0f, 0.0f);
    }

    /// <summary>
    /// Resets the drag origin for a mouse button to the current mouse position.
    /// </summary>
    /// <param name="button">The mouse button whose drag origin is reset.</param>
    /// <remarks>The maximum recorded drag distance is intentionally not reset.</remarks>
    public static void ResetMouseDragDelta(ImGuiMouseButton button = ImGuiMouseButton.Left)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((int)button >= 0 && (int)button < (int)ImGuiMouseButton.COUNT, "Mouse button out of range.");
        g.IO.MouseClickedPos[(int)button] = g.IO.MousePos;
    }

    /// <summary>
    /// Gets the desired mouse cursor shape for the frame.
    /// </summary>
    /// <returns>The cursor shape requested by widgets during the frame.</returns>
    public static ImGuiMouseCursor GetMouseCursor()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.MouseCursor;
    }

    /// <summary>
    /// Sets the desired mouse cursor shape for the frame.
    /// </summary>
    /// <param name="cursorType">The cursor shape to request.</param>
    /// <remarks>
    /// Values outside the enumerated range are accepted so custom cursors can be hacked in; a custom
    /// backend may handle them before its new-frame call.
    /// </remarks>
    public static void SetMouseCursor(ImGuiMouseCursor cursorType)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.MouseCursor = cursorType;
    }

    /// <summary>
    /// Advances the mouse state from the queued input applied earlier in the frame.
    /// </summary>
    /// <remarks>
    /// Ports the wheel axis-swap flag, mouse position flooring, mouse delta, the stationary-hover
    /// timer, and per-button click, double-click, release, duration, and drag-distance bookkeeping.
    /// </remarks>
    internal static void UpdateMouseInputs()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;

        // Holding Shift while using the vertical wheel triggers horizontal scroll instead, except on
        // macOS where the operating system already performs the swap.
        io.MouseWheelRequestAxisSwap = io.KeyShift && !io.ConfigMacOSXBehaviors;

        // Round the mouse position so downstream code does not have to tolerate sub-pixel values.
        if (IsMousePosValid())
        {
            ImVec2 floored = new(MathF.Floor(io.MousePos.X), MathF.Floor(io.MousePos.Y));
            io.MousePos = floored;
            g.MouseLastValidPos = floored;
        }

        // If the mouse just appeared or disappeared, cancel out movement in MouseDelta.
        if (IsMousePosValid() && IsMousePosValid(io.MousePosPrev))
            io.MouseDelta = io.MousePos - io.MousePosPrev;
        else
            io.MouseDelta = new ImVec2(0.0f, 0.0f);

        // Update the stationary timer; touch and pen sources use a slightly higher threshold.
        float mouseStationaryThreshold = (io.MouseSource == ImGuiMouseSource.Mouse) ? 2.0f : 3.0f;
        bool mouseStationary = io.MouseDelta.LengthSquared() <= mouseStationaryThreshold * mouseStationaryThreshold;
        g.MouseStationaryTimer = mouseStationary ? (g.MouseStationaryTimer + io.DeltaTime) : 0.0f;

        // Mouse movement re-enables mouse hovering in case it was disabled by keyboard/gamepad navigation.
        if (io.MouseDelta.X != 0.0f || io.MouseDelta.Y != 0.0f)
            g.NavHighlightItemUnderNav = false;

        for (int i = 0; i < io.MouseDown.Length; i++)
        {
            io.MouseClicked[i] = io.MouseDown[i] && io.MouseDownDuration[i] < 0.0f;
            io.MouseClickedCount[i] = 0; // Filled in below.
            io.MouseReleased[i] = !io.MouseDown[i] && io.MouseDownDuration[i] >= 0.0f;
            if (io.MouseReleased[i])
                io.MouseReleasedTime[i] = g.Time;
            io.MouseDownDurationPrev[i] = io.MouseDownDuration[i];
            io.MouseDownDuration[i] = io.MouseDown[i] ? (io.MouseDownDuration[i] < 0.0f ? 0.0f : io.MouseDownDuration[i] + io.DeltaTime) : -1.0f;
            if (io.MouseClicked[i])
            {
                bool isRepeatedClick = false;
                if ((float)(g.Time - io.MouseClickedTime[i]) < io.MouseDoubleClickTime)
                {
                    ImVec2 deltaFromClickPos = IsMousePosValid(io.MousePos) ? (io.MousePos - io.MouseClickedPos[i]) : new ImVec2(0.0f, 0.0f);
                    if (deltaFromClickPos.LengthSquared() < io.MouseDoubleClickMaxDist * io.MouseDoubleClickMaxDist)
                        isRepeatedClick = true;
                }
                if (isRepeatedClick)
                    io.MouseClickedLastCount[i]++;
                else
                    io.MouseClickedLastCount[i] = 1;
                io.MouseClickedTime[i] = g.Time;
                io.MouseClickedPos[i] = io.MousePos;
                io.MouseClickedCount[i] = io.MouseClickedLastCount[i];
                io.MouseDragMaxDistanceAbs[i] = new ImVec2(0.0f, 0.0f);
                io.MouseDragMaxDistanceSqr[i] = 0.0f;
            }
            else if (io.MouseDown[i])
            {
                // Maintain the maximum distance reached from the initial click position, used by drag thresholds.
                ImVec2 deltaFromClickPos = IsMousePosValid(io.MousePos) ? (io.MousePos - io.MouseClickedPos[i]) : new ImVec2(0.0f, 0.0f);
                io.MouseDragMaxDistanceSqr[i] = Math.Max(io.MouseDragMaxDistanceSqr[i], deltaFromClickPos.LengthSquared());
                io.MouseDragMaxDistanceAbs[i].X = Math.Max(io.MouseDragMaxDistanceAbs[i].X, deltaFromClickPos.X < 0.0f ? -deltaFromClickPos.X : deltaFromClickPos.X);
                io.MouseDragMaxDistanceAbs[i].Y = Math.Max(io.MouseDragMaxDistanceAbs[i].Y, deltaFromClickPos.Y < 0.0f ? -deltaFromClickPos.Y : deltaFromClickPos.Y);
            }

            // io.MouseDoubleClicked[] is maintained as a legacy service.
            io.MouseDoubleClicked[i] = io.MouseClickedCount[i] == 2;

            // Clicking any mouse button reactivates mouse hovering which may have been disabled by navigation.
            if (io.MouseClicked[i])
                g.NavHighlightItemUnderNav = false;
        }
    }

    #endregion

    #region Mouse wheel

    /// <summary>
    /// The time a wheel-scrolled window stays locked so it does not chase child windows (<c>WINDOWS_MOUSE_WHEEL_SCROLL_LOCK_TIMER = 0.70f</c>).
    /// </summary>
    private const float WheelScrollLockTimer = 0.70f;

    /// <summary>
    /// Locks or releases the window that receives wheel scrolling.
    /// </summary>
    /// <param name="window">The window to lock, or <see langword="null"/> to release the lock.</param>
    /// <param name="wheelAmount">The wheel amount that renews the release timer.</param>
    /// <remarks>
    /// The release timer grows with the wheel magnitude up to <see cref="WheelScrollLockTimer"/>.
    /// Changing the locked window latches the reference mouse position; releasing resets the start
    /// frame and the per-axis moving average.
    /// </remarks>
    private static void LockWheelingWindow(ImGuiWindow? window, float wheelAmount)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (window != null)
            g.WheelingWindowReleaseTimer = Math.Min(
                g.WheelingWindowReleaseTimer + MathF.Abs(wheelAmount) * WheelScrollLockTimer,
                WheelScrollLockTimer);
        else
            g.WheelingWindowReleaseTimer = 0.0f;
        if (g.WheelingWindow == window)
            return;
        g.WheelingWindow = window;
        g.WheelingWindowRefMousePos = g.IO.MousePos;
        if (window == null)
        {
            g.WheelingWindowStartFrame = -1;
            g.WheelingAxisAvg = new ImVec2(0.0f, 0.0f);
        }
    }

    /// <summary>
    /// Finds the window that should consume the supplied wheel amount.
    /// </summary>
    /// <param name="wheel">The horizontal and vertical wheel amounts.</param>
    /// <returns>The window that wants scrolling, or <see langword="null"/> when the choice is ambiguous.</returns>
    /// <remarks>
    /// Each axis bubbles up from <c>HoveredWindow</c> through parents until a window with scroll room
    /// that accepts mouse scrolling is found. When the two axes select different windows the first
    /// frame of a two-axis wheel defers the choice and stores the amount in
    /// <c>WheelingWindowWheelRemainder</c>; subsequent frames pick the axis with the larger moving
    /// average.
    /// </remarks>
    private static ImGuiWindow? FindBestWheelingWindow(ImVec2 wheel)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow?[] windows = new ImGuiWindow?[2];
        for (int axis = 0; axis < 2; axis++)
        {
            if ((axis == 0 ? wheel.X : wheel.Y) == 0.0f)
                continue;
            ImGuiWindow? window = g.HoveredWindow;
            windows[axis] = window;
            // Bubble up into the parent while the window is a child that does not allow any scrolling
            // or that refuses mouse scrolling.
            while (window != null && (window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
            {
                bool hasScrolling = (axis == 0 ? window.ScrollMax.X : window.ScrollMax.Y) != 0.0f;
                bool inputsDisabled = (window.Flags & ImGuiWindowFlags.NoScrollWithMouse) != 0
                    && (window.Flags & ImGuiWindowFlags.NoMouseInputs) == 0;
                if (hasScrolling && !inputsDisabled)
                    break; // Select this window.
                window = window.ParentWindow;
                windows[axis] = window;
            }
        }
        if (windows[0] == null && windows[1] == null)
            return null;

        // If there is only one candidate window or only one axis, there is no ambiguity.
        if (windows[0] == windows[1] || windows[0] == null || windows[1] == null)
            return windows[1] ?? windows[0];

        // Different candidate windows: on the first frame only pick a winner if one axis is zero;
        // afterwards pick the axis with the larger moving average.
        if (g.WheelingWindowStartFrame == -1)
            g.WheelingWindowStartFrame = g.FrameCount;
        if ((g.WheelingWindowStartFrame == g.FrameCount && wheel.X != 0.0f && wheel.Y != 0.0f)
            || g.WheelingAxisAvg.X == g.WheelingAxisAvg.Y)
        {
            g.WheelingWindowWheelRemainder = wheel;
            return null;
        }
        return (g.WheelingAxisAvg.X > g.WheelingAxisAvg.Y) ? windows[0] : windows[1];
    }

    /// <summary>
    /// Applies queued mouse-wheel input to the wheeled window, including the Control-wheel font zoom.
    /// </summary>
    /// <remarks>
    /// Releases a stale wheel lock, reads the owner-gated wheel amounts, applies the Control-wheel
    /// font zoom path, performs the Shift axis swap, and finally scrolls the best candidate window by
    /// a step derived from its font reference size.
    /// </remarks>
    internal static void UpdateMouseWheel()
    {
        ImGuiContext g = GetCurrentContext()!;

        // Reset the locked window if the mouse moved past the drag threshold or the timer elapsed.
        if (g.WheelingWindow != null)
        {
            g.WheelingWindowReleaseTimer -= g.IO.DeltaTime;
            if (IsMousePosValid() && (g.IO.MousePos - g.WheelingWindowRefMousePos).LengthSquared() > g.IO.MouseDragThreshold * g.IO.MouseDragThreshold)
                g.WheelingWindowReleaseTimer = 0.0f;
            if (g.WheelingWindowReleaseTimer <= 0.0f)
                LockWheelingWindow(null, 0.0f);
        }

        ImGuiWindow? mouseWindow = g.WheelingWindow ?? g.HoveredWindow;
        if (mouseWindow == null || mouseWindow.Collapsed)
            return;

        uint ownerId = mouseWindow.ID;
        ImVec2 wheel;
        wheel.X = TestKeyOwner(ImGuiKey.MouseWheelX, ownerId) ? g.IO.MouseWheelH : 0.0f;
        wheel.Y = TestKeyOwner(ImGuiKey.MouseWheelY, ownerId) ? g.IO.MouseWheel : 0.0f;
        if (g.WheelingWindow != null)
        {
            SetKeyOwner(ImGuiKey.MouseWheelX, ownerId);
            SetKeyOwner(ImGuiKey.MouseWheelY, ownerId);
        }

        // Control-wheel font zoom (legacy feature): scale the hovered window's font and adjust its size.
        if (wheel.Y != 0.0f && g.IO.KeyCtrl && g.IO.FontAllowUserScaling)
        {
            LockWheelingWindow(mouseWindow, wheel.Y);
            ImGuiWindow zoomWindow = mouseWindow;
            float newFontScale = Math.Clamp(zoomWindow.FontWindowScale + g.IO.MouseWheel * 0.10f, 0.50f, 2.50f);
            float scale = newFontScale / zoomWindow.FontWindowScale;
            zoomWindow.FontWindowScale = newFontScale;
            if (zoomWindow == zoomWindow.RootWindow)
            {
                ImVec2 offset = zoomWindow.Size * (1.0f - scale) * (g.IO.MousePos - zoomWindow.Pos) / zoomWindow.Size;
                SetWindowPos(zoomWindow, zoomWindow.Pos + offset, 0);
                zoomWindow.Size = new ImVec2((float)(int)(zoomWindow.Size.X * scale), (float)(int)(zoomWindow.Size.Y * scale));
                zoomWindow.SizeFull = new ImVec2((float)(int)(zoomWindow.SizeFull.X * scale), (float)(int)(zoomWindow.SizeFull.Y * scale));
                MarkIniSettingsDirty(zoomWindow);
            }
            return;
        }
        if (g.IO.KeyCtrl)
            return;

        // Read about io.MouseWheelRequestAxisSwap and its Emscripten caveat in UpdateMouseInputs().
        if (g.IO.MouseWheelRequestAxisSwap)
            wheel = new ImVec2(wheel.Y, 0.0f);

        // Maintain a rough average of moving magnitude on both axes.
        g.WheelingAxisAvg = new ImVec2(
            ExponentialMovingAverage(g.WheelingAxisAvg.X, MathF.Abs(wheel.X), 30),
            ExponentialMovingAverage(g.WheelingAxisAvg.Y, MathF.Abs(wheel.Y), 30));

        // Reinject a wheel amount deferred by FindBestWheelingWindow() on the ambiguous first frame.
        wheel += g.WheelingWindowWheelRemainder;
        g.WheelingWindowWheelRemainder = new ImVec2(0.0f, 0.0f);
        if (wheel.X == 0.0f && wheel.Y == 0.0f)
            return;

        // Find the target and apply scrolling, disabling the weaker axis when both scroll.
        ImGuiWindow? window = g.WheelingWindow ?? FindBestWheelingWindow(wheel);
        if (window != null
            && (window.Flags & ImGuiWindowFlags.NoScrollWithMouse) == 0
            && (window.Flags & ImGuiWindowFlags.NoMouseInputs) == 0)
        {
            bool[] doScroll =
            {
                wheel.X != 0.0f && window.ScrollMax.X != 0.0f,
                wheel.Y != 0.0f && window.ScrollMax.Y != 0.0f,
            };
            if (doScroll[0] && doScroll[1])
                doScroll[(g.WheelingAxisAvg.X > g.WheelingAxisAvg.Y) ? 1 : 0] = false;
            if (doScroll[0])
            {
                LockWheelingWindow(window, wheel.X);
                float maxStep = window.InnerRect.GetWidth() * 0.67f;
                float scrollStep = (float)(int)Math.Min(2 * window.FontRefSize, maxStep);
                SetScrollX(window, window.Scroll.X - wheel.X * scrollStep);
                g.WheelingWindowScrolledFrame = g.FrameCount;
            }
            if (doScroll[1])
            {
                LockWheelingWindow(window, wheel.Y);
                float maxStep = window.InnerRect.GetHeight() * 0.67f;
                float scrollStep = (float)(int)Math.Min(5 * window.FontRefSize, maxStep);
                SetScrollY(window, window.Scroll.Y - wheel.Y * scrollStep);
                g.WheelingWindowScrolledFrame = g.FrameCount;
            }
        }
    }

    /// <summary>
    /// Updates an exponential moving average by one sample. Mirrors canonical <c>ImExponentialMovingAverage</c>.
    /// </summary>
    /// <param name="avg">The current average.</param>
    /// <param name="sample">The new sample.</param>
    /// <param name="n">The averaging window.</param>
    /// <returns>The updated average.</returns>
    private static float ExponentialMovingAverage(float avg, float sample, int n)
    {
        avg -= avg / n;
        avg += sample / n;
        return avg;
    }

    #endregion

    #region Hovered window and capture flags

    /// <summary>
    /// Finds the hovered window by scanning the display list from front to back.
    /// </summary>
    /// <param name="pos">The position to hit-test, usually the mouse position.</param>
    /// <param name="findFirstAndSkipInactive">Whether to return the first hit immediately without tracking the window under a moving window, as used by the test engine.</param>
    /// <param name="outHoveredWindow">Receives the front-most window under the position, or <see langword="null"/>.</param>
    /// <param name="outHoveredWindowUnderMovingWindow">Receives the front-most window under the position that is not part of the moving window's dock tree, or <see langword="null"/>.</param>
    /// <remarks>
    /// <para>
    /// Inactive, hidden, and mouse-input-disabled windows are skipped, as are windows on another
    /// viewport. The hit rectangle is padded more generously when the window can be resized. One
    /// rectangular hit-test hole per window is honored.
    /// </para>
    /// <para>
    /// While a window is being moved it is forced as the hovered window and its viewport is
    /// temporarily swapped to the mouse viewport, because it may lose its viewport during the
    /// undocking frame.
    /// </para>
    /// </remarks>
    internal static void FindHoveredWindowEx(ImVec2 pos, bool findFirstAndSkipInactive, out ImGuiWindow? outHoveredWindow, out ImGuiWindow? outHoveredWindowUnderMovingWindow)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow? hoveredWindow = null;
        ImGuiWindow? hoveredWindowUnderMovingWindow = null;

        // Special handling for the window being moved: ignore the mouse viewport check.
        ImGuiViewportP? backupMovingWindowViewport = null;
        if (!findFirstAndSkipInactive && g.MovingWindow != null)
        {
            backupMovingWindowViewport = g.MovingWindow.Viewport;
            g.MovingWindow.Viewport = g.MouseViewport;
            if ((g.MovingWindow.Flags & ImGuiWindowFlags.NoMouseInputs) == 0)
                hoveredWindow = g.MovingWindow;
        }

        ImVec2 paddingRegular = g.Style.TouchExtraPadding;
        ImVec2 paddingForResize = new(
            Math.Max(g.Style.TouchExtraPadding.X, g.Style.WindowBorderHoverPadding),
            Math.Max(g.Style.TouchExtraPadding.Y, g.Style.WindowBorderHoverPadding));
        for (int i = g.Windows.Count - 1; i >= 0; i--)
        {
            ImGuiWindow window = g.Windows[i];
            if (!window.WasActive || window.Hidden)
                continue;
            if ((window.Flags & ImGuiWindowFlags.NoMouseInputs) != 0)
                continue;
            ImAssert.Check(window.Viewport != null, "Window without viewport.");
            if (window.Viewport != g.MouseViewport)
                continue;

            // Using the clipped AABB, a child window is typically clipped by its parent.
            ImVec2 hitPadding = (window.Flags & (ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize)) != 0
                ? paddingRegular
                : paddingForResize;
            if (!RectContainsWithPad(window.OuterRectClipped, pos, hitPadding))
                continue;

            // Support for one rectangular hole in a given window.
            if (window.HitTestHoleSize.X != 0)
            {
                ImVec2 holePos = new(
                    window.Pos.X + window.HitTestHoleOffset.X,
                    window.Pos.Y + window.HitTestHoleOffset.Y);
                ImVec2 holeSize = new(window.HitTestHoleSize.X, window.HitTestHoleSize.Y);
                if (new ImRect(holePos, holePos + holeSize).Contains(pos))
                    continue;
            }

            if (findFirstAndSkipInactive)
            {
                hoveredWindow = window;
                break;
            }
            else
            {
                hoveredWindow ??= window;
                if (hoveredWindowUnderMovingWindow == null
                    && (g.MovingWindow == null || window.RootWindowDockTree != g.MovingWindow.RootWindowDockTree))
                    hoveredWindowUnderMovingWindow = window;
                if (hoveredWindow != null && hoveredWindowUnderMovingWindow != null)
                    break;
            }
        }

        outHoveredWindow = hoveredWindow;
        outHoveredWindowUnderMovingWindow = hoveredWindowUnderMovingWindow;
        if (!findFirstAndSkipInactive && g.MovingWindow != null)
            g.MovingWindow.Viewport = backupMovingWindowViewport;
    }

    /// <summary>
    /// Finds the hovered window, returning it and reporting only the window under the moving window.
    /// </summary>
    /// <param name="pos">The position to hit-test, usually the mouse position.</param>
    /// <param name="findFirstAndSkipInactive">Whether to return the first hit immediately without tracking the window under a moving window.</param>
    /// <param name="outHoveredWindowUnderMovingWindow">Receives the front-most window under the position that is not part of the moving window's dock tree.</param>
    /// <returns>The front-most window under the position, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Convenience overload matching the binding signature in CONTRACT.md section 5; the canonical
    /// form returns the hovered window through an output parameter.
    /// </remarks>
    internal static ImGuiWindow? FindHoveredWindowEx(ImVec2 pos, bool findFirstAndSkipInactive, out ImGuiWindow? outHoveredWindowUnderMovingWindow)
    {
        FindHoveredWindowEx(pos, findFirstAndSkipInactive, out ImGuiWindow? hoveredWindow, out ImGuiWindow? underMovingWindow);
        outHoveredWindowUnderMovingWindow = underMovingWindow;
        return hoveredWindow;
    }

    /// <summary>
    /// Updates hovered-window and mouse/keyboard capture flags for the frame.
    /// </summary>
    /// <param name="mousePos">The mouse position used for hover hit-testing.</param>
    /// <remarks>
    /// <para>
    /// Resolves <c>HoveredWindow</c> and <c>HoveredWindowUnderMovingWindow</c>, culls hover behind
    /// modal windows and when mouse input is disabled, tracks click ownership per button, and derives
    /// <c>WantCaptureMouse</c>, <c>WantCaptureKeyboard</c>, and <c>WantTextInput</c>.
    /// </para>
    /// <para>
    /// The top-most modal window is resolved directly from <see cref="ImGuiContext.OpenPopupStack"/>,
    /// because the popup subsystem is owned by a later chunk. The popup stack's window and flag fields
    /// already exist, so the modal exclusion behaves as canonical; when the popup helpers land, this
    /// scan should be replaced by <c>GetTopMostPopupModal()</c>.
    /// </para>
    /// </remarks>
    internal static void UpdateHoveredWindowAndCaptureFlags(ImVec2 mousePos)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;

        g.WindowsBorderHoverPadding = Math.Max(
            Math.Max(g.Style.TouchExtraPadding.X, g.Style.TouchExtraPadding.Y),
            g.Style.WindowBorderHoverPadding);

        // Find the window hovered by the mouse.
        bool clearHoveredWindows = false;
        FindHoveredWindowEx(mousePos, false, out ImGuiWindow? hoveredWindow, out ImGuiWindow? hoveredWindowUnderMovingWindow);
        g.HoveredWindow = hoveredWindow;
        g.HoveredWindowUnderMovingWindow = hoveredWindowUnderMovingWindow;
        ImAssert.Check(g.HoveredWindow == null || g.HoveredWindow == g.MovingWindow || g.HoveredWindow.Viewport == g.MouseViewport,
            "Hovered window must belong to the mouse viewport.");
        g.HoveredWindowBeforeClear = g.HoveredWindow;

        // Modal windows prevent the mouse from hovering behind them.
        ImGuiWindow? modalWindow = FindTopMostModalPopupWindow();
        if (modalWindow != null && g.HoveredWindow != null
            && !IsWindowWithinBeginStackOf(g.HoveredWindow.RootWindow!, modalWindow))
            clearHoveredWindows = true;

        // Disabled mouse hovering.
        if ((io.ConfigFlags & ImGuiConfigFlags.NoMouse) != 0)
            clearHoveredWindows = true;

        // Track click ownership: a click outside a window is owned by the application and does not
        // request capture, even while dragging over windows afterward.
        bool hasOpenPopup = g.OpenPopupStack.Count > 0;
        bool hasOpenModal = modalWindow != null;
        int mouseEarliestDown = -1;
        bool mouseAnyDown = false;
        for (int i = 0; i < io.MouseDown.Length; i++)
        {
            if (io.MouseClicked[i])
            {
                io.MouseDownOwned[i] = g.HoveredWindow != null || hasOpenPopup;
                io.MouseDownOwnedUnlessPopupClose[i] = g.HoveredWindow != null || hasOpenModal;
            }
            mouseAnyDown |= io.MouseDown[i];
            if (io.MouseDown[i] || io.MouseReleased[i]) // Increase the release frame for the earliest-button evaluation.
                if (mouseEarliestDown == -1 || io.MouseClickedTime[i] < io.MouseClickedTime[mouseEarliestDown])
                    mouseEarliestDown = i;
        }
        bool mouseAvail = mouseEarliestDown == -1 || io.MouseDownOwned[mouseEarliestDown];
        bool mouseAvailUnlessPopupClose = mouseEarliestDown == -1 || io.MouseDownOwnedUnlessPopupClose[mouseEarliestDown];

        // If the mouse was first clicked outside the Dear ImGui bounds, cancel hovering.
        bool mouseDraggingExternPayload = g.DragDropActive && (g.DragDropSourceFlags & ImGuiDragDropFlags.SourceExtern) != 0;
        if (!mouseAvail && !mouseDraggingExternPayload)
            clearHoveredWindows = true;

        if (clearHoveredWindows)
        {
            g.HoveredWindow = null;
            g.HoveredWindowUnderMovingWindow = null;
        }

        // Update io.WantCaptureMouse for the host application.
        if (g.WantCaptureMouseNextFrame != -1)
        {
            io.WantCaptureMouse = io.WantCaptureMouseUnlessPopupClose = g.WantCaptureMouseNextFrame != 0;
        }
        else
        {
            io.WantCaptureMouse = (mouseAvail && (g.HoveredWindow != null || mouseAnyDown)) || hasOpenPopup;
            io.WantCaptureMouseUnlessPopupClose = (mouseAvailUnlessPopupClose && (g.HoveredWindow != null || mouseAnyDown)) || hasOpenModal;
        }

        // Update io.WantCaptureKeyboard for the host application.
        io.WantCaptureKeyboard = false;
        if ((io.ConfigFlags & ImGuiConfigFlags.NoKeyboard) == 0)
        {
            if (g.ActiveId != 0 || modalWindow != null)
                io.WantCaptureKeyboard = true;
            else if (io.NavActive && (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0 && io.ConfigNavCaptureKeyboard)
                io.WantCaptureKeyboard = true;
        }
        if (g.WantCaptureKeyboardNextFrame != -1) // Manual override.
            io.WantCaptureKeyboard = g.WantCaptureKeyboardNextFrame != 0;

        // Update io.WantTextInput so systems without a keyboard can show a software keyboard.
        io.WantTextInput = g.WantTextInputNextFrame != -1 && g.WantTextInputNextFrame != 0;
    }

    /// <summary>
    /// Updates hovered-window and capture flags using the current mouse position.
    /// </summary>
    /// <remarks>
    /// Convenience overload matching the binding signature in CONTRACT.md section 5; the canonical
    /// form receives the mouse position as a parameter.
    /// </remarks>
    internal static void UpdateHoveredWindowAndCaptureFlags()
    {
        ImGuiContext g = GetCurrentContext()!;
        UpdateHoveredWindowAndCaptureFlags(g.IO.MousePos);
    }

    /// <summary>
    /// Returns the top-most open popup window that carries the modal flag.
    /// </summary>
    /// <returns>The top-most modal popup window, or <see langword="null"/>.</returns>
    /// <remarks>
    /// This reads the existing <see cref="ImGuiContext.OpenPopupStack"/> because the canonical
    /// <c>GetTopMostPopupModal</c> helper belongs to the popup subsystem (a later chunk). It matches
    /// call sites that do not require the window to be active or visible.
    /// </remarks>
    private static ImGuiWindow? FindTopMostModalPopupWindow()
    {
        ImGuiContext g = GetCurrentContext()!;
        for (int n = g.OpenPopupStack.Count - 1; n >= 0; n--)
        {
            ImGuiWindow? popup = g.OpenPopupStack[n].Window;
            if (popup != null && (popup.Flags & ImGuiWindowFlags.Modal) != 0)
                return popup;
        }
        return null;
    }

    /// <summary>
    /// Tests whether a popup identifier is open at any level of the popup stack.
    /// </summary>
    /// <param name="id">The popup identifier to search for. Must be non-zero.</param>
    /// <returns><see langword="true"/> when the popup appears anywhere in the open-popup stack.</returns>
    /// <remarks>
    /// This is the <c>ImGuiPopupFlags_AnyPopupLevel</c> path of <c>IsPopupOpen</c>, inlined because
    /// the popup subsystem is owned by a later chunk.
    /// </remarks>
    private static bool IsPopupOpenAnyLevel(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        for (int n = 0; n < g.OpenPopupStack.Count; n++)
            if (g.OpenPopupStack[n].PopupId == id)
                return true;
        return false;
    }

    #endregion

    #region Mouse moving window

    /// <summary>
    /// Starts moving a window: focuses it, activates its move identifier, and sets the moving state.
    /// </summary>
    /// <param name="window">The window the user clicked to move.</param>
    /// <remarks>
    /// The active identifier is set even when the window has the no-move flag so that dragging away
    /// from it cannot activate hover on another window; in that case the moving state is not set.
    /// </remarks>
    internal static void StartMouseMovingWindow(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!;
        FocusWindow(window);
        SetActiveID(window.MoveId, window);
        if (g.IO.ConfigNavCursorVisibleAuto)
            g.NavCursorVisible = false;
        ImAssert.Check(window.RootWindowDockTree != null, "Root dock-tree window expected.");
        g.ActiveIdClickOffset = g.IO.MouseClickedPos[0] - window.RootWindowDockTree!.Pos;
        g.ActiveIdNoClearOnFocusLoss = true;
        SetActiveIdUsingAllKeyboardKeys();

        bool canMoveWindow = true;
        if ((window.Flags & ImGuiWindowFlags.NoMove) != 0
            || (window.RootWindowDockTree!.Flags & ImGuiWindowFlags.NoMove) != 0)
            canMoveWindow = false;
        if (window.DockNodeAsHost is { } hostNode)
            if (hostNode.VisibleWindow != null && (hostNode.VisibleWindow.Flags & ImGuiWindowFlags.NoMove) != 0)
                canMoveWindow = false;
        if (canMoveWindow)
            g.MovingWindow = window;
    }

    /// <summary>
    /// Stops moving a window and restores the mouse viewport.
    /// </summary>
    /// <remarks>
    /// The active identifier is intentionally not cleared here so callers can keep inputs locked; the
    /// caller is responsible for clearing it when appropriate.
    /// </remarks>
    internal static void StopMouseMovingWindow()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow? window = g.MovingWindow;

        if (window != null && window.Viewport != null)
        {
            // Multi-viewport merging (UpdateTryMergeWindowIntoHostViewport) and viewport work-rect
            // synchronization are owned by the viewport subsystem (a later chunk). With only the main
            // viewport implemented, MouseViewport and window.Viewport are the same main viewport, so
            // restoring the mouse viewport is normally a no-op. The canonical guard is preserved: a
            // target holding an accepted drag payload keeps the mouse viewport so the drop is not lost.
            if (!IsDragDropPayloadBeingAccepted())
                g.MouseViewport = window.Viewport;

            // Clear the NoInputs window flag set by the viewport system.
            bool windowCanUseInputs = ((window.Flags & ImGuiWindowFlags.NoMouseInputs) != 0
                && (window.Flags & ImGuiWindowFlags.NoNavInputs) != 0) == false;
            if (windowCanUseInputs)
                window.Viewport.Flags &= ~ImGuiViewportFlags.NoInputs;
        }
        g.MovingWindow = null;
    }

    /// <summary>
    /// Moves the active window to follow the mouse during a frame.
    /// </summary>
    /// <remarks>
    /// Called by <c>NewFrame</c>. The root dock-tree window is moved, not the child the user clicked
    /// on, so focus and active-id consistency are preserved. When the button is released, or the
    /// window is no longer submitted, moving stops and the active identifier is cleared.
    /// </remarks>
    internal static void UpdateMouseMovingWindowNewFrame()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.MovingWindow != null)
        {
            KeepAliveID(g.ActiveId);
            ImAssert.Check(g.MovingWindow.RootWindowDockTree != null, "Root dock-tree window expected.");
            ImGuiWindow movingWindow = g.MovingWindow.RootWindowDockTree!;

            // A window that stops being submitted might lose its viewport until the next Begin().
            bool windowDisappeared = !movingWindow.WasActive && !movingWindow.Active;
            if (g.IO.MouseDown[0] && IsMousePosValid() && !windowDisappeared)
            {
                ImVec2 pos = g.IO.MousePos - g.ActiveIdClickOffset;
                if (movingWindow.Pos.X != pos.X || movingWindow.Pos.Y != pos.Y)
                {
                    SetWindowPos(movingWindow, pos, ImGuiCond.Always);
                    // Viewport-owned synchronization of the window position and work rect is owned by
                    // the viewport subsystem (a later chunk) and is inert while only the main viewport
                    // is implemented.
                }
                FocusWindow(g.MovingWindow);
            }
            else
            {
                StopMouseMovingWindow();
                ClearActiveID();
            }
        }
        else
        {
            // Clicking or dragging a window with the no-move flag still sets the active identifier to
            // prevent hovering others; keep it alive until release.
            if (g.ActiveIdWindow != null && g.ActiveIdWindow.MoveId == g.ActiveId)
            {
                KeepAliveID(g.ActiveId);
                if (!g.IO.MouseDown[0])
                    ClearActiveID();
            }
        }
    }

    /// <summary>
    /// Starts moving a window, or focuses one, from a click on empty space or a title bar.
    /// </summary>
    /// <remarks>
    /// Called by <c>EndFrame</c> after all widgets have been submitted. A left click on empty space
    /// starts a move or clears focus; a right click on empty space trims popups. Moving from a title
    /// bar only is honored through <c>ImGuiWindowBgClickFlags</c>. Clicking a disabled item cancels
    /// the move and records the disabled identifier.
    /// </remarks>
    internal static void UpdateMouseMovingWindowEndFrame()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.ActiveId != 0 || (g.HoveredId != 0 && !g.HoveredIdIsDisabled))
            return;

        // Unless a window or popup just appeared.
        if (g.NavWindow != null && g.NavWindow.Appearing)
            return;

        ImGuiWindow? hoveredWindow = g.HoveredWindow;

        // Left click on empty space to focus a window and start moving it.
        if (IsMouseClicked(ImGuiMouseButton.Left, ImGuiInputFlags.None, ImGuiKeyOwner_NoOwner))
        {
            // Handle the edge case of a popup being closed while clicking in its empty space.
            ImGuiWindow? hoveredRoot = hoveredWindow?.RootWindow;
            bool isClosedPopup = hoveredRoot != null
                && (hoveredRoot.Flags & ImGuiWindowFlags.Popup) != 0
                && !IsPopupOpenAnyLevel(hoveredRoot.PopupId);
            bool isQueuedFocusRequest = g.NavMoveSubmitted && (g.NavMoveFlags & ImGuiNavMoveFlags.FocusApi) != 0;

            if (hoveredWindow != null && !isClosedPopup && !isQueuedFocusRequest)
            {
                StartMouseMovingWindow(hoveredWindow);

                // Cancel moving if clicked outside the title bar.
                if ((hoveredWindow.BgClickFlags & ImGuiWindowBgClickFlags.Move) == 0)
                    if ((hoveredRoot!.Flags & ImGuiWindowFlags.NoTitleBar) == 0 || hoveredRoot.DockIsActive)
                        if (!hoveredRoot.TitleBarRect().Contains(g.IO.MouseClickedPos[0]))
                            g.MovingWindow = null;

                // Cancel moving if clicked over a disabled item or an item inhibited by a popup.
                if (g.HoveredIdIsDisabled)
                {
                    g.MovingWindow = null;
                    g.ActiveIdDisabledId = g.HoveredId;
                }
            }
            else if (hoveredWindow == null && g.NavWindow != null)
            {
                // Clicking on void disables focus.
                FocusWindow(null, ImGuiFocusRequestFlags.UnlessBelowModal);
            }
        }

        // With the right mouse button, popups are closed without changing focus based on the mouse
        // position; focus is restored to the window under the bottom-most closed popup.
        if (g.HoveredId == 0 && IsMouseClicked(ImGuiMouseButton.Right, ImGuiInputFlags.None, ImGuiKeyOwner_NoOwner))
        {
            // Trim popups above the hovered window, bounded by the top-most modal, without moving focus
            ImGuiWindow? modal = GetTopMostPopupModal();
            bool hoveredAboveModal = hoveredWindow != null && (modal == null || IsWindowAbove(hoveredWindow, modal));
            ClosePopupsOverWindow(hoveredAboveModal ? hoveredWindow : modal, true);
        }
    }

    #endregion

    #endregion


    #region SECTION: WINDOW PIPELINE

    #region Window pipeline: current window and lookup

    /// <summary>
    /// Makes the supplied window the ambient current window, or clears the ambient current window when
    /// <paramref name="window"/> is null.
    /// </summary>
    /// <param name="window">The window to make current, or null to leave no ambient current window.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetCurrentWindow()</c>. The canonical
    /// table lookup that assigns <c>g.CurrentTable</c> from <c>window->DC.CurrentTableIdx</c> is performed
    /// here, so switching to a child window inside a table restores the correct ambient table.
    /// </para>
    /// <para>
    /// The canonical <c>ImGui::UpdateCurrentFontSize(0.0f)</c> call belongs to the font subsystem and is
    /// invoked as an external dependency; <see cref="NavUpdateCurrentWindowIsScrollPushableX"/> is owned by
    /// part 08.
    /// </para>
    /// <para>
    /// Canonical asserts <c>g.CurrentWindowStack.Size &gt; 0</c> through the dereference of
    /// <c>g.CurrentWindowStack.back()</c>; the port asserts it explicitly.
    /// </para>
    /// </remarks>
    internal static void SetCurrentWindow(ImGuiWindow? window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.CurrentWindow = window;
        if (window != null)
        {
            ImAssert.Check(g.CurrentWindowStack.Count > 0);
            g.StackSizesInBeginForCurrentWindow = g.CurrentWindowStack[^1].StackSizesInBegin;
        }
        else
        {
            g.StackSizesInBeginForCurrentWindow = null;
        }

        // Resolve the ambient table from the pool index the window recorded when the table began. A
        // separate child window keeps its own index, so entering it restores the table it belongs to.
        g.CurrentTable = null;
        if (window != null && window.DC.CurrentTableIdx != -1)
        {
            int tableIdx = window.DC.CurrentTableIdx;
            if (tableIdx >= 0 && tableIdx < g.Tables.Count)
                g.CurrentTable = g.Tables[tableIdx];
        }

        if (window != null)
        {
            if ((g.IO.BackendFlags & ImGuiBackendFlags.RendererHasTextures) != 0)
            {
                ImGuiViewport? viewport = window.Viewport;
                g.FontRasterizerDensity = (viewport != null && viewport.FramebufferScale.X != 0.0f)
                    ? viewport.FramebufferScale.X
                    : g.IO.DisplayFramebufferScale.X;
            }

            bool backupSkipItems = window.SkipItems;
            window.SkipItems = false;
            UpdateCurrentFontSize(0.0f);
            window.SkipItems = backupSkipItems;
            NavUpdateCurrentWindowIsScrollPushableX();
        }
    }

    /// <summary>
    /// Returns the ambient current window and marks it as accessed so the frame's implicit window is not
    /// garbage-collected.
    /// </summary>
    /// <returns>The ambient current window. The canonical contract requires a current window to exist.</returns>
    /// <remarks>
    /// Ports canonical inline <c>GetCurrentWindow()</c>.,
    /// which asserts through dereference when <c>g.CurrentWindow</c> is null. The port asserts explicitly.
    /// </remarks>
    internal static ImGuiWindow GetCurrentWindow()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(g.CurrentWindow != null);
        g.CurrentWindow!.WriteAccessed = true;
        return g.CurrentWindow!;
    }

    /// <summary>
    /// Returns the ambient current window without marking it as accessed.
    /// </summary>
    /// <returns>The ambient current window, or null when no window is current.</returns>
    /// <remarks>
    /// Ports canonical inline <c>GetCurrentWindowRead()</c>.
    /// Callers that require a window follow canonical usage and dereference the result.
    /// </remarks>
    internal static ImGuiWindow? GetCurrentWindowRead()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        return g.CurrentWindow;
    }

    /// <summary>
    /// Finds the window registered under the supplied identifier.
    /// </summary>
    /// <param name="id">The window identifier, produced by hashing the window name.</param>
    /// <returns>The matching window, or null when no window carries the identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>FindWindowByID()</c>. Canonical reads
    /// <c>g.WindowsById</c> through <c>ImGuiStorage::GetVoidPtr</c>; this port stores the map in
    /// <c>Dictionary&lt;uint, ImGuiWindow&gt;</c>.
    /// </remarks>
    public static ImGuiWindow? FindWindowByID(uint id)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        return g.WindowsById.TryGetValue(id, out ImGuiWindow? window) ? window : null;
    }

    /// <summary>
    /// Finds the window whose name hashes to the supplied identifier.
    /// </summary>
    /// <param name="name">The window name, which may use the <c>##</c> and <c>###</c> operators.</param>
    /// <returns>The matching window, or null when no window carries the identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>FindWindowByName()</c>.: the name is
    /// hashed with <c>ImHashStr</c>, which stops at <c>###</c>, so two names that differ only after
    /// <c>###</c> resolve to the same window.
    /// </remarks>
    public static ImGuiWindow? FindWindowByName(string name)
    {
        uint id = ImHashStr(name);
        return FindWindowByID(id);
    }

    /// <summary>
    /// Applies persisted per-window settings to a window and seeds the fields that the first layout pass
    /// depends on.
    /// </summary>
    /// <param name="window">The window that receives the settings.</param>
    /// <param name="settings">
    /// The persisted settings entry, or null when the window has no saved state. Canonical accepts NULL, so
    /// the binding signature is nullable.
    /// </param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ApplyWindowSettings()</c>. Position
    /// values in the settings entry are viewport-relative and are converted to absolute coordinates using
    /// <c>window.ViewportPos</c>.
    /// </para>
    /// <para>
    /// The <c>DockId</c> and <c>DockOrder</c> assignment is retained from canonical source, but the docking
    /// node lifecycle that consumes those fields belongs to Chunk 6. The <c>ImGuiCond.FirstUseEver</c> bit is
    /// cleared so a loaded setting consumes the first-use opportunity.
    /// </para>
    /// </remarks>
    internal static void ApplyWindowSettings(ImGuiWindow window, ImGuiWindowSettings? settings)
    {
        if (settings != null)
        {
            ImGuiViewport mainViewport = GetMainViewport();
            window.ViewportPos = mainViewport.Pos;
            if (settings.ViewportId != 0)
            {
                window.ViewportId = settings.ViewportId;
                window.ViewportPos = new ImVec2(settings.ViewportPos.X, settings.ViewportPos.Y);
            }
            window.Pos = new ImVec2((float)(int)(settings.Pos.X + window.ViewportPos.X), (float)(int)(settings.Pos.Y + window.ViewportPos.Y));
            if (settings.Size.X > 0 && settings.Size.Y > 0)
            {
                window.Size = window.SizeFull = new ImVec2((float)(int)settings.Size.X, (float)(int)settings.Size.Y);
                window.AutoFitFramesX = 0;
                window.AutoFitFramesY = 0;
            }
            window.Collapsed = settings.Collapsed;
            window.DockId = settings.DockId;
            window.DockOrder = settings.DockOrder;
            SetWindowConditionAllowFlags(window, ImGuiCond.FirstUseEver, false);
        }

        // Seed the cursor so the first call to CalcWindowContentSizes() does not observe stale values.
        window.DC.CursorStartPos = window.Pos;
        window.DC.CursorMaxPos = window.Pos;
        window.DC.IdealMaxPos = window.Pos;

        if ((window.Flags & ImGuiWindowFlags.AlwaysAutoResize) != 0)
        {
            window.AutoFitFramesX = 2;
            window.AutoFitFramesY = 2;
            window.AutoFitOnlyGrows = false;
        }
        else
        {
            window.AutoFitFramesX = (sbyte)(window.Size.X <= 0.0f ? 2 : 0);
            window.AutoFitFramesY = (sbyte)(window.Size.Y <= 0.0f ? 2 : 0);
            window.AutoFitOnlyGrows = window.AutoFitFramesX > 0 || window.AutoFitFramesY > 0;
        }
    }

    /// <summary>
    /// Seeds a freshly created window with its default state and then applies any persisted settings.
    /// </summary>
    /// <param name="window">The freshly created window.</param>
    /// <param name="settings">The matching persisted settings entry, or null when none exists.</param>
    /// <remarks>
    /// Ports canonical <c>InitOrLoadWindowSettings()</c>. All
    /// four condition gates receive <c>Always | Once | FirstUseEver | Appearing</c>; a loaded settings entry
    /// then clears <c>FirstUseEver</c> through <see cref="ApplyWindowSettings"/>.
    /// </remarks>
    private static void InitOrLoadWindowSettings(ImGuiWindow window, ImGuiWindowSettings? settings)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiViewport mainViewport = GetMainViewport();
        window.Pos = mainViewport.Pos + new ImVec2(60.0f, 60.0f);
        window.Size = new ImVec2(0.0f, 0.0f);
        window.SizeFull = new ImVec2(0.0f, 0.0f);
        window.ViewportPos = mainViewport.Pos;
        window.SetWindowPosAllowFlags = ImGuiCond.Always | ImGuiCond.Once | ImGuiCond.FirstUseEver | ImGuiCond.Appearing;
        window.SetWindowSizeAllowFlags = window.SetWindowPosAllowFlags;
        window.SetWindowCollapsedAllowFlags = window.SetWindowPosAllowFlags;
        window.SetWindowDockAllowFlags = window.SetWindowPosAllowFlags;
        ApplyWindowSettings(window, settings);
        if (settings != null)
            settings.LastUsedDate = g.SessionDate;
    }

    /// <summary>
    /// Creates a window, registers it in the identifier map, applies saved settings, and inserts it into the
    /// display order.
    /// </summary>
    /// <param name="name">The window name.</param>
    /// <param name="flags">The window flags supplied by the first <see cref="Begin(string, ImGuiWindowFlags)"/> call.</param>
    /// <returns>The newly created window.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>CreateNewWindow()</c>.
    /// </para>
    /// <para>
    /// The port's <c>ImGuiWindow</c> constructor deliberately omits name hashing, identifier-stack seeding,
    /// and move/tab identifier derivation (see <c>ImGuiInternal.cs</c>); those steps are performed here,
    /// matching canonical <c>ImGuiWindow::ImGuiWindow</c>. The draw list is
    /// bound to the current font atlas as the port's realization of canonical
    /// <c>_SetDrawListSharedData()</c>.
    /// </para>
    /// <para>
    /// The settings lookup is the canonical in-memory search
    /// (<c>ImGui::FindWindowSettingsByWindow</c>, ) evaluated directly against
    /// <c>g.SettingsWindows</c>; the file-IO handler that fills and writes that list belongs to Chunk 13.
    /// In this port <c>window.SettingsOffset</c> is an index into <c>g.SettingsWindows</c> rather than a byte
    /// offset into a chunk stream.
    /// </para>
    /// </remarks>
    public static ImGuiWindow CreateNewWindow(string name, ImGuiWindowFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow window = new(g, name);
        window.ID = ImHashStr(name);
        window.IDStack.Add(window.ID);
        window.NameBufLen = name.Length + 1;
        window.MoveId = ImHashStr("#MOVE", 0, window.ID);
        window.TabId = ImHashStr("#TAB", 0, window.ID);
        window.Flags = flags;
        g.WindowsById[window.ID] = window;

        if (g.Font?.OwnerAtlas is ImFontAtlas fontAtlas)
            window.DrawList.UpdateAtlas(fontAtlas);

        ImGuiWindowSettings? settings = null;
        if ((flags & ImGuiWindowFlags.NoSavedSettings) == 0)
        {
            if (window.SettingsOffset != -1 && window.SettingsOffset < g.SettingsWindows.Count)
            {
                settings = g.SettingsWindows[window.SettingsOffset];
            }
            else
            {
                for (int n = 0; n < g.SettingsWindows.Count; n++)
                {
                    if (g.SettingsWindows[n].ID == window.ID && !g.SettingsWindows[n].WantDelete)
                    {
                        settings = g.SettingsWindows[n];
                        break;
                    }
                }
            }
            if (settings != null)
                window.SettingsOffset = g.SettingsWindows.IndexOf(settings);
        }

        InitOrLoadWindowSettings(window, settings);

        if ((flags & ImGuiWindowFlags.NoBringToFrontOnFocus) != 0)
            g.Windows.Insert(0, window);
        else
            g.Windows.Add(window);

        return window;
    }

    /// <summary>
    /// Locates persistent settings for a window by its identifier.
    /// </summary>
    /// <param name="id">Window identifier.</param>
    /// <returns>The window settings if found; otherwise, <see langword="null"/>.</returns>
    public static ImGuiWindowSettings? FindWindowSettingsByID(uint id)
    {
        ImGuiContext g = GImGui!;
        for (int n = 0; n < g.SettingsWindows.Count; n++)
        {
            ImGuiWindowSettings settings = g.SettingsWindows[n];
            if (settings.ID == id && !settings.WantDelete)
                return settings;
        }
        return null;
    }

    /// <summary>
    /// Locates persistent settings for a window.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <returns>The window settings if found; otherwise, <see langword="null"/>.</returns>
    public static ImGuiWindowSettings? FindWindowSettingsByWindow(ImGuiWindow window)
    {
        ImGuiContext g = GImGui!;
        if (window.SettingsOffset != -1 && window.SettingsOffset < g.SettingsWindows.Count)
            return g.SettingsWindows[window.SettingsOffset];
        return FindWindowSettingsByID(window.ID);
    }

    /// <summary>
    /// Creates and registers a new window settings entry.
    /// </summary>
    /// <param name="name">Window name.</param>
    /// <returns>The newly created window settings instance.</returns>
    public static ImGuiWindowSettings CreateNewWindowSettings(string name)
    {
        ImGuiContext g = GImGui!;
        // Canonical stores the full string only when io.ConfigDebugIniSettings is enabled so that the `.ini`
        // file is easier to inspect; otherwise the non-contributing `###` prefix is dropped before hashing,
        // which keeps the generated entries stable when only the displayed part of a label changes.
        if (!g.IO.ConfigDebugIniSettings)
            name = HashSkipUncontributingPrefix(name);
        ImGuiWindowSettings settings = new()
        {
            ID = ImHashStr(name),
            Name = name
        };
        g.SettingsWindows.Add(settings);
        return settings;
    }

    /// <summary>
    /// Marks the settings state dirty so changes are written out.
    /// </summary>
    public static void MarkIniSettingsDirty()
    {
        ImGuiContext g = GImGui!;
        if (g.SettingsDirtyTimer <= 0.0f)
            g.SettingsDirtyTimer = g.IO.IniSavingRate;
    }

    /// <summary>
    /// Marks the settings state dirty if the window saves settings.
    /// </summary>
    /// <param name="window">Window whose settings became dirty.</param>
    public static void MarkIniSettingsDirty(ImGuiWindow? window)
    {
        ImGuiContext g = GImGui!;
        if (window == null || (window.Flags & ImGuiWindowFlags.NoSavedSettings) == 0)
        {
            if (g.SettingsDirtyTimer <= 0.0f)
                g.SettingsDirtyTimer = g.IO.IniSavingRate;
        }
    }

    /// <summary>
    /// Enables or disables one condition bit on all four per-window condition gates at once.
    /// </summary>
    /// <param name="window">The window whose condition gates are updated.</param>
    /// <param name="flags">The condition bits to enable or disable.</param>
    /// <param name="enabled">True to set the bits, false to clear them.</param>
    /// <remarks>
    /// Ports canonical file-scope <c>SetWindowConditionAllowFlags()</c>
    ///. Keeping the four gates in lockstep is what makes the
    /// <c>SetWindowPos</c>/<c>SetWindowSize</c>/<c>SetWindowCollapsed</c>/dock condition tests share one
    /// cleared-bits policy.
    /// </remarks>
    private static void SetWindowConditionAllowFlags(ImGuiWindow window, ImGuiCond flags, bool enabled)
    {
        window.SetWindowPosAllowFlags = enabled ? window.SetWindowPosAllowFlags | flags : window.SetWindowPosAllowFlags & ~flags;
        window.SetWindowSizeAllowFlags = enabled ? window.SetWindowSizeAllowFlags | flags : window.SetWindowSizeAllowFlags & ~flags;
        window.SetWindowCollapsedAllowFlags = enabled ? window.SetWindowCollapsedAllowFlags | flags : window.SetWindowCollapsedAllowFlags & ~flags;
        window.SetWindowDockAllowFlags = enabled ? window.SetWindowDockAllowFlags | flags : window.SetWindowDockAllowFlags & ~flags;
    }

    #endregion

    #region [SECTION] SETTINGS

    /// <summary>
    /// Registers a `.ini` section handler.
    /// (, declared )
    /// </summary>
    /// <param name="handler">The handler to register. Its <see cref="ImGuiSettingsHandler.TypeHash"/> must
    /// already be the hash of its <see cref="ImGuiSettingsHandler.TypeName"/>.</param>
    /// <remarks>
    /// A handler is matched by type hash, so registering two handlers with the same type name is a
    /// programming error; the canonical assertion is preserved as a debug assertion.
    /// </remarks>
    internal static void AddSettingsHandler(ImGuiSettingsHandler handler)
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(FindSettingsHandler(handler.TypeName) == null, "A settings handler is already registered for this type name.");
        g.SettingsHandlers.Add(handler);
    }

    /// <summary>
    /// Removes the `.ini` section handler registered for a type name, when one exists.
    /// (, declared )
    /// </summary>
    /// <param name="typeName">The handler type name to remove.</param>
    internal static void RemoveSettingsHandler(string typeName)
    {
        ImGuiContext g = GetContext();
        ImGuiSettingsHandler? handler = FindSettingsHandler(typeName);
        if (handler != null)
            g.SettingsHandlers.Remove(handler);
    }

    /// <summary>
    /// Finds the `.ini` section handler registered for a type name.
    /// (, declared )
    /// </summary>
    /// <param name="typeName">The handler type name to look up.</param>
    /// <returns>The registered handler, or <see langword="null"/> when none is registered.</returns>
    /// <remarks>
    /// The lookup hashes the type name and compares hashes, so a collision between two distinct type names
    /// would resolve to the first registered handler. This mirrors canonical and is not a practical concern
    /// for the short fixed section names used by the engine.
    /// </remarks>
    internal static ImGuiSettingsHandler? FindSettingsHandler(string typeName)
    {
        ImGuiContext g = GetContext();
        uint typeHash = ImHashStr(typeName);
        for (int n = 0; n < g.SettingsHandlers.Count; n++)
        {
            if (g.SettingsHandlers[n].TypeHash == typeHash)
                return g.SettingsHandlers[n];
        }
        return null;
    }

    /// <summary>
    /// Clears all persisted settings, then clears the in-memory `.ini` text.
    /// (, declared )
    /// </summary>
    /// <remarks>
    /// Every registered handler that supports clearing is invoked in registration order. Handlers without a
    /// clear callback keep their data, matching canonical.
    /// </remarks>
    internal static void ClearIniSettings()
    {
        ImGuiContext g = GetContext();
        g.SettingsIniData.Clear();
        for (int n = 0; n < g.SettingsHandlers.Count; n++)
        {
            ImGuiSettingsHandler handler = g.SettingsHandlers[n];
            handler.ClearAllFn?.Invoke(g, handler);
        }
    }

    /// <summary>
    /// Prunes and date-stamps settings entries according to a cleanup request.
    /// (, declared )
    /// </summary>
    /// <param name="args">The cleanup options; <see cref="ImGuiSettingsCleanupArgs._DiscardOlderThanDate"/>
    /// is overwritten with the unpacked cutoff date derived from the session date.</param>
    /// <remarks>
    /// The cutoff is computed once from the platform session date and the requested month count, then handed
    /// to every handler, so all handlers agree on what "older than N months" means even when the pass
    /// straddles a month boundary.
    /// </remarks>
    internal static void CleanupIniSettings(ImGuiSettingsCleanupArgs args)
    {
        ImGuiContext g = GetContext();
        if (args.DiscardAll)
        {
            for (int n = 0; n < g.SettingsHandlers.Count; n++)
            {
                ImGuiSettingsHandler handler = g.SettingsHandlers[n];
                if ((args.TypeHashFilter == 0 || handler.TypeHash == args.TypeHashFilter) && handler.ClearAllFn != null)
                    handler.ClearAllFn(g, handler);
            }
        }

        if (g.PlatformIO.Platform_SessionDate != 0 && args.DiscardOlderThanMonths != 0)
        {
            ImGuiPackedDate cutoff = new((uint)g.PlatformIO.Platform_SessionDate);
            cutoff.SubtractMonths(args.DiscardOlderThanMonths);
            args._DiscardOlderThanDate = cutoff.Unpack();
        }

        for (int n = 0; n < g.SettingsHandlers.Count; n++)
        {
            ImGuiSettingsHandler handler = g.SettingsHandlers[n];
            if ((args.TypeHashFilter == 0 || handler.TypeHash == args.TypeHashFilter) && handler.CleanupFn != null)
                handler.CleanupFn(g, handler, args);
        }
    }

    /// <summary>
    /// Loads `.ini` settings from a file, silently doing nothing when the file cannot be read.
    /// </summary>
    /// <param name="iniFilename">The path of the `.ini` file to load.</param>
    /// <remarks>
    /// Canonical loads the whole file into memory and treats a missing or unreadable file as "no settings".
    /// The port does the same; any I/O failure is swallowed so a read-only or absent settings file never
    /// interrupts the frame.
    /// </remarks>
    public static void LoadIniSettingsFromDisk(string iniFilename)
    {
        string fileData;
        try
        {
            if (!File.Exists(iniFilename))
                return;
            fileData = File.ReadAllText(iniFilename, Encoding.UTF8);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (fileData.Length > 0)
            LoadIniSettingsFromMemory(fileData);
    }

    /// <summary>
    /// Loads `.ini` settings from an in-memory text blob.
    /// </summary>
    /// <param name="iniData">The `.ini` text. A null or empty value is treated as an empty file.</param>
    /// <remarks>
    /// <para>
    /// The parse is deliberately forgiving: it never reports an error, it ignores `;` comment lines, and a
    /// malformed section header leaves the previously selected handler in place, exactly as canonical does.
    /// A header is a line beginning with `[` and ending with `]`, whose first `]` ends the type name and
    /// whose following `[` starts the entry name; the entry name may itself contain brackets.
    /// </para>
    /// <para>
    /// The canonical implementation parses a mutable copy of the text so it can temporarily terminate lines,
    /// then restores the copy for browsing in the metrics window. This port parses the managed string
    /// directly, which removes the temporary mutation and leaves the stored copy identical.
    /// </para>
    /// </remarks>
    public static void LoadIniSettingsFromMemory(string iniData)
    {
        ImGuiContext g = GetContext();
        ImAssert.Check(g.Initialized, "The context must be initialized before settings can be loaded.");

        iniData ??= string.Empty;
        // Canonical copies the raw bytes into SettingsIniData so the metrics window can browse what was
        // read; this port stores the same text through the byte-oriented text buffer.
        g.SettingsIniData.Clear();
        g.SettingsIniData.Append(iniData);

        // Call pre-read handlers. Some types clear their data (docking) and some merge (windows).
        for (int n = 0; n < g.SettingsHandlers.Count; n++)
        {
            ImGuiSettingsHandler handler = g.SettingsHandlers[n];
            handler.ReadInitFn?.Invoke(g, handler);
        }

        object? entryData = null;
        ImGuiSettingsHandler? entryHandler = null;
        foreach (string line in SplitIniLines(iniData))
        {
            if (line.Length > 0 && line[0] == ';')
                continue;
            if (line.Length > 1 && line[0] == '[' && line[^1] == ']')
            {
                // Parse "[Type][Name]". The entry name may contain '[' and ']', so only the first ']' and the
                // first '[' after it are structural.
                string content = line[1..^1];
                int typeEnd = content.IndexOf(']');
                int nameStart = typeEnd >= 0 ? content.IndexOf('[', typeEnd + 1) : -1;
                if (typeEnd < 0 || nameStart < 0)
                    continue;
                string typeName = content[..typeEnd];
                string entryName = content[(nameStart + 1)..];
                entryHandler = FindSettingsHandler(typeName);
                entryData = entryHandler?.ReadOpenFn?.Invoke(g, entryHandler, entryName);
            }
            else if (entryHandler?.ReadLineFn != null && entryData != null)
            {
                entryHandler.ReadLineFn(g, entryHandler, entryData, line);
            }
        }
        g.SettingsLoaded = true;

        // Age-based discard, then the apply pass.
        if (g.IO.ConfigIniSettingsAutoDiscardMonths > 0)
        {
            ImGuiSettingsCleanupArgs cleanupArgs = new()
            {
                DiscardOlderThanMonths = g.IO.ConfigIniSettingsAutoDiscardMonths,
            };
            CleanupIniSettings(cleanupArgs);
        }
        for (int n = 0; n < g.SettingsHandlers.Count; n++)
        {
            ImGuiSettingsHandler handler = g.SettingsHandlers[n];
            handler.ApplyAllFn?.Invoke(g, handler);
        }
    }

    /// <summary>
    /// Saves the current settings to a file, doing nothing when the filename is null.
    /// </summary>
    /// <param name="iniFilename">The destination path, or <see langword="null"/> to skip writing.</param>
    /// <remarks>
    /// The dirty timer is cleared before the write so a failed write does not re-trigger a retry storm;
    /// canonical behaves the same way and treats a write failure as fatal to that attempt only.
    /// </remarks>
    public static void SaveIniSettingsToDisk(string iniFilename)
    {
        ImGuiContext g = GetContext();
        g.SettingsDirtyTimer = 0.0f;
        if (iniFilename == null)
            return;

        string iniData = SaveIniSettingsToMemory();
        try
        {
            File.WriteAllText(iniFilename, iniData, Encoding.UTF8);
        }
        catch (IOException)
        {
            // A settings write failure must never interrupt rendering; the data remains dirty and will be
            // retried the next time the timer elapses.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Serializes every registered handler's entries into the shared `.ini` text buffer.
    /// </summary>
    /// <returns>The serialized `.ini` text.</returns>
    /// <remarks>
    /// Handlers are written in registration order, which is what produces the canonical Window, Table,
    /// Docking section order. Canonical exposes the byte size through an out parameter; a managed string
    /// already carries its length, so this port returns the text directly.
    /// </remarks>
    public static string SaveIniSettingsToMemory()
    {
        ImGuiContext g = GetContext();
        g.SettingsDirtyTimer = 0.0f;
        g.SettingsIniData.Clear();
        for (int n = 0; n < g.SettingsHandlers.Count; n++)
        {
            ImGuiSettingsHandler handler = g.SettingsHandlers[n];
            handler.WriteAllFn?.Invoke(g, handler, g.SettingsIniData);
        }
        return g.SettingsIniData.CStr();
    }

    /// <summary>
    /// Drives the first-frame load and the delayed save.
    /// </summary>
    /// <remarks>
    /// The load happens once, before any window exists, which is why the window handler's apply pass is the
    /// thing that binds loaded entries to windows created later in the same frame. The save is deferred by
    /// <see cref="ImGuiIO.IniSavingRate"/> seconds after the last change so a burst of layout changes
    /// produces one write.
    /// </remarks>
    internal static void UpdateSettings()
    {
        ImGuiContext g = GetContext();
        if (!g.SettingsLoaded)
        {
            ImAssert.Check(g.SettingsWindows.Count == 0, "Settings must be loaded before any window settings exist.");
            if (g.IO.IniFilename != null)
                LoadIniSettingsFromDisk(g.IO.IniFilename);
            g.SettingsLoaded = true;
        }

        if (g.SettingsDirtyTimer > 0.0f)
        {
            g.SettingsDirtyTimer -= g.IO.DeltaTime;
            if (g.SettingsDirtyTimer <= 0.0f)
            {
                if (g.IO.IniFilename != null)
                    SaveIniSettingsToDisk(g.IO.IniFilename);
                else
                    g.IO.WantSaveIniSettings = true;
                g.SettingsDirtyTimer = 0.0f;
            }
        }
    }

    /// <summary>
    /// Splits raw `.ini` text into lines with their terminators removed.
    /// </summary>
    /// <param name="iniData">The text to split.</param>
    /// <returns>The lines, excluding empty runs that would follow a trailing terminator.</returns>
    /// <remarks>
    /// Canonical walks a mutable buffer while skipping `\n` and `\r` runs, which is equivalent to splitting
    /// on either terminator. Blank lines inside an entry are preserved, as canonical passes them to the line
    /// callback. A single trailing terminator does not produce a final empty line, which matches canonical's
    /// loop bound; handlers ignore empty lines in any case.
    /// </remarks>
    private static IEnumerable<string> SplitIniLines(string iniData)
    {
        int index = 0;
        while (index < iniData.Length)
        {
            while (index < iniData.Length && (iniData[index] == '\n' || iniData[index] == '\r'))
                index++;
            if (index >= iniData.Length)
                break;
            int start = index;
            while (index < iniData.Length && iniData[index] != '\n' && iniData[index] != '\r')
                index++;
            yield return iniData[start..index];
        }
    }

    /// <summary>
    /// Reverts a window to its initial state and invalidates its saved entry.
    /// (, declared )
    /// </summary>
    /// <param name="name">The window name whose settings are cleared.</param>
    /// <remarks>
    /// Clearing also re-enables the <c>FirstUseEver</c>/<c>Once</c> conditions, because the window is
    /// re-seeded as though it had just been created. A docked window is undocked first so the cleared
    /// position is not immediately overwritten by the dock node.
    /// </remarks>
    internal static void ClearWindowSettings(string name)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow? window = FindWindowByName(name);
        if (window != null)
        {
            window.Flags |= ImGuiWindowFlags.NoSavedSettings;
            InitOrLoadWindowSettings(window, null);
            if (window.DockId != 0)
                DockContextProcessUndockWindow(g, window, true);
        }

        ImGuiWindowSettings? settings = window != null
            ? FindWindowSettingsByWindow(window)
            : FindWindowSettingsByID(ImHashStr(name));
        if (settings != null)
            settings.WantDelete = true;
    }

    /// <summary>
    /// Clears every window's settings binding and empties the settings list.
    /// </summary>
    /// <param name="ctx">The context being cleaned.</param>
    /// <param name="handler">The window settings handler, unused.</param>
    internal static void WindowSettingsHandler_ClearAll(ImGuiContext ctx, ImGuiSettingsHandler handler)
    {
        _ = handler;
        for (int n = 0; n < ctx.Windows.Count; n++)
            ctx.Windows[n].SettingsOffset = -1;
        ctx.SettingsWindows.Clear();
    }

    /// <summary>
    /// Applies date-based discard and stamping to the window settings entries.
    /// </summary>
    /// <param name="ctx">The context being cleaned.</param>
    /// <param name="handler">The window settings handler, unused.</param>
    /// <param name="args">The cleanup options.</param>
    internal static void WindowSettingsHandler_Cleanup(ImGuiContext ctx, ImGuiSettingsHandler handler, ImGuiSettingsCleanupArgs args)
    {
        _ = handler;
        for (int n = 0; n < ctx.SettingsWindows.Count; n++)
        {
            ImGuiWindowSettings settings = ctx.SettingsWindows[n];
            bool isValid = settings.LastUsedDate.IsValid();
            if ((args._DiscardOlderThanDate != 0 && settings.LastUsedDate.Unpack() < args._DiscardOlderThanDate)
                || (args.DiscardWhenMissingDate && !isValid))
            {
                settings.WantDelete = true;
            }
            if (args.SetCurrentSessionDateToAll || (args.SetCurrentSessionDateWhenMissingDate && !isValid))
                settings.LastUsedDate = ctx.SessionDate;
        }
    }

    /// <summary>
    /// Opens one `[Window][name]` entry for reading, recycling an existing entry when one exists.
    /// </summary>
    /// <param name="ctx">The context being read into.</param>
    /// <param name="handler">The window settings handler, unused.</param>
    /// <param name="name">The window name in the section header.</param>
    /// <returns>The settings entry that following lines are read into.</returns>
    /// <remarks>
    /// Canonical resets the whole record and clears its inline name before re-reading. Because this port
    /// stores the name in a separate property rather than inline storage, the reset preserves the incoming
    /// name, which keeps a re-read of an existing entry writable; the observable difference is limited to a
    /// malformed file that repeats the same window section.
    /// </remarks>
    internal static object? WindowSettingsHandler_ReadOpen(ImGuiContext ctx, ImGuiSettingsHandler handler, string name)
    {
        _ = ctx;
        _ = handler;
        uint id = ImHashStr(name);
        ImGuiWindowSettings? settings = FindWindowSettingsByID(id);
        if (settings != null)
            ResetWindowSettings(settings, name);
        else
            settings = CreateNewWindowSettings(name);
        settings.ID = id;
        settings.WantApply = true;
        return settings;
    }

    /// <summary>
    /// Reads one `key=value` line of a window settings entry.
    /// </summary>
    /// <param name="ctx">The context being read into.</param>
    /// <param name="handler">The window settings handler, unused.</param>
    /// <param name="entry">The settings entry returned by
    /// <see cref="WindowSettingsHandler_ReadOpen"/>.</param>
    /// <param name="line">The line to parse; unknown keys are ignored.</param>
    internal static void WindowSettingsHandler_ReadLine(ImGuiContext ctx, ImGuiSettingsHandler handler, object entry, string line)
    {
        _ = ctx;
        _ = handler;
        ImGuiWindowSettings settings = (ImGuiWindowSettings)entry;
        if (TryParseIniIntPair(line, "Pos=", out int x, out int y))
            settings.Pos = new ImVec2ih((short)x, (short)y);
        else if (TryParseIniIntPair(line, "Size=", out x, out y))
            settings.Size = new ImVec2ih((short)x, (short)y);
        else if (TryParseIniUInt(line, "ViewportId=", out uint u1))
            settings.ViewportId = u1;
        else if (TryParseIniIntPair(line, "ViewportPos=", out x, out y))
            settings.ViewportPos = new ImVec2ih((short)x, (short)y);
        else if (TryParseIniInt(line, "Collapsed=", out int i))
            settings.Collapsed = i != 0;
        else if (TryParseIniInt(line, "IsChild=", out i))
            settings.IsChild = i != 0;
        else if (TryParseIniInt(line, "LastUsed=", out i))
            settings.LastUsedDate = new ImGuiPackedDate((uint)i);
        else if (TryParseIniUIntAndInt(line, "DockId=", out u1, out i))
        {
            settings.DockId = u1;
            settings.DockOrder = (short)i;
        }
        else if (TryParseIniUInt(line, "DockId=", out u1))
        {
            settings.DockId = u1;
            settings.DockOrder = -1;
        }
        else if (TryParseIniUInt(line, "ClassId=", out u1))
            settings.ClassId = u1;
    }

    /// <summary>
    /// Applies every loaded window entry that is still waiting to be merged.
    /// </summary>
    /// <param name="ctx">The context being applied.</param>
    /// <param name="handler">The window settings handler, unused.</param>
    internal static void WindowSettingsHandler_ApplyAll(ImGuiContext ctx, ImGuiSettingsHandler handler)
    {
        _ = handler;
        for (int n = 0; n < ctx.SettingsWindows.Count; n++)
        {
            ImGuiWindowSettings settings = ctx.SettingsWindows[n];
            if (settings.WantApply)
            {
                ImGuiWindow? window = FindWindowByID(settings.ID);
                if (window != null)
                    ApplyWindowSettings(window, settings);
                settings.WantApply = false;
            }
        }
    }

    /// <summary>
    /// Writes every window settings entry, refreshing entries for windows active this session first.
    /// </summary>
    /// <param name="ctx">The context being written.</param>
    /// <param name="handler">The window settings handler supplying the section type name.</param>
    /// <param name="buf">The output buffer receiving the `[Window][name]` sections.</param>
    /// <remarks>
    /// Settings are stored relative to the viewport, so a window moved between viewports keeps its
    /// viewport-relative position. Entries marked <see cref="ImGuiWindowSettings.WantDelete"/> are skipped
    /// rather than removed, because canonical compacts the list on its next GC pass.
    /// </remarks>
    internal static void WindowSettingsHandler_WriteAll(ImGuiContext ctx, ImGuiSettingsHandler handler, ImGuiTextBuffer buf)
    {
        ImGuiContext g = ctx;
        for (int n = 0; n < g.Windows.Count; n++)
        {
            ImGuiWindow window = g.Windows[n];
            if ((window.Flags & ImGuiWindowFlags.NoSavedSettings) != 0)
                continue;

            ImGuiWindowSettings? settings = FindWindowSettingsByWindow(window);
            if (settings == null)
            {
                settings = CreateNewWindowSettings(window.Name);
                settings.LastUsedDate = g.SessionDate;
                window.SettingsOffset = g.SettingsWindows.IndexOf(settings);
            }
            ImAssert.Check(settings.ID == window.ID, "A window settings entry must match its window identifier.");
            settings.Pos = new ImVec2ih((short)(window.Pos.X - window.ViewportPos.X), (short)(window.Pos.Y - window.ViewportPos.Y));
            settings.Size = new ImVec2ih((short)window.SizeFull.X, (short)window.SizeFull.Y);
            settings.ViewportId = window.ViewportId;
            settings.ViewportPos = new ImVec2ih((short)window.ViewportPos.X, (short)window.ViewportPos.Y);
            settings.DockId = window.DockId;
            settings.ClassId = window.WindowClass.ClassId;
            settings.DockOrder = window.DockOrder;
            settings.Collapsed = window.Collapsed;
            // Docked windows carry the child-window flag, so the root comparison is the reliable test.
            settings.IsChild = !ReferenceEquals(window.RootWindow, window);
            settings.WantDelete = false;
            settings.LastUsedDate = g.SessionDate;
        }

        for (int n = 0; n < g.SettingsWindows.Count; n++)
        {
            ImGuiWindowSettings settings = g.SettingsWindows[n];
            if (settings.WantDelete)
                continue;

            buf.AppendF("[{0}][{1}]\n", handler.TypeName, settings.Name);
            if (settings.IsChild)
            {
                buf.AppendF("IsChild=1\n");
                buf.AppendF("Size={0},{1}\n", settings.Size.X, settings.Size.Y);
            }
            else
            {
                if (settings.ViewportId != 0 && settings.ViewportId != ImGuiInternalConstants.ViewportDefaultId)
                {
                    buf.AppendF("ViewportPos={0},{1}\n", settings.ViewportPos.X, settings.ViewportPos.Y);
                    buf.AppendF("ViewportId=0x{0:X8}\n", settings.ViewportId);
                }
                if (settings.Pos.X != 0 || settings.Pos.Y != 0 || settings.ViewportId == ImGuiInternalConstants.ViewportDefaultId)
                    buf.AppendF("Pos={0},{1}\n", settings.Pos.X, settings.Pos.Y);
                if (settings.Size.X != 0 || settings.Size.Y != 0)
                    buf.AppendF("Size={0},{1}\n", settings.Size.X, settings.Size.Y);
                buf.AppendF("Collapsed={0}\n", settings.Collapsed ? 1 : 0);
                if (settings.DockId != 0)
                {
                    if (settings.DockOrder == -1)
                        buf.AppendF("DockId=0x{0:X8}\n", settings.DockId);
                    else
                        buf.AppendF("DockId=0x{0:X8},{1}\n", settings.DockId, settings.DockOrder);
                    if (settings.ClassId != 0)
                        buf.AppendF("ClassId=0x{0:X8}\n", settings.ClassId);
                }
            }
            if (g.IO.ConfigIniSettingsSaveLastUsedDate)
            {
                int lastUsedDate = settings.LastUsedDate.Unpack();
                if (lastUsedDate != 0)
                    buf.AppendF("LastUsed={0:D8}\n", lastUsedDate);
            }
            buf.Append("\n");
        }
    }

    /// <summary>
    /// Resets a window settings record in place, keeping only the supplied name.
    /// </summary>
    /// <param name="settings">The record to reset.</param>
    /// <param name="name">The window name to preserve.</param>
    /// <remarks>
    /// Canonical assigns a freshly constructed record over the existing one, which also zeroes the inline
    /// name; this port resets every field explicitly because the name is stored separately.
    /// </remarks>
    private static void ResetWindowSettings(ImGuiWindowSettings settings, string name)
    {
        settings.ID = 0;
        settings.Pos = default;
        settings.Size = default;
        settings.ViewportPos = default;
        settings.ViewportId = 0;
        settings.DockId = 0;
        settings.ClassId = 0;
        settings.DockOrder = -1;
        settings.LastUsedDate = default;
        settings.Collapsed = false;
        settings.IsChild = false;
        settings.WantApply = false;
        settings.WantDelete = false;
        settings.Name = name;
    }

    /// <summary>
    /// Parses a decimal <c>Key=a,b</c> line into two integers.
    /// </summary>
    /// <param name="line">The line to parse.</param>
    /// <param name="key">The literal key prefix including the equals sign.</param>
    /// <param name="first">The first parsed value.</param>
    /// <param name="second">The second parsed value.</param>
    /// <returns><see langword="true"/> when the key matched and both values parsed.</returns>
    private static bool TryParseIniIntPair(string line, string key, out int first, out int second)
    {
        first = 0;
        second = 0;
        if (!line.StartsWith(key, StringComparison.Ordinal))
            return false;
        int comma = line.IndexOf(',', key.Length);
        if (comma < 0)
            return false;
        return TryParseIniIntValue(line.AsSpan(key.Length, comma - key.Length), out first)
            && TryParseIniIntValue(line.AsSpan(comma + 1), out second);
    }

    /// <summary>
    /// Parses a decimal <c>Key=value</c> line into one integer.
    /// </summary>
    /// <param name="line">The line to parse.</param>
    /// <param name="key">The literal key prefix including the equals sign.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the key matched and the value parsed.</returns>
    private static bool TryParseIniInt(string line, string key, out int value)
    {
        value = 0;
        if (!line.StartsWith(key, StringComparison.Ordinal))
            return false;
        return TryParseIniIntValue(line.AsSpan(key.Length), out value);
    }

    /// <summary>
    /// Parses a hexadecimal <c>Key=0x.</c> line into one unsigned value.
    /// </summary>
    /// <param name="line">The line to parse.</param>
    /// <param name="key">The literal key prefix including the equals sign.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the key matched and the value parsed.</returns>
    private static bool TryParseIniUInt(string line, string key, out uint value)
    {
        value = 0;
        if (!line.StartsWith(key, StringComparison.Ordinal))
            return false;
        return TryParseIniUIntValue(line.AsSpan(key.Length), out value);
    }

    /// <summary>
    /// Parses a hexadecimal <c>Key=0x.,int</c> line into an unsigned value and an integer.
    /// </summary>
    /// <param name="line">The line to parse.</param>
    /// <param name="key">The literal key prefix including the equals sign.</param>
    /// <param name="value">The parsed unsigned value.</param>
    /// <param name="extra">The parsed trailing integer.</param>
    /// <returns><see langword="true"/> when the key matched and both values parsed.</returns>
    private static bool TryParseIniUIntAndInt(string line, string key, out uint value, out int extra)
    {
        value = 0;
        extra = 0;
        if (!line.StartsWith(key, StringComparison.Ordinal))
            return false;
        int comma = line.IndexOf(',', key.Length);
        if (comma < 0)
            return false;
        return TryParseIniUIntValue(line.AsSpan(key.Length, comma - key.Length), out value)
            && TryParseIniIntValue(line.AsSpan(comma + 1), out extra);
    }

    /// <summary>
    /// Parses a span as a decimal integer using the invariant culture.
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the span was a complete decimal integer.</returns>
    private static bool TryParseIniIntValue(ReadOnlySpan<char> span, out int value)
    {
        return int.TryParse(span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Parses a span as a hexadecimal or decimal unsigned integer using the invariant culture.
    /// </summary>
    /// <param name="span">The text to parse, optionally <c>0x</c>-prefixed.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the span was a complete integer.</returns>
    /// <remarks>
    /// Canonical reads these fields with <c>%X</c>, so the <c>0x</c> prefix is expected but the parse is made
    /// tolerant of a missing prefix to keep hand-edited files readable.
    /// </remarks>
    private static bool TryParseIniUIntValue(ReadOnlySpan<char> span, out uint value)
    {
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            span = span[2..];
        return uint.TryParse(span, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    #endregion

    #region Window pipeline: viewport selection for a single main viewport

    /// <summary>
    /// Selects the viewport that hosts a window. In this port every window is hosted by the single main
    /// viewport.
    /// </summary>
    /// <param name="window">The window whose hosting viewport is selected.</param>
    /// <remarks>
    /// <para>
    /// Replaces canonical <c>WindowSelectViewport()</c>. for
    /// the single-viewport case. Canonical takes the early branch
    /// <c>if (!(g.ConfigFlagsCurrFrame &amp; ImGuiConfigFlags_ViewportsEnable)) { SetWindowViewport(window,
    /// main_viewport); return; }</c> whenever the backend does not enable multi-viewport. Automatic window
    /// detachment into secondary platform viewports is not implemented, so every window is hosted by the
    /// main viewport even when a backend advertises the viewport flags.
    /// </para>
    /// <para>
    /// Consequently <c>g.NextWindowData.ViewportId</c> (set by <see cref="SetNextWindowViewport"/>) is not
    /// consumed here, exactly as canonical leaves it unconsumed in the single-viewport branch. The dock node
    /// hosting case and the owned-viewport cases are inert because <c>window.DockNode</c> does not exist and
    /// <c>window.ViewportOwned</c> stays false.
    /// </para>
    /// </remarks>
    private static void SelectWindowViewport(ImGuiWindow window)
    {
        window.ViewportAllowPlatformMonitorExtend = -1;
        ImGuiViewportP mainViewport = (ImGuiViewportP)GetMainViewport();
        SetWindowViewport(window, mainViewport);
    }

    /// <summary>
    /// Assigns a hosting viewport to a window.
    /// </summary>
    /// <param name="window">The window receiving the viewport.</param>
    /// <param name="viewport">The hosting viewport, which is always the single main viewport in this port.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowViewport()</c>. A
    /// viewport-owned window abandons its viewport by zeroing its size first, which canonical uses to
    /// detach a window before it is reassigned.
    /// </remarks>
    public static void SetWindowViewport(ImGuiWindow window, ImGuiViewportP viewport)
    {
        // Abandon the viewport: a window that owned its viewport collapses it before being reassigned.
        if (window.ViewportOwned && window.Viewport != null && ReferenceEquals(window.Viewport.Window, window))
            window.Viewport.Size = new ImVec2(0.0f, 0.0f);

        window.Viewport = viewport;
        window.ViewportId = viewport.ID;
        window.ViewportOwned = ReferenceEquals(viewport.Window, window);
    }

    #endregion

    #region Window pipeline: size calculation

    /// <summary>
    /// Returns the window whose title bar and menu bar heights participate in the minimum-size computation.
    /// </summary>
    /// <param name="window">The window to query.</param>
    /// <returns>The supplied window; this port has no dock-node host to redirect to.</returns>
    /// <remarks>
    /// Ports canonical <c>GetWindowForTitleAndMenuHeight()</c>
    ///. without the <c>DockNodeAsHost-&gt;VisibleWindow</c>
    /// redirect, because dock nodes belong to Chunk 6 and <c>DockNodeAsHost</c> does not exist in this port.
    /// </remarks>
    private static ImGuiWindow GetWindowForTitleAndMenuHeight(ImGuiWindow window)
    {
        return window;
    }

    /// <summary>
    /// Computes the minimum size a window may be resized or constrained to.
    /// </summary>
    /// <param name="window">The window being measured.</param>
    /// <returns>The minimum size, in unscaled pixels.</returns>
    /// <remarks>
    /// Ports canonical <c>CalcWindowMinSize()</c>. The height
    /// floor reserves the title bar, the menu bar, and one rounding pixel, using the hard minimum of
    /// <c>4.0f</c> (<c>IMGUI_WINDOW_HARD_MIN_SIZE</c>, ).
    /// </remarks>
    private static ImVec2 CalcWindowMinSize(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImVec2 sizeMin;
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0 && (window.Flags & ImGuiWindowFlags.Popup) == 0)
        {
            sizeMin.X = (window.ChildFlags & ImGuiChildFlags.ResizeX) != 0 ? g.Style.WindowMinSize.X : 4.0f;
            sizeMin.Y = (window.ChildFlags & ImGuiChildFlags.ResizeY) != 0 ? g.Style.WindowMinSize.Y : 4.0f;
        }
        else
        {
            sizeMin.X = (window.Flags & ImGuiWindowFlags.AlwaysAutoResize) == 0 ? g.Style.WindowMinSize.X : 4.0f;
            sizeMin.Y = (window.Flags & ImGuiWindowFlags.AlwaysAutoResize) == 0 ? g.Style.WindowMinSize.Y : 4.0f;
        }

        ImGuiWindow windowForHeight = GetWindowForTitleAndMenuHeight(window);
        sizeMin.Y = MathF.Max(sizeMin.Y, windowForHeight.TitleBarHeight + windowForHeight.MenuBarHeight + MathF.Max(0.0f, g.Style.WindowRounding - 1.0f));
        return sizeMin;
    }

    /// <summary>
    /// Applies the pending size constraints and the minimum size to a desired window size.
    /// </summary>
    /// <param name="window">The window being sized.</param>
    /// <param name="sizeDesired">The size requested by the layout pass.</param>
    /// <returns>The constrained size.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>CalcWindowSizeAfterConstraint()</c>
    ///. A constraint axis with a negative minimum or maximum
    /// preserves the window's current full size on that axis; otherwise the desired value is clamped to the
    /// constraint rectangle.
    /// </para>
    /// <para>
    /// The optional size callback runs user code, so this member must only be called from <c>Begin</c>, the
    /// auto-fit helpers, and manual resizing, exactly as canonical documents.
    /// </para>
    /// </remarks>
    internal static ImVec2 CalcWindowSizeAfterConstraint(ImGuiWindow window, ImVec2 sizeDesired)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImVec2 newSize = sizeDesired;
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSizeConstraint) != 0)
        {
            ImRect cr = g.NextWindowData.SizeConstraintRect;
            newSize.X = (cr.Min.X >= 0.0f && cr.Max.X >= 0.0f) ? ClampValue(newSize.X, cr.Min.X, cr.Max.X) : window.SizeFull.X;
            newSize.Y = (cr.Min.Y >= 0.0f && cr.Max.Y >= 0.0f) ? ClampValue(newSize.Y, cr.Min.Y, cr.Max.Y) : window.SizeFull.Y;
            if (g.NextWindowData.SizeCallback != null)
            {
                ImGuiSizeCallbackData data = new()
                {
                    UserData = g.NextWindowData.SizeCallbackUserData,
                    Pos = window.Pos,
                    CurrentSize = window.SizeFull,
                    DesiredSize = newSize,
                };
                g.NextWindowData.SizeCallback(data);
                newSize = data.DesiredSize;
            }
            newSize.X = TruncateToInt(newSize.X);
            newSize.Y = TruncateToInt(newSize.Y);
        }

        ImVec2 sizeMin = CalcWindowMinSize(window);
        return new ImVec2(MathF.Max(newSize.X, sizeMin.X), MathF.Max(newSize.Y, sizeMin.Y));
    }

    /// <summary>
    /// Computes the content sizes used for auto-fitting and scrollbar visibility.
    /// </summary>
    /// <param name="window">The window being measured.</param>
    /// <param name="contentAvail">Receives the content size of the last frame.</param>
    /// <param name="contentMax">Receives the ideal content size, which includes items measured past the cursor.</param>
    /// <remarks>
    /// Ports canonical <c>CalcWindowContentSizes()</c>. The
    /// previous values are preserved while a window is collapsed or hidden from item submission so that
    /// auto-fit does not observe a collapsed layout.
    /// </remarks>
    internal static void CalcWindowContentSizes(ImGuiWindow window, out ImVec2 contentAvail, out ImVec2 contentMax)
    {
        bool preserveOldContentSizes = false;
        if (window.Collapsed && window.AutoFitFramesX <= 0 && window.AutoFitFramesY <= 0)
            preserveOldContentSizes = true;
        else if (window.Hidden && window.HiddenFramesCannotSkipItems == 0 && window.HiddenFramesCanSkipItems > 0)
            preserveOldContentSizes = true;

        if (preserveOldContentSizes)
        {
            contentAvail = window.ContentSize;
            contentMax = window.ContentSizeIdeal;
            return;
        }

        contentAvail.X = window.ContentSizeExplicit.X != 0.0f ? window.ContentSizeExplicit.X : (float)(long)(window.DC.CursorMaxPos.X - window.DC.CursorStartPos.X);
        contentAvail.Y = window.ContentSizeExplicit.Y != 0.0f ? window.ContentSizeExplicit.Y : (float)(long)(window.DC.CursorMaxPos.Y - window.DC.CursorStartPos.Y);
        contentMax.X = window.ContentSizeExplicit.X != 0.0f ? window.ContentSizeExplicit.X : (float)(long)(MathF.Max(window.DC.CursorMaxPos.X, window.DC.IdealMaxPos.X) - window.DC.CursorStartPos.X);
        contentMax.Y = window.ContentSizeExplicit.Y != 0.0f ? window.ContentSizeExplicit.Y : (float)(long)(MathF.Max(window.DC.CursorMaxPos.Y, window.DC.IdealMaxPos.Y) - window.DC.CursorStartPos.Y);
    }

    /// <summary>
    /// Computes the window size that fits the supplied content on the requested axes.
    /// </summary>
    /// <param name="window">The window being sized.</param>
    /// <param name="sizeContents">The content size to fit.</param>
    /// <param name="axisMask">Bit 0 requests auto-fit on the X axis, bit 1 on the Y axis.</param>
    /// <returns>The auto-fitted size.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>CalcWindowAutoFitSize()</c>.
    /// Tooltips always resize up to the maximum; other windows clamp to the minimum and maximum and then
    /// compensate the perpendicular axis for an expected scrollbar.
    /// </para>
    /// <para>
    /// The maximum size is the main viewport's work size, narrowed to a specific platform monitor's work
    /// size when the window recorded <c>ViewportAllowPlatformMonitorExtend</c>. That index is always -1 in
    /// this port because automatic window detachment is not implemented, so the monitor branch is not
    /// reachable yet.
    /// </para>
    /// </remarks>
    private static ImVec2 CalcWindowAutoFitSize(ImGuiWindow window, ImVec2 sizeContents, int axisMask)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiStyle style = g.Style;
        float decorationWWithoutScrollbars = window.DecoOuterSizeX1 + window.DecoOuterSizeX2 - window.ScrollbarSizes.X;
        float decorationHWithoutScrollbars = window.DecoOuterSizeY1 + window.DecoOuterSizeY2 - window.ScrollbarSizes.Y;
        ImVec2 sizePad = window.WindowPadding * 2.0f;
        ImVec2 sizeDesired;
        sizeDesired.X = (axisMask & 1) != 0 ? sizeContents.X + sizePad.X + decorationWWithoutScrollbars : window.Size.X;
        sizeDesired.Y = (axisMask & 2) != 0 ? sizeContents.Y + sizePad.Y + decorationHWithoutScrollbars : window.Size.Y;

        // Determine the maximum window size. Child windows are laid within their parent and have no
        // restriction unless they are also popups or menus.
        ImVec2 sizeMax = new(float.MaxValue, float.MaxValue);
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) == 0 || (window.Flags & ImGuiWindowFlags.Popup) != 0)
        {
            if (!window.ViewportOwned)
                sizeMax = GetMainViewport().WorkSize - style.DisplaySafeAreaPadding * 2.0f;
            int monitorIndex = window.ViewportAllowPlatformMonitorExtend;
            if (monitorIndex >= 0 && monitorIndex < g.PlatformIO.Monitors.Count)
                sizeMax = g.PlatformIO.Monitors[monitorIndex].WorkSize - style.DisplaySafeAreaPadding * 2.0f;
        }

        if ((window.Flags & ImGuiWindowFlags.Tooltip) != 0)
            return MinVector(sizeDesired, sizeMax);

        ImVec2 sizeMin = CalcWindowMinSize(window);
        ImVec2 sizeAutoFit = ClampVector(sizeDesired, MinVector(sizeMin, sizeMax), sizeMax);

        // When the window cannot fit all contents, grow the perpendicular axis to compensate for the
        // expected scrollbar (see canonical comment referencing #9352).
        ImVec2 sizeAutoFitAfterConstraint = CalcWindowSizeAfterConstraint(window, sizeAutoFit);
        float sizeContentsForScrollbarX = (axisMask & 1) != 0 ? sizeContents.X : window.ContentSize.X;
        float sizeContentsForScrollbarY = (axisMask & 2) != 0 ? sizeContents.Y : window.ContentSize.Y;
        bool willHaveScrollbarX = (sizeAutoFitAfterConstraint.X < sizeContentsForScrollbarX + sizePad.X + decorationWWithoutScrollbars
                && (window.Flags & ImGuiWindowFlags.NoScrollbar) == 0
                && (window.Flags & ImGuiWindowFlags.HorizontalScrollbar) != 0)
            || (window.Flags & ImGuiWindowFlags.AlwaysHorizontalScrollbar) != 0;
        bool willHaveScrollbarY = (sizeAutoFitAfterConstraint.Y < sizeContentsForScrollbarY + sizePad.Y + decorationHWithoutScrollbars
                && (window.Flags & ImGuiWindowFlags.NoScrollbar) == 0)
            || (window.Flags & ImGuiWindowFlags.AlwaysVerticalScrollbar) != 0;
        if (willHaveScrollbarX)
            sizeAutoFit.Y += style.ScrollbarSize;
        if (willHaveScrollbarY)
            sizeAutoFit.X += style.ScrollbarSize;
        return sizeAutoFit;
    }

    /// <summary>
    /// Computes the size a window would auto-fit to using its ideal content size.
    /// </summary>
    /// <param name="window">The window being measured.</param>
    /// <returns>The auto-fitted and constrained size.</returns>
    /// <remarks>
    /// Ports canonical <c>CalcWindowNextAutoFitSize()</c>. It is
    /// used by widgets that must reserve space before the window has laid out its contents.
    /// </remarks>
    internal static ImVec2 CalcWindowNextAutoFitSize(ImGuiWindow window)
    {
        CalcWindowContentSizes(window, out ImVec2 sizeContentsCurrent, out ImVec2 sizeContentsIdeal);
        _ = sizeContentsCurrent;
        ImVec2 sizeAutoFit = CalcWindowAutoFitSize(window, sizeContentsIdeal, ~0);
        return CalcWindowSizeAfterConstraint(window, sizeAutoFit);
    }

    #endregion

    #region Window pipeline: manual resize

    /// <summary>
    /// Geometry of one resize-grip corner: normalized corner position, inward direction, and the arc angles
    /// used to round the grip into the window corner.
    /// </summary>
    /// <remarks>
    /// Mirrors canonical file-scope <c>ImGuiResizeGripDef</c> and <c>resize_grip_def[4]</c>
    /// </remarks>
    private struct ImGuiResizeGripDef
    {
        /// <summary>Normalized corner position within the window rectangle.</summary>
        public ImVec2 CornerPosN;
        /// <summary>Unit direction pointing from the corner toward the window interior.</summary>
        public ImVec2 InnerDir;
        /// <summary>Start angle, in twelfths of a circle, of the rounded grip arc.</summary>
        public int AngleMin12;
        /// <summary>End angle, in twelfths of a circle, of the rounded grip arc.</summary>
        public int AngleMax12;
    }

    /// <summary>
    /// Geometry of one resize border: inward direction, the two normalized segment end points, and the
    /// outward stroke angle.
    /// </summary>
    /// <remarks>
    /// Mirrors canonical file-scope <c>ImGuiResizeBorderDef</c> and <c>resize_border_def[4]</c>
    /// </remarks>
    private struct ImGuiResizeBorderDef
    {
        /// <summary>Unit direction pointing from the border toward the window interior.</summary>
        public ImVec2 InnerDir;
        /// <summary>First end point of the border segment, normalized within the window rectangle.</summary>
        public ImVec2 SegmentN1;
        /// <summary>Second end point of the border segment, normalized within the window rectangle.</summary>
        public ImVec2 SegmentN2;
        /// <summary>Angle, in radians, pointing toward the outside of the window.</summary>
        public float OuterAngle;
    }

    /// <summary>Resize-grip geometry for the lower-right, lower-left, upper-left, and upper-right corners.</summary>
    private static readonly ImGuiResizeGripDef[] ResizeGripDefs =
    {
        new() { CornerPosN = new ImVec2(1.0f, 1.0f), InnerDir = new ImVec2(-1.0f, -1.0f), AngleMin12 = 0, AngleMax12 = 3 },
        new() { CornerPosN = new ImVec2(0.0f, 1.0f), InnerDir = new ImVec2(+1.0f, -1.0f), AngleMin12 = 3, AngleMax12 = 6 },
        new() { CornerPosN = new ImVec2(0.0f, 0.0f), InnerDir = new ImVec2(+1.0f, +1.0f), AngleMin12 = 6, AngleMax12 = 9 },
        new() { CornerPosN = new ImVec2(1.0f, 0.0f), InnerDir = new ImVec2(-1.0f, +1.0f), AngleMin12 = 9, AngleMax12 = 12 },
    };

    /// <summary>Resize-border geometry for the left, right, up, and down borders.</summary>
    private static readonly ImGuiResizeBorderDef[] ResizeBorderDefs =
    {
        new() { InnerDir = new ImVec2(+1.0f, 0.0f), SegmentN1 = new ImVec2(0.0f, 1.0f), SegmentN2 = new ImVec2(0.0f, 0.0f), OuterAngle = MathF.PI * 1.00f },
        new() { InnerDir = new ImVec2(-1.0f, 0.0f), SegmentN1 = new ImVec2(1.0f, 0.0f), SegmentN2 = new ImVec2(1.0f, 1.0f), OuterAngle = MathF.PI * 0.00f },
        new() { InnerDir = new ImVec2(0.0f, +1.0f), SegmentN1 = new ImVec2(0.0f, 0.0f), SegmentN2 = new ImVec2(1.0f, 0.0f), OuterAngle = MathF.PI * 1.50f },
        new() { InnerDir = new ImVec2(0.0f, -1.0f), SegmentN1 = new ImVec2(1.0f, 1.0f), SegmentN2 = new ImVec2(0.0f, 1.0f), OuterAngle = MathF.PI * 0.50f },
    };

    /// <summary>
    /// Builds the hit rectangle of one resize border.
    /// </summary>
    /// <param name="window">The window being resized.</param>
    /// <param name="borderN">Border index: 0 left, 1 right, 2 up, 3 down.</param>
    /// <param name="perpPadding">Inset applied along the border so corners do not overlap.</param>
    /// <param name="thickness">Border thickness; zero shrinks the rectangle by one pixel on both axes.</param>
    /// <returns>The border's hit rectangle.</returns>
    /// <remarks>
    /// Ports canonical file-scope <c>GetResizeBorderRect()</c>
    ///. An invalid border index is a programmer error and is
    /// asserted.
    /// </remarks>
    private static ImRect GetResizeBorderRect(ImGuiWindow window, int borderN, float perpPadding, float thickness)
    {
        ImRect rect = window.Rect();
        if (thickness == 0.0f)
            rect.Max = rect.Max - new ImVec2(1.0f, 1.0f);
        if (borderN == (int)ImGuiDir.Left)
            return new ImRect(rect.Min.X - thickness, rect.Min.Y + perpPadding, rect.Min.X + thickness, rect.Max.Y - perpPadding);
        if (borderN == (int)ImGuiDir.Right)
            return new ImRect(rect.Max.X - thickness, rect.Min.Y + perpPadding, rect.Max.X + thickness, rect.Max.Y - perpPadding);
        if (borderN == (int)ImGuiDir.Up)
            return new ImRect(rect.Min.X + perpPadding, rect.Min.Y - thickness, rect.Max.X - perpPadding, rect.Min.Y + thickness);
        if (borderN == (int)ImGuiDir.Down)
            return new ImRect(rect.Min.X + perpPadding, rect.Max.Y - thickness, rect.Max.X - perpPadding, rect.Max.Y + thickness);
        ImAssert.Check(false, "Invalid border index for GetResizeBorderRect().");
        return new ImRect();
    }

    /// <summary>
    /// Derives a window position and size from a dragged resize corner.
    /// </summary>
    /// <param name="window">The window being resized.</param>
    /// <param name="cornerTargetArg">The absolute position the corner is being dragged to.</param>
    /// <param name="cornerNorm">Normalized corner position within the window rectangle.</param>
    /// <param name="outPos">Receives the resulting window position.</param>
    /// <param name="outSize">Receives the resulting constrained window size.</param>
    /// <remarks>
    /// Ports canonical file-scope <c>CalcResizePosSizeFromAnyCorner()</c>
    ///. Child windows are clamped within the parent's inner
    /// rectangle unless the parent scrolls.
    /// </remarks>
    private static void CalcResizePosSizeFromAnyCorner(ImGuiWindow window, ImVec2 cornerTargetArg, ImVec2 cornerNorm, out ImVec2 outPos, out ImVec2 outSize)
    {
        ImVec2 cornerTarget = cornerTargetArg;
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
        {
            ImAssert.Check(window.ParentWindow != null);
            ImGuiWindow parentWindow = window.ParentWindow!;
            ImGuiWindowFlags parentFlags = parentWindow.Flags;
            ImRect limitRect = parentWindow.InnerRect;
            limitRect.Expand(new ImVec2(
                -MathF.Max(parentWindow.WindowPadding.X, parentWindow.WindowBorderSize),
                -MathF.Max(parentWindow.WindowPadding.Y, parentWindow.WindowBorderSize)));
            if ((parentFlags & (ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysHorizontalScrollbar)) == 0
                || (parentFlags & ImGuiWindowFlags.NoScrollbar) != 0)
                cornerTarget.X = ClampValue(cornerTarget.X, limitRect.Min.X, limitRect.Max.X);
            if ((parentFlags & ImGuiWindowFlags.NoScrollbar) != 0)
                cornerTarget.Y = ClampValue(cornerTarget.Y, limitRect.Min.Y, limitRect.Max.Y);
        }
        ImVec2 posMin = LerpVector(cornerTarget, window.Pos, cornerNorm);
        ImVec2 posMax = LerpVector(window.Pos + window.Size, cornerTarget, cornerNorm);
        ImVec2 sizeExpected = posMax - posMin;
        ImVec2 sizeConstrained = CalcWindowSizeAfterConstraint(window, sizeExpected);
        outPos = posMin;
        if (cornerNorm.X == 0.0f)
            outPos.X -= sizeConstrained.X - sizeExpected.X;
        if (cornerNorm.Y == 0.0f)
            outPos.Y -= sizeConstrained.Y - sizeExpected.Y;
        outSize = sizeConstrained;
    }

    /// <summary>
    /// Returns the identifier of a resize-grip corner.
    /// </summary>
    /// <param name="window">The window owning the grip.</param>
    /// <param name="n">Corner index in the range 0 to 3.</param>
    /// <returns>The corner grip identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>GetWindowResizeCornerID()</c>.: the
    /// window identifier seeds the hash of <c>#RESIZE</c>, which is then extended with the corner index. When
    /// docking is active canonical seeds with the dock node host identifier; docking belongs to Chunk 6, so
    /// the window identifier is always used here.
    /// </remarks>
    public static uint GetWindowResizeCornerID(ImGuiWindow window, int n)
    {
        ImAssert.Check(n >= 0 && n < 4);
        uint id = window.ID;
        id = ImHashStr("#RESIZE", 0, id);
        id = ImHashData((uint)n, id);
        return id;
    }

    /// <summary>
    /// Returns the identifier of a resize border.
    /// </summary>
    /// <param name="window">The window owning the border.</param>
    /// <param name="dir">Border direction: left, right, up, or down.</param>
    /// <returns>The border identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>GetWindowResizeBorderID()</c>.: the
    /// border index is the direction offset by four, so borders and corners never collide.
    /// </remarks>
    public static uint GetWindowResizeBorderID(ImGuiWindow window, ImGuiDir dir)
    {
        ImAssert.Check((int)dir >= 0 && (int)dir < 4);
        int n = (int)dir + 4;
        uint id = window.ID;
        id = ImHashStr("#RESIZE", 0, id);
        id = ImHashData((uint)n, id);
        return id;
    }

    /// <summary>
    /// Handles manual resizing through the corner grips, the window borders, and the keyboard/gamepad
    /// navigation resize mode.
    /// </summary>
    /// <param name="window">The window being resized.</param>
    /// <param name="borderHovered">Receives the hovered border index, or -1.</param>
    /// <param name="borderHeld">Receives the held border index, or -1.</param>
    /// <param name="resizeGripCount">Number of active corner grips.</param>
    /// <param name="resizeGripCol">Per-grip colors, written when a grip must be drawn.</param>
    /// <param name="visibilityRect">The clamped range of allowed window positions.</param>
    /// <returns>A bitmask of the axes that were auto-fitted this frame (bit 0 X, bit 1 Y).</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>UpdateWindowManualResize()</c>.
    /// </para>
    /// <para>
    /// The navigation-resize sub-block reads the canonical main scale through <c>GetScale()</c>, which this
    /// port reaches as <c>Style.FontScaleMain</c> because <c>ImGuiStyle._MainScale</c> is private.
    /// </para>
    /// </remarks>
    private static int UpdateWindowManualResize(ImGuiWindow window, out int borderHovered, out int borderHeld, int resizeGripCount, uint[] resizeGripCol, ImRect visibilityRect)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindowFlags flags = window.Flags;

        borderHovered = -1;
        borderHeld = -1;

        if ((flags & ImGuiWindowFlags.NoResize) != 0 || window.AutoFitFramesX > 0 || window.AutoFitFramesY > 0)
            return 0;
        if ((flags & ImGuiWindowFlags.AlwaysAutoResize) != 0 && (window.ChildFlags & (ImGuiChildFlags.ResizeX | ImGuiChildFlags.ResizeY)) == 0)
            return 0;
        if (!window.WasActive) // Early out to avoid running this code for e.g. a hidden implicit/fallback Debug window.
            return 0;

        int retAutoFitMask = 0x00;
        const float windowsResizeFromEdgesFeedbackTimer = 0.04f; // 
        float gripDrawSize = TruncateToInt(MathF.Max(g.FontSize * 1.35f, window.WindowRounding + 1.0f + g.FontSize * 0.2f));
        float gripHoverInnerSize = resizeGripCount > 0 ? TruncateToInt(gripDrawSize * 0.75f) : 0.0f;
        float gripHoverOuterSize = g.WindowsBorderHoverPadding;

        ImRect clampRect = visibilityRect;
        bool windowMoveFromTitleBar = (window.BgClickFlags & ImGuiWindowBgClickFlags.Move) == 0 && (window.Flags & ImGuiWindowFlags.NoTitleBar) == 0;
        if (windowMoveFromTitleBar)
            clampRect.Min.Y -= window.TitleBarHeight;

        ImVec2 posTarget = new(float.MaxValue, float.MaxValue);
        ImVec2 sizeTarget = new(float.MaxValue, float.MaxValue);

        // Clip mouse interaction rectangles within the viewport rectangle. This overwrites window.ClipRect
        // directly because the draw list is not set up yet at this point.
        ImAssert.Check(window.Viewport != null);
        bool clipWithViewportRect = (g.IO.BackendFlags & ImGuiBackendFlags.HasMouseHoveredViewport) == 0
            || g.IO.MouseHoveredViewport != window.ViewportId
            || (window.Viewport!.Flags & ImGuiViewportFlags.NoDecoration) == 0;
        if (clipWithViewportRect)
            window.ClipRect = window.Viewport!.GetMainRect();

        // Resize grips and borders live on the menu navigation layer.
        window.DC.NavLayerCurrent = ImGuiNavLayer.Menu;

        PushID("#RESIZE");
        for (int resizeGripN = 0; resizeGripN < resizeGripCount; resizeGripN++)
        {
            ImGuiResizeGripDef def = ResizeGripDefs[resizeGripN];
            ImVec2 corner = LerpVector(window.Pos, window.Pos + window.Size, def.CornerPosN);

            // FlattenChildren makes the resize button reachable even when the pointer is over a child window.
            ImRect resizeRect = new(corner - def.InnerDir * gripHoverOuterSize, corner + def.InnerDir * gripHoverInnerSize);
            if (resizeRect.Min.X > resizeRect.Max.X)
            {
                (resizeRect.Min.X, resizeRect.Max.X) = (resizeRect.Max.X, resizeRect.Min.X);
            }
            if (resizeRect.Min.Y > resizeRect.Max.Y)
            {
                (resizeRect.Min.Y, resizeRect.Max.Y) = (resizeRect.Max.Y, resizeRect.Min.Y);
            }
            uint resizeGripId = GetWindowResizeCornerID(window, resizeGripN); // == window.GetID(resize_grip_n)
            ItemAdd(resizeRect, resizeGripId, null, ImGuiItemFlags.NoNav);
            ButtonBehavior(resizeRect, resizeGripId, out bool hovered, out bool held,
                (ImGuiButtonFlags)((int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_FlattenChildren | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus));
            if (hovered || held)
                SetMouseCursor((resizeGripN & 1) != 0 ? ImGuiMouseCursor.ResizeNESW : ImGuiMouseCursor.ResizeNWSE);

            if (held && g.IO.MouseDoubleClicked[0])
            {
                // Auto-fit when double-clicking the grip.
                ImVec2 sizeAutoFit = CalcWindowAutoFitSize(window, window.ContentSizeIdeal, ~0);
                sizeTarget = CalcWindowSizeAfterConstraint(window, sizeAutoFit);
                retAutoFitMask = 0x03; // Both axes
                ClearActiveID();
            }
            else if (held)
            {
                // Resize from any of the four corners using an absolute target derived from the mouse
                // position rather than an incremental delta.
                ImVec2 clampMin = new(
                    def.CornerPosN.X == 1.0f ? clampRect.Min.X : -float.MaxValue,
                    (def.CornerPosN.Y == 1.0f || (def.CornerPosN.Y == 0.0f && windowMoveFromTitleBar)) ? clampRect.Min.Y : -float.MaxValue);
                ImVec2 clampMax = new(
                    def.CornerPosN.X == 0.0f ? clampRect.Max.X : +float.MaxValue,
                    def.CornerPosN.Y == 0.0f ? clampRect.Max.Y : +float.MaxValue);
                ImVec2 cornerTarget = g.IO.MousePos - g.ActiveIdClickOffset + LerpVector(def.InnerDir * gripHoverOuterSize, def.InnerDir * -gripHoverInnerSize, def.CornerPosN);
                cornerTarget = ClampVector(cornerTarget, clampMin, clampMax);
                CalcResizePosSizeFromAnyCorner(window, cornerTarget, def.CornerPosN, out posTarget, out sizeTarget);
            }

            // Only the lower-right grip is visible before hovering or activating.
            bool resizeGripVisible = held || hovered || (resizeGripN == 0 && (window.Flags & ImGuiWindowFlags.ChildWindow) == 0);
            if (resizeGripVisible)
                resizeGripCol[resizeGripN] = GetColorU32(held ? ImGuiCol.ResizeGripActive : hovered ? ImGuiCol.ResizeGripHovered : ImGuiCol.ResizeGrip);
        }

        int resizeBorderMask;
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
            resizeBorderMask = ((window.ChildFlags & ImGuiChildFlags.ResizeX) != 0 ? 0x02 : 0)
                | ((window.ChildFlags & ImGuiChildFlags.ResizeY) != 0 ? 0x08 : 0);
        else
            resizeBorderMask = g.IO.ConfigWindowsResizeFromEdges ? 0x0F : 0x00;
        for (int borderN = 0; borderN < 4; borderN++)
        {
            if ((resizeBorderMask & (1 << borderN)) == 0)
                continue;
            ImGuiResizeBorderDef def = ResizeBorderDefs[borderN];
            ImGuiAxis axis = (borderN == (int)ImGuiDir.Left || borderN == (int)ImGuiDir.Right) ? ImGuiAxis.X : ImGuiAxis.Y;

            ImRect borderRect = GetResizeBorderRect(window, borderN, gripHoverInnerSize, g.WindowsBorderHoverPadding);
            uint borderId = GetWindowResizeBorderID(window, (ImGuiDir)borderN); // == window.GetID(border_n + 4)
            ItemAdd(borderRect, borderId, null, ImGuiItemFlags.NoNav);
            ButtonBehavior(borderRect, borderId, out bool hovered, out bool held,
                (ImGuiButtonFlags)((int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_FlattenChildren | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus));
            if (hovered && g.HoveredIdTimer <= windowsResizeFromEdgesFeedbackTimer)
                hovered = false; // Reduce visual noise by only highlighting the border after a short delay.
            if (hovered || held)
                SetMouseCursor(axis == ImGuiAxis.X ? ImGuiMouseCursor.ResizeEW : ImGuiMouseCursor.ResizeNS);

            if (held && g.IO.MouseDoubleClicked[0])
            {
                // Double-clicking the bottom or right border auto-fits that axis.
                if (borderN == 1 || borderN == 3) // Right and bottom borders
                {
                    ImVec2 sizeAutoFit = CalcWindowAutoFitSize(window, window.ContentSizeIdeal, 1 << (int)axis);
                    float constrained = ReadAxisValue(CalcWindowSizeAfterConstraint(window, sizeAutoFit), axis);
                    WriteAxisValue(ref sizeTarget, axis, constrained);
                    retAutoFitMask |= 1 << (int)axis;
                    hovered = false;
                    held = false; // So the border does not show highlighted at its new position
                }
                ClearActiveID();
            }
            else if (held)
            {
                // Switch to relative resizing when the border geometry moved (for example when resizing a
                // child alters the parent scroll) to avoid a resize feedback loop.
                bool justScrolledManuallyWhileResizing = g.WheelingWindow != null
                    && g.WheelingWindowScrolledFrame == g.FrameCount
                    && IsWindowChildOf(window, g.WheelingWindow, false, true);
                if (g.ActiveIdIsJustActivated || justScrolledManuallyWhileResizing)
                {
                    g.WindowResizeBorderExpectedRect = borderRect;
                    g.WindowResizeRelativeMode = false;
                }
                if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0 && !g.WindowResizeBorderExpectedRect.Equals(borderRect))
                    g.WindowResizeRelativeMode = true;

                ImVec2 borderCurr = window.Pos + MinVector(def.SegmentN1, def.SegmentN2) * window.Size;
                float borderTargetRelModeForAxis = ReadAxisValue(borderCurr, axis) + ReadAxisValue(g.IO.MouseDelta, axis);
                float borderTargetAbsModeForAxis = ReadAxisValue(g.IO.MousePos, axis) - ReadAxisValue(g.ActiveIdClickOffset, axis) + g.WindowsBorderHoverPadding;

                ImVec2 borderTarget = window.Pos;
                WriteAxisValue(ref borderTarget, axis, borderTargetAbsModeForAxis);

                bool ignoreResize = false;
                if (g.WindowResizeRelativeMode)
                {
                    WriteAxisValue(ref borderTarget, axis, borderTargetRelModeForAxis);
                    if (ReadAxisValue(g.IO.MouseDelta, axis) == 0.0f
                        || (ReadAxisValue(g.IO.MouseDelta, axis) > 0.0f) == (borderTargetRelModeForAxis > borderTargetAbsModeForAxis))
                        ignoreResize = true;
                }

                ImVec2 clampMin = new(
                    borderN == (int)ImGuiDir.Right ? clampRect.Min.X : -float.MaxValue,
                    borderN == (int)ImGuiDir.Down || (borderN == (int)ImGuiDir.Up && windowMoveFromTitleBar) ? clampRect.Min.Y : -float.MaxValue);
                ImVec2 clampMax = new(
                    borderN == (int)ImGuiDir.Left ? clampRect.Max.X : +float.MaxValue,
                    borderN == (int)ImGuiDir.Up ? clampRect.Max.Y : +float.MaxValue);
                borderTarget = ClampVector(borderTarget, clampMin, clampMax);
                if (!ignoreResize)
                    CalcResizePosSizeFromAnyCorner(window, borderTarget, MinVector(def.SegmentN1, def.SegmentN2), out posTarget, out sizeTarget);
            }
            if (hovered)
                borderHovered = borderN;
            if (held)
                borderHeld = borderN;
        }
        PopID();

        // Restore the navigation layer.
        window.DC.NavLayerCurrent = ImGuiNavLayer.Main;

        // Navigation resize (keyboard/gamepad). This cannot move to NavUpdateWindowing() because
        // CalcWindowSizeAfterConstraint() may call back into user code.
        if (g.NavWindowingTarget != null && g.NavWindowingTarget.RootWindowDockTree == window)
        {
            ImVec2 navResizeDir = default;
            if (g.NavInputSource == ImGuiInputSource.Keyboard && g.IO.KeyShift)
                navResizeDir = GetKeyMagnitude2d(ImGuiKey.LeftArrow, ImGuiKey.RightArrow, ImGuiKey.UpArrow, ImGuiKey.DownArrow);
            if (g.NavInputSource == ImGuiInputSource.Gamepad)
                navResizeDir = GetKeyMagnitude2d(ImGuiKey.GamepadDpadLeft, ImGuiKey.GamepadDpadRight, ImGuiKey.GamepadDpadUp, ImGuiKey.GamepadDpadDown);
            if (navResizeDir.X != 0.0f || navResizeDir.Y != 0.0f)
            {
                const float navResizeSpeed = 600.0f;
                float resizeStep = navResizeSpeed * g.IO.DeltaTime * g.Style.FontScaleMain;
                g.NavWindowingAccumDeltaSize += navResizeDir * resizeStep;
                g.NavWindowingAccumDeltaSize = MaxVector(g.NavWindowingAccumDeltaSize, clampRect.Min - window.Pos - window.Size);
                g.NavWindowingToggleLayer = false;
                g.NavHighlightItemUnderNav = true;
                resizeGripCol[0] = GetColorU32(ImGuiCol.ResizeGripActive);
                ImVec2 accumFloored = TruncateVector(g.NavWindowingAccumDeltaSize);
                if (accumFloored.X != 0.0f || accumFloored.Y != 0.0f)
                {
                    sizeTarget = CalcWindowSizeAfterConstraint(window, window.SizeFull + accumFloored);
                    g.NavWindowingAccumDeltaSize -= accumFloored;
                }
            }
        }

        // Apply the modified position and size back to the window.
        ImVec2 oldPos = window.Pos;
        ImVec2 oldSize = window.SizeFull;
        if (sizeTarget.X != float.MaxValue && (window.Size.X != sizeTarget.X || window.SizeFull.X != sizeTarget.X))
        {
            window.Size = new ImVec2(sizeTarget.X, window.Size.Y);
            window.SizeFull = new ImVec2(sizeTarget.X, window.SizeFull.Y);
        }
        if (sizeTarget.Y != float.MaxValue && (window.Size.Y != sizeTarget.Y || window.SizeFull.Y != sizeTarget.Y))
        {
            window.Size = new ImVec2(window.Size.X, sizeTarget.Y);
            window.SizeFull = new ImVec2(window.SizeFull.X, sizeTarget.Y);
        }
        if (posTarget.X != float.MaxValue && window.Pos.X != TruncateToInt(posTarget.X))
            window.Pos = new ImVec2(TruncateToInt(posTarget.X), window.Pos.Y);
        if (posTarget.Y != float.MaxValue && window.Pos.Y != TruncateToInt(posTarget.Y))
            window.Pos = new ImVec2(window.Pos.X, TruncateToInt(posTarget.Y));
        if (oldPos.X != window.Pos.X || oldPos.Y != window.Pos.Y || oldSize.X != window.SizeFull.X || oldSize.Y != window.SizeFull.Y)
        {
            // The window moved or resized, so its persisted geometry must be rewritten.
            MarkIniSettingsDirty(window);
        }

        // Recompute the expected border rectangle from the final window geometry.
        if (borderHeld != -1)
            g.WindowResizeBorderExpectedRect = GetResizeBorderRect(window, borderHeld, gripHoverInnerSize, g.WindowsBorderHoverPadding);

        return retAutoFitMask;
    }

    /// <summary>
    /// Clamps a window position so the window remains visible within the allowed range.
    /// </summary>
    /// <param name="window">The window whose position is clamped.</param>
    /// <param name="visibilityRect">The range of allowed positions for the window's upper-left corner.</param>
    /// <remarks>
    /// Ports canonical <c>ClampWindowPos()</c>. When moving is
    /// restricted to the title bar, only the title bar height is kept inside the range. The dock-node-host
    /// case is inert because dock nodes belong to Chunk 6.
    /// </remarks>
    private static void ClampWindowPos(ImGuiWindow window, ImRect visibilityRect)
    {
        ImVec2 sizeForClamping = window.Size;
        bool moveFromTitleBarOnly = (window.BgClickFlags & ImGuiWindowBgClickFlags.Move) == 0;
        if (moveFromTitleBarOnly && (window.Flags & ImGuiWindowFlags.NoTitleBar) == 0)
            sizeForClamping.Y = window.TitleBarHeight;
        window.Pos = ClampVector(window.Pos, visibilityRect.Min - sizeForClamping, visibilityRect.Max);
    }

    #endregion

    #region Window pipeline: canonical math helpers

    /// <summary>Clamps a scalar to the inclusive range without requiring the range to be ordered.</summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <returns>The clamped value. Unlike <c>Math.Clamp</c>, an inverted range is tolerated because canonical
    /// <c>ImClamp</c> is used on rectangles that may legitimately be inverted.</returns>
    private static float ClampValue(float value, float min, float max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    /// <summary>Clamps each component of a vector to the corresponding component range.</summary>
    /// <param name="value">The vector to clamp.</param>
    /// <param name="min">The per-component lower bounds.</param>
    /// <param name="max">The per-component upper bounds.</param>
    /// <returns>The clamped vector.</returns>
    private static ImVec2 ClampVector(ImVec2 value, ImVec2 min, ImVec2 max)
    {
        return new ImVec2(ClampValue(value.X, min.X, max.X), ClampValue(value.Y, min.Y, max.Y));
    }

    /// <summary>Computes the component-wise minimum of two vectors, matching canonical <c>ImMin</c>.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The component-wise minimum.</returns>
    private static ImVec2 MinVector(ImVec2 a, ImVec2 b)
    {
        return new ImVec2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
    }

    /// <summary>Computes the component-wise maximum of two vectors, matching canonical <c>ImMax</c>.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The component-wise maximum.</returns>
    private static ImVec2 MaxVector(ImVec2 a, ImVec2 b)
    {
        return new ImVec2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
    }

    /// <summary>Interpolates two vectors with a scalar factor, matching canonical <c>ImLerp</c>.</summary>
    /// <param name="a">The value returned when the factor is zero.</param>
    /// <param name="b">The value returned when the factor is one.</param>
    /// <param name="t">The interpolation factor.</param>
    /// <returns>The interpolated vector.</returns>
    private static ImVec2 LerpVector(ImVec2 a, ImVec2 b, float t)
    {
        return new ImVec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>Interpolates two vectors with a per-component factor, matching canonical <c>ImLerp</c>.</summary>
    /// <param name="a">The value returned when the factor is zero.</param>
    /// <param name="b">The value returned when the factor is one.</param>
    /// <param name="t">The per-component interpolation factor.</param>
    /// <returns>The interpolated vector.</returns>
    private static ImVec2 LerpVector(ImVec2 a, ImVec2 b, ImVec2 t)
    {
        return new ImVec2(a.X + (b.X - a.X) * t.X, a.Y + (b.Y - a.Y) * t.Y);
    }

    /// <summary>Truncates a scalar toward zero, matching canonical <c>IM_TRUNC</c>.</summary>
    /// <param name="value">The value to truncate.</param>
    /// <returns>The truncated value.</returns>
    private static float TruncateToInt(float value)
    {
        return (float)(int)value;
    }

    /// <summary>Truncates each component of a vector toward zero, matching canonical <c>ImTrunc</c>.</summary>
    /// <param name="value">The vector to truncate.</param>
    /// <returns>The truncated vector.</returns>
    private static ImVec2 TruncateVector(ImVec2 value)
    {
        return new ImVec2((float)(int)value.X, (float)(int)value.Y);
    }

    /// <summary>
    /// Sweeps a scalar toward a target value by at most <paramref name="speed"/> units.
    /// </summary>
    /// <param name="current">Current scalar value.</param>
    /// <param name="target">Target value.</param>
    /// <param name="speed">Maximum delta per step.</param>
    /// <returns>Updated scalar.</returns>
    internal static float ImLinearSweep(float current, float target, float speed)
    {
        if (current < target) return MathF.Min(current + speed, target);
        if (current > target) return MathF.Max(current - speed, target);
        return current;
    }

    /// <summary>Reads one component of a vector by axis.</summary>
    /// <param name="value">The vector to read.</param>
    /// <param name="axis">The axis to read.</param>
    /// <returns>The selected component.</returns>
    private static float ReadAxisValue(ImVec2 value, ImGuiAxis axis)
    {
        return axis == ImGuiAxis.X ? value.X : value.Y;
    }

    /// <summary>Writes one component of a vector by axis.</summary>
    /// <param name="value">The vector to mutate.</param>
    /// <param name="axis">The axis to write.</param>
    /// <param name="component">The component value to store.</param>
    private static void WriteAxisValue(ref ImVec2 value, ImGuiAxis axis, float component)
    {
        if (axis == ImGuiAxis.X)
            value.X = component;
        else
            value.Y = component;
    }

    #endregion

    #region Window pipeline: decorations and title bar

    /// <summary>
    /// Returns the style color index used to fill a window's background.
    /// </summary>
    /// <param name="window">The window being drawn.</param>
    /// <returns>The background color index.</returns>
    /// <remarks>
    /// Ports canonical file-scope <c>GetWindowBgColorIdx()</c>
    /// </remarks>
    private static ImGuiCol GetWindowBgColorIdx(ImGuiWindow window)
    {
        if ((window.Flags & (ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.Popup)) != 0)
            return ImGuiCol.PopupBg;
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0 && !window.DockIsActive)
            return ImGuiCol.ChildBg;
        return ImGuiCol.WindowBg;
    }

    /// <summary>
    /// Strokes one resize border of a window.
    /// </summary>
    /// <param name="window">The window owning the border.</param>
    /// <param name="borderN">Border index: 0 left, 1 right, 2 up, 3 down.</param>
    /// <param name="borderCol">The stroke color.</param>
    /// <param name="borderSize">The stroke thickness.</param>
    /// <remarks>
    /// Ports canonical file-scope <c>RenderWindowOuterSingleBorder()</c>
    ///. The two arc segments are joined by the caller through
    /// <c>PathStroke</c>, which is why the path is left open here.
    /// </remarks>
    private static void RenderWindowOuterSingleBorder(ImGuiWindow window, int borderN, uint borderCol, float borderSize)
    {
        ImGuiResizeBorderDef def = ResizeBorderDefs[borderN];
        float rounding = window.WindowRounding;
        ImRect borderR = GetResizeBorderRect(window, borderN, rounding, 0.0f);
        window.DrawList.PathArcTo(
            LerpVector(borderR.Min, borderR.Max, def.SegmentN1) + new ImVec2(0.5f, 0.5f) + def.InnerDir * rounding,
            rounding, def.OuterAngle - MathF.PI * 0.25f, def.OuterAngle);
        window.DrawList.PathArcTo(
            LerpVector(borderR.Min, borderR.Max, def.SegmentN2) + new ImVec2(0.5f, 0.5f) + def.InnerDir * rounding,
            rounding, def.OuterAngle, def.OuterAngle + MathF.PI * 0.25f);
        window.DrawList.PathStroke(borderCol, borderSize);
    }

    /// <summary>
    /// Draws the outer border of a window, including the active or hovered resize indicator and the frame
    /// border under the title bar.
    /// </summary>
    /// <param name="window">The window being drawn.</param>
    /// <remarks>
    /// Ports canonical <c>RenderWindowOuterBorders()</c>. When
    /// the window has no background only explicitly resizable child borders are drawn, which keeps undecorated
    /// children from gaining a visual border.
    /// </remarks>
    private static void RenderWindowOuterBorders(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        float borderSize = window.WindowBorderSize;
        uint borderCol = GetColorU32(ImGuiCol.Border);
        if (borderSize > 0.0f && (window.Flags & ImGuiWindowFlags.NoBackground) == 0)
        {
            window.DrawList.AddRect(window.Pos, window.Pos + window.Size, borderCol, window.WindowRounding, window.WindowBorderSize);
        }
        else if (borderSize > 0.0f)
        {
            // Similar to the resize-border mask in UpdateWindowManualResize(), but only explicit child
            // resize borders are always drawn.
            if ((window.ChildFlags & ImGuiChildFlags.ResizeX) != 0)
                RenderWindowOuterSingleBorder(window, 1, borderCol, borderSize);
            if ((window.ChildFlags & ImGuiChildFlags.ResizeY) != 0)
                RenderWindowOuterSingleBorder(window, 3, borderCol, borderSize);
        }
        if (window.ResizeBorderHovered != -1 || window.ResizeBorderHeld != -1)
        {
            int borderN = window.ResizeBorderHeld != -1 ? window.ResizeBorderHeld : window.ResizeBorderHovered;
            uint borderColResizing = GetColorU32(window.ResizeBorderHeld != -1 ? ImGuiCol.SeparatorActive : ImGuiCol.SeparatorHovered);
            RenderWindowOuterSingleBorder(window, borderN, borderColResizing, MathF.Max(2.0f, window.WindowBorderSize)); // Thicker than usual
        }
        if (g.Style.FrameBorderSize > 0.0f && (window.Flags & ImGuiWindowFlags.NoTitleBar) == 0 && !window.DockIsActive)
        {
            float y = window.Pos.Y + window.TitleBarHeight - 1.0f;
            window.DrawList.AddLineH(window.Pos.X + borderSize * 0.5f, window.Pos.X + window.Size.X - borderSize * 0.5f, y, borderCol, g.Style.FrameBorderSize);
        }
    }

    /// <summary>
    /// Draws a window's background, title bar, menu bar, scrollbars, resize grips, and borders.
    /// </summary>
    /// <param name="window">The window being drawn.</param>
    /// <param name="titleBarRect">The title-bar rectangle of the window.</param>
    /// <param name="titleBarIsHighlight">True when the title bar should use its active color.</param>
    /// <param name="handleBordersAndResizeGrips">True when this call owns border and resize-grip rendering.</param>
    /// <param name="resizeGripCount">Number of resize grips to draw.</param>
    /// <param name="resizeGripCol">Per-grip colors; a fully transparent entry is skipped.</param>
    /// <param name="resizeGripDrawSize">The size of each resize grip.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>RenderWindowDecorations()</c>. The
    /// window must not yet be counted as begun, because the scrollbar path reads the current frame's
    /// <c>SkipItems</c> state.
    /// </para>
    /// <para>
    /// The following canonical branches are inert: the docking transparent-payload test and the dock-host
    /// background channel ordering (Chunk 6), the dock host's background suppression and border ownership
    /// (Chunk 6), and the hidden-tab-bar unhide triangle (Chunk 6).
    /// </para>
    /// </remarks>
    internal static void RenderWindowDecorations(ImGuiWindow window, ImRect titleBarRect, bool titleBarIsHighlight, bool handleBordersAndResizeGrips, int resizeGripCount, uint[] resizeGripCol, float resizeGripDrawSize)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiStyle style = g.Style;
        ImGuiWindowFlags flags = window.Flags;

        // Ensure that Scrollbar() does not read last frame's SkipItems.
        ImAssert.Check(window.BeginCount == 0);
        window.SkipItems = false;
        window.DC.NavLayerCurrent = ImGuiNavLayer.Menu;

        float windowRounding = window.WindowRounding;
        float windowBorderSize = window.WindowBorderSize;
        if (window.Collapsed)
        {
            // Title bar only.
            float backupBorderSize = style.FrameBorderSize;
            style.FrameBorderSize = window.WindowBorderSize;
            uint titleBarCol = GetColorU32((titleBarIsHighlight && g.NavCursorVisible) ? ImGuiCol.TitleBgActive : ImGuiCol.TitleBgCollapsed);
            if (window.ViewportOwned)
                titleBarCol |= 0xFF000000u; // No alpha
            RenderFrame(titleBarRect.Min, titleBarRect.Max, titleBarCol, true, windowRounding);
            style.FrameBorderSize = backupBorderSize;
        }
        else
        {
            if ((flags & ImGuiWindowFlags.NoBackground) == 0)
            {
                // Chunk 6 (docking): canonical computes `is_docking_transparent_payload` from the docking
                // drag-drop payload here and scales the background alpha. Docking payloads are inert.
                uint bgCol = GetColorU32(GetWindowBgColorIdx(window));
                if (window.ViewportOwned)
                {
                    bgCol |= 0xFF000000u; // No alpha
                }
                else
                {
                    bool overrideAlpha = false;
                    float alpha = 1.0f;
                    if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasBgAlpha) != 0)
                    {
                        alpha = g.NextWindowData.BgAlphaVal;
                        overrideAlpha = true;
                    }
                    if (overrideAlpha)
                        bgCol = (bgCol & ~0xFF000000u) | ((uint)(int)(Math.Clamp(alpha, 0.0f, 1.0f) * 255.0f + 0.5f) << 24);
                }

                // Chunk 6 (docking): canonical caches the color on the dock node, zeroes it for dock hosts,
                // and routes docked fills through the host draw list's background channel.
                if ((bgCol & 0xFF000000u) != 0)
                {
                    ImRect bgRect = new(window.Pos + new ImVec2(0.0f, window.TitleBarHeight), window.Pos + window.Size);
                    ImDrawFlags bgRoundingFlags = (flags & ImGuiWindowFlags.NoTitleBar) != 0 ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersBottom;
                    window.DrawList.AddRectFilled(bgRect.Min, bgRect.Max, bgCol, windowRounding, bgRoundingFlags);
                }
            }

            // Title bar. When docked, the dock node draws its own title bar, so this is skipped for docked
            // windows (Chunk 6).
            if ((flags & ImGuiWindowFlags.NoTitleBar) == 0 && !window.DockIsActive)
            {
                uint titleBarCol = GetColorU32(titleBarIsHighlight ? ImGuiCol.TitleBgActive : ImGuiCol.TitleBg);
                if (window.ViewportOwned)
                    titleBarCol |= 0xFF000000u; // No alpha
                window.DrawList.AddRectFilled(titleBarRect.Min, titleBarRect.Max, titleBarCol, windowRounding, ImDrawFlags.RoundCornersTop);
            }

            if ((flags & ImGuiWindowFlags.MenuBar) != 0)
            {
                ImRect menuBarRect = window.MenuBarRect();
                menuBarRect.ClipWith(window.Rect()); // Soft clipping; child windows may not cover the menu bar.
                window.DrawList.AddRectFilled(menuBarRect.Min, menuBarRect.Max, GetColorU32(ImGuiCol.MenuBarBg),
                    (flags & ImGuiWindowFlags.NoTitleBar) != 0 ? windowRounding : 0.0f, ImDrawFlags.RoundCornersTop);
                if (style.FrameBorderSize > 0.0f && menuBarRect.Max.Y < window.Pos.Y + window.Size.Y)
                    window.DrawList.AddLineH(menuBarRect.Min.X + windowBorderSize * 0.5f, menuBarRect.Max.X - windowBorderSize * 0.5f,
                        menuBarRect.Max.Y, GetColorU32(ImGuiCol.Border), style.FrameBorderSize);
            }

            // Chunk 6 (docking): the hidden-tab-bar unhide triangle is drawn and handled here. Inert.

            if (window.ScrollbarX)
                Scrollbar(ImGuiAxis.X);
            if (window.ScrollbarY)
                Scrollbar(ImGuiAxis.Y);

            // Draw resize grips after their input handling so they do not lag by one frame.
            if (handleBordersAndResizeGrips && (flags & ImGuiWindowFlags.NoResize) == 0)
            {
                for (int resizeGripN = 0; resizeGripN < resizeGripCount; resizeGripN++)
                {
                    uint col = resizeGripCol[resizeGripN];
                    if ((col & 0xFF000000u) == 0)
                        continue;
                    ImGuiResizeGripDef grip = ResizeGripDefs[resizeGripN];
                    ImVec2 corner = LerpVector(window.Pos, window.Pos + window.Size, grip.CornerPosN);
                    float borderInner = (float)(int)(windowBorderSize * 0.5f + 0.5f); // IM_ROUND
                    window.DrawList.PathLineTo(corner + grip.InnerDir * ((resizeGripN & 1) != 0
                        ? new ImVec2(borderInner, resizeGripDrawSize)
                        : new ImVec2(resizeGripDrawSize, borderInner)));
                    window.DrawList.PathLineTo(corner + grip.InnerDir * ((resizeGripN & 1) != 0
                        ? new ImVec2(resizeGripDrawSize, borderInner)
                        : new ImVec2(borderInner, resizeGripDrawSize)));
                    window.DrawList.PathArcToFast(
                        new ImVec2(corner.X + grip.InnerDir.X * (windowRounding + borderInner), corner.Y + grip.InnerDir.Y * (windowRounding + borderInner)),
                        windowRounding, grip.AngleMin12, grip.AngleMax12);
                    window.DrawList.PathFillConvex(col);
                }
            }

            // Borders. For a dock-node host the dock node draws them later, over the tab bar (Chunk 6).
            if (handleBordersAndResizeGrips)
                RenderWindowOuterBorders(window);
        }
        window.DC.NavLayerCurrent = ImGuiNavLayer.Main;
    }

    /// <summary>
    /// Lays out and renders the window title bar contents: the collapse button, the close button, the
    /// optional unsaved-document marker, and the centered or aligned title text.
    /// </summary>
    /// <param name="window">The window whose title bar is rendered.</param>
    /// <param name="titleBarRect">The title-bar rectangle, already inset by the window border.</param>
    /// <param name="name">The window name to display.</param>
    /// <param name="hasPOpen">True when the caller supplied an open flag and a close button must be laid out.</param>
    /// <param name="pOpen">The caller's open flag, updated to false when the close button is pressed.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>RenderWindowTitleBarContents()</c>
    /// </para>
    /// <para>
    /// The button submission and text rendering call into the widget layer
    /// (<c>CollapseButton</c>, <c>CloseButton</c>, <c>RenderBullet</c>, and <c>RenderTextClipped</c>).
    /// All layout arithmetic is
    /// performed here so the widget layer can be wired without revisiting the geometry.
    /// </para>
    /// </remarks>
    internal static void RenderWindowTitleBarContents(ImGuiWindow window, ImRect titleBarRect, string name, bool hasPOpen, ref bool pOpen)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiStyle style = g.Style;
        ImGuiWindowFlags flags = window.Flags;

        bool hasCloseButton = hasPOpen;
        bool hasCollapseButton = (flags & ImGuiWindowFlags.NoCollapse) == 0 && style.WindowMenuButtonPosition != ImGuiDir.None;

        // The close and collapse buttons live on the menu navigation layer and do not take default focus.
        ImGuiItemFlags itemFlagsBackup = g.CurrentItemFlags;
        g.CurrentItemFlags |= ImGuiItemFlags.NoNavDefaultFocus;
        window.DC.NavLayerCurrent = ImGuiNavLayer.Menu;

        float padL = style.FramePadding.X;
        float padR = style.FramePadding.X;
        float buttonSz = g.FontSize;
        ImVec2 closeButtonPos = default;
        ImVec2 collapseButtonPos = default;
        if (hasCloseButton)
        {
            closeButtonPos = new ImVec2(titleBarRect.Max.X - padR - buttonSz, titleBarRect.Min.Y + style.FramePadding.Y);
            padR += buttonSz + style.ItemInnerSpacing.X;
        }
        if (hasCollapseButton && style.WindowMenuButtonPosition == ImGuiDir.Right)
        {
            collapseButtonPos = new ImVec2(titleBarRect.Max.X - padR - buttonSz, titleBarRect.Min.Y + style.FramePadding.Y);
            padR += buttonSz + style.ItemInnerSpacing.X;
        }
        if (hasCollapseButton && style.WindowMenuButtonPosition == ImGuiDir.Left)
        {
            collapseButtonPos = new ImVec2(titleBarRect.Min.X + padL, titleBarRect.Min.Y + style.FramePadding.Y);
            padL += buttonSz + style.ItemInnerSpacing.X;
        }

        // Widget layer: the collapse button is submitted first so it wins the navigation-init fallback,
        // then the close button submits with ImGuiItemFlags_NoFocus temporarily added.
        if (hasCollapseButton)
            if (CollapseButton(window.GetID("#COLLAPSE"), collapseButtonPos, null))
                window.WantCollapseToggle = true; // Consumed by the title-bar collapse handler in Begin().

        if (hasCloseButton)
        {
            ImGuiItemFlags backupItemFlags = g.CurrentItemFlags;
            g.CurrentItemFlags |= (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoFocus;
            if (CloseButton(window.GetID("#CLOSE"), closeButtonPos))
                pOpen = false;
            g.CurrentItemFlags = backupItemFlags;
        }

        window.DC.NavLayerCurrent = ImGuiNavLayer.Main;
        g.CurrentItemFlags = itemFlagsBackup;

        // Title bar text with horizontal alignment, avoidance of the buttons, and the optional unsaved marker.
        float markerSizeX = (flags & ImGuiWindowFlags.UnsavedDocument) != 0 ? buttonSz * 0.80f : 0.0f;
        ImVec2 textSize = CalcTextSize(name, true) + new ImVec2(markerSizeX, 0.0f);

        // Centered title text is kept independent of the close and collapse button visibility.
        if (padL > style.FramePadding.X)
            padL += g.Style.ItemInnerSpacing.X;
        if (padR > style.FramePadding.X)
            padR += g.Style.ItemInnerSpacing.X;
        if (style.WindowTitleAlign.X > 0.0f && style.WindowTitleAlign.X < 1.0f)
        {
            float centerness = Math.Clamp(1.0f - MathF.Abs(style.WindowTitleAlign.X - 0.5f) * 2.0f, 0.0f, 1.0f); // 0 at edges, 1 at center
            float padExtend = MathF.Min(MathF.Max(padL, padR), titleBarRect.GetWidth() - padL - padR - textSize.X);
            padL = MathF.Max(padL, padExtend * centerness);
            padR = MathF.Max(padR, padExtend * centerness);
        }

        ImRect layoutR = new(titleBarRect.Min.X + padL, titleBarRect.Min.Y, titleBarRect.Max.X - padR, titleBarRect.Max.Y);
        ImRect clipR = new(layoutR.Min.X, layoutR.Min.Y, MathF.Min(layoutR.Max.X + g.Style.ItemInnerSpacing.X, titleBarRect.Max.X), layoutR.Max.Y);
        if ((flags & ImGuiWindowFlags.UnsavedDocument) != 0)
        {
            float markerPosX = ClampValue(layoutR.Min.X + (layoutR.GetWidth() - textSize.X) * style.WindowTitleAlign.X + textSize.X, layoutR.Min.X, layoutR.Max.X);
            float markerPosY = (layoutR.Min.Y + layoutR.Max.Y) * 0.5f;
            if (markerPosX > layoutR.Min.X)
            {
                RenderBullet(window.DrawList, new ImVec2(markerPosX, markerPosY), GetColorU32(ImGuiCol.UnsavedMarker));
                clipR.Max.X = MathF.Min(clipR.Max.X, markerPosX - (int)(markerSizeX * 0.5f));
            }
        }
        RenderTextClipped(layoutR.Min, layoutR.Max, name, null, textSize, style.WindowTitleAlign, clipR);
    }

    #endregion

    #region Window pipeline: parent and root links

    /// <summary>
    /// Latches a window's parent and propagates the root-window pointers used by clipping, focus, popup
    /// trees, title-bar highlighting, and navigation flattening.
    /// </summary>
    /// <param name="window">The window whose links are updated.</param>
    /// <param name="flags">The window flags resolved for this frame.</param>
    /// <param name="parentWindow">The parent latched on the first begin of the frame, or null.</param>
    /// <remarks>
    /// Ports canonical <c>UpdateWindowParentAndRootLinks()</c>
    ///. The docked-window root propagation is inert because
    /// <c>window.DockIsActive</c> is always false in this port (Chunk 6).
    /// </remarks>
    internal static void UpdateWindowParentAndRootLinks(ImGuiWindow window, ImGuiWindowFlags flags, ImGuiWindow? parentWindow)
    {
        window.ParentWindow = parentWindow;
        window.RootWindow = window;
        window.RootWindowPopupTree = window;
        window.RootWindowDockTree = window;
        window.RootWindowForTitleBarHighlight = window;
        window.RootWindowForNav = window;
        if (parentWindow != null && (flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.Tooltip) == 0)
        {
            window.RootWindowDockTree = parentWindow.RootWindowDockTree;
            if (!window.DockIsActive && (parentWindow.Flags & ImGuiWindowFlags.DockNodeHost) == 0)
                window.RootWindow = parentWindow.RootWindow;
        }
        if (parentWindow != null && (flags & ImGuiWindowFlags.Popup) != 0)
            window.RootWindowPopupTree = parentWindow.RootWindowPopupTree;
        if (parentWindow != null
            && (flags & ImGuiWindowFlags.Modal) == 0
            && (flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) != 0)
            window.RootWindowForTitleBarHighlight = parentWindow.RootWindowForTitleBarHighlight;

        // Navigation flattening walks up while the current navigation root requests flattening.
        while ((window.RootWindowForNav!.ChildFlags & ImGuiChildFlags.NavFlattened) != 0)
        {
            ImAssert.Check(window.RootWindowForNav.ParentWindow != null);
            window.RootWindowForNav = window.RootWindowForNav.ParentWindow;
        }
    }

    /// <summary>
    /// Applies the experimental skip-refresh policy for the current frame.
    /// </summary>
    /// <param name="window">The window being begun.</param>
    /// <remarks>
    /// <para>
    /// Canonical <c>UpdateWindowSkipRefresh()</c>. detaches the
    /// window draw list (<c>window-&gt;DrawList = NULL</c>) and sets <c>window-&gt;SkipRefresh</c> so the
    /// frame skips layout and rendering while keeping input state. This port declares
    /// <c>ImGuiWindow.DrawList</c> as a non-nullable property, so the detach step is not representable and
    /// canonical's own specification marks the mode as safe to defer with <c>SkipRefresh = false</c>
    /// (<c>03_windowing.md</c> section 13.6).
    /// </para>
    /// <para>
    /// The policy is therefore never enabled; <c>window.SkipRefresh</c> is always false and the guarded
    /// branches in <see cref="BeginCore"/> and <see cref="End"/> remain inert.
    /// </para>
    /// </remarks>
    public static void UpdateWindowSkipRefresh(ImGuiWindow window)
    {
        window.SkipRefresh = false;
    }

    #endregion

    #region Window pipeline: disabled-state override for nested root windows

    /// <summary>
    /// Re-enables interaction for a nested root window (typically a tooltip) begun while a disabled scope is
    /// active.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>BeginDisabledOverrideReenable()</c>
    ///. The canonical member is not listed as owned by any part
    /// of this chunk, so it is defined here as an internal helper because both <see cref="BeginCore"/> and
    /// <see cref="End"/> depend on it.
    /// </remarks>
    internal static void BeginDisabledOverrideReenable()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check((g.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0);
        g.CurrentWindowStack[^1].DisabledOverrideReenableAlphaBackup = g.Style.Alpha;
        g.Style.Alpha = g.DisabledAlphaBackup;
        g.CurrentItemFlags &= ~ImGuiItemFlags.Disabled;
        g.ItemFlagsStack.Add(g.CurrentItemFlags);
        g.DisabledStackSize++;
    }

    /// <summary>
    /// Restores the disabled state saved by <see cref="BeginDisabledOverrideReenable"/>.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>EndDisabledOverrideReenable()</c>
    /// </remarks>
    internal static void EndDisabledOverrideReenable()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(g.DisabledStackSize > 0);
        g.DisabledStackSize--;
        g.ItemFlagsStack.RemoveAt(g.ItemFlagsStack.Count - 1);
        g.CurrentItemFlags = g.ItemFlagsStack[^1];
        g.Style.Alpha = g.CurrentWindowStack[^1].DisabledOverrideReenableAlphaBackup;
    }

    #endregion

    #region Begin / End

    /// <summary>
    /// Starts a window and returns false when its contents must be skipped for this frame.
    /// </summary>
    /// <param name="name">The window name; it must be non-empty.</param>
    /// <param name="flags">The window flags.</param>
    /// <returns>True when the window is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// Canonical <c>Begin()</c> always pushes the window stack, so every call must be paired with
    /// <see cref="End"/> even when this overload returns false. This overload forwards to
    /// <see cref="BeginCore"/> with no open flag.
    /// </remarks>
    public static bool Begin(string name, ImGuiWindowFlags flags = 0)
    {
        bool dummyOpen = true;
        return BeginCore(name, ref dummyOpen, false, flags);
    }

    /// <summary>
    /// Starts a window that displays a close button bound to the supplied open flag.
    /// </summary>
    /// <param name="name">The window name; it must be non-empty.</param>
    /// <param name="pOpen">The open flag; it is set to false when the close button is pressed.</param>
    /// <param name="flags">The window flags.</param>
    /// <returns>True when the window is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// The canonical parameter is <c>bool* p_open</c>; the port represents the optional pointer as a
    /// <c>ref</c> parameter plus the companion <c>has_p_open</c> flag accepted by <see cref="BeginCore"/>.
    /// </remarks>
    public static bool Begin(string name, ref bool pOpen, ImGuiWindowFlags flags = 0)
    {
        return BeginCore(name, ref pOpen, true, flags);
    }

    /// <summary>
    /// Implements the canonical <c>Begin()</c> window pipeline.
    /// </summary>
    /// <param name="name">The window name; it must be non-empty.</param>
    /// <param name="pOpen">The caller's open flag; only meaningful when <paramref name="hasPOpen"/> is true.</param>
    /// <param name="hasPOpen">True when the caller supplied an open flag, matching canonical <c>p_open != NULL</c>.</param>
    /// <param name="flags">The window flags.</param>
    /// <returns>True when the window is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>Begin()</c>. with the canonical phase
    /// order preserved: flag resolution, parent/child validation, window lookup and creation,
    /// <c>SetNextWindow*</c> consumption, size and position resolution, collapsing, resizing, scrollbar
    /// setup, rectangle updates, decoration and title-bar marking, layout-context reset, window-stack push,
    /// title-bar item submission, and the return-value decision.
    /// </para>
    /// <para>
    /// Branch ownership: the docking block (Chunk 6), the secondary-viewport selection and late viewport
    /// creation (automatic detachment into secondary platform viewports is not implemented), table and tab
    /// context (Chunks 5 and 7), the menu column measurement (Chunk 4), the platform-request-close dock
    /// guard (Chunk 6), the Ctrl+C content copy (input-shortcut and logging subsystems), the docking
    /// drag-drop source and target (Chunk 6), the debug locate resolve (debug tooling), the test-engine
    /// window registration (test tooling), and the settings dirty marking (Chunk 13) are all inert as
    /// documented at their call sites.
    /// </para>
    /// <para>
    /// Canonical asserts become <c>ImAssert.Check</c> for programmer errors. The two window-stack
    /// asserts in <see cref="End"/> and the flag-validation assert in <see cref="IsWindowHovered"/> use
    /// <see cref="ErrorLog"/> guarded early returns because canonical treats them as user errors with error
    /// recovery.
    /// </para>
    /// </remarks>
    private static bool BeginCore(string name, ref bool pOpen, bool hasPOpen, ImGuiWindowFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiStyle style = g.Style;
        ImAssert.Check(!string.IsNullOrEmpty(name)); // Window name required.
        ImAssert.Check(g.WithinFrameScope); // Forgot to call ImGui::NewFrame().
        ImAssert.Check(g.FrameCountEnded != g.FrameCount); // Called ImGui::Render() or EndFrame() and haven't called NewFrame() again yet.

        // Find or create.
        ImGuiWindow? windowLookup = FindWindowByName(name);
        bool windowJustCreated = windowLookup == null;
        ImGuiWindow window = windowJustCreated ? CreateNewWindow(name, flags) : windowLookup!;

        // Debug break requested by the user.
        if (g.DebugBreakInWindow == window.ID)
            Debugger.Break();

        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasWindowFlags) != 0)
            flags = (flags | g.NextWindowData.WindowFlagsSet) & ~g.NextWindowData.WindowFlagsClear;

        // Automatically disable manual moving and resizing when NoInputs is set.
        if ((flags & ImGuiWindowFlags.NoInputs) == ImGuiWindowFlags.NoInputs)
            flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;

        int currentFrame = g.FrameCount;
        bool firstBeginOfTheFrame = window.LastFrameActive != currentFrame;
        window.IsFallbackWindow = g.CurrentWindowStack.Count == 0 && g.WithinFrameScopeWithImplicitWindow;

        // Update the Appearing flag. BeginDocked() may also set it later (Chunk 6).
        bool windowJustActivatedByUser = window.LastFrameActive < currentFrame - 1; // Not using !WasActive because the implicit "Debug" window would always toggle off->on.
        if ((flags & ImGuiWindowFlags.Popup) != 0)
        {
            ImAssert.Check(g.BeginPopupStack.Count < g.OpenPopupStack.Count);
            ImGuiPopupData popupRef = g.OpenPopupStack[g.BeginPopupStack.Count];
            windowJustActivatedByUser |= window.PopupId != popupRef.PopupId; // Recycled popups count as activated when the popup id changes.
            windowJustActivatedByUser |= !ReferenceEquals(window, popupRef.Window);
        }

        // Update Flags, LastFrameActive, and BeginOrderXXX fields.
        if (firstBeginOfTheFrame)
        {
            UpdateWindowInFocusOrderList(window, windowJustCreated, flags);
            window.Appearing = windowJustActivatedByUser;
            if (window.Appearing)
                SetWindowConditionAllowFlags(window, ImGuiCond.Appearing, true);
            window.FlagsPreviousFrame = window.Flags;
            window.Flags = flags;
            window.ChildFlags = (g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasChildFlags) != 0 ? g.NextWindowData.ChildFlagsSet : 0;
            window.LastFrameActive = currentFrame;
            window.LastTimeActive = (float)g.Time;
            window.BeginOrderWithinParent = 0;
            window.BeginOrderWithinContext = (short)(g.WindowsActiveCount++);
        }
        else
        {
            flags = window.Flags;
        }

        // Docking
        ImAssert.Check(window.DockNode == null || window.DockNodeAsHost == null); // Cannot be both
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasDock) != 0)
            SetWindowDock(window, g.NextWindowData.DockId, g.NextWindowData.DockCond);
        if (firstBeginOfTheFrame)
        {
            bool hasDockNode = (window.DockId != 0 || window.DockNode != null);
            bool newAutoDockNode = !hasDockNode && GetWindowAlwaysWantOwnTabBar(window);
            bool dockNodeWasVisible = window.DockNodeIsVisible;
            bool dockTabWasVisible = window.DockTabIsVisible;
            window.DockIsActive = false;
            window.DockNodeIsVisible = false;
            window.DockTabIsVisible = false;

            if (hasDockNode || newAutoDockNode)
            {
                BeginDocked(window, ref pOpen, hasPOpen);
                flags = window.Flags;
                if (window.DockIsActive)
                {
                    ImAssert.Check(window.DockNode != null);
                    g.NextWindowData.HasFlags &= ~ImGuiNextWindowDataFlags.HasSizeConstraint; // Docking overrides constraints
                }

                if (window.DockTabIsVisible && !dockTabWasVisible && dockNodeWasVisible && !window.Appearing && !windowJustActivatedByUser)
                {
                    window.Appearing = true;
                    SetWindowConditionAllowFlags(window, ImGuiCond.Appearing, true);
                }
            }
        }

        // Parent window is latched only on the first Begin() of the frame, so further append calls can
        // be issued from a different window stack.
        ImGuiWindow? parentWindowInStack = (window.DockIsActive && window.DockNode?.HostWindow != null)
            ? window.DockNode.HostWindow
            : g.CurrentWindowStack.Count == 0 ? null : g.CurrentWindowStack[^1].Window;
        ImGuiWindow? parentWindow = firstBeginOfTheFrame
            ? (((flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) != 0) ? parentWindowInStack : null)
            : window.ParentWindow;
        ImAssert.Check(parentWindow != null || (flags & ImGuiWindowFlags.ChildWindow) == 0);

        // Window memory may have been compacted, so recreate the base identifier stack entry when needed.
        if (window.IDStack.Count == 0)
            window.IDStack.Add(window.ID);

        // Add to the window stack.
        g.CurrentWindow = window;
        g.CurrentWindowStack.Add(new ImGuiWindowStackData());
        ImGuiWindowStackData windowStackData = g.CurrentWindowStack[^1];
        windowStackData.Window = window;
        windowStackData.ParentLastItemDataBackup.CopyFrom(g.LastItemData);
        windowStackData.DisabledOverrideReenable = (flags & ImGuiWindowFlags.Tooltip) != 0 && (g.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0;
        windowStackData.DisabledOverrideReenableAlphaBackup = 0.0f;
        ErrorRecoveryStoreState(windowStackData.StackSizesInBegin);
        g.StackSizesInBeginForCurrentWindow = windowStackData.StackSizesInBegin;
        if ((flags & ImGuiWindowFlags.ChildMenu) != 0)
            g.BeginMenuDepth++;

        // Update the root-window pointers before any possible call to FocusWindow().
        if (firstBeginOfTheFrame)
        {
            UpdateWindowParentAndRootLinks(window, flags, parentWindow);
            window.ParentWindowInBeginStack = parentWindowInStack;

            window.ParentWindowForFocusRoute = window.RootWindow != window ? parentWindowInStack : null;
            if (window.ParentWindowForFocusRoute == null && window.DockNode != null)
                if ((window.DockNode.MergedFlags & ImGuiDockNodeFlags.DockedWindowsInFocusRoute) != 0)
                    window.ParentWindowForFocusRoute = window.DockNode.HostWindow;

            if (window.WindowClass.FocusRouteParentWindowId != 0)
            {
                window.ParentWindowForFocusRoute = FindWindowByID(window.WindowClass.FocusRouteParentWindowId);
                ImAssert.Check(window.ParentWindowForFocusRoute != null); // Invalid value for FocusRouteParentWindowId.
            }

            window.FontWindowScaleParents = parentWindow != null
                ? parentWindow.FontWindowScaleParents * parentWindow.FontWindowScale
                : 1.0f;
        }

        // Add to the focus-scope stack. Canonical PushFocusScope(). makes
        // the window its own focus scope, except for a navigation-flattened child that inherits the
        // enclosing scope. The cached root scope records the resulting scope identifier, which focused
        // shortcut routing and navigation ownership both rely on.
        PushFocusScope((window.ChildFlags & ImGuiChildFlags.NavFlattened) != 0 ? g.CurrentFocusScopeId : window.ID);
        window.NavRootFocusScopeId = g.CurrentFocusScopeId;

        // Add to the popup stacks: update the open-popup entry and push the begin-popup entry.
        if ((flags & ImGuiWindowFlags.Popup) != 0)
        {
            ImAssert.Check(g.BeginPopupStack.Count < g.OpenPopupStack.Count);
            ImAssert.Check(parentWindowInStack != null);
            ImGuiPopupData popupRef = g.OpenPopupStack[g.BeginPopupStack.Count];
            popupRef.Window = window;
            popupRef.ParentNavLayer = (int)parentWindowInStack!.DC.NavLayerCurrent;
            g.BeginPopupStack.Add(popupRef);
            window.PopupId = popupRef.PopupId;
        }

        // Process SetNextWindow***() calls.
        bool windowPosSetByApi = false;
        bool windowSizeXSetByApi = false;
        bool windowSizeYSetByApi = false;
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasPos) != 0)
        {
            windowPosSetByApi = (window.SetWindowPosAllowFlags & g.NextWindowData.PosCond) != 0;
            ImVec2 posPivot = g.NextWindowData.PosPivotVal;
            if (windowPosSetByApi && (posPivot.X * posPivot.X + posPivot.Y * posPivot.Y) > 0.00001f)
            {
                // May be processed on the next frame while the window is still measuring its size.
                window.SetWindowPosVal = g.NextWindowData.PosVal;
                window.SetWindowPosPivot = g.NextWindowData.PosPivotVal;
                window.SetWindowPosAllowFlags &= ~(ImGuiCond.Once | ImGuiCond.FirstUseEver | ImGuiCond.Appearing);
            }
            else
            {
                SetWindowPos(window, g.NextWindowData.PosVal, g.NextWindowData.PosCond);
            }
        }
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSize) != 0)
        {
            windowSizeXSetByApi = (window.SetWindowSizeAllowFlags & g.NextWindowData.SizeCond) != 0 && g.NextWindowData.SizeVal.X > 0.0f;
            windowSizeYSetByApi = (window.SetWindowSizeAllowFlags & g.NextWindowData.SizeCond) != 0 && g.NextWindowData.SizeVal.Y > 0.0f;
            if ((window.ChildFlags & ImGuiChildFlags.ResizeX) != 0 && (window.SetWindowSizeAllowFlags & ImGuiCond.FirstUseEver) == 0)
                g.NextWindowData.SizeVal = new ImVec2(window.SizeFull.X, g.NextWindowData.SizeVal.Y);
            if ((window.ChildFlags & ImGuiChildFlags.ResizeY) != 0 && (window.SetWindowSizeAllowFlags & ImGuiCond.FirstUseEver) == 0)
                g.NextWindowData.SizeVal = new ImVec2(g.NextWindowData.SizeVal.X, window.SizeFull.Y);
            SetWindowSize(window, g.NextWindowData.SizeVal, g.NextWindowData.SizeCond);
        }
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasScroll) != 0)
        {
            ImVec2 scrollTarget = window.ScrollTarget;
            ImVec2 scrollTargetCenterRatio = window.ScrollTargetCenterRatio;
            if (g.NextWindowData.ScrollVal.X >= 0.0f)
            {
                scrollTarget.X = g.NextWindowData.ScrollVal.X;
                scrollTargetCenterRatio.X = 0.0f;
            }
            if (g.NextWindowData.ScrollVal.Y >= 0.0f)
            {
                scrollTarget.Y = g.NextWindowData.ScrollVal.Y;
                scrollTargetCenterRatio.Y = 0.0f;
            }
            window.ScrollTarget = scrollTarget;
            window.ScrollTargetCenterRatio = scrollTargetCenterRatio;
        }
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasContentSize) != 0)
            window.ContentSizeExplicit = g.NextWindowData.ContentSizeVal;
        else if (firstBeginOfTheFrame)
            window.ContentSizeExplicit = new ImVec2(0.0f, 0.0f);
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasWindowClass) != 0)
            window.WindowClass = g.NextWindowData.WindowClass;
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasCollapsed) != 0)
            SetWindowCollapsed(window, g.NextWindowData.CollapsedVal, g.NextWindowData.CollapsedCond);
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasFocus) != 0)
            FocusWindow(window);
        if (window.Appearing)
            SetWindowConditionAllowFlags(window, ImGuiCond.Appearing, false);

        // [EXPERIMENTAL] Skip-refresh mode: evaluated but never enabled in this port.
        UpdateWindowSkipRefresh(window);

        // Nested root windows (typically tooltips) override the disabled state.
        if (windowStackData.DisabledOverrideReenable && window.RootWindow == window)
            BeginDisabledOverrideReenable();

        // g.CurrentWindow is intentionally cleared until the viewport is selected and SetCurrentWindow() runs.
        g.CurrentWindow = null;

        // When reusing a window multiple times a frame, only append content: the setup below runs once.
        if (firstBeginOfTheFrame && !window.SkipRefresh)
        {
            // Initialize.
            bool windowIsChildTooltip = (flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.Tooltip) != 0; // Undocumented Child+Tooltip behavior for pinned tooltips (#1345)
            bool windowJustAppearingAfterHiddenForResize = window.HiddenFramesCannotSkipItems > 0;
            window.Active = true;
            window.HasCloseButton = hasPOpen;
            window.ClipRect = new ImRect(-float.MaxValue, -float.MaxValue, +float.MaxValue, +float.MaxValue);
            if (window.IDStack.Count > 1)
                window.IDStack.RemoveRange(1, window.IDStack.Count - 1);
            window.DrawList.Clear(GetCurrentContext()!.DrawListSharedData.InitialDrawFlags);
            window.DC.CurrentTableIdx = -1;
            if ((flags & ImGuiWindowFlags.DockNodeHost) != 0)
            {
                window.DrawList.ChannelsSplit(2);
                window.DrawList.ChannelsSetCurrent(DOCKING_HOST_DRAW_CHANNEL_FG);
            }

            // Restore buffer capacity when the window wakes from a compacted state.
            if (window.MemoryCompacted)
                GcAwakeTransientWindowBuffers(window);

            // Update the stored window name when it changes, which can only happen through the "###"
            // operator, so the identifier stays unchanged.
            bool windowTitleVisibleElsewhere = false;
            if ((window.Viewport != null && ReferenceEquals(window.Viewport.Window, window)) || window.DockIsActive)
                windowTitleVisibleElsewhere = true;
            else if (g.NavWindowingListWindow != null && g.NavWindowingListWindow.WasActive && (flags & ImGuiWindowFlags.NoNavFocus) == 0)
                windowTitleVisibleElsewhere = true; // Window titles are visible while using Ctrl+Tab.
            else if ((flags & ImGuiWindowFlags.ChildMenu) != 0)
                windowTitleVisibleElsewhere = true;
            if ((windowTitleVisibleElsewhere || windowJustActivatedByUser) && !windowJustCreated && !string.Equals(name, window.Name))
            {
                // Canonical ImStrdupcpy() reallocates the name buffer; managed strings are replaced whole.
                window.Name = name;
                window.NameBufLen = name.Length + 1;
            }

            // Update content size from the last frame for auto-fitting, or use the explicit size.
            CalcWindowContentSizes(window, out ImVec2 contentSize, out ImVec2 contentSizeIdeal);
            window.ContentSize = contentSize;
            window.ContentSizeIdeal = contentSizeIdeal;

            // These counters are decremented before use, so a behavior lasting one frame requires a value
            // of at least two.
            if (window.HiddenFramesCanSkipItems > 0)
                window.HiddenFramesCanSkipItems--;
            if (window.HiddenFramesCannotSkipItems > 0)
                window.HiddenFramesCannotSkipItems--;
            if (window.HiddenFramesForRenderOnly > 0)
                window.HiddenFramesForRenderOnly--;

            // Hide new windows for one frame until they calculate their size.
            if (windowJustCreated && (!windowSizeXSetByApi || !windowSizeYSetByApi))
                window.HiddenFramesCannotSkipItems = 1;

            // Hide popups and tooltips when they are reopened while their size is measured.
            if (windowJustActivatedByUser && (flags & (ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) != 0)
            {
                window.HiddenFramesCannotSkipItems = 1;
                if ((flags & ImGuiWindowFlags.AlwaysAutoResize) != 0)
                {
                    if (!windowSizeXSetByApi)
                    {
                        window.Size = new ImVec2(0.0f, window.Size.Y);
                        window.SizeFull = new ImVec2(0.0f, window.SizeFull.Y);
                    }
                    if (!windowSizeYSetByApi)
                    {
                        window.Size = new ImVec2(window.Size.X, 0.0f);
                        window.SizeFull = new ImVec2(window.SizeFull.X, 0.0f);
                    }
                    window.ContentSize = new ImVec2(0.0f, 0.0f);
                    window.ContentSizeIdeal = new ImVec2(0.0f, 0.0f);
                }
            }

            // Select the viewport before any style or font size is read, because a viewport with a different
            // DPI may affect font sizes.
            SelectWindowViewport(window);
            SetCurrentViewport(window, window.Viewport);
            SetCurrentWindow(window);
            flags = window.Flags;

            // Lock the border size and padding for the frame so altering them cannot cause inconsistencies.
            if (!window.DockIsActive && (flags & ImGuiWindowFlags.ChildWindow) != 0)
                window.WindowBorderSize = style.ChildBorderSize;
            else
                window.WindowBorderSize = ((flags & (ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) != 0 && (flags & ImGuiWindowFlags.Modal) == 0)
                    ? style.PopupBorderSize
                    : style.WindowBorderSize;
            window.WindowPadding = style.WindowPadding;
            if (!window.DockIsActive && (flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.Popup) == 0
                && (window.ChildFlags & ImGuiChildFlags.AlwaysUseWindowPadding) == 0 && window.WindowBorderSize == 0.0f)
                window.WindowPadding = new ImVec2(0.0f, (flags & ImGuiWindowFlags.MenuBar) != 0 ? style.WindowPadding.Y : 0.0f);

            // Lock the menu offset so size calculation can use it: menu-bar windows need a minimum size.
            window.DC.MenuBarOffset = new ImVec2(
                MathF.Max(MathF.Max(window.WindowPadding.X, style.ItemSpacing.X), g.NextWindowData.MenuBarOffsetMinVal.X),
                g.NextWindowData.MenuBarOffsetMinVal.Y);
            window.TitleBarHeight = (flags & ImGuiWindowFlags.NoTitleBar) != 0 ? 0.0f : g.FontSize + style.FramePadding.Y * 2.0f;
            window.MenuBarHeight = (flags & ImGuiWindowFlags.MenuBar) != 0 ? window.DC.MenuBarOffset.Y + g.FontSize + style.FramePadding.Y * 2.0f : 0.0f;
            window.FontRefSize = g.FontSize; // Lock this to discourage calling window->CalcFontSize() outside of the current window.

            // Decide whether the current or the previous size is compared against the contents when
            // determining scrollbar visibility. These flags are amended further below.
            bool useCurrentSizeForScrollbarX = windowJustCreated;
            bool useCurrentSizeForScrollbarY = windowJustCreated;
            if (windowSizeXSetByApi && window.ContentSizeExplicit.X != 0.0f)
                useCurrentSizeForScrollbarX = true;
            if (windowSizeYSetByApi && window.ContentSizeExplicit.Y != 0.0f) // #7252
                useCurrentSizeForScrollbarY = true;

            // Collapse the window by double-clicking the title bar. No clip rectangle exists yet, so the
            // title bar rectangle is used directly for hit detection.
            if ((flags & ImGuiWindowFlags.NoTitleBar) == 0 && (flags & ImGuiWindowFlags.NoCollapse) == 0 && !window.DockIsActive)
            {
                // This local is named distinctly from the later title-bar rectangle declared at method
                // scope, because C# rejects a nested local whose name is declared in an enclosing scope.
                ImRect collapseTitleBarRect = window.TitleBarRect();
                if (g.HoveredWindow == window && g.HoveredId == 0 && g.HoveredIdPreviousFrame == 0 && g.ActiveId == 0
                    && IsMouseHoveringRect(collapseTitleBarRect.Min, collapseTitleBarRect.Max))
                {
                    if (g.IO.MouseClickedCount[0] == 2 && GetKeyOwner(ImGuiKey.MouseLeft) == ImGuiKeyOwner_NoOwner)
                    {
                        window.WantCollapseToggle = true;
                        SetKeyOwner(ImGuiKey.MouseLeft, window.MoveId); // Claim input the same way ButtonBehavior() does, preventing a same-frame move from triggering other items (#9439).
                    }
                }
                if (window.WantCollapseToggle)
                {
                    window.Collapsed = !window.Collapsed;
                    if (!window.Collapsed)
                        useCurrentSizeForScrollbarY = true;
                    // The collapsed state is persisted, so toggling it marks the settings dirty.
                    MarkIniSettingsDirty(window);
                }
            }
            else
            {
                window.Collapsed = false;
            }
            window.WantCollapseToggle = false;

            // Outer decoration sizes. ScrollbarSize is cleared immediately because CalcWindowAutoFitSize()
            // reads it and may be called from other locations.
            ImVec2 scrollbarSizesFromLastFrame = window.ScrollbarSizes;
            window.DecoOuterSizeX1 = 0.0f;
            window.DecoOuterSizeX2 = 0.0f;
            window.DecoOuterSizeY1 = window.TitleBarHeight + window.MenuBarHeight;
            window.DecoOuterSizeY2 = 0.0f;
            window.ScrollbarSizes = new ImVec2(0.0f, 0.0f);

            // Calculate the auto-fit size and handle automatic resizing. SetNextWindowSize() overrides
            // AlwaysAutoResize; initial auto-fit still runs on collapsed windows to obtain a width.
            {
                bool sizeAutoFitXAlways = !windowSizeXSetByApi && (flags & ImGuiWindowFlags.AlwaysAutoResize) != 0 && !window.Collapsed;
                bool sizeAutoFitYAlways = !windowSizeYSetByApi && (flags & ImGuiWindowFlags.AlwaysAutoResize) != 0 && !window.Collapsed;
                bool sizeAutoFitXCurrent = !windowSizeXSetByApi && window.AutoFitFramesX > 0;
                bool sizeAutoFitYCurrent = !windowSizeYSetByApi && window.AutoFitFramesY > 0;
                int sizeAutoFitMask = 0;
                if (sizeAutoFitXAlways || sizeAutoFitXCurrent)
                    sizeAutoFitMask |= 1 << (int)ImGuiAxis.X;
                if (sizeAutoFitYAlways || sizeAutoFitYCurrent)
                    sizeAutoFitMask |= 1 << (int)ImGuiAxis.Y;
                ImVec2 sizeAutoFit = CalcWindowAutoFitSize(window, window.ContentSizeIdeal, sizeAutoFitMask);

                ImVec2 oldSize = window.SizeFull;
                if (sizeAutoFitXAlways || sizeAutoFitXCurrent)
                {
                    window.SizeFull = new ImVec2(
                        sizeAutoFitXAlways ? sizeAutoFit.X : (window.AutoFitOnlyGrows ? MathF.Max(window.SizeFull.X, sizeAutoFit.X) : sizeAutoFit.X),
                        window.SizeFull.Y);
                    useCurrentSizeForScrollbarX = true;
                }
                if (sizeAutoFitYAlways || sizeAutoFitYCurrent)
                {
                    window.SizeFull = new ImVec2(
                        window.SizeFull.X,
                        sizeAutoFitYAlways ? sizeAutoFit.Y : (window.AutoFitOnlyGrows ? MathF.Max(window.SizeFull.Y, sizeAutoFit.Y) : sizeAutoFit.Y));
                    useCurrentSizeForScrollbarY = true;
                }
                if (oldSize.X != window.SizeFull.X || oldSize.Y != window.SizeFull.Y)
                {
                    // Auto-fit changed the persisted size.
                    MarkIniSettingsDirty(window);
                }
            }

            // Apply the minimum and maximum size constraints and the final size.
            window.SizeFull = CalcWindowSizeAfterConstraint(window, window.SizeFull);
            window.Size = window.Collapsed && (flags & ImGuiWindowFlags.ChildWindow) == 0 ? window.TitleBarRect().GetSize() : window.SizeFull;

            // Popups latch their initial position and position themselves when they appear next frame.
            if (windowJustActivatedByUser)
            {
                window.AutoPosLastDirection = ImGuiDir.None;
                if ((flags & ImGuiWindowFlags.Popup) != 0 && (flags & ImGuiWindowFlags.Modal) == 0 && !windowPosSetByApi)
                    window.Pos = g.BeginPopupStack[^1].OpenPopupPos;
            }

            // Position child windows.
            if ((flags & ImGuiWindowFlags.ChildWindow) != 0)
            {
                ImAssert.Check(parentWindow != null && parentWindow.Active);
                window.BeginOrderWithinParent = (short)parentWindow!.DC.ChildWindows.Count;
                parentWindow.DC.ChildWindows.Add(window);
                if ((flags & ImGuiWindowFlags.Popup) == 0 && !windowPosSetByApi && !windowIsChildTooltip)
                    window.Pos = parentWindow.DC.CursorPos;
            }

            bool windowPosWithPivot = window.SetWindowPosVal.X != float.MaxValue && window.HiddenFramesCannotSkipItems == 0;
            if (windowPosWithPivot)
            {
                SetWindowPos(window, window.SetWindowPosVal - window.Size * window.SetWindowPosPivot, 0); // Position given a pivot, e.g. for centering.
            }
            else if ((flags & ImGuiWindowFlags.ChildMenu) != 0)
            {
                SetWindowPos(window, FindBestWindowPosForPopup(window), 0);
            }
            else if ((flags & ImGuiWindowFlags.Popup) != 0 && !windowPosSetByApi && windowJustAppearingAfterHiddenForResize)
            {
                SetWindowPos(window, FindBestWindowPosForPopup(window), 0);
            }
            else if ((flags & ImGuiWindowFlags.Tooltip) != 0 && !windowPosSetByApi && !windowIsChildTooltip)
            {
                SetWindowPos(window, FindBestWindowPosForPopup(window), 0);
            }

            // Automatic window detachment (canonical AddUpdateViewport and WindowSyncOwnedViewport) is
            // not implemented, so every window is hosted by the main viewport and the owned-viewport
            // synchronization is inert. The monitor clamping branch below is still ported and runs if a
            // backend or application ever marks a window as viewport-owned.

            // Calculate the allowed position range that keeps the window visible inside safe-area padding.
            ImAssert.Check(window.Viewport != null);
            ImRect viewportRect = window.Viewport!.GetMainRect();
            ImRect viewportWorkRect = window.Viewport!.GetWorkRect();
            ImVec2 visibilityPadding = MaxVector(style.DisplayWindowPadding, style.DisplaySafeAreaPadding);
            ImRect visibilityRect = new(viewportWorkRect.Min + visibilityPadding, viewportWorkRect.Max - visibilityPadding);

            // Clamp position and size so the window stays visible within its viewport or monitor. A
            // zero-sized display is ignored so positions are not lost during initialization or minimization.
            if (!windowPosSetByApi && (flags & ImGuiWindowFlags.ChildWindow) == 0)
            {
                if (!window.ViewportOwned && viewportRect.GetWidth() > 0.0f && viewportRect.GetHeight() > 0.0f)
                {
                    ClampWindowPos(window, visibilityRect);
                }
                else if (window.ViewportOwned && g.PlatformIO.Monitors.Count > 0)
                {
                    if (g.MovingWindow != null && window.RootWindowDockTree == g.MovingWindow.RootWindowDockTree)
                    {
                        // While moving, a window may straddle monitors (#7299, #3071).
                        visibilityRect = g.PlatformMonitorsFullWorkRect;
                    }
                    else
                    {
                        // Otherwise keep the window inside the monitor that hosts its viewport. A window
                        // whose monitor disappeared resolves to the fallback monitor.
                        ImGuiPlatformMonitor monitor = GetViewportPlatformMonitor(window.Viewport!);
                        visibilityRect = new ImRect(monitor.WorkPos, monitor.WorkPos + monitor.WorkSize);
                    }
                    ImRect monitorVisibilityRect = visibilityRect;
                    monitorVisibilityRect.Expand(new ImVec2(-visibilityPadding.X, -visibilityPadding.Y));
                    ClampWindowPos(window, monitorVisibilityRect);
                }
            }
            window.Pos = TruncateVector(window.Pos);

            // Lock window rounding for the frame so altering it cannot cause inconsistencies.
            if ((flags & ImGuiWindowFlags.ChildWindow) != 0 && !window.DockIsActive)
                window.WindowRounding = style.ChildRounding;
            else if (window.RootWindowDockTree!.ViewportOwned)
                window.WindowRounding = 0.0f;
            else
                window.WindowRounding = ((flags & ImGuiWindowFlags.Popup) != 0 && (flags & ImGuiWindowFlags.Modal) == 0) ? style.PopupRounding : style.WindowRounding;

            // Apply window focus: newly created and reactivated windows are moved to the front.
            bool wantFocus = false;
            if (windowJustActivatedByUser && (flags & ImGuiWindowFlags.NoFocusOnAppearing) == 0)
            {
                if ((flags & ImGuiWindowFlags.Popup) != 0)
                    wantFocus = true;
                else if ((window.DockIsActive || (flags & ImGuiWindowFlags.ChildWindow) == 0) && (flags & ImGuiWindowFlags.Tooltip) == 0)
                    wantFocus = true;
            }

            // Test tooling: canonical registers the whole window in the item system here
            // (IMGUI_TEST_ENGINE_ITEM_ADD). The test engine is not owned by this chunk.

            // Decide whether this call handles borders and resize grips. SkipItems is not updated yet, so
            // child windows consult the parent to avoid submitting decorations (#8815).
            bool handleBordersAndResizeGrips = (window.DockNodeAsHost != null || !window.DockIsActive);
            if ((flags & ImGuiWindowFlags.ChildWindow) != 0 && parentWindow!.SkipItems)
                handleBordersAndResizeGrips = false;

            // Handle manual resizing: resize grips, borders, and keyboard/gamepad. Child windows are
            // resizable only when both child resize flags are set, because the grip resizes both directions.
            int resizeGripCount;
            if ((flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.Popup) == 0)
                resizeGripCount = (window.ChildFlags & ImGuiChildFlags.ResizeX) != 0 && (window.ChildFlags & ImGuiChildFlags.ResizeY) != 0 ? 1 : 0;
            else
                resizeGripCount = g.IO.ConfigWindowsResizeFromEdges ? 2 : 1; // Allow resizing from the lower-left corner when the mouse cursor provides feedback.

            float resizeGripDrawSize = TruncateToInt(MathF.Max(g.FontSize * 1.10f, window.WindowRounding + 1.0f + g.FontSize * 0.2f));
            uint[] resizeGripCol = new uint[4];
            if (handleBordersAndResizeGrips && !window.Collapsed)
            {
                int autoFitMask = UpdateWindowManualResize(window, out int borderHovered, out int borderHeld, resizeGripCount, resizeGripCol, visibilityRect);
                if (autoFitMask != 0)
                {
                    if ((autoFitMask & (1 << (int)ImGuiAxis.X)) != 0)
                        useCurrentSizeForScrollbarX = true;
                    if ((autoFitMask & (1 << (int)ImGuiAxis.Y)) != 0)
                        useCurrentSizeForScrollbarY = true;
                }
                window.ResizeBorderHovered = (sbyte)borderHovered;
                window.ResizeBorderHeld = (sbyte)borderHeld;
            }
            else
            {
                window.ResizeBorderHovered = -1;
                window.ResizeBorderHeld = -1;
            }

            // Synchronize window to viewport one last time: clamping and manual resize may have changed it.
            if (window.ViewportOwned)
            {
                if (!window.Viewport!.PlatformRequestMove)
                    window.Viewport.Pos = window.Pos;
                if (!window.Viewport.PlatformRequestResize)
                    window.Viewport.Size = window.Size;
                window.Viewport.UpdateWorkRect();
                viewportRect = window.Viewport.GetMainRect();
            }

            // Save the last known viewport position within the window so it can be persisted.
            window.ViewportPos = window.Viewport!.Pos;

            // Update scrollbar visibility based on the size effective during the last frame or the
            // auto-resized size.
            if (!window.Collapsed)
            {
                ImVec2 availSizeFromCurrentFrame = new(window.SizeFull.X, window.SizeFull.Y - (window.DecoOuterSizeY1 + window.DecoOuterSizeY2));
                ImVec2 availSizeFromLastFrame = window.InnerRect.GetSize() + scrollbarSizesFromLastFrame;
                ImVec2 neededSizeFromLastFrame = windowJustCreated ? new ImVec2(0.0f, 0.0f) : window.ContentSize + window.WindowPadding * 2.0f;
                float sizeForScrollbarsX = useCurrentSizeForScrollbarX ? availSizeFromCurrentFrame.X : availSizeFromLastFrame.X;
                float sizeForScrollbarsY = useCurrentSizeForScrollbarY ? availSizeFromCurrentFrame.Y : availSizeFromLastFrame.Y;
                bool scrollbarXPrev = window.ScrollbarX;
                window.ScrollbarY = (flags & ImGuiWindowFlags.AlwaysVerticalScrollbar) != 0
                    || (neededSizeFromLastFrame.Y > sizeForScrollbarsY && (flags & ImGuiWindowFlags.NoScrollbar) == 0);
                window.ScrollbarX = (flags & ImGuiWindowFlags.AlwaysHorizontalScrollbar) != 0
                    || (neededSizeFromLastFrame.X > sizeForScrollbarsX - (window.ScrollbarY ? style.ScrollbarSize : 0.0f)
                        && (flags & ImGuiWindowFlags.NoScrollbar) == 0
                        && (flags & ImGuiWindowFlags.HorizontalScrollbar) != 0);

                // Track toggling ScrollbarX visibility, which indicates a feedback loop, and stabilize by
                // enforcing visibility (#3285, #8488). The history is an 8-bit shift register; half of its
                // bits set (four) enables the stabilizer.
                window.ScrollbarXStabilizeToggledHistory = (byte)(window.ScrollbarXStabilizeToggledHistory << 1);
                window.ScrollbarXStabilizeToggledHistory |= (byte)(scrollbarXPrev != window.ScrollbarX ? 0x01 : 0x00);
                byte toggledHistory = window.ScrollbarXStabilizeToggledHistory;
                int toggledBitCount = 0;
                for (int bit = 0; bit < 8; bit++)
                    toggledBitCount += (toggledHistory >> bit) & 1;
                bool scrollbarXStabilize = toggledHistory != 0 && toggledBitCount >= 4;
                if (scrollbarXStabilize)
                    window.ScrollbarX = true;
                window.ScrollbarXStabilizeEnabled = scrollbarXStabilize;

                if (window.ScrollbarX && !window.ScrollbarY)
                    window.ScrollbarY = neededSizeFromLastFrame.Y > sizeForScrollbarsY - style.ScrollbarSize && (flags & ImGuiWindowFlags.NoScrollbar) == 0;
                window.ScrollbarSizes = new ImVec2(window.ScrollbarY ? style.ScrollbarSize : 0.0f, window.ScrollbarX ? style.ScrollbarSize : 0.0f);

                // Amend the partially filled decoration sizes.
                window.DecoOuterSizeX2 += window.ScrollbarSizes.X;
                window.DecoOuterSizeY2 += window.ScrollbarSizes.Y;
            }

            // Update rectangles that are not affected by scrolling. This runs after the resize grips are
            // processed so the rectangles do not lag by a frame.
            ImRect hostRect = ((flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.Popup) == 0 && !windowIsChildTooltip)
                ? parentWindow!.ClipRect
                : viewportRect;
            ImRect outerRect = window.Rect();
            ImRect titleBarRect = window.TitleBarRect();
            ImRect outerRectClipped = outerRect;
            outerRectClipped.ClipWith(hostRect);
            window.OuterRectClipped = outerRectClipped;

            // Inner rectangle, unaffected by the window border size.
            ImRect innerRect = window.InnerRect;
            innerRect.Min.X = window.Pos.X + window.DecoOuterSizeX1;
            innerRect.Min.Y = window.Pos.Y + window.DecoOuterSizeY1;
            innerRect.Max.X = window.Pos.X + window.Size.X - window.DecoOuterSizeX2;
            innerRect.Max.Y = window.Pos.Y + window.Size.Y - window.DecoOuterSizeY2;
            window.InnerRect = innerRect;

            // Inner clipping rectangle: extends past the work region up to the borders so e.g. selectables
            // can cover that space. The border is drawn centered over the window rectangle, hence the 0.5f.
            float topBorderSize = ((flags & ImGuiWindowFlags.MenuBar) != 0 || (flags & ImGuiWindowFlags.NoTitleBar) == 0) ? style.FrameBorderSize : window.WindowBorderSize;
            ImRect innerClipRect = window.InnerClipRect;
            innerClipRect.Min.X = MathF.Floor(0.5f + window.InnerRect.Min.X + window.WindowBorderSize * 0.5f);
            innerClipRect.Min.Y = MathF.Floor(0.5f + window.InnerRect.Min.Y + topBorderSize * 0.5f);
            innerClipRect.Max.X = MathF.Floor(window.InnerRect.Max.X - window.WindowBorderSize * 0.5f);
            innerClipRect.Max.Y = MathF.Floor(window.InnerRect.Max.Y - window.WindowBorderSize * 0.5f);
            innerClipRect.ClipWithFull(hostRect);
            window.InnerClipRect = innerClipRect;

            // Lock down maximum scrolling. ScrollMax is intentionally ahead of ScrollbarX/ScrollbarY, which
            // use the previous frame's InnerRect, to accommodate right- and bottom-aligned items.
            window.ScrollMax = new ImVec2(
                MathF.Max(0.0f, window.ContentSize.X + window.WindowPadding.X * 2.0f - window.InnerRect.GetWidth()),
                MathF.Max(0.0f, window.ContentSize.Y + window.WindowPadding.Y * 2.0f - window.InnerRect.GetHeight()));

            // Apply scrolling.
            window.Scroll = CalcNextScrollFromScrollTargetAndClamp(window);
            window.ScrollTarget = new ImVec2(float.MaxValue, float.MaxValue);
            window.DecoInnerSizeX1 = 0.0f;
            window.DecoInnerSizeY1 = 0.0f;

            // Setup the draw list and the outer clipping rectangle.
            ImAssert.Check(window.DrawList.CmdBuffer.Count == 1 && window.DrawList.CmdBuffer[0].ElemCount == 0);
            // Canonical pushes the font's atlas texture reference here
            // ImTextureRef carries it for the draw list's texture stack.
            if (g.Font?.OwnerAtlas is ImFontAtlas fontAtlas)
                window.DrawList.PushTexture(new ImTextureRef(fontAtlas.TexID));
            PushClipRect(hostRect.Min, hostRect.Max, false);

            // Child windows can render their decoration in their parent to save a draw call (since 1.71).
            bool isUndockedOrDockedVisible = !window.DockIsActive || window.DockTabIsVisible;
            if (isUndockedOrDockedVisible)
            {
                bool renderDecorationsInParent = false;
                if ((flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.Popup) == 0 && !windowIsChildTooltip)
                {
                    // Only the previous child window is tested for overlap; testing all would be unnecessarily
                    // expensive. This is disabled when the parent has no vertices, a common pattern for
                    // multiple overlapping children.
                    ImGuiWindow? previousChild = parentWindow!.DC.ChildWindows.Count >= 2 ? parentWindow.DC.ChildWindows[^2] : null;
                    bool previousChildOverlapping = previousChild != null && previousChild.Rect().Overlaps(window.Rect());
                    bool parentIsEmpty = parentWindow.DrawList.VtxBuffer.Count == 0;
                    if (window.DrawList.CmdBuffer[^1].ElemCount == 0 && !parentIsEmpty && !previousChildOverlapping)
                        renderDecorationsInParent = true;
                }
                if (renderDecorationsInParent)
                    window.DrawList = parentWindow!.DrawList;

                ImGuiWindow? windowToHighlight = g.NavWindowingTarget != null ? g.NavWindowingTarget : g.NavWindow;
                bool titleBarIsHighlight = wantFocus
                    || (windowToHighlight != null && window.RootWindowForTitleBarHighlight == windowToHighlight.RootWindowForTitleBarHighlight);
                RenderWindowDecorations(window, titleBarRect, titleBarIsHighlight, handleBordersAndResizeGrips, resizeGripCount, resizeGripCol, resizeGripDrawSize);

                if (renderDecorationsInParent)
                    window.DrawList = window.DrawListInst;
            }

            // Update rectangles that are affected by scrolling.
            bool allowScrollbarX = (flags & ImGuiWindowFlags.NoScrollbar) == 0 && (flags & ImGuiWindowFlags.HorizontalScrollbar) != 0;
            bool allowScrollbarY = (flags & ImGuiWindowFlags.NoScrollbar) == 0;
            float workRectSizeX = window.ContentSizeExplicit.X != 0.0f
                ? window.ContentSizeExplicit.X
                : MathF.Max(allowScrollbarX ? window.ContentSize.X : 0.0f, window.Size.X - window.WindowPadding.X * 2.0f - (window.DecoOuterSizeX1 + window.DecoOuterSizeX2));
            float workRectSizeY = window.ContentSizeExplicit.Y != 0.0f
                ? window.ContentSizeExplicit.Y
                : MathF.Max(allowScrollbarY ? window.ContentSize.Y : 0.0f, window.Size.Y - window.WindowPadding.Y * 2.0f - (window.DecoOuterSizeY1 + window.DecoOuterSizeY2));
            ImRect workRect = window.WorkRect;
            workRect.Min.X = TruncateToInt(window.InnerRect.Min.X - window.Scroll.X + MathF.Max(window.WindowPadding.X, window.WindowBorderSize));
            workRect.Min.Y = TruncateToInt(window.InnerRect.Min.Y - window.Scroll.Y + MathF.Max(window.WindowPadding.Y, window.WindowBorderSize));
            workRect.Max.X = workRect.Min.X + workRectSizeX;
            workRect.Max.Y = workRect.Min.Y + workRectSizeY;
            window.WorkRect = workRect;
            window.ParentWorkRect = window.WorkRect;

            // Legacy content region. Canonical notes that ContentRegionRect.Max is partly faulty but some
            // BeginChild() patterns rely on it.
            ImRect contentRegionRect = window.ContentRegionRect;
            contentRegionRect.Min.X = window.Pos.X - window.Scroll.X + window.WindowPadding.X + window.DecoOuterSizeX1;
            contentRegionRect.Min.Y = window.Pos.Y - window.Scroll.Y + window.WindowPadding.Y + window.DecoOuterSizeY1;
            contentRegionRect.Max.X = contentRegionRect.Min.X + (window.ContentSizeExplicit.X != 0.0f
                ? window.ContentSizeExplicit.X
                : window.Size.X - window.WindowPadding.X * 2.0f - (window.DecoOuterSizeX1 + window.DecoOuterSizeX2));
            contentRegionRect.Max.Y = contentRegionRect.Min.Y + (window.ContentSizeExplicit.Y != 0.0f
                ? window.ContentSizeExplicit.Y
                : window.Size.Y - window.WindowPadding.Y * 2.0f - (window.DecoOuterSizeY1 + window.DecoOuterSizeY2));
            window.ContentRegionRect = contentRegionRect;

            // Setup the drawing context. The term "drawing context" is historical: it now holds both
            // transient and persistent per-window layout state.
            window.DC.Indent = new ImVec1(window.DecoOuterSizeX1 + window.WindowPadding.X - window.Scroll.X);
            window.DC.GroupOffset = new ImVec1(0.0f);
            window.DC.ColumnsOffset = new ImVec1(0.0f);

            // Record the loss of precision of CursorStartPos, which can happen with very large scroll
            // amounts. The clipper uses it to compensate.
            double startPosHighpX = (double)window.Pos.X + window.WindowPadding.X - (double)window.Scroll.X + window.DecoOuterSizeX1 + window.DC.ColumnsOffset.X;
            double startPosHighpY = (double)window.Pos.Y + window.WindowPadding.Y - (double)window.Scroll.Y + window.DecoOuterSizeY1;
            window.DC.CursorStartPos = new ImVec2((float)startPosHighpX, (float)startPosHighpY);
            window.DC.CursorStartPosLossyness = new ImVec2(
                (float)(startPosHighpX - window.DC.CursorStartPos.X),
                (float)(startPosHighpY - window.DC.CursorStartPos.Y));
            window.DC.CursorPos = window.DC.CursorStartPos;
            window.DC.CursorPosPrevLine = window.DC.CursorPos;
            window.DC.CursorMaxPos = window.DC.CursorStartPos;
            window.DC.IdealMaxPos = window.DC.CursorStartPos;
            window.DC.CurrLineSize = new ImVec2(0.0f, 0.0f);
            window.DC.PrevLineSize = new ImVec2(0.0f, 0.0f);
            window.DC.CurrLineTextBaseOffset = 0.0f;
            window.DC.PrevLineTextBaseOffset = 0.0f;
            window.DC.IsSameLine = false;
            window.DC.IsSetPos = false;

            window.DC.NavLayerCurrent = ImGuiNavLayer.Main;
            window.DC.NavLayersActiveMask = window.DC.NavLayersActiveMaskNext;
            window.DC.NavLayersActiveMaskNext = 0x00;
            window.DC.NavIsScrollPushableX = true;
            window.DC.NavHideHighlightOneFrame = false;
            window.DC.NavWindowHasScrollY = window.ScrollMax.Y > 0.0f;

            window.DC.MenuBarAppending = false;
            // Canonical measures the menu columns here so a freshly shown menu does not inherit the
            // previous showing's column offsets. The three
            // ImGuiMenuColumns methods are declared as extension methods in ImGuiWidgets.cs because
            // ImGuiInternal.cs declares the type without a partial modifier; the canonical call syntax
            // is preserved.
            window.DC.MenuColumns.Update(style.ItemSpacing.X, windowJustActivatedByUser);
            window.DC.TreeDepth = 0;
            window.DC.TreeHasStackDataDepthMask = 0;
            window.DC.TreeRecordsClippedNodesY2Mask = 0;
            window.DC.ChildWindows.Clear();
            window.DC.StateStorage = window.StateStorage;
            window.DC.CurrentColumns = null;
            window.DC.LayoutType = ImGuiLayoutType.Vertical;
            window.DC.ParentLayoutType = parentWindow != null ? parentWindow.DC.LayoutType : ImGuiLayoutType.Vertical;

            // Default item width: proportional to the window size when the window can be manually resized.
            bool isResizableWidth;
            if ((flags & ImGuiWindowFlags.ChildWindow) != 0 && !window.DockIsActive)
                isResizableWidth = window.Size.X > 0.0f && (window.ChildFlags & (ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AlwaysAutoResize)) == 0;
            else
                isResizableWidth = window.Size.X > 0.0f && (flags & ImGuiWindowFlags.AlwaysAutoResize) == 0;
            window.DC.ItemWidthDefault = isResizableWidth ? TruncateToInt(window.Size.X * 0.65f) : TruncateToInt(g.FontSize * 16.0f);
            window.DC.ItemWidth = window.DC.ItemWidthDefault;
            window.DC.ItemWidthStack.Clear();
            window.DC.TextWrapPos = -1.0f; // Disabled
            window.DC.TextWrapPosStack.Clear();
            if ((flags & ImGuiWindowFlags.Modal) != 0)
            {
                ImVec4 modalDimBg = GetStyleColorVec4(ImGuiCol.ModalWindowDimBg);
                window.DC.ModalDimBgColor = ImColor.Pack(modalDimBg.X, modalDimBg.Y, modalDimBg.Z, modalDimBg.W);
            }

            if (window.AutoFitFramesX > 0)
                window.AutoFitFramesX--;
            if (window.AutoFitFramesY > 0)
                window.AutoFitFramesY--;

            // Clear SetNextWindowXXX data.
            g.NextWindowData.ClearFlags();

            // Apply focus after setting DC.CursorStartPos so the initial navigation reference rectangle can
            // start around there. UnlessBelowModal avoids focusing a window created outside a modal.
            if (wantFocus)
                FocusWindow(window, ImGuiFocusRequestFlags.UnlessBelowModal);
            if (wantFocus && window == g.NavWindow)
                NavInitWindow(window, false);

            // Close requested by a platform window. Canonical additionally requires the window to not be a
            // dock space (Chunk 6), which always holds when no dock node exists.
            if (hasPOpen && window.Viewport!.PlatformRequestClose && !ReferenceEquals(window.Viewport, GetMainViewport()))
            {
                pOpen = false;
                g.NavWindowingToggleLayer = false; // PlatformRequestClose is often mapped to Alt-F4, so Alt is disabled for the menu toggle.
            }

            // Ctrl+C copies the window contents into the clipboard. Canonical records this as experimental
            // because it breaks on nested Begin/End pairs and interacts poorly with some text outputs.
            if (g.IO.ConfigWindowsCopyContentsWithCtrlC)
            {
                if (g.NavWindow != null
                    && g.NavWindow.RootWindow == window
                    && g.ActiveId == 0
                    && Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.C))
                {
                    LogToClipboard(0);
                }
            }

            // Title bar.
            if ((flags & ImGuiWindowFlags.NoTitleBar) == 0 && !window.DockIsActive)
            {
                ImRect insetTitleBarRect = new(
                    titleBarRect.Min.X + window.WindowBorderSize, titleBarRect.Min.Y,
                    titleBarRect.Max.X - window.WindowBorderSize, titleBarRect.Max.Y);
                RenderWindowTitleBarContents(window, insetTitleBarRect, name, hasPOpen, ref pOpen);
            }
            else if ((flags & ImGuiWindowFlags.NoTitleBar) == 0 && window.DockIsActive)
            {
                // A docked window presents its title through the tab bar rather than through its own title
                // bar, so the visible title text is recorded into the capture here instead.
                LogText("{0}\n", FindRenderedTextEnd(window.Name));
            }

            // Clear the hit-test shape every frame.
            window.HitTestHoleSize = new ImVec2ih(0, 0);

            if ((flags & ImGuiWindowFlags.Tooltip) != 0)
                g.TooltipPreviousWindow = window;

            if ((g.IO.ConfigFlags & ImGuiConfigFlags.DockingEnable) != 0)
            {
                if (g.MovingWindow == window && (window.RootWindowDockTree!.Flags & ImGuiWindowFlags.NoDocking) == 0)
                    BeginDockableDragDropSource(window);

                if (g.DragDropActive && (flags & ImGuiWindowFlags.NoDocking) == 0)
                    if (g.MovingWindow == null || g.MovingWindow.RootWindowDockTree != window)
                        if (window == window.RootWindowDockTree && (window.Flags & ImGuiWindowFlags.DockNodeHost) == 0)
                            BeginDockableDragDropTarget(window);
            }

            // Set the default background-click flags at the end so UpdateWindowManualResize() and
            // ClampWindowPos() may use the previous frame's overridden value.
            window.BgClickFlags = (flags & ImGuiWindowFlags.ChildWindow) != 0
                ? parentWindow!.BgClickFlags
                : (g.IO.ConfigWindowsMoveFromTitleBarOnly ? ImGuiWindowBgClickFlags.None : ImGuiWindowBgClickFlags.Move);

            // Fill last-item data from the title bar so IsItemHovered() and IsItemActive() are usable right
            // after Begin(), e.g. to create context menus on the title bar only.
            window.DC.WindowItemStatusFlags = ImGuiItemStatusFlags.None;
            window.DC.WindowItemStatusFlags |= IsMouseHoveringRect(titleBarRect.Min, titleBarRect.Max, false)
                ? ImGuiItemStatusFlags.HoveredRect
                : ImGuiItemStatusFlags.None;
            SetLastItemDataForWindow(window, titleBarRect);

            // Debug tooling: resolve a pending locate request addressed to this window's own identifier or
            // to its move identifier, now that the title bar has been published as the last item.
            if (g.DebugLocateId != 0
                && (window.ID == g.DebugLocateId || window.MoveId == g.DebugLocateId))
            {
                DebugLocateItemResolveWithLastItem();
            }

            // Test tooling: canonical registers the title bar with MoveId here (IMGUI_TEST_ENGINE_ITEM_ADD).
        }
        else
        {
            // Append path. Skip-refresh windows always mark themselves active (inert in this port).
            if (window.SkipRefresh)
            {
                // Canonical SetWindowActiveForSkipRefresh(window) recursively marks non-hidden children
                // active. SkipRefresh is never enabled here.
            }

            SetCurrentViewport(window, window.Viewport);
            SetCurrentWindow(window);
            g.NextWindowData.ClearFlags();
            SetLastItemDataForWindow(window, window.TitleBarRect());
        }

        if ((flags & ImGuiWindowFlags.DockNodeHost) == 0 && !window.SkipRefresh)
            PushClipRect(window.InnerClipRect.Min, window.InnerClipRect.Max, true);

        // Clear the accessed flag last, after PushClipRect set it, so the flag stays false when the default
        // "Debug" window is unused.
        window.WriteAccessed = false;
        window.BeginCount++;

        // Update visibility.
        if (firstBeginOfTheFrame && !window.SkipRefresh)
        {
            // Chunk 6 (docking): canonical flags a docked window that is about to select a tab with a
            // non-zero HiddenFramesCannotSkipItems so that contents can be submitted one frame early. Inert.

            if ((flags & ImGuiWindowFlags.ChildWindow) != 0 && (flags & ImGuiWindowFlags.ChildMenu) == 0)
            {
                // Child windows can be out of sight and have inverted clip rectangles; mark them hidden so
                // commands are skipped earlier. They cannot be collapsed because they have no title bar.
                ImAssert.Check((flags & ImGuiWindowFlags.NoTitleBar) != 0 || window.DockIsActive);
                bool navRequest = (window.ChildFlags & ImGuiChildFlags.NavFlattened) != 0
                    && g.NavAnyRequest && g.NavWindow != null
                    && g.NavWindow.RootWindowForNav == window.RootWindowForNav;
                if (!g.LogEnabled && !navRequest)
                {
                    if (window.OuterRectClipped.Min.X >= window.OuterRectClipped.Max.X || window.OuterRectClipped.Min.Y >= window.OuterRectClipped.Max.Y)
                    {
                        if (window.AutoFitFramesX > 0 || window.AutoFitFramesY > 0)
                            window.HiddenFramesCannotSkipItems = 1;
                        else
                            window.HiddenFramesCanSkipItems = 1;
                    }
                }

                // Hide along with the parent, or when the parent is collapsed.
                if (parentWindow != null && (parentWindow.Collapsed || parentWindow.HiddenFramesCanSkipItems > 0))
                    window.HiddenFramesCanSkipItems = 1;
                if (parentWindow != null && parentWindow.HiddenFramesCannotSkipItems > 0)
                    window.HiddenFramesCannotSkipItems = 1;
            }

            // Do not render when the style alpha is zero at the time of Begin().
            if (style.Alpha <= 0.0f)
                window.HiddenFramesCanSkipItems = 1;

            bool hiddenRegular = window.HiddenFramesCanSkipItems > 0 || window.HiddenFramesCannotSkipItems > 0;
            window.Hidden = hiddenRegular || window.HiddenFramesForRenderOnly > 0;

            // Disable inputs for the requested number of frames.
            if (window.DisableInputsFrames > 0)
            {
                window.DisableInputsFrames--;
                window.Flags |= ImGuiWindowFlags.NoInputs;
            }

            // Update the SkipItems flag, used to early out of all item functions without layout.
            bool skipItems = false;
            if (window.Collapsed || !window.Active || hiddenRegular)
            {
                if (window.AutoFitFramesX <= 0 && window.AutoFitFramesY <= 0 && window.HiddenFramesCannotSkipItems <= 0)
                    skipItems = true;
            }
            window.SkipItems = skipItems;

            // Restore NavLayersActiveMaskNext to the previous value when not visible, so Ctrl+Tab back can
            // use a safe value.
            if (window.SkipItems)
                window.DC.NavLayersActiveMaskNext = window.DC.NavLayersActiveMask;

            if (window.SkipItems && !window.Appearing)
                ImAssert.Check(window.Appearing == false); // Canonical: if (window->SkipItems && !window->Appearing) IM_ASSERT(window->Appearing == false); (#4177)
        }
        else if (firstBeginOfTheFrame)
        {
            // Skip-refresh mode: contents are not laid out. Inert in this port.
            window.SkipItems = true;
        }

        // Debug override of the return value, used to exercise Begin/End and BeginChild/EndChild behavior.
        // The implicit fallback window is excluded so it can always receive commands.
        if (!window.IsFallbackWindow)
        {
            if ((g.IO.ConfigDebugBeginReturnValueOnce && windowJustCreated)
                || (g.IO.ConfigDebugBeginReturnValueLoop && g.DebugBeginReturnValueCullDepth == g.CurrentWindowStack.Count))
            {
                if (window.AutoFitFramesX > 0)
                    window.AutoFitFramesX++;
                if (window.AutoFitFramesY > 0)
                    window.AutoFitFramesY++;
                return false;
            }
        }

        return !window.SkipItems;
    }

    /// <summary>
    /// Ends the current window and restores the parent window as the ambient current window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>End()</c>. Every
    /// <see cref="Begin(string, ImGuiWindowFlags)"/> must be paired with exactly one call.
    /// </para>
    /// <para>
    /// Canonical <c>IM_ASSERT_USER_ERROR</c> checks become <see cref="ErrorLog"/> guarded early returns
    /// because they represent recoverable user errors; the remaining asserts become
    /// <c>ImAssert.Check</c>. Legacy columns (<c>EndColumns</c>), the focus-scope pop, the disabled
    /// override restore for non-root windows, the log finish, and the docking content-size propagation belong
    /// to subsystems that are inert or owned elsewhere, as documented at each site.
    /// </para>
    /// </remarks>
    public static void End()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow window = g.CurrentWindow!;

        // Error checking: End() was called too many times.
        if (g.CurrentWindowStack.Count <= 1 && g.WithinFrameScopeWithImplicitWindow)
        {
            if (g.CurrentWindowStack.Count <= 1)
            {
                ErrorLog("Calling End() too many times!");
                return;
            }
        }
        ImGuiWindowStackData windowStackData = g.CurrentWindowStack[^1];

        // Error checking: End() was called directly on a popup or child window.
        if ((window.Flags & ImGuiWindowFlags.Popup) != 0 && g.WithinEndPopupID != window.ID)
        {
            ErrorLog("Must call EndPopup() and not End()!");
            return;
        }
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0
            && (window.Flags & ImGuiWindowFlags.DockNodeHost) == 0 && !window.DockIsActive
            && g.WithinEndChildID != window.ID)
        {
            ErrorLog("Must call EndChild() and not End()!");
            return;
        }

        // Close anything that is open.
        if (window.DC.CurrentColumns != null)
            EndColumns();
        if ((window.Flags & ImGuiWindowFlags.DockNodeHost) == 0 && !window.SkipRefresh)
            PopClipRect();
        // Pop the focus scope pushed by Begin().
        PopFocusScope();
        if (windowStackData.DisabledOverrideReenable && window.RootWindow == window)
            EndDisabledOverrideReenable();

        if (window.SkipRefresh)
        {
            // Canonical asserts window.DrawList == NULL and rebinds DrawListInst. SkipRefresh is never
            // enabled in this port, so the window has kept its own draw list throughout.
        }

        // Stop logging when this window owns the log scope.
        if (g.LogWindow == window)
            LogFinish();

        if (window.DC.IsSetPos)
            ErrorCheckUsingSetCursorPosToExtendParentBoundaries();

        // Chunk 6 (docking): canonical propagates the content size to the dock node host window here.

        // Pop from the window stack.
        g.LastItemData.CopyFrom(windowStackData.ParentLastItemDataBackup);
        if ((window.Flags & ImGuiWindowFlags.ChildMenu) != 0)
            g.BeginMenuDepth--;
        if ((window.Flags & ImGuiWindowFlags.Popup) != 0)
            g.BeginPopupStack.RemoveAt(g.BeginPopupStack.Count - 1);

        // Error handling and state recovery.
        if (g.IO.ConfigErrorRecovery)
            ErrorRecoveryTryToRecoverWindowState(windowStackData.StackSizesInBegin);

        g.CurrentWindowStack.RemoveAt(g.CurrentWindowStack.Count - 1);
        SetCurrentWindow(g.CurrentWindowStack.Count == 0 ? null : g.CurrentWindowStack[^1].Window);
        if (g.CurrentWindow != null)
            SetCurrentViewport(g.CurrentWindow, g.CurrentWindow.Viewport);
    }

    #endregion

    #region Child windows

    /// <summary>
    /// Begins a child window identified by a string scoped to the current window's identifier stack.
    /// </summary>
    /// <param name="strId">The child identifier, hashed against the current window's identifier stack.</param>
    /// <param name="size">The requested size; a zero axis resolves through the child's auto-resize flags.</param>
    /// <param name="childFlags">Child-window flags controlling borders, resizing, auto-resizing, and navigation flattening.</param>
    /// <param name="windowFlags">Additional window flags for the child.</param>
    /// <returns>True when the child is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// Ports canonical <c>BeginChild(const char*,.)</c>.
    /// </remarks>
    public static bool BeginChild(string strId, ImVec2 size = default, ImGuiChildFlags childFlags = 0, ImGuiWindowFlags windowFlags = 0)
    {
        uint id = GetID(strId);
        return BeginChildEx(strId, id, size, childFlags, windowFlags);
    }

    /// <summary>
    /// Begins a child window identified by a stable numeric identifier.
    /// </summary>
    /// <param name="id">The child identifier.</param>
    /// <param name="size">The requested size; a zero axis resolves through the child's auto-resize flags.</param>
    /// <param name="childFlags">Child-window flags controlling borders, resizing, auto-resizing, and navigation flattening.</param>
    /// <param name="windowFlags">Additional window flags for the child.</param>
    /// <returns>True when the child is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// Ports canonical <c>BeginChild(ImGuiID,.)</c>. The
    /// stable identifier form lets the caller append to the same child from multiple identifier-stack
    /// positions.
    /// </remarks>
    public static bool BeginChild(uint id, ImVec2 size = default, ImGuiChildFlags childFlags = 0, ImGuiWindowFlags windowFlags = 0)
    {
        return BeginChildEx(null, id, size, childFlags, windowFlags);
    }

    /// <summary>
    /// Implements the canonical child-window begin sequence shared by both <c>BeginChild</c> overloads.
    /// </summary>
    /// <param name="name">The child name fragment, or null for the numeric-identifier form. Canonical accepts NULL here.</param>
    /// <param name="id">The child identifier; zero is a programmer error.</param>
    /// <param name="sizeArg">The requested size argument.</param>
    /// <param name="childFlags">Child-window flags.</param>
    /// <param name="windowFlags">Additional window flags.</param>
    /// <returns>True when the child is visible and its contents should be submitted.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>BeginChildEx()</c>. The child window
    /// name is composed as <c>"parent/name_%08X"</c> or <c>"parent/%08X"</c>; the trailing hash works around
    /// child paths that contain multiple <c>###</c> operators.
    /// </para>
    /// <para>
    /// The navigation activation block calls into the navigation subsystem (part 08). The frame-style style
    /// stack is pushed and popped with the public style-stack API owned by part 01.
    /// </para>
    /// </remarks>
    public static bool BeginChildEx(string? name, uint id, ImVec2 sizeArg, ImGuiChildFlags childFlags, ImGuiWindowFlags windowFlags)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow parentWindow = g.CurrentWindow!;
        ImAssert.Check(id != 0);

        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasChildFlags) != 0)
            childFlags = (childFlags | g.NextWindowData.ChildFlagsSet) & ~g.NextWindowData.ChildFlagsClear;

        // Sanity check: it is likely that a caller accidentally passes ImGuiWindowFlags into the child flags.
        const ImGuiChildFlags supportedMask = ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding | ImGuiChildFlags.ResizeX
            | ImGuiChildFlags.ResizeY | ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize
            | ImGuiChildFlags.FrameStyle | ImGuiChildFlags.NavFlattened;
        ImAssert.Check((childFlags & ~supportedMask) == 0, "Illegal ImGuiChildFlags value. Did you pass ImGuiWindowFlags values instead of ImGuiChildFlags?");
        ImAssert.Check((windowFlags & ImGuiWindowFlags.AlwaysAutoResize) == 0, "Cannot specify ImGuiWindowFlags_AlwaysAutoResize for BeginChild(). Use ImGuiChildFlags_AlwaysAutoResize!");
        if ((childFlags & ImGuiChildFlags.AlwaysAutoResize) != 0)
        {
            ImAssert.Check((childFlags & (ImGuiChildFlags.ResizeX | ImGuiChildFlags.ResizeY)) == 0, "Cannot use ImGuiChildFlags_ResizeX or ImGuiChildFlags_ResizeY with ImGuiChildFlags_AlwaysAutoResize!");
            ImAssert.Check((childFlags & (ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY)) != 0, "Must use ImGuiChildFlags_AutoResizeX or ImGuiChildFlags_AutoResizeY with ImGuiChildFlags_AlwaysAutoResize!");
        }

        if ((childFlags & ImGuiChildFlags.AutoResizeX) != 0)
            childFlags &= ~ImGuiChildFlags.ResizeX;
        if ((childFlags & ImGuiChildFlags.AutoResizeY) != 0)
            childFlags &= ~ImGuiChildFlags.ResizeY;

        // Set window flags.
        windowFlags |= ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoDocking;
        windowFlags |= parentWindow.Flags & ImGuiWindowFlags.NoMove; // Inherit the NoMove flag.
        if ((childFlags & (ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize)) != 0)
            windowFlags |= ImGuiWindowFlags.AlwaysAutoResize; // FIXME (canonical): single-axis flags set the always-auto-resize behavior (#9355).
        if ((childFlags & (ImGuiChildFlags.ResizeX | ImGuiChildFlags.ResizeY)) == 0)
            windowFlags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings;

        // Special framed style.
        if ((childFlags & ImGuiChildFlags.FrameStyle) != 0)
        {
            PushStyleColor(ImGuiCol.ChildBg, g.Style.Colors[(int)ImGuiCol.FrameBg]);
            PushStyleVar(ImGuiStyleVar.ChildRounding, g.Style.FrameRounding);
            PushStyleVar(ImGuiStyleVar.ChildBorderSize, g.Style.FrameBorderSize);
            PushStyleVar(ImGuiStyleVar.WindowPadding, g.Style.FramePadding);
            childFlags |= ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding;
            windowFlags |= ImGuiWindowFlags.NoMove;
        }

        // Forward size. Begin() switches the condition to ImGuiCond_FirstUseEver for an axis whose child
        // resize flag is set.
        ImVec2 sizeAvail = GetContentRegionAvail();
        ImVec2 sizeDefault = new(
            (childFlags & ImGuiChildFlags.AutoResizeX) != 0 ? 0.0f : sizeAvail.X,
            (childFlags & ImGuiChildFlags.AutoResizeY) != 0 ? 0.0f : sizeAvail.Y);
        ImVec2 size = CalcItemSize(sizeArg, sizeDefault.X, sizeDefault.Y);

        // A SetNextWindowSize() call always has priority (#8020). Only ImGuiCond_Always is supported on this
        // path because other conditions would require the window pointer.
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSize) != 0 && (g.NextWindowData.SizeCond & ImGuiCond.Always) != 0)
        {
            if (g.NextWindowData.SizeVal.X > 0.0f)
            {
                size.X = g.NextWindowData.SizeVal.X;
                childFlags &= ~ImGuiChildFlags.ResizeX;
            }
            if (g.NextWindowData.SizeVal.Y > 0.0f)
            {
                size.Y = g.NextWindowData.SizeVal.Y;
                childFlags &= ~ImGuiChildFlags.ResizeY;
            }
        }
        SetNextWindowSize(size);

        // Forward child flags: prior settings may be merged, but only additive merging is supported.
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasChildFlags) != 0)
            g.NextWindowData.ChildFlags |= childFlags;
        else
            g.NextWindowData.ChildFlags = childFlags;
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasChildFlags;

        // Build the name. The trailing hash mitigates child paths that contain multiple "###" operators.
        string tempWindowName = name != null
            ? $"{parentWindow.Name}/{name}_{id:X8}"
            : $"{parentWindow.Name}/{id:X8}";

        // Set style.
        float backupBorderSize = g.Style.ChildBorderSize;
        if ((childFlags & ImGuiChildFlags.Borders) == 0)
            g.Style.ChildBorderSize = 0.0f;

        // Begin into the window.
        bool ret = Begin(tempWindowName, windowFlags);

        // Restore style.
        g.Style.ChildBorderSize = backupBorderSize;
        if ((childFlags & ImGuiChildFlags.FrameStyle) != 0)
        {
            PopStyleVar(3);
            PopStyleColor();
        }

        ImGuiWindow childWindow = g.CurrentWindow!;
        childWindow.ChildId = id;

        // Set the cursor to handle the case where the user called SetNextWindowPos() followed by BeginChild().
        if (childWindow.BeginCount == 1)
            parentWindow.DC.CursorPos = childWindow.Pos;

        // Process navigation-in immediately so NavInit can run on the first frame. A child can be entered
        // when it has navigable items or when it can be scrolled.
        uint tempIdForActivation = ImHashStr("##Child", 0, id);
        if (g.ActiveId == tempIdForActivation)
            ClearActiveID();
        if (g.NavActivateId == id && (childFlags & ImGuiChildFlags.NavFlattened) == 0
            && (childWindow.DC.NavLayersActiveMask != 0 || childWindow.DC.NavWindowHasScrollY))
        {
            FocusWindow(childWindow);
            NavInitWindow(childWindow, false);
            SetActiveID(tempIdForActivation, childWindow); // Steal ActiveId with another arbitrary id so a key press does not activate a child item.
            g.ActiveIdSource = g.NavInputSource;
        }
        return ret;
    }

    /// <summary>
    /// Ends the current child window and submits the parent-side item that represents it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>EndChild()</c>. The parent item is
    /// submitted only on the child's first begin of the frame; append calls reuse the last-item data.
    /// </para>
    /// <para>
    /// The navigation cursor rendering (canonical <c>RenderNavCursor</c>) belongs to
    /// the widget layer, which is not owned by any part of this chunk; the item submission and status flags
    /// are ported in full.
    /// </para>
    /// </remarks>
    public static void EndChild()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow childWindow = g.CurrentWindow!;

        uint backupWithinEndChildId = g.WithinEndChildID;
        ImAssert.Check((childWindow.Flags & ImGuiWindowFlags.ChildWindow) != 0); // Mismatched BeginChild()/EndChild() calls.

        g.WithinEndChildID = childWindow.ID;
        ImVec2 childSize = childWindow.Size;
        End();
        if (childWindow.BeginCount == 1)
        {
            ImGuiWindow parentWindow = g.CurrentWindow!;
            ImRect bb = new(parentWindow.DC.CursorPos, parentWindow.DC.CursorPos + childSize);
            ItemSize(childSize);
            bool navFlattened = (childWindow.ChildFlags & ImGuiChildFlags.NavFlattened) != 0;
            if ((childWindow.DC.NavLayersActiveMask != 0 || childWindow.DC.NavWindowHasScrollY) && !navFlattened)
            {
                ItemAdd(bb, childWindow.ChildId);
                // Widget layer: canonical RenderNavCursor(bb, child_window->ChildId) draws the navigation
                // cursor here.
                if (childWindow.DC.NavLayersActiveMask == 0 && childWindow == g.NavWindow)
                {
                    // Widget layer: canonical additionally draws a compact navigation cursor around the
                    // child expanded by two pixels so a scroll-only child keeps a visible highlight.
                }
            }
            else
            {
                // Not navigable into: mostly useful for undecorated, non-scrolling content children.
                ItemAdd(bb, childWindow.ChildId, null, ImGuiItemFlags.NoNav);

                // When flattened the parent directly reaches the child's items, so the active layer mask is
                // adjusted accordingly.
                if (navFlattened)
                    parentWindow.DC.NavLayersActiveMaskNext = (short)(parentWindow.DC.NavLayersActiveMaskNext | childWindow.DC.NavLayersActiveMaskNext);
            }
            if (g.HoveredWindow == childWindow)
                g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredWindow;
            childWindow.DC.ChildItemStatusFlags = g.LastItemData.StatusFlags;
        }
        else
        {
            SetLastItemDataForChildWindowItem(childWindow, childWindow.Rect());
        }

        g.WithinEndChildID = backupWithinEndChildId;
        g.LogLinePosY = -float.MaxValue; // To enforce a carriage return.
    }

    #endregion

    #region Window queries

    /// <summary>
    /// Returns the current window's position in screen coordinates.
    /// </summary>
    /// <returns>The current window position.</returns>
    /// <remarks>Ports canonical <c>GetWindowPos()</c>.</remarks>
    public static ImVec2 GetWindowPos()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow window = g.CurrentWindow!;
        return window.Pos;
    }

    /// <summary>
    /// Returns the current window's size.
    /// </summary>
    /// <returns>The current window size.</returns>
    /// <remarks>Ports canonical <c>GetWindowSize()</c>.</remarks>
    public static ImVec2 GetWindowSize()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        return window!.Size;
    }

    /// <summary>
    /// Returns the current window's width.
    /// </summary>
    /// <returns>The current window width.</returns>
    /// <remarks>Ports canonical <c>GetWindowWidth()</c>., which reads the current window without marking it accessed.</remarks>
    public static float GetWindowWidth()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        return g.CurrentWindow!.Size.X;
    }

    /// <summary>
    /// Returns the current window's height.
    /// </summary>
    /// <returns>The current window height.</returns>
    /// <remarks>Ports canonical <c>GetWindowHeight()</c>.</remarks>
    public static float GetWindowHeight()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        return g.CurrentWindow!.Size.Y;
    }

    /// <summary>
    /// Returns the draw list of the current window.
    /// </summary>
    /// <returns>The current window's draw list. The caller may not retain it past the frame.</returns>
    /// <remarks>Ports canonical <c>GetWindowDrawList()</c>., which marks the current window accessed.</remarks>
    public static ImDrawList GetWindowDrawList()
    {
        ImGuiWindow window = GetCurrentWindow();
        return window.DrawList;
    }

    /// <summary>
    /// Returns the DPI scale of the ambient current viewport.
    /// </summary>
    /// <returns>The current DPI scale, 1.0 for the default density.</returns>
    /// <remarks>Ports canonical <c>GetWindowDpiScale()</c>.</remarks>
    public static float GetWindowDpiScale()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        return g.CurrentDpiScale;
    }

    /// <summary>
    /// Returns whether the current window appeared for the first time this frame.
    /// </summary>
    /// <returns>True when the current window is appearing.</returns>
    /// <remarks>Ports canonical <c>IsWindowAppearing()</c>.</remarks>
    public static bool IsWindowAppearing()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        return window!.Appearing;
    }

    /// <summary>
    /// Returns whether the current window is collapsed.
    /// </summary>
    /// <returns>True when the current window is collapsed.</returns>
    /// <remarks>Ports canonical <c>IsWindowCollapsed()</c>.</remarks>
    public static bool IsWindowCollapsed()
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        return window!.Collapsed;
    }

    /// <summary>
    /// Resolves the combined root of a window, optionally following the popup and dock hierarchies.
    /// </summary>
    /// <param name="window">The window to resolve.</param>
    /// <param name="popupHierarchy">True to follow <c>RootWindowPopupTree</c> links.</param>
    /// <param name="dockHierarchy">True to follow <c>RootWindowDockTree</c> links.</param>
    /// <returns>The combined root window.</returns>
    /// <remarks>
    /// Ports canonical file-scope <c>GetCombinedRootWindow()</c>
    ///. It is the query-side counterpart of
    /// <see cref="UpdateWindowParentAndRootLinks"/>.
    /// </remarks>
    private static ImGuiWindow GetCombinedRootWindow(ImGuiWindow window, bool popupHierarchy, bool dockHierarchy)
    {
        ImGuiWindow? lastWindow = null;
        while (!ReferenceEquals(lastWindow, window))
        {
            lastWindow = window;
            window = window.RootWindow!;
            if (popupHierarchy)
                window = window.RootWindowPopupTree!;
            if (dockHierarchy)
                window = window.RootWindowDockTree!;
        }
        return window;
    }

    /// <summary>
    /// Returns whether a window belongs to the parent chain of another window.
    /// </summary>
    /// <param name="window">The window to test.</param>
    /// <param name="potentialParent">The candidate ancestor.</param>
    /// <param name="popupHierarchy">True to follow popup-tree root links.</param>
    /// <param name="dockHierarchy">True to follow dock-tree root links.</param>
    /// <returns>True when <paramref name="window"/> is <paramref name="potentialParent"/> or one of its descendants.</returns>
    /// <remarks>Ports canonical <c>IsWindowChildOf()</c>.</remarks>
    internal static bool IsWindowChildOf(ImGuiWindow window, ImGuiWindow potentialParent, bool popupHierarchy = false, bool dockHierarchy = false)
    {
        ImGuiWindow windowRoot = GetCombinedRootWindow(window, popupHierarchy, dockHierarchy);
        if (ReferenceEquals(windowRoot, potentialParent))
            return true;
        ImGuiWindow? current = window;
        while (current != null)
        {
            if (ReferenceEquals(current, potentialParent))
                return true;
            if (ReferenceEquals(current, windowRoot)) // End of chain.
                return false;
            current = current.ParentWindow;
        }
        return false;
    }

    /// <summary>
    /// Returns whether a window is present in the current window stack.
    /// </summary>
    /// <param name="window">The window to test.</param>
    /// <returns>True when the window is currently open through <see cref="Begin(string, ImGuiWindowFlags)"/>.</returns>
    /// <remarks>Ports canonical <c>IsWindowInBeginStack()</c>.</remarks>
    internal static bool IsWindowInBeginStack(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        for (int n = g.CurrentWindowStack.Count - 1; n >= 0; n--)
            if (ReferenceEquals(g.CurrentWindowStack[n].Window, window))
                return true;
        return false;
    }

    /// <summary>
    /// Returns whether a window is within the begin stack of another window.
    /// </summary>
    /// <param name="window">The window to test.</param>
    /// <param name="potentialParent">The candidate begin-stack ancestor.</param>
    /// <returns>True when the window's root is the candidate or the begin-stack chain reaches it.</returns>
    /// <remarks>
    /// Ports canonical <c>IsWindowWithinBeginStackOf()</c>.
    /// Unlike <see cref="IsWindowChildOf"/> this walks the begin-stack parent chain, which may differ from the
    /// window parent chain across append calls.
    /// </remarks>
    internal static bool IsWindowWithinBeginStackOf(ImGuiWindow window, ImGuiWindow potentialParent)
    {
        if (ReferenceEquals(window.RootWindow, potentialParent))
            return true;
        ImGuiWindow? current = window;
        while (current != null)
        {
            if (ReferenceEquals(current, potentialParent))
                return true;
            current = current.ParentWindowInBeginStack;
        }
        return false;
    }

    /// <summary>
    /// Returns whether one window is displayed above another.
    /// </summary>
    /// <param name="potentialAbove">The window tested for being in front.</param>
    /// <param name="potentialBelow">The window tested for being behind.</param>
    /// <returns>True when <paramref name="potentialAbove"/> is drawn above <paramref name="potentialBelow"/>.</returns>
    /// <remarks>
    /// Ports canonical <c>IsWindowAbove()</c>. The display
    /// layer is compared first, so a tooltip wins over any regular window; otherwise the display order array
    /// is scanned from front to back.
    /// </remarks>
    internal static bool IsWindowAbove(ImGuiWindow potentialAbove, ImGuiWindow potentialBelow)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        int displayLayerDelta = GetWindowDisplayLayer(potentialAbove) - GetWindowDisplayLayer(potentialBelow);
        if (displayLayerDelta != 0)
            return displayLayerDelta > 0;
        for (int i = g.Windows.Count - 1; i >= 0; i--)
        {
            ImGuiWindow candidateWindow = g.Windows[i];
            if (ReferenceEquals(candidateWindow, potentialAbove))
                return true;
            if (ReferenceEquals(candidateWindow, potentialBelow))
                return false;
        }
        return false;
    }

    /// <summary>
    /// Returns whether the current window is focused, optionally considering child windows.
    /// </summary>
    /// <param name="flags">Focus query flags.</param>
    /// <returns>True when the current window satisfies the focus query.</returns>
    /// <remarks>
    /// Ports canonical <c>IsWindowFocused()</c>. A null
    /// focused window yields false; <c>AnyWindow</c> yields true before the current window is examined.
    /// </remarks>
    public static bool IsWindowFocused(ImGuiFocusedFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow? refWindow = g.NavWindow;
        ImGuiWindow? curWindow = g.CurrentWindow;
        if (refWindow == null)
            return false;
        if ((flags & ImGuiFocusedFlags.AnyWindow) != 0)
            return true;

        ImAssert.Check(curWindow != null); // Not inside a Begin()/End().
        bool popupHierarchy = (flags & ImGuiFocusedFlags.NoPopupHierarchy) == 0;
        bool dockHierarchy = (flags & ImGuiFocusedFlags.DockHierarchy) != 0;
        if ((flags & ImGuiFocusedFlags.RootWindow) != 0)
            curWindow = GetCombinedRootWindow(curWindow!, popupHierarchy, dockHierarchy);

        if ((flags & ImGuiFocusedFlags.ChildWindows) != 0)
            return IsWindowChildOf(refWindow, curWindow!, popupHierarchy, dockHierarchy);
        return ReferenceEquals(refWindow, curWindow);
    }

    /// <summary>
    /// Returns whether the current window is hovered and hoverable, for example not blocked by a popup or a
    /// modal.
    /// </summary>
    /// <param name="flags">Hover query flags.</param>
    /// <returns>True when the current window satisfies the hover query.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>IsWindowHovered()</c>. This reports
    /// whether a window is hovered; it must not be used to decide mouse dispatch, which is
    /// <c>io.WantCaptureMouse</c>.
    /// </para>
    /// <para>
    /// Canonical remaps the delay bits through <c>ApplyHoverFlagsForTooltip()</c>; the expression is inlined
    /// here (as part 04 does at its two call sites) so that no two private members with that name exist in
    /// the assembled class.
    /// </para>
    /// </remarks>
    public static bool IsWindowHovered(ImGuiHoveredFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiHoveredFlags allowed = (ImGuiHoveredFlags)ImGuiHoveredFlagsPrivate.ImGuiHoveredFlags_AllowedMaskForIsWindowHovered;
        if ((flags & ~allowed) != 0)
        {
            ErrorLog("Invalid flags for IsWindowHovered()!");
            return false;
        }

        ImGuiWindow? refWindow = g.HoveredWindow;
        ImGuiWindow? curWindow = g.CurrentWindow;
        if (refWindow == null)
            return false;

        if ((flags & ImGuiHoveredFlags.AnyWindow) == 0)
        {
            ImAssert.Check(curWindow != null); // Not inside a Begin()/End().
            bool popupHierarchy = (flags & ImGuiHoveredFlags.NoPopupHierarchy) == 0;
            bool dockHierarchy = (flags & ImGuiHoveredFlags.DockHierarchy) != 0;
            if ((flags & ImGuiHoveredFlags.RootWindow) != 0)
                curWindow = GetCombinedRootWindow(curWindow!, popupHierarchy, dockHierarchy);

            bool result = (flags & ImGuiHoveredFlags.ChildWindows) != 0
                ? IsWindowChildOf(refWindow, curWindow!, popupHierarchy, dockHierarchy)
                : ReferenceEquals(refWindow, curWindow);
            if (!result)
                return false;
        }

        if (!IsWindowContentHoverable(refWindow, flags))
            return false;
        if ((flags & ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) == 0)
        {
            if (g.ActiveId != 0 && !g.ActiveIdAllowOverlap && g.ActiveId != refWindow.MoveId)
                return false;
        }

        // When the hovered window changes, a stationary delay is required before the hover timer activates.
        // Inlined canonical ApplyHoverFlagsForTooltip(flags, g.Style.HoverFlagsForTooltipMouse): instance
        // delay flags override the shared ones.
        if ((flags & ImGuiHoveredFlags.ForTooltip) != 0)
        {
            ImGuiHoveredFlags sharedFlags = g.Style.HoverFlagsForTooltipMouse;
            if ((flags & (ImGuiHoveredFlags.DelayNone | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.DelayNormal)) != 0)
                sharedFlags &= ~(ImGuiHoveredFlags.DelayNone | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.DelayNormal);
            flags |= sharedFlags;
        }
        if ((flags & ImGuiHoveredFlags.Stationary) != 0 && g.HoverWindowUnlockedStationaryId != refWindow.ID)
            return false;

        return true;
    }

    /// <summary>
    /// Returns whether a window can receive navigation focus.
    /// </summary>
    /// <param name="window">The window to test.</param>
    /// <returns>True when the window was active, is a root window, and does not disable navigation focus.</returns>
    /// <remarks>Ports canonical <c>IsWindowNavFocusable()</c>.</remarks>
    internal static bool IsWindowNavFocusable(ImGuiWindow window)
    {
        return window.WasActive && ReferenceEquals(window, window.RootWindow) && (window.Flags & ImGuiWindowFlags.NoNavFocus) == 0;
    }

    #endregion

    #region Content region

    /// <summary>
    /// Returns the size of the region available to the next item before scrolling or clipping occurs.
    /// </summary>
    /// <returns>The remaining content-region size.</returns>
    /// <remarks>
    /// Ports canonical <c>GetContentRegionAvail()</c>. While a
    /// table or a legacy column set is current the table work rectangle defines the content bound;
    /// otherwise the window content region is used.
    /// </remarks>
    public static ImVec2 GetContentRegionAvail()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow window = g.CurrentWindow!;
        ImVec2 mx = (window.DC.CurrentColumns != null || g.CurrentTable != null) ? window.WorkRect.Max : window.ContentRegionRect.Max;
        return mx - window.DC.CursorPos;
    }

    /// <summary>
    /// Returns the position of the content region's right and bottom edges in window-local coordinates.
    /// </summary>
    /// <returns>The content-region maximum in local coordinates.</returns>
    /// <remarks>
    /// Ports canonical obsolete <c>GetContentRegionMax()</c>
    ///. New code should use
    /// <see cref="GetCursorScreenPos"/> with <see cref="GetContentRegionAvail"/> instead.
    /// </remarks>
    public static ImVec2 GetContentRegionMax()
    {
        return GetContentRegionAvail() + GetCursorScreenPos() - GetWindowPos();
    }

    /// <summary>
    /// Returns the minimum corner of the content region in window-local coordinates.
    /// </summary>
    /// <returns>The content-region minimum in local coordinates.</returns>
    /// <remarks>
    /// Ports canonical obsolete <c>GetWindowContentRegionMin()</c>
    /// </remarks>
    public static ImVec2 GetWindowContentRegionMin()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow window = g.CurrentWindow!;
        return window.ContentRegionRect.Min - window.Pos;
    }

    /// <summary>
    /// Returns the maximum corner of the content region in window-local coordinates.
    /// </summary>
    /// <returns>The content-region maximum in local coordinates.</returns>
    /// <remarks>
    /// Ports canonical obsolete <c>GetWindowContentRegionMax()</c>
    /// </remarks>
    public static ImVec2 GetWindowContentRegionMax()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow window = g.CurrentWindow!;
        return window.ContentRegionRect.Max - window.Pos;
    }

    #endregion

    #region Window property setters

    /// <summary>
    /// Moves a window when its condition gate permits it.
    /// </summary>
    /// <param name="window">The window to move.</param>
    /// <param name="pos">The requested position in screen coordinates.</param>
    /// <param name="cond">The condition that gates the move; zero means unconditional.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowPos(ImGuiWindow*,.)</c>
    ///. Moving a window offsets the drawing context so an
    /// in-progress layout is not smeared, and the content-size accumulation is not corrupted.
    /// </remarks>
    internal static void SetWindowPos(ImGuiWindow window, ImVec2 pos, ImGuiCond cond = 0)
    {
        // Test the condition and clear the flags for next time.
        if (cond != 0 && (window.SetWindowPosAllowFlags & cond) == 0)
            return;
        ImAssert.Check(cond == 0 || (((int)cond & ((int)cond - 1)) == 0)); // Do not combine multiple condition flags.
        window.SetWindowPosAllowFlags &= ~(ImGuiCond.Once | ImGuiCond.FirstUseEver | ImGuiCond.Appearing);
        window.SetWindowPosVal = new ImVec2(float.MaxValue, float.MaxValue);

        ImVec2 oldPos = window.Pos;
        window.Pos = TruncateVector(pos);
        ImVec2 offset = window.Pos - oldPos;
        if (offset.X == 0.0f && offset.Y == 0.0f)
            return;
        // Moving a window changes its persisted position, so mark the settings dirty.
        MarkIniSettingsDirty(window);
        window.DC.CursorPos += offset;
        window.DC.CursorMaxPos += offset;
        window.DC.IdealMaxPos += offset;
        window.DC.CursorStartPos += offset;
    }

    /// <summary>
    /// Moves the current window when its condition gate permits it.
    /// </summary>
    /// <param name="pos">The requested position in screen coordinates.</param>
    /// <param name="cond">The condition that gates the move; zero means unconditional.</param>
    /// <remarks>Ports canonical <c>SetWindowPos(const ImVec2&amp;,.)</c>.</remarks>
    public static void SetWindowPos(ImVec2 pos, ImGuiCond cond = 0)
    {
        ImGuiWindow? window = GetCurrentWindowRead();
        SetWindowPos(window!, pos, cond);
    }

    /// <summary>
    /// Moves the named window when its condition gate permits it.
    /// </summary>
    /// <param name="name">The name of the window to move.</param>
    /// <param name="pos">The requested position in screen coordinates.</param>
    /// <param name="cond">The condition that gates the move; zero means unconditional.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowPos(const char*,.)</c>. A
    /// name that resolves to no window is ignored.
    /// </remarks>
    public static void SetWindowPos(string name, ImVec2 pos, ImGuiCond cond = 0)
    {
        ImGuiWindow? window = FindWindowByName(name);
        if (window != null)
            SetWindowPos(window, pos, cond);
    }

    /// <summary>
    /// Resizes a window when its condition gate permits it.
    /// </summary>
    /// <param name="window">The window to resize.</param>
    /// <param name="size">The requested size; a non-positive axis enables auto-fitting on that axis.</param>
    /// <param name="cond">The condition that gates the resize; zero means unconditional.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowSize(ImGuiWindow*,.)</c>
    ///. Auto-fit is enabled for non-child windows, appearing
    /// children, and children with <c>ImGuiChildFlags_AlwaysAutoResize</c>.
    /// </remarks>
    internal static void SetWindowSize(ImGuiWindow window, ImVec2 size, ImGuiCond cond = 0)
    {
        // Test the condition and clear the flags for next time.
        if (cond != 0 && (window.SetWindowSizeAllowFlags & cond) == 0)
            return;
        ImAssert.Check(cond == 0 || (((int)cond & ((int)cond - 1)) == 0)); // Do not combine multiple condition flags.
        window.SetWindowSizeAllowFlags &= ~(ImGuiCond.Once | ImGuiCond.FirstUseEver | ImGuiCond.Appearing);

        // Enable auto-fit. Canonical repeats this test once per axis; the condition is identical.
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) == 0 || window.Appearing || (window.ChildFlags & ImGuiChildFlags.AlwaysAutoResize) != 0)
        {
            window.AutoFitFramesX = (sbyte)(size.X <= 0.0f ? 2 : 0);
            window.AutoFitFramesY = (sbyte)(size.Y <= 0.0f ? 2 : 0);
        }

        ImVec2 oldSize = window.SizeFull;
        if (size.X <= 0.0f)
        {
            window.AutoFitOnlyGrows = false;
        }
        else
        {
            window.SizeFull = new ImVec2(TruncateToInt(size.X), window.SizeFull.Y);
        }
        if (size.Y <= 0.0f)
        {
            window.AutoFitOnlyGrows = false;
        }
        else
        {
            window.SizeFull = new ImVec2(window.SizeFull.X, TruncateToInt(size.Y));
        }
        if (oldSize.X != window.SizeFull.X || oldSize.Y != window.SizeFull.Y)
        {
            // An explicit size assignment must be persisted.
            MarkIniSettingsDirty(window);
        }
    }

    /// <summary>
    /// Resizes the current window when its condition gate permits it.
    /// </summary>
    /// <param name="size">The requested size.</param>
    /// <param name="cond">The condition that gates the resize; zero means unconditional.</param>
    /// <remarks>Ports canonical <c>SetWindowSize(const ImVec2&amp;,.)</c>.</remarks>
    public static void SetWindowSize(ImVec2 size, ImGuiCond cond = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        SetWindowSize(g.CurrentWindow!, size, cond);
    }

    /// <summary>
    /// Resizes the named window when its condition gate permits it.
    /// </summary>
    /// <param name="name">The name of the window to resize.</param>
    /// <param name="size">The requested size.</param>
    /// <param name="cond">The condition that gates the resize; zero means unconditional.</param>
    /// <remarks>Ports canonical <c>SetWindowSize(const char*,.)</c>.</remarks>
    public static void SetWindowSize(string name, ImVec2 size, ImGuiCond cond = 0)
    {
        ImGuiWindow? window = FindWindowByName(name);
        if (window != null)
            SetWindowSize(window, size, cond);
    }

    /// <summary>
    /// Queues a collapsed-state change for a window.
    /// </summary>
    /// <param name="window">The window whose collapsed state is queued.</param>
    /// <param name="collapsed">The desired collapsed state.</param>
    /// <param name="cond">The condition that gates the change; zero means unconditional.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowCollapsed(ImGuiWindow*,.)</c>
    ///. The change is queued through
    /// <c>window.WantCollapseToggle</c> and applied by <see cref="BeginCore"/> so that collapsing does not
    /// disturb the layout pass that requested it.
    /// </remarks>
    internal static void SetWindowCollapsed(ImGuiWindow window, bool collapsed, ImGuiCond cond = 0)
    {
        // Test the condition and clear the flags for next time.
        if (cond != 0 && (window.SetWindowCollapsedAllowFlags & cond) == 0)
            return;
        window.SetWindowCollapsedAllowFlags &= ~(ImGuiCond.Once | ImGuiCond.FirstUseEver | ImGuiCond.Appearing);

        // Queue applying in Begin().
        if (window.WantCollapseToggle)
            window.Collapsed = !window.Collapsed;
        window.WantCollapseToggle = window.Collapsed != collapsed;
    }

    /// <summary>
    /// Queues a collapsed-state change for the current window.
    /// </summary>
    /// <param name="collapsed">The desired collapsed state.</param>
    /// <param name="cond">The condition that gates the change; zero means unconditional.</param>
    /// <remarks>Ports canonical <c>SetWindowCollapsed(bool,.)</c>.</remarks>
    public static void SetWindowCollapsed(bool collapsed, ImGuiCond cond = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        SetWindowCollapsed(g.CurrentWindow!, collapsed, cond);
    }

    /// <summary>
    /// Queues a collapsed-state change for the named window.
    /// </summary>
    /// <param name="name">The name of the window to collapse or expand.</param>
    /// <param name="collapsed">The desired collapsed state.</param>
    /// <param name="cond">The condition that gates the change; zero means unconditional.</param>
    /// <remarks>Ports canonical <c>SetWindowCollapsed(const char*,.)</c>.</remarks>
    public static void SetWindowCollapsed(string name, bool collapsed, ImGuiCond cond = 0)
    {
        ImGuiWindow? window = FindWindowByName(name);
        if (window != null)
            SetWindowCollapsed(window, collapsed, cond);
    }

    /// <summary>
    /// Declares a rectangular hole in a window for hit-testing so the platform window can be dragged or
    /// resized through it.
    /// </summary>
    /// <param name="window">The window owning the hole.</param>
    /// <param name="pos">The hole position in screen coordinates.</param>
    /// <param name="size">The hole size.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowHitTestHole()</c>. Multiple
    /// holes per window are not supported and are a programmer error.
    /// </remarks>
    internal static void SetWindowHitTestHole(ImGuiWindow window, ImVec2 pos, ImVec2 size)
    {
        ImAssert.Check(window.HitTestHoleSize.X == 0); // Multiple holes and hit-test filters are not supported.
        window.HitTestHoleSize = new ImVec2ih(size);
        window.HitTestHoleOffset = new ImVec2ih(pos - window.Pos);
    }

    /// <summary>
    /// Marks a window hidden and instructs item submission to be skipped for the current frame.
    /// </summary>
    /// <param name="window">The window to hide.</param>
    /// <remarks>Ports canonical <c>SetWindowHiddenAndSkipItemsForCurrentFrame()</c>.</remarks>
    internal static void SetWindowHiddenAndSkipItemsForCurrentFrame(ImGuiWindow window)
    {
        window.Hidden = true;
        window.SkipItems = true;
        window.HiddenFramesCanSkipItems = 1;
    }

    /// <summary>
    /// Overwrites a window's clip rectangle without going through the clip-rectangle stack, so a subsequent
    /// channel switch applies it only once.
    /// </summary>
    /// <param name="window">The window whose clip rectangle is overwritten.</param>
    /// <param name="clipRect">The new clip rectangle.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetWindowClipRectBeforeSetChannel()</c>., which
    /// the table subsystem uses to avoid an extraneous command-buffer update between a clip change and a
    /// channel switch. Canonical also overwrites the top of the draw list's clip-rectangle stack; that stack
    /// is private in this port, and <c>ImDrawList.PushClipRect</c>/<c>PopClipRect</c> recompute the command
    /// header from it, so only the logical rectangle and the active command header are overwritten here.
    /// </para>
    /// </remarks>
    internal static void SetWindowClipRectBeforeSetChannel(ImGuiWindow window, ImRect clipRect)
    {
        window.ClipRect = clipRect;
        window.DrawList._cmdHeaderClipRect = new ImVec4(clipRect.Min.X, clipRect.Min.Y, clipRect.Max.X, clipRect.Max.Y);
    }

    #endregion

    #region SetNextWindow API

    /// <summary>
    /// Sets the position of the next window begun by the caller.
    /// </summary>
    /// <param name="pos">The requested position in screen coordinates.</param>
    /// <param name="cond">The condition that gates the position; zero means always.</param>
    /// <param name="pivot">The pivot within the window that is aligned to <paramref name="pos"/>.</param>
    /// <remarks>
    /// Ports canonical <c>SetNextWindowPos()</c>. A non-zero
    /// pivot defers application by one frame while the window measures itself; the canonical <c>PosUndock</c>
    /// flag is preserved for Chunk 6.
    /// </remarks>
    public static void SetNextWindowPos(ImVec2 pos, ImGuiCond cond = 0, ImVec2 pivot = default)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(cond == 0 || (((int)cond & ((int)cond - 1)) == 0)); // Do not combine multiple condition flags.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasPos;
        g.NextWindowData.PosVal = pos;
        g.NextWindowData.PosPivotVal = pivot;
        g.NextWindowData.PosCond = cond != 0 ? cond : ImGuiCond.Always;
        g.NextWindowData.PosUndock = true;
    }

    /// <summary>
    /// Sets the size of the next window begun by the caller.
    /// </summary>
    /// <param name="size">The requested size; zero on an axis selects auto-sizing for that axis in Begin().</param>
    /// <param name="cond">The condition that gates the size; zero means always.</param>
    /// <remarks>Ports canonical <c>SetNextWindowSize()</c>.</remarks>
    public static void SetNextWindowSize(ImVec2 size, ImGuiCond cond = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(cond == 0 || (((int)cond & ((int)cond - 1)) == 0)); // Do not combine multiple condition flags.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasSize;
        g.NextWindowData.SizeVal = size;
        g.NextWindowData.SizeCond = cond != 0 ? cond : ImGuiCond.Always;
    }

    /// <summary>
    /// Sets the size constraints and optional user callback for the next window begun by the caller.
    /// </summary>
    /// <param name="sizeMin">The minimum size; zero disables the limit and -1 preserves the current size.</param>
    /// <param name="sizeMax">The maximum size; <c>float.MaxValue</c> disables the limit and -1 preserves the current size.</param>
    /// <param name="customCallback">An optional callback invoked while the window is being resized.</param>
    /// <param name="customCallbackData">
    /// Opaque user data passed to <paramref name="customCallback"/>. Canonical types this as <c>void*</c>, so the
    /// port uses <see cref="IntPtr"/> rather than the <c>object?</c> form listed in the coordination contract.
    /// </param>
    /// <remarks>
    /// Ports canonical <c>SetNextWindowSizeConstraints()</c>
    ///. Using -1 for both ends of an axis preserves the current
    /// size and therefore acts as a constraint.
    /// </remarks>
    public static void SetNextWindowSizeConstraints(ImVec2 sizeMin, ImVec2 sizeMax, ImGuiSizeCallback? customCallback = null, IntPtr customCallbackData = default)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasSizeConstraint;
        g.NextWindowData.SizeConstraintRect = new ImRect(sizeMin, sizeMax);
        g.NextWindowData.SizeCallback = customCallback;
        g.NextWindowData.SizeCallbackUserData = customCallbackData;
    }

    /// <summary>
    /// Sets the explicit content size of the next window begun by the caller.
    /// </summary>
    /// <param name="size">The content size, which is the inner scrollable rectangle padded by the window padding.</param>
    /// <remarks>
    /// Ports canonical <c>SetNextWindowContentSize()</c>.
    /// Combined with <c>ImGuiWindowFlags_AlwaysAutoResize</c> it allows submitting a fixed-size content area.
    /// </remarks>
    public static void SetNextWindowContentSize(ImVec2 size)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasContentSize;
        g.NextWindowData.ContentSizeVal = TruncateVector(size);
    }

    /// <summary>
    /// Sets the scroll position of the next window begun by the caller.
    /// </summary>
    /// <param name="scroll">The requested scroll position; a negative axis is ignored by Begin().</param>
    /// <remarks>Ports canonical <c>SetNextWindowScroll()</c>.</remarks>
    public static void SetNextWindowScroll(ImVec2 scroll)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasScroll;
        g.NextWindowData.ScrollVal = scroll;
    }

    /// <summary>
    /// Sets the collapsed state of the next window begun by the caller.
    /// </summary>
    /// <param name="collapsed">The requested collapsed state.</param>
    /// <param name="cond">The condition that gates the state; zero means always.</param>
    /// <remarks>Ports canonical <c>SetNextWindowCollapsed()</c>.</remarks>
    public static void SetNextWindowCollapsed(bool collapsed, ImGuiCond cond = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(cond == 0 || (((int)cond & ((int)cond - 1)) == 0)); // Do not combine multiple condition flags.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasCollapsed;
        g.NextWindowData.CollapsedVal = collapsed;
        g.NextWindowData.CollapsedCond = cond != 0 ? cond : ImGuiCond.Always;
    }

    /// <summary>
    /// Sets the background alpha of the next window begun by the caller.
    /// </summary>
    /// <param name="alpha">The background alpha.</param>
    /// <remarks>Ports canonical <c>SetNextWindowBgAlpha()</c>.</remarks>
    public static void SetNextWindowBgAlpha(float alpha)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasBgAlpha;
        g.NextWindowData.BgAlphaVal = alpha;
    }

    /// <summary>
    /// Sets the viewport of the next window begun by the caller.
    /// </summary>
    /// <param name="viewportId">The target viewport identifier.</param>
    /// <remarks>
    /// Ports canonical <c>SetNextWindowViewport()</c>. The value
    /// is stored exactly as canonical does, but it is not consumed because automatic window detachment into
    /// secondary platform viewports is not implemented; canonical's single-viewport path returns before
    /// reading it.
    /// </remarks>
    public static void SetNextWindowViewport(uint viewportId)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasViewport;
        g.NextWindowData.ViewportId = viewportId;
    }

    /// <summary>
    /// Sets the window class of the next window begun by the caller.
    /// </summary>
    /// <param name="windowClass">The window class to copy into the next-window data.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetNextWindowClass()</c>. Setting and
    /// clearing the same viewport flag bit is a programmer error.
    /// </para>
    /// <para>
    /// Canonical copies the class by value. This port assigns the reference because <c>ImGuiWindowClass</c> is
    /// a reference type (see the documented deviation in <c>06_internal_api.md</c>).
    /// </para>
    /// </remarks>
    public static void SetNextWindowClass(ImGuiWindowClass? windowClass)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(windowClass != null);
        ImAssert.Check((windowClass!.ViewportFlagsOverrideSet & windowClass.ViewportFlagsOverrideClear) == 0); // Cannot set and clear the same bit.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasWindowClass;
        g.NextWindowData.WindowClass = windowClass;
    }

    /// <summary>
    /// Sets the experimental refresh policy of the next window begun by the caller.
    /// </summary>
    /// <param name="flags">The refresh policy flags.</param>
    /// <remarks>
    /// Ports canonical <c>SetNextWindowRefreshPolicy()</c>. The
    /// policy is stored but never enabled in this port; see <see cref="UpdateWindowSkipRefresh"/>.
    /// </remarks>
    public static void SetNextWindowRefreshPolicy(ImGuiWindowRefreshFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasRefreshPolicy;
        g.NextWindowData.RefreshFlagsVal = flags;
    }

    /// <summary>
    /// Requests that the next window begun by the caller receives focus.
    /// </summary>
    /// <remarks>Ports canonical <c>SetNextWindowFocus()</c>.</remarks>
    public static void SetNextWindowFocus()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasFocus;
    }

    /// <summary>
    /// Sets or clears window flags for the next window begun by the caller.
    /// </summary>
    /// <param name="flags">The window flags to enable or disable.</param>
    /// <param name="enabled">True to set the flags, false to clear them.</param>
    /// <remarks>Ports canonical <c>SetNextWindowFlags()</c>.</remarks>
    public static void SetNextWindowFlags(ImGuiWindowFlags flags, bool enabled = true)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasWindowFlags) == 0)
            g.NextWindowData.WindowFlagsSet = g.NextWindowData.WindowFlagsClear = ImGuiWindowFlags.None;
        g.NextWindowData.WindowFlagsSet = enabled ? (g.NextWindowData.WindowFlagsSet | flags) : (g.NextWindowData.WindowFlagsSet & ~flags);
        g.NextWindowData.WindowFlagsClear = !enabled ? (g.NextWindowData.WindowFlagsClear | flags) : (g.NextWindowData.WindowFlagsClear & ~flags);
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasWindowFlags;
    }

    /// <summary>
    /// Sets or clears child flags for the next child window begun by the caller.
    /// </summary>
    /// <param name="flags">The child flags to enable or disable.</param>
    /// <param name="enabled">True to set the flags, false to clear them.</param>
    /// <remarks>Ports canonical <c>SetNextWindowChildFlags()</c>.</remarks>
    public static void SetNextWindowChildFlags(ImGuiChildFlags flags, bool enabled = true)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasChildFlags) == 0)
            g.NextWindowData.ChildFlagsSet = g.NextWindowData.ChildFlagsClear = ImGuiChildFlags.None;
        g.NextWindowData.ChildFlagsSet = enabled ? (g.NextWindowData.ChildFlagsSet | flags) : (g.NextWindowData.ChildFlagsSet & ~flags);
        g.NextWindowData.ChildFlagsClear = !enabled ? (g.NextWindowData.ChildFlagsClear | flags) : (g.NextWindowData.ChildFlagsClear & ~flags);
        g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasChildFlags;
    }

    #endregion

    #region Window focus and display ordering

    /// <summary>
    /// Focuses the current window.
    /// </summary>
    /// <remarks>Ports canonical <c>SetWindowFocus()</c>.</remarks>
    public static void SetWindowFocus()
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        FocusWindow(g.CurrentWindow);
    }

    /// <summary>
    /// Focuses the named window, or disables keyboard focus when the name is null.
    /// </summary>
    /// <param name="name">The window name to focus, or null to disable focus. Canonical accepts NULL here, so the parameter is nullable.</param>
    /// <remarks>
    /// Ports canonical <c>SetWindowFocus(const char*)</c>. A
    /// non-null name that resolves to no window leaves focus unchanged.
    /// </remarks>
    public static void SetWindowFocus(string? name)
    {
        if (name != null)
        {
            ImGuiWindow? window = FindWindowByName(name);
            if (window != null)
                FocusWindow(window);
        }
        else
        {
            FocusWindow(null);
        }
    }

    /// <summary>
    /// Returns the focus-order index of a window.
    /// </summary>
    /// <param name="window">The root window whose index is returned.</param>
    /// <returns>The index into <c>g.WindowsFocusOrder</c>.</returns>
    /// <remarks>
    /// Ports canonical file-scope <c>FindWindowFocusIndex()</c>
    ///. A child window or a stale index is a programmer error
    /// and is asserted.
    /// </remarks>
    private static int FindWindowFocusIndex(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        int order = window.FocusOrder;
        ImAssert.Check(ReferenceEquals(window.RootWindow, window)); // No child window.
        ImAssert.Check(ReferenceEquals(g.WindowsFocusOrder[order], window));
        return order;
    }

    /// <summary>
    /// Maintains the focus-order list when a window is created or its explicit-child classification changes.
    /// </summary>
    /// <param name="window">The window being registered or removed.</param>
    /// <param name="justCreated">True on the frame the window was created.</param>
    /// <param name="newFlags">The flags resolved for this frame.</param>
    /// <remarks>
    /// Ports canonical <c>UpdateWindowInFocusOrderList()</c>
    ///. Only root windows and explicit child menus participate
    /// in the focus order; popup children and ordinary children are removed.
    /// </remarks>
    internal static void UpdateWindowInFocusOrderList(ImGuiWindow window, bool justCreated, ImGuiWindowFlags newFlags)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        bool newIsExplicitChild = (newFlags & ImGuiWindowFlags.ChildWindow) != 0
            && ((newFlags & ImGuiWindowFlags.Popup) == 0 || (newFlags & ImGuiWindowFlags.ChildMenu) != 0);
        bool childFlagChanged = newIsExplicitChild != window.IsExplicitChild;
        if ((justCreated || childFlagChanged) && !newIsExplicitChild)
        {
            ImAssert.Check(!g.WindowsFocusOrder.Contains(window));
            g.WindowsFocusOrder.Add(window);
            window.FocusOrder = (short)(g.WindowsFocusOrder.Count - 1);
        }
        else if (!justCreated && childFlagChanged && newIsExplicitChild)
        {
            ImAssert.Check(ReferenceEquals(g.WindowsFocusOrder[window.FocusOrder], window));
            for (int n = window.FocusOrder + 1; n < g.WindowsFocusOrder.Count; n++)
                g.WindowsFocusOrder[n].FocusOrder--;
            g.WindowsFocusOrder.RemoveAt(window.FocusOrder);
            window.FocusOrder = -1;
        }
        window.IsExplicitChild = newIsExplicitChild;
    }

    /// <summary>
    /// Moves a root window to the front of the focus order.
    /// </summary>
    /// <param name="window">The root window to move.</param>
    /// <remarks>
    /// Ports canonical <c>BringWindowToFocusFront()</c>. The
    /// focus order stores the most recently focused window at the back.
    /// </remarks>
    internal static void BringWindowToFocusFront(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImAssert.Check(ReferenceEquals(window, window.RootWindow));
        int curOrder = window.FocusOrder;
        ImAssert.Check(ReferenceEquals(g.WindowsFocusOrder[curOrder], window));
        if (ReferenceEquals(g.WindowsFocusOrder[^1], window))
            return;
        int newOrder = g.WindowsFocusOrder.Count - 1;
        for (int n = curOrder; n < newOrder; n++)
        {
            g.WindowsFocusOrder[n] = g.WindowsFocusOrder[n + 1];
            g.WindowsFocusOrder[n].FocusOrder--;
            ImAssert.Check(g.WindowsFocusOrder[n].FocusOrder == n);
        }
        g.WindowsFocusOrder[newOrder] = window;
        window.FocusOrder = (short)newOrder;
    }

    /// <summary>
    /// Moves a window to the front of the display order.
    /// </summary>
    /// <param name="window">The window to move.</param>
    /// <remarks>
    /// Ports canonical <c>BringWindowToDisplayFront()</c>. The
    /// canonical <c>memmove</c> tail shift is expressed as a list removal followed by a list append.
    /// </remarks>
    internal static void BringWindowToDisplayFront(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        ImGuiWindow currentFrontWindow = g.Windows[^1];
        if (ReferenceEquals(currentFrontWindow, window) || ReferenceEquals(currentFrontWindow.RootWindowDockTree, window))
            return; // Cheap early out.
        for (int i = g.Windows.Count - 2; i >= 0; i--) // The top-most window can be ignored.
        {
            if (ReferenceEquals(g.Windows[i], window))
            {
                g.Windows.RemoveAt(i);
                g.Windows.Add(window);
                break;
            }
        }
    }

    /// <summary>
    /// Moves a window to the back of the display order.
    /// </summary>
    /// <param name="window">The window to move.</param>
    /// <remarks>
    /// Ports canonical <c>BringWindowToDisplayBack()</c>.
    /// </remarks>
    internal static void BringWindowToDisplayBack(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        if (ReferenceEquals(g.Windows[0], window))
            return;
        for (int i = 0; i < g.Windows.Count; i++)
        {
            if (ReferenceEquals(g.Windows[i], window))
            {
                g.Windows.RemoveAt(i);
                g.Windows.Insert(0, window);
                break;
            }
        }
    }

    /// <summary>
    /// Moves a window directly behind another window in the display order.
    /// </summary>
    /// <param name="window">The window to move.</param>
    /// <param name="behindWindow">The window that <paramref name="window"/> is placed behind.</param>
    /// <remarks>
    /// Ports canonical <c>BringWindowToDisplayBehind()</c>.
    /// Both windows are reduced to their roots before their positions are compared.
    /// </remarks>
    internal static void BringWindowToDisplayBehind(ImGuiWindow window, ImGuiWindow behindWindow)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        window = window.RootWindow!;
        behindWindow = behindWindow.RootWindow!;
        int posWnd = FindWindowDisplayIndex(window);
        int posBeh = FindWindowDisplayIndex(behindWindow);
        g.Windows.RemoveAt(posWnd);
        g.Windows.Insert(posWnd < posBeh ? posBeh - 1 : posBeh, window);
    }

    /// <summary>
    /// Returns the display-order index of a window.
    /// </summary>
    /// <param name="window">The window to locate.</param>
    /// <returns>The index into the display-order list, or -1 when the window is not present.</returns>
    /// <remarks>Ports canonical <c>FindWindowDisplayIndex()</c>.</remarks>
    internal static int FindWindowDisplayIndex(ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        return g.Windows.IndexOf(window);
    }

    /// <summary>
    /// Moves a window to the front of both the focus and display orders and assigns it keyboard focus.
    /// </summary>
    /// <param name="window">The window to focus, or null to disable keyboard focus.</param>
    /// <param name="flags">Focus request flags.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>FocusWindow()</c>.
    /// </para>
    /// <para>
    /// The modal check, popup closure over the newly focused window, and the popup-stack trimming on
    /// focus change follow canonical <c>FocusWindow</c>. The dock-node active-widget exception is inert
    /// because docking belongs to Chunk 6.
    /// </para>
    /// </remarks>
    internal static void FocusWindow(ImGuiWindow? window, ImGuiFocusRequestFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.

        // Modal check. A request that would move focus under a blocking modal is
        // refused: the window is placed directly behind the modal, and popups above the top-most modal close.
        // The common case (focus already on the window) skips the lookup.
        if ((flags & ImGuiFocusRequestFlags.UnlessBelowModal) != 0 && !ReferenceEquals(g.NavWindow, window))
        {
            ImGuiWindow? blockingModal = FindBlockingModal(window);
            if (blockingModal != null)
            {
                if (window != null && ReferenceEquals(window, window.RootWindow)
                    && (window.Flags & ImGuiWindowFlags.NoBringToFrontOnFocus) == 0)
                    BringWindowToDisplayBehind(window, blockingModal);
                // GetTopMostPopupModal, not blockingModal, handles nested modals.
                ClosePopupsOverWindow(GetTopMostPopupModal(), false);
                return;
            }
        }

        // Focus the last focused child if any.
        if ((flags & ImGuiFocusRequestFlags.RestoreFocusedChild) != 0 && window != null)
            window = NavRestoreLastChildNavWindow(window);

        // Apply focus.
        if (!ReferenceEquals(g.NavWindow, window))
        {
            SetNavWindow(window);
            if (window != null && g.NavHighlightItemUnderNav)
                g.NavMousePosDirty = true;
            g.NavId = window != null ? window.NavLastIds[0] : 0; // Restore NavId.
            g.NavLayer = ImGuiNavLayer.Main;
            SetNavFocusScope(window != null ? window.NavRootFocusScopeId : 0);
            g.NavIdIsAlive = false;
            g.NavLastValidSelectionUserData = -1; // ImGuiSelectionUserData_Invalid

            // Close popups that are not descendants of the newly focused window.
            ClosePopupsOverWindow(window, false);
        }

        // Move the root window to the top of the pile.
        ImAssert.Check(window == null || window.RootWindowDockTree != null);
        ImGuiWindow? focusFrontWindow = window != null ? window.RootWindow : null;
        ImGuiWindow? displayFrontWindow = window != null ? window.RootWindowDockTree : null;
        // Chunk 6 (docking): canonical derives the dock node here and tests whether the active identifier
        // belongs to the dock node host. The exception is inert.
        const bool activeIdWindowIsDockNodeHost = false;

        // Steal active widgets when focus moves away from the window that owns the active identifier.
        if (g.ActiveId != 0 && g.ActiveIdWindow != null && g.ActiveIdWindow.RootWindow != focusFrontWindow)
        {
            if (!g.ActiveIdNoClearOnFocusLoss && !activeIdWindowIsDockNodeHost)
                ClearActiveID();
        }

        // A null window disables keyboard focus.
        if (window == null)
            return;
        window.LastFrameJustFocused = g.FrameCount;

        // Bring to front.
        BringWindowToFocusFront(focusFrontWindow!);
        if (((window.Flags | focusFrontWindow!.Flags | displayFrontWindow!.Flags) & ImGuiWindowFlags.NoBringToFrontOnFocus) == 0)
            BringWindowToDisplayFront(displayFrontWindow);
    }

    /// <summary>
    /// Focuses the top-most focusable window below a given window in the focus order.
    /// </summary>
    /// <param name="underThisWindow">The window to search below, or null to start from the front-most window.</param>
    /// <param name="ignoreWindow">A window to skip, or null.</param>
    /// <param name="filterViewport">When non-null, only windows hosted by this viewport are considered.</param>
    /// <param name="flags">Focus request flags forwarded to the selected window.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>FocusTopMostWindowUnderOne()</c>. A
    /// child window is first reduced to its root, then the focus order is scanned backwards for a window that
    /// accepts either mouse or navigation input.
    /// </para>
    /// <para>
    /// Canonical accepts NULL for <paramref name="underThisWindow"/>, so the parameter is nullable.
    /// </para>
    /// </remarks>
    internal static void FocusTopMostWindowUnderOne(ImGuiWindow? underThisWindow, ImGuiWindow? ignoreWindow, ImGuiViewport? filterViewport, ImGuiFocusRequestFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!; // Canonical dereferences GImGui unconditionally; a missing context is a precondition violation.
        int startIdx = g.WindowsFocusOrder.Count - 1;
        if (underThisWindow != null)
        {
            // Aim at the root window behind the supplied window when starting from one of its children (#4640).
            int offset = -1;
            // The loop condition dereferences the cursor, so the cursor is non-nullable and the loop
            // assigns the nullable parent only after checking that the child-window flag is set.
            ImGuiWindow current = underThisWindow;
            while ((current.Flags & ImGuiWindowFlags.ChildWindow) != 0)
            {
                current = current.ParentWindow!;
                offset = 0;
            }
            startIdx = FindWindowFocusIndex(current) + offset;
        }
        for (int i = startIdx; i >= 0; i--)
        {
            // The canonical input-kind test is deliberately coarse: either mouse or navigation input is enough.
            ImGuiWindow window = g.WindowsFocusOrder[i];
            if (ReferenceEquals(window, ignoreWindow) || !window.WasActive)
                continue;
            if (filterViewport != null && !ReferenceEquals(window.Viewport, filterViewport))
                continue;
            if ((window.Flags & (ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNavInputs))
                != (ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoNavInputs))
            {
                FocusWindow(window, flags);
                return;
            }
        }
        FocusWindow(null, flags);
    }

    #endregion

    #endregion


    #region SECTION: LAYOUT, ITEM PRIMITIVES, AND ITEM QUERIES

    #region ITEM SUBMISSION

    /// <summary>
    /// Advances the layout cursor past an item of the given size, registering the minimum size used for
    /// auto-fit computation and content-size growth. Positions are aligned on pixel boundaries.
    /// </summary>
    /// <param name="size">The item size to reserve at the current cursor position.</param>
    /// <param name="textBaselineY">
    /// The desired text baseline, or a negative value to skip baseline alignment. Canonical default is
    /// <c>-1.0f</c>; the binding contract listed <c>0.0f</c>, which would change line height whenever the
    /// current line has a non-zero baseline offset, so the canonical default is used.
    /// </param>
    /// <remarks>
    /// The item bounding box minimum is expected to equal the current cursor; the <see cref="ImRect"/>
    /// overload ignores it. In horizontal layout mode the call ends by invoking <c>SameLine()</c> so the
    /// next item continues on the same row.
    /// </remarks>
    internal static void ItemSize(ImVec2 size, float textBaselineY = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        // The height is enlarged here to accommodate the baseline offset; offsetting the starting cursor
        // instead would require a larger layout refactor, and enlarging has the same effect on line flow.
        ImGuiWindowTempData dc = window.DC;
        float offsetToMatchBaselineY = (textBaselineY >= 0.0f) ? MathF.Max(0.0f, dc.CurrLineTextBaseOffset - textBaselineY) : 0.0f;

        float lineY1 = dc.IsSameLine ? dc.CursorPosPrevLine.Y : dc.CursorPos.Y;
        float lineHeight = MathF.Max(dc.CurrLineSize.Y, dc.CursorPos.Y - lineY1 + size.Y + offsetToMatchBaselineY);

        // Always align on pixel boundaries using the canonical positive-only truncation.
        dc.CursorPosPrevLine = new ImVec2(dc.CursorPos.X + size.X, lineY1);
        dc.CursorPos = new ImVec2(
            (float)(int)(window.Pos.X + dc.Indent.X + dc.ColumnsOffset.X),
            (float)(int)(lineY1 + lineHeight + g.Style.ItemSpacing.Y));
        dc.CursorMaxPos = new ImVec2(
            MathF.Max(dc.CursorMaxPos.X, dc.CursorPosPrevLine.X),
            MathF.Max(dc.CursorMaxPos.Y, dc.CursorPos.Y - g.Style.ItemSpacing.Y));

        dc.PrevLineSize = new ImVec2(dc.PrevLineSize.X, lineHeight);
        dc.CurrLineSize = new ImVec2(dc.CurrLineSize.X, 0.0f);
        dc.PrevLineTextBaseOffset = MathF.Max(dc.CurrLineTextBaseOffset, textBaselineY);
        dc.CurrLineTextBaseOffset = 0.0f;
        dc.IsSameLine = false;
        dc.IsSetPos = false;

        if (dc.LayoutType == ImGuiLayoutType.Horizontal)
            SameLine();
    }

    /// <summary>
    /// Advances the layout cursor by the size of the given bounding box.
    /// </summary>
    /// <param name="bb">The item bounding box; only its size is used.</param>
    /// <param name="textBaselineY">The desired text baseline, or a negative value to skip baseline alignment.</param>
    /// <remarks>
    /// Canonical describes this overload as misleading because callers are expected to have positioned the
    /// cursor at <c>bb.Min</c> beforehand; <c>bb.Min</c> itself is ignored. It is a one-statement delegation
    /// to <see cref="ItemSize(ImVec2, float)"/>.
    /// </remarks>
    internal static void ItemSize(ImRect bb, float textBaselineY = -1.0f)
        => ItemSize(bb.GetSize(), textBaselineY);

    /// <summary>
    /// Declares an item bounding box for clipping and interaction, publishes it as the last-item data, runs
    /// per-item navigation and shortcut processing, and applies the canonical clipping early-out.
    /// </summary>
    /// <param name="bb">The interaction and draw bounding box in absolute screen coordinates.</param>
    /// <param name="id">The item identifier; zero is permitted for layout-only items.</param>
    /// <param name="navBb">An optional navigation bounding box; when omitted, <paramref name="bb"/> is used.</param>
    /// <param name="extraFlags">Item flags OR-ed onto the current flags and the queued next-item flags.</param>
    /// <returns>True when the item is accepted; false when it is clipped and therefore not submitted.</returns>
    /// <remarks>
    /// <para>
    /// This function is in the performance-critical path. The canonical step order is preserved exactly:
    /// last-item initialization, <c>KeepAliveID</c>, navigation processing, shortcut handling, next-item
    /// data clearing, the clipping early-out (which intentionally runs after navigation processing so that
    /// navigation initialization and scroll-past-clipped behavior still work), then the visible and
    /// hovered-rectangle status flags.
    /// </para>
    /// <para>
    /// This port splits canonical <c>ImGuiItemFlags</c> into a public enum and
    /// <see cref="ImGuiItemFlagsPrivate"/>; the private flags used here are folded back into
    /// <see cref="ImGuiItemFlags"/> through the constants in the private-helpers region.
    /// </para>
    /// <para>
    /// Branches left inert: the test-engine item-add hook (canonical <c>IMGUI_TEST_ENGINE_ITEM_ADD</c>, only
    /// active with <c>IMGUI_ENABLE_TEST_ENGINE</c>) and the <c>IMGUI_DEBUG_HIGHLIGHT_ALL_ID_CONFLICTS</c>
    /// storage-based highlight (compiled out by default in canonical).
    /// </para>
    /// <para>
    /// The locate-item resolution runs after the clipping early-out, as canonical does, so a submission that
    /// is clipped and rejected does not consume a pending <see cref="DebugLocateItem"/> request.
    /// </para>
    /// </remarks>
    internal static bool ItemAdd(ImRect bb, uint id, ImRect? navBb = null, ImGuiItemFlags extraFlags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        // Publish item data. DisplayRect is deliberately left untouched until HasDisplayRect is set.
        g.LastItemData.ID = id;
        g.LastItemData.Rect = bb;
        g.LastItemData.NavRect = navBb ?? bb;
        g.LastItemData.ItemFlags = g.CurrentItemFlags | g.NextItemData.ItemFlagsSet | extraFlags;
        g.LastItemData.StatusFlags = ImGuiItemStatusFlags.None;

        if (id != 0)
        {
            KeepAliveID(id);

            // Directional navigation processing runs before the clipping early-out so that NavInitRequest can
            // be honored for newly opened windows and so navigation can scroll past clipped items.
            if ((g.LastItemData.ItemFlags & ImGuiItemFlags.NoNav) == 0)
            {
                // The layer mask is a 16-bit bitfield; the unsigned widening avoids sign-extension of the
                // canonical short operand before the bitwise OR.
                window.DC.NavLayersActiveMaskNext = (short)((ushort)window.DC.NavLayersActiveMaskNext | (1 << (int)window.DC.NavLayerCurrent));
                if (g.NavId == id || g.NavAnyRequest)
                {
                    // NavAnyRequest is only ever set while a navigation window exists.
                    ImGuiWindow navWindow = g.NavWindow!;
                    if (navWindow.RootWindowForNav == window.RootWindowForNav)
                        if (window == navWindow || ((window.ChildFlags | navWindow.ChildFlags) & ImGuiChildFlags.NavFlattened) != 0)
                            NavProcessItem();
                }
            }

            if ((g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasShortcut) != 0)
                ItemHandleShortcut(id);
        }

        // Lightweight clear of the queued SetNextItemXXX data.
        g.NextItemData.HasFlags = ImGuiNextItemDataFlags.None;
        g.NextItemData.ItemFlagsSet = ImGuiItemFlags.None;

        // Clipping test. This is an inline copy of IsClippedEx() so that isRectVisible can be reused below.
        // NavActivateId is not necessarily equal to NavId in the remote-activation (shortcut) case.
        bool isRectVisible = bb.Overlaps(window.ClipRect);
        if (!isRectVisible)
            if (id == 0 || (id != g.ActiveId && id != g.ActiveIdPreviousFrame && id != g.NavId && id != g.NavActivateId))
                if (!g.ItemUnclipByLog)
                    return false;

        if (id != 0)
        {
            // Debug tooling: when this identifier is the one being located, draw the highlight now, while
            // the last-item data still describes the item that was just submitted. This runs after the
            // clipping early-out, so a rejected submission does not consume the request.
            if (id == g.DebugLocateId)
                DebugLocateItemResolveWithLastItem();

            ImAssert.Check(id != window.ID,
                "Cannot have an empty ID at the root of a window. If you need an empty label, use ## and read the FAQ about how the ID Stack works!");
        }

        if (id != 0 && g.DeactivatedItemData.ID == id)
            g.DeactivatedItemData.ElapseFrame = g.FrameCount;

        // Computed after navigation so that widgets which temporarily alter the clip rectangle are honored.
        if (isRectVisible)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.Visible;
        if (IsMouseHoveringRect(bb.Min, bb.Max))
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredRect;
        return true;
    }

    /// <summary>
    /// Consumes the queued next-item shortcut for the item just submitted by <see cref="ItemAdd"/>: it
    /// publishes the shortcut tooltip data and, when the chord is pressed, queues navigation activation of
    /// the item.
    /// </summary>
    /// <param name="id">The identifier of the item whose queued shortcut is being consumed.</param>
    /// <remarks>
    /// <para>
    /// Canonical calls this from within <see cref="ItemAdd"/> after navigation processing, at which point the
    /// queued next-item data may be read and the last-item data may be written. Disabled items ignore their
    /// shortcut entirely.
    /// </para>
    /// <para>
    /// When <see cref="ImGuiInputFlags.Tooltip"/> is set, the chord and the
    /// <see cref="ImGuiItemStatusFlags.HasShortcut"/> status flag are published so that
    /// <see cref="ItemHoverable"/> can display the chord. When the chord is pressed and no navigation
    /// activation is already pending, the item is queued for activation with
    /// <see cref="ImGuiActivateFlags.PreferInput"/> and <see cref="ImGuiActivateFlags.FromShortcut"/>, and the
    /// navigation highlight is refreshed through <c>NavHighlightActivated</c> (owned by part 08).
    /// </para>
    /// <para>
    /// The public <c>SetNextItemShortcut</c>/<c>Shortcut</c> API that queues this data belongs to Chunk 10;
    /// this member only consumes it. The <c>Shortcut()</c> query itself is not owned by any part of this
    /// contract and is called as <c>Shortcut(chord, flags, ownerId)</c>.
    /// </para>
    /// <para>
    /// The private constraint masks used here are folded from <see cref="ImGuiInputFlagsPrivate"/> into
    /// <see cref="ImGuiInputFlags"/>, matching the public/private enum split of this port.
    /// </para>
    /// </remarks>
    internal static void ItemHandleShortcut(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiInputFlags flags = g.NextItemData.ShortcutFlags;
        ImAssert.Check(((int)flags & ~(int)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedBySetNextItemShortcut) == 0,
            "Passing flags not supported by SetNextItemShortcut()!");

        if ((g.LastItemData.ItemFlags & ImGuiItemFlags.Disabled) != 0)
            return;
        if ((flags & ImGuiInputFlags.Tooltip) != 0)
        {
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HasShortcut;
            g.LastItemData.Shortcut = g.NextItemData.Shortcut;
        }
        if (!Shortcut(g.NextItemData.Shortcut, flags & (ImGuiInputFlags)ImGuiInputFlagsPrivate.ImGuiInputFlags_SupportedByShortcut, id) || g.NavActivateId != 0)
            return;

        // Navigation activation is queued rather than applied immediately.
        g.NavActivateId = id; // Will effectively disable clipping.
        g.NavActivateFlags = ImGuiActivateFlags.PreferInput | ImGuiActivateFlags.FromShortcut;
        g.NavActivateDownId = g.NavActivatePressedId = id;
        NavHighlightActivated(id);
    }

    /// <summary>
    /// Submission-side hover test used by widgets; it updates the hovered identifier and the disabled-hover
    /// state rather than being a pure query.
    /// </summary>
    /// <param name="bb">The interaction bounding box in absolute screen coordinates.</param>
    /// <param name="id">The item identifier; zero is permitted for high-level hover tests.</param>
    /// <param name="itemFlags">The effective item flags, normally the last item's flags.</param>
    /// <returns>True when the item is hovered; false when it is blocked, overlapped, or disabled.</returns>
    /// <remarks>
    /// <para>
    /// Hover precedence follows canonical order: hovered-window match, geometry test, hovered-identifier
    /// conflict, active-identifier conflict, window-content-hoverable test, drag-source suppression,
    /// <c>SetHoveredID</c>, the one-frame <c>AllowOverlap</c> deferral, the shortcut tooltip, the disabled
    /// branch (which still records <c>HoveredId</c>), the debug item picker, and finally the navigation
    /// override.
    /// </para>
    /// <para>
    /// The shortcut tooltip emission is left inert because the tooltip subsystem is owned by a later chunk;
    /// canonical calls <c>SetTooltip("%s", GetKeyChordName(g.LastItemData.Shortcut))</c>. The hover-delay
    /// state update performed by the surrounding <c>IsItemHovered()</c> call is preserved.
    /// </para>
    /// <para>
    /// The <c>ImGuiItemFlags_NoWindowHoverableCheck</c>, <c>ImGuiItemFlags_AllowOverlap</c>, and
    /// <c>ImGuiItemFlags_NoNavDisableMouseHover</c> tests use private item-flag constants folded into
    /// <see cref="ImGuiItemFlags"/>.
    /// </para>
    /// </remarks>
    internal static bool ItemHoverable(ImRect bb, uint id, ImGuiItemFlags itemFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        // Detect duplicate identifiers cheaply on hover: one 32-bit comparison per item.
        if (id != 0 && g.HoveredIdPreviousFrame == id && (itemFlags & ImGuiItemFlags.AllowDuplicateId) == 0)
        {
            g.HoveredIdPreviousFrameItemCount++;
            if (g.DebugDrawIdConflictsId == id)
                window.DrawList.AddRect(bb.Min - new ImVec2(1.0f, 1.0f), bb.Max + new ImVec2(1.0f, 1.0f), 0xFF0000FF, 0.0f, 2.0f, ImDrawFlags.None);
        }

        if (g.HoveredWindow != window)
            return false;
        if (!IsMouseHoveringRect(bb.Min, bb.Max))
            return false;

        if (g.HoveredId != 0 && g.HoveredId != id && !g.HoveredIdAllowOverlap)
            return false;
        if (g.ActiveId != 0 && g.ActiveId != id && !g.ActiveIdAllowOverlap)
            if (!g.ActiveIdFromShortcut)
                return false;

        // Rectangle culling is done; the heavier window-content test can now run.
        if ((itemFlags & ItemFlagNoWindowHoverableCheck) == 0 && !IsWindowContentHoverable(window, ImGuiHoveredFlags.None))
        {
            g.HoveredIdIsDisabled = true;
            return false;
        }

        // id == 0 is permitted so widgets can perform a high-level hover test without publishing an item.
        if (id != 0)
        {
            // A drag source does not report as hovered by default.
            if (g.DragDropActive && g.DragDropPayload.SourceId == id && (g.DragDropSourceFlags & ImGuiDragDropFlags.SourceNoDisableHover) == 0)
                return false;

            SetHoveredID(id);

            // AllowOverlap requires the previous frame's HoveredId to be null or to match, realizing a
            // front-to-back hit test through a one-frame deferral.
            if ((itemFlags & ItemFlagAllowOverlap) != 0)
            {
                g.HoveredIdAllowOverlap = true;
                if (g.HoveredIdPreviousFrame != id)
                    return false;
            }

            // Display the shortcut tooltip (mouse-only path). Canonical condition is preserved; the tooltip
            // emission itself belongs to the tooltip subsystem, owned by a later chunk.
            if (id == g.LastItemData.ID && (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HasShortcut) != 0 && g.ActiveId != id)
            {
                if (IsItemHovered(ImGuiHoveredFlags.ForTooltip | ImGuiHoveredFlags.DelayNormal))
                {
                    // Inert: SetTooltip("%s", GetKeyChordName(g.LastItemData.Shortcut)) is owned by a later chunk.
                }
            }
        }

        // Disabled items still record HoveredId but report no hover.
        if ((itemFlags & ImGuiItemFlags.Disabled) != 0)
        {
            if (g.ActiveId == id && id != 0)
                ClearActiveID();
            g.HoveredIdIsDisabled = true;
            return false;
        }

        if (id != 0)
        {
            // Debug item picker: reaching this path is rare, so the tool cost is near-zero.
            if (g.DebugItemPickerActive && g.HoveredIdPreviousFrame == id)
                GetForegroundDrawList().AddRect(bb.Min, bb.Max, 0xFFFFFF00);
            if (g.DebugItemPickerBreakId == id)
                Debugger.Break();
        }

        if (g.NavHighlightItemUnderNav && (itemFlags & ItemFlagNoNavDisableMouseHover) == 0)
            return false;

        return true;
    }

    /// <summary>
    /// Determines whether an item is clipped by the current window clip rectangle. This is the standalone
    /// form of the clipping test that <see cref="ItemAdd"/> performs inline.
    /// </summary>
    /// <param name="bb">The item bounding box in absolute screen coordinates.</param>
    /// <param name="id">The item identifier; zero is permitted.</param>
    /// <returns>True when the item is clipped and should be skipped.</returns>
    /// <remarks>
    /// Active, active-previous-frame, navigation-focused, and remotely-activatable items are never treated as
    /// clipped. <c>g.ItemUnclipByLog</c> suppresses clipping while logging is active.
    /// </remarks>
    internal static bool IsClippedEx(ImRect bb, uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (!bb.Overlaps(window.ClipRect))
            if (id == 0 || (id != g.ActiveId && id != g.ActiveIdPreviousFrame && id != g.NavId && id != g.NavActivateId))
                if (!g.ItemUnclipByLog)
                    return true;
        return false;
    }

    /// <summary>
    /// Publishes the last-item data for an explicitly submitted item.
    /// </summary>
    /// <param name="itemId">The item identifier.</param>
    /// <param name="itemFlags">The item flags to publish.</param>
    /// <param name="statusFlags">The status flags to publish.</param>
    /// <param name="itemRect">The item rectangle, used for both the draw rectangle and the navigation rectangle.</param>
    /// <remarks>
    /// The display rectangle is left untouched; when <see cref="ImGuiItemStatusFlags.HasDisplayRect"/> is set,
    /// the caller must assign <c>g.LastItemData.DisplayRect</c> itself.
    /// </remarks>
    internal static void SetLastItemData(uint itemId, ImGuiItemFlags itemFlags, ImGuiItemStatusFlags statusFlags, ImRect itemRect)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.LastItemData.ID = itemId;
        g.LastItemData.ItemFlags = itemFlags;
        g.LastItemData.StatusFlags = statusFlags;
        g.LastItemData.Rect = itemRect;
        g.LastItemData.NavRect = itemRect;
    }

    /// <summary>
    /// Publishes the last-item data for a window's title bar, or for its dock tab when the window is docked.
    /// </summary>
    /// <param name="window">The window whose title-bar item is being published.</param>
    /// <param name="rect">The title-bar rectangle, used when the window is not docked.</param>
    /// <remarks>
    /// Docked windows publish the dock-tab status flags and dock-tab rectangle captured by the docking
    /// subsystem instead of the supplied rectangle.
    /// </remarks>
    internal static void SetLastItemDataForWindow(ImGuiWindow window, ImRect rect)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (window.DockIsActive)
            SetLastItemData(window.MoveId, g.CurrentItemFlags, window.DC.DockTabItemStatusFlags, window.DC.DockTabItemRect);
        else
            SetLastItemData(window.MoveId, g.CurrentItemFlags, window.DC.WindowItemStatusFlags, rect);
    }

    /// <summary>
    /// Publishes the last-item data for a child window's bounding item.
    /// </summary>
    /// <param name="window">The child window whose item is being published.</param>
    /// <param name="rect">The child-window rectangle.</param>
    /// <remarks>
    /// The child item identifier is <c>window.ChildId</c> and its status flags are accumulated in
    /// <c>window.DC.ChildItemStatusFlags</c>.
    /// </remarks>
    internal static void SetLastItemDataForChildWindowItem(ImGuiWindow window, ImRect rect)
    {
        ImGuiContext g = GetCurrentContext()!;
        SetLastItemData(window.ChildId, g.CurrentItemFlags, window.DC.ChildItemStatusFlags, rect);
    }

    #endregion

    #region ITEM QUERIES

    /// <summary>
    /// Queries whether the last submitted item is hovered, honoring the full canonical hovered-flag set.
    /// </summary>
    /// <param name="flags">The hovered flags controlling window, overlap, disabled, and delay behavior.</param>
    /// <returns>True when the last item is hovered under the requested conditions.</returns>
    /// <remarks>
    /// <para>
    /// The navigation-override branch runs first: while the navigation cursor is visible and the highlight is
    /// under navigation, focus substitutes for the hovered rectangle unless
    /// <see cref="ImGuiHoveredFlags.NoNavOverride"/> is set. The mouse branch then tests the hovered
    /// rectangle, the hovered window, the active item (including the <c>MoveId</c> and <c>TabId</c>
    /// exemptions), window-content hoverability, the disabled state, the title-bar/tab guard, and the
    /// <c>AllowOverlap</c> deferral.
    /// </para>
    /// <para>
    /// Tooltip flag merging is normally factored into the canonical file-scope
    /// <c>ApplyHoverFlagsForTooltip()</c> helper, which is also used by <c>IsWindowHovered()</c>. It is
    /// inlined here to avoid defining a duplicate helper in two parts of the same partial class.
    /// </para>
    /// <para>
    /// Popup and modal blocking is realized through <see cref="IsWindowContentHoverable"/>; the popup stacks
    /// in the context are present and no boundary is required there.
    /// </para>
    /// </remarks>
    public static bool IsItemHovered(ImGuiHoveredFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (((int)flags & ~(int)ImGuiHoveredFlagsPrivate.ImGuiHoveredFlags_AllowedMaskForIsItemHovered) != 0)
            ErrorLog("Invalid flags for IsItemHovered()!");

        if (g.NavHighlightItemUnderNav && g.NavCursorVisible && (flags & ImGuiHoveredFlags.NoNavOverride) == 0)
        {
            if (!IsItemFocused())
                return false;
            if ((g.LastItemData.ItemFlags & ImGuiItemFlags.Disabled) != 0 && (flags & ImGuiHoveredFlags.AllowWhenDisabled) == 0)
                return false;

            if ((flags & ImGuiHoveredFlags.ForTooltip) != 0)
            {
                // Allow instance flags to override the shared style defaults, then merge the remainder.
                ImGuiHoveredFlags sharedFlags = g.Style.HoverFlagsForTooltipNav;
                if ((flags & (ImGuiHoveredFlags.DelayNone | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.DelayNormal)) != 0)
                    sharedFlags &= ~(ImGuiHoveredFlags.DelayNone | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.DelayNormal);
                flags |= sharedFlags;
            }
        }
        else
        {
            // Bounding-box overlap was recorded by ItemAdd().
            ImGuiItemStatusFlags statusFlags = g.LastItemData.StatusFlags;
            if ((statusFlags & ImGuiItemStatusFlags.HoveredRect) == 0)
                return false;

            if ((flags & ImGuiHoveredFlags.ForTooltip) != 0)
            {
                ImGuiHoveredFlags sharedFlags = g.Style.HoverFlagsForTooltipMouse;
                if ((flags & (ImGuiHoveredFlags.DelayNone | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.DelayNormal)) != 0)
                    sharedFlags &= ~(ImGuiHoveredFlags.DelayNone | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.DelayNormal);
                flags |= sharedFlags;
            }

            // Hovering the right window: the hovered window may be another window in front of this one.
            if (g.HoveredWindow != window && (statusFlags & ImGuiItemStatusFlags.HoveredWindow) == 0)
                if ((flags & ImGuiHoveredFlags.AllowWhenOverlappedByWindow) == 0)
                    return false;

            // Another item being active (for example dragged) blocks hover.
            uint id = g.LastItemData.ID;
            if ((flags & ImGuiHoveredFlags.AllowWhenBlockedByActiveItem) == 0)
                if (g.ActiveId != 0 && g.ActiveId != id && !g.ActiveIdAllowOverlap && !g.ActiveIdFromShortcut)
                {
                    // ActiveId == MoveId means the user clicked the void or an item with no id and started a
                    // window move, or clicked a disabled item. ActiveId == TabId means a docking tab click.
                    bool cancelIsHovered = true;
                    if (g.ActiveId == window.MoveId && (id == 0 || g.ActiveIdDisabledId == id))
                        cancelIsHovered = false;
                    if (g.ActiveId == window.TabId)
                        cancelIsHovered = false;
                    if (cancelIsHovered)
                        return false;
                }

            // Interactions blocked by an active popup or modal. AllowWhenBlockedByPopup is tested inside.
            if (!IsWindowContentHoverable(window, flags) && (g.LastItemData.ItemFlags & ItemFlagNoWindowHoverableCheck) == 0)
                return false;

            if ((g.LastItemData.ItemFlags & ImGuiItemFlags.Disabled) != 0 && (flags & ImGuiHoveredFlags.AllowWhenDisabled) == 0)
                return false;

            // Special handling for calls made right after Begin(), representing the title bar or tab.
            if (id == window.MoveId && window.WriteAccessed)
                return false;

            // AllowOverlap items are only hovered when the previous frame confirmed the same item.
            if ((g.LastItemData.ItemFlags & ItemFlagAllowOverlap) != 0 && id != 0)
                if ((flags & ImGuiHoveredFlags.AllowWhenOverlappedByItem) == 0)
                    if (g.HoveredIdPreviousFrame != g.LastItemData.ID)
                        return false;
        }

        // Hover delay handling.
        float delay = CalcDelayFromHoveredFlags(flags);
        if (delay > 0.0f || (flags & ImGuiHoveredFlags.Stationary) != 0)
        {
            // Items without an identifier (rare) derive a stable delay key from their position.
            uint hoverDelayId = (g.LastItemData.ID != 0) ? g.LastItemData.ID : GetIDFromPos(g.LastItemData.Rect.Min);
            if ((flags & ImGuiHoveredFlags.NoSharedDelay) != 0 && g.HoverItemDelayIdPreviousFrame != hoverDelayId)
                g.HoverItemDelayTimer = 0.0f;
            g.HoverItemDelayId = hoverDelayId;

            if ((flags & ImGuiHoveredFlags.Stationary) != 0 && g.HoverItemUnlockedStationaryId != hoverDelayId)
                return false;
            if (g.HoverItemDelayTimer < delay)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether content of the given window can be hovered, accounting for modal and popup roots
    /// and for the viewport that currently owns the mouse.
    /// </summary>
    /// <param name="window">The window whose content hoverability is tested.</param>
    /// <param name="flags">Hovered flags; <see cref="ImGuiHoveredFlags.AllowWhenBlockedByPopup"/> exempts popups.</param>
    /// <returns>True when the window's content may be hovered.</returns>
    /// <remarks>
    /// A focused root that is a modal window, or a popup window without
    /// <see cref="ImGuiHoveredFlags.AllowWhenBlockedByPopup"/>, inhibits hover on other root dock trees
    /// unless the tested root is within the focused root's begin stack. Windows belonging to a different
    /// viewport than the mouse are rejected unless they share a dock tree with the moving window.
    /// </remarks>
    internal static bool IsWindowContentHoverable(ImGuiWindow window, ImGuiHoveredFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;

        // An active popup disables hovering on other windows, apart from its own children.
        if (g.NavWindow != null)
        {
            ImGuiWindow? focusedRootWindow = g.NavWindow.RootWindowDockTree;
            if (focusedRootWindow != null && focusedRootWindow.WasActive && focusedRootWindow != window.RootWindowDockTree)
            {
                // Modal windows are also popups, hence the else branch.
                bool wantInhibit = false;
                if ((focusedRootWindow.Flags & ImGuiWindowFlags.Modal) != 0)
                    wantInhibit = true;
                else if ((focusedRootWindow.Flags & ImGuiWindowFlags.Popup) != 0 && (flags & ImGuiHoveredFlags.AllowWhenBlockedByPopup) == 0)
                    wantInhibit = true;

                if (wantInhibit)
                    if (!IsWindowWithinBeginStackOf(window.RootWindow!, focusedRootWindow))
                        return false;
            }
        }

        // Filter by viewport: the moving window keeps its own dock tree hoverable.
        if (window.Viewport != g.MouseViewport)
            if (g.MovingWindow == null || window.RootWindowDockTree != g.MovingWindow.RootWindowDockTree)
                return false;

        return true;
    }

    /// <summary>
    /// Queries whether the last submitted item is currently active.
    /// </summary>
    /// <returns>True when an item is active and it is the last submitted item.</returns>
    public static bool IsItemActive()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.ActiveId != 0)
            return g.ActiveId == g.LastItemData.ID;
        return false;
    }

    /// <summary>
    /// Queries whether the last submitted item became active this frame.
    /// </summary>
    /// <returns>True when the last item was just activated.</returns>
    public static bool IsItemActivated()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.ActiveId != 0)
            if (g.ActiveId == g.LastItemData.ID && g.ActiveIdPreviousFrame != g.LastItemData.ID)
                return true;
        return false;
    }

    /// <summary>
    /// Queries whether the last submitted item was just deactivated.
    /// </summary>
    /// <returns>True when the last item was deactivated this frame.</returns>
    /// <remarks>
    /// A widget-provided <see cref="ImGuiItemStatusFlags.HasDeactivated"/> flag takes precedence over the
    /// context's deactivated-item record.
    /// </remarks>
    public static bool IsItemDeactivated()
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HasDeactivated) != 0)
            return (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Deactivated) != 0;
        return g.DeactivatedItemData.ID == g.LastItemData.ID && g.LastItemData.ID != 0 && g.DeactivatedItemData.ElapseFrame >= g.FrameCount;
    }

    /// <summary>
    /// Queries whether the last submitted item was deactivated after having been edited.
    /// </summary>
    /// <returns>True when the last item was deactivated after an edit.</returns>
    public static bool IsItemDeactivatedAfterEdit()
    {
        ImGuiContext g = GetCurrentContext()!;
        return IsItemDeactivated() && g.DeactivatedItemData.HasBeenEditedBefore;
    }

    /// <summary>
    /// Queries whether the last submitted item currently holds navigation focus.
    /// </summary>
    /// <returns>True when the last item is the navigation-focused item.</returns>
    /// <remarks>
    /// The dummy item submitted by <c>Begin()</c>, which represents the title bar or tab, is rejected while
    /// the window was written to this frame.
    /// </remarks>
    public static bool IsItemFocused()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavId != g.LastItemData.ID || g.NavId == 0)
            return false;

        ImGuiWindow window = g.CurrentWindow!;
        if (g.LastItemData.ID == window.ID && window.WriteAccessed)
            return false;
        return true;
    }

    /// <summary>
    /// Queries whether the last submitted item was clicked this frame.
    /// </summary>
    /// <param name="mouseButton">The mouse button to test.</param>
    /// <returns>True when the button was clicked and the last item is hovered.</returns>
    /// <remarks>
    /// This is not equivalent to the behavior of widgets such as <c>Button()</c>, which additionally consider
    /// press/release position and active state.
    /// </remarks>
    public static bool IsItemClicked(ImGuiMouseButton mouseButton = ImGuiMouseButton.Left)
        => IsMouseClicked(mouseButton) && IsItemHovered(ImGuiHoveredFlags.None);

    /// <summary>
    /// Queries whether the last submitted tree node or collapsing header toggled its open state.
    /// </summary>
    /// <returns>True when <see cref="ImGuiItemStatusFlags.ToggledOpen"/> is set.</returns>
    public static bool IsItemToggledOpen()
    {
        ImGuiContext g = GetCurrentContext()!;
        return (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.ToggledOpen) != 0;
    }

    /// <summary>
    /// Queries whether the last submitted selectable, tree node, or multi-select item toggled its
    /// selection state this frame.
    /// (, )
    /// </summary>
    /// <returns>True when <see cref="ImGuiItemStatusFlags.ToggledSelection"/> is set.</returns>
    /// <remarks>
    /// Only state changes are reported rather than the selected state itself, so that clipped items
    /// can be handled without resolving their selection up front. The toggled-selection flag is only
    /// meaningful while a multi-selection scope is open, so the debug assertion records the canonical
    /// precondition.
    /// </remarks>
    public static bool IsItemToggledSelection()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.CurrentMultiSelect != null, "IsItemToggledSelection() can only be used inside a BeginMultiSelect()/EndMultiSelect() scope");
        return (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.ToggledSelection) != 0;
    }

    /// <summary>
    /// Queries whether any item is hovered, including the previous frame's hovered identifier.
    /// </summary>
    /// <returns>True when an item is hovered this frame or was hovered last frame.</returns>
    public static bool IsAnyItemHovered()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.HoveredId != 0 || g.HoveredIdPreviousFrame != 0;
    }

    /// <summary>
    /// Queries whether any item is active.
    /// </summary>
    /// <returns>True when an identifier is active.</returns>
    public static bool IsAnyItemActive()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.ActiveId != 0;
    }

    /// <summary>
    /// Queries whether any item currently holds visible navigation focus.
    /// </summary>
    /// <returns>True when a navigation-focused item exists and the navigation cursor is visible.</returns>
    public static bool IsAnyItemFocused()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.NavId != 0 && g.NavCursorVisible;
    }

    /// <summary>
    /// Queries whether the last submitted item is visible under the current clip rectangle.
    /// </summary>
    /// <returns>True when <see cref="ImGuiItemStatusFlags.Visible"/> is set.</returns>
    public static bool IsItemVisible()
    {
        ImGuiContext g = GetCurrentContext()!;
        return (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Visible) != 0;
    }

    /// <summary>
    /// Queries whether the last submitted item was edited this frame.
    /// </summary>
    /// <returns>True when <see cref="ImGuiItemStatusFlags.Edited"/> is set.</returns>
    public static bool IsItemEdited()
    {
        ImGuiContext g = GetCurrentContext()!;
        return (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Edited) != 0;
    }

    /// <summary>
    /// Returns the identifier of the last submitted item.
    /// </summary>
    /// <returns>The last item identifier, or zero when the item had none.</returns>
    public static uint GetItemID()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.LastItemData.ID;
    }

    /// <summary>
    /// Returns the minimum corner of the last submitted item's rectangle.
    /// </summary>
    /// <returns>The item rectangle minimum in absolute screen coordinates.</returns>
    public static ImVec2 GetItemRectMin()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.LastItemData.Rect.Min;
    }

    /// <summary>
    /// Returns the maximum corner of the last submitted item's rectangle.
    /// </summary>
    /// <returns>The item rectangle maximum in absolute screen coordinates.</returns>
    public static ImVec2 GetItemRectMax()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.LastItemData.Rect.Max;
    }

    /// <summary>
    /// Returns the size of the last submitted item's rectangle.
    /// </summary>
    /// <returns>The item rectangle size.</returns>
    public static ImVec2 GetItemRectSize()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.LastItemData.Rect.GetSize();
    }

    /// <summary>
    /// Returns the item flags of the last submitted item.
    /// </summary>
    /// <returns>The last item's flags.</returns>
    public static ImGuiItemFlags GetItemFlags()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.LastItemData.ItemFlags;
    }

    /// <summary>
    /// Returns the status flags of the last submitted item.
    /// </summary>
    /// <returns>The last item's status flags.</returns>
    public static ImGuiItemStatusFlags GetItemStatusFlags()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.LastItemData.StatusFlags;
    }

    #endregion

    #region LAYOUT

    /// <summary>
    /// Returns to the previous line and continues items horizontally.
    /// </summary>
    /// <param name="offsetFromStartX">
    /// When non-zero, aligns to this x position relative to the window or group left edge. When zero, follows
    /// directly after the previous item.
    /// </param>
    /// <param name="spacing">
    /// The spacing to enforce. A negative value selects the default item spacing when
    /// <paramref name="offsetFromStartX"/> is zero, and no spacing otherwise.
    /// </param>
    /// <remarks>
    /// The previous line's size and baseline offset are restored so that subsequent items accumulate on one
    /// row, and <c>IsSameLine</c> is set so that <see cref="ItemSize(ImVec2, float)"/> measures from the
    /// previous line.
    /// </remarks>
    public static void SameLine(float offsetFromStartX = 0.0f, float spacing = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        ImVec2 cursorPos = window.DC.CursorPos;
        if (offsetFromStartX != 0.0f)
        {
            if (spacing < 0.0f)
                spacing = 0.0f;
            cursorPos.X = window.Pos.X - window.Scroll.X + offsetFromStartX + spacing + window.DC.GroupOffset.X + window.DC.ColumnsOffset.X;
            cursorPos.Y = window.DC.CursorPosPrevLine.Y;
        }
        else
        {
            if (spacing < 0.0f)
                spacing = g.Style.ItemSpacing.X;
            cursorPos.X = window.DC.CursorPosPrevLine.X + spacing;
            cursorPos.Y = window.DC.CursorPosPrevLine.Y;
        }
        window.DC.CursorPos = cursorPos;
        window.DC.CurrLineSize = window.DC.PrevLineSize;
        window.DC.CurrLineTextBaseOffset = window.DC.PrevLineTextBaseOffset;
        window.DC.IsSameLine = true;
    }

    /// <summary>
    /// Returns the cursor position in absolute screen coordinates without marking the window as written.
    /// </summary>
    /// <returns>The cursor position in absolute screen coordinates.</returns>
    public static ImVec2 GetCursorScreenPos()
    {
        ImGuiWindow window = GetCurrentWindowRead()!;
        return window.DC.CursorPos;
    }

    /// <summary>
    /// Sets the cursor position in absolute screen coordinates.
    /// </summary>
    /// <param name="pos">The new cursor position in absolute screen coordinates.</param>
    /// <remarks>
    /// Setting the cursor does not grow the content boundaries; a caller that needs to extend the parent
    /// boundary must submit an item such as <see cref="Dummy"/>.
    /// </remarks>
    public static void SetCursorScreenPos(ImVec2 pos)
    {
        ImGuiWindow window = GetCurrentWindow();
        window.DC.CursorPos = pos;
        window.DC.IsSetPos = true;
    }

    /// <summary>
    /// Returns the cursor position in window-local coordinates, including the scroll offset.
    /// </summary>
    /// <returns>The cursor position in window-local coordinates.</returns>
    public static ImVec2 GetCursorPos()
    {
        ImGuiWindow window = GetCurrentWindowRead()!;
        return window.DC.CursorPos - window.Pos + window.Scroll;
    }

    /// <summary>
    /// Returns the cursor's horizontal position in window-local coordinates.
    /// </summary>
    /// <returns>The horizontal cursor position in window-local coordinates.</returns>
    public static float GetCursorPosX()
    {
        ImGuiWindow window = GetCurrentWindowRead()!;
        return window.DC.CursorPos.X - window.Pos.X + window.Scroll.X;
    }

    /// <summary>
    /// Returns the cursor's vertical position in window-local coordinates.
    /// </summary>
    /// <returns>The vertical cursor position in window-local coordinates.</returns>
    public static float GetCursorPosY()
    {
        ImGuiWindow window = GetCurrentWindowRead()!;
        return window.DC.CursorPos.Y - window.Pos.Y + window.Scroll.Y;
    }

    /// <summary>
    /// Sets the cursor position from window-local coordinates.
    /// </summary>
    /// <param name="localPos">The new cursor position in window-local coordinates.</param>
    /// <remarks>
    /// Setting the cursor does not grow the content boundaries; a caller that needs to extend the parent
    /// boundary must submit an item such as <see cref="Dummy"/>.
    /// </remarks>
    public static void SetCursorPos(ImVec2 localPos)
    {
        ImGuiWindow window = GetCurrentWindow();
        window.DC.CursorPos = window.Pos - window.Scroll + localPos;
        window.DC.IsSetPos = true;
    }

    /// <summary>
    /// Sets the cursor's horizontal position from a window-local coordinate.
    /// </summary>
    /// <param name="localX">The new horizontal cursor position in window-local coordinates.</param>
    public static void SetCursorPosX(float localX)
    {
        ImGuiWindow window = GetCurrentWindow();
        ImVec2 cursorPos = window.DC.CursorPos;
        cursorPos.X = window.Pos.X - window.Scroll.X + localX;
        window.DC.CursorPos = cursorPos;
        window.DC.IsSetPos = true;
    }

    /// <summary>
    /// Sets the cursor's vertical position from a window-local coordinate.
    /// </summary>
    /// <param name="localY">The new vertical cursor position in window-local coordinates.</param>
    public static void SetCursorPosY(float localY)
    {
        ImGuiWindow window = GetCurrentWindow();
        ImVec2 cursorPos = window.DC.CursorPos;
        cursorPos.Y = window.Pos.Y - window.Scroll.Y + localY;
        window.DC.CursorPos = cursorPos;
        window.DC.IsSetPos = true;
    }

    /// <summary>
    /// Returns the cursor position captured at the start of the window's content.
    /// </summary>
    /// <returns>The content start position relative to the window origin.</returns>
    public static ImVec2 GetCursorStartPos()
    {
        ImGuiWindow window = GetCurrentWindowRead()!;
        return window.DC.CursorStartPos - window.Pos;
    }

    /// <summary>
    /// Increases the indentation and shifts the cursor to the new content left edge.
    /// </summary>
    /// <param name="indentW">The indentation to add, or zero to use the style's indent spacing.</param>
    public static void Indent(float indentW = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        ImVec1 indent = window.DC.Indent;
        indent.X += (indentW != 0.0f) ? indentW : g.Style.IndentSpacing;
        window.DC.Indent = indent;
        ImVec2 cursorPos = window.DC.CursorPos;
        cursorPos.X = window.Pos.X + indent.X + window.DC.ColumnsOffset.X;
        window.DC.CursorPos = cursorPos;
    }

    /// <summary>
    /// Decreases the indentation and shifts the cursor to the new content left edge.
    /// </summary>
    /// <param name="indentW">The indentation to remove, or zero to use the style's indent spacing.</param>
    /// <remarks>
    /// Canonical performs no clamping or underflow assertion for this operation.
    /// </remarks>
    public static void Unindent(float indentW = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        ImVec1 indent = window.DC.Indent;
        indent.X -= (indentW != 0.0f) ? indentW : g.Style.IndentSpacing;
        window.DC.Indent = indent;
        ImVec2 cursorPos = window.DC.CursorPos;
        cursorPos.X = window.Pos.X + indent.X + window.DC.ColumnsOffset.X;
        window.DC.CursorPos = cursorPos;
    }

    /// <summary>
    /// Queues an item width for the next submitted item.
    /// </summary>
    /// <param name="itemWidth">The item width; a negative value means "content region minus the magnitude".</param>
    /// <remarks>
    /// The queued width is consumed by <see cref="CalcItemWidth"/> and cleared by <see cref="ItemAdd"/>.
    /// </remarks>
    public static void SetNextItemWidth(float itemWidth)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasWidth;
        g.NextItemData.Width = itemWidth;
    }

    /// <summary>
    /// Pushes the current item width and sets a new one.
    /// </summary>
    /// <param name="itemWidth">The new item width, or zero to restore the window's default item width.</param>
    public static void PushItemWidth(float itemWidth = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        window.DC.ItemWidthStack.Add(window.DC.ItemWidth);
        window.DC.ItemWidth = (itemWidth == 0.0f) ? window.DC.ItemWidthDefault : itemWidth;
        g.NextItemData.HasFlags &= ~ImGuiNextItemDataFlags.HasWidth;
    }

    /// <summary>
    /// Pushes one item width per component, splitting the available width evenly while reserving inner
    /// spacing between components.
    /// </summary>
    /// <param name="components">The number of components to split the width across; must be positive.</param>
    /// <param name="widthFull">The full width to split, including the inner spacing between components.</param>
    /// <remarks>
    /// Widths are truncated to whole pixels from the last component backward, and each component keeps at
    /// least one pixel of width.
    /// </remarks>
    internal static void PushMultiItemsWidths(int components, float widthFull = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImAssert.Check(components > 0);

        ImGuiStyle style = g.Style;
        window.DC.ItemWidthStack.Add(window.DC.ItemWidth);
        float wItems = widthFull - style.ItemInnerSpacing.X * (components - 1);
        float prevSplit = wItems;
        for (int i = components - 1; i > 0; i--)
        {
            float nextSplit = (float)(int)(wItems * i / components);
            window.DC.ItemWidthStack.Add(MathF.Max(prevSplit - nextSplit, 1.0f));
            prevSplit = nextSplit;
        }
        window.DC.ItemWidth = MathF.Max(prevSplit, 1.0f);
        g.NextItemData.HasFlags &= ~ImGuiNextItemDataFlags.HasWidth;
    }

    /// <summary>
    /// Pops the most recently pushed item width.
    /// </summary>
    /// <remarks>
    /// Logs a recoverable user error and returns when the item-width stack is empty.
    /// </remarks>
    public static void PopItemWidth()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.DC.ItemWidthStack.Count <= 0)
        {
            ErrorLog("Calling PopItemWidth() too many times!");
            return;
        }
        window.DC.ItemWidth = window.DC.ItemWidthStack[^1];
        window.DC.ItemWidthStack.RemoveAt(window.DC.ItemWidthStack.Count - 1);
    }

    /// <summary>
    /// Calculates the default item width from the queued width or the current item width stack.
    /// </summary>
    /// <returns>The resolved item width, at least one pixel and truncated to a whole pixel.</returns>
    /// <remarks>
    /// A negative width means "available content region minus the magnitude".
    /// </remarks>
    public static float CalcItemWidth()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        float w;
        if ((g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasWidth) != 0)
            w = g.NextItemData.Width;
        else
            w = window.DC.ItemWidth;
        if (w < 0.0f)
        {
            float regionAvailX = GetContentRegionAvail().X;
            w = MathF.Max(1.0f, regionAvailX + w);
        }
        w = (float)(int)w;
        return w;
    }

    /// <summary>
    /// Calculates a full item size from a user-provided size and default width and height.
    /// </summary>
    /// <param name="size">The requested size; zero substitutes the default and negative values shrink the available region.</param>
    /// <param name="defaultW">The width substituted when the requested width is zero.</param>
    /// <param name="defaultH">The height substituted when the requested height is zero.</param>
    /// <returns>The resolved item size.</returns>
    /// <remarks>
    /// Negative sizes use a floor of 4.0f, which intentionally differs from the 1.0f floor used by
    /// <see cref="CalcItemWidth"/>; canonical documents the mismatch as harmless.
    /// </remarks>
    internal static ImVec2 CalcItemSize(ImVec2 size, float defaultW, float defaultH)
    {
        ImVec2 avail = default;
        if (size.X < 0.0f || size.Y < 0.0f)
            avail = GetContentRegionAvail();

        if (size.X == 0.0f)
            size.X = defaultW;
        else if (size.X < 0.0f)
            size.X = MathF.Max(4.0f, avail.X + size.X);

        if (size.Y == 0.0f)
            size.Y = defaultH;
        else if (size.Y < 0.0f)
            size.Y = MathF.Max(4.0f, avail.Y + size.Y);

        return size;
    }

    /// <summary>
    /// Pushes the current text wrap position and sets a new one, in legacy window-local space.
    /// </summary>
    /// <param name="wrapLocalPosX">The wrap position in legacy window-local space; zero uses the work-area right edge.</param>
    public static void PushTextWrapPos(float wrapLocalPosX = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        window.DC.TextWrapPosStack.Add(window.DC.TextWrapPos);
        window.DC.TextWrapPos = wrapLocalPosX;
    }

    /// <summary>
    /// Pops the most recently pushed text wrap position.
    /// </summary>
    /// <remarks>
    /// Logs a recoverable user error and returns when the text-wrap stack is empty.
    /// </remarks>
    public static void PopTextWrapPos()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.DC.TextWrapPosStack.Count <= 0)
        {
            ErrorLog("Calling PopTextWrapPos() too many times!");
            return;
        }
        window.DC.TextWrapPos = window.DC.TextWrapPosStack[^1];
        window.DC.TextWrapPosStack.RemoveAt(window.DC.TextWrapPosStack.Count - 1);
    }

    /// <summary>
    /// Ends the current line and advances the cursor vertically.
    /// </summary>
    /// <remarks>
    /// The layout type is forced vertical while the line is closed. When the current line already holds
    /// items that are shorter than the font, its height is preserved; otherwise one font height is reserved.
    /// </remarks>
    public static void NewLine()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiLayoutType backupLayoutType = window.DC.LayoutType;
        window.DC.LayoutType = ImGuiLayoutType.Vertical;
        window.DC.IsSameLine = false;
        if (window.DC.CurrLineSize.Y > 0.0f)
            ItemSize(new ImVec2(0.0f, 0.0f));
        else
            ItemSize(new ImVec2(0.0f, g.FontSize));
        window.DC.LayoutType = backupLayoutType;
    }

    /// <summary>
    /// Advances the cursor by the normal item spacing without submitting an item.
    /// </summary>
    public static void Spacing()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;
        ItemSize(new ImVec2(0.0f, 0.0f));
    }

    /// <summary>
    /// Submits a layout-only item of the given size with no identifier.
    /// </summary>
    /// <param name="size">The size to reserve.</param>
    /// <remarks>
    /// Because the identifier is zero, the item interacts with nothing, but it still grows the content
    /// boundaries and is the canonical way to extend a parent boundary after moving the cursor.
    /// </remarks>
    public static void Dummy(ImVec2 size)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + size);
        ItemSize(size);
        ItemAdd(bb, 0);
    }

    /// <summary>
    /// Grows the current line's height and baseline offset to match a framed widget, so that labels on the
    /// same line align with framed controls.
    /// </summary>
    public static void AlignTextToFramePadding()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        window.DC.CurrLineSize = new ImVec2(
            window.DC.CurrLineSize.X,
            MathF.Max(window.DC.CurrLineSize.Y, g.FontSize + g.Style.FramePadding.Y * 2.0f));
        window.DC.CurrLineTextBaseOffset = MathF.Max(window.DC.CurrLineTextBaseOffset, g.Style.FramePadding.Y);
    }

    /// <summary>
    /// Tests whether a rectangle at the current cursor with the given size is visible under the window clip
    /// rectangle.
    /// </summary>
    /// <param name="size">The size of the rectangle starting at the cursor position.</param>
    /// <returns>True when the rectangle overlaps the logical window clip rectangle.</returns>
    public static bool IsRectVisible(ImVec2 size)
    {
        ImGuiWindow window = GetCurrentContext()!.CurrentWindow!;
        return window.ClipRect.Overlaps(new ImRect(window.DC.CursorPos, window.DC.CursorPos + size));
    }

    /// <summary>
    /// Tests whether an explicit rectangle is visible under the window clip rectangle.
    /// </summary>
    /// <param name="rectMin">The rectangle minimum in absolute screen coordinates.</param>
    /// <param name="rectMax">The rectangle maximum in absolute screen coordinates.</param>
    /// <returns>True when the rectangle overlaps the logical window clip rectangle.</returns>
    /// <remarks>
    /// This uses the logical <c>window.ClipRect</c> maintained by <c>PushClipRect</c>, not the draw list.
    /// </remarks>
    public static bool IsRectVisible(ImVec2 rectMin, ImVec2 rectMax)
    {
        ImGuiWindow window = GetCurrentContext()!.CurrentWindow!;
        return window.ClipRect.Overlaps(new ImRect(rectMin, rectMax));
    }

    #endregion

    #region ITEM FLAGS AND NEXT-ITEM DATA

    /// <summary>
    /// Pushes the current item flags and applies an item-flag option.
    /// </summary>
    /// <param name="option">The item flag to set or clear.</param>
    /// <param name="enabled">True to set the flag, false to clear it.</param>
    /// <remarks>
    /// The invariant <c>CurrentItemFlags == ItemFlagsStack[^1]</c> is maintained; the assert verifies the
    /// stack was not corrupted by direct assignment.
    /// </remarks>
    public static void PushItemFlag(ImGuiItemFlags option, bool enabled)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiItemFlags itemFlags = g.CurrentItemFlags;
        ImAssert.Check(itemFlags == g.ItemFlagsStack[^1]);
        if (enabled)
            itemFlags |= option;
        else
            itemFlags &= ~option;
        g.CurrentItemFlags = itemFlags;
        g.ItemFlagsStack.Add(itemFlags);
    }

    /// <summary>
    /// Pops the most recent item-flag push and restores the previous flags.
    /// </summary>
    /// <remarks>
    /// Logs a recoverable user error and returns when the item-flag stack would be emptied; canonical keeps a
    /// base entry at all times.
    /// </remarks>
    public static void PopItemFlag()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.ItemFlagsStack.Count <= 1)
        {
            ErrorLog("Calling PopItemFlag() too many times!");
            return;
        }
        g.ItemFlagsStack.RemoveAt(g.ItemFlagsStack.Count - 1);
        g.CurrentItemFlags = g.ItemFlagsStack[^1];
    }

    /// <summary>
    /// Enables or disables button repeat for subsequently submitted items.
    /// </summary>
    /// <param name="repeat">True to enable button repeat, false to disable it.</param>
    /// <remarks>
    /// Canonical defines this as a header-inline call to
    /// <c>PushItemFlag(ImGuiItemFlags_ButtonRepeat, repeat)</c>; the repeat flag is consumed by
    /// <c>ButtonBehavior</c> and button-like widgets.
    /// </remarks>
    public static void PushButtonRepeat(bool repeat)
        => PushItemFlag(ImGuiItemFlags.ButtonRepeat, repeat);

    /// <summary>
    /// Restores the button-repeat state saved by <see cref="PushButtonRepeat"/>.
    /// </summary>
    public static void PopButtonRepeat()
        => PopItemFlag();

    /// <summary>
    /// Queues the open state applied to the next tree node or collapsing header.
    /// </summary>
    /// <param name="isOpen">The open state to apply.</param>
    /// <param name="cond">The condition under which the state applies; zero means always.</param>
    public static void SetNextItemOpen(bool isOpen, ImGuiCond cond = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.CurrentWindow!.SkipItems)
            return;
        g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasOpen;
        g.NextItemData.OpenVal = isOpen;
        g.NextItemData.OpenCond = (byte)(cond != 0 ? (int)cond : (int)ImGuiCond.Always);
    }

    /// <summary>
    /// Queues the storage identifier used by the next tree node or collapsing header.
    /// </summary>
    /// <param name="storageId">The storage identifier for the next item's persistent open state.</param>
    public static void SetNextItemStorageID(uint storageId)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.CurrentWindow!.SkipItems)
            return;
        g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasStorageID;
        g.NextItemData.StorageId = storageId;
    }

    /// <summary>
    /// Replaces the current window's state storage with a caller-owned store, or restores the window's
    /// own store when <paramref name="storage"/> is <see langword="null"/>
    /// </summary>
    /// <param name="storage">The storage to use, or <see langword="null"/> to restore the window's
    /// default store. The caller retains ownership of a supplied store.</param>
    /// <remarks>
    /// Every widget that reads persistent state through <c>window.DC.StateStorage</c> is redirected
    /// until the caller restores the default. The window's own store is never overwritten.
    /// </remarks>
    public static void SetStateStorage(ImGuiStorage? storage)
    {
        ImGuiWindow window = GetContext().CurrentWindow!;
        window.DC.StateStorage = storage ?? window.StateStorage;
    }

    /// <summary>
    /// Returns the current window's active state storage
    /// </summary>
    /// <returns>
    /// The store installed by <see cref="SetStateStorage"/>, or the window's own store when none was
    /// installed.
    /// </returns>
    public static ImGuiStorage GetStateStorage()
    {
        ImGuiWindow window = GetContext().CurrentWindow!;
        return window.DC.StateStorage!;
    }

    /// <summary>
    /// Allows the next submitted item to be overlapped by subsequently submitted items.
    /// </summary>
    /// <remarks>
    /// Overlap is resolved by requiring the hovered identifier to match for two consecutive frames, so an
    /// item submitted later naturally takes priority. The queued flag is consumed by
    /// <see cref="ItemAdd"/> through the next-item flag set.
    /// </remarks>
    public static void SetNextItemAllowOverlap()
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NextItemData.ItemFlagsSet |= ItemFlagAllowOverlap;
    }

    /// <summary>
    /// Requests that the last submitted item become the default navigation focus if the window is appearing
    /// and no better initialization result exists.
    /// </summary>
    /// <remarks>
    /// The request only applies on the appearing frame of the focused navigation root, in the current
    /// navigation layer. When the last item lies outside the clip rectangle, it is scrolled into view.
    /// </remarks>
    public static void SetItemDefaultFocus()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (!window.Appearing)
            return;
        if (g.NavWindow != window.RootWindowForNav || (!g.NavInitRequest && g.NavInitResult.ID == 0) || g.NavLayer != window.DC.NavLayerCurrent)
            return;

        g.NavInitRequest = false;
        NavApplyItemToResult(g.NavInitResult);
        NavUpdateAnyRequestFlag();

        // Scrolling is not part of the regular initialization path, so it is requested explicitly here.
        if (!window.ClipRect.Contains(g.LastItemData.Rect))
            _ = ScrollToRectEx(window, g.LastItemData.Rect, ImGuiScrollFlags.None);
    }

    #endregion

    #region TEXT METRICS AND WRAPPING

    /// <summary>
    /// Calculates the pixel size of a text run, optionally ignoring content after a <c>##</c> marker and
    /// optionally wrapping at a given width.
    /// </summary>
    /// <param name="text">The text to measure; may contain <c>##</c> to separate display text from an identifier.</param>
    /// <param name="hideTextAfterDoubleHash">True to stop measuring at the first <c>##</c> marker.</param>
    /// <param name="wrapWidth">The wrap position in window-local space, or a negative value to disable wrapping.</param>
    /// <returns>The measured text size; an empty display run returns zero width and one font height.</returns>
    /// <remarks>
    /// The width is rounded up with <c>MathF.Ceiling</c>, matching canonical <c>ImCeilFast</c>.
    /// </remarks>
    public static ImVec2 CalcTextSize(string text, bool hideTextAfterDoubleHash = false, float wrapWidth = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        string textDisplayEnd = hideTextAfterDoubleHash ? FindRenderedTextEnd(text) : text;

        ImFont font = g.Font!;
        float fontSize = g.FontSize;
        if (textDisplayEnd.Length == 0)
            return new ImVec2(0.0f, fontSize);

        ImVec2 textSize = font.CalcTextSizeA(fontSize, float.MaxValue, wrapWidth, textDisplayEnd.AsSpan());

        // The horizontal size is rounded up; see canonical notes on glyph advance accumulation.
        textSize.X = MathF.Ceiling(textSize.X);
        return textSize;
    }

    /// <summary>
    /// Measures a text run with explicit font size and wrapping parameters.
    /// </summary>
    /// <param name="size">The font size to measure with.</param>
    /// <param name="maxWidth">The maximum width before an ellipsis or clipping is applied.</param>
    /// <param name="wrapWidth">The wrap width, or zero to disable wrapping.</param>
    /// <param name="text">The UTF-16 text run to measure.</param>
    /// <returns>The measured text size.</returns>
    /// <remarks>
    /// Canonical declares this as <c>ImFont::CalcTextSizeA</c>, not as an <c>ImGui::</c> member; the binding
    /// contract lists it under part 04, so this port provides a thin delegation to the current font's
    /// implementation and reuses its text-metric derivation rather than duplicating it.
    /// </remarks>
    public static ImVec2 CalcTextSizeA(float size, float maxWidth, float wrapWidth, ReadOnlySpan<char> text)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImFont font = g.Font!;
        return font.CalcTextSizeA(size, maxWidth, wrapWidth, text);
    }

    /// <summary>
    /// Returns the portion of a text run that is rendered, stopping before the first <c>##</c> marker.
    /// </summary>
    /// <param name="text">The text run, which may embed an identifier after <c>##</c>.</param>
    /// <returns>The displayable prefix of the text run.</returns>
    /// <remarks>
    /// Canonical returns a pointer into the caller's buffer; this port returns the corresponding managed
    /// substring. A run beginning with <c>##</c> yields an empty display string, matching the canonical
    /// pointer-equality test in <see cref="CalcTextSize"/>.
    /// </remarks>
    public static string FindRenderedTextEnd(string text)
    {
        int end = 0;
        int length = text.Length;
        // Reading text[end + 1] is guarded because managed strings are not NUL-terminated.
        while (end < length && text[end] != '\0' && (end + 1 >= length || text[end] != '#' || text[end + 1] != '#'))
            end++;
        return text[..end];
    }

    /// <summary>
    /// Converts a text wrap position into a wrap width in absolute coordinates.
    /// </summary>
    /// <param name="pos">The absolute position from which wrapping is measured.</param>
    /// <param name="wrapPosX">The legacy window-local wrap position; zero uses the work-area right edge.</param>
    /// <returns>The wrap width, at least one pixel; zero when wrapping is disabled.</returns>
    /// <remarks>
    /// A negative <paramref name="wrapPosX"/> disables wrapping. Positive values are converted from legacy
    /// window-local space to absolute coordinates.
    /// </remarks>
    internal static float CalcWrapWidthForPos(ImVec2 pos, float wrapPosX)
    {
        if (wrapPosX < 0.0f)
            return 0.0f;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (wrapPosX == 0.0f)
        {
            wrapPosX = window.WorkRect.Max.X;
        }
        else if (wrapPosX > 0.0f)
        {
            wrapPosX += window.Pos.X - window.Scroll.X;
        }

        return MathF.Max(wrapPosX - pos.X, 1.0f);
    }

    #endregion

    #region PRIVATE HELPERS

    /// <summary>
    /// Item flag value for <c>ImGuiItemFlags_AllowOverlap</c>, folded from
    /// <see cref="ImGuiItemFlagsPrivate"/> into <see cref="ImGuiItemFlags"/>.
    /// </summary>
    private const ImGuiItemFlags ItemFlagAllowOverlap = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_AllowOverlap;

    /// <summary>
    /// Item flag value for <c>ImGuiItemFlags_NoWindowHoverableCheck</c>, folded from
    /// <see cref="ImGuiItemFlagsPrivate"/> into <see cref="ImGuiItemFlags"/>.
    /// </summary>
    private const ImGuiItemFlags ItemFlagNoWindowHoverableCheck = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoWindowHoverableCheck;

    /// <summary>
    /// Item flag value for <c>ImGuiItemFlags_NoNavDisableMouseHover</c>, folded from
    /// <see cref="ImGuiItemFlagsPrivate"/> into <see cref="ImGuiItemFlags"/>.
    /// </summary>
    private const ImGuiItemFlags ItemFlagNoNavDisableMouseHover = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoNavDisableMouseHover;

    /// <summary>
    /// Resolves the hover delay implied by the hovered flags.
    /// </summary>
    /// <param name="flags">The hovered flags to interpret.</param>
    /// <returns>The delay in seconds, or zero when no delay flag is present.</returns>
    private static float CalcDelayFromHoveredFlags(ImGuiHoveredFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((flags & ImGuiHoveredFlags.DelayNormal) != 0)
            return g.Style.HoverDelayNormal;
        if ((flags & ImGuiHoveredFlags.DelayShort) != 0)
            return g.Style.HoverDelayShort;
        return 0.0f;
    }

    #endregion

    #endregion


    #region SECTION: ITEM INTERACTION

    #region Widgets: Main (ButtonBehavior)

    /// <summary>
    /// Time, in seconds, that a drag-and-drop payload must be held over a compatible item before
    /// <see cref="ButtonBehavior"/> reports the press that opens it.
    /// </summary>
    /// <remarks>
    /// Ports the file-scope constant <c>DRAGDROP_HOLD_TO_OPEN_TIMER</c> declared at
    ///. The canonical value is <c>0.70f</c> and the
    /// constant is read only by <see cref="ButtonBehavior"/>., so it is
    /// owned by this part rather than by the drag-and-drop subsystem of Chunk 8.
    /// </remarks>
    private const float DragDropHoldToOpenTimer = 0.70f;

    /// <summary>
    /// Resolves the hovered, held, and pressed state of a button-like item from mouse, keyboard, and
    /// gamepad input, and advances the active-ID lifecycle accordingly.
    /// </summary>
    /// <param name="bb">The interactive bounding box of the item, in screen coordinates.</param>
    /// <param name="id">The item identifier. The canonical body asserts this is non-zero on the hovered path.</param>
    /// <param name="outHovered">
    /// Receives whether the item is hovered. Navigation-focused and drag-hold items report as hovered
    /// without writing <c>g.HoveredId</c>; the activation highlight also forces this to true.
    /// </param>
    /// <param name="outHeld">
    /// Receives whether the item is currently held: the mouse button is down on the active item, or the
    /// navigation activation key is still held for a keyboard/gamepad activation.
    /// </param>
    /// <param name="flags">
    /// Button and private button flags. Public bits come from <see cref="ImGuiButtonFlags"/> and internal
    /// bits from <see cref="ImGuiButtonFlagsPrivate"/>; the canonical single bit field is reconstructed in
    /// the local <c>f</c> variable because this port splits those bits across two enum types.
    /// </param>
    /// <returns>True when the item produced a press this frame.</returns>
    /// <remarks>
    /// The default behavior is click plus
    /// release inside <paramref name="bb"/>; <see cref="ImGuiItemFlags.ButtonRepeat"/> promotes the default
    /// to press-on-click and makes repeat presses trump the release behavior. Every canonical branch is
    /// ported in canonical order.
    /// <para>
    /// Boundary handling. The canonical <c>IM_ASSERT(id != 0)</c>. becomes a
    /// <see cref="ImAssert.Check(bool, string)"/> because it guards programmer error in a rare path. The canonical
    /// repeat path reads <c>g.IO.MouseDownDuration[g.ActiveIdMouseButton]</c> without checking the recorded
    /// button index; an additional <c>g.ActiveIdMouseButton &gt;= 0</c> guard prevents an out-of-range index
    /// for a programmatically activated item whose button was never recorded.
    /// </para>
    /// <para>
    /// The canonical <c>IMGUI_ENABLE_TEST_ENGINE</c> registration block.
    /// is compiled out of the default canonical build and is therefore not ported; no test-engine behavior
    /// is dropped from the shipped configuration.
    /// </para>
    /// <para>
    /// The drag-and-drop hold branch reads <c>g.DragDropActive</c>, <c>g.DragDropSourceFlags</c>,
    /// <c>g.DragDropAcceptIdPrev</c>, <c>g.DragDropAcceptFlagsPrev</c>, and
    /// <c>g.DragDropHoldJustPressedId</c>, all of which are declared by <c>Hikari.ImGui/ImGuiInternal.cs</c>
    /// and populated by the drag-and-drop subsystem, so the branch is live and preserves the canonical
    /// condition structure and hold timer.
    /// </para>
    /// </remarks>
    internal static bool ButtonBehavior(ImRect bb, uint id, out bool outHovered, out bool outHeld, ImGuiButtonFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();

        // Reconstruct the canonical single ImGuiButtonFlags bit field. Public and private bits live in two
        // enum types in this port, so all merging happens on the integer representation.
        int f = (int)flags;

        // Default behavior inherited from item flags. Both button flags and item flags are valid sources,
        // so copy the item flags into one value and only test that.
        ImGuiItemFlags itemFlags = g.LastItemData.ID == id ? g.LastItemData.ItemFlags : g.CurrentItemFlags;
        if ((f & (int)ImGuiButtonFlags.AllowOverlap) != 0)
            itemFlags |= (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_AllowOverlap;
        if ((itemFlags & (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoFocus) != 0)
            f |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoFocus | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus;

        // Default only reacts to the left mouse button.
        if ((f & (int)ImGuiButtonFlags.MouseButtonMask_) == 0)
            f |= (int)ImGuiButtonFlags.MouseButtonLeft;

        // Default behavior requires click plus release inside the bounding box. Repeat items press on click.
        if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnMask_) == 0)
            f |= (itemFlags & ImGuiItemFlags.ButtonRepeat) != 0
                ? (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick
                : (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDefault_;

        // Temporarily flatten the hovered window so a hovered child does not block its owning parent when
        // the button explicitly allows overlap across the shared dock tree root.
        ImGuiWindow? backupHoveredWindow = g.HoveredWindow;
        bool flattenHoveredChildren = (f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_FlattenChildren) != 0
            && g.HoveredWindow != null
            && g.HoveredWindow.RootWindowDockTree == window.RootWindowDockTree;
        if (flattenHoveredChildren)
            g.HoveredWindow = window;

        // Canonical registers an alternate test-engine item here when the caller did not use ItemAdd();
        // the block is compiled out of the default build and is intentionally not ported.

        bool pressed = false;
        bool hovered = ItemHoverable(bb, id, itemFlags);

        // Special mode for drag and drop used by openables (tree nodes, tabs): holding the button pressed
        // for a long time while dragging a payload triggers the button.
        if (g.DragDropActive)
        {
            if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDragDropHold) != 0
                && (g.DragDropSourceFlags & ImGuiDragDropFlags.SourceNoHoldToOpenOthers) == 0
                && IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            {
                hovered = true;
                SetHoveredID(id);
                if (g.HoveredIdTimer - g.IO.DeltaTime <= DragDropHoldToOpenTimer && g.HoveredIdTimer >= DragDropHoldToOpenTimer)
                {
                    pressed = true;
                    g.DragDropHoldJustPressedId = id;
                    FocusWindow(window);
                }
            }
            if (g.DragDropAcceptIdPrev == id && (g.DragDropAcceptFlagsPrev & ImGuiDragDropFlags.AcceptDrawAsHovered) != 0)
                hovered = true;
        }

        if (flattenHoveredChildren)
            g.HoveredWindow = backupHoveredWindow;

        // Mouse handling.
        // The owner is normally the item id; NoTestKeyOwner relaxes it to ImGuiKeyOwner_Any, which accepts
        // any owner unless the key was explicitly locked.
        uint testOwnerId = (f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoTestKeyOwner) != 0 ? ImGuiKeyOwner_Any : id;
        if (hovered)
        {
            ImAssert.Check(id != 0); // Lazily checked inside this rare path.

            // Poll mouse buttons. mouse_button_clicked is generally carried into ActiveIdMouseButton when
            // setting the active id; both values are recorded for the first matching button only.
            int mouseButtonClicked = -1;
            int mouseButtonReleased = -1;
            for (int button = 0; button < 3; button++)
            {
                if ((f & ((int)ImGuiButtonFlags.MouseButtonLeft << button)) != 0)
                {
                    if (IsMouseClicked((ImGuiMouseButton)button, ImGuiInputFlags.None, testOwnerId) && mouseButtonClicked == -1)
                        mouseButtonClicked = button;
                    if (IsMouseReleased((ImGuiMouseButton)button, testOwnerId) && mouseButtonReleased == -1)
                        mouseButtonReleased = button;
                }
            }

            // Process the initial action.
            bool modsOk = (f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoKeyModsAllowed) == 0
                || (!g.IO.KeyCtrl && !g.IO.KeyShift && !g.IO.KeyAlt);
            if (modsOk)
            {
                if (mouseButtonClicked != -1 && g.ActiveId != id)
                {
                    if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoSetKeyOwner) == 0)
                        SetKeyOwner(MouseButtonToKey((ImGuiMouseButton)mouseButtonClicked), id);

                    if ((f & ((int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease
                            | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickReleaseAnywhere)) != 0)
                    {
                        SetActiveID(id, window);
                        g.ActiveIdMouseButton = (sbyte)mouseButtonClicked;
                        if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus) == 0)
                        {
                            SetFocusID(id, window);
                            FocusWindow(window);
                        }
                        else if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoFocus) == 0)
                        {
                            // Still focus and bring to front, but try to avoid losing NavId when navigating a child.
                            FocusWindow(window, ImGuiFocusRequestFlags.RestoreFocusedChild);
                        }
                    }

                    if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick) != 0
                        || ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDoubleClick) != 0
                            && g.IO.MouseClickedCount[mouseButtonClicked] == 2))
                    {
                        pressed = true;
                        if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoHoldingActiveId) != 0)
                            ClearActiveID();
                        else
                            SetActiveID(id, window); // Hold on the ID.
                        g.ActiveIdMouseButton = (sbyte)mouseButtonClicked;
                        if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus) == 0)
                        {
                            SetFocusID(id, window);
                            FocusWindow(window);
                        }
                        else if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoFocus) == 0)
                        {
                            FocusWindow(window, ImGuiFocusRequestFlags.RestoreFocusedChild);
                        }
                    }

                    if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnRelease) != 0)
                    {
                        // PressedOnRelease retains the active id since 2026-03-20; NoHoldingActiveId opts out.
                        if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoHoldingActiveId) == 0)
                            SetActiveID(id, window); // Hold on the ID.
                        g.ActiveIdMouseButton = (sbyte)mouseButtonClicked;
                    }
                }

                if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnRelease) != 0)
                {
                    if (mouseButtonReleased != -1)
                    {
                        // Repeat mode trumps the release behavior.
                        bool hasRepeatedAtLeastOnce = (itemFlags & ImGuiItemFlags.ButtonRepeat) != 0
                            && g.IO.MouseDownDurationPrev[mouseButtonReleased] >= g.IO.KeyRepeatDelay;
                        if (!hasRepeatedAtLeastOnce)
                            pressed = true;
                        if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus) == 0)
                            SetFocusID(id, window); // Canonical omits FocusWindow() here; kept for parity.
                        ClearActiveID();
                    }
                }

                // Repeat mode acts while held regardless of the PressedOn flags. It relies on the repeat logic
                // of IsMouseClicked().
                if (g.ActiveId == id && (itemFlags & ImGuiItemFlags.ButtonRepeat) != 0)
                {
                    // Boundary guard: an item activated programmatically may not have a recorded mouse button.
                    if (g.ActiveIdMouseButton >= 0
                        && g.IO.MouseDownDuration[g.ActiveIdMouseButton] > 0.0f
                        && IsMouseClicked((ImGuiMouseButton)g.ActiveIdMouseButton, ImGuiInputFlags.Repeat, testOwnerId))
                        pressed = true;
                }
            }

            if (pressed && g.IO.ConfigNavCursorVisibleAuto)
                g.NavCursorVisible = false;
        }

        // Keyboard/gamepad navigation handling. Navigated and navigation-activated items report as hovered,
        // but g.HoveredId is not written so mouse hover is not disturbed.
        if ((itemFlags & ImGuiItemFlags.Disabled) == 0)
        {
            if (g.NavId == id && g.NavCursorVisible && g.NavHighlightItemUnderNav)
                if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoHoveredOnFocus) == 0)
                    hovered = true;
            if (g.NavActivateDownId == id)
            {
                bool navActivatedByCode = g.NavActivateId == id;
                bool navActivatedByInputs = g.NavActivatePressedId == id;
                if (!navActivatedByInputs && (itemFlags & ImGuiItemFlags.ButtonRepeat) != 0)
                {
                    // Avoid multiple keys triggering an excessive amount of repeat events: take the largest
                    // down duration among Space, Enter, and the navigation gamepad activate key.
                    // ImGuiKey_NavGamepadActivate is the canonical macro at.
                    ImGuiKeyData key1 = GetKeyData(ImGuiKey.Space);
                    ImGuiKeyData key2 = GetKeyData(ImGuiKey.Enter);
                    ImGuiKeyData key3 = GetKeyData(g.IO.ConfigNavSwapGamepadButtons ? ImGuiKey.GamepadFaceRight : ImGuiKey.GamepadFaceDown);
                    float t1 = MathF.Max(MathF.Max(key1.DownDuration, key2.DownDuration), key3.DownDuration);
                    navActivatedByInputs = CalcTypematicRepeatAmount(t1 - g.IO.DeltaTime, t1, g.IO.KeyRepeatDelay, g.IO.KeyRepeatRate) > 0;
                }
                if (navActivatedByCode || navActivatedByInputs)
                {
                    // Set the active id so it can be queried via IsItemActive(), equivalent to holding the
                    // mouse button down.
                    pressed = true;
                    SetActiveID(id, window);
                    g.ActiveIdSource = g.NavInputSource;
                    if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus) == 0
                        && (g.NavActivateFlags & ImGuiActivateFlags.FromShortcut) == 0)
                        SetFocusID(id, window);
                    if ((g.NavActivateFlags & ImGuiActivateFlags.FromShortcut) != 0)
                        g.ActiveIdFromShortcut = true;
                }
            }
        }

        // Process while held.
        bool held = false;
        if (g.ActiveId == id)
        {
            if (g.ActiveIdSource == ImGuiInputSource.Mouse)
            {
                if (g.ActiveIdIsJustActivated)
                    g.ActiveIdClickOffset = g.IO.MousePos - bb.Min;

                int mouseButton = g.ActiveIdMouseButton;
                if (mouseButton == -1)
                {
                    // Fallback for the rare situation where g.ActiveId was set programmatically or by
                    // another widget (e.g. #6304).
                    ClearActiveID();
                }
                else if (IsMouseDown((ImGuiMouseButton)mouseButton, testOwnerId))
                {
                    held = true;
                }
                else
                {
                    bool releaseIn = hovered && (f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease) != 0;
                    bool releaseAnywhere = (f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickReleaseAnywhere) != 0;
                    if ((releaseIn || releaseAnywhere) && !g.DragDropActive)
                    {
                        // Report as pressed when releasing the mouse (the most common path).
                        bool isDoubleClickRelease = (f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDoubleClick) != 0
                            && g.IO.MouseReleased[mouseButton]
                            && g.IO.MouseClickedLastCount[mouseButton] == 2;
                        bool isRepeatingAlready = (itemFlags & ImGuiItemFlags.ButtonRepeat) != 0
                            && g.IO.MouseDownDurationPrev[mouseButton] >= g.IO.KeyRepeatDelay; // Repeat trumps release.
                        bool isButtonAvailOrOwned = TestKeyOwner(MouseButtonToKey((ImGuiMouseButton)mouseButton), testOwnerId);
                        if (!isDoubleClickRelease && !isRepeatingAlready && isButtonAvailOrOwned)
                            pressed = true;
                    }
                    ClearActiveID();
                }
                if ((f & (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus) == 0 && g.IO.ConfigNavCursorVisibleAuto)
                    g.NavCursorVisible = false;
            }
            else if (g.ActiveIdSource == ImGuiInputSource.Keyboard || g.ActiveIdSource == ImGuiInputSource.Gamepad)
            {
                // When activated using navigation, the active id is held until the activation button is
                // released.
                if (g.NavActivateDownId == id)
                    held = true; // Hovered is not reported as true because the item is already directly activated.
                else
                    ClearActiveID();
            }
            if (pressed)
                g.ActiveIdHasBeenPressedBefore = true;
        }

        // Activation highlight; this may be a remote activation.
        if (g.NavHighlightActivatedId == id && (itemFlags & ImGuiItemFlags.Disabled) == 0)
            hovered = true;

        outHovered = hovered;
        outHeld = held;

        return pressed;
    }

    #endregion

    #region Main Code (Active/Hovered ID State)

    /// <summary>
    /// Sets the active item identifier and completes the deactivation bookkeeping for the outgoing item.
    /// </summary>
    /// <param name="id">The identifier to activate, or zero to clear the active id.</param>
    /// <param name="window">
    /// The window that owns the item, stored as <c>g.ActiveIdWindow</c>. Canonical passes null only from
    /// <see cref="ClearActiveID"/>.
    /// </param>
    /// <remarks>
    /// When the previous active id was non-zero the
    /// deactivation record is filled from the last submitted item, the text-input deactivation hook is
    /// invoked for an active text field, and the mouse-moving window is stopped if it owned the active id. When the id actually changes,
    /// <c>g.ActiveIdIsJustActivated</c> is raised and the per-activation counters are reset. The input claims
    /// recorded by the widget are cleared last.
    /// <para>
    /// The canonical <c>IM_ASSERT(g.ActiveIdSource != ImGuiInputSource_None)</c> at
    ///  becomes a <see cref="ImAssert.Check(bool, string)"/> because it guards programmer error.
    /// </para>
    /// <para>
    /// The canonical guard <c>if (g.InputTextState.ID == g.ActiveId) InputTextDeactivateHook(g.ActiveId);</c>
    ///. runs before the id changes, so an edited text field writes back on the
    /// frame it deactivates.
    /// </para>
    /// </remarks>
    internal static void SetActiveID(uint id, ImGuiWindow? window)
    {
        ImGuiContext g = GetCurrentContext()!;

        // Clear the previous active id.
        if (g.ActiveId != 0)
        {
            // Store deactivate data.
            ImGuiDeactivatedItemData deactivatedData = g.DeactivatedItemData;
            deactivatedData.ID = g.ActiveId;
            deactivatedData.ElapseFrame = g.LastItemData.ID == g.ActiveId ? g.FrameCount : g.FrameCount + 1;
            deactivatedData.HasBeenEditedBefore = g.ActiveIdHasBeenEditedBefore;
            deactivatedData.IsAlive = g.ActiveIdIsAlive == g.ActiveId;

            // Reapply edited text to the caller's buffer on the deactivation frame.
            // A text field that was edited and then loses the active id, for example when a button takes
            // focus, must still write its retained text back exactly once.
            if (g.InputTextState.ID == g.ActiveId)
                InputTextDeactivateHook(g.ActiveId);

            // While most code avoids stealing the active id during a window move or drag, remain resilient
            // when it does happen.
            if (g.MovingWindow != null && g.ActiveId == g.MovingWindow.MoveId)
                StopMouseMovingWindow();
        }

        // Set the active id.
        g.ActiveIdIsJustActivated = g.ActiveId != id;
        if (g.ActiveIdIsJustActivated)
        {
            g.ActiveIdTimer = 0.0f;
            g.ActiveIdHasBeenPressedBefore = false;
            g.ActiveIdHasBeenEditedBefore = false;
            g.ActiveIdMouseButton = -1;
            if (id != 0)
            {
                g.LastActiveId = id;
                g.LastActiveIdTimer = 0.0f;
            }
        }
        g.ActiveId = id;
        g.ActiveIdAllowOverlap = false;
        g.ActiveIdNoClearOnFocusLoss = false;
        g.ActiveIdWindow = window;
        g.ActiveIdHasBeenEditedThisFrame = false;
        g.ActiveIdFromShortcut = false;
        g.ActiveIdDisabledId = 0;
        if (id != 0)
        {
            g.ActiveIdIsAlive = id;
            // A mouse click activates its item as a mouse interaction, even when the navigation layer
            // recorded the same item as "just moved to" during the pointer's own navigation update.
            // Canonical reaches the same result because mouse-driven navigation never raises a move
            // result, so `NavJustMovedToId` is unset for a pointer activation
            // hovered item, so the mouse buttons are consulted explicitly to keep drag and slider
            // widgets responsive to the pointer.
            bool activatedByMouse = g.IO.MouseClicked[0] || g.IO.MouseClicked[1] || g.IO.MouseClicked[2]
                || g.IO.MouseClicked[3] || g.IO.MouseClicked[4];
            g.ActiveIdSource = !activatedByMouse && (g.NavActivateId == id || g.NavJustMovedToId == id)
                ? g.NavInputSource
                : ImGuiInputSource.Mouse;
            ImAssert.Check(g.ActiveIdSource != ImGuiInputSource.None);
        }

        // Clear the declaration of inputs claimed by the widget. Not all keys and inputs are declared by all
        // widgets yet, matching canonical.
        g.ActiveIdUsingNavDirMask = 0x00;
        g.ActiveIdUsingAllKeyboardKeys = false;
    }

    /// <summary>
    /// Clears the active item identifier.
    /// </summary>
    /// <remarks>
    /// A single call to
    /// <c>SetActiveID(0, NULL)</c>; all deactivation bookkeeping is performed there.
    /// </remarks>
    internal static void ClearActiveID()
    {
        SetActiveID(0, null);
    }

    /// <summary>
    /// Sets the hovered item identifier and resets the hover timers when the hover moves.
    /// </summary>
    /// <param name="id">The item identifier to record as hovered, or zero to clear the hover.</param>
    /// <remarks>
    /// The allow-overlap deferral is cleared
    /// unconditionally; the hovered and hovered-not-active timers restart only when a non-zero id differs
    /// from the previous frame's hovered id. Clearing is achieved by passing zero; there is no separate
    /// <c>ClearHoveredID</c> in this revision.
    /// </remarks>
    internal static void SetHoveredID(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.HoveredId = id;
        g.HoveredIdAllowOverlap = false;
        if (id != 0 && g.HoveredIdPreviousFrame != id)
            g.HoveredIdTimer = g.HoveredIdNotActiveTimer = 0.0f;
    }

    /// <summary>
    /// Returns the hovered item identifier for the current frame, or the previous frame's hovered id when
    /// nothing is hovered yet this frame.
    /// </summary>
    /// <returns>The current or previous hovered item identifier; zero when neither is set.</returns>
    /// <remarks>
    /// The fallback to
    /// <c>g.HoveredIdPreviousFrame</c> keeps the hover observable for one frame after the mouse leaves.
    /// </remarks>
    internal static uint GetHoveredID()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.HoveredId != 0 ? g.HoveredId : g.HoveredIdPreviousFrame;
    }

    /// <summary>
    /// Marks the last submitted item as edited and records the edit on the active and deactivated item
    /// state used by the <c>IsItemDeactivatedAfterEdit</c> family of queries.
    /// </summary>
    /// <param name="id">The identifier of the item that was edited.</param>
    /// <remarks>
    /// The internal edited status flag is always set;
    /// the public edited flag and the context edit counters are skipped for items carrying
    /// <c>ImGuiItemFlags_NoMarkEdited</c>. The edited flags are recorded when this id is the active id or
    /// nothing is active, and the deactivated record is updated when it matches.
    /// <para>
    /// The canonical assert. accepts an edit reported while drag-and-drop is active,
    /// while this item is active, while it was active last frame, when a navigation move just landed, or
    /// while a multi-select box selection is active; all of those terms are ported.
    /// </para>
    /// </remarks>
    internal static void MarkItemEdited(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;

        g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.EditedInternal;
        if ((g.LastItemData.ItemFlags & (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoMarkEdited) != 0)
            return;
        g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.Edited;

        if (g.ActiveId == id || g.ActiveId == 0)
        {
            // The active id may already have been released, as in the typical press/release button
            // behavior, but the data still needs to be filled.
            g.AnyIdHasBeenEditedThisFrame = g.ActiveIdHasBeenEditedThisFrame = g.ActiveIdHasBeenEditedBefore = true;
        }
        if (g.DeactivatedItemData.ID == id)
            g.DeactivatedItemData.HasBeenEditedBefore = true;

        // Accepted cases: a drag-and-drop target edit, an edit on the active or last-frame active item, an
        // edit reported after deactivation, and an edit following a navigation move.
        ImAssert.Check(g.DragDropActive
            || g.ActiveId == id
            || g.ActiveId == 0
            || g.DeactivatedItemData.ID == id
            || g.ActiveIdPreviousFrame == id
            || g.NavJustMovedToId != 0
            || (g.CurrentMultiSelect != null && g.BoxSelectState.IsActive));
    }

    #endregion

    #region Item Submission (KeepAliveID)

    /// <summary>
    /// Re-asserts the liveness of an item that does not use <see cref="ItemAdd"/> so the active id is not
    /// cleared at the end of the frame.
    /// </summary>
    /// <param name="id">The identifier of the item to keep alive.</param>
    /// <remarks>
    /// Code that does not call <c>ItemAdd</c> may
    /// need to call this manually; the deactivated record is also kept observable for one more frame.
    /// </remarks>
    internal static void KeepAliveID(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.ActiveId == id)
            g.ActiveIdIsAlive = id;
        if (g.DeactivatedItemData.ID == id)
            g.DeactivatedItemData.IsAlive = true;
    }

    #endregion

    #region Keyboard/Gamepad Navigation (SetFocusID/GetFocusID)

    /// <summary>
    /// Moves keyboard/gamepad navigation focus to the given item in the given window.
    /// </summary>
    /// <param name="id">The identifier of the item to focus. The canonical body asserts this is non-zero.</param>
    /// <param name="window">The window that owns the item and whose navigation layer is used.</param>
    /// <remarks>
    /// The window is switched when it is not the
    /// current navigation window, the per-layer last id and relative navigation rectangle are recorded, and
    /// the navigation highlight state is updated from the active input source. Preferred scoring positions
    /// are cleared on both axes so the next move request rescoring starts clean.
    /// <para>
    /// The canonical <c>IM_ASSERT(id != 0)</c>. becomes a
    /// <see cref="ImAssert.Check(bool, string)"/> because it guards programmer error.
    /// </para>
    /// <para>
    /// The canonical inline <c>WindowRectAbsToRel</c>. is expanded in place, as
    /// it is in <c>06_ids_clip.cs</c>: the screen-space navigation rectangle is offset by
    /// <c>window.DC.CursorStartPos</c> to produce the window-relative rectangle.
    /// </para>
    /// </remarks>
    internal static void SetFocusID(uint id, ImGuiWindow window)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(id != 0);

        if (g.NavWindow != window)
            SetNavWindow(window);

        // SetFocusID is called where window.DC.NavLayerCurrent and g.CurrentFocusScopeId are valid. The
        // window may differ from g.CurrentWindow, for example in multi-line InputText.
        ImGuiNavLayer navLayer = window.DC.NavLayerCurrent;
        g.NavId = id;
        g.NavLayer = navLayer;
        SetNavFocusScope(g.CurrentFocusScopeId);
        window.NavLastIds[(int)navLayer] = id;
        if (g.LastItemData.ID == id)
        {
            // WindowRectAbsToRel(window, g.LastItemData.NavRect).
            ImVec2 offset = window.DC.CursorStartPos;
            ImRect navRect = g.LastItemData.NavRect;
            window.NavRectRel[(int)navLayer] = new ImRect(
                navRect.Min.X - offset.X,
                navRect.Min.Y - offset.Y,
                navRect.Max.X - offset.X,
                navRect.Max.Y - offset.Y);
        }
        g.NavIdItemFlags = g.LastItemData.ID == id ? g.LastItemData.ItemFlags : ImGuiItemFlags.None;
        if (id == g.ActiveIdIsAlive)
            g.NavIdIsAlive = true;

        if (g.ActiveIdSource == ImGuiInputSource.Keyboard || g.ActiveIdSource == ImGuiInputSource.Gamepad)
            g.NavHighlightItemUnderNav = true;
        else if (g.IO.ConfigNavCursorVisibleAuto)
            g.NavCursorVisible = false;

        // Clear the preferred scoring position; NavMoveRequestApplyResult() tends to restore it.
        NavClearPreferredPosForAxis(ImGuiAxis.X);
        NavClearPreferredPosForAxis(ImGuiAxis.Y);
    }

    /// <summary>
    /// Returns the identifier of the item that currently holds keyboard/gamepad navigation focus.
    /// </summary>
    /// <returns>The focused item identifier, or zero when no item is focused.</returns>
    /// <remarks>
    /// Returns <c>GImGui-&gt;NavId</c>.
    /// </remarks>
    internal static uint GetFocusID()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.NavId;
    }

    #endregion

    #endregion


    #region SECTION: ID STACK, HASHING, AND CLIPPING

    #region MISC HELPERS/UTILITIES (String, Format, Hash functions)

    /// <summary>
    /// Holds the canonical 256-entry CRC32c lookup table used by the two hashing entry points below.
    /// </summary>
    /// <remarks>
    /// Transcribed entry-for-entry from canonical <c>GCrc32LookupTable</c>
    ///., selecting the non-legacy CRC32c variant emitted by the
    /// <c>#else</c> branch. The legacy CRC32-adler variant is selected only when
    /// <c>IMGUI_USE_LEGACY_CRC32_ADLER</c> is defined; this port, like the default canonical build, does not
    /// define it. The table is immutable and therefore safe to share across contexts and threads.
    /// </remarks>
    private static readonly uint[] GCrc32LookupTable =
    {
        0x00000000u, 0xF26B8303u, 0xE13B70F7u, 0x1350F3F4u,
        0xC79A971Fu, 0x35F1141Cu, 0x26A1E7E8u, 0xD4CA64EBu,
        0x8AD958CFu, 0x78B2DBCCu, 0x6BE22838u, 0x9989AB3Bu,
        0x4D43CFD0u, 0xBF284CD3u, 0xAC78BF27u, 0x5E133C24u,
        0x105EC76Fu, 0xE235446Cu, 0xF165B798u, 0x030E349Bu,
        0xD7C45070u, 0x25AFD373u, 0x36FF2087u, 0xC494A384u,
        0x9A879FA0u, 0x68EC1CA3u, 0x7BBCEF57u, 0x89D76C54u,
        0x5D1D08BFu, 0xAF768BBCu, 0xBC267848u, 0x4E4DFB4Bu,
        0x20BD8EDEu, 0xD2D60DDDu, 0xC186FE29u, 0x33ED7D2Au,
        0xE72719C1u, 0x154C9AC2u, 0x061C6936u, 0xF477EA35u,
        0xAA64D611u, 0x580F5512u, 0x4B5FA6E6u, 0xB93425E5u,
        0x6DFE410Eu, 0x9F95C20Du, 0x8CC531F9u, 0x7EAEB2FAu,
        0x30E349B1u, 0xC288CAB2u, 0xD1D83946u, 0x23B3BA45u,
        0xF779DEAEu, 0x05125DADu, 0x1642AE59u, 0xE4292D5Au,
        0xBA3A117Eu, 0x4851927Du, 0x5B016189u, 0xA96AE28Au,
        0x7DA08661u, 0x8FCB0562u, 0x9C9BF696u, 0x6EF07595u,
        0x417B1DBCu, 0xB3109EBFu, 0xA0406D4Bu, 0x522BEE48u,
        0x86E18AA3u, 0x748A09A0u, 0x67DAFA54u, 0x95B17957u,
        0xCBA24573u, 0x39C9C670u, 0x2A993584u, 0xD8F2B687u,
        0x0C38D26Cu, 0xFE53516Fu, 0xED03A29Bu, 0x1F682198u,
        0x5125DAD3u, 0xA34E59D0u, 0xB01EAA24u, 0x42752927u,
        0x96BF4DCCu, 0x64D4CECFu, 0x77843D3Bu, 0x85EFBE38u,
        0xDBFC821Cu, 0x2997011Fu, 0x3AC7F2EBu, 0xC8AC71E8u,
        0x1C661503u, 0xEE0D9600u, 0xFD5D65F4u, 0x0F36E6F7u,
        0x61C69362u, 0x93AD1061u, 0x80FDE395u, 0x72966096u,
        0xA65C047Du, 0x5437877Eu, 0x4767748Au, 0xB50CF789u,
        0xEB1FCBADu, 0x197448AEu, 0x0A24BB5Au, 0xF84F3859u,
        0x2C855CB2u, 0xDEEEDFB1u, 0xCDBE2C45u, 0x3FD5AF46u,
        0x7198540Du, 0x83F3D70Eu, 0x90A324FAu, 0x62C8A7F9u,
        0xB602C312u, 0x44694011u, 0x5739B3E5u, 0xA55230E6u,
        0xFB410CC2u, 0x092A8FC1u, 0x1A7A7C35u, 0xE811FF36u,
        0x3CDB9BDDu, 0xCEB018DEu, 0xDDE0EB2Au, 0x2F8B6829u,
        0x82F63B78u, 0x709DB87Bu, 0x63CD4B8Fu, 0x91A6C88Cu,
        0x456CAC67u, 0xB7072F64u, 0xA457DC90u, 0x563C5F93u,
        0x082F63B7u, 0xFA44E0B4u, 0xE9141340u, 0x1B7F9043u,
        0xCFB5F4A8u, 0x3DDE77ABu, 0x2E8E845Fu, 0xDCE5075Cu,
        0x92A8FC17u, 0x60C37F14u, 0x73938CE0u, 0x81F80FE3u,
        0x55326B08u, 0xA759E80Bu, 0xB4091BFFu, 0x466298FCu,
        0x1871A4D8u, 0xEA1A27DBu, 0xF94AD42Fu, 0x0B21572Cu,
        0xDFEB33C7u, 0x2D80B0C4u, 0x3ED04330u, 0xCCBBC033u,
        0xA24BB5A6u, 0x502036A5u, 0x4370C551u, 0xB11B4652u,
        0x65D122B9u, 0x97BAA1BAu, 0x84EA524Eu, 0x7681D14Du,
        0x2892ED69u, 0xDAF96E6Au, 0xC9A99D9Eu, 0x3BC21E9Du,
        0xEF087A76u, 0x1D63F975u, 0x0E330A81u, 0xFC588982u,
        0xB21572C9u, 0x407EF1CAu, 0x532E023Eu, 0xA145813Du,
        0x758FE5D6u, 0x87E466D5u, 0x94B49521u, 0x66DF1622u,
        0x38CC2A06u, 0xCAA7A905u, 0xD9F75AF1u, 0x2B9CD9F2u,
        0xFF56BD19u, 0x0D3D3E1Au, 0x1E6DCDEEu, 0xEC064EEDu,
        0xC38D26C4u, 0x31E6A5C7u, 0x22B65633u, 0xD0DDD530u,
        0x0417B1DBu, 0xF67C32D8u, 0xE52CC12Cu, 0x1747422Fu,
        0x49547E0Bu, 0xBB3FFD08u, 0xA86F0EFCu, 0x5A048DFFu,
        0x8ECEE914u, 0x7CA56A17u, 0x6FF599E3u, 0x9D9E1AE0u,
        0xD3D3E1ABu, 0x21B862A8u, 0x32E8915Cu, 0xC083125Fu,
        0x144976B4u, 0xE622F5B7u, 0xF5720643u, 0x07198540u,
        0x590AB964u, 0xAB613A67u, 0xB831C993u, 0x4A5A4A90u,
        0x9E902E7Bu, 0x6CFBAD78u, 0x7FAB5E8Cu, 0x8DC0DD8Fu,
        0xE330A81Au, 0x115B2B19u, 0x020BD8EDu, 0xF0605BEEu,
        0x24AA3F05u, 0xD6C1BC06u, 0xC5914FF2u, 0x37FACCF1u,
        0x69E9F0D5u, 0x9B8273D6u, 0x88D28022u, 0x7AB90321u,
        0xAE7367CAu, 0x5C18E4C9u, 0x4F48173Du, 0xBD23943Eu,
        0xF36E6F75u, 0x0105EC76u, 0x12551F82u, 0xE03E9C81u,
        0x34F4F86Au, 0xC69F7B69u, 0xD5CF889Du, 0x27A40B9Eu,
        0x79B737BAu, 0x8BDCB4B9u, 0x988C474Du, 0x6AE7C44Eu,
        0xBE2DA0A5u, 0x4C4623A6u, 0x5F16D052u, 0xAD7D5351u,
    };

    /// <summary>
    /// Hashes an arbitrary byte span with the canonical CRC32c algorithm and seed folding.
    /// </summary>
    /// <param name="data">The bytes to hash. The span is read once and is never retained.</param>
    /// <param name="seed">The seed folded into the accumulator; canonical callers default it to zero.</param>
    /// <returns>The resulting 32-bit identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>ImHashData</c>. byte-for-byte: the
    /// accumulator starts at <c>~seed</c>, each input byte applies
    /// <c>crc = (crc &gt;&gt; 8) ^ table[(crc &amp; 0xFF) ^ byte]</c>, and the function returns <c>~crc</c>. The
    /// method allocates nothing. The known-length form deliberately does not interpret the <c>###</c> reset
    /// marker; that behavior belongs to <see cref="ImHashStr(string, int, uint)"/>.
    /// </remarks>
    internal static uint ImHashData(ReadOnlySpan<byte> data, uint seed = 0)
    {
        uint crc = ~seed;
        for (int i = 0; i < data.Length; i++)
            crc = (crc >> 8) ^ GCrc32LookupTable[(crc & 0xFF) ^ data[i]];
        return ~crc;
    }

    /// <summary>
    /// Hashes the raw 32-bit little-endian representation of an integer value.
    /// </summary>
    /// <param name="data">The value whose four bytes are hashed.</param>
    /// <param name="seed">The seed folded into the accumulator.</param>
    /// <returns>The resulting 32-bit identifier.</returns>
    /// <remarks>
    /// Ports the canonical <c>ImHashData(&amp;n, sizeof(n), seed)</c> call used by
    /// <c>ImGuiWindow::GetID(int)</c> and <c>ImGui::GetIDWithSeed(int, ImGuiID)</c>.
    /// Every canonical host is
    /// little-endian, so the bytes are emitted least-significant first and the result matches the host build.
    /// The helper is allocation-free; callers reach it through the seeding overloads below.
    /// </remarks>
    internal static uint ImHashData(uint data, uint seed = 0)
    {
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)data;
        bytes[1] = (byte)(data >> 8);
        bytes[2] = (byte)(data >> 16);
        bytes[3] = (byte)(data >> 24);
        return ImHashData(bytes, seed);
    }

    /// <summary>
    /// Hashes a string with the canonical zero-terminated or known-length CRC32c algorithm, including the
    /// <c>###</c> display-label reset rule.
    /// </summary>
    /// <param name="data">The text to hash. It is encoded as UTF-8 before hashing (see the remarks).</param>
    /// <param name="dataSize">
    /// The number of UTF-16 characters to hash, or zero to hash the whole string up to its first embedded
    /// NUL. A value larger than the string length is clamped.
    /// </param>
    /// <param name="seed">The seed folded into the accumulator; canonical callers default it to zero.</param>
    /// <returns>The resulting 32-bit identifier.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImHashStr</c>. The accumulator starts
    /// at the already-inverted seed (<c>seed = ~seed; crc = seed;</c>) and the result is <c>~crc</c>. When
    /// either loop meets <c>'#'</c> followed by two more <c>'#'</c> characters, the accumulator is reset to
    /// that same inverted seed and the three marker characters are skipped without being hashed; this makes
    /// <c>ImHashStr("a###b")</c> produce exactly the same value as <c>ImHashStr("b")</c>. The literal
    /// sequence <c>"""</c> (three double-quote characters) is not special. With an explicit
    /// <paramref name="dataSize"/> the loop
    /// runs over exactly that many characters and hashes embedded NUL bytes like any other byte; with
    /// <paramref name="dataSize"/> equal to zero the loop stops at the first NUL byte without hashing it.
    /// </para>
    /// <para>
    /// Canonical hashes the raw bytes supplied by the caller. A C# <see cref="string"/> stores UTF-16 code
    /// units, so this port emits the UTF-8 bytes of the selected characters first. ASCII input therefore
    /// produces bit-identical results to canonical, and non-ASCII input has well-defined behavior that is
    /// stable across processes. The encoding buffer is stack-allocated for ordinary identifier lengths and
    /// heap-allocated only for strings longer than 256 bytes.
    /// </para>
    /// </remarks>
    internal static uint ImHashStr(string data, int dataSize = 0, uint seed = 0)
    {
        // Canonical folds the incoming seed once and reuses that same inverted value for both the running
        // accumulator and the "###" reset.
        uint invertedSeed = ~seed;

        int charCount = dataSize != 0 ? dataSize : data.Length;
        ImAssert.Check(charCount >= 0 && charCount <= data.Length);
        if (charCount < 0)
            charCount = 0;
        if (charCount > data.Length)
            charCount = data.Length;

        int byteCount = Encoding.UTF8.GetByteCount(data, 0, charCount);
        if (byteCount <= 256)
        {
            Span<byte> bytes = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(data.AsSpan(0, charCount), bytes);
            return HashEncodedBytes(bytes, invertedSeed, dataSize != 0);
        }
        else
        {
            byte[] bytes = new byte[byteCount];
            Encoding.UTF8.GetBytes(data.AsSpan(0, charCount), bytes);
            return HashEncodedBytes(bytes, invertedSeed, dataSize != 0);
        }
    }

    /// <summary>
    /// Runs the canonical CRC32c byte loop over already-encoded text and applies the <c>###</c> reset rule.
    /// </summary>
    /// <param name="bytes">The UTF-8 bytes of the selected characters.</param>
    /// <param name="invertedSeed">The seed already inverted once by the caller, used for both the accumulator
    /// and every <c>###</c> reset.</param>
    /// <param name="knownLength">
    /// True for the canonical explicit-length loop, which hashes every byte including embedded NUL; false for
    /// the zero-terminated loop, which stops at the first NUL without hashing it.
    /// </param>
    /// <returns>The canonical 32-bit identifier, i.e. the accumulator after the final bitwise complement.</returns>
    /// <remarks>
    /// Split out of <see cref="ImHashStr(string, int, uint)"/> so the stack-allocated encoding buffer is
    /// passed to a method as a span instead of being assigned across branches, which the C# definite-assignment
    /// and ref-safety rules reject. The canonical marker check <c>data_size &gt;= 2</c> is realized as a bounds
    /// check against the encoded span; because the marker consists of ASCII bytes, equivalence is exact.
    /// </remarks>
    private static uint HashEncodedBytes(ReadOnlySpan<byte> bytes, uint invertedSeed, bool knownLength)
    {
        uint crc = invertedSeed;
        if (knownLength)
        {
            // Known-length loop: exactly the requested characters were encoded, so bounds checks against the
            // encoded span are equivalent to the canonical "data_size >= 2" check.
            for (int i = 0; i < bytes.Length; i++)
            {
                byte c = bytes[i];
                if (c == (byte)'#' && i + 2 < bytes.Length && bytes[i + 1] == (byte)'#' && bytes[i + 2] == (byte)'#')
                {
                    crc = invertedSeed;
                    i += 2;
                    continue;
                }
                crc = (crc >> 8) ^ GCrc32LookupTable[(crc & 0xFF) ^ c];
            }
        }
        else
        {
            // Zero-terminated loop: canonical stops at the NUL terminator and never hashes it.
            for (int i = 0; i < bytes.Length; i++)
            {
                byte c = bytes[i];
                if (c == 0)
                    break;
                if (c == (byte)'#' && i + 2 < bytes.Length && bytes[i + 1] == (byte)'#' && bytes[i + 2] == (byte)'#')
                {
                    crc = invertedSeed;
                    i += 2;
                    continue;
                }
                crc = (crc >> 8) ^ GCrc32LookupTable[(crc & 0xFF) ^ c];
            }
        }
        return ~crc;
    }

    /// <summary>
    /// Hashes the eight bytes of a managed object identity hash, emulating a pointer-value hash.
    /// </summary>
    /// <param name="pointerId">The object whose identity is hashed; null hashes the value zero.</param>
    /// <param name="seed">The seed folded into the accumulator.</param>
    /// <returns>The resulting 32-bit identifier.</returns>
    /// <remarks>
    /// Canonical <c>ImGuiWindow::GetID(const void*)</c> hashes the raw pointer value
    ///. A managed reference has no stable address that survives
    /// compaction, so this port hashes <c>(ulong)RuntimeHelpers.GetHashCode(pointerId)</c> as eight
    /// little-endian bytes, the same width canonical uses for <c>sizeof(void*)</c> on 64-bit hosts.
    /// Consequence: pointer-derived identifiers are stable for the lifetime of the object and unique per
    /// live object, but they are not comparable to canonical's per-run addresses and may be reused after the
    /// object is collected.
    /// </remarks>
    internal static uint HashPointerId(object? pointerId, uint seed)
    {
        ulong value = unchecked((ulong)RuntimeHelpers.GetHashCode(pointerId));
        Span<byte> bytes = stackalloc byte[8];
        for (int i = 0; i < 8; i++)
            bytes[i] = (byte)(value >> (8 * i));
        return ImHashData(bytes, seed);
    }

    /// <summary>
    /// Writes a single-precision float to a span in canonical little-endian byte order.
    /// </summary>
    /// <param name="bytes">The destination span; it must have room for four bytes at the offset.</param>
    /// <param name="offset">The zero-based byte index at which the value is written.</param>
    /// <param name="value">The value whose raw IEEE 754 bits are written.</param>
    /// <remarks>
    /// Shared by the position and rectangle identity helpers so both hash the same in-memory representation
    /// canonical hashes when it passes a struct address to <c>ImHashData</c>.
    /// </remarks>
    private static void WriteFloatLittleEndian(Span<byte> bytes, int offset, float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        bytes[offset] = (byte)bits;
        bytes[offset + 1] = (byte)(bits >> 8);
        bytes[offset + 2] = (byte)(bits >> 16);
        bytes[offset + 3] = (byte)(bits >> 24);
    }

    /// <summary>
    /// Hashes the two floats of a point exactly as canonical hashes a struct address.
    /// </summary>
    /// <param name="value">The point whose eight bytes are hashed in <c>X</c>, <c>Y</c> order.</param>
    /// <param name="seed">The seed folded into the accumulator.</param>
    /// <returns>The resulting 32-bit identifier.</returns>
    /// <remarks>
    /// Canonical <c>ImGuiWindow::GetIDFromPos</c> passes <c>&amp;p_rel, sizeof(p_rel)</c> where
    /// <c>p_rel</c> is an <c>ImVec2</c>.
    /// </remarks>
    private static uint HashPoint(ImVec2 value, uint seed)
    {
        Span<byte> bytes = stackalloc byte[8];
        WriteFloatLittleEndian(bytes, 0, value.X);
        WriteFloatLittleEndian(bytes, 4, value.Y);
        return ImHashData(bytes, seed);
    }

    /// <summary>
    /// Hashes the four floats of a rectangle exactly as canonical hashes a struct address.
    /// </summary>
    /// <param name="value">The rectangle hashed in <c>Min.X</c>, <c>Min.Y</c>, <c>Max.X</c>, <c>Max.Y</c> order.</param>
    /// <param name="seed">The seed folded into the accumulator.</param>
    /// <returns>The resulting 32-bit identifier.</returns>
    /// <remarks>
    /// Canonical <c>ImGuiWindow::GetIDFromRectangle</c> passes <c>&amp;r_rel, sizeof(r_rel)</c> where
    /// <c>r_rel</c> is an <c>ImRect</c>, i.e. four contiguous floats
    /// </remarks>
    private static uint HashRectangle(ImRect value, uint seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteFloatLittleEndian(bytes, 0, value.Min.X);
        WriteFloatLittleEndian(bytes, 4, value.Min.Y);
        WriteFloatLittleEndian(bytes, 8, value.Max.X);
        WriteFloatLittleEndian(bytes, 12, value.Max.Y);
        return ImHashData(bytes, seed);
    }

    #endregion

    #region ID STACK

    /// <summary>
    /// Computes an identifier seeded by the current window's ID stack.
    /// </summary>
    /// <param name="strId">The label text to hash.</param>
    /// <returns>The identifier, seeded by the top of the current window's ID stack.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetID(const char*)</c>.,
    /// which delegates to <c>ImGuiWindow::GetID</c> and therefore uses
    /// <c>IDStack.back()</c> as the seed. Canonical additionally notifies the debug-tools ID hook when
    /// <c>g.DebugHookIdInfoId</c> matches the produced identifier; that hook is owned by the debug-tools
    /// chunk and is deferred here, so the branch is documented but not yet active.
    /// </remarks>
    public static uint GetID(string strId)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        return GetIDWithSeed(strId, null, window.IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from a character range, seeded by the current window's ID stack.
    /// </summary>
    /// <param name="strIdBegin">The full label text.</param>
    /// <param name="strIdEnd">
    /// The trailing portion of <paramref name="strIdBegin"/> that begins at the canonical end pointer; the
    /// hashed region is the prefix that precedes it. Null selects the zero-terminated path.
    /// </param>
    /// <returns>The identifier, seeded by the top of the current window's ID stack.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetID(const char*, const char*)</c>
    ///. Canonical derives the byte length as
    /// <c>str_end - str</c>. C# strings cannot be pointer-differenced, so this port defines
    /// <paramref name="strIdEnd"/> as the trailing substring located at or after that end pointer and hashes
    /// the preceding prefix; see <see cref="GetIDWithSeed(string, string?, uint)"/> for the exact rule.
    /// </remarks>
    public static uint GetID(string strIdBegin, string strIdEnd)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        return GetIDWithSeed(strIdBegin, strIdEnd, window.IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from a managed object identity, seeded by the current window's ID stack.
    /// </summary>
    /// <param name="ptrId">The object whose identity is hashed; null hashes the value zero.</param>
    /// <returns>The identifier, seeded by the top of the current window's ID stack.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetID(const void*)</c>. The
    /// pointer-hashing deviation is documented on <see cref="HashPointerId(object?, uint)"/>.
    /// </remarks>
    public static uint GetID(object? ptrId)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        return HashPointerId(ptrId, window.IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from an integer value, seeded by the current window's ID stack.
    /// </summary>
    /// <param name="n">The value whose four little-endian bytes are hashed.</param>
    /// <returns>The identifier, seeded by the top of the current window's ID stack.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetID(int)</c>.
    /// </remarks>
    public static uint GetID(int n)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        return GetIDWithSeed(n, window.IDStack[^1]);
    }

    /// <summary>
    /// Hashes a string with an explicit seed, bypassing the window ID stack.
    /// </summary>
    /// <param name="strId">The full label text.</param>
    /// <param name="strIdEnd">
    /// The trailing portion of <paramref name="strId"/> that begins at the canonical end pointer, or null for
    /// the zero-terminated path. The hashed character count is
    /// <c>strId.Length - strIdEnd.Length</c>; a zero result selects the whole-string path exactly as
    /// canonical's <c>data_size == 0</c> does, while a negative result (which canonical cannot express) hashes
    /// no characters.
    /// </param>
    /// <param name="seed">The seed to hash with.</param>
    /// <returns>The resulting identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetIDWithSeed(const char*, const char*, ImGuiID)</c>
    ///. Canonical computes the byte length as
    /// <c>str_end - str</c>; because C# strings are not pointer-addressable, the second argument is the
    /// trailing substring that starts at the end pointer and the prefix before it is hashed. The
    /// debug-tools ID hook is deferred to its owning chunk.
    /// </remarks>
    internal static uint GetIDWithSeed(string strId, string? strIdEnd, uint seed)
    {
        int dataSize = 0;
        if (strIdEnd != null)
        {
            dataSize = strId.Length - strIdEnd.Length;
            ImAssert.Check(dataSize >= 0 && dataSize <= strId.Length);
        }
        uint id = ImHashStr(strId, dataSize, seed);
        // The identifier hook fires only for the single identifier the item-path query is currently walking,
        // which is what keeps this comparison cheap on every other call.
        if (GetContext().DebugHookIdInfoId == id)
            DebugHookIdInfo(id, (int)ImGuiDataType.String, IntPtr.Zero, IntPtr.Zero, strId);
        return id;
    }

    /// <summary>
    /// Hashes an integer with an explicit seed, bypassing the window ID stack.
    /// </summary>
    /// <param name="n">The value whose four little-endian bytes are hashed.</param>
    /// <param name="seed">The seed to hash with.</param>
    /// <returns>The resulting identifier.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetIDWithSeed(int, ImGuiID)</c>
    /// </remarks>
    internal static uint GetIDWithSeed(int n, uint seed)
    {
        uint id = ImHashData((uint)n, seed);
        if (GetContext().DebugHookIdInfoId == id)
            DebugHookIdInfo(id, (int)ImGuiDataType.S32, new IntPtr(n), IntPtr.Zero);
        return id;
    }

    /// <summary>
    /// Manufactures an identifier from an absolute position for callers that have no stable label.
    /// </summary>
    /// <param name="pos">The absolute position to hash.</param>
    /// <returns>The identifier, seeded by the top of the current window's ID stack.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGuiWindow::GetIDFromPos</c>. The
    /// position is first converted to window-relative coordinates by subtracting
    /// <c>window.DC.CursorStartPos</c>, mirroring the inline <c>WindowPosAbsToRel</c>
    ///.; that inline helper is not owned by any part of this
    /// chunk, so the subtraction is inlined here rather than declared as a shared member. The relative point's
    /// eight bytes are then hashed, exactly as canonical hashes <c>sizeof(ImVec2)</c> bytes. Canonical's
    /// comment marks this as a rare fallback used only when no meaningful label exists.
    /// </remarks>
    internal static uint GetIDFromPos(ImVec2 pos)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        ImVec2 cursorStartPos = window.DC.CursorStartPos;
        ImVec2 relativePos = new(pos.X - cursorStartPos.X, pos.Y - cursorStartPos.Y);
        return HashPoint(relativePos, window.IDStack[^1]);
    }

    /// <summary>
    /// Manufactures an identifier from an absolute rectangle for callers that have no stable label.
    /// </summary>
    /// <param name="r">The absolute rectangle to hash.</param>
    /// <returns>The identifier, seeded by the top of the current window's ID stack.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGuiWindow::GetIDFromRectangle</c>.
    /// The rectangle is converted to window-relative coordinates by subtracting <c>window.DC.CursorStartPos</c>
    /// from both corners, mirroring the inline <c>WindowRectAbsToRel</c>
    ///., and its four floats are hashed exactly as canonical
    /// hashes <c>sizeof(ImRect)</c> bytes.
    /// </remarks>
    internal static uint GetIDFromRectangle(ImRect r)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        ImVec2 offset = window.DC.CursorStartPos;
        ImRect relativeRect = new(
            r.Min.X - offset.X,
            r.Min.Y - offset.Y,
            r.Max.X - offset.X,
            r.Max.Y - offset.Y);
        return HashRectangle(relativeRect, window.IDStack[^1]);
    }

    /// <summary>
    /// Pushes a label-derived identifier onto the current window's ID stack.
    /// </summary>
    /// <param name="strId">The label text to hash against the current stack seed.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::PushID(const char*)</c>.
    /// </remarks>
    public static void PushID(string strId)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        uint id = GetIDWithSeed(strId, null, window.IDStack[^1]);
        window.IDStack.Add(id);
    }

    /// <summary>
    /// Pushes a character-range identifier onto the current window's ID stack.
    /// </summary>
    /// <param name="strIdBegin">The full label text.</param>
    /// <param name="strIdEnd">
    /// The trailing portion of <paramref name="strIdBegin"/> that begins at the canonical end pointer, or null
    /// for the zero-terminated path.
    /// </param>
    /// <remarks>
    /// Ports canonical <c>ImGui::PushID(const char*, const char*)</c>
    ///. The begin/end adaptation is documented on
    /// <see cref="GetIDWithSeed(string, string?, uint)"/>.
    /// </remarks>
    public static void PushID(string strIdBegin, string strIdEnd)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        uint id = GetIDWithSeed(strIdBegin, strIdEnd, window.IDStack[^1]);
        window.IDStack.Add(id);
    }

    /// <summary>
    /// Pushes an object-identity identifier onto the current window's ID stack.
    /// </summary>
    /// <param name="ptrId">The object whose identity is hashed; null hashes the value zero.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::PushID(const void*)</c>. The
    /// pointer-hashing deviation is documented on <see cref="HashPointerId(object?, uint)"/>.
    /// </remarks>
    public static void PushID(object? ptrId)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        uint id = HashPointerId(ptrId, window.IDStack[^1]);
        window.IDStack.Add(id);
    }

    /// <summary>
    /// Pushes an integer-derived identifier onto the current window's ID stack.
    /// </summary>
    /// <param name="intId">The value whose four little-endian bytes are hashed.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::PushID(int)</c>.
    /// </remarks>
    public static void PushID(int intId)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        uint id = GetIDWithSeed(intId, window.IDStack[^1]);
        window.IDStack.Add(id);
    }

    /// <summary>
    /// Pushes an identifier verbatim, ignoring the current ID stack as a seed.
    /// </summary>
    /// <param name="id">The already-computed identifier to push.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::PushOverrideID</c>., declared
    /// internal. No hashing occurs; the value is pushed
    /// exactly as supplied. This is used to establish an identity that is not derived from the surrounding
    /// stack. Canonical notifies the debug-tools ID hook when the value matches <c>g.DebugHookIdInfoId</c>;
    /// that hook is deferred to the debug-tools chunk.
    /// </remarks>
    internal static void PushOverrideID(uint id)
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        window.IDStack.Add(id);
        if (g.DebugHookIdInfoId == id)
            DebugHookIdInfo(id, (int)ImGuiDataTypePrivate.ImGuiDataType_ID, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Pops the most recent identifier from the current window's ID stack.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::PopID</c>. The stack always
    /// retains its root (window-name) entry, because every identifier lookup reads the top of the stack;
    /// popping the root is a user error and is reported through <see cref="ErrorLog"/> without mutating the
    /// stack, reproducing canonical's <c>IM_ASSERT_USER_ERROR_RET</c> guarded early-out.
    /// </remarks>
    public static void PopID()
    {
        ImGuiContext? g = GetCurrentContext();
        ImAssert.Check(g != null);
        ImGuiWindow window = g!.CurrentWindow!;
        if (window.IDStack.Count <= 1)
        {
            ErrorLog("Calling PopID() too many times!");
            return;
        }
        window.IDStack.RemoveAt(window.IDStack.Count - 1);
    }

    #endregion

    #region CLIP RECTS

    /// <summary>
    /// Pushes a clipping rectangle for both item logic and draw-list rendering.
    /// </summary>
    /// <param name="clipRectMin">The top-left corner of the new clip rectangle, in screen coordinates.</param>
    /// <param name="clipRectMax">The bottom-right corner of the new clip rectangle, in screen coordinates.</param>
    /// <param name="intersectWithCurrentClipRect">
    /// When true, the new rectangle is intersected with the currently active one before it is pushed.
    /// </param>
    /// <remarks>
    /// Ports canonical <c>ImGui::PushClipRect</c>. The draw-list
    /// clip rectangle is pushed first, then the window's logical <c>ClipRect</c> is refreshed from the top of
    /// the draw-list clip stack so item hit-testing and culling observe the same rectangle the renderer does.
    /// Canonical reads the private <c>_ClipRectStack</c> directly; this port reads the equivalent public
    /// accessors <c>ImDrawList.GetClipRectMin</c>/<c>GetClipRectMax</c> because the stack itself is private in
    /// the draw-list port. The push is expected to be paired with <see cref="PopClipRect"/> before the current
    /// window ends; canonical enforces that only through the underlying draw-list stack, so this port relies on
    /// the draw-list's own guarded underflow handling.
    /// </remarks>
    public static void PushClipRect(ImVec2 clipRectMin, ImVec2 clipRectMax, bool intersectWithCurrentClipRect)
    {
        ImGuiWindow window = GetCurrentWindow();
        window.DrawList.PushClipRect(clipRectMin, clipRectMax, intersectWithCurrentClipRect);
        window.ClipRect = new ImRect(window.DrawList.GetClipRectMin(), window.DrawList.GetClipRectMax());
    }

    /// <summary>
    /// Pops the most recent clipping rectangle and restores the previous logical clip rectangle.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::PopClipRect</c>. The draw-list
    /// clip stack is popped first, then the window's logical <c>ClipRect</c> is refreshed from the new top of
    /// the stack. When the draw-list stack becomes empty the port's <c>ImDrawList.PopClipRect</c> restores the
    /// fullscreen clip rectangle, whereas canonical would read past an empty vector; the guarded behavior is
    /// recorded here rather than reproduced as undefined behavior.
    /// </remarks>
    public static void PopClipRect()
    {
        ImGuiWindow window = GetCurrentWindow();
        window.DrawList.PopClipRect();
        window.ClipRect = new ImRect(window.DrawList.GetClipRectMin(), window.DrawList.GetClipRectMax());
    }

    #endregion

    #endregion


    #region SECTION: SCROLLING AND SCROLLBARS

    #region Scrolling

    /// <summary>
    /// Reads one component of a two-component vector selected by a layout axis.
    /// </summary>
    /// <param name="value">The vector whose component is read.</param>
    /// <param name="axis">The axis selecting the component: <see cref="ImGuiAxis.X"/> reads
    /// <see cref="ImVec2.X"/>, and every other value reads <see cref="ImVec2.Y"/>.</param>
    /// <returns>The selected component.</returns>
    /// <remarks>
    /// Canonical code indexes an <c>ImVec2</c> through the pointer returned by its <c>operator[]</c>,
    /// for example <c>window->Scroll[axis]</c>. The port stores scroll state in the <see cref="ImVec2"/>
    /// struct, which exposes named fields rather than an indexer, so this helper and
    /// <see cref="SetAxisComponent"/> reproduce the canonical indexing without allocating.
    /// </remarks>
    private static float AxisComponent(ImVec2 value, ImGuiAxis axis)
    {
        return axis == ImGuiAxis.X ? value.X : value.Y;
    }

    /// <summary>
    /// Writes one component of a two-component vector selected by a layout axis.
    /// </summary>
    /// <param name="value">The vector whose component is written.</param>
    /// <param name="axis">The axis selecting the component: <see cref="ImGuiAxis.X"/> writes
    /// <see cref="ImVec2.X"/>, and every other value writes <see cref="ImVec2.Y"/>.</param>
    /// <param name="component">The component value to store.</param>
    /// <remarks>
    /// This is the write counterpart of <see cref="AxisComponent"/> and exists for the same reason: the
    /// port cannot index an <see cref="ImVec2"/> by axis. Callers must pass a variable they own; the
    /// <see cref="ImGuiWindow"/> scroll fields are auto-properties, so canonical in-place element writes
    /// such as <c>window->Scroll[axis] = x</c> are lowered to a read-modify-write of the whole vector by
    /// the caller.
    /// </remarks>
    private static void SetAxisComponent(ref ImVec2 value, ImGuiAxis axis, float component)
    {
        if (axis == ImGuiAxis.X)
            value.X = component;
        else
            value.Y = component;
    }

    /// <summary>
    /// Snaps a requested scroll target toward the start or end of the scrollable range when the request
    /// aims at an item very close to that edge.
    /// </summary>
    /// <param name="target">The requested scroll target, in scroll-offset units.</param>
    /// <param name="snapMin">The lower edge of the scrollable range, always <c>0.0f</c> in the canonical
    /// caller.</param>
    /// <param name="snapMax">The upper edge of the scrollable range, computed from
    /// <c>ScrollMax + SizeFull - decorationSize</c> by the caller.</param>
    /// <param name="snapThreshold">The distance from an edge within which snapping is applied. A
    /// non-positive value disables snapping.</param>
    /// <param name="centerRatio">The centering ratio requested by the caller; <c>0.0f</c> keeps the
    /// target, <c>1.0f</c> moves it fully onto the edge, and <c>0.5f</c> halves the snap.</param>
    /// <returns>The snapped scroll target, or <paramref name="target"/> unchanged when it lies inside the
    /// snapping thresholds.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>CalcScrollEdgeSnap</c>. The snap
    /// preserves the visible difference between <c>WindowPadding</c> and <c>ItemSpacing</c> when the target
    /// item sits almost on an edge. With default style values the effect is invisible on the X axis because
    /// <c>WindowPadding.x == ItemSpacing.x</c>.
    /// </para>
    /// <para>
    /// Canonical applies <c>ImLerp(snap_min, target, center_ratio)</c> and <c>ImLerp(target, snap_max,
    /// center_ratio)</c>; both are expanded inline here to avoid introducing a shared float interpolation
    /// helper that the assembled partial class does not yet own.
    /// </para>
    /// </remarks>
    internal static float CalcScrollEdgeSnap(float target, float snapMin, float snapMax, float snapThreshold, float centerRatio)
    {
        if (target <= snapMin + snapThreshold)
            return snapMin + (target - snapMin) * centerRatio;
        if (target >= snapMax - snapThreshold)
            return target + (snapMax - target) * centerRatio;
        return target;
    }

    /// <summary>
    /// Resolves the pending scroll target into the scroll offset that should be applied next, then clamps
    /// it to the legal range.
    /// </summary>
    /// <param name="window">The window whose <c>ScrollTarget</c> and geometry are resolved. Must be a
    /// window that has already run its sizing pass, because the result depends on <c>SizeFull</c>,
    /// decoration sizes, and <c>ScrollMax</c>.</param>
    /// <returns>The scroll offset to apply. The returned vector is pure output; the window state is not
    /// modified.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>CalcNextScrollFromScrollTargetAndClamp</c>
    ///., which <c>Begin</c> calls at
    ///  and <c>ScrollToRectEx</c> calls.
    /// </para>
    /// <para>
    /// Per axis: when <c>ScrollTarget[axis] &lt; FLT_MAX</c> a change was requested, so the value is
    /// optionally edge-snapped and converted into a scrolled position by subtracting
    /// <c>center_ratio * (SizeFull - decoration_size)</c>. The result is then rounded to the nearest
    /// integer-representable value (canonical <c>ImRound64</c>, positive values only) and clamped to be
    /// non-negative. The upper clamp against <c>ScrollMax</c> is intentionally skipped while the window is
    /// collapsed or skipping items, so a queued target survives a frame in which layout is unavailable.
    /// </para>
    /// <para>
    /// Canonical returns <c>ImVec2</c>. The CONTRACT §5 entry lists a <c>float</c> return; that is a typo
    /// and this port follows canonical, because both <c>Begin</c> and <c>ScrollToRectEx</c> consume a
    /// vector.
    /// </para>
    /// </remarks>
    internal static ImVec2 CalcNextScrollFromScrollTargetAndClamp(ImGuiWindow window)
    {
        ImVec2 scroll = window.Scroll;
        ImVec2 decorationSize = new(
            window.DecoOuterSizeX1 + window.DecoInnerSizeX1 + window.DecoOuterSizeX2,
            window.DecoOuterSizeY1 + window.DecoInnerSizeY1 + window.DecoOuterSizeY2);
        for (int axisIndex = 0; axisIndex < 2; axisIndex++)
        {
            ImGuiAxis axis = (ImGuiAxis)axisIndex;
            if (AxisComponent(window.ScrollTarget, axis) < float.MaxValue)
            {
                float centerRatio = AxisComponent(window.ScrollTargetCenterRatio, axis);
                float scrollTarget = AxisComponent(window.ScrollTarget, axis);
                if (AxisComponent(window.ScrollTargetEdgeSnapDist, axis) > 0.0f)
                {
                    float snapMin = 0.0f;
                    float snapMax = AxisComponent(window.ScrollMax, axis) + AxisComponent(window.SizeFull, axis) - AxisComponent(decorationSize, axis);
                    scrollTarget = CalcScrollEdgeSnap(scrollTarget, snapMin, snapMax, AxisComponent(window.ScrollTargetEdgeSnapDist, axis), centerRatio);
                }
                SetAxisComponent(ref scroll, axis, scrollTarget - centerRatio * (AxisComponent(window.SizeFull, axis) - AxisComponent(decorationSize, axis)));
            }
            SetAxisComponent(ref scroll, axis, (float)(long)(MathF.Max(AxisComponent(scroll, axis), 0.0f) + 0.5f));
            if (!window.Collapsed && !window.SkipItems)
                SetAxisComponent(ref scroll, axis, MathF.Min(AxisComponent(scroll, axis), AxisComponent(window.ScrollMax, axis)));
        }
        return scroll;
    }

    /// <summary>
    /// Scrolls the current window so that the rectangle recorded for the last submitted item becomes
    /// visible.
    /// </summary>
    /// <param name="flags">Requested scrolling behavior for each axis, plus parent-propagation control.
    /// See <see cref="ImGuiScrollFlags"/>.</param>
    /// <remarks>
    /// Ports canonical <c>ScrollToItem</c>. The navigation
    /// rectangle of the last item, <c>g.LastItemData.NavRect</c>, is the input rectangle rather than the
    /// painted item rectangle.
    /// </remarks>
    public static void ScrollToItem(ImGuiScrollFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        ScrollToRectEx(window, g.LastItemData.NavRect, flags);
    }

    /// <summary>
    /// Scrolls the supplied window so that the supplied rectangle becomes visible.
    /// </summary>
    /// <param name="window">The window to scroll.</param>
    /// <param name="rect">The rectangle, in absolute screen coordinates, that must be brought into
    /// view.</param>
    /// <param name="flags">Requested scrolling behavior for each axis, plus parent-propagation control.
    /// See <see cref="ImGuiScrollFlags"/>.</param>
    /// <remarks>
    /// Ports canonical <c>ScrollToRect</c>., which forwards
    /// directly to <see cref="ScrollToRectEx"/>.
    /// </remarks>
    public static void ScrollToRect(ImGuiWindow window, ImRect rect, ImGuiScrollFlags flags = 0)
    {
        ScrollToRectEx(window, rect, flags);
    }

    /// <summary>
    /// Computes and applies the scroll change that brings a rectangle into view, returning the applied
    /// delta.
    /// </summary>
    /// <param name="window">The window to scroll. The window must already have valid geometry for the
    /// current frame; its <c>InnerRect</c>, decoration sizes, and <c>ScrollMax</c> are read.</param>
    /// <param name="itemRect">The rectangle, in absolute screen coordinates, that should become
    /// visible.</param>
    /// <param name="flags">Requested scrolling behavior for each axis, plus parent-propagation control.
    /// See <see cref="ImGuiScrollFlags"/>.</param>
    /// <returns>The scroll delta that this call applied to <paramref name="window"/>, accumulated with the
    /// delta applied to its parent chain.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ScrollToRectEx</c>. This is the
    /// core of all scroll-to behavior and is the shared implementation behind <see cref="ScrollToItem"/>,
    /// <see cref="ScrollToRect"/>, <see cref="ScrollToBringRectIntoView"/>, and the navigation move path
    /// (<c>NavMoveRequestApplyResult</c>, ). The <c>SetScrollHereX</c> and
    /// <c>SetScrollHereY</c> entry points do not route through it; they write <c>ScrollTarget</c> directly
    /// through <see cref="SetScrollFromPosX(ImGuiWindow, float, float)"/> and
    /// <see cref="SetScrollFromPosY(ImGuiWindow, float, float)"/>.
    /// </para>
    /// <para>
    /// The visible rectangle is the window <c>InnerRect</c> expanded by one pixel and inset horizontally and
    /// vertically by the inner decoration sizes. Canonical asserts that at most one behavior bit is set per
    /// axis; this port reproduces that as <see cref="ImAssert.Check(bool, string)"/> on the axis masks. When a caller
    /// requests no behavior, the canonical defaults apply: an edge-keep request on X when the window has a
    /// horizontal scrollbar, and an always-center request on Y while the window is appearing, otherwise an
    /// edge-keep request.
    /// </para>
    /// <para>
    /// Each axis then selects either an edge-keep scroll or a center scroll. A rectangle that cannot be
    /// made fully visible, because it is wider or taller than the visible rectangle and the window is not
    /// auto-fitting, is aligned to the leading edge so the user sees its beginning. Requests are stored into
    /// <c>ScrollTarget</c> through <see cref="SetScrollFromPosX(ImGuiWindow, float, float)"/> and
    /// <see cref="SetScrollFromPosY(ImGuiWindow, float, float)"/>; the resulting delta is measured by
    /// resolving the target with <see cref="CalcNextScrollFromScrollTargetAndClamp"/> and subtracting the
    /// current <c>Scroll</c>.
    /// </para>
    /// <para>
    /// Unless <see cref="ImGuiScrollFlags.NoScrollParent"/> is set and the window is a child window, the
    /// center-request bits in the incoming flags are downgraded to edge-keep bits and the same operation is
    /// applied to the parent window with the rectangle translated by the delta already applied here.
    /// </para>
    /// </remarks>
    public static ImVec2 ScrollToRectEx(ImGuiWindow window, ImRect itemRect, ImGuiScrollFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImRect scrollRect = new(window.InnerRect.Min - new ImVec2(1, 1), window.InnerRect.Max + new ImVec2(1, 1));
        scrollRect.Min.X = MathF.Min(scrollRect.Min.X + window.DecoInnerSizeX1, scrollRect.Max.X);
        scrollRect.Min.Y = MathF.Min(scrollRect.Min.Y + window.DecoInnerSizeY1, scrollRect.Max.Y);

        // Canonical asserts that only one behavior may be selected per axis.
        uint maskX = (uint)(flags & ImGuiScrollFlags.MaskX_);
        uint maskY = (uint)(flags & ImGuiScrollFlags.MaskY_);
        ImAssert.Check(maskX == 0 || (maskX & (maskX - 1)) == 0);
        ImAssert.Check(maskY == 0 || (maskY & (maskY - 1)) == 0);

        ImGuiScrollFlags inFlags = flags;
        if ((flags & ImGuiScrollFlags.MaskX_) == 0 && window.ScrollbarX)
            flags |= ImGuiScrollFlags.KeepVisibleEdgeX;
        if ((flags & ImGuiScrollFlags.MaskY_) == 0)
            flags |= window.Appearing ? ImGuiScrollFlags.AlwaysCenterY : ImGuiScrollFlags.KeepVisibleEdgeY;

        bool fullyVisibleX = itemRect.Min.X >= scrollRect.Min.X && itemRect.Max.X <= scrollRect.Max.X;
        bool fullyVisibleY = itemRect.Min.Y >= scrollRect.Min.Y && itemRect.Max.Y <= scrollRect.Max.Y;
        bool canBeFullyVisibleX = (itemRect.GetWidth() + g.Style.ItemSpacing.X * 2.0f) <= scrollRect.GetWidth() || window.AutoFitFramesX > 0 || (window.Flags & ImGuiWindowFlags.AlwaysAutoResize) != 0;
        bool canBeFullyVisibleY = (itemRect.GetHeight() + g.Style.ItemSpacing.Y * 2.0f) <= scrollRect.GetHeight() || window.AutoFitFramesY > 0 || (window.Flags & ImGuiWindowFlags.AlwaysAutoResize) != 0;

        if ((flags & ImGuiScrollFlags.KeepVisibleEdgeX) != 0 && !fullyVisibleX)
        {
            if (itemRect.Min.X < scrollRect.Min.X || !canBeFullyVisibleX)
                SetScrollFromPosX(window, itemRect.Min.X - g.Style.ItemSpacing.X - window.Pos.X, 0.0f);
            else if (itemRect.Max.X >= scrollRect.Max.X)
                SetScrollFromPosX(window, itemRect.Max.X + g.Style.ItemSpacing.X - window.Pos.X, 1.0f);
        }
        else if (((flags & ImGuiScrollFlags.KeepVisibleCenterX) != 0 && !fullyVisibleX) || (flags & ImGuiScrollFlags.AlwaysCenterX) != 0)
        {
            if (canBeFullyVisibleX)
                SetScrollFromPosX(window, (float)(int)((itemRect.Min.X + itemRect.Max.X) * 0.5f) - window.Pos.X, 0.5f);
            else
                SetScrollFromPosX(window, itemRect.Min.X - window.Pos.X, 0.0f);
        }

        if ((flags & ImGuiScrollFlags.KeepVisibleEdgeY) != 0 && !fullyVisibleY)
        {
            if (itemRect.Min.Y < scrollRect.Min.Y || !canBeFullyVisibleY)
                SetScrollFromPosY(window, itemRect.Min.Y - g.Style.ItemSpacing.Y - window.Pos.Y, 0.0f);
            else if (itemRect.Max.Y >= scrollRect.Max.Y)
                SetScrollFromPosY(window, itemRect.Max.Y + g.Style.ItemSpacing.Y - window.Pos.Y, 1.0f);
        }
        else if (((flags & ImGuiScrollFlags.KeepVisibleCenterY) != 0 && !fullyVisibleY) || (flags & ImGuiScrollFlags.AlwaysCenterY) != 0)
        {
            if (canBeFullyVisibleY)
                SetScrollFromPosY(window, (float)(int)((itemRect.Min.Y + itemRect.Max.Y) * 0.5f) - window.Pos.Y, 0.5f);
            else
                SetScrollFromPosY(window, itemRect.Min.Y - window.Pos.Y, 0.0f);
        }

        ImVec2 nextScroll = CalcNextScrollFromScrollTargetAndClamp(window);
        ImVec2 deltaScroll = nextScroll - window.Scroll;

        // Also scroll the parent window to keep the rectangle into view when necessary.
        if ((flags & ImGuiScrollFlags.NoScrollParent) == 0 && (window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
        {
            if ((inFlags & (ImGuiScrollFlags.AlwaysCenterX | ImGuiScrollFlags.KeepVisibleCenterX)) != 0)
                inFlags = (inFlags & ~ImGuiScrollFlags.MaskX_) | ImGuiScrollFlags.KeepVisibleEdgeX;
            if ((inFlags & (ImGuiScrollFlags.AlwaysCenterY | ImGuiScrollFlags.KeepVisibleCenterY)) != 0)
                inFlags = (inFlags & ~ImGuiScrollFlags.MaskY_) | ImGuiScrollFlags.KeepVisibleEdgeY;
            ImGuiWindow? parentWindow = window.ParentWindow;
            ImAssert.Check(parentWindow != null);
            if (parentWindow != null)
                deltaScroll += ScrollToRectEx(parentWindow, new ImRect(itemRect.Min - deltaScroll, itemRect.Max - deltaScroll), inFlags);
        }

        return deltaScroll;
    }

    /// <summary>
    /// Scrolls a window just enough to bring a rectangle into view on the vertical axis.
    /// </summary>
    /// <param name="window">The window to scroll.</param>
    /// <param name="rect">The rectangle, in absolute screen coordinates, that must be brought into
    /// view.</param>
    /// <remarks>
    /// Ports the canonical inline wrapper <c>ScrollToBringRectIntoView</c>
    ///., which forwards to
    /// <see cref="ScrollToRect"/> with <see cref="ImGuiScrollFlags.KeepVisibleEdgeY"/>.
    /// </remarks>
    internal static void ScrollToBringRectIntoView(ImGuiWindow window, ImRect rect)
    {
        ScrollToRect(window, rect, ImGuiScrollFlags.KeepVisibleEdgeY);
    }

    /// <summary>
    /// Gets the current horizontal scroll offset of the current window.
    /// </summary>
    /// <returns>The <c>Scroll.x</c> value of the current window.</returns>
    /// <remarks>
    /// Ports canonical <c>GetScrollX</c>.
    /// </remarks>
    public static float GetScrollX()
    {
        return GetCurrentWindow().Scroll.X;
    }

    /// <summary>
    /// Gets the current vertical scroll offset of the current window.
    /// </summary>
    /// <returns>The <c>Scroll.y</c> value of the current window.</returns>
    /// <remarks>
    /// Ports canonical <c>GetScrollY</c>.
    /// </remarks>
    public static float GetScrollY()
    {
        return GetCurrentWindow().Scroll.Y;
    }

    /// <summary>
    /// Gets the maximum legal horizontal scroll offset of the current window.
    /// </summary>
    /// <returns>The <c>ScrollMax.x</c> value of the current window.</returns>
    /// <remarks>
    /// Ports canonical <c>GetScrollMaxX</c>.
    /// </remarks>
    public static float GetScrollMaxX()
    {
        return GetCurrentWindow().ScrollMax.X;
    }

    /// <summary>
    /// Gets the maximum legal vertical scroll offset of the current window.
    /// </summary>
    /// <returns>The <c>ScrollMax.y</c> value of the current window.</returns>
    /// <remarks>
    /// Ports canonical <c>GetScrollMaxY</c>.
    /// </remarks>
    public static float GetScrollMaxY()
    {
        return GetCurrentWindow().ScrollMax.Y;
    }

    /// <summary>
    /// Sets the horizontal scroll offset of a window for the next frame.
    /// </summary>
    /// <param name="window">The window whose horizontal scroll is requested.</param>
    /// <param name="scrollX">The requested horizontal scroll offset, in pixels.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetScrollX(ImGuiWindow*, float)</c>
    ///. The value is stored as a pending target with a
    /// center ratio of <c>0.0f</c> and no edge snapping; <c>Scroll</c> itself is not modified until the
    /// window's next <c>Begin</c> resolves the target through
    /// <see cref="CalcNextScrollFromScrollTargetAndClamp"/>.
    /// </para>
    /// <para>
    /// This window overload is canonical internal API. and is not listed
    /// in CONTRACT §5; it is required by <see cref="ScrollToRectEx"/> and by <c>Begin</c> callers.
    /// </para>
    /// </remarks>
    internal static void SetScrollX(ImGuiWindow window, float scrollX)
    {
        ImVec2 target = window.ScrollTarget;
        SetAxisComponent(ref target, ImGuiAxis.X, scrollX);
        window.ScrollTarget = target;
        window.ScrollTargetCenterRatio = new ImVec2(0.0f, window.ScrollTargetCenterRatio.Y);
        window.ScrollTargetEdgeSnapDist = new ImVec2(0.0f, window.ScrollTargetEdgeSnapDist.Y);
    }

    /// <summary>
    /// Sets the vertical scroll offset of a window for the next frame.
    /// </summary>
    /// <param name="window">The window whose vertical scroll is requested.</param>
    /// <param name="scrollY">The requested vertical scroll offset, in pixels.</param>
    /// <remarks>
    /// Ports canonical <c>SetScrollY(ImGuiWindow*, float)</c>
    ///., the Y mirror of
    /// <see cref="SetScrollX(ImGuiWindow, float)"/>. Canonical internal API
    ///. and an addition to CONTRACT §5.
    /// </remarks>
    internal static void SetScrollY(ImGuiWindow window, float scrollY)
    {
        ImVec2 target = window.ScrollTarget;
        SetAxisComponent(ref target, ImGuiAxis.Y, scrollY);
        window.ScrollTarget = target;
        window.ScrollTargetCenterRatio = new ImVec2(window.ScrollTargetCenterRatio.X, 0.0f);
        window.ScrollTargetEdgeSnapDist = new ImVec2(window.ScrollTargetEdgeSnapDist.X, 0.0f);
    }

    /// <summary>
    /// Sets the horizontal scroll offset of the current window for the next frame.
    /// </summary>
    /// <param name="scrollX">The requested horizontal scroll offset, in pixels.</param>
    /// <remarks>
    /// Ports canonical <c>SetScrollX(float)</c>., which
    /// forwards to <see cref="SetScrollX(ImGuiWindow, float)"/> with <c>g.CurrentWindow</c>.
    /// </remarks>
    public static void SetScrollX(float scrollX)
    {
        SetScrollX(GetCurrentWindow(), scrollX);
    }

    /// <summary>
    /// Sets the vertical scroll offset of the current window for the next frame.
    /// </summary>
    /// <param name="scrollY">The requested vertical scroll offset, in pixels.</param>
    /// <remarks>
    /// Ports canonical <c>SetScrollY(float)</c>., which
    /// forwards to <see cref="SetScrollY(ImGuiWindow, float)"/> with <c>g.CurrentWindow</c>.
    /// </remarks>
    public static void SetScrollY(float scrollY)
    {
        SetScrollY(GetCurrentWindow(), scrollY);
    }

    /// <summary>
    /// Requests a horizontal scroll position from a local position within the window.
    /// </summary>
    /// <param name="window">The window whose horizontal scroll is requested.</param>
    /// <param name="localX">The local X position, measured from the window's upper-left corner.</param>
    /// <param name="centerXRatio">The centering ratio applied to the position: <c>0.0f</c> places it at the
    /// left edge of the view, <c>0.5f</c> centers it, and <c>1.0f</c> places it at the right edge.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetScrollFromPosX(ImGuiWindow*, float, float)</c>
    ///. The local position is converted into a
    /// scroll-offset target by adding the current scroll and subtracting the outer and inner left
    /// decoration sizes, then truncating toward zero.
    /// </para>
    /// <para>
    /// Because a local position depends on the current scroll value, canonical documents that
    /// <c>SetScrollFromPosY(0.0f)</c> is a no-op and <c>SetScrollFromPosY(-scroll.y)</c> resets the scroll.
    /// The target is applied on the next frame once the window size is known.
    /// </para>
    /// <para>
    /// Canonical asserts that <paramref name="centerXRatio"/> lies in the zero-to-one range; this port keeps
    /// that as <see cref="ImAssert.Check(bool, string)"/>. This window overload is canonical internal API
    ///. and an addition to CONTRACT §5.
    /// </para>
    /// </remarks>
    internal static void SetScrollFromPosX(ImGuiWindow window, float localX, float centerXRatio)
    {
        ImAssert.Check(centerXRatio >= 0.0f && centerXRatio <= 1.0f);
        ImVec2 target = window.ScrollTarget;
        SetAxisComponent(ref target, ImGuiAxis.X, (float)(int)(localX - window.DecoOuterSizeX1 - window.DecoInnerSizeX1 + window.Scroll.X));
        window.ScrollTarget = target;
        window.ScrollTargetCenterRatio = new ImVec2(centerXRatio, window.ScrollTargetCenterRatio.Y);
        window.ScrollTargetEdgeSnapDist = new ImVec2(0.0f, window.ScrollTargetEdgeSnapDist.Y);
    }

    /// <summary>
    /// Requests a vertical scroll position from a local position within the window.
    /// </summary>
    /// <param name="window">The window whose vertical scroll is requested.</param>
    /// <param name="localY">The local Y position, measured from the window's upper-left corner.</param>
    /// <param name="centerYRatio">The centering ratio applied to the position: <c>0.0f</c> places it at the
    /// top edge of the view, <c>0.5f</c> centers it, and <c>1.0f</c> places it at the bottom edge.</param>
    /// <remarks>
    /// Ports canonical <c>SetScrollFromPosY(ImGuiWindow*, float, float)</c>
    ///., the Y mirror of
    /// <see cref="SetScrollFromPosX(ImGuiWindow, float, float)"/>. Canonical internal API
    ///. and an addition to CONTRACT §5.
    /// </remarks>
    internal static void SetScrollFromPosY(ImGuiWindow window, float localY, float centerYRatio)
    {
        ImAssert.Check(centerYRatio >= 0.0f && centerYRatio <= 1.0f);
        ImVec2 target = window.ScrollTarget;
        SetAxisComponent(ref target, ImGuiAxis.Y, (float)(int)(localY - window.DecoOuterSizeY1 - window.DecoInnerSizeY1 + window.Scroll.Y));
        window.ScrollTarget = target;
        window.ScrollTargetCenterRatio = new ImVec2(window.ScrollTargetCenterRatio.X, centerYRatio);
        window.ScrollTargetEdgeSnapDist = new ImVec2(window.ScrollTargetEdgeSnapDist.X, 0.0f);
    }

    /// <summary>
    /// Requests a horizontal scroll position from a local position within the current window.
    /// </summary>
    /// <param name="localX">The local X position, measured from the current window's upper-left
    /// corner.</param>
    /// <param name="centerXRatio">The centering ratio applied to the position: <c>0.0f</c> places it at the
    /// left edge of the view, <c>0.5f</c> centers it, and <c>1.0f</c> places it at the right edge.</param>
    /// <remarks>
    /// Ports canonical <c>SetScrollFromPosX(float, float)</c>
    ///., which forwards to
    /// <see cref="SetScrollFromPosX(ImGuiWindow, float, float)"/>.
    /// </remarks>
    public static void SetScrollFromPosX(float localX, float centerXRatio = 0.5f)
    {
        SetScrollFromPosX(GetCurrentWindow(), localX, centerXRatio);
    }

    /// <summary>
    /// Requests a vertical scroll position from a local position within the current window.
    /// </summary>
    /// <param name="localY">The local Y position, measured from the current window's upper-left
    /// corner.</param>
    /// <param name="centerYRatio">The centering ratio applied to the position: <c>0.0f</c> places it at the
    /// top edge of the view, <c>0.5f</c> centers it, and <c>1.0f</c> places it at the bottom edge.</param>
    /// <remarks>
    /// Ports canonical <c>SetScrollFromPosY(float, float)</c>
    ///., which forwards to
    /// <see cref="SetScrollFromPosY(ImGuiWindow, float, float)"/>.
    /// </remarks>
    public static void SetScrollFromPosY(float localY, float centerYRatio = 0.5f)
    {
        SetScrollFromPosY(GetCurrentWindow(), localY, centerYRatio);
    }

    /// <summary>
    /// Requests a horizontal scroll position that places the last submitted item at the supplied centering
    /// ratio of the visible area.
    /// </summary>
    /// <param name="centerXRatio">The centering ratio relative to the last item: <c>0.0f</c> aligns the
    /// item's left edge with the left edge of the view, <c>0.5f</c> centers it, and <c>1.0f</c> aligns its
    /// right edge.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetScrollHereX</c>. The target
    /// position is interpolated between the last item's rectangle expanded by
    /// <c>max(WindowPadding.x, ItemSpacing.x)</c> on each side, then converted from absolute to local
    /// coordinates before being stored.
    /// </para>
    /// <para>
    /// The edge-snap distance is then set to <c>max(0.0f, WindowPadding.x - spacing_x)</c> so that aiming at
    /// an item very close to the edge keeps the padding-versus-spacing difference visible.
    /// </para>
    /// </remarks>
    public static void SetScrollHereX(float centerXRatio = 0.5f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        float spacingX = MathF.Max(window.WindowPadding.X, g.Style.ItemSpacing.X);
        float targetPosX = g.LastItemData.Rect.Min.X - spacingX + (g.LastItemData.Rect.Max.X + spacingX - (g.LastItemData.Rect.Min.X - spacingX)) * centerXRatio;
        SetScrollFromPosX(window, targetPosX - window.Pos.X, centerXRatio);

        // Tweak: snap on edges when aiming at an item very close to the edge.
        ImVec2 edgeSnapDist = window.ScrollTargetEdgeSnapDist;
        SetAxisComponent(ref edgeSnapDist, ImGuiAxis.X, MathF.Max(0.0f, window.WindowPadding.X - spacingX));
        window.ScrollTargetEdgeSnapDist = edgeSnapDist;
    }

    /// <summary>
    /// Requests a vertical scroll position that places the last submitted item at the supplied centering
    /// ratio of the visible area.
    /// </summary>
    /// <param name="centerYRatio">The centering ratio relative to the last item: <c>0.0f</c> aligns the
    /// item's top edge with the top edge of the view, <c>0.5f</c> centers it, and <c>1.0f</c> aligns its
    /// bottom edge.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>SetScrollHereY</c>. Unlike the X
    /// form, the Y target is built from <c>DC.CursorPosPrevLine.y</c> and <c>DC.PrevLineSize.y</c> rather
    /// than from the last item rectangle, because a line-oriented layout advances the cursor before the item
    /// rectangle is finalized.
    /// </para>
    /// <para>
    /// The edge-snap distance is set to <c>max(0.0f, WindowPadding.y - spacing_y)</c>.
    /// </para>
    /// </remarks>
    public static void SetScrollHereY(float centerYRatio = 0.5f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        float spacingY = MathF.Max(window.WindowPadding.Y, g.Style.ItemSpacing.Y);
        float lineTop = window.DC.CursorPosPrevLine.Y - spacingY;
        float lineBottom = window.DC.CursorPosPrevLine.Y + window.DC.PrevLineSize.Y + spacingY;
        float targetPosY = lineTop + (lineBottom - lineTop) * centerYRatio;
        SetScrollFromPosY(window, targetPosY - window.Pos.Y, centerYRatio);

        // Tweak: snap on edges when aiming at an item very close to the edge.
        ImVec2 edgeSnapDist = window.ScrollTargetEdgeSnapDist;
        SetAxisComponent(ref edgeSnapDist, ImGuiAxis.Y, MathF.Max(0.0f, window.WindowPadding.Y - spacingY));
        window.ScrollTargetEdgeSnapDist = edgeSnapDist;
    }

    #endregion

    #region Widgets: Window Decorations

    /// <summary>
    /// Computes the corner-rounding flags that keep a rectangle's rounded corners only where it touches the
    /// outer rectangle's edges.
    /// </summary>
    /// <param name="rIn">The rectangle being drawn.</param>
    /// <param name="rOuter">The host rectangle whose edges define where rounding is allowed.</param>
    /// <param name="threshold">The distance from an outer edge within which the corresponding corners are
    /// rounded.</param>
    /// <returns>The selected <see cref="ImDrawFlags.RoundCornersMask_"/> bits, or
    /// <see cref="ImDrawFlags.RoundCornersNone"/> when the inner rectangle touches no edge.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>CalcRoundingFlagsForRectInRect</c>
    /// (, declared ). Each of the
    /// four corners is rounded only when both of its edges lie within <paramref name="threshold"/> of the
    /// matching outer edges.
    /// </para>
    /// <para>
    /// CONTRACT §2 assigns this canonical helper to no part. It is declared here as an unowned shared
    /// internal helper because <see cref="Scrollbar"/> requires it; the assembled file must contain exactly
    /// one definition, so no other part may declare it.
    /// </para>
    /// </remarks>
    internal static ImDrawFlags CalcRoundingFlagsForRectInRect(ImRect rIn, ImRect rOuter, float threshold)
    {
        bool roundLeft = rIn.Min.X <= rOuter.Min.X + threshold;
        bool roundRight = rIn.Max.X >= rOuter.Max.X - threshold;
        bool roundTop = rIn.Min.Y <= rOuter.Min.Y + threshold;
        bool roundBottom = rIn.Max.Y >= rOuter.Max.Y - threshold;
        return ImDrawFlags.RoundCornersNone
            | ((roundTop && roundLeft) ? ImDrawFlags.RoundCornersTopLeft : ImDrawFlags.None)
            | ((roundTop && roundRight) ? ImDrawFlags.RoundCornersTopRight : ImDrawFlags.None)
            | ((roundBottom && roundLeft) ? ImDrawFlags.RoundCornersBottomLeft : ImDrawFlags.None)
            | ((roundBottom && roundRight) ? ImDrawFlags.RoundCornersBottomRight : ImDrawFlags.None);
    }

    /// <summary>
    /// Returns the identifier used by the scrollbar for the supplied axis.
    /// </summary>
    /// <param name="window">The window that owns the scrollbar.</param>
    /// <param name="axis">The scrollbar axis.</param>
    /// <returns>The window-scoped hash of <c>"#SCROLLX"</c> for the X axis, or of <c>"#SCROLLY"</c> for the
    /// Y axis.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>GetWindowScrollbarID</c>.
    /// Canonical hashes through <c>ImGuiWindow::GetID</c>., which seeds the hash
    /// with <c>IDStack.back()</c>; this port calls the part-06 <c>ImHashStr</c> hash with the same seed.
    /// </para>
    /// <para>
    /// The canonical debug-tools branch that invokes <c>DebugHookIdInfo</c> when
    /// <c>g.DebugHookIdInfoId</c> matches is deferred to the debug-tools chunk (12) and is not reproduced
    /// here. Canonical assumes the window identity stack is non-empty; this port asserts that precondition.
    /// </para>
    /// </remarks>
    internal static uint GetWindowScrollbarID(ImGuiWindow window, ImGuiAxis axis)
    {
        ImAssert.Check(window.IDStack.Count > 0);
        uint seed = window.IDStack[window.IDStack.Count - 1];
        return ImHashStr(axis == ImGuiAxis.X ? "#SCROLLX" : "#SCROLLY", 0, seed);
    }

    /// <summary>
    /// Returns the scrollbar rectangle for the supplied axis.
    /// </summary>
    /// <param name="window">The window that owns the scrollbar. Its <c>InnerRect</c>, <c>Rect</c>,
    /// <c>ScrollbarSizes</c>, and border metrics are read.</param>
    /// <param name="axis">The scrollbar axis.</param>
    /// <returns>The scrollbar rectangle in absolute screen coordinates.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>GetWindowScrollbarRect</c>.
    /// The function must only be called for an axis whose <c>ScrollbarX</c> or <c>ScrollbarY</c> flag is set.
    /// </para>
    /// <para>
    /// <c>ScrollbarSizes.x</c> is the width of the vertical scrollbar and <c>ScrollbarSizes.y</c> is the
    /// height of the horizontal scrollbar, so canonical indexes the opposite axis (<c>axis ^ 1</c>); the
    /// port selects the field explicitly. The reserved top border depends on whether the window has a menu
    /// bar, no title bar, or neither.
    /// </para>
    /// </remarks>
    internal static ImRect GetWindowScrollbarRect(ImGuiWindow window, ImGuiAxis axis)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImRect outerRect = window.Rect();
        ImRect innerRect = window.InnerRect;
        // ScrollbarSizes.x = width of the Y scrollbar; ScrollbarSizes.y = height of the X scrollbar.
        float scrollbarSize = axis == ImGuiAxis.X ? window.ScrollbarSizes.Y : window.ScrollbarSizes.X;
        ImAssert.Check(scrollbarSize >= 0.0f);
        float borderSize = (float)(int)(window.WindowBorderSize * 0.5f + 0.5f);
        float borderTop = (window.Flags & ImGuiWindowFlags.MenuBar) != 0
            ? (float)(int)(g.Style.FrameBorderSize * 0.5f + 0.5f)
            : (window.Flags & ImGuiWindowFlags.NoTitleBar) != 0 ? borderSize : 0.0f;
        if (axis == ImGuiAxis.X)
            return new ImRect(
                innerRect.Min.X + borderSize,
                MathF.Max(outerRect.Min.Y + borderSize, outerRect.Max.Y - borderSize - scrollbarSize),
                innerRect.Max.X - borderSize,
                outerRect.Max.Y - borderSize);
        return new ImRect(
            MathF.Max(outerRect.Min.X, outerRect.Max.X - borderSize - scrollbarSize),
            innerRect.Min.Y + borderTop,
            outerRect.Max.X - borderSize,
            innerRect.Max.Y - borderSize);
    }

    /// <summary>
    /// Extends a scrollbar hit box onto the viewport edge when the scrollbar is flush against that edge.
    /// </summary>
    /// <param name="window">The window whose scrollbar is being hit-tested.</param>
    /// <param name="bb">The hit-box rectangle to extend in place.</param>
    /// <param name="threshold">The distance from the viewport edge within which the hit box is
    /// extended.</param>
    /// <param name="axis">The axis to test. Canonical passes the scrollbar's perpendicular axis, so a
    /// vertical scrollbar tests the horizontal axis.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ExtendHitBoxWhenNearViewportEdge</c>
    /// (, declared ). The
    /// extension makes a scrollbar pinned to the screen edge easier to grab (issue #9276).
    /// </para>
    /// <para>
    /// Canonical assumes the root window and viewport are non-null and dereferences them; this port asserts
    /// those preconditions and returns without modifying the hit box when either is absent.
    /// </para>
    /// </remarks>
    internal static void ExtendHitBoxWhenNearViewportEdge(ImGuiWindow window, ref ImRect bb, float threshold, ImGuiAxis axis)
    {
        ImGuiWindow? rootWindow = window.RootWindow;
        ImGuiViewportP? viewport = window.Viewport;
        ImAssert.Check(rootWindow != null);
        ImAssert.Check(viewport != null);
        if (rootWindow == null || viewport == null)
            return;

        ImRect windowRect = rootWindow.Rect();
        ImRect viewportRect = viewport.GetMainRect();
        float windowMin = AxisComponent(windowRect.Min, axis);
        float windowMax = AxisComponent(windowRect.Max, axis);
        if (windowMin == AxisComponent(viewportRect.Min, axis)
            && AxisComponent(bb.Min, axis) > windowMin
            && AxisComponent(bb.Min, axis) - threshold <= windowMin)
            SetAxisComponent(ref bb.Min, axis, windowMin);
        if (windowMax == AxisComponent(viewportRect.Max, axis)
            && AxisComponent(bb.Max, axis) < windowMax
            && AxisComponent(bb.Max, axis) + threshold >= windowMax)
            SetAxisComponent(ref bb.Max, axis, windowMax);
    }

    /// <summary>
    /// Draws and services one scrollbar of the current window.
    /// </summary>
    /// <param name="axis">The scrollbar axis.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>Scrollbar</c>., called from
    /// <c>Begin</c> after the content size and <c>InnerRect</c> are known and before the window's starting
    /// cursor position is derived from the scroll value. The scrollbar identifier and rectangle come from
    /// <see cref="GetWindowScrollbarID"/> and <see cref="GetWindowScrollbarRect"/>.
    /// </para>
    /// <para>
    /// The visible size along the axis is the <c>InnerRect</c> extent; the content size is
    /// <c>ContentSize[axis] + WindowPadding[axis] * 2</c>. Canonical stores the scroll value as
    /// <c>ImS64</c> for the duration of the interaction, truncating the float <c>Scroll</c> component on
    /// entry and writing the truncated result back on exit. This port has no <c>ImS64</c>: scroll values are
    /// <c>float</c> throughout, and the canonical typed truncation is reproduced with a
    /// <c>(float)(long)</c> cast so the observable value is identical.
    /// </para>
    /// <para>
    /// Canonical computes the host rectangle as <c>(DockIsActive ? DockNode-&gt;HostWindow : window)-&gt;Rect()</c>.
    /// The docking subsystem, including <c>DockNode</c>, is owned by a later chunk, so the guarded docked
    /// branch is inert here and the window rectangle is always used. Once docking lands, this call site must
    /// substitute the dock node host window.
    /// </para>
    /// </remarks>
    internal static void Scrollbar(ImGuiAxis axis)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        uint id = GetWindowScrollbarID(window, axis);

        // Calculate scrollbar bounding box.
        ImRect bb = GetWindowScrollbarRect(window, axis);
        ImRect hostRect = window.Rect();
        ImDrawFlags roundingCorners = CalcRoundingFlagsForRectInRect(bb, hostRect, g.Style.WindowBorderSize);
        float sizeVisible = AxisComponent(window.InnerRect.Max, axis) - AxisComponent(window.InnerRect.Min, axis);
        float sizeContents = AxisComponent(window.ContentSize, axis) + AxisComponent(window.WindowPadding, axis) * 2.0f;
        float scroll = (float)(long)AxisComponent(window.Scroll, axis);
        // Canonical truncates the visible and content sizes to ImS64 at this call site; the port keeps the
        // float representation and reproduces the truncation explicitly.
        ScrollbarEx(bb, id, axis, ref scroll, (float)(long)sizeVisible, (float)(long)sizeContents, roundingCorners);

        ImVec2 windowScroll = window.Scroll;
        SetAxisComponent(ref windowScroll, axis, scroll);
        window.Scroll = windowScroll;
    }

    /// <summary>
    /// Draws and services a scrollbar from explicit geometry and scroll state.
    /// </summary>
    /// <param name="bbFrame">The scrollbar frame rectangle in absolute screen coordinates.</param>
    /// <param name="id">The scrollbar identifier returned by <see cref="GetWindowScrollbarID"/>.</param>
    /// <param name="axis">The scrollbar axis.</param>
    /// <param name="pScroll">The scroll value to read and, when the user interacts, to update.</param>
    /// <param name="availV">The visible size along the scrollbar's main axis.</param>
    /// <param name="contentsV">The total content size along the scrollbar's main axis.</param>
    /// <param name="drawRoundingFlags">Corner-rounding flags for the scrollbar background, normally from
    /// <see cref="CalcRoundingFlagsForRectInRect"/>.</param>
    /// <returns><see langword="true"/> while the scrollbar grab is held; otherwise
    /// <see langword="false"/>.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ScrollbarEx</c>. Canonical
    /// stores the value behind <paramref name="pScroll"/> and the sizes as <c>ImS64</c>; this port has no
    /// <c>ImS64</c> and uses <paramref name="pScroll"/> as a <c>float</c> directly, reproducing each
    /// canonical integer conversion with an explicit truncating cast.
    /// </para>
    /// <para>
    /// Early-outs reproduce the canonical guards: the item is skipped, a non-positive frame rectangle is
    /// rejected, and a vertical scrollbar narrower in height than twice its width fades out and becomes
    /// non-interactive. Normalized grab geometry is derived from the visible-to-total ratio with the
    /// <c>GrabMinSize</c> lower bound; the grab size in pixels is truncated to an integer exactly as
    /// canonical does.
    /// </para>
    /// <para>
    /// Interaction goes through the part-05 <c>ButtonBehavior</c>, which owns the active, hovered, and focus
    /// identifier transitions. On the activation frame the canonical code chooses between absolute seeking
    /// and page-by-page seeking: page mode is selected when <c>ConfigScrollbarScrollByPage</c> is enabled and
    /// the click landed outside the grab without Shift held, and only then does a repeated left click page
    /// the view by one visible size in the held direction. The interaction writes
    /// <c>g.ScrollbarSeekMode</c> and <c>g.ScrollbarClickDeltaToGrabCenter</c> so the grab does not jump
    /// while held.
    /// </para>
    /// <para>
    /// Canonical calls <c>IsMouseClicked(ImGuiMouseButton_Left, ImGuiInputFlags_Repeat)</c>; the port's
    /// part-02 signature is <c>IsMouseClicked(ImGuiMouseButton, bool repeat)</c>, which canonical implements
    /// as the same repeat query, so the call uses <c>true</c> for <c>repeat</c>.
    /// </para>
    /// </remarks>
    internal static bool ScrollbarEx(ImRect bbFrame, uint id, ImGuiAxis axis, ref float pScroll, float availV, float contentsV, ImDrawFlags drawRoundingFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        float bbFrameWidth = bbFrame.GetWidth();
        float bbFrameHeight = bbFrame.GetHeight();
        if (bbFrameWidth <= 0.0f || bbFrameHeight <= 0.0f)
            return false;

        // When the scrollbar is too small, start hiding and disabling the grab. This reduces visual noise on
        // very small windows and facilitates using the window resize grab.
        float alpha = 1.0f;
        if (axis == ImGuiAxis.Y && bbFrameHeight < bbFrameWidth)
            alpha = Math.Clamp(bbFrameHeight / MathF.Max(bbFrameWidth * 2.0f, 1.0f), 0.0f, 1.0f);
        if (alpha <= 0.0f)
            return false;

        ImGuiStyle style = g.Style;
        bool allowInteraction = alpha >= 1.0f;

        ImRect bb = bbFrame;
        float padding = (float)(int)MathF.Min(style.ScrollbarPadding, MathF.Min(bbFrameWidth, bbFrameHeight) * 0.5f);
        bb.Expand(-padding);

        // V denotes the main, longer axis of the scrollbar (height for a vertical scrollbar).
        float scrollbarSizeV = axis == ImGuiAxis.X ? bb.GetWidth() : bb.GetHeight();
        if (scrollbarSizeV < 1.0f)
            return false;

        // Calculate the height of the grabbable box: it generally represents the visible amount versus the
        // total scrollable amount, but a minimum size in pixels keeps it aimable.
        ImAssert.Check(MathF.Max(contentsV, availV) > 0.0f);
        float winSizeV = MathF.Max(MathF.Max(contentsV, availV), 1.0f);
        float grabHMinsize = MathF.Min(AxisComponent(bb.GetSize(), axis), style.GrabMinSize);
        float grabHPixels = (float)(int)Math.Clamp(scrollbarSizeV * (availV / winSizeV), grabHMinsize, scrollbarSizeV);
        float grabHNorm = grabHPixels / scrollbarSizeV;

        // A scrollbar near the edge of a screen or viewport must remain reachable with the mouse at the
        // extreme edge (#9276).
        ImRect bbHit = bbFrame;
        ExtendHitBoxWhenNearViewportEdge(window, ref bbHit, g.Style.WindowBorderSize, (ImGuiAxis)((int)axis ^ 1));

        // Handle input right away. None of the Begin() code relies on the scrolling position before calling
        // Scrollbar().
        bool held = false;
        bool hovered = false;
        ItemAdd(bbFrame, id, null, ImGuiItemFlags.NoNav);
        // Canonical ImGuiButtonFlags is an integer typedef, so the private ImGuiButtonFlags_NoNavFocus bit
        // passes directly; the port declares ImGuiButtonFlags and the private bits as separate enums, so the
        // private bit is cast into the public parameter type.
        ButtonBehavior(bbHit, id, out hovered, out held, (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus);

        float scrollMax = MathF.Max(1.0f, contentsV - availV);
        float scrollRatio = Math.Clamp(pScroll / scrollMax, 0.0f, 1.0f);
        float grabVNorm = scrollRatio * (scrollbarSizeV - grabHPixels) / scrollbarSizeV; // Grab position in normalized space
        if (held && allowInteraction && grabHNorm < 1.0f)
        {
            float scrollbarPosV = AxisComponent(bb.Min, axis);
            float mousePosV = AxisComponent(g.IO.MousePos, axis);

            // Click position in scrollbar normalized space (0.0f -> 1.0f).
            float clickedVNorm = Math.Clamp((mousePosV - scrollbarPosV) / scrollbarSizeV, 0.0f, 1.0f);

            int heldDir = clickedVNorm < grabVNorm ? -1 : clickedVNorm > grabVNorm + grabHNorm ? +1 : 0;
            if (g.ActiveIdIsJustActivated)
            {
                // On the initial click with held_dir == 0 (clicked over the grab), calculate the distance
                // between the mouse and the center of the grab.
                bool scrollToClickedLocation = g.IO.ConfigScrollbarScrollByPage == false || g.IO.KeyShift || heldDir == 0;
                g.ScrollbarSeekMode = scrollToClickedLocation ? (short)0 : (short)heldDir;
                g.ScrollbarClickDeltaToGrabCenter = (heldDir == 0 && !g.IO.KeyShift) ? clickedVNorm - grabVNorm - grabHNorm * 0.5f : 0.0f;
            }

            // Apply scroll. p_scroll points at one component of the window scroll vector. It is safe to
            // modify the scroll here because Scrollbar() runs inside Begin() after the content size is
            // computed and before the starting position is set up.
            if (g.ScrollbarSeekMode == 0)
            {
                // Absolute seeking.
                float scrollVNorm = Math.Clamp((clickedVNorm - g.ScrollbarClickDeltaToGrabCenter - grabHNorm * 0.5f) / (1.0f - grabHNorm), 0.0f, 1.0f);
                pScroll = (float)(long)(scrollVNorm * scrollMax);
            }
            else
            {
                // Page by page.
                if (IsMouseClicked(ImGuiMouseButton.Left, true) && heldDir == g.ScrollbarSeekMode)
                {
                    float pageDir = g.ScrollbarSeekMode > 0.0f ? +1.0f : -1.0f;
                    pScroll = Math.Clamp(pScroll + (float)(long)(pageDir * availV), 0.0f, scrollMax);
                }
            }

            // Update values for rendering.
            scrollRatio = Math.Clamp(pScroll / scrollMax, 0.0f, 1.0f);
            grabVNorm = scrollRatio * (scrollbarSizeV - grabHPixels) / scrollbarSizeV;
        }

        // Render.
        uint bgCol = GetColorU32(ImGuiCol.ScrollbarBg);
        uint grabCol = GetColorU32(held ? ImGuiCol.ScrollbarGrabActive : hovered ? ImGuiCol.ScrollbarGrabHovered : ImGuiCol.ScrollbarGrab, alpha);
        window.DrawList.AddRectFilled(bbFrame.Min, bbFrame.Max, bgCol, window.WindowRounding, drawRoundingFlags);
        ImRect grabRect;
        if (axis == ImGuiAxis.X)
        {
            float grabStartX = bb.Min.X + (bb.Max.X - bb.Min.X) * grabVNorm;
            grabRect = new ImRect(grabStartX, bb.Min.Y, grabStartX + grabHPixels, bb.Max.Y);
        }
        else
        {
            float grabStartY = bb.Min.Y + (bb.Max.Y - bb.Min.Y) * grabVNorm;
            grabRect = new ImRect(bb.Min.X, grabStartY, bb.Max.X, grabStartY + grabHPixels);
        }
        window.DrawList.AddRectFilled(grabRect.Min, grabRect.Max, grabCol, style.ScrollbarRounding);

        return held;
    }

    #endregion

    #endregion


    #region SECTION: DISPLAY LAYERING, DRAW DATA, AND NAVIGATION

    #region Display Layering & Draw-Data Assembly

    /// <summary>
    /// Orders two sibling windows for the draw-data sort: popups after ordinary windows, tooltips after
    /// popups, and otherwise by the order in which they were first submitted within their parent.
    /// </summary>
    /// <param name="a">The left window to compare.</param>
    /// <param name="b">The right window to compare.</param>
    /// <returns>A negative value when <paramref name="a"/> sorts first, a positive value when
    /// <paramref name="b"/> sorts first, and zero when the submission order is equal.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>ChildWindowComparer</c>
    ///. The canonical implementation subtracts the masked
    /// flag values as integers; the explicit casts reproduce that integer arithmetic under a C# flags
    /// enum.
    /// </remarks>
    private static int ChildWindowComparer(ImGuiWindow a, ImGuiWindow b)
    {
        int d = (int)(a.Flags & ImGuiWindowFlags.Popup) - (int)(b.Flags & ImGuiWindowFlags.Popup);
        if (d != 0)
            return d;
        d = (int)(a.Flags & ImGuiWindowFlags.Tooltip) - (int)(b.Flags & ImGuiWindowFlags.Tooltip);
        if (d != 0)
            return d;
        return a.BeginOrderWithinParent - b.BeginOrderWithinParent;
    }

    /// <summary>
    /// Cached comparison delegate for <see cref="ChildWindowComparer"/>, so the per-frame window sort
    /// does not allocate a delegate for every window.
    /// </summary>
    private static readonly Comparison<ImGuiWindow> ChildWindowComparison = ChildWindowComparer;

    /// <summary>
    /// Flattens a window and its active children into a display-ordered scratch list, parents before
    /// children and siblings in <see cref="ChildWindowComparer"/> order.
    /// </summary>
    /// <param name="outSortBuffer">The scratch list receiving the flattened window order.</param>
    /// <param name="window">The window to append, followed recursively by its active children.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>AddWindowToSortBuffer</c>
    ///. Canonical uses <c>ImQsort</c>; this port uses the
    /// unstable <see cref="List{T}.Sort(Comparison{T})"/>, which matches <c>ImQsort</c>'s lack of a
    /// stability guarantee.
    /// </remarks>
    internal static void AddWindowToSortBuffer(List<ImGuiWindow> outSortBuffer, ImGuiWindow window)
    {
        outSortBuffer.Add(window);
        if (!window.Active)
            return;

        List<ImGuiWindow> children = window.DC.ChildWindows;
        children.Sort(ChildWindowComparison);
        for (int i = 0; i < children.Count; i++)
        {
            ImGuiWindow child = children[i];
            if (child.Active)
                AddWindowToSortBuffer(outSortBuffer, child);
        }
    }

    /// <summary>
    /// Adds one window and its active children to the target viewport's draw data for a display layer.
    /// </summary>
    /// <param name="window">The root or child window to add.</param>
    /// <param name="layer">The display layer slot, where 0 is the regular layer and 1 the tooltip layer.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>AddWindowToDrawData</c>
    ///. Child visibility is tested with the canonical
    /// file-static <c>IsWindowActiveAndVisible</c> predicate, inlined here as
    /// <c>Active &amp;&amp; !Hidden</c>. so the port keeps a
    /// single definition instead of redefining a helper owned by the frame pipeline.
    /// </remarks>
    internal static void AddWindowToDrawData(ImGuiWindow window, int layer)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiViewportP? viewport = window.Viewport;
        ImAssert.Check(viewport != null);
        g.IO.MetricsRenderWindows++;

        // Merge user-split channels back so a forgotten ChannelsMerge does not leak a partial command list.
        if (window.DrawList.Splitter.ChannelCount > 1)
            window.DrawList.ChannelsMerge();

        ImGuiViewportP viewportP = viewport!;
        AddDrawListToDrawDataEx(viewportP.DrawDataP, viewportP.DrawDataBuilder.Layers[layer]!, window.DrawList);

        List<ImGuiWindow> children = window.DC.ChildWindows;
        for (int i = 0; i < children.Count; i++)
        {
            ImGuiWindow child = children[i];
            if (child.Active && !child.Hidden)
                AddWindowToDrawData(child, layer);
        }
    }

    /// <summary>
    /// Returns the display layer slot a window is submitted to.
    /// </summary>
    /// <param name="window">The window to classify.</param>
    /// <returns>1 for a tooltip window, otherwise 0.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>GetWindowDisplayLayer</c>
    /// </remarks>
    internal static int GetWindowDisplayLayer(ImGuiWindow window)
    {
        return (window.Flags & ImGuiWindowFlags.Tooltip) != 0 ? 1 : 0;
    }

    /// <summary>
    /// Adds a root window to the draw data of its viewport using the window's own display layer.
    /// </summary>
    /// <param name="window">The root window to add.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>AddRootWindowToDrawData</c>
    ///. The layer is locked for the root window so that
    /// children extruding into another viewport still follow their root.
    /// </remarks>
    internal static void AddRootWindowToDrawData(ImGuiWindow window)
    {
        AddWindowToDrawData(window, GetWindowDisplayLayer(window));
    }

    /// <summary>
    /// Appends every layer after layer 0 onto layer 0 and empties the later layers.
    /// </summary>
    /// <param name="builder">The per-viewport draw-data builder whose layers are flattened.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>FlattenDrawDataIntoSingleLayer</c>
    ///. Layer 1 (tooltips) is appended after layer 0, so
    /// tooltips render on top. Canonical resizes layer 0 and copies pointers; this port uses
    /// <see cref="List{T}.EnsureCapacity"/> and <see cref="List{T}.AddRange"/> for the same effect.
    /// </remarks>
    internal static void FlattenDrawDataIntoSingleLayer(ImDrawDataBuilder builder)
    {
        List<ImDrawList> layer0 = builder.Layers[0]!;
        int fullSize = layer0.Count;
        for (int i = 1; i < builder.Layers.Length; i++)
            fullSize += builder.Layers[i]?.Count ?? 0;
        layer0.EnsureCapacity(fullSize);

        for (int layerN = 1; layerN < builder.Layers.Length; layerN++)
        {
            List<ImDrawList>? layer = builder.Layers[layerN];
            if (layer == null || layer.Count == 0)
                continue;
            layer0.AddRange(layer);
            layer.Clear();
        }
    }

    /// <summary>
    /// Initializes the draw data of one viewport for the current frame and binds the builder layer slots.
    /// </summary>
    /// <param name="viewport">The viewport whose draw data and builder are reset.</param>
    /// <remarks>
    /// <para>
    /// Ports the file-static canonical <c>InitViewportDrawData</c>
    ///. The canonical parameter is an
    /// <c>ImGuiViewportP*</c>; this port accepts the public <see cref="ImGuiViewport"/> shape required by
    /// the part contract and asserts the runtime type, because only the internal
    /// <see cref="ImGuiViewportP"/> carries the draw-data and builder members.
    /// </para>
    /// <para>
    /// Two canonical writes are intentionally not reproduced: <c>draw_data-&gt;OwnerViewport</c> has no
    /// counterpart on this port's <see cref="ImDrawData"/>, and <c>draw_data-&gt;Textures</c> would alias
    /// <c>g.PlatformIO.Textures</c>, which this port does not assemble. Texture upload is therefore left to
    /// the backend, matching the documented null contract of <see cref="ImDrawData.Textures"/>.
    /// </para>
    /// </remarks>
    internal static void InitViewportDrawData(ImGuiViewport viewport)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(viewport is ImGuiViewportP);
        ImGuiViewportP viewportP = (ImGuiViewportP)viewport;
        ImDrawData drawData = viewportP.DrawDataP;

        viewport.DrawData = drawData; // Make publicly accessible
        viewportP.DrawDataBuilder.Layers[0] = drawData.CmdLists;
        viewportP.DrawDataBuilder.Layers[1] = viewportP.DrawDataBuilder.LayerData1;
        drawData.CmdLists.Clear();
        viewportP.DrawDataBuilder.LayerData1.Clear();

        // When minimized, report a zero display size so backends can skip rendering consistently with
        // the non-viewport path.
        bool isMinimized = (viewport.Flags & ImGuiViewportFlags.IsMinimized) != 0;

        drawData.Valid = true;
        drawData.FrameCount = g.FrameCount;
        drawData.TotalVtxCount = 0;
        drawData.TotalIdxCount = 0;
        drawData.DisplayPos = viewport.Pos;
        drawData.DisplaySize = isMinimized ? ImVec2.Zero : viewport.Size;
        drawData.FramebufferScale = viewport.FramebufferScale.X != 0.0f ? viewport.FramebufferScale : g.IO.DisplayFramebufferScale;
        drawData.Textures = null;
    }

    #endregion

    #region Draw-Data List Assembly

    /// <summary>
    /// Appends one command list to a draw-data aggregate and accumulates its vertex and index totals,
    /// skipping lists that would contribute no draw call.
    /// </summary>
    /// <param name="drawData">The aggregate whose totals are accumulated.</param>
    /// <param name="outList">The command-list target; generally <see cref="ImDrawData.CmdLists"/>, but a
    /// temporary layer list is also accepted and later merged.</param>
    /// <param name="drawList">The command list to append.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::AddDrawListToDrawDataEx</c>
    ///. A list with an empty command buffer, or with a
    /// single command that has no geometry and no user callback, is skipped entirely.
    /// </para>
    /// <para>
    /// The canonical write-pointer sanity asserts compare <c>_VtxWritePtr</c>/<c>_IdxWritePtr</c> with the
    /// ends of the vertex and index buffers. This port's <see cref="ImDrawList"/> keeps those cursors
    /// private and never exposes a partially-reserved buffer, so only the canonical 16-bit index limit is
    /// asserted here; the callback-data pointer remap is likewise unnecessary because this port pins
    /// callback data through a pinned <c>GCHandle</c> rather than a copied byte buffer.
    /// </para>
    /// </remarks>
    internal static void AddDrawListToDrawDataEx(ImDrawData drawData, List<ImDrawList> outList, ImDrawList drawList)
    {
        if (drawList.CmdBuffer.Count == 0)
            return;
        if (drawList.CmdBuffer.Count == 1)
        {
            ImDrawCmd onlyCommand = drawList.CmdBuffer[0];
            if (onlyCommand.ElemCount == 0 && onlyCommand.UserCallback == null)
                return;
        }

        // This port always uses 16-bit indices, so a single list must stay below 64K vertices.
        ImAssert.Check(drawList.VtxBuffer.Count < (1 << 16), "Too many vertices in ImDrawList using 16-bit indices.");

        outList.Add(drawList);
        drawData.TotalVtxCount += drawList.VtxBuffer.Count;
        drawData.TotalIdxCount += drawList.IdxBuffer.Count;
    }

    #endregion

    #region Background & Foreground Draw Lists

    /// <summary>
    /// Returns the lazily created background or foreground draw list of a viewport, resetting it once per
    /// frame in which it is first requested.
    /// </summary>
    /// <param name="viewport">The owning viewport.</param>
    /// <param name="drawlistNo">Slot index: 0 for background, 1 for foreground.</param>
    /// <param name="drawlistName">Canonical owner name for debugging.</param>
    /// <returns>The viewport's background or foreground draw list.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>GetViewportBgFgDrawList</c>
    ///. The list is constructed against the context font
    /// atlas because this port's <see cref="ImDrawList"/> is atlas-bound rather than shared-data-bound.
    /// The canonical <c>_OwnerName</c> write has no counterpart on this port's draw list and is therefore
    /// retained only as the parameter used for API parity.
    /// </remarks>
    private static ImDrawList GetViewportBgFgDrawList(ImGuiViewportP viewport, int drawlistNo, string drawlistName)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(drawlistNo < viewport.BgFgDrawLists.Length);
        ImDrawList? drawList = viewport.BgFgDrawLists[drawlistNo];
        if (drawList == null)
        {
            drawList = new ImDrawList(g.IO.Fonts) { OwnerName = drawlistName };
            viewport.BgFgDrawLists[drawlistNo] = drawList;
        }

        // The draw-list system requires that there is always one command; reset on first use this frame.
        if (viewport.BgFgDrawListsLastTimeActive[drawlistNo] != (float)g.Time)
        {
            drawList.Clear(g.DrawListSharedData.InitialDrawFlags);
            drawList.PushTexture(new ImTextureRef(g.IO.Fonts.TexID));
            drawList.PushClipRect(viewport.Pos, viewport.Pos + viewport.Size, false);
            viewport.BgFgDrawListsLastTimeActive[drawlistNo] = (float)g.Time;
        }
        return drawList;
    }

    /// <summary>
    /// Returns the background draw list of the current window's viewport.
    /// </summary>
    /// <returns>The current viewport's background draw list.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetBackgroundDrawList()</c>.
    /// </remarks>
    public static ImDrawList GetBackgroundDrawList()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.CurrentWindow != null);
        return GetBackgroundDrawList(g.CurrentWindow!.Viewport!);
    }

    /// <summary>
    /// Returns the background draw list of the supplied viewport, creating it on demand.
    /// </summary>
    /// <param name="viewport">The viewport whose background draw list is requested.</param>
    /// <returns>The viewport's background draw list.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetBackgroundDrawList(ImGuiViewport*)</c>
    /// </remarks>
    public static ImDrawList GetBackgroundDrawList(ImGuiViewport viewport)
    {
        ImAssert.Check(viewport is ImGuiViewportP);
        return GetViewportBgFgDrawList((ImGuiViewportP)viewport, 0, "##Background");
    }

    /// <summary>
    /// Returns the foreground draw list of the current window's viewport.
    /// </summary>
    /// <returns>The current viewport's foreground draw list.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetForegroundDrawList()</c>.
    /// </remarks>
    public static ImDrawList GetForegroundDrawList()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.CurrentWindow != null);
        return GetForegroundDrawList(g.CurrentWindow!.Viewport!);
    }

    /// <summary>
    /// Returns the foreground draw list of the supplied viewport, creating it on demand.
    /// </summary>
    /// <param name="viewport">The viewport whose foreground draw list is requested.</param>
    /// <returns>The viewport's foreground draw list.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetForegroundDrawList(ImGuiViewport*)</c>
    /// </remarks>
    public static ImDrawList GetForegroundDrawList(ImGuiViewport viewport)
    {
        ImAssert.Check(viewport is ImGuiViewportP);
        return GetViewportBgFgDrawList((ImGuiViewportP)viewport, 1, "##Foreground");
    }

    /// <summary>
    /// Returns the foreground draw list of the viewport that owns the supplied window.
    /// </summary>
    /// <param name="window">The window whose viewport foreground draw list is requested.</param>
    /// <returns>The owning viewport's foreground draw list.</returns>
    /// <remarks>
    /// Canonical declares this convenience overload inline in the internal header; it exists so debug and
    /// overlay code can name the window it is annotating without repeating the viewport hop.
    /// </remarks>
    internal static ImDrawList GetForegroundDrawList(ImGuiWindow window)
        => GetForegroundDrawList(window.Viewport!);

    /// <summary>
    /// Renders a drag-and-drop target rectangle for a specific viewport.
    /// </summary>
    /// <param name="viewportId">Target viewport ID.</param>
    /// <param name="bb">Bounding box to render.</param>
    /// <remarks>Ports canonical <c>ImGui::RenderDragDropTargetRectForViewport</c>.</remarks>
    public static void RenderDragDropTargetRectForViewport(uint viewportId, in ImRect bb)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiViewport? viewport = FindViewportByID(viewportId);
        ImAssert.Check(viewport != null);
        ImRect bbPadded = bb;
        bbPadded.Expand(-g.Style.DragDropTargetPadding);
        RenderDragDropTargetRectEx(GetForegroundDrawList(viewport!), bbPadded, g.Style.DragDropTargetRounding);
    }

    /// <summary>
    /// Returns the context draw-list shared data published to low-level callers.
    /// </summary>
    /// <returns>The current context's <see cref="ImDrawListSharedData"/>.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::GetDrawListSharedData()</c>
    ///. In this port the shared data carries the atlas UV
    /// tables and font metrics that the frame pipeline refreshes; the draw lists themselves are bound to
    /// the font atlas directly.
    /// </remarks>
    public static ImDrawListSharedData GetDrawListSharedData()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.DrawListSharedData;
    }

    #endregion

    #region Navigation State Entry Points

    /// <summary>
    /// Sets whether the navigation cursor is drawn, honoring the focused window's input configuration and
    /// the global always-visible preference.
    /// </summary>
    /// <param name="visible">The requested cursor visibility.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::SetNavCursorVisible</c>
    ///.: a window with
    /// <see cref="ImGuiWindowFlags.NoNavInputs"/> forces the cursor off, otherwise
    /// <see cref="ImGuiIO.ConfigNavCursorVisibleAlways"/> forces it on.
    /// </remarks>
    public static void SetNavCursorVisible(bool visible)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) != 0)
            visible = false;
        else if (g.IO.ConfigNavCursorVisibleAlways)
            visible = true;
        g.NavCursorVisible = visible;
    }

    /// <summary>
    /// Restores navigation cursor visibility after a successful move and marks the navigation highlight and
    /// mouse position dirty.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::SetNavCursorVisibleAfterMove</c>
    ///., formerly
    /// <c>NavRestoreHighlightAfterMove</c>. The cursor is hidden when the active input source is disabled by
    /// configuration, and shown again when <see cref="ImGuiIO.ConfigNavCursorVisibleAuto"/> is set.
    /// </remarks>
    internal static void SetNavCursorVisibleAfterMove()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) != 0)
            g.NavCursorVisible = false;
        else if (g.NavInputSource == ImGuiInputSource.Keyboard && (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) == 0)
            g.NavCursorVisible = false;
        else if (g.NavInputSource == ImGuiInputSource.Gamepad && (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) == 0)
            g.NavCursorVisible = false;
        else if (g.IO.ConfigNavCursorVisibleAuto)
            g.NavCursorVisible = true;
        g.NavHighlightItemUnderNav = g.NavMousePosDirty = true;
    }

    /// <summary>
    /// Rebinds the navigation window and cancels any pending init or move request.
    /// </summary>
    /// <param name="window">The window that navigation should operate on, or null to clear it.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::SetNavWindow</c>. The
    /// last valid selection user data is invalidated whenever the window actually changes.
    /// </remarks>
    internal static void SetNavWindow(ImGuiWindow? window)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavWindow != window)
        {
            g.NavWindow = window;
            g.NavLastValidSelectionUserData = -1;
        }
        g.NavInitRequest = false;
        g.NavMoveSubmitted = false;
        g.NavMoveScoringItems = false;
        NavUpdateAnyRequestFlag();
    }

    /// <summary>
    /// Starts the activation highlight timer for an item activated by navigation or shortcut.
    /// </summary>
    /// <param name="id">The activated item identifier.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavHighlightActivated</c>.
    /// </remarks>
    internal static void NavHighlightActivated(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavHighlightActivatedId = id;
        g.NavHighlightActivatedTimer = NavActivateHighlightTimer;
    }

    /// <summary>
    /// Clears the preferred scoring position for one axis of the current navigation root window.
    /// </summary>
    /// <param name="axis">The axis whose preferred scoring position is reset to an invalid value.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavClearPreferredPosForAxis</c>
    ///. The stored X or Y component is reset to
    /// <see cref="float.MaxValue"/>, the canonical invalid marker.
    /// </remarks>
    internal static void NavClearPreferredPosForAxis(ImGuiAxis axis)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow rootWindowForNav = g.NavWindow!.RootWindowForNav!;
        if (axis == ImGuiAxis.X)
            rootWindowForNav.NavPreferredScoringPosRel[(int)g.NavLayer].X = float.MaxValue;
        else
            rootWindowForNav.NavPreferredScoringPosRel[(int)g.NavLayer].Y = float.MaxValue;
    }

    /// <summary>
    /// Sets the current navigation identifier, layer and focus scope, and stores the identifier per layer.
    /// </summary>
    /// <param name="id">The navigation identifier to focus, or zero to clear focus.</param>
    /// <param name="navLayer">The navigation layer, main or menu.</param>
    /// <param name="focusScopeId">The focus scope that owns the identifier.</param>
    /// <param name="navRect">The item rectangle relative to the window, or null for the empty rectangle.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::SetNavID</c>. Canonical
    /// takes a required <c>const ImRect&amp; rect_rel</c>; the nullable optional parameter is the shape
    /// required by the part contract, and null selects the default rectangle that canonical callers pass
    /// explicitly.
    /// </para>
    /// <para>
    /// Both preferred scoring positions are cleared, and a later
    /// <see cref="NavMoveRequestApplyResult"/> tends to restore the axis that moved.
    /// </para>
    /// </remarks>
    internal static void SetNavID(uint id, ImGuiNavLayer navLayer, uint focusScopeId, ImRect? navRect = null)
    {
        SetNavIDWithRectRel(id, navLayer, focusScopeId, navRect ?? default);
    }

    /// <summary>
    /// Sets the current navigation identifier and stores its window-relative scoring rectangle.
    /// </summary>
    /// <param name="id">The navigation identifier to focus, or zero to clear focus.</param>
    /// <param name="navLayer">The navigation layer, main or menu.</param>
    /// <param name="focusScopeId">The focus scope that owns the identifier.</param>
    /// <param name="navRectRel">The item rectangle relative to the window origin.</param>
    /// <remarks>
    /// This member has no direct canonical counterpart in the reference revision; canonical exposes only
    /// <c>ImGui::SetNavID(ImGuiID, ImGuiNavLayer, ImGuiID, const ImRect&amp;)</c>
    ///. It carries the canonical body and exists only so the
    /// separate nullable overload demanded by the part contract can delegate here without duplicating the
    /// state transition. It must not be removed while that overload exists.
    /// </remarks>
    internal static void SetNavIDWithRectRel(uint id, ImGuiNavLayer navLayer, uint focusScopeId, ImRect navRectRel)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.NavWindow != null);
        ImAssert.Check(navLayer == ImGuiNavLayer.Main || navLayer == ImGuiNavLayer.Menu);
        g.NavId = id;
        g.NavLayer = navLayer;
        SetNavFocusScope(focusScopeId);
        g.NavWindow!.NavLastIds[(int)navLayer] = id;
        g.NavWindow.NavRectRel[(int)navLayer] = navRectRel;

        // A later successful move restores the preferred position for the axis that moved.
        NavClearPreferredPosForAxis(ImGuiAxis.X);
        NavClearPreferredPosForAxis(ImGuiAxis.Y);
    }

    /// <summary>
    /// Sets the current navigation focus scope and rebuilds the focus route that navigation traverses.
    /// </summary>
    /// <param name="focusScopeId">The focus scope identifier, or zero to clear the scope.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::SetNavFocusScope</c>. When
    /// the scope belongs to the current focus stack it is rebuilt from the top of that stack; when it is
    /// the navigation root's scope it is seeded from the navigation window; then manually chained
    /// <see cref="ImGuiWindow.ParentWindowForFocusRoute"/> windows are appended. The canonical maximum
    /// route depth assert is preserved.
    /// </remarks>
    internal static void SetNavFocusScope(uint focusScopeId)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavFocusScopeId = focusScopeId;
        g.NavFocusRoute.Clear(); // Invalidate
        if (focusScopeId == 0)
            return;
        ImAssert.Check(g.NavWindow != null);

        if (focusScopeId == g.CurrentFocusScopeId)
        {
            // Top of the focus stack contains local focus scopes inside the current window.
            for (int n = g.FocusScopeStack.Count - 1; n >= 0 && g.FocusScopeStack[n].WindowID == g.CurrentWindow!.ID; n--)
            {
                ImGuiFocusScopeData scope = g.FocusScopeStack[n];
                g.NavFocusRoute.Add(new ImGuiFocusScopeData { ID = scope.ID, WindowID = scope.WindowID });
            }
        }
        else if (focusScopeId == g.NavWindow!.NavRootFocusScopeId)
        {
            g.NavFocusRoute.Add(new ImGuiFocusScopeData { ID = focusScopeId, WindowID = g.NavWindow.ID });
        }
        else
        {
            return;
        }

        for (ImGuiWindow? window = g.NavWindow!.ParentWindowForFocusRoute; window != null; window = window.ParentWindowForFocusRoute)
            g.NavFocusRoute.Add(new ImGuiFocusScopeData { ID = window.NavRootFocusScopeId, WindowID = window.ID });
        ImAssert.Check(g.NavFocusRoute.Count < 100); // Maximum depth is technically 251 as per CalcRoutingScore().
    }

    /// <summary>
    /// Refreshes whether the current window may be scrolled horizontally by navigation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::NavUpdateCurrentWindowIsScrollPushableX</c>
    ///.: horizontal push-scrolling is enabled unless the
    /// current window sits inside a table or a legacy column set.
    /// </para>
    /// </remarks>
    internal static void NavUpdateCurrentWindowIsScrollPushableX()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        window.DC.NavIsScrollPushableX = window.DC.CurrentColumns == null && g.CurrentTable == null;
    }

    #endregion

    #region Navigation Item Scoring

    /// <summary>
    /// Maximum time an item stays highlighted after activation.
    /// </summary>
    private const float NavActivateHighlightTimer = 0.10f;

    /// <summary>
    /// Hold time that turns a gamepad activation press into a text-input activation
    /// </summary>
    private const float NavActivateInputWithGamepadDelay = 0.60f;

    /// <summary>
    /// Keys whose press switches the navigation input source to the gamepad
    /// </summary>
    private static readonly ImGuiKey[] NavGamepadKeysToChangeSource =
    {
        ImGuiKey.GamepadFaceRight, ImGuiKey.GamepadFaceLeft, ImGuiKey.GamepadFaceUp, ImGuiKey.GamepadFaceDown,
        ImGuiKey.GamepadDpadRight, ImGuiKey.GamepadDpadLeft, ImGuiKey.GamepadDpadUp, ImGuiKey.GamepadDpadDown,
    };

    /// <summary>
    /// Keys whose press switches the navigation input source to the keyboard
    /// </summary>
    private static readonly ImGuiKey[] NavKeyboardKeysToChangeSource =
    {
        ImGuiKey.Space, ImGuiKey.Enter, ImGuiKey.Escape, ImGuiKey.RightArrow,
        ImGuiKey.LeftArrow, ImGuiKey.UpArrow, ImGuiKey.DownArrow,
    };

    /// <summary>
    /// Resolves the gamepad activation key, honoring the swapped-buttons preference.
    /// </summary>
    /// <param name="g">The current context.</param>
    /// <returns>The key that activates the focused item with a gamepad.</returns>
    /// <remarks>
    /// Canonical <c>ImGuiKey_NavGamepadActivate</c> is a preprocessor alias
    ///. that this port cannot express as an enum member, so
    /// it is resolved at each use site.
    /// </remarks>
    private static ImGuiKey NavGamepadActivateKey(ImGuiContext g)
    {
        return g.IO.ConfigNavSwapGamepadButtons ? ImGuiKey.GamepadFaceRight : ImGuiKey.GamepadFaceDown;
    }

    /// <summary>
    /// Resolves the gamepad cancel key, honoring the swapped-buttons preference.
    /// </summary>
    /// <param name="g">The current context.</param>
    /// <returns>The key that cancels navigation with a gamepad.</returns>
    /// <remarks>
    /// Canonical <c>ImGuiKey_NavGamepadCancel</c> is a preprocessor alias
    /// </remarks>
    private static ImGuiKey NavGamepadCancelKey(ImGuiContext g)
    {
        return g.IO.ConfigNavSwapGamepadButtons ? ImGuiKey.GamepadFaceDown : ImGuiKey.GamepadFaceRight;
    }

    /// <summary>
    /// Maps a signed delta to the dominant axis and direction it points along.
    /// </summary>
    /// <param name="dx">The horizontal delta.</param>
    /// <param name="dy">The vertical delta.</param>
    /// <returns>The direction of the dominant component.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>ImGetDirQuadrantFromDelta</c>
    /// </remarks>
    private static ImGuiDir ImGetDirQuadrantFromDelta(float dx, float dy)
    {
        if (MathF.Abs(dx) > MathF.Abs(dy))
            return dx > 0.0f ? ImGuiDir.Right : ImGuiDir.Left;
        return dy > 0.0f ? ImGuiDir.Down : ImGuiDir.Up;
    }

    /// <summary>
    /// Returns the signed gap between two one-dimensional intervals, or zero when they overlap.
    /// </summary>
    /// <param name="candMin">Candidate interval minimum.</param>
    /// <param name="candMax">Candidate interval maximum.</param>
    /// <param name="currMin">Current interval minimum.</param>
    /// <param name="currMax">Current interval maximum.</param>
    /// <returns>The signed distance separating the intervals.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>NavScoreItemDistInterval</c>
    /// </remarks>
    private static float NavScoreItemDistInterval(float candMin, float candMax, float currMin, float currMax)
    {
        if (candMax < currMin)
            return candMax - currMin;
        if (currMax < candMin)
            return candMin - currMax;
        return 0.0f;
    }

    /// <summary>
    /// Linear interpolation helper used only by the navigation scoring math.
    /// </summary>
    /// <param name="a">Value returned when the factor is zero.</param>
    /// <param name="b">Value returned when the factor is one.</param>
    /// <param name="t">Interpolation factor.</param>
    /// <returns>The interpolated value.</returns>
    /// <remarks>Mirrors canonical <c>ImLerp</c>.</remarks>
    private static float NavLerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Scores a candidate item against the current navigation scoring rectangle and updates the result when
    /// the candidate becomes the best match for the requested direction.
    /// </summary>
    /// <param name="result">The accumulating best candidate for the current window.</param>
    /// <param name="navBb">The candidate item's navigation bounding box in absolute screen space.</param>
    /// <returns>True when the candidate became the new best result.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>NavScoreItem</c>
    ///. without changing its scoring math. The distance is a
    /// weighted box distance with a vertical bias, ties are broken by center distance and then by an
    /// infinitesimal symbolic shift, and an axial fallback link is added for menu-layer items when no real
    /// match exists. The canonical <c>IMGUI_DEBUG_NAV_SCORING</c> rendering block is compiled out in the
    /// reference build and is therefore omitted.
    /// </remarks>
    private static bool NavScoreItem(ImGuiNavItemData result, ImRect navBb)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (g.NavLayer != window.DC.NavLayerCurrent)
            return false;

        ImRect cand = navBb;                 // Current item navigation rectangle
        ImRect curr = g.NavScoringRect;      // Current modified source rectangle
        g.NavScoringDebugCount++;

        // When entering through a NavFlattened border, child items are considered fully clipped for scoring.
        if (window.ParentWindow == g.NavWindow)
        {
            ImAssert.Check(((window.ChildFlags | g.NavWindow!.ChildFlags) & ImGuiChildFlags.NavFlattened) != 0);
            if (!window.ClipRect.Overlaps(cand))
                return false;
            cand.ClipWithFull(window.ClipRect);
        }

        // Compute the distance between the boxes. The Y axis is compressed toward the center to keep using
        // box distance for vertically touching items.
        float dbx = NavScoreItemDistInterval(cand.Min.X, cand.Max.X, curr.Min.X, curr.Max.X);
        float dby = NavScoreItemDistInterval(
            NavLerp(cand.Min.Y, cand.Max.Y, 0.2f),
            NavLerp(cand.Min.Y, cand.Max.Y, 0.8f),
            NavLerp(curr.Min.Y, curr.Max.Y, 0.2f),
            NavLerp(curr.Min.Y, curr.Max.Y, 0.8f));
        if (dby != 0.0f && dbx != 0.0f)
            dbx = (dbx / 1000.0f) + (dbx > 0.0f ? 1.0f : -1.0f);
        float distBox = MathF.Abs(dbx) + MathF.Abs(dby);

        // Distance between centers; off by a factor of two, which is irrelevant because only the ordering
        // of candidates is compared.
        float dcx = (cand.Min.X + cand.Max.X) - (curr.Min.X + curr.Max.X);
        float dcy = (cand.Min.Y + cand.Max.Y) - (curr.Min.Y + curr.Max.Y);
        float distCenter = MathF.Abs(dcx) + MathF.Abs(dcy);

        ImGuiDir quadrant;
        float dax = 0.0f, day = 0.0f, distAxial = 0.0f;
        if (dbx != 0.0f || dby != 0.0f)
        {
            dax = dbx;
            day = dby;
            distAxial = distBox;
            quadrant = ImGetDirQuadrantFromDelta(dbx, dby);
        }
        else if (dcx != 0.0f || dcy != 0.0f)
        {
            dax = dcx;
            day = dcy;
            distAxial = distCenter;
            quadrant = ImGetDirQuadrantFromDelta(dcx, dcy);
        }
        else
        {
            // Degenerate case: overlapping items with the same center break the tie arbitrarily.
            quadrant = g.LastItemData.ID < g.NavId ? ImGuiDir.Left : ImGuiDir.Right;
        }

        // Is the candidate in the quadrant being moved toward?
        bool newBest = false;
        if (quadrant == g.NavMoveDir)
        {
            if (distBox < result.DistBox)
            {
                result.DistBox = distBox;
                result.DistCenter = distCenter;
                return true;
            }
            if (distBox == result.DistBox)
            {
                if (distCenter < result.DistCenter)
                {
                    result.DistCenter = distCenter;
                    newBest = true;
                }
                else if (distCenter == result.DistCenter)
                {
                    // Still tied: move "later" items right/down by an infinitesimal amount so that
                    // dx==dy==0 items link in order of appearance along the axis.
                    if (((g.NavMoveDir == ImGuiDir.Up || g.NavMoveDir == ImGuiDir.Down) ? dby : dbx) < 0.0f)
                        newBest = true;
                }
            }
        }

        // Axial fallback: add a tentative link when the current rectangle has no real match in the moved
        // direction. It only augments the graph and is enabled for the menu layer outside child menus.
        if (result.DistBox == float.MaxValue && distAxial < result.DistAxial)
        {
            if (g.NavLayer == ImGuiNavLayer.Menu && (g.NavWindow!.Flags & ImGuiWindowFlags.ChildMenu) == 0)
            {
                if ((g.NavMoveDir == ImGuiDir.Left && dax < 0.0f)
                    || (g.NavMoveDir == ImGuiDir.Right && dax > 0.0f)
                    || (g.NavMoveDir == ImGuiDir.Up && day < 0.0f)
                    || (g.NavMoveDir == ImGuiDir.Down && day > 0.0f))
                {
                    result.DistAxial = distAxial;
                    newBest = true;
                }
            }
        }

        return newBest;
    }

    /// <summary>
    /// Copies the last submitted item into a navigation result and records its window-relative rectangle.
    /// </summary>
    /// <param name="result">The navigation result to populate.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>NavApplyItemToResult</c>
    ///. The selection user data is intentionally read from
    /// the still-valid next-item data rather than copied into the last-item data first.
    /// </remarks>
    internal static void NavApplyItemToResult(ImGuiNavItemData result)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        result.Window = window;
        result.ID = g.LastItemData.ID;
        result.FocusScopeId = g.CurrentFocusScopeId;
        result.ItemFlags = g.LastItemData.ItemFlags;
        result.RectRel = NavWindowRectAbsToRel(window, g.LastItemData.NavRect);
        if (((ImGuiItemFlagsPrivate)result.ItemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData) != 0)
        {
            ImAssert.Check(g.NextItemData.SelectionUserData != -1);
            result.SelectionUserData = g.NextItemData.SelectionUserData;
        }
    }

    /// <summary>
    /// Processes the last submitted item for pending navigation init and move requests.
    /// </summary>
    /// <remarks>
    /// Ports the file-static canonical <c>NavProcessItem</c>
    ///. It is called by <c>ItemAdd</c> before the clipping
    /// early-out so that a newly opened window can honor its init request and so that navigation can reach
    /// clipped items that require scrolling. Disabled items are skipped for both request kinds.
    /// </remarks>
    internal static void NavProcessItem()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        uint id = g.LastItemData.ID;
        ImGuiItemFlags itemFlags = g.LastItemData.ItemFlags;

        // When inside a container that cannot be scrolled with Left/Right, clip the navigation rectangle.
        ImRect navBb = g.LastItemData.NavRect;
        if (!window.DC.NavIsScrollPushableX)
        {
            navBb.Min.X = Math.Clamp(navBb.Min.X, window.ClipRect.Min.X, window.ClipRect.Max.X);
            navBb.Max.X = Math.Clamp(navBb.Max.X, window.ClipRect.Min.X, window.ClipRect.Max.X);
        }

        // Process the init request. Items flagged NoNavDefaultFocus still record a fallback first result.
        if (g.NavInitRequest && g.NavLayer == window.DC.NavLayerCurrent && (itemFlags & ImGuiItemFlags.Disabled) == 0)
        {
            bool candidateForNavDefaultFocus = (itemFlags & ImGuiItemFlags.NoNavDefaultFocus) == 0;
            if (candidateForNavDefaultFocus || g.NavInitResult.ID == 0)
                NavApplyItemToResult(g.NavInitResult);
            if (candidateForNavDefaultFocus)
            {
                g.NavInitRequest = false; // Found a match, clear the request
                NavUpdateAnyRequestFlag();
            }
        }

        // Process the move request (directional scoring).
        if (g.NavMoveScoringItems && (itemFlags & ImGuiItemFlags.Disabled) == 0)
        {
            if ((g.NavMoveFlags & ImGuiNavMoveFlags.FocusApi) != 0 || (window.Flags & ImGuiWindowFlags.NoNavInputs) == 0)
            {
                bool isTabbing = (g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0;
                if (isTabbing)
                {
                    NavProcessItemForTabbingRequest(id, itemFlags, g.NavMoveFlags);
                }
                else if (g.NavId != id || (g.NavMoveFlags & ImGuiNavMoveFlags.AllowCurrentNavId) != 0)
                {
                    ImGuiNavItemData result = window == g.NavWindow ? g.NavMoveResultLocal : g.NavMoveResultOther;
                    if (NavScoreItem(result, navBb))
                        NavApplyItemToResult(result);

                    // PageUp/PageDown maintain a separate score for the visible set of items.
                    const float visibleRatio = 0.70f;
                    if ((g.NavMoveFlags & ImGuiNavMoveFlags.AlsoScoreVisibleSet) != 0)
                    {
                        ImRect r = window.InnerRect;
                        if (r.Overlaps(navBb))
                        {
                            if (Math.Clamp(navBb.Max.Y, r.Min.Y, r.Max.Y) - Math.Clamp(navBb.Min.Y, r.Min.Y, r.Max.Y) >= (navBb.Max.Y - navBb.Min.Y) * visibleRatio)
                            {
                                if (NavScoreItem(g.NavMoveResultLocalVisible, navBb))
                                    NavApplyItemToResult(g.NavMoveResultLocalVisible);
                            }
                        }
                    }
                }
            }
        }

        // Update information for the currently focused item.
        if (g.NavId == id)
        {
            if (g.NavWindow != window)
                SetNavWindow(window); // Some operations such as FocusItem() may not have set a window.
            g.NavLayer = window.DC.NavLayerCurrent;
            SetNavFocusScope(g.CurrentFocusScopeId);
            g.NavFocusScopeId = g.CurrentFocusScopeId;
            g.NavIdIsAlive = true;
            g.NavIdItemFlags = itemFlags;
            if (((ImGuiItemFlagsPrivate)g.LastItemData.ItemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData) != 0)
            {
                ImAssert.Check(g.NextItemData.SelectionUserData != -1);
                g.NavLastValidSelectionUserData = g.NextItemData.SelectionUserData;
            }
            window.NavRectRel[(int)window.DC.NavLayerCurrent] = NavWindowRectAbsToRel(window, navBb);
        }
    }

    /// <summary>
    /// Scores one item for a tabbing or focus-API request.
    /// </summary>
    /// <param name="id">The candidate item identifier.</param>
    /// <param name="itemFlags">The candidate item flags.</param>
    /// <param name="moveFlags">The active navigation move flags.</param>
    /// <remarks>
    /// Ports canonical <c>NavProcessItemForTabbingRequest</c>
    ///. Focus-API requests may land on any item; tabbing
    /// without keyboard navigation is restricted to inputable items. The five canonical forward, backward
    /// and wrapping cases are reproduced verbatim.
    /// </remarks>
    internal static void NavProcessItemForTabbingRequest(uint id, ImGuiItemFlags itemFlags, ImGuiNavMoveFlags moveFlags)
    {
        ImGuiContext g = GetCurrentContext()!;

        if ((moveFlags & ImGuiNavMoveFlags.FocusApi) == 0)
        {
            if (g.NavLayer != g.CurrentWindow!.DC.NavLayerCurrent)
                return;
            if (g.NavFocusScopeId != g.CurrentFocusScopeId)
                return;
        }

        bool canStop;
        if ((moveFlags & ImGuiNavMoveFlags.FocusApi) != 0)
            canStop = true;
        else
            canStop = (itemFlags & ImGuiItemFlags.NoTabStop) == 0
                && ((g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0
                    || ((ImGuiItemFlagsPrivate)itemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_Inputable) != 0);

        // Tabbing always accumulates into the local result, unlike directional scoring on sibling windows.
        ImGuiNavItemData result = g.NavMoveResultLocal;
        if (g.NavTabbingDir == +1)
        {
            // Tab forward or SetKeyboardFocusHere() with a non-negative offset.
            if (canStop && g.NavTabbingResultFirst.ID == 0)
                NavApplyItemToResult(g.NavTabbingResultFirst);
            if (canStop && g.NavTabbingCounter > 0 && --g.NavTabbingCounter == 0)
                NavMoveRequestResolveWithLastItem(result);
            else if (g.NavId == id)
                g.NavTabbingCounter = 1;
        }
        else if (g.NavTabbingDir == -1)
        {
            // Tab backward.
            if (g.NavId == id)
            {
                if (result.ID != 0)
                {
                    g.NavMoveScoringItems = false;
                    NavUpdateAnyRequestFlag();
                }
            }
            else if (canStop)
            {
                NavApplyItemToResult(result); // Keep applying until reaching NavId
            }
        }
        else if (g.NavTabbingDir == 0)
        {
            if (canStop && g.NavId == id)
                NavMoveRequestResolveWithLastItem(result);
            if (canStop && g.NavTabbingResultFirst.ID == 0)
                NavApplyItemToResult(g.NavTabbingResultFirst);
        }
    }

    /// <summary>
    /// Recomputes <see cref="ImGuiContext.NavAnyRequest"/> from the pending init and move requests.
    /// </summary>
    /// <remarks>
    /// Ports the file-static canonical <c>NavUpdateAnyRequestFlag</c>
    ///. The canonical
    /// <c>IMGUI_DEBUG_NAV_SCORING</c> disjunct is compiled out in the reference build and omitted. A
    /// pending request may only exist while a navigation window is bound, which is asserted here.
    /// </remarks>
    internal static void NavUpdateAnyRequestFlag()
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavAnyRequest = g.NavMoveScoringItems || g.NavInitRequest;
        ImAssert.Check(!g.NavAnyRequest || g.NavWindow != null);
    }

    /// <summary>
    /// Converts an absolute rectangle into window-relative coordinates.
    /// </summary>
    /// <param name="window">The window providing the cursor start origin.</param>
    /// <param name="rect">The absolute rectangle.</param>
    /// <returns>The rectangle relative to the window's cursor start position.</returns>
    /// <remarks>
    /// Mirrors the inline canonical <c>WindowRectAbsToRel</c>
    ///. It is kept private to this part because the
    /// canonical helper is an unowned namespace-scope inline and other parts either inline the arithmetic
    /// or own their own copy.
    /// </remarks>
    private static ImRect NavWindowRectAbsToRel(ImGuiWindow window, ImRect rect)
    {
        ImVec2 offset = window.DC.CursorStartPos;
        return new ImRect(
            rect.Min.X - offset.X,
            rect.Min.Y - offset.Y,
            rect.Max.X - offset.X,
            rect.Max.Y - offset.Y);
    }

    /// <summary>
    /// Converts a window-relative rectangle into absolute screen coordinates.
    /// </summary>
    /// <param name="window">The window providing the cursor start origin.</param>
    /// <param name="rect">The window-relative rectangle.</param>
    /// <returns>The absolute rectangle.</returns>
    /// <remarks>
    /// Mirrors the inline canonical <c>WindowRectRelToAbs</c>
    ///. The clipper reuses it while adding the focused
    /// item's rectangle to the visible range.
    /// </remarks>
    internal static ImRect WindowRectRelToAbs(ImGuiWindow window, ImRect rect)
    {
        ImVec2 offset = window.DC.CursorStartPos;
        return new ImRect(
            rect.Min.X + offset.X,
            rect.Min.Y + offset.Y,
            rect.Max.X + offset.X,
            rect.Max.Y + offset.Y);
    }

    #endregion

    #region Navigation Move Request API

    /// <summary>
    /// Returns whether a submitted move request has not produced a result yet.
    /// </summary>
    /// <returns>True while scoring is still active and no local or sibling result exists.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestButNoResultYet</c>
    ///. The part contract lists a void return; canonical
    /// returns <c>bool</c> and is read as a predicate by <see cref="NavEndFrame"/>, so the boolean form is
    /// kept.
    /// </remarks>
    internal static bool NavMoveRequestButNoResultYet()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.NavMoveScoringItems && g.NavMoveResultLocal.ID == 0 && g.NavMoveResultOther.ID == 0;
    }

    /// <summary>
    /// Submits a directional navigation move request for scoring during the next item submission pass.
    /// </summary>
    /// <param name="moveDir">The requested movement direction.</param>
    /// <param name="clipDir">The clip-check direction; equals <paramref name="moveDir"/> for a fresh request.</param>
    /// <param name="moveFlags">Flags describing the request.</param>
    /// <param name="scrollFlags">Flags controlling the scroll performed by the result.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestSubmit</c>
    ///. Tabbing requests implicitly allow the current
    /// navigation identifier as a result.
    /// </remarks>
    internal static void NavMoveRequestSubmit(ImGuiDir moveDir, ImGuiDir clipDir, ImGuiNavMoveFlags moveFlags, ImGuiScrollFlags scrollFlags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.NavWindow != null);

        if ((moveFlags & ImGuiNavMoveFlags.IsTabbing) != 0)
            moveFlags |= ImGuiNavMoveFlags.AllowCurrentNavId;

        g.NavMoveSubmitted = true;
        g.NavMoveScoringItems = true;
        g.NavMoveDir = moveDir;
        g.NavMoveDirForDebug = moveDir;
        g.NavMoveClipDir = clipDir;
        g.NavMoveFlags = moveFlags;
        g.NavMoveScrollFlags = scrollFlags;
        g.NavMoveForwardToNextFrame = false;
        g.NavMoveKeyMods = (moveFlags & ImGuiNavMoveFlags.FocusApi) != 0 ? ImGuiKey.None : g.IO.KeyMods;
        g.NavMoveResultLocal.Clear();
        g.NavMoveResultLocalVisible.Clear();
        g.NavMoveResultOther.Clear();
        g.NavTabbingCounter = 0;
        g.NavTabbingResultFirst.Clear();
        NavUpdateAnyRequestFlag();
    }

    /// <summary>
    /// Resolves an active move request with the last submitted item, stopping further scoring.
    /// </summary>
    /// <param name="result">The result populated from the last submitted item.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestResolveWithLastItem</c>
    /// </remarks>
    internal static void NavMoveRequestResolveWithLastItem(ImGuiNavItemData result)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavMoveScoringItems = false; // Ensure the request needs no more processing
        NavApplyItemToResult(result);
        NavUpdateAnyRequestFlag();
    }

    /// <summary>
    /// Moves navigation cursor, sets scrolling, and sets focus window to the last submitted item.
    /// Ports canonical <c>ImGui::FocusItem</c>.
    /// </summary>
    public static void FocusItem()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (g.DragDropActive || g.MovingWindow != null)
            return;

        ImGuiNavMoveFlags moveFlags = ImGuiNavMoveFlags.IsTabbing | ImGuiNavMoveFlags.FocusApi | ImGuiNavMoveFlags.NoSetNavCursorVisible | ImGuiNavMoveFlags.NoSelect;
        ImGuiScrollFlags scrollFlags = window.Appearing ? ImGuiScrollFlags.KeepVisibleEdgeX | ImGuiScrollFlags.AlwaysCenterY : ImGuiScrollFlags.KeepVisibleEdgeX | ImGuiScrollFlags.KeepVisibleEdgeY;
        SetNavWindow(window);
        NavMoveRequestSubmit(ImGuiDir.None, ImGuiDir.Up, moveFlags, scrollFlags);
        NavMoveRequestResolveWithLastItem(g.NavMoveResultLocal);
    }

    /// <summary>
    /// Requests activation of an item by ID.
    /// Ports canonical <c>ImGui::ActivateItemByID</c>.
    /// </summary>
    public static void ActivateItemByID(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavNextActivateId = id;
        g.NavNextActivateFlags = ImGuiActivateFlags.None;
    }

    /// <summary>
    /// Requests that keyboard or gamepad focus move to an item near the current one. Mirrors canonical
    /// <c>ImGui::SetKeyboardFocusHere</c>.
    /// </summary>
    /// <param name="offset">
    /// The number of items to skip forward from the next item; <c>0</c> focuses the next item and
    /// <c>-1</c> focuses the last submitted item.
    /// </param>
    /// <remarks>
    /// <para>
    /// The request is ignored while a drag and drop or a window move is in progress, because the
    /// active identifier would otherwise be stolen from the drag. The move is submitted as a tabbing
    /// request that activates the focused item and does not make the navigation cursor visible.
    /// </para>
    /// <para>
    /// For <paramref name="offset"/> <c>-1</c> the request resolves immediately against the last
    /// submitted item. For non-negative offsets the tabbing direction is set forward and the counter
    /// is primed so the move lands on the requested row.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no current context is installed.</exception>
    public static void SetKeyboardFocusHere(int offset = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImAssert.Check(offset >= -1, "SetKeyboardFocusHere() accepts -1 but not a lower offset.");

        // Never interrupt a drag and drop: the active identifier would be stolen from the drag.
        if (g.DragDropActive || g.MovingWindow != null)
            return;

        SetNavWindow(window);

        ImGuiNavMoveFlags moveFlags = ImGuiNavMoveFlags.IsTabbing | ImGuiNavMoveFlags.Activate
            | ImGuiNavMoveFlags.FocusApi | ImGuiNavMoveFlags.NoSetNavCursorVisible;
        ImGuiScrollFlags scrollFlags = window.Appearing
            ? ImGuiScrollFlags.KeepVisibleEdgeX | ImGuiScrollFlags.AlwaysCenterY
            : ImGuiScrollFlags.KeepVisibleEdgeX | ImGuiScrollFlags.KeepVisibleEdgeY;
        NavMoveRequestSubmit(ImGuiDir.None, offset < 0 ? ImGuiDir.Up : ImGuiDir.Down, moveFlags, scrollFlags);
        if (offset == -1)
        {
            NavMoveRequestResolveWithLastItem(g.NavMoveResultLocal);
        }
        else
        {
            g.NavTabbingDir = 1;
            g.NavTabbingCounter = offset + 1;
        }
    }

    /// <summary>
    /// Resolves an active move request with a saved tree-node item and clears the vertical preferred position.
    /// </summary>
    /// <param name="result">The result populated from the saved tree-node data.</param>
    /// <param name="treeNodeStackData">The saved tree-node stack entry, used to implement
    /// <c>ImGuiTreeNodeFlags_NavLeftJumpsToParent</c>.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestResolveWithPastTreeNode</c>
    ///. The selection user data is dropped because it is
    /// recovered from the next-item data on the following frame.
    /// </remarks>
    internal static void NavMoveRequestResolveWithPastTreeNode(ImGuiNavItemData result, ImGuiTreeNodeStackData treeNodeStackData)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavMoveScoringItems = false;
        g.LastItemData.ID = treeNodeStackData.ID;
        g.LastItemData.ItemFlags = treeNodeStackData.ItemFlags & (ImGuiItemFlags)~(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData;
        g.LastItemData.NavRect = treeNodeStackData.NavRect;
        NavApplyItemToResult(result);
        NavClearPreferredPosForAxis(ImGuiAxis.Y);
        NavUpdateAnyRequestFlag();
    }

    /// <summary>
    /// Cancels any pending navigation move request.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestCancel</c>
    /// </remarks>
    internal static void NavMoveRequestCancel()
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavMoveSubmitted = false;
        g.NavMoveScoringItems = false;
        NavUpdateAnyRequestFlag();
    }

    /// <summary>
    /// Re-arms the previous move request for the next frame with a modified direction and flags.
    /// </summary>
    /// <param name="moveDir">The forwarded movement direction.</param>
    /// <param name="clipDir">The forwarded clip-check direction.</param>
    /// <param name="moveFlags">The forwarded request flags.</param>
    /// <param name="scrollFlags">The forwarded scroll flags.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestForward</c>
    ///. The forwarded flag is added so that the request is
    /// recognized as a continuation during <see cref="NavUpdateCreateMoveRequest"/>.
    /// </remarks>
    internal static void NavMoveRequestForward(ImGuiDir moveDir, ImGuiDir clipDir, ImGuiNavMoveFlags moveFlags, ImGuiScrollFlags scrollFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.NavMoveForwardToNextFrame == false);
        NavMoveRequestCancel();
        g.NavMoveForwardToNextFrame = true;
        g.NavMoveDir = moveDir;
        g.NavMoveClipDir = clipDir;
        g.NavMoveFlags = moveFlags | ImGuiNavMoveFlags.Forwarded;
        g.NavMoveScrollFlags = scrollFlags;
    }

    /// <summary>
    /// Replaces the wrap-around bits of an active move request, deferring the actual wrap to the end of the
    /// frame.
    /// </summary>
    /// <param name="window">The window the request must still be operating on.</param>
    /// <param name="wrapFlags">The wrap/loop bits to install.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestTryWrapping</c>
    ///. Wrapping is only valid once the popup hierarchy is
    /// fully assembled, so it is applied by <see cref="NavEndFrame"/>. The part contract omits this
    /// signature; it is defined here because the move-request family owns it.
    /// </remarks>
    internal static void NavMoveRequestTryWrapping(ImGuiWindow window, ImGuiNavMoveFlags wrapFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check((wrapFlags & ImGuiNavMoveFlags.WrapMask_) != 0 && (wrapFlags & ~ImGuiNavMoveFlags.WrapMask_) == 0);

        // NavEndFrame() performs the same no-result test and creates the wrapping request.
        if (g.NavWindow == window && g.NavMoveScoringItems && g.NavLayer == window.DC.NavLayerCurrent)
            g.NavMoveFlags = (g.NavMoveFlags & ~ImGuiNavMoveFlags.WrapMask_) | wrapFlags;
    }

    /// <summary>
    /// Records the currently focused child window on its nearest popup or child-menu ancestor.
    /// </summary>
    /// <param name="navWindow">The navigation window to record.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>NavSaveLastChildNavWindowIntoParent</c>
    ///., which allows navigation to return to the last
    /// focused child when re-entering a window.
    /// </remarks>
    private static void NavSaveLastChildNavWindowIntoParent(ImGuiWindow navWindow)
    {
        ImGuiWindow? parent = navWindow;
        while (parent != null
            && parent.RootWindow != parent
            && (parent.Flags & (ImGuiWindowFlags.Popup | ImGuiWindowFlags.ChildMenu)) == 0)
        {
            parent = parent.ParentWindow;
        }
        if (parent != null && parent != navWindow)
            parent.NavLastChildNavWindow = navWindow;
    }

    /// <summary>
    /// Returns the last focused child window of a window when it is still active.
    /// </summary>
    /// <param name="window">The window whose last focused child is restored.</param>
    /// <returns>The child window to focus, or <paramref name="window"/> itself.</returns>
    /// <remarks>
    /// Ports canonical <c>NavRestoreLastChildNavWindow</c>
    ///. When no remembered child window is active, a dock
    /// node host window falls back to its most recently selected tab whose window is still active.
    /// </remarks>
    private static ImGuiWindow NavRestoreLastChildNavWindow(ImGuiWindow window)
    {
        if (window.NavLastChildNavWindow != null && window.NavLastChildNavWindow.WasActive)
            return window.NavLastChildNavWindow;

        if (window.DockNodeAsHost != null && window.DockNodeAsHost.TabBar != null)
        {
            ImGuiTabItem? tab = TabBarFindMostRecentlySelectedTabForActiveWindow(window.DockNodeAsHost.TabBar);
            if (tab != null && tab.Window != null)
                return tab.Window;
        }
        return window;
    }

    /// <summary>
    /// Restores navigation to a previously visited layer of the navigation window.
    /// </summary>
    /// <param name="layer">The layer to restore, main or menu.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavRestoreLayer</c>
    ///. Restoring the main layer first revisits the last
    /// focused child window; a layer with no remembered identifier starts a fresh init request.
    /// </remarks>
    internal static void NavRestoreLayer(ImGuiNavLayer layer)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (layer == ImGuiNavLayer.Main)
        {
            g.NavWindow = NavRestoreLastChildNavWindow(g.NavWindow!);
            g.NavLastValidSelectionUserData = -1;
        }

        ImGuiWindow window = g.NavWindow!;
        if (window.NavLastIds[(int)layer] != 0)
        {
            SetNavID(window.NavLastIds[(int)layer], layer, 0, window.NavRectRel[(int)layer]);
        }
        else
        {
            g.NavLayer = layer;
            NavInitWindow(window, true);
        }
    }

    /// <summary>
    /// Initializes or restores the navigation state of a window.
    /// </summary>
    /// <param name="window">The window being entered; must already be the navigation window.</param>
    /// <param name="forceReinit">When true, starts a fresh init request even if an identifier is remembered.</param>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavInitWindow</c>. Called
    /// before any widget is submitted, typically from <c>Begin</c>.
    /// </remarks>
    internal static void NavInitWindow(ImGuiWindow window, bool forceReinit)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(window == g.NavWindow);

        if ((window.Flags & ImGuiWindowFlags.NoNavInputs) != 0)
        {
            g.NavId = 0;
            SetNavFocusScope(window.NavRootFocusScopeId);
            return;
        }

        bool initForNav = window == window.RootWindow
            || (window.Flags & ImGuiWindowFlags.Popup) != 0
            || window.NavLastIds[0] == 0
            || forceReinit;
        if (initForNav)
        {
            SetNavID(0, g.NavLayer, window.NavRootFocusScopeId, new ImRect());
            g.NavInitRequest = true;
            g.NavInitRequestFromMove = false;
            g.NavInitResult.ID = 0;
            NavUpdateAnyRequestFlag();
        }
        else
        {
            g.NavId = window.NavLastIds[0];
            SetNavFocusScope(window.NavRootFocusScopeId);
        }
    }

    /// <summary>
    /// Selects the reference position source used by popups and tooltips.
    /// </summary>
    /// <param name="windowType">The canonical popup/tooltip window-type flags of the requesting window.</param>
    /// <returns>The input source that should provide the reference position.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>NavCalcPreferredRefPosSource</c>
    ///. A remote activation through a shortcut positions a
    /// popup from the item even when the navigation cursor is hidden.
    /// </remarks>
    private static ImGuiInputSource NavCalcPreferredRefPosSource(ImGuiWindowFlags windowType)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow? window = g.NavWindow;

        bool activatedShortcut = g.ActiveId != 0 && g.ActiveIdFromShortcut && g.ActiveId == g.LastItemData.ID;
        if ((windowType & ImGuiWindowFlags.Popup) != 0 && activatedShortcut)
            return ImGuiInputSource.Keyboard;

        if (!g.NavCursorVisible || !g.NavHighlightItemUnderNav || window == null)
            return ImGuiInputSource.Mouse;
        return ImGuiInputSource.Keyboard;
    }

    /// <summary>
    /// Computes the reference position used to open popups and tooltips.
    /// </summary>
    /// <param name="windowType">The canonical popup/tooltip window-type flags of the requesting window.</param>
    /// <returns>The reference position in absolute screen coordinates.</returns>
    /// <remarks>
    /// <para>
    /// Ports the file-static canonical <c>NavCalcPreferredRefPos</c>
    ///. With the mouse as the source the position is the
    /// last valid mouse position plus a one-pixel X offset; otherwise it is derived from the bottom-left of
    /// the currently navigated item, with any pending scroll target taken into account.
    /// </para>
    /// <para>
    /// The canonical <c>IsMousePosValid</c> helper. is
    /// inlined because its C# signature is owned by the input pipeline.
    /// </para>
    /// </remarks>
    internal static ImVec2 NavCalcPreferredRefPos(ImGuiWindowFlags windowType)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow? window = g.NavWindow;
        ImGuiInputSource source = NavCalcPreferredRefPosSource(windowType);

        if (source == ImGuiInputSource.Mouse)
        {
            const float mouseInvalid = -256000.0f;
            bool mousePosValid = g.IO.MousePos.X >= mouseInvalid && g.IO.MousePos.Y >= mouseInvalid;
            ImVec2 p = mousePosValid ? g.IO.MousePos : g.MouseLastValidPos;
            return new ImVec2(p.X + 1.0f, p.Y);
        }

        bool activatedShortcut = g.ActiveId != 0 && g.ActiveIdFromShortcut && g.ActiveId == g.LastItemData.ID;
        ImRect refRect = default;
        if (activatedShortcut && (windowType & ImGuiWindowFlags.Popup) != 0)
            refRect = g.LastItemData.NavRect;
        else if (window != null)
            refRect = WindowRectRelToAbs(window, window.NavRectRel[(int)g.NavLayer]);

        // Account for an upcoming scroll, because the final mouse position is applied before scrolling.
        if (window != null && window.LastFrameActive != g.FrameCount
            && (window.ScrollTarget.X != float.MaxValue || window.ScrollTarget.Y != float.MaxValue))
        {
            ImVec2 nextScroll = CalcNextScrollFromScrollTargetAndClamp(window);
            refRect.Translate(nextScroll - window.Scroll);
        }

        ImVec2 pos = new(
            refRect.Min.X + MathF.Min(g.Style.FramePadding.X * 4.0f, refRect.GetWidth()),
            refRect.Max.Y - MathF.Min(g.Style.FramePadding.Y, refRect.GetHeight()));
        if (window != null && window.Viewport != null)
        {
            ImGuiViewport viewport = window.Viewport;
            pos = new ImVec2(
                Math.Clamp(pos.X, viewport.Pos.X, viewport.Pos.X + viewport.Size.X),
                Math.Clamp(pos.Y, viewport.Pos.Y, viewport.Pos.Y + viewport.Size.Y));
        }

        // ImTrunc: non-integer mouse application in a backend may be lossy and introduce a nonzero delta.
        return new ImVec2((float)(int)pos.X, (float)(int)pos.Y);
    }

    /// <summary>
    /// Returns the typematic amount for a navigation tweak axis.
    /// </summary>
    /// <param name="axis">The axis whose less/more keys are sampled.</param>
    /// <returns>The signed repeat amount, zero when opposite directions cancel.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::GetNavTweakPressedAmount</c>
    ///. The part contract lists a parameterless form;
    /// canonical takes the axis, so that signature is kept.
    /// </para>
    /// <para>
    /// The canonical <c>GetTypematicRepeatRate(RepeatRateNavTweak,.)</c> lookup
    ///. is inlined as the 0.72 delay and 0.30 rate scaling,
    /// because that input helper is owned by the input pipeline.
    /// </para>
    /// </remarks>
    internal static float GetNavTweakPressedAmount(ImGuiAxis axis)
    {
        ImGuiContext g = GetCurrentContext()!;
        // RepeatRateNavTweak: delay * 0.72, rate * 0.30.
        float repeatDelay = g.IO.KeyRepeatDelay * 0.72f;
        float repeatRate = g.IO.KeyRepeatRate * 0.30f;

        ImGuiKey keyLess;
        ImGuiKey keyMore;
        if (g.NavInputSource == ImGuiInputSource.Gamepad)
        {
            keyLess = axis == ImGuiAxis.X ? ImGuiKey.GamepadDpadLeft : ImGuiKey.GamepadDpadUp;
            keyMore = axis == ImGuiAxis.X ? ImGuiKey.GamepadDpadRight : ImGuiKey.GamepadDpadDown;
        }
        else
        {
            keyLess = axis == ImGuiAxis.X ? ImGuiKey.LeftArrow : ImGuiKey.UpArrow;
            keyMore = axis == ImGuiAxis.X ? ImGuiKey.RightArrow : ImGuiKey.DownArrow;
        }

        float amount = GetKeyPressedAmount(keyMore, repeatDelay, repeatRate) - GetKeyPressedAmount(keyLess, repeatDelay, repeatRate);
        if (amount != 0.0f && IsKeyDown(keyLess) && IsKeyDown(keyMore)) // Cancel regardless of repeat phase
            amount = 0.0f;
        return amount;
    }

    #endregion

    #region Navigation Update

    /// <summary>
    /// Runs the per-frame navigation pass: input-source selection, init and move request application,
    /// activation, tabbing, wrapping and manual scrolling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>Imgui::NavUpdate</c>. in canonical
    /// step order. The canonical debug scoring and debug rectangle blocks are compiled out in the reference
    /// build and are omitted.
    /// </para>
    /// <para>
    /// Two canonical calls are left as inert branches because their implementations belong to later chunks:
    /// <c>NavUpdateWindowing()</c> (deep Ctrl+Tab and gamepad window navigation) and
    /// <c>NavUpdateWindowingOverlay()</c>. Extending this method is the responsibility of the chunk that
    /// owns windowing navigation.
    /// </para>
    /// <para>
    /// Owner-scoped input routing is not part of the binding cross-part signatures, so the canonical
    /// <c>ImGuiKeyOwner_NoOwner</c> arguments are omitted and the input layer's owner defaults apply.
    /// </para>
    /// </remarks>
    internal static void NavUpdate()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;

        io.WantSetMousePos = false;

        // Set the input source from whichever navigation keys were pressed last, because some features
        // differ between gamepad and keyboard.
        bool navGamepadActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0
            && (io.BackendFlags & ImGuiBackendFlags.HasGamepad) != 0;
        if (navGamepadActive && g.NavInputSource != ImGuiInputSource.Gamepad)
        {
            for (int i = 0; i < NavGamepadKeysToChangeSource.Length; i++)
            {
                if (IsKeyDown(NavGamepadKeysToChangeSource[i]))
                    g.NavInputSource = ImGuiInputSource.Gamepad;
            }
        }
        bool navKeyboardActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        if (navKeyboardActive && g.NavInputSource != ImGuiInputSource.Keyboard)
        {
            for (int i = 0; i < NavKeyboardKeysToChangeSource.Length; i++)
            {
                if (IsKeyDown(NavKeyboardKeysToChangeSource[i]))
                    g.NavInputSource = ImGuiInputSource.Keyboard;
            }
        }

        // Process the navigation init request (select first/default focus).
        g.NavJustMovedToId = 0;
        g.NavJustMovedToFocusScopeId = 0;
        g.NavJustMovedFromFocusScopeId = 0;
        if (g.NavInitResult.ID != 0)
            NavInitRequestApplyResult();
        g.NavInitRequest = false;
        g.NavInitRequestFromMove = false;
        g.NavInitResult.ID = 0;

        // Process the navigation move request.
        if (g.NavMoveSubmitted)
            NavMoveRequestApplyResult();
        g.NavTabbingCounter = 0;
        g.NavMoveSubmitted = false;
        g.NavMoveScoringItems = false;
        if (g.NavCursorHideFrames > 0)
        {
            g.NavCursorHideFrames--;
            if (g.NavCursorHideFrames == 0)
                g.NavCursorVisible = true;
        }

        // Schedule the mouse-position update, applied at the bottom of this method after move requests and
        // scrolling have been processed.
        bool setMousePos = false;
        if (g.NavMousePosDirty && g.NavIdIsAlive)
        {
            if (g.NavCursorVisible && g.NavHighlightItemUnderNav && g.NavWindow != null)
                setMousePos = true;
        }
        g.NavMousePosDirty = false;
        ImAssert.Check(g.NavLayer == ImGuiNavLayer.Main || g.NavLayer == ImGuiNavLayer.Menu);

        // Store the return window for leaving the menu layer, and clear it once back on the main layer.
        if (g.NavWindow != null)
            NavSaveLastChildNavWindowIntoParent(g.NavWindow);
        if (g.NavWindow != null && g.NavWindow.NavLastChildNavWindow != null && g.NavLayer == ImGuiNavLayer.Main)
            g.NavWindow.NavLastChildNavWindow = null;

        // Ctrl+Tab and gamepad window navigation.
        NavUpdateWindowing();

        // Publish navigation output flags for the host application.
        io.NavActive = (navKeyboardActive || navGamepadActive) && g.NavWindow != null
            && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) == 0;
        io.NavVisible = (io.NavActive && g.NavId != 0 && g.NavCursorVisible) || g.NavWindowingTarget != null;

        // Process NavCancel input, then the context-menu request.
        NavUpdateCancelRequest();
        NavUpdateContextMenuRequest();

        // Process the manual activation request.
        g.NavActivateId = 0;
        g.NavActivateDownId = 0;
        g.NavActivatePressedId = 0;
        g.NavActivateFlags = ImGuiActivateFlags.None;
        ImGuiKey navGamepadActivate = NavGamepadActivateKey(g);
        if (g.NavId != 0 && g.NavCursorVisible && g.NavWindowingTarget == null
            && g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) == 0)
        {
            bool activateDown = (navKeyboardActive && IsKeyDown(ImGuiKey.Space))
                || (navGamepadActive && IsKeyDown(navGamepadActivate));
            bool activatePressed = activateDown
                && ((navKeyboardActive && IsKeyPressed(ImGuiKey.Space, false))
                    || (navGamepadActive && IsKeyPressed(navGamepadActivate, false)));
            bool inputPressedKeyboard = navKeyboardActive
                && (IsKeyPressed(ImGuiKey.Enter, false) || IsKeyPressed(ImGuiKey.KeypadEnter, false));
            bool inputPressedGamepad = false;
            if (activateDown && navGamepadActive && IsKeyDown(navGamepadActivate)
                && ((ImGuiItemFlagsPrivate)g.NavIdItemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_Inputable) != 0)
            {
                ImGuiKeyData keyData = GetKeyData(navGamepadActivate);
                if (keyData.DownDurationPrev < NavActivateInputWithGamepadDelay && keyData.DownDuration >= NavActivateInputWithGamepadDelay)
                    inputPressedGamepad = true;
            }

            if (g.ActiveId == 0 && activatePressed)
            {
                g.NavActivateId = g.NavId;
                g.NavActivateFlags = ImGuiActivateFlags.PreferTweak;
            }
            if ((g.ActiveId == 0 || g.ActiveId == g.NavId) && (inputPressedKeyboard || inputPressedGamepad))
            {
                g.NavActivateId = g.NavId;
                g.NavActivateFlags = ImGuiActivateFlags.PreferInput;
            }
            if ((g.ActiveId == 0 || g.ActiveId == g.NavId) && (activateDown || inputPressedKeyboard || inputPressedGamepad))
                g.NavActivateDownId = g.NavId;
            if ((g.ActiveId == 0 || g.ActiveId == g.NavId) && (activatePressed || inputPressedKeyboard || inputPressedGamepad))
            {
                g.NavActivatePressedId = g.NavId;
                NavHighlightActivated(g.NavId);
            }
        }
        if (g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) != 0)
            g.NavCursorVisible = false;
        else if (g.IO.ConfigNavCursorVisibleAlways && g.NavCursorHideFrames == 0)
            g.NavCursorVisible = true;
        if (g.NavActivateId != 0)
            ImAssert.Check(g.NavActivateDownId == g.NavActivateId);

        // Decay the activation highlight.
        if (g.NavHighlightActivatedTimer > 0.0f)
            g.NavHighlightActivatedTimer = MathF.Max(0.0f, g.NavHighlightActivatedTimer - io.DeltaTime);
        if (g.NavHighlightActivatedTimer == 0.0f)
            g.NavHighlightActivatedId = 0;

        // Process the programmatic activation request.
        if (g.NavNextActivateId != 0)
        {
            g.NavActivateId = g.NavNextActivateId;
            g.NavActivateDownId = g.NavNextActivateId;
            g.NavActivatePressedId = g.NavNextActivateId;
            g.NavActivateFlags = g.NavNextActivateFlags;
        }
        g.NavNextActivateId = 0;

        // Process move requests.
        NavUpdateCreateMoveRequest();
        if (g.NavMoveDir == ImGuiDir.None)
            NavUpdateCreateTabbingRequest();
        NavUpdateAnyRequestFlag();
        g.NavIdIsAlive = false;

        // Scrolling.
        if (g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) == 0 && g.NavWindowingTarget == null)
        {
            ImGuiWindow window = g.NavWindow;
            // Round because sub-pixel scrolling is not reliably supported.
            float scrollSpeed = (float)(int)((window.FontRefSize * 100.0f * io.DeltaTime) + 0.5f);
            ImGuiDir moveDir = g.NavMoveDir;
            if (window.DC.NavLayersActiveMask == 0x00 && window.DC.NavWindowHasScrollY && moveDir != ImGuiDir.None)
            {
                // Fallback manual scroll with the navigation direction keys when the window has no items.
                if (moveDir == ImGuiDir.Left || moveDir == ImGuiDir.Right)
                    SetScrollX(window, (float)(int)(window.Scroll.X + (moveDir == ImGuiDir.Left ? -1.0f : 1.0f) * scrollSpeed));
                if (moveDir == ImGuiDir.Up || moveDir == ImGuiDir.Down)
                    SetScrollY(window, (float)(int)(window.Scroll.Y + (moveDir == ImGuiDir.Up ? -1.0f : 1.0f) * scrollSpeed));
            }

            // Normal manual scroll with the left stick.
            if (navGamepadActive)
            {
                ImVec2 scrollDir = GetKeyMagnitude2d(
                    ImGuiKey.GamepadLStickLeft, ImGuiKey.GamepadLStickRight,
                    ImGuiKey.GamepadLStickUp, ImGuiKey.GamepadLStickDown);
                // GamepadL1/R1 are the canonical NavGamepadTweakSlow/Fast aliases.
                float tweakFactor = IsKeyDown(ImGuiKey.GamepadL1) ? 1.0f / 10.0f
                    : IsKeyDown(ImGuiKey.GamepadR1) ? 10.0f
                    : 1.0f;
                if (scrollDir.X != 0.0f && window.ScrollbarX)
                    SetScrollX(window, (float)(int)(window.Scroll.X + scrollDir.X * scrollSpeed * tweakFactor));
                if (scrollDir.Y != 0.0f)
                    SetScrollY(window, (float)(int)(window.Scroll.Y + scrollDir.Y * scrollSpeed * tweakFactor));
            }
        }

        // Always prioritize the mouse highlight when navigation is fully disabled.
        if (!navKeyboardActive && !navGamepadActive)
        {
            g.NavCursorVisible = false;
            g.NavHighlightItemUnderNav = false;
            setMousePos = false;
        }

        // Apply the scheduled mouse position, accounting for any scroll queued in the window.
        if (setMousePos && io.ConfigNavMoveSetMousePos && (io.BackendFlags & ImGuiBackendFlags.HasSetMousePos) != 0)
            TeleportMousePos(NavCalcPreferredRefPos(ImGuiWindowFlags.Popup));

        g.NavScoringDebugCount = 0;
    }

    /// <summary>
    /// Applies the outstanding navigation init result to the navigation state.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavInitRequestApplyResult</c>
    ///. The navigation window may legitimately be null when
    /// focus is cleared while an init request is outstanding. A move-triggered init request also restores
    /// cursor visibility.
    /// </remarks>
    public static void NavInitRequestApplyResult()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavWindow == null)
            return;

        ImGuiNavItemData result = g.NavInitResult;
        if (g.NavId != result.ID)
        {
            g.NavJustMovedFromFocusScopeId = g.NavFocusScopeId;
            g.NavJustMovedToId = result.ID;
            g.NavJustMovedToFocusScopeId = result.FocusScopeId;
            g.NavJustMovedToKeyMods = ImGuiKey.None;
            g.NavJustMovedToIsTabbing = false;
            g.NavJustMovedToHasSelectionData = ((ImGuiItemFlagsPrivate)result.ItemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData) != 0;
        }

        SetNavID(result.ID, g.NavLayer, result.FocusScopeId, result.RectRel);
        g.NavIdIsAlive = true; // Mark as alive from the previous frame because a result was obtained
        if (result.SelectionUserData != -1)
            g.NavLastValidSelectionUserData = result.SelectionUserData;
        if (g.NavInitRequestFromMove)
            SetNavCursorVisibleAfterMove();
    }

    /// <summary>
    /// Biases the scoring rectangle ahead of scoring and seeds the preferred position on departure.
    /// </summary>
    /// <param name="r">The scoring rectangle to bias.</param>
    /// <param name="preferredPosRel">The stored preferred scoring position for this layer, updated when
    /// missing and read to bias the other axis.</param>
    /// <param name="moveDir">The requested movement direction.</param>
    /// <param name="moveFlags">The active move request flags.</param>
    /// <remarks>
    /// Ports the file-static canonical <c>NavBiasScoringRect</c>
    ///. The default left/up bias makes a downward move from
    /// a wide item land on the leftmost column, while each successful move stores a new bias for one axis.
    /// </remarks>
    private static void NavBiasScoringRect(ref ImRect r, ref ImVec2 preferredPosRel, ImGuiDir moveDir, ImGuiNavMoveFlags moveFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImVec2 relToAbsOffset = g.NavWindow!.DC.CursorStartPos;

        if ((moveFlags & ImGuiNavMoveFlags.Forwarded) == 0)
        {
            if (preferredPosRel.X == float.MaxValue)
                preferredPosRel.X = MathF.Min(r.Min.X + 1.0f, r.Max.X) - relToAbsOffset.X;
            if (preferredPosRel.Y == float.MaxValue)
                preferredPosRel.Y = r.GetCenter().Y - relToAbsOffset.Y;
        }

        if ((moveDir == ImGuiDir.Up || moveDir == ImGuiDir.Down) && preferredPosRel.X != float.MaxValue)
            r.Min.X = r.Max.X = preferredPosRel.X + relToAbsOffset.X;
        else if ((moveDir == ImGuiDir.Left || moveDir == ImGuiDir.Right) && preferredPosRel.Y != float.MaxValue)
            r.Min.Y = r.Max.Y = preferredPosRel.Y + relToAbsOffset.Y;
    }

    /// <summary>
    /// Creates or forwards the directional move request for this frame and prepares the scoring rectangle.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavUpdateCreateMoveRequest</c>
    ///. Directional keys are tested with the repeat-enabled
    /// binding signature; the canonical nav-move repeat-rate flag has no equivalent in that signature. The
    /// canonical <c>IsActiveIdUsingNavDir</c> inline. is
    /// inlined as the corresponding bit test.
    /// </remarks>
    private static void NavUpdateCreateMoveRequest()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;
        ImGuiWindow? window = g.NavWindow;
        bool navGamepadActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0
            && (io.BackendFlags & ImGuiBackendFlags.HasGamepad) != 0;
        bool navKeyboardActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;

        if (g.NavMoveForwardToNextFrame && window != null)
        {
            // Forwarding a previous request whose state was already set by NavMoveRequestForward().
            ImAssert.Check(g.NavMoveDir != ImGuiDir.None && g.NavMoveClipDir != ImGuiDir.None);
            ImAssert.Check((g.NavMoveFlags & ImGuiNavMoveFlags.Forwarded) != 0);
        }
        else
        {
            // Initiate the directional input request.
            g.NavMoveDir = ImGuiDir.None;
            g.NavMoveFlags = ImGuiNavMoveFlags.None;
            g.NavMoveScrollFlags = ImGuiScrollFlags.None;
            if (window != null && g.NavWindowingTarget == null && (window.Flags & ImGuiWindowFlags.NoNavInputs) == 0)
            {
                uint usedDirMask = g.ActiveIdUsingNavDirMask;
                if ((usedDirMask & (1u << (int)ImGuiDir.Left)) == 0
                    && ((navGamepadActive && IsKeyPressed(ImGuiKey.GamepadDpadLeft)) || (navKeyboardActive && IsKeyPressed(ImGuiKey.LeftArrow))))
                    g.NavMoveDir = ImGuiDir.Left;
                if ((usedDirMask & (1u << (int)ImGuiDir.Right)) == 0
                    && ((navGamepadActive && IsKeyPressed(ImGuiKey.GamepadDpadRight)) || (navKeyboardActive && IsKeyPressed(ImGuiKey.RightArrow))))
                    g.NavMoveDir = ImGuiDir.Right;
                if ((usedDirMask & (1u << (int)ImGuiDir.Up)) == 0
                    && ((navGamepadActive && IsKeyPressed(ImGuiKey.GamepadDpadUp)) || (navKeyboardActive && IsKeyPressed(ImGuiKey.UpArrow))))
                    g.NavMoveDir = ImGuiDir.Up;
                if ((usedDirMask & (1u << (int)ImGuiDir.Down)) == 0
                    && ((navGamepadActive && IsKeyPressed(ImGuiKey.GamepadDpadDown)) || (navKeyboardActive && IsKeyPressed(ImGuiKey.DownArrow))))
                    g.NavMoveDir = ImGuiDir.Down;
            }
            g.NavMoveClipDir = g.NavMoveDir;
            g.NavScoringNoClipRect = new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);
        }

        // Update PageUp/PageDown/Home/End scroll.
        float scoringPageOffsetY = 0.0f;
        if (window != null && g.NavMoveDir == ImGuiDir.None && navKeyboardActive)
            scoringPageOffsetY = NavUpdatePageUpPageDown();

        // Submit.
        g.NavMoveForwardToNextFrame = false;
        if (g.NavMoveDir != ImGuiDir.None)
            NavMoveRequestSubmit(g.NavMoveDir, g.NavMoveClipDir, g.NavMoveFlags, g.NavMoveScrollFlags);

        // Moving with no reference triggers an init request used as a fallback when the direction fails.
        if (g.NavMoveSubmitted && g.NavId == 0)
        {
            g.NavInitRequest = true;
            g.NavInitRequestFromMove = true;
            g.NavInitResult.ID = 0;
            if (g.IO.ConfigNavCursorVisibleAuto) // No _NoNavInputs check: move requests cannot be created there.
                g.NavCursorVisible = true;
        }

        // With a gamepad, project the reference navigation box into the visible area so navigation resumes
        // inside the visible region after large scrolls.
        if (g.NavMoveSubmitted && g.NavInputSource == ImGuiInputSource.Gamepad && g.NavLayer == ImGuiNavLayer.Main && window != null)
        {
            bool clampX = (g.NavMoveFlags & (ImGuiNavMoveFlags.LoopX | ImGuiNavMoveFlags.WrapX)) == 0;
            bool clampY = (g.NavMoveFlags & (ImGuiNavMoveFlags.LoopY | ImGuiNavMoveFlags.WrapY)) == 0;
            ImRect innerRectRel = NavWindowRectAbsToRel(window, new ImRect(
                window.InnerRect.Min - new ImVec2(1.0f, 1.0f),
                window.InnerRect.Max + new ImVec2(1.0f, 1.0f)));

            // Account for a changing scroll so the reference is correct on the move-result frame.
            innerRectRel.Translate(CalcNextScrollFromScrollTargetAndClamp(window) - window.Scroll);

            if ((clampX || clampY) && !innerRectRel.Contains(window.NavRectRel[(int)g.NavLayer]))
            {
                float padX = MathF.Min(innerRectRel.GetWidth(), window.FontRefSize * 0.5f);
                float padY = MathF.Min(innerRectRel.GetHeight(), window.FontRefSize * 0.5f);
                innerRectRel.Min.X = clampX ? innerRectRel.Min.X + padX : -float.MaxValue;
                innerRectRel.Max.X = clampX ? innerRectRel.Max.X - padX : +float.MaxValue;
                innerRectRel.Min.Y = clampY ? innerRectRel.Min.Y + padY : -float.MaxValue;
                innerRectRel.Max.Y = clampY ? innerRectRel.Max.Y - padY : +float.MaxValue;
                window.NavRectRel[(int)g.NavLayer].ClipWithFull(innerRectRel);
                g.NavId = 0;
            }
        }

        // Prepare the scoring rectangle: a single segment at the source item's box, biased for the move axis.
        ImRect scoringRect = default;
        if (window != null)
        {
            ImRect navRectRel = !window.NavRectRel[(int)g.NavLayer].IsInverted()
                ? window.NavRectRel[(int)g.NavLayer]
                : new ImRect(0.0f, 0.0f, 0.0f, 0.0f);
            scoringRect = WindowRectRelToAbs(window, navRectRel);

            if ((g.NavMoveFlags & ImGuiNavMoveFlags.IsPageMove) != 0)
            {
                // Starting from a visible location scores visible items first.
                if (window.InnerRect.Contains(scoringRect))
                    g.NavMoveFlags |= ImGuiNavMoveFlags.AlsoScoreVisibleSet;
                g.NavScoringNoClipRect = scoringRect;
                scoringRect.Translate(new ImVec2(0.0f, scoringPageOffsetY));
                g.NavScoringNoClipRect.Add(scoringRect);
            }

            if (g.NavMoveSubmitted)
            {
                NavBiasScoringRect(
                    ref scoringRect,
                    ref window.RootWindowForNav!.NavPreferredScoringPosRel[(int)g.NavLayer],
                    g.NavMoveDir,
                    g.NavMoveFlags);
            }
            ImAssert.Check(!scoringRect.IsInverted());
        }
        g.NavScoringRect = scoringRect;
    }

    /// <summary>
    /// Starts a tabbing request when the tab key was pressed and no directional request exists.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavUpdateCreateTabbingRequest</c>
    ///. Tabbing is always enabled regardless of the
    /// keyboard-navigation configuration flag; the tab direction depends on the shift key and on whether a
    /// navigation cursor is already visible.
    /// </remarks>
    private static void NavUpdateCreateTabbingRequest()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow? window = g.NavWindow;
        ImAssert.Check(g.NavMoveDir == ImGuiDir.None);
        if (window == null || g.NavWindowingTarget != null || (window.Flags & ImGuiWindowFlags.NoNavInputs) != 0 || !g.ConfigNavEnableTabbing)
            return;

        bool tabPressed = IsKeyPressed(ImGuiKey.Tab, true) && !g.IO.KeyCtrl && !g.IO.KeyAlt;
        if (!tabPressed)
            return;

        bool navKeyboardActive = (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        if (navKeyboardActive)
            g.NavTabbingDir = g.IO.KeyShift ? -1 : (g.NavCursorVisible == false && g.ActiveId == 0) ? 0 : +1;
        else
            g.NavTabbingDir = g.IO.KeyShift ? -1 : (g.ActiveId == 0) ? 0 : +1;

        ImGuiNavMoveFlags moveFlags = ImGuiNavMoveFlags.IsTabbing | ImGuiNavMoveFlags.Activate;
        ImGuiScrollFlags scrollFlags = window.Appearing
            ? ImGuiScrollFlags.KeepVisibleEdgeX | ImGuiScrollFlags.AlwaysCenterY
            : ImGuiScrollFlags.KeepVisibleEdgeX | ImGuiScrollFlags.KeepVisibleEdgeY;
        ImGuiDir clipDir = g.NavTabbingDir < 0 ? ImGuiDir.Up : ImGuiDir.Down;
        NavMoveRequestSubmit(ImGuiDir.None, clipDir, moveFlags, scrollFlags);
        g.NavTabbingCounter = -1;
    }

    /// <summary>
    /// Applies the result of the previous frame's navigation move request.
    /// </summary>
    /// <remarks>
    /// Ports canonical <c>ImGui::NavMoveRequestApplyResult</c>
    ///.: selects the best result, scrolls it into view,
    /// possibly switches the navigation window, records the just-moved identifiers, stores the new
    /// identifier and restores the preferred scoring position for the moved axis.
    /// </remarks>
    public static void NavMoveRequestApplyResult()
    {
        ImGuiContext g = GetCurrentContext()!;

        // Select which result to use.
        ImGuiNavItemData? result = g.NavMoveResultLocal.ID != 0
            ? g.NavMoveResultLocal
            : g.NavMoveResultOther.ID != 0 ? g.NavMoveResultOther : null;

        // Tabbing forward wrap.
        if ((g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0 && result == null)
        {
            if ((g.NavTabbingCounter == 1 || g.NavTabbingDir == 0) && g.NavTabbingResultFirst.ID != 0)
                result = g.NavTabbingResultFirst;
        }

        // When no result exists but a navigation identifier does, re-enable the navigation highlight,
        // because the current identifier is not itself a candidate.
        ImGuiAxis axis = g.NavMoveDir == ImGuiDir.Up || g.NavMoveDir == ImGuiDir.Down ? ImGuiAxis.Y : ImGuiAxis.X;
        if (result == null)
        {
            if ((g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0)
                g.NavMoveFlags |= ImGuiNavMoveFlags.NoSetNavCursorVisible;
            if (g.NavId != 0 && (g.NavMoveFlags & ImGuiNavMoveFlags.NoSetNavCursorVisible) == 0)
                SetNavCursorVisibleAfterMove();
            NavClearPreferredPosForAxis(axis); // Failed move clears the preferred position for this axis
            return;
        }

        // PageUp/PageDown first jump to the mostly visible item, otherwise use the previous/next page result.
        if ((g.NavMoveFlags & ImGuiNavMoveFlags.AlsoScoreVisibleSet) != 0)
        {
            if (g.NavMoveResultLocalVisible.ID != 0 && g.NavMoveResultLocalVisible.ID != g.NavId)
                result = g.NavMoveResultLocalVisible;
        }

        // Entering a flattened child from outside: solve the tie with the regular scoring rules.
        if (result != g.NavMoveResultOther && g.NavMoveResultOther.ID != 0 && g.NavMoveResultOther.Window!.ParentWindow == g.NavWindow)
        {
            if (g.NavMoveResultOther.DistBox < result.DistBox
                || (g.NavMoveResultOther.DistBox == result.DistBox && g.NavMoveResultOther.DistCenter < result.DistCenter))
                result = g.NavMoveResultOther;
        }
        ImAssert.Check(g.NavWindow != null && result.Window != null);

        // Scroll to keep the newly navigated item fully into view.
        if (g.NavLayer == ImGuiNavLayer.Main)
        {
            ImRect rectAbs = WindowRectRelToAbs(result.Window!, result.RectRel);
            _ = ScrollToRectEx(result.Window!, rectAbs, g.NavMoveScrollFlags);

            if ((g.NavMoveFlags & ImGuiNavMoveFlags.ScrollToEdgeY) != 0)
            {
                float scrollTarget = g.NavMoveDir == ImGuiDir.Up ? result.Window!.ScrollMax.Y : 0.0f;
                SetScrollY(result.Window!, scrollTarget);
            }
        }

        if (g.NavWindow != result.Window)
        {
            g.NavWindow = result.Window;
            g.NavLastValidSelectionUserData = -1;
        }

        // Clear the active identifier unless the request asked to preserve it.
        if (g.ActiveId != result.ID && (g.NavMoveFlags & ImGuiNavMoveFlags.NoClearActiveId) == 0)
            ClearActiveID();

        // Do not record a just-moved identifier when landing on the same spot, except for page moves.
        if ((g.NavId != result.ID || (g.NavMoveFlags & ImGuiNavMoveFlags.IsPageMove) != 0)
            && (g.NavMoveFlags & ImGuiNavMoveFlags.NoSelect) == 0)
        {
            g.NavJustMovedFromFocusScopeId = g.NavFocusScopeId;
            g.NavJustMovedToId = result.ID;
            g.NavJustMovedToFocusScopeId = result.FocusScopeId;
            g.NavJustMovedToKeyMods = g.NavMoveKeyMods;
            g.NavJustMovedToIsTabbing = (g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0;
            g.NavJustMovedToHasSelectionData = ((ImGuiItemFlagsPrivate)result.ItemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_HasSelectionUserData) != 0;
        }

        // Apply the new navigation identifier and focus scope.
        ImVec2 preferredScoringPosRel = g.NavWindow!.RootWindowForNav!.NavPreferredScoringPosRel[(int)g.NavLayer];
        SetNavID(result.ID, g.NavLayer, result.FocusScopeId, result.RectRel);
        if (result.SelectionUserData != -1)
            g.NavLastValidSelectionUserData = result.SelectionUserData;

        // Restore the preferred position for the moved axis.
        if ((g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) == 0)
        {
            ImVec2 center = result.RectRel.GetCenter();
            if (axis == ImGuiAxis.X)
                preferredScoringPosRel.X = center.X;
            else
                preferredScoringPosRel.Y = center.Y;
            g.NavWindow.RootWindowForNav!.NavPreferredScoringPosRel[(int)g.NavLayer] = preferredScoringPosRel;
        }

        // Tabbing only activates inputable items; other items are merely focused.
        if ((g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0
            && ((ImGuiItemFlagsPrivate)result.ItemFlags & ImGuiItemFlagsPrivate.ImGuiItemFlags_Inputable) == 0)
            g.NavMoveFlags &= ~ImGuiNavMoveFlags.Activate;

        // Activate.
        if ((g.NavMoveFlags & ImGuiNavMoveFlags.Activate) != 0)
        {
            g.NavNextActivateId = result.ID;
            g.NavNextActivateFlags = ImGuiActivateFlags.None;
            if ((g.NavMoveFlags & ImGuiNavMoveFlags.FocusApi) != 0)
                g.NavNextActivateFlags |= ImGuiActivateFlags.FromFocusApi;
            if ((g.NavMoveFlags & ImGuiNavMoveFlags.IsTabbing) != 0)
                g.NavNextActivateFlags |= ImGuiActivateFlags.PreferInput | ImGuiActivateFlags.TryToPreserveState | ImGuiActivateFlags.FromTabbing;
        }

        // Make the navigation cursor visible.
        if ((g.NavMoveFlags & ImGuiNavMoveFlags.NoSetNavCursorVisible) == 0)
            SetNavCursorVisibleAfterMove();
    }

    /// <summary>
    /// Processes Escape and gamepad cancel input to close a popup, leave the menu layer, exit a child
    /// window, or clear navigation focus.
    /// </summary>
    /// <remarks>
    /// Ports the file-static canonical <c>NavUpdateCancelRequest</c>
    ///. The canonical popup-closing branch calls
    /// <c>ClosePopupToLevel</c>, which closes the top non-modal popup.
    /// </remarks>
    private static void NavUpdateCancelRequest()
    {
        ImGuiContext g = GetCurrentContext()!;
        bool navGamepadActive = (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0
            && (g.IO.BackendFlags & ImGuiBackendFlags.HasGamepad) != 0;
        bool navKeyboardActive = (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        if (!(navKeyboardActive && IsKeyPressed(ImGuiKey.Escape, false))
            && !(navGamepadActive && IsKeyPressed(NavGamepadCancelKey(g), false)))
            return;

        if (g.ActiveId != 0)
        {
            ClearActiveID();
        }
        else if (g.NavLayer != ImGuiNavLayer.Main)
        {
            // Leave the menu layer.
            NavRestoreLayer(ImGuiNavLayer.Main);
            SetNavCursorVisibleAfterMove();
        }
        else if (g.NavWindow != null
            && g.NavWindow != g.NavWindow.RootWindow
            && (g.NavWindow.RootWindowForNav!.Flags & ImGuiWindowFlags.Popup) == 0
            && g.NavWindow.RootWindowForNav.ParentWindow != null)
        {
            // Exit a child window.
            ImGuiWindow childWindow = g.NavWindow.RootWindowForNav;
            ImGuiWindow parentWindow = childWindow.ParentWindow!;
            ImAssert.Check(childWindow.ChildId != 0);
            FocusWindow(parentWindow);
            SetNavID(childWindow.ChildId, ImGuiNavLayer.Main, 0, NavWindowRectAbsToRel(parentWindow, childWindow.Rect()));
            SetNavCursorVisibleAfterMove();
        }
        else if (g.OpenPopupStack.Count > 0 && g.OpenPopupStack[^1].Window != null
            && (g.OpenPopupStack[^1].Window!.Flags & ImGuiWindowFlags.Modal) == 0)
        {
            // Close the open non-modal popup or menu.
            ClosePopupToLevel(g.OpenPopupStack.Count - 1, true);
        }
        else
        {
            if (g.IO.ConfigNavEscapeClearFocusItem || g.IO.ConfigNavEscapeClearFocusWindow)
            {
                if (g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.Popup) != 0)
                    g.NavWindow.NavLastIds[0] = 0;
            }

            if (g.IO.ConfigNavEscapeClearFocusItem || g.IO.ConfigNavEscapeClearFocusWindow)
                g.NavId = 0;
            if (g.IO.ConfigNavEscapeClearFocusWindow)
                FocusWindow(null);
        }
    }

    /// <summary>
    /// Processes the context-menu key request and records the item and window that should open a context
    /// menu.
    /// </summary>
    /// <remarks>
    /// Ports the file-static canonical <c>NavUpdateContextMenuRequest</c>
    ///. The <c>#CLOSE</c>/<c>#COLLAPSE</c> identifiers are
    /// hashed with the navigation window's ID-stack seed to match canonical <c>ImGuiWindow::GetID</c>, which
    /// this port defers to the windowing pipeline.
    /// </remarks>
    private static void NavUpdateContextMenuRequest()
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NavOpenContextMenuItemId = 0;
        g.NavOpenContextMenuWindowId = 0;
        bool navKeyboardActive = (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        bool navGamepadActive = (g.IO.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0;
        if ((!navKeyboardActive && !navGamepadActive) || g.NavWindow == null)
            return;

        bool request = false;
        request |= navKeyboardActive && (IsKeyReleased(ImGuiKey.Menu)
            || (IsKeyPressed(ImGuiKey.F10, false) && g.IO.KeyMods == ImGuiKey.ImGuiMod_Shift));
        // GamepadFaceUp is the canonical NavGamepadContextMenu alias.
        request |= navGamepadActive && IsKeyPressed(ImGuiKey.GamepadFaceUp, false);
        if (!request)
            return;

        g.NavOpenContextMenuItemId = g.NavId;
        g.NavOpenContextMenuWindowId = g.NavWindow.ID;

        // Allow triggering for Begin().BeginPopupContextItem().
        ImGuiWindow navWindow = g.NavWindow;
        if (g.NavId == ImHashStr("#CLOSE", 0, navWindow.IDStack[^1])
            || g.NavId == ImHashStr("#COLLAPSE", 0, navWindow.IDStack[^1]))
            g.NavOpenContextMenuItemId = navWindow.MoveId;

        g.NavInputSource = ImGuiInputSource.Keyboard;
        SetNavCursorVisibleAfterMove();
    }

    /// <summary>
    /// Handles the PageUp, PageDown, Home and End navigation keys and returns the vertical scoring offset.
    /// </summary>
    /// <returns>The scoring rectangle Y offset applied for a page move, or zero when no page move started.</returns>
    /// <remarks>
    /// Ports the file-static canonical <c>NavUpdatePageUpPageDown</c>
    ///. When the window has no navigable item the keys fall
    /// back to plain scrolling; otherwise they start a page or edge move request.
    /// </remarks>
    private static float NavUpdatePageUpPageDown()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.NavWindow!;
        if ((window.Flags & ImGuiWindowFlags.NoNavInputs) != 0 || g.NavWindowingTarget != null)
            return 0.0f;

        bool pageUpHeld = IsKeyDown(ImGuiKey.PageUp);
        bool pageDownHeld = IsKeyDown(ImGuiKey.PageDown);
        bool homePressed = IsKeyPressed(ImGuiKey.Home, true);
        bool endPressed = IsKeyPressed(ImGuiKey.End, true);
        if (pageUpHeld == pageDownHeld && homePressed == endPressed) // Proceed only when exactly one is pressed
            return 0.0f;

        if (g.NavLayer != ImGuiNavLayer.Main)
            NavRestoreLayer(ImGuiNavLayer.Main);

        if ((window.DC.NavLayersActiveMask & (1 << (int)ImGuiNavLayer.Main)) == 0 && window.DC.NavWindowHasScrollY)
        {
            // Fallback manual scroll when the window has no navigable item.
            if (IsKeyPressed(ImGuiKey.PageUp, true))
                SetScrollY(window, window.Scroll.Y - window.InnerRect.GetHeight());
            else if (IsKeyPressed(ImGuiKey.PageDown, true))
                SetScrollY(window, window.Scroll.Y + window.InnerRect.GetHeight());
            else if (homePressed)
                SetScrollY(window, 0.0f);
            else if (endPressed)
                SetScrollY(window, window.ScrollMax.Y);
        }
        else
        {
            ref ImRect navRectRel = ref window.NavRectRel[(int)g.NavLayer];
            float pageOffsetY = MathF.Max(
                0.0f,
                window.InnerRect.GetHeight() - window.FontRefSize * 1.0f + navRectRel.GetHeight());
            float navScoringRectOffsetY = 0.0f;
            if (IsKeyPressed(ImGuiKey.PageUp, true))
            {
                navScoringRectOffsetY = -pageOffsetY;
                g.NavMoveDir = ImGuiDir.Down; // The scoring rectangle is offset up, so request the down direction
                g.NavMoveClipDir = ImGuiDir.Up;
                g.NavMoveFlags = ImGuiNavMoveFlags.AllowCurrentNavId | ImGuiNavMoveFlags.IsPageMove;
            }
            else if (IsKeyPressed(ImGuiKey.PageDown, true))
            {
                navScoringRectOffsetY = +pageOffsetY;
                g.NavMoveDir = ImGuiDir.Up; // The scoring rectangle is offset down, so request the up direction
                g.NavMoveClipDir = ImGuiDir.Down;
                g.NavMoveFlags = ImGuiNavMoveFlags.AllowCurrentNavId | ImGuiNavMoveFlags.IsPageMove;
            }
            else if (homePressed)
            {
                navRectRel.Min.Y = navRectRel.Max.Y = 0.0f;
                if (navRectRel.IsInverted())
                    navRectRel.Min.X = navRectRel.Max.X = 0.0f;
                g.NavMoveDir = ImGuiDir.Down;
                g.NavMoveFlags = ImGuiNavMoveFlags.AllowCurrentNavId | ImGuiNavMoveFlags.ScrollToEdgeY;
            }
            else if (endPressed)
            {
                navRectRel.Min.Y = navRectRel.Max.Y = window.ContentSize.Y;
                if (navRectRel.IsInverted())
                    navRectRel.Min.X = navRectRel.Max.X = 0.0f;
                g.NavMoveDir = ImGuiDir.Up;
                g.NavMoveFlags = ImGuiNavMoveFlags.AllowCurrentNavId | ImGuiNavMoveFlags.ScrollToEdgeY;
            }
            return navScoringRectOffsetY;
        }
        return 0.0f;
    }

    /// <summary>
    /// Clamps a value to the inclusive <c>[0,1]</c> range. Mirrors canonical <c>ImSaturate</c>
    /// as used by the windowing highlight fade.
    /// </summary>
    /// <param name="value">Value to clamp.</param>
    /// <returns>The value limited to <c>[0,1]</c>.</returns>
    private static float NavWindowingSaturate(float value)
    {
        return value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value;
    }

    /// <summary>Delay before the windowing highlight starts to fade in.</summary>
    private const float NavWindowingHighlightDelay = 0.20f;

    /// <summary>Delay before the windowing window list starts to appear.</summary>
    private const float NavWindowingListAppearDelay = 0.15f;

    /// <summary>
    /// Finds the next window in focus order that can receive navigation focus, searching from
    /// <paramref name="iStart"/> toward <paramref name="iStop"/> in direction <paramref name="dir"/>.
    /// Ports canonical <c>FindWindowNavFocusable</c>.
    /// </summary>
    /// <param name="iStart">First focus-order index to test.</param>
    /// <param name="iStop">Index at which the search ends, exclusive.</param>
    /// <param name="dir">Step per iteration: <c>+1</c> or <c>-1</c>.</param>
    /// <returns>The first navigable window, or <see langword="null"/>.</returns>
    private static ImGuiWindow? FindWindowNavFocusable(int iStart, int iStop, int dir)
    {
        ImGuiContext g = GetCurrentContext()!;
        for (int i = iStart; i >= 0 && i < g.WindowsFocusOrder.Count && i != iStop; i += dir)
            if (IsWindowNavFocusable(g.WindowsFocusOrder[i]))
                return g.WindowsFocusOrder[i];
        return null;
    }

    /// <summary>
    /// Moves the windowing target to the next or previous navigable window in focus order, wrapping at the
    /// ends. A modal target is never changed. Ports canonical <c>NavUpdateWindowingTarget</c>
    /// </summary>
    /// <param name="focusChangeDir">Direction: <c>-1</c> selects the next (newer) window, <c>+1</c> the previous.</param>
    private static void NavUpdateWindowingTarget(int focusChangeDir)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.NavWindowingTarget != null);
        if ((g.NavWindowingTarget!.Flags & ImGuiWindowFlags.Modal) != 0)
            return;

        int iCurrent = FindWindowFocusIndex(g.NavWindowingTarget);
        ImGuiWindow? windowTarget = FindWindowNavFocusable(iCurrent + focusChangeDir, int.MinValue, focusChangeDir);
        if (windowTarget == null)
            windowTarget = FindWindowNavFocusable(focusChangeDir < 0 ? g.WindowsFocusOrder.Count - 1 : 0, iCurrent, focusChangeDir);
        if (windowTarget != null) // A single window in the list keeps the existing target.
        {
            g.NavWindowingTarget = g.NavWindowingTargetAnim = windowTarget;
            g.NavWindowingAccumDeltaPos = g.NavWindowingAccumDeltaSize = new ImVec2(0.0f, 0.0f);
        }
        g.NavWindowingToggleLayer = false;
    }

    /// <summary>
    /// Focuses the window chosen in the windowing list, closing the overlay, and selects its navigable entry.
    /// Ports canonical <c>NavUpdateWindowingApplyFocus</c>.
    /// </summary>
    /// <param name="applyFocusWindow">Window chosen by the user.</param>
    private static void NavUpdateWindowingApplyFocus(ImGuiWindow applyFocusWindow)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.NavWindow == null || !ReferenceEquals(applyFocusWindow, g.NavWindow.RootWindow))
        {
            ClearActiveID();
            SetNavCursorVisibleAfterMove();
            ClosePopupsOverWindow(applyFocusWindow, false);
            FocusWindow(applyFocusWindow, ImGuiFocusRequestFlags.RestoreFocusedChild);
            ImAssert.Check(g.NavWindow != null);
            applyFocusWindow = g.NavWindow!;
            if (applyFocusWindow.NavLastIds[0] == 0)
                NavInitWindow(applyFocusWindow, false);

            // A window whose only layer is the menu layer selects that layer directly. The check uses the
            // next-frame mask because the window may not have been submitted yet this frame.
            if (applyFocusWindow.DC.NavLayersActiveMaskNext == (1 << (int)ImGuiNavLayer.Menu))
                g.NavLayer = ImGuiNavLayer.Menu;
        }
        g.NavWindowingTarget = null;
    }

    /// <summary>
    /// Handles Ctrl+Tab window switching, gamepad menu/window navigation, Alt layer toggling, and window
    /// movement during windowing. Ports canonical <c>NavUpdateWindowing</c>
    ///., called from <c>NavEndFrame</c>.
    /// </summary>
    /// <remarks>
    /// Two input sources drive it: the keyboard (Ctrl+Tab, Alt) and the gamepad (menu button, L1/R1, left
    /// stick). A target is previewed while the key is held and focus is applied on release. Modal windows
    /// suppress windowing. The window list overlay is drawn by <see cref="NavUpdateWindowingOverlay"/>.
    /// </remarks>
    private static void NavUpdateWindowing()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;

        ImGuiWindow? applyFocusWindow = null;
        bool applyToggleLayer = false;

        ImGuiWindow? modalWindow = GetTopMostPopupModal();
        bool allowWindowing = modalWindow == null;
        if (!allowWindowing)
            g.NavWindowingTarget = null;

        // Fade out the highlight when the target is cleared.
        if (g.NavWindowingTargetAnim != null && g.NavWindowingTarget == null)
        {
            g.NavWindowingHighlightAlpha = Math.Max(g.NavWindowingHighlightAlpha - io.DeltaTime * 10.0f, 0.0f);
            if (g.DimBgRatio <= 0.0f && g.NavWindowingHighlightAlpha <= 0.0f)
                g.NavWindowingTargetAnim = null;
        }

        // Start windowing.
        uint ownerId = ImHashStr("##NavUpdateWindowing");
        bool navGamepadActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0
            && (io.BackendFlags & ImGuiBackendFlags.HasGamepad) != 0;
        bool navKeyboardActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableKeyboard) != 0;
        bool keyboardNextWindow = allowWindowing && g.ConfigNavWindowingKeyNext != ImGuiKey.None
            && Shortcut(g.ConfigNavWindowingKeyNext, ImGuiInputFlags.Repeat | ImGuiInputFlags.RouteAlways, ownerId);
        bool keyboardPrevWindow = allowWindowing && g.ConfigNavWindowingKeyPrev != ImGuiKey.None
            && Shortcut(g.ConfigNavWindowingKeyPrev, ImGuiInputFlags.Repeat | ImGuiInputFlags.RouteAlways, ownerId);
        bool startTogglingWithGamepad = navGamepadActive && g.NavWindowingTarget == null
            && Shortcut(ImGuiKey.GamepadFaceLeft, ImGuiInputFlags.RouteAlways, ownerId);
        bool startWindowingWithGamepad = allowWindowing && startTogglingWithGamepad;
        bool startWindowingWithKeyboard = allowWindowing && g.NavWindowingTarget == null
            && (keyboardNextWindow || keyboardPrevWindow);
        bool justStartedWindowingFromNullFocus = false;
        if (startTogglingWithGamepad)
        {
            g.NavWindowingToggleLayer = true;
            g.NavWindowingToggleKey = ImGuiKey.GamepadFaceLeft;
            g.NavWindowingInputSource = g.NavInputSource = ImGuiInputSource.Gamepad;
        }
        if (startWindowingWithGamepad || startWindowingWithKeyboard)
        {
            ImGuiWindow? window = g.NavWindow != null && IsWindowNavFocusable(g.NavWindow)
                ? g.NavWindow
                : FindWindowNavFocusable(g.WindowsFocusOrder.Count - 1, int.MinValue, -1);
            if (window != null)
            {
                if (startWindowingWithKeyboard || g.ConfigNavWindowingWithGamepad)
                    g.NavWindowingTarget = g.NavWindowingTargetAnim = window.RootWindow;
                g.NavWindowingTimer = g.NavWindowingHighlightAlpha = 0.0f;
                g.NavWindowingAccumDeltaPos = g.NavWindowingAccumDeltaSize = new ImVec2(0.0f, 0.0f);
                g.NavWindowingInputSource = g.NavInputSource = startWindowingWithKeyboard
                    ? ImGuiInputSource.Keyboard : ImGuiInputSource.Gamepad;
                if (g.NavWindow == null)
                    justStartedWindowingFromNullFocus = true;

                // Register ownership of the Next/Prev modifiers.
                if (keyboardNextWindow || keyboardPrevWindow)
                    SetKeyOwnersForKeyChord((g.ConfigNavWindowingKeyNext | g.ConfigNavWindowingKeyPrev) & ImGuiKey.ImGuiMod_Mask_, ownerId);
            }
        }

        // Gamepad update.
        if ((g.NavWindowingTarget != null || g.NavWindowingToggleLayer)
            && g.NavWindowingInputSource == ImGuiInputSource.Gamepad)
        {
            if (g.NavWindowingTarget != null)
            {
                // The highlight appears only after a short hold, so a fast tap that toggles the layer adds no
                // visual noise, while inputs are accepted immediately.
                g.NavWindowingTimer += io.DeltaTime;
                g.NavWindowingHighlightAlpha = Math.Max(g.NavWindowingHighlightAlpha,
                    NavWindowingSaturate((g.NavWindowingTimer - NavWindowingHighlightDelay) / 0.05f));

                int focusChangeDir = (IsKeyPressed(ImGuiKey.GamepadL1) ? 1 : 0) - (IsKeyPressed(ImGuiKey.GamepadR1) ? 1 : 0);
                if (focusChangeDir != 0 && !justStartedWindowingFromNullFocus)
                {
                    NavUpdateWindowingTarget(focusChangeDir);
                    g.NavWindowingHighlightAlpha = 1.0f;
                }
            }

            // A single press toggles the layer; a long press applies focus on release.
            if (!IsKeyDown(ImGuiKey.GamepadFaceLeft))
            {
                g.NavWindowingToggleLayer &= g.NavWindowingHighlightAlpha < 1.0f;
                if (g.NavWindowingToggleLayer && g.NavWindow != null)
                    applyToggleLayer = true;
                else if (!g.NavWindowingToggleLayer)
                    applyFocusWindow = g.NavWindowingTarget;
                g.NavWindowingTarget = null;
                g.NavWindowingToggleLayer = false;
            }
        }

        // Keyboard focus.
        if (g.NavWindowingTarget != null && g.NavWindowingInputSource == ImGuiInputSource.Keyboard)
        {
            ImGuiKey sharedMods = (ImGuiKey)(((uint)(g.ConfigNavWindowingKeyNext != ImGuiKey.None ? g.ConfigNavWindowingKeyNext : ImGuiKey.ImGuiMod_Mask_)
                & (uint)(g.ConfigNavWindowingKeyPrev != ImGuiKey.None ? g.ConfigNavWindowingKeyPrev : ImGuiKey.ImGuiMod_Mask_))
                & (uint)ImGuiKey.ImGuiMod_Mask_);
            ImAssert.Check(sharedMods != ImGuiKey.None, "Next/Prev shortcut needs a shared modifier to hold.");
            g.NavWindowingTimer += io.DeltaTime;
            g.NavWindowingHighlightAlpha = Math.Max(g.NavWindowingHighlightAlpha,
                NavWindowingSaturate((g.NavWindowingTimer - NavWindowingHighlightDelay) / 0.05f));
            if ((keyboardNextWindow || keyboardPrevWindow) && !justStartedWindowingFromNullFocus)
                NavUpdateWindowingTarget(keyboardNextWindow ? -1 : 1);
            else if ((io.KeyMods & sharedMods) != sharedMods)
                applyFocusWindow = g.NavWindowingTarget;
        }

        // Alt (left or right) press and release toggles the menu layer.
        bool windowingToggleLayerStart = false;
        if (g.NavWindow != null && (g.NavWindow.Flags & ImGuiWindowFlags.NoNavInputs) == 0)
        {
            foreach (ImGuiKey toggleKey in new[] { ImGuiKey.LeftAlt, ImGuiKey.RightAlt })
            {
                if (navKeyboardActive && IsKeyPressed(toggleKey, ImGuiInputFlags.None, ImGuiKeyOwner_NoOwner))
                {
                    windowingToggleLayerStart = true;
                    g.NavWindowingToggleLayer = true;
                    g.NavWindowingToggleKey = toggleKey;
                    g.NavWindowingInputSource = g.NavInputSource = ImGuiInputSource.Keyboard;
                    break;
                }
            }
        }
        if (g.NavWindowingToggleLayer && g.NavWindowingInputSource == ImGuiInputSource.Keyboard)
        {
            // Cancel the toggle when text is typed or another modifier is held.
            if (io.InputQueueCharacters.Count > 0 || io.KeyCtrl || io.KeyShift || io.KeySuper)
                g.NavWindowingToggleLayer = false;
            else if (!windowingToggleLayerStart && g.LastKeyboardKeyPressTime == g.Time)
                g.NavWindowingToggleLayer = false;
            else if (!TestKeyOwner(g.NavWindowingToggleKey, ImGuiKeyOwner_NoOwner)
                || !TestKeyOwner(ImGuiKey.ImGuiMod_Alt, ImGuiKeyOwner_NoOwner))
                g.NavWindowingToggleLayer = false;

            // Apply the toggle on release when the mouse did not just move in or out.
            if (IsKeyReleased(g.NavWindowingToggleKey, ImGuiKeyOwner_NoOwner) && g.NavWindowingToggleLayer)
                if (g.ActiveId == 0 || g.ActiveIdAllowOverlap)
                    if (IsMousePosValid(io.MousePos) == IsMousePosValid(io.MousePosPrev))
                        applyToggleLayer = true;
            if (!IsKeyDown(g.NavWindowingToggleKey, ImGuiKeyOwner_NoOwner))
                g.NavWindowingToggleLayer = false;
        }

        // Move the windowing target with arrows (keyboard, without Shift) or the left stick (gamepad).
        if (g.NavWindowingTarget != null && (g.NavWindowingTarget.Flags & ImGuiWindowFlags.NoMove) == 0)
        {
            ImVec2 navMoveDir = new ImVec2(0.0f, 0.0f);
            if (g.NavInputSource == ImGuiInputSource.Keyboard && !io.KeyShift)
                navMoveDir = GetKeyMagnitude2d(ImGuiKey.LeftArrow, ImGuiKey.RightArrow, ImGuiKey.UpArrow, ImGuiKey.DownArrow);
            if (g.NavInputSource == ImGuiInputSource.Gamepad)
                navMoveDir = GetKeyMagnitude2d(ImGuiKey.GamepadLStickLeft, ImGuiKey.GamepadLStickRight,
                    ImGuiKey.GamepadLStickUp, ImGuiKey.GamepadLStickDown);
            if (navMoveDir.X != 0.0f || navMoveDir.Y != 0.0f)
            {
                const float NavMoveSpeed = 800.0f;
                float moveStep = NavMoveSpeed * io.DeltaTime * g.Style.FontScaleMain;
                g.NavWindowingAccumDeltaPos += navMoveDir * moveStep;
                g.NavHighlightItemUnderNav = true;
                ImVec2 accumFloored = new ImVec2(MathF.Truncate(g.NavWindowingAccumDeltaPos.X), MathF.Truncate(g.NavWindowingAccumDeltaPos.Y));
                if (accumFloored.X != 0.0f || accumFloored.Y != 0.0f)
                {
                    // The moving window is the dock-tree root when docked, otherwise the root window. Both are set for a
                    // navigable target, so the canonical unconditional dereference is asserted here.
                    ImGuiWindow? movingWindowCandidate = g.NavWindowingTarget.RootWindowDockTree ?? g.NavWindowingTarget.RootWindow;
                    ImAssert.Check(movingWindowCandidate != null, "A navigation windowing target always has a root window.");
                    ImGuiWindow movingWindow = movingWindowCandidate!;
                    SetWindowPos(movingWindow, movingWindow.Pos + accumFloored, ImGuiCond.Always);
                    g.NavWindowingAccumDeltaPos -= accumFloored;
                }
            }
        }

        // Apply the final focus change.
        if (applyFocusWindow != null)
            NavUpdateWindowingApplyFocus(applyFocusWindow);

        // Apply the menu or layer toggle.
        if (applyToggleLayer && g.NavWindow != null)
        {
            ClearActiveID();

            // Move to the parent when the window has no menu layer of its own.
            ImGuiWindow newNavWindow = g.NavWindow;
            while (newNavWindow.ParentWindow != null
                && (newNavWindow.DC.NavLayersActiveMask & (1 << (int)ImGuiNavLayer.Menu)) == 0
                && (newNavWindow.Flags & ImGuiWindowFlags.ChildWindow) != 0
                && (newNavWindow.Flags & (ImGuiWindowFlags.Popup | ImGuiWindowFlags.ChildMenu)) == 0)
                newNavWindow = newNavWindow.ParentWindow;
            if (!ReferenceEquals(newNavWindow, g.NavWindow))
            {
                ImGuiWindow oldNavWindow = g.NavWindow;
                FocusWindow(newNavWindow);
                newNavWindow.NavLastChildNavWindow = oldNavWindow;
            }

            // Toggle between the main and menu layers.
            ImGuiNavLayer newNavLayer = (g.NavWindow!.DC.NavLayersActiveMask & (1 << (int)ImGuiNavLayer.Menu)) != 0
                ? (ImGuiNavLayer)((int)g.NavLayer ^ 1)
                : ImGuiNavLayer.Main;
            if (newNavLayer != g.NavLayer)
            {
                // Reset the menu layer's nav id when entering the menu bar with Alt.
                bool preserveLayer1NavId = newNavWindow.DockNodeAsHost != null;
                if (newNavLayer == ImGuiNavLayer.Menu && !preserveLayer1NavId)
                    g.NavWindow.NavLastIds[(int)newNavLayer] = 0;
                NavRestoreLayer(newNavLayer);
                SetNavCursorVisibleAfterMove();
            }
        }
    }

    /// <summary>
    /// Draws the Ctrl+Tab window list in the centre of the main viewport while windowing is active and the list
    /// delay has elapsed. Ports canonical <c>NavUpdateWindowingOverlay</c>
    /// </summary>
    /// <remarks>
    /// The list shows every navigable window in focus order from the top down. The current target is
    /// highlighted. The overlay is non-interactive: it has no input, title bar, or resize handle.
    /// </remarks>
    private static void NavUpdateWindowingOverlay()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.NavWindowingTarget != null);

        if (g.NavWindowingTimer < NavWindowingListAppearDelay)
            return;

        ImGuiViewport viewport = GetMainViewport();
        SetNextWindowSizeConstraints(new ImVec2(viewport.Size.X * 0.20f, viewport.Size.Y * 0.20f),
            new ImVec2(float.MaxValue, float.MaxValue));
        SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Always, new ImVec2(0.5f, 0.5f));
        PushStyleVar(ImGuiStyleVar.WindowPadding, g.Style.WindowPadding * 2.0f);
        Begin("##NavWindowingOverlay",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings);
        g.NavWindowingListWindow = g.CurrentWindow;
        if (g.ContextName.Length > 0)
            SeparatorText(g.ContextName);
        for (int n = g.WindowsFocusOrder.Count - 1; n >= 0; n--)
        {
            ImGuiWindow window = g.WindowsFocusOrder[n];
            if (!IsWindowNavFocusable(window))
                continue;
            string label = window.Name;
            if (label.Length == 0)
                label = GetFallbackWindowNameForWindowingList(window);
            Selectable(label, g.NavWindowingTarget == window);
        }
        End();
        PopStyleVar();
    }

    /// <summary>
    /// Returns the label shown in the windowing list for a window without a visible name. Ports canonical
    /// <c>GetFallbackWindowNameForWindowingList</c>.
    /// </summary>
    /// <param name="window">A navigable window with no visible name.</param>
    /// <returns>The localized fallback label.</returns>
    private static string GetFallbackWindowNameForWindowingList(ImGuiWindow window)
    {
        if ((window.Flags & ImGuiWindowFlags.Popup) != 0)
            return "(Popup)";
        if ((window.Flags & ImGuiWindowFlags.MenuBar) != 0 && window.Name == "##MainMenuBar")
            return "(Main menu bar)";
        if (window.DockNodeAsHost != null)
            return "(Dock node)";
        return "(Untitled)";
    }

    /// <summary>
    /// Performs end-of-frame navigation work: the windowing overlay and menu wrap-around requests.
    /// </summary>
    /// <remarks>
    /// Ports the file-static canonical <c>NavEndFrame</c>
    ///. The windowing overlay is left as an inert branch
    /// because deep navigation windowing is owned by the chunk that implements <c>NavUpdateWindowing</c>
    /// and <c>NavUpdateWindowingOverlay</c>.
    /// </remarks>
    internal static void NavEndFrame()
    {
        ImGuiContext g = GetCurrentContext()!;

        // Show the Ctrl+Tab window list while a windowing target exists.
        if (g.NavWindowingTarget != null)
            NavUpdateWindowingOverlay();

        // Perform wrap-around in menus. This is delayed until the end of the frame because appended popups
        // make it ambiguous which EndPopup() call is final.
        if (g.NavWindow != null && NavMoveRequestButNoResultYet()
            && (g.NavMoveFlags & ImGuiNavMoveFlags.WrapMask_) != 0
            && (g.NavMoveFlags & ImGuiNavMoveFlags.Forwarded) == 0)
        {
            NavUpdateCreateWrappingRequest();
        }
    }

    /// <summary>
    /// Rewrites the move request as a wrap-around request starting from the opposite side of the window.
    /// </summary>
    /// <remarks>
    /// Ports the file-static canonical <c>NavUpdateCreateWrappingRequest</c>
    ///. The menu layer does not maintain content size or
    /// scrolling, so it wraps within the window rectangle.
    /// </remarks>
    private static void NavUpdateCreateWrappingRequest()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.NavWindow!;

        bool doForward = false;
        ImRect bbRel = window.NavRectRel[(int)g.NavLayer];
        ImGuiDir clipDir = g.NavMoveDir;
        ImGuiNavMoveFlags moveFlags = g.NavMoveFlags;

        ImVec2 wrapSize = g.NavLayer == ImGuiNavLayer.Menu ? window.Size : window.ContentSize + window.WindowPadding;

        if (g.NavMoveDir == ImGuiDir.Left && (moveFlags & (ImGuiNavMoveFlags.WrapX | ImGuiNavMoveFlags.LoopX)) != 0)
        {
            bbRel.Min.X = bbRel.Max.X = wrapSize.X;
            if ((moveFlags & ImGuiNavMoveFlags.WrapX) != 0)
            {
                bbRel.Translate(new ImVec2(0.0f, -bbRel.GetHeight())); // Previous row
                clipDir = ImGuiDir.Up;
            }
            doForward = true;
        }
        if (g.NavMoveDir == ImGuiDir.Right && (moveFlags & (ImGuiNavMoveFlags.WrapX | ImGuiNavMoveFlags.LoopX)) != 0)
        {
            bbRel.Min.X = bbRel.Max.X = -window.WindowPadding.X;
            if ((moveFlags & ImGuiNavMoveFlags.WrapX) != 0)
            {
                bbRel.Translate(new ImVec2(0.0f, +bbRel.GetHeight())); // Next row
                clipDir = ImGuiDir.Down;
            }
            doForward = true;
        }
        if (g.NavMoveDir == ImGuiDir.Up && (moveFlags & (ImGuiNavMoveFlags.WrapY | ImGuiNavMoveFlags.LoopY)) != 0)
        {
            bbRel.Min.Y = bbRel.Max.Y = wrapSize.Y;
            if ((moveFlags & ImGuiNavMoveFlags.WrapY) != 0)
            {
                bbRel.Translate(new ImVec2(-bbRel.GetWidth(), 0.0f)); // Previous column
                clipDir = ImGuiDir.Left;
            }
            doForward = true;
        }
        if (g.NavMoveDir == ImGuiDir.Down && (moveFlags & (ImGuiNavMoveFlags.WrapY | ImGuiNavMoveFlags.LoopY)) != 0)
        {
            bbRel.Min.Y = bbRel.Max.Y = -window.WindowPadding.Y;
            if ((moveFlags & ImGuiNavMoveFlags.WrapY) != 0)
            {
                bbRel.Translate(new ImVec2(+bbRel.GetWidth(), 0.0f)); // Next column
                clipDir = ImGuiDir.Right;
            }
            doForward = true;
        }
        if (!doForward)
            return;

        window.NavRectRel[(int)g.NavLayer] = bbRel;
        NavClearPreferredPosForAxis(ImGuiAxis.X);
        NavClearPreferredPosForAxis(ImGuiAxis.Y);
        NavMoveRequestForward(g.NavMoveDir, clipDir, moveFlags, g.NavMoveScrollFlags);
    }

    #endregion

    #endregion


    #region SECTION: KEY CLASSIFICATION

    #region Key Classification

    /// <summary>
    /// Determines whether a key value is one of the named keys with per-key state in
    /// <see cref="ImGuiIO.KeysData"/>.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True when the key lies in the canonical named-key range.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsNamedKey</c>.
    /// Legacy native key codes and the modifier pseudo-keys are not named keys.
    /// </remarks>
    public static bool IsNamedKey(ImGuiKey key)
    {
        return key >= ImGuiKey.NamedKey_BEGIN && key < ImGuiKey.NamedKey_END;
    }

    /// <summary>
    /// Determines whether a key value is a named key or one of the four modifier pseudo-keys.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True for a named key or for Control, Shift, Alt, or Super.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsNamedKeyOrMod</c>.
    /// Backends may submit modifier pseudo-keys as well as named keys.
    /// </remarks>
    public static bool IsNamedKeyOrMod(ImGuiKey key)
    {
        return IsNamedKey(key)
            || key == ImGuiKey.ImGuiMod_Ctrl
            || key == ImGuiKey.ImGuiMod_Shift
            || key == ImGuiKey.ImGuiMod_Alt
            || key == ImGuiKey.ImGuiMod_Super;
    }

    /// <summary>
    /// Determines whether a key value is a legacy native key code.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True when the key lies below the named-key range.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsLegacyKey</c>.
    /// Legacy key codes are not accepted by <see cref="ImGuiIO.AddKeyEvent"/>.
    /// </remarks>
    public static bool IsLegacyKey(ImGuiKey key)
    {
        return key >= ImGuiKey.None && key < ImGuiKey.NamedKey_BEGIN;
    }

    /// <summary>
    /// Determines whether a named key is produced by a keyboard.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True for the keyboard portion of the named-key range.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsKeyboardKey</c>.
    /// The keyboard range begins at the first named key and ends where the gamepad range begins.
    /// </remarks>
    public static bool IsKeyboardKey(ImGuiKey key)
    {
        return key >= ImGuiKey.NamedKey_BEGIN && key < ImGuiKey.GamepadStart;
    }

    /// <summary>
    /// Determines whether a named key is produced by a gamepad.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True for the gamepad portion of the named-key range.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsGamepadKey</c>.
    /// The canonical gamepad range ends immediately after the last right-stick direction, which is the
    /// value before the first mouse key.
    /// </remarks>
    public static bool IsGamepadKey(ImGuiKey key)
    {
        return key >= ImGuiKey.GamepadStart && key < ImGuiKey.MouseLeft;
    }

    /// <summary>
    /// Determines whether a named key is an inferred mouse key rather than a backend-submitted key.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True for the mouse-key portion of the named-key range.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsMouseKey</c>.
    /// Mouse keys are derived from the mouse event stream and cannot be submitted directly.
    /// </remarks>
    public static bool IsMouseKey(ImGuiKey key)
    {
        return key >= ImGuiKey.MouseLeft && key <= ImGuiKey.MouseWheelY;
    }

    /// <summary>
    /// Determines whether a key value is an alias key inferred by the input layer.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True for the alias-key range, which currently coincides with the mouse-key range.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsAliasKey</c>.
    /// </remarks>
    public static bool IsAliasKey(ImGuiKey key)
    {
        return key >= ImGuiKey.MouseLeft && key <= ImGuiKey.MouseWheelY;
    }

    /// <summary>
    /// Determines whether a key value is one of the left or right modifier keys.
    /// </summary>
    /// <param name="key">The key value to classify.</param>
    /// <returns>True for LeftCtrl through RightSuper.</returns>
    /// <remarks>
    /// Ports canonical <c>ImGui::IsLRModKey</c>.
    /// </remarks>
    public static bool IsLRModKey(ImGuiKey key)
    {
        return key >= ImGuiKey.LeftCtrl && key <= ImGuiKey.RightSuper;
    }

    #endregion

    #region [SECTION] Canonical Parity & Geometry Utilities

    /// <summary>
    /// Extracts the target mouse button index from popup flags.
    /// Ports canonical <c>ImGui::GetMouseButtonFromPopupFlags</c>.
    /// </summary>
    public static ImGuiMouseButton GetMouseButtonFromPopupFlags(ImGuiPopupFlags flags)
    {
        if ((flags & ImGuiPopupFlags.MouseButtonMask_) != 0)
            return (ImGuiMouseButton)(((int)(flags & ImGuiPopupFlags.MouseButtonMask_) >> (int)ImGuiPopupFlags.MouseButtonShift_) - 1);
        return ImGuiMouseButton.Right;
    }

    /// <summary>
    /// Retrieves the key data for a key from an explicit context.
    /// Ports canonical <c>ImGui::GetKeyData</c>.
    /// </summary>
    public static ImGuiKeyData GetKeyData(ImGuiContext ctx, ImGuiKey key)
    {
        if ((key & ImGuiKey.ImGuiMod_Mask_) != 0)
            key = ConvertSingleModFlagToKey(key);
        ImAssert.Check(IsNamedKey(key), "Support for user key indices was dropped in favor of ImGuiKey.");
        return ctx.IO.KeysData[(int)key - (int)ImGuiKey.NamedKey_BEGIN];
    }

    /// <summary>
    /// Computes linear texture coordinates across a vertex range.
    /// Ports canonical <c>ImGui::ShadeVertsLinearUV</c>.
    /// </summary>
    public static void ShadeVertsLinearUV(ImDrawList drawList, int vertStartIdx, int vertEndIdx, ImVec2 a, ImVec2 b, ImVec2 uvA, ImVec2 uvB, bool clamp)
    {
        drawList.ShadeVertsLinearUV(vertStartIdx, vertEndIdx, a, b, uvA, uvB, clamp);
    }

    /// <summary>
    /// Calculates a point on a cubic Bézier curve at parameter t.
    /// Ports canonical <c>ImBezierCubicCalc</c>.
    /// </summary>
    public static ImVec2 ImBezierCubicCalc(ImVec2 p1, ImVec2 p2, ImVec2 p3, ImVec2 p4, float t)
    {
        float u = 1.0f - t;
        float w1 = u * u * u;
        float w2 = 3 * u * u * t;
        float w3 = 3 * u * t * t;
        float w4 = t * t * t;
        return new ImVec2(w1 * p1.X + w2 * p2.X + w3 * p3.X + w4 * p4.X, w1 * p1.Y + w2 * p2.Y + w3 * p3.Y + w4 * p4.Y);
    }

    /// <summary>
    /// Calculates a point on a quadratic Bézier curve at parameter t.
    /// Ports canonical <c>ImBezierQuadraticCalc</c>.
    /// </summary>
    public static ImVec2 ImBezierQuadraticCalc(ImVec2 p1, ImVec2 p2, ImVec2 p3, float t)
    {
        float u = 1.0f - t;
        float w1 = u * u;
        float w2 = 2 * u * t;
        float w3 = t * t;
        return new ImVec2(w1 * p1.X + w2 * p2.X + w3 * p3.X, w1 * p1.Y + w2 * p2.Y + w3 * p3.Y);
    }

    /// <summary>
    /// Sets the window font scale. Obsolete helper matching canonical <c>ImGui::SetWindowFontScale</c>.
    /// </summary>
    [Obsolete("Use io.FontGlobalScale or ImFontConfig::OversampleH/V or modify font scale directly.")]
    public static void SetWindowFontScale(float scale)
    {
        ImAssert.Check(scale > 0.0f);
        ImGuiWindow window = GetCurrentWindow();
        window.FontWindowScale = scale;
        UpdateCurrentFontSize(0.0f);
    }

    /// <summary>
    /// Sets the context name. Ports canonical <c>ImGui::SetContextName</c>.
    /// </summary>
    public static void SetContextName(ImGuiContext ctx, string name)
    {
        ctx.ContextName = name ?? string.Empty;
    }

    /// <summary>
    /// Marks a demonstration section. Ports canonical <c>ImGui::DemoMarker</c>.
    /// </summary>
    public static void DemoMarker(string file, int line, string section)
    {
        ImGuiContext g = GetContext();
        g.DemoMarkerCallback?.Invoke(file, line, section);
    }

    #endregion

    #endregion

}
