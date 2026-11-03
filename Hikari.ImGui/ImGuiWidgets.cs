// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

namespace Hikari.ImGui;

/// <summary>
/// Provides immediate-mode GUI operations, context and frame lifecycle management, windowing,
/// layout, input routing, styling, widgets, tables, and docking facilities.
/// </summary>
public static partial class ImGui
{

    #region SECTION: TEXT AND RENDER HELPERS

    #region Widgets: Text, etc.

    /// <summary>
    /// Submits a text run, word-wrapping it when a wrap position is pushed and falling back to manual
    /// vertical coarse clipping for very long unwrapped runs. Canvases canonical
    /// <c>ImGui::TextEx</c>., declared in
    /// </summary>
    /// <param name="text">The text run. Canonical may pass a non-terminated buffer; this port passes a
    /// managed string.</param>
    /// <param name="textEnd">
    /// The canonical <c>text_end</c> exclusive end expressed as the resolved range content, that is the
    /// substring <c>[text, text_end)</c>. <see langword="null"/> selects the canonical "scan to the end of
    /// the string" behavior.
    /// </param>
    /// <param name="flags">
    /// Canonical <see cref="ImGuiTextFlags"/>. Only <see cref="ImGuiTextFlags.NoWidthForLargeClippedText"/>
    /// is interpreted, and only by the long-text branch, where it suppresses width accumulation for lines
    /// that are skipped or left unrendered.
    /// </param>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: skip check, range resolution, wrap decision, the common or
    /// wrapped path., then the long-text path with line skipping, line
    /// rendering, and remaining-line width accumulation.
    /// </para>
    /// <para>
    /// Canonical's null-range coercion compares the two end pointers.; an
    /// empty range is represented here by an empty end string and needs no coercion, while an end string
    /// whose content equals the whole text legitimately means the full range.
    /// </para>
    /// <para>
    /// Canonical measures the length guard in buffer bytes (<c>text_end - text &lt;= 2000</c>); this port
    /// measures UTF-16 characters. Canonical obtains the line height from <c>GetTextLineHeight()</c>
    ///. and searches lines with <c>ImMemchr</c>; the port's public line-height
    /// accessor is not yet ported, so this method reads <c>g.FontSize</c> directly, which is exactly its
    /// canonical value, and searches with <see cref="string.IndexOf(char, int)"/>.
    /// </para>
    /// <para>
    /// This end-user function deliberately does not hide text after <c>##</c>.
    /// </para>
    /// </remarks>
    internal static void TextEx(string text, string? textEnd = null, ImGuiTextFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;
        ImGuiContext g = GetCurrentContext()!;

        // Canonical coerces a null range by comparing the two end pointers.
        // Under the port's content convention an empty range is an empty end string, which already measures
        // as (0, line height) and draws nothing, so no pointer-equality coercion is required.
        string textRange = ResolveTextRange(text, textEnd);
        ImVec2 textPos = new(window.DC.CursorPos.X, window.DC.CursorPos.Y + window.DC.CurrLineTextBaseOffset);
        float wrapPosX = window.DC.TextWrapPos;
        bool wrapEnabled = wrapPosX >= 0.0f;
        if (textRange.Length <= 2000 || wrapEnabled)
        {
            // Common case.
            float wrapWidth = wrapEnabled ? CalcWrapWidthForPos(window.DC.CursorPos, wrapPosX) : 0.0f;
            ImVec2 textSize = CalcTextSize(textRange, false, wrapWidth);

            ImRect bb = new(textPos, textPos + textSize);
            ItemSize(textSize, 0.0f);
            if (!ItemAdd(bb, 0))
                return;

            RenderTextWrapped(bb.Min, textRange, null, wrapWidth);
        }
        else
        {
            // Long text: manual coarse clipping so only visible lines are measured
            float lineHeight = g.FontSize; // Canonical GetTextLineHeight().
            ImVec2 textSize = ImVec2.Zero;
            ImVec2 pos = textPos;
            int lineStart = 0;

            // Lines to skip. Logging disables skipping because the log must describe every line.
            if (!g.LogEnabled)
            {
                int linesSkippable = (int)((window.ClipRect.Min.Y - textPos.Y) / lineHeight);
                if (linesSkippable > 0)
                {
                    int linesSkipped = 0;
                    while (lineStart < textRange.Length && linesSkipped < linesSkippable)
                    {
                        int lineEnd = FindLineEnd(textRange, lineStart);
                        if ((flags & ImGuiTextFlags.NoWidthForLargeClippedText) == 0)
                            textSize.X = MathF.Max(textSize.X, CalcTextSize(textRange[lineStart..lineEnd]).X);
                        lineStart = lineEnd + 1;
                        linesSkipped++;
                    }
                    pos.Y += linesSkipped * lineHeight;
                }
            }

            // Lines to render. The line rectangle starts at the first
            // visible line and advances with the cursor so IsClippedEx() can stop the loop.
            if (lineStart < textRange.Length)
            {
                ImRect lineRect = new(pos, pos + new ImVec2(float.MaxValue, lineHeight));
                while (lineStart < textRange.Length)
                {
                    if (IsClippedEx(lineRect, 0))
                        break;

                    int lineEnd = FindLineEnd(textRange, lineStart);
                    textSize.X = MathF.Max(textSize.X, CalcTextSize(textRange[lineStart..lineEnd]).X);
                    RenderText(pos, textRange[lineStart..lineEnd], null, false);
                    lineStart = lineEnd + 1;
                    lineRect.Min.Y += lineHeight;
                    lineRect.Max.Y += lineHeight;
                    pos.Y += lineHeight;
                }

                // Count remaining lines and accumulate their width unless suppressed
                int linesSkipped = 0;
                while (lineStart < textRange.Length)
                {
                    int lineEnd = FindLineEnd(textRange, lineStart);
                    if ((flags & ImGuiTextFlags.NoWidthForLargeClippedText) == 0)
                        textSize.X = MathF.Max(textSize.X, CalcTextSize(textRange[lineStart..lineEnd]).X);
                    lineStart = lineEnd + 1;
                    linesSkipped++;
                }
                pos.Y += linesSkipped * lineHeight;
            }
            textSize.Y = pos.Y - textPos.Y;

            ImRect bb = new(textPos, textPos + textSize);
            ItemSize(textSize, 0.0f);
            ItemAdd(bb, 0); // Canonical ignores the return value in this branch.
        }
    }

    /// <summary>
    /// Resolves the canonical <c>(text, text_end)</c> range into the managed substring that is rendered.
    /// </summary>
    /// <param name="text">The full text buffer.</param>
    /// <param name="textEnd">
    /// The canonical end expressed as the resolved range content, or <see langword="null"/> for
    /// "to the end of <paramref name="text"/>".
    /// </param>
    /// <returns>The substring that canonical would describe as <c>[text, text_end)</c>.</returns>
    /// <remarks>
    /// Canonical carries two pointers into one NUL-terminated buffer. A C# string has no interior pointer,
    /// so the port carries the canonical range as the substring it covers; this is exactly the string
    /// <see cref="FindRenderedTextEnd"/> returns for a label. <see cref="GetIDWithSeed(string, string?, uint)"/>
    /// makes the opposite choice for hashing (it interprets its end argument as the trailing substring), so
    /// callers passing a range end to the <c>RenderText*</c> family must pass the covered content, not the
    /// tail.
    /// </remarks>
    private static string ResolveTextRange(string text, string? textEnd) => textEnd ?? text;

    /// <summary>
    /// Returns the index just past the next newline, or the end of the text when none follows.
    /// </summary>
    /// <param name="text">The text being scanned.</param>
    /// <param name="lineStart">Index of the first character of the current line.</param>
    /// <returns>The exclusive line end index.</returns>
    /// <remarks>
    /// Substitutes canonical <c>ImMemchr(line, '\n', text_end - line)</c>, which returns a null pointer
    /// when no newline is found and is then replaced by <c>text_end</c>.
    /// </remarks>
    private static int FindLineEnd(string text, int lineStart)
    {
        int index = text.IndexOf('\n', lineStart);
        return index < 0 ? text.Length : index;
    }

    /// <summary>
    /// Submits raw text without formatting. Canvases canonical
    /// <c>ImGui::TextUnformatted</c>., declared in
    /// </summary>
    /// <param name="text">The raw text run, which may contain <c>##</c> (the marker is rendered here).</param>
    /// <param name="textEnd">The canonical exclusive end expressed as the resolved range content, or
    /// <see langword="null"/> to use the whole string.</param>
    /// <remarks>
    /// A single delegation to <see cref="TextEx"/> with
    /// <see cref="ImGuiTextFlags.NoWidthForLargeClippedText"/>. CONTRACT §4 omitted the canonical
    /// <c>text_end</c> parameter; it is restored here with a default so the binding stays call-compatible.
    /// </remarks>
    public static void TextUnformatted(string text, string? textEnd = null)
    {
        TextEx(text, textEnd, ImGuiTextFlags.NoWidthForLargeClippedText);
    }

    /// <summary>
    /// Submits formatted text. Canvases canonical <c>ImGui::Text</c>
    ///.
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>Canonical's <c>va_start</c>/<c>va_end</c> pair becomes a direct delegation to
    /// <see cref="TextV"/>.</remarks>
    public static void Text(string fmt, params object?[] args)
    {
        TextV(fmt, args);
    }

    /// <summary>
    /// Submits formatted text from a pre-built argument array. Canvases canonical <c>ImGui::TextV</c>
    ///.
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>
    /// The port's <see cref="ImFormatString"/> returns the formatted managed string, so the canonical
    /// temporary-buffer <c>(text, text_end)</c> pair is represented by that string alone and
    /// <see cref="TextEx"/> receives a null end. Passes
    /// <see cref="ImGuiTextFlags.NoWidthForLargeClippedText"/>, exactly as canonical
    /// </remarks>
    public static void TextV(string fmt, object?[] args)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        string text = ImFormatString(fmt, args);
        TextEx(text, null, ImGuiTextFlags.NoWidthForLargeClippedText);
    }

    /// <summary>
    /// Submits formatted text in a temporary text color. Canvases canonical <c>ImGui::TextColored</c>
    ///.
    /// </summary>
    /// <param name="col">Text color pushed for the duration of the call.</param>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void TextColored(ImVec4 col, string fmt, params object?[] args)
    {
        TextColoredV(col, fmt, args);
    }

    /// <summary>
    /// Submits formatted text in a temporary text color from a pre-built argument array. Canvases canonical
    /// <c>ImGui::TextColoredV</c>., declared in
    /// </summary>
    /// <param name="col">Text color pushed for the duration of the call.</param>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>The style-color stack push and pop are balanced around <see cref="TextV"/>.</remarks>
    public static void TextColoredV(ImVec4 col, string fmt, object?[] args)
    {
        PushStyleColor(ImGuiCol.Text, col);
        TextV(fmt, args);
        PopStyleColor();
    }

    /// <summary>
    /// Submits formatted text in the disabled text color. Canvases canonical <c>ImGui::TextDisabled</c>
    ///.
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void TextDisabled(string fmt, params object?[] args)
    {
        TextDisabledV(fmt, args);
    }

    /// <summary>
    /// Submits formatted text in the disabled text color from a pre-built argument array. Canvases canonical
    /// <c>ImGui::TextDisabledV</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void TextDisabledV(string fmt, object?[] args)
    {
        ImGuiContext g = GetCurrentContext()!;
        PushStyleColor(ImGuiCol.Text, g.Style.Colors[(int)ImGuiCol.TextDisabled]);
        TextV(fmt, args);
        PopStyleColor();
    }

    /// <summary>
    /// Submits formatted text with word-wrapping enabled at the end of the available width. Canvases
    /// canonical <c>ImGui::TextWrapped</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void TextWrapped(string fmt, params object?[] args)
    {
        TextWrappedV(fmt, args);
    }

    /// <summary>
    /// Submits formatted wrapped text from a pre-built argument array. Canvases canonical
    /// <c>ImGui::TextWrappedV</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>
    /// A wrap position is pushed only when the current one is negative, so an explicitly pushed wrap
    /// position is preserved.
    /// </remarks>
    public static void TextWrappedV(string fmt, object?[] args)
    {
        ImGuiContext g = GetCurrentContext()!;
        bool needBackup = g.CurrentWindow!.DC.TextWrapPos < 0.0f;
        if (needBackup)
            PushTextWrapPos(0.0f);
        TextV(fmt, args);
        if (needBackup)
            PopTextWrapPos();
    }

    /// <summary>
    /// Submits formatted text aligned horizontally inside a given width. Canvases canonical
    /// <c>ImGui::TextAligned</c>., declared in
    ///  and therefore internal.
    /// </summary>
    /// <param name="alignX">Horizontal alignment: <c>0.0</c> left, <c>0.5</c> centered, <c>1.0</c> right.</param>
    /// <param name="sizeX">Layout width, or <c>0.0</c> for the available content width.</param>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>The API is marked FIXME-WIP in canonical and is designed for one item per line.</remarks>
    internal static void TextAligned(float alignX, float sizeX, string fmt, params object?[] args)
    {
        TextAlignedV(alignX, sizeX, fmt, args);
    }

    /// <summary>
    /// Submits horizontally aligned formatted text from a pre-built argument array. Canvases canonical
    /// <c>ImGui::TextAlignedV</c>., declared in
    ///  and therefore internal.
    /// </summary>
    /// <param name="alignX">Horizontal alignment: <c>0.0</c> left, <c>0.5</c> centered, <c>1.0</c> right.</param>
    /// <param name="sizeX">Layout width, or <c>0.0</c> for the available content width.</param>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: measure the text, resolve the width with <see cref="CalcItemSize"/>,
    /// extend content extents manually, offset for alignment, render through <see cref="RenderTextEllipsis"/>,
    /// submit the item, then restore the horizontal content maximum so right-aligned text does not inflate
    /// the content width.
    /// </para>
    /// <para>
    /// The content-extent fields are properties, so each is read into a local, modified, and written back;
    /// canonical mutates the fields in place.
    /// </para>
    /// </remarks>
    internal static void TextAlignedV(float alignX, float sizeX, string fmt, object?[] args)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        string text = ImFormatString(fmt, args);
        ImVec2 textSize = CalcTextSize(text);
        sizeX = CalcItemSize(new ImVec2(sizeX, 0.0f), 0.0f, textSize.Y).X;

        ImVec2 pos = new(window.DC.CursorPos.X, window.DC.CursorPos.Y + window.DC.CurrLineTextBaseOffset);
        ImVec2 posMax = new(pos.X + sizeX, window.ClipRect.Max.Y);
        ImVec2 size = new(MathF.Min(sizeX, textSize.X), textSize.Y);

        ImVec2 cursorMaxPos = window.DC.CursorMaxPos;
        cursorMaxPos.X = MathF.Max(cursorMaxPos.X, pos.X + textSize.X);
        window.DC.CursorMaxPos = cursorMaxPos;
        ImVec2 idealMaxPos = window.DC.IdealMaxPos;
        idealMaxPos.X = MathF.Max(idealMaxPos.X, pos.X + textSize.X);
        window.DC.IdealMaxPos = idealMaxPos;

        if (alignX > 0.0f && textSize.X < sizeX)
            pos.X += TruncateToInt((sizeX - textSize.X) * alignX);
        RenderTextEllipsis(window.DrawList, pos, posMax, posMax.X, text, null, textSize);

        ImVec2 backupMaxPos = window.DC.CursorMaxPos;
        ItemSize(size);
        ItemAdd(new ImRect(pos, pos + size), 0);
        // Cancel out the content-size extension; a right-aligned run would otherwise widen the content.
        cursorMaxPos = window.DC.CursorMaxPos;
        cursorMaxPos.X = backupMaxPos.X;
        window.DC.CursorMaxPos = cursorMaxPos;

        if (sizeX < textSize.X && IsItemHovered(ImGuiHoveredFlags.NoNavOverride | ImGuiHoveredFlags.AllowWhenDisabled | ImGuiHoveredFlags.ForTooltip))
            SetTooltip("%s", text);
    }

    /// <summary>
    /// Submits a label followed by a formatted value, aligned like the label+value part of a framed widget.
    /// Canvases canonical <c>ImGui::LabelText</c>.,
    ///.
    /// </summary>
    /// <param name="label">Label text; anything after the first <c>##</c> is hidden.</param>
    /// <param name="fmt">Canonical printf-style format string for the value.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void LabelText(string label, string fmt, params object?[] args)
    {
        LabelTextV(label, fmt, args);
    }

    /// <summary>
    /// Submits a label followed by a formatted value from a pre-built argument array. Canvases canonical
    /// <c>ImGui::LabelTextV</c>., declared in
    /// </summary>
    /// <param name="label">Label text; anything after the first <c>##</c> is hidden.</param>
    /// <param name="fmt">Canonical printf-style format string for the value.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>
    /// The value is measured with <see cref="CalcTextSize(string, bool, float)"/> and the label with
    /// <c>FindRenderedTextEnd</c>, mirroring canonical's two-pointer forms.
    /// The label is rendered with <c>hide_text_after_hash = false</c> because it is already truncated.
    /// </remarks>
    public static void LabelTextV(string label, string fmt, object?[] args)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        float w = CalcItemWidth();

        string valueText = ImFormatString(fmt, args);
        ImVec2 valueSize = CalcTextSize(valueText, false);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false);

        ImVec2 pos = window.DC.CursorPos;
        ImRect valueBb = new(pos, pos + new ImVec2(w, valueSize.Y + style.FramePadding.Y * 2));
        ImRect totalBb = new(pos, pos + new ImVec2(
            w + (labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f),
            MathF.Max(valueSize.Y, labelSize.Y) + style.FramePadding.Y * 2));
        ItemSize(totalBb, style.FramePadding.Y);
        if (!ItemAdd(totalBb, 0))
            return;

        RenderTextClipped(valueBb.Min + style.FramePadding, valueBb.Max, valueText, null, valueSize, ImVec2.Zero);
        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(valueBb.Max.X + style.ItemInnerSpacing.X, valueBb.Min.Y + style.FramePadding.Y), labelEnd, null, false);
    }

    /// <summary>
    /// Submits a bullet followed by formatted text, aligned to the typical tree node. Canvases canonical
    /// <c>ImGui::BulletText</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void BulletText(string fmt, params object?[] args)
    {
        BulletTextV(fmt, args);
    }

    /// <summary>
    /// Submits a bullet followed by formatted text from a pre-built argument array. Canvases canonical
    /// <c>ImGui::BulletTextV</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>Empty text contributes no label padding.</remarks>
    public static void BulletTextV(string fmt, object?[] args)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;

        string text = ImFormatString(fmt, args);
        ImVec2 labelSize = CalcTextSize(text, false);
        ImVec2 totalSize = new(g.FontSize + (labelSize.X > 0.0f ? labelSize.X + style.FramePadding.X * 2.0f : 0.0f), labelSize.Y);
        ImVec2 pos = window.DC.CursorPos;
        pos.Y += window.DC.CurrLineTextBaseOffset;
        ItemSize(totalSize, 0.0f);
        ImRect bb = new(pos, pos + totalSize);
        if (!ItemAdd(bb, 0))
            return;

        uint textCol = GetColorU32(ImGuiCol.Text);
        RenderBullet(window.DrawList, bb.Min + new ImVec2(style.FramePadding.X + g.FontSize * 0.5f, g.FontSize * 0.5f), textCol);
        RenderText(bb.Min + new ImVec2(g.FontSize + style.FramePadding.X * 2.0f, 0.0f), text, null, false);
    }

    #endregion

    #region Widgets: Main — TextLink

    /// <summary>
    /// Submits a hyperlink-style text button and reports whether it was clicked. Canvases canonical
    /// <c>ImGui::TextLink</c>., declared in
    /// </summary>
    /// <param name="label">Label text; anything after the first <c>##</c> is hidden and still contributes
    /// to the identifier.</param>
    /// <returns><see langword="true"/> when the link was pressed.</returns>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: item submission, <see cref="ButtonBehavior"/> with default flags,
    /// <see cref="RenderNavCursor"/>, hand mouse cursor on hover, HSV derivation of the text and underline
    /// colors, underline, then the text draw.
    /// </para>
    /// <para>
    /// The canonical <c>window-&gt;GetID(label)</c> call is realized through the port's public
    /// <see cref="GetID(string)"/>, which seeds the hash from the current window's ID stack; the port has no
    /// per-window <c>GetID</c>. The underline offset reads <c>g.FontBaked</c> and <c>g.FontBakedScale</c>,
    /// which are the port's cached font metrics.
    /// </para>
    /// </remarks>
    public static bool TextLink(string label)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);

        ImVec2 pos = new(window.DC.CursorPos.X, window.DC.CursorPos.Y + window.DC.CurrLineTextBaseOffset);
        ImVec2 size = CalcTextSize(labelEnd, false);
        ImRect bb = new(pos, pos + size);
        ItemSize(size, 0.0f);
        if (!ItemAdd(bb, id))
            return false;

        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held);
        RenderNavCursor(bb, id);

        if (hovered)
            SetMouseCursor(ImGuiMouseCursor.Hand);

        // The hovered/held colors are inferred from the single ImGuiCol_TextLink color, by design.
        ImVec4 textColf = g.Style.Colors[(int)ImGuiCol.TextLink];
        ImVec4 lineColf = textColf;
        ColorConvertRGBtoHSV(textColf.X, textColf.Y, textColf.Z, out float h, out float s, out float v);
        if (held || hovered)
        {
            v = Math.Clamp(v + (held ? 0.4f : 0.3f), 0.0f, 1.0f); // Canonical ImSaturate.
            h = (h + 0.02f) % 1.0f;                              // Canonical ImFmod(h + 0.02f, 1.0f).
        }
        ColorConvertHSVtoRGB(h, s, v, out textColf.X, out textColf.Y, out textColf.Z);
        v = Math.Clamp(v - 0.20f, 0.0f, 1.0f);
        ColorConvertHSVtoRGB(h, s, v, out lineColf.X, out lineColf.Y, out lineColf.Z);

        float lineY = bb.Max.Y + MathF.Floor(g.FontBaked!.Descent * g.FontBakedScale * 0.20f);
        window.DrawList.AddLineH(bb.Min.X, bb.Max.X, lineY, GetColorU32(lineColf), 1.0f * TruncateToInt(g.Style._MainScale));

        PushStyleColor(ImGuiCol.Text, GetColorU32(textColf));
        RenderText(bb.Min, labelEnd, null, false);
        PopStyleColor();

        return pressed;
    }

    /// <summary>
    /// Submits a hyperlink-style text button that opens a file or URL when clicked. Canvases canonical
    /// <c>ImGui::TextLinkOpenURL</c>., declared in
    /// </summary>
    /// <param name="label">Label text; also the URL when <paramref name="url"/> is
    /// <see langword="null"/>.</param>
    /// <param name="url">The file or URL to open; <see langword="null"/> reuses the label.</param>
    /// <returns><see langword="true"/> when the link was pressed.</returns>
    /// <remarks>
    /// <para>
    /// The context-menu branch calls <c>BeginPopupContextItem</c>, <c>MenuItem</c>, and <c>EndPopup</c>
    /// from parts 04 and 05; the tooltip uses <c>SetItemTooltip</c> from part 05. When the link is
    /// pressed and the platform installed <see cref="ImGuiPlatformIO.Platform_OpenInShellFn"/>, the URL
    /// is opened in the OS shell.
    /// </para>
    /// </remarks>
    public static bool TextLinkOpenURL(string label, string? url = null)
    {
        ImGuiContext g = GetCurrentContext()!;
        url ??= label;
        bool pressed = TextLink(label);
        if (pressed && g.PlatformIO.Platform_OpenInShellFn != null)
            g.PlatformIO.Platform_OpenInShellFn(g, url);
        // Always displayed, even when the URL equals the label; the localized message is the format string.
        SetItemTooltip(LocalizeGetMsg(ImGuiLocKey.OpenLink_s), url);
        if (BeginPopupContextItem())
        {
            if (MenuItem(LocalizeGetMsg(ImGuiLocKey.CopyLink)))
                SetClipboardText(url);
            EndPopup();
        }
        return pressed;
    }

    #endregion

    #region RENDER HELPERS

    /// <summary>
    /// Draws a text run on the current window's draw list, optionally hiding everything after the first
    /// <c>##</c>. Canvases canonical <c>ImGui::RenderText</c>
    ///.
    /// </summary>
    /// <param name="pos">Top-left text position in screen space.</param>
    /// <param name="text">The text run.</param>
    /// <param name="textEnd">The canonical exclusive end expressed as the resolved range content, or
    /// <see langword="null"/> for the whole string.</param>
    /// <param name="hideTextAfterHash">When true, truncates the run at the first <c>##</c>, as canonical
    /// does by default; widget callers pass false with an already-truncated label.</param>
    /// <remarks>
    /// <para>
    /// Canonical selects <c>FindRenderedTextEnd</c> when hiding and the raw range otherwise
    ///., then draws only when the range is non-empty.
    /// </para>
    /// <para>
    /// Canonical's logging side effect (<c>LogRenderedText</c>, ) is reproduced
    /// when logging is active.
    /// </para>
    /// </remarks>
    internal static void RenderText(ImVec2 pos, string text, string? textEnd = null, bool hideTextAfterHash = true)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        string displayText = ResolveTextRange(text, textEnd);
        if (hideTextAfterHash)
            displayText = FindRenderedTextEnd(displayText);

        if (displayText.Length > 0)
        {
            window.DrawList.AddText(g.Font!, g.FontSize, pos, GetColorU32(ImGuiCol.Text), displayText.AsSpan());
            if (g.LogEnabled)
                LogRenderedText(pos, displayText);
        }
    }

    /// <summary>
    /// Draws a text run wrapped at a caller-supplied width on the current window's draw list. Canvases
    /// canonical <c>ImGui::RenderTextWrapped</c>., declared in
    /// </summary>
    /// <param name="pos">Top-left text position in screen space.</param>
    /// <param name="text">The text run.</param>
    /// <param name="textEnd">The canonical exclusive end expressed as the resolved range content, or
    /// <see langword="null"/> for the whole string.</param>
    /// <param name="wrapWidth">Wrap width in pixels; zero disables wrapping.</param>
    /// <remarks>
    /// No <c>##</c> hiding is performed: the range is rendered raw, as canonical documents
    ///. The logging side effect is reproduced when logging is active.
    /// </remarks>
    internal static void RenderTextWrapped(ImVec2 pos, string text, string? textEnd, float wrapWidth)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        string displayText = ResolveTextRange(text, textEnd);
        if (displayText.Length > 0)
        {
            window.DrawList.AddText(g.Font!, g.FontSize, pos, GetColorU32(ImGuiCol.Text), displayText.AsSpan(), wrapWidth);
            if (g.LogEnabled)
                // The caller's end marker is forwarded so a run whose range was already resolved is
                // captured verbatim, matching canonical.
                LogRenderedText(pos, displayText, textEnd);
        }
    }

    /// <summary>
    /// Draws a clipped text run on an explicit draw list, performing CPU-side fine clipping instead of
    /// pushing a scissor rectangle. Canvases canonical <c>ImGui::RenderTextClippedEx</c>
    ///.
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="posMin">Layout minimum; also the default clip minimum.</param>
    /// <param name="posMax">Layout maximum; also the default clip maximum.</param>
    /// <param name="text">The text run.</param>
    /// <param name="textEnd">The canonical display end expressed as the resolved range content, or
    /// <see langword="null"/> for the whole string.</param>
    /// <param name="textSizeIfKnown">Pre-measured text size, or <see langword="null"/> to measure here.</param>
    /// <param name="align">Alignment in <c>[0,1]</c>; only positive components move the run. Defaults to
    /// <c>(0,0)</c>, as canonical.</param>
    /// <param name="clipRect">Explicit clip rectangle, or <see langword="null"/> to clip against the layout
    /// rectangle.</param>
    /// <remarks>
    /// <para>
    /// The alignment expressions are guarded with <c>ImMax</c> in canonical, so the run never moves left or
    /// up of <paramref name="posMin"/>.
    /// </para>
    /// <para>
    /// Canonical passes a null font and zero font size to <c>ImDrawList::AddText</c>, which then uses the
    /// draw list's shared font and font size. The port's draw list does not publish its shared data, so the
    /// equivalent non-null values are supplied from the context: <c>g.Font</c> and <c>g.FontSize</c> are
    /// exactly what canonical publishes into <c>ImDrawListSharedData</c>.
    /// </para>
    /// </remarks>
    internal static void RenderTextClippedEx(ImDrawList drawList, ImVec2 posMin, ImVec2 posMax, string text, string? textEnd, ImVec2? textSizeIfKnown, ImVec2 align = default, ImRect? clipRect = null)
    {
        ImVec2 pos = posMin;
        string displayText = ResolveTextRange(text, textEnd);
        ImVec2 textSize = textSizeIfKnown ?? CalcTextSize(displayText, false, 0.0f);

        ImVec2 clipMin = clipRect?.Min ?? posMin;
        ImVec2 clipMax = clipRect?.Max ?? posMax;
        bool needClipping = (pos.X + textSize.X >= clipMax.X) || (pos.Y + textSize.Y >= clipMax.Y);
        if (clipRect.HasValue) // Without an explicit clip rectangle, pos == clip_min.
            needClipping |= (pos.X < clipMin.X) || (pos.Y < clipMin.Y);

        if (align.X > 0.0f)
            pos.X = MathF.Max(pos.X, pos.X + (posMax.X - pos.X - textSize.X) * align.X);
        if (align.Y > 0.0f)
            pos.Y = MathF.Max(pos.Y, pos.Y + (posMax.Y - pos.Y - textSize.Y) * align.Y);

        ImGuiContext g = GetCurrentContext()!;
        if (needClipping)
        {
            ImVec4 fineClipRect = new(clipMin.X, clipMin.Y, clipMax.X, clipMax.Y);
            drawList.AddText(g.Font!, g.FontSize, pos, GetColorU32(ImGuiCol.Text), displayText.AsSpan(), 0.0f, fineClipRect);
        }
        else
        {
            drawList.AddText(g.Font!, g.FontSize, pos, GetColorU32(ImGuiCol.Text), displayText.AsSpan());
        }
    }

    /// <summary>
    /// Draws a clipped text run on the current window's draw list, always hiding everything after the first
    /// <c>##</c>. Canvases canonical <c>ImGui::RenderTextClipped</c>
    ///.
    /// </summary>
    /// <param name="posMin">Layout minimum; also the default clip minimum.</param>
    /// <param name="posMax">Layout maximum; also the default clip maximum.</param>
    /// <param name="text">The text run.</param>
    /// <param name="textEnd">The canonical exclusive end expressed as the resolved range content, or
    /// <see langword="null"/> for the whole string.</param>
    /// <param name="textSizeIfKnown">Pre-measured text size of the full run, or <see langword="null"/> to
    /// measure here.</param>
    /// <param name="align">Alignment in <c>[0,1]</c> forwarded to <see cref="RenderTextClippedEx"/>. Defaults
    /// to <c>(0,0)</c>, as canonical.</param>
    /// <param name="clipRect">Explicit clip rectangle forwarded to <see cref="RenderTextClippedEx"/>.</param>
    /// <remarks>
    /// Unlike <see cref="RenderText"/>, this helper always truncates at <c>##</c>
    ///. and returns without drawing an empty display run. The logging side effect is
    /// reproduced when logging is active.
    /// </remarks>
    internal static void RenderTextClipped(ImVec2 posMin, ImVec2 posMax, string text, string? textEnd, ImVec2? textSizeIfKnown, ImVec2 align = default, ImRect? clipRect = null)
    {
        string displayText = FindRenderedTextEnd(ResolveTextRange(text, textEnd));
        if (displayText.Length == 0)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        RenderTextClippedEx(window.DrawList, posMin, posMax, displayText, null, textSizeIfKnown, align, clipRect);
        if (g.LogEnabled)
            LogRenderedText(posMin, displayText);
    }

    /// <summary>
    /// Draws a text run with a trailing ellipsis when it overflows its layout rectangle. Canvases canonical
    /// <c>ImGui::RenderTextEllipsis</c>., declared in
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="posMin">Layout minimum.</param>
    /// <param name="posMax">Layout maximum; exceeding this width triggers the ellipsis.</param>
    /// <param name="ellipsisMaxX">Right edge that the ellipsis may claim; may exceed
    /// <paramref name="posMax"/>.X because layout and ellipsis budget are deliberately decoupled.</param>
    /// <param name="text">The text run.</param>
    /// <param name="textEnd">The canonical full end expressed as the resolved range content, or
    /// <see langword="null"/> to truncate at the first <c>##</c>.</param>
    /// <param name="textSizeIfKnown">Pre-measured full text size, or <see langword="null"/> to measure here.</param>
    /// <remarks>
    /// <para>
    /// Canonical removed the obsolete <c>float clip_max_x</c> parameter on 2025/04/16; CONTRACT §4 still
    /// lists it, and it is omitted here.
    /// </para>
    /// <para>
    /// The ellipsis width uses the baked glyph advance scaled by <c>g.FontBakedScale</c>, which canonical
    /// publishes as <c>ImDrawListSharedData::FontScale</c>. The truncated run length
    /// comes from the port's <c>ImFontCalcTextSizeEx</c> "remaining characters" output, the managed
    /// equivalent of canonical's <c>out_remaining</c> pointer. The logging side effect is reproduced when
    /// logging is active.
    /// </para>
    /// </remarks>
    internal static void RenderTextEllipsis(ImDrawList drawList, ImVec2 posMin, ImVec2 posMax, float ellipsisMaxX, string text, string? textEnd, ImVec2? textSizeIfKnown)
    {
        ImGuiContext g = GetCurrentContext()!;
        string fullText = textEnd is null ? FindRenderedTextEnd(text) : ResolveTextRange(text, textEnd);
        ImVec2 textSize = textSizeIfKnown ?? CalcTextSize(fullText, false, 0.0f);

        if (textSize.X > posMax.X - posMin.X)
        {
            ImFont font = g.Font!;
            float fontSize = g.FontSize;
            float fontScale = g.FontBakedScale;
            ImFontBaked baked = font.GetFontBaked(fontSize);
            float ellipsisWidth = baked.GetCharAdvance(font.EllipsisChar) * fontScale;

            // The space between pos_max.x and ellipsis_max.x can be claimed by the text.
            float textAvailWidth = MathF.Max((MathF.Max(posMax.X, ellipsisMaxX) - ellipsisWidth) - posMin.X, 1.0f);
            ImVec2 clippedSize = ImFont.ImFontCalcTextSizeEx(font, fontSize, textAvailWidth, 0.0f, fullText.AsSpan(), out int remainingChars, out _);
            float textSizeClippedX = clippedSize.X;
            string textEndEllipsis = remainingChars > 0 ? fullText[..(fullText.Length - remainingChars)] : fullText;

            RenderTextClippedEx(drawList, posMin, posMax, fullText, textEndEllipsis, textSize, ImVec2.Zero);
            ImVec4 cpuFineClipRect = new(posMin.X, posMin.Y, posMax.X, posMax.Y);
            ImVec2 ellipsisPos = TruncateVector(new ImVec2(posMin.X + textSizeClippedX, posMin.Y));
            font.RenderChar(drawList, fontSize, ellipsisPos, GetColorU32(ImGuiCol.Text), font.EllipsisChar, cpuFineClipRect);
        }
        else
        {
            RenderTextClippedEx(drawList, posMin, posMax, fullText, null, textSize, ImVec2.Zero);
        }
        if (g.LogEnabled)
            LogRenderedText(posMin, fullText);
    }

    /// <summary>
    /// Draws a filled rectangle with optional border and drop shadow on the current window's draw list.
    /// Canvases canonical <c>ImGui::RenderFrame</c>., declared
    ///.
    /// </summary>
    /// <param name="pMin">Rectangle minimum.</param>
    /// <param name="pMax">Rectangle maximum.</param>
    /// <param name="fillCol">Packed fill color in canonical ABGR order.</param>
    /// <param name="borders">Whether the border pair is drawn when the style border size is positive.</param>
    /// <param name="rounding">Corner rounding in pixels.</param>
    /// <remarks>
    /// The border is a pair of one-pixel offset rectangles: the shadow color at <c>(pMin + (1,1))</c> and the
    /// border color at <paramref name="pMin"/>.
    /// </remarks>
    internal static void RenderFrame(ImVec2 pMin, ImVec2 pMax, uint fillCol, bool borders = true, float rounding = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        window.DrawList.AddRectFilled(pMin, pMax, fillCol, rounding);
        float borderSize = g.Style.FrameBorderSize;
        if (borders && borderSize > 0.0f)
        {
            window.DrawList.AddRect(pMin + new ImVec2(1, 1), pMax + new ImVec2(1, 1), GetColorU32(ImGuiCol.BorderShadow), rounding, borderSize);
            window.DrawList.AddRect(pMin, pMax, GetColorU32(ImGuiCol.Border), rounding, borderSize);
        }
    }

    /// <summary>
    /// Draws only the drop-shadow and border pair of a framed rectangle on the current window's draw list.
    /// Canvases canonical <c>ImGui::RenderFrameBorder</c>.,
    ///.
    /// </summary>
    /// <param name="pMin">Rectangle minimum.</param>
    /// <param name="pMax">Rectangle maximum.</param>
    /// <param name="rounding">Corner rounding in pixels.</param>
    internal static void RenderFrameBorder(ImVec2 pMin, ImVec2 pMax, float rounding = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        float borderSize = g.Style.FrameBorderSize;
        if (borderSize > 0.0f)
        {
            window.DrawList.AddRect(pMin + new ImVec2(1, 1), pMax + new ImVec2(1, 1), GetColorU32(ImGuiCol.BorderShadow), rounding, borderSize);
            window.DrawList.AddRect(pMin, pMax, GetColorU32(ImGuiCol.Border), rounding, borderSize);
        }
    }

    /// <summary>
    /// Draws the component marker used by color drag/slider widgets on the current window's draw list.
    /// Canvases canonical <c>ImGui::RenderColorComponentMarker</c>
    ///.
    /// </summary>
    /// <param name="bb">Widget bounds; a degenerate width of one pixel or less draws nothing.</param>
    /// <param name="col">Packed marker color in canonical ABGR order.</param>
    /// <param name="rounding">Corner rounding in pixels.</param>
    /// <remarks>
    /// CONTRACT §4 binds an <c>ImGuiColorEditFlags</c> parameter here, but canonical takes a rounding amount
    /// and both call sites pass <c>style.FrameRounding</c>; the
    /// canonical parameter is restored.
    /// </remarks>
    internal static void RenderColorComponentMarker(ImRect bb, uint col, float rounding)
    {
        if (bb.Min.X + 1 >= bb.Max.X)
            return;
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        RenderRectFilledInRangeH(window.DrawList, bb, col, bb.Min.X, MathF.Min(bb.Min.X + g.Style.ColorMarkerSize, bb.Max.X), rounding);
    }

    /// <summary>
    /// Draws the navigation cursor around an item. Canvases canonical <c>ImGui::RenderNavCursor</c>
    ///.
    /// </summary>
    /// <param name="bb">Item bounds.</param>
    /// <param name="id">Item identifier; the cursor is drawn only for the focused navigation identifier.</param>
    /// <param name="flags">Canonical <see cref="ImGuiNavRenderCursorFlags"/>.</param>
    /// <param name="rounding">Corner rounding; a negative value selects <c>style.FrameRounding</c>.
    /// Canonical default.</param>
    /// <remarks>
    /// <para>
    /// The canonical early-outs are preserved in order: identifier mismatch, cursor visibility unless
    /// <see cref="ImGuiNavRenderCursorFlags.AlwaysDraw"/>, no-navigation items, and
    /// <c>DC.NavHideHighlightOneFrame</c>.
    /// </para>
    /// <para>
    /// Canonical reads the main scale through <c>GetScale()</c>., which is an
    /// inline returning <c>g.Style._MainScale</c>; that accessor is not ported, so the field is read directly.
    /// </para>
    /// <para>
    /// The obsolete <c>NoRounding</c> compatibility branch. is inert: the port's
    /// <see cref="ImGuiNavRenderCursorFlags"/> omits <c>NoRounding</c> (canonical value <c>1 &lt;&lt; 3</c>)
    /// and its obsolete alias carries the wrong value, so no caller can currently request it. The branch is
    /// restored when the enum value is added.
    /// </para>
    /// </remarks>
    internal static void RenderNavCursor(ImRect bb, uint id, ImGuiNavRenderCursorFlags flags = 0, float rounding = -1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (id != g.NavId)
            return;
        if (!g.NavCursorVisible && (flags & ImGuiNavRenderCursorFlags.AlwaysDraw) == 0)
            return;
        if (id == g.LastItemData.ID && (g.LastItemData.ItemFlags & ImGuiItemFlags.NoNav) != 0)
            return;

        // Canonical does not early out on ImGuiWindowFlags_NoNavInputs here; NavCursorVisible is the
        // consistent gate instead.
        ImGuiWindow window = g.CurrentWindow!;
        if (window.DC.NavHideHighlightOneFrame)
            return;

        if (rounding < 0.0f)
            rounding = g.Style.FrameRounding;

        ImRect displayRect = bb;
        displayRect.ClipWith(window.ClipRect);
        float scaleFactor = g.Style._MainScale; // Canonical GetScale().
        float thickness = TruncateToInt(MathF.Max(2.0f, 1.5f * scaleFactor));
        if ((flags & ImGuiNavRenderCursorFlags.Compact) != 0)
        {
            window.DrawList.AddRect(displayRect.Min, displayRect.Max, GetColorU32(ImGuiCol.NavCursor), rounding, thickness);
        }
        else
        {
            float distance = TruncateToInt(3.0f + thickness * 0.5f);
            displayRect.Expand(new ImVec2(distance, distance));
            bool fullyVisible = window.ClipRect.Contains(displayRect);
            if (!fullyVisible)
                window.DrawList.PushClipRect(displayRect.Min, displayRect.Max);
            window.DrawList.AddRect(displayRect.Min, displayRect.Max, GetColorU32(ImGuiCol.NavCursor), rounding, thickness);
            if (!fullyVisible)
                window.DrawList.PopClipRect();
        }
    }

    /// <summary>
    /// Draws the software mouse cursor for every viewport. Canvases canonical <c>ImGui::RenderMouseCursor</c>
    ///.
    /// </summary>
    /// <param name="pos">Cursor position in screen space.</param>
    /// <param name="scale">Cursor scale multiplier before the per-viewport DPI scale is applied.</param>
    /// <param name="mouseCursor">Cursor shape; out-of-range values are coerced to the arrow.</param>
    /// <param name="colFill">Packed fill color in canonical ABGR order.</param>
    /// <param name="colBorder">Packed border color in canonical ABGR order.</param>
    /// <param name="colShadow">Packed drop-shadow color in canonical ABGR order.</param>
    /// <remarks>
    /// <para>
    /// Only the out-of-range coercion is active; canonical intentionally accepts out-of-bound values
    /// </para>
    /// <para>
    /// The cursor geometry is deliberately inert. Canonical retrieves the shape offset, size, and UV
    /// rectangles through <c>ImFontAtlasGetMouseCursorTexData(g.DrawListSharedData.FontAtlas,.)</c>,
    /// pushes <c>font_atlas-&gt;TexRef</c> on <c>GetForegroundDrawList(viewport)</c>, draws four shadow,
    /// border, and fill images, and animates the Wait/Progress arc from <c>g.Time</c>. The port's
    /// <see cref="ImDrawListSharedData"/> publishes neither an atlas reference nor cursor texture data, and
    /// no <c>ImFontAtlasGetMouseCursorTexData</c> exists, so no offset, size, or UV values can be produced.
    /// The cursor bitmap lookup remains deferred with the font-atlas surface.
    /// </para>
    /// </remarks>
    internal static void RenderMouseCursor(ImVec2 pos, float scale, ImGuiMouseCursor mouseCursor, uint colFill, uint colBorder, uint colShadow)
    {
        if (mouseCursor <= ImGuiMouseCursor.None || mouseCursor >= ImGuiMouseCursor.COUNT)
            mouseCursor = ImGuiMouseCursor.Arrow;
    }

    #endregion

    #region Drag-drop target rendering

    /// <summary>
    /// Draws the highlight rectangle for an item that is a drag-and-drop target, expanding the clipped item
    /// rectangle by the style padding. Canvases canonical <c>ImGui::RenderDragDropTargetRectForItem</c>
    ///.
    /// </summary>
    /// <param name="bb">Item bounds in screen space.</param>
    /// <remarks>
    /// Canonical clips before expanding so a partially visible target remains visible
    ///.; the drawing side reads <c>g.DragDropTargetClipRect</c>,
    /// <c>style.DragDropTargetPadding</c>, and <c>style.DragDropTargetRounding</c>. The drag-and-drop
    /// state machine that populates the clip rectangle is implemented by
    /// <c>BeginDragDropTarget()</c> and <c>AcceptDragDropPayload()</c> in <c>ImGuiDocking.cs</c>.
    /// </remarks>
    internal static void RenderDragDropTargetRectForItem(ImRect bb)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImRect bbDisplay = bb;
        bbDisplay.ClipWith(g.DragDropTargetClipRect);
        bbDisplay.Expand(g.Style.DragDropTargetPadding);
        bool pushClipRect = !window.ClipRect.Contains(bbDisplay);
        if (pushClipRect)
            window.DrawList.PushClipRectFullScreen();
        RenderDragDropTargetRectEx(window.DrawList, bbDisplay, g.Style.DragDropTargetRounding);
        if (pushClipRect)
            window.DrawList.PopClipRect();
    }

    /// <summary>
    /// Draws a drag-and-drop target rectangle on an explicit draw list. Canvases canonical
    /// <c>ImGui::RenderDragDropTargetRectEx</c>., declared in
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="bb">Rectangle to draw.</param>
    /// <param name="rounding">Corner rounding in pixels. Canonical parameter; CONTRACT §4 omitted it and it
    /// is restored with a default so the binding stays call-compatible.</param>
    /// <remarks>
    /// The fill uses <see cref="ImGuiCol.DragDropTargetBg"/> with no corner flags and the outline uses
    /// <see cref="ImGuiCol.DragDropTarget"/> with <c>style.DragDropTargetBorderSize</c>
    /// </remarks>
    internal static void RenderDragDropTargetRectEx(ImDrawList drawList, ImRect bb, float rounding = 0.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        drawList.AddRectFilled(bb.Min, bb.Max, GetColorU32(ImGuiCol.DragDropTargetBg), rounding, ImDrawFlags.None);
        drawList.AddRect(bb.Min, bb.Max, GetColorU32(ImGuiCol.DragDropTarget), rounding, g.Style.DragDropTargetBorderSize);
    }

    #endregion

    #region ImGui Internal Render Helpers

    /// <summary>
    /// Draws an arrow aligned with text, used for example to denote expanded/collapsed state. Canvases
    /// canonical <c>ImGui::RenderArrow</c>., declared in
    /// </summary>
    /// <param name="drawList">Target draw list. Canonical parameter; CONTRACT §4 omitted it and it is
    /// restored because every canonical call site passes <c>window-&gt;DrawList</c>.</param>
    /// <param name="pos">Text-space origin; the arrow is centered one half line height below and right.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="dir">Arrow direction.</param>
    /// <param name="scale">Size multiplier applied to the radius.</param>
    /// <remarks>
    /// Canonical derives the size from <c>draw_list-&gt;_Data-&gt;FontSize</c>; the port's draw list does not
    /// publish its shared data, so <c>g.FontSize</c> is used, which is exactly the value canonical publishes
    /// there. Canonical asserts on <see cref="ImGuiDir.None"/> and
    /// <see cref="ImGuiDir.COUNT"/> and then draws with uninitialized vertices in release builds; the port
    /// asserts and substitutes a zero triangle so the result is deterministic.
    /// </remarks>
    internal static void RenderArrow(ImDrawList drawList, ImVec2 pos, uint col, ImGuiDir dir, float scale = 1.0f)
    {
        ImGuiContext g = GetCurrentContext()!;
        float h = g.FontSize * 1.00f;
        float r = h * 0.40f * scale;
        ImVec2 center = pos + new ImVec2(h * 0.50f, h * 0.50f * scale);

        ImVec2 a, b, c;
        switch (dir)
        {
            case ImGuiDir.Up:
            case ImGuiDir.Down:
                if (dir == ImGuiDir.Up)
                    r = -r;
                a = new ImVec2(+0.000f, +0.750f) * r;
                b = new ImVec2(-0.866f, -0.750f) * r;
                c = new ImVec2(+0.866f, -0.750f) * r;
                break;
            case ImGuiDir.Left:
            case ImGuiDir.Right:
                if (dir == ImGuiDir.Left)
                    r = -r;
                a = new ImVec2(+0.750f, +0.000f) * r;
                b = new ImVec2(-0.750f, +0.866f) * r;
                c = new ImVec2(-0.750f, -0.866f) * r;
                break;
            case ImGuiDir.None:
            case ImGuiDir.COUNT:
            default:
                ImAssert.Check(false, "RenderArrow() called with an invalid direction."); // Canonical IM_ASSERT(0).
                a = b = c = ImVec2.Zero;
                break;
        }
        drawList.AddTriangleFilled(center + a, center + b, center + c, col);
    }

    /// <summary>
    /// Draws the small filled circle used by <c>Bullet</c> and <c>BulletText</c>. Canvases canonical
    /// <c>ImGui::RenderBullet</c>., declared in
    /// </summary>
    /// <param name="drawList">Target draw list. Canonical parameter; CONTRACT §4 omitted it and it is
    /// restored because every canonical call site passes <c>window-&gt;DrawList</c>.</param>
    /// <param name="pos">Circle center.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <remarks>
    /// The radius is one fifth of the font size and the tessellation is a hardcoded three-step threshold,
    /// where zero selects the automatic segment count. The font size comes from
    /// <c>g.FontSize</c> for the same reason as <see cref="RenderArrow"/>.
    /// </remarks>
    internal static void RenderBullet(ImDrawList drawList, ImVec2 pos, uint col)
    {
        float fontSize = GetCurrentContext()!.FontSize;
        drawList.AddCircleFilled(pos, fontSize * 0.20f, col, (fontSize < 22.0f) ? 8 : (fontSize < 40.0f) ? 12 : 0);
    }

    /// <summary>
    /// Draws a crisp three-point checkmark. Canvases canonical <c>ImGui::RenderCheckMark</c>
    ///.
    /// </summary>
    /// <param name="drawList">Target draw list. Canonical parameter; CONTRACT §4 omitted it and it is
    /// restored because every canonical call site passes <c>window-&gt;DrawList</c>.</param>
    /// <param name="pos">Top-left position of the checkmark bounding box.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="sz">Checkmark size in pixels.</param>
    /// <remarks>
    /// Delegates to <see cref="ImDrawList.RenderCheckMark"/>, which already realizes the canonical geometry
    /// (thickness <c>sz / 5</c>, a quarter-thickness inset, and a three-point stroked path).
    /// </remarks>
    internal static void RenderCheckMark(ImDrawList drawList, ImVec2 pos, uint col, float sz)
    {
        drawList.RenderCheckMark(pos, col, sz);
    }

    /// <summary>
    /// Draws an arrow whose tip is at a given position. Canvases canonical
    /// <c>ImGui::RenderArrowPointingAt</c>., declared in
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="pos">Arrow tip.</param>
    /// <param name="halfSz">Half-size; <c>X</c> is the base-to-tip length and <c>Y</c> the half-height.</param>
    /// <param name="direction">Arrow direction.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <remarks>
    /// Unlike <see cref="RenderArrow"/>, an invalid direction silently draws nothing and does not assert
    ///. This method is implemented here rather than delegating to
    /// <see cref="ImDrawList.RenderArrowPointingAt"/> because the port's draw-list convenience method draws a
    /// downward triangle for <see cref="ImGuiDir.None"/> and <see cref="ImGuiDir.COUNT"/> through its
    /// <c>default</c> case, which canonical does not.
    /// </remarks>
    internal static void RenderArrowPointingAt(ImDrawList drawList, ImVec2 pos, ImVec2 halfSz, ImGuiDir direction, uint col)
    {
        switch (direction)
        {
            case ImGuiDir.Left:
                drawList.AddTriangleFilled(new ImVec2(pos.X + halfSz.X, pos.Y - halfSz.Y), new ImVec2(pos.X + halfSz.X, pos.Y + halfSz.Y), pos, col);
                return;
            case ImGuiDir.Right:
                drawList.AddTriangleFilled(new ImVec2(pos.X - halfSz.X, pos.Y + halfSz.Y), new ImVec2(pos.X - halfSz.X, pos.Y - halfSz.Y), pos, col);
                return;
            case ImGuiDir.Up:
                drawList.AddTriangleFilled(new ImVec2(pos.X + halfSz.X, pos.Y + halfSz.Y), new ImVec2(pos.X - halfSz.X, pos.Y + halfSz.Y), pos, col);
                return;
            case ImGuiDir.Down:
                drawList.AddTriangleFilled(new ImVec2(pos.X - halfSz.X, pos.Y - halfSz.Y), new ImVec2(pos.X + halfSz.X, pos.Y - halfSz.Y), pos, col);
                return;
            case ImGuiDir.None:
            case ImGuiDir.COUNT:
            default:
                return;
        }
    }

    /// <summary>
    /// Draws a rounded rectangle filled only between two absolute horizontal coordinates. Canvases canonical
    /// <c>ImGui::RenderRectFilledInRangeH</c>., declared in
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="rect">Full rectangle whose vertical extent is always used.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="fillX0">Left absolute fill coordinate.</param>
    /// <param name="fillX1">Right absolute fill coordinate.</param>
    /// <param name="rounding">Corner rounding in pixels; clamped to half the smaller rectangle extent minus
    /// one.</param>
    /// <remarks>
    /// <para>
    /// Renamed from canonical <c>RenderRectFilledRangeH</c> on 2025/12/04 to make the coordinates absolute
    /// rather than normalized. A zero rounding takes the plain
    /// <c>AddRectFilled</c> early-out; otherwise the two vertical edges are emitted from arc samples and
    /// closed with <c>PathFillConvex</c>.
    /// </para>
    /// <para>
    /// Canonical compares the arc angles against the exact <c>ImAcos01</c> returns, so the fast
    /// <c>PathArcToFast</c> path is selected only for the exact half-quadrant cases; see
    /// <see cref="Acos01"/>.
    /// </para>
    /// </remarks>
    internal static void RenderRectFilledInRangeH(ImDrawList drawList, ImRect rect, uint col, float fillX0, float fillX1, float rounding)
    {
        if (fillX0 > fillX1)
            return;

        ImVec2 p0 = new(fillX0, rect.Min.Y);
        ImVec2 p1 = new(fillX1, rect.Max.Y);
        if (rounding == 0.0f)
        {
            drawList.AddRectFilled(p0, p1, col, 0.0f);
            return;
        }

        // Canonical ImClamp(min(.) - 1.0f, 0.0f, rounding), written with canonical's comparison order so a
        // negative rounding cannot raise an exception the way Math.Clamp would.
        float clampedRounding = MathF.Min((rect.Max.X - rect.Min.X) * 0.5f, (rect.Max.Y - rect.Min.Y) * 0.5f) - 1.0f;
        rounding = clampedRounding < 0.0f ? 0.0f : (clampedRounding > rounding ? rounding : clampedRounding);
        float invRounding = 1.0f / rounding;
        float arc0B = Acos01(1.0f - (p0.X - rect.Min.X) * invRounding);
        float arc0E = Acos01(1.0f - (p1.X - rect.Min.X) * invRounding);
        float halfPi = MathF.PI * 0.5f; // Compared for equality because ImAcos01 returns this exact value.
        float x0 = MathF.Max(p0.X, rect.Min.X + rounding);
        if (arc0B == arc0E)
        {
            drawList.PathLineTo(new ImVec2(x0, p1.Y));
            drawList.PathLineTo(new ImVec2(x0, p0.Y));
        }
        else if (arc0B == 0.0f && arc0E == halfPi)
        {
            drawList.PathArcToFast(new ImVec2(x0, p1.Y - rounding), rounding, 3, 6); // Bottom-left.
            drawList.PathArcToFast(new ImVec2(x0, p0.Y + rounding), rounding, 6, 9); // Top-right.
        }
        else
        {
            drawList.PathArcTo(new ImVec2(x0, p1.Y - rounding), rounding, MathF.PI - arc0E, MathF.PI - arc0B); // Bottom-left.
            drawList.PathArcTo(new ImVec2(x0, p0.Y + rounding), rounding, MathF.PI + arc0B, MathF.PI + arc0E); // Top-right.
        }
        if (p1.X > rect.Min.X + rounding)
        {
            float arc1B = Acos01(1.0f - (rect.Max.X - p1.X) * invRounding);
            float arc1E = Acos01(1.0f - (rect.Max.X - p0.X) * invRounding);
            float x1 = MathF.Min(p1.X, rect.Max.X - rounding);
            if (arc1B == arc1E)
            {
                drawList.PathLineTo(new ImVec2(x1, p0.Y));
                drawList.PathLineTo(new ImVec2(x1, p1.Y));
            }
            else if (arc1B == 0.0f && arc1E == halfPi)
            {
                drawList.PathArcToFast(new ImVec2(x1, p0.Y + rounding), rounding, 9, 12); // Top-right.
                drawList.PathArcToFast(new ImVec2(x1, p1.Y - rounding), rounding, 0, 3);  // Bottom-right.
            }
            else
            {
                drawList.PathArcTo(new ImVec2(x1, p0.Y + rounding), rounding, -arc1E, -arc1B); // Top-right.
                drawList.PathArcTo(new ImVec2(x1, p1.Y - rounding), rounding, +arc1B, +arc1E); // Bottom-right.
            }
        }
        drawList.PathFillConvex(col);
    }

    /// <summary>
    /// Draws the four edge bands and four corner squares that fill a rectangle except for an inner hole.
    /// Canvases canonical <c>ImGui::RenderRectFilledWithHole</c>
    ///.
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="outer">Outer rectangle.</param>
    /// <param name="inner">Inner rectangle describing the hole.</param>
    /// <param name="col">Packed color in canonical ABGR order.</param>
    /// <param name="rounding">Corner rounding in pixels.</param>
    /// <remarks>
    /// Each edge is suppressed when the hole touches that side, and the round-corner flags are masked so
    /// corners shared with a filled adjacent edge stay square. An edge that is filled together with an
    /// adjacent filled edge additionally emits its corner square.
    /// </remarks>
    internal static void RenderRectFilledWithHole(ImDrawList drawList, ImRect outer, ImRect inner, uint col, float rounding)
    {
        bool fillL = inner.Min.X > outer.Min.X;
        bool fillR = inner.Max.X < outer.Max.X;
        bool fillU = inner.Min.Y > outer.Min.Y;
        bool fillD = inner.Max.Y < outer.Max.Y;
        if (fillL)
            drawList.AddRectFilled(new ImVec2(outer.Min.X, inner.Min.Y), new ImVec2(inner.Min.X, inner.Max.Y), col, rounding,
                ImDrawFlags.RoundCornersNone | (fillU ? ImDrawFlags.None : ImDrawFlags.RoundCornersTopLeft) | (fillD ? ImDrawFlags.None : ImDrawFlags.RoundCornersBottomLeft));
        if (fillR)
            drawList.AddRectFilled(new ImVec2(inner.Max.X, inner.Min.Y), new ImVec2(outer.Max.X, inner.Max.Y), col, rounding,
                ImDrawFlags.RoundCornersNone | (fillU ? ImDrawFlags.None : ImDrawFlags.RoundCornersTopRight) | (fillD ? ImDrawFlags.None : ImDrawFlags.RoundCornersBottomRight));
        if (fillU)
            drawList.AddRectFilled(new ImVec2(inner.Min.X, outer.Min.Y), new ImVec2(inner.Max.X, inner.Min.Y), col, rounding,
                ImDrawFlags.RoundCornersNone | (fillL ? ImDrawFlags.None : ImDrawFlags.RoundCornersTopLeft) | (fillR ? ImDrawFlags.None : ImDrawFlags.RoundCornersTopRight));
        if (fillD)
            drawList.AddRectFilled(new ImVec2(inner.Min.X, inner.Max.Y), new ImVec2(inner.Max.X, outer.Max.Y), col, rounding,
                ImDrawFlags.RoundCornersNone | (fillL ? ImDrawFlags.None : ImDrawFlags.RoundCornersBottomLeft) | (fillR ? ImDrawFlags.None : ImDrawFlags.RoundCornersBottomRight));
        if (fillL && fillU)
            drawList.AddRectFilled(new ImVec2(outer.Min.X, outer.Min.Y), new ImVec2(inner.Min.X, inner.Min.Y), col, rounding, ImDrawFlags.RoundCornersTopLeft);
        if (fillR && fillU)
            drawList.AddRectFilled(new ImVec2(inner.Max.X, outer.Min.Y), new ImVec2(outer.Max.X, inner.Min.Y), col, rounding, ImDrawFlags.RoundCornersTopRight);
        if (fillL && fillD)
            drawList.AddRectFilled(new ImVec2(outer.Min.X, inner.Max.Y), new ImVec2(inner.Min.X, outer.Max.Y), col, rounding, ImDrawFlags.RoundCornersBottomLeft);
        if (fillR && fillD)
            drawList.AddRectFilled(new ImVec2(inner.Max.X, inner.Max.Y), new ImVec2(outer.Max.X, outer.Max.Y), col, rounding, ImDrawFlags.RoundCornersBottomRight);
    }

    /// <summary>
    /// Draws a color rectangle over an alpha checkerboard when its alpha is not opaque. Canvases canonical
    /// <c>ImGui::RenderColorRectWithAlphaCheckerboard</c>
    ///.
    /// </summary>
    /// <param name="drawList">Target draw list.</param>
    /// <param name="pMin">Rectangle minimum.</param>
    /// <param name="pMax">Rectangle maximum.</param>
    /// <param name="fillCol">Packed color in canonical ABGR order; a non-opaque alpha enables the
    /// checkerboard.</param>
    /// <param name="gridStep">Checker cell size in pixels.</param>
    /// <param name="gridOff">Checker grid offset, which shifts the alternating pattern.</param>
    /// <param name="rounding">Corner rounding in pixels.</param>
    /// <param name="flags">Corner flags; <see cref="ImDrawFlags.None"/> selects all corners.</param>
    /// <remarks>
    /// <para>
    /// The checker pattern alternates by row parity and advances two grid steps per cell within a row
    ///. Canonical documents this helper as brittle when rounding overlaps more
    /// than one cell. <paramref name="pMin"/> must not exceed <paramref name="pMax"/> on either axis, which
    /// canonical also assumes.
    /// </para>
    /// <para>
    /// Canonical derives the two background grays with <c>ImAlphaBlendColors</c> (,
    ///.); that helper belongs to the Chunk 9 Helpers category and is
    /// not yet ported, so <see cref="BlendAlphaOverOpaque"/> realizes the same arithmetic locally.
    /// </para>
    /// </remarks>
    internal static void RenderColorRectWithAlphaCheckerboard(ImDrawList drawList, ImVec2 pMin, ImVec2 pMax, uint fillCol, float gridStep, ImVec2 gridOff, float rounding = 0.0f, ImDrawFlags flags = 0)
    {
        if ((flags & ImDrawFlags.RoundCornersMask_) == 0)
            flags = ImDrawFlags.RoundCornersDefault_;
        if (((fillCol >> 24) & 0xFF) < 0xFF)
        {
            uint colBg1 = GetColorU32(BlendAlphaOverOpaque(0xFF808080u, fillCol));
            uint colBg2 = GetColorU32(BlendAlphaOverOpaque(0xFFCCCCCCu, fillCol));
            drawList.AddRectFilled(pMin, pMax, colBg1, rounding, flags);

            int yi = 0;
            for (float y = pMin.Y + gridOff.Y; y < pMax.Y; y += gridStep, yi++)
            {
                float y1 = Math.Clamp(y, pMin.Y, pMax.Y);
                float y2 = MathF.Min(y + gridStep, pMax.Y);
                if (y2 <= y1)
                    continue;
                for (float x = pMin.X + gridOff.X + ((yi ^ 1) & 1) * gridStep; x < pMax.X; x += gridStep * 2.0f)
                {
                    float x1 = Math.Clamp(x, pMin.X, pMax.X);
                    float x2 = MathF.Min(x + gridStep, pMax.X);
                    if (x2 <= x1)
                        continue;
                    ImDrawFlags cellFlags = ImDrawFlags.RoundCornersNone;
                    if (y1 <= pMin.Y)
                    {
                        if (x1 <= pMin.X)
                            cellFlags |= ImDrawFlags.RoundCornersTopLeft;
                        if (x2 >= pMax.X)
                            cellFlags |= ImDrawFlags.RoundCornersTopRight;
                    }
                    if (y2 >= pMax.Y)
                    {
                        if (x1 <= pMin.X)
                            cellFlags |= ImDrawFlags.RoundCornersBottomLeft;
                        if (x2 >= pMax.X)
                            cellFlags |= ImDrawFlags.RoundCornersBottomRight;
                    }

                    // Combine flags: a corner is rounded only where both the cell and the whole rectangle allow it.
                    cellFlags = (flags == ImDrawFlags.RoundCornersNone || cellFlags == ImDrawFlags.RoundCornersNone)
                        ? ImDrawFlags.RoundCornersNone
                        : (cellFlags & flags);
                    drawList.AddRectFilled(new ImVec2(x1, y1), new ImVec2(x2, y2), colBg2, rounding, cellFlags);
                }
            }
        }
        else
        {
            drawList.AddRectFilled(pMin, pMax, fillCol, rounding, flags);
        }
    }

    /// <summary>
    /// Arccosine clamped to the unit interval, used by <see cref="RenderRectFilledInRangeH"/>.
    /// </summary>
    /// <param name="x">Value in the unit interval, or outside it.</param>
    /// <returns>The clamped arccosine in radians.</returns>
    /// <remarks>
    /// Ports the canonical static helper <c>ImAcos01</c>
    ///.: values at or below zero return a quarter turn and
    /// values at or above one return zero, so callers may compare the result for exact equality. The helper
    /// is file-local in canonical and is therefore realized privately here rather than as a shared member.
    /// </remarks>
    private static float Acos01(float x)
    {
        if (x <= 0.0f)
            return MathF.PI * 0.5f;
        if (x >= 1.0f)
            return 0.0f;
        return MathF.Acos(x);
    }

    /// <summary>
    /// Blends an opaque background color with a foreground color whose alpha selects the blend factor,
    /// returning an opaque canonical ABGR value.
    /// </summary>
    /// <param name="colA">Opaque background color in canonical ABGR order.</param>
    /// <param name="colB">Foreground color whose alpha channel is the blend factor.</param>
    /// <returns>The opaque blended color.</returns>
    /// <remarks>
    /// Realizes canonical <c>ImAlphaBlendColors</c> (, declared in
    /// ) with the canonical integer lerp and truncation, so the checkout pattern
    /// matches pixel for pixel. The canonical helper belongs to the Chunk 9 Helpers category and is not yet
    /// ported; this private realization is replaced by a call to it once that category lands.
    /// </remarks>
    private static uint BlendAlphaOverOpaque(uint colA, uint colB)
    {
        // Channel values are widened to int before differencing so the interpolation cannot wrap on a
        // decreasing channel.
        int aR = (int)(colA & 0xFF), aG = (int)((colA >> 8) & 0xFF), aB = (int)((colA >> 16) & 0xFF);
        int bR = (int)(colB & 0xFF), bG = (int)((colB >> 8) & 0xFF), bB = (int)((colB >> 16) & 0xFF);
        float t = (int)((colB >> 24) & 0xFF) / 255.0f;
        int r = (int)(aR + (bR - aR) * t);
        int g = (int)(aG + (bG - aG) * t);
        int b = (int)(aB + (bB - aB) * t);
        return (uint)(r | (g << 8) | (b << 16) | (0xFF << 24));
    }

    /// <summary>
    /// Blends an opaque background color with a foreground color whose alpha selects the blend factor.
    /// Ports canonical <c>ImAlphaBlendColors</c> (.).
    /// </summary>
    public static uint ImAlphaBlendColors(uint colA, uint colB) => BlendAlphaOverOpaque(colA, colB);

    #endregion

    #endregion


    #region SECTION: BASIC WIDGETS

    #region [SECTION] Layout accessors

    /// <summary>
    /// Returns the height of one text line. Mirrors canonical <c>ImGui::GetTextLineHeight</c>
    /// </summary>
    /// <returns><c>g.FontSize</c>, the current scaled font size in pixels.</returns>
    /// <remarks>
    /// This is the canonical public text-height accessor; <see cref="GetTextLineHeightWithSpacing"/>
    /// adds the vertical item spacing on top of it.
    /// </remarks>
    public static float GetTextLineHeight()
    {
        return GetCurrentContext()!.FontSize;
    }

    /// <summary>
    /// Returns the height of one text line plus the vertical item spacing, the distance between two
    /// consecutive text rows. (, declared in
    /// </summary>
    /// <returns><c>g.FontSize + g.Style.ItemSpacing.y</c>.</returns>
    /// <remarks>
    /// Several list and combo widgets size themselves from this accessor. Canonical declares it in
    ///  alongside <see cref="GetTextLineHeight"/>; the port exposes it publicly for
    /// the same reason, so callers such as the demo do not have to re-derive the formula.
    /// </remarks>
    public static float GetTextLineHeightWithSpacing()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.FontSize + g.Style.ItemSpacing.Y;
    }

    /// <summary>
    /// Returns the height of a framed widget: one text line plus the vertical frame padding on both
    /// sides. (.)
    /// </summary>
    /// <returns><c>g.FontSize + g.Style.FramePadding.y * 2.0f</c>.</returns>
    /// <remarks>
    /// Canonical declares this accessor.; buttons, checkboxes, radio buttons,
    /// arrow buttons, and combo headers size themselves from it, and the demo needs the same formula,
    /// so the port exposes it publicly rather than having callers re-derive it.
    /// </remarks>
    public static float GetFrameHeight()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.FontSize + g.Style.FramePadding.Y * 2.0f;
    }

    /// <summary>
    /// Returns the height of a framed widget plus the vertical item spacing. Mirrors canonical
    /// <c>ImGui::GetFrameHeightWithSpacing</c>.
    /// </summary>
    /// <returns><c>g.FontSize + g.Style.FramePadding.y * 2.0f + g.Style.ItemSpacing.y</c>.</returns>
    /// <remarks>
    /// This is the canonical public accessor used to advance one framed row; it combines
    /// <see cref="GetFrameHeight"/> with <see cref="ImGuiStyle.ItemSpacing"/>.
    /// </remarks>
    public static float GetFrameHeightWithSpacing()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.FontSize + g.Style.FramePadding.Y * 2.0f + g.Style.ItemSpacing.Y;
    }

    #endregion

    #region [SECTION] Widgets: Main (Button, Image, Checkbox, RadioButton, ProgressBar, Bullet, etc.)

    /// <summary>
    /// Submits a labeled button with explicit flags and size.
    /// (.)
    /// </summary>
    /// <param name="label">The display label; its ID seeds from the current window's ID stack.</param>
    /// <param name="sizeArg">The requested size; zero components fall back to the label size plus frame
    /// padding.</param>
    /// <param name="flags">The button behavior flags.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: skip check, ID and label measurement, baseline adjustment for
    /// <c>ImGuiButtonFlags_AlignTextBaseLine</c>, size resolution, <c>ItemSize</c>, <c>ItemAdd</c>,
    /// <c>ButtonBehavior</c>, background color selection, nav cursor, frame, and clipped label text.
    /// </para>
    /// <para>
    /// Canonical passes the private <c>ImGuiButtonFlags_AlignTextBaseLine</c> bit through the same
    /// <c>ImGuiButtonFlags</c> parameter; this port declares private button bits in
    /// <see cref="ImGuiButtonFlagsPrivate"/> and the caller casts the folded value.
    /// </para>
    /// <para>
    /// The canonical popup auto-close block is commented out. and is
    /// therefore not ported, matching canonical behavior. The test-engine instrumentation
    /// (<c>IMGUI_TEST_ENGINE_ITEM_INFO</c>) is compiled out of the default build and is not ported; the
    /// logging decoration (<c>LogSetNextTextDecoration</c>) is applied when logging is active.
    /// </para>
    /// </remarks>
    internal static bool ButtonEx(string label, ImVec2 sizeArg, ImGuiButtonFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd);

        ImVec2 pos = window.DC.CursorPos;
        if ((flags & (ImGuiButtonFlags)(int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_AlignTextBaseLine) != 0
            && style.FramePadding.Y < window.DC.CurrLineTextBaseOffset)
            pos.Y += window.DC.CurrLineTextBaseOffset - style.FramePadding.Y;
        ImVec2 size = CalcItemSize(sizeArg, labelSize.X + style.FramePadding.X * 2.0f, labelSize.Y + style.FramePadding.Y * 2.0f);

        ImRect bb = new(pos, pos + size);
        ItemSize(size, style.FramePadding.Y);
        if (!ItemAdd(bb, id))
            return false;

        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held, flags);

        uint col = GetColorU32((held && hovered) ? ImGuiCol.ButtonActive : hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
        RenderNavCursor(bb, id);
        RenderFrame(bb.Min, bb.Max, col, true, style.FrameRounding);

        if (g.LogEnabled)
            LogSetNextTextDecoration("[", "]");

        RenderTextClipped(bb.Min + style.FramePadding, bb.Max - style.FramePadding, label, labelEnd, labelSize, style.ButtonTextAlign, bb);
        return pressed;
    }

    /// <summary>
    /// Submits a labeled button. (, declared in
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="size">The requested size; the default <c>(0, 0)</c> fits the label plus frame padding.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>A pure forwarder to <see cref="ButtonEx"/> with no flags, exactly as canonical.</remarks>
    public static bool Button(string label, ImVec2 size = default)
        => ButtonEx(label, size, 0);

    /// <summary>
    /// Submits a button that fits within a text line by temporarily zeroing the vertical frame padding.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>
    /// The frame padding is saved, zeroed, restored around the <see cref="ButtonEx"/> call, and the
    /// call passes the private <c>AlignTextBaseLine</c> flag so the text baseline stays aligned.
    /// </remarks>
    public static bool SmallButton(string label)
    {
        ImGuiContext g = GetCurrentContext()!;
        float backupPaddingY = g.Style.FramePadding.Y;
        g.Style.FramePadding = new ImVec2(g.Style.FramePadding.X, 0.0f);
        bool pressed = ButtonEx(label, default, (ImGuiButtonFlags)(int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_AlignTextBaseLine);
        g.Style.FramePadding = new ImVec2(g.Style.FramePadding.X, backupPaddingY);
        return pressed;
    }

    /// <summary>
    /// Submits an invisible, interactive item sized from the cursor position.
    /// (.)
    /// </summary>
    /// <param name="strId">The identifier string hashed under the window's ID stack.</param>
    /// <param name="size">The requested size; a zero component is replaced by <c>-FLT_MIN</c> so
    /// <c>CalcItemSize</c> resolves it to the remaining available space.</param>
    /// <param name="flags">The button behavior flags; <c>EnableNav</c> re-enables navigation.</param>
    /// <returns>True on the frame the item is pressed.</returns>
    /// <remarks>
    /// Navigation is disabled unless <see cref="ImGuiButtonFlags.EnableNav"/> is set, exactly as
    /// canonical. The only drawing is the navigation cursor highlight.
    /// </remarks>
    public static bool InvisibleButton(string strId, ImVec2 size, ImGuiButtonFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        // Canonical FLT_MIN (1.17549435e-38f) is the smallest positive normal float; C# float.Epsilon is
        // a subnormal, so the literal is used to preserve the canonical sentinel.
        ImVec2 sizeArg = new(
            size.X != 0.0f ? size.X : -1.17549435e-38f,
            size.Y != 0.0f ? size.Y : -1.17549435e-38f);
        ImVec2 resolved = CalcItemSize(sizeArg, 0.0f, 0.0f);

        uint id = GetID(strId);
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + resolved);
        ItemSize(resolved);
        if (!ItemAdd(bb, id, null, (flags & ImGuiButtonFlags.EnableNav) != 0 ? ImGuiItemFlags.None : ImGuiItemFlags.NoNav))
            return false;

        bool pressed = ButtonBehavior(bb, id, out _, out _, flags);
        RenderNavCursor(bb, id);
        return pressed;
    }

    /// <summary>
    /// Submits an arrow button with an explicit size and flags.
    /// (; the canonical definition has no header
    /// declaration, so the port marks it <c>internal</c> for reuse by the tab scrolling buttons)
    /// </summary>
    /// <param name="strId">The identifier string hashed under the window's ID stack.</param>
    /// <param name="dir">The arrow direction.</param>
    /// <param name="size">The button size.</param>
    /// <param name="flags">The button behavior flags.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>
    /// The layout baseline is only aligned when the height reaches a full frame height, matching
    /// canonical. The arrow is centered at the larger of zero and half the leftover size on each axis.
    /// </remarks>
    internal static bool ArrowButtonEx(string strId, ImGuiDir dir, ImVec2 size, ImGuiButtonFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        uint id = GetID(strId);
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + size);
        float defaultSize = GetFrameHeight();
        ItemSize(size, (size.Y >= defaultSize) ? g.Style.FramePadding.Y : -1.0f);
        if (!ItemAdd(bb, id))
            return false;

        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held, flags);

        uint bgCol = GetColorU32((held && hovered) ? ImGuiCol.ButtonActive : hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
        uint textCol = GetColorU32(ImGuiCol.Text);
        RenderNavCursor(bb, id);
        RenderFrame(bb.Min, bb.Max, bgCol, true, g.Style.FrameRounding);
        RenderArrow(
            window.DrawList,
            bb.Min + new ImVec2(MathF.Max(0.0f, (size.X - g.FontSize) * 0.5f), MathF.Max(0.0f, (size.Y - g.FontSize) * 0.5f)),
            textCol,
            dir);
        return pressed;
    }

    /// <summary>
    /// Submits a square arrow button sized to one frame height.
    /// (.)
    /// </summary>
    /// <param name="strId">The identifier string hashed under the window's ID stack.</param>
    /// <param name="dir">The arrow direction.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    public static bool ArrowButton(string strId, ImGuiDir dir)
    {
        float sz = GetFrameHeight();
        return ArrowButtonEx(strId, dir, new ImVec2(sz, sz), 0);
    }

    /// <summary>
    /// Submits the small window close button. (,
    ///.)
    /// </summary>
    /// <param name="id">The item identifier.</param>
    /// <param name="pos">The top-left corner of the square button, which is one font size wide and tall.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>
    /// <para>
    /// The hit-testing rectangle is shrunk by a quarter when the button covers an abnormally large share of
    /// the visible window area, so the window can still be grabbed next to the button. The button remains
    /// interactive while clipped, allowing a mechanical close sequence. Two diagonal lines form the cross.
    /// </para>
    /// <para>
    /// Canonical declares this as <c>bool CloseButton(ImGuiID id, const ImVec2&amp; pos)</c> with no radius
    /// parameter, so the canonical signature is retained rather than the coordination request's
    /// three-parameter <c>void</c> form. The length is one font size and the thickness is the integer main
    /// style scale.
    /// </para>
    /// <para>
    /// Callers include the tab bar and the docking node layer. The
    /// <c>CollapsingHeader(label, ref bool pVisible,.)</c> close button and the window title-bar close
    /// button are the immediate in-chunk consumers.
    /// </para>
    /// </remarks>
    internal static bool CloseButton(uint id, ImVec2 pos)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        ImRect bb = new(pos, pos + new ImVec2(g.FontSize, g.FontSize));
        ImRect bbInteract = bb;
        // Tweak 1: shrink the hit area when the button covers an abnormally large part of the window, so
        // that the window can still be moved by grabbing next to it.
        float areaToVisibleRatio = window.OuterRectClipped.GetArea() / bb.GetArea();
        if (areaToVisibleRatio < 1.5f)
        {
            ImVec2 expandSize = bbInteract.GetSize() * -0.25f;
            bbInteract.Expand(new ImVec2((float)(int)expandSize.X, (float)(int)expandSize.Y));
        }

        // Tweak 2: interaction is intentionally allowed while clipped so a mechanical Alt,Right,Activate
        // sequence can always close a window.
        bool isClipped = !ItemAdd(bbInteract, id);

        bool pressed = ButtonBehavior(bbInteract, id, out bool hovered, out bool held);
        if (isClipped)
            return pressed;

        uint bgCol = GetColorU32(held ? ImGuiCol.ButtonActive : ImGuiCol.ButtonHovered);
        if (hovered)
            window.DrawList.AddRectFilled(bb.Min, bb.Max, bgCol);
        RenderNavCursor(bb, id, ImGuiNavRenderCursorFlags.Compact);
        uint crossCol = GetColorU32(ImGuiCol.Text);
        ImVec2 crossCenter = bb.GetCenter() - new ImVec2(0.5f, 0.5f);
        float crossExtent = g.FontSize * 0.5f * 0.7071f - 1.0f;
        float crossThickness = 1.0f * (float)(int)g.Style._MainScale;
        window.DrawList.AddLine(
            crossCenter + new ImVec2(+crossExtent, +crossExtent),
            crossCenter + new ImVec2(-crossExtent, -crossExtent),
            crossCol,
            crossThickness);
        window.DrawList.AddLine(
            crossCenter + new ImVec2(+crossExtent, -crossExtent),
            crossCenter + new ImVec2(-crossExtent, +crossExtent),
            crossCol,
            crossThickness);
        return pressed;
    }

    /// <summary>
    /// Submits a non-interactive image with an optional background and tint.
    /// (.)
    /// </summary>
    /// <param name="texRef">The texture to draw.</param>
    /// <param name="imageSize">The image size; the border padding is added around it.</param>
    /// <param name="uv0">The top-left texture coordinate; <see langword="null"/> selects canonical
    /// <c>(0, 0)</c>.</param>
    /// <param name="uv1">The bottom-right texture coordinate; <see langword="null"/> selects canonical
    /// <c>(1, 1)</c>.</param>
    /// <param name="bgCol">The background color; <see langword="null"/> selects canonical transparent
    /// black.</param>
    /// <param name="tintCol">The image tint; <see langword="null"/> selects canonical opaque white.</param>
    /// <remarks>
    /// The item has no identifier (ID zero), so it neither registers navigation nor participates in
    /// hover testing. The image is rounded when <c>style.ImageRounding &gt; 0</c>; the border is drawn
    /// when <c>style.ImageBorderSize &gt; 0</c>. Nullable optional parameters reproduce the canonical
    /// default arguments, which C# cannot express for a struct with non-zero defaults.
    /// </remarks>
    public static void ImageWithBg(
        ImTextureRef texRef,
        ImVec2 imageSize,
        ImVec2? uv0 = null,
        ImVec2? uv1 = null,
        ImVec4? bgCol = null,
        ImVec4? tintCol = null)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImVec2 uv0Value = uv0 ?? new ImVec2(0.0f, 0.0f);
        ImVec2 uv1Value = uv1 ?? new ImVec2(1.0f, 1.0f);
        ImVec4 bgColor = bgCol ?? new ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
        ImVec4 tintColor = tintCol ?? new ImVec4(1.0f, 1.0f, 1.0f, 1.0f);

        ImVec2 padding = new(g.Style.ImageBorderSize, g.Style.ImageBorderSize);
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + imageSize + padding * 2.0f);
        ItemSize(bb);
        if (!ItemAdd(bb, 0))
            return;

        float rounding = g.Style.ImageRounding;
        if (bgColor.W > 0.0f)
            window.DrawList.AddRectFilled(bb.Min + padding, bb.Max - padding, GetColorU32(bgColor), rounding);
        if (rounding > 0.0f)
            window.DrawList.AddImageRounded(texRef, bb.Min + padding, bb.Max - padding, uv0Value, uv1Value, GetColorU32(tintColor), rounding);
        else
            window.DrawList.AddImage(texRef, bb.Min + padding, bb.Max - padding, uv0Value, uv1Value, GetColorU32(tintColor));
        if (g.Style.ImageBorderSize > 0.0f)
            window.DrawList.AddRect(bb.Min, bb.Max, GetColorU32(ImGuiCol.Border), rounding, g.Style.ImageBorderSize);
    }

    /// <summary>
    /// Submits a non-interactive image with canonical default background and tint.
    /// (.)
    /// </summary>
    /// <param name="texRef">The texture to draw.</param>
    /// <param name="imageSize">The image size.</param>
    /// <param name="uv0">The top-left texture coordinate; <see langword="null"/> selects <c>(0, 0)</c>.</param>
    /// <param name="uv1">The bottom-right texture coordinate; <see langword="null"/> selects <c>(1, 1)</c>.</param>
    /// <remarks>A pure forwarder to <see cref="ImageWithBg"/>, exactly as canonical.</remarks>
    public static void Image(ImTextureRef texRef, ImVec2 imageSize, ImVec2? uv0 = null, ImVec2? uv1 = null)
        => ImageWithBg(texRef, imageSize, uv0, uv1);

    /// <summary>
    /// Submits a non-interactive image using the obsolete tint and border color parameters.
    /// (. under
    /// <c>IMGUI_DISABLE_OBSOLETE_FUNCTIONS</c>)
    /// </summary>
    /// <param name="texRef">The texture to draw.</param>
    /// <param name="imageSize">The image size.</param>
    /// <param name="uv0">The top-left texture coordinate.</param>
    /// <param name="uv1">The bottom-right texture coordinate.</param>
    /// <param name="tintCol">The image tint, passed through to <see cref="ImageWithBg"/>.</param>
    /// <param name="borderCol">The legacy border color, pushed as <c>ImGuiCol_Border</c>; a positive alpha
    /// forces a visible border of at least one pixel.</param>
    /// <remarks>
    /// This overload is the contract's bound <c>Image</c> signature and remains live in canonical while
    /// obsolete functions are enabled; it is kept for canonical parity. The simple form carries no tint
    /// or border color.
    /// </remarks>
    public static void Image(ImTextureRef texRef, ImVec2 imageSize, ImVec2 uv0, ImVec2 uv1, ImVec4 tintCol, ImVec4 borderCol)
    {
        ImGuiContext g = GetCurrentContext()!;
        PushStyleVar(ImGuiStyleVar.ImageBorderSize, (borderCol.W > 0.0f) ? MathF.Max(1.0f, g.Style.ImageBorderSize) : 0.0f);
        PushStyleColor(ImGuiCol.Border, borderCol);
        ImageWithBg(texRef, imageSize, uv0, uv1, new ImVec4(0.0f, 0.0f, 0.0f, 0.0f), tintCol);
        PopStyleColor();
        PopStyleVar();
    }

    /// <summary>
    /// Submits an image button with an explicit identifier and flags.
    /// (.)
    /// </summary>
    /// <param name="id">The item identifier.</param>
    /// <param name="texRef">The texture to draw.</param>
    /// <param name="imageSize">The image size; <c>style.FramePadding</c> is added around it.</param>
    /// <param name="uv0">The top-left texture coordinate.</param>
    /// <param name="uv1">The bottom-right texture coordinate.</param>
    /// <param name="bgCol">The inner background color; drawn only when its alpha is positive.</param>
    /// <param name="tintCol">The image tint.</param>
    /// <param name="flags">The button behavior flags.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>
    /// The image rounding shrinks the frame rounding by the larger padding axis and is clamped below by
    /// <c>style.ImageRounding</c>. Unlike canonical <c>ButtonEx</c>, this function emits no test-engine
    /// item info.
    /// </remarks>
    internal static bool ImageButtonEx(
        uint id,
        ImTextureRef texRef,
        ImVec2 imageSize,
        ImVec2 uv0,
        ImVec2 uv1,
        ImVec4 bgCol,
        ImVec4 tintCol,
        ImGuiButtonFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImVec2 padding = g.Style.FramePadding;
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + imageSize + padding * 2.0f);
        ItemSize(bb);
        if (!ItemAdd(bb, id))
            return false;

        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held, flags);

        uint col = GetColorU32((held && hovered) ? ImGuiCol.ButtonActive : hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
        RenderNavCursor(bb, id);
        RenderFrame(bb.Min, bb.Max, col, true, g.Style.FrameRounding);
        if (bgCol.W > 0.0f)
            window.DrawList.AddRectFilled(bb.Min + padding, bb.Max - padding, GetColorU32(bgCol));
        float imageRounding = MathF.Max(g.Style.FrameRounding - MathF.Max(padding.X, padding.Y), g.Style.ImageRounding);
        if (imageRounding > 0.0f)
            window.DrawList.AddImageRounded(texRef, bb.Min + padding, bb.Max - padding, uv0, uv1, GetColorU32(tintCol), imageRounding);
        else
            window.DrawList.AddImage(texRef, bb.Min + padding, bb.Max - padding, uv0, uv1, GetColorU32(tintCol));
        return pressed;
    }

    /// <summary>
    /// Submits an image button identified by a string. (,
    ///.)
    /// </summary>
    /// <param name="strId">The identifier string hashed under the window's ID stack.</param>
    /// <param name="texRef">The texture to draw.</param>
    /// <param name="imageSize">The image size.</param>
    /// <param name="uv0">The top-left texture coordinate; <see langword="null"/> selects <c>(0, 0)</c>.</param>
    /// <param name="uv1">The bottom-right texture coordinate; <see langword="null"/> selects <c>(1, 1)</c>.</param>
    /// <param name="bgCol">The inner background color; <see langword="null"/> selects transparent black.</param>
    /// <param name="tintCol">The image tint; <see langword="null"/> selects opaque white.</param>
    /// <returns>True on the frame the button is pressed.</returns>
    /// <remarks>
    /// The item is never identified by the texture, unlike the legacy overload that canonical keeps
    /// commented out. and that must not be ported. Nullable optional
    /// parameters reproduce the canonical default arguments.
    /// </remarks>
    public static bool ImageButton(
        string strId,
        ImTextureRef texRef,
        ImVec2 imageSize,
        ImVec2? uv0 = null,
        ImVec2? uv1 = null,
        ImVec4? bgCol = null,
        ImVec4? tintCol = null)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;

        ImVec2 uv0Value = uv0 ?? new ImVec2(0.0f, 0.0f);
        ImVec2 uv1Value = uv1 ?? new ImVec2(1.0f, 1.0f);
        ImVec4 bgColor = bgCol ?? new ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
        ImVec4 tintColor = tintCol ?? new ImVec4(1.0f, 1.0f, 1.0f, 1.0f);
        return ImageButtonEx(GetID(strId), texRef, imageSize, uv0Value, uv1Value, bgColor, tintColor);
    }

    /// <summary>
    /// Submits a checkbox bound to a caller-owned boolean.
    /// (.)
    /// </summary>
    /// <param name="label">The display label; its ID seeds from the current window's ID stack.</param>
    /// <param name="v">The value to display and toggle in place.</param>
    /// <returns>True when the value changed or the box was pressed.</returns>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: measure label, resolve the check square from
    /// <see cref="GetFrameHeight"/>, <c>ItemSize</c>, <c>ItemAdd</c>, <c>ButtonBehavior</c>, toggle, write
    /// the caller value and <c>MarkItemEdited</c>, then render the box (or the mixed-value dash) and the
    /// label.
    /// </para>
    /// <para>
    /// The mixed-value dash reads the private <c>ImGuiItemFlags_MixedValue</c> bit, set by
    /// <see cref="CheckboxFlagsT{T}"/> for a partially-selected flag set.
    /// </para>
    /// <para>
    /// The logging text is emitted when logging is active. Test-engine instrumentation is not ported.
    /// </para>
    /// </remarks>
    public static bool Checkbox(string label, ref bool v)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd);

        float squareSize = GetFrameHeight();
        ImVec2 pos = window.DC.CursorPos;
        ImRect totalBb = new(
            pos,
            pos + new ImVec2(
                squareSize + (labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f),
                labelSize.Y + style.FramePadding.Y * 2.0f));
        ItemSize(totalBb, style.FramePadding.Y);
        bool isVisible = ItemAdd(totalBb, id);
        bool isMultiSelect = (g.LastItemData.ItemFlags & (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_IsMultiSelect) != 0;
        if (!isVisible)
        {
            // An unclipped checkbox still participates in box-selection when the box-select unclip
            // rectangle overlaps it.
            if (!isMultiSelect || !g.BoxSelectState.UnclipMode || !g.BoxSelectState.UnclipRect.Overlaps(totalBb))
                return false;
        }

        // Multi-select header. Canonical passes a null button-flag pointer because the checkbox does not
        // forward the flags to ButtonBehavior, so the discarded local reproduces that path.
        bool checkedValue = v;
        if (isMultiSelect)
        {
            int unusedButtonFlags = 0;
            MultiSelectItemHeader(id, ref checkedValue, ref unusedButtonFlags);
        }

        bool pressed = ButtonBehavior(totalBb, id, out bool hovered, out bool held);
        // Multi-select footer; outside a scope the plain toggle path applies.
        if (isMultiSelect)
            MultiSelectItemFooter(id, ref checkedValue, ref pressed, ImGuiMultiSelectFlags.CheckboxMode_);
        else if (pressed)
            checkedValue = !checkedValue;

        if (v != checkedValue)
        {
            v = checkedValue;
            pressed = true;
            MarkItemEdited(id);
        }

        ImRect checkBb = new(pos, pos + new ImVec2(squareSize, squareSize));
        bool mixedValue = (g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0;
        if (isVisible)
        {
            RenderNavCursor(totalBb, id);
            uint bgCol = GetColorU32(
                (held && hovered) ? ImGuiCol.FrameBgActive
                : hovered ? ImGuiCol.FrameBgHovered
                : (mixedValue || checkedValue) ? ImGuiCol.CheckboxSelectedBg
                : ImGuiCol.FrameBg);
            uint checkCol = GetColorU32(ImGuiCol.CheckMark);
            RenderFrame(checkBb.Min, checkBb.Max, bgCol, true, style.FrameRounding);
            if (mixedValue)
            {
                ImVec2 pad = new(
                    MathF.Max(1.0f, (float)(int)(squareSize / 3.6f)),
                    MathF.Max(1.0f, (float)(int)(squareSize / 3.6f)));
                window.DrawList.AddRectFilled(checkBb.Min + pad, checkBb.Max - pad, checkCol, style.FrameRounding);
            }
            else if (v)
            {
                float pad = MathF.Max(1.0f, (float)(int)(squareSize / 6.0f));
                RenderCheckMark(window.DrawList, checkBb.Min + new ImVec2(pad, pad), checkCol, squareSize - pad * 2.0f);
            }
        }

        ImVec2 labelPos = new(checkBb.Max.X + style.ItemInnerSpacing.X, checkBb.Min.Y + style.FramePadding.Y);
        if (g.LogEnabled)
            LogRenderedText(labelPos, mixedValue ? "[~]" : v ? "[x]" : "[ ]");
        if (isVisible && labelSize.X > 0.0f)
            RenderText(labelPos, label, labelEnd, false);
        return pressed;
    }

    /// <summary>
    /// Implements the templated checkbox-for-flags behavior shared by the four public overloads.
    /// </summary>
    /// <typeparam name="T">The integer flags type; canonical instantiated only <c>int</c>,
    /// <c>unsigned int</c>, <c>ImS64</c>, and <c>ImU64</c>.</typeparam>
    /// <param name="label">The display label.</param>
    /// <param name="flags">The flag word updated in place.</param>
    /// <param name="flagsValue">The bit group this checkbox represents.</param>
    /// <returns>True when the flag word changed or the box was pressed.</returns>
    /// <remarks>
    /// A partial selection (some but not all bits set) submits the checkbox with the private
    /// <c>ImGuiItemFlags_MixedValue</c> bit queued, which renders the tristate dash. The all-on check
    /// uses the neutral element of the bitwise AND for the generic comparison to zero.
    /// </remarks>
    private static bool CheckboxFlagsT<T>(string label, ref T flags, T flagsValue)
        where T : unmanaged, System.Numerics.IBinaryInteger<T>
    {
        bool allOn = (flags & flagsValue) == flagsValue;
        bool anyOn = (flags & flagsValue) != T.Zero;
        bool pressed;
        if (!allOn && anyOn)
        {
            ImGuiContext g = GetCurrentContext()!;
            g.NextItemData.ItemFlagsSet |= (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_MixedValue;
            pressed = Checkbox(label, ref allOn);
        }
        else
        {
            pressed = Checkbox(label, ref allOn);
        }
        if (pressed)
        {
            if (allOn)
                flags |= flagsValue;
            else
                flags &= ~flagsValue;
        }
        return pressed;
    }

    /// <summary>
    /// Submits a checkbox that toggles a signed 32-bit flag group.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="flags">The flag word updated in place.</param>
    /// <param name="flagsValue">The bit group this checkbox represents.</param>
    /// <returns>True when the flag word changed or the box was pressed.</returns>
    public static bool CheckboxFlags(string label, ref int flags, int flagsValue)
        => CheckboxFlagsT(label, ref flags, flagsValue);

    /// <summary>
    /// Submits a checkbox that toggles an unsigned 32-bit flag group.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="flags">The flag word updated in place.</param>
    /// <param name="flagsValue">The bit group this checkbox represents.</param>
    /// <returns>True when the flag word changed or the box was pressed.</returns>
    public static bool CheckboxFlags(string label, ref uint flags, uint flagsValue)
        => CheckboxFlagsT(label, ref flags, flagsValue);

    /// <summary>
    /// Submits a checkbox that toggles a signed 64-bit flag group.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="flags">The flag word updated in place.</param>
    /// <param name="flagsValue">The bit group this checkbox represents.</param>
    /// <returns>True when the flag word changed or the box was pressed.</returns>
    internal static bool CheckboxFlags(string label, ref long flags, long flagsValue)
        => CheckboxFlagsT(label, ref flags, flagsValue);

    /// <summary>
    /// Submits a checkbox that toggles an unsigned 64-bit flag group.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="flags">The flag word updated in place.</param>
    /// <param name="flagsValue">The bit group this checkbox represents.</param>
    /// <returns>True when the flag word changed or the box was pressed.</returns>
    internal static bool CheckboxFlags(string label, ref ulong flags, ulong flagsValue)
        => CheckboxFlagsT(label, ref flags, flagsValue);

    /// <summary>
    /// Submits a radio button whose state is supplied directly by the caller.
    /// (.)
    /// </summary>
    /// <param name="label">The display label; its ID seeds from the current window's ID stack.</param>
    /// <param name="active">Whether the radio button is selected.</param>
    /// <returns>True on the frame the radio button is pressed.</returns>
    /// <remarks>
    /// The circle center is rounded to whole pixels before drawing, and the filled inner disc is inset by
    /// the truncated sixth of the frame height. The frame border is drawn with both the shadow and border
    /// colors when <c>style.FrameBorderSize &gt; 0</c>. The logging text is emitted when logging is active.
    /// </remarks>
    public static bool RadioButton(string label, bool active)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd);

        float squareSize = GetFrameHeight();
        ImVec2 pos = window.DC.CursorPos;
        ImRect checkBb = new(pos, pos + new ImVec2(squareSize, squareSize));
        ImRect totalBb = new(
            pos,
            pos + new ImVec2(
                squareSize + (labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f),
                labelSize.Y + style.FramePadding.Y * 2.0f));
        ItemSize(totalBb, style.FramePadding.Y);
        if (!ItemAdd(totalBb, id))
            return false;

        ImVec2 center = checkBb.GetCenter();
        center.X = (float)(int)(center.X + 0.5f);
        center.Y = (float)(int)(center.Y + 0.5f);
        float radius = (squareSize - 1.0f) * 0.5f;

        bool pressed = ButtonBehavior(totalBb, id, out bool hovered, out bool held);
        if (pressed)
            MarkItemEdited(id);

        RenderNavCursor(totalBb, id);
        int numSegment = window.DrawList.CalcCircleAutoSegmentCount(radius);
        window.DrawList.AddCircleFilled(
            center,
            radius,
            GetColorU32((held && hovered) ? ImGuiCol.FrameBgActive : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg),
            numSegment);
        if (active && (g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) == 0)
        {
            float pad = MathF.Max(1.0f, (float)(int)(squareSize / 6.0f));
            window.DrawList.AddCircleFilled(center, radius - pad, GetColorU32(ImGuiCol.CheckMark));
        }

        if (style.FrameBorderSize > 0.0f)
        {
            window.DrawList.AddCircle(center + new ImVec2(1.0f, 1.0f), radius, GetColorU32(ImGuiCol.BorderShadow), numSegment, style.FrameBorderSize);
            window.DrawList.AddCircle(center, radius, GetColorU32(ImGuiCol.Border), numSegment, style.FrameBorderSize);
        }

        ImVec2 labelPos = new(checkBb.Max.X + style.ItemInnerSpacing.X, checkBb.Min.Y + style.FramePadding.Y);
        if (g.LogEnabled)
            LogRenderedText(labelPos, active ? "(x)" : "( )");
        if (labelSize.X > 0.0f)
            RenderText(labelPos, label, labelEnd, false);
        return pressed;
    }

    /// <summary>
    /// Submits a radio button bound to an integer value, selecting <paramref name="vButton"/> on press.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="v">The integer value updated to <paramref name="vButton"/> when pressed.</param>
    /// <param name="vButton">The value this radio button represents.</param>
    /// <returns>True on the frame the radio button is pressed.</returns>
    public static bool RadioButton(string label, ref int v, int vButton)
    {
        bool pressed = RadioButton(label, v == vButton);
        if (pressed)
            v = vButton;
        return pressed;
    }

    /// <summary>
    /// Submits a progress bar, optionally animated as indeterminate and optionally overlaid with text.
    /// (.)
    /// </summary>
    /// <param name="fraction">The progress in <c>[0, 1]</c>; a negative value selects the indeterminate
    /// animation, which canonical expects to be time-driven.</param>
    /// <param name="sizeArg">The requested size; <see langword="null"/> selects the canonical
    /// <c>(-FLT_MIN, 0)</c> default, which fills the available width at one frame height.</param>
    /// <param name="overlay">The overlay text; <see langword="null"/> shows the rounded percentage for a
    /// determinate bar and nothing for an indeterminate one.</param>
    /// <remarks>
    /// The item has no identifier. The indeterminate animation keeps a 0.2 fraction-wide band that slides
    /// with the negative fraction; out-of-range and NaN fractions are accepted without trapping. The
    /// overlay is left-clamped to the bar and right-clamped so that it cannot overlap the inner spacing.
    /// </remarks>
    public static void ProgressBar(float fraction, ImVec2? sizeArg = null, string? overlay = null)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;

        // Canonical FLT_MIN sentinel; C# float.Epsilon is a subnormal, so the literal is used instead.
        ImVec2 resolvedSizeArg = sizeArg ?? new ImVec2(-1.17549435e-38f, 0.0f);
        ImVec2 pos = window.DC.CursorPos;
        ImVec2 size = CalcItemSize(resolvedSizeArg, CalcItemWidth(), g.FontSize + style.FramePadding.Y * 2.0f);
        ImRect bb = new(pos, pos + size);
        ItemSize(size, style.FramePadding.Y);
        if (!ItemAdd(bb, 0))
            return;

        bool isIndeterminate = fraction < 0.0f;
        if (!isIndeterminate)
            fraction = Math.Clamp(fraction, 0.0f, 1.0f);

        float fillN0 = 0.0f;
        // Canonical tests "fraction == fraction" for NaN tolerance; float.IsNaN is the C# equivalent.
        float fillN1 = !float.IsNaN(fraction) ? fraction : 0.0f;

        if (isIndeterminate)
        {
            const float fillWidthN = 0.2f;
            // Canonical ImFmod truncates toward zero, which C# float remainder also does.
            fillN0 = (-fraction % 1.0f) * (1.0f + fillWidthN) - fillWidthN;
            fillN1 = Math.Clamp(fillN0 + fillWidthN, 0.0f, 1.0f);
            fillN0 = Math.Clamp(fillN0, 0.0f, 1.0f);
        }

        RenderFrame(bb.Min, bb.Max, GetColorU32(ImGuiCol.FrameBg), true, style.FrameRounding);
        bb.Expand(new ImVec2(-style.FrameBorderSize, -style.FrameBorderSize));
        float fillX0 = bb.Min.X + (bb.Max.X - bb.Min.X) * fillN0;
        float fillX1 = bb.Min.X + (bb.Max.X - bb.Min.X) * fillN1;
        if (fillX0 < fillX1)
            RenderRectFilledInRangeH(window.DrawList, bb, GetColorU32(ImGuiCol.PlotHistogram), fillX0, fillX1, style.FrameRounding);

        if (!isIndeterminate || overlay != null)
        {
            string overlayText = overlay ?? ImFormatString("%.0f%%", fraction * 100.0f + 0.01f);
            ImVec2 overlaySize = CalcTextSize(overlayText);
            if (overlaySize.X > 0.0f)
            {
                float textX = isIndeterminate ? (bb.Min.X + bb.Max.X - overlaySize.X) * 0.5f : fillX1 + style.ItemSpacing.X;
                float maxX = bb.Max.X - overlaySize.X - style.ItemInnerSpacing.X;
                // Canonical ImClamp tolerates an inverted min/max range by returning the max; Math.Clamp
                // would throw, so the comparison chain is written out.
                float clampedX = textX < bb.Min.X ? bb.Min.X : (textX > maxX ? maxX : textX);
                RenderTextClipped(
                    new ImVec2(clampedX, bb.Min.Y),
                    bb.Max,
                    overlayText,
                    null,
                    overlaySize,
                    new ImVec2(0.0f, 0.5f),
                    bb);
            }
        }
    }

    /// <summary>
    /// Submits a bullet marker and keeps the cursor on the same line.
    /// (.)
    /// </summary>
    /// <remarks>
    /// The item has no identifier. The line height is clamped between the current line height and one
    /// font height, and the bullet is centered vertically within it. When the item is clipped the cursor
    /// still advances with the canonical same-line spacing so text following the bullet stays aligned.
    /// </remarks>
    public static void Bullet()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        float lineHeight = MathF.Max(MathF.Min(window.DC.CurrLineSize.Y, g.FontSize + style.FramePadding.Y * 2.0f), g.FontSize);
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + new ImVec2(g.FontSize, lineHeight));
        ItemSize(bb);
        if (!ItemAdd(bb, 0))
        {
            SameLine(0.0f, style.FramePadding.X * 2.0f);
            return;
        }

        uint textCol = GetColorU32(ImGuiCol.Text);
        RenderBullet(window.DrawList, bb.Min + new ImVec2(style.FramePadding.X + g.FontSize * 0.5f, lineHeight * 0.5f), textCol);
        SameLine(0.0f, style.FramePadding.X * 2.0f);
    }

    #endregion

    #region [SECTION] Widgets: Low-level Layout helpers (Separator, etc.)

    /// <summary>
    /// Submits a horizontal or vertical separating line with an explicit thickness.
    /// (.)
    /// </summary>
    /// <param name="flags">Exactly one of the horizontal or vertical orientation bits, optionally with
    /// <c>SpanAllColumns</c>.</param>
    /// <param name="thickness">The line thickness, which must be positive.</param>
    /// <remarks>
    /// <para>
    /// The vertical form uses the current line height and advances the layout only by the thickness; the
    /// horizontal form spans the work rectangle and deliberately feeds a zero width to <c>ItemSize</c> so
    /// that it does not affect auto-fit width.
    /// </para>
    /// <para>
    /// A span-all-columns horizontal separator switches to the legacy columns background draw channel so
    /// it is drawn across every column (<c>PushColumnsBackground</c>/<c>PopColumnsBackground</c>). The logging
    /// text is emitted when logging is active.
    /// </para>
    /// </remarks>
    internal static void SeparatorEx(ImGuiSeparatorFlags flags, float thickness = 1.0f)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        ImGuiContext g = GetCurrentContext()!;
        int orientation = (int)(flags & (ImGuiSeparatorFlags.Horizontal | ImGuiSeparatorFlags.Vertical));
        ImAssert.Check(orientation != 0 && (orientation & (orientation - 1)) == 0, "Only one separator orientation may be selected.");
        ImAssert.Check(thickness > 0.0f, "Separator thickness must be positive.");

        if ((flags & ImGuiSeparatorFlags.Vertical) != 0)
        {
            float y1 = window.DC.CursorPos.Y;
            float y2 = window.DC.CursorPos.Y + window.DC.CurrLineSize.Y;
            ImRect bb = new(new ImVec2(window.DC.CursorPos.X, y1), new ImVec2(window.DC.CursorPos.X + thickness, y2));
            ItemSize(new ImVec2(thickness, 0.0f));
            if (!ItemAdd(bb, 0))
                return;

            window.DrawList.AddRectFilled(bb.Min, bb.Max, GetColorU32(ImGuiCol.Separator));
            if (g.LogEnabled)
                LogText(" |");
        }
        else if ((flags & ImGuiSeparatorFlags.Horizontal) != 0)
        {
            float x1 = window.DC.CursorPos.X;
            float x2 = window.WorkRect.Max.X;

            // Legacy Columns() only; tables have their own border features and are excluded
            ImGuiOldColumns? columns = (flags & ImGuiSeparatorFlags.SpanAllColumns) != 0 ? window.DC.CurrentColumns : null;
            if (columns != null)
            {
                x1 = window.Pos.X + window.DC.Indent.X;
                x2 = window.Pos.X + window.Size.X;
                PushColumnsBackground();
            }

            // The width is intentionally not fed to the layout; see the canonical FIXME at
            float thicknessForLayout = g.Style.SeparatorSize <= 0.0f ? 0.0f : thickness;
            ImRect bb = new(new ImVec2(x1, window.DC.CursorPos.Y), new ImVec2(x2, window.DC.CursorPos.Y + thickness));
            ItemSize(new ImVec2(0.0f, thicknessForLayout));

            if (ItemAdd(bb, 0))
            {
                window.DrawList.AddRectFilled(bb.Min, bb.Max, GetColorU32(ImGuiCol.Separator));
                if (g.LogEnabled)
                    LogRenderedText(bb.Min, "--------------------------------\n");
            }
            if (columns != null)
            {
                PopColumnsBackground();
                columns.LineMinY = window.DC.CursorPos.Y;
            }
        }
    }

    /// <summary>
    /// Submits a separator whose orientation follows the current layout type.
    /// (.)
    /// </summary>
    /// <remarks>
    /// Horizontal layout produces a vertical separator. The legacy columns span bit is applied only when
    /// <c>window.DC.CurrentColumns</c> is set; tables intentionally do not rely on this function for
    /// column spanning.
    /// </remarks>
    public static void Separator()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return;

        ImGuiSeparatorFlags flags = (window.DC.LayoutType == ImGuiLayoutType.Horizontal)
            ? ImGuiSeparatorFlags.Vertical
            : ImGuiSeparatorFlags.Horizontal;
        if (window.DC.CurrentColumns != null)
            flags |= ImGuiSeparatorFlags.SpanAllColumns;

        SeparatorEx(flags, MathF.Max(g.Style.SeparatorSize, 1.0f));
    }

    /// <summary>
    /// Submits a titled separator with an explicit identifier, label range, and reserved extra width.
    /// (.)
    /// </summary>
    /// <param name="id">The item identifier; callers that do not need one pass zero.</param>
    /// <param name="label">The full label buffer.</param>
    /// <param name="labelEnd">The displayable label content, already resolved to the rendered range.</param>
    /// <param name="extraWidth">Additional width reserved after the label, which later items may fill via
    /// <c>SameLine</c>.</param>
    /// <remarks>
    /// The item spans the full work width while the layout advance uses only the minimal content size.
    /// The font-dependent baseline is truncated upward by adding 0.999 before truncation, matching the
    /// canonical rounding idiom. The logging decoration is applied when logging is active.
    /// </remarks>
    internal static void SeparatorTextEx(uint id, string label, string labelEnd, float extraWidth)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiStyle style = g.Style;

        ImVec2 labelSize = CalcTextSize(labelEnd);
        ImVec2 pos = window.DC.CursorPos;
        ImVec2 padding = style.SeparatorTextPadding;

        float separatorThickness = style.SeparatorTextBorderSize;
        ImVec2 minSize = new(
            labelSize.X + extraWidth + padding.X * 2.0f,
            MathF.Max(labelSize.Y + padding.Y * 2.0f, separatorThickness));
        ImRect bb = new(pos, new ImVec2(window.WorkRect.Max.X, pos.Y + minSize.Y));
        float textBaselineY = (float)(int)((bb.GetHeight() - labelSize.Y) * style.SeparatorTextAlign.Y + 0.999f);
        ItemSize(minSize, textBaselineY);
        if (!ItemAdd(bb, id))
            return;

        float sep1X1 = pos.X;
        float sep2X2 = bb.Max.X;
        float sepsY = (float)(int)((bb.Min.Y + bb.Max.Y) * 0.5f + 0.999f);

        float labelAvailWidth = MathF.Max(0.0f, sep2X2 - sep1X1 - padding.X * 2.0f);
        ImVec2 labelPos = new(
            pos.X + padding.X + MathF.Max(0.0f, (labelAvailWidth - labelSize.X - extraWidth) * style.SeparatorTextAlign.X),
            pos.Y + textBaselineY);

        // Lets SameLine() target the reserved extra width region.
        window.DC.CursorPosPrevLine = new ImVec2(labelPos.X + labelSize.X, window.DC.CursorPosPrevLine.Y);

        uint separatorCol = GetColorU32(ImGuiCol.Separator);
        if (labelSize.X > 0.0f)
        {
            float sep1X2 = labelPos.X - style.ItemSpacing.X;
            float sep2X1 = labelPos.X + labelSize.X + extraWidth + style.ItemSpacing.X;
            if (sep1X2 > sep1X1 && separatorThickness > 0.0f)
                window.DrawList.AddLineH(sep1X1, sep1X2, sepsY, separatorCol, separatorThickness);
            if (sep2X2 > sep2X1 && separatorThickness > 0.0f)
                window.DrawList.AddLineH(sep2X1, sep2X2, sepsY, separatorCol, separatorThickness);
            if (g.LogEnabled)
                LogSetNextTextDecoration("---", null);
            RenderTextEllipsis(
                window.DrawList,
                labelPos,
                new ImVec2(bb.Max.X, bb.Max.Y + style.ItemSpacing.Y),
                bb.Max.X,
                label,
                labelEnd,
                labelSize);
        }
        else
        {
            if (g.LogEnabled)
                LogText("---");
            if (separatorThickness > 0.0f)
                window.DrawList.AddLineH(sep1X1, sep2X2, sepsY, separatorCol, separatorThickness);
        }
    }

    /// <summary>
    /// Submits a titled separator. (, declared in
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <remarks>
    /// The item identifier is intentionally zero rather than a hash of the label, so the separator is not
    /// a navigation or interaction target; the canonical rationale is documented at
    /// </remarks>
    public static void SeparatorText(string label)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;

        SeparatorTextEx(0, label, FindRenderedTextEnd(label), 0.0f);
    }

    #endregion

    #region [SECTION] Layout: ShrinkWidths

    /// <summary>
    /// Progressively shrinks item widths when elements do not fit within an allocated parent span.
    /// </summary>
    /// <param name="items">List of items being sized.</param>
    /// <param name="offset">Starting element offset within <paramref name="items"/>.</param>
    /// <param name="count">Count of items to process.</param>
    /// <param name="width_excess">Total excess width that must be subtracted.</param>
    /// <param name="width_min">Minimum width floor below which no item may shrink.</param>
    public static void ShrinkWidths(List<ImGuiShrinkWidthItem> items, int offset, int count, float width_excess, float width_min)
    {
        if (count <= 0)
            return;

        if (count == 1)
        {
            if (items[offset].Width >= 0.0f)
                items[offset].Width = MathF.Max(items[offset].Width - width_excess, width_min);
            return;
        }

        items.Sort(offset, count, Comparer<ImGuiShrinkWidthItem>.Create((a, b) =>
        {
            float d = b.Width - a.Width;
            if (d > 0.0f) return +1;
            if (d < 0.0f) return -1;
            return a.Index - b.Index;
        }));

        int countSameWidth = 1;
        while (width_excess > 0.001f && countSameWidth < count)
        {
            while (countSameWidth < count && items[offset].Width <= items[offset + countSameWidth].Width)
                countSameWidth++;
            float maxWidthToRemovePerItem = (countSameWidth < count && items[offset + countSameWidth].Width >= 0.0f)
                ? (items[offset].Width - items[offset + countSameWidth].Width)
                : (items[offset].Width - 1.0f);
            maxWidthToRemovePerItem = MathF.Min(items[offset].Width - width_min, maxWidthToRemovePerItem);
            if (maxWidthToRemovePerItem <= 0.0f)
                break;
            float baseWidthToRemovePerItem = MathF.Min(width_excess / countSameWidth, maxWidthToRemovePerItem);
            for (int itemN = 0; itemN < countSameWidth; itemN++)
            {
                float widthToRemoveForThisItem = MathF.Min(baseWidthToRemovePerItem, items[offset + itemN].Width - width_min);
                items[offset + itemN].Width -= widthToRemoveForThisItem;
                width_excess -= widthToRemoveForThisItem;
            }
        }

        width_excess = 0.0f;
        for (int n = 0; n < count; n++)
        {
            float widthRounded = MathF.Truncate(items[offset + n].Width);
            width_excess += items[offset + n].Width - widthRounded;
            items[offset + n].Width = widthRounded;
        }
        while (width_excess > 0.0f)
        {
            for (int n = 0; n < count && width_excess > 0.0f; n++)
            {
                float widthToAdd = MathF.Min(items[offset + n].InitialWidth - items[offset + n].Width, 1.0f);
                items[offset + n].Width += widthToAdd;
                width_excess -= widthToAdd;
            }
        }
    }

    /// <summary>
    /// Progressively shrinks item widths from index 0 when elements do not fit within an allocated parent span.
    /// </summary>
    /// <param name="items">List of items being sized.</param>
    /// <param name="count">Count of items to process.</param>
    /// <param name="width_excess">Total excess width that must be subtracted.</param>
    /// <param name="width_min">Minimum width floor below which no item may shrink.</param>
    public static void ShrinkWidths(List<ImGuiShrinkWidthItem> items, int count, float width_excess, float width_min)
    {
        ShrinkWidths(items, 0, count, width_excess, width_min);
    }

    #endregion

    #region [SECTION] Widgets: ComboBox

    /// <summary>
    /// Computes the maximum popup height for a given number of items.
    /// </summary>
    /// <param name="itemsCount">The number of items; non-positive values disable the constraint.</param>
    /// <returns>The height covering every item plus the window padding, or <see cref="float.MaxValue"/>
    /// when the count is not positive.</returns>
    /// <remarks>
    /// This is a translation-unit static helper in canonical. It is private to this part because both
    /// <see cref="BeginComboPopup"/> and <see cref="Combo(string, ref int, string[], int)"/> use it.
    /// </remarks>
    private static float CalcMaxPopupHeightFromItemCount(int itemsCount)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (itemsCount <= 0)
            return float.MaxValue;
        return (g.FontSize + g.Style.ItemSpacing.Y) * itemsCount - g.Style.ItemSpacing.Y + g.Style.WindowPadding.Y * 2.0f;
    }

    /// <summary>
    /// Submits a combo header and opens the combo popup when clicked.
    /// (.)
    /// </summary>
    /// <param name="label">The label drawn after the frame; its ID seeds the item and popup IDs.</param>
    /// <param name="previewValue">The current value drawn inside the frame, or <see langword="null"/> when
    /// the caller supplies a custom preview.</param>
    /// <param name="flags">The combo flags; <c>NoArrowButton</c> and <c>NoPreview</c> are mutually
    /// exclusive and <c>WidthFitPreview</c> excludes <c>NoPreview</c> and the private
    /// <c>CustomPreview</c> flag.</param>
    /// <returns>True when the popup is open and the caller should submit items and call
    /// <see cref="EndCombo"/>.</returns>
    /// <remarks>
    /// <para>
    /// The saved <c>NextWindowData</c> flags are cleared on entry, mirroring the Begin-like consumption
    /// contract, and restored before the popup begins. Pressing the header opens the popup unless it is
    /// already open.
    /// </para>
    /// <para>
    /// The logging decoration is applied when logging is active. The private <c>CustomPreview</c> flag
    /// lives in <see cref="ImGuiComboFlagsPrivate"/>, so it is folded through a cast.
    /// </para>
    /// </remarks>
    public static bool BeginCombo(string label, string? previewValue, ImGuiComboFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();

        ImGuiNextWindowDataFlags backupNextWindowDataFlags = g.NextWindowData.HasFlags;
        g.NextWindowData.ClearFlags();
        if (window.SkipItems)
            return false;

        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        ImAssert.Check(
            (flags & (ImGuiComboFlags.NoArrowButton | ImGuiComboFlags.NoPreview)) != (ImGuiComboFlags.NoArrowButton | ImGuiComboFlags.NoPreview),
            "Can't use both ImGuiComboFlags_NoArrowButton and ImGuiComboFlags_NoPreview together.");
        if ((flags & ImGuiComboFlags.WidthFitPreview) != 0)
        {
            ImAssert.Check(
                (flags & (ImGuiComboFlags.NoPreview | (ImGuiComboFlags)(int)ImGuiComboFlagsPrivate.ImGuiComboFlags_CustomPreview)) == 0,
                "WidthFitPreview cannot be used with NoPreview or CustomPreview.");
        }

        float arrowSize = (flags & ImGuiComboFlags.NoArrowButton) != 0 ? 0.0f : GetFrameHeight();
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd);
        float previewWidth = ((flags & ImGuiComboFlags.WidthFitPreview) != 0 && previewValue != null)
            ? CalcTextSize(previewValue).X
            : 0.0f;
        float w = (flags & ImGuiComboFlags.NoPreview) != 0
            ? arrowSize
            : ((flags & ImGuiComboFlags.WidthFitPreview) != 0
                ? arrowSize + previewWidth + style.FramePadding.X * 2.0f
                : CalcItemWidth());
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + new ImVec2(w, labelSize.Y + style.FramePadding.Y * 2.0f));
        ImRect totalBb = new(bb.Min, bb.Max + new ImVec2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0.0f));
        ItemSize(totalBb, style.FramePadding.Y);
        if (!ItemAdd(totalBb, id, bb))
            return false;

        // Interaction uses the frame rectangle, not the label-extended total rectangle.
        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held);
        uint popupId = ImHashStr("##ComboPopup", 0, id);
        bool popupOpen = IsPopupOpen(popupId, ImGuiPopupFlags.None);
        if (pressed && !popupOpen)
        {
            OpenPopupEx(popupId, ImGuiPopupFlags.None);
            popupOpen = true;
        }

        uint frameCol = GetColorU32(hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        float valueX2 = MathF.Max(bb.Min.X, bb.Max.X - arrowSize);
        RenderNavCursor(bb, id);
        if ((flags & ImGuiComboFlags.NoPreview) == 0)
        {
            window.DrawList.AddRectFilled(
                bb.Min,
                new ImVec2(valueX2, bb.Max.Y),
                frameCol,
                style.FrameRounding,
                (flags & ImGuiComboFlags.NoArrowButton) != 0 ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft);
        }
        if ((flags & ImGuiComboFlags.NoArrowButton) == 0)
        {
            uint bgCol = GetColorU32((popupOpen || hovered) ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
            uint textCol = GetColorU32(ImGuiCol.Text);
            window.DrawList.AddRectFilled(
                new ImVec2(valueX2, bb.Min.Y),
                bb.Max,
                bgCol,
                style.FrameRounding,
                (w <= arrowSize) ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersRight);
            if (valueX2 + arrowSize - style.FramePadding.X <= bb.Max.X)
            {
                RenderArrow(
                    window.DrawList,
                    new ImVec2(valueX2 + style.FramePadding.Y, bb.Min.Y + style.FramePadding.Y),
                    textCol,
                    ImGuiDir.Down,
                    1.0f);
            }
        }
        RenderFrameBorder(bb.Min, bb.Max, style.FrameRounding);

        if ((flags & (ImGuiComboFlags)(int)ImGuiComboFlagsPrivate.ImGuiComboFlags_CustomPreview) != 0)
        {
            g.ComboPreviewData.PreviewRect = new ImRect(bb.Min.X, bb.Min.Y, valueX2, bb.Max.Y);
            ImAssert.Check(previewValue == null || previewValue.Length == 0, "CustomPreview requires an empty or null preview value.");
            previewValue = null;
        }

        if (previewValue != null && (flags & ImGuiComboFlags.NoPreview) == 0)
        {
            if (g.LogEnabled)
            {
                LogSetNextTextDecoration("{", "}");
            }
            RenderTextClipped(bb.Min + style.FramePadding, new ImVec2(valueX2, bb.Max.Y), previewValue, null, null);
        }
        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(bb.Max.X + style.ItemInnerSpacing.X, bb.Min.Y + style.FramePadding.Y), label, labelEnd, false);

        if (!popupOpen)
            return false;

        g.NextWindowData.HasFlags = backupNextWindowDataFlags;
        return BeginComboPopup(popupId, bb, flags);
    }

    /// <summary>
    /// Begins the window of an already-open combo popup.
    /// (.)
    /// </summary>
    /// <param name="popupId">The popup identifier produced by <see cref="BeginCombo"/>.</param>
    /// <param name="bb">The combo frame rectangle, used for width and placement.</param>
    /// <param name="flags">The combo flags, including the height class.</param>
    /// <returns>True when the popup window is current and the caller must submit items and call
    /// <see cref="EndCombo"/>.</returns>
    /// <remarks>
    /// <para>
    /// When the caller did not queue a size constraint, the width is inherited from the combo and the
    /// height is derived from the height flag class; <c>HeightLargest</c> leaves the height unconstrained.
    /// Popup windows are recycled by nesting depth through the <c>##Combo_%02d</c> name.
    /// </para>
    /// <para>
    /// <see cref="GetPopupAllowedExtentRect"/> and <see cref="FindBestWindowPosForPopupEx"/> are popup
    /// subsystem members owned by the popup part (part 05) and are called exactly as canonical.
    /// </para>
    /// </remarks>
    internal static bool BeginComboPopup(uint popupId, ImRect bb, ImGuiComboFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (!IsPopupOpen(popupId, ImGuiPopupFlags.None))
        {
            g.NextWindowData.ClearFlags();
            return false;
        }

        float w = bb.GetWidth();
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSizeConstraint) != 0)
        {
            ImRect constraint = g.NextWindowData.SizeConstraintRect;
            g.NextWindowData.SizeConstraintRect = new ImRect(
                new ImVec2(MathF.Max(constraint.Min.X, w), constraint.Min.Y),
                constraint.Max);
        }
        else
        {
            if ((flags & ImGuiComboFlags.HeightMask_) == 0)
                flags |= ImGuiComboFlags.HeightRegular;
            int heightMask = (int)(flags & ImGuiComboFlags.HeightMask_);
            ImAssert.Check(heightMask != 0 && (heightMask & (heightMask - 1)) == 0, "Only one combo height flag may be selected.");

            int popupMaxHeightInItems = -1;
            if ((flags & ImGuiComboFlags.HeightRegular) != 0)
                popupMaxHeightInItems = 8;
            else if ((flags & ImGuiComboFlags.HeightSmall) != 0)
                popupMaxHeightInItems = 4;
            else if ((flags & ImGuiComboFlags.HeightLarge) != 0)
                popupMaxHeightInItems = 20;
            ImVec2 constraintMin = new(0.0f, 0.0f);
            ImVec2 constraintMax = new(float.MaxValue, float.MaxValue);
            if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSize) == 0 || g.NextWindowData.SizeVal.X <= 0.0f)
                constraintMin.X = w;
            if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSize) == 0 || g.NextWindowData.SizeVal.Y <= 0.0f)
                constraintMax.Y = CalcMaxPopupHeightFromItemCount(popupMaxHeightInItems);
            SetNextWindowSizeConstraints(constraintMin, constraintMax);
        }

        // Windows are recycled by nesting depth.
        string name = ImFormatString("##Combo_%02d", g.BeginComboDepth);

        ImGuiWindow? popupWindow = FindWindowByName(name);
        if (popupWindow != null && popupWindow.WasActive)
        {
            ImVec2 sizeExpected = CalcWindowNextAutoFitSize(popupWindow);
            popupWindow.AutoPosLastDirection = (flags & ImGuiComboFlags.PopupAlignLeft) != 0 ? ImGuiDir.Left : ImGuiDir.Down;
            ImRect rOuter = GetPopupAllowedExtentRect(popupWindow);
            // A property cannot be passed by reference, so the direction round-trips through a local.
            ImGuiDir lastDir = popupWindow.AutoPosLastDirection;
            ImVec2 pos = FindBestWindowPosForPopupEx(bb.GetBL(), sizeExpected, ref lastDir, rOuter, bb, ImGuiPopupPositionPolicy.ComboBox);
            popupWindow.AutoPosLastDirection = lastDir;
            SetNextWindowPos(pos);
        }

        ImGuiWindowFlags windowFlags =
            ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.Popup
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove;
        PushStyleVarX(ImGuiStyleVar.WindowPadding, g.Style.FramePadding.X);
        bool ret = Begin(name, windowFlags);
        PopStyleVar();
        if (!ret)
        {
            EndPopup();
            // Begin may only return false with the debug-cull tools enabled; the popup was tested open
            // above, so reaching here otherwise is a programming error.
            ImAssert.Check(g.IO.ConfigDebugBeginReturnValueOnce || g.IO.ConfigDebugBeginReturnValueLoop);
            return false;
        }
        g.BeginComboDepth++;
        return true;
    }

    /// <summary>
    /// Ends the combo popup opened by <see cref="BeginCombo"/>.
    /// (.)
    /// </summary>
    /// <remarks>
    /// The nesting depth is decremented before the recycled window name is recomputed; a mismatch reports
    /// a recoverable user error and returns without closing the wrong popup, matching canonical
    /// <c>IM_ASSERT_USER_ERROR_RET</c>.
    /// </remarks>
    public static void EndCombo()
    {
        ImGuiContext g = GetCurrentContext()!;
        g.BeginComboDepth--;
        string name = ImFormatString("##Combo_%02d", g.BeginComboDepth);
        if (g.CurrentWindow!.Name != name)
        {
            AssertUserError(false, "Calling EndCombo() in wrong window!");
            return;
        }
        EndPopup();
    }

    /// <summary>
    /// Begins a custom combo preview immediately after a <c>BeginCombo</c>/<c>EndCombo</c> block.
    /// (.)
    /// </summary>
    /// <returns>True when the preview cursor is redirected into the preview rectangle.</returns>
    /// <remarks>
    /// The function asserts that it directly follows the combo call by comparing the last item rectangle's
    /// minimum with the saved preview rectangle. The cursor, maximum cursor, previous-line cursor, line
    /// baseline, and layout type are saved for <see cref="EndComboPreview"/> to restore.
    /// </remarks>
    internal static bool BeginComboPreview()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiComboPreviewData previewData = g.ComboPreviewData;

        if (window.SkipItems || (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Visible) == 0)
            return false;
        ImAssert.Check(
            g.LastItemData.Rect.Min.X == previewData.PreviewRect.Min.X && g.LastItemData.Rect.Min.Y == previewData.PreviewRect.Min.Y,
            "BeginComboPreview() must directly follow the BeginCombo/EndCombo block with ImGuiComboFlags_CustomPreview.");
        if (!window.ClipRect.Overlaps(previewData.PreviewRect))
            return false;

        previewData.BackupCursorPos = window.DC.CursorPos;
        previewData.BackupCursorMaxPos = window.DC.CursorMaxPos;
        previewData.BackupCursorPosPrevLine = window.DC.CursorPosPrevLine;
        previewData.BackupPrevLineTextBaseOffset = window.DC.PrevLineTextBaseOffset;
        previewData.BackupLayout = window.DC.LayoutType;
        window.DC.CursorPos = previewData.PreviewRect.Min + g.Style.FramePadding;
        window.DC.CursorMaxPos = window.DC.CursorPos;
        window.DC.LayoutType = ImGuiLayoutType.Horizontal;
        window.DC.IsSameLine = false;
        PushClipRect(previewData.PreviewRect.Min, previewData.PreviewRect.Max, true);
        return true;
    }

    /// <summary>
    /// Ends the custom combo preview begun by <see cref="BeginComboPreview"/>.
    /// (.)
    /// </summary>
    /// <remarks>
    /// Canonical additionally rewrites the trailing draw command's clip rectangle and merges commands
    /// before popping the clip rectangle; this port's <see cref="ImDrawList"/> performs that bookkeeping
    /// inside its own clip-change handling (<c>PopClipRect</c> and <c>_OnChangedClipRect</c>), so the
    /// explicit raw-buffer manipulation has no counterpart here.
    /// </remarks>
    internal static void EndComboPreview()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiComboPreviewData previewData = g.ComboPreviewData;

        PopClipRect();
        window.DC.CursorPos = previewData.BackupCursorPos;
        window.DC.CursorMaxPos = new ImVec2(
            MathF.Max(window.DC.CursorMaxPos.X, previewData.BackupCursorMaxPos.X),
            MathF.Max(window.DC.CursorMaxPos.Y, previewData.BackupCursorMaxPos.Y));
        window.DC.CursorPosPrevLine = previewData.BackupCursorPosPrevLine;
        window.DC.PrevLineTextBaseOffset = previewData.BackupPrevLineTextBaseOffset;
        window.DC.LayoutType = previewData.BackupLayout;
        window.DC.IsSameLine = false;
        previewData.PreviewRect = default;
    }

    /// <summary>
    /// Submits an array-backed combo box and returns the item selection.
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="currentItem">The selected index, updated in place.</param>
    /// <param name="items">The item strings; a <see langword="null"/> entry is displayed as
    /// <c>*Unknown item*</c>.</param>
    /// <param name="popupMaxHeightInItems">The legacy popup height in items, or <c>-1</c> to leave the
    /// height unconstrained.</param>
    /// <returns>True when the selection changed.</returns>
    /// <remarks>
    /// <para>
    /// Canonical carries a separate item count and a getter callback; this port's bound array surface
    /// derives the count from <paramref name="items"/>.Length and indexes the array directly, keeping the
    /// canonical item loop, per-row <c>PushID</c>, selection, and default-focus behavior.
    /// </para>
    /// <para>
    /// Branches guarded inert: the row clipper (<c>ImGuiListClipper</c>, Chunk 9), so every row is
    /// submitted rather than a visible subset.
    /// </para>
    /// </remarks>
    public static bool Combo(string label, ref int currentItem, string[] items, int popupMaxHeightInItems = -1)
    {
        ImGuiContext g = GetCurrentContext()!;

        string? previewValue = null;
        if (((g.NextItemData.ItemFlagsSet | g.CurrentItemFlags) & ImGuiItemFlags.MixedValue) != 0)
            previewValue = string.Empty;
        else if (currentItem >= 0 && currentItem < items.Length)
            previewValue = items[currentItem];

        if (popupMaxHeightInItems != -1 && (g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasSizeConstraint) == 0)
        {
            SetNextWindowSizeConstraints(
                new ImVec2(0.0f, 0.0f),
                new ImVec2(float.MaxValue, CalcMaxPopupHeightFromItemCount(popupMaxHeightInItems)));
        }

        if (!BeginCombo(label, previewValue, ImGuiComboFlags.None))
            return false;

        bool valueChanged = false;
        // ImGuiListClipper (Chunk 9): the full row range is submitted inertly.
        for (int i = 0; i < items.Length; i++)
        {
            string itemText = items[i];
            if (itemText == null)
                itemText = "*Unknown item*";

            PushID(i);
            bool itemSelected = i == currentItem;
            if (Selectable(itemText, itemSelected) && currentItem != i)
            {
                valueChanged = true;
                currentItem = i;
            }
            if (itemSelected)
                SetItemDefaultFocus();
            PopID();
        }

        EndCombo();
        if (valueChanged)
            MarkItemEdited(g.LastItemData.ID);
        return valueChanged;
    }

    /// <summary>
    /// Submits a combo box whose items are zero-terminated entries inside one string.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="currentItem">The selected index, updated in place.</param>
    /// <param name="itemsSeparatedByZeros">The items separated by <c>'\0'</c> and ending with a double
    /// terminator.</param>
    /// <param name="popupMaxHeightInItems">The legacy popup height in items, or <c>-1</c>.</param>
    /// <returns>True when the selection changed.</returns>
    /// <remarks>
    /// The entries are copied into an array so the array overload can be reused; this trades the canonical
    /// pointer walk for one per-call allocation, which is acceptable for the rare zero-separated form.
    /// </remarks>
    public static bool Combo(string label, ref int currentItem, string itemsSeparatedByZeros, int popupMaxHeightInItems = -1)
    {
        int itemsCount = 0;
        int scan = 0;
        while (scan < itemsSeparatedByZeros.Length && itemsSeparatedByZeros[scan] != '\0')
        {
            int terminator = itemsSeparatedByZeros.IndexOf('\0', scan);
            scan = (terminator < 0 ? itemsSeparatedByZeros.Length : terminator) + 1;
            itemsCount++;
        }

        string[] items = new string[itemsCount];
        scan = 0;
        for (int i = 0; i < itemsCount; i++)
        {
            int terminator = itemsSeparatedByZeros.IndexOf('\0', scan);
            if (terminator < 0)
                terminator = itemsSeparatedByZeros.Length;
            items[i] = itemsSeparatedByZeros[scan..terminator];
            scan = terminator + 1;
        }

        return Combo(label, ref currentItem, items, popupMaxHeightInItems);
    }

    /// <summary>
    /// Submits a combo box whose items are produced on demand by a getter function.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="currentItem">The selected index, updated in place.</param>
    /// <param name="itemsGetter">Getter returning the item text for an index; a <see langword="null"/>
    /// result is displayed as <c>*Unknown item*</c>.</param>
    /// <param name="userData">Opaque value passed through to <paramref name="itemsGetter"/>.</param>
    /// <param name="itemsCount">Number of items the getter can produce.</param>
    /// <param name="popupMaxHeightInItems">The legacy popup height in items, or <c>-1</c>.</param>
    /// <returns>True when the selection changed.</returns>
    /// <remarks>
    /// Canonical implements this overload as the core loop and expresses the array overload through a
    /// getter wrapper. This port's core is the array overload, so the getter is materialized into an
    /// array for the current item count and then delegated. The port's item loop already submits every
    /// row (the row clipper is inert), so the eager materialization matches the existing behavior; it
    /// does allocate one array per call and should be used for small, non-per-frame item sets.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemsGetter"/> is null.</exception>
    public static bool Combo(string label, ref int currentItem, Func<object?, int, string> itemsGetter, object? userData,
        int itemsCount, int popupMaxHeightInItems = -1)
    {
        ArgumentNullException.ThrowIfNull(itemsGetter);
        ImAssert.Check(itemsCount >= 0, "Combo() requires a non-negative item count.");
        string[] items = new string[itemsCount];
        for (int i = 0; i < itemsCount; i++)
            items[i] = itemsGetter(userData, i);
        return Combo(label, ref currentItem, items, popupMaxHeightInItems);
    }

    #endregion

    #region [SECTION] Widgets: Selectable

    /// <summary>
    /// Submits a selectable row carrying its selection state read-only.
    /// (.)
    /// </summary>
    /// <param name="label">The display label; its ID seeds from the current window's ID stack.</param>
    /// <param name="selected">The current selection state read by the widget.</param>
    /// <param name="flags">The selectable flags, including the private span/no-padding/nav bits.</param>
    /// <param name="size">The requested size; zero components fall back to the label size, and a zero width
    /// or the private <c>SpanAvailWidth</c> fills the available width.</param>
    /// <returns>True when the item is pressed; toggled selection is queried with
    /// <c>IsItemToggledSelection</c>.</returns>
    /// <remarks>
    /// <para>
    /// The layout advance uses the label/explicit size submitted before the half-spacing expansion, while
    /// <c>ItemAdd</c> receives the expanded interaction rectangle, exactly as canonical.
    /// </para>
    /// <para>
    /// A span-all-columns selectable switches to the table or legacy columns background draw channel for
    /// its highlight, and an unclipped multi-select item is still evaluated while a box-selection unclip
    /// rectangle overlaps it. Branches guarded inert: disabled alpha modulation through
    /// <c>BeginDisabled</c>/<c>EndDisabled</c> (Chunk 10). The popup auto-close path is live and calls
    /// <see cref="CloseCurrentPopup"/>.
    /// </para>
    /// <para>
    /// The canonical flag <c>ImGuiSelectableFlags_SelectableDontClosePopup</c> does not exist at this
    /// revision; the inverted <c>AutoClosePopups</c> item flag is tested instead, and the public
    /// <c>NoAutoClosePopups</c> flag suppresses the close.
    /// </para>
    /// </remarks>
    public static bool Selectable(string label, bool selected = false, ImGuiSelectableFlags flags = 0, ImVec2 size = default)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;

        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd);
        ImVec2 itemSize = new(size.X != 0.0f ? size.X : labelSize.X, size.Y != 0.0f ? size.Y : labelSize.Y);
        ImVec2 pos = window.DC.CursorPos;
        pos.Y += window.DC.CurrLineTextBaseOffset;
        ItemSize(itemSize, 0.0f);

        // Horizontal span and half-spacing expansion.
        bool spanAllColumns = (flags & ImGuiSelectableFlags.SpanAllColumns) != 0;
        float minX = spanAllColumns ? window.ParentWorkRect.Min.X : pos.X;
        float maxX = spanAllColumns ? window.ParentWorkRect.Max.X : window.WorkRect.Max.X;
        if (size.X == 0.0f || (flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SpanAvailWidth) != 0)
            itemSize.X = MathF.Max(labelSize.X, maxX - minX);

        ImRect bb = new(minX, pos.Y, minX + itemSize.X, pos.Y + itemSize.Y);
        if ((flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_NoPadWithHalfSpacing) == 0)
        {
            float spacingX = spanAllColumns ? 0.0f : style.ItemSpacing.X;
            float spacingY = style.ItemSpacing.Y;
            float spacingL = (float)(int)(spacingX * 0.50f);
            float spacingU = (float)(int)(spacingY * 0.50f);
            bb.Min.X -= spacingL;
            bb.Min.Y -= spacingU;
            bb.Max.X += spacingX - spacingL;
            bb.Max.Y += spacingY - spacingU;
        }

        bool disabledItem = (flags & ImGuiSelectableFlags.Disabled) != 0;
        ImGuiItemFlags extraItemFlags = disabledItem ? ImGuiItemFlags.Disabled : ImGuiItemFlags.None;
        bool isVisible;
        if (spanAllColumns)
        {
            // The clip rectangle is widened in place around ItemAdd instead of starting a background
            // channel for every selectable.
            ImRect backupClipRect = window.ClipRect;
            window.ClipRect = new ImRect(
                new ImVec2(window.ParentWorkRect.Min.X, backupClipRect.Min.Y),
                new ImVec2(window.ParentWorkRect.Max.X, backupClipRect.Max.Y));
            isVisible = ItemAdd(bb, id, null, extraItemFlags);
            window.ClipRect = backupClipRect;
        }
        else
        {
            isVisible = ItemAdd(bb, id, null, extraItemFlags);
        }

        bool isMultiSelect = (g.LastItemData.ItemFlags & (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_IsMultiSelect) != 0;
        if (!isVisible)
        {
            // An unclipped multi-select item still participates in box-selection when the box-select
            // unclip rectangle overlaps it. This extra layer avoids paying for a no-logic-clip test in
            // ItemAdd itself.
            if (!isMultiSelect || !g.BoxSelectState.UnclipMode || !g.BoxSelectState.UnclipRect.Overlaps(bb))
                return false;
        }

        // BeginDisabled()/EndDisabled() are owned by Chunk 10 and are inert here; only the disabled alpha
        // modulation is lost, while ItemAdd still applies ImGuiItemFlags_Disabled for interaction.

        if (spanAllColumns)
        {
            if (g.CurrentTable != null)
                TablePushBackgroundChannel();
            else if (window.DC.CurrentColumns != null)
                PushColumnsBackground();
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HasClipRect;
            g.LastItemData.ClipRect = window.ClipRect;
        }

        // Translate selectable flags into the button flag bits.
        int buttonFlags = 0;
        if ((flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_NoHoldingActiveID) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoHoldingActiveId;
        if ((flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_NoSetKeyOwner) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoSetKeyOwner;
        if ((flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SelectOnClick) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick;
        if ((flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SelectOnRelease) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnRelease;
        if ((flags & ImGuiSelectableFlags.AllowDoubleClick) != 0)
            buttonFlags |= (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease | (int)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDoubleClick;
        if ((flags & ImGuiSelectableFlags.AllowOverlap) != 0
            || (g.LastItemData.ItemFlags & (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_AllowOverlap) != 0)
            buttonFlags |= (int)ImGuiButtonFlags.AllowOverlap;

        bool wasSelected = selected;
        // Multi-selection support (header): resolve the visible selection and adjust the button flags.
        if (isMultiSelect)
            MultiSelectItemHeader(id, ref selected, ref buttonFlags);
        bool pressed = ButtonBehavior(bb, id, out bool hovered, out bool held, (ImGuiButtonFlags)buttonFlags);
        bool autoSelected = false;

        // Multi-selection support (footer): emit the selection requests and update range/nav state.
        if (isMultiSelect)
        {
            MultiSelectItemFooter(id, ref selected, ref pressed);
        }
        else if ((flags & ImGuiSelectableFlags.SelectOnNav) != 0
            && g.NavJustMovedToId != 0
            && g.NavJustMovedToFocusScopeId == g.CurrentFocusScopeId)
        {
            // Auto-select when navigation moves into the item. The multi-select scope replaces this
            // legacy path.
            if (g.NavJustMovedToId == id && (g.NavJustMovedToKeyMods & ImGuiKey.ImGuiMod_Ctrl) == 0)
                selected = pressed = autoSelected = true;
        }

        // Update NavId on click or on hover when requested, so navigation can resume from the mouse.
        if (pressed || (hovered && (flags & (ImGuiSelectableFlags)(int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SetNavIdOnHover) != 0))
        {
            if (!g.NavHighlightItemUnderNav && g.NavWindow == window && g.NavLayer == window.DC.NavLayerCurrent)
            {
                SetNavID(id, window.DC.NavLayerCurrent, g.CurrentFocusScopeId, NavWindowRectAbsToRel(window, bb));
                if (g.IO.ConfigNavCursorVisibleAuto)
                    g.NavCursorVisible = false;
            }
        }
        if (pressed)
            MarkItemEdited(id);

        if (selected != wasSelected)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.ToggledSelection;
        if (g.ActiveId == id && g.ActiveIdIsJustActivated)
        {
            g.ActiveIdWasSelected = wasSelected;
            g.ActiveIdWasSoleSelected = wasSelected && (!isMultiSelect || g.CurrentMultiSelect!.IsSoleOrUnknownSelectionSize);
        }

        if (isVisible)
        {
            bool highlighted = hovered || (flags & ImGuiSelectableFlags.Highlight) != 0;
            if (highlighted || selected)
            {
                uint col = GetColorU32((held && highlighted) ? ImGuiCol.HeaderActive : highlighted ? ImGuiCol.HeaderHovered : ImGuiCol.Header);
                RenderFrame(bb.Min, bb.Max, col, false, style.SelectableRounding);
            }
            if (g.NavId == id)
            {
                ImGuiNavRenderCursorFlags navRenderCursorFlags = ImGuiNavRenderCursorFlags.Compact;
                if (isMultiSelect)
                    navRenderCursorFlags |= ImGuiNavRenderCursorFlags.AlwaysDraw;
                RenderNavCursor(bb, id, navRenderCursorFlags, style.SelectableRounding);
            }
        }

        if (spanAllColumns)
        {
            if (g.CurrentTable != null)
                TablePopBackgroundChannel();
            else if (window.DC.CurrentColumns != null)
                PopColumnsBackground();
        }

        // Text stays at the submission position; alignment and clipping ignore SpanAllColumns.
        if (isVisible)
        {
            RenderTextClipped(
                pos,
                new ImVec2(MathF.Min(pos.X + itemSize.X, window.WorkRect.Max.X), pos.Y + itemSize.Y),
                label,
                labelEnd,
                labelSize,
                style.SelectableTextAlign,
                bb);
        }

        if (pressed && !autoSelected
            && (window.Flags & ImGuiWindowFlags.Popup) != 0
            && (flags & ImGuiSelectableFlags.NoAutoClosePopups) == 0
            && (g.LastItemData.ItemFlags & ImGuiItemFlags.AutoClosePopups) != 0)
        {
            CloseCurrentPopup();
        }

        // EndDisabled() is owned by Chunk 10 and is inert here.
        return pressed;
    }

    /// <summary>
    /// Submits a selectable row bound to a caller-owned boolean that is toggled on press.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="pSelected">The selection state read and toggled in place.</param>
    /// <param name="flags">The selectable flags.</param>
    /// <param name="size">The requested size.</param>
    /// <returns>True when the row is pressed.</returns>
    public static bool Selectable(string label, ref bool pSelected, ImGuiSelectableFlags flags = 0, ImVec2 size = default)
    {
        if (Selectable(label, pSelected, flags, size))
        {
            pSelected = !pSelected;
            return true;
        }
        return false;
    }

    #endregion

    #region [SECTION] Widgets: ListBox

    /// <summary>
    /// Begins a framed child window that hosts a list of selectable rows.
    /// (.)
    /// </summary>
    /// <param name="label">The label drawn above the frame; its ID seeds the item and child window IDs.</param>
    /// <param name="size">The frame size; zero components resolve to the item width and roughly 7.25
    /// text rows so partial scrolling is visible.</param>
    /// <returns>True when the list box is open and the caller should submit rows and call
    /// <see cref="EndListBox"/>.</returns>
    /// <remarks>
    /// <para>
    /// The early-out when the box is clipped still submits the item and consumes the queued
    /// <c>NextWindowData</c> flags, because the function behaves like a Begin call.
    /// </para>
    /// <para>
    /// Branches guarded inert: <c>BeginGroup()</c> (Chunk 10), whose only purpose is to make
    /// <c>IsItemXXX</c> queries cover the label; the child window and its frame styling still work.
    /// </para>
    /// </remarks>
    public static bool BeginListBox(string label, ImVec2 size = default)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd);

        ImVec2 listSize = CalcItemSize(size, CalcItemWidth(), GetTextLineHeightWithSpacing() * 7.25f + style.FramePadding.Y * 2.0f);
        listSize.X = (float)(int)listSize.X;
        listSize.Y = (float)(int)listSize.Y;
        ImVec2 frameSize = new(listSize.X, MathF.Max(listSize.Y, labelSize.Y));
        ImRect frameBb = new(window.DC.CursorPos, window.DC.CursorPos + frameSize);
        ImRect bb = new(frameBb.Min, frameBb.Max + new ImVec2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0.0f));
        g.NextItemData.ClearFlags();

        if (!IsRectVisible(bb.Min, bb.Max))
        {
            ItemSize(bb.GetSize(), style.FramePadding.Y);
            ItemAdd(bb, 0, frameBb);
            g.NextWindowData.ClearFlags();
            return false;
        }

        // BeginGroup() is owned by Chunk 10 and is inert here.
        if (labelSize.X > 0.0f)
        {
            ImVec2 labelPos = new(frameBb.Max.X + style.ItemInnerSpacing.X, frameBb.Min.Y + style.FramePadding.Y);
            RenderText(labelPos, label, labelEnd, false);
            window.DC.CursorMaxPos = new ImVec2(
                MathF.Max(window.DC.CursorMaxPos.X, labelPos.X + labelSize.X),
                MathF.Max(window.DC.CursorMaxPos.Y, labelPos.Y + labelSize.Y));
            AlignTextToFramePadding();
        }

        BeginChild(id, frameBb.GetSize(), ImGuiChildFlags.FrameStyle);
        return true;
    }

    /// <summary>
    /// Ends the list box begun by <see cref="BeginListBox"/>.
    /// (.)
    /// </summary>
    /// <remarks>
    /// A non-child current window reports a mismatched-call assertion, matching canonical. Branches
    /// guarded inert: <c>EndGroup()</c> (Chunk 10), paired with the inert <c>BeginGroup()</c>.
    /// </remarks>
    public static void EndListBox()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImAssert.Check(
            (window.Flags & ImGuiWindowFlags.ChildWindow) != 0,
            "Mismatched BeginListBox/EndListBox calls. Did you test the return value of BeginListBox?");
        EndChild();
        // EndGroup() is owned by Chunk 10 and is inert here.
    }

    /// <summary>
    /// Submits an array-backed list box and returns the row selection.
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="currentItem">The selected index, updated in place.</param>
    /// <param name="items">The item strings; a <see langword="null"/> entry is displayed as
    /// <c>*Unknown item*</c>.</param>
    /// <param name="heightInItems">The visible row count; a negative value selects
    /// <c>min(items.Length, 7)</c>, and a fractional row is added so a partial row is visible.</param>
    /// <returns>True when the selection changed.</returns>
    /// <remarks>
    /// Canonical carries a separate item count and a getter callback; this port's bound array surface
    /// derives the count from the array length and indexes it directly. Branches guarded inert: the row
    /// clipper (<c>ImGuiListClipper</c>, Chunk 9), so every row is submitted.
    /// </remarks>
    public static bool ListBox(string label, ref int currentItem, string[] items, int heightInItems = -1)
    {
        ImGuiContext g = GetCurrentContext()!;

        if (heightInItems < 0)
            heightInItems = Math.Min(items.Length, 7);
        float heightInItemsF = heightInItems + 0.25f;
        ImVec2 size = new(0.0f, (float)(int)(GetTextLineHeightWithSpacing() * heightInItemsF + g.Style.FramePadding.Y * 2.0f));

        if (!BeginListBox(label, size))
            return false;

        bool valueChanged = false;
        // ImGuiListClipper (Chunk 9): the full row range is submitted inertly. Canonical assumes every row
        // is one text line tall.
        for (int i = 0; i < items.Length; i++)
        {
            string itemText = items[i];
            if (itemText == null)
                itemText = "*Unknown item*";

            PushID(i);
            bool itemSelected = i == currentItem;
            if (Selectable(itemText, itemSelected))
            {
                currentItem = i;
                valueChanged = true;
            }
            if (itemSelected)
                SetItemDefaultFocus();
            PopID();
        }
        EndListBox();

        if (valueChanged)
            MarkItemEdited(g.LastItemData.ID);
        return valueChanged;
    }

    /// <summary>
    /// Submits a list box whose rows are produced on demand by a getter function.
    /// (.)
    /// </summary>
    /// <param name="label">The display label.</param>
    /// <param name="currentItem">The selected index, updated in place.</param>
    /// <param name="itemsGetter">Getter returning the row text for an index; a <see langword="null"/>
    /// result is displayed as <c>*Unknown item*</c>.</param>
    /// <param name="userData">Opaque value passed through to <paramref name="itemsGetter"/>.</param>
    /// <param name="itemsCount">Number of rows the getter can produce.</param>
    /// <param name="heightInItems">The visible row count; a negative value selects
    /// <c>min(itemsCount, 7)</c>.</param>
    /// <returns>True when the selection changed.</returns>
    /// <remarks>
    /// Canonical implements this overload as the core loop and expresses the array overload through a
    /// getter wrapper. This port's core is the array overload, so the getter is materialized into an
    /// array for the current row count and then delegated. The port's row loop already submits every
    /// row (the row clipper is inert), so the eager materialization matches the existing behavior; it
    /// does allocate one array per call and should be used for small, non-per-frame item sets.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemsGetter"/> is null.</exception>
    public static bool ListBox(string label, ref int currentItem, Func<object?, int, string> itemsGetter, object? userData,
        int itemsCount, int heightInItems = -1)
    {
        ArgumentNullException.ThrowIfNull(itemsGetter);
        ImAssert.Check(itemsCount >= 0, "ListBox() requires a non-negative item count.");
        string[] items = new string[itemsCount];
        for (int i = 0; i < itemsCount; i++)
            items[i] = itemsGetter(userData, i);
        return ListBox(label, ref currentItem, items, heightInItems);
    }

    #endregion

    #region [SECTION] Widgets: Value helpers

    /// <summary>
    /// Submits a <c>prefix: true/false</c> text line.
    /// (.)
    /// </summary>
    /// <param name="prefix">The label preceding the value.</param>
    /// <param name="v">The boolean value to display.</param>
    /// <remarks>Text-only: layout and rendering occur inside <c>Text</c>; there is no item identifier.</remarks>
    public static void Value(string prefix, bool v)
        => Text("%s: %s", prefix, v ? "true" : "false");

    /// <summary>
    /// Submits a <c>prefix: value</c> text line for a signed integer.
    /// (.)
    /// </summary>
    /// <param name="prefix">The label preceding the value.</param>
    /// <param name="v">The value to display.</param>
    public static void Value(string prefix, int v)
        => Text("%s: %d", prefix, v);

    /// <summary>
    /// Submits a <c>prefix: value</c> text line for an unsigned integer.
    /// (.)
    /// </summary>
    /// <param name="prefix">The label preceding the value.</param>
    /// <param name="v">The value to display.</param>
    /// <remarks>
    /// Canonical prints the unsigned value through the signed <c>%d</c> conversion; the literal format is
    /// preserved here for parity rather than corrected to <c>%u</c>.
    /// </remarks>
    public static void Value(string prefix, uint v)
        => Text("%s: %d", prefix, v);

    /// <summary>
    /// Submits a <c>prefix: value</c> text line for a float, optionally with a caller-supplied format.
    /// (.)
    /// </summary>
    /// <param name="prefix">The label preceding the value.</param>
    /// <param name="v">The value to display.</param>
    /// <param name="floatFormat">The value format appended to <c>%s: </c>; <see langword="null"/> selects
    /// <c>%.3f</c>.</param>
    /// <remarks>
    /// Canonical composes the format into a 64-byte buffer; this port composes it into a managed string.
    /// </remarks>
    public static void Value(string prefix, float v, string? floatFormat = null)
    {
        if (floatFormat != null)
        {
            string fmt = ImFormatString("%%s: %s", floatFormat);
            Text(fmt, prefix, v);
        }
        else
        {
            Text("%s: %.3f", prefix, v);
        }
    }

    #endregion

    #endregion


    #region SECTION: TREES

    #region Widgets: TreeNode, CollapsingHeader, etc.

    /// <summary>
    /// Submits a tree node whose displayed text is also its identifier seed and returns whether the node
    /// is open. Canvases canonical <c>ImGui::TreeNode(const char*)</c>
    ///.
    /// </summary>
    /// <param name="label">The display label, which may carry a <c>##</c> suffix that only participates in
    /// the identifier. The rendered range is resolved by <see cref="TreeNodeBehavior"/>.</param>
    /// <returns>
    /// <see langword="true"/> while the node is open, in which case the caller must eventually call
    /// <see cref="TreePop"/>; <see langword="false"/> when the window is skipped or the node is closed.
    /// </returns>
    /// <remarks>
    /// The identifier is <c>GetID(label)</c> against the current window's ID stack. The canonical
    /// <c>label_end == NULL</c> argument is preserved by passing <see langword="null"/>, which makes
    /// <see cref="TreeNodeBehavior"/> call <see cref="FindRenderedTextEnd"/> itself
    /// </remarks>
    public static bool TreeNode(string label)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;
        uint id = GetID(label);
        return TreeNodeBehavior(id, ImGuiTreeNodeFlags.None, label, null);
    }

    /// <summary>
    /// Submits a tree node whose identifier seed and displayed text are supplied separately; the
    /// displayed text is produced by formatting <paramref name="fmt"/> with <paramref name="args"/>.
    /// Canvases canonical <c>ImGui::TreeNode(const char*, const char*,.)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier seed; it is hashed independently of the displayed text.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// Canonical forwards to <c>TreeNodeExV(str_id, 0, fmt, args)</c>., so
    /// the identifier is hashed from <paramref name="strId"/> alone and the formatted text is submitted
    /// through the shared tree-node path.
    /// </remarks>
    public static bool TreeNode(string strId, string fmt, params object?[] args)
        => TreeNodeExV(strId, ImGuiTreeNodeFlags.None, fmt, args);

    /// <summary>
    /// Submits a tree node whose identifier seed is an opaque object reference and whose displayed text
    /// is produced by formatting <paramref name="fmt"/> with <paramref name="args"/>. Canvases canonical
    /// <c>ImGui::TreeNode(const void*, const char*,.)</c>.,
    ///.
    /// </summary>
    /// <param name="ptrId">The pointer-derived identifier seed; the port hashes the reference identity
    /// (see <see cref="GetID(object?)"/>).</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// Canonical forwards to <c>TreeNodeExV(ptr_id, 0, fmt, args)</c>. The
    /// overload exists so a caller can decorrelate the identifier from any displayed string.
    /// </remarks>
    public static bool TreeNode(object? ptrId, string fmt, params object?[] args)
        => TreeNodeExV(ptrId, ImGuiTreeNodeFlags.None, fmt, args);

    /// <summary>
    /// Submits a tree node whose identifier seed and displayed text are supplied separately from an
    /// already-collected argument array. Canvases canonical <c>ImGui::TreeNodeV(const char*, const char*, va_list)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier seed.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// Canonical is a pure forwarder to <c>TreeNodeExV(str_id, 0, fmt, args)</c>.
    /// and performs no skip check of its own; the check happens inside <c>TreeNodeExV</c>.
    /// </remarks>
    public static bool TreeNodeV(string strId, string fmt, object?[] args)
        => TreeNodeExV(strId, ImGuiTreeNodeFlags.None, fmt, args);

    /// <summary>
    /// Submits a tree node from an opaque object identifier seed and an already-collected argument array.
    /// Canvases canonical <c>ImGui::TreeNodeV(const void*, const char*, va_list)</c>
    ///.
    /// </summary>
    /// <param name="ptrId">The pointer-derived identifier seed.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// Canonical is a pure forwarder to <c>TreeNodeExV(ptr_id, 0, fmt, args)</c>.
    /// </remarks>
    public static bool TreeNodeV(object? ptrId, string fmt, object?[] args)
        => TreeNodeExV(ptrId, ImGuiTreeNodeFlags.None, fmt, args);

    /// <summary>
    /// Submits a tree node with explicit behavior flags and a label that also serves as its identifier
    /// seed. Canvases canonical <c>ImGui::TreeNodeEx(const char*, ImGuiTreeNodeFlags)</c>
    ///.
    /// </summary>
    /// <param name="label">The display label, which may carry a <c>##</c> suffix that only participates in
    /// the identifier.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// The identifier is <c>GetID(label)</c> against the current window's ID stack and
    /// <c>label_end</c> is <see langword="null"/>, so the rendered range is resolved inside
    /// <see cref="TreeNodeBehavior"/> exactly as canonical.
    /// </remarks>
    public static bool TreeNodeEx(string label, ImGuiTreeNodeFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;
        uint id = GetID(label);
        return TreeNodeBehavior(id, flags, label, null);
    }

    /// <summary>
    /// Submits a flagged tree node whose identifier seed and displayed text are supplied separately, the
    /// text being produced by formatting <paramref name="fmt"/> with <paramref name="args"/>. Canvases
    /// canonical <c>ImGui::TreeNodeEx(const char*, ImGuiTreeNodeFlags, const char*,.)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier seed.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// Canonical forwards to <c>TreeNodeExV(str_id, flags, fmt, args)</c>.
    /// </remarks>
    public static bool TreeNodeEx(string strId, ImGuiTreeNodeFlags flags, string fmt, params object?[] args)
        => TreeNodeExV(strId, flags, fmt, args);

    /// <summary>
    /// Submits a flagged tree node whose identifier seed is an opaque object reference and whose displayed
    /// text is produced by formatting <paramref name="fmt"/> with <paramref name="args"/>. Canvases
    /// canonical <c>ImGui::TreeNodeEx(const void*, ImGuiTreeNodeFlags, const char*,.)</c>
    ///.
    /// </summary>
    /// <param name="ptrId">The pointer-derived identifier seed.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; see <see cref="TreeNode(string)"/> for the
    /// caller's <see cref="TreePop"/> obligation.</returns>
    /// <remarks>
    /// Canonical forwards to <c>TreeNodeExV(ptr_id, flags, fmt, args)</c>.
    /// </remarks>
    public static bool TreeNodeEx(object? ptrId, ImGuiTreeNodeFlags flags, string fmt, params object?[] args)
        => TreeNodeExV(ptrId, flags, fmt, args);

    /// <summary>
    /// Submits a flagged tree node from an identifier seed and an already-collected argument array.
    /// Canvases canonical <c>ImGui::TreeNodeExV(const char*, ImGuiTreeNodeFlags, const char*, va_list)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier seed.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; <see langword="false"/> when the current
    /// window is skipped.</returns>
    /// <remarks>
    /// <para>
    /// The canonical sequence is the window skip check, <c>window->GetID(str_id)</c>, then
    /// <c>ImFormatStringToTempBufferV</c> to resolve the label and its rendered end, then
    /// <c>TreeNodeBehavior</c>.
    /// </para>
    /// <para>
    /// The port's <see cref="ImFormatStringToTempBuffer(out string, out string, string, object?[])"/>
    /// returns the formatted text and the unconsumed tail of the canonical temp buffer; the rendered end
    /// passed to <see cref="TreeNodeBehavior"/> is therefore that buffer prefix, matching the port's
    /// resolved-range content convention for <c>textEnd</c>.
    /// </para>
    /// </remarks>
    public static bool TreeNodeExV(string strId, ImGuiTreeNodeFlags flags, string fmt, object?[] args)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        uint id = GetID(strId);
        ImFormatStringToTempBuffer(out string label, out string labelEndTail, fmt, args);
        string labelEnd = labelEndTail.Length == 0 ? label : label[..(label.Length - labelEndTail.Length)];
        return TreeNodeBehavior(id, flags, label, labelEnd);
    }

    /// <summary>
    /// Submits a flagged tree node from an opaque object identifier seed and an already-collected argument
    /// array. Canvases canonical <c>ImGui::TreeNodeExV(const void*, ImGuiTreeNodeFlags, const char*, va_list)</c>
    ///.
    /// </summary>
    /// <param name="ptrId">The pointer-derived identifier seed.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <param name="fmt">The display format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <returns><see langword="true"/> while the node is open; <see langword="false"/> when the current
    /// window is skipped.</returns>
    /// <remarks>
    /// Identical to <see cref="TreeNodeExV(string, ImGuiTreeNodeFlags, string, object?[])"/> except that
    /// the identifier comes from <c>GetID(ptr_id)</c>.
    /// </remarks>
    public static bool TreeNodeExV(object? ptrId, ImGuiTreeNodeFlags flags, string fmt, object?[] args)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        uint id = GetID(ptrId);
        ImFormatStringToTempBuffer(out string label, out string labelEndTail, fmt, args);
        string labelEnd = labelEndTail.Length == 0 ? label : label[..(label.Length - labelEndTail.Length)];
        return TreeNodeBehavior(id, flags, label, labelEnd);
    }

    /// <summary>
    /// Reads the persistent open state a tree node stored under an explicit storage identifier. Canvases
    /// canonical <c>ImGui::TreeNodeGetOpen(ImGuiID)</c>.,
    ///.
    /// </summary>
    /// <param name="storageId">The storage key, which is the item identifier unless
    /// <see cref="SetNextItemStorageID"/> overrode it.</param>
    /// <returns><see langword="true"/> when the stored value is nonzero, that is when the node is open.
    /// </returns>
    /// <remarks>
    /// The read targets the current window's per-window state storage through
    /// <c>window->DC.StateStorage</c>, with a canonical default of zero for nodes that were never
    /// toggled.
    /// </remarks>
    public static bool TreeNodeGetOpen(uint storageId)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiStorage storage = g.CurrentWindow!.DC.StateStorage!;
        return storage.GetInt(storageId, 0) != 0;
    }

    /// <summary>
    /// Writes the persistent open state of a tree node under an explicit storage identifier. Canvases
    /// canonical <c>ImGui::TreeNodeSetOpen(ImGuiID, bool)</c>.,
    ///.
    /// </summary>
    /// <param name="storageId">The storage key.</param>
    /// <param name="isOpen">The open state to store; it is written as the canonical integer one or zero.
    /// </param>
    /// <remarks>
    /// <para>
    /// The write targets the current window's per-window state storage through
    /// <c>window->DC.StateStorage</c>.
    /// </para>
    /// <para>
    /// Canonical declares this function. and forward declares it in
    ///  specifically so the demo can close a node programmatically; the
    /// port exposes it publicly for the same reason, matching how
    /// <see cref="ShowFontAtlas"/> is exposed for the demo's font section.
    /// </para>
    /// </remarks>
    public static void TreeNodeSetOpen(uint storageId, bool isOpen)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiStorage storage = g.CurrentWindow!.DC.StateStorage!;
        storage.SetInt(storageId, isOpen ? 1 : 0);
    }

    /// <summary>
    /// Resolves a tree node's open state for the current frame, consuming any state queued by
    /// <see cref="SetNextItemOpen"/> and applying the persistent value, the
    /// <see cref="ImGuiTreeNodeFlags.DefaultOpen"/> default, and the logging auto-open rule. Canvases
    /// canonical <c>ImGui::TreeNodeUpdateNextOpen(ImGuiID, ImGuiTreeNodeFlags)</c>
    ///.
    /// </summary>
    /// <param name="storageId">The storage key for the persistent open state.</param>
    /// <param name="flags">The node's behavior flags.</param>
    /// <returns>The open state the node must report for this frame.</returns>
    /// <remarks>
    /// <para>
    /// Canonical step order.: a
    /// <see cref="ImGuiTreeNodeFlags.Leaf"/> returns <see langword="true"/> immediately so its scope
    /// always exists; a queued open value with <see cref="ImGuiCond.Always"/> overwrites the stored value;
    /// a queued value with any other condition is used only while the stored value is the canonical
    /// <c>-1</c> sentinel; otherwise the stored value is read with the <c>DefaultOpen</c> default and
    /// stored back. Finally, logging forces the node open unless
    /// <see cref="ImGuiTreeNodeFlags.NoAutoOpenOnLog"/> is set and the node is deeper than
    /// <c>g.LogDepthToExpand</c>.
    /// </para>
    /// <para>
    /// <see cref="ImGuiCond.Once"/> and <see cref="ImGuiCond.FirstUseEver"/> are treated identically,
    /// because tree state is not persisted to disk.
    /// </para>
    /// </remarks>
    internal static bool TreeNodeUpdateNextOpen(uint storageId, ImGuiTreeNodeFlags flags)
    {
        // Leaf nodes always open a new tree/ID scope.
        if ((flags & ImGuiTreeNodeFlags.Leaf) != 0)
            return true;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        ImGuiStorage storage = window.DC.StateStorage!;

        bool isOpen;
        if ((g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasOpen) != 0)
        {
            if (((ImGuiCond)g.NextItemData.OpenCond & ImGuiCond.Always) != 0)
            {
                isOpen = g.NextItemData.OpenVal;
                TreeNodeSetOpen(storageId, isOpen);
            }
            else
            {
                int storedValue = storage.GetInt(storageId, -1);
                if (storedValue == -1)
                {
                    isOpen = g.NextItemData.OpenVal;
                    TreeNodeSetOpen(storageId, isOpen);
                }
                else
                {
                    isOpen = storedValue != 0;
                }
            }
        }
        else
        {
            isOpen = storage.GetInt(storageId, (flags & ImGuiTreeNodeFlags.DefaultOpen) != 0 ? 1 : 0) != 0;
        }

        // Logging auto-opens nodes below the configured expansion depth; CollapsingHeader is normally
        // exempt because it carries NoAutoOpenOnLog.
        if (g.LogEnabled && (flags & ImGuiTreeNodeFlags.NoAutoOpenOnLog) == 0
            && (window.DC.TreeDepth - g.LogDepthRef) < g.LogDepthToExpand)
        {
            isOpen = true;
        }

        return isOpen;
    }

    /// <summary>
    /// Appends an <see cref="ImGuiTreeNodeStackData"/> entry describing just-submitted node so that
    /// hierarchy lines and the "left arrow jumps to parent" navigation can be resolved by
    /// <see cref="TreePop"/>. Canvases canonical static <c>TreeNodeStoreStackData</c>
    /// </summary>
    /// <param name="flags">The node's behavior flags after the style default has been folded in.</param>
    /// <param name="x1">The arrow-column left edge, that is <c>text_pos.x - text_offset_x</c>.</param>
    /// <remarks>
    /// <para>
    /// The entry records the last item's identifier, flags, item flags, and navigation rectangle, then
    /// the line-drawing state. <c>DrawLinesX1</c> holds
    /// <c>+FLT_MAX</c> when no line is drawn, which <see cref="TreePop"/> tests as the "no lines"
    /// sentinel. Depth bookkeeping sets the bit for the current depth in
    /// <c>TreeHasStackDataDepthMask</c>, and additionally in <c>TreeRecordsClippedNodesY2Mask</c> for
    /// <see cref="ImGuiTreeNodeFlags.DrawLinesToNodes"/>.
    /// </para>
    /// <para>
    /// Canonical also records the active table column so the vertical line can be drawn into that
    /// column's channel. Tables are owned by Chunk 5
    /// (<c>g.CurrentTable</c> is not declared in the port), so <c>DrawLinesTableColumn</c> is always
    /// stored as <c>-1</c>, which is the canonical no-table value.
    /// </para>
    /// </remarks>
    private static void TreeNodeStoreStackData(ImGuiTreeNodeFlags flags, float x1)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;

        // Canonical resizes the stack and fills the new tail in place.
        ImGuiTreeNodeStackData treeNodeData = new();
        g.TreeNodeStack.Add(treeNodeData);
        treeNodeData.ID = g.LastItemData.ID;
        treeNodeData.TreeFlags = flags;
        treeNodeData.ItemFlags = g.LastItemData.ItemFlags;
        treeNodeData.NavRect = g.LastItemData.NavRect;

        bool drawLines = (flags & (ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DrawLinesToNodes)) != 0;
        treeNodeData.DrawLinesX1 = drawLines ? x1 + g.FontSize * 0.5f + g.Style.FramePadding.X : float.MaxValue;
        // Hierarchy lines are drawn in the column that holds the node's arrow.
        treeNodeData.DrawLinesTableColumn = drawLines && g.CurrentTable != null
            ? (short)g.CurrentTable.CurrentColumn
            : (short)-1;
        treeNodeData.DrawLinesToNodesY2 = -float.MaxValue;
        window.DC.TreeHasStackDataDepthMask |= (uint)(1 << window.DC.TreeDepth);
        if ((flags & ImGuiTreeNodeFlags.DrawLinesToNodes) != 0)
            window.DC.TreeRecordsClippedNodesY2Mask |= (uint)(1 << window.DC.TreeDepth);
    }

    /// <summary>
    /// Submits the tree-node item itself: layout, identifier and storage resolution, interaction,
    /// open-state toggling, hierarchy lines, arrow/bullet rendering, label rendering, and the child ID
    /// scope push. Canvases canonical <c>ImGui::TreeNodeBehavior</c>
    ///.
    /// </summary>
    /// <param name="id">The pre-computed item identifier.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <param name="label">The display label.</param>
    /// <param name="labelEnd">
    /// The canonical rendered end expressed as the resolved range content, or <see langword="null"/> to
    /// scan the label for the first <c>##</c>.
    /// </param>
    /// <returns>
    /// The node's open state after this frame's interaction, not whether it was clicked. When it is
    /// <see langword="true"/> and <see cref="ImGuiTreeNodeFlags.NoTreePushOnOpen"/> is clear, a child ID
    /// scope has been pushed and the caller must call <see cref="TreePop"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Canonical step order.: skip check; layout metrics; frame,
    /// interaction, and text rectangles; <c>ItemSize</c>; storage-identifier and open-state resolution
    /// before <c>ItemAdd</c> because that call clears the next-item data; <c>ItemAdd</c> with the display
    /// rectangle recorded; hierarchy-line bookkeeping; the invisible fast path that still pushes the
    /// child scope; button-flag construction; selection setup; <c>ButtonBehavior</c>; open-state
    /// toggling from mouse, keyboard, navigation, and drag-and-drop hold; render; then the child scope
    /// push.
    /// </para>
    /// <para>
    /// Port guards, each with its owning chunk: <c>g.CurrentTable</c> is not declared, so
    /// <see cref="ImGuiTreeNodeFlags.SpanAllColumns"/> and
    /// <see cref="ImGuiTreeNodeFlags.LabelSpanAllColumns"/> are inert and the
    /// <c>TablePush/PopBackgroundChannel</c> calls are omitted (Chunk 5). The logging decoration is
    /// applied when logging is active. <c>IMGUI_TEST_ENGINE_ITEM_INFO</c> is
    /// omitted because the port does not enable the test engine. A node inside a multi-selection scope
    /// is fully supported: the open behavior is promoted from the
    /// <c>ImGuiItemFlags_IsMultiSelect</c> item flag, and the multi-select header and footer run around
    /// <c>ButtonBehavior</c> (<c>, 7067-7077, 7124-7131</c>).
    /// </para>
    /// </remarks>
    internal static bool TreeNodeBehavior(uint id, ImGuiTreeNodeFlags flags, string label, string? labelEnd = null)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;

        // When unframed, the node grows vertically up to the typical framed widget height.
        bool displayFrame = (flags & ImGuiTreeNodeFlags.Framed) != 0;
        bool useFramePadding = displayFrame || (flags & ImGuiTreeNodeFlags.FramePadding) != 0;
        ImVec2 padding = useFramePadding
            ? style.FramePadding
            : new ImVec2(style.FramePadding.X, MathF.Min(window.DC.CurrLineTextBaseOffset, style.FramePadding.Y));

        labelEnd ??= FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false);

        float textOffsetX = g.FontSize + (displayFrame ? padding.X * 3.0f : padding.X * 2.0f);
        // Latched before ItemSize changes the current line baseline offset.
        float textOffsetY = useFramePadding
            ? MathF.Max(style.FramePadding.Y, window.DC.CurrLineTextBaseOffset)
            : window.DC.CurrLineTextBaseOffset;
        float textWidth = g.FontSize + labelSize.X + padding.X * 2.0f;
        float frameHeight = labelSize.Y + padding.Y * 2.0f;

        // SpanAllColumns and LabelSpanAllColumns take effect only inside a table; outside one they are
        // no-ops.
        bool spanAllColumns = (flags & ImGuiTreeNodeFlags.SpanAllColumns) != 0 && g.CurrentTable != null;
        bool spanAllColumnsLabel = (flags & ImGuiTreeNodeFlags.LabelSpanAllColumns) != 0 && g.CurrentTable != null;

        ImRect frameBb = new();
        frameBb.Min.X = spanAllColumns
            ? window.ParentWorkRect.Min.X
            : (flags & ImGuiTreeNodeFlags.SpanFullWidth) != 0 ? window.WorkRect.Min.X : window.DC.CursorPos.X;
        frameBb.Min.Y = window.DC.CursorPos.Y + (textOffsetY - padding.Y);
        frameBb.Max.X = spanAllColumns
            ? window.ParentWorkRect.Max.X
            : (flags & ImGuiTreeNodeFlags.SpanLabelWidth) != 0 ? window.DC.CursorPos.X + textWidth + padding.X : window.WorkRect.Max.X;
        frameBb.Max.Y = window.DC.CursorPos.Y + (textOffsetY - padding.Y) + frameHeight;
        if (displayFrame)
        {
            // A framed header expands a little outside the current limits.
            float outerExtend = TruncateToInt(window.WindowPadding.X * 0.5f);
            frameBb.Min.X -= outerExtend;
            frameBb.Max.X += outerExtend;
        }

        ImVec2 textPos = new(window.DC.CursorPos.X + textOffsetX, window.DC.CursorPos.Y + textOffsetY);
        ItemSize(new ImVec2(textWidth, frameHeight), padding.Y);

        // Regular tree nodes arbitrarily allow clicking past two worth of ItemSpacing.
        ImRect interactBb = frameBb;
        if ((flags & (ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.SpanFullWidth
                | ImGuiTreeNodeFlags.SpanLabelWidth | ImGuiTreeNodeFlags.SpanAllColumns)) == 0)
        {
            interactBb.Max.X = frameBb.Min.X + textWidth + (labelSize.X > 0.0f ? style.ItemSpacing.X * 2.0f : 0.0f);
        }

        // Open and multi-select state must be resolved before ItemAdd, which clears the next-item data.
        uint storageId = (g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasStorageID) != 0 ? g.NextItemData.StorageId : id;
        bool isOpen = TreeNodeUpdateNextOpen(storageId, flags);

        // Span-all-columns nodes widen the clip rect to ParentWorkRect around ItemAdd, then restore it,
        // so the item is tested against the full table row.
        bool isVisible;
        if (spanAllColumns || spanAllColumnsLabel)
        {
            ImRect backupClipRect = window.ClipRect;
            window.ClipRect = new ImRect(
                new ImVec2(window.ParentWorkRect.Min.X, backupClipRect.Min.Y),
                new ImVec2(window.ParentWorkRect.Max.X, backupClipRect.Max.Y));
            isVisible = ItemAdd(interactBb, id);
            window.ClipRect = backupClipRect;
        }
        else
        {
            isVisible = ItemAdd(interactBb, id);
        }
        g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HasDisplayRect;
        g.LastItemData.DisplayRect = frameBb;

        // Store stack data for the current depth so ImGuiTreeNodeFlags.NavLeftJumpsToParent can return
        // to this node from any child item.
        bool storeTreeNodeStackData = false;
        if ((flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_DrawLinesMask_) == 0)
            flags |= g.Style.TreeLinesFlags;
        bool drawTreeLines = (flags & (ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DrawLinesToNodes)) != 0
            && frameBb.Min.Y < window.ClipRect.Max.Y
            && g.Style.TreeLinesSize > 0.0f;
        if ((flags & ImGuiTreeNodeFlags.NoTreePushOnOpen) == 0)
        {
            storeTreeNodeStackData = drawTreeLines;
            if ((flags & ImGuiTreeNodeFlags.NavLeftJumpsToParent) != 0 && !g.NavIdIsAlive)
                if (g.NavMoveDir == ImGuiDir.Left && g.NavWindow == window && NavMoveRequestButNoResultYet())
                    storeTreeNodeStackData = true;
        }

        bool isLeaf = (flags & ImGuiTreeNodeFlags.Leaf) != 0;
        if (!isVisible)
        {
            // Invisible nodes still feed the clipped-node Y2 of their parent, then push their scope when
            // open so layout inside them can continue.
            if ((flags & ImGuiTreeNodeFlags.DrawLinesToNodes) != 0
                && (window.DC.TreeRecordsClippedNodesY2Mask & (uint)(1 << (window.DC.TreeDepth - 1))) != 0)
            {
                ImGuiTreeNodeStackData parentData = g.TreeNodeStack[^1];
                parentData.DrawLinesToNodesY2 = MathF.Max(parentData.DrawLinesToNodesY2, window.DC.CursorPos.Y);
                if (frameBb.Min.Y >= window.ClipRect.Max.Y)
                    window.DC.TreeRecordsClippedNodesY2Mask &= ~(uint)(1 << (window.DC.TreeDepth - 1));
            }
            if (isOpen && storeTreeNodeStackData)
                TreeNodeStoreStackData(flags, textPos.X - textOffsetX);
            if (isOpen && (flags & ImGuiTreeNodeFlags.NoTreePushOnOpen) == 0)
                TreePushOverrideID(id);
            // IMGUI_TEST_ENGINE_ITEM_INFO is omitted: the port does not enable the test engine.
            return isOpen;
        }

        // Span-all-columns nodes draw their background in the table background channel, and record the
        // clip rect so that later hit-testing matches the drawn extent.
        if (spanAllColumns || spanAllColumnsLabel)
        {
            TablePushBackgroundChannel();
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HasClipRect;
            g.LastItemData.ClipRect = window.ClipRect;
        }

        ImGuiButtonFlags buttonFlags = ImGuiButtonFlags.None;
        if ((flags & ImGuiTreeNodeFlags.AllowOverlap) != 0
            || (g.LastItemData.ItemFlags & (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_AllowOverlap) != 0)
        {
            buttonFlags |= ImGuiButtonFlags.AllowOverlap;
        }
        if (!isLeaf)
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDragDropHold;

        // Clicking the arrow section may carry keyboard modifiers, so a tree can be browsed while a
        // selection is preserved; clicking the rest of the node never does.
        float arrowHitX1 = (textPos.X - textOffsetX) - style.TouchExtraPadding.X;
        float arrowHitX2 = (textPos.X - textOffsetX) + (g.FontSize + padding.X * 2.0f) + style.TouchExtraPadding.X;
        bool isMouseXOverArrow = g.IO.MousePos.X >= arrowHitX1 && g.IO.MousePos.X < arrowHitX2;

        // A node submitted inside a multi-selection scope must keep opening and selecting apart, so when
        // the caller leaves both open-behavior flags unset the arrow and double-click behaviors are
        // adopted by default; an explicitly requested open behavior is only extended with the arrow.
        bool isMultiSelect = (g.LastItemData.ItemFlags & (ImGuiItemFlags)(int)ImGuiItemFlagsPrivate.ImGuiItemFlags_IsMultiSelect) != 0;
        if (isMultiSelect)
        {
            flags |= (flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_OpenOnMask_) == 0
                ? ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick
                : ImGuiTreeNodeFlags.OpenOnArrow;
        }

        // Open behaviors can be altered with OpenOnArrow and OpenOnDoubleClick. Arrow clicks react on
        // MouseDown rather than MouseUp, and the double-click policy still presses on release so drag and
        // drop can start on the initial press.
        if (isMouseXOverArrow)
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick;
        else if ((flags & ImGuiTreeNodeFlags.OpenOnDoubleClick) != 0)
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease
                | (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDoubleClick;
        else
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease;
        if ((flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_NoNavFocus) != 0)
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoNavFocus;

        bool selected = (flags & ImGuiTreeNodeFlags.Selected) != 0;
        bool wasSelected = selected;

        // Multi-selection support (header): resolve the visible selection for the pending clear or
        // select-all operation and the keyboard range, then adjust the button flags. Because the header
        // already establishes the press timing for a multi-select item, keyboard modifiers are not
        // suppressed; only the non-multi-select path adds NoKeyModsAllowed.
        if (isMultiSelect)
        {
            // The multi-select helper operates on the reconstructed integer button-flag word; the value is
            // converted back to the strongly typed flag set used by the rest of this function.
            int multiSelectButtonFlags = (int)buttonFlags;
            MultiSelectItemHeader(id, ref selected, ref multiSelectButtonFlags);
            buttonFlags = (ImGuiButtonFlags)multiSelectButtonFlags;

            // An arrow click that carries modifiers must still open on the initial press rather than wait
            // for release, so the release press timing installed by the header is replaced.
            if (isMouseXOverArrow)
            {
                buttonFlags = (buttonFlags | (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClick)
                    & ~(ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnClickRelease;
            }
        }
        else if (window != g.HoveredWindow || !isMouseXOverArrow)
        {
            buttonFlags |= (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_NoKeyModsAllowed;
        }

        bool pressed = ButtonBehavior(interactBb, id, out bool hovered, out bool held, buttonFlags);
        bool toggled = false;
        if (!isLeaf)
        {
            if (pressed && g.DragDropHoldJustPressedId != id)
            {
                // Single click, unless an open-on-arrow/double-click policy is in effect; navigation
                // activation always toggles.
                if ((flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_OpenOnMask_) == 0
                    || (g.NavActivateId == id && !isMultiSelect))
                {
                    toggled = true;
                }
                if ((flags & ImGuiTreeNodeFlags.OpenOnArrow) != 0)
                    toggled |= isMouseXOverArrow && !g.NavHighlightItemUnderNav;
                if ((flags & ImGuiTreeNodeFlags.OpenOnDoubleClick) != 0 && g.IO.MouseClickedCount[0] == 2)
                    toggled = true;
            }
            else if (pressed && g.DragDropHoldJustPressedId == id)
            {
                // Hold-to-open: open once and keep the node highlighted, but never close it again.
                ImAssert.Check(
                    (buttonFlags & (ImGuiButtonFlags)ImGuiButtonFlagsPrivate.ImGuiButtonFlags_PressedOnDragDropHold) != 0);
                if (!isOpen)
                    toggled = true;
                else
                    pressed = false; // Cancel the press so it does not trigger selection.
            }

            // Left closes, right opens, both consuming the navigation request.
            if (g.NavId == id && g.NavMoveDir == ImGuiDir.Left && isOpen)
            {
                toggled = true;
                NavClearPreferredPosForAxis(ImGuiAxis.X);
                NavMoveRequestCancel();
            }
            if (g.NavId == id && g.NavMoveDir == ImGuiDir.Right && !isOpen)
            {
                toggled = true;
                NavClearPreferredPosForAxis(ImGuiAxis.X);
                NavMoveRequestCancel();
            }

            if (toggled)
            {
                isOpen = !isOpen;
                window.DC.StateStorage!.SetInt(storageId, isOpen ? 1 : 0);
                g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.ToggledOpen;
            }
        }

        // Multi-selection support (footer): emit the selection requests for this node and update the
        // range and navigation tracking. A press that also toggled the node's open state must not be
        // reported to the footer as a selection press, but it still re-anchors navigation on the node.
        if (isMultiSelect)
        {
            bool pressedForFooter = pressed && !toggled;
            MultiSelectItemFooter(id, ref selected, ref pressedForFooter);
            if (pressed)
                SetNavID(id, window.DC.NavLayerCurrent, g.CurrentFocusScopeId, interactBb);
        }

        if (selected != wasSelected)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.ToggledSelection;

        // Render.
        {
            uint textCol = GetColorU32(ImGuiCol.Text);
            ImGuiNavRenderCursorFlags navRenderCursorFlags = ImGuiNavRenderCursorFlags.Compact;
            if (isMultiSelect)
                navRenderCursorFlags |= ImGuiNavRenderCursorFlags.AlwaysDraw;

            if (displayFrame)
            {
                // Framed type.
                uint bgCol = GetColorU32((held && hovered) ? ImGuiCol.HeaderActive : hovered ? ImGuiCol.HeaderHovered : ImGuiCol.Header);
                RenderFrame(frameBb.Min, frameBb.Max, bgCol, true, style.FrameRounding);
                RenderNavCursor(frameBb, id, navRenderCursorFlags);
                if (spanAllColumns && !spanAllColumnsLabel)
                    TablePopBackgroundChannel();
                if ((flags & ImGuiTreeNodeFlags.Bullet) != 0)
                {
                    RenderBullet(window.DrawList,
                        new ImVec2(textPos.X - textOffsetX * 0.60f, textPos.Y + g.FontSize * 0.5f), textCol);
                }
                else if (!isLeaf)
                {
                    ImGuiDir arrowDir = isOpen
                        ? ((flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_UpsideDownArrow) != 0 ? ImGuiDir.Up : ImGuiDir.Down)
                        : ImGuiDir.Right;
                    RenderArrow(window.DrawList, new ImVec2(textPos.X - textOffsetX + padding.X, textPos.Y), textCol, arrowDir, 1.0f);
                }
                else
                {
                    // Leaf without a bullet uses left-adjusted text.
                    textPos.X -= textOffsetX - padding.X;
                }
                if ((flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_ClipLabelForTrailingButton) != 0)
                    frameBb.Max.X -= g.FontSize + style.FramePadding.X;
                // Canonical latches the frame decoration while logging is active.
                if (g.LogEnabled)
                    LogSetNextTextDecoration("###", "###"); //.
            }
            else
            {
                // Unframed type for plain tree nodes.
                if (hovered || selected)
                {
                    uint bgCol = GetColorU32((held && hovered) ? ImGuiCol.HeaderActive : hovered ? ImGuiCol.HeaderHovered : ImGuiCol.Header);
                    RenderFrame(frameBb.Min, frameBb.Max, bgCol, false, 0.0f);
                }
                RenderNavCursor(frameBb, id, navRenderCursorFlags, 0.0f);
                if (spanAllColumns && !spanAllColumnsLabel)
                    TablePopBackgroundChannel();
                if ((flags & ImGuiTreeNodeFlags.Bullet) != 0)
                {
                    RenderBullet(window.DrawList,
                        new ImVec2(textPos.X - textOffsetX * 0.5f, textPos.Y + g.FontSize * 0.5f), textCol);
                }
                else if (!isLeaf)
                {
                    ImGuiDir arrowDir = isOpen
                        ? ((flags & (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_UpsideDownArrow) != 0 ? ImGuiDir.Up : ImGuiDir.Down)
                        : ImGuiDir.Right;
                    RenderArrow(window.DrawList,
                        new ImVec2(textPos.X - textOffsetX + padding.X, textPos.Y + g.FontSize * 0.15f), textCol, arrowDir, 0.70f);
                }
                // Canonical latches the collapsed-marker decoration while logging is active.
                if (g.LogEnabled)
                    LogSetNextTextDecoration(">", null); //.
            }

            // Horizontal line from the parent node into this visible child.
            if (drawTreeLines)
                TreeNodeDrawLineToChildNode(new ImVec2(textPos.X - textOffsetX + padding.X, textPos.Y + g.FontSize * 0.5f));

            // Label.
            if (displayFrame)
                RenderTextClipped(textPos, frameBb.Max, label, labelEnd, labelSize);
            else
                RenderText(textPos, label, labelEnd, false);

            if (spanAllColumnsLabel)
                TablePopBackgroundChannel();
        }

        // Push the child scope after rendering so hierarchy lines were drawn above it.
        if (isOpen && storeTreeNodeStackData)
            TreeNodeStoreStackData(flags, textPos.X - textOffsetX);
        if (isOpen && (flags & ImGuiTreeNodeFlags.NoTreePushOnOpen) == 0)
            TreePushOverrideID(id);

        // IMGUI_TEST_ENGINE_ITEM_INFO is omitted: the port does not enable the test engine.
        return isOpen;
    }

    /// <summary>
    /// Draws the horizontal hierarchy line from the parent node's arrow column into a visible child node
    /// and records the child's Y coordinate on the parent's stack entry. Canvases canonical
    /// <c>ImGui::TreeNodeDrawLineToChildNode</c>.,
    ///.
    /// </summary>
    /// <param name="targetPos">The child position the line points at, normally the arrow center.</param>
    /// <remarks>
    /// The function returns early when there is no parent depth or the parent's
    /// <c>TreeHasStackDataDepthMask</c> bit is clear. All
    /// coordinates are truncated toward zero; when rounding is enabled the line ends in a quarter arc on
    /// the parent's vertical line, otherwise it is a plain horizontal segment
    /// </remarks>
    internal static void TreeNodeDrawLineToChildNode(ImVec2 targetPos)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (window.DC.TreeDepth == 0
            || (window.DC.TreeHasStackDataDepthMask & (uint)(1 << (window.DC.TreeDepth - 1))) == 0)
        {
            return;
        }

        ImGuiTreeNodeStackData parentData = g.TreeNodeStack[^1];
        float x1 = TruncateToInt(parentData.DrawLinesX1);
        float x2 = TruncateToInt(targetPos.X - g.Style.ItemInnerSpacing.X);
        float y = TruncateToInt(targetPos.Y);
        float rounding = g.Style.TreeLinesRounding > 0.0f ? MathF.Min(x2 - x1, g.Style.TreeLinesRounding) : 0.0f;
        parentData.DrawLinesToNodesY2 = MathF.Max(parentData.DrawLinesToNodesY2, y - rounding);
        if (x1 >= x2)
            return;

        if (rounding > 0.0f)
        {
            // The arc is centered one rounding radius above the line end; PathArcToFast covers the
            // quarter turn from the 6 o'clock to the 3 o'clock clock position.
            x1 += 0.5f + rounding;
            window.DrawList.PathArcToFast(new ImVec2(x1, y - rounding), rounding, 6, 3);
            if (x1 < x2)
                window.DrawList.PathLineTo(new ImVec2(x2, y));
            window.DrawList.PathStroke(GetColorU32(ImGuiCol.TreeLines), g.Style.TreeLinesSize);
        }
        else
        {
            window.DrawList.AddLineH(x1, x2, y, GetColorU32(ImGuiCol.TreeLines), g.Style.TreeLinesSize);
        }
    }

    /// <summary>
    /// Draws the vertical hierarchy line belonging to one tree node, from its navigation rectangle down
    /// to the cursor position or to the bottom-most recorded child. Canvases canonical
    /// <c>ImGui::TreeNodeDrawLineToTreePop</c>.,
    ///.
    /// </summary>
    /// <param name="data">The stack entry of the node being closed, carrying its line geometry.</param>
    /// <remarks>
    /// <para>
    /// The top of the line is clamped to the visible clip region, and the bottom is first narrowed by
    /// <c>DrawLinesFull</c> toward the current cursor, then clamped to the clip rectangle; the function
    /// returns without drawing when the range is empty.
    /// </para>
    /// <para>
    /// Two canonical branches are guarded inert because tables are owned by Chunk 5: the
    /// <c>g.CurrentTable</c> extension of the full-line bottom to <c>RowPosY2</c>
    ///., and the
    /// <c>TablePushColumnChannel</c>/<c>TablePopColumnChannel</c> wrapper around the draw call. The stored <c>DrawLinesTableColumn</c> is always <c>-1</c>, which is the
    /// canonical no-table value, so both branches are unreachable in canonical as well.
    /// </para>
    /// </remarks>
    internal static void TreeNodeDrawLineToTreePop(ImGuiTreeNodeStackData data)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        float y1 = MathF.Max(data.NavRect.Max.Y, window.ClipRect.Min.Y);
        float y2 = data.DrawLinesToNodesY2;
        if ((data.TreeFlags & ImGuiTreeNodeFlags.DrawLinesFull) != 0)
        {
            float y2Full = window.DC.CursorPos.Y;
            // Inside a table the full line extends to the bottom of the current row.
            if (g.CurrentTable != null)
                y2Full = MathF.Max(g.CurrentTable.RowPosY2, y2Full);
            y2Full = TruncateToInt(y2Full - g.Style.ItemSpacing.Y - g.FontSize * 0.5f);
            if (y2 + (g.Style.ItemSpacing.Y + g.Style.TreeLinesRounding) < y2Full)
                y2 = y2Full;
        }
        y2 = MathF.Min(y2, window.ClipRect.Max.Y);
        if (y2 <= y1)
            return;

        // The line is drawn in the column that owns the arrow, so table clipping applies to it
        float x = TruncateToInt(data.DrawLinesX1);
        if (data.DrawLinesTableColumn != -1)
            TablePushColumnChannel(data.DrawLinesTableColumn);
        window.DrawList.AddLineV(x, y1, y2, GetColorU32(ImGuiCol.TreeLines), g.Style.TreeLinesSize);
        if (data.DrawLinesTableColumn != -1)
            TablePopColumnChannel();
    }

    /// <summary>
    /// Opens a child scope addressed by a string identifier: indents the layout cursor, increases the tree
    /// depth, and pushes the identifier. Canvases canonical <c>ImGui::TreePush(const char*)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier seed pushed onto the ID stack.</param>
    /// <remarks>
    /// <see cref="TreeNode(string)"/> already performs this push when it returns true; the explicit form
    /// exists for callers that want to open a scope without a visible node. The matching call is
    /// <see cref="TreePop"/>.
    /// </remarks>
    public static void TreePush(string strId)
    {
        ImGuiWindow window = GetCurrentWindow();
        Indent();
        window.DC.TreeDepth++;
        PushID(strId);
    }

    /// <summary>
    /// Opens a child scope addressed by an opaque object reference: indents the layout cursor, increases
    /// the tree depth, and pushes the pointer-derived identifier. Canvases canonical
    /// <c>ImGui::TreePush(const void*)</c>.,
    ///.
    /// </summary>
    /// <param name="ptrId">The reference whose identity seeds the pushed identifier.</param>
    /// <remarks>
    /// The matching call is <see cref="TreePop"/>.
    /// </remarks>
    public static void TreePush(object? ptrId)
    {
        ImGuiWindow window = GetCurrentWindow();
        Indent();
        window.DC.TreeDepth++;
        PushID(ptrId);
    }

    /// <summary>
    /// Opens a child scope from an already-computed identifier, avoiding a second identifier hash for a
    /// node whose identifier is known. Canvases canonical <c>ImGui::TreePushOverrideID</c>
    ///.
    /// </summary>
    /// <param name="id">The exact identifier to push onto the ID stack.</param>
    /// <remarks>
    /// <see cref="TreeNodeBehavior"/> uses this form because it already computed the item identifier
    ///. The matching call is <see cref="TreePop"/>.
    /// </remarks>
    internal static void TreePushOverrideID(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        Indent();
        window.DC.TreeDepth++;
        PushOverrideID(id);
    }

    /// <summary>
    /// Closes the innermost child scope opened by a tree node or <c>TreePush</c>: unindents, decreases the
    /// tree depth, resolves the saved navigation and hierarchy-line state, then pops the identifier.
    /// Canvases canonical <c>ImGui::TreePop</c>.,
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the closed depth carries stack data (<c>TreeHasStackDataDepthMask</c> bit set), the function
    /// asserts that the saved identifier matches the top of the window ID stack, resolves a pending Left
    /// navigation request against the saved node for
    /// <see cref="ImGuiTreeNodeFlags.NavLeftJumpsToParent"/>, draws the vertical hierarchy line when the
    /// node stored one and the cursor is below the clip top, then removes the entry and clears both depth
    /// masks.
    /// </para>
    /// <para>
    /// The final assertion documents that a window always keeps one base identifier on its ID stack, so
    /// the identifier pop is always balanced.
    /// </para>
    /// </remarks>
    public static void TreePop()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        Unindent();

        window.DC.TreeDepth--;
        uint treeDepthMask = (uint)(1 << window.DC.TreeDepth);

        if ((window.DC.TreeHasStackDataDepthMask & treeDepthMask) != 0)
        {
            ImGuiTreeNodeStackData data = g.TreeNodeStack[^1];
            ImAssert.Check(data.ID == window.IDStack[^1]);

            // Left arrow moves to the parent tree node when the flag is enabled.
            if ((data.TreeFlags & ImGuiTreeNodeFlags.NavLeftJumpsToParent) != 0)
                if (g.NavIdIsAlive && g.NavMoveDir == ImGuiDir.Left && g.NavWindow == window && NavMoveRequestButNoResultYet())
                    NavMoveRequestResolveWithPastTreeNode(g.NavMoveResultLocal, data);

            // Draw hierarchy lines.
            if (data.DrawLinesX1 != float.MaxValue && window.DC.CursorPos.Y >= window.ClipRect.Min.Y)
                TreeNodeDrawLineToTreePop(data);

            g.TreeNodeStack.RemoveAt(g.TreeNodeStack.Count - 1);
            window.DC.TreeHasStackDataDepthMask &= ~treeDepthMask;
            window.DC.TreeRecordsClippedNodesY2Mask &= ~treeDepthMask;
        }

        ImAssert.Check(window.IDStack.Count > 1);
        PopID();
    }

    /// <summary>
    /// Returns the horizontal distance preceding a label in a regular unframed tree node, which is also
    /// the advance performed by <see cref="Bullet"/>. Canvases canonical
    /// <c>ImGui::GetTreeNodeToLabelSpacing</c>.,
    ///.
    /// </summary>
    /// <returns>The font size plus twice the horizontal frame padding.</returns>
    /// <remarks>
    /// The value is exactly <c>g.FontSize + style.FramePadding.x * 2</c>.
    /// </remarks>
    public static float GetTreeNodeToLabelSpacing()
    {
        ImGuiContext g = GetCurrentContext()!;
        return g.FontSize + g.Style.FramePadding.X * 2.0f;
    }

    /// <summary>
    /// Submits a collapsing header, which is a framed tree node that neither indents nor pushes an
    /// identifier scope. Canvases canonical <c>ImGui::CollapsingHeader(const char*, ImGuiTreeNodeFlags)</c>
    ///.
    /// </summary>
    /// <param name="label">The display label, which also seeds the identifier.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <returns><see langword="true"/> while the header is open. The caller must not call
    /// <see cref="TreePop"/>, because <see cref="ImGuiTreeNodeFlags.CollapsingHeader"/> includes
    /// <see cref="ImGuiTreeNodeFlags.NoTreePushOnOpen"/>.</returns>
    /// <remarks>
    /// The flag composition <c>Framed | NoTreePushOnOpen | NoAutoOpenOnLog</c> is folded in before the
    /// <see cref="TreeNodeBehavior"/> call.
    /// </remarks>
    public static bool CollapsingHeader(string label, ImGuiTreeNodeFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;
        uint id = GetID(label);
        return TreeNodeBehavior(id, flags | ImGuiTreeNodeFlags.CollapsingHeader, label);
    }

    /// <summary>
    /// Submits a collapsing header with a visibility toggle: the header is drawn only while
    /// <paramref name="pVisible"/> is true, and the canonical trailing close button clears it. Canvases
    /// canonical <c>ImGui::CollapsingHeader(const char*, bool*, ImGuiTreeNodeFlags)</c>
    ///.
    /// </summary>
    /// <param name="label">The display label, which also seeds the identifier.</param>
    /// <param name="pVisible">The visibility state. The canonical null-pointer case is represented by the
    /// separate <see cref="CollapsingHeader(string, ImGuiTreeNodeFlags)"/> overload.</param>
    /// <param name="flags">Behavior flags composed from <see cref="ImGuiTreeNodeFlags"/>.</param>
    /// <returns><see langword="false"/> when the window is skipped or <paramref name="pVisible"/> is false;
    /// otherwise the header's open state. The caller must not call <see cref="TreePop"/>.</returns>
    /// <remarks>
    /// <para>
    /// The identifier is the header label; the close button uses
    /// <c>GetIDWithSeed("#CLOSE", NULL, id)</c>.
    /// </para>
    /// <para>
    /// After the header is submitted, canonical snapshots <c>g.LastItemData</c>, places the small close
    /// button at the header's trailing edge, consumes the click by clearing
    /// <paramref name="pVisible"/>, and restores the snapshot so the header remains the item reported to
    /// the caller. The port's <see cref="ImGuiLastItemData"/> is a
    /// reference type, so the snapshot and the restore both go through
    /// <see cref="ImGuiLastItemData.CopyFrom(ImGuiLastItemData)"/>; a plain assignment would alias the
    /// live record. The helper is part 02's <c>CloseButton</c>
    /// (.).
    /// </para>
    /// </remarks>
    public static bool CollapsingHeader(string label, ref bool pVisible, ImGuiTreeNodeFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        if (!pVisible)
            return false;

        uint id = GetID(label);
        flags |= ImGuiTreeNodeFlags.CollapsingHeader;
        // Canonical reserves trailing space for the close button it submits below.
        flags |= ImGuiTreeNodeFlags.AllowOverlap
            | (ImGuiTreeNodeFlags)ImGuiTreeNodeFlagsPrivate.ImGuiTreeNodeFlags_ClipLabelForTrailingButton;
        bool isOpen = TreeNodeBehavior(id, flags, label);

        // Create the small overlapping close button that clears the caller's visibility flag. The
        // canonical local `ImGuiLastItemData last_item_backup = g.LastItemData` is a value copy; the port
        // record is a reference type, so it is snapshotted field-by-field and restored the same way.
        ImGuiContext g = GetCurrentContext()!;
        ImGuiLastItemData lastItemBackup = new();
        lastItemBackup.CopyFrom(g.LastItemData);
        float buttonSize = g.FontSize;
        float buttonX = MathF.Max(g.LastItemData.Rect.Min.X,
            g.LastItemData.Rect.Max.X - g.Style.FramePadding.X - buttonSize);
        float buttonY = g.LastItemData.Rect.Min.Y + g.Style.FramePadding.Y;
        uint closeButtonId = GetIDWithSeed("#CLOSE", null, id);
        if (CloseButton(closeButtonId, new ImVec2(buttonX, buttonY)))
            pVisible = false;
        g.LastItemData.CopyFrom(lastItemBackup);

        return isOpen;
    }

    #endregion

    #endregion


    #region SECTION: MENUS

    #region Menu columns

    /// <summary>
    /// Commits the column widths measured during the previous frame and resets the per-frame width
    /// accumulators. Canvases canonical <c>ImGuiMenuColumns::Update</c>
    ///.
    /// </summary>
    /// <param name="menuColumns">The menu-column state owned by the current window's temporary data.</param>
    /// <param name="spacing">Horizontal spacing inserted between non-empty columns, in pixels.</param>
    /// <param name="windowReappearing">
    /// True when the owning window was just activated, which discards the pending measurements so a
    /// freshly shown menu does not inherit the previous showing's offsets.
    /// </param>
    /// <remarks>
    /// <para>
    /// The canonical method is an instance member of <c>ImGuiMenuColumns</c>. This port realizes it as an
    /// extension method on the same type so that the part file keeps the single-top-level-type shape required
    /// by CONTRACT.md §1 while the canonical call syntax
    /// <c>window.DC.MenuColumns.Update(style.ItemSpacing.x, windowJustActivatedByUser)</c> remains valid at
    /// the deferred call site in <c>ImGui.cs:8172-8173</c>. The method is found without a <c>using</c>
    /// directive because the declaring type and the receiver type share the <c>Hikari.ImGui</c> namespace.
    /// </para>
    /// <para>
    /// Step order follows canonical: clear on reappearance, latch <c>Spacing</c>, lock the offsets from the
    /// previous frame's widths through <see cref="CalcNextTotalWidth"/>, clear the accumulators, publish
    /// <c>TotalWidth</c>, and reset <c>NextTotalWidth</c>.
    /// </para>
    /// <para>
    /// Canonical casts the floating-point spacing with <c>(ImU16)</c> and clears the width array with
    /// <c>memset</c>; this port truncates through <c>int</c> for a deterministic result and clears the managed
    /// array with <see cref="System.Array.Clear(Array, int, int)"/>.
    /// </para>
    /// </remarks>
    internal static void Update(this ImGuiMenuColumns menuColumns, float spacing, bool windowReappearing)
    {
        if (windowReappearing)
            System.Array.Clear(menuColumns.Widths, 0, menuColumns.Widths.Length);
        menuColumns.Spacing = (ushort)(int)spacing;
        menuColumns.CalcNextTotalWidth(true);
        System.Array.Clear(menuColumns.Widths, 0, menuColumns.Widths.Length);
        menuColumns.TotalWidth = menuColumns.NextTotalWidth;
        menuColumns.NextTotalWidth = 0;
    }

    /// <summary>
    /// Measures the total width of the four menu columns from the accumulated widths, optionally latching
    /// the label, shortcut, and mark offsets. Canvases canonical <c>ImGuiMenuColumns::CalcNextTotalWidth</c>
    ///.
    /// </summary>
    /// <param name="menuColumns">The menu-column state to measure.</param>
    /// <param name="updateOffsets">
    /// True when called from the per-frame update pass, which locks <c>OffsetLabel</c>,
    /// <c>OffsetShortcut</c>, and <c>OffsetMark</c> from this frame's widths; false while background widgets
    /// are still accumulating widths.
    /// </param>
    /// <remarks>
    /// The canonical method is an instance member of <c>ImGuiMenuColumns</c>; see <see cref="Update"/> for
    /// why this port declares it as an extension method. The horizontal spacing is inserted before every
    /// non-empty column except the first, and <c>OffsetIcon</c> intentionally stays zero because the icon
    /// column is always the leftmost one.
    /// </remarks>
    internal static void CalcNextTotalWidth(this ImGuiMenuColumns menuColumns, bool updateOffsets)
    {
        ushort offset = 0;
        bool wantSpacing = false;
        for (int i = 0; i < menuColumns.Widths.Length; i++)
        {
            ushort width = menuColumns.Widths[i];
            if (wantSpacing && width > 0)
                offset += menuColumns.Spacing;
            wantSpacing |= width > 0;
            if (updateOffsets)
            {
                if (i == 1)
                    menuColumns.OffsetLabel = offset;
                if (i == 2)
                    menuColumns.OffsetShortcut = offset;
                if (i == 3)
                    menuColumns.OffsetMark = offset;
            }
            offset += width;
        }
        menuColumns.NextTotalWidth = offset;
    }

    /// <summary>
    /// Declares this frame's icon, label, shortcut, and mark column widths and returns the width the calling
    /// menu entry must reserve. Canvases canonical <c>ImGuiMenuColumns::DeclColumns</c>
    ///.
    /// </summary>
    /// <param name="menuColumns">The menu-column state to grow.</param>
    /// <param name="wIcon">Measured icon column width, or zero when the entry has no icon.</param>
    /// <param name="wLabel">Measured label column width.</param>
    /// <param name="wShortcut">Measured shortcut column width, or zero when the entry has no shortcut.</param>
    /// <param name="wMark">Reserved checkmark column width, always accounted for because selection has no
    /// neutral state.</param>
    /// <returns>The larger of the locked total width and the accumulated width so far, which the caller uses
    /// as the entry's minimum width.</returns>
    /// <remarks>
    /// The canonical method is an instance member of <c>ImGuiMenuColumns</c>; see <see cref="Update"/> for
    /// why this port declares it as an extension method. Each column keeps a per-frame maximum and the new
    /// maximum is folded through <see cref="CalcNextTotalWidth"/> without locking offsets, so entries
    /// submitted later in the same frame still see the growing width.
    /// </remarks>
    internal static float DeclColumns(this ImGuiMenuColumns menuColumns, float wIcon, float wLabel, float wShortcut, float wMark)
    {
        menuColumns.Widths[0] = System.Math.Max(menuColumns.Widths[0], (ushort)(int)wIcon);
        menuColumns.Widths[1] = System.Math.Max(menuColumns.Widths[1], (ushort)(int)wLabel);
        menuColumns.Widths[2] = System.Math.Max(menuColumns.Widths[2], (ushort)(int)wShortcut);
        menuColumns.Widths[3] = System.Math.Max(menuColumns.Widths[3], (ushort)(int)wMark);
        menuColumns.CalcNextTotalWidth(false);
        return (float)System.Math.Max(menuColumns.TotalWidth, menuColumns.NextTotalWidth);
    }

    #endregion

    #region Menu bar

    /// <summary>
    /// Begins appending entries to the current window's menu bar. Canvases canonical
    /// <c>ImGui::BeginMenuBar</c>., declared in
    /// </summary>
    /// <returns>True when the menu bar is being appended and <see cref="EndMenuBar"/> must be called.</returns>
    /// <remarks>
    /// <para>
    /// The window must carry <see cref="ImGuiWindowFlags.MenuBar"/>; otherwise the call reports false and no
    /// state is changed. The horizontal menu geometry itself is
    /// established during <c>Begin()</c> through <c>window.DC.MenuBarOffset</c> and
    /// <c>window.MenuBarHeight</c> (, port <c>ImGui.cs:7764-7767</c>), so this
    /// function only claims the clip rectangle, switches the layout to horizontal, and selects the menu
    /// navigation layer.
    /// </para>
    /// <para>
    /// The clip rectangle removes half the border and the window rounding so long menus do not paint over the
    /// lower-right rounded corner. Canonical rounds with
    /// <c>IM_ROUND</c> and floors with <c>ImFloor</c>; both are expressed here with the equivalent
    /// <c>(float)(int)(value + 0.5f)</c> and <see cref="System.MathF.Floor(float)"/> forms.
    /// </para>
    /// <para>
    /// Canonical wraps the bar in <c>BeginGroup()</c>/<c>EndGroup()</c> so the parent window's cursor is
    /// restored; those two helpers belong to the deferred group/disabled chunk (Chunk 10) and are called
    /// here as canonical requires. The <c>IM_ASSERT(!window-&gt;DC.MenuBarAppending)</c> precondition is a
    /// debug assertion. Tab bars and columns have no canonical role inside this
    /// function.
    /// </para>
    /// </remarks>
    public static bool BeginMenuBar()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;
        if ((window.Flags & ImGuiWindowFlags.MenuBar) == 0)
            return false;

        ImAssert.Check(!window.DC.MenuBarAppending, "BeginMenuBar() called while a menu bar is already being appended."); // Canonical IM_ASSERT.
        BeginGroup(); // Chunk 10 owns BeginGroup/EndGroup; canonical uses the group to restore layer-0 position.
        PushID("##MenuBar");

        // We don't clip with the current window clipping rectangle as it is already set to the area below.
        // However we clip with the full window rectangle.
        float borderTop = System.MathF.Max((float)(int)(window.WindowBorderSize * 0.5f - window.TitleBarHeight + 0.5f), 0.0f);
        float borderHalf = (float)(int)(window.WindowBorderSize * 0.5f + 0.5f);
        ImRect barRect = window.MenuBarRect();
        ImRect clipRect = new ImRect(
            System.MathF.Floor(barRect.Min.X + borderHalf),
            System.MathF.Floor(barRect.Min.Y + borderTop),
            System.MathF.Floor(System.MathF.Max(barRect.Min.X, barRect.Max.X - System.MathF.Max(window.WindowRounding, borderHalf))),
            System.MathF.Floor(barRect.Max.Y));
        clipRect.ClipWith(window.OuterRectClipped);
        PushClipRect(clipRect.Min, clipRect.Max, false);

        // Overwrite CursorMaxPos because BeginGroup() set it to CursorPos: the bar claims its own space.
        window.DC.CursorPos = window.DC.CursorMaxPos = new ImVec2(barRect.Min.X + window.DC.MenuBarOffset.X, barRect.Min.Y + window.DC.MenuBarOffset.Y);
        window.DC.LayoutType = ImGuiLayoutType.Horizontal;
        window.DC.IsSameLine = false;
        window.DC.NavLayerCurrent = ImGuiNavLayer.Menu;
        window.DC.MenuBarAppending = true;
        AlignTextToFramePadding();
        return true;
    }

    /// <summary>
    /// Ends appending entries to the current window's menu bar and restores the vertical layout and main
    /// navigation layer. Canvases canonical <c>ImGui::EndMenuBar</c>
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Navigation handling comes first: when a move request inside one of the bar's child menus produced no
    /// result and the navigation window is a child menu, the bar claims focus back, restores its menu-layer
    /// navigation identifier, hides the navigation cursor for the transition frame, and forwards the move
    /// request so the sibling menu is scored as if it were a direct navigation move
    ///. Otherwise the request is offered to horizontal wrapping.
    /// </para>
    /// <para>
    /// The group reconciliation then converts the bar's horizontal extents into the parent's ideal maximum
    /// width, ends the group, and restores the vertical layout and main navigation layer
    ///. The two <c>IM_ASSERT</c> preconditions and the
    /// menu-layer-mask sanity assertion are debug assertions.
    /// </para>
    /// <para>
    /// The group stack is inspected directly because canonical disables item emission for the bar's group
    /// before ending it.; <c>EndGroup()</c> itself belongs to Chunk 10.
    /// </para>
    /// </remarks>
    public static void EndMenuBar()
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return;
        ImGuiContext g = GetCurrentContext()!;

        ImAssert.Check((window.Flags & ImGuiWindowFlags.MenuBar) != 0, "EndMenuBar() called on a window without ImGuiWindowFlags_MenuBar."); // Canonical IM_ASSERT.
        ImAssert.Check(window.DC.MenuBarAppending, "EndMenuBar() called without a matching BeginMenuBar()."); // Canonical IM_ASSERT.

        ImGuiWindow? navWindow = g.NavWindow;
        if (NavMoveRequestButNoResultYet() && (g.NavMoveDir == ImGuiDir.Left || g.NavMoveDir == ImGuiDir.Right)
            && navWindow != null && (navWindow.Flags & ImGuiWindowFlags.ChildMenu) != 0)
        {
            // Walk to the earliest child menu so the request can be re-scored against this bar's siblings.
            ImGuiWindow navEarliestChild = navWindow;
            while (navEarliestChild.ParentWindow != null && (navEarliestChild.ParentWindow.Flags & ImGuiWindowFlags.ChildMenu) != 0)
                navEarliestChild = navEarliestChild.ParentWindow;
            if (navEarliestChild.ParentWindow == window && navEarliestChild.DC.ParentLayoutType == ImGuiLayoutType.Horizontal
                && (g.NavMoveFlags & ImGuiNavMoveFlags.Forwarded) == 0)
            {
                const ImGuiNavLayer layer = ImGuiNavLayer.Menu;
                ImAssert.Check((window.DC.NavLayersActiveMaskNext & (1 << (int)layer)) != 0, "The menu navigation layer must be active for the bar."); // Canonical IM_ASSERT.
                FocusWindow(window);
                SetNavID(window.NavLastIds[(int)layer], layer, 0, window.NavRectRel[(int)layer]);
                if (g.NavCursorVisible)
                {
                    g.NavCursorVisible = false; // Hide the cursor for the transition frame so the intermediary selection is not shown.
                    g.NavCursorHideFrames = 2;
                }
                g.NavHighlightItemUnderNav = g.NavMousePosDirty = true;
                NavMoveRequestForward(g.NavMoveDir, g.NavMoveClipDir, g.NavMoveFlags, g.NavMoveScrollFlags);
            }
        }
        else
        {
            NavMoveRequestTryWrapping(window, ImGuiNavMoveFlags.WrapX);
        }

        PopClipRect();
        PopID();
        window.DC.MenuBarOffset = new ImVec2(window.DC.CursorPos.X - window.Pos.X, window.DC.MenuBarOffset.Y); // Save the horizontal position for the next append.

        ImGuiGroupData groupData = g.GroupStack[g.GroupStack.Count - 1];
        groupData.EmitItem = false;
        ImVec2 restoreCursorMaxPos = groupData.BackupCursorMaxPos;
        window.DC.IdealMaxPos = new ImVec2(System.MathF.Max(window.DC.IdealMaxPos.X, window.DC.CursorMaxPos.X - window.Scroll.X), window.DC.IdealMaxPos.Y); // Convert the bar's ideal extents for the scrolling layer.
        EndGroup(); // Chunk 10 owns BeginGroup/EndGroup.
        window.DC.LayoutType = ImGuiLayoutType.Vertical;
        window.DC.IsSameLine = false;
        window.DC.NavLayerCurrent = ImGuiNavLayer.Main;
        window.DC.MenuBarAppending = false;
        window.DC.CursorMaxPos = restoreCursorMaxPos;
    }

    /// <summary>
    /// Opens a full-side window docked to an edge of a viewport's work area. Mirrors canonical
    /// <c>ImGui::BeginViewportSideBar</c>.
    /// </summary>
    /// <param name="name">Window name; canonical uses <c>"##MainMenuBar"</c> for the main menu bar.</param>
    /// <param name="viewport">Host viewport, or <see langword="null"/> to use the main viewport.</param>
    /// <param name="dir">Edge the bar is docked to; <see cref="ImGuiDir.None"/> is rejected.</param>
    /// <param name="axisSize">Bar thickness along its docking axis.</param>
    /// <param name="windowFlags">Additional window flags, combined with the bar-mandated flags.</param>
    /// <returns><see langword="true"/> when the bar window is open and <see cref="End"/> must be called.</returns>
    /// <remarks>
    /// The bar's rectangle is cut from the viewport's building work rectangle and reported back into the
    /// viewport insets so the remaining work area shrinks on the next frame. The viewport is enforced
    /// with <see cref="SetNextWindowViewport"/> so the bar never creates its own platform window when
    /// <c>io.ConfigViewportsNoAutoMerge</c> is set.
    /// </remarks>
    internal static bool BeginViewportSideBar(string name, ImGuiViewport? viewport, ImGuiDir dir, float axisSize, ImGuiWindowFlags windowFlags)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(dir != ImGuiDir.None, "A viewport side bar must have a direction.");
        ImGuiWindow? barWindow = FindWindowByName(name);
        ImGuiViewportP viewportP = (ImGuiViewportP)(viewport ?? GetMainViewport());
        if (barWindow == null || barWindow.BeginCount == 0)
        {
            // Compute the bar rectangle inside the viewport's building work rectangle.
            ImRect availRect = viewportP.GetBuildWorkRect();
            ImGuiAxis axis = dir == ImGuiDir.Up || dir == ImGuiDir.Down ? ImGuiAxis.Y : ImGuiAxis.X;
            ImVec2 pos = availRect.Min;
            if (dir == ImGuiDir.Right || dir == ImGuiDir.Down)
                pos[axis] = availRect.Max[axis] - axisSize;
            ImVec2 size = availRect.GetSize();
            size[axis] = axisSize;
            SetNextWindowPos(pos);
            SetNextWindowSize(size);

            // Report the bar size into the work area for the next frame, using the requested size.
            if (dir == ImGuiDir.Up || dir == ImGuiDir.Left)
            {
                ImVec2 insetMin = viewportP.BuildWorkInsetMin;
                insetMin[axis] += axisSize;
                viewportP.BuildWorkInsetMin = insetMin;
            }
            else if (dir == ImGuiDir.Down || dir == ImGuiDir.Right)
            {
                ImVec2 insetMax = viewportP.BuildWorkInsetMax;
                insetMax[axis] += axisSize;
                viewportP.BuildWorkInsetMax = insetMax;
            }
        }

        windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
        SetNextWindowViewport(viewportP.ID); // Enforce the viewport so the bar cannot create its own.
        PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        PushStyleVar(ImGuiStyleVar.WindowMinSize, new ImVec2(0.0f, 0.0f)); // Lift the normal size constraint.
        bool isOpen = Begin(name, windowFlags);
        PopStyleVar(2);
        return isOpen;
    }

    /// <summary>
    /// Opens the full-screen main menu bar on the main viewport and begins its menu bar. Canvases canonical
    /// <c>ImGui::BeginMainMenuBar</c>., declared in
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the bar window is open and <see cref="EndMainMenuBar"/> must be called;
    /// <see langword="false"/> when the bar is closed.
    /// </returns>
    /// <remarks>
    /// Canonical notifies the main viewport so <c>GetFrameHeight()</c> remains accurate across DPI changes,
    /// queues the safe-area padding as the menu-bar offset, computes the bar height, and then enters the bar
    /// window through <c>BeginViewportSideBar("##MainMenuBar", viewport, ImGuiDir_Up, height,
    /// window_flags)</c>. On success it temporarily clears <c>ImGuiWindowFlags_NoSavedSettings</c> so tables
    /// or child windows submitted inside the bar may still use settings, then calls
    /// <see cref="BeginMenuBar"/>.
    /// </remarks>
    public static bool BeginMainMenuBar()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiViewportP viewport = (ImGuiViewportP)GetMainViewport();

        // Notify of the viewport change so GetFrameHeight() can be accurate in case of a DPI change.
        SetCurrentViewport(null, viewport);

        // The main menu bar cannot be moved, so it honors the display safe area to keep text visible on a TV set.
        g.NextWindowData.MenuBarOffsetMinVal = new ImVec2(
            g.Style.DisplaySafeAreaPadding.X,
            System.MathF.Max(g.Style.DisplaySafeAreaPadding.Y - g.Style.FramePadding.Y, 0.0f));
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.MenuBar;
        float height = GetFrameHeight();
        bool isOpen = BeginViewportSideBar("##MainMenuBar", viewport, ImGuiDir.Up, height, windowFlags);
        g.NextWindowData.MenuBarOffsetMinVal = new ImVec2(0.0f, 0.0f);
        if (!isOpen)
        {
            End();
            return false;
        }

        // Temporarily allow settings inside the bar so nested tables and child windows may use them.
        g.CurrentWindow!.Flags &= ~ImGuiWindowFlags.NoSavedSettings;
        BeginMenuBar();
        return isOpen;
    }

    /// <summary>
    /// Ends the full-screen main menu bar opened by <see cref="BeginMainMenuBar"/> and restores the focus the
    /// user had before entering the menu layer. Canvases canonical <c>ImGui::EndMainMenuBar</c>
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The user-error precondition is reported through <see cref="ErrorLog"/> and the call returns without
    /// touching the window stack, reproducing canonical's <c>IM_ASSERT_USER_ERROR_RET</c>
    ///. <c>ImGuiWindowFlags_NoSavedSettings</c> is restored afterwards so
    /// tables or child windows submitted inside the bar may still use settings (<c>9360, 9371</c>).
    /// </para>
    /// <para>
    /// Focus restoration runs when the user left the menu layer by activating an item and no navigation or
    /// active item is pending.; it is the pair of
    /// <see cref="BeginMainMenuBar"/>.
    /// </para>
    /// </remarks>
    public static void EndMainMenuBar()
    {
        ImGuiContext g = GetCurrentContext()!;
        if (g.CurrentWindow == null || !g.CurrentWindow.DC.MenuBarAppending)
        {
            ErrorLog("Calling EndMainMenuBar() not from a menu-bar!"); // Canonical IM_ASSERT_USER_ERROR_RET.
            return;
        }

        EndMenuBar();
        g.CurrentWindow.Flags |= ImGuiWindowFlags.NoSavedSettings;

        // When the user has left the menu layer (typically by activating an item), restore the previous window.
        if (g.CurrentWindow == g.NavWindow && g.NavLayer == ImGuiNavLayer.Main && !g.NavAnyRequest && g.ActiveId == 0)
            FocusTopMostWindowUnderOne(g.NavWindow, null, null, ImGuiFocusRequestFlags.UnlessBelowModal | ImGuiFocusRequestFlags.RestoreFocusedChild);

        End();
    }

    #endregion

    #region Menu and menu item

    /// <summary>
    /// Tests whether the current window is the root of the menu set whose popup was opened at the current
    /// begin-popup level. Canvases canonical <c>IsRootOfOpenMenuSet</c>
    ///., a translation-unit-local helper.
    /// </summary>
    /// <returns>True when hovering may be shared across the menus of the same set.</returns>
    /// <remarks>
    /// <para>
    /// Canonical is <c>static</c> (file-local). This port declares it <c>private</c>; because every part is a
    /// declaration of the same partial <see cref="ImGui"/> type, a later part can still call it without
    /// widening its visibility or editing this part.
    /// </para>
    /// <para>
    /// The check compares the current window's navigation layer against the parent navigation layer recorded
    /// on the upper popup, then requires that popup's window to be a child menu of the current window
    ///. The canonical rationale explains that
    /// the identifier comparison was replaced by the navigation-layer comparison so user <c>PushID()</c>
    /// scopes do not break menu-set detection.
    /// </para>
    /// </remarks>
    private static bool IsRootOfOpenMenuSet()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if (g.OpenPopupStack.Count <= g.BeginPopupStack.Count || (window.Flags & ImGuiWindowFlags.ChildMenu) != 0)
            return false;

        ImGuiPopupData upperPopup = g.OpenPopupStack[g.BeginPopupStack.Count];
        if ((int)window.DC.NavLayerCurrent != upperPopup.ParentNavLayer)
            return false;
        return upperPopup.Window != null
            && (upperPopup.Window.Flags & ImGuiWindowFlags.ChildMenu) != 0
            && IsWindowChildOf(upperPopup.Window, window, true, false);
    }

    /// <summary>
    /// Begins a sub-menu entry, opening its popup window when the entry is activated or hovered. Canvases
    /// canonical <c>ImGui::BeginMenuEx</c>., declared in
    /// </summary>
    /// <param name="label">Entry label, which also supplies the identifier; a <c>##</c> suffix is hidden from
    /// the rendered text but remains part of the identifier.</param>
    /// <param name="icon">Optional leading icon drawn in the icon column, or null when the entry has no
    /// icon.</param>
    /// <param name="enabled">False disables the entry and force-closes its popup.</param>
    /// <returns>True when the sub-menu popup is open and <see cref="EndMenu"/> must be called.</returns>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: skip check, identifier and open-state read, popup-window flag
    /// composition, duplicate-submission short-circuit, label measurement, menu-set hover setup, layout
    /// branch, drag-away active-identifier release, open/close decision, popup opening, and
    /// <c>LastItemData</c> restoration. The vertical branch implements the Amazon mega-dropdown triangle test
    ///. so a menu does not close while the pointer travels toward its
    /// child menu.
    /// </para>
    /// <para>
    /// Canonical's <c>ImGuiMenuColumns</c> is reached through <c>window-&gt;DC.MenuColumns</c>; the column
    /// declarations feed the entry's minimum width and are measured through
    /// <see cref="ImGuiMenuColumns"/>'s <c>DeclColumns</c> extension (see <see cref="Update"/>).
    /// </para>
    /// <para>
    /// The popup subsystem is owned by part 05: this function calls
    /// <c>BeginPopupMenuEx(id, label, windowFlags)</c> and expects <see cref="EndMenu"/> to reach
    /// <c>EndPopup()</c>. <c>BeginDisabled()</c>/<c>EndDisabled()</c> belong to Chunk 10 and are called as
    /// canonical requires.
    /// </para>
    /// <para>
    /// The logging decoration (<c>LogSetNextTextDecoration</c>) is applied when logging is active; the test-engine item registration is not ported. Tab bars and columns
    /// have no canonical role in this function.
    /// </para>
    /// </remarks>
    internal static bool BeginMenuEx(string label, string? icon, bool enabled = true)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        bool menuIsOpen = IsPopupOpen(id, ImGuiPopupFlags.None);

        // Sub-menus are child windows so the mouse can hover across them; the first level of a hierarchy
        // relies on IsRootOfOpenMenuSet() instead.
        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.ChildMenu | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNavFocus;
        if ((window.Flags & ImGuiWindowFlags.ChildMenu) != 0)
            windowFlags |= ImGuiWindowFlags.ChildWindow;

        // A menu with the same identifier already submitted this frame is appended to, matching Begin().
        if (g.MenusIdSubmittedThisFrame.Contains(id))
        {
            if (menuIsOpen)
                menuIsOpen = BeginPopupMenuEx(id, label, windowFlags); // May report false when the popup is completely clipped.
            else
                g.NextWindowData.ClearFlags(); // Behave like Begin() and consume the queued values.
            return menuIsOpen;
        }

        g.MenusIdSubmittedThisFrame.Add(id);

        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false, -1.0f); // Canonical CalcTextSize(label, label_end, false).

        // Allow hovering across the menus of one set without making every entry a child window.
        bool menusetIsOpen = IsRootOfOpenMenuSet();
        if (menusetIsOpen)
            g.NextItemData.ItemFlagsSet |= ItemFlagNoWindowHoverableCheck;

        ImVec2 popupPos;
        ImVec2 pos = window.DC.CursorPos;
        PushID(label);
        if (!enabled)
            BeginDisabled(); // Chunk 10 owns BeginDisabled/EndDisabled.

        bool pressed;

        float backupRounding = style.SelectableRounding;
        style.SelectableRounding = style.MenuItemRounding;
        ImGuiSelectableFlags selectableFlags = (ImGuiSelectableFlags)((int)ImGuiSelectableFlags.NoAutoClosePopups
            | (int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SelectOnClick);
        ImGuiMenuColumns offsets = window.DC.MenuColumns;
        if (window.DC.LayoutType == ImGuiLayoutType.Horizontal)
        {
            // Menu inside a horizontal menu bar. Selectable extends its highlight by half ItemSpacing.
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X + (float)(int)(style.ItemSpacing.X * 0.5f), window.DC.CursorPos.Y);
            PushStyleVarX(ImGuiStyleVar.ItemSpacing, style.ItemSpacing.X * 2.0f);
            ImVec2 textPos = new ImVec2(window.DC.CursorPos.X + offsets.OffsetLabel, pos.Y + window.DC.CurrLineTextBaseOffset);
            pressed = Selectable("", menuIsOpen, selectableFlags, labelSize);
            LogSetNextTextDecoration("[", "]"); //.
            RenderText(textPos, label, labelEnd, false);
            PopStyleVar();
            // -1 spacing compensates the spacing Selectable() added through SameLine().
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X + (float)(int)(style.ItemSpacing.X * (-1.0f + 0.5f)), window.DC.CursorPos.Y);
            popupPos = new ImVec2(pos.X - 1.0f - (float)(int)(style.ItemSpacing.X * 0.5f), textPos.Y - style.FramePadding.Y + window.MenuBarHeight);
        }
        else
        {
            // Menu inside a regular vertical menu.
            float iconW = !string.IsNullOrEmpty(icon) ? CalcTextSize(icon!, false, -1.0f).X : 0.0f;
            float checkmarkW = (float)(int)(g.FontSize * 1.20f);
            float minW = offsets.DeclColumns(iconW, labelSize.X, 0.0f, checkmarkW); // Feedback to the next frame.
            float extraW = System.MathF.Max(0.0f, GetContentRegionAvail().X - minW);
            ImVec2 textPos = new ImVec2(window.DC.CursorPos.X, pos.Y + window.DC.CurrLineTextBaseOffset);
            pressed = Selectable("", menuIsOpen,
                selectableFlags | (ImGuiSelectableFlags)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SpanAvailWidth,
                new ImVec2(minW, labelSize.Y));
            LogSetNextTextDecoration("", ">"); //.
            RenderText(new ImVec2(textPos.X + offsets.OffsetLabel, textPos.Y), label, labelEnd, false);
            if (iconW > 0.0f)
                RenderText(new ImVec2(textPos.X + offsets.OffsetIcon, textPos.Y), icon!);
            RenderArrow(window.DrawList, new ImVec2(textPos.X + offsets.OffsetMark + extraW + g.FontSize * 0.30f, textPos.Y), GetColorU32(ImGuiCol.Text), ImGuiDir.Right);
            popupPos = new ImVec2(pos.X, textPos.Y - style.WindowPadding.Y);
        }
        style.SelectableRounding = backupRounding;
        if (!enabled)
            EndDisabled(); // Chunk 10 owns BeginDisabled/EndDisabled.

        // Once dragged, release the active identifier and key ownership so pressing down on one menu and
        // releasing on another works.
        if (g.ActiveId == id && g.HoveredId != id && g.ActiveIdSource == ImGuiInputSource.Mouse && IsMouseDragging(ImGuiMouseButton.Left))
        {
            ClearActiveID();
            SetKeyOwner(ImGuiKey.MouseLeft, ImGuiKeyOwner_NoOwner);
        }

        bool hovered = (g.HoveredId == id) && enabled && !g.NavHighlightItemUnderNav;

        bool wantOpen = false;
        bool wantOpenNavInit = false;
        bool wantClose = false;
        if (window.DC.LayoutType == ImGuiLayoutType.Vertical)
        {
            // Close the menu when the pointer is no longer over it unless it is moving roughly toward the
            // child menu; the triangle test avoids timers and makes menus feel reactive
            // (http://bjk5.com/post/44698559168, ).
            bool movingTowardChildMenu = false;
            ImGuiPopupData? childPopup = g.BeginPopupStack.Count < g.OpenPopupStack.Count ? g.OpenPopupStack[g.BeginPopupStack.Count] : null;
            ImGuiWindow? childMenuWindow = childPopup != null && childPopup.Window != null && childPopup.Window.ParentWindow == window
                ? childPopup.Window
                : null;
            if (g.HoveredWindow == window && childMenuWindow != null)
            {
                float refUnit = g.FontSize;
                float childDir = window.Pos.X < childMenuWindow.Pos.X ? 1.0f : -1.0f;
                ImRect nextWindowRect = childMenuWindow.Rect();
                ImVec2 ta = g.IO.MousePos - g.IO.MouseDelta;
                ImVec2 tb = childDir > 0.0f ? nextWindowRect.GetTL() : nextWindowRect.GetTR();
                ImVec2 tc = childDir > 0.0f ? nextWindowRect.GetBL() : nextWindowRect.GetBR();
                float padFarmostH = System.Math.Clamp(System.MathF.Abs(ta.X - tb.X) * 0.30f, refUnit * 0.5f, refUnit * 2.5f); // Add extra slack.
                ta.X += childDir * -0.5f;
                tb.X += childDir * refUnit;
                tc.X += childDir * refUnit;
                tb.Y = ta.Y + System.MathF.Max((tb.Y - padFarmostH) - ta.Y, -refUnit * 8.0f); // Cap the triangle height to limit the bias toward large sub-menus.
                tc.Y = ta.Y + System.MathF.Min((tc.Y + padFarmostH) - ta.Y, +refUnit * 8.0f);
                movingTowardChildMenu = TriangleContainsPoint(ta, tb, tc, g.IO.MousePos);
            }

            if (menuIsOpen && !hovered && g.HoveredWindow == window && !movingTowardChildMenu && !g.NavHighlightItemUnderNav && g.ActiveId == 0)
                wantClose = true;

            if (!menuIsOpen && pressed)
                wantOpen = true;
            else if (!menuIsOpen && hovered && !movingTowardChildMenu)
                wantOpen = true;
            else if (!menuIsOpen && hovered && g.HoveredIdTimer >= 0.30f && g.MouseStationaryTimer >= 0.30f)
                wantOpen = true;
            if (g.NavId == id && g.NavMoveDir == ImGuiDir.Right)
            {
                wantOpen = wantOpenNavInit = true;
                NavMoveRequestCancel();
                SetNavCursorVisibleAfterMove();
            }
        }
        else
        {
            // Menu bar: click an open menu again to close it, otherwise press to open and hover to switch.
            if (menuIsOpen && pressed && menusetIsOpen)
            {
                wantClose = true;
                wantOpen = menuIsOpen = false;
            }
            else if (pressed || (hovered && menusetIsOpen && !menuIsOpen))
            {
                wantOpen = true;
            }
            else if (g.NavId == id && g.NavMoveDir == ImGuiDir.Down)
            {
                wantOpen = true;
                NavMoveRequestCancel();
            }
        }

        if (!enabled) // Explicitly close an open menu that became disabled.
            wantClose = true;
        if (wantClose && IsPopupOpen(id, ImGuiPopupFlags.None))
            ClosePopupToLevel(g.BeginPopupStack.Count, true);

        // Test-engine item registration. is not ported.
        PopID();

        if (g.ActiveId == id && wantOpen)
            g.ActiveIdNoClearOnFocusLoss = true;

        if (wantOpen && !menuIsOpen && g.OpenPopupStack.Count > g.BeginPopupStack.Count)
        {
            // Do not recycle the same menu level in one frame for a different identifier: close the other
            // menu first and yield for a frame.
            OpenPopup(label);
        }
        else if (wantOpen)
        {
            menuIsOpen = true;
            OpenPopup(label, ImGuiPopupFlags.NoReopen);
        }

        if (menuIsOpen)
        {
            ImGuiLastItemData lastItemInParent = CopyLastItemData(g.LastItemData);
            SetNextWindowPos(popupPos, ImGuiCond.Always); // Serves as a reference for FindBestWindowPosForPopup(), not the final position.
            PushStyleVar(ImGuiStyleVar.ChildRounding, style.PopupRounding);
            menuIsOpen = BeginPopupMenuEx(id, label, windowFlags);
            PopStyleVar();
            if (menuIsOpen)
            {
                // Perform the navigation init request that ImGuiPopupFlags_NoReopenAlwaysNavInit would issue
                // when the popup was already open through a previous mouse hover.
                if (wantOpen && wantOpenNavInit && !g.NavInitRequest)
                {
                    FocusWindow(g.CurrentWindow, ImGuiFocusRequestFlags.UnlessBelowModal);
                    NavInitWindow(g.CurrentWindow!, false);
                }

                // Restore LastItemData so the IsItem* functions keep working after BeginMenu()/EndMenu().
                g.LastItemData = lastItemInParent;
                if (g.HoveredWindow == window)
                    g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredWindow;
            }
        }
        else
        {
            g.NextWindowData.ClearFlags(); // Behave like Begin() and consume the queued values.
        }

        return menuIsOpen;
    }

    /// <summary>
    /// Begins a sub-menu entry without an icon. Canvases canonical <c>ImGui::BeginMenu</c>
    ///.
    /// </summary>
    /// <param name="label">Entry label, which also supplies the identifier.</param>
    /// <param name="enabled">False disables the entry and force-closes its popup.</param>
    /// <returns>True when the sub-menu popup is open and <see cref="EndMenu"/> must be called.</returns>
    public static bool BeginMenu(string label, bool enabled = true)
    {
        return BeginMenuEx(label, null, enabled);
    }

    /// <summary>
    /// Ends the sub-menu popup opened by <see cref="BeginMenu"/> and handles left-navigation closing.
    /// Canvases canonical <c>ImGui::EndMenu</c>.,
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the popup was not re-entered this frame and a left move request inside it found no result, the
    /// menu closes itself and the request is cancelled so the parent menu can process it
    ///. The user-error precondition is reported through
    /// <see cref="ErrorLog"/> and the call returns, reproducing canonical's
    /// <c>IM_ASSERT_USER_ERROR_RET</c>.
    /// </para>
    /// <para>
    /// <c>EndPopup()</c> is owned by part 05.
    /// </para>
    /// </remarks>
    public static void EndMenu()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = g.CurrentWindow!;
        if ((window.Flags & (ImGuiWindowFlags.Popup | ImGuiWindowFlags.ChildMenu)) != (ImGuiWindowFlags.Popup | ImGuiWindowFlags.ChildMenu))
        {
            ErrorLog("Calling EndMenu() in wrong window!"); // Canonical IM_ASSERT_USER_ERROR_RET.
            return;
        }

        ImGuiWindow? parentWindow = window.ParentWindow; // Never null because the precondition above passed.
        if (window.BeginCount == window.BeginCountPreviousFrame)
            if (g.NavMoveDir == ImGuiDir.Left && NavMoveRequestButNoResultYet())
                if (g.NavWindow != null && g.NavWindow.RootWindowForNav == window && parentWindow!.DC.LayoutType == ImGuiLayoutType.Vertical)
                {
                    ClosePopupToLevel(g.BeginPopupStack.Count - 1, true);
                    NavMoveRequestCancel();
                }

        EndPopup();
    }

    /// <summary>
    /// Submits a menu entry with an optional icon and shortcut, rendering the selection state and returning
    /// whether the entry was activated. Canvases canonical <c>ImGui::MenuItemEx</c>
    ///.
    /// </summary>
    /// <param name="label">Entry label, which also supplies the identifier.</param>
    /// <param name="icon">Optional icon drawn in the icon column, or null when the entry has no icon.</param>
    /// <param name="shortcut">Optional shortcut text drawn right-aligned in the shortcut column, or null.</param>
    /// <param name="selected">Whether the entry currently shows its checkmark.</param>
    /// <param name="enabled">False disables the entry.</param>
    /// <returns>True when the entry was activated.</returns>
    /// <remarks>
    /// <para>
    /// Step order follows canonical: skip check, label measurement, menu-set hover setup, identifier push,
    /// layout branch, drag-away active-identifier release, and disabled/identifier unwinding. In horizontal
    /// layout the shortcut is not rendered and the selected state is carried by the highlight, matching
    /// <see cref="BeginMenuEx"/>'s spacing so menu entries align inside a menu bar
    /// </para>
    /// <para>
    /// The vertical branch declares the four columns through <c>DeclColumns</c>, right-aligns the shortcut
    /// with the stretch width, and draws the checkmark at the mark column
    ///. The shortcut is rendered with the disabled text color and its
    /// logging decoration is applied when logging is active.
    /// </para>
    /// <para>
    /// <c>BeginDisabled()</c>/<c>EndDisabled()</c> belong to Chunk 10 and are called as canonical requires.
    /// The test-engine item registration is not ported.
    /// </para>
    /// </remarks>
    internal static bool MenuItemEx(string label, string? icon, string? shortcut, bool selected, bool enabled)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        ImVec2 pos = window.DC.CursorPos;
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false, -1.0f); // Canonical CalcTextSize(label, label_end, false).

        bool menusetIsOpen = IsRootOfOpenMenuSet();
        if (menusetIsOpen)
            g.NextItemData.ItemFlagsSet |= ItemFlagNoWindowHoverableCheck;

        bool pressed;
        PushID(label);
        if (!enabled)
            BeginDisabled(); // Chunk 10 owns BeginDisabled/EndDisabled.

        float backupRounding = style.SelectableRounding;
        style.SelectableRounding = style.MenuItemRounding;
        ImGuiSelectableFlags selectableFlags = (ImGuiSelectableFlags)((int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SelectOnRelease
            | (int)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SetNavIdOnHover);
        ImGuiMenuColumns offsets = window.DC.MenuColumns;
        if (window.DC.LayoutType == ImGuiLayoutType.Horizontal)
        {
            // Mimic BeginMenu()'s layout spacing so MenuItem() works inside a menu bar; the shortcut is not
            // rendered and the highlight replaces the checkmark.
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X + (float)(int)(style.ItemSpacing.X * 0.5f), window.DC.CursorPos.Y);
            ImVec2 textPos = new ImVec2(window.DC.CursorPos.X + offsets.OffsetLabel, window.DC.CursorPos.Y + window.DC.CurrLineTextBaseOffset);
            PushStyleVarX(ImGuiStyleVar.ItemSpacing, style.ItemSpacing.X * 2.0f);
            pressed = Selectable("", selected, selectableFlags, new ImVec2(labelSize.X, 0.0f));
            PopStyleVar();
            if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Visible) != 0)
                RenderText(textPos, label, labelEnd, false);
            window.DC.CursorPos = new ImVec2(window.DC.CursorPos.X + (float)(int)(style.ItemSpacing.X * (-1.0f + 0.5f)), window.DC.CursorPos.Y);
        }
        else
        {
            // Menu item inside a vertical menu.
            float iconW = !string.IsNullOrEmpty(icon) ? CalcTextSize(icon!, false, -1.0f).X : 0.0f;
            float shortcutW = !string.IsNullOrEmpty(shortcut) ? CalcTextSize(shortcut!, false, -1.0f).X : 0.0f;
            float checkmarkW = (float)(int)(g.FontSize * 1.20f); // Always accounted for because selection has no neutral state.
            float minW = offsets.DeclColumns(iconW, labelSize.X, shortcutW, checkmarkW); // Feedback to the next frame.
            float stretchW = System.MathF.Max(0.0f, GetContentRegionAvail().X - minW);
            ImVec2 textPos = new ImVec2(pos.X, pos.Y + window.DC.CurrLineTextBaseOffset);
            pressed = Selectable("", false,
                selectableFlags | (ImGuiSelectableFlags)ImGuiSelectableFlagsPrivate.ImGuiSelectableFlags_SpanAvailWidth,
                new ImVec2(minW, labelSize.Y));
            if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.Visible) != 0)
            {
                RenderText(textPos + new ImVec2(offsets.OffsetLabel, 0.0f), label, labelEnd, false);
                if (iconW > 0.0f)
                    RenderText(textPos + new ImVec2(offsets.OffsetIcon, 0.0f), icon!);
                if (shortcutW > 0.0f)
                {
                    PushStyleColor(ImGuiCol.Text, style.Colors[(int)ImGuiCol.TextDisabled]);
                    LogSetNextTextDecoration("(", ")"); //.
                    RenderText(textPos + new ImVec2(offsets.OffsetShortcut + stretchW, 0.0f), shortcut!, null, false);
                    PopStyleColor();
                }
                if (selected)
                    RenderCheckMark(window.DrawList,
                        textPos + new ImVec2(offsets.OffsetMark + stretchW + g.FontSize * 0.40f, g.FontSize * 0.134f * 0.5f),
                        GetColorU32(ImGuiCol.Text), g.FontSize * 0.866f);
            }
        }
        style.SelectableRounding = backupRounding;

        // Once dragged, release the active identifier and key ownership so pressing down on one menu item and
        // releasing on another works.
        uint id = g.LastItemData.ID;
        if (g.ActiveId == id && g.HoveredId != id && g.ActiveIdSource == ImGuiInputSource.Mouse && IsMouseDragging(ImGuiMouseButton.Left))
        {
            ClearActiveID();
            SetKeyOwner(ImGuiKey.MouseLeft, ImGuiKeyOwner_NoOwner);
        }

        // Test-engine item registration. is not ported.
        if (!enabled)
            EndDisabled(); // Chunk 10 owns BeginDisabled/EndDisabled.
        PopID();

        return pressed;
    }

    /// <summary>
    /// Submits a menu entry that returns whether it was activated. Canvases canonical
    /// <c>ImGui::MenuItem</c>., declared in
    /// </summary>
    /// <param name="label">Entry label, which also supplies the identifier.</param>
    /// <param name="shortcut">Optional shortcut text drawn right-aligned, or null.</param>
    /// <param name="selected">Whether the entry currently shows its checkmark.</param>
    /// <param name="enabled">False disables the entry.</param>
    /// <returns>True when the entry was activated.</returns>
    public static bool MenuItem(string label, string? shortcut = null, bool selected = false, bool enabled = true)
    {
        return MenuItemEx(label, null, shortcut, selected, enabled);
    }

    /// <summary>
    /// Submits a menu entry that toggles a caller-owned selection flag when activated. Canvases canonical
    /// <c>ImGui::MenuItem</c>., declared in
    /// </summary>
    /// <param name="label">Entry label, which also supplies the identifier.</param>
    /// <param name="shortcut">Optional shortcut text drawn right-aligned, or null.</param>
    /// <param name="pSelected">Selection flag read to draw the checkmark and flipped when the entry is
    /// activated.</param>
    /// <param name="enabled">False disables the entry.</param>
    /// <returns>True when the entry was activated and the selection flag was flipped.</returns>
    public static bool MenuItem(string label, string? shortcut, ref bool pSelected, bool enabled = true)
    {
        if (MenuItemEx(label, null, shortcut, pSelected, enabled))
        {
            pSelected = !pSelected;
            return true;
        }
        return false;
    }

    #endregion

    #region Private menu helpers

    /// <summary>
    /// Tests whether a point lies inside a triangle. Canvases canonical <c>ImTriangleContainsPoint</c>
    ///.
    /// </summary>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="p">Point to test.</param>
    /// <returns>True when the point lies on the same side of all three directed edges.</returns>
    /// <remarks>
    /// The canonical helper lives in the general-helpers category owned by Chunk 9; the only widget that
    /// needs it before then is the menu mega-dropdown test., so it is
    /// realized here as a private helper. Chunk 9 may promote it to the shared surface without changing this
    /// call site.
    /// </remarks>
    private static bool TriangleContainsPoint(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 p)
    {
        bool b1 = (p.X - b.X) * (a.Y - b.Y) - (p.Y - b.Y) * (a.X - b.X) < 0.0f;
        bool b2 = (p.X - c.X) * (b.Y - c.Y) - (p.Y - c.Y) * (b.X - c.X) < 0.0f;
        bool b3 = (p.X - a.X) * (c.Y - a.Y) - (p.Y - a.Y) * (c.X - a.X) < 0.0f;
        return b1 == b2 && b2 == b3;
    }

    /// <summary>
    /// Copies the last-item record so it can be restored after a nested popup window has overwritten it.
    /// </summary>
    /// <param name="source">Record to copy.</param>
    /// <returns>A new record holding the same values as <paramref name="source"/>.</returns>
    /// <remarks>
    /// Canonical <c>ImGuiLastItemData</c> is a value structure, so
    /// <c>ImGuiLastItemData last_item_in_parent = g.LastItemData;</c> (<c>, 9617</c>)
    /// is a snapshot. The port declares the type as a reference type that item submission mutates in place,
    /// so saving the reference alone would alias the mutated record and the restore would be a no-op; this
    /// helper reproduces the canonical value copy.
    /// </remarks>
    private static ImGuiLastItemData CopyLastItemData(ImGuiLastItemData source)
    {
        return new ImGuiLastItemData
        {
            ID = source.ID,
            ItemFlags = source.ItemFlags,
            StatusFlags = source.StatusFlags,
            Rect = source.Rect,
            NavRect = source.NavRect,
            DisplayRect = source.DisplayRect,
            ClipRect = source.ClipRect,
            Shortcut = source.Shortcut,
        };
    }

    #endregion

    #endregion


    #region SECTION: POPUPS AND TOOLTIPS

    #region [SECTION] TOOLTIPS

    /// <summary>
    /// Begins a tooltip window positioned automatically relative to the mouse or to the active
    /// drag-and-drop source. Ports canonical <c>ImGui::BeginTooltip()</c>
    ///.
    /// </summary>
    /// <returns><see langword="true"/> when the tooltip window began; canonical always returns true.</returns>
    /// <remarks>Equivalent to <see cref="BeginTooltipEx"/> with no tooltip flags and no extra window
    /// flags.</remarks>
    public static bool BeginTooltip()
    {
        return BeginTooltipEx(ImGuiTooltipFlags.None, ImGuiWindowFlags.None);
    }

    /// <summary>
    /// Begins a tooltip for the last submitted item, but only while that item is hovered with the
    /// flags canonical selects for tooltips. Ports canonical <c>ImGui::BeginItemTooltip()</c>
    ///.
    /// </summary>
    /// <returns><see langword="true"/> when the item is hovered and the tooltip window began.</returns>
    /// <remarks>The hover test uses <see cref="ImGuiHoveredFlags.ForTooltip"/>, which canonical maps to
    /// the user-configurable <c>style.HoverFlagsForTooltipMouse</c> or
    /// <c>style.HoverFlagsForTooltipNav</c> depending on the input source.</remarks>
    public static bool BeginItemTooltip()
    {
        if (!IsItemHovered(ImGuiHoveredFlags.ForTooltip))
            return false;
        return BeginTooltipEx(ImGuiTooltipFlags.None, ImGuiWindowFlags.None);
    }

    /// <summary>
    /// Begins a tooltip window with the supplied tooltip behavior and extra window flags. Ports
    /// canonical <c>ImGui::BeginTooltipEx()</c>.,
    ///.
    /// </summary>
    /// <param name="tooltipFlags">Tooltip behavior flags. <see cref="ImGuiTooltipFlags.OverridePrevious"/>
    /// hides a previously submitted tooltip window and advances the tooltip override counter.</param>
    /// <param name="extraWindowFlags">Window flags OR'ed with the canonical tooltip flag set.</param>
    /// <returns><see langword="true"/>; canonical documents that the return value is currently always
    /// true even though the API was expanded for future false returns.</returns>
    /// <remarks>
    /// <para>
    /// The window name carries the tooltip override count. so a fresh
    /// window is created instead of recycling the previous tooltip content:
    /// <c>##Tooltip_%02d</c>, or <c>##Tooltip_DragDrop_%02d</c> for a drag-and-drop tooltip.
    /// </para>
    /// <para>
    /// Chunk 8 guard: the drag-and-drop branch is selected through <c>g.DragDropWithinSource</c> and
    /// <c>g.DragDropWithinTarget</c>, which the drag-and-drop chunk owns. No port code sets those
    /// fields yet, so the disjunction is always false and the branch is inert today. The canonical
    /// <c>NoDocking</c> bit is retained in the tooltip flag set; docking is owned by Chunk 6 and
    /// <c>Begin()</c> currently ignores the bit.
    /// </para>
    /// <para>
    /// The drag-and-drop offsets are canonical <c>TOOLTIP_DEFAULT_OFFSET_MOUSE = (16, 10)</c>,
    /// <c>TOOLTIP_DEFAULT_OFFSET_TOUCH = (0, -20)</c> and
    /// <c>TOOLTIP_DEFAULT_PIVOT_TOUCH = (0.5, 1.0)</c>., multiplied by
    /// <c>style.MouseCursorScale</c>.
    /// </para>
    /// </remarks>
    internal static bool BeginTooltipEx(ImGuiTooltipFlags tooltipFlags, ImGuiWindowFlags extraWindowFlags)
    {
        ImGuiContext g = GetContext();
        bool isDragDropTooltip = g.DragDropWithinSource || g.DragDropWithinTarget;
        if (isDragDropTooltip)
        {
            // Drag-and-drop tooltips are positioned off the mouse and are never clamped by the
            // viewport edge; SetNextWindowPos() enforces the position and disables clamping.
            bool isTouchscreen = g.IO.MouseSource == ImGuiMouseSource.TouchScreen;
            if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasPos) == 0)
            {
                ImVec2 mouseOffset = isTouchscreen ? new ImVec2(0.0f, -20.0f) : new ImVec2(16.0f, 10.0f);
                ImVec2 tooltipPivot = isTouchscreen ? new ImVec2(0.5f, 1.0f) : new ImVec2(0.0f, 0.0f);
                ImVec2 tooltipPos = g.IO.MousePos + mouseOffset * g.Style.MouseCursorScale;
                SetNextWindowPos(tooltipPos, ImGuiCond.None, tooltipPivot);
            }

            SetNextWindowBgAlpha(g.Style.Colors[(int)ImGuiCol.PopupBg].W * 0.60f);
            tooltipFlags |= ImGuiTooltipFlags.OverridePrevious;
        }

        // Hide the previous tooltip window for this frame: the content of a window cannot be reset, so
        // a new window is created under a higher override count instead.
        if ((tooltipFlags & ImGuiTooltipFlags.OverridePrevious) != 0
            && g.TooltipPreviousWindow != null
            && g.TooltipPreviousWindow.Active
            && !IsWindowInBeginStack(g.TooltipPreviousWindow))
        {
            SetWindowHiddenAndSkipItemsForCurrentFrame(g.TooltipPreviousWindow);
            g.TooltipOverrideCount++;
        }

        string windowNameTemplate = isDragDropTooltip ? "##Tooltip_DragDrop_%02d" : "##Tooltip_%02d";
        string windowName = ImFormatString(windowNameTemplate, g.TooltipOverrideCount);
        ImGuiWindowFlags flags = ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoDocking;
        Begin(windowName, flags | extraWindowFlags);
        return true;
    }

    /// <summary>
    /// Begins a hidden tooltip window used internally to prepare tooltip content without taking input
    /// or space. Ports canonical <c>ImGui::BeginTooltipHidden()</c>
    ///.
    /// </summary>
    /// <returns><see langword="true"/> when the window began.</returns>
    /// <remarks>Unlike <see cref="BeginTooltipEx"/> the flag set omits <c>NoDocking</c>, matching
    /// canonical. The window is hidden and skipped for the current frame immediately after
    /// <c>Begin()</c>.</remarks>
    internal static bool BeginTooltipHidden()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoInputs
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize;
        bool ret = Begin("##Tooltip_Hidden", flags);
        SetWindowHiddenAndSkipItemsForCurrentFrame(GetCurrentWindow());
        return ret;
    }

    /// <summary>
    /// Ends the tooltip window begun by <see cref="BeginTooltip"/>, <see cref="BeginItemTooltip"/>,
    /// <see cref="BeginTooltipEx"/>, <see cref="BeginTooltipHidden"/> or
    /// <see cref="SetTooltipV"/>. Ports canonical <c>ImGui::EndTooltip()</c>
    ///.
    /// </summary>
    /// <remarks>The canonical <c>IM_ASSERT</c> that the current window carries
    /// <see cref="ImGuiWindowFlags.Tooltip"/> is realized with
    /// <c>ImAssert.Check</c>, which reports mismatched
    /// <c>BeginTooltip()</c>/<c>EndTooltip()</c> calls.</remarks>
    public static void EndTooltip()
    {
        ImAssert.Check((GetCurrentWindowRead()!.Flags & ImGuiWindowFlags.Tooltip) != 0);
        End();
    }

    /// <summary>
    /// Submits a tooltip containing formatted text, replacing any tooltip already submitted for this
    /// frame. Ports canonical <c>ImGui::SetTooltip()</c>
    ///.
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void SetTooltip(string fmt, params object?[] args)
    {
        SetTooltipV(fmt, args);
    }

    /// <summary>
    /// Submits a tooltip containing formatted text from a pre-built argument array. Ports canonical
    /// <c>ImGui::SetTooltipV()</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    /// <remarks>Begins with <see cref="ImGuiTooltipFlags.OverridePrevious"/>, submits the text through
    /// <see cref="TextV"/>, and ends the tooltip. The canonical early-out on a false
    /// <c>BeginTooltipEx()</c> return is retained even though that return is currently always true.</remarks>
    public static void SetTooltipV(string fmt, object?[] args)
    {
        if (!BeginTooltipEx(ImGuiTooltipFlags.OverridePrevious, ImGuiWindowFlags.None))
            return;
        TextV(fmt, args);
        EndTooltip();
    }

    /// <summary>
    /// Submits a tooltip for the last submitted item, but only while that item is hovered with the
    /// flags canonical selects for tooltips. Ports canonical <c>ImGui::SetItemTooltip()</c>
    ///.
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void SetItemTooltip(string fmt, params object?[] args)
    {
        if (IsItemHovered(ImGuiHoveredFlags.ForTooltip))
            SetTooltipV(fmt, args);
    }

    /// <summary>
    /// Submits a tooltip for the last submitted item from a pre-built argument array, but only while
    /// that item is hovered with the flags canonical selects for tooltips. Ports canonical
    /// <c>ImGui::SetItemTooltipV()</c>., declared in
    /// </summary>
    /// <param name="fmt">Canonical printf-style format string.</param>
    /// <param name="args">Arguments consumed by the conversions in <paramref name="fmt"/>.</param>
    public static void SetItemTooltipV(string fmt, object?[] args)
    {
        if (IsItemHovered(ImGuiHoveredFlags.ForTooltip))
            SetTooltipV(fmt, args);
    }

    #endregion

    #region [SECTION] POPUPS

    /// <summary>
    /// Tests whether a popup identifier is open, optionally ignoring the identifier or the popup level.
    /// Ports canonical <c>ImGui::IsPopupOpen(ImGuiID, ImGuiPopupFlags)</c>
    ///.
    /// </summary>
    /// <param name="id">The popup identifier; must be zero when
    /// <see cref="ImGuiPopupFlags.AnyPopupId"/> is set.</param>
    /// <param name="popupFlags">The supported flags are <see cref="ImGuiPopupFlags.AnyPopupId"/> and
    /// <see cref="ImGuiPopupFlags.AnyPopupLevel"/>.</param>
    /// <returns>
    /// With <see cref="ImGuiPopupFlags.AnyPopupId"/>: whether any popup is open, considering all levels
    /// when <see cref="ImGuiPopupFlags.AnyPopupLevel"/> is also set and only the current begin level
    /// otherwise. With <see cref="ImGuiPopupFlags.AnyPopupLevel"/> alone: whether the identifier
    /// appears anywhere on the open-popup stack. Otherwise: whether the popup is open at the current
    /// begin-popup level.
    /// </returns>
    /// <remarks>The canonical <c>IM_ASSERT(id == 0)</c> for the any-identifier path is realized with
    /// <c>ImAssert.Check</c>.</remarks>
    internal static bool IsPopupOpen(uint id, ImGuiPopupFlags popupFlags)
    {
        ImGuiContext g = GetContext();
        if ((popupFlags & ImGuiPopupFlags.AnyPopupId) != 0)
        {
            // Any-popup queries identify a stack position rather than a popup, so an identifier is
            // meaningless and canonical rejects a non-zero one.
            ImAssert.Check(id == 0);
            if ((popupFlags & ImGuiPopupFlags.AnyPopupLevel) != 0)
                return g.OpenPopupStack.Count > 0;
            return g.OpenPopupStack.Count > g.BeginPopupStack.Count;
        }

        if ((popupFlags & ImGuiPopupFlags.AnyPopupLevel) != 0)
        {
            // Search the whole open-popup stack because the caller does not know the level.
            for (int n = 0; n < g.OpenPopupStack.Count; n++)
                if (g.OpenPopupStack[n].PopupId == id)
                    return true;
            return false;
        }

        // Most-common query: is this identifier open at the current BeginPopup() level?
        return g.OpenPopupStack.Count > g.BeginPopupStack.Count
            && g.OpenPopupStack[g.BeginPopupStack.Count].PopupId == id;
    }

    /// <summary>
    /// Tests whether the popup identified by a string is open. Ports canonical
    /// <c>ImGui::IsPopupOpen(const char*, ImGuiPopupFlags)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier string, hashed relative to the current window.</param>
    /// <param name="popupFlags">The supported flags are <see cref="ImGuiPopupFlags.AnyPopupId"/> and
    /// <see cref="ImGuiPopupFlags.AnyPopupLevel"/>.</param>
    /// <returns>Whether the identifier is open under the query described by
    /// <see cref="IsPopupOpen(uint, ImGuiPopupFlags)"/>.</returns>
    /// <remarks>Canonical forbids combining a string identifier with
    /// <see cref="ImGuiPopupFlags.AnyPopupLevel"/>; the string overload therefore asserts that the
    /// combination is not used and then delegates to the identifier overload.</remarks>
    public static bool IsPopupOpen(string strId, ImGuiPopupFlags popupFlags = 0)
    {
        uint id = (popupFlags & ImGuiPopupFlags.AnyPopupId) != 0 ? 0u : GetID(strId);
        if ((popupFlags & ImGuiPopupFlags.AnyPopupLevel) != 0 && id != 0)
            ImAssert.Check(false, "Cannot use IsPopupOpen() with a string id and ImGuiPopupFlags_AnyPopupLevel.");
        return IsPopupOpen(id, popupFlags);
    }

    /// <summary>
    /// Returns the top-most open modal window, or <see langword="null"/> when none is open. Ports
    /// canonical <c>ImGui::GetTopMostPopupModal()</c>
    ///.
    /// </summary>
    /// <returns>The deepest modal window on the open-popup stack, searching from the top down.</returns>
    /// <remarks>Also see <see cref="FindBlockingModal"/>.</remarks>
    internal static ImGuiWindow? GetTopMostPopupModal()
    {
        ImGuiContext g = GetContext();
        for (int n = g.OpenPopupStack.Count - 1; n >= 0; n--)
        {
            ImGuiWindow? popup = g.OpenPopupStack[n].Window;
            if (popup != null && (popup.Flags & ImGuiWindowFlags.Modal) != 0)
                return popup;
        }
        return null;
    }

    /// <summary>
    /// Returns the top-most modal window that is also active and visible, or <see langword="null"/>.
    /// Ports canonical <c>ImGui::GetTopMostAndVisiblePopupModal()</c>
    ///.
    /// </summary>
    /// <returns>The deepest active and visible modal window on the open-popup stack.</returns>
    /// <remarks>Used to confirm what a stack of modals displays.</remarks>
    internal static ImGuiWindow? GetTopMostAndVisiblePopupModal()
    {
        ImGuiContext g = GetContext();
        for (int n = g.OpenPopupStack.Count - 1; n >= 0; n--)
        {
            ImGuiWindow? popup = g.OpenPopupStack[n].Window;
            if (popup != null && (popup.Flags & ImGuiWindowFlags.Modal) != 0 && IsWindowActiveAndVisible(popup))
                return popup;
        }
        return null;
    }

    /// <summary>
    /// Finds the first modal window that should block focus for the specified window. Ports canonical
    /// <c>ImGui::FindBlockingModal(ImGuiWindow*)</c>
    ///.
    /// </summary>
    /// <param name="window">The window that wants focus, or <see langword="null"/> to test whether any
    /// modal would block a newly focused window.</param>
    /// <returns>
    /// The first modal on the open-popup stack that is active or was active and whose begin-stack scope
    /// does not contain <paramref name="window"/>; <see langword="null"/> when no modal blocks it.
    /// </returns>
    /// <remarks>
    /// When multiple modals are stacked, a newly created window honors begin-stack order and never
    /// drops below its own modal parent; the canonical comment block 
    /// documents the contract. <c>FindBlockingModal(null) == null</c> is generally equivalent to
    /// <c>GetTopMostPopupModal() == null</c>, except that this function also tests
    /// <c>Active</c>/<c>WasActive</c>.
    /// </remarks>
    internal static ImGuiWindow? FindBlockingModal(ImGuiWindow? window)
    {
        ImGuiContext g = GetContext();
        if (g.OpenPopupStack.Count <= 0)
            return null;

        // Find a modal that has a common parent with the specified window, which should be positioned
        // behind that modal.
        for (int n = 0; n < g.OpenPopupStack.Count; n++)
        {
            ImGuiWindow? popupWindow = g.OpenPopupStack[n].Window;
            if (popupWindow == null || (popupWindow.Flags & ImGuiWindowFlags.Modal) == 0)
                continue;
            // Check WasActive because this may run before the popup renders on the current frame, and
            // check Active to handle newly created windows.
            if (!popupWindow.Active && !popupWindow.WasActive)
                continue;
            if (window == null)
                return popupWindow;
            if (IsWindowWithinBeginStackOf(window, popupWindow))
                continue;
            return popupWindow;
        }
        return null;
    }

    /// <summary>
    /// Marks the popup identified by a string as open. Ports canonical
    /// <c>ImGui::OpenPopup(const char*, ImGuiPopupFlags)</c>
    ///.
    /// </summary>
    /// <param name="strId">The identifier string, hashed relative to the current window.</param>
    /// <param name="popupFlags">Popup behavior flags forwarded to <see cref="OpenPopupEx"/>.</param>
    /// <returns>Whether the popup was toggled open.</returns>
    /// <remarks>The resolved identifier is debug-logged before the popup is opened, so a capture records
    /// both the name and the identifier the popup was registered under.</remarks>
    public static bool OpenPopup(string strId, ImGuiPopupFlags popupFlags = 0)
    {
        uint id = GetID(strId);
        DebugLogEvent(ImGuiDebugLogFlags.EventPopup, "[popup] OpenPopup(\"{0}\" -> 0x{1:X8})\n", strId, id);
        return OpenPopupEx(id, popupFlags);
    }

    /// <summary>
    /// Marks the popup identified by an explicit identifier as open. Ports canonical
    /// <c>ImGui::OpenPopup(ImGuiID, ImGuiPopupFlags)</c>
    ///.
    /// </summary>
    /// <param name="id">The pre-computed popup identifier.</param>
    /// <param name="popupFlags">Popup behavior flags forwarded to <see cref="OpenPopupEx"/>.</param>
    /// <returns>Whether the popup was toggled open.</returns>
    /// <remarks>Canonical returns <c>bool</c> from this overload.</remarks>
    public static bool OpenPopup(uint id, ImGuiPopupFlags popupFlags = 0)
    {
        return OpenPopupEx(id, popupFlags);
    }

    /// <summary>
    /// Marks a popup as open, keeping at most one open popup per level of the popup hierarchy. Ports
    /// canonical <c>ImGui::OpenPopupEx()</c>.,
    ///.
    /// </summary>
    /// <param name="id">The popup identifier; it is relative to the current ID stack.</param>
    /// <param name="popupFlags">Popup behavior flags; <see cref="ImGuiPopupFlags.NoOpenOverExistingPopup"/>
    /// suppresses opening over an existing popup and <see cref="ImGuiPopupFlags.NoReopen"/> keeps an
    /// already-open entry in place.</param>
    /// <returns><see langword="true"/> when the popup is toggled open, which allows the caller to
    /// capture local state; <see langword="false"/> when an existing entry was kept.</returns>
    /// <remarks>
    /// <para>
    /// Popups are closed when the user clicks outside, activates a pressable item, or calls
    /// <see cref="CloseCurrentPopup"/>. The entry stores the navigation window to restore, the frame
    /// count, the parent identifier, and both the preferred and mouse positions at open time.
    /// </para>
    /// <para>
    /// Calling <c>OpenPopup()</c> every frame is a likely programming mistake that would leave the UI
    /// unusable, so successive-frame calls silently keep the existing entry even without
    /// <see cref="ImGuiPopupFlags.NoReopen"/>.
    /// </para>
    /// <para>The open request is debug-logged after the reference positions are resolved and before the
    /// open-popup stack is inspected.</para>
    /// </remarks>
    internal static bool OpenPopupEx(uint id, ImGuiPopupFlags popupFlags = 0)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow parentWindow = g.CurrentWindow!;
        int currentStackSize = g.BeginPopupStack.Count;

        if ((popupFlags & ImGuiPopupFlags.NoOpenOverExistingPopup) != 0)
        {
            if (IsPopupOpen(0u, ImGuiPopupFlags.AnyPopupId))
                return false;
        }

        ImGuiPopupData popupRef = new()
        {
            PopupId = id,
            Window = null,
            RestoreNavWindow = g.NavWindow,
            OpenFrameCount = g.FrameCount,
            OpenParentId = parentWindow.IDStack[^1],
            OpenPopupPos = NavCalcPreferredRefPos(ImGuiWindowFlags.Popup),
        };
        popupRef.OpenMousePos = IsMousePosValid(g.IO.MousePos) ? g.IO.MousePos : popupRef.OpenPopupPos;

        DebugLogEvent(ImGuiDebugLogFlags.EventPopup, "[popup] OpenPopupEx(0x{0:X8})\n", id);
        if (g.OpenPopupStack.Count < currentStackSize + 1)
        {
            g.OpenPopupStack.Add(popupRef);
            return true;
        }

        bool keepExisting = false;
        if (g.OpenPopupStack[currentStackSize].PopupId == id)
        {
            if (g.OpenPopupStack[currentStackSize].OpenFrameCount == g.FrameCount - 1
                || (popupFlags & ImGuiPopupFlags.NoReopen) != 0)
                keepExisting = true;
        }

        if (keepExisting)
        {
            g.OpenPopupStack[currentStackSize].OpenFrameCount = popupRef.OpenFrameCount;
        }
        else
        {
            // Reopen: close child popups if any, then queue the popup for open/reopen so that Begin()
            // can set its position, focus and navigation state.
            ClosePopupToLevel(currentStackSize, true);
            g.OpenPopupStack.Add(popupRef);
        }

        return !keepExisting;
    }

    /// <summary>
    /// Closes any popups that are stacked over the reference window. Ports canonical
    /// <c>ImGui::ClosePopupsOverWindow()</c>.,
    ///.
    /// </summary>
    /// <param name="refWindow">The reference window, generally the navigation window; when
    /// <see langword="null"/> no popup is considered a descendant and the whole stack is closed.</param>
    /// <param name="restoreFocusToWindowUnder">Whether focus should be restored to the window below the
    /// closed popup.</param>
    /// <remarks>
    /// Popups form the begin-stack chain <c>Window -&gt; Popup1 -&gt; Popup1_Child -&gt; Popup2</c>, so
    /// the stack is trimmed only while each popup is an ancestor of the reference window. Canonical
    /// asserts that every resolved entry carries <see cref="ImGuiWindowFlags.Popup"/>; the assert is
    /// realized with <c>ImAssert.Check</c>.
    /// </remarks>
    internal static void ClosePopupsOverWindow(ImGuiWindow? refWindow, bool restoreFocusToWindowUnder)
    {
        ImGuiContext g = GetContext();
        if (g.OpenPopupStack.Count == 0)
            return;

        // Do not close our own child popup windows.
        int popupCountToKeep = 0;
        if (refWindow != null)
        {
            for (; popupCountToKeep < g.OpenPopupStack.Count; popupCountToKeep++)
            {
                ImGuiPopupData popup = g.OpenPopupStack[popupCountToKeep];
                if (popup.Window == null)
                    continue;
                ImAssert.Check((popup.Window.Flags & ImGuiWindowFlags.Popup) != 0);

                // Step through every popup from bottom to top to validate its position relative to the
                // reference window; a popup survives only when the reference window lies within its
                // begin stack.
                bool refWindowIsDescendantOfPopup = false;
                for (int n = popupCountToKeep; n < g.OpenPopupStack.Count; n++)
                {
                    ImGuiWindow? popupWindow = g.OpenPopupStack[n].Window;
                    if (popupWindow != null && IsWindowWithinBeginStackOf(refWindow, popupWindow))
                    {
                        refWindowIsDescendantOfPopup = true;
                        break;
                    }
                }
                if (!refWindowIsDescendantOfPopup)
                    break;
            }
        }

        if (popupCountToKeep < g.OpenPopupStack.Count)
            ClosePopupToLevel(popupCountToKeep, restoreFocusToWindowUnder);
    }

    /// <summary>
    /// Closes every open popup except modals. Ports canonical
    /// <c>ImGui::ClosePopupsExceptModals()</c>.,
    ///.
    /// </summary>
    /// <remarks>The stack is scanned from the top down; the scan stops at the first entry whose window
    /// is missing or modal, and everything above it is removed with focus restoration.</remarks>
    internal static void ClosePopupsExceptModals()
    {
        ImGuiContext g = GetContext();

        int popupCountToKeep;
        for (popupCountToKeep = g.OpenPopupStack.Count; popupCountToKeep > 0; popupCountToKeep--)
        {
            ImGuiWindow? window = g.OpenPopupStack[popupCountToKeep - 1].Window;
            if (window == null || (window.Flags & ImGuiWindowFlags.Modal) != 0)
                break;
        }

        if (popupCountToKeep < g.OpenPopupStack.Count)
            ClosePopupToLevel(popupCountToKeep, true);
    }

    /// <summary>
    /// Trims the open-popup stack to the specified level and optionally restores navigation focus.
    /// Ports canonical <c>ImGui::ClosePopupToLevel()</c>
    ///.
    /// </summary>
    /// <param name="remaining">The number of entries to keep; this must satisfy
    /// <c>0 &lt;= remaining &lt; g.OpenPopupStack.Count</c>.</param>
    /// <param name="restoreFocusToWindowUnder">Whether focus should be restored to the window below the
    /// closed popup.</param>
    /// <remarks>
    /// <para>
    /// Only <c>g.OpenPopupStack</c> is modified here; the matching <c>g.BeginPopupStack</c> entries are
    /// popped by <c>End()</c> when the popup window closes.
    /// </para>
    /// <para>
    /// Focus restoration prefers the closed popup's parent window for a child menu and otherwise the
    /// navigation window recorded at open time; when that window was never active, focus falls back to
    /// the top-most window below the popup. The canonical assert on <paramref name="remaining"/> is
    /// realized with <c>ImAssert.Check</c>, and the entry report plus the per-entry closing report are
    /// debug-logged before the stack is trimmed.
    /// </para>
    /// </remarks>
    internal static void ClosePopupToLevel(int remaining, bool restoreFocusToWindowUnder)
    {
        ImGuiContext g = GetContext();
        DebugLogEvent(
            ImGuiDebugLogFlags.EventPopup,
            "[popup] ClosePopupToLevel({0}), restore_under={1}\n",
            remaining,
            restoreFocusToWindowUnder ? 1 : 0);
        ImAssert.Check(remaining >= 0 && remaining < g.OpenPopupStack.Count);

        // The per-entry report is emitted only when the category is enabled, so the loop is skipped
        // entirely in the common case, matching canonical.
        if ((g.DebugLogFlags & ImGuiDebugLogFlags.EventPopup) != 0)
        {
            for (int n = remaining; n < g.OpenPopupStack.Count; n++)
            {
                DebugLogEvent(
                    ImGuiDebugLogFlags.EventPopup,
                    "[popup] - Closing PopupID 0x{0:X8} Window \"{1}\"\n",
                    g.OpenPopupStack[n].PopupId,
                    g.OpenPopupStack[n].Window != null ? g.OpenPopupStack[n].Window!.Name : "NULL");
            }
        }

        // Trim the open-popup stack. The reference remains valid after removal because no further
        // mutation of the removed entry is performed.
        ImGuiPopupData prevPopup = g.OpenPopupStack[remaining];
        g.OpenPopupStack.RemoveRange(remaining, g.OpenPopupStack.Count - remaining);

        // Restore focus unless the popup window was not yet submitted and therefore never had a chance
        // to take focus.
        if (restoreFocusToWindowUnder && prevPopup.Window != null)
        {
            ImGuiWindow popupWindow = prevPopup.Window;
            ImGuiWindow? focusWindow = (popupWindow.Flags & ImGuiWindowFlags.ChildMenu) != 0
                ? popupWindow.ParentWindow
                : prevPopup.RestoreNavWindow;
            if (focusWindow != null && !focusWindow.WasActive)
                FocusTopMostWindowUnderOne(popupWindow, null, null, ImGuiFocusRequestFlags.RestoreFocusedChild);
            else
                FocusWindow(focusWindow, g.NavLayer == ImGuiNavLayer.Main
                    ? ImGuiFocusRequestFlags.RestoreFocusedChild
                    : ImGuiFocusRequestFlags.None);
        }
    }

    /// <summary>
    /// Closes the popup that was begun with <c>BeginPopup()</c> and, for menus, its top-most non-modal
    /// parent. Ports canonical <c>ImGui::CloseCurrentPopup()</c>
    ///.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The call silently does nothing when the begin-popup and open-popup stacks disagree at the top,
    /// because user code may call it defensively. Otherwise the menu-parent collapse loop walks down
    /// while the current entry is a child menu whose parent is not a menu bar, and the resulting index
    /// is passed to <see cref="ClosePopupToLevel"/>.
    /// </para>
    /// <para>Navigation highlight is suppressed for one frame in the navigation window, because a
    /// common pattern closes a popup while a menu item or selectable opens another window.</para>
    /// </remarks>
    public static void CloseCurrentPopup()
    {
        ImGuiContext g = GetContext();
        int popupIndex = g.BeginPopupStack.Count - 1;
        if (popupIndex < 0 || popupIndex >= g.OpenPopupStack.Count
            || g.BeginPopupStack[popupIndex].PopupId != g.OpenPopupStack[popupIndex].PopupId)
            return;

        // Closing a menu closes its top-most parent popup, unless that parent is a modal.
        while (popupIndex > 0)
        {
            ImGuiWindow? popupWindow = g.OpenPopupStack[popupIndex].Window;
            ImGuiWindow? parentPopupWindow = g.OpenPopupStack[popupIndex - 1].Window;
            bool closeParent = false;
            if (popupWindow != null && (popupWindow.Flags & ImGuiWindowFlags.ChildMenu) != 0)
            {
                if (parentPopupWindow != null && (parentPopupWindow.Flags & ImGuiWindowFlags.MenuBar) == 0)
                    closeParent = true;
            }
            if (!closeParent)
                break;
            popupIndex--;
        }

        DebugLogEvent(
            ImGuiDebugLogFlags.EventPopup,
            "[popup] CloseCurrentPopup {0} -> {1}\n",
            g.BeginPopupStack.Count - 1,
            popupIndex);
        ClosePopupToLevel(popupIndex, true);

        if (g.NavWindow != null)
            g.NavWindow.DC.NavHideHighlightOneFrame = true;
    }

    /// <summary>
    /// Begins the popup window for an already-open popup identifier. Ports canonical
    /// <c>ImGui::BeginPopupEx()</c>., declared in
    /// </summary>
    /// <param name="id">The popup identifier previously passed to <c>OpenPopup()</c>.</param>
    /// <param name="extraWindowFlags">Window flags supplied by the caller; the popup, no-docking and
    /// caller flags are combined before <c>Begin()</c>.</param>
    /// <returns><see langword="true"/> when the popup window began and survived clipping.</returns>
    /// <remarks>
    /// <para>
    /// The window name embeds the identifier (<c>##Popup_%08x</c>); no recycling occurs, so a popup may
    /// close and reopen within the same frame. When <c>Begin()</c> reports the popup completely clipped
    /// (for example on a zero-size display), <see cref="EndPopup"/> is called immediately.
    /// </para>
    /// <para>Child menus must use <see cref="BeginPopupMenuEx"/>; canonical asserts that
    /// <see cref="ImGuiWindowFlags.ChildMenu"/> is absent, realized here with
    /// <c>ImAssert.Check</c>.</para>
    /// <para>Chunk 6 guard: the canonical <c>NoDocking</c> bit is retained in the flag set, but docking
    /// is owned by Chunk 6 and <c>Begin()</c> currently ignores the bit.</para>
    /// </remarks>
    internal static bool BeginPopupEx(uint id, ImGuiWindowFlags extraWindowFlags)
    {
        ImGuiContext g = GetContext();
        if (!IsPopupOpen(id, ImGuiPopupFlags.None))
        {
            // Behave like Begin() and consume the queued window data.
            g.NextWindowData.ClearFlags();
            return false;
        }

        ImAssert.Check((extraWindowFlags & ImGuiWindowFlags.ChildMenu) == 0);
        string name = ImFormatString("##Popup_%08x", id);
        bool isOpen = Begin(name, extraWindowFlags | ImGuiWindowFlags.Popup | ImGuiWindowFlags.NoDocking);
        if (!isOpen)
            EndPopup();
        return isOpen;
    }

    /// <summary>
    /// Begins a child-menu popup window. Ports canonical <c>ImGui::BeginPopupMenuEx()</c>
    ///.
    /// </summary>
    /// <param name="id">The popup identifier computed by the menu that owns this popup.</param>
    /// <param name="label">The menu label; it becomes the window identifier portion of the name.</param>
    /// <param name="extraWindowFlags">Menu window flags; must include
    /// <see cref="ImGuiWindowFlags.ChildMenu"/>.</param>
    /// <returns><see langword="true"/> when the popup window began and survived clipping.</returns>
    /// <remarks>
    /// <para>
    /// Menu windows are recycled by depth through the <c>%s###Menu_%02d</c> name: the <c>###</c> makes
    /// the label the window identifier while the depth suffix keeps windows at different depths
    /// distinct.
    /// </para>
    /// <para>
    /// Because this bypasses <c>BeginChild()</c>, canonical explicitly injects
    /// <see cref="ImGuiChildFlags.AlwaysAutoResize"/> into the queued child flags when both
    /// <see cref="ImGuiWindowFlags.ChildWindow"/> and
    /// <see cref="ImGuiWindowFlags.AlwaysAutoResize"/> are requested, since that child flag is checked
    /// independently of the window flag at this revision.
    /// </para>
    /// </remarks>
    internal static bool BeginPopupMenuEx(uint id, string label, ImGuiWindowFlags extraWindowFlags)
    {
        ImGuiContext g = GetContext();
        if (!IsPopupOpen(id, ImGuiPopupFlags.None))
        {
            // Behave like Begin() and consume the queued window data.
            g.NextWindowData.ClearFlags();
            return false;
        }

        // As this bypasses BeginChild(), set ImGuiChildFlags_AlwaysAutoResize because it is checked
        // independently from ImGuiWindowFlags_AlwaysAutoResize at this revision.
        if ((extraWindowFlags & ImGuiWindowFlags.ChildWindow) != 0
            && (extraWindowFlags & ImGuiWindowFlags.AlwaysAutoResize) != 0)
        {
            if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasChildFlags) != 0)
                g.NextWindowData.ChildFlags |= ImGuiChildFlags.AlwaysAutoResize;
            else
                g.NextWindowData.ChildFlags = ImGuiChildFlags.AlwaysAutoResize;
            g.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasChildFlags;
        }

        ImAssert.Check((extraWindowFlags & ImGuiWindowFlags.ChildMenu) != 0);
        string name = ImFormatString("%s###Menu_%02d", label, g.BeginMenuDepth);
        bool isOpen = Begin(name, extraWindowFlags | ImGuiWindowFlags.Popup);
        if (!isOpen)
            EndPopup();
        return isOpen;
    }

    /// <summary>
    /// Begins the popup identified by a string, adding the canonical popup default flags. Ports
    /// canonical <c>ImGui::BeginPopup()</c>., declared
    ///.
    /// </summary>
    /// <param name="strId">The identifier string, hashed relative to the current window.</param>
    /// <param name="flags">Caller window flags; <see cref="ImGuiWindowFlags.AlwaysAutoResize"/>,
    /// <see cref="ImGuiWindowFlags.NoTitleBar"/> and <see cref="ImGuiWindowFlags.NoSavedSettings"/> are
    /// added before <see cref="BeginPopupEx"/>.</param>
    /// <returns><see langword="true"/> when the popup window began.</returns>
    /// <remarks>The early-out on an empty open-popup stack clears any queued next-window data so the
    /// call behaves like <c>Begin()</c>.</remarks>
    public static bool BeginPopup(string strId, ImGuiWindowFlags flags = 0)
    {
        ImGuiContext g = GetContext();
        if (g.OpenPopupStack.Count <= g.BeginPopupStack.Count)
        {
            // Behave like Begin() and consume the queued window data.
            g.NextWindowData.ClearFlags();
            return false;
        }

        flags |= ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings;
        uint id = GetID(strId);
        return BeginPopupEx(id, flags);
    }

    /// <summary>
    /// Begins a modal popup window without a close button. Ports canonical
    /// <c>ImGui::BeginPopupModal(const char*, bool*, ImGuiWindowFlags)</c> with a null
    /// <c>p_open</c>.
    /// </summary>
    /// <param name="name">The modal name; it is hashed relative to the current window.</param>
    /// <param name="flags">Caller window flags; the popup, modal, no-collapse and no-docking flags are
    /// added.</param>
    /// <returns><see langword="true"/> when the modal window began and remained open.</returns>
    /// <remarks>Routes through <see cref="BeginPopupModalCore"/> without a caller open flag, which
    /// selects the canonical <c>Begin(name, NULL, flags)</c> path and installs no close button.</remarks>
    public static bool BeginPopupModal(string name, ImGuiWindowFlags flags = 0)
    {
        bool pOpen = true;
        return BeginPopupModalCore(name, flags, false, ref pOpen);
    }

    /// <summary>
    /// Begins a modal popup window with an optional close button. Ports canonical
    /// <c>ImGui::BeginPopupModal(const char*, bool*, ImGuiWindowFlags)</c>
    ///.
    /// </summary>
    /// <param name="name">The modal name; it is hashed relative to the current window.</param>
    /// <param name="pOpen">When the modal is not open it is set back to <see langword="false"/>; setting
    /// it to <see langword="false"/> before the call closes the popup. The caller-supplied value of
    /// <see langword="true"/> installs the canonical close button.</param>
    /// <param name="flags">Caller window flags; the popup, modal, no-collapse and no-docking flags are
    /// added.</param>
    /// <returns><see langword="true"/> when the modal window began and remained open.</returns>
    /// <remarks>Routes through <see cref="BeginPopupModalCore"/> with the caller open flag, which
    /// selects the canonical <c>Begin(name, p_open, flags)</c> path and installs the close button.</remarks>
    public static bool BeginPopupModal(string name, ref bool pOpen, ImGuiWindowFlags flags = 0)
    {
        return BeginPopupModalCore(name, flags, true, ref pOpen);
    }

    /// <summary>
    /// Implements the shared modal-popup begin path for both <c>BeginPopupModal()</c> overloads. Ports
    /// the body of canonical <c>ImGui::BeginPopupModal()</c>
    /// </summary>
    /// <param name="name">The modal name; it is hashed relative to the current window.</param>
    /// <param name="flags">Caller window flags; the popup, modal, no-collapse and no-docking flags are
    /// added.</param>
    /// <param name="hasPOpen">Whether the caller supplied an open flag, mirroring canonical
    /// <c>p_open != NULL</c>.</param>
    /// <param name="pOpen">The caller's open flag; only meaningful when <paramref name="hasPOpen"/> is
    /// true.</param>
    /// <returns><see langword="true"/> when the modal window began and remained open.</returns>
    /// <remarks>
    /// <para>
    /// Modal visibility is owned by Dear ImGui and manipulated with <c>OpenPopup()</c>;
    /// <paramref name="pOpen"/> only controls the close button and the requested close. When the popup
    /// is not open the method clears queued next-window data, sets the flag back to false when it was
    /// supplied and true, and returns.
    /// </para>
    /// <para>
    /// Canonical centers a modal that has no queued position on the current window's viewport when that
    /// window was active, and on the main viewport otherwise. This port selects the same reference
    /// viewport; with automatic detachment unimplemented, both branches resolve to the main viewport.
    /// </para>
    /// <para>When <c>Begin()</c> reports the modal completely clipped, <see cref="EndPopup"/> is called
    /// and, if the window had begun, the open-popup stack is closed back to the begin level. This port
    /// does not emit a separate popup debug-log line at that close; the close itself is reported by the
    /// <see cref="ClosePopupToLevel"/> entry report.</para>
    /// </remarks>
    private static bool BeginPopupModalCore(string name, ImGuiWindowFlags flags, bool hasPOpen, ref bool pOpen)
    {
        ImGuiContext g = GetContext();
        uint id = GetID(name);
        if (!IsPopupOpen(id, ImGuiPopupFlags.None))
        {
            // Behave like Begin() and consume the queued window data.
            g.NextWindowData.ClearFlags();
            if (hasPOpen && pOpen)
                pOpen = false;
            return false;
        }

        // Center modal windows by default for increased visibility (mostly for backward compatibility;
        // settings and user code may override this).
        if ((g.NextWindowData.HasFlags & ImGuiNextWindowDataFlags.HasPos) == 0)
        {
            ImGuiWindow? referenceWindow = g.CurrentWindow;
            ImGuiViewport viewport = referenceWindow != null && referenceWindow.WasActive && referenceWindow.Viewport != null
                ? referenceWindow.Viewport
                : GetMainViewport();
            SetNextWindowPos(viewport.GetCenter(), ImGuiCond.FirstUseEver, new ImVec2(0.5f, 0.5f));
        }

        flags |= ImGuiWindowFlags.Popup | ImGuiWindowFlags.Modal | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking;
        bool isOpen = hasPOpen ? Begin(name, ref pOpen, flags) : Begin(name, flags);
        if (!isOpen || (hasPOpen && !pOpen))
        {
            EndPopup();
            if (isOpen)
                ClosePopupToLevel(g.BeginPopupStack.Count, true);
            return false;
        }
        return isOpen;
    }

    /// <summary>
    /// Ends a popup window begun with any of the <c>BeginPopup*()</c> functions. Ports canonical
    /// <c>ImGui::EndPopup()</c>., declared in
    /// </summary>
    /// <remarks>
    /// <para>
    /// Menus and popups wrap around for navigation, so a pending move is wrapped against the popup
    /// window when it owns the navigation focus. Around <c>End()</c> the within-popup and
    /// within-child identifiers are set to the current window so the generic misuse asserts inside
    /// <c>End()</c> can report that <c>EndPopup()</c> or <c>EndChild()</c> should have been called.
    /// </para>
    /// <para>Calling this from a non-popup window or with an empty begin-popup stack is a recoverable
    /// user error: it is reported through <see cref="AssertUserError"/> and the call returns, mirroring
    /// canonical <c>IM_ASSERT_USER_ERROR_RET</c>.</para>
    /// </remarks>
    public static void EndPopup()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        if ((window.Flags & ImGuiWindowFlags.Popup) == 0 || g.BeginPopupStack.Count <= 0)
        {
            AssertUserError(false, "Calling EndPopup() in wrong window!");
            return;
        }

        // Make all menus and popups wrap around for now; this policy may later be exposed through the
        // focus scope.
        if (ReferenceEquals(g.NavWindow, window))
            NavMoveRequestTryWrapping(window, ImGuiNavMoveFlags.LoopY);

        // Child popups do not need to be laid out.
        uint backupWithinEndPopupId = g.WithinEndPopupID;
        uint backupWithinEndChildId = g.WithinEndChildID;
        g.WithinEndPopupID = window.ID;
        if ((window.Flags & ImGuiWindowFlags.ChildWindow) != 0)
            g.WithinEndChildID = window.ID;
        End();
        g.WithinEndPopupID = backupWithinEndPopupId;
        g.WithinEndChildID = backupWithinEndChildId;
    }

    /// <summary>
    /// Derives the mouse button that opens a context menu from popup flags. Ports the logic of canonical
    /// <c>ImGui::GetMouseButtonFromPopupFlags()</c>
    ///.
    /// </summary>
    /// <param name="flags">The popup flags whose encoded mouse-button field is decoded.</param>
    /// <returns>
    /// The legacy raw value when the invalid mask bits are set (canonical maps
    /// <c>1</c>/<c>2</c> to right/middle for obsolete call sites); otherwise the decoded button, or
    /// <see cref="ImGuiMouseButton.Right"/> when no button field is present.
    /// </returns>
    /// <remarks>
    /// Part 05 binds this logic privately because the three popup context-menu request functions below
    /// are its only consumers, and because the canonical internal request for the helper must not be
    /// duplicated by internal helper methods. The method name
    /// therefore differs from the canonical one while the derivation is exact.
    /// </remarks>
    private static ImGuiMouseButton GetPopupMouseButton(ImGuiPopupFlags flags)
    {
        // Legacy bits 1 and 2 are reinterpreted as right and middle; obsolete functions are enabled in
        // this port, matching canonical without IMGUI_DISABLE_OBSOLETE_FUNCTIONS.
        if ((flags & ImGuiPopupFlags.InvalidMask_) != 0)
            return (ImGuiMouseButton)(flags & ImGuiPopupFlags.InvalidMask_);
        if ((flags & ImGuiPopupFlags.MouseButtonMask_) != 0)
            return (ImGuiMouseButton)(((int)(flags & ImGuiPopupFlags.MouseButtonMask_)
                >> (int)ImGuiPopupFlags.MouseButtonShift_) - 1);
        return ImGuiMouseButton.Right;
    }

    /// <summary>
    /// Tests whether the last submitted item requests that its context-menu popup be opened. Ports
    /// canonical <c>ImGui::IsPopupOpenRequestForItem()</c>
    ///.
    /// </summary>
    /// <param name="popupFlags">Popup flags selecting the mouse button.</param>
    /// <param name="id">The identifier of the item whose context menu is requested.</param>
    /// <returns>
    /// <see langword="true"/> when the selected mouse button was released over the item while popups may
    /// still hover it, or when the navigation subsystem requested the item's context menu through
    /// <c>g.NavOpenContextMenuItemId</c> and the item is focused or is the window move identifier.
    /// </returns>
    internal static bool IsPopupOpenRequestForItem(ImGuiPopupFlags popupFlags, uint id)
    {
        ImGuiContext g = GetContext();
        ImGuiMouseButton mouseButton = GetPopupMouseButton(popupFlags);
        if (IsMouseReleased(mouseButton) && IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
            return true;
        if (g.NavOpenContextMenuItemId == id
            && (IsItemFocused() || id == g.CurrentWindow!.MoveId))
            return true;
        return false;
    }

    /// <summary>
    /// Tests whether the current window requests that its context-menu popup be opened. Ports
    /// canonical <c>ImGui::IsPopupOpenRequestForWindow()</c>
    ///.
    /// </summary>
    /// <param name="popupFlags">Popup flags selecting the mouse button; when
    /// <see cref="ImGuiPopupFlags.NoOpenOverItems"/> is set, the mouse path is suppressed while any item
    /// is hovered.</param>
    /// <returns>
    /// <see langword="true"/> when the selected mouse button was released over the window while popups
    /// may still hover it and either no item blocks the request or item blocking is disabled, or when
    /// the navigation subsystem requested the window context menu and the navigation window is a child
    /// of the current window. The navigation ordering disambiguates item versus window requests.
    /// </returns>
    internal static bool IsPopupOpenRequestForWindow(ImGuiPopupFlags popupFlags)
    {
        ImGuiContext g = GetContext();
        ImGuiMouseButton mouseButton = GetPopupMouseButton(popupFlags);
        if (IsMouseReleased(mouseButton) && IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup))
        {
            if ((popupFlags & ImGuiPopupFlags.NoOpenOverItems) == 0 || !IsAnyItemHovered())
                return true;
        }
        if (g.NavOpenContextMenuWindowId != 0 && g.CurrentWindow!.ID != 0)
        {
            if (g.NavWindow != null && IsWindowChildOf(g.NavWindow, g.CurrentWindow!, false, false))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Opens a popup when the selected mouse button is released over the last item. Ports canonical
    /// <c>ImGui::OpenPopupOnItemClick()</c>., declared
    ///.
    /// </summary>
    /// <param name="strId">An explicit popup identifier string, or <see langword="null"/> to use the
    /// last item identifier.</param>
    /// <param name="popupFlags">Popup flags; the default opens on the right mouse button.</param>
    /// <returns>Whether a popup was opened.</returns>
    /// <remarks>This is the same as <see cref="BeginPopupContextItem"/> without the trailing
    /// <c>BeginPopup()</c>. Canonical asserts that the resolved identifier is non-zero, because a null
    /// <paramref name="strId"/> cannot be used when the last item has no identifier; the assert is
    /// realized with <c>ImAssert.Check</c>.</remarks>
    public static bool OpenPopupOnItemClick(string? strId = null, ImGuiPopupFlags popupFlags = 0)
    {
        ImGuiContext g = GetContext();
        if (IsPopupOpenRequestForItem(popupFlags, g.LastItemData.ID))
        {
            uint id = strId != null ? GetID(strId) : g.LastItemData.ID;
            ImAssert.Check(id != 0);
            return OpenPopupEx(id, popupFlags);
        }
        return false;
    }

    /// <summary>
    /// Opens and begins a popup associated with the last submitted item when the selected mouse button
    /// is released over it. Ports canonical <c>ImGui::BeginPopupContextItem()</c>
    ///.
    /// </summary>
    /// <param name="strId">An explicit popup identifier string, or <see langword="null"/> to associate
    /// the popup with the last item identifier.</param>
    /// <param name="popupFlags">Popup flags; the default opens on the right mouse button.</param>
    /// <returns><see langword="true"/> when the popup window began.</returns>
    /// <remarks>
    /// This is equivalent to resolving the identifier, calling <see cref="OpenPopupOnItemClick"/>, and
    /// then calling <c>BeginPopup()</c>, but it avoids computing the identifier twice. Canonical asserts
    /// that the resolved identifier is non-zero; the assert is realized with
    /// <c>ImAssert.Check</c>.
    /// </remarks>
    public static bool BeginPopupContextItem(string? strId = null, ImGuiPopupFlags popupFlags = 0)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        if (window.SkipItems)
            return false;
        uint id = strId != null ? GetIDWithSeed(strId, null, window.IDStack[^1]) : g.LastItemData.ID;
        ImAssert.Check(id != 0);
        if (IsPopupOpenRequestForItem(popupFlags, g.LastItemData.ID))
            OpenPopupEx(id, popupFlags);
        return BeginPopupEx(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
    }

    /// <summary>
    /// Opens and begins a popup associated with the current window when the selected mouse button is
    /// released over it. Ports canonical <c>ImGui::BeginPopupContextWindow()</c>
    ///.
    /// </summary>
    /// <param name="strId">An explicit popup identifier string, or <see langword="null"/> to use the
    /// canonical <c>"window_context"</c> identifier.</param>
    /// <param name="popupFlags">Popup flags; the default opens on the right mouse button.</param>
    /// <returns><see langword="true"/> when the popup window began.</returns>
    public static bool BeginPopupContextWindow(string? strId = null, ImGuiPopupFlags popupFlags = 0)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        string resolvedStrId = strId ?? "window_context";
        uint id = GetIDWithSeed(resolvedStrId, null, window.IDStack[^1]);
        if (IsPopupOpenRequestForWindow(popupFlags))
            OpenPopupEx(id, popupFlags);
        return BeginPopupEx(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
    }

    /// <summary>
    /// Opens and begins a popup when the selected mouse button is released over an empty area of the
    /// current window. Ports canonical <c>ImGui::BeginPopupContextVoid()</c>
    ///.
    /// </summary>
    /// <param name="strId">An explicit popup identifier string, or <see langword="null"/> to use the
    /// canonical <c>"void_context"</c> identifier.</param>
    /// <param name="popupFlags">Popup flags; the default opens on the right mouse button.</param>
    /// <returns><see langword="true"/> when the popup window began.</returns>
    /// <remarks>The popup opens only when no window is hovered at all and no modal popup is currently
    /// open.</remarks>
    public static bool BeginPopupContextVoid(string? strId = null, ImGuiPopupFlags popupFlags = 0)
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = g.CurrentWindow!;
        string resolvedStrId = strId ?? "void_context";
        uint id = GetIDWithSeed(resolvedStrId, null, window.IDStack[^1]);
        ImGuiMouseButton mouseButton = GetPopupMouseButton(popupFlags);
        if (IsMouseReleased(mouseButton) && !IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
        {
            if (GetTopMostPopupModal() == null)
                OpenPopupEx(id, popupFlags);
        }
        return BeginPopupEx(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
    }

    #endregion

    #endregion


    #region SECTION: DRAG AND SLIDER WIDGETS

    #region [SECTION] Widgets: DragScalar, DragFloat, DragInt, etc. — shared scalar support

    /// <summary>
    /// The canonical <c>FLT_MIN</c> value, the smallest positive normalized <see cref="float"/>
    /// returned by <see cref="GetMinimumStepAtDecimalPrecision"/> when no decimal precision is known
    /// </summary>
    /// <remarks>
    /// This is deliberately not <see cref="float.Epsilon"/>, which is the smallest denormal value and
    /// is therefore many orders of magnitude smaller than the C <c>FLT_MIN</c> the canonical code means.
    /// </remarks>
    private const float DragSliderFltMin = 1.17549435E-38f;

    /// <summary>
    /// Distance multiplier applied to <c>IO.MouseDragThreshold</c> before a mouse drag is considered to
    /// have passed its threshold, mirroring the canonical <c>DRAG_MOUSE_THRESHOLD_FACTOR</c> define.
    /// </summary>
    private const float DragSliderMouseThresholdFactor = 0.50f;

    /// <summary>
    /// Read-only item flag, folded from the canonical private item-flag namespace into the public
    /// <see cref="ImGuiItemFlags"/> value the widget checks.
    /// </summary>
    private const ImGuiItemFlags DragSliderItemFlagReadOnly = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_ReadOnly;

    /// <summary>
    /// Inputable item flag, folded from the canonical private item-flag namespace; it tells the item
    /// system that a drag or slider can absorb keyboard activation into text entry.
    /// </summary>
    private const ImGuiItemFlags DragSliderItemFlagInputable = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_Inputable;

    /// <summary>
    /// Internal read-only slider flag, folded from <see cref="ImGuiSliderFlagsPrivate"/>.
    /// </summary>
    private const ImGuiSliderFlags DragSliderFlagReadOnly = (ImGuiSliderFlags)ImGuiSliderFlagsPrivate.ImGuiSliderFlags_ReadOnly;

    /// <summary>
    /// Internal vertical-orientation slider flag, folded from <see cref="ImGuiSliderFlagsPrivate"/>
    /// (, ).
    /// </summary>
    private const ImGuiSliderFlags DragSliderFlagVertical = (ImGuiSliderFlags)ImGuiSliderFlagsPrivate.ImGuiSliderFlags_Vertical;

    /// <summary>
    /// Returns the smallest value change that one keyboard or gamepad tweak step should produce for a
    /// numeric field displayed with the given decimal precision.
    /// Canvases canonical <c>GetMinimumStepAtDecimalPrecision</c>
    /// </summary>
    /// <param name="decimalPrecision">
    /// Number of digits displayed after the decimal point, or a negative value when the precision is
    /// unknown or the field is an integer.
    /// </param>
    /// <returns>
    /// <see cref="DragSliderFltMin"/> for a negative precision, otherwise <c>10^-decimalPrecision</c> computed
    /// exactly from a table for precisions below ten and with a power function beyond that.
    /// </returns>
    /// <remarks>
    /// The result is used by <see cref="DragBehaviorT{T}"/>. to keep a
    /// nav tweak from moving the value less than the displayed precision allows.
    /// </remarks>
    internal static float GetMinimumStepAtDecimalPrecision(int decimalPrecision)
    {
        if (decimalPrecision < 0)
            return DragSliderFltMin;
        switch (decimalPrecision)
        {
            case 0: return 1.0f;
            case 1: return 0.1f;
            case 2: return 0.01f;
            case 3: return 0.001f;
            case 4: return 0.0001f;
            case 5: return 0.00001f;
            case 6: return 0.000001f;
            case 7: return 0.0000001f;
            case 8: return 0.00000001f;
            case 9: return 0.000000001f;
            default: return MathF.Pow(10.0f, -decimalPrecision);
        }
    }

    /// <summary>
    /// Rounds a floating-point scalar by formatting it with the widget's display format and reading the
    /// text back, so the stored value matches what the user sees.
    /// Canvases canonical <c>RoundScalarWithFormatT</c>
    /// </summary>
    /// <typeparam name="T">The scalar type; canonical only supports <see cref="float"/> and <see cref="double"/>.</typeparam>
    /// <param name="format">The widget display format, which may carry leading or trailing decoration.</param>
    /// <param name="dataType">The data type of <paramref name="v"/>.</param>
    /// <param name="v">The value to round.</param>
    /// <returns>
    /// The round-tripped value, or <paramref name="v"/> unchanged when the format holds no visible
    /// number.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Canonical declares this helper as returning <c>TYPE</c>; the <c>CONTRACT.md</c> §4 binding lists a
    /// <see cref="string"/> return, which contradicts canonical and is therefore not followed here.
    /// </para>
    /// <para>
    /// Canonical sanitizes the format for printing by removing the stb_sprintf custom flags
    /// <c>'</c>, <c>$</c> and <c>_</c>. The shared
    /// <c>ImParseFormatSanitizeForPrinting</c> helper is not present in part 90, so the sanitization is
    /// performed inline here on the exact trimmed specifier range; this is reported as a missing
    /// part-90 helper rather than silently diverging.
    /// </para>
    /// <para>
    /// The formatted text is parsed back with the invariant culture, which is the managed equivalent of
    /// the canonical <c>ImAtof</c> scan and avoids the locale-sensitive parsing paths. The text always
    /// comes from this port's own formatter, so no user input reaches this parse.
    /// </para>
    /// </remarks>
    internal static T RoundScalarWithFormatT<T>(string format, ImGuiDataType dataType, T v)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        ImAssert.Check(
            dataType == ImGuiDataType.Float || dataType == ImGuiDataType.Double,
            "RoundScalarWithFormatT only supports Float and Double.");

        int fmtStart = ImParseFormatFindStart(format);
        if (fmtStart >= format.Length || format[fmtStart] != '%')
            return v;
        if (fmtStart + 1 < format.Length && format[fmtStart + 1] == '%')
            return v;

        int fmtEnd = ImParseFormatFindEnd(format, fmtStart);
        System.Text.StringBuilder sanitizedBuilder = new System.Text.StringBuilder(fmtEnd - fmtStart);
        for (int i = fmtStart; i < fmtEnd; i++)
        {
            char c = format[i];
            if (c != '\'' && c != '$' && c != '_')
                sanitizedBuilder.Append(c);
        }

        string formatted = ImFormatString(sanitizedBuilder.ToString(), v);
        double parsed = DragSliderParseFormattedDouble(formatted);
        return DragSliderFromDouble<T>(dataType, parsed);
    }

    /// <summary>
    /// Parses the leading decimal number of a string produced by <see cref="ImFormatString"/>.
    /// </summary>
    /// <param name="text">The formatted text.</param>
    /// <returns>
    /// The leading number, or zero when the text holds no digit. This mirrors the canonical
    /// <c>ImAtof</c> prefix scan used by <c>RoundScalarWithFormatT</c>.
    /// </returns>
    /// <remarks>
    /// Only the leading sign, digits, decimal point and exponent are consumed; any trailing characters
    /// are ignored exactly as <c>atof</c> ignores them. The parse uses the invariant culture so a host
    /// locale cannot change the decimal separator.
    /// </remarks>
    private static double DragSliderParseFormattedDouble(string text)
    {
        int i = 0;
        int length = text.Length;
        while (i < length && (text[i] == ' ' || text[i] == '\t'))
            i++;

        int start = i;
        if (i < length && (text[i] == '+' || text[i] == '-'))
            i++;
        int digitsStart = i;
        while (i < length && text[i] >= '0' && text[i] <= '9')
            i++;
        if (i < length && text[i] == '.')
        {
            i++;
            while (i < length && text[i] >= '0' && text[i] <= '9')
                i++;
        }
        if (i == digitsStart)
            return 0.0;
        if (i < length && (text[i] == 'e' || text[i] == 'E'))
        {
            int exponentStart = i;
            i++;
            if (i < length && (text[i] == '+' || text[i] == '-'))
                i++;
            int exponentDigitsStart = i;
            while (i < length && text[i] >= '0' && text[i] <= '9')
                i++;
            if (i == exponentDigitsStart)
                i = exponentStart;
        }

        string number = text.Substring(start, i - start);
        return double.TryParse(
            number,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : 0.0;
    }

    /// <summary>
    /// Reports whether a data type is one of the two floating-point types the drag and slider widgets treat specially.
    /// </summary>
    /// <param name="dataType">The data type to classify.</param>
    /// <returns><see langword="true"/> for <see cref="ImGuiDataType.Float"/> and <see cref="ImGuiDataType.Double"/>.</returns>
    private static bool DragSliderIsFloatingPoint(ImGuiDataType dataType)
    {
        return dataType == ImGuiDataType.Float || dataType == ImGuiDataType.Double;
    }

    /// <summary>
    /// Reads a generic scalar as a <see cref="double"/>.
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of <paramref name="value"/>.</param>
    /// <param name="value">The value to read.</param>
    /// <returns>The value widened to <see cref="double"/>.</returns>
    /// <remarks>
    /// Canonical instantiates its templates with a <c>FLOATTYPE</c> that is <see cref="float"/> for the
    /// 32-bit type group and <see cref="double"/> for the 64-bit group. The port's single generic
    /// parameter cannot carry both, so the floating-point parametric math is evaluated in
    /// <see cref="double"/> and cast back to <typeparamref name="T"/> on store. This is a documented
    /// precision superset, not a behavioral redesign: extent values are special-cased by
    /// <see cref="ScaleValueFromRatioT{T}"/> and integer overflow handling uses the exact 32-bit or
    /// 64-bit arithmetic of <paramref name="dataType"/>.
    /// </remarks>
    private static double DragSliderToDouble<T>(ImGuiDataType dataType, T value)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.S8: return System.Runtime.CompilerServices.Unsafe.As<T, sbyte>(ref value);
            case ImGuiDataType.U8: return System.Runtime.CompilerServices.Unsafe.As<T, byte>(ref value);
            case ImGuiDataType.S16: return System.Runtime.CompilerServices.Unsafe.As<T, short>(ref value);
            case ImGuiDataType.U16: return System.Runtime.CompilerServices.Unsafe.As<T, ushort>(ref value);
            case ImGuiDataType.S32: return System.Runtime.CompilerServices.Unsafe.As<T, int>(ref value);
            case ImGuiDataType.U32: return System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref value);
            case ImGuiDataType.S64: return System.Runtime.CompilerServices.Unsafe.As<T, long>(ref value);
            case ImGuiDataType.U64: return System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref value);
            case ImGuiDataType.Float: return System.Runtime.CompilerServices.Unsafe.As<T, float>(ref value);
            case ImGuiDataType.Double: return System.Runtime.CompilerServices.Unsafe.As<T, double>(ref value);
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return 0.0;
        }
    }

    /// <summary>
    /// Stores a <see cref="double"/> into a generic scalar slot, converting to the concrete type named by
    /// <paramref name="dataType"/> first.
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of the destination.</param>
    /// <param name="value">The value to store; it is truncated toward zero for integer destinations.</param>
    /// <returns>The stored value.</returns>
    /// <remarks>
    /// The inverse of <see cref="DragSliderToDouble{T}"/> for the values produced by the parametric scale
    /// math. Integer conversions intentionally discard the fractional part, matching the canonical
    /// <c>(TYPE)</c> casts.
    /// </remarks>
    private static T DragSliderFromDouble<T>(ImGuiDataType dataType, double value)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.S8: { sbyte tmp = (sbyte)value; return System.Runtime.CompilerServices.Unsafe.As<sbyte, T>(ref tmp); }
            case ImGuiDataType.U8: { byte tmp = (byte)value; return System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref tmp); }
            case ImGuiDataType.S16: { short tmp = (short)value; return System.Runtime.CompilerServices.Unsafe.As<short, T>(ref tmp); }
            case ImGuiDataType.U16: { ushort tmp = (ushort)value; return System.Runtime.CompilerServices.Unsafe.As<ushort, T>(ref tmp); }
            case ImGuiDataType.S32: { int tmp = (int)value; return System.Runtime.CompilerServices.Unsafe.As<int, T>(ref tmp); }
            case ImGuiDataType.U32: { uint tmp = (uint)value; return System.Runtime.CompilerServices.Unsafe.As<uint, T>(ref tmp); }
            case ImGuiDataType.S64: { long tmp = (long)value; return System.Runtime.CompilerServices.Unsafe.As<long, T>(ref tmp); }
            case ImGuiDataType.U64: { ulong tmp = (ulong)value; return System.Runtime.CompilerServices.Unsafe.As<ulong, T>(ref tmp); }
            case ImGuiDataType.Float: { float tmp = (float)value; return System.Runtime.CompilerServices.Unsafe.As<float, T>(ref tmp); }
            case ImGuiDataType.Double: { double tmp = value; return System.Runtime.CompilerServices.Unsafe.As<double, T>(ref tmp); }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return default;
        }
    }

    /// <summary>
    /// Computes the difference of two scalars using the exact arithmetic width of their data type.
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of both operands.</param>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The difference widened to <see cref="double"/>.</returns>
    /// <remarks>
    /// Integer subtraction wraps modulo the type width exactly as canonical's <c>v_max - v_min</c> does
    /// ; unsigned differences therefore stay
    /// non-negative until they wrap, matching the unsigned comparisons canonical performs.
    /// </remarks>
    private static double DragSliderDifference<T>(ImGuiDataType dataType, T a, T b)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.S32:
            {
                int av = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref a);
                int bv = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref b);
                return unchecked(av - bv);
            }
            case ImGuiDataType.U32:
            {
                uint av = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref a);
                uint bv = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref b);
                return unchecked(av - bv);
            }
            case ImGuiDataType.S64:
            {
                long av = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref a);
                long bv = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref b);
                return unchecked(av - bv);
            }
            case ImGuiDataType.U64:
            {
                ulong av = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref a);
                ulong bv = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref b);
                return unchecked(av - bv);
            }
            case ImGuiDataType.Float:
            {
                float av = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref a);
                float bv = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref b);
                return av - bv;
            }
            case ImGuiDataType.Double:
            {
                double av = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref a);
                double bv = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref b);
                return av - bv;
            }
            default:
                return DragSliderToDouble(dataType, a) - DragSliderToDouble(dataType, b);
        }
    }

    /// <summary>
    /// Adds the drag accumulator to a scalar using the signed accumulator type canonical selects for the
    /// data type.
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of <paramref name="value"/>.</param>
    /// <param name="value">The value the accumulator is added to.</param>
    /// <param name="accum">The accumulated pixel-equivalent delta.</param>
    /// <returns>The wrapped sum.</returns>
    /// <remarks>
    /// Canonical widens 32-bit types to <c>ImS32</c> and 64-bit types to <c>ImS64</c> before adding, so
    /// <c>uint</c> and <c>ulong</c> values wrap through the signed representation. The port reproduces
    /// that wrap with unchecked conversions.
    /// </remarks>
    private static T DragSliderAddAccum<T>(ImGuiDataType dataType, T value, float accum)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.S32:
            {
                int v = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref value);
                v = unchecked(v + (int)accum);
                return System.Runtime.CompilerServices.Unsafe.As<int, T>(ref v);
            }
            case ImGuiDataType.U32:
            {
                uint v = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref value);
                v = unchecked(v + (uint)(int)accum);
                return System.Runtime.CompilerServices.Unsafe.As<uint, T>(ref v);
            }
            case ImGuiDataType.S64:
            {
                long v = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref value);
                v = unchecked(v + (long)accum);
                return System.Runtime.CompilerServices.Unsafe.As<long, T>(ref v);
            }
            case ImGuiDataType.U64:
            {
                ulong v = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref value);
                v = unchecked(v + (ulong)(long)accum);
                return System.Runtime.CompilerServices.Unsafe.As<ulong, T>(ref v);
            }
            case ImGuiDataType.Float:
            {
                float v = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref value);
                v += accum;
                return System.Runtime.CompilerServices.Unsafe.As<float, T>(ref v);
            }
            case ImGuiDataType.Double:
            {
                double v = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref value);
                v += accum;
                return System.Runtime.CompilerServices.Unsafe.As<double, T>(ref v);
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return value;
        }
    }

    /// <summary>
    /// Computes the signed difference canonical subtracts from the drag accumulator after rounding
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of both operands.</param>
    /// <param name="a">The rounded value.</param>
    /// <param name="b">The value before this frame's drag.</param>
    /// <returns>The difference in the signed accumulator type, widened to <see cref="float"/>.</returns>
    /// <remarks>
    /// Integer differences are computed in the canonical signed accumulator type (<c>ImS32</c> or
    /// <c>ImS64</c>) so the unsigned types wrap through the signed representation, exactly as the
    /// canonical template instantiation does.
    /// </remarks>
    private static float DragSliderSignedDifference<T>(ImGuiDataType dataType, T a, T b)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.S32:
            {
                int av = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref a);
                int bv = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref b);
                return unchecked(av - bv);
            }
            case ImGuiDataType.U32:
            {
                uint av = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref a);
                uint bv = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref b);
                return unchecked((int)(av - bv));
            }
            case ImGuiDataType.S64:
            {
                long av = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref a);
                long bv = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref b);
                return unchecked(av - bv);
            }
            case ImGuiDataType.U64:
            {
                ulong av = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref a);
                ulong bv = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref b);
                return unchecked((long)(av - bv));
            }
            case ImGuiDataType.Float:
            {
                float av = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref a);
                float bv = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref b);
                return av - bv;
            }
            case ImGuiDataType.Double:
            {
                double av = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref a);
                double bv = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref b);
                return (float)(av - bv);
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return 0.0f;
        }
    }

    /// <summary>
    /// Applies one wrapped range step used by the <see cref="ImGuiSliderFlags.WrapAround"/> path.
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of all three scalars.</param>
    /// <param name="value">The value being wrapped.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="sign">Positive to add the range, negative to subtract it.</param>
    /// <returns>The wrapped value.</returns>
    /// <remarks>
    /// Canonical adds <c>(is_floating_point ? 0 : 1)</c> to the range so an integer range of
    /// <c>[min, max]</c> has width <c>max - min + 1</c>; floating-point ranges use the exact width.
    /// </remarks>
    private static T DragSliderWrapOffset<T>(ImGuiDataType dataType, T value, T vMin, T vMax, int sign)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.Float:
            {
                float v = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref value);
                float lo = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref vMin);
                float hi = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref vMax);
                float result = sign > 0 ? v + (hi - lo) : v - (hi - lo);
                return System.Runtime.CompilerServices.Unsafe.As<float, T>(ref result);
            }
            case ImGuiDataType.Double:
            {
                double v = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref value);
                double lo = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref vMin);
                double hi = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref vMax);
                double result = sign > 0 ? v + (hi - lo) : v - (hi - lo);
                return System.Runtime.CompilerServices.Unsafe.As<double, T>(ref result);
            }
            case ImGuiDataType.S32:
            {
                int v = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref value);
                int lo = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vMin);
                int hi = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vMax);
                int offset = unchecked(hi - lo + 1);
                int result = sign > 0 ? unchecked(v + offset) : unchecked(v - offset);
                return System.Runtime.CompilerServices.Unsafe.As<int, T>(ref result);
            }
            case ImGuiDataType.U32:
            {
                uint v = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref value);
                uint lo = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vMin);
                uint hi = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vMax);
                uint offset = unchecked(hi - lo + 1u);
                uint result = sign > 0 ? unchecked(v + offset) : unchecked(v - offset);
                return System.Runtime.CompilerServices.Unsafe.As<uint, T>(ref result);
            }
            case ImGuiDataType.S64:
            {
                long v = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref value);
                long lo = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vMin);
                long hi = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vMax);
                long offset = unchecked(hi - lo + 1);
                long result = sign > 0 ? unchecked(v + offset) : unchecked(v - offset);
                return System.Runtime.CompilerServices.Unsafe.As<long, T>(ref result);
            }
            case ImGuiDataType.U64:
            {
                ulong v = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref value);
                ulong lo = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vMin);
                ulong hi = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vMax);
                ulong offset = unchecked(hi - lo + 1u);
                ulong result = sign > 0 ? unchecked(v + offset) : unchecked(v - offset);
                return System.Runtime.CompilerServices.Unsafe.As<ulong, T>(ref result);
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return value;
        }
    }

    /// <summary>
    /// Clamps a generic scalar to an inclusive range.
    /// </summary>
    /// <typeparam name="T">The scalar type.</typeparam>
    /// <param name="v">The value to clamp.</param>
    /// <param name="lo">The lower bound.</param>
    /// <param name="hi">The upper bound.</param>
    /// <returns>The clamped value. The result is undefined when <paramref name="lo"/> is greater than
    /// <paramref name="hi"/>, matching the canonical <c>ImClamp</c> contract.</returns>
    private static T DragSliderClamp<T>(T v, T lo, T hi)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        if (v.CompareTo(lo) < 0)
            return lo;
        if (v.CompareTo(hi) > 0)
            return hi;
        return v;
    }

    /// <summary>
    /// Computes the linear parametric position of <paramref name="vClamped"/> on the range
    /// <c>[vMin, vMax]</c>, reproducing the canonical linear branch of
    /// <c>ScaleRatioFromValueT</c>.
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of all three scalars.</param>
    /// <param name="vClamped">The value already clamped to the range.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <returns>The parametric position in <c>[0,1]</c>.</returns>
    /// <remarks>
    /// Integer types compute the numerator and denominator in their own width and then convert to
    /// <see cref="float"/>, matching canonical's <c>(FLOATTYPE)(SIGNEDTYPE)(.)</c> casts; the unsigned
    /// types wrap through the signed accumulator before the conversion.
    /// </remarks>
    private static float DragSliderScaleLinearRatio<T>(ImGuiDataType dataType, T vClamped, T vMin, T vMax)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.Float:
            {
                float a = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref vClamped);
                float lo = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref vMin);
                float hi = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref vMax);
                return (a - lo) / (hi - lo);
            }
            case ImGuiDataType.Double:
            {
                double a = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref vClamped);
                double lo = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref vMin);
                double hi = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref vMax);
                return (float)((a - lo) / (hi - lo));
            }
            case ImGuiDataType.S32:
            {
                int a = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vClamped);
                int lo = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vMin);
                int hi = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vMax);
                int numerator = unchecked(a - lo);
                int denominator = unchecked(hi - lo);
                return (float)numerator / (float)denominator;
            }
            case ImGuiDataType.U32:
            {
                uint a = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vClamped);
                uint lo = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vMin);
                uint hi = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vMax);
                int numerator = unchecked((int)(a - lo));
                int denominator = unchecked((int)(hi - lo));
                return (float)numerator / (float)denominator;
            }
            case ImGuiDataType.S64:
            {
                long a = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vClamped);
                long lo = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vMin);
                long hi = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vMax);
                double numerator = unchecked(a - lo);
                double denominator = unchecked(hi - lo);
                return (float)(numerator / denominator);
            }
            case ImGuiDataType.U64:
            {
                ulong a = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vClamped);
                ulong lo = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vMin);
                ulong hi = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vMax);
                double numerator = unchecked((long)(a - lo));
                double denominator = unchecked((long)(hi - lo));
                return (float)(numerator / denominator);
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return 0.0f;
        }
    }

    /// <summary>
    /// Converts a parametric position back into an integer scalar, reproducing the canonical integer
    /// branch of <c>ScaleValueFromRatioT</c>.
    /// </summary>
    /// <typeparam name="T">The concrete integer scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of both bounds.</param>
    /// <param name="t">The parametric position in <c>[0,1)</c>.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <returns>The scalar nearest the requested position.</returns>
    /// <remarks>
    /// The <c>±0.5</c> rounding makes the clicking position match the grab box. The extent values are
    /// handled by <see cref="ScaleValueFromRatioT{T}"/> before this helper runs, so the lossy final
    /// multiply never has to reach the exact bounds.
    /// </remarks>
    private static T DragSliderScaleLinearValue<T>(ImGuiDataType dataType, float t, T vMin, T vMax)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        switch (dataType)
        {
            case ImGuiDataType.S32:
            {
                int lo = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vMin);
                int hi = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref vMax);
                int range = unchecked(hi - lo);
                float offset = range * t;
                int result = unchecked(lo + (int)(offset + (lo > hi ? -0.5f : 0.5f)));
                return System.Runtime.CompilerServices.Unsafe.As<int, T>(ref result);
            }
            case ImGuiDataType.U32:
            {
                uint lo = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vMin);
                uint hi = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref vMax);
                int loSigned = unchecked((int)lo);
                int hiSigned = unchecked((int)hi);
                int range = unchecked(hiSigned - loSigned);
                float offset = range * t;
                int result = unchecked(loSigned + (int)(offset + (loSigned > hiSigned ? -0.5f : 0.5f)));
                uint stored = unchecked((uint)result);
                return System.Runtime.CompilerServices.Unsafe.As<uint, T>(ref stored);
            }
            case ImGuiDataType.S64:
            {
                long lo = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vMin);
                long hi = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref vMax);
                long range = unchecked(hi - lo);
                float offset = (float)range * t;
                long result = unchecked(lo + (long)((double)offset + (lo > hi ? -0.5 : 0.5)));
                return System.Runtime.CompilerServices.Unsafe.As<long, T>(ref result);
            }
            case ImGuiDataType.U64:
            {
                ulong lo = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vMin);
                ulong hi = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref vMax);
                long loSigned = unchecked((long)lo);
                long hiSigned = unchecked((long)hi);
                long range = unchecked(hiSigned - loSigned);
                float offset = (float)range * t;
                long result = unchecked(loSigned + (long)((double)offset + (loSigned > hiSigned ? -0.5 : 0.5)));
                ulong stored = unchecked((ulong)result);
                return System.Runtime.CompilerServices.Unsafe.As<ulong, T>(ref stored);
            }
            default:
                ImAssert.Check(false, "DragSliderScaleLinearValue only supports integer data types.");
                return vMin;
        }
    }

    /// <summary>
    /// Clamps a float to the inclusive <c>[0,1]</c> interval, mirroring canonical <c>ImSaturate</c>.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <returns>The clamped value.</returns>
    private static float DragSliderSaturate(float value)
    {
        if (value < 0.0f)
            return 0.0f;
        return value > 1.0f ? 1.0f : value;
    }

    /// <summary>
    /// Linearly interpolates between two floats, mirroring canonical <c>ImLerp</c>.
    /// </summary>
    /// <param name="a">The value at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The value at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation parameter.</param>
    /// <returns><c>a + (b - a) * t</c>.</returns>
    private static float DragSliderLerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    /// <summary>
    /// Orders two boxed scalars of the same data type.
    /// </summary>
    /// <param name="dataType">The data type of both values.</param>
    /// <param name="a">The left value, boxed as the concrete type named by <paramref name="dataType"/>.</param>
    /// <param name="b">The right value, boxed as the same concrete type.</param>
    /// <returns><c>-1</c> when <paramref name="a"/> is smaller, <c>+1</c> when larger, otherwise <c>0</c>.</returns>
    /// <remarks>
    /// Realizes the canonical <c>DataTypeCompare</c> call inside <c>TempInputIsClampEnabled</c>
    ///. on the port's boxed scalar boundary.
    /// </remarks>
    private static int DragSliderCompareBoxed(ImGuiDataType dataType, object a, object b)
    {
        switch (dataType)
        {
            case ImGuiDataType.S8: return DataTypeCompare(dataType, (sbyte)a, (sbyte)b);
            case ImGuiDataType.U8: return DataTypeCompare(dataType, (byte)a, (byte)b);
            case ImGuiDataType.S16: return DataTypeCompare(dataType, (short)a, (short)b);
            case ImGuiDataType.U16: return DataTypeCompare(dataType, (ushort)a, (ushort)b);
            case ImGuiDataType.S32: return DataTypeCompare(dataType, (int)a, (int)b);
            case ImGuiDataType.U32: return DataTypeCompare(dataType, (uint)a, (uint)b);
            case ImGuiDataType.S64: return DataTypeCompare(dataType, (long)a, (long)b);
            case ImGuiDataType.U64: return DataTypeCompare(dataType, (ulong)a, (ulong)b);
            case ImGuiDataType.Float: return DataTypeCompare(dataType, (float)a, (float)b);
            case ImGuiDataType.Double: return DataTypeCompare(dataType, (double)a, (double)b);
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return 0;
        }
    }

    /// <summary>
    /// Copies the scalar into the context's activation-time backup storage.
    /// </summary>
    /// <param name="dataType">The data type of <paramref name="pData"/>.</param>
    /// <param name="pData">The boxed scalar to back up.</param>
    /// <remarks>
    /// Canonical performs <c>memcpy(&amp;g.ActiveIdValueOnActivation, p_data, DataTypeGetInfo(data_type)-&gt;Size)</c>
    /// when a drag or slider becomes active.
    /// The main library never reads the backup; it exists so mods can revert an interaction, and the
    /// port preserves it for the same reason. Only <see cref="ImGuiDataTypeInfo.Size"/> bytes are
    /// written, matching the canonical width for narrow types.
    /// </remarks>
    private static void DragSliderStoreActivationValue(ImGuiDataType dataType, object? pData)
    {
        ImGuiContext g = GetCurrentContext()!;
        byte[] storage = g.ActiveIdValueOnActivation.Data;
        switch (dataType)
        {
            case ImGuiDataType.S8: { sbyte value = (sbyte)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.U8: { byte value = (byte)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.S16: { short value = (short)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.U16: { ushort value = (ushort)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.S32: { int value = (int)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.U32: { uint value = (uint)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.S64: { long value = (long)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.U64: { ulong value = (ulong)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.Float: { float value = (float)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            case ImGuiDataType.Double: { double value = (double)pData!; System.Runtime.InteropServices.MemoryMarshal.Write(storage.AsSpan(), in value); break; }
            default: ImAssert.Check(false, "Invalid ImGuiDataType value."); break;
        }
    }

    #endregion

    #region [SECTION] Widgets: DragScalar, DragFloat, DragInt, etc.

    /// <summary>
    /// Applies one frame of drag interaction to an active scalar widget.
    /// Canvases canonical <c>DragBehaviorT</c>
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="v">The live value in user memory, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel; zero selects the bounded-range default.</param>
    /// <param name="vMin">The lower bound, already unwrapped by the caller.</param>
    /// <param name="vMax">The upper bound, already unwrapped by the caller.</param>
    /// <param name="format">The display format used to derive precision and to round the value.</param>
    /// <param name="flags">The slider flags; <see cref="ImGuiSliderFlags.WrapAround"/> is honored only here.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// <para>
    /// Canonical is a three-parameter template (<c>TYPE, SIGNEDTYPE, FLOATTYPE</c>); the CONTRACT §3
    /// binding collapses it to a single <typeparamref name="T"/> and the port preserves the canonical
    /// 32-bit versus 64-bit accumulator split inside its arithmetic helpers.
    /// </para>
    /// <para>
    /// The logarithmic branch converts through the parametric space of
    /// <see cref="ScaleRatioFromValueT{T}"/> and <see cref="ScaleValueFromRatioT{T}"/>; the drag-specific
    /// zero dead-zone is deliberately zero.
    /// </para>
    /// <para>
    /// The state read and written is the canonical set: <c>g.ActiveIdSource</c>, <c>g.IO.MouseDelta</c>,
    /// <c>g.IO.KeyAlt</c>, <c>g.IO.KeyShift</c>, <c>g.ActiveIdIsJustActivated</c>,
    /// <c>g.NavInputSource</c>, <c>g.DragCurrentAccum</c> and <c>g.DragCurrentAccumDirty</c>.
    /// </para>
    /// </remarks>
    internal static bool DragBehaviorT<T>(ImGuiDataType dataType, ref T v, float vSpeed, T vMin, T vMax, string? format, ImGuiSliderFlags flags)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiAxis axis = (flags & DragSliderFlagVertical) != 0 ? ImGuiAxis.Y : ImGuiAxis.X;
        bool isBounded = vMin.CompareTo(vMax) < 0 || (vMin.Equals(vMax) && (!vMin.Equals(default(T)) || (flags & ImGuiSliderFlags.ClampZeroRange) != 0));
        bool isWrapped = isBounded && (flags & ImGuiSliderFlags.WrapAround) != 0;
        bool isLogarithmic = (flags & ImGuiSliderFlags.Logarithmic) != 0;
        bool isFloatingPoint = DragSliderIsFloatingPoint(dataType);

        // Default tweak speed: a bounded range drags across its whole extent over a fixed pixel distance.
        double range = DragSliderDifference(dataType, vMax, vMin);
        if (vSpeed == 0.0f && isBounded && range < float.MaxValue)
            vSpeed = (float)(range * g.DragSpeedDefaultRatio);

        // Mouse and nav input both accumulate into g.DragCurrentAccum, which is flushed as soon as it
        // moves the value by at least one display-precision step.
        float adjustDelta = 0.0f;
        if (g.ActiveIdSource == ImGuiInputSource.Mouse && IsMousePosValid() && IsMouseDragPastThreshold(ImGuiMouseButton.Left, g.IO.MouseDragThreshold * DragSliderMouseThresholdFactor))
        {
            adjustDelta = AxisComponent(g.IO.MouseDelta, axis);
            if (g.IO.KeyAlt && (flags & ImGuiSliderFlags.NoSpeedTweaks) == 0)
                adjustDelta *= 1.0f / 100.0f;
            if (g.IO.KeyShift && (flags & ImGuiSliderFlags.NoSpeedTweaks) == 0)
                adjustDelta *= 10.0f;
        }
        else if (g.ActiveIdSource == ImGuiInputSource.Keyboard || g.ActiveIdSource == ImGuiInputSource.Gamepad)
        {
            int decimalPrecision = isFloatingPoint ? ImParseFormatPrecision(format ?? string.Empty, 3) : 0;
            bool tweakSlow = IsKeyDown(g.NavInputSource == ImGuiInputSource.Gamepad ? ImGuiKey.GamepadL1 : ImGuiKey.ImGuiMod_Ctrl);
            bool tweakFast = IsKeyDown(g.NavInputSource == ImGuiInputSource.Gamepad ? ImGuiKey.GamepadR1 : ImGuiKey.ImGuiMod_Shift);
            float tweakFactor = (flags & ImGuiSliderFlags.NoSpeedTweaks) != 0 ? 1.0f : tweakSlow ? 0.1f : tweakFast ? 10.0f : 1.0f;
            adjustDelta = GetNavTweakPressedAmount(axis) * tweakFactor;
            vSpeed = MathF.Max(vSpeed, GetMinimumStepAtDecimalPrecision(decimalPrecision));
        }
        adjustDelta *= vSpeed;

        // For vertical drag, Up raises the value, matching vertical sliders.
        if (axis == ImGuiAxis.Y)
            adjustDelta = -adjustDelta;

        // For logarithmic use the range is effectively 0.1, so scale the delta into that range.
        if (isLogarithmic && range < float.MaxValue && range > 0.000001f)
            adjustDelta /= (float)range;

        // Reset the accumulator on activation, and refuse to alter or clamp a value that is already past
        // the limits and being pushed further outward.
        bool isJustActivated = g.ActiveIdIsJustActivated;
        bool isAlreadyPastLimitsAndPushingOutward = isBounded && !isWrapped
            && ((v.CompareTo(vMax) >= 0 && adjustDelta > 0.0f) || (v.CompareTo(vMin) <= 0 && adjustDelta < 0.0f));
        if (isJustActivated || isAlreadyPastLimitsAndPushingOutward)
        {
            g.DragCurrentAccum = 0.0f;
            g.DragCurrentAccumDirty = false;
        }
        else if (adjustDelta != 0.0f)
        {
            g.DragCurrentAccum += adjustDelta;
            g.DragCurrentAccumDirty = true;
        }

        if (!g.DragCurrentAccumDirty)
            return false;

        T vCur = v;
        double vOldRefForAccumRemainder = 0.0;
        float logarithmicZeroEpsilon = 0.0f;
        const float zeroDeadzoneHalfsize = 0.0f; // Drag widgets have no dead zone.
        if (isLogarithmic)
        {
            int decimalPrecision = isFloatingPoint ? ImParseFormatPrecision(format ?? string.Empty, 3) : 1;
            logarithmicZeroEpsilon = MathF.Pow(0.1f, decimalPrecision);

            float vOldParametric = ScaleRatioFromValueT(dataType, vCur, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);
            float vNewParametric = vOldParametric + g.DragCurrentAccum;
            vCur = ScaleValueFromRatioT(dataType, vNewParametric, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);
            vOldRefForAccumRemainder = vOldParametric;
        }
        else
        {
            vCur = DragSliderAddAccum(dataType, vCur, g.DragCurrentAccum);
        }

        // Round to the display precision derived from the format string.
        if (isFloatingPoint && (flags & ImGuiSliderFlags.NoRoundToFormat) == 0)
            vCur = RoundScalarWithFormatT(format ?? string.Empty, dataType, vCur);

        // Preserve the remainder after rounding, which is what allows sub-step slow tweaking.
        g.DragCurrentAccumDirty = false;
        if (isLogarithmic)
        {
            float vNewParametric = ScaleRatioFromValueT(dataType, vCur, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);
            g.DragCurrentAccum -= (float)(vNewParametric - vOldRefForAccumRemainder);
        }
        else
        {
            g.DragCurrentAccum -= DragSliderSignedDifference(dataType, vCur, v);
        }

        // Losing the sign of zero keeps the displayed text and stored value consistent.
        if (vCur.Equals(default(T)))
            vCur = default;

        if (!v.Equals(vCur) && isBounded)
        {
            if (isWrapped)
            {
                if (vCur.CompareTo(vMin) < 0)
                    vCur = DragSliderWrapOffset(dataType, vCur, vMin, vMax, +1);
                if (vCur.CompareTo(vMax) > 0)
                    vCur = DragSliderWrapOffset(dataType, vCur, vMin, vMax, -1);
            }
            else
            {
                // The extra integer clauses guard against signed/unsigned overflow wrap-around.
                if (vCur.CompareTo(vMin) < 0 || (vCur.CompareTo(v) > 0 && adjustDelta < 0.0f && !isFloatingPoint))
                    vCur = vMin;
                if (vCur.CompareTo(vMax) > 0 || (vCur.CompareTo(v) < 0 && adjustDelta > 0.0f && !isFloatingPoint))
                    vCur = vMax;
            }
        }

        if (v.Equals(vCur))
            return false;
        v = vCur;
        return true;
    }

    /// <summary>
    /// Dispatches drag interaction to the generic core for the concrete data type.
    /// Canvases canonical <c>DragBehavior</c>
    /// </summary>
    /// <param name="id">The widget identifier.</param>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="pV">The boxed live value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="pMin">The optional boxed lower bound.</param>
    /// <param name="pMax">The optional boxed upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// <para>
    /// Canonical takes <c>void*</c> and passes the caller's pointer straight into the template, so the
    /// generic core writes directly into the caller's memory
    ///. Because C# cannot express an untyped pointer, the port
    /// reads the boxed value into a typed local, calls the core through <c>ref</c>, and copies the local
    /// back into the boxed slot whenever the core reports a change; every canonical data type therefore
    /// writes through, including the wide <c>S32</c>/<c>U32</c>/<c>S64</c>/<c>U64</c>/<c>Float</c>/<c>Double</c>
    /// cases that need no width adjustment. Narrow types are widened to the canonical 32-bit accumulators
    /// and stored back at their own width, exactly as the canonical switch does.
    /// </para>
    /// <para>
    /// Canonical asserts that the flags carry no legacy <c>float power</c> argument
    ///.; the port mirrors that assert, including the <c>flags == 1</c>
    /// allowance.
    /// </para>
    /// </remarks>
    internal static bool DragBehavior(uint id, ImGuiDataType dataType, ref object? pV, float vSpeed, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags)
    {
        ImAssert.Check(
            flags == (ImGuiSliderFlags)1 || (flags & ImGuiSliderFlags.InvalidMask_) == 0,
            "Invalid ImGuiSliderFlags flags! Has the legacy 'float power' argument been mistakenly cast to flags? Call function with ImGuiSliderFlags_Logarithmic flags instead.");

        ImGuiContext g = GetCurrentContext()!;
        if (g.ActiveId == id)
        {
            if (g.ActiveIdSource == ImGuiInputSource.Mouse && !g.IO.MouseDown[0])
                ClearActiveID();
            else if ((g.ActiveIdSource == ImGuiInputSource.Keyboard || g.ActiveIdSource == ImGuiInputSource.Gamepad) && g.NavActivatePressedId == id && !g.ActiveIdIsJustActivated)
                ClearActiveID();
        }
        if (g.ActiveId != id)
            return false;
        if ((g.LastItemData.ItemFlags & DragSliderItemFlagReadOnly) != 0 || (flags & DragSliderFlagReadOnly) != 0)
            return false;

        switch (dataType)
        {
            case ImGuiDataType.S8:
            {
                int v32 = (sbyte)pV!;
                bool r = DragBehaviorT(ImGuiDataType.S32, ref v32, vSpeed, pMin != null ? (sbyte)pMin : sbyte.MinValue, pMax != null ? (sbyte)pMax : sbyte.MaxValue, format, flags);
                if (r) pV = (sbyte)v32;
                return r;
            }
            case ImGuiDataType.U8:
            {
                uint v32 = (byte)pV!;
                bool r = DragBehaviorT(ImGuiDataType.U32, ref v32, vSpeed, pMin != null ? (byte)pMin : byte.MinValue, pMax != null ? (byte)pMax : byte.MaxValue, format, flags);
                if (r) pV = (byte)v32;
                return r;
            }
            case ImGuiDataType.S16:
            {
                int v32 = (short)pV!;
                bool r = DragBehaviorT(ImGuiDataType.S32, ref v32, vSpeed, pMin != null ? (short)pMin : short.MinValue, pMax != null ? (short)pMax : short.MaxValue, format, flags);
                if (r) pV = (short)v32;
                return r;
            }
            case ImGuiDataType.U16:
            {
                uint v32 = (ushort)pV!;
                bool r = DragBehaviorT(ImGuiDataType.U32, ref v32, vSpeed, pMin != null ? (ushort)pMin : ushort.MinValue, pMax != null ? (ushort)pMax : ushort.MaxValue, format, flags);
                if (r) pV = (ushort)v32;
                return r;
            }
            case ImGuiDataType.S32:
            {
                int v32 = (int)pV!;
                bool r = DragBehaviorT(dataType, ref v32, vSpeed, pMin != null ? (int)pMin : int.MinValue, pMax != null ? (int)pMax : int.MaxValue, format, flags);
                if (r) pV = v32;
                return r;
            }
            case ImGuiDataType.U32:
            {
                uint v32 = (uint)pV!;
                bool r = DragBehaviorT(dataType, ref v32, vSpeed, pMin != null ? (uint)pMin : uint.MinValue, pMax != null ? (uint)pMax : uint.MaxValue, format, flags);
                if (r) pV = v32;
                return r;
            }
            case ImGuiDataType.S64:
            {
                long v64 = (long)pV!;
                bool r = DragBehaviorT(dataType, ref v64, vSpeed, pMin != null ? (long)pMin : long.MinValue, pMax != null ? (long)pMax : long.MaxValue, format, flags);
                if (r) pV = v64;
                return r;
            }
            case ImGuiDataType.U64:
            {
                ulong v64 = (ulong)pV!;
                bool r = DragBehaviorT(dataType, ref v64, vSpeed, pMin != null ? (ulong)pMin : ulong.MinValue, pMax != null ? (ulong)pMax : ulong.MaxValue, format, flags);
                if (r) pV = v64;
                return r;
            }
            case ImGuiDataType.Float:
            {
                float vf = (float)pV!;
                bool r = DragBehaviorT(dataType, ref vf, vSpeed, pMin != null ? (float)pMin : -float.MaxValue, pMax != null ? (float)pMax : float.MaxValue, format, flags);
                if (r) pV = vf;
                return r;
            }
            case ImGuiDataType.Double:
            {
                double vd = (double)pV!;
                bool r = DragBehaviorT(dataType, ref vd, vSpeed, pMin != null ? (double)pMin : -double.MaxValue, pMax != null ? (double)pMax : double.MaxValue, format, flags);
                if (r) pV = vd;
                return r;
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return false;
        }
    }

    /// <summary>
    /// Reports whether Ctrl+Click text entry should clamp its result to the widget bounds.
    /// Canvases canonical <c>TempInputIsClampEnabled</c>
    /// </summary>
    /// <param name="flags">The slider flags.</param>
    /// <param name="dataType">The data type of the bounds.</param>
    /// <param name="pMin">The optional lower bound.</param>
    /// <param name="pMax">The optional upper bound.</param>
    /// <returns><see langword="true"/> when clamping is enabled for this configuration.</returns>
    /// <remarks>
    /// Clamping only happens when <see cref="ImGuiSliderFlags.ClampOnInput"/> is set (generally through
    /// <see cref="ImGuiSliderFlags.AlwaysClamp"/>). A zero-width range only clamps when that range is
    /// explicitly zero and <see cref="ImGuiSliderFlags.ClampZeroRange"/> is set.
    /// </remarks>
    private static bool TempInputIsClampEnabled(ImGuiSliderFlags flags, ImGuiDataType dataType, object? pMin, object? pMax)
    {
        if ((flags & ImGuiSliderFlags.ClampOnInput) != 0 && (pMin != null || pMax != null))
        {
            int clampRangeDir = (pMin != null && pMax != null) ? DragSliderCompareBoxed(dataType, pMin, pMax) : 0;
            if (pMin == null || pMax == null || clampRangeDir < 0)
                return true;
            if (clampRangeDir == 0)
                return DataTypeIsZero(dataType, pMin) ? (flags & ImGuiSliderFlags.ClampZeroRange) != 0 : true;
        }
        return false;
    }

    /// <summary>
    /// Implements the full drag scalar item for one data type on the port's boxed scalar boundary.
    /// Canvases canonical <c>DragScalar</c>
    /// </summary>
    /// <param name="label">The widget label; it supplies both the identifier and the displayed text.</param>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="pData">The boxed value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="pMin">The optional boxed lower bound.</param>
    /// <param name="pMax">The optional boxed upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the data type's default.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// Canonical takes <c>void*</c>; the four public <see cref="DragScalar(string, ImGuiDataType, ref float, float, float?, float?, string?, ImGuiSliderFlags)"/>
    /// overloads and the array forms wrap this core. The public entry points keep the canonical name so
    /// parity measurement sees <c>DragScalar</c>.
    /// </remarks>
    private static bool DragScalarCore(string label, ImGuiDataType dataType, ref object? pData, float vSpeed, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        float w = CalcItemWidth();
        uint colorMarker = (g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasColorMarker) != 0 ? g.NextItemData.ColorMarker : 0u;

        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false);
        ImRect frameBb = new(window.DC.CursorPos, window.DC.CursorPos + new ImVec2(w, labelSize.Y + (style.FramePadding.Y * 2.0f)));
        ImRect totalBb = new(frameBb.Min, frameBb.Max + new ImVec2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0.0f));

        bool tempInputAllowed = (flags & ImGuiSliderFlags.NoInput) == 0;
        ItemSize(totalBb, style.FramePadding.Y);
        if (!ItemAdd(totalBb, id, frameBb, tempInputAllowed ? DragSliderItemFlagInputable : 0))
            return false;

        if (format == null)
            format = DataTypeGetInfo(dataType)!.PrintFmt;

        bool hovered = ItemHoverable(frameBb, id, g.LastItemData.ItemFlags);
        bool tempInputIsActive = tempInputAllowed && TempInputIsActive(id);
        if (!tempInputIsActive)
        {
            // Tabbing or Ctrl+Click on the drag turns it into a text input.
            bool clicked = hovered && IsMouseClicked(ImGuiMouseButton.Left, ImGuiInputFlags.None, id);
            bool doubleClicked = hovered && g.IO.MouseClickedCount[0] == 2 && TestKeyOwner(ImGuiKey.MouseLeft, id);
            bool makeActive = clicked || doubleClicked || g.NavActivateId == id;
            if (makeActive && (clicked || doubleClicked))
                SetKeyOwner(ImGuiKey.MouseLeft, id);
            if (makeActive && tempInputAllowed)
            {
                if ((clicked && g.IO.KeyCtrl) || doubleClicked || (g.NavActivateId == id && (g.NavActivateFlags & ImGuiActivateFlags.PreferInput) != 0))
                    tempInputIsActive = true;
            }

            // A simple click without moving can also turn the drag into a text input.
            if (g.IO.ConfigDragClickToInputText && tempInputAllowed && !tempInputIsActive)
            {
                if (g.ActiveId == id && hovered && g.IO.MouseReleased[0] && !IsMouseDragPastThreshold(ImGuiMouseButton.Left, g.IO.MouseDragThreshold * DragSliderMouseThresholdFactor))
                {
                    g.NavActivateId = id;
                    g.NavActivateFlags = ImGuiActivateFlags.PreferInput;
                    tempInputIsActive = true;
                }
            }

            // The activation-time backup is not read by the library but is preserved for mods.
            if (makeActive)
                DragSliderStoreActivationValue(dataType, pData);

            if (makeActive && !tempInputIsActive)
            {
                SetActiveID(id, window);
                SetFocusID(id, window);
                FocusWindow(window);
                g.ActiveIdUsingNavDirMask = (1u << (int)ImGuiDir.Left) | (1u << (int)ImGuiDir.Right);
            }
        }

        if (tempInputIsActive)
        {
            bool clampEnabled = TempInputIsClampEnabled(flags, dataType, pMin, pMax);
            return TempInputScalar(frameBb, id, label, dataType, ref pData, format, clampEnabled ? pMin : null, clampEnabled ? pMax : null);
        }

        uint frameCol = GetColorU32(g.ActiveId == id ? ImGuiCol.FrameBgActive : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        RenderNavCursor(frameBb, id);
        RenderFrame(frameBb.Min, frameBb.Max, frameCol, false, style.FrameRounding);
        if (colorMarker != 0 && style.ColorMarkerSize > 0.0f)
            RenderColorComponentMarker(frameBb, GetColorU32(colorMarker), style.FrameRounding);
        RenderFrameBorder(frameBb.Min, frameBb.Max, style.FrameRounding);

        bool valueChanged = DragBehavior(id, dataType, ref pData, vSpeed, pMin, pMax, format, flags);
        if (valueChanged)
            MarkItemEdited(id);

        bool isMixed = (g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0;
        string valueBuf = (isMixed && g.MixedValueLabel != null)
            ? g.MixedValueLabel
            : DataTypeFormatString(dataType, pData, format);
        if (g.LogEnabled)
            LogSetNextTextDecoration("{", "}");
        if (isMixed)
            PushStyleColor(ImGuiCol.Text, g.Style.Colors[(int)ImGuiCol.TextMixedValue]);
        RenderTextClipped(frameBb.Min, frameBb.Max, valueBuf, null, null, new ImVec2(0.5f, 0.5f));
        if (isMixed)
            PopStyleColor();

        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(frameBb.Max.X + style.ItemInnerSpacing.X, frameBb.Min.Y + style.FramePadding.Y), label, labelEnd, false);

        return valueChanged;
    }

    /// <summary>
    /// Drags a single-precision floating-point value.
    /// Canvases canonical <c>DragScalar</c> (, implemented at
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Float"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound; <see langword="null"/> leaves the value unbounded below.</param>
    /// <param name="vMax">The optional upper bound; <see langword="null"/> leaves the value unbounded above.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%.3f</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// The <c>dataType</c> argument exists for canonical parity; the port asserts it matches the
    /// <see cref="float"/> storage.
    /// </remarks>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref float v, float vSpeed = 1.0f, float? vMin = null, float? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Float, "This DragScalar overload stores a float.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (float)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags a signed 32-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S32"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref int v, float vSpeed = 1.0f, int? vMin = null, int? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S32, "This DragScalar overload stores an int.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (int)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags an unsigned 32-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U32"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref uint v, float vSpeed = 1.0f, uint? vMin = null, uint? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U32, "This DragScalar overload stores a uint.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (uint)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags a double-precision floating-point value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Double"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%f</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref double v, float vSpeed = 1.0f, double? vMin = null, double? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Double, "This DragScalar overload stores a double.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (double)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags a signed 8-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S8"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound; <see langword="null"/> leaves the value unbounded below.</param>
    /// <param name="vMax">The optional upper bound; <see langword="null"/> leaves the value unbounded above.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref sbyte v, float vSpeed = 1.0f, sbyte? vMin = null, sbyte? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S8, "This DragScalar overload stores an sbyte.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (sbyte)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags an unsigned 8-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U8"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref byte v, float vSpeed = 1.0f, byte? vMin = null, byte? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U8, "This DragScalar overload stores a byte.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (byte)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags a signed 16-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S16"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref short v, float vSpeed = 1.0f, short? vMin = null, short? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S16, "This DragScalar overload stores a short.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (short)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags an unsigned 16-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U16"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref ushort v, float vSpeed = 1.0f, ushort? vMin = null, ushort? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U16, "This DragScalar overload stores a ushort.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (ushort)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags a signed 64-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S64"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the canonical 64-bit decimal format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref long v, float vSpeed = 1.0f, long? vMin = null, long? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S64, "This DragScalar overload stores a long.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (long)boxed!;
        return changed;
    }

    /// <summary>
    /// Drags an unsigned 64-bit integer value.
    /// Canvases canonical <c>DragScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U64"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the canonical 64-bit decimal format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragScalar(string label, ImGuiDataType dataType, ref ulong v, float vSpeed = 1.0f, ulong? vMin = null, ulong? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U64, "This DragScalar overload stores a ulong.");
        object? boxed = v;
        bool changed = DragScalarCore(label, dataType, ref boxed, vSpeed, vMin, vMax, format, flags);
        v = (ulong)boxed!;
        return changed;
    }

    /// <summary>
    /// Lays out several drag fields on one line and routes each to <see cref="DragScalarCore"/>.
    /// Canvases canonical <c>DragScalarN</c>.
    /// </summary>
    /// <typeparam name="T">The concrete element type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type of the elements.</param>
    /// <param name="v">The element array; only the first <paramref name="components"/> entries are used.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="pMin">The optional boxed lower bound shared by all components.</param>
    /// <param name="pMax">The optional boxed upper bound shared by all components.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    /// <remarks>
    /// Canonical advances a <c>void*</c> by the data type size; the port indexes the managed array. The
    /// per-component identifier scope (<c>PushID(label)</c>, <c>PushID(i)</c>, empty widget label)
    /// matches canonical.
    /// </remarks>
    private static bool DragScalarNCore<T>(string label, ImGuiDataType dataType, T[] v, int components, float vSpeed, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        ImAssert.Check(v.Length >= components, "DragScalarN requires at least 'components' elements.");
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        bool valueChanged = false;
        BeginGroup();
        PushID(label);
        PushMultiItemsWidths(components, CalcItemWidth());
        for (int i = 0; i < components; i++)
        {
            PushID(i);
            if (i > 0)
                SameLine(0, g.Style.ItemInnerSpacing.X);
            if ((flags & ImGuiSliderFlags.ColorMarkers) != 0)
                SetNextItemColorMarker(GDefaultRgbaColorMarkers[i]);
            object? boxed = v[i];
            valueChanged |= DragScalarCore(string.Empty, dataType, ref boxed, vSpeed, pMin, pMax, format, flags);
            v[i] = (T)boxed!;
            PopID();
            PopItemWidth();
        }
        PopID();

        string labelEnd = FindRenderedTextEnd(label);
        if (labelEnd.Length > 0)
        {
            SameLine(0, g.Style.ItemInnerSpacing.X);
            TextEx(label, labelEnd);
        }

        EndGroup();
        return valueChanged;
    }

    /// <summary>
    /// Drags a fixed number of single-precision floating-point components on one line.
    /// Canvases canonical <c>DragScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Float"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragScalarN(string label, ImGuiDataType dataType, float[] v, int components, float vSpeed = 1.0f, float? vMin = null, float? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Float, "This DragScalarN overload stores floats.");
        return DragScalarNCore(label, dataType, v, components, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags a fixed number of signed 32-bit integer components on one line.
    /// Canvases canonical <c>DragScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S32"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragScalarN(string label, ImGuiDataType dataType, int[] v, int components, float vSpeed = 1.0f, int? vMin = null, int? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S32, "This DragScalarN overload stores ints.");
        return DragScalarNCore(label, dataType, v, components, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags a fixed number of unsigned 32-bit integer components on one line.
    /// Canvases canonical <c>DragScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U32"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragScalarN(string label, ImGuiDataType dataType, uint[] v, int components, float vSpeed = 1.0f, uint? vMin = null, uint? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U32, "This DragScalarN overload stores uints.");
        return DragScalarNCore(label, dataType, v, components, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags a fixed number of double-precision floating-point components on one line.
    /// Canvases canonical <c>DragScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Double"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The optional lower bound.</param>
    /// <param name="vMax">The optional upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragScalarN(string label, ImGuiDataType dataType, double[] v, int components, float vSpeed = 1.0f, double? vMin = null, double? vMax = null, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Double, "This DragScalarN overload stores doubles.");
        return DragScalarNCore(label, dataType, v, components, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags a single float with the canonical float defaults.
    /// Canvases canonical <c>DragFloat</c> (, implemented at
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound; when <paramref name="vMin"/> is greater than or equal to it the value is unbounded.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragFloat(string label, ref float v, float vSpeed = 1.0f, float vMin = 0.0f, float vMax = 0.0f, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        return DragScalar(label, ImGuiDataType.Float, ref v, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags two floats on one line. Canvases canonical <c>DragFloat2</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least two elements are required.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragFloat2(string label, float[] v, float vSpeed = 1.0f, float vMin = 0.0f, float vMax = 0.0f, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 2, "DragFloat2 requires at least two components.");
        return DragScalarN(label, ImGuiDataType.Float, v, 2, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags three floats on one line. Canvases canonical <c>DragFloat3</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least three elements are required.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragFloat3(string label, float[] v, float vSpeed = 1.0f, float vMin = 0.0f, float vMax = 0.0f, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 3, "DragFloat3 requires at least three components.");
        return DragScalarN(label, ImGuiDataType.Float, v, 3, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags four floats on one line. Canvases canonical <c>DragFloat4</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least four elements are required.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragFloat4(string label, float[] v, float vSpeed = 1.0f, float vMin = 0.0f, float vMax = 0.0f, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 4, "DragFloat4 requires at least four components.");
        return DragScalarN(label, ImGuiDataType.Float, v, 4, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags a minimum and a maximum float that cross-clamp each other.
    /// Canvases canonical <c>DragFloatRange2</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="vCurrentMin">The current minimum, updated in place.</param>
    /// <param name="vCurrentMax">The current maximum, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The outer lower bound.</param>
    /// <param name="vMax">The outer upper bound; when <paramref name="vMin"/> is greater than or equal to it both fields are unbounded.</param>
    /// <param name="format">The display format for the minimum field.</param>
    /// <param name="formatMax">The optional separate display format for the maximum field.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when either field changed.</returns>
    /// <remarks>
    /// Callers generally want <see cref="ImGuiSliderFlags.AlwaysClamp"/> so manual text entry stays
    /// inside the cross-clamped bounds.
    /// </remarks>
    public static bool DragFloatRange2(string label, ref float vCurrentMin, ref float vCurrentMax, float vSpeed = 1.0f, float vMin = 0.0f, float vMax = 0.0f, string? format = "%.3f", string? formatMax = null, ImGuiSliderFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        PushID(label);
        BeginGroup();
        PushMultiItemsWidths(2, CalcItemWidth());

        float minMin = (vMin >= vMax) ? -float.MaxValue : vMin;
        float minMax = (vMin >= vMax) ? vCurrentMax : MathF.Min(vMax, vCurrentMax);
        ImGuiSliderFlags minFlags = flags | ((minMin == minMax) ? DragSliderFlagReadOnly : 0);
        bool valueChanged = DragScalar("##min", ImGuiDataType.Float, ref vCurrentMin, vSpeed, minMin, minMax, format, minFlags);
        PopItemWidth();
        SameLine(0, g.Style.ItemInnerSpacing.X);

        float maxMin = (vMin >= vMax) ? vCurrentMin : MathF.Max(vMin, vCurrentMin);
        float maxMax = (vMin >= vMax) ? float.MaxValue : vMax;
        ImGuiSliderFlags maxFlags = flags | ((maxMin == maxMax) ? DragSliderFlagReadOnly : 0);
        valueChanged |= DragScalar("##max", ImGuiDataType.Float, ref vCurrentMax, vSpeed, maxMin, maxMax, formatMax ?? format, maxFlags);
        PopItemWidth();
        SameLine(0, g.Style.ItemInnerSpacing.X);

        TextEx(label, FindRenderedTextEnd(label));
        EndGroup();
        PopID();

        return valueChanged;
    }

    /// <summary>
    /// Drags a single signed integer with the canonical integer defaults.
    /// Canvases canonical <c>DragInt</c> (, implemented at
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel; it stays a float for finer speed control.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound; when <paramref name="vMin"/> is greater than or equal to it the value is unbounded.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool DragInt(string label, ref int v, float vSpeed = 1.0f, int vMin = 0, int vMax = 0, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        return DragScalar(label, ImGuiDataType.S32, ref v, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags two integers on one line. Canvases canonical <c>DragInt2</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least two elements are required.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragInt2(string label, int[] v, float vSpeed = 1.0f, int vMin = 0, int vMax = 0, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 2, "DragInt2 requires at least two components.");
        return DragScalarN(label, ImGuiDataType.S32, v, 2, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags three integers on one line. Canvases canonical <c>DragInt3</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least three elements are required.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragInt3(string label, int[] v, float vSpeed = 1.0f, int vMin = 0, int vMax = 0, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 3, "DragInt3 requires at least three components.");
        return DragScalarN(label, ImGuiDataType.S32, v, 3, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags four integers on one line. Canvases canonical <c>DragInt4</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least four elements are required.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool DragInt4(string label, int[] v, float vSpeed = 1.0f, int vMin = 0, int vMax = 0, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 4, "DragInt4 requires at least four components.");
        return DragScalarN(label, ImGuiDataType.S32, v, 4, vSpeed, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Drags a minimum and a maximum integer that cross-clamp each other.
    /// Canvases canonical <c>DragIntRange2</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="vCurrentMin">The current minimum, updated in place.</param>
    /// <param name="vCurrentMax">The current maximum, updated in place.</param>
    /// <param name="vSpeed">Base units of change per pixel of mouse travel.</param>
    /// <param name="vMin">The outer lower bound.</param>
    /// <param name="vMax">The outer upper bound; when <paramref name="vMin"/> is greater than or equal to it both fields are unbounded.</param>
    /// <param name="format">The display format for the minimum field.</param>
    /// <param name="formatMax">The optional separate display format for the maximum field.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when either field changed.</returns>
    /// <remarks>
    /// Callers generally want <see cref="ImGuiSliderFlags.AlwaysClamp"/> so manual text entry stays
    /// inside the cross-clamped bounds.
    /// </remarks>
    public static bool DragIntRange2(string label, ref int vCurrentMin, ref int vCurrentMax, float vSpeed = 1.0f, int vMin = 0, int vMax = 0, string? format = "%d", string? formatMax = null, ImGuiSliderFlags flags = 0)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        PushID(label);
        BeginGroup();
        PushMultiItemsWidths(2, CalcItemWidth());

        int minMin = (vMin >= vMax) ? int.MinValue : vMin;
        int minMax = (vMin >= vMax) ? vCurrentMax : Math.Min(vMax, vCurrentMax);
        ImGuiSliderFlags minFlags = flags | ((minMin == minMax) ? DragSliderFlagReadOnly : 0);
        bool valueChanged = DragInt("##min", ref vCurrentMin, vSpeed, minMin, minMax, format, minFlags);
        PopItemWidth();
        SameLine(0, g.Style.ItemInnerSpacing.X);

        int maxMin = (vMin >= vMax) ? vCurrentMin : Math.Max(vMin, vCurrentMin);
        int maxMax = (vMin >= vMax) ? int.MaxValue : vMax;
        ImGuiSliderFlags maxFlags = flags | ((maxMin == maxMax) ? DragSliderFlagReadOnly : 0);
        valueChanged |= DragInt("##max", ref vCurrentMax, vSpeed, maxMin, maxMax, formatMax ?? format, maxFlags);
        PopItemWidth();
        SameLine(0, g.Style.ItemInnerSpacing.X);

        TextEx(label, FindRenderedTextEnd(label));
        EndGroup();
        PopID();

        return valueChanged;
    }

    #endregion

    #region [SECTION] Widgets: SliderScalar, SliderFloat, SliderInt, etc.

    /// <summary>
    /// Converts a value in the slider's output space into a parametric slider position.
    /// Canvases canonical <c>ScaleRatioFromValueT</c>
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="v">The value to convert.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="logarithmicZeroEpsilon">The zero-avoidance epsilon; zero selects the linear conversion.</param>
    /// <param name="zeroDeadzoneHalfsize">Half the parametric width of the zero dead zone.</param>
    /// <returns>The parametric position in <c>[0,1]</c>.</returns>
    /// <remarks>
    /// The logarithmic branch fudges endpoints away from <c>log(0)</c>, splits ranges that cross zero
    /// into two logarithmic halves around a dead zone, and flips the result when the range is backwards.
    /// The floating-point math is evaluated in <see cref="double"/> and cast back, as documented on
    /// <see cref="DragSliderToDouble{T}"/>.
    /// </remarks>
    private static float ScaleRatioFromValueT<T>(ImGuiDataType dataType, T v, T vMin, T vMax, float logarithmicZeroEpsilon, float zeroDeadzoneHalfsize)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        if (vMin.Equals(vMax))
            return 0.0f;

        T vClamped = (vMin.CompareTo(vMax) < 0) ? DragSliderClamp(v, vMin, vMax) : DragSliderClamp(v, vMax, vMin);
        if (logarithmicZeroEpsilon > 0.0f)
        {
            bool flipped = vMax.CompareTo(vMin) < 0;
            if (flipped)
            {
                T swap = vMin;
                vMin = vMax;
                vMax = swap;
            }

            double vMinDouble = DragSliderToDouble(dataType, vMin);
            double vMaxDouble = DragSliderToDouble(dataType, vMax);
            double vMinFudged = Math.Abs(vMinDouble) < logarithmicZeroEpsilon
                ? (vMinDouble < 0.0 ? -(double)logarithmicZeroEpsilon : logarithmicZeroEpsilon)
                : vMinDouble;
            double vMaxFudged = Math.Abs(vMaxDouble) < logarithmicZeroEpsilon
                ? (vMaxDouble < 0.0 ? -(double)logarithmicZeroEpsilon : logarithmicZeroEpsilon)
                : vMaxDouble;

            // Ranges of the form (-100. 0) must convert to (-100. -epsilon), not (-100. epsilon).
            if (vMinDouble == 0.0 && vMaxDouble < 0.0)
                vMinFudged = -(double)logarithmicZeroEpsilon;
            else if (vMaxDouble == 0.0 && vMinDouble < 0.0)
                vMaxFudged = -(double)logarithmicZeroEpsilon;

            double vClampedDouble = DragSliderToDouble(dataType, vClamped);
            double result;
            if (vClampedDouble <= vMinFudged)
                result = 0.0;
            else if (vClampedDouble >= vMaxFudged)
                result = 1.0;
            else if (vMinDouble * vMaxDouble < 0.0)
            {
                double zeroPointCenter = (-vMinDouble) / (vMaxDouble - vMinDouble);
                double zeroPointSnapL = zeroPointCenter - zeroDeadzoneHalfsize;
                double zeroPointSnapR = zeroPointCenter + zeroDeadzoneHalfsize;
                double vDouble = DragSliderToDouble(dataType, v);
                if (vDouble == 0.0)
                    result = zeroPointCenter;
                else if (vDouble < 0.0)
                    result = (1.0 - (Math.Log(-vClampedDouble / logarithmicZeroEpsilon) / Math.Log(-vMinFudged / logarithmicZeroEpsilon))) * zeroPointSnapL;
                else
                    result = zeroPointSnapR + ((Math.Log(vClampedDouble / logarithmicZeroEpsilon) / Math.Log(vMaxFudged / logarithmicZeroEpsilon)) * (1.0 - zeroPointSnapR));
            }
            else if (vMinDouble < 0.0 || vMaxDouble < 0.0)
            {
                result = 1.0 - (Math.Log(-vClampedDouble / -vMaxFudged) / Math.Log(-vMinFudged / -vMaxFudged));
            }
            else
            {
                result = Math.Log(vClampedDouble / vMinFudged) / Math.Log(vMaxFudged / vMinFudged);
            }

            return flipped ? (float)(1.0 - result) : (float)result;
        }

        return DragSliderScaleLinearRatio(dataType, vClamped, vMin, vMax);
    }

    /// <summary>
    /// Converts a parametric slider position into a value in the slider's output space.
    /// Canvases canonical <c>ScaleValueFromRatioT</c>
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of the bounds.</param>
    /// <param name="t">The parametric position in <c>[0,1]</c>.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="logarithmicZeroEpsilon">The zero-avoidance epsilon; zero selects the linear conversion.</param>
    /// <param name="zeroDeadzoneHalfsize">Half the parametric width of the zero dead zone.</param>
    /// <returns>The value at the requested position.</returns>
    /// <remarks>
    /// The extents are special-cased so a fully-left or fully-right slider reaches the exact bounds
    /// instead of a logarithmically fudged approximation. Integer values round the fractional position so
    /// the clicking position matches the grab box.
    /// </remarks>
    private static T ScaleValueFromRatioT<T>(ImGuiDataType dataType, float t, T vMin, T vMax, float logarithmicZeroEpsilon, float zeroDeadzoneHalfsize)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        if (t <= 0.0f || vMin.Equals(vMax))
            return vMin;
        if (t >= 1.0f)
            return vMax;

        T result = default;
        if (logarithmicZeroEpsilon > 0.0f)
        {
            double vMinDouble = DragSliderToDouble(dataType, vMin);
            double vMaxDouble = DragSliderToDouble(dataType, vMax);
            double vMinFudged = Math.Abs(vMinDouble) < logarithmicZeroEpsilon
                ? (vMinDouble < 0.0 ? -(double)logarithmicZeroEpsilon : logarithmicZeroEpsilon)
                : vMinDouble;
            double vMaxFudged = Math.Abs(vMaxDouble) < logarithmicZeroEpsilon
                ? (vMaxDouble < 0.0 ? -(double)logarithmicZeroEpsilon : logarithmicZeroEpsilon)
                : vMaxDouble;

            bool flipped = vMaxDouble < vMinDouble;
            if (flipped)
            {
                double swap = vMinFudged;
                vMinFudged = vMaxFudged;
                vMaxFudged = swap;
            }

            if (vMaxDouble == 0.0 && vMinDouble < 0.0)
                vMaxFudged = -(double)logarithmicZeroEpsilon;

            double tWithFlip = flipped ? 1.0 - t : t;
            if (vMinDouble * vMaxDouble < 0.0)
            {
                double zeroPointCenter = (-Math.Min(vMinDouble, vMaxDouble)) / Math.Abs(vMaxDouble - vMinDouble);
                double zeroPointSnapL = zeroPointCenter - zeroDeadzoneHalfsize;
                double zeroPointSnapR = zeroPointCenter + zeroDeadzoneHalfsize;
                if (tWithFlip >= zeroPointSnapL && tWithFlip <= zeroPointSnapR)
                    result = default;
                else if (tWithFlip < zeroPointCenter)
                    result = DragSliderFromDouble<T>(dataType, -(logarithmicZeroEpsilon * Math.Pow(-vMinFudged / logarithmicZeroEpsilon, 1.0 - (tWithFlip / zeroPointSnapL))));
                else
                    result = DragSliderFromDouble<T>(dataType, logarithmicZeroEpsilon * Math.Pow(vMaxFudged / logarithmicZeroEpsilon, (tWithFlip - zeroPointSnapR) / (1.0 - zeroPointSnapR)));
            }
            else if (vMinDouble < 0.0 || vMaxDouble < 0.0)
            {
                result = DragSliderFromDouble<T>(dataType, -(-vMaxFudged * Math.Pow(-vMinFudged / -vMaxFudged, 1.0 - tWithFlip)));
            }
            else
            {
                result = DragSliderFromDouble<T>(dataType, vMinFudged * Math.Pow(vMaxFudged / vMinFudged, tWithFlip));
            }
        }
        else if (DragSliderIsFloatingPoint(dataType))
        {
            // Canonical evaluates ImLerp in FLOATTYPE: float for Float and double for Double.
            if (dataType == ImGuiDataType.Float)
            {
                float vMinSingle = (float)DragSliderToDouble(dataType, vMin);
                float vMaxSingle = (float)DragSliderToDouble(dataType, vMax);
                result = DragSliderFromDouble<T>(dataType, DragSliderLerp(vMinSingle, vMaxSingle, t));
            }
            else
            {
                double vMinDouble = DragSliderToDouble(dataType, vMin);
                double vMaxDouble = DragSliderToDouble(dataType, vMax);
                result = DragSliderFromDouble<T>(dataType, vMinDouble + ((vMaxDouble - vMinDouble) * t));
            }
        }
        else if (t < 1.0f)
        {
            result = DragSliderScaleLinearValue(dataType, t, vMin, vMax);
        }

        return result;
    }

    /// <summary>
    /// Applies one frame of slider interaction to an active scalar widget and reports the grab rectangle.
    /// Canvases canonical <c>SliderBehaviorT</c>
    /// </summary>
    /// <typeparam name="T">The concrete scalar type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="bb">The slider frame rectangle.</param>
    /// <param name="id">The widget identifier.</param>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="v">The live value in user memory, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format used to derive precision and to round the value.</param>
    /// <param name="flags">The slider flags.</param>
    /// <param name="outGrabBb">Receives the grab rectangle in screen space.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// The grab geometry reserves <c>grab_padding</c> pixels at each end and sizes an integer grab to
    /// represent one unit when the range allows. Mouse input positions the grab through the parametric
    /// space; nav input accumulates fractional range steps in <c>g.SliderCurrentAccum</c> and subtracts the
    /// movement it actually realized so sub-step tweaks are preserved.
    /// </remarks>
    internal static bool SliderBehaviorT<T>(ImRect bb, uint id, ImGuiDataType dataType, ref T v, T vMin, T vMax, string? format, ImGuiSliderFlags flags, out ImRect outGrabBb)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;

        ImGuiAxis axis = (flags & DragSliderFlagVertical) != 0 ? ImGuiAxis.Y : ImGuiAxis.X;
        bool isLogarithmic = (flags & ImGuiSliderFlags.Logarithmic) != 0;
        bool isFloatingPoint = DragSliderIsFloatingPoint(dataType);
        float vRangeF = (float)(vMin.CompareTo(vMax) < 0 ? DragSliderDifference(dataType, vMax, vMin) : DragSliderDifference(dataType, vMin, vMax));

        // Grab and track geometry.
        const float grabPadding = 2.0f; // FIXME: should be part of the style, as in canonical.
        float sliderSz = (AxisComponent(bb.Max, axis) - AxisComponent(bb.Min, axis)) - (grabPadding * 2.0f);
        float grabSz = style.GrabMinSize;
        if (!isFloatingPoint && vRangeF >= 0.0f) // v_range_f < 0 can happen on integer overflows
            grabSz = MathF.Max(sliderSz / (vRangeF + 1.0f), style.GrabMinSize);
        grabSz = MathF.Min(grabSz, sliderSz);
        float sliderUsableSz = sliderSz - grabSz;
        float sliderUsablePosMin = AxisComponent(bb.Min, axis) + grabPadding + (grabSz * 0.5f);
        float sliderUsablePosMax = AxisComponent(bb.Max, axis) - grabPadding - (grabSz * 0.5f);

        float logarithmicZeroEpsilon = 0.0f;
        float zeroDeadzoneHalfsize = 0.0f;
        if (isLogarithmic)
        {
            int decimalPrecision = isFloatingPoint ? ImParseFormatPrecision(format ?? string.Empty, 3) : 1;
            logarithmicZeroEpsilon = MathF.Pow(0.1f, decimalPrecision);
            zeroDeadzoneHalfsize = (style.LogSliderDeadzone * 0.5f) / MathF.Max(sliderUsableSz, 1.0f);
        }

        bool valueChanged = false;
        if (g.ActiveId == id)
        {
            bool setNewValue = false;
            float clickedT = 0.0f;
            if (g.ActiveIdSource == ImGuiInputSource.Mouse)
            {
                if (!g.IO.MouseDown[0])
                {
                    ClearActiveID();
                }
                else
                {
                    float mouseAbsPos = AxisComponent(g.IO.MousePos, axis);
                    if (g.ActiveIdIsJustActivated)
                    {
                        float grabT = ScaleRatioFromValueT(dataType, v, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);
                        if (axis == ImGuiAxis.Y)
                            grabT = 1.0f - grabT;
                        float grabPos = DragSliderLerp(sliderUsablePosMin, sliderUsablePosMax, grabT);
                        bool clickedAroundGrab = (mouseAbsPos >= grabPos - (grabSz * 0.5f) - 1.0f) && (mouseAbsPos <= grabPos + (grabSz * 0.5f) + 1.0f);
                        g.SliderGrabClickOffset = (clickedAroundGrab && isFloatingPoint) ? mouseAbsPos - grabPos : 0.0f;
                    }
                    if (sliderUsableSz > 0.0f)
                        clickedT = DragSliderSaturate((mouseAbsPos - g.SliderGrabClickOffset - sliderUsablePosMin) / sliderUsableSz);
                    if (axis == ImGuiAxis.Y)
                        clickedT = 1.0f - clickedT;
                    setNewValue = true;
                }
            }
            else if (g.ActiveIdSource == ImGuiInputSource.Keyboard || g.ActiveIdSource == ImGuiInputSource.Gamepad)
            {
                if (g.ActiveIdIsJustActivated)
                {
                    g.SliderCurrentAccum = 0.0f;
                    g.SliderCurrentAccumDirty = false;
                }

                float inputDelta = axis == ImGuiAxis.X ? GetNavTweakPressedAmount(axis) : -GetNavTweakPressedAmount(axis);
                if (inputDelta != 0.0f)
                {
                    bool tweakSlow = IsKeyDown(g.NavInputSource == ImGuiInputSource.Gamepad ? ImGuiKey.GamepadL1 : ImGuiKey.ImGuiMod_Ctrl);
                    bool tweakFast = IsKeyDown(g.NavInputSource == ImGuiInputSource.Gamepad ? ImGuiKey.GamepadR1 : ImGuiKey.ImGuiMod_Shift);
                    int decimalPrecision = isFloatingPoint ? ImParseFormatPrecision(format ?? string.Empty, 3) : 0;
                    if (decimalPrecision > 0)
                    {
                        inputDelta /= 100.0f; // Tweak speed in percent of slider bounds.
                        if (tweakSlow)
                            inputDelta /= 10.0f;
                    }
                    else
                    {
                        if ((vRangeF >= -100.0f && vRangeF <= 100.0f && vRangeF != 0.0f) || tweakSlow)
                            inputDelta = ((inputDelta < 0.0f) ? -1.0f : +1.0f) / vRangeF; // One integer step.
                        else
                            inputDelta /= 100.0f;
                    }
                    if (tweakFast)
                        inputDelta *= 10.0f;

                    g.SliderCurrentAccum += inputDelta;
                    g.SliderCurrentAccumDirty = true;
                }

                float delta = g.SliderCurrentAccum;
                if (g.NavActivatePressedId == id && !g.ActiveIdIsJustActivated)
                {
                    ClearActiveID();
                }
                else if (g.SliderCurrentAccumDirty)
                {
                    clickedT = ScaleRatioFromValueT(dataType, v, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);

                    if ((clickedT >= 1.0f && delta > 0.0f) || (clickedT <= 0.0f && delta < 0.0f))
                    {
                        // Avoid applying saturation when the slider is already past its limits.
                        setNewValue = false;
                        g.SliderCurrentAccum = 0.0f;
                    }
                    else
                    {
                        setNewValue = true;
                        float oldClickedT = clickedT;
                        clickedT = DragSliderSaturate(clickedT + delta);

                        // Subtract the movement actually realized so the remainder stays in the accumulator.
                        T vNew = ScaleValueFromRatioT(dataType, clickedT, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);
                        if (isFloatingPoint && (flags & ImGuiSliderFlags.NoRoundToFormat) == 0)
                            vNew = RoundScalarWithFormatT(format ?? string.Empty, dataType, vNew);
                        float newClickedT = ScaleRatioFromValueT(dataType, vNew, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);

                        if (delta > 0)
                            g.SliderCurrentAccum -= MathF.Min(newClickedT - oldClickedT, delta);
                        else
                            g.SliderCurrentAccum -= MathF.Max(newClickedT - oldClickedT, delta);
                    }

                    g.SliderCurrentAccumDirty = false;
                }
            }

            if (setNewValue && ((g.LastItemData.ItemFlags & DragSliderItemFlagReadOnly) != 0 || (flags & DragSliderFlagReadOnly) != 0))
                setNewValue = false;

            if (setNewValue)
            {
                T vNew = ScaleValueFromRatioT(dataType, clickedT, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);

                if (isFloatingPoint && (flags & ImGuiSliderFlags.NoRoundToFormat) == 0)
                    vNew = RoundScalarWithFormatT(format ?? string.Empty, dataType, vNew);

                if (!v.Equals(vNew))
                {
                    v = vNew;
                    valueChanged = true;
                }
            }
        }

        if (sliderSz < 1.0f)
        {
            outGrabBb = new ImRect(bb.Min, bb.Min);
        }
        else
        {
            float grabT = ScaleRatioFromValueT(dataType, v, vMin, vMax, logarithmicZeroEpsilon, zeroDeadzoneHalfsize);
            if (axis == ImGuiAxis.Y)
                grabT = 1.0f - grabT;
            float grabPos = DragSliderLerp(sliderUsablePosMin, sliderUsablePosMax, grabT);
            if (axis == ImGuiAxis.X)
                outGrabBb = new ImRect(grabPos - (grabSz * 0.5f), bb.Min.Y + grabPadding, grabPos + (grabSz * 0.5f), bb.Max.Y - grabPadding);
            else
                outGrabBb = new ImRect(bb.Min.X + grabPadding, grabPos - (grabSz * 0.5f), bb.Max.X - grabPadding, grabPos + (grabSz * 0.5f));
        }

        return valueChanged;
    }

    /// <summary>
    /// Dispatches slider interaction to the generic core for the concrete data type.
    /// Canvases canonical <c>SliderBehavior</c>
    /// </summary>
    /// <param name="bb">The slider frame rectangle.</param>
    /// <param name="id">The widget identifier.</param>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="pV">The boxed live value, updated in place.</param>
    /// <param name="pMin">The required boxed lower bound.</param>
    /// <param name="pMax">The required boxed upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <param name="outGrabBb">Receives the grab rectangle in screen space.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// <para>
    /// Canonical takes <c>void*</c> and passes the caller's pointer straight into the template, so each
    /// case writes directly into the caller's memory. The port reads
    /// the boxed value into a typed local, calls the core through <c>ref</c>, and copies the local back
    /// into the boxed slot whenever the core reports a change, so every canonical data type writes
    /// through, including the wide <c>S32</c>/<c>U32</c>/<c>S64</c>/<c>U64</c>/<c>Float</c>/<c>Double</c>
    /// cases. Narrow types are widened to 32-bit accumulators and stored back at their own width.
    /// </para>
    /// <para>
    /// The canonical half-range asserts protect the parametric math from overflow
    ///.; the port mirrors them, and also asserts that
    /// <see cref="ImGuiSliderFlags.WrapAround"/> is unset because only the drag widgets support wrapping.
    /// </para>
    /// </remarks>
    internal static bool SliderBehavior(ImRect bb, uint id, ImGuiDataType dataType, ref object? pV, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags, out ImRect outGrabBb)
    {
        ImAssert.Check(
            flags == (ImGuiSliderFlags)1 || (flags & ImGuiSliderFlags.InvalidMask_) == 0,
            "Invalid ImGuiSliderFlags flags! Has the legacy 'float power' argument been mistakenly cast to flags? Call function with ImGuiSliderFlags_Logarithmic flags instead.");
        ImAssert.Check((flags & ImGuiSliderFlags.WrapAround) == 0, "WrapAround is not supported by sliders.");

        switch (dataType)
        {
            case ImGuiDataType.S8:
            {
                int v32 = (sbyte)pV!;
                bool r = SliderBehaviorT(bb, id, ImGuiDataType.S32, ref v32, (sbyte)pMin!, (sbyte)pMax!, format, flags, out outGrabBb);
                if (r) pV = (sbyte)v32;
                return r;
            }
            case ImGuiDataType.U8:
            {
                uint v32 = (byte)pV!;
                bool r = SliderBehaviorT(bb, id, ImGuiDataType.U32, ref v32, (byte)pMin!, (byte)pMax!, format, flags, out outGrabBb);
                if (r) pV = (byte)v32;
                return r;
            }
            case ImGuiDataType.S16:
            {
                int v32 = (short)pV!;
                bool r = SliderBehaviorT(bb, id, ImGuiDataType.S32, ref v32, (short)pMin!, (short)pMax!, format, flags, out outGrabBb);
                if (r) pV = (short)v32;
                return r;
            }
            case ImGuiDataType.U16:
            {
                uint v32 = (ushort)pV!;
                bool r = SliderBehaviorT(bb, id, ImGuiDataType.U32, ref v32, (ushort)pMin!, (ushort)pMax!, format, flags, out outGrabBb);
                if (r) pV = (ushort)v32;
                return r;
            }
            case ImGuiDataType.S32:
            {
                int lo = (int)pMin!;
                int hi = (int)pMax!;
                ImAssert.Check(lo >= int.MinValue / 2 && hi <= int.MaxValue / 2, "Slider bounds must be within half the 32-bit signed range.");
                int v32 = (int)pV!;
                bool r32 = SliderBehaviorT(bb, id, dataType, ref v32, lo, hi, format, flags, out outGrabBb);
                if (r32) pV = v32;
                return r32;
            }
            case ImGuiDataType.U32:
            {
                uint lo = (uint)pMin!;
                uint hi = (uint)pMax!;
                ImAssert.Check(hi <= uint.MaxValue / 2, "Slider bounds must be within half the 32-bit unsigned range.");
                uint v32 = (uint)pV!;
                bool r32 = SliderBehaviorT(bb, id, dataType, ref v32, lo, hi, format, flags, out outGrabBb);
                if (r32) pV = v32;
                return r32;
            }
            case ImGuiDataType.S64:
            {
                long lo = (long)pMin!;
                long hi = (long)pMax!;
                ImAssert.Check(lo >= long.MinValue / 2 && hi <= long.MaxValue / 2, "Slider bounds must be within half the 64-bit signed range.");
                long v64 = (long)pV!;
                bool r64 = SliderBehaviorT(bb, id, dataType, ref v64, lo, hi, format, flags, out outGrabBb);
                if (r64) pV = v64;
                return r64;
            }
            case ImGuiDataType.U64:
            {
                ulong lo = (ulong)pMin!;
                ulong hi = (ulong)pMax!;
                ImAssert.Check(hi <= ulong.MaxValue / 2, "Slider bounds must be within half the 64-bit unsigned range.");
                ulong v64 = (ulong)pV!;
                bool r64 = SliderBehaviorT(bb, id, dataType, ref v64, lo, hi, format, flags, out outGrabBb);
                if (r64) pV = v64;
                return r64;
            }
            case ImGuiDataType.Float:
            {
                float lo = (float)pMin!;
                float hi = (float)pMax!;
                ImAssert.Check(lo >= -float.MaxValue / 2.0f && hi <= float.MaxValue / 2.0f, "Slider bounds must be within half the float range.");
                float vf = (float)pV!;
                bool rf = SliderBehaviorT(bb, id, dataType, ref vf, lo, hi, format, flags, out outGrabBb);
                if (rf) pV = vf;
                return rf;
            }
            case ImGuiDataType.Double:
            {
                double lo = (double)pMin!;
                double hi = (double)pMax!;
                ImAssert.Check(lo >= -double.MaxValue / 2.0 && hi <= double.MaxValue / 2.0, "Slider bounds must be within half the double range.");
                double vd = (double)pV!;
                bool rd = SliderBehaviorT(bb, id, dataType, ref vd, lo, hi, format, flags, out outGrabBb);
                if (rd) pV = vd;
                return rd;
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                outGrabBb = new ImRect(bb.Min, bb.Min);
                return false;
        }
    }

    /// <summary>
    /// Implements the full horizontal slider item for one data type on the port's boxed scalar boundary.
    /// Canvases canonical <c>SliderScalar</c>
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="pData">The boxed value, updated in place.</param>
    /// <param name="pMin">The required boxed lower bound.</param>
    /// <param name="pMax">The required boxed upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the data type's default.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// Unlike <see cref="DragScalarCore"/>, the slider has no double-click-to-input path and no
    /// <c>ConfigDragClickToInputText</c> handling, and it ORs rather than assigns
    /// <c>g.ActiveIdUsingNavDirMask</c>. Its temp-input clamping uses
    /// <see cref="ImGuiSliderFlags.ClampOnInput"/> directly instead of
    /// <see cref="TempInputIsClampEnabled"/>.
    /// </remarks>
    private static bool SliderScalarCore(string label, ImGuiDataType dataType, ref object? pData, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);
        float w = CalcItemWidth();
        uint colorMarker = (g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasColorMarker) != 0 ? g.NextItemData.ColorMarker : 0u;

        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false);
        ImRect frameBb = new(window.DC.CursorPos, window.DC.CursorPos + new ImVec2(w, labelSize.Y + (style.FramePadding.Y * 2.0f)));
        ImRect totalBb = new(frameBb.Min, frameBb.Max + new ImVec2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0.0f));

        bool tempInputAllowed = (flags & ImGuiSliderFlags.NoInput) == 0;
        ItemSize(totalBb, style.FramePadding.Y);
        if (!ItemAdd(totalBb, id, frameBb, tempInputAllowed ? DragSliderItemFlagInputable : 0))
            return false;

        if (format == null)
            format = DataTypeGetInfo(dataType)!.PrintFmt;

        bool hovered = ItemHoverable(frameBb, id, g.LastItemData.ItemFlags);
        bool tempInputIsActive = tempInputAllowed && TempInputIsActive(id);
        if (!tempInputIsActive)
        {
            // Tabbing or Ctrl+Click on the slider turns it into an input box.
            bool clicked = hovered && IsMouseClicked(ImGuiMouseButton.Left, ImGuiInputFlags.None, id);
            bool makeActive = clicked || g.NavActivateId == id;
            if (makeActive && clicked)
                SetKeyOwner(ImGuiKey.MouseLeft, id);
            if (makeActive && tempInputAllowed)
            {
                if ((clicked && g.IO.KeyCtrl) || (g.NavActivateId == id && (g.NavActivateFlags & ImGuiActivateFlags.PreferInput) != 0))
                    tempInputIsActive = true;
            }

            if (makeActive)
                DragSliderStoreActivationValue(dataType, pData);

            if (makeActive && !tempInputIsActive)
            {
                SetActiveID(id, window);
                SetFocusID(id, window);
                FocusWindow(window);
                g.ActiveIdUsingNavDirMask |= (1u << (int)ImGuiDir.Left) | (1u << (int)ImGuiDir.Right);
            }
        }

        if (tempInputIsActive)
        {
            bool clampEnabled = (flags & ImGuiSliderFlags.ClampOnInput) != 0;
            return TempInputScalar(frameBb, id, label, dataType, ref pData, format, clampEnabled ? pMin : null, clampEnabled ? pMax : null);
        }

        uint frameCol = GetColorU32(g.ActiveId == id ? ImGuiCol.FrameBgActive : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        RenderNavCursor(frameBb, id);
        RenderFrame(frameBb.Min, frameBb.Max, frameCol, false, style.FrameRounding);
        if (colorMarker != 0 && style.ColorMarkerSize > 0.0f)
            RenderColorComponentMarker(frameBb, GetColorU32(colorMarker), style.FrameRounding);
        RenderFrameBorder(frameBb.Min, frameBb.Max, style.FrameRounding);

        bool valueChanged = SliderBehavior(frameBb, id, dataType, ref pData, pMin, pMax, format, flags, out ImRect grabBb);
        if (valueChanged)
            MarkItemEdited(id);

        if (grabBb.Max.X > grabBb.Min.X)
            window.DrawList.AddRectFilled(grabBb.Min, grabBb.Max, GetColorU32(g.ActiveId == id ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab), style.GrabRounding);

        bool isMixed = (g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0;
        string valueBuf = (isMixed && g.MixedValueLabel != null)
            ? g.MixedValueLabel
            : DataTypeFormatString(dataType, pData, format);
        if (g.LogEnabled)
            LogSetNextTextDecoration("{", "}");
        if (isMixed)
            PushStyleColor(ImGuiCol.Text, g.Style.Colors[(int)ImGuiCol.TextMixedValue]);
        RenderTextClipped(frameBb.Min, frameBb.Max, valueBuf, null, null, new ImVec2(0.5f, 0.5f));
        if (isMixed)
            PopStyleColor();

        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(frameBb.Max.X + style.ItemInnerSpacing.X, frameBb.Min.Y + style.FramePadding.Y), label, labelEnd, false);

        return valueChanged;
    }

    /// <summary>
    /// Slides a single-precision floating-point value.
    /// Canvases canonical <c>SliderScalar</c> (, implemented at
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Float"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref float v, float vMin, float vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Float, "This SliderScalar overload stores a float.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (float)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a signed 32-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S32"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref int v, int vMin, int vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S32, "This SliderScalar overload stores an int.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (int)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides an unsigned 32-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U32"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref uint v, uint vMin, uint vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U32, "This SliderScalar overload stores a uint.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (uint)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a double-precision floating-point value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Double"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref double v, double vMin, double vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Double, "This SliderScalar overload stores a double.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (double)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a signed 8-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S8"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref sbyte v, sbyte vMin, sbyte vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S8, "This SliderScalar overload stores an sbyte.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (sbyte)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides an unsigned 8-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U8"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref byte v, byte vMin, byte vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U8, "This SliderScalar overload stores a byte.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (byte)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a signed 16-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S16"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref short v, short vMin, short vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S16, "This SliderScalar overload stores a short.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (short)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides an unsigned 16-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U16"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref ushort v, ushort vMin, ushort vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U16, "This SliderScalar overload stores a ushort.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (ushort)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a signed 64-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S64"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the canonical 64-bit decimal format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref long v, long vMin, long vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S64, "This SliderScalar overload stores a long.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (long)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides an unsigned 64-bit integer value.
    /// Canvases canonical <c>SliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U64"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the canonical 64-bit decimal format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderScalar(string label, ImGuiDataType dataType, ref ulong v, ulong vMin, ulong vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U64, "This SliderScalar overload stores a ulong.");
        object? boxed = v;
        bool changed = SliderScalarCore(label, dataType, ref boxed, vMin, vMax, format, flags);
        v = (ulong)boxed!;
        return changed;
    }

    /// <summary>
    /// Lays out several sliders on one line and routes each to <see cref="SliderScalarCore"/>.
    /// Canvases canonical <c>SliderScalarN</c>.
    /// </summary>
    /// <typeparam name="T">The concrete element type; it must match <paramref name="dataType"/>.</typeparam>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type of the elements.</param>
    /// <param name="v">The element array; only the first <paramref name="components"/> entries are used.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="pMin">The required boxed lower bound shared by all components.</param>
    /// <param name="pMax">The required boxed upper bound shared by all components.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    private static bool SliderScalarNCore<T>(string label, ImGuiDataType dataType, T[] v, int components, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        ImAssert.Check(v.Length >= components, "SliderScalarN requires at least 'components' elements.");
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        bool valueChanged = false;
        BeginGroup();
        PushID(label);
        PushMultiItemsWidths(components, CalcItemWidth());
        for (int i = 0; i < components; i++)
        {
            PushID(i);
            if (i > 0)
                SameLine(0, g.Style.ItemInnerSpacing.X);
            if ((flags & ImGuiSliderFlags.ColorMarkers) != 0)
                SetNextItemColorMarker(GDefaultRgbaColorMarkers[i]);
            object? boxed = v[i];
            valueChanged |= SliderScalarCore(string.Empty, dataType, ref boxed, pMin, pMax, format, flags);
            v[i] = (T)boxed!;
            PopID();
            PopItemWidth();
        }
        PopID();

        string labelEnd = FindRenderedTextEnd(label);
        if (labelEnd.Length > 0)
        {
            SameLine(0, g.Style.ItemInnerSpacing.X);
            TextEx(label, labelEnd);
        }

        EndGroup();
        return valueChanged;
    }

    /// <summary>
    /// Slides a fixed number of single-precision floating-point components on one line.
    /// Canvases canonical <c>SliderScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Float"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderScalarN(string label, ImGuiDataType dataType, float[] v, int components, float vMin, float vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Float, "This SliderScalarN overload stores floats.");
        return SliderScalarNCore(label, dataType, v, components, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides a fixed number of signed 32-bit integer components on one line.
    /// Canvases canonical <c>SliderScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S32"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderScalarN(string label, ImGuiDataType dataType, int[] v, int components, int vMin, int vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S32, "This SliderScalarN overload stores ints.");
        return SliderScalarNCore(label, dataType, v, components, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides a fixed number of unsigned 32-bit integer components on one line.
    /// Canvases canonical <c>SliderScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U32"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderScalarN(string label, ImGuiDataType dataType, uint[] v, int components, uint vMin, uint vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U32, "This SliderScalarN overload stores uints.");
        return SliderScalarNCore(label, dataType, v, components, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides a fixed number of double-precision floating-point components on one line.
    /// Canvases canonical <c>SliderScalarN</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Double"/> for this overload.</param>
    /// <param name="v">The component array.</param>
    /// <param name="components">The number of components to submit.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderScalarN(string label, ImGuiDataType dataType, double[] v, int components, double vMin, double vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Double, "This SliderScalarN overload stores doubles.");
        return SliderScalarNCore(label, dataType, v, components, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides a single float. Canvases canonical <c>SliderFloat</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderFloat(string label, ref float v, float vMin, float vMax, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        return SliderScalar(label, ImGuiDataType.Float, ref v, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides two floats on one line. Canvases canonical <c>SliderFloat2</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least two elements are required.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderFloat2(string label, float[] v, float vMin, float vMax, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 2, "SliderFloat2 requires at least two components.");
        return SliderScalarN(label, ImGuiDataType.Float, v, 2, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides three floats on one line. Canvases canonical <c>SliderFloat3</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least three elements are required.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderFloat3(string label, float[] v, float vMin, float vMax, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 3, "SliderFloat3 requires at least three components.");
        return SliderScalarN(label, ImGuiDataType.Float, v, 3, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides four floats on one line. Canvases canonical <c>SliderFloat4</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least four elements are required.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderFloat4(string label, float[] v, float vMin, float vMax, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 4, "SliderFloat4 requires at least four components.");
        return SliderScalarN(label, ImGuiDataType.Float, v, 4, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides an angle expressed in radians but displayed and bounded in degrees.
    /// Canvases canonical <c>SliderAngle</c> (, implemented at
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="vRad">The angle in radians, updated in place.</param>
    /// <param name="vDegreesMin">The lower bound in degrees.</param>
    /// <param name="vDegreesMax">The upper bound in degrees.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>"%.0f deg"</c>.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>The identifier comes from the degrees value, so it is independent of the radians
    /// representation and matches the identifier a call to <c>SliderFloat</c> would produce.</remarks>
    public static bool SliderAngle(string label, ref float vRad, float vDegreesMin = -360.0f, float vDegreesMax = 360.0f, string? format = "%.0f deg", ImGuiSliderFlags flags = 0)
    {
        if (format == null)
            format = "%.0f deg";
        float vDeg = vRad * 360.0f / (2 * MathF.PI);
        bool valueChanged = SliderFloat(label, ref vDeg, vDegreesMin, vDegreesMax, format, flags);
        if (valueChanged)
            vRad = vDeg * (2 * MathF.PI) / 360.0f;
        return valueChanged;
    }

    /// <summary>
    /// Slides a single signed integer. Canvases canonical <c>SliderInt</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool SliderInt(string label, ref int v, int vMin, int vMax, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        return SliderScalar(label, ImGuiDataType.S32, ref v, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides two integers on one line. Canvases canonical <c>SliderInt2</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least two elements are required.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderInt2(string label, int[] v, int vMin, int vMax, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 2, "SliderInt2 requires at least two components.");
        return SliderScalarN(label, ImGuiDataType.S32, v, 2, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides three integers on one line. Canvases canonical <c>SliderInt3</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least three elements are required.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderInt3(string label, int[] v, int vMin, int vMax, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 3, "SliderInt3 requires at least three components.");
        return SliderScalarN(label, ImGuiDataType.S32, v, 3, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides four integers on one line. Canvases canonical <c>SliderInt4</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="v">The component array; at least four elements are required.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool SliderInt4(string label, int[] v, int vMin, int vMax, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 4, "SliderInt4 requires at least four components.");
        return SliderScalarN(label, ImGuiDataType.S32, v, 4, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Implements the full vertical slider item for one data type on the port's boxed scalar boundary.
    /// Canvases canonical <c>VSliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size supplied by the caller.</param>
    /// <param name="dataType">The data type of the value and bounds.</param>
    /// <param name="pData">The boxed value, updated in place.</param>
    /// <param name="pMin">The required boxed lower bound.</param>
    /// <param name="pMax">The required boxed upper bound.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the data type's default.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// The vertical slider has no temporary text-input path, submits its item rectangle without the
    /// inputable flag, and draws its border directly through <c>RenderFrame(borders: true)</c> rather than
    /// a separate <c>RenderFrameBorder</c> call.
    /// </remarks>
    private static bool VSliderScalarCore(string label, ImVec2 size, ImGuiDataType dataType, ref object? pData, object? pMin, object? pMax, string? format, ImGuiSliderFlags flags)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        uint id = GetID(label);

        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false);
        ImRect frameBb = new(window.DC.CursorPos, window.DC.CursorPos + size);
        ImRect bb = new(frameBb.Min, frameBb.Max + new ImVec2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0.0f));

        ItemSize(bb, style.FramePadding.Y);
        if (!ItemAdd(frameBb, id))
            return false;

        if (format == null)
            format = DataTypeGetInfo(dataType)!.PrintFmt;

        bool hovered = ItemHoverable(frameBb, id, g.LastItemData.ItemFlags);
        bool clicked = hovered && IsMouseClicked(ImGuiMouseButton.Left, ImGuiInputFlags.None, id);
        if (clicked || g.NavActivateId == id)
        {
            if (clicked)
                SetKeyOwner(ImGuiKey.MouseLeft, id);
            SetActiveID(id, window);
            SetFocusID(id, window);
            FocusWindow(window);
            g.ActiveIdUsingNavDirMask |= (1u << (int)ImGuiDir.Up) | (1u << (int)ImGuiDir.Down);
        }

        uint frameCol = GetColorU32(g.ActiveId == id ? ImGuiCol.FrameBgActive : hovered ? ImGuiCol.FrameBgHovered : ImGuiCol.FrameBg);
        RenderNavCursor(frameBb, id);
        RenderFrame(frameBb.Min, frameBb.Max, frameCol, true, g.Style.FrameRounding);

        bool valueChanged = SliderBehavior(frameBb, id, dataType, ref pData, pMin, pMax, format, flags | DragSliderFlagVertical, out ImRect grabBb);
        if (valueChanged)
            MarkItemEdited(id);

        if (grabBb.Max.Y > grabBb.Min.Y)
            window.DrawList.AddRectFilled(grabBb.Min, grabBb.Max, GetColorU32(g.ActiveId == id ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab), style.GrabRounding);

        // The vertical slider permits centered text to overlap the frame padding.
        string valueBuf = DataTypeFormatString(dataType, pData, format);
        RenderTextClipped(new ImVec2(frameBb.Min.X, frameBb.Min.Y + style.FramePadding.Y), frameBb.Max, valueBuf, null, null, new ImVec2(0.5f, 0.0f));
        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(frameBb.Max.X + style.ItemInnerSpacing.X, frameBb.Min.Y + style.FramePadding.Y), label, labelEnd, false);

        return valueChanged;
    }

    /// <summary>
    /// Slides a single-precision floating-point value vertically.
    /// Canvases canonical <c>VSliderScalar</c> (, implemented at
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Float"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool VSliderScalar(string label, ImVec2 size, ImGuiDataType dataType, ref float v, float vMin, float vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Float, "This VSliderScalar overload stores a float.");
        object? boxed = v;
        bool changed = VSliderScalarCore(label, size, dataType, ref boxed, vMin, vMax, format, flags);
        v = (float)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a signed 32-bit integer value vertically.
    /// Canvases canonical <c>VSliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S32"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool VSliderScalar(string label, ImVec2 size, ImGuiDataType dataType, ref int v, int vMin, int vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.S32, "This VSliderScalar overload stores an int.");
        object? boxed = v;
        bool changed = VSliderScalarCore(label, size, dataType, ref boxed, vMin, vMax, format, flags);
        v = (int)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides an unsigned 32-bit integer value vertically.
    /// Canvases canonical <c>VSliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U32"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool VSliderScalar(string label, ImVec2 size, ImGuiDataType dataType, ref uint v, uint vMin, uint vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.U32, "This VSliderScalar overload stores a uint.");
        object? boxed = v;
        bool changed = VSliderScalarCore(label, size, dataType, ref boxed, vMin, vMax, format, flags);
        v = (uint)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a double-precision floating-point value vertically.
    /// Canvases canonical <c>VSliderScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.Double"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool VSliderScalar(string label, ImVec2 size, ImGuiDataType dataType, ref double v, double vMin, double vMax, string? format = null, ImGuiSliderFlags flags = 0)
    {
        ImAssert.Check(dataType == ImGuiDataType.Double, "This VSliderScalar overload stores a double.");
        object? boxed = v;
        bool changed = VSliderScalarCore(label, size, dataType, ref boxed, vMin, vMax, format, flags);
        v = (double)boxed!;
        return changed;
    }

    /// <summary>
    /// Slides a float vertically. Canvases canonical <c>VSliderFloat</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool VSliderFloat(string label, ImVec2 size, ref float v, float vMin, float vMax, string? format = "%.3f", ImGuiSliderFlags flags = 0)
    {
        return VSliderScalar(label, size, ImGuiDataType.Float, ref v, vMin, vMax, format, flags);
    }

    /// <summary>
    /// Slides an integer vertically. Canvases canonical <c>VSliderInt</c>
    /// (, implemented.).
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="size">The frame size.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="vMin">The lower bound.</param>
    /// <param name="vMax">The upper bound.</param>
    /// <param name="format">The display format.</param>
    /// <param name="flags">The slider flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool VSliderInt(string label, ImVec2 size, ref int v, int vMin, int vMax, string? format = "%d", ImGuiSliderFlags flags = 0)
    {
        return VSliderScalar(label, size, ImGuiDataType.S32, ref v, vMin, vMax, format, flags);
    }

    #endregion

    #endregion


    #region SECTION: INPUT TEXT AND SCALAR INPUTS

    #region [SECTION] Widgets: InputScalar, InputFloat, InputInt, etc.

    #region Shared scalar and text-buffer helpers

    /// <summary>
    /// Removes leading and trailing spaces and tabs from a zero-terminated byte buffer in place; ports
    /// the canonical <c>ImStrTrimBlanks</c> trim used by <see cref="TempInputScalar"/>
    /// (, ).
    /// </summary>
    /// <param name="buf">Zero-terminated UTF-8 buffer to trim; the terminator is rewritten.</param>
    private static void ImStrTrimBlanks(byte[] buf)
    {
        int length = InputTextStrLen(buf, buf.Length);
        int start = 0;
        while (start < length && (buf[start] == (byte)' ' || buf[start] == (byte)'\t'))
            start++;

        int end = length;
        while (end > start && (buf[end - 1] == (byte)' ' || buf[end - 1] == (byte)'\t'))
            end--;

        int newLength = end - start;
        for (int i = 0; i < newLength; i++)
            buf[i] = buf[start + i];
        buf[newLength] = 0;
    }

    /// <summary>
    /// Returns the number of bytes before the zero terminator, or <paramref name="capacity"/> when no
    /// terminator is present; the UTF-8 counterpart of canonical <c>ImStrlen</c>.
    /// </summary>
    /// <param name="buf">Byte buffer to scan.</param>
    /// <param name="capacity">Maximum number of bytes to inspect.</param>
    /// <returns>The number of bytes before the first zero byte.</returns>
    private static int InputTextStrLen(byte[] buf, int capacity)
    {
        int limit = Math.Min(capacity, buf.Length);
        int i = 0;
        while (i < limit && buf[i] != 0)
            i++;
        return i;
    }

    /// <summary>
    /// Grows a managed text buffer with zeros until it holds at least <paramref name="length"/>
    /// elements, matching the value-initializing canonical <c>ImVector::resize</c> growth used for
    /// text state buffers.
    /// </summary>
    /// <param name="buffer">Buffer to grow.</param>
    /// <param name="length">Required element count.</param>
    private static void InputTextEnsureBuffer(List<byte> buffer, int length)
    {
        while (buffer.Count < length)
            buffer.Add(0);
    }

    /// <summary>
    /// Copies a leading run of bytes into a managed text buffer, growing it first.
    /// </summary>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="source">Source bytes.</param>
    /// <param name="count">Number of bytes to copy.</param>
    private static void InputTextCopy(List<byte> destination, byte[] source, int count)
    {
        if (count <= 0)
            return;
        InputTextEnsureBuffer(destination, count);
        for (int i = 0; i < count; i++)
            destination[i] = source[i];
    }

    /// <summary>
    /// Copies a leading run of bytes from a managed text buffer into a fresh array.
    /// </summary>
    /// <param name="source">Source buffer.</param>
    /// <param name="length">Number of bytes to copy.</param>
    /// <returns>A new array holding the copied bytes.</returns>
    private static byte[] InputTextByteArray(List<byte> source, int length)
    {
        byte[] result = new byte[Math.Max(length, 0)];
        for (int i = 0; i < result.Length && i < source.Count; i++)
            result[i] = source[i];
        return result;
    }

    /// <summary>
    /// Compares the leading bytes of a managed text buffer with a byte array, mirroring canonical
    /// <c>strncmp</c> equality tests.
    /// </summary>
    /// <param name="buffer">Managed buffer.</param>
    /// <param name="other">Byte array to compare against.</param>
    /// <param name="length">Number of bytes to compare.</param>
    /// <returns><see langword="true"/> when the leading runs are equal.</returns>
    private static bool InputTextComparePrefix(List<byte> buffer, byte[] other, int length)
    {
        if (buffer.Count < length || other.Length < length)
            return false;
        for (int i = 0; i < length; i++)
            if (buffer[i] != other[i])
                return false;
        return true;
    }

    /// <summary>
    /// Compares a managed text buffer with the zero-terminated content of a byte array, mirroring the
    /// canonical full <c>strcmp</c> tests used for change detection.
    /// </summary>
    /// <param name="state">Managed buffer holding the edited text.</param>
    /// <param name="buf">Zero-terminated user byte buffer.</param>
    /// <param name="stateLen">Edited text length in bytes.</param>
    /// <returns><see langword="true"/> when both texts are identical, including their lengths.</returns>
    private static bool InputTextStateEqualsBuffer(List<byte> state, byte[] buf, int stateLen)
    {
        int bufLen = InputTextStrLen(buf, buf.Length);
        if (bufLen != stateLen || state.Count < stateLen)
            return false;
        for (int i = 0; i < stateLen; i++)
            if (state[i] != buf[i])
                return false;
        return true;
    }

    /// <summary>
    /// Copies a byte run into a managed buffer and truncates it to exactly that length, matching the
    /// canonical <c>ImVector::resize</c> shrink used for the deactivation backup
    /// </summary>
    /// <param name="destination">Destination buffer, replaced in place.</param>
    /// <param name="source">Source bytes.</param>
    /// <param name="count">Exact number of bytes to copy.</param>
    private static void InputTextSetExact(List<byte> destination, List<byte> source, int count)
    {
        destination.Clear();
        for (int i = 0; i < count && i < source.Count; i++)
            destination.Add(source[i]);
    }

    /// <summary>
    /// Compares two managed text buffers over an explicit byte count.
    /// </summary>
    /// <param name="a">First buffer.</param>
    /// <param name="b">Second buffer.</param>
    /// <param name="length">Number of bytes to compare.</param>
    /// <returns><see langword="true"/> when the leading runs are equal.</returns>
    private static bool InputTextCompareBuffers(List<byte> a, List<byte> b, int length)
    {
        if (a.Count < length || b.Count < length)
            return false;
        for (int i = 0; i < length; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    /// <summary>
    /// Copies a string's UTF-8 bytes into a zero-terminated byte buffer.
    /// </summary>
    /// <param name="destination">Destination buffer; it must have room for the terminator.</param>
    /// <param name="text">Text to encode.</param>
    private static void InputTextEncodeInto(byte[] destination, string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        int count = Math.Min(bytes.Length, destination.Length - 1);
        for (int i = 0; i < count; i++)
            destination[i] = bytes[i];
        destination[count] = 0;
    }

    /// <summary>
    /// Decodes the zero-terminated content of a byte buffer into a managed string.
    /// </summary>
    /// <param name="buffer">UTF-8 byte buffer.</param>
    /// <returns>The decoded managed string.</returns>
    private static string InputTextDecodeBuffer(byte[] buffer)
    {
        return System.Text.Encoding.UTF8.GetString(buffer, 0, InputTextStrLen(buffer, buffer.Length));
    }

    /// <summary>
    /// Decodes a UTF-8 byte run into a managed string for rendering and text metrics.
    /// </summary>
    /// <param name="text">UTF-8 bytes.</param>
    /// <returns>The decoded managed string.</returns>
    private static string InputTextDecodeUtf8(ReadOnlySpan<byte> text)
    {
        return text.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(text);
    }

    /// <summary>
    /// Finds one byte within a run starting at a given offset, mirroring canonical <c>ImMemchr</c>
    /// </summary>
    /// <param name="text">Byte run to scan.</param>
    /// <param name="start">First offset to inspect.</param>
    /// <param name="value">Byte value to find.</param>
    /// <returns>The matching offset, or <c>-1</c> when absent.</returns>
    private static int InputTextMemchr(ReadOnlySpan<byte> text, int start, byte value)
    {
        for (int i = Math.Max(start, 0); i < text.Length; i++)
            if (text[i] == value)
                return i;
        return -1;
    }

    /// <summary>
    /// Stores a boxed scalar into the context's eight-byte data-type storage.
    /// </summary>
    /// <param name="storage">Destination storage.</param>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="value">Boxed value to store.</param>
    private static void InputTextStoreDataType(ImGuiDataTypeStorage storage, ImGuiDataType dataType, object? value)
    {
        byte[] data = storage.Data;
        switch (dataType)
        {
            case ImGuiDataType.S8: data[0] = (byte)(sbyte)value!; break;
            case ImGuiDataType.U8: data[0] = (byte)value!; break;
            case ImGuiDataType.S16: System.BitConverter.TryWriteBytes(data.AsSpan(0, 2), (short)value!); break;
            case ImGuiDataType.U16: System.BitConverter.TryWriteBytes(data.AsSpan(0, 2), (ushort)value!); break;
            case ImGuiDataType.S32: System.BitConverter.TryWriteBytes(data.AsSpan(0, 4), (int)value!); break;
            case ImGuiDataType.U32: System.BitConverter.TryWriteBytes(data.AsSpan(0, 4), (uint)value!); break;
            case ImGuiDataType.S64: System.BitConverter.TryWriteBytes(data.AsSpan(0, 8), (long)value!); break;
            case ImGuiDataType.U64: System.BitConverter.TryWriteBytes(data.AsSpan(0, 8), (ulong)value!); break;
            case ImGuiDataType.Float: System.BitConverter.TryWriteBytes(data.AsSpan(0, 4), (float)value!); break;
            case ImGuiDataType.Double: System.BitConverter.TryWriteBytes(data.AsSpan(0, 8), (double)value!); break;
            case ImGuiDataType.Bool: data[0] = (bool)value! ? (byte)1 : (byte)0; break;
        }
    }

    /// <summary>
    /// Reads a boxed scalar from the context's eight-byte data-type storage.
    /// </summary>
    /// <param name="storage">Source storage.</param>
    /// <param name="dataType">Concrete type to read.</param>
    /// <returns>The boxed value.</returns>
    private static object InputTextReadDataType(ImGuiDataTypeStorage storage, ImGuiDataType dataType)
    {
        byte[] data = storage.Data;
        return dataType switch
        {
            ImGuiDataType.S8 => (sbyte)data[0],
            ImGuiDataType.U8 => data[0],
            ImGuiDataType.S16 => System.BitConverter.ToInt16(data, 0),
            ImGuiDataType.U16 => System.BitConverter.ToUInt16(data, 0),
            ImGuiDataType.S32 => System.BitConverter.ToInt32(data, 0),
            ImGuiDataType.U32 => System.BitConverter.ToUInt32(data, 0),
            ImGuiDataType.S64 => System.BitConverter.ToInt64(data, 0),
            ImGuiDataType.U64 => System.BitConverter.ToUInt64(data, 0),
            ImGuiDataType.Float => System.BitConverter.ToSingle(data, 0),
            ImGuiDataType.Double => System.BitConverter.ToDouble(data, 0),
            ImGuiDataType.Bool => data[0] != 0,
            _ => 0,
        };
    }

    /// <summary>
    /// Re-boxes a scalar into the concrete type that <paramref name="dataType"/> names, so downstream
    /// comparisons, clamps, and public overload casts see a canonical box.
    /// </summary>
    /// <param name="dataType">Concrete type the value must be boxed as.</param>
    /// <param name="value">Value to normalize.</param>
    /// <returns>The value boxed as the canonical type.</returns>
    /// <remarks>
    /// <c>DataTypeApplyFromText</c> scans floating-point text into a <see cref="double"/> and its
    /// ternary expression then unifies the <c>float</c> and <c>double</c> branches to <see cref="double"/>,
    /// so a field declared as <see cref="ImGuiDataType.Float"/> can come back boxed as a double. This
    /// helper restores the declared type at the widget boundary.
    /// </remarks>
    private static object? InputTextNormalizeBoxed(ImGuiDataType dataType, object? value)
    {
        if (value == null)
            return null;
        return dataType switch
        {
            ImGuiDataType.Float => value is float f ? f : Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
            ImGuiDataType.Double => value is double d ? d : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    /// <summary>
    /// Compares two boxed scalar values of the same data type, the boxed realization of canonical
    /// <c>DataTypeCompare</c>.
    /// </summary>
    /// <param name="dataType">Concrete type of both values.</param>
    /// <param name="a">First value.</param>
    /// <param name="b">Second value.</param>
    /// <returns>A negative value, zero, or a positive value following the canonical comparison order.</returns>
    private static int InputTextCompareBoxed(ImGuiDataType dataType, object a, object b)
    {
        return dataType switch
        {
            ImGuiDataType.S8 => ((sbyte)a).CompareTo((sbyte)b),
            ImGuiDataType.U8 => ((byte)a).CompareTo((byte)b),
            ImGuiDataType.S16 => ((short)a).CompareTo((short)b),
            ImGuiDataType.U16 => ((ushort)a).CompareTo((ushort)b),
            ImGuiDataType.S32 => ((int)a).CompareTo((int)b),
            ImGuiDataType.U32 => ((uint)a).CompareTo((uint)b),
            ImGuiDataType.S64 => ((long)a).CompareTo((long)b),
            ImGuiDataType.U64 => ((ulong)a).CompareTo((ulong)b),
            ImGuiDataType.Float => ((float)a).CompareTo((float)b),
            ImGuiDataType.Double => ((double)a).CompareTo((double)b),
            ImGuiDataType.Bool => ((bool)a).CompareTo((bool)b),
            ImGuiDataType.String => string.CompareOrdinal((string)a, (string)b),
            _ => 0,
        };
    }

    /// <summary>
    /// Clamps a boxed scalar to an optional inclusive range, the boxed realization of canonical
    /// <c>DataTypeClamp</c>.
    /// </summary>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="pData">Boxed value, clamped in place.</param>
    /// <param name="pMin">Optional lower bound.</param>
    /// <param name="pMax">Optional upper bound.</param>
    private static void InputTextClampBoxed(ImGuiDataType dataType, ref object? pData, object? pMin, object? pMax)
    {
        switch (dataType)
        {
            case ImGuiDataType.S8:
            {
                sbyte v = (sbyte)pData!;
                if (pMin != null) v = Math.Max(v, (sbyte)pMin);
                if (pMax != null) v = Math.Min(v, (sbyte)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.U8:
            {
                byte v = (byte)pData!;
                if (pMin != null) v = Math.Max(v, (byte)pMin);
                if (pMax != null) v = Math.Min(v, (byte)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.S16:
            {
                short v = (short)pData!;
                if (pMin != null) v = Math.Max(v, (short)pMin);
                if (pMax != null) v = Math.Min(v, (short)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.U16:
            {
                ushort v = (ushort)pData!;
                if (pMin != null) v = Math.Max(v, (ushort)pMin);
                if (pMax != null) v = Math.Min(v, (ushort)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.S32:
            {
                int v = (int)pData!;
                if (pMin != null) v = Math.Max(v, (int)pMin);
                if (pMax != null) v = Math.Min(v, (int)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.U32:
            {
                uint v = (uint)pData!;
                if (pMin != null) v = Math.Max(v, (uint)pMin);
                if (pMax != null) v = Math.Min(v, (uint)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.S64:
            {
                long v = (long)pData!;
                if (pMin != null) v = Math.Max(v, (long)pMin);
                if (pMax != null) v = Math.Min(v, (long)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.U64:
            {
                ulong v = (ulong)pData!;
                if (pMin != null) v = Math.Max(v, (ulong)pMin);
                if (pMax != null) v = Math.Min(v, (ulong)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.Float:
            {
                float v = (float)pData!;
                if (pMin != null) v = MathF.Max(v, (float)pMin);
                if (pMax != null) v = MathF.Min(v, (float)pMax);
                pData = v;
                break;
            }
            case ImGuiDataType.Double:
            {
                double v = (double)pData!;
                if (pMin != null) v = Math.Max(v, (double)pMin);
                if (pMax != null) v = Math.Min(v, (double)pMax);
                pData = v;
                break;
            }
        }
    }

    /// <summary>
    /// Applies the canonical <c>'+'</c> or <c>'-'</c> scalar operation to a boxed value, the boxed
    /// realization of canonical <c>DataTypeApplyOp</c>
    /// (, used at <c>3882,3888</c>).
    /// </summary>
    /// <param name="dataType">Concrete type of the operands.</param>
    /// <param name="op">Either <c>'+'</c> or <c>'-'</c>.</param>
    /// <param name="pData">Boxed value updated in place.</param>
    /// <param name="arg">Boxed step value.</param>
    private static void InputTextApplyOpBoxed(ImGuiDataType dataType, int op, ref object? pData, object? arg)
    {
        switch (dataType)
        {
            case ImGuiDataType.S8: { sbyte v = (sbyte)pData!; DataTypeApplyOp(dataType, op, ref v, v, (sbyte)arg!); pData = v; break; }
            case ImGuiDataType.U8: { byte v = (byte)pData!; DataTypeApplyOp(dataType, op, ref v, v, (byte)arg!); pData = v; break; }
            case ImGuiDataType.S16: { short v = (short)pData!; DataTypeApplyOp(dataType, op, ref v, v, (short)arg!); pData = v; break; }
            case ImGuiDataType.U16: { ushort v = (ushort)pData!; DataTypeApplyOp(dataType, op, ref v, v, (ushort)arg!); pData = v; break; }
            case ImGuiDataType.S32: { int v = (int)pData!; DataTypeApplyOp(dataType, op, ref v, v, (int)arg!); pData = v; break; }
            case ImGuiDataType.U32: { uint v = (uint)pData!; DataTypeApplyOp(dataType, op, ref v, v, (uint)arg!); pData = v; break; }
            case ImGuiDataType.S64: { long v = (long)pData!; DataTypeApplyOp(dataType, op, ref v, v, (long)arg!); pData = v; break; }
            case ImGuiDataType.U64: { ulong v = (ulong)pData!; DataTypeApplyOp(dataType, op, ref v, v, (ulong)arg!); pData = v; break; }
            case ImGuiDataType.Float: { float v = (float)pData!; DataTypeApplyOp(dataType, op, ref v, v, (float)arg!); pData = v; break; }
            case ImGuiDataType.Double: { double v = (double)pData!; DataTypeApplyOp(dataType, op, ref v, v, (double)arg!); pData = v; break; }
        }
    }

    #endregion

    /// <summary>
    /// Stores the default value of the next scalar item, used to display or parse an empty field as a
    /// reference value; ports canonical <c>SetNextItemRefVal</c>.
    /// </summary>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="pData">Boxed value to remember as the reference default.</param>
    internal static void SetNextItemRefVal(ImGuiDataType dataType, ref object? pData)
    {
        ImGuiContext g = GetCurrentContext()!;
        g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasRefVal;
        InputTextStoreDataType(g.NextItemData.RefVal, dataType, pData);
    }

    /// <summary>
    /// Reports whether a temporary text input owns the given identifier, either directly or through a
    /// pending deactivation record; ports canonical <c>TempInputIsActive</c>.
    /// </summary>
    /// <param name="id">Widget identifier to test.</param>
    /// <returns><see langword="true"/> when the temporary input is active or pends deactivation.</returns>
    internal static bool TempInputIsActive(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        return (g.TempInputId == id && g.ActiveId == id) || g.InputTextDeactivatedState.ID == id;
    }

    /// <summary>
    /// Creates a text input overlaid on another active widget, such as the Ctrl+Click entry of a drag
    /// or slider; ports canonical <c>TempInputText</c>
    /// </summary>
    /// <param name="bb">Frame rectangle the input must cover.</param>
    /// <param name="id">Identifier of the widget being overlaid.</param>
    /// <param name="label">Label used for the identifier and the display text.</param>
    /// <param name="buf">User-visible text buffer, zero-terminated.</param>
    /// <param name="bufSize">Buffer capacity including the terminator.</param>
    /// <param name="flags">Text input flags; the temporary-input and auto-select flags are added.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns><see langword="true"/> when the edited text changed.</returns>
    /// <remarks>
    /// The widget must be submitted immediately after the item it overlays. On the first frame the
    /// active identifier is cleared so that the embedded <c>InputTextEx</c> can claim it back.
    /// </remarks>
    internal static bool TempInputText(ImRect bb, uint id, string label, byte[] buf, int bufSize, ImGuiInputTextFlags flags, ImGuiInputTextCallback? callback, IntPtr userData)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();

        bool isDeactivated = g.InputTextDeactivatedState.ID == id;
        bool isActive = g.TempInputId == id;
        if (!isActive && !isDeactivated)
            ClearActiveID();

        ImVec2 backupPos = window.DC.CursorPos;
        window.DC.CursorPos = bb.Min;
        g.LastItemData.ItemFlags |= ImGuiItemFlags.AllowDuplicateId;
        ImGuiInputTextFlags mergedFlags = flags |
            (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_TempInput |
            ImGuiInputTextFlags.AutoSelectAll;
        bool valueChanged = InputTextEx(label, null, buf, bufSize, bb.GetSize(), mergedFlags, callback, userData);
        KeepAliveID(id);
        if (!isActive && !isDeactivated)
        {
            ImAssert.Check(g.ActiveId == id, "The temporary text input was expected to take the active id.");
            g.TempInputId = g.ActiveId;
        }
        if (isActive && g.ActiveId != id)
            g.TempInputId = 0;
        window.DC.CursorPos = backupPos;
        return valueChanged;
    }

    /// <summary>
    /// Edits a scalar value in place through a temporary text input, applying the display format and
    /// optional clamping; ports canonical <c>TempInputScalar</c>
    /// </summary>
    /// <param name="bb">Frame rectangle of the overlaid widget.</param>
    /// <param name="id">Identifier of the overlaid widget.</param>
    /// <param name="label">Label used for the identifier and the display text.</param>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="pData">Boxed value read, edited, and written back in place.</param>
    /// <param name="format">Display format; decorations outside the conversion are trimmed.</param>
    /// <param name="pClampMin">Optional lower bound forwarded only when the caller enables clamping.</param>
    /// <param name="pClampMax">Optional upper bound forwarded only when the caller enables clamping.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    /// <remarks>
    /// Drag and slider widgets forward bounds only for <see cref="ImGuiSliderFlags.AlwaysClamp"/>, so
    /// manual entry may intentionally produce an out-of-range value. The item's edited marker is
    /// deferred until the raw values have been compared.
    /// </remarks>
    internal static bool TempInputScalar(ImRect bb, uint id, string label, ImGuiDataType dataType, ref object? pData, string format, object? pClampMin = null, object? pClampMax = null)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiDataTypeInfo typeInfo = DataTypeGetInfo(dataType)!;
        string resolvedFormat = ImParseFormatTrimDecorations(format);
        if (resolvedFormat.Length == 0)
            resolvedFormat = typeInfo.PrintFmt;

        byte[] dataBuf = new byte[32];
        InputTextEncodeInto(dataBuf, DataTypeFormatString(dataType, pData, resolvedFormat));
        ImStrTrimBlanks(dataBuf);

        ImGuiInputTextFlags flags = ImGuiInputTextFlags.AutoSelectAll |
            (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_LocalizeDecimalPoint;
        ImGuiItemFlags noMarkEdited = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoMarkEdited;
        g.LastItemData.ItemFlags |= noMarkEdited;
        g.LastItemData.ItemFlags = (g.LastItemData.ItemFlags & ImGuiItemFlags.LiveEditOnInputScalar) != 0
            ? (g.LastItemData.ItemFlags | ImGuiItemFlags.LiveEditOnInputText)
            : (g.LastItemData.ItemFlags & ~ImGuiItemFlags.LiveEditOnInputText);
        if (!TempInputText(bb, id, label, dataBuf, dataBuf.Length, flags, null, default))
            return false;

        object? dataBackup = pData;
        DataTypeApplyFromText(InputTextDecodeBuffer(dataBuf), dataType, ref pData, resolvedFormat, null);
        pData = InputTextNormalizeBoxed(dataType, pData);
        if (pClampMin != null || pClampMax != null)
        {
            object? lo = pClampMin;
            object? hi = pClampMax;
            if (lo != null && hi != null && InputTextCompareBoxed(dataType, lo, hi) > 0)
                (lo, hi) = (hi, lo);
            InputTextClampBoxed(dataType, ref pData, lo, hi);
        }

        g.LastItemData.ItemFlags &= ~noMarkEdited;
        bool valueChanged = !Equals(dataBackup, pData);
        if ((g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0)
        {
            ImGuiInputTextState? inputState = GetInputTextState(g.LastItemData.ID);
            if (inputState != null)
                valueChanged |= (g.LastItemData.ItemFlags & ImGuiItemFlags.LiveEditOnInputScalar) != 0
                    ? (inputState.EditedThisFrame || inputState.ValidatedThisFrame)
                    : inputState.ValidatedThisFrame;
        }

        if (valueChanged)
            MarkItemEdited(id);
        return valueChanged;
    }

    /// <summary>
    /// Implements the scalar input item for one data type on the port's boxed scalar boundary; ports
    /// canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">Widget label; it supplies both the identifier and the displayed text.</param>
    /// <param name="dataType">Concrete type of the value and the step values.</param>
    /// <param name="pData">Boxed value updated in place.</param>
    /// <param name="pStep">Optional boxed step applied by the <c>-</c>/<c>+</c> buttons.</param>
    /// <param name="pStepFast">Optional boxed fast step applied while Ctrl is held.</param>
    /// <param name="format">Display format; <see langword="null"/> selects the data type's default.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the stored value changed.
    /// </returns>
    /// <remarks>
    /// Canonical takes <c>void*</c> and a size; the port carries a boxed value and switches on
    /// <paramref name="dataType"/>. <c>ImGuiInputTextFlags_EnterReturnsTrue</c> is deliberately not
    /// supported by the scalar item, matching the commented-out canonical assert.
    /// </remarks>
    private static bool InputScalarCore(string label, ImGuiDataType dataType, ref object? pData, object? pStep, object? pStepFast, string? format, ImGuiInputTextFlags flags)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        if (format == null)
            format = DataTypeGetInfo(dataType)!.PrintFmt;

        object pDataDefault = (g.NextItemData.HasFlags & ImGuiNextItemDataFlags.HasRefVal) != 0
            ? InputTextReadDataType(g.NextItemData.RefVal, dataType)
            : InputTextReadDataType(g.DataTypeZeroValue, dataType);

        byte[] buf = new byte[64];
        if ((flags & ImGuiInputTextFlags.DisplayEmptyRefVal) != 0 && InputTextCompareBoxed(dataType, pData!, pDataDefault) == 0)
            buf[0] = 0;
        else
            InputTextEncodeInto(buf, DataTypeFormatString(dataType, pData, format));

        // Defer the edit marker to the raw-value comparison performed below.
        g.NextItemData.ItemFlagsSet |= (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoMarkEdited;
        flags |= ImGuiInputTextFlags.AutoSelectAll |
            (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_LocalizeDecimalPoint;

        bool hasStepButtons = pStep != null;
        float buttonSize = hasStepButtons ? GetFrameHeight() : 0.0f;
        bool ret;
        if (hasStepButtons)
        {
            BeginGroup();
            PushID(label);
            SetNextItemWidth(MathF.Max(1.0f, CalcItemWidth() - (buttonSize + style.ItemInnerSpacing.X) * 2.0f));
            ret = InputTextEx(string.Empty, null, buf, buf.Length, default, flags, null, default);
        }
        else
        {
            ret = InputTextEx(label, null, buf, buf.Length, default, flags, null, default);
        }

        bool valueChanged = false;
        if ((g.LastItemData.ItemFlags & ImGuiItemFlags.LiveEditOnInputScalar) != 0)
        {
            bool inputEdited = (g.LastItemData.StatusFlags & ImGuiItemStatusFlags.EditedInternal) != 0;
            if (inputEdited)
            {
                valueChanged = DataTypeApplyFromText(InputTextDecodeBuffer(buf), dataType, ref pData, format,
                    (flags & ImGuiInputTextFlags.ParseEmptyRefVal) != 0 ? pDataDefault : null);
                pData = InputTextNormalizeBoxed(dataType, pData);
            }
        }
        else if (g.DeactivatedItemData.ID == g.LastItemData.ID)
        {
            valueChanged = DataTypeApplyFromText(InputTextDecodeBuffer(buf), dataType, ref pData, format,
                (flags & ImGuiInputTextFlags.ParseEmptyRefVal) != 0 ? pDataDefault : null);
            pData = InputTextNormalizeBoxed(dataType, pData);
        }
        if ((g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0)
        {
            ImGuiInputTextState? state = GetInputTextState(g.LastItemData.ID);
            if (state != null)
            {
                bool mixedEdited = (g.LastItemData.ItemFlags & ImGuiItemFlags.LiveEditOnInputScalar) != 0
                    ? (state.EditedThisFrame || state.ValidatedThisFrame)
                    : state.ValidatedThisFrame;
                if (mixedEdited)
                {
                    valueChanged = DataTypeApplyFromText(InputTextDecodeBuffer(buf), dataType, ref pData, format,
                        (flags & ImGuiInputTextFlags.ParseEmptyRefVal) != 0 ? pDataDefault : null);
                    pData = InputTextNormalizeBoxed(dataType, pData);
                }
            }
        }

        if (hasStepButtons)
        {
            ImVec2 backupFramePadding = style.FramePadding;
            style.FramePadding = new ImVec2(style.FramePadding.Y, style.FramePadding.Y);
            if ((flags & ImGuiInputTextFlags.ReadOnly) != 0)
                BeginDisabled();
            PushItemFlag(ImGuiItemFlags.ButtonRepeat, true);
            SameLine(0.0f, style.ItemInnerSpacing.X);
            if (ButtonEx("-", new ImVec2(buttonSize, buttonSize)))
            {
                InputTextApplyOpBoxed(dataType, '-', ref pData, g.IO.KeyCtrl && pStepFast != null ? pStepFast : pStep);
                valueChanged = ret = true;
            }
            SameLine(0.0f, style.ItemInnerSpacing.X);
            if (ButtonEx("+", new ImVec2(buttonSize, buttonSize)))
            {
                InputTextApplyOpBoxed(dataType, '+', ref pData, g.IO.KeyCtrl && pStepFast != null ? pStepFast : pStep);
                valueChanged = ret = true;
            }
            PopItemFlag();
            if ((flags & ImGuiInputTextFlags.ReadOnly) != 0)
                EndDisabled();

            string labelEnd = FindRenderedTextEnd(label);
            if (labelEnd.Length > 0)
            {
                SameLine(0.0f, style.ItemInnerSpacing.X);
                TextEx(label, labelEnd);
            }
            style.FramePadding = backupFramePadding;

            PopID();
            EndGroup();
        }

        g.LastItemData.ItemFlags &= ~(ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoMarkEdited;
        if (valueChanged)
            MarkItemEdited(g.LastItemData.ID);

        return (flags & ImGuiInputTextFlags.EnterReturnsTrue) != 0 ? ret : valueChanged;
    }

    /// <summary>
    /// Edits a single-precision floating-point value. Canvases canonical <c>InputScalar</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">Fast step applied while Ctrl is held.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref float v, float? step = null, float? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = boxed is float f ? f : Convert.ToSingle(boxed, System.Globalization.CultureInfo.InvariantCulture);
        return changed;
    }

    /// <summary>
    /// Edits a 32-bit signed integer value. Canvases canonical <c>InputScalar</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">Fast step applied while Ctrl is held.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref int v, int? step = null, int? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (int)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits a 32-bit unsigned integer value. Canvases canonical <c>InputScalar</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">Fast step applied while Ctrl is held.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref uint v, uint? step = null, uint? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (uint)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits a double-precision floating-point value. Canvases canonical <c>InputScalar</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="dataType">Concrete type of the value.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">Fast step applied while Ctrl is held.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref double v, double? step = null, double? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (double)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits a signed 8-bit integer value with an optional text field.
    /// Canvases canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S8"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="step">The step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">The fast step applied while Ctrl is held.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The text input flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref sbyte v, sbyte? step = null, sbyte? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (sbyte)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits an unsigned 8-bit integer value with an optional text field.
    /// Canvases canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U8"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="step">The step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">The fast step applied while Ctrl is held.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The text input flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref byte v, byte? step = null, byte? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (byte)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits a signed 16-bit integer value with an optional text field.
    /// Canvases canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S16"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="step">The step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">The fast step applied while Ctrl is held.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%d</c>.</param>
    /// <param name="flags">The text input flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref short v, short? step = null, short? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (short)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits an unsigned 16-bit integer value with an optional text field.
    /// Canvases canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U16"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="step">The step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">The fast step applied while Ctrl is held.</param>
    /// <param name="format">The display format; <see langword="null"/> selects <c>%u</c>.</param>
    /// <param name="flags">The text input flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref ushort v, ushort? step = null, ushort? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (ushort)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits a signed 64-bit integer value with an optional text field.
    /// Canvases canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.S64"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="step">The step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">The fast step applied while Ctrl is held.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the canonical 64-bit decimal format.</param>
    /// <param name="flags">The text input flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref long v, long? step = null, long? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (long)boxed!;
        return changed;
    }

    /// <summary>
    /// Edits an unsigned 64-bit integer value with an optional text field.
    /// Canvases canonical <c>InputScalar</c>.
    /// </summary>
    /// <param name="label">The widget label.</param>
    /// <param name="dataType">The data type; it must be <see cref="ImGuiDataType.U64"/> for this overload.</param>
    /// <param name="v">The value, updated in place.</param>
    /// <param name="step">The step applied by the <c>-</c>/<c>+</c> buttons; <see langword="null"/> hides them.</param>
    /// <param name="stepFast">The fast step applied while Ctrl is held.</param>
    /// <param name="format">The display format; <see langword="null"/> selects the canonical 64-bit decimal format.</param>
    /// <param name="flags">The text input flags.</param>
    /// <returns><see langword="true"/> when the stored value changed.</returns>
    public static bool InputScalar(string label, ImGuiDataType dataType, ref ulong v, ulong? step = null, ulong? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object? boxed = v;
        bool changed = InputScalarCore(label, dataType, ref boxed, step, stepFast, format, flags);
        v = (ulong)boxed!;
        return changed;
    }

    /// <summary>
    /// Lays out a row of scalar inputs over a typed array; ports canonical <c>InputScalarN</c>
    /// </summary>
    /// <param name="label">Widget label; the per-component identifiers are scoped by it.</param>
    /// <param name="dataType">Concrete type of every component.</param>
    /// <param name="values">Component storage updated in place.</param>
    /// <param name="components">Number of components to display; must equal the usable array length.</param>
    /// <param name="step">Optional boxed step applied by the <c>-</c>/<c>+</c> buttons.</param>
    /// <param name="stepFast">Optional boxed fast step.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    /// <remarks>
    /// Canonical advances a <c>void*</c> by the data-type size; the port indexes the component array
    /// and reuses <see cref="InputScalarCore"/> for each element. The trailing label uses zero spacing,
    /// unlike the drag variants.
    /// </remarks>
    private static bool InputScalarNBoxed(string label, ImGuiDataType dataType, object?[] values, int components, object? step, object? stepFast, string? format, ImGuiInputTextFlags flags)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        bool valueChanged = false;
        BeginGroup();
        PushID(label);
        PushMultiItemsWidths(components, CalcItemWidth());
        for (int i = 0; i < components; i++)
        {
            PushID(i);
            if (i > 0)
                SameLine(0.0f, g.Style.ItemInnerSpacing.X);
            object? component = values[i];
            valueChanged |= InputScalarCore(string.Empty, dataType, ref component, step, stepFast, format, flags);
            values[i] = component;
            PopID();
            PopItemWidth();
        }
        PopID();

        string labelEnd = FindRenderedTextEnd(label);
        if (labelEnd.Length > 0)
        {
            SameLine(0.0f, g.Style.ItemInnerSpacing.X);
            TextEx(label, labelEnd);
        }

        EndGroup();
        return valueChanged;
    }

    /// <summary>
    /// Displays a row of floating-point inputs. Canvases canonical <c>InputScalarN</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="dataType">Concrete type of every component.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="components">Number of components to display.</param>
    /// <param name="step">Optional step value.</param>
    /// <param name="stepFast">Optional fast step value.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputScalarN(string label, ImGuiDataType dataType, float[] v, int components, float? step = null, float? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object?[] boxed = new object?[components];
        for (int i = 0; i < components; i++)
            boxed[i] = v[i];
        bool changed = InputScalarNBoxed(label, dataType, boxed, components, step, stepFast, format, flags);
        for (int i = 0; i < components; i++)
            v[i] = boxed[i] is float f ? f : Convert.ToSingle(boxed[i], System.Globalization.CultureInfo.InvariantCulture);
        return changed;
    }

    /// <summary>
    /// Displays a row of signed-integer inputs. Canvases canonical <c>InputScalarN</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="dataType">Concrete type of every component.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="components">Number of components to display.</param>
    /// <param name="step">Optional step value.</param>
    /// <param name="stepFast">Optional fast step value.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputScalarN(string label, ImGuiDataType dataType, int[] v, int components, int? step = null, int? stepFast = null, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        object?[] boxed = new object?[components];
        for (int i = 0; i < components; i++)
            boxed[i] = v[i];
        bool changed = InputScalarNBoxed(label, dataType, boxed, components, step, stepFast, format, flags);
        for (int i = 0; i < components; i++)
            v[i] = (int)boxed[i]!;
        return changed;
    }

    /// <summary>
    /// Edits a single floating-point value with optional step buttons; ports canonical
    /// <c>InputFloat</c>.
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the buttons when greater than zero.</param>
    /// <param name="stepFast">Fast step applied when greater than zero.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputFloat(string label, ref float v, float step = 0.0f, float stepFast = 0.0f, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        return InputScalar(label, ImGuiDataType.Float, ref v, step > 0.0f ? step : null, stepFast > 0.0f ? stepFast : null, format, flags);
    }

    /// <summary>
    /// Edits a two-component floating-point vector; ports canonical <c>InputFloat2</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputFloat2(string label, float[] v, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 2, "InputFloat2 needs at least two components.");
        return InputScalarN(label, ImGuiDataType.Float, v, 2, null, null, format, flags);
    }

    /// <summary>
    /// Edits a three-component floating-point vector; ports canonical <c>InputFloat3</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputFloat3(string label, float[] v, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 3, "InputFloat3 needs at least three components.");
        return InputScalarN(label, ImGuiDataType.Float, v, 3, null, null, format, flags);
    }

    /// <summary>
    /// Edits a four-component floating-point vector; ports canonical <c>InputFloat4</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputFloat4(string label, float[] v, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 4, "InputFloat4 needs at least four components.");
        return InputScalarN(label, ImGuiDataType.Float, v, 4, null, null, format, flags);
    }

    /// <summary>
    /// Edits a single signed-integer value, using a hexadecimal display when requested; ports canonical
    /// <c>InputInt</c>.
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the buttons when greater than zero.</param>
    /// <param name="stepFast">Fast step applied when greater than zero.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputInt(string label, ref int v, int step = 1, int stepFast = 100, ImGuiInputTextFlags flags = 0)
    {
        // Hexadecimal input is provided as a convenience even though the flag name is awkward.
        string format = (flags & ImGuiInputTextFlags.CharsHexadecimal) != 0 ? "%08X" : "%d";
        return InputScalar(label, ImGuiDataType.S32, ref v, step > 0 ? step : null, stepFast > 0 ? stepFast : null, format, flags);
    }

    /// <summary>
    /// Edits a two-component signed-integer vector; ports canonical <c>InputInt2</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputInt2(string label, int[] v, ImGuiInputTextFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 2, "InputInt2 needs at least two components.");
        return InputScalarN(label, ImGuiDataType.S32, v, 2, null, null, "%d", flags);
    }

    /// <summary>
    /// Edits a three-component signed-integer vector; ports canonical <c>InputInt3</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputInt3(string label, int[] v, ImGuiInputTextFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 3, "InputInt3 needs at least three components.");
        return InputScalarN(label, ImGuiDataType.S32, v, 3, null, null, "%d", flags);
    }

    /// <summary>
    /// Edits a four-component signed-integer vector; ports canonical <c>InputInt4</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Component storage updated in place.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when any component changed.</returns>
    public static bool InputInt4(string label, int[] v, ImGuiInputTextFlags flags = 0)
    {
        ImAssert.Check(v.Length >= 4, "InputInt4 needs at least four components.");
        return InputScalarN(label, ImGuiDataType.S32, v, 4, null, null, "%d", flags);
    }

    /// <summary>
    /// Edits a double-precision value with optional step buttons; ports canonical <c>InputDouble</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="v">Value updated in place.</param>
    /// <param name="step">Step applied by the buttons when greater than zero.</param>
    /// <param name="stepFast">Fast step applied when greater than zero.</param>
    /// <param name="format">Display format.</param>
    /// <param name="flags">Text input flags.</param>
    /// <returns><see langword="true"/> when the value changed.</returns>
    public static bool InputDouble(string label, ref double v, double step = 0.0, double stepFast = 0.0, string? format = null, ImGuiInputTextFlags flags = 0)
    {
        return InputScalar(label, ImGuiDataType.Double, ref v, step > 0.0 ? step : null, stepFast > 0.0 ? stepFast : null, format, flags);
    }

    #endregion

    #region [SECTION] Widgets: InputText support utilities

    /// <summary>Character class for a line-breaking blank, matching <c>ImWcharClass_Blank</c>.</summary>
    private const int InputTextClassBlank = 1;

    /// <summary>Character class for a line-breaking separator, matching <c>ImWcharClass_Punct</c>.</summary>
    private const int InputTextClassPunct = 2;

    /// <summary>Character class for any other character, matching <c>ImWcharClass_Other</c>.</summary>
    private const int InputTextClassOther = 0;

    /// <summary>
    /// Returns whether a codepoint is one of the blank characters that can end a wrapped line; ports
    /// canonical <c>ImCharIsBlankW</c> (, used at
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns><see langword="true"/> for space, tab, and the ideographic space.</returns>
    private static bool ImCharIsBlankW(uint c)
    {
        return c == ' ' || c == '\t' || c == 0x3000;
    }

    /// <summary>
    /// Classifies a codepoint for word wrapping, reproducing the canonical classifier tables
    /// </summary>
    /// <param name="c">Unicode codepoint.</param>
    /// <returns>One of the <c>InputTextClass*</c> values.</returns>
    private static int InputTextClassifyChar(uint c)
    {
        if (c == ' ' || c == '\t' || c == 0x3000)
            return InputTextClassBlank;
        if (c == '.' || c == ',' || c == ';' || c == '!' || c == '?' || c == '"' || c == 0x3001 || c == 0x3002)
            return InputTextClassPunct;
        return InputTextClassOther;
    }

    /// <summary>
    /// Decodes the codepoint starting at a byte offset and reports how many bytes it consumed.
    /// Canonical uses <c>ImTextCharFromUtf8</c> over the UTF-8 buffer
    /// </summary>
    /// <param name="text">UTF-8 byte run.</param>
    /// <param name="idx">Byte offset of the codepoint.</param>
    /// <param name="consumed">Receives the number of bytes consumed.</param>
    /// <returns>The decoded codepoint, or zero at and beyond the end.</returns>
    private static uint InputTextDecodeAt(ReadOnlySpan<byte> text, int idx, out int consumed)
    {
        if (idx >= text.Length)
        {
            consumed = 0;
            return 0;
        }
        int at = idx;
        consumed = ImFont.ImTextCharFromUtf8(text, ref at, out uint c);
        if (consumed <= 0)
            consumed = 1;
        return c;
    }

    /// <summary>
    /// Finds the byte offset where word wrapping should break; ports
    /// <c>ImFontCalcWordWrapPositionEx</c>. as used by
    /// the line-index builder.
    /// </summary>
    /// <param name="g">Owning context; it supplies the baked font and scale.</param>
    /// <param name="text">UTF-8 byte run.</param>
    /// <param name="start">First byte offset of the candidate line.</param>
    /// <param name="textEnd">Exclusive end offset of the wrapped text.</param>
    /// <param name="wrapWidth">Remaining horizontal space in pixels.</param>
    /// <param name="flags">Wrap behavior flags; <see cref="ImDrawTextFlags.WrapKeepBlanks"/> preserves blanks.</param>
    /// <returns>The absolute byte offset of the wrap position.</returns>
    /// <remarks>
    /// <see cref="ImStbTextedit"/> keeps its identical wrap hook private, so the host mirrors the same
    /// canonical classifier for its layout pass rather than exposing editor internals.
    /// </remarks>
    private static int InputTextCalcWordWrapPosition(ImGuiContext g, ReadOnlySpan<byte> text, int start, int textEnd, float wrapWidth, ImDrawTextFlags flags)
    {
        ImFontBaked? baked = g.FontBaked;
        if (baked == null)
            return start;

        float scale = g.FontBakedScale;
        float lineWidth = 0.0f;
        float blankWidth = 0.0f;
        float unscaledWrap = scale != 0.0f ? wrapWidth / scale : 0.0f;

        int prevType = InputTextClassOther;
        int spanEnd = start;
        float spanWidth = 0.0f;
        bool keepBlanks = (flags & ImDrawTextFlags.WrapKeepBlanks) != 0;

        int s = start;
        while (s < textEnd)
        {
            int charStart = s;
            uint c = InputTextDecodeAt(text, s, out int consumed);
            s += consumed;

            if (c < 32)
            {
                if (c == '\n')
                    return charStart;
                if (c == '\r')
                    continue;
            }

            float charWidth = baked.GetCharAdvance(c);
            int currType = InputTextClassifyChar(c);
            if (currType == InputTextClassBlank)
            {
                if (prevType != InputTextClassBlank && !keepBlanks)
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth;
                    spanWidth = 0.0f;
                }
                blankWidth += charWidth;
            }
            else
            {
                if (prevType == InputTextClassPunct && currType != InputTextClassPunct && !(c >= '0' && c <= '9'))
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth + blankWidth;
                    spanWidth = blankWidth = 0.0f;
                }
                else if (prevType == InputTextClassBlank && keepBlanks)
                {
                    spanEnd = charStart;
                    lineWidth += spanWidth + blankWidth;
                    spanWidth = blankWidth = 0.0f;
                }
                spanWidth += charWidth;
            }

            if (spanWidth + blankWidth + lineWidth > unscaledWrap)
            {
                if (spanWidth + blankWidth > unscaledWrap)
                    break;
                return spanEnd;
            }

            prevType = currType;
        }

        if (s == start && start < textEnd)
        {
            InputTextDecodeAt(text, start, out int single);
            return start + Math.Max(single, 1);
        }
        return s;
    }

    /// <summary>
    /// Measures the widest line of a UTF-8 byte run without wrapping; the port counterpart of the
    /// canonical <c>CalcTextSize</c> calls in the selection and left-elision paths.
    /// </summary>
    /// <param name="g">Owning context; it supplies the baked font and scale.</param>
    /// <param name="text">UTF-8 byte run.</param>
    /// <returns>The widest line advance in pixels.</returns>
    private static float InputTextBoxWidth(ImGuiContext g, ReadOnlySpan<byte> text)
    {
        ImFontBaked? baked = g.FontBaked;
        if (baked == null || text.Length == 0)
            return 0.0f;

        float scale = g.FontBakedScale;
        float lineWidth = 0.0f;
        float maxWidth = 0.0f;
        int s = 0;
        while (s < text.Length)
        {
            uint c = InputTextDecodeAt(text, s, out int consumed);
            s += consumed;
            if (c == '\n')
            {
                maxWidth = MathF.Max(maxWidth, lineWidth);
                lineWidth = 0.0f;
                continue;
            }
            if (c == '\r')
                continue;
            lineWidth += baked.GetCharAdvance(c) * scale;
        }
        return MathF.Max(maxWidth, lineWidth);
    }

    /// <summary>
    /// Returns the byte offset of the start of the line that contains a position; ports canonical
    /// <c>ImStrbol</c> (, used at <c>4167,4204</c>).
    /// </summary>
    /// <param name="text">UTF-8 byte run.</param>
    /// <param name="at">Byte offset inside the run.</param>
    /// <returns>The offset of the first byte after the last newline before <paramref name="at"/>.</returns>
    private static int ImStrbol(ReadOnlySpan<byte> text, int at)
    {
        int mid = Math.Clamp(at, 0, text.Length);
        while (mid > 0 && text[mid - 1] != (byte)'\n')
            mid--;
        return mid;
    }

    /// <summary>
    /// Steps back to the first byte of the previous UTF-8 codepoint; ports canonical
    /// <c>ImTextFindPreviousUtf8Codepoint</c> (, used at
    /// </summary>
    /// <param name="text">UTF-8 byte run.</param>
    /// <param name="at">Byte offset whose predecessor is requested.</param>
    /// <returns>The byte offset of the preceding codepoint, clamped to zero.</returns>
    private static int ImTextFindPreviousUtf8Codepoint(ReadOnlySpan<byte> text, int at)
    {
        int p = Math.Clamp(at, 0, text.Length);
        while (p > 0)
        {
            p--;
            if ((text[p] & 0xC0) != 0x80)
                return p;
        }
        return 0;
    }

    /// <summary>
    /// Truncates a UTF-8 byte range to the closest codepoint boundary at or before a limit; ports
    /// canonical <c>ImTextFindValidUtf8CodepointEnd</c>.
    /// </summary>
    /// <param name="text">UTF-8 byte run.</param>
    /// <param name="textEnd">Exclusive end of the proposed insertion.</param>
    /// <param name="limit">Exclusive end of the permitted space.</param>
    /// <returns>The offset of a valid truncation point.</returns>
    internal static int ImTextFindValidUtf8CodepointEnd(ReadOnlySpan<byte> text, int textEnd, int limit)
    {
        int p = Math.Min(limit, Math.Min(textEnd, text.Length));
        if (p <= 0)
            return 0;

        int prev = ImTextFindPreviousUtf8Codepoint(text, p);
        int at = prev;
        int consumed = ImFont.ImTextCharFromUtf8(text, ref at, out uint prevC);
        if (prevC != 0xFFFD && consumed <= p - prev)
            return p;
        return prev;
    }

    /// <summary>
    /// Encodes one Unicode codepoint as UTF-8, mirroring <c>ImTextCharToUtf8</c>
    ///. Kept local because the encoding is used by the
    /// filter and paste paths only.
    /// </summary>
    /// <param name="outUtf8">Destination span of at least four bytes.</param>
    /// <param name="c">Codepoint to encode.</param>
    /// <returns>The number of bytes written.</returns>
    private static int ImTextCharToUtf8(Span<byte> outUtf8, uint c)
    {
        if (c < 0x80)
        {
            outUtf8[0] = (byte)c;
            return 1;
        }
        if (c < 0x800)
        {
            outUtf8[0] = (byte)(0xC0 | (c >> 6));
            outUtf8[1] = (byte)(0x80 | (c & 0x3F));
            return 2;
        }
        if (c < 0x10000)
        {
            outUtf8[0] = (byte)(0xE0 | (c >> 12));
            outUtf8[1] = (byte)(0x80 | ((c >> 6) & 0x3F));
            outUtf8[2] = (byte)(0x80 | (c & 0x3F));
            return 3;
        }
        if (c < 0x110000)
        {
            outUtf8[0] = (byte)(0xF0 | (c >> 18));
            outUtf8[1] = (byte)(0x80 | ((c >> 12) & 0x3F));
            outUtf8[2] = (byte)(0x80 | ((c >> 6) & 0x3F));
            outUtf8[3] = (byte)(0x80 | (c & 0x3F));
            return 4;
        }
        outUtf8[0] = (byte)'?';
        return 1;
    }

    /// <summary>
    /// Computes the first and last-plus-one visible item indices for a vertically clipped list; ports
    /// canonical <c>CalcClipRectVisibleItemsY</c>.
    /// </summary>
    /// <param name="clipRect">Clip rectangle in screen space.</param>
    /// <param name="pos">Position of the first item.</param>
    /// <param name="itemsHeight">Height of one item.</param>
    /// <param name="outVisibleN0">Receives the first visible item index.</param>
    /// <param name="outVisibleN1">Receives the last visible item index plus one.</param>
    public static void CalcClipRectVisibleItemsY(ImRect clipRect, ImVec2 pos, float itemsHeight, ref int outVisibleN0, ref int outVisibleN1)
    {
        float visibleY0 = clipRect.Min.Y - pos.Y;
        float visibleY1 = clipRect.Max.Y - pos.Y;
        outVisibleN0 = Math.Max(0, (int)(visibleY0 / itemsHeight));
        outVisibleN1 = Math.Max(0, (int)(visibleY1 / itemsHeight) + 1);
    }

    /// <summary>
    /// Returns the active text state when the identifier owns it, or <see langword="null"/>; ports
    /// canonical <c>GetInputTextState</c> (, used at
    /// </summary>
    /// <param name="id">Widget identifier to test.</param>
    /// <returns>The shared text state when it belongs to <paramref name="id"/>, otherwise <see langword="null"/>.</returns>
    private static ImGuiInputTextState? GetInputTextState(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        return id != 0 && g.InputTextState.ID == id ? g.InputTextState : null;
    }

    /// <summary>
    /// Returns the lower-bound index of a value in a sorted offset list; ports the file-scope
    /// <c>ImLowerBound</c> used by the line-index lookup.
    /// </summary>
    /// <param name="offsets">Sorted offset list.</param>
    /// <param name="v">Value to locate.</param>
    /// <returns>The first index whose value is not less than <paramref name="v"/>.</returns>
    private static int ImLowerBound(List<int> offsets, int v)
    {
        int begin = 0;
        int count = offsets.Count;
        while (count > 0)
        {
            int count2 = count >> 1;
            int mid = begin + count2;
            if (offsets[mid] < v)
            {
                begin = mid + 1;
                count -= count2 + 1;
            }
            else
            {
                count = count2;
            }
        }
        return begin;
    }

    /// <summary>
    /// Returns the canonical line-begin offset for a line index, falling back to zero when the index
    /// holds no offsets.
    /// </summary>
    /// <param name="lineIndex">Line index.</param>
    /// <param name="line">Zero-based line number.</param>
    /// <returns>The byte offset at which the line begins.</returns>
    private static int InputTextLineBegin(ImGuiTextIndex lineIndex, int line)
    {
        return line < lineIndex.Offsets.Count ? lineIndex.Offsets[line] : 0;
    }

    /// <summary>
    /// Rebuilds the line-start index over a text buffer; ports canonical
    /// <c>InputTextLineIndexBuild</c>.
    /// </summary>
    /// <param name="flags">Text input flags; <see cref="ImGuiInputTextFlags.WordWrap"/> selects the wrap path.</param>
    /// <param name="lineIndex">Index rebuilt in place.</param>
    /// <param name="buf">Text buffer being indexed.</param>
    /// <param name="wrapWidth">Wrap width in pixels for the word-wrap path.</param>
    /// <param name="maxOutputBufferSize">Maximum number of line starts to append.</param>
    /// <returns>The line count.</returns>
    private static int InputTextLineIndexBuild(ImGuiInputTextFlags flags, ImGuiTextIndex lineIndex, ReadOnlySpan<byte> buf, float wrapWidth, int maxOutputBufferSize)
    {
        ImGuiContext g = GetCurrentContext()!;
        int size = 0;
        if ((flags & ImGuiInputTextFlags.WordWrap) != 0)
        {
            int s = 0;
            while (s < buf.Length)
            {
                if (size++ <= maxOutputBufferSize)
                    lineIndex.Offsets.Add(s);
                s = InputTextCalcWordWrapPosition(g, buf, s, buf.Length, wrapWidth, ImDrawTextFlags.WrapKeepBlanks);
                if (s < buf.Length && buf[s] == (byte)'\n')
                    s++;
            }
        }
        else
        {
            int s = 0;
            while (s < buf.Length)
            {
                if (size++ <= maxOutputBufferSize)
                    lineIndex.Offsets.Add(s);
                int newline = InputTextMemchr(buf, s, (byte)'\n');
                s = newline >= 0 ? newline + 1 : buf.Length;
            }
        }

        if (size == 0)
        {
            lineIndex.Offsets.Add(0);
            size++;
        }
        if (buf.Length > 0 && buf[buf.Length - 1] == (byte)'\n' && size++ <= maxOutputBufferSize)
            lineIndex.Offsets.Add(buf.Length);
        return size;
    }

    /// <summary>
    /// Computes the cursor offset within the rendered text; ports canonical
    /// <c>InputTextLineIndexGetPosOffset</c>.
    /// </summary>
    /// <param name="g">Owning context.</param>
    /// <param name="state">Active text state.</param>
    /// <param name="lineIndex">Line index matching the buffer.</param>
    /// <param name="buf">Text buffer being displayed.</param>
    /// <param name="cursorN">Cursor byte offset.</param>
    /// <returns>The cursor offset relative to the text origin.</returns>
    private static ImVec2 InputTextLineIndexGetPosOffset(ImGuiContext g, ImGuiInputTextState state, ImGuiTextIndex lineIndex, ReadOnlySpan<byte> buf, int cursorN)
    {
        int it = ImLowerBound(lineIndex.Offsets, cursorN);
        if (it > 0)
        {
            int at = it < lineIndex.Offsets.Count ? lineIndex.Offsets[it] : int.MaxValue;
            bool atWrappedBoundary = state.WrapWidth > 0.0f && state.LastMoveDirectionLR == (sbyte)ImGuiDir.Right &&
                cursorN > 0 && cursorN <= buf.Length && buf[cursorN - 1] != (byte)'\n' && buf[cursorN - 1] != 0;
            if (it == lineIndex.Offsets.Count || at != cursorN || atWrappedBoundary)
                it--;
        }

        int lineNo = it;
        int lineStart = InputTextLineBegin(lineIndex, lineNo);
        int count = Math.Clamp(cursorN - lineStart, 0, Math.Max(buf.Length - lineStart, 0));
        return new ImVec2(InputTextBoxWidth(g, buf.Slice(lineStart, count)), (lineNo + 1) * g.FontSize);
    }

    /// <summary>
    /// Reconciles the undo stack after a callback or reload replaced the text buffer; ports canonical
    /// <c>InputTextReconcileUndoState</c>.
    /// </summary>
    /// <param name="state">Active text state; its buffer is assumed to still hold the old text.</param>
    /// <param name="oldBuf">Old text bytes.</param>
    /// <param name="oldLength">Length of the old text.</param>
    /// <param name="newBuf">New text bytes.</param>
    /// <param name="newLength">Length of the new text.</param>
    /// <remarks>
    /// Canonical pushes the shortest single replacement onto the undo stack without touching the
    /// buffer. This port delegates to <see cref="ImStbTextedit.Replace"/>, which records one undo step
    /// for the whole buffer and reuses the editor engine instead of duplicating its private undo-ring
    /// creation; the cursor, selection and edited flags are restored afterwards so the observable
    /// contract is unchanged.
    /// </remarks>
    private static void InputTextReconcileUndoState(ImGuiInputTextState state, byte[] oldBuf, int oldLength, byte[] newBuf, int newLength)
    {
        int shorterLength = Math.Min(oldLength, newLength);
        int firstDiff = 0;
        while (firstDiff < shorterLength && oldBuf[firstDiff] == newBuf[firstDiff])
            firstDiff++;
        if (firstDiff == oldLength && firstDiff == newLength)
            return;

        int cursor = state.Stb.Cursor;
        int selectStart = state.Stb.SelectStart;
        int selectEnd = state.Stb.SelectEnd;
        bool editedBefore = state.EditedBefore;
        bool editedThisFrame = state.EditedThisFrame;

        ImStbTextedit.Replace(state, newBuf.AsSpan(0, newLength));

        state.Stb.Cursor = cursor;
        state.Stb.SelectStart = selectStart;
        state.Stb.SelectEnd = selectEnd;
        state.EditedBefore = editedBefore;
        state.EditedThisFrame = editedThisFrame;
    }

    /// <summary>
    /// Swaps the active font for one whose fallback is the password bullet; ports canonical
    /// <c>PushPasswordFont</c>.
    /// </summary>
    /// <remarks>
    /// The baked lookup tables are swapped into the context backup so every codepoint resolves to the
    /// bullet glyph until <see cref="PopPasswordFont"/> restores them.
    /// </remarks>
    public static void PushPasswordFont()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImFontBaked? fontBaked = g.FontBaked;
        ImFont? font = g.Font;
        if (fontBaked == null || font == null)
            return;

        ImFontBaked backup = g.InputTextPasswordFontBackupBaked ??= new ImFontBaked();
        ImFontGlyph glyph = fontBaked.FindGlyph('*');
        int glyphIndex = fontBaked.IndexLookup.Length > '*' && fontBaked.IndexLookup['*'] >= 0
            ? fontBaked.IndexLookup['*']
            : fontBaked.FallbackGlyphIndex;

        g.InputTextPasswordFontBackupFlags = font.Flags;
        backup.FallbackGlyphIndex = fontBaked.FallbackGlyphIndex;
        backup.FallbackAdvanceX = fontBaked.FallbackAdvanceX;

        (backup.IndexLookup, fontBaked.IndexLookup) = (fontBaked.IndexLookup, backup.IndexLookup);
        (backup.IndexAdvanceX, fontBaked.IndexAdvanceX) = (fontBaked.IndexAdvanceX, backup.IndexAdvanceX);

        font.Flags |= ImFontFlags.NoLoadGlyphs;
        fontBaked.FallbackGlyphIndex = glyphIndex;
        fontBaked.FallbackAdvanceX = glyph.AdvanceX;
    }

    /// <summary>
    /// Restores the font tables backed up by <see cref="PushPasswordFont"/>; ports canonical
    /// <c>PopPasswordFont</c>.
    /// </summary>
    public static void PopPasswordFont()
    {
        ImGuiContext g = GetCurrentContext()!;
        ImFontBaked? fontBaked = g.FontBaked;
        ImFont? font = g.Font;
        ImFontBaked? backup = g.InputTextPasswordFontBackupBaked;
        if (fontBaked == null || font == null || backup == null)
            return;

        font.Flags = g.InputTextPasswordFontBackupFlags;
        fontBaked.FallbackGlyphIndex = backup.FallbackGlyphIndex;
        fontBaked.FallbackAdvanceX = backup.FallbackAdvanceX;
        (fontBaked.IndexLookup, backup.IndexLookup) = (backup.IndexLookup, fontBaked.IndexLookup);
        (fontBaked.IndexAdvanceX, backup.IndexAdvanceX) = (backup.IndexAdvanceX, fontBaked.IndexAdvanceX);
    }

    /// <summary>
    /// Filters or rewrites one input character for the active text field; ports canonical
    /// <c>InputTextFilterCharacter</c>.
    /// </summary>
    /// <param name="g">Owning context.</param>
    /// <param name="state">Active text state; its flags and identity drive the filters.</param>
    /// <param name="pChar">Character rewritten in place; it may be discarded.</param>
    /// <param name="callback">Optional user callback invoked for character filtering.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <param name="inputSourceIsClipboard">Whether the character came from pasted clipboard text.</param>
    /// <returns><see langword="false"/> when the character must be discarded.</returns>
    /// <remarks>
    /// Canonical reads <c>g.PlatformIO.Platform_LocaleDecimalPoint</c> so a host that overrides the
    /// numeric locale can accept that decimal separator; this port reads the same field, which defaults
    /// to the C-locale <c>'.'</c>.
    /// </remarks>
    private static bool InputTextFilterCharacter(ImGuiContext g, ImGuiInputTextState state, ref uint pChar, ImGuiInputTextCallback? callback, IntPtr userData, bool inputSourceIsClipboard = false)
    {
        uint c = pChar;
        ImGuiInputTextFlags flags = state.Flags;
        ImGuiInputTextFlags multilineFlag = (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline;

        bool applyNamedFilters = true;
        if (c < 0x20)
        {
            bool pass = (c == '\n') && (flags & multilineFlag) != 0;
            if (c == '\n' && inputSourceIsClipboard && (flags & multilineFlag) == 0)
            {
                c = pChar = ' ';
                pass = true;
            }
            pass |= (c == '\n') && (flags & multilineFlag) != 0;
            pass |= (c == '\t') && (flags & ImGuiInputTextFlags.AllowTabInput) != 0;
            if (!pass)
                return false;
            applyNamedFilters = false;
        }

        if (!inputSourceIsClipboard)
        {
            if (c == 127)
                return false;
            if (c >= 0xE000 && c <= 0xF8FF)
                return false;
        }

        if (c > 0x10FFFF)
            return false;

        if (applyNamedFilters && (flags & (ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.CharsHexadecimal |
            ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.CharsNoBlank | ImGuiInputTextFlags.CharsScientific |
            (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_LocalizeDecimalPoint)) != 0)
        {
            uint localeDecimalPoint = g.PlatformIO.Platform_LocaleDecimalPoint;
            if ((flags & (ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.CharsScientific |
                (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_LocalizeDecimalPoint)) != 0)
            {
                if (c == '.' || c == ',')
                    c = localeDecimalPoint;
            }

            if ((flags & (ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.CharsScientific | ImGuiInputTextFlags.CharsHexadecimal)) != 0)
            {
                if (c >= 0xFF01 && c <= 0xFF5E)
                    c = c - 0xFF01 + 0x21;
            }

            if ((flags & ImGuiInputTextFlags.CharsDecimal) != 0)
            {
                if (!(c >= '0' && c <= '9') && c != localeDecimalPoint && c != '-' && c != '+' && c != '*' && c != '/')
                    return false;
            }
            if ((flags & ImGuiInputTextFlags.CharsScientific) != 0)
            {
                if (!(c >= '0' && c <= '9') && c != localeDecimalPoint && c != '-' && c != '+' && c != '*' && c != '/' && c != 'e' && c != 'E')
                    return false;
            }
            if ((flags & ImGuiInputTextFlags.CharsHexadecimal) != 0)
            {
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F'))
                    return false;
            }
            if ((flags & ImGuiInputTextFlags.CharsUppercase) != 0)
            {
                if (c >= 'a' && c <= 'z')
                    c -= 32;
            }
            if ((flags & ImGuiInputTextFlags.CharsNoBlank) != 0)
            {
                if (ImCharIsBlankW(c))
                    return false;
            }
            pChar = c;
        }

        if ((flags & ImGuiInputTextFlags.CallbackCharFilter) != 0 && callback != null)
        {
            ImGuiInputTextCallbackData callbackData = new()
            {
                Ctx = g,
                ID = state.ID,
                Flags = flags,
                EventFlag = ImGuiInputTextFlags.CallbackCharFilter,
                EventChar = c,
                EventActivated = g.ActiveId == state.ID && g.ActiveIdIsJustActivated,
                CursorPos = state.Stb.Cursor,
                SelectionStart = state.Stb.SelectStart,
                SelectionEnd = state.Stb.SelectEnd,
                UserData = userData,
            };
            if (callback(callbackData) != 0)
                return false;
            pChar = callbackData.EventChar;
            if (callbackData.EventChar == 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reapplies the retained text to the user buffer on the frame a text field deactivates; ports
    /// canonical <c>InputTextDeactivateHook</c>
    /// (called by the
    /// active-id machinery).
    /// </summary>
    /// <param name="id">Identifier of the deactivating field.</param>
    /// <remarks>
    /// The hook exists so a caller may pass a non-retained buffer: on deactivation the internal text
    /// is copied into <see cref="ImGuiInputTextDeactivatedState.TextA"/> for one frame so that
    /// <c>IsItemDeactivatedAfterEdit</c> observers and the widget's own write-back agree.
    /// </remarks>
    internal static void InputTextDeactivateHook(uint id)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiInputTextState state = g.InputTextState;
        if (id == 0 || state.ID != id || g.ActiveId != id)
            return;
        if (!state.EditedBefore)
            return;

        state.EditedBefore = false;
        g.InputTextDeactivatedState.ID = state.ID;
        g.InputTextDeactivatedState.ElapseFrame = g.FrameCount + 1;
        if ((state.Flags & ImGuiInputTextFlags.ReadOnly) != 0)
        {
            g.InputTextDeactivatedState.TextA.Clear();
        }
        else
        {
            InputTextSetExact(g.InputTextDeactivatedState.TextA, state.TextA, state.TextLen + 1);
        }
    }

    /// <summary>
    /// Returns the gamepad key used to validate the active text field, honoring
    /// <see cref="ImGuiIO.ConfigNavSwapGamepadButtons"/>
    /// </summary>
    /// <param name="g">Owning context.</param>
    /// <returns>The validate key.</returns>
    private static ImGuiKey InputTextNavGamepadActivateKey(ImGuiContext g)
    {
        return g.IO.ConfigNavSwapGamepadButtons ? ImGuiKey.GamepadFaceRight : ImGuiKey.GamepadFaceDown;
    }

    /// <summary>
    /// Returns the gamepad key used to cancel the active text field, honoring
    /// <see cref="ImGuiIO.ConfigNavSwapGamepadButtons"/>
    /// </summary>
    /// <param name="g">Owning context.</param>
    /// <returns>The cancel key.</returns>
    private static ImGuiKey InputTextNavGamepadCancelKey(ImGuiContext g)
    {
        return g.IO.ConfigNavSwapGamepadButtons ? ImGuiKey.GamepadFaceDown : ImGuiKey.GamepadFaceRight;
    }

    #endregion

    #region [SECTION] Widgets: InputText, InputTextMultiline, InputTextWithHint

    /// <summary>
    /// Edits a single-line string buffer. Canvases canonical <c>InputText</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="buf">Zero-terminated UTF-8 buffer; its contents are replaced with the edited text.</param>
    /// <param name="bufSize">Buffer capacity including the terminator.</param>
    /// <param name="flags">Text input flags; the multiline flag is rejected.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    /// <remarks>
    /// The managed-string overload below is the idiomatic entry point for callers that keep their text
    /// in a <see cref="string"/>; this byte-buffer overload preserves canonical capacity semantics.
    /// </remarks>
    public static bool InputText(string label, byte[] buf, int bufSize, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallback? callback = null, IntPtr userData = default)
    {
        ImAssert.Check((flags & (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline) == 0, "Call InputTextMultiline() for multiline fields.");
        return InputTextEx(label, null, buf, bufSize, default, flags, callback, userData);
    }

    /// <summary>
    /// Edits a single-line managed string. Canvases canonical <c>InputText</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="buf">String replaced with the edited text.</param>
    /// <param name="flags">Text input flags; the multiline flag is rejected.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    /// <remarks>
    /// A managed string has no reserved capacity, so this overload mirrors the canonical
    /// <c>imgui_stdlib</c> wrapper: it enables <see cref="ImGuiInputTextFlags.CallbackResize"/> and
    /// grows its temporary buffer as the user types.
    /// </remarks>
    public static bool InputText(string label, ref string buf, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallback? callback = null, IntPtr userData = default)
    {
        ImAssert.Check((flags & (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline) == 0, "Call InputTextMultiline() for multiline fields.");
        return InputTextString(label, null, ref buf, default, flags, callback, userData, isMultiline: false);
    }

    /// <summary>
    /// Edits a multiline byte buffer. Canvases canonical <c>InputTextMultiline</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="buf">Zero-terminated UTF-8 buffer; its contents are replaced with the edited text.</param>
    /// <param name="bufSize">Buffer capacity including the terminator.</param>
    /// <param name="size">Frame size; a default size selects eight lines of height.</param>
    /// <param name="flags">Text input flags; the multiline flag is added automatically.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    public static bool InputTextMultiline(string label, byte[] buf, int bufSize, ImVec2 size = default, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallback? callback = null, IntPtr userData = default)
    {
        ImGuiInputTextFlags multiline = (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline;
        return InputTextEx(label, null, buf, bufSize, size, flags | multiline, callback, userData);
    }

    /// <summary>
    /// Edits a multiline managed string. Canvases canonical <c>InputTextMultiline</c>
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="buf">String replaced with the edited text.</param>
    /// <param name="size">Frame size; a default size selects eight lines of height.</param>
    /// <param name="flags">Text input flags; the multiline flag is added automatically.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    public static bool InputTextMultiline(string label, ref string buf, ImVec2 size = default, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallback? callback = null, IntPtr userData = default)
    {
        return InputTextString(label, null, ref buf, size, flags, callback, userData, isMultiline: true);
    }

    /// <summary>
    /// Edits a single-line buffer while displaying a hint when it is empty. Canvases canonical
    /// <c>InputTextWithHint</c>.
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="hint">Placeholder shown while the buffer is empty.</param>
    /// <param name="buf">Zero-terminated UTF-8 buffer; its contents are replaced with the edited text.</param>
    /// <param name="bufSize">Buffer capacity including the terminator.</param>
    /// <param name="flags">Text input flags; the multiline flag is rejected.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    public static bool InputTextWithHint(string label, string hint, byte[] buf, int bufSize, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallback? callback = null, IntPtr userData = default)
    {
        ImAssert.Check((flags & (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline) == 0, "Call InputTextMultiline() or InputTextEx() for multiline fields with a hint.");
        return InputTextEx(label, hint, buf, bufSize, default, flags, callback, userData);
    }

    /// <summary>
    /// Edits a single-line managed string while displaying a hint when it is empty. Canvases canonical
    /// <c>InputTextWithHint</c>.
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="hint">Placeholder shown while the buffer is empty.</param>
    /// <param name="buf">String replaced with the edited text.</param>
    /// <param name="flags">Text input flags; the multiline flag is rejected.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    public static bool InputTextWithHint(string label, string hint, ref string buf, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallback? callback = null, IntPtr userData = default)
    {
        ImAssert.Check((flags & (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline) == 0, "Call InputTextMultiline() or InputTextEx() for multiline fields with a hint.");
        return InputTextString(label, hint, ref buf, default, flags, callback, userData, isMultiline: false);
    }

    /// <summary>
    /// Edits a managed string through a growable temporary byte buffer, mirroring the canonical
    /// <c>imgui_stdlib</c> wrapper. It composes the caller callback with an internal resize callback so
    /// text may grow beyond the initial allocation.
    /// </summary>
    /// <param name="label">Widget label.</param>
    /// <param name="hint">Optional placeholder text.</param>
    /// <param name="buf">String replaced with the edited text.</param>
    /// <param name="sizeArg">Frame size for the multiline form.</param>
    /// <param name="flags">Text input flags.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="userData">Opaque user data forwarded to the callback.</param>
    /// <param name="isMultiline">Whether to edit in multiline mode.</param>
    /// <returns>The widget's reported change.</returns>
    private static bool InputTextString(string label, string? hint, ref string buf, ImVec2 sizeArg, ImGuiInputTextFlags flags, ImGuiInputTextCallback? callback, IntPtr userData, bool isMultiline)
    {
        byte[] buffer = new byte[System.Text.Encoding.UTF8.GetByteCount(buf) + 1];
        InputTextEncodeInto(buffer, buf);

        int capacity = buffer.Length;
        ImGuiInputTextCallback resizeCallback = data =>
        {
            int needed = data.BufTextLen + 1;
            if (needed > capacity)
            {
                int newCapacity = Math.Max(needed, capacity * 2);
                Array.Resize(ref buffer, newCapacity);
                capacity = newCapacity;
                data.Buf = buffer;
                data.BufSize = newCapacity;
            }
            return 0;
        };
        ImGuiInputTextCallback combined = data =>
        {
            if (data.EventFlag == ImGuiInputTextFlags.CallbackResize)
                resizeCallback(data);
            return callback != null ? callback(data) : 0;
        };

        ImGuiInputTextFlags effectiveFlags = flags | ImGuiInputTextFlags.CallbackResize;
        if (isMultiline)
            effectiveFlags |= (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline;

        bool changed = InputTextEx(label, hint, buffer, buffer.Length, sizeArg, effectiveFlags, combined, userData);
        buf = InputTextDecodeBuffer(buffer);
        return changed;
    }

    /// <summary>
    /// Edits a UTF-8 text buffer with the full canonical behavior set. Canvases canonical
    /// <c>InputTextEx</c>.
    /// </summary>
    /// <param name="label">Widget label; it supplies both the identifier and the displayed text.</param>
    /// <param name="hint">Optional placeholder shown while the buffer is empty.</param>
    /// <param name="buf">Zero-terminated UTF-8 user buffer.</param>
    /// <param name="bufSize">Buffer capacity including the terminator.</param>
    /// <param name="sizeArg">Frame size; a default size selects the label or eight-line default.</param>
    /// <param name="flags">Text input flags.</param>
    /// <param name="callback">Optional user text callback.</param>
    /// <param name="callbackUserData">Opaque user data forwarded to the callback.</param>
    /// <returns>
    /// With <see cref="ImGuiInputTextFlags.EnterReturnsTrue"/>, whether Return was pressed; otherwise
    /// whether the text changed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// While the field is active the port retains a private UTF-8 copy in
    /// <see cref="ImGuiInputTextState.TextA"/> and applies it back to <paramref name="buf"/>; changing
    /// <paramref name="buf"/> while active therefore has no effect unless the reload request API is
    /// used. The embedded editor state machine lives in <see cref="ImStbTextedit"/>.
    /// </para>
    /// <para>
    /// The rendered text is captured through the logging helpers when logging is active. The platform
    /// IME text-input request is published to
    /// <see cref="ImGuiContext.PlatformImeData"/> and forwarded by <see cref="ImGui.EndFrame"/>.
    /// </para>
    /// </remarks>
    internal static bool InputTextEx(string label, string? hint, byte[] buf, int bufSize, ImVec2 sizeArg, ImGuiInputTextFlags flags, ImGuiInputTextCallback? callback, IntPtr callbackUserData)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImAssert.Check(bufSize >= 0, "InputTextEx requires non-negative capacity.");
        ImGuiInputTextFlags flagMultiline = (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_Multiline;
        ImGuiInputTextFlags flagTempInput = (ImGuiInputTextFlags)(int)ImGuiInputTextFlagsPrivate.ImGuiInputTextFlags_TempInput;
        ImGuiItemFlags flagInputable = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_Inputable;
        ImGuiItemFlags flagReadOnly = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_ReadOnly;
        ImGuiItemFlags flagNoNavDisableMouseHover = (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoNavDisableMouseHover;

        ImAssert.Check(!((flags & ImGuiInputTextFlags.CallbackHistory) != 0 && (flags & flagMultiline) != 0), "CallbackHistory and Multiline cannot be combined.");
        ImAssert.Check(!((flags & ImGuiInputTextFlags.CallbackCompletion) != 0 && (flags & ImGuiInputTextFlags.AllowTabInput) != 0), "CallbackCompletion and AllowTabInput cannot be combined.");
        ImAssert.Check(!((flags & ImGuiInputTextFlags.ElideLeft) != 0 && (flags & flagMultiline) != 0), "ElideLeft does not work with multiline fields.");
        ImAssert.Check((flags & ImGuiInputTextFlags.WordWrap) == 0 || (flags & ImGuiInputTextFlags.Password) == 0, "WordWrap does not work with password fields.");
        ImAssert.Check((flags & ImGuiInputTextFlags.WordWrap) == 0 || (flags & flagMultiline) != 0, "WordWrap is multiline-only.");

        ImGuiContext g = GetCurrentContext()!;
        ImGuiIO io = g.IO;
        ImGuiStyle style = g.Style;

        const bool renderSelectionWhenInactive = false;
        bool isMultiline = (flags & flagMultiline) != 0;

        if (isMultiline)
            BeginGroup();
        uint id = GetID(label);
        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(label, true);
        ImVec2 frameSize = CalcItemSize(sizeArg, CalcItemWidth(), (isMultiline ? g.FontSize * 8.0f : labelSize.Y) + style.FramePadding.Y * 2.0f);
        ImVec2 totalSize = new(frameSize.X + (labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f), frameSize.Y);

        ImRect frameBb = new(window.DC.CursorPos, window.DC.CursorPos + frameSize);
        ImRect totalBb = new(frameBb.Min, frameBb.Min + totalSize);

        ImGuiWindow drawWindow = window;
        ImVec2 innerSize = frameSize;
        ImGuiItemFlags itemDataBackupItemFlags;
        ImGuiItemStatusFlags itemDataBackupStatusFlags;
        if (isMultiline)
        {
            ImVec2 backupPos = window.DC.CursorPos;
            ItemSize(totalBb, style.FramePadding.Y);
            bool noClip = g.InputTextDeactivatedState.ID == id || g.ActiveId == id || id == g.NavActivateId;
            if (!ItemAdd(totalBb, id, frameBb, flagInputable) && !noClip)
            {
                EndGroup();
                return false;
            }
            itemDataBackupItemFlags = g.LastItemData.ItemFlags;
            itemDataBackupStatusFlags = g.LastItemData.StatusFlags;
            window.DC.CursorPos = backupPos;

            // Tabbing must be able to cycle through fields that accept Tab input.
            if (g.NavActivateId == id && (g.NavActivateFlags & ImGuiActivateFlags.FromTabbing) != 0 &&
                (g.NavActivateFlags & ImGuiActivateFlags.FromFocusApi) == 0 && (flags & ImGuiInputTextFlags.AllowTabInput) != 0)
                g.NavActivateId = 0;

            // Prevent BeginChild() from reactivating the field while it is already active.
            uint backupActivateId = g.NavActivateId;
            if (g.ActiveId == id)
                g.NavActivateId = 0;

            PushStyleColor(ImGuiCol.ChildBg, style.Colors[(int)ImGuiCol.ChildBg]);
            PushStyleVar(ImGuiStyleVar.ChildRounding, style.FrameRounding);
            PushStyleVar(ImGuiStyleVar.ChildBorderSize, style.FrameBorderSize);
            PushStyleVar(ImGuiStyleVar.WindowPadding, new ImVec2(0.0f, 0.0f));
            bool childVisible = BeginChildEx(label, id, frameBb.GetSize(), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoMove);
            g.NavActivateId = backupActivateId;
            PopStyleVar(3);
            PopStyleColor();
            if (!childVisible && !noClip)
            {
                EndChild();
                EndGroup();
                return false;
            }

            drawWindow = g.CurrentWindow!;
            drawWindow.DC.NavLayersActiveMaskNext |= (short)(1 << (int)drawWindow.DC.NavLayerCurrent);
            drawWindow.DC.CursorPos += style.FramePadding;
            innerSize.X -= drawWindow.ScrollbarSizes.X;

            g.LastItemData.ID = id;
            g.LastItemData.ItemFlags = itemDataBackupItemFlags;
            g.LastItemData.StatusFlags = itemDataBackupStatusFlags;
        }
        else
        {
            ItemSize(totalBb, style.FramePadding.Y);
            if ((flags & flagTempInput) == 0)
            {
                if (!ItemAdd(totalBb, id, frameBb, flagInputable))
                    return false;
            }
            itemDataBackupItemFlags = g.LastItemData.ItemFlags;
            itemDataBackupStatusFlags = g.LastItemData.StatusFlags;
        }

        bool hovered = ItemHoverable(frameBb, id, g.LastItemData.ItemFlags | flagNoNavDisableMouseHover);
        if (hovered)
            SetMouseCursor(ImGuiMouseCursor.TextInput);
        if (hovered && g.NavHighlightItemUnderNav)
            hovered = false;

        ImGuiInputTextState? state = GetInputTextState(id);
        if ((g.LastItemData.ItemFlags & flagReadOnly) != 0)
            flags |= ImGuiInputTextFlags.ReadOnly;
        bool isReadonly = (flags & ImGuiInputTextFlags.ReadOnly) != 0;
        bool isPassword = (flags & ImGuiInputTextFlags.Password) != 0;
        bool isUndoable = (flags & ImGuiInputTextFlags.NoUndoRedo) == 0;
        bool isResizable = (flags & ImGuiInputTextFlags.CallbackResize) != 0;
        bool isMixed = (g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0;
        ImAssert.Check(!isResizable || callback != null, "A callback must be supplied with ImGuiInputTextFlags.CallbackResize.");

        bool isWordwrap = (flags & ImGuiInputTextFlags.WordWrap) != 0;
        float wrapWidth = 0.0f;
        if (isWordwrap)
        {
            ImVec2 contentAvail = GetContentRegionAvail();
            wrapWidth = MathF.Max(1.0f, contentAvail.X + (drawWindow.ScrollbarY ? 0.0f : -style.ScrollbarSize));
        }

        bool userClicked = hovered && io.MouseClicked[0];
        bool inputRequestedByNav = g.ActiveId != id && g.NavActivateId == id;
        bool inputRequestedByReactivate = g.InputTextReactivateId == id;
        bool inputRequestedByUser = userClicked || (g.ActiveId == 0 && (flags & flagTempInput) != 0 && g.InputTextDeactivatedState.ID != id);
        uint scrollbarId = isMultiline && state != null ? GetWindowScrollbarID(drawWindow, ImGuiAxis.Y) : 0;
        bool userScrollFinish = isMultiline && state != null && g.ActiveId == 0 && g.ActiveIdPreviousFrame == scrollbarId;
        bool userScrollActive = isMultiline && state != null && g.ActiveId == scrollbarId;
        bool clearActiveId = false;
        bool selectAll = false;

        float scrollY = isMultiline ? drawWindow.Scroll.Y : float.MaxValue;

        bool initReloadFromUserBuf = state != null && state.WantReloadUserBuf;
        bool initChangedSpecsMultiline = state != null && state.Stb.SingleLine != (isMultiline ? (byte)0 : (byte)1);
        bool initChangedSpecsReadonly = state != null && ((state.Flags ^ flags) & ImGuiInputTextFlags.ReadOnly) != 0;
        bool initMakeActive = inputRequestedByUser || inputRequestedByNav || inputRequestedByReactivate || userScrollFinish;

        if (initReloadFromUserBuf)
        {
            int newLen = InputTextStrLen(buf, bufSize);
            ImAssert.Check(newLen + 1 <= bufSize, "The input buffer must be properly zero-terminated.");
            state!.WantReloadUserBuf = false;
            InputTextReconcileUndoState(state, InputTextByteArray(state.TextA, state.TextLen), state.TextLen, buf, newLen);
            InputTextEnsureBuffer(state.TextA, bufSize + 1);
            state.TextLen = newLen;
            InputTextCopy(state.TextA, buf, state.TextLen + 1);
            state.Stb.SelectStart = state.ReloadSelectionStart;
            state.Stb.Cursor = state.Stb.SelectEnd = state.ReloadSelectionEnd;
        }
        else if ((initMakeActive && g.ActiveId != id) || initChangedSpecsMultiline || initChangedSpecsReadonly)
        {
            state = g.InputTextState;
            state.CursorAnimReset();

            // Give the deactivating field a chance to write to its buffer on the same frame.
            if (state.ID != id && state.ID == g.ActiveId && initMakeActive && g.ActiveId != id)
                InputTextDeactivateHook(state.ID);

            int bufLen = InputTextStrLen(buf, bufSize);
            ImAssert.Check((bufLen + 1 <= bufSize) || (bufLen == 0 && bufSize == 0), "The input buffer must be properly zero-terminated.");
            if (!userScrollFinish)
            {
                InputTextEnsureBuffer(state.TextToRevertTo, bufLen + 1);
                InputTextCopy(state.TextToRevertTo, buf, bufLen + 1);
            }

            bool recycleState = state.ID == id && !initChangedSpecsMultiline;
            if (recycleState && !initChangedSpecsReadonly && (state.TextLen != bufLen || !InputTextComparePrefix(state.TextA, buf, bufLen)))
                recycleState = false;

            state.ID = id;
            state.TextLen = bufLen;
            state.EditedBefore = false;
            if (!isReadonly)
            {
                InputTextEnsureBuffer(state.TextA, bufSize + 1);
                InputTextCopy(state.TextA, buf, state.TextLen + 1);
            }

            state.Scroll = new ImVec2(0.0f, 0.0f);
            if ((flags & ImGuiInputTextFlags.ElideLeft) != 0)
            {
                float textWidth = InputTextBoxWidth(g, buf.AsSpan(0, bufLen));
                state.Scroll = new ImVec2(state.Scroll.X + MathF.Max(0.0f, textWidth - frameSize.X + style.FramePadding.X * 2.0f), state.Scroll.Y);
            }

            if (!recycleState)
                ImStbTextedit.InitializeState(state.Stb, !isMultiline);

            if (!isMultiline)
            {
                if ((flags & ImGuiInputTextFlags.AutoSelectAll) != 0 || isMixed)
                    selectAll = true;
                if (inputRequestedByNav && (!recycleState || (g.NavActivateFlags & ImGuiActivateFlags.TryToPreserveState) == 0))
                    selectAll = true;
                if (userClicked && io.KeyCtrl)
                    selectAll = true;
            }

            if ((flags & ImGuiInputTextFlags.AlwaysOverwrite) != 0)
                state.Stb.InsertMode = 1;
        }

        bool isOsx = io.ConfigMacOSXBehaviors;
        if (initMakeActive && g.ActiveId != id)
        {
            ImAssert.Check(state != null && state.ID == id, "The text state must be initialized before activation.");
            SetActiveID(id, window);
            SetFocusID(id, window);
            FocusWindow(window);
            if (inputRequestedByNav)
                SetNavCursorVisibleAfterMove();
        }

        if (g.ActiveId == id)
        {
            SetKeyOwner(ImGuiKey.LeftArrow, id);
            SetKeyOwner(ImGuiKey.RightArrow, id);
            SetKeyOwner(ImGuiKey.Delete, id);
            SetKeyOwner(ImGuiKey.Backspace, id);
            SetKeyOwner(ImGuiKey.Home, id);
            SetKeyOwner(ImGuiKey.End, id);
            if (userClicked)
                SetKeyOwner(ImGuiKey.MouseLeft, id);
            g.ActiveIdUsingNavDirMask |= (1u << (int)ImGuiDir.Left) | (1u << (int)ImGuiDir.Right);
            if (isMultiline || (flags & ImGuiInputTextFlags.CallbackHistory) != 0)
            {
                g.ActiveIdUsingNavDirMask |= (1u << (int)ImGuiDir.Up) | (1u << (int)ImGuiDir.Down);
                SetKeyOwner(ImGuiKey.UpArrow, id);
                SetKeyOwner(ImGuiKey.DownArrow, id);
            }
            if (isMultiline)
            {
                SetKeyOwner(ImGuiKey.PageUp, id);
                SetKeyOwner(ImGuiKey.PageDown, id);
            }
            if (isOsx)
                SetKeyOwner(ImGuiKey.ImGuiMod_Alt, id);

            if (isMultiline && state != null)
                state.Scroll = new ImVec2(state.Scroll.X, drawWindow.Scroll.Y);
            if (isReadonly && state != null)
                state.TextLen = InputTextStrLen(buf, bufSize);
            state?.CursorClamp();
        }

        // An active identifier with no matching state can happen when another widget owned it.
        if (g.ActiveId == id && state == null)
            ClearActiveID();
        if (g.ActiveId == id && io.MouseClicked[0] && !initMakeActive)
            clearActiveId = true;

        bool renderCursor = g.ActiveId == id || (state != null && userScrollActive);
        bool renderSelection = state != null && (state.HasSelection() || selectAll) && (renderSelectionWhenInactive || renderCursor);
        bool valueChanged = false;
        bool validated = false;

        bool bufDisplayFromState = (renderCursor || renderSelection || g.ActiveId == id) && !isReadonly && state != null;
        bool isDisplayingHint = hint != null && (bufDisplayFromState ? (state!.TextA.Count > 0 ? state.TextA[0] : 0) : buf[0]) == 0 && !isMixed;

        if (isPassword && !isDisplayingHint)
            PushPasswordFont();

        if (state != null && state.ID == id)
        {
            state.Flags = flags;
            state.EditedThisFrame = state.ValidatedThisFrame = false;
            if (isWordwrap && state.WrapWidth != wrapWidth)
            {
                state.CursorCenterY = true;
                state.WrapWidth = wrapWidth;
                renderCursor = true;
            }
        }

        if (g.ActiveId == id)
        {
            ImAssert.Check(state != null, "The active text field must own its state.");
            state!.EditedThisFrame = false;
            state.BufCapacity = bufSize;
            state.WrapWidth = wrapWidth;
            g.ActiveIdAllowOverlap = !io.MouseDown[0];

            float mouseX = (io.MousePos.X - frameBb.Min.X - style.FramePadding.X) + state.Scroll.X;
            float mouseY = isMultiline ? io.MousePos.Y - drawWindow.DC.CursorPos.Y : g.FontSize * 0.5f;

            if (selectAll)
            {
                state.SelectAll();
                state.SelectedAllMouseLock = true;
            }
            else if (hovered && io.MouseClickedCount[0] >= 2 && !io.KeyShift)
            {
                ImStbTextedit.Click(state, mouseX, mouseY);
                int multiclickCount = io.MouseClickedCount[0] - 2;
                if (multiclickCount % 2 == 0)
                {
                    // Double-click selects a word; canonical uses the Mac advance unconditionally.
                    bool isBol = state.Stb.Cursor == 0 || (state.Stb.Cursor - 1 < state.TextA.Count && state.TextA[state.Stb.Cursor - 1] == (byte)'\n');
                    if (state.Stb.SelectStart != state.Stb.SelectEnd || !isBol)
                        state.OnKeyPressed(ImStbTextedit.KeyWordLeft);
                    if (state.Stb.SelectStart == state.Stb.SelectEnd)
                        ImStbTextedit.PrepSelectionAtCursor(state.Stb);
                    state.OnKeyPressed(ImStbTextedit.KeyWordRight | ImStbTextedit.KeyShift);
                    ImStbTextedit.Clamp(state, state.Stb);
                }
                else
                {
                    // Triple-click selects the line.
                    bool isEol = (state.Stb.Cursor < state.TextA.Count ? state.TextA[state.Stb.Cursor] : (byte)0) == (byte)'\n';
                    state.WrapWidth = 0.0f;
                    state.OnKeyPressed(ImStbTextedit.KeyLineStart);
                    state.OnKeyPressed(ImStbTextedit.KeyLineEnd | ImStbTextedit.KeyShift);
                    state.OnKeyPressed(ImStbTextedit.KeyRight | ImStbTextedit.KeyShift);
                    state.WrapWidth = wrapWidth;
                    if (!isEol && isMultiline)
                    {
                        (state.Stb.SelectStart, state.Stb.SelectEnd) = (state.Stb.SelectEnd, state.Stb.SelectStart);
                        state.Stb.Cursor = state.Stb.SelectEnd;
                    }
                    state.CursorFollow = false;
                }
                state.CursorAnimReset();
            }
            else if (io.MouseClicked[0] && !state.SelectedAllMouseLock)
            {
                if (hovered)
                {
                    if (io.KeyShift)
                        ImStbTextedit.Drag(state, mouseX, mouseY);
                    else
                        ImStbTextedit.Click(state, mouseX, mouseY);
                    state.CursorAnimReset();
                }
            }
            else if (io.MouseDown[0] && !state.SelectedAllMouseLock && (io.MouseDelta.X != 0.0f || io.MouseDelta.Y != 0.0f))
            {
                ImStbTextedit.Drag(state, mouseX, mouseY);
                state.CursorAnimReset();
                state.CursorFollow = true;
            }
            if (state.SelectedAllMouseLock && !io.MouseDown[0])
                state.SelectedAllMouseLock = false;

            if ((flags & ImGuiInputTextFlags.AllowTabInput) != 0 && !isReadonly)
            {
                if (Shortcut(ImGuiKey.Tab, ImGuiInputFlags.Repeat, id))
                {
                    uint c = '\t';
                    if (InputTextFilterCharacter(g, state, ref c, callback, callbackUserData))
                        state.OnCharPressed(c);
                }
            }

            // Regular text input is processed before Return because some IMEs emit a Return.
            bool ignoreCharInputs = (io.KeyCtrl && !io.KeyAlt) || (isOsx && io.KeyCtrl);
            if (io.InputQueueCharacters.Count > 0)
            {
                if (!ignoreCharInputs && !isReadonly && !inputRequestedByNav)
                {
                    for (int n = 0; n < io.InputQueueCharacters.Count; n++)
                    {
                        uint c = io.InputQueueCharacters[n];
                        if (c == '\t')
                            continue;
                        if (InputTextFilterCharacter(g, state, ref c, callback, callbackUserData))
                            state.OnCharPressed(c);
                    }
                }
                io.InputQueueCharacters.Clear();
            }
        }

        bool revertEdit = false;
        if (g.ActiveId == id && !g.ActiveIdIsJustActivated && !clearActiveId)
        {
            ImAssert.Check(state != null, "The active text field must own its state.");
            int rowCountPerPage = Math.Max((int)((innerSize.Y - style.FramePadding.Y) / g.FontSize), 1);
            state!.Stb.RowCountPerPage = rowCountPerPage;

            int kMask = io.KeyShift ? ImStbTextedit.KeyShift : 0;
            bool isWordmoveKeyDown = isOsx ? io.KeyAlt : io.KeyCtrl;
            bool isStartendKeyDown = isOsx && io.KeyCtrl && !io.KeySuper && !io.KeyAlt;

            ImGuiInputFlags fRepeat = ImGuiInputFlags.Repeat;
            bool isCut = (Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.X, fRepeat, id) || Shortcut(ImGuiKey.ImGuiMod_Shift | ImGuiKey.Delete, fRepeat, id)) &&
                !isReadonly && !isPassword && (!isMultiline || state.HasSelection());
            bool isCopy = (Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.C, 0, id) || Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.Insert, 0, id)) &&
                !isPassword && (!isMultiline || state.HasSelection());
            bool isPaste = (Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.V, fRepeat, id) || Shortcut(ImGuiKey.ImGuiMod_Shift | ImGuiKey.Insert, fRepeat, id)) && !isReadonly;
            bool isUndo = Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.Z, fRepeat, id) && !isReadonly && isUndoable;
            bool isRedo = (Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.Y, fRepeat, id) || Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.ImGuiMod_Shift | ImGuiKey.Z, fRepeat, id)) && !isReadonly && isUndoable;
            bool isSelectAll = Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.A, 0, id);

            bool navGamepadActive = (io.ConfigFlags & ImGuiConfigFlags.NavEnableGamepad) != 0 && (io.BackendFlags & ImGuiBackendFlags.HasGamepad) != 0;
            bool isEnter = Shortcut(ImGuiKey.Enter, fRepeat, id) || Shortcut(ImGuiKey.KeypadEnter, fRepeat, id);
            bool isCtrlEnter = Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.Enter, fRepeat, id) || Shortcut(ImGuiKey.ImGuiMod_Ctrl | ImGuiKey.KeypadEnter, fRepeat, id);
            bool isShiftEnter = Shortcut(ImGuiKey.ImGuiMod_Shift | ImGuiKey.Enter, fRepeat, id) || Shortcut(ImGuiKey.ImGuiMod_Shift | ImGuiKey.KeypadEnter, fRepeat, id);
            bool isGamepadValidate = navGamepadActive && IsKeyPressed(InputTextNavGamepadActivateKey(g), false);
            bool isCancel = Shortcut(ImGuiKey.Escape, fRepeat, id) || (navGamepadActive && Shortcut(InputTextNavGamepadCancelKey(g), fRepeat, id));

            if (IsKeyPressed(ImGuiKey.LeftArrow))
                state.OnKeyPressed((isStartendKeyDown ? ImStbTextedit.KeyLineStart : isWordmoveKeyDown ? ImStbTextedit.KeyWordLeft : ImStbTextedit.KeyLeft) | kMask);
            else if (IsKeyPressed(ImGuiKey.RightArrow))
                state.OnKeyPressed((isStartendKeyDown ? ImStbTextedit.KeyLineEnd : isWordmoveKeyDown ? ImStbTextedit.KeyWordRight : ImStbTextedit.KeyRight) | kMask);
            else if (IsKeyPressed(ImGuiKey.UpArrow) && isMultiline)
            {
                if (io.KeyCtrl) SetScrollY(drawWindow, MathF.Max(drawWindow.Scroll.Y - g.FontSize, 0.0f));
                else state.OnKeyPressed((isStartendKeyDown ? ImStbTextedit.KeyTextStart : ImStbTextedit.KeyUp) | kMask);
            }
            else if (IsKeyPressed(ImGuiKey.DownArrow) && isMultiline)
            {
                if (io.KeyCtrl) SetScrollY(drawWindow, MathF.Min(drawWindow.Scroll.Y + g.FontSize, GetScrollMaxY()));
                else state.OnKeyPressed((isStartendKeyDown ? ImStbTextedit.KeyTextEnd : ImStbTextedit.KeyDown) | kMask);
            }
            else if (IsKeyPressed(ImGuiKey.PageUp) && isMultiline)
            {
                state.OnKeyPressed(ImStbTextedit.KeyPageUp | kMask);
                scrollY -= rowCountPerPage * g.FontSize;
            }
            else if (IsKeyPressed(ImGuiKey.PageDown) && isMultiline)
            {
                state.OnKeyPressed(ImStbTextedit.KeyPageDown | kMask);
                scrollY += rowCountPerPage * g.FontSize;
            }
            else if (IsKeyPressed(ImGuiKey.Home))
                state.OnKeyPressed(io.KeyCtrl ? ImStbTextedit.KeyTextStart | kMask : ImStbTextedit.KeyLineStart | kMask);
            else if (IsKeyPressed(ImGuiKey.End))
                state.OnKeyPressed(io.KeyCtrl ? ImStbTextedit.KeyTextEnd | kMask : ImStbTextedit.KeyLineEnd | kMask);
            else if (IsKeyPressed(ImGuiKey.Delete) && !isReadonly && !isCut)
            {
                if (!state.HasSelection() && isWordmoveKeyDown)
                    state.OnKeyPressed(ImStbTextedit.KeyWordRight | ImStbTextedit.KeyShift);
                state.OnKeyPressed(ImStbTextedit.KeyDelete | kMask);
            }
            else if (IsKeyPressed(ImGuiKey.Backspace) && !isReadonly)
            {
                if (!state.HasSelection())
                {
                    if (isWordmoveKeyDown)
                        state.OnKeyPressed(ImStbTextedit.KeyWordLeft | ImStbTextedit.KeyShift);
                    else if (isOsx && io.KeyCtrl && !io.KeyAlt && !io.KeySuper)
                        state.OnKeyPressed(ImStbTextedit.KeyLineStart | ImStbTextedit.KeyShift);
                }
                state.OnKeyPressed(ImStbTextedit.KeyBackspace | kMask);
            }
            else if (isEnter || isCtrlEnter || isShiftEnter || isGamepadValidate)
            {
                bool ctrlEnterForNewLine = (flags & ImGuiInputTextFlags.CtrlEnterForNewLine) != 0;
                bool isNewLine = isMultiline && !isGamepadValidate &&
                    (isShiftEnter || (isEnter && !ctrlEnterForNewLine) || (isCtrlEnter && ctrlEnterForNewLine));
                if (!isNewLine)
                {
                    validated = clearActiveId = true;
                    if (io.ConfigInputTextEnterKeepActive && !isMultiline && !isCtrlEnter && !isShiftEnter)
                    {
                        state.SelectAll();
                        g.InputTextReactivateId = id;
                    }
                }
                else if (!isReadonly)
                {
                    uint c = '\n';
                    if (InputTextFilterCharacter(g, state, ref c, callback, callbackUserData))
                        state.OnCharPressed(c);
                }
            }
            else if (isCancel)
            {
                if ((flags & ImGuiInputTextFlags.EscapeClearsAll) != 0)
                {
                    if (state.TextA.Count > 0 && state.TextA[0] != 0)
                    {
                        revertEdit = true;
                    }
                    else
                    {
                        renderCursor = renderSelection = false;
                        clearActiveId = true;
                    }
                }
                else
                {
                    clearActiveId = revertEdit = true;
                    renderCursor = renderSelection = false;
                }
            }
            else if (isUndo || isRedo)
            {
                state.OnKeyPressed(isUndo ? ImStbTextedit.KeyUndo : ImStbTextedit.KeyRedo);
                state.ClearSelection();
            }
            else if (isSelectAll)
            {
                state.SelectAll();
                state.CursorFollow = true;
            }
            else if (isCut || isCopy)
            {
                // SetClipboardText() prefers the platform handler and falls back to the IO callback.
                if (g.IO.SetClipboardTextFn != null || g.PlatformIO.Platform_SetClipboardTextFn != null)
                {
                    int ib = state.HasSelection() ? Math.Min(state.Stb.SelectStart, state.Stb.SelectEnd) : 0;
                    int ie = state.HasSelection() ? Math.Max(state.Stb.SelectStart, state.Stb.SelectEnd) : state.TextLen;
                    ReadOnlySpan<byte> clipSource = isReadonly ? buf : System.Runtime.InteropServices.CollectionsMarshal.AsSpan(state.TextA);
                    int count = Math.Clamp(ie - ib, 0, Math.Max(clipSource.Length - ib, 0));
                    SetClipboardText(InputTextDecodeUtf8(clipSource.Slice(ib, count)));
                }
                if (isCut)
                {
                    if (!state.HasSelection())
                        state.SelectAll();
                    state.CursorFollow = true;
                    ImStbTextedit.Cut(state);
                }
            }
            else if (isPaste)
            {
                string clipboard = GetClipboardText();
                // Canonical gates on the resolved clipboard text, not on the obsolete IO getter
                if (clipboard.Length > 0)
                {
                    byte[] clipBytes = System.Text.Encoding.UTF8.GetBytes(clipboard);
                    List<byte> clipboardFiltered = new(clipBytes.Length + 1);
                    Span<byte> encoded = stackalloc byte[4];
                    int s = 0;
                    while (s < clipBytes.Length)
                    {
                        uint c = InputTextDecodeAt(clipBytes, s, out int consumed);
                        s += consumed;
                        if (!InputTextFilterCharacter(g, state, ref c, callback, callbackUserData, true))
                            continue;
                        int outLen = ImTextCharToUtf8(encoded, c);
                        for (int i = 0; i < outLen; i++)
                            clipboardFiltered.Add(encoded[i]);
                    }
                    if (clipboardFiltered.Count > 0)
                    {
                        ImStbTextedit.Paste(state, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(clipboardFiltered));
                        state.CursorFollow = true;
                    }
                }
            }

            renderSelection |= state.HasSelection() && (renderSelectionWhenInactive || renderCursor);
        }

        byte[]? applyNewText = null;
        int applyNewTextLength = 0;
        if (g.ActiveId == id)
        {
            ImAssert.Check(state != null, "The active text field must own its state.");
            if (revertEdit && !isReadonly)
            {
                if ((flags & ImGuiInputTextFlags.EscapeClearsAll) != 0)
                {
                    applyNewText = Array.Empty<byte>();
                    applyNewTextLength = 0;
                    valueChanged = true;
                    ImStbTextedit.Replace(state!, ReadOnlySpan<byte>.Empty);
                }
                else if (!InputTextCompareBuffers(state!.TextA, state.TextToRevertTo, state.TextToRevertTo.Count - 1) || state.TextLen != state.TextToRevertTo.Count - 1)
                {
                    applyNewText = InputTextByteArray(state.TextToRevertTo, state.TextToRevertTo.Count - 1);
                    applyNewTextLength = state.TextToRevertTo.Count - 1;
                    valueChanged = true;
                    ImStbTextedit.Replace(state, applyNewText);
                }
            }

            if ((flags & (ImGuiInputTextFlags.CallbackCompletion | ImGuiInputTextFlags.CallbackHistory | ImGuiInputTextFlags.CallbackEdit | ImGuiInputTextFlags.CallbackAlways)) != 0)
            {
                ImAssert.Check(callback != null, "A callback must be supplied for the requested callback events.");
                ImGuiInputTextFlags eventFlag = ImGuiInputTextFlags.None;
                ImGuiKey eventKey = ImGuiKey.None;
                if ((flags & ImGuiInputTextFlags.CallbackCompletion) != 0 && Shortcut(ImGuiKey.Tab, 0, id))
                {
                    eventFlag = ImGuiInputTextFlags.CallbackCompletion;
                    eventKey = ImGuiKey.Tab;
                }
                else if ((flags & ImGuiInputTextFlags.CallbackHistory) != 0 && IsKeyPressed(ImGuiKey.UpArrow))
                {
                    eventFlag = ImGuiInputTextFlags.CallbackHistory;
                    eventKey = ImGuiKey.UpArrow;
                }
                else if ((flags & ImGuiInputTextFlags.CallbackHistory) != 0 && IsKeyPressed(ImGuiKey.DownArrow))
                {
                    eventFlag = ImGuiInputTextFlags.CallbackHistory;
                    eventKey = ImGuiKey.DownArrow;
                }
                else if ((flags & ImGuiInputTextFlags.CallbackEdit) != 0 && state!.EditedThisFrame)
                {
                    eventFlag = ImGuiInputTextFlags.CallbackEdit;
                }
                else if ((flags & ImGuiInputTextFlags.CallbackAlways) != 0)
                {
                    eventFlag = ImGuiInputTextFlags.CallbackAlways;
                }

                if (eventFlag != ImGuiInputTextFlags.None)
                {
                    ImGuiInputTextCallbackData callbackData = new()
                    {
                        Ctx = g,
                        ID = id,
                        Flags = flags,
                        EventFlag = eventFlag,
                        EventActivated = g.ActiveId == state!.ID && g.ActiveIdIsJustActivated,
                        UserData = callbackUserData,
                        EventKey = eventKey,
                        Buf = InputTextByteArray(state.TextA, state.TextLen + 1),
                        BufTextLen = state.TextLen,
                        BufSize = state.BufCapacity,
                        BufDirty = false,
                        CursorPos = state.Stb.Cursor,
                        SelectionStart = state.Stb.SelectStart,
                        SelectionEnd = state.Stb.SelectEnd,
                    };
                    InputTextEnsureBuffer(state.CallbackTextBackup, state.TextLen + 1);
                    InputTextCopy(state.CallbackTextBackup, InputTextByteArray(state.TextA, state.TextLen + 1), state.TextLen + 1);

                    callback!(callbackData);

                    byte[] callbackBuf = callbackData.Buf ?? Array.Empty<byte>();
                    if (callbackData.BufDirty || callbackData.CursorPos != state.Stb.Cursor)
                        state.CursorFollow = true;
                    if (callbackData.BufDirty)
                    {
                        ImAssert.Check(callbackData.BufTextLen == InputTextStrLen(callbackBuf, callbackBuf.Length), "A dirty callback buffer must keep BufTextLen in sync with the text.");
                        InputTextReconcileUndoState(state, InputTextByteArray(state.CallbackTextBackup, state.CallbackTextBackup.Count), state.CallbackTextBackup.Count - 1, callbackBuf, callbackData.BufTextLen);
                        InputTextEnsureBuffer(state.TextA, callbackData.BufTextLen + 1);
                        InputTextCopy(state.TextA, callbackBuf, callbackData.BufTextLen + 1);
                        state.TextLen = callbackData.BufTextLen;
                        state.EditedBefore = state.EditedThisFrame = true;
                        state.CursorAnimReset();
                    }

                    state.Stb.Cursor = Math.Clamp(callbackData.CursorPos, 0, callbackData.BufTextLen);
                    state.Stb.SelectStart = Math.Clamp(callbackData.SelectionStart, 0, callbackData.BufTextLen);
                    state.Stb.SelectEnd = Math.Clamp(callbackData.SelectionEnd, 0, callbackData.BufTextLen);
                }
            }

            if (!isReadonly)
            {
                if ((g.LastItemData.ItemFlags & ImGuiItemFlags.LiveEditOnInputText) != 0)
                {
                    if (!InputTextStateEqualsBuffer(state!.TextA, buf, state.TextLen))
                    {
                        applyNewText = InputTextByteArray(state.TextA, state.TextLen);
                        applyNewTextLength = state.TextLen;
                        valueChanged = true;
                    }
                }
                else
                {
                    bool differs = !InputTextStateEqualsBuffer(state!.TextA, buf, state.TextLen);
                    valueChanged = (validated || clearActiveId || revertEdit) && differs;
                    applyNewText = valueChanged ? InputTextByteArray(state.TextA, state.TextLen) : null;
                    applyNewTextLength = valueChanged ? state.TextLen : 0;
                }
            }
        }

        if (g.InputTextDeactivatedState.ID == id)
        {
            bool reapply = (g.ActiveId != id && IsItemDeactivated()) || (g.ActiveId == id && (flags & flagTempInput) != 0);
            if (reapply && !isReadonly &&
                !InputTextStateEqualsBuffer(g.InputTextDeactivatedState.TextA, buf, g.InputTextDeactivatedState.TextA.Count - 1))
            {
                applyNewText = InputTextByteArray(g.InputTextDeactivatedState.TextA, g.InputTextDeactivatedState.TextA.Count - 1);
                applyNewTextLength = g.InputTextDeactivatedState.TextA.Count - 1;
                valueChanged = true;
            }
            g.InputTextDeactivatedState.ID = 0;
        }

        if (applyNewText != null)
        {
            ImAssert.Check(applyNewTextLength >= 0, "The applied text length cannot be negative.");
            if (isResizable)
            {
                ImGuiInputTextCallbackData callbackData = new()
                {
                    Ctx = g,
                    ID = id,
                    Flags = flags,
                    EventFlag = ImGuiInputTextFlags.CallbackResize,
                    EventActivated = state != null && g.ActiveId == state.ID && g.ActiveIdIsJustActivated,
                    Buf = buf,
                    BufTextLen = applyNewTextLength,
                    BufSize = Math.Max(bufSize, applyNewTextLength + 1),
                    UserData = callbackUserData,
                };
                callback!(callbackData);
                if (callbackData.Buf is { } newBuf)
                    buf = newBuf;
                bufSize = callbackData.BufSize;
                applyNewTextLength = Math.Min(callbackData.BufTextLen, bufSize - 1);
                ImAssert.Check(applyNewTextLength <= bufSize, "The applied text must fit the resized buffer.");
            }

            int copyCount = Math.Min(applyNewTextLength + 1, bufSize);
            for (int i = 0; i < copyCount; i++)
                buf[i] = i < applyNewText.Length ? applyNewText[i] : (byte)0;
        }

        if (g.ActiveId == id && clearActiveId)
        {
            state!.EditedBefore = false;
            ClearActiveID();
        }

        if (!isMultiline)
        {
            RenderNavCursor(frameBb, id);
            RenderFrame(frameBb.Min, frameBb.Max, GetColorU32(ImGuiCol.FrameBg), true, style.FrameRounding);
        }

        ImVec2 drawPos = isMultiline ? drawWindow.DC.CursorPos : new ImVec2(frameBb.Min.X + style.FramePadding.X, frameBb.Min.Y + style.FramePadding.Y);
        ImRect clipRect = new(frameBb.Min.X, frameBb.Min.Y, frameBb.Min.X + innerSize.X, frameBb.Min.Y + innerSize.Y);
        if (isMultiline)
            clipRect.ClipWith(drawWindow.ClipRect);

        byte[] hintBytes = hint != null ? System.Text.Encoding.UTF8.GetBytes(hint) : Array.Empty<byte>();
        bool isDisplayingMixed = isMixed && g.ActiveId != id && applyNewText == null;
        byte[]? mixedBytes = isDisplayingMixed && g.MixedValueLabel != null ? System.Text.Encoding.UTF8.GetBytes(g.MixedValueLabel) : null;
        ReadOnlySpan<byte> displaySpan;
        int displayEnd;
        if (isDisplayingMixed && mixedBytes != null)
        {
            displaySpan = mixedBytes;
            displayEnd = mixedBytes.Length;
            renderCursor = renderSelection = false;
        }
        else if (isDisplayingHint)
        {
            displaySpan = hintBytes;
            displayEnd = hintBytes.Length;
        }
        else if (bufDisplayFromState)
        {
            displaySpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(state!.TextA);
            displayEnd = state.TextLen;
        }
        else
        {
            displaySpan = buf;
            bool useStateLength = state != null && (renderCursor || renderSelection || g.ActiveId == id);
            displayEnd = useStateLength ? state!.TextLen : InputTextStrLen(buf, bufSize);
        }
        displayEnd = Math.Clamp(displayEnd, 0, displaySpan.Length);

        bool newIsDisplayingHint = hint != null && (bufDisplayFromState ? (state!.TextA.Count > 0 ? state.TextA[0] : 0) : buf[0]) == 0 && !isMixed;
        if (newIsDisplayingHint != isDisplayingHint)
        {
            if (isPassword && !isDisplayingHint)
                PopPasswordFont();
            isDisplayingHint = newIsDisplayingHint;
            if (isPassword && !isDisplayingHint)
                PushPasswordFont();
        }

        int lineVisibleN0 = 0;
        int lineVisibleN1 = 1;
        if (isMultiline)
            CalcClipRectVisibleItemsY(clipRect, drawPos, g.FontSize, ref lineVisibleN0, ref lineVisibleN1);

        ImGuiTextIndex lineIndex = g.InputTextLineIndex;
        lineIndex.Clear();
        int lineCount = 1;
        if (isMultiline)
        {
            bool willScrollY = state != null && ((state.CursorFollow && renderCursor) || (state.CursorCenterY && (renderCursor || renderSelection)));
            int maxLines = willScrollY ? int.MaxValue : lineVisibleN1 + 1;
            lineCount = InputTextLineIndexBuild(flags, lineIndex, displaySpan.Slice(0, displayEnd), wrapWidth, maxLines);
        }
        lineIndex.EndOffset = displayEnd;
        lineVisibleN1 = Math.Min(lineVisibleN1, lineCount);

        float textSizeY = lineCount * g.FontSize;
        ImVec2 cursorOffset = renderCursor && state != null
            ? InputTextLineIndexGetPosOffset(g, state, lineIndex, displaySpan, state.Stb.Cursor)
            : new ImVec2(0.0f, 0.0f);
        ImVec2 drawScroll = new(0.0f, 0.0f);

        uint textCol = GetColorU32(isDisplayingHint ? ImGuiCol.TextDisabled : isDisplayingMixed ? ImGuiCol.TextMixedValue : ImGuiCol.Text);
        if (renderCursor || renderSelection)
        {
            ImAssert.Check(state != null, "Rendering a cursor requires a text state.");
            state!.LineCount = lineCount;
            float newScrollY = scrollY;
            if (renderCursor && state.CursorFollow)
            {
                if ((flags & ImGuiInputTextFlags.NoHorizontalScroll) == 0)
                {
                    float scrollIncrementX = innerSize.X * 0.25f;
                    float visibleWidth = innerSize.X - style.FramePadding.X;
                    if (cursorOffset.X < state.Scroll.X)
                        state.Scroll = new ImVec2(MathF.Truncate(MathF.Max(0.0f, cursorOffset.X - scrollIncrementX)), state.Scroll.Y);
                    else if (cursorOffset.X - visibleWidth >= state.Scroll.X)
                        state.Scroll = new ImVec2(MathF.Truncate(cursorOffset.X - visibleWidth + scrollIncrementX), state.Scroll.Y);
                }
                else
                {
                    state.Scroll = new ImVec2(0.0f, state.Scroll.Y);
                }

                if (isMultiline)
                {
                    if (cursorOffset.Y - g.FontSize < scrollY)
                        newScrollY = MathF.Max(0.0f, cursorOffset.Y - g.FontSize);
                    else if (cursorOffset.Y - (innerSize.Y - style.FramePadding.Y * 2.0f) >= scrollY)
                        newScrollY = cursorOffset.Y - innerSize.Y + style.FramePadding.Y * 2.0f;
                }
                state.CursorFollow = false;
            }
            if (state.CursorCenterY)
            {
                if (isMultiline)
                    newScrollY = cursorOffset.Y - g.FontSize - (innerSize.Y * 0.5f - style.FramePadding.Y);
                state.CursorCenterY = false;
                renderCursor = false;
            }
            if (newScrollY != scrollY)
            {
                float scrollMaxY = MathF.Max((textSizeY + style.FramePadding.Y * 2.0f) - innerSize.Y, 0.0f);
                scrollY = Math.Clamp(newScrollY, 0.0f, scrollMaxY);
                drawPos = new ImVec2(drawPos.X, drawPos.Y + (drawWindow.Scroll.Y - scrollY));
                drawWindow.Scroll = new ImVec2(drawWindow.Scroll.X, scrollY);
                CalcClipRectVisibleItemsY(clipRect, drawPos, g.FontSize, ref lineVisibleN0, ref lineVisibleN1);
                lineVisibleN1 = Math.Min(lineVisibleN1, lineCount);
            }

            drawScroll = new ImVec2(state.Scroll.X, 0.0f);
            if (renderSelection)
            {
                uint bgColor = GetColorU32(ImGuiCol.TextSelectedBg, renderCursor ? 1.0f : 0.6f);
                float bgOffyUp = isMultiline ? 0.0f : -1.0f;
                float bgOffyDn = isMultiline ? 0.0f : 2.0f;
                float bgEolWidth = (int)(g.FontBaked!.GetCharAdvance((uint)' ') * 0.5f);
                int selectedBegin = Math.Min(state.Stb.SelectStart, state.Stb.SelectEnd);
                int selectedEnd = Math.Max(state.Stb.SelectStart, state.Stb.SelectEnd);
                for (int lineN = lineVisibleN0; lineN < lineVisibleN1; lineN++)
                {
                    int p = InputTextLineBegin(lineIndex, lineN);
                    int pEol = lineIndex.GetLineEnd(lineN);
                    bool pEolIsWrap = pEol < displayEnd && pEol < displaySpan.Length && displaySpan[pEol] != (byte)'\n';
                    if (pEolIsWrap)
                        pEol++;
                    int lineSelectedBegin = Math.Max(selectedBegin, p);
                    int lineSelectedEnd = Math.Min(selectedEnd, pEol);

                    float rectWidth = 0.0f;
                    if (lineSelectedBegin < lineSelectedEnd)
                        rectWidth += InputTextBoxWidth(g, displaySpan.Slice(lineSelectedBegin, lineSelectedEnd - lineSelectedBegin));
                    if (selectedBegin <= pEol && selectedEnd > pEol && !pEolIsWrap)
                        rectWidth += bgEolWidth;
                    if (rectWidth == 0.0f)
                        continue;

                    ImRect rect;
                    rect.Min.X = drawPos.X - drawScroll.X + InputTextBoxWidth(g, displaySpan.Slice(p, Math.Max(lineSelectedBegin - p, 0)));
                    rect.Min.Y = drawPos.Y - drawScroll.Y + lineN * g.FontSize;
                    rect.Max.X = rect.Min.X + rectWidth;
                    rect.Max.Y = rect.Min.Y + bgOffyDn + g.FontSize;
                    rect.Min.Y += bgOffyUp;
                    rect.ClipWith(clipRect);
                    drawWindow.DrawList.AddRectFilled(rect.Min, rect.Max, bgColor);
                }
            }
        }

        if (g.ActiveId != id && (flags & ImGuiInputTextFlags.ElideLeft) != 0 && !renderCursor && !renderSelection)
            drawPos = new ImVec2(MathF.Min(drawPos.X, frameBb.Max.X - InputTextBoxWidth(g, displaySpan.Slice(0, displayEnd)) - style.FramePadding.X), drawPos.Y);

        if ((isMultiline || displayEnd < 2 * 1024 * 1024) &&
            (textCol & 0xFF000000u) != 0 && lineVisibleN0 < lineVisibleN1 && g.Font != null)
        {
            int renderBegin = InputTextLineBegin(lineIndex, lineVisibleN0);
            int renderEnd = Math.Clamp(lineIndex.GetLineEnd(lineVisibleN1 - 1), renderBegin, displayEnd);
            string renderText = InputTextDecodeUtf8(displaySpan.Slice(renderBegin, renderEnd - renderBegin));
            ImVec2 renderPos = new(drawPos.X - drawScroll.X, drawPos.Y - drawScroll.Y + lineVisibleN0 * g.FontSize);
            g.Font.RenderText(drawWindow.DrawList, g.FontSize, renderPos, textCol, clipRect, renderText.AsSpan(), wrapWidth, cpuFineClip: true);
        }

        if (renderCursor && state != null)
        {
            state.CursorAnim += io.DeltaTime;
            bool cursorIsVisible = !g.IO.ConfigInputTextCursorBlink || state.CursorAnim <= 0.0f || state.CursorAnim % 1.20f <= 0.80f;
            ImVec2 cursorScreenPos = new(MathF.Truncate(drawPos.X + cursorOffset.X - drawScroll.X), MathF.Truncate(drawPos.Y + cursorOffset.Y - drawScroll.Y));
            ImRect cursorScreenRect = new(cursorScreenPos.X, cursorScreenPos.Y - g.FontSize + 0.5f, cursorScreenPos.X + 1.0f, cursorScreenPos.Y - 1.5f);
            if (cursorIsVisible && cursorScreenRect.Overlaps(clipRect))
                drawWindow.DrawList.AddLineV(cursorScreenRect.Min.X, cursorScreenRect.Min.Y, cursorScreenRect.Max.Y, GetColorU32(ImGuiCol.InputTextCursor), style.InputTextCursorSize);

            // Notify the OS of the text-input position for advanced IME. The -1 x offset lets the Windows
            // IME cover the cursor. The assignment is skipped on the deactivating frame (#6341).
            if (!isReadonly && g.ActiveId == id)
            {
                ImGuiPlatformImeData imeData = g.PlatformImeData;
                imeData.WantVisible = true;
                imeData.WantTextInput = true;
                imeData.InputPos = new ImVec2(cursorScreenPos.X - 1.0f, cursorScreenPos.Y - g.FontSize);
                imeData.InputLineHeight = g.FontSize;
                imeData.ViewportId = window.Viewport!.ID;
            }
        }

        if (isPassword && !isDisplayingHint)
            PopPasswordFont();

        if (isMultiline)
        {
            Dummy(new ImVec2(0.0f, textSizeY + style.FramePadding.Y));
            g.NextItemData.ItemFlagsSet |= flagInputable | ImGuiItemFlags.NoTabStop;
            EndChild();
            itemDataBackupStatusFlags |= g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HoveredWindow;
            EndGroup();
            if (g.LastItemData.ID == 0 || g.LastItemData.ID != GetWindowScrollbarID(drawWindow, ImGuiAxis.Y))
            {
                g.LastItemData.ID = id;
                g.LastItemData.ItemFlags = itemDataBackupItemFlags;
                g.LastItemData.StatusFlags = itemDataBackupStatusFlags;
            }
        }
        if (state != null && isReadonly)
            state.TextSrc = null;

        // Log as text.
        if (g.LogEnabled && (!isPassword || isDisplayingHint))
        {
            LogSetNextTextDecoration("{", "}");
            // Canonical passes an explicit end pointer for the displayed input text, so the range is
            // captured verbatim rather than being re-truncated at a literal "##" in the user's content
            string loggedInputText = InputTextDecodeUtf8(displaySpan.Slice(0, displayEnd));
            LogRenderedText(drawPos, loggedInputText, loggedInputText);
        }

        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(frameBb.Max.X + style.ItemInnerSpacing.X, frameBb.Min.Y + style.FramePadding.Y), label, labelEnd, false);

        if (state != null && validated)
            state.ValidatedThisFrame = true;

        if (valueChanged)
            MarkItemEdited(id);

        return (flags & ImGuiInputTextFlags.EnterReturnsTrue) != 0 ? validated : valueChanged;
    }

    #endregion

    #endregion


    #region SECTION: COLOR WIDGETS, PLOTS, AND TYPING SELECT

    #region [SECTION] Widgets: ColorEdit, ColorPicker, ColorButton, etc.

    /// <summary>
    /// Gets the fixed identifiers of the four component sliders. Kept as file-scope data because
    /// canonical declares the array <c>static const</c> inside <c>ColorEdit4</c>.
    /// </summary>
    private static readonly string[] ColorEditComponentIds = { "##X", "##Y", "##Z", "##W" };

    /// <summary>
    /// Gets the integer component-slider format tables, indexed by short/long-RGBA/long-HSVA. Kept as
    /// file-scope data because canonical declares the table <c>static const</c>.
    /// </summary>
    private static readonly string[][] ColorEditIntFormats =
    {
        new[] { "%3d", "%3d", "%3d", "%3d" },
        new[] { "R:%3d", "G:%3d", "B:%3d", "A:%3d" },
        new[] { "H:%3d", "S:%3d", "V:%3d", "A:%3d" },
    };

    /// <summary>
    /// Gets the floating-point component-slider format tables, indexed by short/long-RGBA/long-HSVA.
    /// Kept as file-scope data because canonical declares the table <c>static const</c>.
    /// </summary>
    private static readonly string[][] ColorEditFloatFormats =
    {
        new[] { "%0.3f", "%0.3f", "%0.3f", "%0.3f" },
        new[] { "R:%0.3f", "G:%0.3f", "B:%0.3f", "A:%0.3f" },
        new[] { "H:%0.3f", "S:%0.3f", "V:%0.3f", "A:%0.3f" },
    };

    /// <summary>
    /// Displays three color components as an RGB editor. Only the first three elements of
    /// <paramref name="col"/> are read or written because the canonical call adds
    /// <see cref="ImGuiColorEditFlags.NoAlpha"/>.
    /// (.)
    /// </summary>
    /// <param name="label">Label text, also used as the identifier seed and popup title.</param>
    /// <param name="col">Color components; at least three elements.</param>
    /// <param name="flags">Color-edit flags; <see cref="ImGuiColorEditFlags.NoAlpha"/> is added.</param>
    /// <returns><see langword="true"/> when the color or its storage options changed.</returns>
    public static bool ColorEdit3(string label, float[] col, ImGuiColorEditFlags flags = 0)
    {
        ImAssert.Check(col is { Length: >= 3 }, "ColorEdit3 requires at least three components.");
        return ColorEdit4(label, col, flags | ImGuiColorEditFlags.NoAlpha);
    }

    /// <summary>
    /// Restores the hue of a grayscale color from the value saved by the last RGB-to-HSV round trip.
    /// </summary>
    /// <param name="col">The RGB color being edited.</param>
    /// <param name="h">The hue in/out parameter, overwritten with the saved hue when the guard passes.</param>
    /// <remarks>
    /// The saved hue is only meaningful while both the widget identifier and the RGB-only packed color
    /// still match the saved snapshot; otherwise the caller keeps the freshly converted hue. The port
    /// mirrors the canonical packed comparison with <see cref="ColorConvertFloat4ToU32(ImVec4)"/>.
    /// </remarks>
    private static void ColorEditRestoreH(float[] col, ref float h)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.ColorEditCurrentID != 0, "ColorEditRestoreH requires an active color-edit identifier.");
        if (g.ColorEditSavedID != g.ColorEditCurrentID || g.ColorEditSavedColor != ColorConvertFloat4ToU32(new ImVec4(col[0], col[1], col[2], 0.0f)))
            return;
        h = g.ColorEditSavedHue;
    }

    /// <summary>
    /// Restores the hue and saturation of a color whose HSV conversion became undefined because the
    /// color is grayscale or black. RGB input can produce hue/saturation that reset to zero; preserving
    /// the last meaningful values keeps the displayed HSV from jittering.
    /// </summary>
    /// <param name="col">The RGB color being edited.</param>
    /// <param name="h">The hue in/out parameter.</param>
    /// <param name="s">The saturation in/out parameter.</param>
    /// <param name="v">The value in/out parameter.</param>
    private static void ColorEditRestoreHS(float[] col, ref float h, ref float s, ref float v)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImAssert.Check(g.ColorEditCurrentID != 0, "ColorEditRestoreHS requires an active color-edit identifier.");
        if (g.ColorEditSavedID != g.ColorEditCurrentID || g.ColorEditSavedColor != ColorConvertFloat4ToU32(new ImVec4(col[0], col[1], col[2], 0.0f)))
            return;

        // When S == 0 the hue is undefined; when H wraps from 1 to 0 the saved hue is the meaningful one.
        if (s == 0.0f || (h == 0.0f && g.ColorEditSavedHue == 1.0f))
            h = g.ColorEditSavedHue;

        // When V == 0 the saturation is undefined.
        if (v == 0.0f)
            s = g.ColorEditSavedSat;
    }

    /// <summary>
    /// Displays a color as a row of R/G/B/A or H/S/V/A drag inputs, a hexadecimal input, or a compact
    /// preview that opens a picker. The widget is the canonical multi-mode color editor.
    /// (.)
    /// </summary>
    /// <param name="label">Label text, identifier seed, and picker popup title.</param>
    /// <param name="col">Color components; at least three elements, four when alpha is enabled.</param>
    /// <param name="flags">Color-edit flags, optionally extended from the stored configuration.</param>
    /// <returns><see langword="true"/> when the caller's color buffer changed.</returns>
    /// <remarks>
    /// <para>
    /// The canonical step order is preserved: option composition, stored-option resolution, working
    /// conversion, component or hex inputs, small preview and picker popup, label placement, conversion
    /// back to the caller's storage, and finally the model-edit notification.
    /// </para>
    /// <para>
    /// The widget is also a standardized drag target for
    /// <c>IMGUI_PAYLOAD_TYPE_COLOR_3F</c>/<c>IMGUI_PAYLOAD_TYPE_COLOR_4F</c> payloads, matching
    ///. Accepted RGB values are converted to HSV when the widget
    /// edits in HSV. The canonical <c>SetNextItemColorMarker()</c> inline.
    /// is not yet declared in the port, so the two queued fields are written directly.
    /// </para>
    /// <para>
    /// <c>BeginGroup()</c>/<c>EndGroup()</c> and the hexadecimal <c>InputText()</c> are shared members
    /// owned by Chunk 10 and part 07 respectively; they are called here and resolve once those members
    /// are ported.
    /// </para>
    /// </remarks>
    public static bool ColorEdit4(string label, float[] col, ImGuiColorEditFlags flags = 0)
    {
        ImAssert.Check(col is { Length: >= 3 }, "ColorEdit4 requires at least three components.");
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        ImGuiStyle style = g.Style;
        float squareSz = GetFrameHeight();
        string labelDisplayEnd = FindRenderedTextEnd(label);
        float wFull = CalcItemWidth();
        g.NextItemData.ClearFlags();

        BeginGroup();
        PushID(label);
        bool setCurrentColorEditId = g.ColorEditCurrentID == 0;
        if (setCurrentColorEditId)
            g.ColorEditCurrentID = window.IDStack[^1];

        // If no slider is shown there is no point in performing HSV conversions.
        ImGuiColorEditFlags flagsUntouched = flags;
        if ((flags & ImGuiColorEditFlags.NoInputs) != 0)
            flags = (flags & ~ImGuiColorEditFlags.DisplayMask_) | ImGuiColorEditFlags.DisplayRGB | ImGuiColorEditFlags.NoOptions;

        // Context menu: display and modify options, before the stored defaults are applied.
        if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
            ColorEditOptionsPopup(col, flags);

        if ((flags & ImGuiColorEditFlags.DisplayMask_) == 0)
            flags |= g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.DisplayMask_;
        if ((flags & ImGuiColorEditFlags.DataTypeMask_) == 0)
            flags |= g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.DataTypeMask_;
        if ((flags & ImGuiColorEditFlags.PickerMask_) == 0)
            flags |= g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.PickerMask_;
        if ((flags & ImGuiColorEditFlags.InputMask_) == 0)
            flags |= g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.InputMask_;
        flags |= g.IO.ConfigColorEditFlags & ~(ImGuiColorEditFlags.DisplayMask_ | ImGuiColorEditFlags.DataTypeMask_ | ImGuiColorEditFlags.PickerMask_ | ImGuiColorEditFlags.InputMask_);
        uint displayMaskBits = (uint)(flags & ImGuiColorEditFlags.DisplayMask_);
        ImAssert.Check(displayMaskBits != 0 && (displayMaskBits & (displayMaskBits - 1)) == 0, "Only one Display flag may be selected.");
        uint inputMaskBits = (uint)(flags & ImGuiColorEditFlags.InputMask_);
        ImAssert.Check(inputMaskBits != 0 && (inputMaskBits & (inputMaskBits - 1)) == 0, "Only one Input flag may be selected.");

        bool alpha = (flags & ImGuiColorEditFlags.NoAlpha) == 0;
        bool hdr = (flags & ImGuiColorEditFlags.HDR) != 0;
        int components = alpha ? 4 : 3;
        float wButton = (flags & ImGuiColorEditFlags.NoSmallPreview) != 0 ? 0.0f : squareSz + style.ItemInnerSpacing.X;
        float wInputs = MathF.Max(wFull - wButton, 1.0f);
        wFull = wInputs + wButton;

        // Convert to the working representation.
        float[] f = { col[0], col[1], col[2], alpha ? col[3] : 1.0f };
        if ((flags & ImGuiColorEditFlags.InputHSV) != 0 && (flags & ImGuiColorEditFlags.DisplayRGB) != 0)
        {
            ColorConvertHSVtoRGB(f[0], f[1], f[2], out float rgb0, out float rgb1, out float rgb2);
            f[0] = rgb0;
            f[1] = rgb1;
            f[2] = rgb2;
        }
        else if ((flags & ImGuiColorEditFlags.InputRGB) != 0 && (flags & ImGuiColorEditFlags.DisplayHSV) != 0)
        {
            // Hue is lost when converting from grayscale RGB, so it is restored explicitly.
            ColorConvertRGBtoHSV(f[0], f[1], f[2], out float hsv0, out float hsv1, out float hsv2);
            f[0] = hsv0;
            f[1] = hsv1;
            f[2] = hsv2;
            ColorEditRestoreHS(col, ref f[0], ref f[1], ref f[2]);
        }
        int[] i =
        {
            (int)(f[0] * 255.0f + (f[0] >= 0.0f ? 0.5f : -0.5f)),
            (int)(f[1] * 255.0f + (f[1] >= 0.0f ? 0.5f : -0.5f)),
            (int)(f[2] * 255.0f + (f[2] >= 0.0f ? 0.5f : -0.5f)),
            (int)(f[3] * 255.0f + (f[3] >= 0.0f ? 0.5f : -0.5f)),
        };

        bool valueChanged = false;
        bool valueChangedAsFloat = false;
        ImVec2 pos = window.DC.CursorPos;
        float inputsOffsetX = style.ColorButtonPosition == ImGuiDir.Left ? wButton : 0.0f;
        window.DC.CursorPos = new ImVec2(pos.X + inputsOffsetX, pos.Y);

        if ((flags & (ImGuiColorEditFlags.DisplayRGB | ImGuiColorEditFlags.DisplayHSV)) != 0 && (flags & ImGuiColorEditFlags.NoInputs) == 0)
        {
            // RGB/HSV 0.255 sliders.
            float wItems = wInputs - style.ItemInnerSpacing.X * (components - 1);
            float wPerComponent = TruncateToInt(wItems / components);
            bool drawColorMarker = (flags & (ImGuiColorEditFlags.DisplayHSV | ImGuiColorEditFlags.NoColorMarkers)) == 0;
            bool hidePrefix = drawColorMarker || wPerComponent <= CalcTextSize((flags & ImGuiColorEditFlags.Float) != 0 ? "M:0.000" : "M:000").X;
            int fmtIdx = hidePrefix ? 0 : ((flags & ImGuiColorEditFlags.DisplayHSV) != 0 ? 2 : 1);
            ImGuiSliderFlags dragFlags = drawColorMarker ? ImGuiSliderFlags.ColorMarkers : ImGuiSliderFlags.None;

            float prevSplit = 0.0f;
            for (int n = 0; n < components; n++)
            {
                if (n > 0)
                    SameLine(0.0f, style.ItemInnerSpacing.X);
                float nextSplit = TruncateToInt(wItems * (n + 1) / components);
                SetNextItemWidth(MathF.Max(nextSplit - prevSplit, 1.0f));
                prevSplit = nextSplit;
                if (drawColorMarker)
                {
                    // Canonical SetNextItemColorMarker(GDefaultRgbaColorMarkers[n]).
                    g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasColorMarker;
                    g.NextItemData.ColorMarker = GDefaultRgbaColorMarkers[n];
                }

                if ((flags & ImGuiColorEditFlags.Float) != 0)
                {
                    valueChanged |= DragFloat(ColorEditComponentIds[n], ref f[n], 1.0f / 255.0f, 0.0f, hdr ? 0.0f : 1.0f, ColorEditFloatFormats[fmtIdx][n], dragFlags);
                    valueChangedAsFloat |= valueChanged;
                }
                else
                {
                    valueChanged |= DragInt(ColorEditComponentIds[n], ref i[n], 1.0f, 0, hdr ? 0 : 255, ColorEditIntFormats[fmtIdx][n], dragFlags);
                }
                if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
                    OpenPopupOnItemClick("context", ImGuiPopupFlags.MouseButtonRight);
            }
        }
        else if ((flags & ImGuiColorEditFlags.DisplayHex) != 0 && (flags & ImGuiColorEditFlags.NoInputs) == 0)
        {
            // RGB hexadecimal input.
            string buf = alpha
                ? ImFormatString("#%02X%02X%02X%02X", Math.Clamp(i[0], 0, 255), Math.Clamp(i[1], 0, 255), Math.Clamp(i[2], 0, 255), Math.Clamp(i[3], 0, 255))
                : ImFormatString("#%02X%02X%02X", Math.Clamp(i[0], 0, 255), Math.Clamp(i[1], 0, 255), Math.Clamp(i[2], 0, 255));
            SetNextItemWidth(wInputs);
            if (InputText("##Text", ref buf, ImGuiInputTextFlags.CharsUppercase))
            {
                valueChanged = true;
                int p = 0;
                while (p < buf.Length && (buf[p] == '#' || buf[p] == ' ' || buf[p] == '\t'))
                    p++;
                i[0] = i[1] = i[2] = 0;
                i[3] = 0xFF; // Alpha defaults to 255 because scanf does not parse an omitted alpha.
                // Canonical sscanf("%02X.") stops at the first non-hexadecimal character.
                int parsedComponents = alpha ? 4 : 3;
                for (int n = 0; n < parsedComponents; n++)
                {
                    int digits = 0;
                    int value = 0;
                    while (digits < 2 && p < buf.Length && TryParseHexDigit(buf[p], out int digit))
                    {
                        value = (value * 16) + digit;
                        p++;
                        digits++;
                    }
                    if (digits == 0)
                        break;
                    i[n] = value;
                }
            }
            if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
                OpenPopupOnItemClick("context", ImGuiPopupFlags.MouseButtonRight);
        }

        ImGuiWindow? pickerActiveWindow = null;
        if ((flags & ImGuiColorEditFlags.NoSmallPreview) == 0)
        {
            float buttonOffsetX = (flags & ImGuiColorEditFlags.NoInputs) != 0 || style.ColorButtonPosition == ImGuiDir.Left ? 0.0f : wInputs + style.ItemInnerSpacing.X;
            window.DC.CursorPos = new ImVec2(pos.X + buttonOffsetX, pos.Y);

            ImVec4 colV4 = new(col[0], col[1], col[2], alpha ? col[3] : 1.0f);
            if (ColorButton("##ColorButton", colV4, flags))
            {
                if ((flags & ImGuiColorEditFlags.NoPicker) == 0)
                {
                    // Store the current color and open the picker.
                    g.ColorPickerRef = colV4;
                    OpenPopup("picker");
                    SetNextWindowPos(g.LastItemData.Rect.GetBL() + new ImVec2(0.0f, style.ItemSpacing.Y));
                }
            }
            if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
                OpenPopupOnItemClick("context", ImGuiPopupFlags.MouseButtonRight);

            if (BeginPopup("picker"))
            {
                if (g.CurrentWindow!.BeginCount == 1)
                {
                    pickerActiveWindow = g.CurrentWindow;
                    if (labelDisplayEnd.Length > 0)
                    {
                        TextEx(label, labelDisplayEnd);
                        Spacing();
                    }
                    ImGuiColorEditFlags pickerFlagsToForward = ImGuiColorEditFlags.DataTypeMask_ | ImGuiColorEditFlags.PickerMask_ | ImGuiColorEditFlags.InputMask_ | ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.AlphaBar;
                    ImGuiColorEditFlags pickerFlags = (flagsUntouched & pickerFlagsToForward) | ImGuiColorEditFlags.DisplayMask_ | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaPreviewHalf;
                    SetNextItemWidth(squareSz * 12.0f);
                    valueChanged |= ColorPicker4("##picker", col, pickerFlags, g.ColorPickerRef);
                }
                EndPopup();
            }
        }

        if (labelDisplayEnd.Length > 0 && (flags & ImGuiColorEditFlags.NoLabel) == 0)
        {
            // SameLine() is needed to set the baseline correctly even though the position is overridden.
            SameLine(0.0f, style.ItemInnerSpacing.X);
            window.DC.CursorPos = new ImVec2(pos.X + ((flags & ImGuiColorEditFlags.NoInputs) != 0 ? wButton : wFull + style.ItemInnerSpacing.X), window.DC.CursorPos.Y);
            TextEx(label, labelDisplayEnd);
        }

        // Convert back to the caller's representation.
        if (valueChanged && pickerActiveWindow == null)
        {
            if (!valueChangedAsFloat)
                for (int n = 0; n < 4; n++)
                    f[n] = i[n] / 255.0f;
            if ((flags & ImGuiColorEditFlags.DisplayHSV) != 0 && (flags & ImGuiColorEditFlags.InputRGB) != 0)
            {
                g.ColorEditSavedHue = f[0];
                g.ColorEditSavedSat = f[1];
                ColorConvertHSVtoRGB(f[0], f[1], f[2], out float back0, out float back1, out float back2);
                f[0] = back0;
                f[1] = back1;
                f[2] = back2;
                g.ColorEditSavedID = g.ColorEditCurrentID;
                g.ColorEditSavedColor = ColorConvertFloat4ToU32(new ImVec4(f[0], f[1], f[2], 0.0f));
            }
            if ((flags & ImGuiColorEditFlags.DisplayRGB) != 0 && (flags & ImGuiColorEditFlags.InputHSV) != 0)
            {
                ColorConvertRGBtoHSV(f[0], f[1], f[2], out float backH, out float backS, out float backV);
                f[0] = backH;
                f[1] = backS;
                f[2] = backV;
            }

            col[0] = f[0];
            col[1] = f[1];
            col[2] = f[2];
            if (alpha)
                col[3] = f[3];
        }

        if (setCurrentColorEditId)
            g.ColorEditCurrentID = 0;
        PopID();
        EndGroup();

        // Drag and Drop Target. The HoveredRect test is an optional
        // micro-optimization because BeginDragDropTarget() performs the same check. The read-only bit
        // is the canonical internal ImGuiItemFlags_ReadOnly, which the public item-flag enum does not
        // expose.
        if ((g.LastItemData.StatusFlags & ImGuiItemStatusFlags.HoveredRect) != 0
            && ((int)g.LastItemData.ItemFlags & (int)ImGuiItemFlagsPrivate.ImGuiItemFlags_ReadOnly) == 0
            && (flags & ImGuiColorEditFlags.NoDragDrop) == 0
            && BeginDragDropTarget())
        {
            bool acceptedDragDrop = false;
            ImGuiPayload? payload3 = AcceptDragDropPayload(IMGUI_PAYLOAD_TYPE_COLOR_3F);
            if (payload3?.ObjectData is float[] { Length: >= 3 } rgbPayload)
            {
                // A three-component payload preserves any existing alpha.
                col[0] = rgbPayload[0];
                col[1] = rgbPayload[1];
                col[2] = rgbPayload[2];
                acceptedDragDrop = true;
            }
            ImGuiPayload? payload4 = AcceptDragDropPayload(IMGUI_PAYLOAD_TYPE_COLOR_4F);
            if (payload4?.ObjectData is float[] { Length: >= 4 } rgbaPayload)
            {
                col[0] = rgbaPayload[0];
                col[1] = rgbaPayload[1];
                col[2] = rgbaPayload[2];
                if (components == 4)
                    col[3] = rgbaPayload[3];
                acceptedDragDrop = true;
            }
            valueChanged |= acceptedDragDrop;

            // Color payloads are always RGB, so an HSV-editing widget converts the accepted values.
            if (acceptedDragDrop && (flags & ImGuiColorEditFlags.InputHSV) != 0)
            {
                ColorConvertRGBtoHSV(col[0], col[1], col[2], out float hsvCol0, out float hsvCol1, out float hsvCol2);
                col[0] = hsvCol0;
                col[1] = hsvCol1;
                col[2] = hsvCol2;
            }
            EndDragDropTarget();
        }

        // While the picker is being used, adopt its active identifier so IsItemActive() works here.
        if (pickerActiveWindow != null && g.ActiveId != 0 && ReferenceEquals(g.ActiveIdWindow, pickerActiveWindow))
            g.LastItemData.ID = g.ActiveId;

        if (valueChanged && g.LastItemData.ID != 0)
            MarkItemEdited(g.LastItemData.ID);

        return valueChanged;
    }

    /// <summary>
    /// Displays a color picker for three RGB components by promoting them to an alpha-opaque four
    /// component buffer and delegating to <see cref="ColorPicker4"/>.
    /// (.)
    /// </summary>
    /// <param name="label">Label text, identifier seed, and picker title.</param>
    /// <param name="col">Color components; at least three elements.</param>
    /// <param name="flags">Color-edit flags; <see cref="ImGuiColorEditFlags.NoAlpha"/> is added.</param>
    /// <returns><see langword="true"/> when the caller's color buffer changed.</returns>
    public static bool ColorPicker3(string label, float[] col, ImGuiColorEditFlags flags = 0)
    {
        ImAssert.Check(col is { Length: >= 3 }, "ColorPicker3 requires at least three components.");
        float[] col4 = { col[0], col[1], col[2], 1.0f };
        if (!ColorPicker4(label, col4, flags | ImGuiColorEditFlags.NoAlpha))
            return false;
        col[0] = col4[0];
        col[1] = col4[1];
        col[2] = col4[2];
        return true;
    }

    /// <summary>
    /// Renders the pair of black-outlined white triangles that flank a hue or alpha bar, indicating
    /// that the bar is horizontally draggable through this widget.
    /// </summary>
    /// <param name="drawList">The draw list receiving the geometry.</param>
    /// <param name="pos">The center of the left arrow triangle.</param>
    /// <param name="halfSz">The half size of the white triangles.</param>
    /// <param name="barW">The width of the bar the arrows flank.</param>
    /// <param name="alpha">The global style alpha applied to the arrow colors.</param>
    private static void RenderArrowsForVerticalBar(ImDrawList drawList, ImVec2 pos, ImVec2 halfSz, float barW, float alpha)
    {
        int alpha8 = (int)(ColorConvertSaturate(alpha) * 255.0f + 0.5f);
        RenderArrowPointingAt(drawList, new ImVec2(pos.X + halfSz.X + 1, pos.Y), new ImVec2(halfSz.X + 2, halfSz.Y + 1), ImGuiDir.Right, ImColor.Pack((byte)0, (byte)0, (byte)0, (byte)alpha8));
        RenderArrowPointingAt(drawList, new ImVec2(pos.X + halfSz.X, pos.Y), halfSz, ImGuiDir.Right, ImColor.Pack((byte)255, (byte)255, (byte)255, (byte)alpha8));
        RenderArrowPointingAt(drawList, new ImVec2(pos.X + barW - halfSz.X - 1, pos.Y), new ImVec2(halfSz.X + 2, halfSz.Y + 1), ImGuiDir.Left, ImColor.Pack((byte)0, (byte)0, (byte)0, (byte)alpha8));
        RenderArrowPointingAt(drawList, new ImVec2(pos.X + barW - halfSz.X, pos.Y), halfSz, ImGuiDir.Left, ImColor.Pack((byte)255, (byte)255, (byte)255, (byte)alpha8));
    }

    /// <summary>
    /// Displays the canonical color picker: either a hue wheel with an SV triangle or an SV square
    /// with hue and optional alpha bars, plus an optional side preview and RGB/HSV/hex sub-editors.
    /// (.)
    /// </summary>
    /// <param name="label">Label text, identifier seed, and picker title.</param>
    /// <param name="col">Color components; at least three elements, four when alpha is enabled.</param>
    /// <param name="flags">Color-edit flags, optionally extended from the stored configuration.</param>
    /// <param name="refCol">
    /// The "original" color shown beside the current color, or <see langword="null"/>. Canonical passes
    /// a <c>const float*</c>; the port uses the existing <see cref="ImVec4"/> value to avoid a per-frame
    /// array allocation. The value is read only.
    /// </param>
    /// <returns><see langword="true"/> when the caller's color buffer changed.</returns>
    /// <remarks>
    /// <para>
    /// The canonical interaction model is preserved, including the read-only guard on the wheel and
    /// bars (the alpha bar deliberately has no read-only check, matching canonical), the hue-wrap
    /// cancellation pass, and the no-round-trip guard before the edit notification.
    /// </para>
    /// <para>
    /// The hue wheel's arc strokes are recolored with
    /// <see cref="ShadeVertsLinearColorGradientKeepAlpha"/>, reproducing the canonical gradient painted
    /// over each of the six arc segments. The per-vertex <c>PrimVtx()</c> triangle fill remains inert
    /// because that draw primitive is absent from the port's draw pipeline; the triangle outline is
    /// still emitted.
    /// </para>
    /// <para>
    /// <c>BeginGroup()</c>/<c>EndGroup()</c> are owned by Chunk 10; they are called here and resolve once
    /// that member is ported.
    /// </para>
    /// </remarks>
    public static bool ColorPicker4(string label, float[] col, ImGuiColorEditFlags flags = 0, ImVec4? refCol = null)
    {
        ImAssert.Check(col is { Length: >= 3 }, "ColorPicker4 requires at least three components.");
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImDrawList drawList = window.DrawList;
        ImGuiStyle style = g.Style;
        ImGuiIO io = g.IO;

        float width = CalcItemWidth();
        bool isReadonly = ((g.NextItemData.ItemFlagsSet | g.CurrentItemFlags) & (ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_ReadOnly) != 0;
        g.NextItemData.ClearFlags();

        PushID(label);
        bool setCurrentColorEditId = g.ColorEditCurrentID == 0;
        if (setCurrentColorEditId)
            g.ColorEditCurrentID = window.IDStack[^1];
        BeginGroup();

        if ((flags & ImGuiColorEditFlags.NoSidePreview) == 0)
            flags |= ImGuiColorEditFlags.NoSmallPreview;

        // Context menu: display and store options.
        if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
            ColorPickerOptionsPopup(col, flags);

        // Read stored options; the picker and input masks fall back to the compiled defaults when the
        // stored configuration has no mask set.
        if ((flags & ImGuiColorEditFlags.PickerMask_) == 0)
            flags |= ((g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.PickerMask_) != 0 ? g.IO.ConfigColorEditFlags : ImGuiColorEditFlags.DefaultOptions_) & ImGuiColorEditFlags.PickerMask_;
        if ((flags & ImGuiColorEditFlags.InputMask_) == 0)
            flags |= ((g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.InputMask_) != 0 ? g.IO.ConfigColorEditFlags : ImGuiColorEditFlags.DefaultOptions_) & ImGuiColorEditFlags.InputMask_;
        uint pickerMaskBits = (uint)(flags & ImGuiColorEditFlags.PickerMask_);
        ImAssert.Check(pickerMaskBits != 0 && (pickerMaskBits & (pickerMaskBits - 1)) == 0, "Only one Picker flag may be selected.");
        uint pickerInputMaskBits = (uint)(flags & ImGuiColorEditFlags.InputMask_);
        ImAssert.Check(pickerInputMaskBits != 0 && (pickerInputMaskBits & (pickerInputMaskBits - 1)) == 0, "Only one Input flag may be selected.");
        if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
            flags |= g.IO.ConfigColorEditFlags & ImGuiColorEditFlags.AlphaBar;

        // Setup.
        int components = (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 3 : 4;
        bool alphaBar = (flags & ImGuiColorEditFlags.AlphaBar) != 0 && (flags & ImGuiColorEditFlags.NoAlpha) == 0;
        ImVec2 pickerPos = window.DC.CursorPos;
        float squareSz = GetFrameHeight();
        float barsWidth = squareSz; // Arbitrary smallish width of the hue/alpha picking bars.
        float svPickerSize = MathF.Max(barsWidth, width - (alphaBar ? 2 : 1) * (barsWidth + style.ItemInnerSpacing.X));
        float bar0PosX = pickerPos.X + svPickerSize + style.ItemInnerSpacing.X;
        float bar1PosX = bar0PosX + barsWidth + style.ItemInnerSpacing.X;
        float barsTrianglesHalfSz = TruncateToInt(barsWidth * 0.20f);

        float[] backupInitialCol = new float[4];
        Array.Copy(col, backupInitialCol, components);

        float wheelThickness = svPickerSize * 0.08f;
        float wheelROuter = svPickerSize * 0.50f;
        float wheelRInner = wheelROuter - wheelThickness;
        ImVec2 wheelCenter = new(pickerPos.X + (svPickerSize + barsWidth) * 0.5f, pickerPos.Y + svPickerSize * 0.5f);

        // The triangle is displayed rotated with trianglePa pointing to the hue, but most coordinates
        // stay unrotated for the interaction logic.
        bool triangleRotate = (flags & ImGuiColorEditFlags.PickerNoRotate) == 0;
        float triangleR = wheelRInner - (int)(svPickerSize * 0.027f);
        ImVec2 trianglePa = triangleRotate ? new ImVec2(triangleR, 0.0f) : new ImVec2(triangleR * +0.866f, triangleR * +0.5f);
        ImVec2 trianglePb = triangleRotate ? new ImVec2(triangleR * -0.5f, triangleR * -0.866f) : new ImVec2(0.0f, -triangleR);
        ImVec2 trianglePc = triangleRotate ? new ImVec2(triangleR * -0.5f, triangleR * +0.866f) : new ImVec2(triangleR * -0.866f, triangleR * +0.5f);

        float h = col[0], s = col[1], v = col[2];
        float red = col[0], green = col[1], blue = col[2];
        if ((flags & ImGuiColorEditFlags.InputRGB) != 0)
        {
            // Hue is lost when converting from grayscale RGB, so it is restored explicitly.
            ColorConvertRGBtoHSV(red, green, blue, out h, out s, out v);
            ColorEditRestoreHS(col, ref h, ref s, ref v);
        }
        else if ((flags & ImGuiColorEditFlags.InputHSV) != 0)
        {
            ColorConvertHSVtoRGB(h, s, v, out red, out green, out blue);
        }

        bool valueChanged = false, valueChangedH = false, valueChangedSv = false;
        PushItemFlag(ImGuiItemFlags.NoNav, true);
        if ((flags & ImGuiColorEditFlags.PickerHueWheel) != 0)
        {
            // Hue wheel plus SV triangle interaction.
            InvisibleButton("hsv", new ImVec2(svPickerSize + style.ItemInnerSpacing.X + barsWidth, svPickerSize));
            if (IsItemActive() && !isReadonly)
            {
                ImVec2 initialOff = g.IO.MouseClickedPos[0] - wheelCenter;
                ImVec2 currentOff = g.IO.MousePos - wheelCenter;
                float initialDist2 = (initialOff.X * initialOff.X) + (initialOff.Y * initialOff.Y);
                if (initialDist2 >= (wheelRInner - 1) * (wheelRInner - 1) && initialDist2 <= (wheelROuter + 1) * (wheelROuter + 1))
                {
                    // Interactive with the hue wheel.
                    h = MathF.Atan2(currentOff.Y, currentOff.X) / MathF.PI * 0.5f;
                    if (h < 0.0f)
                        h += 1.0f;
                    valueChanged = valueChangedH = true;
                }
                float cosHueAngle = triangleRotate ? MathF.Cos(-h * 2.0f * MathF.PI) : 1.0f;
                float sinHueAngle = triangleRotate ? MathF.Sin(-h * 2.0f * MathF.PI) : 0.0f;
                if (ImTriangleContainsPoint(trianglePa, trianglePb, trianglePc, ImRotate(initialOff, cosHueAngle, sinHueAngle)))
                {
                    // Interacting with the SV triangle.
                    ImVec2 currentOffUnrotated = ImRotate(currentOff, cosHueAngle, sinHueAngle);
                    if (!ImTriangleContainsPoint(trianglePa, trianglePb, trianglePc, currentOffUnrotated))
                        currentOffUnrotated = ImTriangleClosestPoint(trianglePa, trianglePb, trianglePc, currentOffUnrotated);
                    ImVec2 bary = ImTriangleBarycentricCoords(trianglePa, trianglePb, trianglePc, currentOffUnrotated);
                    float vv = bary.X;
                    float uu = 1.0f - bary.X - bary.Y;
                    v = ClampValue(1.0f - vv, 0.0001f, 1.0f);
                    s = ClampValue(uu / v, 0.0001f, 1.0f);
                    valueChanged = valueChangedSv = true;
                }
            }
            if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
                OpenPopupOnItemClick("context", ImGuiPopupFlags.MouseButtonRight);
        }
        else if ((flags & ImGuiColorEditFlags.PickerHueBar) != 0)
        {
            // SV rectangle interaction.
            InvisibleButton("sv", new ImVec2(svPickerSize, svPickerSize));
            if (IsItemActive() && !isReadonly)
            {
                s = ColorConvertSaturate((io.MousePos.X - pickerPos.X) / (svPickerSize - 1));
                v = 1.0f - ColorConvertSaturate((io.MousePos.Y - pickerPos.Y) / (svPickerSize - 1));
                ColorEditRestoreH(col, ref h); // Greatly reduces hue jitter while the SV square is dragged.
                valueChanged = valueChangedSv = true;
            }
            if ((flags & ImGuiColorEditFlags.NoOptions) == 0)
                OpenPopupOnItemClick("context", ImGuiPopupFlags.MouseButtonRight);

            // Hue bar interaction.
            SetCursorScreenPos(new ImVec2(bar0PosX, pickerPos.Y));
            InvisibleButton("hue", new ImVec2(barsWidth, svPickerSize));
            if (IsItemActive() && !isReadonly)
            {
                h = ColorConvertSaturate((io.MousePos.Y - pickerPos.Y) / (svPickerSize - 1));
                valueChanged = valueChangedH = true;
            }
        }

        // Alpha bar interaction. Canonical deliberately omits the read-only check here.
        if (alphaBar)
        {
            SetCursorScreenPos(new ImVec2(bar1PosX, pickerPos.Y));
            InvisibleButton("alpha", new ImVec2(barsWidth, svPickerSize));
            if (IsItemActive())
            {
                col[3] = 1.0f - ColorConvertSaturate((io.MousePos.Y - pickerPos.Y) / (svPickerSize - 1));
                valueChanged = true;
            }
        }
        PopItemFlag(); // ImGuiItemFlags_NoNav.

        if ((flags & ImGuiColorEditFlags.NoSidePreview) == 0)
        {
            SameLine(0.0f, style.ItemInnerSpacing.X);
            BeginGroup();
        }

        if ((flags & ImGuiColorEditFlags.NoLabel) == 0)
        {
            string labelDisplayEnd = FindRenderedTextEnd(label);
            if (labelDisplayEnd.Length > 0)
            {
                if ((flags & ImGuiColorEditFlags.NoSidePreview) != 0)
                    SameLine(0.0f, style.ItemInnerSpacing.X);
                TextEx(label, labelDisplayEnd);
            }
        }

        if ((flags & ImGuiColorEditFlags.NoSidePreview) == 0)
        {
            PushItemFlag(ImGuiItemFlags.NoNavDefaultFocus, true);
            ImVec4 colV4 = new(col[0], col[1], col[2], (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 1.0f : col[3]);
            if ((flags & ImGuiColorEditFlags.NoLabel) != 0)
                Text("Current");

            ImGuiColorEditFlags subFlagsToForward = ImGuiColorEditFlags.InputMask_ | ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.AlphaMask_ | ImGuiColorEditFlags.NoTooltip;
            ColorButton("##current", colV4, flags & subFlagsToForward, new ImVec2(squareSz * 3, squareSz * 2));
            if (refCol.HasValue)
            {
                Text("Original");
                ImVec4 refColV4 = new(refCol.Value.X, refCol.Value.Y, refCol.Value.Z, (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 1.0f : refCol.Value.W);
                if (ColorButton("##original", refColV4, flags & subFlagsToForward, new ImVec2(squareSz * 3, squareSz * 2)))
                {
                    col[0] = refCol.Value.X;
                    col[1] = refCol.Value.Y;
                    col[2] = refCol.Value.Z;
                    if (components == 4)
                        col[3] = refCol.Value.W;
                    valueChanged = true;
                }
            }
            PopItemFlag();
            EndGroup();
        }

        // Convert the hue and SV state back to RGB.
        if (valueChangedH || valueChangedSv)
        {
            if ((flags & ImGuiColorEditFlags.InputRGB) != 0)
            {
                ColorConvertHSVtoRGB(h, s, v, out float hsvR, out float hsvG, out float hsvB);
                col[0] = hsvR;
                col[1] = hsvG;
                col[2] = hsvB;
                g.ColorEditSavedHue = h;
                g.ColorEditSavedSat = s;
                g.ColorEditSavedID = g.ColorEditCurrentID;
                g.ColorEditSavedColor = ColorConvertFloat4ToU32(new ImVec4(col[0], col[1], col[2], 0.0f));
            }
            else if ((flags & ImGuiColorEditFlags.InputHSV) != 0)
            {
                col[0] = h;
                col[1] = s;
                col[2] = v;
            }
        }

        // R,G,B and H,S,V slider color editor.
        bool valueChangedFixHueWrap = false;
        if ((flags & ImGuiColorEditFlags.NoInputs) == 0)
        {
            PushItemWidth((alphaBar ? bar1PosX : bar0PosX) + barsWidth - pickerPos.X);
            ImGuiColorEditFlags subFlagsToForward = ImGuiColorEditFlags.DataTypeMask_ | ImGuiColorEditFlags.InputMask_ | ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.AlphaMask_ | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoSmallPreview;
            ImGuiColorEditFlags subFlags = (flags & subFlagsToForward) | ImGuiColorEditFlags.NoPicker;
            if ((flags & ImGuiColorEditFlags.DisplayRGB) != 0 || (flags & ImGuiColorEditFlags.DisplayMask_) == 0)
            {
                if (ColorEdit4("##rgb", col, subFlags | ImGuiColorEditFlags.DisplayRGB))
                {
                    // The drag path is distinguished from the text-input/drop-target path because the
                    // latter must not run the hue-wrap cancellation.
                    valueChangedFixHueWrap = g.ActiveId != 0 && !g.ActiveIdAllowOverlap;
                    valueChanged = true;
                }
            }
            if ((flags & ImGuiColorEditFlags.DisplayHSV) != 0 || (flags & ImGuiColorEditFlags.DisplayMask_) == 0)
                valueChanged |= ColorEdit4("##hsv", col, subFlags | ImGuiColorEditFlags.DisplayHSV);
            if ((flags & ImGuiColorEditFlags.DisplayHex) != 0 || (flags & ImGuiColorEditFlags.DisplayMask_) == 0)
                valueChanged |= ColorEdit4("##hex", col, subFlags | ImGuiColorEditFlags.DisplayHex);
            PopItemWidth();
        }

        // Try to cancel the hue wrap after the ColorEdit4 call.
        if (valueChangedFixHueWrap && (flags & ImGuiColorEditFlags.InputRGB) != 0)
        {
            ColorConvertRGBtoHSV(col[0], col[1], col[2], out float newH, out float newS, out float newV);
            if (newH <= 0.0f && h > 0.0f)
            {
                if (newV <= 0.0f && v != newV)
                {
                    ColorConvertHSVtoRGB(h, s, newV <= 0.0f ? v * 0.5f : newV, out float fixR, out float fixG, out float fixB);
                    col[0] = fixR;
                    col[1] = fixG;
                    col[2] = fixB;
                }
                else if (newS <= 0.0f)
                {
                    ColorConvertHSVtoRGB(h, newS <= 0.0f ? s * 0.5f : newS, newV, out float fixR, out float fixG, out float fixB);
                    col[0] = fixR;
                    col[1] = fixG;
                    col[2] = fixB;
                }
            }
        }

        if (valueChanged)
        {
            if ((flags & ImGuiColorEditFlags.InputRGB) != 0)
            {
                red = col[0];
                green = col[1];
                blue = col[2];
                ColorConvertRGBtoHSV(red, green, blue, out h, out s, out v);
                ColorEditRestoreHS(col, ref h, ref s, ref v); // Fix the local hue the display below uses.
            }
            else if ((flags & ImGuiColorEditFlags.InputHSV) != 0)
            {
                h = col[0];
                s = col[1];
                v = col[2];
                ColorConvertHSVtoRGB(h, s, v, out red, out green, out blue);
            }
        }

        int styleAlpha8 = (int)(ColorConvertSaturate(style.Alpha) * 255.0f + 0.5f);
        uint colBlack = ImColor.Pack((byte)0, (byte)0, (byte)0, (byte)styleAlpha8);
        uint colWhite = ImColor.Pack((byte)255, (byte)255, (byte)255, (byte)styleAlpha8);
        uint colMidgrey = ImColor.Pack((byte)128, (byte)128, (byte)128, (byte)styleAlpha8);
        uint[] colHues =
        {
            ImColor.Pack((byte)255, (byte)0, (byte)0, (byte)styleAlpha8),
            ImColor.Pack((byte)255, (byte)255, (byte)0, (byte)styleAlpha8),
            ImColor.Pack((byte)0, (byte)255, (byte)0, (byte)styleAlpha8),
            ImColor.Pack((byte)0, (byte)255, (byte)255, (byte)styleAlpha8),
            ImColor.Pack((byte)0, (byte)0, (byte)255, (byte)styleAlpha8),
            ImColor.Pack((byte)255, (byte)0, (byte)255, (byte)styleAlpha8),
            ImColor.Pack((byte)255, (byte)0, (byte)0, (byte)styleAlpha8),
        };

        ImVec4 hueColorF = new(1.0f, 1.0f, 1.0f, style.Alpha);
        ColorConvertHSVtoRGB(h, 1.0f, 1.0f, out float hueR, out float hueG, out float hueB);
        hueColorF.X = hueR;
        hueColorF.Y = hueG;
        hueColorF.Z = hueB;
        uint hueColor32 = ColorConvertFloat4ToU32(hueColorF);
        uint userCol32StripedOfAlpha = ColorConvertFloat4ToU32(new ImVec4(red, green, blue, style.Alpha)); // Includes the style alpha.

        ImVec2 svCursorPos = default;
        if ((flags & ImGuiColorEditFlags.PickerHueWheel) != 0)
        {
            // Render the hue wheel.
            float aeps = 0.5f / wheelROuter; // Half a pixel arc length in radians.
            int segmentPerArc = Math.Max(4, (int)wheelROuter / 12);
            for (int n = 0; n < 6; n++)
            {
                float a0 = n / 6.0f * 2.0f * MathF.PI - aeps;
                float a1 = (n + 1.0f) / 6.0f * 2.0f * MathF.PI + aeps;
                int vertStartIdx = drawList.VtxBuffer.Count;
                drawList.PathArcTo(wheelCenter, (wheelRInner + wheelROuter) * 0.5f, a0, a1, segmentPerArc);
                drawList.PathStroke(colWhite, wheelThickness);
                int vertEndIdx = drawList.VtxBuffer.Count;

                // Paint the hue gradient over the arc's freshly stroked vertices. The gradient axis
                // runs between the inner-radius points at the two arc angles, matching canonical
                ImVec2 gradientP0 = new(wheelCenter.X + MathF.Cos(a0) * wheelRInner, wheelCenter.Y + MathF.Sin(a0) * wheelRInner);
                ImVec2 gradientP1 = new(wheelCenter.X + MathF.Cos(a1) * wheelRInner, wheelCenter.Y + MathF.Sin(a1) * wheelRInner);
                ShadeVertsLinearColorGradientKeepAlpha(drawList, vertStartIdx, vertEndIdx, gradientP0, gradientP1, colHues[n], colHues[n + 1]);
            }

            // Render the cursor and preview on the hue wheel.
            float cosHueAngle = MathF.Cos(h * 2.0f * MathF.PI);
            float sinHueAngle = MathF.Sin(h * 2.0f * MathF.PI);
            ImVec2 hueCursorPos = new(wheelCenter.X + cosHueAngle * (wheelRInner + wheelROuter) * 0.5f, wheelCenter.Y + sinHueAngle * (wheelRInner + wheelROuter) * 0.5f);
            float hueCursorRad = valueChangedH ? wheelThickness * 0.65f : wheelThickness * 0.55f;
            int hueCursorSegments = drawList.CalcCircleAutoSegmentCount(hueCursorRad);
            drawList.AddCircleFilled(hueCursorPos, hueCursorRad, hueColor32, hueCursorSegments);
            drawList.AddCircle(hueCursorPos, hueCursorRad + 1, colMidgrey, hueCursorSegments);
            drawList.AddCircle(hueCursorPos, hueCursorRad, colWhite, hueCursorSegments);
            if (!triangleRotate)
            {
                cosHueAngle = 1.0f;
                sinHueAngle = 0.0f;
            }

            // Render the SV triangle (rotated according to the hue). Canonical fills it with three
            // per-vertex colors through PrimVtx(); that draw primitive is not yet ported, so only the
            // mid-grey outline below is emitted.
            ImVec2 tra = wheelCenter + ImRotate(trianglePa, cosHueAngle, sinHueAngle);
            ImVec2 trb = wheelCenter + ImRotate(trianglePb, cosHueAngle, sinHueAngle);
            ImVec2 trc = wheelCenter + ImRotate(trianglePc, cosHueAngle, sinHueAngle);
            drawList.AddTriangle(tra, trb, trc, colMidgrey, 1.5f);
            svCursorPos = LerpVector(LerpVector(trc, tra, ColorConvertSaturate(s)), trb, ColorConvertSaturate(1.0f - v));
        }
        else if ((flags & ImGuiColorEditFlags.PickerHueBar) != 0)
        {
            // Render the SV square.
            drawList.AddRectFilledMultiColor(pickerPos, pickerPos + new ImVec2(svPickerSize, svPickerSize), colWhite, hueColor32, hueColor32, colWhite);
            drawList.AddRectFilledMultiColor(pickerPos, pickerPos + new ImVec2(svPickerSize, svPickerSize), 0, 0, colBlack, colBlack);
            RenderFrameBorder(pickerPos, pickerPos + new ImVec2(svPickerSize, svPickerSize), 0.0f);
            // Clamp the cursor so the preview circle does not stick too far outside the square.
            svCursorPos.X = ClampValue(RoundToInt(pickerPos.X + ColorConvertSaturate(s) * svPickerSize), pickerPos.X + 2, pickerPos.X + svPickerSize - 2);
            svCursorPos.Y = ClampValue(RoundToInt(pickerPos.Y + ColorConvertSaturate(1.0f - v) * svPickerSize), pickerPos.Y + 2, pickerPos.Y + svPickerSize - 2);

            // Render the hue bar.
            for (int n = 0; n < 6; ++n)
                drawList.AddRectFilledMultiColor(new ImVec2(bar0PosX, pickerPos.Y + n * (svPickerSize / 6)), new ImVec2(bar0PosX + barsWidth, pickerPos.Y + (n + 1) * (svPickerSize / 6)), colHues[n], colHues[n], colHues[n + 1], colHues[n + 1]);
            float bar0LineY = RoundToInt(pickerPos.Y + h * svPickerSize);
            RenderFrameBorder(new ImVec2(bar0PosX, pickerPos.Y), new ImVec2(bar0PosX + barsWidth, pickerPos.Y + svPickerSize), 0.0f);
            RenderArrowsForVerticalBar(drawList, new ImVec2(bar0PosX - 1, bar0LineY), new ImVec2(barsTrianglesHalfSz + 1, barsTrianglesHalfSz), barsWidth + 2.0f, style.Alpha);
        }

        // Render the cursor/preview circle. S and V are saturated because floating-point colors may
        // produce out-of-range HSV values.
        float svCursorRad = valueChangedSv ? wheelThickness * 0.55f : wheelThickness * 0.40f;
        int svCursorSegments = drawList.CalcCircleAutoSegmentCount(svCursorRad);
        drawList.AddCircleFilled(svCursorPos, svCursorRad, userCol32StripedOfAlpha, svCursorSegments);
        drawList.AddCircle(svCursorPos, svCursorRad + 1, colMidgrey, svCursorSegments);
        drawList.AddCircle(svCursorPos, svCursorRad, colWhite, svCursorSegments);

        // Render the alpha bar.
        if (alphaBar)
        {
            float alphaValue = ColorConvertSaturate(col[3]);
            ImRect bar1Bb = new(bar1PosX, pickerPos.Y, bar1PosX + barsWidth, pickerPos.Y + svPickerSize);
            RenderColorRectWithAlphaCheckerboard(drawList, bar1Bb.Min, bar1Bb.Max, 0, bar1Bb.GetWidth() / 2.0f, new ImVec2(0.0f, 0.0f));
            drawList.AddRectFilledMultiColor(bar1Bb.Min, bar1Bb.Max, userCol32StripedOfAlpha, userCol32StripedOfAlpha, userCol32StripedOfAlpha & ~0xFF000000u, userCol32StripedOfAlpha & ~0xFF000000u);
            float bar1LineY = RoundToInt(pickerPos.Y + (1.0f - alphaValue) * svPickerSize);
            RenderFrameBorder(bar1Bb.Min, bar1Bb.Max, 0.0f);
            RenderArrowsForVerticalBar(drawList, new ImVec2(bar1PosX - 1, bar1LineY), new ImVec2(barsTrianglesHalfSz + 1, barsTrianglesHalfSz), barsWidth + 2.0f, style.Alpha);
        }

        EndGroup();

        bool sameAsBackup = true;
        for (int n = 0; n < components; n++)
            sameAsBackup &= backupInitialCol[n] == col[n];
        if (valueChanged && sameAsBackup)
            valueChanged = false;
        if (valueChanged && g.LastItemData.ID != 0)
            MarkItemEdited(g.LastItemData.ID);

        if (setCurrentColorEditId)
            g.ColorEditCurrentID = 0;
        PopID();

        return valueChanged;
    }

    /// <summary>
    /// Displays a small color square that acts as a button, a drag-and-drop source, and a hover
    /// tooltip. The description string is used only for the identifier and the tooltip title.
    /// (.)
    /// </summary>
    /// <param name="descId">Identifier text; never rendered beside the square.</param>
    /// <param name="col">The displayed color; interpreted as HSV when <see cref="ImGuiColorEditFlags.InputHSV"/> is set.</param>
    /// <param name="flags">Color-button flags.</param>
    /// <param name="sizeArg">Requested square size; a zero component selects the frame height.</param>
    /// <returns><see langword="true"/> when the button was pressed.</returns>
    /// <remarks>
    /// The button is also a standardized drag source for color payloads: while it is the active item it
    /// publishes <c>IMGUI_PAYLOAD_TYPE_COLOR_3F</c> or <c>IMGUI_PAYLOAD_TYPE_COLOR_4F</c> once at drag
    /// start, with the RGB components already converted out of HSV when
    /// <see cref="ImGuiColorEditFlags.InputHSV"/> is set.
    /// </remarks>
    public static bool ColorButton(string descId, ImVec4 col, ImGuiColorEditFlags flags = 0, ImVec2 sizeArg = default)
    {
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return false;

        ImGuiContext g = GetCurrentContext()!;
        uint id = GetID(descId);
        float defaultSize = GetFrameHeight();
        ImVec2 size = new(sizeArg.X == 0.0f ? defaultSize : sizeArg.X, sizeArg.Y == 0.0f ? defaultSize : sizeArg.Y);
        ImRect bb = new(window.DC.CursorPos, window.DC.CursorPos + size);
        ItemSize(bb, size.Y >= defaultSize ? g.Style.FramePadding.Y : 0.0f);
        if (!ItemAdd(bb, id))
            return false;

        bool pressed = ButtonBehavior(bb, id, out bool hovered, out _);

        if ((flags & (ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.AlphaOpaque)) != 0)
            flags &= ~(ImGuiColorEditFlags.AlphaNoBg | ImGuiColorEditFlags.AlphaPreviewHalf);

        ImVec4 colRgb = col;
        if ((flags & ImGuiColorEditFlags.InputHSV) != 0)
        {
            ColorConvertHSVtoRGB(colRgb.X, colRgb.Y, colRgb.Z, out float convR, out float convG, out float convB);
            colRgb.X = convR;
            colRgb.Y = convG;
            colRgb.Z = convB;
        }

        ImVec4 colRgbWithoutAlpha = new(colRgb.X, colRgb.Y, colRgb.Z, 1.0f);
        float gridStep = MathF.Min(size.X, size.Y) / 2.99f;
        float rounding = MathF.Min(g.Style.FrameRounding, gridStep * 0.5f);
        ImRect bbInner = bb;
        float off = 0.0f;
        if ((flags & ImGuiColorEditFlags.NoBorder) == 0)
        {
            // The border uses Col_FrameBg and looks off when the color is near-opaque, so the fill is
            // shrunk by a fraction of a pixel to reduce those artifacts.
            off = -0.75f;
            bbInner.Expand(off);
        }

        bool isMixed = (g.LastItemData.ItemFlags & ImGuiItemFlags.MixedValue) != 0;
        if (isMixed)
        {
            window.DrawList.AddRectFilled(bb.Min, bb.Max, GetColorU32(ImGuiCol.FrameBg), rounding);
            PushStyleColor(ImGuiCol.Text, g.Style.Colors[(int)ImGuiCol.TextMixedValue]);
            if (g.MixedValueLabel != null)
                RenderTextClipped(new ImVec2(bb.Min.X, bb.Min.Y + g.Style.FramePadding.Y), bb.Max, g.MixedValueLabel, null, null, new ImVec2(0.5f, 0.0f));
            PopStyleColor();
        }
        else if ((flags & ImGuiColorEditFlags.AlphaPreviewHalf) != 0 && colRgb.W < 1.0f)
        {
            float midX = RoundToInt((bbInner.Min.X + bbInner.Max.X) * 0.5f);
            if ((flags & ImGuiColorEditFlags.AlphaNoBg) == 0)
                RenderColorRectWithAlphaCheckerboard(window.DrawList, new ImVec2(bbInner.Min.X + gridStep, bbInner.Min.Y), bbInner.Max, GetColorU32(colRgb), gridStep, new ImVec2(-gridStep + off, off), rounding, ImDrawFlags.RoundCornersRight);
            else
                window.DrawList.AddRectFilled(new ImVec2(bbInner.Min.X + gridStep, bbInner.Min.Y), bbInner.Max, GetColorU32(colRgb), rounding, ImDrawFlags.RoundCornersRight);
            window.DrawList.AddRectFilled(bbInner.Min, new ImVec2(midX, bbInner.Max.Y), GetColorU32(colRgbWithoutAlpha), rounding, ImDrawFlags.RoundCornersLeft);
        }
        else
        {
            // GetColorU32() multiplies by the global style alpha, so an opaque source must not display
            // a checkerboard.
            ImVec4 colSource = (flags & ImGuiColorEditFlags.AlphaOpaque) != 0 ? colRgbWithoutAlpha : colRgb;
            if (colSource.W < 1.0f && (flags & ImGuiColorEditFlags.AlphaNoBg) == 0)
                RenderColorRectWithAlphaCheckerboard(window.DrawList, bbInner.Min, bbInner.Max, GetColorU32(colSource), gridStep, new ImVec2(off, off), rounding);
            else
                window.DrawList.AddRectFilled(bbInner.Min, bbInner.Max, GetColorU32(colSource), rounding);
        }

        RenderNavCursor(bb, id);
        if ((flags & ImGuiColorEditFlags.NoBorder) == 0)
        {
            if (g.Style.FrameBorderSize > 0.0f)
                RenderFrameBorder(bb.Min, bb.Max, rounding);
            else
                window.DrawList.AddRect(bb.Min, bb.Max, GetColorU32(ImGuiCol.FrameBg), rounding, 1.0f * (float)(int)g.Style._MainScale);
        }

        // Drag and Drop Source. The ActiveId test is an optional
        // micro-optimization because BeginDragDropSource() performs the same check.
        if (g.ActiveId == id && (flags & ImGuiColorEditFlags.NoDragDrop) == 0 && BeginDragDropSource())
        {
            // The managed payload path carries a float[] because the port has no core byte-copy of raw
            // pointers; the target reads the same array shape.
            if ((flags & ImGuiColorEditFlags.NoAlpha) != 0)
                SetDragDropPayload(IMGUI_PAYLOAD_TYPE_COLOR_3F, new[] { colRgb.X, colRgb.Y, colRgb.Z }, ImGuiCond.Once);
            else
                SetDragDropPayload(IMGUI_PAYLOAD_TYPE_COLOR_4F, new[] { colRgb.X, colRgb.Y, colRgb.Z, colRgb.W }, ImGuiCond.Once);
            ColorButton(descId, col, flags);
            SameLine();
            TextEx("Color");
            EndDragDropSource();
        }

        if ((flags & ImGuiColorEditFlags.NoTooltip) == 0 && hovered && IsItemHovered(ImGuiHoveredFlags.ForTooltip))
            ColorTooltip(descId, col, flags & (ImGuiColorEditFlags.InputMask_ | ImGuiColorEditFlags.AlphaMask_));

        return pressed;
    }

    /// <summary>
    /// Stores the color-edit display, data-type, picker, and input defaults that widgets without an
    /// explicit mask pick up from the context.
    /// (.)
    /// </summary>
    /// <param name="flags">The replacement options; an absent category falls back to the compiled default.</param>
    /// <remarks>
    /// Canonical wraps this obsolete entry point in <c>#ifndef IMGUI_DISABLE_OBSOLETE_FUNCTIONS</c>. The
    /// port has no equivalent compile-time switch, so the function is always available.
    /// </remarks>
    public static void SetColorEditOptions(ImGuiColorEditFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        if ((flags & ImGuiColorEditFlags.DisplayMask_) == 0)
            flags |= ImGuiColorEditFlags.DefaultOptions_ & ImGuiColorEditFlags.DisplayMask_;
        if ((flags & ImGuiColorEditFlags.DataTypeMask_) == 0)
            flags |= ImGuiColorEditFlags.DefaultOptions_ & ImGuiColorEditFlags.DataTypeMask_;
        if ((flags & ImGuiColorEditFlags.PickerMask_) == 0)
            flags |= ImGuiColorEditFlags.DefaultOptions_ & ImGuiColorEditFlags.PickerMask_;
        if ((flags & ImGuiColorEditFlags.InputMask_) == 0)
            flags |= ImGuiColorEditFlags.DefaultOptions_ & ImGuiColorEditFlags.InputMask_;
        g.IO.ConfigColorEditFlags = flags;
    }

    /// <summary>
    /// Appends the hover tooltip that describes a color component as hexadecimal, integer, and
    /// floating-point tuples.
    /// (.)
    /// </summary>
    /// <param name="text">Optional title text rendered above a separator.</param>
    /// <param name="col">The color to describe. Canonical passes a
    /// <c>const float*</c>; the port passes the existing <see cref="ImVec4"/> value so no array is
    /// allocated on the hover path.</param>
    /// <param name="flags">The input and alpha flags forwarded to the preview button.</param>
    /// <remarks>
    /// The port reads only the first three components for the RGB description and adds the fourth only
    /// when alpha is enabled, matching canonical's conditional access.
    /// </remarks>
    internal static void ColorTooltip(string text, ImVec4 col, ImGuiColorEditFlags flags)
    {
        ImGuiContext g = GetCurrentContext()!;
        if (!BeginTooltipEx(ImGuiTooltipFlags.OverridePrevious, ImGuiWindowFlags.None))
            return;
        string textEnd = text.Length > 0 ? FindRenderedTextEnd(text) : text;
        if (textEnd.Length > 0)
        {
            TextEx(text, textEnd);
            Separator();
        }

        ImVec2 sz = new(g.FontSize * 3 + g.Style.FramePadding.Y * 2, g.FontSize * 3 + g.Style.FramePadding.Y * 2);
        ImVec4 cf = new(col.X, col.Y, col.Z, (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 1.0f : col.W);
        int cr = (int)(ColorConvertSaturate(col.X) * 255.0f + 0.5f);
        int cg = (int)(ColorConvertSaturate(col.Y) * 255.0f + 0.5f);
        int cb = (int)(ColorConvertSaturate(col.Z) * 255.0f + 0.5f);
        int ca = (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 255 : (int)(ColorConvertSaturate(col.W) * 255.0f + 0.5f);
        ImGuiColorEditFlags flagsToForward = ImGuiColorEditFlags.InputMask_ | ImGuiColorEditFlags.AlphaMask_;
        ColorButton("##preview", cf, (flags & flagsToForward) | ImGuiColorEditFlags.NoTooltip, sz);
        SameLine();
        if ((flags & ImGuiColorEditFlags.InputRGB) != 0 || (flags & ImGuiColorEditFlags.InputMask_) == 0)
        {
            if ((flags & ImGuiColorEditFlags.NoAlpha) != 0)
                Text("#%02X%02X%02X\nR: %d, G: %d, B: %d\n(%.3f, %.3f, %.3f)", cr, cg, cb, cr, cg, cb, col.X, col.Y, col.Z);
            else
                Text("#%02X%02X%02X%02X\nR:%d, G:%d, B:%d, A:%d\n(%.3f, %.3f, %.3f, %.3f)", cr, cg, cb, ca, cr, cg, cb, ca, col.X, col.Y, col.Z, col.W);
        }
        else if ((flags & ImGuiColorEditFlags.InputHSV) != 0)
        {
            if ((flags & ImGuiColorEditFlags.NoAlpha) != 0)
                Text("H: %.3f, S: %.3f, V: %.3f", col.X, col.Y, col.Z);
            else
                Text("H: %.3f, S: %.3f, V: %.3f, A: %.3f", col.X, col.Y, col.Z, col.W);
        }
        EndTooltip();
    }

    /// <summary>
    /// Builds the right-click context menu that selects the display mode and data type and offers
    /// clipboard copy formats for the color being edited.
    /// (.)
    /// </summary>
    /// <param name="col">The color whose copy strings are generated.</param>
    /// <param name="flags">The widget flags; a present display or data-type mask disables that group.</param>
    internal static void ColorEditOptionsPopup(float[] col, ImGuiColorEditFlags flags)
    {
        bool allowOptInputs = (flags & ImGuiColorEditFlags.DisplayMask_) == 0;
        bool allowOptDataType = (flags & ImGuiColorEditFlags.DataTypeMask_) == 0;
        if ((!allowOptInputs && !allowOptDataType) || !BeginPopup("context"))
            return;

        ImGuiContext g = GetCurrentContext()!;
        PushItemFlag((ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoMarkEdited, true);
        ImGuiColorEditFlags opts = g.IO.ConfigColorEditFlags;
        if (allowOptInputs)
        {
            if (RadioButton("RGB", (opts & ImGuiColorEditFlags.DisplayRGB) != 0))
                opts = (opts & ~ImGuiColorEditFlags.DisplayMask_) | ImGuiColorEditFlags.DisplayRGB;
            if (RadioButton("HSV", (opts & ImGuiColorEditFlags.DisplayHSV) != 0))
                opts = (opts & ~ImGuiColorEditFlags.DisplayMask_) | ImGuiColorEditFlags.DisplayHSV;
            if (RadioButton("Hex", (opts & ImGuiColorEditFlags.DisplayHex) != 0))
                opts = (opts & ~ImGuiColorEditFlags.DisplayMask_) | ImGuiColorEditFlags.DisplayHex;
        }
        if (allowOptDataType)
        {
            if (allowOptInputs)
                Separator();
            if (RadioButton("0..255", (opts & ImGuiColorEditFlags.Uint8) != 0))
                opts = (opts & ~ImGuiColorEditFlags.DataTypeMask_) | ImGuiColorEditFlags.Uint8;
            if (RadioButton("0.00..1.00", (opts & ImGuiColorEditFlags.Float) != 0))
                opts = (opts & ~ImGuiColorEditFlags.DataTypeMask_) | ImGuiColorEditFlags.Float;
        }

        if (allowOptInputs || allowOptDataType)
            Separator();
        if (Button("Copy as..", new ImVec2(-1, 0)))
            OpenPopup("Copy");
        if (BeginPopup("Copy"))
        {
            int cr = (int)(ColorConvertSaturate(col[0]) * 255.0f + 0.5f);
            int cg = (int)(ColorConvertSaturate(col[1]) * 255.0f + 0.5f);
            int cb = (int)(ColorConvertSaturate(col[2]) * 255.0f + 0.5f);
            int ca = (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 255 : (int)(ColorConvertSaturate(col[3]) * 255.0f + 0.5f);
            string buf = ImFormatString("(%.3ff, %.3ff, %.3ff, %.3ff)", col[0], col[1], col[2], (flags & ImGuiColorEditFlags.NoAlpha) != 0 ? 1.0f : col[3]);
            if (Selectable(buf))
                SetClipboardText(buf);
            buf = ImFormatString("(%d,%d,%d,%d)", cr, cg, cb, ca);
            if (Selectable(buf))
                SetClipboardText(buf);
            buf = ImFormatString("#%02X%02X%02X", cr, cg, cb);
            if (Selectable(buf))
                SetClipboardText(buf);
            if ((flags & ImGuiColorEditFlags.NoAlpha) == 0)
            {
                buf = ImFormatString("#%02X%02X%02X%02X", cr, cg, cb, ca);
                if (Selectable(buf))
                    SetClipboardText(buf);
            }
            EndPopup();
        }

        g.IO.ConfigColorEditFlags = opts;
        PopItemFlag();
        EndPopup();
    }

    /// <summary>
    /// Builds the right-click context menu that selects the picker style (hue bar or hue wheel) and
    /// toggles the alpha bar, previewing each picker style with a nested color picker.
    /// (.)
    /// </summary>
    /// <param name="col">The color previewed in each nested picker.</param>
    /// <param name="flags">The widget flags; a present picker mask disables the thumbnail group.</param>
    internal static void ColorPickerOptionsPopup(float[] col, ImGuiColorEditFlags flags)
    {
        bool allowOptPicker = (flags & ImGuiColorEditFlags.PickerMask_) == 0;
        bool allowOptAlphaBar = (flags & ImGuiColorEditFlags.NoAlpha) == 0 && (flags & ImGuiColorEditFlags.AlphaBar) == 0;
        if ((!allowOptPicker && !allowOptAlphaBar) || !BeginPopup("context"))
            return;

        ImGuiContext g = GetCurrentContext()!;
        PushItemFlag((ImGuiItemFlags)ImGuiItemFlagsPrivate.ImGuiItemFlags_NoMarkEdited, true);
        if (allowOptPicker)
        {
            ImVec2 pickerSize = new(g.FontSize * 8, MathF.Max(g.FontSize * 8 - (GetFrameHeight() + g.Style.ItemInnerSpacing.X), 1.0f));
            PushItemWidth(pickerSize.X);
            for (int pickerType = 0; pickerType < 2; pickerType++)
            {
                // Draw a thumbnail of each picker type over an invisible button for selection.
                if (pickerType > 0)
                    Separator();
                PushID(pickerType);
                ImGuiColorEditFlags pickerFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoSidePreview | (flags & ImGuiColorEditFlags.NoAlpha);
                if (pickerType == 0)
                    pickerFlags |= ImGuiColorEditFlags.PickerHueBar;
                if (pickerType == 1)
                    pickerFlags |= ImGuiColorEditFlags.PickerHueWheel;
                ImVec2 backupPos = GetCursorScreenPos();
                if (Selectable("##selectable", false, 0, pickerSize))
                    g.IO.ConfigColorEditFlags = (g.IO.ConfigColorEditFlags & ~ImGuiColorEditFlags.PickerMask_) | (pickerFlags & ImGuiColorEditFlags.PickerMask_);
                SetCursorScreenPos(backupPos);
                float[] previewingRefCol = new float[4];
                Array.Copy(col, previewingRefCol, (pickerFlags & ImGuiColorEditFlags.NoAlpha) != 0 ? 3 : 4);
                ColorPicker4("##previewing_picker", previewingRefCol, pickerFlags);
                PopID();
            }
            PopItemWidth();
        }
        if (allowOptAlphaBar)
        {
            if (allowOptPicker)
                Separator();
            int configColorEditFlags = (int)g.IO.ConfigColorEditFlags;
            CheckboxFlags("Alpha Bar", ref configColorEditFlags, (int)ImGuiColorEditFlags.AlphaBar);
            g.IO.ConfigColorEditFlags = (ImGuiColorEditFlags)configColorEditFlags;
        }
        PopItemFlag();
        EndPopup();
    }

    /// <summary>
    /// Rounds a non-negative scalar to the nearest integer using the canonical <c>IM_ROUND</c> rule.
    /// </summary>
    /// <param name="value">The scalar to round.</param>
    /// <returns>The rounded scalar.</returns>
    private static float RoundToInt(float value)
    {
        return (float)(int)(value + 0.5f);
    }

    /// <summary>
    /// Parses one hexadecimal digit, matching the character set accepted by canonical <c>%X</c>.
    /// </summary>
    /// <param name="c">The character to inspect.</param>
    /// <param name="value">Receives the digit value zero through fifteen.</param>
    /// <returns><see langword="true"/> when the character is a hexadecimal digit.</returns>
    private static bool TryParseHexDigit(char c, out int value)
    {
        if (c >= '0' && c <= '9')
        {
            value = c - '0';
            return true;
        }
        if (c >= 'A' && c <= 'F')
        {
            value = c - 'A' + 10;
            return true;
        }
        if (c >= 'a' && c <= 'f')
        {
            value = c - 'a' + 10;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>
    /// Rotates a point by a precomputed cosine and sine pair, matching canonical <c>ImRotate</c>.
    /// </summary>
    /// <param name="p">The point to rotate.</param>
    /// <param name="cosA">The cosine of the rotation angle.</param>
    /// <param name="sinA">The sine of the rotation angle.</param>
    /// <returns>The rotated point.</returns>
    private static ImVec2 ImRotate(ImVec2 p, float cosA, float sinA)
    {
        return new ImVec2((p.X * cosA) - (p.Y * sinA), (p.X * sinA) + (p.Y * cosA));
    }

    /// <summary>
    /// Tests whether a point lies inside a triangle by comparing the signs of the three edge cross
    /// products, matching canonical <c>ImTriangleContainsPoint</c>.
    /// </summary>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="p">The point to test.</param>
    /// <returns><see langword="true"/> when the point is inside or on the triangle boundary.</returns>
    public static bool ImTriangleContainsPoint(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 p)
    {
        bool b1 = ((p.X - b.X) * (a.Y - b.Y)) - ((p.Y - b.Y) * (a.X - b.X)) < 0.0f;
        bool b2 = ((p.X - c.X) * (b.Y - c.Y)) - ((p.Y - c.Y) * (b.X - c.X)) < 0.0f;
        bool b3 = ((p.X - a.X) * (c.Y - a.Y)) - ((p.Y - a.Y) * (c.X - a.X)) < 0.0f;
        return b1 == b2 && b2 == b3;
    }

    /// <summary>
    /// Finds the point of a triangle closest to a query point, matching canonical
    /// <c>ImTriangleClosestPoint</c>.
    /// </summary>
    /// <param name="a">First triangle vertex.</param>
    /// <param name="b">Second triangle vertex.</param>
    /// <param name="c">Third triangle vertex.</param>
    /// <param name="p">The query point.</param>
    /// <returns>The closest point on the triangle boundary.</returns>
    public static ImVec2 ImTriangleClosestPoint(ImVec2 a, ImVec2 b, ImVec2 c, ImVec2 p)
    {
        ImVec2 projAb = ImLineClosestPoint(a, b, p);
        ImVec2 projBc = ImLineClosestPoint(b, c, p);
        ImVec2 projCa = ImLineClosestPoint(c, a, p);
        float dist2Ab = LengthSqr(p - projAb);
        float dist2Bc = LengthSqr(p - projBc);
        float dist2Ca = LengthSqr(p - projCa);
        float m = MathF.Min(dist2Ab, MathF.Min(dist2Bc, dist2Ca));
        if (m == dist2Ab)
            return projAb;
        if (m == dist2Bc)
            return projBc;
        return projCa;
    }

    /// <summary>
    /// Projects a point onto a segment, matching canonical <c>ImLineClosestPoint</c>.
    /// </summary>
    /// <param name="a">First segment endpoint.</param>
    /// <param name="b">Second segment endpoint.</param>
    /// <param name="p">The point to project.</param>
    /// <returns>The point of the segment closest to <paramref name="p"/>.</returns>
    public static ImVec2 ImLineClosestPoint(ImVec2 a, ImVec2 b, ImVec2 p)
    {
        ImVec2 ap = p - a;
        ImVec2 abDir = b - a;
        float dot = (ap.X * abDir.X) + (ap.Y * abDir.Y);
        if (dot < 0.0f)
            return a;
        float abLenSqr = LengthSqr(abDir);
        if (dot > abLenSqr)
            return b;
        return a + abDir * (dot / abLenSqr);
    }

    /// <summary>
    /// Computes the squared length of a vector, matching canonical <c>ImLengthSqr</c>.
    /// </summary>
    /// <param name="v">The vector to measure.</param>
    /// <returns>The sum of the squared components.</returns>
    private static float LengthSqr(ImVec2 v)
    {
        return (v.X * v.X) + (v.Y * v.Y);
    }

    #endregion

    #region [SECTION] Widgets: Typing-Select support

    /// <summary>
    /// Consumes the frame's queued character input into the typing-select search buffer and returns the
    /// request for the current frame, or <see langword="null"/> when the buffer is empty.
    /// (.)
    /// </summary>
    /// <param name="flags">Typing-select behavior flags.</param>
    /// <returns>
    /// The persistent typing-select request object populated for this frame, or <see langword="null"/>
    /// when nothing is buffered.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Calling this from several locations in one frame is safe: the state machine is idempotent except
    /// for backspace consumption.
    /// </para>
    /// <para>
    /// Canonical stores the buffer as a null-terminated UTF-8 byte array and reports byte lengths. The
    /// port stores a managed UTF-16 string, so <see cref="ImGuiTypingSelectRequest.SearchBufferLen"/>
    /// and <see cref="ImGuiTypingSelectRequest.SingleCharSize"/> count UTF-16 code units instead of UTF-8
    /// bytes; the codepoint-striding logic is otherwise identical.
    /// </para>
    /// <para>
    /// The canonical UTF-8 helpers (<c>ImTextCountUtf8BytesFromStr</c>, <c>ImTextCharToUtf8</c>,
    /// <c>ImTextFindPreviousUtf8Codepoint</c>, <c>ImCharIsBlankW</c>) are not present in the port, so
    /// their logic is inlined here with.NET string primitives.
    /// </para>
    /// </remarks>
    internal static ImGuiTypingSelectRequest? GetTypingSelectRequest(ImGuiTypingSelectFlags flags = 0)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiTypingSelectState data = g.TypingSelectState;
        ImGuiTypingSelectRequest outRequest = data.Request;

        // Clear the buffer.
        const float TypingSelectResetTimer = 1.80f;
        const int TypingSelectSingleCharCountForLock = 4;
        if (data.SearchBuffer.Length != 0)
        {
            bool clearBuffer = false;
            clearBuffer |= g.NavFocusScopeId != data.FocusScope;
            clearBuffer |= data.LastRequestTime + TypingSelectResetTimer < g.Time;
            clearBuffer |= g.NavAnyRequest;
            clearBuffer |= g.ActiveId != 0 && g.NavActivateId == 0; // Allows a temporary SPACE activation.
            clearBuffer |= IsKeyPressed(ImGuiKey.Escape) || IsKeyPressed(ImGuiKey.Enter);
            clearBuffer |= IsKeyPressed(ImGuiKey.Backspace) && (flags & ImGuiTypingSelectFlags.AllowBackspace) == 0;
            if (clearBuffer)
                data.Clear();
        }

        // Append the queued input characters.
        const int bufferMaxLen = 63; // sizeof(SearchBuffer) - 1.
        int bufferLen = data.SearchBuffer.Length;
        bool selectRequest = false;
        foreach (uint w in g.IO.InputQueueCharacters)
        {
            if (w < 32 || (w >= 0xD800 && w <= 0xDFFF) || w > 0x10FFFF)
                continue;
            string wBuf = char.ConvertFromUtf32((int)w);
            int wLen = wBuf.Length;
            if (bufferLen == 0 && (w == ' ' || w == '\t' || w == 0x3000)) // Ignore leading blanks.
                continue;
            if (bufferLen + wLen > bufferMaxLen)
                continue;
            if (data.SingleCharModeLock && wLen == outRequest.SingleCharSize && data.SearchBuffer.Length >= wLen && string.CompareOrdinal(wBuf, 0, data.SearchBuffer, 0, wLen) == 0)
            {
                selectRequest = true; // Same character: no need to append to the buffer.
                continue;
            }
            if (data.SingleCharModeLock)
            {
                data.Clear(); // Different character: clear.
                bufferLen = 0;
            }
            data.SearchBuffer = bufferLen == 0 ? wBuf : string.Concat(data.SearchBuffer, wBuf);
            bufferLen += wLen;
            selectRequest = true;
        }
        g.IO.InputQueueCharacters.Clear();

        // Handle backspace.
        if ((flags & ImGuiTypingSelectFlags.AllowBackspace) != 0 && IsKeyPressed(ImGuiKey.Backspace, ImGuiInputFlags.Repeat))
        {
            int previousLength = PreviousCodepointLength(data.SearchBuffer);
            data.SearchBuffer = data.SearchBuffer[..(data.SearchBuffer.Length - previousLength)];
            bufferLen = data.SearchBuffer.Length;
        }

        // Return the request if any.
        if (bufferLen == 0)
            return null;
        if (selectRequest)
        {
            data.FocusScope = g.NavFocusScopeId;
            data.LastRequestFrame = g.FrameCount;
            data.LastRequestTime = (float)g.Time;
        }
        outRequest.Flags = flags;
        outRequest.SearchBufferLen = bufferLen;
        outRequest.SearchBuffer = data.SearchBuffer;
        outRequest.SelectRequest = data.LastRequestFrame == g.FrameCount;
        outRequest.SingleCharMode = false;
        outRequest.SingleCharSize = 0;

        // Determine whether the buffer holds one character repeated. This is always true for the first
        // input character, because it usually leads to a "next match" request.
        if ((flags & ImGuiTypingSelectFlags.AllowSingleCharMode) != 0)
        {
            string buf = outRequest.SearchBuffer;
            int bufEnd = outRequest.SearchBufferLen;
            int c0Len = FirstCodepointLength(buf, bufEnd);
            int p = c0Len;
            for (; p + c0Len <= bufEnd; p += c0Len)
            {
                if (string.CompareOrdinal(buf, 0, buf, p, c0Len) != 0)
                    break;
            }
            int singleCharCount = p == bufEnd ? outRequest.SearchBufferLen / c0Len : 0;
            outRequest.SingleCharMode = singleCharCount > 0 || data.SingleCharModeLock;
            outRequest.SingleCharSize = c0Len;
            data.SingleCharModeLock |= singleCharCount >= TypingSelectSingleCharCountForLock; // Lock to single character mode.
        }

        return outRequest;
    }

    /// <summary>
    /// Returns the UTF-16 length of the first codepoint of a string, or one when the range is empty.
    /// </summary>
    /// <param name="text">The string to inspect.</param>
    /// <param name="length">The valid length of the string.</param>
    /// <returns>Two for a surrogate pair, otherwise one.</returns>
    private static int FirstCodepointLength(string text, int length)
    {
        if (length >= 2 && char.IsHighSurrogate(text[0]) && char.IsLowSurrogate(text[1]))
            return 2;
        return 1;
    }

    /// <summary>
    /// Returns the UTF-16 length of the last codepoint of a string.
    /// </summary>
    /// <param name="text">The string to inspect.</param>
    /// <returns>Two for a trailing surrogate pair, otherwise one; zero for an empty string.</returns>
    private static int PreviousCodepointLength(string text)
    {
        if (text.Length == 0)
            return 0;
        if (text.Length >= 2 && char.IsLowSurrogate(text[^1]) && char.IsHighSurrogate(text[^2]))
            return 2;
        return 1;
    }

    /// <summary>
    /// Returns the length of the shared case-insensitive prefix between a search-buffer range and an
    /// item name, matching canonical <c>ImStrimatchlen</c>.
    /// </summary>
    /// <param name="s1">The search buffer.</param>
    /// <param name="s1End">The exclusive index in <paramref name="s1"/> at which matching stops.</param>
    /// <param name="s2">The item name.</param>
    /// <returns>The number of matching characters.</returns>
    /// <remarks>
    /// Canonical uppercases single bytes with the ASCII-only <c>ImToUpper</c>; this port performs the
    /// same ASCII-only conversion so non-ASCII characters compare exactly.
    /// </remarks>
    private static int ImStrimatchlen(string s1, int s1End, string s2)
    {
        int matchLen = 0;
        int i = 0;
        while (i < s1End && i < s1.Length && i < s2.Length)
        {
            char c1 = s1[i];
            if (c1 >= 'a' && c1 <= 'z')
                c1 = (char)(c1 - 32);
            char c2 = s2[i];
            if (c2 >= 'a' && c2 <= 'z')
                c2 = (char)(c2 - 32);
            if (c1 != c2)
                break;
            matchLen++;
            i++;
        }
        return matchLen;
    }

    /// <summary>
    /// Finds the next item whose name starts with the single character held by the request, cycling
    /// forward from the currently selected item.
    /// (.)
    /// </summary>
    /// <param name="req">The request produced by <see cref="GetTypingSelectRequest"/>.</param>
    /// <param name="itemsCount">The number of candidate items.</param>
    /// <param name="getItemNameFunc">Returns the display name of the item at the given index.</param>
    /// <param name="userData">Opaque state forwarded to <paramref name="getItemNameFunc"/>.</param>
    /// <param name="navItemIdx">The index of the currently selected item, or -1 when unknown.</param>
    /// <returns>The matched index, or -1 when no item matches.</returns>
    /// <remarks>
    /// Canonical carries a FIXME about deriving <paramref name="navItemIdx"/> from
    /// <c>g.NavLastValidSelectionUserData</c> when it is -1; that behavior is not implemented
    /// canonically and is therefore not implemented here.
    /// </remarks>
    internal static int TypingSelectFindNextSingleCharMatch(ImGuiTypingSelectRequest req, int itemsCount, Func<object?, int, string> getItemNameFunc, object? userData, int navItemIdx)
    {
        int firstMatchIdx = -1;
        bool returnNextMatch = false;
        for (int idx = 0; idx < itemsCount; idx++)
        {
            string itemName = getItemNameFunc(userData, idx);
            if (ImStrimatchlen(req.SearchBuffer, req.SingleCharSize, itemName) < req.SingleCharSize)
                continue;
            if (returnNextMatch)
                return idx; // Return the next matching item after the current item.
            if (firstMatchIdx == -1 && navItemIdx == -1)
                return idx; // Return the first match immediately when there is no current item.
            if (firstMatchIdx == -1)
                firstMatchIdx = idx; // Record the first match for wrapping.
            if (navItemIdx == idx)
                returnNextMatch = true;
        }
        return firstMatchIdx;
    }

    /// <summary>
    /// Finds the item with the longest case-insensitive prefix match against the search buffer,
    /// matching canonical <c>TypingSelectFindBestLeadingMatch</c>.
    /// (.)
    /// </summary>
    /// <param name="req">The request produced by <see cref="GetTypingSelectRequest"/>.</param>
    /// <param name="itemsCount">The number of candidate items.</param>
    /// <param name="getItemNameFunc">Returns the display name of the item at the given index.</param>
    /// <param name="userData">Opaque state forwarded to <paramref name="getItemNameFunc"/>.</param>
    /// <returns>The best matching index, or -1 when no item matches.</returns>
    internal static int TypingSelectFindBestLeadingMatch(ImGuiTypingSelectRequest req, int itemsCount, Func<object?, int, string> getItemNameFunc, object? userData)
    {
        int longestMatchIdx = -1;
        int longestMatchLen = 0;
        for (int idx = 0; idx < itemsCount; idx++)
        {
            string itemName = getItemNameFunc(userData, idx);
            int matchLen = ImStrimatchlen(req.SearchBuffer, req.SearchBufferLen, itemName);
            if (matchLen <= longestMatchLen)
                continue;
            longestMatchIdx = idx;
            longestMatchLen = matchLen;
            if (matchLen == req.SearchBufferLen)
                break;
        }
        return longestMatchIdx;
    }

    /// <summary>
    /// Resolves a typing-select request to an item index by dispatching to the single-character or
    /// best-leading-prefix search, then makes the navigation cursor visible when a match was found.
    /// (.)
    /// </summary>
    /// <param name="req">
    /// The request produced by <see cref="GetTypingSelectRequest"/>, or <see langword="null"/> so that
    /// callers can invoke this unconditionally.
    /// </param>
    /// <param name="itemsCount">The number of candidate items.</param>
    /// <param name="getItemNameFunc">Returns the display name of the item at the given index.</param>
    /// <param name="userData">Opaque state forwarded to <paramref name="getItemNameFunc"/>.</param>
    /// <param name="navItemIdx">The index of the currently selected item, or -1 when unknown.</param>
    /// <returns>The matched index, or -1 when there is no active request or no match.</returns>
    internal static int TypingSelectFindMatch(ImGuiTypingSelectRequest? req, int itemsCount, Func<object?, int, string> getItemNameFunc, object? userData, int navItemIdx)
    {
        if (req == null || !req.SelectRequest)
            return -1;
        int idx;
        if (req.SingleCharMode && (req.Flags & ImGuiTypingSelectFlags.AllowSingleCharMode) != 0)
            idx = TypingSelectFindNextSingleCharMatch(req, itemsCount, getItemNameFunc, userData, navItemIdx);
        else
            idx = TypingSelectFindBestLeadingMatch(req, itemsCount, getItemNameFunc, userData);
        if (idx != -1)
            SetNavCursorVisibleAfterMove();
        return idx;
    }

    /// <summary>
    /// Writes the typing-select debug state to the current debug window.
    /// (.)
    /// </summary>
    /// <param name="data">The typing-select state to report.</param>
    /// <remarks>
    /// Canonical guards the body with <c>#ifndef IMGUI_DISABLE_DEBUG_TOOLS</c> and falls back to
    /// <c>IM_UNUSED(data)</c>. The port has no equivalent compile-time switch, so the reporting body is
    /// always emitted.
    /// </remarks>
    internal static void DebugNodeTypingSelectState(ImGuiTypingSelectState data)
    {
        Text("SearchBuffer = \"%s\"", data.SearchBuffer);
        Text("SingleCharMode = %d, Size = %d, Lock = %d", data.Request.SingleCharMode ? 1 : 0, data.Request.SingleCharSize, data.SingleCharModeLock ? 1 : 0);
        Text("LastRequest = time: %.2f, frame: %d", data.LastRequestTime, data.LastRequestFrame);
    }

    #endregion

    #region [SECTION] Widgets: PlotLines, PlotHistogram

    /// <summary>
    /// Holds the array view and byte stride consumed by <see cref="Plot_ArrayGetter"/> for the array
    /// overloads of <c>PlotLines</c> and <c>PlotHistogram</c>.
    /// </summary>
    private sealed class ImGuiPlotArrayGetterData
    {
        /// <summary>Gets the backing array.</summary>
        public float[] Values { get; }

        /// <summary>Gets the byte stride between consecutive samples.</summary>
        public int Stride { get; }

        /// <summary>
        /// Initializes the array getter data.
        /// </summary>
        /// <param name="values">The backing array.</param>
        /// <param name="stride">The byte stride between consecutive samples.</param>
        public ImGuiPlotArrayGetterData(float[] values, int stride)
        {
            Values = values;
            Stride = stride;
        }
    }

    /// <summary>
    /// Reads one sample through the array getter data, matching canonical <c>Plot_ArrayGetter</c>.
    /// </summary>
    /// <param name="data">An <see cref="ImGuiPlotArrayGetterData"/> instance.</param>
    /// <param name="idx">The sample index.</param>
    /// <returns>The sampled value.</returns>
    /// <remarks>
    /// Canonical performs byte pointer arithmetic so non-contiguous arrays are supported. The port
    /// divides the byte offset by <c>sizeof(float)</c>; strides that are not a multiple of four would
    /// address a misaligned element and are rejected by the assertion.
    /// </remarks>
    private static float Plot_ArrayGetter(object? data, int idx)
    {
        ImGuiPlotArrayGetterData plotData = (ImGuiPlotArrayGetterData)data!;
        ImAssert.Check(plotData.Stride % sizeof(float) == 0, "The plot stride must be a multiple of sizeof(float).");
        int elementIndex = idx * plotData.Stride / sizeof(float);
        return plotData.Values[elementIndex];
    }

    /// <summary>
    /// Gets the cached delegate for <see cref="Plot_ArrayGetter"/> so the array plot overloads do not
    /// allocate a delegate on every call.
    /// </summary>
    private static readonly Func<object?, int, float> PlotArrayGetterFunc = Plot_ArrayGetter;

    /// <summary>
    /// Draws the shared plot core: frame, auto-scale, hover tooltip, line or histogram geometry,
    /// overlay text, and label. Returns the hovered sample index.
    /// (.)
    /// </summary>
    /// <param name="plotType">Whether to draw connected lines or zero-anchored histogram bars.</param>
    /// <param name="label">Label text and identifier seed.</param>
    /// <param name="valuesGetter">Reads the sample at the given index.</param>
    /// <param name="data">Opaque state forwarded to <paramref name="valuesGetter"/>.</param>
    /// <param name="valuesCount">The number of samples.</param>
    /// <param name="valuesOffset">The ring-buffer offset of the first displayed sample.</param>
    /// <param name="overlayText">Optional centered text drawn at the top of the frame.</param>
    /// <param name="scaleMin">The lower value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="scaleMax">The upper value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="sizeArg">The requested frame size; zero components select defaults.</param>
    /// <returns>The hovered sample index, or -1 when no sample is hovered.</returns>
    /// <remarks>
    /// Canonical formats the hover tooltip with <c>%8.4g</c>. The port's <see cref="ImFormatString"/>
    /// formatter does not implement the <c>%g</c> conversion, so the value is printed with <c>%.4f</c>
    /// (fixed four decimals) instead of four significant digits.
    /// </remarks>
    internal static int PlotEx(ImGuiPlotType plotType, string label, Func<object?, int, float> valuesGetter, object? data, int valuesCount, int valuesOffset, string? overlayText, float scaleMin, float scaleMax, ImVec2 sizeArg)
    {
        ImGuiContext g = GetCurrentContext()!;
        ImGuiWindow window = GetCurrentWindow();
        if (window.SkipItems)
            return -1;

        ImGuiStyle style = g.Style;
        uint id = GetID(label);

        string labelEnd = FindRenderedTextEnd(label);
        ImVec2 labelSize = CalcTextSize(labelEnd, false);
        ImVec2 frameSize = CalcItemSize(sizeArg, CalcItemWidth(), labelSize.Y + style.FramePadding.Y * 2.0f);

        ImRect frameBb = new(window.DC.CursorPos, window.DC.CursorPos + frameSize);
        ImRect innerBb = new(frameBb.Min + style.FramePadding, frameBb.Max - style.FramePadding);
        ImRect totalBb = new(frameBb.Min, frameBb.Max + new ImVec2(labelSize.X > 0.0f ? style.ItemInnerSpacing.X + labelSize.X : 0.0f, 0.0f));
        ItemSize(totalBb, style.FramePadding.Y);
        if (!ItemAdd(totalBb, id, frameBb, ImGuiItemFlags.NoNav))
            return -1;
        ButtonBehavior(frameBb, id, out bool hovered, out _);

        // Determine the scale from the values when it is not specified. Averaging is not performed;
        // plots display the raw values.
        if (scaleMin == float.MaxValue || scaleMax == float.MaxValue)
        {
            float vMin = float.MaxValue;
            float vMax = -float.MaxValue;
            for (int n = 0; n < valuesCount; n++)
            {
                float v = valuesGetter(data, n);
                if (float.IsNaN(v))
                    continue;
                vMin = MathF.Min(vMin, v);
                vMax = MathF.Max(vMax, v);
            }
            if (scaleMin == float.MaxValue)
                scaleMin = vMin;
            if (scaleMax == float.MaxValue)
                scaleMax = vMax;
        }

        RenderFrame(frameBb.Min, frameBb.Max, GetColorU32(ImGuiCol.FrameBg), true, style.FrameRounding);

        int valuesCountMin = plotType == ImGuiPlotType.Lines ? 2 : 1;
        int idxHovered = -1;
        if (valuesCount >= valuesCountMin)
        {
            int resW = Math.Min((int)frameSize.X, valuesCount) + (plotType == ImGuiPlotType.Lines ? -1 : 0);
            int itemCount = valuesCount + (plotType == ImGuiPlotType.Lines ? -1 : 0);

            // Tooltip on hover.
            if (hovered && innerBb.Contains(g.IO.MousePos))
            {
                float t = ClampValue((g.IO.MousePos.X - innerBb.Min.X) / (innerBb.Max.X - innerBb.Min.X), 0.0f, 0.999f);
                int vIdx = (int)(t * itemCount);
                ImAssert.Check(vIdx >= 0 && vIdx < valuesCount, "Hovered plot index is out of range.");

                float v0 = valuesGetter(data, (vIdx + valuesOffset) % valuesCount);
                float v1 = valuesGetter(data, (vIdx + 1 + valuesOffset) % valuesCount);
                if (plotType == ImGuiPlotType.Lines)
                    SetTooltip("%d: %.4f\n%d: %.4f", vIdx, v0, vIdx + 1, v1);
                else if (plotType == ImGuiPlotType.Histogram)
                    SetTooltip("%d: %.4f", vIdx, v0);
                idxHovered = vIdx;
            }

            float tStep = 1.0f / resW;
            float invScale = scaleMin == scaleMax ? 0.0f : 1.0f / (scaleMax - scaleMin);

            float value0 = valuesGetter(data, (0 + valuesOffset) % valuesCount);
            float t0 = 0.0f;
            ImVec2 tp0 = new(t0, 1.0f - ColorConvertSaturate((value0 - scaleMin) * invScale));
            // The normalized Y of the zero baseline that anchors histogram bars.
            float histogramZeroLineT = scaleMin * scaleMax < 0.0f ? 1.0f + scaleMin * invScale : (scaleMin < 0.0f ? 0.0f : 1.0f);

            uint colBase = GetColorU32(plotType == ImGuiPlotType.Lines ? ImGuiCol.PlotLines : ImGuiCol.PlotHistogram);
            uint colHovered = GetColorU32(plotType == ImGuiPlotType.Lines ? ImGuiCol.PlotLinesHovered : ImGuiCol.PlotHistogramHovered);

            for (int n = 0; n < resW; n++)
            {
                float t1 = t0 + tStep;
                int v1Idx = (int)(t0 * itemCount + 0.5f);
                ImAssert.Check(v1Idx >= 0 && v1Idx < valuesCount, "Plot segment index is out of range.");
                float value1 = valuesGetter(data, (v1Idx + valuesOffset + 1) % valuesCount);
                ImVec2 tp1 = new(t1, 1.0f - ColorConvertSaturate((value1 - scaleMin) * invScale));

                // Draw calls are merged by the draw-list system, but rendering at this level saves CPU.
                ImVec2 pos0 = LerpVector(innerBb.Min, innerBb.Max, tp0);
                ImVec2 pos1 = LerpVector(innerBb.Min, innerBb.Max, plotType == ImGuiPlotType.Lines ? tp1 : new ImVec2(tp1.X, histogramZeroLineT));
                if (plotType == ImGuiPlotType.Lines)
                {
                    window.DrawList.AddLine(pos0, pos1, idxHovered == v1Idx ? colHovered : colBase);
                }
                else if (plotType == ImGuiPlotType.Histogram)
                {
                    if (pos1.X >= pos0.X + 2.0f)
                        pos1.X -= 1.0f;
                    window.DrawList.AddRectFilled(pos0, pos1, idxHovered == v1Idx ? colHovered : colBase);
                }

                t0 = t1;
                tp0 = tp1;
            }
        }

        // Text overlay.
        if (overlayText != null)
            RenderTextClipped(new ImVec2(frameBb.Min.X, frameBb.Min.Y + style.FramePadding.Y), frameBb.Max, overlayText, null, null, new ImVec2(0.5f, 0.0f));

        if (labelSize.X > 0.0f)
            RenderText(new ImVec2(frameBb.Max.X + style.ItemInnerSpacing.X, innerBb.Min.Y), label, labelEnd, false);

        return idxHovered;
    }

    /// <summary>
    /// Draws a line plot from a flat array of samples, optionally strided.
    /// (.)
    /// </summary>
    /// <param name="label">Label text and identifier seed.</param>
    /// <param name="values">The sample array.</param>
    /// <param name="valuesCount">The number of samples.</param>
    /// <param name="valuesOffset">The ring-buffer offset of the first displayed sample.</param>
    /// <param name="overlayText">Optional centered text drawn at the top of the frame.</param>
    /// <param name="scaleMin">The lower value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="scaleMax">The upper value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="graphSize">The requested frame size; zero components select defaults.</param>
    /// <param name="stride">The byte stride between consecutive samples; must be a multiple of four.</param>
    public static void PlotLines(string label, float[] values, int valuesCount, int valuesOffset = 0, string? overlayText = null, float scaleMin = float.MaxValue, float scaleMax = float.MaxValue, ImVec2 graphSize = default, int stride = sizeof(float))
    {
        ImGuiPlotArrayGetterData data = new(values, stride);
        PlotEx(ImGuiPlotType.Lines, label, PlotArrayGetterFunc, data, valuesCount, valuesOffset, overlayText, scaleMin, scaleMax, graphSize);
    }

    /// <summary>
    /// Draws a line plot whose samples are produced by a callback.
    /// (.)
    /// </summary>
    /// <param name="label">Label text and identifier seed.</param>
    /// <param name="valuesGetter">Reads the sample at the given index.</param>
    /// <param name="data">Opaque state forwarded to <paramref name="valuesGetter"/>.</param>
    /// <param name="valuesCount">The number of samples.</param>
    /// <param name="valuesOffset">The ring-buffer offset of the first displayed sample.</param>
    /// <param name="overlayText">Optional centered text drawn at the top of the frame.</param>
    /// <param name="scaleMin">The lower value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="scaleMax">The upper value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="graphSize">The requested frame size; zero components select defaults.</param>
    public static void PlotLines(string label, Func<object?, int, float> valuesGetter, object? data, int valuesCount, int valuesOffset = 0, string? overlayText = null, float scaleMin = float.MaxValue, float scaleMax = float.MaxValue, ImVec2 graphSize = default)
    {
        PlotEx(ImGuiPlotType.Lines, label, valuesGetter, data, valuesCount, valuesOffset, overlayText, scaleMin, scaleMax, graphSize);
    }

    /// <summary>
    /// Draws a histogram from a flat array of samples. The supplied data is already histogram data;
    /// canonical does not compute a histogram.
    /// (.)
    /// </summary>
    /// <param name="label">Label text and identifier seed.</param>
    /// <param name="values">The sample array.</param>
    /// <param name="valuesCount">The number of samples.</param>
    /// <param name="valuesOffset">The ring-buffer offset of the first displayed sample.</param>
    /// <param name="overlayText">Optional centered text drawn at the top of the frame.</param>
    /// <param name="scaleMin">The lower value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="scaleMax">The upper value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="graphSize">The requested frame size; zero components select defaults.</param>
    /// <param name="stride">The byte stride between consecutive samples; must be a multiple of four.</param>
    public static void PlotHistogram(string label, float[] values, int valuesCount, int valuesOffset = 0, string? overlayText = null, float scaleMin = float.MaxValue, float scaleMax = float.MaxValue, ImVec2 graphSize = default, int stride = sizeof(float))
    {
        ImGuiPlotArrayGetterData data = new(values, stride);
        PlotEx(ImGuiPlotType.Histogram, label, PlotArrayGetterFunc, data, valuesCount, valuesOffset, overlayText, scaleMin, scaleMax, graphSize);
    }

    /// <summary>
    /// Draws a histogram whose samples are produced by a callback.
    /// (.)
    /// </summary>
    /// <param name="label">Label text and identifier seed.</param>
    /// <param name="valuesGetter">Reads the sample at the given index.</param>
    /// <param name="data">Opaque state forwarded to <paramref name="valuesGetter"/>.</param>
    /// <param name="valuesCount">The number of samples.</param>
    /// <param name="valuesOffset">The ring-buffer offset of the first displayed sample.</param>
    /// <param name="overlayText">Optional centered text drawn at the top of the frame.</param>
    /// <param name="scaleMin">The lower value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="scaleMax">The upper value bound, or <see cref="float.MaxValue"/> to auto-scale.</param>
    /// <param name="graphSize">The requested frame size; zero components select defaults.</param>
    public static void PlotHistogram(string label, Func<object?, int, float> valuesGetter, object? data, int valuesCount, int valuesOffset = 0, string? overlayText = null, float scaleMin = float.MaxValue, float scaleMax = float.MaxValue, ImVec2 graphSize = default)
    {
        PlotEx(ImGuiPlotType.Histogram, label, valuesGetter, data, valuesCount, valuesOffset, overlayText, scaleMin, scaleMax, graphSize);
    }

    #endregion

    #endregion


    #region SECTION: DATA TYPE AND COLOR CONVERSION HELPERS

    #region [SECTION] Data Type and Data Formatting Helpers [Internal]

    /// <summary>
    /// The four default component markers used by the color editing and picking widgets, one per RGBA
    /// component.
    /// </summary>
    /// <remarks>
    /// Canonical declares the table in this section as <c>static const ImU32
    /// GDefaultRgbaColorMarkers[4]</c>. The port keeps canonical's packed <c>IM_COL32</c> byte order
    /// and exposes a single shared table so the color component marker and the color widgets do not
    /// each carry a private copy.
    /// </remarks>
    internal static readonly uint[] GDefaultRgbaColorMarkers =
    {
        ImColor.Pack(240, 20, 20, 255),
        ImColor.Pack(20, 240, 20, 255),
        ImColor.Pack(20, 20, 240, 255),
        ImColor.Pack(140, 140, 140, 255),
    };

    /// <summary>
    /// Returns the static description of a data type: its byte size, short debug name, default
    /// printing format, and default scanning format.
    /// (, )
    /// </summary>
    /// <param name="dataType">The data type to describe.</param>
    /// <returns>
    /// The description, or <see langword="null"/> when the value is outside the supported range
    /// (which mirrors canonically asserting on an invalid enum value).
    /// </returns>
    /// <remarks>
    /// Canonical indexes a static <c>GDataTypeInfo</c> array with the enum value and returns a pointer;
    /// this port returns the equivalent managed description. The printing and scanning formats for the
    /// 64-bit types are written as <c>%d</c>/<c>%u</c> rather than canonical <c>%lld</c>/<c>%I64d</c>
    /// because the port's <see cref="ImFormatString"/> formatter does not consume C length modifiers;
    /// the resulting text is identical. The <c>Double</c> scanning format is <c>%f</c> rather than
    /// canonical <c>%lf</c> for the same reason.
    /// </remarks>
    internal static ImGuiDataTypeInfo? DataTypeGetInfo(ImGuiDataType dataType)
    {
        ImAssert.Check(
            dataType >= ImGuiDataType.S8 && dataType <= ImGuiDataType.String,
            "Invalid ImGuiDataType value.");
        switch (dataType)
        {
            case ImGuiDataType.S8: return new ImGuiDataTypeInfo(1, "S8", "%d", "%d");
            case ImGuiDataType.U8: return new ImGuiDataTypeInfo(1, "U8", "%u", "%u");
            case ImGuiDataType.S16: return new ImGuiDataTypeInfo(2, "S16", "%d", "%d");
            case ImGuiDataType.U16: return new ImGuiDataTypeInfo(2, "U16", "%u", "%u");
            case ImGuiDataType.S32: return new ImGuiDataTypeInfo(4, "S32", "%d", "%d");
            case ImGuiDataType.U32: return new ImGuiDataTypeInfo(4, "U32", "%u", "%u");
            case ImGuiDataType.S64: return new ImGuiDataTypeInfo(8, "S64", "%d", "%d");
            case ImGuiDataType.U64: return new ImGuiDataTypeInfo(8, "U64", "%u", "%u");
            case ImGuiDataType.Float: return new ImGuiDataTypeInfo(4, "float", "%.3f", "%f");
            case ImGuiDataType.Double: return new ImGuiDataTypeInfo(8, "double", "%f", "%f");
            case ImGuiDataType.Bool: return new ImGuiDataTypeInfo(1, "bool", "%d", "%d");
            case ImGuiDataType.String: return new ImGuiDataTypeInfo(0, "char*", "%s", "%s");
            default: return null;
        }
    }

    /// <summary>
    /// Formats one boxed data-type value with a printf-style format.
    /// </summary>
    /// <param name="dataType">The data type of <paramref name="pData"/>.</param>
    /// <param name="pData">The boxed value to format.</param>
    /// <param name="format">
    /// The printf-style format; when <see langword="null"/> the data type's default printing format is
    /// used, matching the callers that pre-resolve <c>type_info-&gt;PrintFmt</c>.
    /// </param>
    /// <returns>The formatted text.</returns>
    /// <remarks>
    /// Canonical writes into a fixed 32-byte buffer and truncates; this port returns a managed string,
    /// so no output buffer is taken and no truncation occurs. Canonical takes a single
    /// <c>const void*</c>; this port reads the value from the boxed <see cref="object"/>, which is the
    /// established managed boundary for the data-type helpers. Canonical asserts for <c>Bool</c> and
    /// <c>String</c> because its switch only lists the ten numeric types; this port formats them as
    /// <c>%d</c> and <c>%s</c> respectively so the helpers cannot trap.
    /// </remarks>
    internal static string DataTypeFormatString(ImGuiDataType dataType, object? pData, string? format)
    {
        ImGuiDataTypeInfo? typeInfo = DataTypeGetInfo(dataType);
        string fmt = format ?? typeInfo?.PrintFmt ?? "%d";
        switch (dataType)
        {
            case ImGuiDataType.S8:
            case ImGuiDataType.S16:
            case ImGuiDataType.S32:
            case ImGuiDataType.S64:
                return ImFormatString(fmt, DataTypeReadInt64(pData));
            case ImGuiDataType.U8:
            case ImGuiDataType.U16:
            case ImGuiDataType.U32:
            case ImGuiDataType.U64:
                return ImFormatString(fmt, DataTypeReadUInt64(pData));
            case ImGuiDataType.Float:
                return ImFormatString(fmt, DataTypeReadFloat(pData));
            case ImGuiDataType.Double:
                return ImFormatString(fmt, DataTypeReadDouble(pData));
            case ImGuiDataType.Bool:
                return ImFormatString(fmt, DataTypeReadInt64(pData) != 0 ? 1 : 0);
            case ImGuiDataType.String:
                return ImFormatString(fmt, pData as string ?? string.Empty);
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return string.Empty;
        }
    }

    /// <summary>
    /// Adds or subtracts one typed value from another with the canonical saturating-overflow rule.
    /// (, )
    /// </summary>
    /// <typeparam name="T">The value type; it must match the concrete type named by <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of all three values.</param>
    /// <param name="op">The operator character, either <c>'+'</c> or <c>'-'</c>.</param>
    /// <param name="outValue">Receives <paramref name="arg1"/> combined with <paramref name="arg2"/>.</param>
    /// <param name="arg1">The first operand.</param>
    /// <param name="arg2">The second operand.</param>
    /// <returns>
    /// <see langword="true"/> when the stored value differs from its previous value. Canonical returns
    /// <c>void</c>; the boolean is a managed convenience that callers are free to ignore.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Managed generics cannot express arithmetic over <typeparamref name="T"/>, so the function
    /// switches on <paramref name="dataType"/> and reinterprets the generic storage with
    /// <c>Unsafe.As</c>. This performs no allocation, which matters because the drag and slider
    /// widgets call it per frame. The caller must pass a <typeparamref name="T"/> whose size and
    /// signedness match <paramref name="dataType"/>; a mismatch would reinterpret storage of the wrong
    /// width (for example an eight-byte <see cref="double"/> read from a one-byte slot).
    /// </para>
    /// <para>
    /// Integer overflow saturates to the type's limits exactly as canonical
    /// <c>ImAddClampOverflow</c>/<c>ImSubClampOverflow</c> do; floating-point values use IEEE
    /// addition and subtraction unchanged.
    /// </para>
    /// </remarks>
    internal static bool DataTypeApplyOp<T>(ImGuiDataType dataType, int op, ref T outValue, T arg1, T arg2)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        ImAssert.Check(op == '+' || op == '-', "Unsupported arithmetic operator.");
        T before = outValue;
        switch (dataType)
        {
            case ImGuiDataType.S8:
            {
                ref sbyte o = ref System.Runtime.CompilerServices.Unsafe.As<T, sbyte>(ref outValue);
                sbyte a = System.Runtime.CompilerServices.Unsafe.As<T, sbyte>(ref arg1);
                sbyte b = System.Runtime.CompilerServices.Unsafe.As<T, sbyte>(ref arg2);
                o = (sbyte)(op == '+'
                    ? DataTypeAddClampSigned(a, b, sbyte.MinValue, sbyte.MaxValue)
                    : DataTypeSubClampSigned(a, b, sbyte.MinValue, sbyte.MaxValue));
                break;
            }
            case ImGuiDataType.U8:
            {
                ref byte o = ref System.Runtime.CompilerServices.Unsafe.As<T, byte>(ref outValue);
                byte a = System.Runtime.CompilerServices.Unsafe.As<T, byte>(ref arg1);
                byte b = System.Runtime.CompilerServices.Unsafe.As<T, byte>(ref arg2);
                o = (byte)(op == '+'
                    ? DataTypeAddClampUnsigned(a, b, byte.MaxValue)
                    : DataTypeSubClampUnsigned(a, b));
                break;
            }
            case ImGuiDataType.S16:
            {
                ref short o = ref System.Runtime.CompilerServices.Unsafe.As<T, short>(ref outValue);
                short a = System.Runtime.CompilerServices.Unsafe.As<T, short>(ref arg1);
                short b = System.Runtime.CompilerServices.Unsafe.As<T, short>(ref arg2);
                o = (short)(op == '+'
                    ? DataTypeAddClampSigned(a, b, short.MinValue, short.MaxValue)
                    : DataTypeSubClampSigned(a, b, short.MinValue, short.MaxValue));
                break;
            }
            case ImGuiDataType.U16:
            {
                ref ushort o = ref System.Runtime.CompilerServices.Unsafe.As<T, ushort>(ref outValue);
                ushort a = System.Runtime.CompilerServices.Unsafe.As<T, ushort>(ref arg1);
                ushort b = System.Runtime.CompilerServices.Unsafe.As<T, ushort>(ref arg2);
                o = (ushort)(op == '+'
                    ? DataTypeAddClampUnsigned(a, b, ushort.MaxValue)
                    : DataTypeSubClampUnsigned(a, b));
                break;
            }
            case ImGuiDataType.S32:
            {
                ref int o = ref System.Runtime.CompilerServices.Unsafe.As<T, int>(ref outValue);
                int a = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref arg1);
                int b = System.Runtime.CompilerServices.Unsafe.As<T, int>(ref arg2);
                o = (int)(op == '+'
                    ? DataTypeAddClampSigned(a, b, int.MinValue, int.MaxValue)
                    : DataTypeSubClampSigned(a, b, int.MinValue, int.MaxValue));
                break;
            }
            case ImGuiDataType.U32:
            {
                ref uint o = ref System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref outValue);
                uint a = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref arg1);
                uint b = System.Runtime.CompilerServices.Unsafe.As<T, uint>(ref arg2);
                o = (uint)(op == '+'
                    ? DataTypeAddClampUnsigned(a, b, uint.MaxValue)
                    : DataTypeSubClampUnsigned(a, b));
                break;
            }
            case ImGuiDataType.S64:
            {
                ref long o = ref System.Runtime.CompilerServices.Unsafe.As<T, long>(ref outValue);
                long a = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref arg1);
                long b = System.Runtime.CompilerServices.Unsafe.As<T, long>(ref arg2);
                o = op == '+'
                    ? DataTypeAddClampSigned(a, b, long.MinValue, long.MaxValue)
                    : DataTypeSubClampSigned(a, b, long.MinValue, long.MaxValue);
                break;
            }
            case ImGuiDataType.U64:
            {
                ref ulong o = ref System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref outValue);
                ulong a = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref arg1);
                ulong b = System.Runtime.CompilerServices.Unsafe.As<T, ulong>(ref arg2);
                o = op == '+'
                    ? DataTypeAddClampUnsigned(a, b, ulong.MaxValue)
                    : DataTypeSubClampUnsigned(a, b);
                break;
            }
            case ImGuiDataType.Float:
            {
                ref float o = ref System.Runtime.CompilerServices.Unsafe.As<T, float>(ref outValue);
                float a = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref arg1);
                float b = System.Runtime.CompilerServices.Unsafe.As<T, float>(ref arg2);
                o = op == '+' ? a + b : a - b;
                break;
            }
            case ImGuiDataType.Double:
            {
                ref double o = ref System.Runtime.CompilerServices.Unsafe.As<T, double>(ref outValue);
                double a = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref arg1);
                double b = System.Runtime.CompilerServices.Unsafe.As<T, double>(ref arg2);
                o = op == '+' ? a + b : a - b;
                break;
            }
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return false;
        }
        return !before.Equals(outValue);
    }

    /// <summary>
    /// Parses widget text into a boxed data-type value, honouring the canonical blank trimming, the
    /// empty-input default, the format-derived number base, and the small-type clamping.
    /// (, )
    /// </summary>
    /// <param name="buf">The text the user typed or the widget produced.</param>
    /// <param name="dataType">The data type to parse.</param>
    /// <param name="pData">The boxed destination value; it is replaced when parsing changes it.</param>
    /// <param name="format">
    /// The display format used to derive the number base for integer types; when
    /// <see langword="null"/> the data type's default printing format is used.
    /// </param>
    /// <param name="dataWhenEmpty">
    /// The value copied into <paramref name="pData"/> when the text is empty, or <see langword="null"/>
    /// to leave the value unchanged. Canonical passes <c>NULL</c> unless the caller requested
    /// <c>ImGuiInputTextFlags_ParseEmptyRefVal</c>.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="pData"/> changed.</returns>
    /// <remarks>
    /// <para>
    /// Canonical takes <c>void*</c>; this port takes the boxed value by reference, which is the
    /// documented boxing boundary for the data-type helpers. Assigning a new box allocates, so this
    /// path is used for text entry rather than the per-frame drag/slider arithmetic path.
    /// </para>
    /// <para>
    /// Canonical delegates the actual scanning to <c>sscanf</c>; this port implements the scanner
    /// directly (see <see cref="DataTypeScanInteger"/> and <see cref="DataTypeScanFloat"/>) so widget
    /// text never goes through the culture-sensitive <c>double.Parse</c>/<c>int.Parse</c> APIs. The
    /// canonical function has no <see cref="ImGuiInputTextFlags"/> parameter; character
    /// filtering (for example <c>ImGuiInputTextFlags_CharsDecimal</c>) happens earlier, inside
    /// <c>InputTextEx</c>, and is owned by the input-text part.
    /// </para>
    /// <para>
    /// The "changed" test compares the boxed values rather than raw bytes. This differs from canonical
    /// <c>memcmp</c> only for values that are bitwise distinct but semantically equal, such as
    /// <c>-0.0f</c> and <c>+0.0f</c>. A parsed value that exceeds the target integer type's range is
    /// saturated to that range; canonical hands such input to <c>sscanf</c>, whose overflow is
    /// undefined behaviour, so the port substitutes a deterministic result.
    /// </para>
    /// <para>
    /// Invariant: the boxed value's runtime type is the CLR type corresponding to
    /// <paramref name="dataType"/>. <c>Float</c> boxes as <see cref="float"/>, <c>Double</c> as
    /// <see cref="double"/>, the eight integer types as <see cref="sbyte"/>, <see cref="byte"/>,
    /// <see cref="short"/>, <see cref="ushort"/>, <see cref="int"/>, <see cref="uint"/>,
    /// <see cref="long"/> and <see cref="ulong"/> respectively, and <c>Bool</c> as
    /// <see cref="bool"/>. Callers may therefore unbox directly with a cast without consulting
    /// <paramref name="dataType"/> again.
    /// </para>
    /// </remarks>
    internal static bool DataTypeApplyFromText(string buf, ImGuiDataType dataType, ref object? pData, string? format, object? dataWhenEmpty = null)
    {
        ImGuiDataTypeInfo? typeInfo = DataTypeGetInfo(dataType);
        ImAssert.Check(typeInfo != null, "Invalid ImGuiDataType value.");
        if (typeInfo == null)
            return false;

        object? dataBackup = pData;

        // Canonical trims leading blanks before testing for empty input.
        int start = 0;
        while (start < buf.Length && (buf[start] == ' ' || buf[start] == '\t'))
            start++;
        if (start >= buf.Length)
        {
            if (dataWhenEmpty != null)
            {
                pData = dataWhenEmpty;
                return !Equals(dataBackup, pData);
            }
            return false;
        }
        string text = buf.Substring(start);

        // Float and double always scan with the type's default format: sscanf cannot take precision.
        string scanFormat;
        if (dataType == ImGuiDataType.Float || dataType == ImGuiDataType.Double)
        {
            scanFormat = typeInfo.ScanFmt;
        }
        else
        {
            scanFormat = ImParseFormatSanitizeForScanning(format ?? typeInfo.PrintFmt);
            if (scanFormat.Length == 0)
                scanFormat = typeInfo.ScanFmt;
        }

        if (dataType == ImGuiDataType.Float || dataType == ImGuiDataType.Double)
        {
            if (!DataTypeScanFloat(text, out double parsedFloat))
                return false;
            // Box through separate branches: a `condition ? (float)x : x` expression would unify the
            // float and double branches to double and box every float as System.Double.
            if (dataType == ImGuiDataType.Float)
                pData = (float)parsedFloat;
            else
                pData = parsedFloat;
            return !Equals(dataBackup, pData);
        }

        char conversion = DataTypeScanConversion(scanFormat);
        if (!DataTypeScanInteger(text, conversion, out long signedValue, out ulong unsignedValue))
            return false;

        switch (dataType)
        {
            case ImGuiDataType.S8: pData = (sbyte)Math.Clamp(signedValue, sbyte.MinValue, sbyte.MaxValue); break;
            case ImGuiDataType.U8: pData = (byte)Math.Min(unsignedValue, byte.MaxValue); break;
            case ImGuiDataType.S16: pData = (short)Math.Clamp(signedValue, short.MinValue, short.MaxValue); break;
            case ImGuiDataType.U16: pData = (ushort)Math.Min(unsignedValue, ushort.MaxValue); break;
            case ImGuiDataType.S32: pData = (int)Math.Clamp(signedValue, int.MinValue, int.MaxValue); break;
            case ImGuiDataType.U32: pData = (uint)Math.Min(unsignedValue, uint.MaxValue); break;
            case ImGuiDataType.S64: pData = signedValue; break;
            case ImGuiDataType.U64: pData = unsignedValue; break;
            case ImGuiDataType.Bool: pData = signedValue != 0; break;
            default:
                // Canonical has no branch for String (nor for the private pointer/ID types) and
                // asserts; the port does not parse strings through this numeric entry point.
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return false;
        }
        return !Equals(dataBackup, pData);
    }

    /// <summary>
    /// Orders two typed values.
    /// </summary>
    /// <typeparam name="T">The value type; it must match the concrete type named by <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of both operands; retained for canonical signature parity.</param>
    /// <param name="arg1">The left operand.</param>
    /// <param name="arg2">The right operand.</param>
    /// <returns><c>-1</c> when <paramref name="arg1"/> is smaller, <c>+1</c> when larger, otherwise <c>0</c>.</returns>
    /// <remarks>
    /// These values are only ever compared with each other, so the managed ordering interface of
    /// <typeparamref name="T"/> reproduces the canonical template comparison. For a <c>NaN</c> operand
    /// the ordering interface is defined while canonical's <c>&lt;</c>/<c>&gt;</c> tests are both false;
    /// canonical would report equality, this port reports an order.
    /// </remarks>
    internal static int DataTypeCompare<T>(ImGuiDataType dataType, T arg1, T arg2)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        int comparison = arg1.CompareTo(arg2);
        return comparison < 0 ? -1 : (comparison > 0 ? 1 : 0);
    }

    /// <summary>
    /// Clamps a typed value to an optional lower and upper bound.
    /// </summary>
    /// <typeparam name="T">The value type; it must match the concrete type named by <paramref name="dataType"/>.</typeparam>
    /// <param name="dataType">The data type of the value; retained for canonical signature parity.</param>
    /// <param name="data">The value to clamp in place.</param>
    /// <param name="min">The lower bound, or <see langword="null"/> for no lower bound.</param>
    /// <param name="max">The upper bound, or <see langword="null"/> for no upper bound.</param>
    /// <returns><see langword="true"/> when the value was changed.</returns>
    /// <remarks>Each side is optional and only the first exceeded bound is applied, matching canonical.</remarks>
    internal static bool DataTypeClamp<T>(ImGuiDataType dataType, ref T data, T? min, T? max)
        where T : unmanaged, IComparable<T>, IEquatable<T>
    {
        if (min.HasValue && data.CompareTo(min.Value) < 0)
        {
            data = min.Value;
            return true;
        }
        if (max.HasValue && data.CompareTo(max.Value) > 0)
        {
            data = max.Value;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Tests whether a boxed data-type value equals zero.
    /// </summary>
    /// <param name="dataType">The data type of <paramref name="pData"/>.</param>
    /// <param name="pData">The boxed value to test.</param>
    /// <returns><see langword="true"/> when the value is zero.</returns>
    /// <remarks>
    /// Canonical compares raw bytes against the context's <c>DataTypeZeroValue</c> buffer. Because the
    /// managed boundary already knows the concrete boxed type, this port compares the unboxed value
    /// against the typed zero instead, which is equivalent for the numeric types.
    /// </remarks>
    internal static bool DataTypeIsZero(ImGuiDataType dataType, object? pData)
    {
        if (pData == null)
            return false;
        switch (dataType)
        {
            case ImGuiDataType.S8:
            case ImGuiDataType.S16:
            case ImGuiDataType.S32:
            case ImGuiDataType.S64:
            case ImGuiDataType.Bool:
                return DataTypeReadInt64(pData) == 0;
            case ImGuiDataType.U8:
            case ImGuiDataType.U16:
            case ImGuiDataType.U32:
            case ImGuiDataType.U64:
                return DataTypeReadUInt64(pData) == 0;
            case ImGuiDataType.Float:
            case ImGuiDataType.Double:
                return DataTypeReadDouble(pData) == 0.0;
            default:
                ImAssert.Check(false, "Invalid ImGuiDataType value.");
                return false;
        }
    }

    /// <summary>
    /// Finds the start of the first real conversion in a printf-style format, skipping escaped
    /// <c>%%</c> pairs.
    /// </summary>
    /// <param name="fmt">The format string.</param>
    /// <returns>
    /// The index of the first <c>'%'</c> not followed by another <c>'%'</c>, or
    /// <paramref name="fmt"/>'s length when no conversion exists.
    /// </returns>
    internal static int ImParseFormatFindStart(string fmt)
    {
        int i = 0;
        while (i < fmt.Length)
        {
            char c = fmt[i];
            if (c == '%' && (i + 1 >= fmt.Length || fmt[i + 1] != '%'))
                return i;
            if (c == '%')
                i++;
            i++;
        }
        return fmt.Length;
    }

    /// <summary>
    /// Finds the index just past the conversion character of the format specifier that starts at
    /// <paramref name="start"/>.
    /// </summary>
    /// <param name="fmt">The format string.</param>
    /// <param name="start">The index of the specifier's <c>'%'</c>.</param>
    /// <returns>The index one past the conversion character, or <paramref name="start"/> when it is not a <c>'%'</c>.</returns>
    /// <remarks>
    /// Canonical skips the length modifiers <c>I</c>, <c>L</c>, <c>h</c>, <c>j</c>, <c>l</c>, <c>t</c>,
    /// <c>w</c> and <c>z</c>; any other letter ends the specifier.
    /// </remarks>
    internal static int ImParseFormatFindEnd(string fmt, int start)
    {
        if (start >= fmt.Length || fmt[start] != '%')
            return start;
        for (int i = start; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c >= 'A' && c <= 'Z' && c != 'I' && c != 'L')
                return i + 1;
            if (c >= 'a' && c <= 'z' && c != 'h' && c != 'j' && c != 'l' && c != 't' && c != 'w' && c != 'z')
                return i + 1;
        }
        return fmt.Length;
    }

    /// <summary>
    /// Extracts the conversion specifier out of a format that may carry leading or trailing
    /// decorations, for example <c>"hello %.3f"</c>.
    /// </summary>
    /// <param name="fmt">The decorated format string.</param>
    /// <returns>The bare specifier, or an empty string when the format holds no visible number.</returns>
    internal static string ImParseFormatTrimDecorations(string fmt)
    {
        int start = ImParseFormatFindStart(fmt);
        if (start >= fmt.Length || fmt[start] != '%')
            return string.Empty;
        int end = ImParseFormatFindEnd(fmt, start);
        if (end >= fmt.Length)
            return fmt.Substring(start);
        return fmt.Substring(start, end - start);
    }

    /// <summary>
    /// Reduces a format specifier to the form a printer accepts by dropping the stb_sprintf custom
    /// flags <c>'</c>, <c>$</c> and <c>_</c> and zero-terminating at the conversion character.
    /// </summary>
    /// <param name="fmt">The format specifier, normally the start of a decorated format.</param>
    /// <returns>The sanitized specifier, or an empty string when no specifier is present.</returns>
    /// <remarks>
    /// Canonical writes into a caller buffer and asserts it is long enough; this port returns a managed
    /// string, so no output buffer is taken. <c>RoundScalarWithFormatT</c> and the color widgets share
    /// this one implementation.
    /// </remarks>
    internal static string ImParseFormatSanitizeForPrinting(string fmt)
    {
        int end = ImParseFormatFindEnd(fmt, 0);
        System.Text.StringBuilder builder = new System.Text.StringBuilder(end);
        for (int i = 0; i < end; i++)
        {
            char c = fmt[i];
            if (c != '\'' && c != '$' && c != '_')
                builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Reduces a format specifier to the form a scanner accepts by dropping flags, width and precision
    /// while keeping length modifiers.
    /// </summary>
    /// <param name="fmt">The format specifier, normally the start of a decorated format.</param>
    /// <returns>The sanitized specifier, or an empty string when no specifier is present.</returns>
    /// <remarks>For example <c>"%+3.7f"</c> becomes <c>"%f"</c> while <c>"%07I64d"</c> becomes <c>"%I64d"</c>.</remarks>
    internal static string ImParseFormatSanitizeForScanning(string fmt)
    {
        int end = ImParseFormatFindEnd(fmt, 0);
        System.Text.StringBuilder builder = new System.Text.StringBuilder(end);
        bool hasType = false;
        for (int i = 0; i < end; i++)
        {
            char c = fmt[i];
            if (!hasType && ((c >= '0' && c <= '9') || c == '.' || c == '+' || c == '#'))
                continue;
            hasType |= (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            if (c != '\'' && c != '$' && c != '_')
                builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Reads the displayed decimal precision back out of a format string.
    /// </summary>
    /// <param name="format">The format string.</param>
    /// <param name="defaultPrecision">The precision to return when the format does not specify one.</param>
    /// <returns>The precision, or <c>-1</c> for the maximum precision used by <c>%e</c>/<c>%g</c>.</returns>
    internal static int ImParseFormatPrecision(string format, int defaultPrecision)
    {
        int start = ImParseFormatFindStart(format);
        if (start >= format.Length || format[start] != '%')
            return defaultPrecision;
        int i = start + 1;
        while (i < format.Length && format[i] >= '0' && format[i] <= '9')
            i++;
        int precision = int.MaxValue;
        if (i < format.Length && format[i] == '.')
        {
            i = ImParseFormatAtoi(format, i + 1, out precision);
            if (precision < 0 || precision > 99)
                precision = defaultPrecision;
        }
        if (i < format.Length && (format[i] == 'e' || format[i] == 'E'))
            precision = -1;
        if (i < format.Length && (format[i] == 'g' || format[i] == 'G') && precision == int.MaxValue)
            precision = -1;
        return precision == int.MaxValue ? defaultPrecision : precision;
    }

    /// <summary>
    /// Reads an optionally signed decimal integer from a format string, matching canonical
    /// <c>ImAtoi</c>.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="start">The index to begin reading at.</param>
    /// <param name="value">Receives the parsed integer, or zero when no digit follows.</param>
    /// <returns>The index of the first character not consumed.</returns>
    private static int ImParseFormatAtoi(string text, int start, out int value)
    {
        int i = start;
        bool negative = false;
        if (i < text.Length && text[i] == '-')
        {
            negative = true;
            i++;
        }
        if (i < text.Length && text[i] == '+')
            i++;
        int parsed = 0;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9')
        {
            parsed = (parsed * 10) + (text[i] - '0');
            i++;
        }
        value = negative ? -parsed : parsed;
        return i;
    }

    /// <summary>
    /// Returns the conversion character of a sanitized scanning format.
    /// </summary>
    /// <param name="scanFormat">The sanitized specifier, for example <c>"%I64d"</c>.</param>
    /// <returns>The trailing conversion character, or <c>'d'</c> when the format holds no letter.</returns>
    private static char DataTypeScanConversion(string scanFormat)
    {
        for (int i = scanFormat.Length - 1; i >= 0; i--)
        {
            char c = scanFormat[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                return c;
        }
        return 'd';
    }

    /// <summary>
    /// Scans an integer the way canonical <c>sscanf</c> does, honouring the conversion character's
    /// number base.
    /// </summary>
    /// <param name="text">The trimmed text to scan.</param>
    /// <param name="conversion">The conversion character (<c>d</c>, <c>i</c>, <c>u</c>, <c>x</c> or <c>o</c>).</param>
    /// <param name="signedValue">Receives the value interpreted as signed, saturated to the 64-bit range.</param>
    /// <param name="unsignedValue">Receives the value interpreted as unsigned, wrapped modulo 2^64 when a minus sign is present.</param>
    /// <returns><see langword="true"/> when at least one digit was consumed.</returns>
    /// <remarks>
    /// The scanner accumulates digits with defined wrapping where canonical's <c>sscanf</c> overflow is
    /// undefined behaviour, so a malformed extreme value can never fault.
    /// </remarks>
    private static bool DataTypeScanInteger(string text, char conversion, out long signedValue, out ulong unsignedValue)
    {
        signedValue = 0;
        unsignedValue = 0;

        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        bool negative = false;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
        {
            negative = text[i] == '-';
            i++;
        }

        int numberBase = 10;
        if (conversion == 'x' || conversion == 'X')
        {
            numberBase = 16;
        }
        else if (conversion == 'o')
        {
            numberBase = 8;
        }
        else if (conversion == 'i')
        {
            if (i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            {
                numberBase = 16;
                i += 2;
            }
            else if (i < text.Length && text[i] == '0')
            {
                numberBase = 8;
            }
        }
        if ((conversion == 'x' || conversion == 'X') && i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            i += 2;

        int digitStart = i;
        ulong magnitude = 0;
        while (i < text.Length)
        {
            int digit = DataTypeHexDigitValue(text[i]);
            if (digit < 0 || digit >= numberBase)
                break;
            magnitude = unchecked((magnitude * (ulong)numberBase) + (ulong)digit);
            i++;
        }
        if (i == digitStart)
            return false;

        unsignedValue = negative ? unchecked(0UL - magnitude) : magnitude;
        if (!negative)
        {
            signedValue = magnitude > long.MaxValue ? long.MaxValue : (long)magnitude;
        }
        else
        {
            signedValue = magnitude >= 9223372036854775808UL ? long.MinValue : -(long)magnitude;
        }
        return true;
    }

    /// <summary>
    /// Scans a floating-point number the way canonical <c>sscanf</c> with <c>%f</c> does: leading
    /// whitespace, an optional sign, <c>inf</c>/<c>nan</c>, a hexadecimal float, or a decimal mantissa
    /// with an optional <c>e</c>/<c>E</c> exponent, plus the C99 hexadecimal <c>p</c> exponent.
    /// </summary>
    /// <param name="text">The trimmed text to scan.</param>
    /// <param name="value">Receives the parsed value.</param>
    /// <returns><see langword="true"/> when a number was consumed.</returns>
    /// <remarks>
    /// The decimal mantissa is accumulated as a double and scaled by a power of ten. That reproduces
    /// the common widget inputs exactly; it is not guaranteed to match a correctly rounded library
    /// decimal conversion in the last unit in the last place for very long digit strings.
    /// </remarks>
    private static bool DataTypeScanFloat(string text, out double value)
    {
        value = 0.0;

        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        int sign = 1;
        if (i < text.Length && (text[i] == '-' || text[i] == '+'))
        {
            if (text[i] == '-')
                sign = -1;
            i++;
        }

        if (DataTypeMatchIgnoreCase(text, i, "infinity"))
        {
            value = sign * double.PositiveInfinity;
            return true;
        }
        if (DataTypeMatchIgnoreCase(text, i, "inf"))
        {
            value = sign * double.PositiveInfinity;
            return true;
        }
        if (DataTypeMatchIgnoreCase(text, i, "nan"))
        {
            value = double.NaN;
            return true;
        }

        int hexFallback = i;
        if (i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X'))
        {
            i += 2;
            double mantissa = 0.0;
            bool anyHexDigit = false;
            int binaryExponent = 0;
            while (i < text.Length && DataTypeHexDigitValue(text[i]) >= 0)
            {
                mantissa = (mantissa * 16.0) + DataTypeHexDigitValue(text[i]);
                anyHexDigit = true;
                i++;
            }
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && DataTypeHexDigitValue(text[i]) >= 0)
                {
                    mantissa = (mantissa * 16.0) + DataTypeHexDigitValue(text[i]);
                    binaryExponent -= 4;
                    anyHexDigit = true;
                    i++;
                }
            }
            if (anyHexDigit)
            {
                if (i < text.Length && (text[i] == 'p' || text[i] == 'P'))
                {
                    int exponentStart = i;
                    i++;
                    int exponentSign = 1;
                    if (i < text.Length && (text[i] == '-' || text[i] == '+'))
                    {
                        if (text[i] == '-')
                            exponentSign = -1;
                        i++;
                    }
                    int exponent = 0;
                    bool anyExponentDigit = false;
                    while (i < text.Length && text[i] >= '0' && text[i] <= '9')
                    {
                        exponent = Math.Min((exponent * 10) + (text[i] - '0'), 100000);
                        anyExponentDigit = true;
                        i++;
                    }
                    if (anyExponentDigit)
                        binaryExponent += exponentSign * exponent;
                    else
                        i = exponentStart;
                }
                value = sign * mantissa * Math.Pow(2.0, binaryExponent);
                return true;
            }
            i = hexFallback;
        }

        int digitStart = i;
        double decimalMantissa = 0.0;
        int fractionalDigits = 0;
        bool anyDigit = false;
        while (i < text.Length && text[i] >= '0' && text[i] <= '9')
        {
            decimalMantissa = (decimalMantissa * 10.0) + (text[i] - '0');
            anyDigit = true;
            i++;
        }
        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                decimalMantissa = (decimalMantissa * 10.0) + (text[i] - '0');
                fractionalDigits++;
                anyDigit = true;
                i++;
            }
        }
        if (!anyDigit)
        {
            i = digitStart;
            return false;
        }

        int decimalExponent = 0;
        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            int exponentStart = i;
            i++;
            int exponentSign = 1;
            if (i < text.Length && (text[i] == '-' || text[i] == '+'))
            {
                if (text[i] == '-')
                    exponentSign = -1;
                i++;
            }
            int exponent = 0;
            bool anyExponentDigit = false;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                exponent = Math.Min((exponent * 10) + (text[i] - '0'), 100000);
                anyExponentDigit = true;
                i++;
            }
            if (anyExponentDigit)
                decimalExponent = exponentSign * exponent;
            else
                i = exponentStart;
        }

        value = sign * decimalMantissa * Math.Pow(10.0, decimalExponent - fractionalDigits);
        return true;
    }

    /// <summary>
    /// Tests whether <paramref name="text"/> contains a case-insensitive ASCII keyword at
    /// <paramref name="start"/>.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="start">The index to compare at.</param>
    /// <param name="keyword">The lowercase keyword.</param>
    /// <returns><see langword="true"/> when the keyword matches at that position.</returns>
    private static bool DataTypeMatchIgnoreCase(string text, int start, string keyword)
    {
        if (start + keyword.Length > text.Length)
            return false;
        for (int i = 0; i < keyword.Length; i++)
        {
            char c = text[start + i];
            if (c >= 'A' && c <= 'Z')
                c = (char)(c + ('a' - 'A'));
            if (c != keyword[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Converts one hexadecimal digit to its value.
    /// </summary>
    /// <param name="c">The character to convert.</param>
    /// <returns>The digit value, or <c>-1</c> when the character is not hexadecimal.</returns>
    private static int DataTypeHexDigitValue(char c)
    {
        if (c >= '0' && c <= '9')
            return c - '0';
        if (c >= 'a' && c <= 'f')
            return (c - 'a') + 10;
        if (c >= 'A' && c <= 'F')
            return (c - 'A') + 10;
        return -1;
    }

    /// <summary>
    /// Reads a boxed integer value as a signed 64-bit value.
    /// </summary>
    /// <param name="pData">The boxed value.</param>
    /// <returns>The value, or zero for an unsupported box.</returns>
    private static long DataTypeReadInt64(object? pData)
    {
        switch (pData)
        {
            case sbyte v: return v;
            case byte v: return v;
            case short v: return v;
            case ushort v: return v;
            case int v: return v;
            case uint v: return v;
            case long v: return v;
            case ulong v: return unchecked((long)v);
            case bool v: return v ? 1 : 0;
            default: return 0;
        }
    }

    /// <summary>
    /// Reads a boxed integer value as an unsigned 64-bit value.
    /// </summary>
    /// <param name="pData">The boxed value.</param>
    /// <returns>The value, or zero for an unsupported box.</returns>
    private static ulong DataTypeReadUInt64(object? pData)
    {
        switch (pData)
        {
            case sbyte v: return unchecked((ulong)v);
            case byte v: return v;
            case short v: return unchecked((ulong)v);
            case ushort v: return v;
            case int v: return unchecked((ulong)v);
            case uint v: return v;
            case long v: return unchecked((ulong)v);
            case ulong v: return v;
            case bool v: return v ? 1UL : 0UL;
            default: return 0;
        }
    }

    /// <summary>
    /// Reads a boxed floating-point value as a <see cref="float"/>.
    /// </summary>
    /// <param name="pData">The boxed value.</param>
    /// <returns>The value, or zero for an unsupported box.</returns>
    private static float DataTypeReadFloat(object? pData)
    {
        if (pData is float f)
            return f;
        if (pData is double d)
            return (float)d;
        return 0.0f;
    }

    /// <summary>
    /// Reads a boxed floating-point value as a <see cref="double"/>.
    /// </summary>
    /// <param name="pData">The boxed value.</param>
    /// <returns>The value, or zero for an unsupported box.</returns>
    private static double DataTypeReadDouble(object? pData)
    {
        if (pData is double d)
            return d;
        if (pData is float f)
            return f;
        return 0.0;
    }

    /// <summary>
    /// Adds two signed 64-bit operands, saturating at the supplied bounds.
    /// </summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <param name="min">The lower saturation bound.</param>
    /// <param name="max">The upper saturation bound.</param>
    /// <returns>The sum, or the exceeded bound.</returns>
    private static long DataTypeAddClampSigned(long a, long b, long min, long max)
    {
        if (b > 0 && a > max - b)
            return max;
        if (b < 0 && a < min - b)
            return min;
        return a + b;
    }

    /// <summary>
    /// Subtracts two signed 64-bit operands, saturating at the supplied bounds.
    /// </summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <param name="min">The lower saturation bound.</param>
    /// <param name="max">The upper saturation bound.</param>
    /// <returns>The difference, or the exceeded bound.</returns>
    private static long DataTypeSubClampSigned(long a, long b, long min, long max)
    {
        if (b > 0 && a < min + b)
            return min;
        if (b < 0 && a > max + b)
            return max;
        return a - b;
    }

    /// <summary>
    /// Adds two unsigned 64-bit operands, saturating at <paramref name="max"/>.
    /// </summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <param name="max">The upper saturation bound.</param>
    /// <returns>The sum, or <paramref name="max"/>.</returns>
    private static ulong DataTypeAddClampUnsigned(ulong a, ulong b, ulong max)
    {
        if (a > max - b)
            return max;
        return a + b;
    }

    /// <summary>
    /// Subtracts two unsigned 64-bit operands, saturating at zero.
    /// </summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The difference, or zero.</returns>
    private static ulong DataTypeSubClampUnsigned(ulong a, ulong b)
    {
        if (a < b)
            return 0;
        return a - b;
    }

    #endregion

    #region [SECTION] MISC HELPERS/UTILITIES (Color functions)

    /// <summary>
    /// Unpacks a 32-bit RGBA color into normalized floating-point components, in the default
    /// <c>IM_COL32</c> byte order (red least significant).
    /// </summary>
    /// <param name="in_">The packed color.</param>
    /// <returns>The unpacked color with components in the 0.1 range.</returns>
    /// <remarks>Canonical multiplies by <c>1.0f / 255.0f</c>; this port performs the same multiply so the result is bit-identical.</remarks>
    public static ImVec4 ColorConvertU32ToFloat4(uint in_)
    {
        const float scale = 1.0f / 255.0f;
        return new ImVec4(
            ((in_ >> 0) & 0xFF) * scale,
            ((in_ >> 8) & 0xFF) * scale,
            ((in_ >> 16) & 0xFF) * scale,
            ((in_ >> 24) & 0xFF) * scale);
    }

    /// <summary>
    /// Packs normalized floating-point color components into a 32-bit RGBA value, in the default
    /// <c>IM_COL32</c> byte order (red least significant).
    /// (, )
    /// </summary>
    /// <param name="in_">The color with components nominally in the 0.1 range; they are saturated.</param>
    /// <returns>The packed color.</returns>
    /// <remarks>
    /// Each component is saturated to 0.1, multiplied by 255, offset by 0.5 and truncated, matching
    /// canonical <c>IM_F32_TO_INT8_SAT</c>. The saturation helper follows canonical
    /// <c>ImSaturate</c> (<c>ImMax(ImMin(v, 1), 0)</c>) rather than <see cref="Math.Clamp(float, float, float)"/>
    /// so that a <c>NaN</c> component packs as 255 exactly as canonical does.
    /// </remarks>
    public static uint ColorConvertFloat4ToU32(ImVec4 in_)
    {
        uint r = (uint)(int)(((ColorConvertSaturate(in_.X) * 255.0f) + 0.5f));
        uint g = (uint)(int)(((ColorConvertSaturate(in_.Y) * 255.0f) + 0.5f));
        uint b = (uint)(int)(((ColorConvertSaturate(in_.Z) * 255.0f) + 0.5f));
        uint a = (uint)(int)(((ColorConvertSaturate(in_.W) * 255.0f) + 0.5f));
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    /// <summary>
    /// Converts normalized red/green/blue components to hue/saturation/value, using the canonical
    /// branch structure derived from Foley and van Dam.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="outH">Receives the hue.</param>
    /// <param name="outS">Receives the saturation.</param>
    /// <param name="outV">Receives the value (brightness).</param>
    /// <remarks>
    /// The canonical ordering network swaps components and accumulates an offset <c>K</c>; it is
    /// reproduced literally, including the <c>1e-20f</c> guards that keep a fully grey or black input
    /// from dividing by zero. Hue is unsigned by the final absolute value.
    /// </remarks>
    public static void ColorConvertRGBtoHSV(float r, float g, float b, out float outH, out float outS, out float outV)
    {
        float k = 0.0f;
        if (g < b)
        {
            float swap = g;
            g = b;
            b = swap;
            k = -1.0f;
        }
        if (r < g)
        {
            float swap = r;
            r = g;
            g = swap;
            k = (-2.0f / 6.0f) - k;
        }

        float chroma = r - (g < b ? g : b);
        outH = MathF.Abs(k + ((g - b) / ((6.0f * chroma) + 1e-20f)));
        outS = chroma / (r + 1e-20f);
        outV = r;
    }

    /// <summary>
    /// Converts normalized hue/saturation/value to red/green/blue components, using the canonical
    /// six-sector branch structure.
    /// </summary>
    /// <param name="h">The hue.</param>
    /// <param name="s">The saturation.</param>
    /// <param name="v">The value (brightness).</param>
    /// <param name="outR">Receives the red component.</param>
    /// <param name="outG">Receives the green component.</param>
    /// <param name="outB">Receives the blue component.</param>
    /// <remarks>
    /// A zero saturation yields the canonical grey result <c>(v, v, v)</c> before any sector math.
    /// The hue is reduced with <c>fmod</c>-style remainder and truncated to a sector index; negative
    /// hues therefore fall into the default sector, exactly as canonical does.
    /// </remarks>
    public static void ColorConvertHSVtoRGB(float h, float s, float v, out float outR, out float outG, out float outB)
    {
        if (s == 0.0f)
        {
            outR = v;
            outG = v;
            outB = v;
            return;
        }

        h = (h % 1.0f) / (60.0f / 360.0f);
        int sector = (int)h;
        float f = h - sector;
        float p = v * (1.0f - s);
        float q = v * (1.0f - (s * f));
        float t = v * (1.0f - (s * (1.0f - f)));

        switch (sector)
        {
            case 0: outR = v; outG = t; outB = p; break;
            case 1: outR = q; outG = v; outB = p; break;
            case 2: outR = p; outG = v; outB = t; break;
            case 3: outR = p; outG = q; outB = v; break;
            case 4: outR = t; outG = p; outB = v; break;
            default: outR = v; outG = p; outB = q; break;
        }
    }

    /// <summary>
    /// Saturates a color component to the 0.1 range with the canonical <c>ImSaturate</c> comparison
    /// order.
    /// </summary>
    /// <param name="value">The component value.</param>
    /// <returns>The value clamped to 0.1; a <c>NaN</c> input resolves to 1 as in canonical.</returns>
    private static float ColorConvertSaturate(float value)
    {
        float clamped = value < 1.0f ? value : 1.0f;
        return clamped > 0.0f ? clamped : 0.0f;
    }

    #endregion

    #endregion


    #region SECTION: GROUP AND DISABLED SCOPES, LOG DECORATION, ITEM COLOR MARKER

    #region [SECTION] GROUPS

    /// <summary>
    /// Starts a group: locks the horizontal cursor position and begins capturing a bounding box that the
    /// whole group can later be treated as one item through <c>IsItemHovered()</c>, <c>SameLine()</c>, and
    /// the other item primitives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::BeginGroup()</c>.
    /// </para>
    /// <para>
    /// The saved cursor, indent, line, and interaction fields are restored by <see cref="EndGroup"/>, so
    /// calls must nest correctly and stay within one window (<see cref="ImGuiGroupData.WindowID"/> records
    /// which window owns the level). The captured group offset replaces the current indent for the
    /// duration of the group, which makes <c>SameLine()</c> inside the group relative to the group origin.
    /// </para>
    /// <para>
    /// Ownership: the chunk plan assigned <c>BeginGroup()</c>/<c>EndGroup()</c> to Chunk 10, but the menu
    /// and color widgets in parts 04/06/08 already call them, so Chunk 4 realizes them here. The pair keeps
    /// its canonical public surface. Chunk 10 owns nothing in
    /// this member any more and must not redefine either half of the pair.
    /// </para>
    /// </remarks>
    public static void BeginGroup()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = GetCurrentWindow();

        // Canonical grows the group stack by one default-constructed entry, then fills it in place.
        ImGuiGroupData groupData = new ImGuiGroupData();
        g.GroupStack.Add(groupData);
        groupData.WindowID = window.ID;
        groupData.BackupCursorPos = window.DC.CursorPos;
        groupData.BackupCursorPosPrevLine = window.DC.CursorPosPrevLine;
        groupData.BackupCursorMaxPos = window.DC.CursorMaxPos;
        groupData.BackupIndent = window.DC.Indent;
        groupData.BackupGroupOffset = window.DC.GroupOffset;
        groupData.BackupCurrLineSize = window.DC.CurrLineSize;
        groupData.BackupCurrLineTextBaseOffset = window.DC.CurrLineTextBaseOffset;
        groupData.BackupActiveIdIsAlive = g.ActiveIdIsAlive;
        groupData.BackupHoveredIdIsAlive = g.HoveredId != 0;
        groupData.BackupIsSameLine = window.DC.IsSameLine;
        groupData.BackupAnyIdHasBeenEditedThisFrame = g.AnyIdHasBeenEditedThisFrame;
        groupData.BackupDeactivatedIdIsAlive = g.DeactivatedItemData.IsAlive;
        groupData.EmitItem = true;

        // The group origin is the current cursor X expressed in window-local space, excluding the columns
        // offset; it becomes the new indent so child lines align to the group rather than the window.
        ImVec1 groupOffset = window.DC.GroupOffset;
        groupOffset.X = window.DC.CursorPos.X - window.Pos.X - window.DC.ColumnsOffset.X;
        window.DC.GroupOffset = groupOffset;
        window.DC.Indent = groupOffset;
        window.DC.CursorMaxPos = window.DC.CursorPos;
        window.DC.CurrLineSize = new ImVec2(0.0f, 0.0f);
        if (g.LogEnabled)
            g.LogLinePosY = -float.MaxValue; // To enforce a carriage return.
    }

    /// <summary>
    /// Ends the innermost group, restores the cursor state saved by <see cref="BeginGroup"/>, and submits
    /// the captured bounding box as one parent item.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::EndGroup()</c>.
    /// </para>
    /// <para>
    /// The group rectangle spans from the saved cursor position to the largest cursor extent reached
    /// inside the group, additionally folded with <c>LastItemData.Rect.Max</c> to cover containers such as
    /// tables that report their extent through the last item rather than the cursor (canonical #7543).
    /// Active, hovered, edited, and deactivated state produced inside the group is forwarded onto the
    /// parent item so the whole group answers the corresponding <c>IsItem*()</c> queries.
    /// </para>
    /// <para>
    /// Ownership: realize here together with <see cref="BeginGroup"/> and keep the canonical public surface
    ///.; Chunk 10 owns nothing in this member any more and must not
    /// redefine it.
    /// </para>
    /// </remarks>
    public static void EndGroup()
    {
        ImGuiContext g = GetContext();
        ImGuiWindow window = GetCurrentWindow();
        ImAssert.Check(g.GroupStack.Count > 0, "Mismatched BeginGroup()/EndGroup() calls.");

        ImGuiGroupData groupData = g.GroupStack[^1];
        ImAssert.Check(groupData.WindowID == window.ID, "EndGroup() in the wrong window.");

        if (window.DC.IsSetPos)
            ErrorCheckUsingSetCursorPosToExtendParentBoundaries();

        // Include LastItemData.Rect.Max as a workaround for e.g. EndTable() undershooting with the
        // CursorMaxPos report (canonical #7543).
        ImRect groupBb = new(
            groupData.BackupCursorPos,
            MaxVector(MaxVector(window.DC.CursorMaxPos, g.LastItemData.Rect.Max), groupData.BackupCursorPos));
        window.DC.CursorPos = groupData.BackupCursorPos;
        window.DC.CursorPosPrevLine = groupData.BackupCursorPosPrevLine;
        window.DC.CursorMaxPos = MaxVector(groupData.BackupCursorMaxPos, groupBb.Max);
        window.DC.Indent = groupData.BackupIndent;
        window.DC.GroupOffset = groupData.BackupGroupOffset;
        window.DC.CurrLineSize = groupData.BackupCurrLineSize;
        window.DC.CurrLineTextBaseOffset = groupData.BackupCurrLineTextBaseOffset;
        window.DC.IsSameLine = groupData.BackupIsSameLine;
        if (g.LogEnabled)
            g.LogLinePosY = -float.MaxValue; // To enforce a carriage return.

        if (!groupData.EmitItem)
        {
            g.GroupStack.RemoveAt(g.GroupStack.Count - 1);
            return;
        }

        // FIXME: Incorrect, we should grab the base offset from the *first line* of the group but it is
        // hard to obtain now.
        window.DC.CurrLineTextBaseOffset = MathF.Max(window.DC.PrevLineTextBaseOffset, groupData.BackupCurrLineTextBaseOffset);
        ItemSize(groupBb.GetSize());
        ItemAdd(groupBb, 0, null, ImGuiItemFlags.NoTabStop);

        // If the current ActiveId was declared within the boundary of the group, it is copied to
        // LastItemData so IsItemActive(), IsItemDeactivated(), etc. work on the entire group.
        bool groupContainsCurrActiveId = (groupData.BackupActiveIdIsAlive != g.ActiveId) && (g.ActiveIdIsAlive == g.ActiveId) && g.ActiveId != 0;
        bool groupContainsDeactivatedId = groupData.BackupDeactivatedIdIsAlive == false && g.DeactivatedItemData.IsAlive == true;
        if (groupContainsCurrActiveId)
            g.LastItemData.ID = g.ActiveId;
        else if (groupContainsDeactivatedId)
            g.LastItemData.ID = g.DeactivatedItemData.ID;
        g.LastItemData.Rect = groupBb;

        // Forward the hovered flag.
        bool groupContainsCurrHoveredId = groupData.BackupHoveredIdIsAlive == false && g.HoveredId != 0;
        if (groupContainsCurrHoveredId)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HoveredWindow;

        // Forward the edited flag.
        if (g.AnyIdHasBeenEditedThisFrame && !groupData.BackupAnyIdHasBeenEditedThisFrame)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.Edited;

        // Forward the deactivated flag.
        g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.HasDeactivated;
        if (groupContainsDeactivatedId)
            g.LastItemData.StatusFlags |= ImGuiItemStatusFlags.Deactivated;

        g.GroupStack.RemoveAt(g.GroupStack.Count - 1);
        if (g.DebugShowGroupRects)
            window.DrawList.AddRect(groupBb.Min, groupBb.Max, 0xFFFF00FF); // IM_COL32(255, 0, 255, 255) [Debug]
    }

    #endregion

    #region [SECTION] DISABLED STATE

    /// <summary>
    /// Starts a disabled scope: while at least one nested scope is disabled, the interaction flag
    /// <see cref="ImGuiItemFlags.Disabled"/> stays set for all submitted items and the style alpha is
    /// multiplied by <see cref="ImGuiStyle.DisabledAlpha"/>.
    /// </summary>
    /// <param name="disabled">Whether this scope requests disabling. A nested <see langword="false"/> scope
    /// cannot re-enable an already disabled section; one <see langword="true"/> entry anywhere in the stack
    /// keeps everything disabled.</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::BeginDisabled()</c>.
    /// </para>
    /// <para>
    /// Canonical comments that the visual effect is currently expressed by altering
    /// <c>Style.Alpha</c> in place, equivalent to <c>PushStyleVar(ImGuiStyleVar_Alpha,.)</c>, and that a
    /// future styling system may change that mechanism. The port performs the same direct alpha backup and
    /// scaling so it interoperates with the already-existing
    /// <see cref="BeginDisabledOverrideReenable"/>/<see cref="EndDisabledOverrideReenable"/> pair, which
    /// restore <see cref="ImGuiContext.DisabledAlphaBackup"/> for nested root windows.
    /// </para>
    /// <para>
    /// The item-flag stack is pushed on every call, even for <paramref name="disabled"/> false, so
    /// <see cref="EndDisabled"/> always has a matching pop and the nesting depth is validated by
    /// <see cref="ImGuiContext.DisabledStackSize"/>. Mixing this pair with <c>PushItemFlag(ImGuiItemFlags.Disabled)</c>
    /// is not supported, matching canonical.
    /// </para>
    /// <para>
    /// Ownership: the chunk plan assigned the pair to Chunk 10, but part 08 already calls
    /// <c>BeginDisabled()</c>/<c>EndDisabled()</c>, so Chunk 4 realizes them here. The pair keeps its
    /// canonical public surface. Chunk 10 owns nothing in these
    /// members any more and must not redefine them.
    /// </para>
    /// </remarks>
    public static void BeginDisabled(bool disabled = true)
    {
        ImGuiContext g = GetContext();
        bool wasDisabled = (g.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0;
        if (!wasDisabled && disabled)
        {
            g.DisabledAlphaBackup = g.Style.Alpha;
            g.Style.Alpha *= g.Style.DisabledAlpha; // PushStyleVar(ImGuiStyleVar_Alpha, g.Style.Alpha * g.Style.DisabledAlpha);
        }
        if (wasDisabled || disabled)
            g.CurrentItemFlags |= ImGuiItemFlags.Disabled;
        g.ItemFlagsStack.Add(g.CurrentItemFlags); // FIXME-OPT: could we simply skip this and use DisabledStackSize?
        g.DisabledStackSize++;
    }

    /// <summary>
    /// Ends the innermost disabled scope started by <see cref="BeginDisabled"/>, restoring the prior item
    /// flags and, when disabling actually ends, the style alpha saved on the outermost enable-to-disable
    /// transition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::EndDisabled()</c>.
    /// </para>
    /// <para>
    /// Calling this more times than <see cref="BeginDisabled"/> is a recoverable user error: canonical
    /// <c>IM_ASSERT_USER_ERROR_RET</c> reports it through <see cref="ErrorLog"/> and returns without
    /// touching the stacks. The port follows that contract.
    /// </para>
    /// <para>
    /// Ownership: realize here together with <see cref="BeginDisabled"/> and keep the canonical public surface
    ///.; Chunk 10 owns nothing in these members any more and must not
    /// redefine them.
    /// </para>
    /// </remarks>
    public static void EndDisabled()
    {
        ImGuiContext g = GetContext();
        if (g.DisabledStackSize <= 0)
        {
            AssertUserError(false, "Calling EndDisabled() too many times!");
            return;
        }
        g.DisabledStackSize--;
        bool wasDisabled = (g.CurrentItemFlags & ImGuiItemFlags.Disabled) != 0;
        // PopItemFlag();
        g.ItemFlagsStack.RemoveAt(g.ItemFlagsStack.Count - 1);
        g.CurrentItemFlags = g.ItemFlagsStack[^1];
        if (wasDisabled && (g.CurrentItemFlags & ImGuiItemFlags.Disabled) == 0)
            g.Style.Alpha = g.DisabledAlphaBackup; // PopStyleVar();
    }

    #endregion

    #region [SECTION] LOGGING

    /// <summary>
    /// Latches a prefix and suffix to decorate the next text submission that the render logger captures.
    /// </summary>
    /// <param name="prefix">Text written immediately before the next logged text run; canonical does not
    /// copy the underlying data, so the string must stay unchanged until that submission.</param>
    /// <param name="suffix">Text written immediately after the next logged text run, or
    /// <see langword="null"/> when no suffix is wanted (canonical passes <c>NULL</c>).</param>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::LogSetNextTextDecoration(const char* prefix, const char* suffix)</c>
    /// (, declared.).
    /// Canonical has no active/inactive branch: the two pointers are latched unconditionally into
    /// <see cref="ImGuiContext.LogNextPrefix"/> and <see cref="ImGuiContext.LogNextSuffix"/> and consumed
    /// by <c>LogRenderedText</c> only if logging is subsequently enabled.
    /// </para>
    /// <para>
    /// The logging output subsystem itself (the <c>LogBegin</c>/<c>LogToTTY</c>/<c>LogToFile</c>/
    /// <c>LogToClipboard</c>/<c>LogToBuffer</c> family and <c>LogRenderedText</c>) is realized separately
    /// in <c>ImGuiDebug.cs</c>. This latch is called by the widgets in this file and must not be
    /// redefined.
    /// </para>
    /// </remarks>
    internal static void LogSetNextTextDecoration(string prefix, string? suffix)
    {
        ImGuiContext g = GetContext();
        g.LogNextPrefix = prefix;
        g.LogNextSuffix = suffix;
    }

    #endregion

    #region [SECTION] NEXT ITEM DATA: COLOR MARKER

    /// <summary>
    /// Queues a color marker on the next submitted item, enabling the color-marker display used by the
    /// drag, slider, and color widgets.
    /// </summary>
    /// <param name="col">The packed color used to draw the marker.</param>
    /// <remarks>
    /// Ports the canonical inline <c>SetNextItemColorMarker(ImU32 col)</c>
    ///.: the <see cref="ImGuiNextItemDataFlags.HasColorMarker"/>
    /// bit is OR'ed into <see cref="ImGuiNextItemData.HasFlags"/> and the color is stored in
    /// <see cref="ImGuiNextItemData.ColorMarker"/>. The next <c>ItemAdd</c> clears the flags, so the marker
    /// applies to exactly one item. Chunk 12 (metrics/debug) consumes the marker when it draws item
    /// diagnostics.
    /// </remarks>
    internal static void SetNextItemColorMarker(uint col)
    {
        ImGuiContext g = GetContext();
        g.NextItemData.HasFlags |= ImGuiNextItemDataFlags.HasColorMarker;
        g.NextItemData.ColorMarker = col;
    }

    #endregion

    #endregion


    #region SECTION: POPUP AND TOOLTIP PLACEMENT

    #region [SECTION] POPUP PLACEMENT

    /// <summary>
    /// The preferred direction order used by <see cref="FindBestWindowPosForPopupEx"/> for the combo-box
    /// position policy, matching the canonical <c>dir_preferred_order</c>
    /// </summary>
    /// <remarks>
    /// A combo popup wants a connecting edge to its preview box, so below/right/left/above are tried in
    /// that order after the direction used on the previous frame.
    /// </remarks>
    private static readonly ImGuiDir[] ComboPopupDirectionOrder =
    {
        ImGuiDir.Down, ImGuiDir.Right, ImGuiDir.Left, ImGuiDir.Up,
    };

    /// <summary>
    /// The preferred direction order used by <see cref="FindBestWindowPosForPopupEx"/> for the default and
    /// tooltip position policies, matching the canonical <c>dir_preferred_order</c>
    /// </summary>
    /// <remarks>
    /// Right/down/up/left maximize the chance that a free-standing popup or tooltip stays fully inside the
    /// allowed extent.
    /// </remarks>
    private static readonly ImGuiDir[] DefaultPopupDirectionOrder =
    {
        ImGuiDir.Right, ImGuiDir.Down, ImGuiDir.Up, ImGuiDir.Left,
    };

    /// <summary>
    /// Searches for a popup position that fits inside <paramref name="rOuter"/> while avoiding
    /// <paramref name="rAvoid"/> underneath the requested policy.
    /// </summary>
    /// <param name="refPos">The requested reference position; it is clamped into the outer rectangle and
    /// used as the cross-axis coordinate when a direction does not pin that axis.</param>
    /// <param name="size">The popup size to place.</param>
    /// <param name="lastDir">On entry, the direction chosen on the previous frame, or
    /// <see cref="ImGuiDir.None"/>; on return, the direction chosen this frame, or
    /// <see cref="ImGuiDir.None"/> when the fallback position was used.</param>
    /// <param name="rOuter">The visible placement rectangle, excluding safe-area padding.</param>
    /// <param name="rAvoid">The rectangle the popup should avoid, such as the combo preview box or the
    /// mouse cursor.</param>
    /// <param name="policy">The position policy selecting the direction order and fallback behavior.</param>
    /// <returns>The chosen popup position.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::FindBestWindowPosForPopupEx()</c>
    /// (, declared.).
    /// </para>
    /// <para>
    /// The combo policy first retries the previous direction, then walks the canonical
    /// below/right/left/above order and accepts the first side whose rectangle lies wholly inside the outer
    /// extent. The default and tooltip policies retry the previous direction and then walk
    /// right/down/up/left, skipping a side whose available width or height along that axis cannot hold the
    /// popup, and clamp the resulting top-left corner into the outer extent. When nothing fits, the
    /// direction resets to <see cref="ImGuiDir.None"/>: a tooltip returns the reference position offset by
    /// two pixels so the cursor stays clear, while any other popup is shifted back inside the display.
    /// </para>
    /// <para>
    /// No docking or viewport branch is present in this function at the canonical revision; the
    /// per-monitor extent adjustment lives in <see cref="GetPopupAllowedExtentRect"/>.
    /// </para>
    /// </remarks>
    internal static ImVec2 FindBestWindowPosForPopupEx(
        ImVec2 refPos,
        ImVec2 size,
        ref ImGuiDir lastDir,
        ImRect rOuter,
        ImRect rAvoid,
        ImGuiPopupPositionPolicy policy)
    {
        ImVec2 basePosClamped = ClampVector(refPos, rOuter.Min, rOuter.Max - size);

        // Combo-box policy: the popup prefers to touch the preview box with a connecting edge.
        if (policy == ImGuiPopupPositionPolicy.ComboBox)
        {
            for (int n = lastDir != ImGuiDir.None ? -1 : 0; n < (int)ImGuiDir.COUNT; n++)
            {
                ImGuiDir dir = n == -1 ? lastDir : ComboPopupDirectionOrder[n];
                if (n != -1 && dir == lastDir) // Already tried this direction.
                    continue;
                ImVec2 pos;
                if (dir == ImGuiDir.Down)
                    pos = new ImVec2(rAvoid.Min.X, rAvoid.Max.Y); // Below, toward right (default).
                else if (dir == ImGuiDir.Right)
                    pos = new ImVec2(rAvoid.Min.X, rAvoid.Min.Y - size.Y); // Above, toward right.
                else if (dir == ImGuiDir.Left)
                    pos = new ImVec2(rAvoid.Max.X - size.X, rAvoid.Max.Y); // Below, toward left.
                else
                    pos = new ImVec2(rAvoid.Max.X - size.X, rAvoid.Min.Y - size.Y); // Above, toward left.
                if (!rOuter.Contains(new ImRect(pos, pos + size)))
                    continue;
                lastDir = dir;
                return pos;
            }
        }

        // Tooltip and default policies: always retry the direction used on the previous frame, if any.
        if (policy == ImGuiPopupPositionPolicy.Tooltip || policy == ImGuiPopupPositionPolicy.Default)
        {
            for (int n = lastDir != ImGuiDir.None ? -1 : 0; n < (int)ImGuiDir.COUNT; n++)
            {
                ImGuiDir dir = n == -1 ? lastDir : DefaultPopupDirectionOrder[n];
                if (n != -1 && dir == lastDir) // Already tried this direction.
                    continue;

                float availW = (dir == ImGuiDir.Left ? rAvoid.Min.X : rOuter.Max.X) - (dir == ImGuiDir.Right ? rAvoid.Max.X : rOuter.Min.X);
                float availH = (dir == ImGuiDir.Up ? rAvoid.Min.Y : rOuter.Max.Y) - (dir == ImGuiDir.Down ? rAvoid.Max.Y : rOuter.Min.Y);

                // If there is not enough room on one axis, there is no point positioning on a side along
                // that axis (e.g. when not enough width, use a top/bottom position to maximize width).
                if (availW < size.X && (dir == ImGuiDir.Left || dir == ImGuiDir.Right))
                    continue;
                if (availH < size.Y && (dir == ImGuiDir.Up || dir == ImGuiDir.Down))
                    continue;

                ImVec2 pos;
                pos.X = dir == ImGuiDir.Left ? rAvoid.Min.X - size.X : (dir == ImGuiDir.Right ? rAvoid.Max.X : basePosClamped.X);
                pos.Y = dir == ImGuiDir.Up ? rAvoid.Min.Y - size.Y : (dir == ImGuiDir.Down ? rAvoid.Max.Y : basePosClamped.Y);

                // Clamp the top-left corner of the popup.
                pos.X = MathF.Max(pos.X, rOuter.Min.X);
                pos.Y = MathF.Max(pos.Y, rOuter.Min.Y);

                lastDir = dir;
                return pos;
            }
        }

        // Fallback when there is not enough room.
        lastDir = ImGuiDir.None;

        // For a tooltip, avoid the cursor at all cost, even if part of the tooltip is not visible.
        if (policy == ImGuiPopupPositionPolicy.Tooltip)
            return refPos + new ImVec2(2.0f, 2.0f);

        // Otherwise try to keep the popup within the display.
        ImVec2 fallbackPos = refPos;
        fallbackPos.X = MathF.Max(MathF.Min(fallbackPos.X + size.X, rOuter.Max.X) - size.X, rOuter.Min.X);
        fallbackPos.Y = MathF.Max(MathF.Min(fallbackPos.Y + size.Y, rOuter.Max.Y) - size.Y, rOuter.Min.Y);
        return fallbackPos;
    }

    /// <summary>
    /// Returns the screen rectangle within which popups belonging to <paramref name="window"/> may be
    /// placed, reduced by the style safe-area padding when the rectangle is large enough to absorb it.
    /// </summary>
    /// <param name="window">The popup window whose host viewport defines the extent.</param>
    /// <returns>The allowed placement rectangle.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::GetPopupAllowedExtentRect()</c>
    /// (, declared.).
    /// Note that this is used for popups, which may overlap the non-work area of individual viewports, so
    /// the full viewport rectangle is preferred over the work-area rectangle.
    /// </para>
    /// <para>
    /// When <c>ViewportAllowPlatformMonitorExtend</c> is non-negative the extent is the work area of the
    /// selected platform monitor, which lets a popup span the whole monitor hosting its viewport; that
    /// index is assigned by the docking layer. Otherwise the full main rectangle of the window's viewport
    /// is used, because popups may overlap the non-work area of a viewport.
    /// </para>
    /// </remarks>
    internal static ImRect GetPopupAllowedExtentRect(ImGuiWindow window)
    {
        ImGuiContext g = GetContext();
        ImRect rScreen;
        int monitorIndex = window.ViewportAllowPlatformMonitorExtend;
        if (monitorIndex >= 0 && monitorIndex < g.PlatformIO.Monitors.Count)
        {
            // The extent is expressed in the frame of reference of the selected viewport, so its minimum
            // is usually negative.
            ImGuiPlatformMonitor monitor = g.PlatformIO.Monitors[monitorIndex];
            rScreen = new ImRect(monitor.WorkPos, monitor.WorkPos + monitor.WorkSize);
        }
        else
        {
            // Use the full viewport area, not the work area, for popups.
            rScreen = window.Viewport!.GetMainRect();
        }
        ImVec2 padding = g.Style.DisplaySafeAreaPadding;
        rScreen.Expand(new ImVec2(
            rScreen.GetWidth() > padding.X * 2.0f ? -padding.X : 0.0f,
            rScreen.GetHeight() > padding.Y * 2.0f ? -padding.Y : 0.0f));
        return rScreen;
    }

    /// <summary>
    /// Chooses the automatic position of a popup, child menu, or tooltip window.
    /// </summary>
    /// <param name="window">The window being positioned; its flags select the placement rule.</param>
    /// <returns>The chosen window position.</returns>
    /// <remarks>
    /// <para>
    /// Ports canonical <c>ImGui::FindBestWindowPosForPopup()</c>
    /// (, declared.).
    /// </para>
    /// <para>
    /// Child menus are placed beside the parent menu item, avoiding the parent menu bar's clip rectangle
    /// when the parent is appending one, and overlapping the parent horizontally by
    /// <see cref="ImGuiStyle.ItemInnerSpacing"/>.X to convey depth. A regular popup is placed relative to
    /// itself under the default policy, and a tooltip follows the preferred navigation/mouse reference
    /// position with a cursor-sized avoidance rectangle; the canonical drag-and-drop tooltip path instead
    /// fixes its position through <c>SetNextWindowPos()</c> before <c>Begin()</c> and never reaches here.
    /// </para>
    /// <para>
    /// The canonical tooltip offset constants <c>TOOLTIP_DEFAULT_OFFSET_TOUCH = (0, -20)</c>,
    /// <c>TOOLTIP_DEFAULT_OFFSET_MOUSE = (16, 10)</c>, and <c>TOOLTIP_DEFAULT_PIVOT_TOUCH = (0.5, 1.0)</c>
    ///. are inlined because no shared member declares them.
    /// </para>
    /// <para>
    /// The canonical automatic-position direction is stored back into
    /// <see cref="ImGuiWindow.AutoPosLastDirection"/>; because that member is a property rather than a
    /// field, the direction round-trips through a local for each of the three placement paths.
    /// </para>
    /// <para>
    /// No docking or viewport branch is present in this function at the canonical revision; the
    /// per-monitor extent adjustment lives in <see cref="GetPopupAllowedExtentRect"/>, which this function
    /// calls first.
    /// </para>
    /// </remarks>
    internal static ImVec2 FindBestWindowPosForPopup(ImGuiWindow window)
    {
        ImGuiContext g = GetContext();

        ImRect rOuter = GetPopupAllowedExtentRect(window);
        if ((window.Flags & ImGuiWindowFlags.ChildMenu) != 0)
        {
            // Child menus typically request any position within the parent menu item, and then the menu is
            // moved outside the parent bounds; this is how child menus end up (most commonly) to the right
            // of the parent menu.
            ImGuiWindow parentWindow = window.ParentWindow!;
            float horizontalOverlap = g.Style.ItemInnerSpacing.X; // Convey the relative depth of each menu.
            ImRect rAvoid;
            if (parentWindow.DC.MenuBarAppending)
            {
                // Avoid the parent menu bar; a multi-line menu bar would instead need an explicit avoid
                // rectangle supplied by the calling window.
                rAvoid = new ImRect(-float.MaxValue, parentWindow.ClipRect.Min.Y, float.MaxValue, parentWindow.ClipRect.Max.Y);
            }
            else
            {
                rAvoid = new ImRect(
                    parentWindow.Pos.X + horizontalOverlap,
                    -float.MaxValue,
                    parentWindow.Pos.X + parentWindow.Size.X - horizontalOverlap - parentWindow.ScrollbarSizes.X,
                    float.MaxValue);
            }
            ImGuiDir lastDir = window.AutoPosLastDirection;
            ImVec2 pos = FindBestWindowPosForPopupEx(window.Pos, window.Size, ref lastDir, rOuter, rAvoid, ImGuiPopupPositionPolicy.Default);
            window.AutoPosLastDirection = lastDir;
            return pos;
        }
        if ((window.Flags & ImGuiWindowFlags.Popup) != 0)
        {
            // Ideally the avoid rectangle would be disabled here.
            ImGuiDir lastDir = window.AutoPosLastDirection;
            ImVec2 pos = FindBestWindowPosForPopupEx(
                window.Pos, window.Size, ref lastDir, rOuter, new ImRect(window.Pos, window.Pos), ImGuiPopupPositionPolicy.Default);
            window.AutoPosLastDirection = lastDir;
            return pos;
        }
        if ((window.Flags & ImGuiWindowFlags.Tooltip) != 0)
        {
            // Position the tooltip (always follows the mouse and clamps within the outer boundaries).
            ImAssert.Check(g.CurrentWindow == window);
            float scale = g.Style.MouseCursorScale;
            ImVec2 refPos = NavCalcPreferredRefPos(ImGuiWindowFlags.Tooltip);

            if (g.IO.MouseSource == ImGuiMouseSource.TouchScreen && NavCalcPreferredRefPosSource(ImGuiWindowFlags.Tooltip) == ImGuiInputSource.Mouse)
            {
                ImVec2 tooltipPosTouch = refPos + new ImVec2(0.0f, -20.0f) * scale - new ImVec2(0.5f, 1.0f) * window.Size;
                if (rOuter.Contains(new ImRect(tooltipPosTouch, tooltipPosTouch + window.Size)))
                    return tooltipPosTouch;
            }

            ImVec2 tooltipPos = refPos + new ImVec2(16.0f, 10.0f) * scale;
            ImRect rAvoid;
            if (g.NavCursorVisible && g.NavHighlightItemUnderNav && !g.IO.ConfigNavMoveSetMousePos)
            {
                rAvoid = new ImRect(refPos.X - 16.0f, refPos.Y - 8.0f, refPos.X + 16.0f, refPos.Y + 8.0f);
            }
            else
            {
                // Hard-coded based on the expected mouse cursor shape; the exact dimensions are not
                // important.
                rAvoid = new ImRect(refPos.X - 16.0f, refPos.Y - 8.0f, refPos.X + 24.0f * scale, refPos.Y + 24.0f * scale);
            }

            ImGuiDir lastDir = window.AutoPosLastDirection;
            ImVec2 pos = FindBestWindowPosForPopupEx(tooltipPos, window.Size, ref lastDir, rOuter, rAvoid, ImGuiPopupPositionPolicy.Tooltip);
            window.AutoPosLastDirection = lastDir;
            return pos;
        }
        ImAssert.Check(false, "FindBestWindowPosForPopup() called for a window with no popup, child-menu, or tooltip flag.");
        return window.Pos;
    }

    #endregion

    #endregion

    #region SECTION: INPUT TEXT DEBUG INSPECTOR

    /// <summary>
    /// Reports an input-text editor's internal state: its identity, cursor and selection, buffer accounting,
    /// preferred cursor column, and the whole undo ring.
    /// (, declared )
    /// </summary>
    /// <param name="state">The input-text state to report.</param>
    /// <remarks>
    /// <para>
    /// The undo ring is drawn into its own scrollable, vertically resizable child so a long history can be
    /// read without pushing the rest of the metrics window off screen. Entries that are neither undoable nor
    /// redoable at the current point are shown through the disabled style, and each entry previews the text it
    /// would restore.
    /// </para>
    /// <para>
    /// Canonical stores undo characters in a wide-character array and prints a counted prefix; this port
    /// stores them as bytes, one per character, matching the rest of the port's byte-oriented text storage,
    /// so the preview is decoded from those bytes and clamped to the storage that actually holds data.
    /// </para>
    /// </remarks>
    internal static void DebugNodeInputTextState(ImGuiInputTextState state)
    {
        ImGuiContext g = GetContext();
        ImStbTexteditState stbState = state.Stb;
        StbUndoState undoState = stbState.Undostate;

        Text("ID: 0x{0:X8}, ActiveID: 0x{1:X8}", state.ID, g.ActiveId);
        DebugLocateItemOnHover(state.ID);
        Text(
            "TextLen: {0}, Cursor: {1}{2}, Selection: {3}..{4}",
            state.TextLen,
            stbState.Cursor,
            (state.Flags & ImGuiInputTextFlags.WordWrap) != 0
                ? (state.LastMoveDirectionLR == (sbyte)ImGuiDir.Left ? " (L)" : " (R)")
                : string.Empty,
            stbState.SelectStart,
            stbState.SelectEnd);
        Text("BufCapacity: {0}, LineCount: {1}", state.BufCapacity, state.LineCount);
        Text("(Internal Buffer: TextA Size: {0}, Capacity: {1})", state.TextA.Count, state.TextA.Capacity);
        Text("has_preferred_x: {0} ({1:F2})", stbState.HasPreferredX, stbState.PreferredX);
        Text(
            "undo_point: {0}, redo_point: {1}, undo_char_point: {2}, redo_char_point: {3}",
            undoState.UndoPoint,
            undoState.RedoPoint,
            undoState.UndoCharPoint,
            undoState.RedoCharPoint);

        // The child height is sized from ten text lines of the current font size.
        if (BeginChild(
                "undopoints",
                new ImVec2(0.0f, g.FontSize * 10.0f),
                ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeY))
        {
            PushStyleVar(ImGuiStyleVar.ItemSpacing, new ImVec2(0.0f, 0.0f));
            for (int n = 0; n < undoState.UndoRec.Length; n++)
            {
                StbUndoRecord undoRec = undoState.UndoRec[n];
                // 'u' before the undo point is undoable, 'r' from the redo point on is redoable, and the gap
                // between the two is not reachable from the current history position.
                char undoRecType = n < undoState.UndoPoint ? 'u' : (n >= undoState.RedoPoint ? 'r' : ' ');
                if (undoRecType == ' ')
                    BeginDisabled();

                int bufPreviewLen = undoRecType != ' ' && undoRec.CharStorage != -1 ? undoRec.InsertLength : 0;
                string bufPreview = string.Empty;
                if (bufPreviewLen > 0
                    && undoRec.CharStorage >= 0
                    && undoRec.CharStorage < undoState.UndoChar.Length)
                {
                    int available = Math.Min(bufPreviewLen, undoState.UndoChar.Length - undoRec.CharStorage);
                    char[] previewChars = new char[available];
                    for (int c = 0; c < available; c++)
                        previewChars[c] = (char)undoState.UndoChar[undoRec.CharStorage + c];
                    bufPreview = new string(previewChars);
                }

                Text(
                    "{0} [{1:D2}] where {2:D3}, insert {3:D3}, delete {4:D3}, char_storage {5:D3} \"{6}\"",
                    undoRecType,
                    n,
                    undoRec.Where,
                    undoRec.InsertLength,
                    undoRec.DeleteLength,
                    undoRec.CharStorage,
                    bufPreview);
                if (undoRecType == ' ')
                    EndDisabled();
            }
            PopStyleVar();
        }
        EndChild();
    }

    #endregion

}
