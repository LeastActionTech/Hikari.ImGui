// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the remaining canonical public helper surface added by Chunk 15: the white-texel and
/// rasterizer-density accessors, the delayed mouse-click queries, item key routing, the storage
/// reference accessors, user-texture registration, the vertex color gradient, the theme and
/// measurement helpers restored to their canonical <see cref="ImGui"/> owner, the font free-function
/// delegations, and the <c>GetIO</c>, <c>Combo</c> and <c>ListBox</c> overloads.
/// </summary>
/// <remarks>
/// <para>
/// Each test drives a real <see cref="ImGuiContext"/> with a default font and deterministic
/// (non-trickling) input processing. Interaction is driven by queueing mouse position and button
/// events before the frame that must observe them, and every window uses an unconditional position
/// and size so item geometry is reproducible across frames.
/// </para>
/// </remarks>
[TestFixture]
public class PublicHelperTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);


    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        // Canonical trickling defers later events of the same kind to the next frame; these tests
        // assert one deterministic frame per queued batch.
        io.ConfigInputTrickleEventQueue = false;
    }


    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="body">The widget submissions and measurements to run while the window is current.</param>
    private static void WindowFrame(Action body)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(600.0f, 400.0f), ImGuiCond.Always);
        ImGui.Begin("Window");
        body();
        ImGui.End();
        ImGui.Render();
    }

    #region White texel, rasterizer density, font free functions

    /// <summary>
    /// Verifies that the white-texel getter reports the atlas's reserved texel once a frame has baked
    /// the atlas. Canonical <c>ImGui::GetFontTexUvWhitePixel</c>
    /// </summary>
    [Test]
    public void GetFontTexUvWhitePixel_ReportsTheBuiltAtlasWhiteTexel()
    {
        ImVec2 uv = default;
        WindowFrame(() => uv = ImGui.GetFontTexUvWhitePixel());

        ImVec2 atlasUv = _ctx.IO.Fonts.TexUvWhitePixel;
        Assert.That(uv.X, Is.EqualTo(atlasUv.X).Within(1e-6f));
        Assert.That(uv.Y, Is.EqualTo(atlasUv.Y).Within(1e-6f));
        Assert.That(uv.Y, Is.GreaterThan(0.0f), "The reserved white texel must lie below the atlas top edge.");
    }

    /// <summary>
    /// Verifies that the rasterizer-density setter writes the context density and re-derives the font
    /// size, and that assigning the value already in effect is a no-op. Canonical
    /// <c>ImGui::SetFontRasterizerDensity</c>.
    /// </summary>
    [Test]
    public void SetFontRasterizerDensity_UpdatesTheContextAndRejectsRedundantAssignments()
    {
        _ctx.IO.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;

        float baseline = 0.0f;
        float density = 0.0f;
        float sizeAfterFirst = 0.0f;
        float sizeAfterSecond = 0.0f;
        WindowFrame(() =>
        {
            baseline = _ctx.FontRasterizerDensity;
            ImGui.SetFontRasterizerDensity(2.0f);
            density = _ctx.FontRasterizerDensity;
            sizeAfterFirst = ImGui.GetFontSize();
            ImGui.SetFontRasterizerDensity(2.0f); // Redundant assignment must remain a no-op.
            sizeAfterSecond = ImGui.GetFontSize();
        });

        Assert.That(baseline, Is.EqualTo(1.0f).Within(1e-6f), "The ambient density derives from the viewport scale.");
        Assert.That(density, Is.EqualTo(2.0f).Within(1e-6f));
        Assert.That(sizeAfterFirst, Is.GreaterThan(0.0f));
        Assert.That(sizeAfterSecond, Is.EqualTo(sizeAfterFirst).Within(1e-6f),
            "Assigning the density already in effect must not re-derive the font size.");
    }

    /// <summary>
    /// Verifies that the font free functions are owned by <see cref="ImGui"/> and delegate to the
    /// managed <see cref="ImFont"/> implementations. Canonical <c>ImFontCalcTextSizeEx</c>
    ///. and <c>ImTextCharFromUtf8</c>
    /// </summary>
    [Test]
    public void FontFreeFunctions_DelegateToTheFontOwner()
    {
        ImFont font = _ctx.IO.Fonts.Fonts[0];
        ImVec2 measured = default;
        int remainingChars = -1;
        ImVec2 offset = default;
        WindowFrame(() =>
        {
            // A max width of zero would truncate immediately, so an unlimited width is passed. The
            // trailing newline exercises the canonical offset, which allows for sitting after it.
            measured = ImGui.ImFontCalcTextSizeEx(font, ImGui.GetFontSize(), float.MaxValue, 0.0f,
                "Hello\n", out remainingChars, out offset);
        });

        Assert.That(measured.X, Is.GreaterThan(0.0f));
        Assert.That(measured.Y, Is.GreaterThan(0.0f));
        Assert.That(remainingChars, Is.EqualTo(0));
        Assert.That(offset.X, Is.EqualTo(0.0f), "The offset sits at the start of the empty trailing line.");
        Assert.That(offset.Y, Is.GreaterThan(measured.Y), "The offset allows for the trailing line.");

        byte[] utf8 = { 0x48, 0xE2, 0x82, 0xAC }; // 'H' followed by the Euro sign.
        int i = 0;
        int consumed = ImGui.ImTextCharFromUtf8(utf8, ref i, out uint decoded);
        Assert.That(consumed, Is.EqualTo(1));
        Assert.That(decoded, Is.EqualTo((uint)'H'));
        Assert.That(i, Is.EqualTo(1));
    }

    #endregion

    #region Mouse click queries and item key routing

    /// <summary>
    /// Verifies that the click-count getter reports the count recorded on the frame that processes the
    /// press. Canonical <c>ImGui::GetMouseClickedCount</c>
    /// </summary>
    [Test]
    public void GetMouseClickedCount_ReportsTheRecordedClick()
    {
        _ctx.IO.AddMousePosEvent(20.0f, 20.0f);
        _ctx.IO.AddMouseButtonEvent(0, true);

        int count = 0;
        WindowFrame(() => count = ImGui.GetMouseClickedCount(ImGuiMouseButton.Left));
        Assert.That(count, Is.EqualTo(1));

        // Canonical clears the count every frame, so the click is no longer reported once consumed.
        WindowFrame(() => count = ImGui.GetMouseClickedCount(ImGuiMouseButton.Left));
        Assert.That(count, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that the delayed release query exposes exactly the frame on which the elapsed time
    /// first reaches the delay. Canonical <c>ImGui::IsMouseReleasedWithDelay</c>
    /// </summary>
    [Test]
    public void IsMouseReleasedWithDelay_ReportsOnlyTheReleaseFrameForAZeroDelay()
    {
        _ctx.IO.AddMousePosEvent(20.0f, 20.0f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        bool whileHeld = true;
        WindowFrame(() => whileHeld = ImGui.IsMouseReleasedWithDelay(ImGuiMouseButton.Left, 0.0f));
        Assert.That(whileHeld, Is.False, "A held button is not released.");

        _ctx.IO.AddMouseButtonEvent(0, false);
        bool onRelease = false;
        WindowFrame(() => onRelease = ImGui.IsMouseReleasedWithDelay(ImGuiMouseButton.Left, 0.0f));
        Assert.That(onRelease, Is.True, "A zero delay is satisfied on the release frame.");

        bool afterRelease = true;
        WindowFrame(() => afterRelease = ImGui.IsMouseReleasedWithDelay(ImGuiMouseButton.Left, 0.0f));
        Assert.That(afterRelease, Is.False, "The zero-delay window closes after the release frame.");
    }

    /// <summary>
    /// Verifies that the disambiguated item click count reports zero when no click reached the last
    /// item. Canonical <c>ImGui::GetItemClickedCountWithSingleClickDelay</c>
    /// </summary>
    [Test]
    public void GetItemClickedCountWithSingleClickDelay_ReturnsZeroWithoutAClick()
    {
        int count = -1;
        WindowFrame(() =>
        {
            ImGui.Button("##item");
            count = ImGui.GetItemClickedCountWithSingleClickDelay();
        });

        Assert.That(count, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that item key routing assigns ownership while the last item is hovered and refuses
    /// when no item qualifies. Canonical <c>ImGui::SetItemKeyOwner</c>
    /// </summary>
    [Test]
    public void SetItemKeyOwner_RoutesTheHoveredItemAndRejectsUnqualifiedItems()
    {
        ImRect rect = default;
        WindowFrame(() =>
        {
            ImGui.Button("##owner");
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);

        bool hovered = false;
        WindowFrame(() =>
        {
            ImGui.Button("##owner");
            hovered = ImGui.SetItemKeyOwner(ImGuiKey.Space);
        });
        Assert.That(hovered, Is.True);

        bool unqualified = true;
        WindowFrame(() => unqualified = ImGui.SetItemKeyOwner(ImGuiKey.Space));
        Assert.That(unqualified, Is.False, "No item qualifies before any item is submitted.");
    }

    #endregion

    #region Storage references and user textures

    /// <summary>
    /// Verifies that the boolean and pointer reference accessors insert a pair and return the stored
    /// instance so later writes are visible to readers of the same key. Canonical
    /// <c>ImGuiStorage::GetBoolRef</c> and <c>GetVoidPtrRef</c>.
    /// </summary>
    [Test]
    public void ImGuiStorage_ReferenceAccessors_AliasTheStoredPair()
    {
        ImGuiStorage store = new();

        ImGuiStoragePair boolRef = store.GetBoolRef(11, true);
        Assert.That(boolRef.Key, Is.EqualTo(11u));
        Assert.That(boolRef.ValueInt, Is.EqualTo(1));
        Assert.That(store.GetBool(11), Is.True);

        boolRef.ValueInt = 0;
        Assert.That(store.GetBool(11), Is.False, "The returned pair is the stored pair, not a copy.");

        IntPtr pointer = (IntPtr)0x1234;
        ImGuiStoragePair pointerRef = store.GetVoidPtrRef(12, pointer);
        Assert.That(pointerRef.Key, Is.EqualTo(12u));
        Assert.That(pointerRef.ValuePointer, Is.EqualTo(pointer));
        Assert.That(store.GetVoidPtr(12), Is.EqualTo(pointer));
    }

    /// <summary>
    /// Verifies that registering a user texture increments its reference count and adds it to the
    /// context, and that unregistering reverses both effects. Canonical
    /// <c>ImGui::RegisterUserTexture</c> and <c>UnregisterUserTexture</c>
    /// </summary>
    [Test]
    public void RegisterUserTexture_TracksTheReferenceCountAndContextList()
    {
        ImTextureData texture = new() { UniqueID = 7 };
        texture.Create(ImTextureFormat.RGBA32, 4, 4);

        ImGui.RegisterUserTexture(texture);
        Assert.That(texture.RefCount, Is.EqualTo(1));
        Assert.That(_ctx.UserTextures, Does.Contain(texture));

        ImGui.UnregisterUserTexture(texture);
        Assert.That(texture.RefCount, Is.EqualTo(0));
        Assert.That(_ctx.UserTextures, Does.Not.Contain(texture));
    }

    /// <summary>
    /// Verifies the pixel-size mapping for the two canonical texture formats. Canonical
    /// <c>ImTextureData</c> format table.
    /// </summary>
    [Test]
    public void ImTextureDataGetFormatBytesPerPixel_ReportsCanonicalSizes()
    {
        Assert.That(ImGui.ImTextureDataGetFormatBytesPerPixel(ImTextureFormat.Alpha8), Is.EqualTo(1));
        Assert.That(ImGui.ImTextureDataGetFormatBytesPerPixel(ImTextureFormat.RGBA32), Is.EqualTo(4));
    }

    #endregion

    #region Vertex color gradient

    /// <summary>
    /// Verifies that the gradient helper interpolates the red and blue channels along the gradient
    /// axis while preserving each vertex's existing alpha. Canonical
    /// <c>ImGui::ShadeVertsLinearColorGradientKeepAlpha</c>.
    /// </summary>
    [Test]
    public void ShadeVertsLinearColorGradientKeepAlpha_InterpolatesRgbAndKeepsAlpha()
    {
        ImDrawList drawList = new(_ctx.IO.Fonts);
        drawList.VtxBuffer.Add(new ImDrawVert(new ImVec2(0.0f, 0.0f), ImVec2.Zero, 0x80112233u));
        drawList.VtxBuffer.Add(new ImDrawVert(new ImVec2(10.0f, 0.0f), ImVec2.Zero, 0x80112233u));

        // Packed colors are canonical ABGR: 0xFF0000FF is red and 0xFFFF0000 is blue.
        ImGui.ShadeVertsLinearColorGradientKeepAlpha(drawList, 0, 2, new ImVec2(0.0f, 0.0f),
            new ImVec2(10.0f, 0.0f), 0xFF0000FFu, 0xFFFF0000u);

        uint first = drawList.VtxBuffer[0].Col;
        uint last = drawList.VtxBuffer[1].Col;
        Assert.That(first & 0xFFu, Is.EqualTo(0xFFu), "The start vertex takes the red of the start color.");
        Assert.That((first >> 16) & 0xFFu, Is.EqualTo(0x00u));
        Assert.That(last & 0xFFu, Is.EqualTo(0x00u));
        Assert.That((last >> 16) & 0xFFu, Is.EqualTo(0xFFu), "The end vertex takes the blue of the end color.");
        Assert.That((first >> 24) & 0xFFu, Is.EqualTo(0x80u), "The vertex alpha is preserved.");
        Assert.That((last >> 24) & 0xFFu, Is.EqualTo(0x80u));
    }

    #endregion

    #region Theme and measurement ownership

    /// <summary>
    /// Verifies that the three canonical theme functions produce distinct palettes and that the
    /// no-argument form targets the current context. Canonical <c>ImGui::StyleColorsDark</c>,
    /// <c>StyleColorsClassic</c>, and <c>StyleColorsLight</c>.
    /// </summary>
    [Test]
    public void StyleColors_ApplyDistinctPalettes()
    {
        ImGuiStyle dark = new();
        ImGuiStyle classic = new();
        ImGuiStyle light = new();

        ImGui.StyleColorsDark(dark);
        ImGui.StyleColorsClassic(classic);
        ImGui.StyleColorsLight(light);

        Assert.That(dark.Colors[(int)ImGuiCol.WindowBg].X, Is.LessThan(0.2f));
        Assert.That(light.Colors[(int)ImGuiCol.WindowBg].X, Is.GreaterThan(0.9f));
        Assert.That(classic.Colors[(int)ImGuiCol.WindowBg].W, Is.EqualTo(0.85f).Within(1e-4f));
        Assert.That(light.Colors[(int)ImGuiCol.Text].X, Is.EqualTo(0.0f).Within(1e-4f));
        Assert.That(dark.Colors[(int)ImGuiCol.Text].X, Is.GreaterThan(0.5f));

        ImGui.StyleColorsLight();
        Assert.That(_ctx.Style.Colors[(int)ImGuiCol.WindowBg].X, Is.GreaterThan(0.9f),
            "A null destination targets the current context's style.");
        ImGui.StyleColorsDark();
        Assert.That(_ctx.Style.Colors[(int)ImGuiCol.WindowBg].X, Is.LessThan(0.2f));
    }

    /// <summary>
    /// Verifies that the style selector submits its combo without changing the style while no entry
    /// is activated. Canonical <c>ImGui::ShowStyleSelector</c>
    /// </summary>
    [Test]
    public void ShowStyleSelector_SubmitsWithoutChangingTheStyle()
    {
        ImVec4 before = _ctx.Style.Colors[(int)ImGuiCol.WindowBg];
        bool changed = true;
        WindowFrame(() => changed = ImGui.ShowStyleSelector("Colors##Selector"));

        Assert.That(changed, Is.False);
        Assert.That(_ctx.Style.Colors[(int)ImGuiCol.WindowBg].X, Is.EqualTo(before.X).Within(1e-6f));
        Assert.That(_ctx.Style.Colors[(int)ImGuiCol.WindowBg].Y, Is.EqualTo(before.Y).Within(1e-6f));
    }

    /// <summary>
    /// Verifies that the line-height, frame-height and keyboard-focus helpers are owned by
    /// <see cref="ImGui"/> and available to a submitted item. Canonical
    /// <c>ImGui::GetTextLineHeight</c> and <c>GetFrameHeightWithSpacing</c>
    ///. and <c>ImGui::SetKeyboardFocusHere</c>
    /// </summary>
    [Test]
    public void MeasurementAndFocusHelpers_AreOwnedByImGui()
    {
        float lineHeight = 0.0f;
        float frameHeightWithSpacing = 0.0f;
        WindowFrame(() =>
        {
            lineHeight = ImGui.GetTextLineHeight();
            frameHeightWithSpacing = ImGui.GetFrameHeightWithSpacing();
            ImGui.SetKeyboardFocusHere();
            ImGui.Button("##focused");
        });

        Assert.That(lineHeight, Is.GreaterThan(0.0f));
        Assert.That(frameHeightWithSpacing, Is.GreaterThanOrEqualTo(lineHeight));
        Assert.That(ImGui.GetFrameHeight(), Is.GreaterThan(0.0f));
    }

    #endregion

    #region Callback buffer editing

    /// <summary>Builds callback data over a UTF-8 text with the requested buffer capacity.</summary>
    /// <param name="text">Initial text, stored with a trailing zero terminator.</param>
    /// <param name="capacity">Capacity in bytes; must be large enough for the text and terminator.</param>
    /// <param name="flags">Input-text flags reported to the callback.</param>
    /// <returns>Callback data positioned with the cursor at the end of the text.</returns>
    private static ImGuiInputTextCallbackData MakeCallbackData(string text, int capacity, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        byte[] buffer = new byte[capacity];
        int length = System.Text.Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0);
        return new ImGuiInputTextCallbackData
        {
            Buf = buffer,
            BufSize = capacity,
            BufTextLen = length,
            CursorPos = length,
            SelectionStart = length,
            SelectionEnd = length,
            Flags = flags,
        };
    }

    /// <summary>Reads the zero-terminated callback text back as a managed string.</summary>
    /// <param name="data">Callback data whose buffer is inspected.</param>
    /// <returns>The text stored before the zero terminator.</returns>
    private static string CallbackText(ImGuiInputTextCallbackData data)
    {
        return System.Text.Encoding.UTF8.GetString(data.Buf ?? Array.Empty<byte>(), 0, data.BufTextLen);
    }

    /// <summary>
    /// Verifies that inserting text shifts the tail, re-terminates the buffer, advances the cursor, and
    /// collapses the selection onto it. Canonical
    /// <c>ImGuiInputTextCallbackData::InsertChars</c>.
    /// </summary>
    [Test]
    public void ImGuiInputTextCallbackData_InsertChars_ShiftsTheTailAndCollapsesTheSelection()
    {
        ImGuiInputTextCallbackData data = MakeCallbackData("hello", 16);
        data.CursorPos = 2;
        data.SelectionStart = 1;
        data.SelectionEnd = 4;

        data.InsertChars(0, "X"u8);

        Assert.That(CallbackText(data), Is.EqualTo("Xhello"));
        Assert.That(data.BufTextLen, Is.EqualTo(6));
        Assert.That(data.Buf![data.BufTextLen], Is.EqualTo(0), "The text stays zero-terminated.");
        Assert.That(data.CursorPos, Is.EqualTo(3), "A cursor after the insertion point shifts by the inserted length.");
        Assert.That(data.SelectionStart, Is.EqualTo(3));
        Assert.That(data.SelectionEnd, Is.EqualTo(3));
        Assert.That(data.BufDirty, Is.True);
    }

    /// <summary>
    /// Verifies that an insertion that does not fit is truncated at a UTF-8 codepoint boundary unless
    /// the resize contract is enabled. Canonical
    /// <c>ImGuiInputTextCallbackData::InsertChars</c>.
    /// </summary>
    [Test]
    public void ImGuiInputTextCallbackData_InsertChars_TruncatesOrGrowsAccordingToTheResizeFlag()
    {
        // Capacity five holds three bytes plus the terminator and one spare byte: only one byte fits.
        ImGuiInputTextCallbackData truncated = MakeCallbackData("abc", 5);
        truncated.InsertChars(3, "XY"u8);
        Assert.That(CallbackText(truncated), Is.EqualTo("abcX"));

        // A two-byte codepoint cannot cross the one-byte boundary, so the insertion is rejected whole.
        ImGuiInputTextCallbackData multibyte = MakeCallbackData("abc", 5);
        multibyte.InsertChars(3, "é"u8);
        Assert.That(CallbackText(multibyte), Is.EqualTo("abc"));

        // With the resize contract the buffer is replaced by a larger one and the whole text is inserted.
        ImGuiInputTextCallbackData resizable = MakeCallbackData("abc", 4, ImGuiInputTextFlags.CallbackResize);
        int originalCapacity = resizable.BufSize;
        resizable.InsertChars(3, "XY"u8);
        Assert.That(CallbackText(resizable), Is.EqualTo("abcXY"));
        Assert.That(resizable.BufSize, Is.GreaterThan(originalCapacity));
        Assert.That(resizable.Buf!.Length, Is.GreaterThanOrEqualTo(resizable.BufSize + 1));
    }

    /// <summary>
    /// Verifies that deleting a byte range moves the tail over it, adjusts the cursor inside and past
    /// the range, and collapses the selection. Canonical
    /// <c>ImGuiInputTextCallbackData::DeleteChars</c>.
    /// </summary>
    [Test]
    public void ImGuiInputTextCallbackData_DeleteChars_MovesTheTailAndAdjustsTheCursor()
    {
        ImGuiInputTextCallbackData past = MakeCallbackData("hello", 16);
        past.CursorPos = 5;
        past.DeleteChars(1, 2);
        Assert.That(CallbackText(past), Is.EqualTo("hlo"));
        Assert.That(past.BufTextLen, Is.EqualTo(3));
        Assert.That(past.Buf![past.BufTextLen], Is.EqualTo(0));
        Assert.That(past.CursorPos, Is.EqualTo(3), "A cursor past the deleted range shifts down by its length.");
        Assert.That(past.SelectionStart, Is.EqualTo(3));
        Assert.That(past.SelectionEnd, Is.EqualTo(3));
        Assert.That(past.BufDirty, Is.True);

        ImGuiInputTextCallbackData inside = MakeCallbackData("hello", 16);
        inside.CursorPos = 2;
        inside.DeleteChars(1, 2);
        Assert.That(inside.CursorPos, Is.EqualTo(1), "A cursor inside the deleted range collapses to its start.");
    }

    /// <summary>
    /// Verifies that both editing methods require an attached buffer.
    /// </summary>
    [Test]
    public void ImGuiInputTextCallbackData_EditingWithoutABufferThrows()
    {
        ImGuiInputTextCallbackData data = new();
        Assert.That(() => data.DeleteChars(0, 0), Throws.InvalidOperationException);
        Assert.That(() => data.InsertChars(0, "X"u8), Throws.InvalidOperationException);
    }

    /// <summary>
    /// Verifies that an edit made through <see cref="ImGuiInputTextCallbackData.InsertChars"/> reaches
    /// the caller's text buffer: the widget layer reconciles the dirty callback buffer after the
    /// callback returns. Canonical callback dispatch.
    /// </summary>
    [Test]
    public void InputText_CallbackInsertChars_ReachesTheCallerBuffer()
    {
        string text = "abc";
        bool inserted = false;
        ImGuiInputTextCallback callback = data =>
        {
            if (!inserted && data.EventFlag == ImGuiInputTextFlags.CallbackAlways)
            {
                inserted = true;
                data.InsertChars(data.BufTextLen, "Z"u8);
            }
            return 0;
        };
        ImGuiInputTextFlags flags = ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackResize;

        ImRect rect = default;
        WindowFrame(() =>
        {
            ImGui.InputText("##callback", ref text, flags, callback);
            rect = new ImRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        });

        _ctx.IO.AddMousePosEvent((rect.Min.X + rect.Max.X) * 0.5f, (rect.Min.Y + rect.Max.Y) * 0.5f);
        _ctx.IO.AddMouseButtonEvent(0, true);
        WindowFrame(() => ImGui.InputText("##callback", ref text, flags, callback));
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(() => ImGui.InputText("##callback", ref text, flags, callback));

        Assert.That(inserted, Is.True, "The callback must fire once the field is active.");
        Assert.That(text, Is.EqualTo("abcZ"));
    }

    #endregion

    #region Overloads

    /// <summary>
    /// Verifies that the context overload returns the same input/output record as the context itself.
    /// Canonical <c>ImGui::GetIO(ImGuiContext*)</c>.
    /// </summary>
    [Test]
    public void GetIO_ContextOverload_ReturnsTheContextRecord()
    {
        Assert.That(ImGui.GetIO(_ctx), Is.SameAs(_ctx.IO));
    }

    /// <summary>
    /// Verifies that the getter overload of the combo box materializes every item through the getter
    /// before delegating to the array core, and reports no change while no entry is activated.
    /// Canonical <c>ImGui::Combo</c> getter overload.
    /// </summary>
    [Test]
    public void Combo_GetterOverload_MaterializesEveryItem()
    {
        int calls = 0;
        int current = 0;
        bool changed = true;
        WindowFrame(() => changed = ImGui.Combo("##combo", ref current,
            (_, index) => { calls++; return index == 0 ? "first" : "second"; }, null, 2));

        Assert.That(changed, Is.False);
        Assert.That(current, Is.EqualTo(0));
        Assert.That(calls, Is.EqualTo(2));
    }

    /// <summary>
    /// Verifies that the getter overload of the list box materializes every row through the getter
    /// before delegating to the array core, and reports no change while no row is activated.
    /// Canonical <c>ImGui::ListBox</c> getter overload.
    /// </summary>
    [Test]
    public void ListBox_GetterOverload_MaterializesEveryRow()
    {
        int calls = 0;
        int current = 0;
        bool changed = true;
        WindowFrame(() => changed = ImGui.ListBox("##listbox", ref current,
            (_, index) => { calls++; return $"row {index}"; }, null, 3));

        Assert.That(changed, Is.False);
        Assert.That(current, Is.EqualTo(0));
        Assert.That(calls, Is.EqualTo(3));
    }

    #endregion
}
