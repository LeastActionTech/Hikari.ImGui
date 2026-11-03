// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Collections.Generic;

namespace Hikari.ImGui;

/// <summary>
/// Pure C# port of the TrueType parsing, glyph-outline, and scanline rasterization routines from
/// stb_truetype that the default ImGui font loader uses.
/// </summary>
/// <remarks>
/// The port covers the canonical <c>glyf</c> path and the CFF/Type2 charstring path, including the
/// CID-keyed FDArray/FDSelect local subroutine selection. It deliberately omits hinting (hint and
/// counter masks are skipped), SDF generation, font packing, SVG glyph tables, and the GPOS
/// kerning path because the embedded faces and the current atlas do not require them. Each
/// omission is documented at the call site that would otherwise use it.
/// </remarks>
internal sealed class ImStbTrueTypeFont
{
    #region Outline vertex constants and small data types

    /// <summary>Vertex type for a contour start, ported from <c>STBTT_vmove</c>.</summary>
    private const byte VertexMove = 1;

    /// <summary>Vertex type for a straight segment, ported from <c>STBTT_vline</c>.</summary>
    private const byte VertexLine = 2;

    /// <summary>Vertex type for a quadratic bezier control point, ported from <c>STBTT_vcurve</c>.</summary>
    private const byte VertexQuadratic = 3;

    /// <summary>Vertex type for a cubic bezier control point, ported from <c>STBTT_vcubic</c>.</summary>
    private const byte VertexCubic = 4;

    /// <summary>
    /// Outline vertex, mirroring <c>stbtt_vertex</c>.
    /// </summary>
    /// <remarks>
    /// The coordinate fields are 16-bit in the canonical struct because the TrueType <c>glyf</c>
    /// table stores signed 16-bit coordinates; the cubic control fields carry the Type2 charstring
    /// cubic control points, and remain zero for quadratic and line vertices.
    /// </remarks>
    internal struct Vertex
    {
        /// <summary>Vertex kind: move, line, quadratic, or cubic.</summary>
        public byte Type;

        /// <summary>On-curve end point X coordinate.</summary>
        public short X;

        /// <summary>On-curve end point Y coordinate.</summary>
        public short Y;

        /// <summary>Quadratic control point X coordinate.</summary>
        public short Cx;

        /// <summary>Quadratic control point Y coordinate.</summary>
        public short Cy;

        /// <summary>Cubic first control point X coordinate; unused for quadratic outlines.</summary>
        public short Cx1;

        /// <summary>Cubic first control point Y coordinate; unused for quadratic outlines.</summary>
        public short Cy1;
    }

    /// <summary>Flattened outline point, mirroring <c>stbtt__point</c>.</summary>
    private struct Point
    {
        /// <summary>Point X coordinate in font units.</summary>
        public float X;

        /// <summary>Point Y coordinate in font units.</summary>
        public float Y;
    }

    /// <summary>Rasterization edge, mirroring <c>stbtt__edge</c>.</summary>
    private struct Edge
    {
        /// <summary>Top endpoint X coordinate in scaled pixel space.</summary>
        public float X0;

        /// <summary>Top endpoint Y coordinate in scaled pixel space.</summary>
        public float Y0;

        /// <summary>Bottom endpoint X coordinate in scaled pixel space.</summary>
        public float X1;

        /// <summary>Bottom endpoint Y coordinate in scaled pixel space.</summary>
        public float Y1;

        /// <summary>Non-zero when the edge points downward and therefore winds positively.</summary>
        public bool Invert;
    }

    /// <summary>
    /// Active edge list node, mirroring <c>stbtt__active_edge</c> for rasterizer version 2.
    /// </summary>
    private sealed class ActiveEdge
    {
        /// <summary>Next active edge in the current scanline list; null ends the list.</summary>
        public ActiveEdge? Next;

        /// <summary>X intersection with the current scanline top.</summary>
        public float Fx;

        /// <summary>Change in X per unit change in Y along the edge.</summary>
        public float Fdx;

        /// <summary>Reciprocal of <see cref="Fdx"/>; zero for vertical edges.</summary>
        public float Fdy;

        /// <summary>Winding direction: +1 for a downward edge, -1 for an upward edge.</summary>
        public float Direction;

        /// <summary>Y coordinate where the edge begins.</summary>
        public float Sy;

        /// <summary>Y coordinate where the edge ends.</summary>
        public float Ey;
    }

    /// <summary>Single-channel destination bitmap, mirroring <c>stbtt__bitmap</c>.</summary>
    private struct Bitmap
    {
        /// <summary>Destination pixel buffer, one byte per pixel.</summary>
        public byte[] Pixels;

        /// <summary>Bitmap width in pixels.</summary>
        public int W;

        /// <summary>Bitmap height in pixels.</summary>
        public int H;

        /// <summary>Distance in bytes between the starts of consecutive rows.</summary>
        public int Stride;
    }

    #endregion

    #region Font directory state

    /// <summary>Complete font file image. The array is owned by the caller and must outlive this instance.</summary>
    public byte[] Data { get; }

    /// <summary>Offset of the selected font within a TrueType collection; zero for a plain TTF.</summary>
    public int FontStart { get; }

    /// <summary>Number of glyphs declared by the <c>maxp</c> table, or 0xFFFF when absent.</summary>
    private int _numGlyphs;

    /// <summary>Offset of the <c>loca</c> table, or zero.</summary>
    private int _loca;

    /// <summary>Offset of the <c>head</c> table.</summary>
    private int _head;

    /// <summary>Offset of the <c>glyf</c> table, or zero for a CFF font.</summary>
    private int _glyf;

    /// <summary>Offset of the <c>hhea</c> table.</summary>
    private int _hhea;

    /// <summary>Offset of the <c>hmtx</c> table.</summary>
    private int _hmtx;

    /// <summary>Offset of the <c>kern</c> table, or zero.</summary>
    private int _kern;

    /// <summary>Offset of a selected <c>cmap</c> subtable suitable for Unicode lookup.</summary>
    private int _indexMap;

    /// <summary>Value of <c>head.indexToLocFormat</c>: 0 selects 16-bit, 1 selects 32-bit offsets.</summary>
    private int _indexToLocFormat;

    /// <summary>
    /// Byte range covering the CFF table, or an empty buffer for a <c>glyf</c>-based face. A
    /// non-empty size selects the Type2 charstring path for outlines and bounding boxes.
    /// </summary>
    private CffBuffer _cff = CffBuffer.Empty;

    /// <summary>CharStrings INDEX decoded from the top dictionary, or an empty buffer for <c>glyf</c> faces.</summary>
    private CffBuffer _charstrings = CffBuffer.Empty;

    /// <summary>Global subroutine INDEX decoded from the CFF table, or an empty buffer.</summary>
    private CffBuffer _gsubrs = CffBuffer.Empty;

    /// <summary>Local subroutines selected by the top dictionary's Private entry, or an empty buffer.</summary>
    private CffBuffer _subrs = CffBuffer.Empty;

    /// <summary>Font DICT INDEX of a CID-keyed CFF face, or an empty buffer for a non-CID face.</summary>
    private CffBuffer _fontdicts = CffBuffer.Empty;

    /// <summary>FDSelect table of a CID-keyed CFF face, or an empty buffer for a non-CID face.</summary>
    private CffBuffer _fdselect = CffBuffer.Empty;

    /// <summary>Initializes the directory state for one font; callers must use <see cref="Init"/>.</summary>
    /// <param name="data">Complete font file image.</param>
    /// <param name="fontStart">Offset of the selected font inside <paramref name="data"/>.</param>
    private ImStbTrueTypeFont(byte[] data, int fontStart)
    {
        Data = data;
        FontStart = fontStart;
    }

    /// <summary>
    /// Returns the byte offset of the font at <paramref name="index"/> within a plain TTF or a
    /// TrueType collection. Ported from <c>stbtt_GetFontOffsetForIndex_internal</c>
    /// </summary>
    /// <param name="data">Complete font file image.</param>
    /// <param name="index">Zero-based font index inside a collection; use zero for a plain TTF.</param>
    /// <returns>The font offset, or -1 when the index or container signature is not valid.</returns>
    public static int GetFontOffsetForIndex(byte[] data, int index)
    {
        if (data == null || data.Length < 4)
            return -1;

        if (IsFont(data, 0))
            return index == 0 ? 0 : -1;

        // TrueType collection: 'ttcf', a version, then a table of font offsets.
        if (Tag(data, 0, (byte)'t', (byte)'t', (byte)'c', (byte)'f'))
        {
            uint version = U32At(data, 4);
            if (version == 0x00010000 || version == 0x00020000)
            {
                int count = (int)U32At(data, 8);
                if (index < 0 || index >= count)
                    return -1;
                return (int)U32At(data, 12 + index * 4);
            }
        }

        return -1;
    }

    /// <summary>
    /// Parses the font directory and prepares glyph lookup. Ported from <c>stbtt_InitFont_internal</c>
    ///., including the CFF/Type2 initialization branch
    ///. used by OpenType faces that carry a <c>CFF </c> table.
    /// </summary>
    /// <param name="data">Complete font file image.</param>
    /// <param name="offset">Font offset returned by <see cref="GetFontOffsetForIndex"/>.</param>
    /// <returns>A ready font, or <see langword="null"/> when required tables are missing or malformed.</returns>
    public static ImStbTrueTypeFont? Init(byte[] data, int offset)
    {
        if (data == null || offset < 0 || offset + 12 > data.Length || !IsFont(data, offset))
            return null;

        var font = new ImStbTrueTypeFont(data, offset);

        int cmap = (int)FindTable(data, offset, "cmap");
        font._loca = (int)FindTable(data, offset, "loca");
        font._head = (int)FindTable(data, offset, "head");
        font._glyf = (int)FindTable(data, offset, "glyf");
        font._hhea = (int)FindTable(data, offset, "hhea");
        font._hmtx = (int)FindTable(data, offset, "hmtx");
        font._kern = (int)FindTable(data, offset, "kern");

        if (cmap == 0 || font._head == 0 || font._hhea == 0 || font._hmtx == 0)
            return null;

        if (font._glyf != 0)
        {
            // TrueType outlines require the glyph location table.
            if (font._loca == 0)
                return null;
        }
        else
        {
            // OpenType with CFF outlines. Ported from the else branch of stbtt_InitFont_internal
            int cff = (int)FindTable(data, offset, "CFF ");
            if (cff == 0)
                return null;

            font._fontdicts = CffBuffer.Empty;
            font._fdselect = CffBuffer.Empty;

            // Canonical opens a fixed 512 MB window at data+cff because it trusts the offsets in
            // the table; the port bounds the window to the remaining image, which is the same bytes.
            font._cff = new CffBuffer(data, cff, data.Length - cff);
            CffBuffer b = font._cff.Clone();

            // Header: major, minor, header size, offset size. Skip to the declared header size so
            // the name INDEX starts at the canonical position.
            b.Skip(2);
            b.Seek(b.Get8());

            b.GetIndex();                          // name INDEX (only the first font is used)
            CffBuffer topDictIndex = b.GetIndex();
            CffBuffer topDict = CffBuffer.IndexGet(topDictIndex, 0);
            b.GetIndex();                          // string INDEX
            font._gsubrs = b.GetIndex();

            var charStrings = new uint[1];
            var cstype = new uint[] { 2 };         // Default Type 2 until the top dictionary says otherwise.
            var fdArrayOffset = new uint[1];
            var fdSelectOffset = new uint[1];
            topDict.DictGetInts(17, 1, charStrings);
            topDict.DictGetInts(0x100 | 6, 1, cstype);
            topDict.DictGetInts(0x100 | 36, 1, fdArrayOffset);
            topDict.DictGetInts(0x100 | 37, 1, fdSelectOffset);
            font._subrs = font.GetSubrs(b, topDict);

            // Only Type 2 charstrings are supported, matching canonical.
            if (cstype[0] != 2)
                return null;
            if (charStrings[0] == 0)
                return null;

            if (fdArrayOffset[0] != 0)
            {
                // A non-zero FDArray marks a CID-keyed font, whose local subroutines are selected
                // per glyph through FDSelect.
                if (fdSelectOffset[0] == 0)
                    return null;
                b.Seek((int)fdArrayOffset[0]);
                font._fontdicts = b.GetIndex();
                font._fdselect = b.Range((int)fdSelectOffset[0], b.Size - (int)fdSelectOffset[0]);
            }

            b.Seek((int)charStrings[0]);
            font._charstrings = b.GetIndex();
        }

        int maxp = (int)FindTable(data, offset, "maxp");
        font._numGlyphs = maxp != 0 ? font.U16(maxp + 4) : 0xFFFF;

        // Select a Unicode cmap subtable: prefer Windows UCS-2/UCS-4, fall back to Unicode platform.
        int numTables = font.U16(cmap + 2);
        font._indexMap = 0;
        for (int i = 0; i < numTables; i++)
        {
            int record = cmap + 4 + 8 * i;
            int platform = font.U16(record);
            int encoding = font.U16(record + 2);
            if (platform == 3 && (encoding == 1 || encoding == 10))
                font._indexMap = cmap + (int)font.U32(record + 4);
            else if (platform == 0)
                font._indexMap = cmap + (int)font.U32(record + 4);
        }

        if (font._indexMap == 0)
            return null;

        font._indexToLocFormat = font.U16(font._head + 50);
        return font;
    }

    #endregion

    #region cmap glyph lookup

    /// <summary>
    /// Maps a Unicode codepoint to a glyph index through the selected cmap subtable. Ported from
    /// <c>stbtt_FindGlyphIndex</c>.; formats 0, 4, 6, 12 and 13
    /// are supported, matching the canonical routine.
    /// </summary>
    /// <param name="codepoint">Unicode scalar value to look up.</param>
    /// <returns>The glyph index, or zero (the missing-glyph index) when absent.</returns>
    public int FindGlyphIndex(int codepoint)
    {
        int map = _indexMap;
        int format = U16(map);

        if (format == 0)
        {
            int bytes = U16(map + 2);
            if (codepoint < bytes - 6 && codepoint >= 0)
                return ByteAt(map + 6 + codepoint);
            return 0;
        }

        if (format == 6)
        {
            int first = U16(map + 6);
            int count = U16(map + 8);
            if (codepoint >= first && codepoint < first + count)
                return U16(map + 10 + (codepoint - first) * 2);
            return 0;
        }

        if (format == 4)
        {
            if (codepoint > 0xFFFF || codepoint < 0)
                return 0;

            int segCount = U16(map + 6) >> 1;
            int searchRange = U16(map + 8) >> 1;
            int entrySelector = U16(map + 10);
            int rangeShift = U16(map + 12) >> 1;

            int endCount = map + 14;
            int search = endCount;

            if (codepoint >= U16(search + rangeShift * 2))
                search += rangeShift * 2;

            search -= 2;
            while (entrySelector != 0)
            {
                searchRange >>= 1;
                int end = U16(search + searchRange * 2);
                if (codepoint > end)
                    search += searchRange * 2;
                --entrySelector;
            }
            search += 2;

            int item = (search - endCount) >> 1;
            int start = U16(map + 14 + segCount * 2 + 2 + 2 * item);
            int last = U16(endCount + 2 * item);
            if (codepoint < start || codepoint > last)
                return 0;

            int offset = U16(map + 14 + segCount * 6 + 2 + 2 * item);
            if (offset == 0)
                return (ushort)(codepoint + S16(map + 14 + segCount * 4 + 2 + 2 * item));

            return U16(map + 14 + segCount * 6 + 2 + 2 * item + offset + (codepoint - start) * 2);
        }

        if (format == 12 || format == 13)
        {
            int ngroups = (int)U32(map + 12);
            int low = 0;
            int high = ngroups;
            while (low < high)
            {
                int mid = low + ((high - low) >> 1);
                uint startChar = U32(map + 16 + mid * 12);
                uint endChar = U32(map + 16 + mid * 12 + 4);
                if ((uint)codepoint < startChar)
                    high = mid;
                else if ((uint)codepoint > endChar)
                    low = mid + 1;
                else
                {
                    uint startGlyph = U32(map + 16 + mid * 12 + 8);
                    return format == 12 ? (int)(startGlyph + (uint)codepoint - startChar) : (int)startGlyph;
                }
            }
            return 0;
        }

        return 0;
    }

    #endregion

    #region Horizontal and vertical metrics

    /// <summary>
    /// Reads horizontal advance width and left side bearing for a glyph. Ported from
    /// <c>stbtt_GetGlyphHMetrics</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index to query.</param>
    /// <param name="advanceWidth">Receives the advance width in font units.</param>
    /// <param name="leftSideBearing">Receives the left side bearing in font units.</param>
    /// <returns><see langword="true"/> when the <c>hmtx</c> table is present.</returns>
    public bool GetGlyphHMetrics(int glyphIndex, out int advanceWidth, out int leftSideBearing)
    {
        advanceWidth = 0;
        leftSideBearing = 0;
        if (_hmtx == 0)
            return false;

        int numOfLongHorMetrics = U16(_hhea + 34);
        if (glyphIndex < numOfLongHorMetrics)
        {
            advanceWidth = S16(_hmtx + 4 * glyphIndex);
            leftSideBearing = S16(_hmtx + 4 * glyphIndex + 2);
        }
        else
        {
            advanceWidth = S16(_hmtx + 4 * (numOfLongHorMetrics - 1));
            leftSideBearing = S16(_hmtx + 4 * numOfLongHorMetrics + 2 * (glyphIndex - numOfLongHorMetrics));
        }
        return true;
    }

    /// <summary>
    /// Reads the font-wide vertical metrics. Ported from <c>stbtt_GetFontVMetrics</c>
    /// </summary>
    /// <param name="ascent">Receives the typographic ascent in font units.</param>
    /// <param name="descent">Receives the typographic descent in font units (typically negative).</param>
    /// <param name="lineGap">Receives the line gap in font units.</param>
    public void GetFontVMetrics(out int ascent, out int descent, out int lineGap)
    {
        ascent = S16(_hhea + 4);
        descent = S16(_hhea + 6);
        lineGap = S16(_hhea + 8);
    }

    /// <summary>
    /// Computes the scale factor that maps font units to a requested pixel height. Ported from
    /// <c>stbtt_ScaleForPixelHeight</c>.
    /// </summary>
    /// <param name="pixels">Requested ascent-to-descent height in pixels.</param>
    /// <returns>The multiplicative scale, or zero when the font reports a zero height.</returns>
    public float ScaleForPixelHeight(float pixels)
    {
        int fheight = S16(_hhea + 4) - S16(_hhea + 6);
        return fheight == 0 ? 0.0f : pixels / fheight;
    }

    #endregion

    #region Glyph bounding boxes

    /// <summary>
    /// Reads a glyph's bounding box from the <c>glyf</c> table or, for a CFF face, from the bounds
    /// accumulated by the Type2 charstring interpreter. Ported from <c>stbtt_GetGlyphBox</c> and
    /// <c>stbtt__GetGlyfOffset</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index to query.</param>
    /// <param name="x0">Receives the minimum X in font units.</param>
    /// <param name="y0">Receives the minimum Y in font units.</param>
    /// <param name="x1">Receives the maximum X in font units.</param>
    /// <param name="y1">Receives the maximum Y in font units.</param>
    /// <returns><see langword="false"/> when the glyf glyph has no outline or is out of range.</returns>
    public bool GetGlyphBox(int glyphIndex, out int x0, out int y0, out int x1, out int y1)
    {
        if (_cff.Size != 0)
        {
            // The charstring interpreter reports a zero box when the glyph draws nothing, and
            // canonical still reports success for CFF faces.
            GetGlyphInfoT2(glyphIndex, out x0, out y0, out x1, out y1);
            return true;
        }

        x0 = y0 = x1 = y1 = 0;
        int g = GetGlyfOffset(glyphIndex);
        if (g < 0)
            return false;

        x0 = S16(g + 2);
        y0 = S16(g + 4);
        x1 = S16(g + 6);
        y1 = S16(g + 8);
        return true;
    }

    /// <summary>
    /// Converts a glyph bounding box into the integer pixel box touched by the rasterized glyph.
    /// Ported from <c>stbtt_GetGlyphBitmapBoxSubpixel</c>.
    /// </summary>
    /// <param name="glyph">Glyph index to query.</param>
    /// <param name="scaleX">Horizontal font-unit-to-pixel scale.</param>
    /// <param name="scaleY">Vertical font-unit-to-pixel scale.</param>
    /// <param name="shiftX">Subpixel horizontal shift in pixels.</param>
    /// <param name="shiftY">Subpixel vertical shift in pixels.</param>
    /// <param name="x0">Receives the left pixel edge.</param>
    /// <param name="y0">Receives the top pixel edge.</param>
    /// <param name="x1">Receives the right pixel edge.</param>
    /// <param name="y1">Receives the bottom pixel edge.</param>
    public void GetGlyphBitmapBoxSubpixel(int glyph, float scaleX, float scaleY, float shiftX, float shiftY,
        out int x0, out int y0, out int x1, out int y1)
    {
        // Pixels are treated as little squares, so the box is floor/ceil of the scaled outline box.
        if (!GetGlyphBox(glyph, out int gx0, out int gy0, out int gx1, out int gy1))
        {
            // e.g. the space character has no outline.
            x0 = y0 = x1 = y1 = 0;
            return;
        }

        x0 = (int)MathF.Floor(gx0 * scaleX + shiftX);
        y0 = (int)MathF.Floor(-gy1 * scaleY + shiftY);
        x1 = (int)MathF.Ceiling(gx1 * scaleX + shiftX);
        y1 = (int)MathF.Ceiling(-gy0 * scaleY + shiftY);
    }

    #endregion

    #region Glyph outlines

    /// <summary>
    /// Resolves the <c>glyf</c> table offset of a glyph, or -1 when the glyph is empty or invalid.
    /// Ported from <c>stbtt__GetGlyfOffset</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index to resolve.</param>
    /// <returns>The glyph record offset, or -1.</returns>
    private int GetGlyfOffset(int glyphIndex)
    {
        if (glyphIndex >= _numGlyphs || glyphIndex < 0)
            return -1;
        if (_indexToLocFormat >= 2)
            return -1;

        int g1;
        int g2;
        if (_indexToLocFormat == 0)
        {
            g1 = _glyf + U16(_loca + glyphIndex * 2) * 2;
            g2 = _glyf + U16(_loca + glyphIndex * 2 + 2) * 2;
        }
        else
        {
            g1 = _glyf + (int)U32(_loca + glyphIndex * 4);
            g2 = _glyf + (int)U32(_loca + glyphIndex * 4 + 4);
        }

        return g1 == g2 ? -1 : g1;
    }

    /// <summary>
    /// Decodes a glyph outline into vertices. Ported from <c>stbtt_GetGlyphShape</c>, which selects
    /// <c>stbtt__GetGlyphShapeT2</c> for a CFF face and <c>stbtt__GetGlyphShapeTT</c> otherwise.
    /// </summary>
    /// <param name="glyphIndex">Glyph index to decode.</param>
    /// <param name="depth">Recursion depth for compound glyphs; bounds malformed fonts.</param>
    /// <returns>The ordered outline vertices; empty for an empty glyph.</returns>
    /// <remarks>
    /// Exposed to the assembly (and the test project) so the outline decode can be asserted directly
    /// against the canonical <c>stbtt_GetGlyphShape</c> return value.
    /// </remarks>
    internal Vertex[] GetGlyphShape(int glyphIndex, int depth = 0)
    {
        if (_cff.Size != 0)
            return GetGlyphShapeT2(glyphIndex);

        if (depth > 4)
            return Array.Empty<Vertex>(); // Robustness bound beyond canonical, which recurses freely.

        int g = GetGlyfOffset(glyphIndex);
        if (g < 0)
            return Array.Empty<Vertex>();

        short numberOfContours = (short)U16(g);
        if (numberOfContours > 0)
            return ReadSimpleGlyph(g, numberOfContours);
        if (numberOfContours < 0)
            return ReadCompoundGlyph(g, depth);
        return Array.Empty<Vertex>();
    }

    /// <summary>
    /// Reads a simple (single-contour-set) glyph, decoding flags and coordinate deltas. Ported from
    /// the <c>numberOfContours &gt; 0</c> branch of <c>stbtt__GetGlyphShapeTT</c>
    /// </summary>
    /// <param name="g">Offset of the glyph record.</param>
    /// <param name="numberOfContours">Contour count read from the glyph header.</param>
    /// <returns>The decoded outline vertices.</returns>
    private Vertex[] ReadSimpleGlyph(int g, short numberOfContours)
    {
        int endPtsOfContours = g + 10;
        int ins = U16(g + 10 + numberOfContours * 2);
        int points = g + 10 + numberOfContours * 2 + 2 + ins;

        int n = 1 + U16(endPtsOfContours + numberOfContours * 2 - 2);

        // First pass: accumulate raw flags and absolute coordinates. The canonical code stores the
        // uninterpreted data at the tail of one allocation; separate storage is equivalent and safer.
        var rawType = new byte[n];
        var rawX = new short[n];
        var rawY = new short[n];

        int flagCount = 0;
        byte flags = 0;
        for (int i = 0; i < n; i++)
        {
            if (flagCount == 0)
            {
                flags = ByteAt(points++);
                if ((flags & 8) != 0)
                    flagCount = ByteAt(points++);
            }
            else
            {
                --flagCount;
            }
            rawType[i] = flags;
        }

        int x = 0;
        for (int i = 0; i < n; i++)
        {
            flags = rawType[i];
            if ((flags & 2) != 0)
            {
                short dx = (short)ByteAt(points++);
                x += (flags & 16) != 0 ? dx : -dx;
            }
            else if ((flags & 16) == 0)
            {
                x += (short)((ByteAt(points) << 8) + ByteAt(points + 1));
                points += 2;
            }
            rawX[i] = (short)x;
        }

        int y = 0;
        for (int i = 0; i < n; i++)
        {
            flags = rawType[i];
            if ((flags & 4) != 0)
            {
                short dy = (short)ByteAt(points++);
                y += (flags & 32) != 0 ? dy : -dy;
            }
            else if ((flags & 32) == 0)
            {
                y += (short)((ByteAt(points) << 8) + ByteAt(points + 1));
                points += 2;
            }
            rawY[i] = (short)y;
        }

        // Second pass: convert the point stream into move/line/quadratic vertices, interpolating an
        // on-curve point whenever two control points are adjacent and wrapping each contour.
        var vertices = new List<Vertex>(n + numberOfContours);
        int j = 0;
        int nextMove = 0;
        int wasOff = 0;
        int startOff = 0;
        int sx = 0, sy = 0, cx = 0, cy = 0, scx = 0, scy = 0;

        for (int i = 0; i < n; i++)
        {
            flags = rawType[i];
            x = rawX[i];
            y = rawY[i];

            if (nextMove == i)
            {
                if (i != 0)
                    CloseShape(vertices, wasOff, startOff, sx, sy, scx, scy, cx, cy);

                startOff = (flags & 1) == 0 ? 1 : 0;
                if (startOff != 0)
                {
                    // The contour starts on a control point; find an on-curve start.
                    scx = x;
                    scy = y;
                    if (i + 1 < n && (rawType[i + 1] & 1) == 0)
                    {
                        sx = (x + rawX[i + 1]) >> 1;
                        sy = (y + rawY[i + 1]) >> 1;
                    }
                    else if (i + 1 < n)
                    {
                        sx = rawX[i + 1];
                        sy = rawY[i + 1];
                        ++i; // Consume the point used as the contour start.
                    }
                }
                else
                {
                    sx = x;
                    sy = y;
                }

                vertices.Add(new Vertex { Type = VertexMove, X = (short)sx, Y = (short)sy });
                wasOff = 0;
                nextMove = 1 + U16(endPtsOfContours + j * 2);
                ++j;
            }
            else if ((flags & 1) == 0)
            {
                if (wasOff != 0)
                {
                    vertices.Add(new Vertex { Type = VertexQuadratic, X = (short)((cx + x) >> 1), Y = (short)((cy + y) >> 1), Cx = (short)cx, Cy = (short)cy });
                }
                cx = x;
                cy = y;
                wasOff = 1;
            }
            else
            {
                if (wasOff != 0)
                    vertices.Add(new Vertex { Type = VertexQuadratic, X = (short)x, Y = (short)y, Cx = (short)cx, Cy = (short)cy });
                else
                    vertices.Add(new Vertex { Type = VertexLine, X = (short)x, Y = (short)y });
                wasOff = 0;
            }
        }

        CloseShape(vertices, wasOff, startOff, sx, sy, scx, scy, cx, cy);
        return vertices.ToArray();
    }

    /// <summary>
    /// Appends the vertices that close the current contour. Ported from <c>stbtt__close_shape</c>
    /// </summary>
    /// <param name="vertices">Destination vertex list.</param>
    /// <param name="wasOff">Non-zero when the previous point was a control point.</param>
    /// <param name="startOff">Non-zero when the contour started on a control point.</param>
    /// <param name="sx">Contour start X.</param>
    /// <param name="sy">Contour start Y.</param>
    /// <param name="scx">Start control X.</param>
    /// <param name="scy">Start control Y.</param>
    /// <param name="cx">Last control X.</param>
    /// <param name="cy">Last control Y.</param>
    private static void CloseShape(List<Vertex> vertices, int wasOff, int startOff,
        int sx, int sy, int scx, int scy, int cx, int cy)
    {
        if (startOff != 0)
        {
            if (wasOff != 0)
                vertices.Add(new Vertex { Type = VertexQuadratic, X = (short)((cx + scx) >> 1), Y = (short)((cy + scy) >> 1), Cx = (short)cx, Cy = (short)cy });
            vertices.Add(new Vertex { Type = VertexQuadratic, X = (short)sx, Y = (short)sy, Cx = (short)scx, Cy = (short)scy });
        }
        else if (wasOff != 0)
        {
            vertices.Add(new Vertex { Type = VertexQuadratic, X = (short)sx, Y = (short)sy, Cx = (short)cx, Cy = (short)cy });
        }
        else
        {
            vertices.Add(new Vertex { Type = VertexLine, X = (short)sx, Y = (short)sy });
        }
    }

    /// <summary>
    /// Reads a compound glyph, recursively decoding and transforming each component. Ported from the
    /// <c>numberOfContours &lt; 0</c> branch of <c>stbtt__GetGlyphShapeTT</c>
    ///. The canonical <c>WE_HAVE_INSTRUCTIONS</c> hinting
    /// program is ignored, as it is by the canonical reader used here.
    /// </summary>
    /// <param name="g">Offset of the glyph record.</param>
    /// <param name="depth">Current recursion depth.</param>
    /// <returns>The concatenated transformed component vertices.</returns>
    private Vertex[] ReadCompoundGlyph(int g, int depth)
    {
        var all = new List<Vertex>();
        int comp = g + 10;
        int more = 1;

        while (more != 0 && comp + 4 <= Data.Length)
        {
            int flags = (short)U16(comp);
            comp += 2;
            int gidx = (short)U16(comp);
            comp += 2;

            float m0 = 1, m1 = 0, m2 = 0, m3 = 1, m4 = 0, m5 = 0;

            if ((flags & 2) != 0)
            {
                if ((flags & 1) != 0)
                {
                    m4 = (short)U16(comp);
                    comp += 2;
                    m5 = (short)U16(comp);
                    comp += 2;
                }
                else
                {
                    m4 = (sbyte)ByteAt(comp);
                    comp += 1;
                    m5 = (sbyte)ByteAt(comp);
                    comp += 1;
                }
            }

            if ((flags & (1 << 3)) != 0)
            {
                m0 = m3 = (short)U16(comp) / 16384.0f;
                comp += 2;
                m1 = m2 = 0;
            }
            else if ((flags & (1 << 6)) != 0)
            {
                m0 = (short)U16(comp) / 16384.0f;
                comp += 2;
                m1 = m2 = 0;
                m3 = (short)U16(comp) / 16384.0f;
                comp += 2;
            }
            else if ((flags & (1 << 7)) != 0)
            {
                m0 = (short)U16(comp) / 16384.0f;
                comp += 2;
                m1 = (short)U16(comp) / 16384.0f;
                comp += 2;
                m2 = (short)U16(comp) / 16384.0f;
                comp += 2;
                m3 = (short)U16(comp) / 16384.0f;
                comp += 2;
            }

            float ms = MathF.Sqrt(m0 * m0 + m1 * m1);
            float ns = MathF.Sqrt(m2 * m2 + m3 * m3);

            Vertex[] component = GetGlyphShape(gidx, depth + 1);
            for (int i = 0; i < component.Length; i++)
            {
                Vertex v = component[i];
                TransformVertex(ref v, m0, m1, m2, m3, m4, m5, ms, ns);
                all.Add(v);
            }

            more = flags & (1 << 5);
        }

        return all.ToArray();
    }

    /// <summary>
    /// Applies a compound-glyph 2x3 affine transform to one vertex. Ported from the transform loop
    /// inside <c>stbtt__GetGlyphShapeTT</c>.
    /// </summary>
    /// <param name="v">Vertex to transform in place.</param>
    /// <param name="m0">Transform column 0 scale.</param>
    /// <param name="m1">Transform row 1, column 0 value.</param>
    /// <param name="m2">Transform row 0, column 1 value.</param>
    /// <param name="m3">Transform column 1 scale.</param>
    /// <param name="m4">X translation.</param>
    /// <param name="m5">Y translation.</param>
    /// <param name="ms">Scale applied after the X transform.</param>
    /// <param name="ns">Scale applied after the Y transform.</param>
    private static void TransformVertex(ref Vertex v, float m0, float m1, float m2, float m3, float m4, float m5, float ms, float ns)
    {
        float x = v.X;
        float y = v.Y;
        v.X = (short)(ms * (m0 * x + m2 * y + m4));
        v.Y = (short)(ns * (m1 * x + m3 * y + m5));
        x = v.Cx;
        y = v.Cy;
        v.Cx = (short)(ms * (m0 * x + m2 * y + m4));
        v.Cy = (short)(ns * (m1 * x + m3 * y + m5));
        x = v.Cx1;
        y = v.Cy1;
        v.Cx1 = (short)(ms * (m0 * x + m2 * y + m4));
        v.Cy1 = (short)(ns * (m1 * x + m3 * y + m5));
    }

    #endregion

    #region CFF byte buffers and dictionary parsing

    /// <summary>
    /// Cursor over a byte range of a font image, mirroring <c>stbtt__buf</c>
    /// </summary>
    /// <remarks>
    /// The canonical structure is passed by pointer or by value; a reference type reproduces the
    /// by-pointer case through field mutation, while <see cref="Clone"/> reproduces the by-value
    /// case. Reads at or past the end of the range return zero, matching the canonical helpers, and
    /// the range is a window into the caller-owned font image so no bytes are copied.
    /// </remarks>
    private sealed class CffBuffer
    {
        /// <summary>The shared empty buffer, mirroring <c>stbtt__new_buf(NULL, 0)</c>.</summary>
        public static readonly CffBuffer Empty = new(Array.Empty<byte>(), 0, 0);

        /// <summary>Font image that owns the addressed bytes.</summary>
        public readonly byte[] Data;

        /// <summary>Absolute offset of the range start within <see cref="Data"/>.</summary>
        public readonly int Offset;

        /// <summary>Number of addressable bytes in the range.</summary>
        public readonly int Size;

        /// <summary>Current read position relative to <see cref="Offset"/>.</summary>
        public int Cursor;

        /// <summary>Creates a cursor over a range of a font image.</summary>
        /// <param name="data">Font image that owns the bytes.</param>
        /// <param name="offset">Absolute offset of the range start.</param>
        /// <param name="size">Number of addressable bytes.</param>
        public CffBuffer(byte[] data, int offset, int size)
        {
            Data = data;
            Offset = offset;
            Size = size;
        }

        /// <summary>
        /// Copies this cursor without copying the underlying bytes, matching the canonical by-value
        /// <c>stbtt__buf</c> parameter semantics.
        /// </summary>
        /// <returns>An independent cursor over the same range, positioned identically.</returns>
        public CffBuffer Clone() => new(Data, Offset, Size) { Cursor = Cursor };

        /// <summary>Reads one byte and advances. Ported from <c>stbtt__buf_get8</c>.</summary>
        /// <returns>The byte value, or zero at or past the end of the range.</returns>
        public byte Get8()
        {
            if (Cursor >= Size)
                return 0;
            return Data[Offset + Cursor++];
        }

        /// <summary>Reads one byte without advancing. Ported from <c>stbtt__buf_peek8</c>.</summary>
        /// <returns>The byte value, or zero at or past the end of the range.</returns>
        public byte Peek8()
        {
            if (Cursor >= Size)
                return 0;
            return Data[Offset + Cursor];
        }

        /// <summary>Moves the cursor to an absolute position. Ported from <c>stbtt__buf_seek</c>.</summary>
        /// <param name="o">Position relative to the range start; negative or oversized values clamp to the end.</param>
        public void Seek(int o) => Cursor = (o > Size || o < 0) ? Size : o;

        /// <summary>Moves the cursor by a signed delta. Ported from <c>stbtt__buf_skip</c>.</summary>
        /// <param name="o">Signed number of bytes to advance; negative values rewind.</param>
        public void Skip(int o) => Seek(Cursor + o);

        /// <summary>Reads a big-endian integer of one to four bytes. Ported from <c>stbtt__buf_get</c>.</summary>
        /// <param name="n">Number of bytes to read.</param>
        /// <returns>The assembled value.</returns>
        public uint Get(int n)
        {
            uint v = 0;
            for (int i = 0; i < n; i++)
                v = (v << 8) | Get8();
            return v;
        }

        /// <summary>
        /// Creates a sub-range of this buffer without moving the cursor. Ported from
        /// <c>stbtt__buf_range</c>.
        /// </summary>
        /// <param name="o">Sub-range start relative to this range start.</param>
        /// <param name="s">Sub-range length in bytes.</param>
        /// <returns>The sub-range, or the empty buffer when the request is out of bounds.</returns>
        public CffBuffer Range(int o, int s)
        {
            if (o < 0 || s < 0 || o > Size || s > Size - o)
                return Empty;
            return new CffBuffer(Data, Offset + o, s);
        }

        /// <summary>
        /// Reads a CFF INDEX and returns its complete byte range, advancing the cursor past it.
        /// Ported from <c>stbtt__cff_get_index</c>.
        /// </summary>
        /// <returns>The INDEX range, including its count and offset array.</returns>
        public CffBuffer GetIndex()
        {
            int start = Cursor;
            int count = (int)Get(2);
            if (count != 0)
            {
                int offSize = Get8();
                Skip(offSize * count);
                Skip((int)Get(offSize) - 1);
            }
            return Range(start, Cursor - start);
        }

        /// <summary>
        /// Reads one CFF operand, matching <c>stbtt__cff_int</c>.
        /// </summary>
        /// <returns>The operand value; a reserved leading byte yields zero rather than the canonical assertion.</returns>
        public int GetInt()
        {
            int b0 = Get8();
            if (b0 >= 32 && b0 <= 246)
                return b0 - 139;
            if (b0 >= 247 && b0 <= 250)
                return (b0 - 247) * 256 + Get8() + 108;
            if (b0 >= 251 && b0 <= 254)
                return -(b0 - 251) * 256 - Get8() - 108;
            if (b0 == 28)
                return (int)Get(2);
            if (b0 == 29)
                return (int)Get(4);
            return 0;
        }

        /// <summary>
        /// Skips one operand, including the packed real-number form. Ported from
        /// <c>stbtt__cff_skip_operand</c>.
        /// </summary>
        public void SkipOperand()
        {
            int b0 = Peek8();
            if (b0 == 30)
            {
                // The packed form ends at the first nibble equal to 0xF.
                Skip(1);
                while (Cursor < Size)
                {
                    int v = Get8();
                    if ((v & 0xF) == 0xF || (v >> 4) == 0xF)
                        break;
                }
            }
            else
            {
                GetInt();
            }
        }

        /// <summary>
        /// Returns the operand bytes of the dictionary entry with the given operator. Ported from
        /// <c>stbtt__dict_get</c>.
        /// </summary>
        /// <param name="key">Dictionary operator; two-byte operators arrive as <c>0x100 | op</c>.</param>
        /// <returns>The operand byte range, or the empty buffer when the key is absent.</returns>
        public CffBuffer DictGet(int key)
        {
            Seek(0);
            while (Cursor < Size)
            {
                int start = Cursor;
                while (Peek8() >= 28)
                    SkipOperand();
                int end = Cursor;
                int op = Get8();
                if (op == 12)
                    op = Get8() | 0x100;
                if (op == key)
                    return Range(start, end - start);
            }
            return Range(0, 0);
        }

        /// <summary>
        /// Reads up to <paramref name="outCount"/> operands of a dictionary entry. Ported from
        /// <c>stbtt__dict_get_ints</c>.
        /// </summary>
        /// <param name="key">Dictionary operator; two-byte operators arrive as <c>0x100 | op</c>.</param>
        /// <param name="outCount">Maximum operand count to read.</param>
        /// <param name="outValues">Destination array; entries beyond the operand count are left unchanged.</param>
        public void DictGetInts(int key, int outCount, uint[] outValues)
        {
            CffBuffer operands = DictGet(key);
            for (int i = 0; i < outCount && operands.Cursor < operands.Size; i++)
                outValues[i] = (uint)operands.GetInt();
        }

        /// <summary>
        /// Returns the item count of an INDEX. Ported from <c>stbtt__cff_index_count</c>
        /// </summary>
        /// <returns>The declared item count.</returns>
        public int IndexCount()
        {
            Seek(0);
            return (int)Get(2);
        }

        /// <summary>
        /// Returns one item of an INDEX without disturbing the source cursor, matching the canonical
        /// by-value buffer parameter. Ported from <c>stbtt__cff_index_get</c>
        /// </summary>
        /// <param name="source">The INDEX range.</param>
        /// <param name="i">Zero-based item index.</param>
        /// <returns>The item byte range, or the empty buffer when the index is out of range.</returns>
        public static CffBuffer IndexGet(CffBuffer source, int i)
        {
            CffBuffer b = source.Clone();
            b.Seek(0);
            int count = (int)b.Get(2);
            if (i < 0 || i >= count)
                return Empty; // Canonical asserts; the port reports absence instead.
            int offSize = b.Get8();
            b.Skip(i * offSize);
            int start = (int)b.Get(offSize);
            int end = (int)b.Get(offSize);
            return b.Range(2 + (count + 1) * offSize + start, end - start);
        }
    }

    /// <summary>
    /// Resolves the local subroutine INDEX of a CFF font dictionary. Ported from
    /// <c>stbtt__get_subrs</c>.
    /// </summary>
    /// <param name="cff">CFF table range that the dictionary offsets are relative to.</param>
    /// <param name="fontDict">Top dictionary of the font.</param>
    /// <returns>The local subroutines, or the empty buffer when the font declares none.</returns>
    private CffBuffer GetSubrs(CffBuffer cff, CffBuffer fontDict)
    {
        // Operator 18 carries the Private dictionary size followed by its offset.
        var privateLoc = new uint[2];
        fontDict.DictGetInts(18, 2, privateLoc);
        if (privateLoc[1] == 0 || privateLoc[0] == 0)
            return CffBuffer.Empty;

        CffBuffer privateDict = cff.Range((int)privateLoc[1], (int)privateLoc[0]);
        var subrsOffset = new uint[1];
        privateDict.DictGetInts(19, 1, subrsOffset);
        if (subrsOffset[0] == 0)
            return CffBuffer.Empty;

        // The canonical helper takes the CFF buffer by value, so the seek must not disturb callers.
        CffBuffer local = cff.Clone();
        local.Seek((int)privateLoc[1] + (int)subrsOffset[0]);
        return local.GetIndex();
    }

    /// <summary>
    /// Returns one subroutine of a subroutine INDEX, applying the count-dependent Type2 bias.
    /// Ported from <c>stbtt__get_subr</c>.
    /// </summary>
    /// <param name="index">Subroutine INDEX.</param>
    /// <param name="n">Subroutine number pushed by the charstring, before biasing.</param>
    /// <returns>The subroutine byte range, or the empty buffer when out of range.</returns>
    private static CffBuffer GetSubr(CffBuffer index, int n)
    {
        // The bias grows with the subroutine count: 107, then 1131, then 32768.
        CffBuffer idx = index.Clone();
        int count = idx.IndexCount();
        int bias = 107;
        if (count >= 33900)
            bias = 32768;
        else if (count >= 1240)
            bias = 1131;
        n += bias;
        if (n < 0 || n >= count)
            return CffBuffer.Empty;
        return CffBuffer.IndexGet(idx, n);
    }

    /// <summary>
    /// Selects the local subroutines of one glyph in a CID-keyed font. Ported from
    /// <c>stbtt__cid_get_glyph_subrs</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index whose FDSelect entry names the font dictionary.</param>
    /// <returns>The selected local subroutines, or the empty buffer when FDSelect has no entry.</returns>
    private CffBuffer CidGetGlyphSubrs(int glyphIndex)
    {
        CffBuffer fdselect = _fdselect.Clone();
        int fdSelector = -1;

        fdselect.Seek(0);
        int format = fdselect.Get8();
        if (format == 0)
        {
            // Format 0 stores one selector byte per glyph.
            fdselect.Skip(glyphIndex);
            fdSelector = fdselect.Get8();
        }
        else if (format == 3)
        {
            // Format 3 stores ranges of [first glyph, selector, end glyph).
            int rangeCount = (int)fdselect.Get(2);
            int start = (int)fdselect.Get(2);
            for (int i = 0; i < rangeCount; i++)
            {
                int selector = fdselect.Get8();
                int end = (int)fdselect.Get(2);
                if (glyphIndex >= start && glyphIndex < end)
                {
                    fdSelector = selector;
                    break;
                }
                start = end;
            }
        }

        if (fdSelector == -1)
            return CffBuffer.Empty;
        return GetSubrs(_cff, CffBuffer.IndexGet(_fontdicts, fdSelector));
    }

    #endregion

    #region CFF Type2 charstring interpreter

    /// <summary>
    /// Accumulator threaded through the Type2 charstring interpreter, mirroring <c>stbtt__csctx</c>
    /// </summary>
    /// <remarks>
    /// A charstring runs twice: once in bounds mode to count vertices and accumulate the glyph box,
    /// and once in output mode to fill an exactly sized vertex array from the same command stream.
    /// The pen position and the current contour start are tracked in font units.
    /// </remarks>
    private sealed class CharstringContext
    {
        /// <summary>When true, tracks only bounds and the vertex count and writes no vertices.</summary>
        public bool Bounds;

        /// <summary>True once a vertex has been tracked, so the first bounds update initializes all four edges.</summary>
        public bool Started;

        /// <summary>X coordinate of the current contour start, in font units.</summary>
        public float FirstX;

        /// <summary>Y coordinate of the current contour start, in font units.</summary>
        public float FirstY;

        /// <summary>Current pen X coordinate, in font units.</summary>
        public float X;

        /// <summary>Current pen Y coordinate, in font units.</summary>
        public float Y;

        /// <summary>Minimum tracked X, meaningful only once <see cref="Started"/> is true.</summary>
        public int MinX;

        /// <summary>Maximum tracked X, meaningful only once <see cref="Started"/> is true.</summary>
        public int MaxX;

        /// <summary>Minimum tracked Y, meaningful only once <see cref="Started"/> is true.</summary>
        public int MinY;

        /// <summary>Maximum tracked Y, meaningful only once <see cref="Started"/> is true.</summary>
        public int MaxY;

        /// <summary>Destination vertex array in output mode, or <see langword="null"/> in bounds mode.</summary>
        public Vertex[]? Vertices;

        /// <summary>Number of vertices produced so far.</summary>
        public int NumVertices;
    }

    /// <summary>
    /// Expands the Type2 charstring of one glyph into vertices or bounds. Ported from
    /// <c>stbtt__run_charstring</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index whose charstring is interpreted.</param>
    /// <param name="c">Accumulator receiving vertices in output mode or bounds in bounds mode.</param>
    /// <returns><see langword="true"/> when the charstring ended with <c>endchar</c>; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// The interpreter covers the Type2 operators used by OpenType CFF outlines. Stem and mask
    /// operators advance the operand cursor without hinting, matching canonical, and the optional
    /// initial width operand is ignored because horizontal metrics come from <c>hmtx</c>.
    /// </remarks>
    private bool RunCharstring(int glyphIndex, CharstringContext c)
    {
        int inHeader = 1;
        int maskBits = 0;
        int subrStackHeight = 0;
        int sp = 0;
        int hasSubrs = 0;
        int v = 0;
        float f = 0.0f;
        bool clearStack;
        var s = new float[48];
        var subrStack = new CffBuffer[10];
        CffBuffer subrs = _subrs;

        CffBuffer b = CffBuffer.IndexGet(_charstrings, glyphIndex);
        while (b.Cursor < b.Size)
        {
            int i = 0;
            clearStack = true;
            int b0 = b.Get8();
            switch (b0)
            {
                // Hint and counter masks are skipped: the port does not implement hinting.
                case 0x13: // hintmask
                case 0x14: // cntrmask
                    if (inHeader != 0)
                        maskBits += sp / 2; // Implicit vstem operands.
                    inHeader = 0;
                    b.Skip((maskBits + 7) / 8);
                    break;

                case 0x01: // hstem
                case 0x03: // vstem
                case 0x12: // hstemhm
                case 0x17: // vstemhm
                    maskBits += sp / 2;
                    break;

                case 0x15: // rmoveto
                    inHeader = 0;
                    if (sp < 2)
                        return false;
                    RMoveTo(c, s[sp - 2], s[sp - 1]);
                    break;
                case 0x04: // vmoveto
                    inHeader = 0;
                    if (sp < 1)
                        return false;
                    RMoveTo(c, 0, s[sp - 1]);
                    break;
                case 0x16: // hmoveto
                    inHeader = 0;
                    if (sp < 1)
                        return false;
                    RMoveTo(c, s[sp - 1], 0);
                    break;

                case 0x05: // rlineto
                    if (sp < 2)
                        return false;
                    for (; i + 1 < sp; i += 2)
                        RLineTo(c, s[i], s[i + 1]);
                    break;

                // hlineto and vlineto alternate horizontal and vertical segments, starting with the
                // axis named by the operator. The canonical code shares one alternating loop through
                // a goto; the port keeps one loop and flips the leading axis each iteration.
                case 0x06: // hlineto
                case 0x07: // vlineto
                    if (sp < 1)
                        return false;
                    bool horizontalLead = b0 == 0x06;
                    for (; i < sp; i++)
                    {
                        if (horizontalLead)
                            RLineTo(c, s[i], 0);
                        else
                            RLineTo(c, 0, s[i]);
                        horizontalLead = !horizontalLead;
                    }
                    break;

                // hvcurveto and vhcurveto likewise alternate the leading axis. A five-operand tail
                // supplies the closing delta of the other axis.
                case 0x1E: // vhcurveto
                case 0x1F: // hvcurveto
                    if (sp < 4)
                        return false;
                    bool curveHorizontalLead = b0 == 0x1F;
                    for (; i + 3 < sp; i += 4)
                    {
                        float tail = (sp - i == 5) ? s[i + 4] : 0.0f;
                        if (curveHorizontalLead)
                            RCCurveTo(c, s[i], 0, s[i + 1], s[i + 2], tail, s[i + 3]);
                        else
                            RCCurveTo(c, 0, s[i], s[i + 1], s[i + 2], s[i + 3], tail);
                        curveHorizontalLead = !curveHorizontalLead;
                    }
                    break;

                case 0x08: // rrcurveto
                    if (sp < 6)
                        return false;
                    for (; i + 5 < sp; i += 6)
                        RCCurveTo(c, s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                    break;

                case 0x18: // rcurveline
                    if (sp < 8)
                        return false;
                    for (; i + 5 < sp - 2; i += 6)
                        RCCurveTo(c, s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                    if (i + 1 >= sp)
                        return false;
                    RLineTo(c, s[i], s[i + 1]);
                    break;

                case 0x19: // rlinecurve
                    if (sp < 8)
                        return false;
                    for (; i + 1 < sp - 6; i += 2)
                        RLineTo(c, s[i], s[i + 1]);
                    if (i + 5 >= sp)
                        return false;
                    RCCurveTo(c, s[i], s[i + 1], s[i + 2], s[i + 3], s[i + 4], s[i + 5]);
                    break;

                case 0x1A: // vvcurveto
                case 0x1B: // hhcurveto
                    if (sp < 4)
                        return false;
                    f = 0.0f;
                    if ((sp & 1) != 0)
                    {
                        // An odd leading operand supplies the first curve's other-axis delta.
                        f = s[i];
                        i++;
                    }
                    for (; i + 3 < sp; i += 4)
                    {
                        if (b0 == 0x1B)
                            RCCurveTo(c, s[i], f, s[i + 1], s[i + 2], s[i + 3], 0.0f);
                        else
                            RCCurveTo(c, f, s[i], s[i + 1], s[i + 2], 0.0f, s[i + 3]);
                        f = 0.0f;
                    }
                    break;

                case 0x0A: // callsubr
                    if (hasSubrs == 0)
                    {
                        // CID-keyed fonts select local subroutines per glyph through FDSelect.
                        if (_fdselect.Size != 0)
                            subrs = CidGetGlyphSubrs(glyphIndex);
                        hasSubrs = 1;
                    }
                    goto case 0x1D;
                case 0x1D: // callgsubr
                    if (sp < 1)
                        return false;
                    v = (int)s[--sp];
                    if (subrStackHeight >= 10)
                        return false; // Recursion limit, matching canonical.
                    subrStack[subrStackHeight++] = b;
                    b = GetSubr(b0 == 0x0A ? subrs : _gsubrs, v);
                    if (b.Size == 0)
                        return false;
                    b.Cursor = 0;
                    clearStack = false;
                    break;

                case 0x0B: // return
                    if (subrStackHeight <= 0)
                        return false;
                    b = subrStack[--subrStackHeight];
                    clearStack = false;
                    break;

                case 0x0E: // endchar
                    CloseShape(c);
                    return true;

                case 0x0C:
                {
                    // Two-byte escape operators. The flex forms ignore flex depth and always draw beziers.
                    int b1 = b.Get8();
                    switch (b1)
                    {
                        case 0x22: // hflex
                        {
                            if (sp < 7)
                                return false;
                            float dx1 = s[0], dx2 = s[1], dy2 = s[2], dx3 = s[3];
                            float dx4 = s[4], dx5 = s[5], dx6 = s[6];
                            RCCurveTo(c, dx1, 0, dx2, dy2, dx3, 0);
                            RCCurveTo(c, dx4, 0, dx5, -dy2, dx6, 0);
                            break;
                        }

                        case 0x23: // flex
                        {
                            if (sp < 13)
                                return false;
                            float dx1 = s[0], dy1 = s[1], dx2 = s[2], dy2 = s[3], dx3 = s[4], dy3 = s[5];
                            float dx4 = s[6], dy4 = s[7], dx5 = s[8], dy5 = s[9], dx6 = s[10], dy6 = s[11];
                            RCCurveTo(c, dx1, dy1, dx2, dy2, dx3, dy3);
                            RCCurveTo(c, dx4, dy4, dx5, dy5, dx6, dy6);
                            break;
                        }

                        case 0x24: // hflex1
                        {
                            if (sp < 9)
                                return false;
                            float dx1 = s[0], dy1 = s[1], dx2 = s[2], dy2 = s[3], dx3 = s[4];
                            float dx4 = s[5], dx5 = s[6], dy5 = s[7], dx6 = s[8];
                            RCCurveTo(c, dx1, dy1, dx2, dy2, dx3, 0);
                            RCCurveTo(c, dx4, 0, dx5, dy5, dx6, -(dy1 + dy2 + dy5));
                            break;
                        }

                        case 0x25: // flex1
                        {
                            if (sp < 11)
                                return false;
                            float dx1 = s[0], dy1 = s[1], dx2 = s[2], dy2 = s[3], dx3 = s[4], dy3 = s[5];
                            float dx4 = s[6], dy4 = s[7], dx5 = s[8], dy5 = s[9];
                            float dx6 = s[10], dy6 = s[10];
                            float dx = dx1 + dx2 + dx3 + dx4 + dx5;
                            float dy = dy1 + dy2 + dy3 + dy4 + dy5;
                            if (MathF.Abs(dx) > MathF.Abs(dy))
                                dy6 = -dy;
                            else
                                dx6 = -dx;
                            RCCurveTo(c, dx1, dy1, dx2, dy2, dx3, dy3);
                            RCCurveTo(c, dx4, dy4, dx5, dy5, dx6, dy6);
                            break;
                        }

                        default:
                            return false; // Unimplemented escape operator.
                    }
                    break;
                }

                default:
                    if (b0 != 255 && b0 != 28 && b0 < 32)
                        return false; // Reserved operator.

                    // Push an immediate operand: 255 is a 16.16 fixed-point number, everything else
                    // is an encoded integer that the interpreter reads from the byte before the op.
                    if (b0 == 255)
                    {
                        f = (float)(int)b.Get(4) / 0x10000;
                    }
                    else
                    {
                        b.Skip(-1);
                        f = (short)b.GetInt();
                    }
                    if (sp >= 48)
                        return false; // Operand stack overflow.
                    s[sp++] = f;
                    clearStack = false;
                    break;
            }

            if (clearStack)
                sp = 0;
        }

        return false; // No endchar before the charstring ran out.
    }

    /// <summary>
    /// Records a point so the glyph box includes it. Ported from <c>stbtt__track_vertex</c>
    /// </summary>
    /// <param name="c">Accumulator whose bounds are updated.</param>
    /// <param name="x">Point X in font units.</param>
    /// <param name="y">Point Y in font units.</param>
    private static void TrackVertex(CharstringContext c, int x, int y)
    {
        if (x > c.MaxX || !c.Started)
            c.MaxX = x;
        if (y > c.MaxY || !c.Started)
            c.MaxY = y;
        if (x < c.MinX || !c.Started)
            c.MinX = x;
        if (y < c.MinY || !c.Started)
            c.MinY = y;
        c.Started = true;
    }

    /// <summary>
    /// Emits one outline vertex, either tracking its bounds or writing it to the output array.
    /// Ported from <c>stbtt__csctx_v</c> and <c>stbtt_setvertex</c>.
    /// </summary>
    /// <param name="c">Accumulator receiving the vertex.</param>
    /// <param name="type">Vertex kind: move, line, quadratic, or cubic.</param>
    /// <param name="x">On-curve end point X in font units.</param>
    /// <param name="y">On-curve end point Y in font units.</param>
    /// <param name="cx">First control point X in font units.</param>
    /// <param name="cy">First control point Y in font units.</param>
    /// <param name="cx1">Second control point X in font units.</param>
    /// <param name="cy1">Second control point Y in font units.</param>
    private static void ContextV(CharstringContext c, byte type, int x, int y, int cx, int cy, int cx1, int cy1)
    {
        if (c.Bounds)
        {
            TrackVertex(c, x, y);
            if (type == VertexCubic)
            {
                TrackVertex(c, cx, cy);
                TrackVertex(c, cx1, cy1);
            }
        }
        else
        {
            c.Vertices![c.NumVertices] = new Vertex
            {
                Type = type,
                X = (short)x,
                Y = (short)y,
                Cx = (short)cx,
                Cy = (short)cy,
                Cx1 = (short)cx1,
                Cy1 = (short)cy1,
            };
        }
        c.NumVertices++;
    }

    /// <summary>
    /// Closes the current contour with a line back to its start when the pen moved away from it.
    /// Ported from <c>stbtt__csctx_close_shape</c>.
    /// </summary>
    /// <param name="ctx">Accumulator whose contour is closed.</param>
    private static void CloseShape(CharstringContext ctx)
    {
        if (ctx.FirstX != ctx.X || ctx.FirstY != ctx.Y)
            ContextV(ctx, VertexLine, (int)ctx.FirstX, (int)ctx.FirstY, 0, 0, 0, 0);
    }

    /// <summary>
    /// Starts a new contour at a relative offset. Ported from <c>stbtt__csctx_rmove_to</c>
    /// </summary>
    /// <param name="ctx">Accumulator receiving the move vertex.</param>
    /// <param name="dx">Relative X offset in font units.</param>
    /// <param name="dy">Relative Y offset in font units.</param>
    private static void RMoveTo(CharstringContext ctx, float dx, float dy)
    {
        CloseShape(ctx);
        ctx.FirstX = ctx.X = ctx.X + dx;
        ctx.FirstY = ctx.Y = ctx.Y + dy;
        ContextV(ctx, VertexMove, (int)ctx.X, (int)ctx.Y, 0, 0, 0, 0);
    }

    /// <summary>
    /// Adds a relative line segment. Ported from <c>stbtt__csctx_rline_to</c>
    /// </summary>
    /// <param name="ctx">Accumulator receiving the line vertex.</param>
    /// <param name="dx">Relative X offset in font units.</param>
    /// <param name="dy">Relative Y offset in font units.</param>
    private static void RLineTo(CharstringContext ctx, float dx, float dy)
    {
        ctx.X += dx;
        ctx.Y += dy;
        ContextV(ctx, VertexLine, (int)ctx.X, (int)ctx.Y, 0, 0, 0, 0);
    }

    /// <summary>
    /// Adds a relative cubic bezier segment. Ported from <c>stbtt__csctx_rccurve_to</c>
    /// </summary>
    /// <param name="ctx">Accumulator receiving the cubic vertex.</param>
    /// <param name="dx1">First control point relative X offset.</param>
    /// <param name="dy1">First control point relative Y offset.</param>
    /// <param name="dx2">Second control point relative X offset.</param>
    /// <param name="dy2">Second control point relative Y offset.</param>
    /// <param name="dx3">End point relative X offset.</param>
    /// <param name="dy3">End point relative Y offset.</param>
    private static void RCCurveTo(CharstringContext ctx, float dx1, float dy1, float dx2, float dy2, float dx3, float dy3)
    {
        float cx1 = ctx.X + dx1;
        float cy1 = ctx.Y + dy1;
        float cx2 = cx1 + dx2;
        float cy2 = cy1 + dy2;
        ctx.X = cx2 + dx3;
        ctx.Y = cy2 + dy3;
        ContextV(ctx, VertexCubic, (int)ctx.X, (int)ctx.Y, (int)cx1, (int)cy1, (int)cx2, (int)cy2);
    }

    /// <summary>
    /// Runs a charstring twice to produce an exactly sized vertex array. Ported from
    /// <c>stbtt__GetGlyphShapeT2</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index to decode.</param>
    /// <returns>The outline vertices, or an empty array when the charstring did not terminate.</returns>
    private Vertex[] GetGlyphShapeT2(int glyphIndex)
    {
        var countContext = new CharstringContext { Bounds = true };
        var outputContext = new CharstringContext();
        if (RunCharstring(glyphIndex, countContext))
        {
            outputContext.Vertices = new Vertex[countContext.NumVertices];
            if (RunCharstring(glyphIndex, outputContext))
                return outputContext.Vertices;
        }
        return Array.Empty<Vertex>();
    }

    /// <summary>
    /// Computes a glyph's outline bounds and vertex count without materializing the vertices.
    /// Ported from <c>stbtt__GetGlyphInfoT2</c>.
    /// </summary>
    /// <param name="glyphIndex">Glyph index to measure.</param>
    /// <param name="x0">Receives the minimum X in font units.</param>
    /// <param name="y0">Receives the minimum Y in font units.</param>
    /// <param name="x1">Receives the maximum X in font units.</param>
    /// <param name="y1">Receives the maximum Y in font units.</param>
    /// <returns>The vertex count, or zero when the charstring did not terminate.</returns>
    private int GetGlyphInfoT2(int glyphIndex, out int x0, out int y0, out int x1, out int y1)
    {
        var c = new CharstringContext { Bounds = true };
        bool r = RunCharstring(glyphIndex, c);
        x0 = r ? c.MinX : 0;
        y0 = r ? c.MinY : 0;
        x1 = r ? c.MaxX : 0;
        y1 = r ? c.MaxY : 0;
        return r ? c.NumVertices : 0;
    }

    #endregion

    #region Curve flattening

    /// <summary>
    /// Flattens outline vertices into one closed point list per contour. Ported from
    /// <c>stbtt_FlattenCurves</c>.
    /// </summary>
    /// <param name="vertices">Outline vertices.</param>
    /// <param name="flatness">Maximum pixel deviation allowed when approximating curves.</param>
    /// <param name="contourLengths">Receives the point count of each contour.</param>
    /// <param name="contourCount">Receives the number of closed contours found.</param>
    /// <returns>The flattened points, in contour order.</returns>
    private static Point[] FlattenCurves(Vertex[] vertices, float flatness, out int[] contourLengths, out int contourCount)
    {
        contourLengths = Array.Empty<int>();
        contourCount = 0;
        if (vertices.Length == 0)
            return Array.Empty<Point>();

        float flatnessSquared = flatness * flatness;

        int moves = 0;
        for (int i = 0; i < vertices.Length; i++)
            if (vertices[i].Type == VertexMove)
                ++moves;

        if (moves == 0)
            return Array.Empty<Point>();

        contourLengths = new int[moves];
        var points = new List<Point>(vertices.Length * 2);

        int n = -1;
        int start = 0;
        float x = 0;
        float y = 0;

        for (int i = 0; i < vertices.Length; i++)
        {
            switch (vertices[i].Type)
            {
                case VertexMove:
                    if (n >= 0)
                        contourLengths[n] = points.Count - start;
                    ++n;
                    start = points.Count;
                    x = vertices[i].X;
                    y = vertices[i].Y;
                    points.Add(new Point { X = x, Y = y });
                    break;
                case VertexLine:
                    x = vertices[i].X;
                    y = vertices[i].Y;
                    points.Add(new Point { X = x, Y = y });
                    break;
                case VertexQuadratic:
                    TesselateCurve(points, x, y, vertices[i].Cx, vertices[i].Cy, vertices[i].X, vertices[i].Y, flatnessSquared, 0);
                    x = vertices[i].X;
                    y = vertices[i].Y;
                    break;
                case VertexCubic:
                    TesselateCubic(points, x, y, vertices[i].Cx, vertices[i].Cy, vertices[i].Cx1, vertices[i].Cy1, vertices[i].X, vertices[i].Y, flatnessSquared, 0);
                    x = vertices[i].X;
                    y = vertices[i].Y;
                    break;
            }
        }

        if (n >= 0)
            contourLengths[n] = points.Count - start;

        contourCount = n + 1;
        return points.ToArray();
    }

    /// <summary>
    /// Recursively subdivides a quadratic bezier until it is flat enough. Ported from
    /// <c>stbtt__tesselate_curve</c>.
    /// </summary>
    /// <param name="points">Destination point list.</param>
    /// <param name="x0">Start point X.</param>
    /// <param name="y0">Start point Y.</param>
    /// <param name="x1">Control point X.</param>
    /// <param name="y1">Control point Y.</param>
    /// <param name="x2">End point X.</param>
    /// <param name="y2">End point Y.</param>
    /// <param name="flatnessSquared">Squared flatness threshold.</param>
    /// <param name="n">Current recursion depth.</param>
    private static void TesselateCurve(List<Point> points, float x0, float y0, float x1, float y1,
        float x2, float y2, float flatnessSquared, int n)
    {
        float mx = (x0 + 2 * x1 + x2) / 4;
        float my = (y0 + 2 * y1 + y2) / 4;
        float dx = (x0 + x2) / 2 - mx;
        float dy = (y0 + y2) / 2 - my;

        if (n > 16)
            return;
        if (dx * dx + dy * dy > flatnessSquared)
        {
            TesselateCurve(points, x0, y0, (x0 + x1) / 2, (y0 + y1) / 2, mx, my, flatnessSquared, n + 1);
            TesselateCurve(points, mx, my, (x1 + x2) / 2, (y1 + y2) / 2, x2, y2, flatnessSquared, n + 1);
        }
        else
        {
            points.Add(new Point { X = x2, Y = y2 });
        }
    }

    /// <summary>
    /// Recursively subdivides a cubic bezier until it is flat enough. Ported from
    /// <c>stbtt__tesselate_cubic</c>.
    /// </summary>
    /// <param name="points">Destination point list.</param>
    /// <param name="x0">Start point X.</param>
    /// <param name="y0">Start point Y.</param>
    /// <param name="x1">First control point X.</param>
    /// <param name="y1">First control point Y.</param>
    /// <param name="x2">Second control point X.</param>
    /// <param name="y2">Second control point Y.</param>
    /// <param name="x3">End point X.</param>
    /// <param name="y3">End point Y.</param>
    /// <param name="flatnessSquared">Squared flatness threshold.</param>
    /// <param name="n">Current recursion depth.</param>
    private static void TesselateCubic(List<Point> points, float x0, float y0, float x1, float y1,
        float x2, float y2, float x3, float y3, float flatnessSquared, int n)
    {
        float dx0 = x1 - x0, dy0 = y1 - y0;
        float dx1 = x2 - x1, dy1 = y2 - y1;
        float dx2 = x3 - x2, dy2 = y3 - y2;
        float dx = x3 - x0, dy = y3 - y0;
        float longLen = MathF.Sqrt(dx0 * dx0 + dy0 * dy0) + MathF.Sqrt(dx1 * dx1 + dy1 * dy1) + MathF.Sqrt(dx2 * dx2 + dy2 * dy2);
        float shortLen = MathF.Sqrt(dx * dx + dy * dy);
        float flatnessSq = longLen * longLen - shortLen * shortLen;

        if (n > 16)
            return;
        if (flatnessSq > flatnessSquared)
        {
            float x01 = (x0 + x1) / 2, y01 = (y0 + y1) / 2;
            float x12 = (x1 + x2) / 2, y12 = (y1 + y2) / 2;
            float x23 = (x2 + x3) / 2, y23 = (y2 + y3) / 2;
            float xa = (x01 + x12) / 2, ya = (y01 + y12) / 2;
            float xb = (x12 + x23) / 2, yb = (y12 + y23) / 2;
            float mx = (xa + xb) / 2, my = (ya + yb) / 2;
            TesselateCubic(points, x0, y0, x01, y01, xa, ya, mx, my, flatnessSquared, n + 1);
            TesselateCubic(points, mx, my, xb, yb, x23, y23, x3, y3, flatnessSquared, n + 1);
        }
        else
        {
            points.Add(new Point { X = x3, Y = y3 });
        }
    }

    #endregion

    #region Scanline rasterizer

    /// <summary>
    /// Rasterizes a glyph's outline into an 8-bit coverage bitmap. Ported from
    /// <c>stbtt_MakeGlyphBitmapSubpixel</c>. and
    /// <c>stbtt_Rasterize</c>.
    /// </summary>
    /// <param name="output">Destination pixel buffer, one byte per pixel.</param>
    /// <param name="outW">Bitmap width in pixels.</param>
    /// <param name="outH">Bitmap height in pixels.</param>
    /// <param name="outStride">Distance in bytes between consecutive rows.</param>
    /// <param name="scaleX">Horizontal font-unit-to-pixel scale.</param>
    /// <param name="scaleY">Vertical font-unit-to-pixel scale.</param>
    /// <param name="shiftX">Subpixel horizontal shift in pixels.</param>
    /// <param name="shiftY">Subpixel vertical shift in pixels.</param>
    /// <param name="glyph">Glyph index to rasterize.</param>
    /// <remarks>
    /// Every destination pixel is written by the scanline loop, so <paramref name="output"/> need
    /// not be cleared by the caller (matching the canonical reader).
    /// </remarks>
    public void MakeGlyphBitmapSubpixel(byte[] output, int outW, int outH, int outStride,
        float scaleX, float scaleY, float shiftX, float shiftY, int glyph)
    {
        if (outW <= 0 || outH <= 0 || output.Length == 0)
            return;

        Vertex[] vertices = GetGlyphShape(glyph);
        GetGlyphBitmapBoxSubpixel(glyph, scaleX, scaleY, shiftX, shiftY, out int ix0, out int iy0, out _, out _);

        var gbm = new Bitmap { Pixels = output, W = outW, H = outH, Stride = outStride };
        Rasterize(gbm, 0.35f, vertices, scaleX, scaleY, shiftX, shiftY, ix0, iy0, true);
    }

    /// <summary>
    /// Rasterizes with a box prefilter over supersampled coverage. Ported from
    /// <c>stbtt_MakeGlyphBitmapSubpixelPrefilter</c>.
    /// </summary>
    /// <param name="output">Destination pixel buffer, one byte per pixel.</param>
    /// <param name="outW">Bitmap width in pixels, including oversampling.</param>
    /// <param name="outH">Bitmap height in pixels, including oversampling.</param>
    /// <param name="outStride">Distance in bytes between consecutive rows.</param>
    /// <param name="scaleX">Horizontal font-unit-to-pixel scale, including oversampling.</param>
    /// <param name="scaleY">Vertical font-unit-to-pixel scale, including oversampling.</param>
    /// <param name="shiftX">Subpixel horizontal shift in pixels.</param>
    /// <param name="shiftY">Subpixel vertical shift in pixels.</param>
    /// <param name="oversampleX">Horizontal oversampling factor.</param>
    /// <param name="oversampleY">Vertical oversampling factor.</param>
    /// <param name="subX">Receives the horizontal phase correction in pixels.</param>
    /// <param name="subY">Receives the vertical phase correction in pixels.</param>
    /// <param name="glyph">Glyph index to rasterize.</param>
    public void MakeGlyphBitmapSubpixelPrefilter(byte[] output, int outW, int outH, int outStride,
        float scaleX, float scaleY, float shiftX, float shiftY, int oversampleX, int oversampleY,
        out float subX, out float subY, int glyph)
    {
        subX = OversampleShift(oversampleX);
        subY = OversampleShift(oversampleY);

        MakeGlyphBitmapSubpixel(output, outW - (oversampleX - 1), outH - (oversampleY - 1),
            outStride, scaleX, scaleY, shiftX, shiftY, glyph);

        if (oversampleX > 1)
            HorizontalPrefilter(output, outW, outH, outStride, (uint)oversampleX);
        if (oversampleY > 1)
            VerticalPrefilter(output, outW, outH, outStride, (uint)oversampleY);
    }

    /// <summary>
    /// Returns the pixel phase correction introduced by box-prefiltering oversampled coverage.
    /// Ported from <c>stbtt__oversample_shift</c>.
    /// </summary>
    /// <param name="oversample">Oversampling factor.</param>
    /// <returns>The opposite-direction phase shift in pixels.</returns>
    private static float OversampleShift(int oversample)
    {
        if (oversample == 0)
            return 0.0f;
        return -(oversample - 1) / (2.0f * oversample);
    }

    /// <summary>
    /// Builds, sorts, and scan-converts edges for one glyph outline. Ported from
    /// <c>stbtt__rasterize</c>.
    /// </summary>
    /// <param name="result">Destination bitmap.</param>
    /// <param name="flatness">Pixel flatness threshold passed to curve flattening.</param>
    /// <param name="vertices">Outline vertices.</param>
    /// <param name="scaleX">Horizontal scale.</param>
    /// <param name="scaleY">Vertical scale.</param>
    /// <param name="shiftX">Horizontal shift.</param>
    /// <param name="shiftY">Vertical shift.</param>
    /// <param name="offX">Destination X origin in pixels.</param>
    /// <param name="offY">Destination Y origin in pixels.</param>
    /// <param name="invert">When true, outline Y is flipped so the glyph renders upright in bitmap space.</param>
    private static void Rasterize(Bitmap result, float flatness, Vertex[] vertices,
        float scaleX, float scaleY, float shiftX, float shiftY, int offX, int offY, bool invert)
    {
        float scale = scaleX > scaleY ? scaleY : scaleX;
        if (scale == 0)
            return;

        Point[] windings = FlattenCurves(vertices, flatness / scale, out int[] windingLengths, out int windingCount);
        if (windings.Length == 0 || windingCount == 0)
            return;

        RasterizeEdges(result, windings, windingLengths, windingCount, scaleX, scaleY, shiftX, shiftY, offX, offY, invert);
    }

    /// <summary>
    /// Expands windings into sorted edges and runs the scanline filler. Ported from
    /// <c>stbtt__rasterize</c> body.
    /// </summary>
    /// <param name="result">Destination bitmap.</param>
    /// <param name="points">Flattened contour points.</param>
    /// <param name="windingLengths">Point count per contour.</param>
    /// <param name="windings">Number of contours.</param>
    /// <param name="scaleX">Horizontal scale.</param>
    /// <param name="scaleY">Vertical scale.</param>
    /// <param name="shiftX">Horizontal shift.</param>
    /// <param name="shiftY">Vertical shift.</param>
    /// <param name="offX">Destination X origin in pixels.</param>
    /// <param name="offY">Destination Y origin in pixels.</param>
    /// <param name="invert">When true, outline Y is flipped.</param>
    private static void RasterizeEdges(Bitmap result, Point[] points, int[] windingLengths, int windings,
        float scaleX, float scaleY, float shiftX, float shiftY, int offX, int offY, bool invert)
    {
        float yScaleInv = invert ? -scaleY : scaleY;

        int totalEdges = 0;
        for (int i = 0; i < windings; i++)
            totalEdges += windingLengths[i];

        var edges = new Edge[totalEdges + 1];
        int n = 0;
        int m = 0;
        for (int i = 0; i < windings; i++)
        {
            int start = m;
            m += windingLengths[i];
            int j = windingLengths[i] - 1;
            for (int k = 0; k < windingLengths[i]; j = k++)
            {
                int a = k;
                int b = j;
                if (points[start + j].Y == points[start + k].Y)
                    continue;

                edges[n].Invert = false;
                bool downward = invert ? points[start + j].Y > points[start + k].Y : points[start + j].Y < points[start + k].Y;
                if (downward)
                {
                    edges[n].Invert = true;
                    a = j;
                    b = k;
                }
                edges[n].X0 = points[start + a].X * scaleX + shiftX;
                edges[n].Y0 = (points[start + a].Y * yScaleInv + shiftY);
                edges[n].X1 = points[start + b].X * scaleX + shiftX;
                edges[n].Y1 = (points[start + b].Y * yScaleInv + shiftY);
                ++n;
            }
        }

        SortEdges(edges, 0, n);
        RasterizeSortedEdges(result, edges, n, offX, offY);
    }

    /// <summary>
    /// Sorts edges by their top Y coordinate using the canonical quicksort plus insertion sort.
    /// Ported from <c>stbtt__sort_edges_quicksort</c> and <c>stbtt__sort_edges_ins_sort</c>
    /// </summary>
    /// <param name="p">Edge array.</param>
    /// <param name="start">First edge index in the range.</param>
    /// <param name="n">Number of edges in the range.</param>
    private static void SortEdges(Edge[] p, int start, int n)
    {
        SortEdgesQuick(p, start, n);
        SortEdgesInsertion(p, start, n);
    }

    /// <summary>
    /// Returns whether edge <paramref name="a"/> sorts before edge <paramref name="b"/> by top Y.
    /// Ports the <c>STBTT__COMPARE</c> macro.
    /// </summary>
    /// <param name="a">Left edge.</param>
    /// <param name="b">Right edge.</param>
    /// <returns><see langword="true"/> when <paramref name="a"/> has the smaller top Y.</returns>
    private static bool CompareEdges(in Edge a, in Edge b) => a.Y0 < b.Y0;

    /// <summary>
    /// Ports <c>stbtt__sort_edges_quicksort</c>. with explicit
    /// range offsets in place of pointer arithmetic.
    /// </summary>
    /// <param name="p">Edge array.</param>
    /// <param name="start">First edge index in the range.</param>
    /// <param name="n">Number of edges in the range.</param>
    private static void SortEdgesQuick(Edge[] p, int start, int n)
    {
        while (n > 12)
        {
            int m = n >> 1;
            bool c01 = CompareEdges(p[start], p[start + m]);
            bool c12 = CompareEdges(p[start + m], p[start + n - 1]);
            if (c01 != c12)
            {
                bool c = CompareEdges(p[start], p[start + n - 1]);
                int z = c == c12 ? 0 : n - 1;
                (p[start + z], p[start + m]) = (p[start + m], p[start + z]);
            }
            (p[start], p[start + m]) = (p[start + m], p[start]);

            int i = 1;
            int j = n - 1;
            for (; ; )
            {
                for (; ; ++i)
                    if (!CompareEdges(p[start + i], p[start]))
                        break;
                for (; ; --j)
                    if (!CompareEdges(p[start], p[start + j]))
                        break;
                if (i >= j)
                    break;
                (p[start + i], p[start + j]) = (p[start + j], p[start + i]);
                ++i;
                --j;
            }

            if (j < n - i)
            {
                SortEdgesQuick(p, start, j);
                start += i;
                n -= i;
            }
            else
            {
                SortEdgesQuick(p, start + i, n - i);
                n = j;
            }
        }
    }

    /// <summary>
    /// Ports <c>stbtt__sort_edges_ins_sort</c>.
    /// </summary>
    /// <param name="p">Edge array.</param>
    /// <param name="start">First edge index in the range.</param>
    /// <param name="n">Number of edges in the range.</param>
    private static void SortEdgesInsertion(Edge[] p, int start, int n)
    {
        for (int i = 1; i < n; ++i)
        {
            Edge t = p[start + i];
            int j = i;
            while (j > 0)
            {
                if (!CompareEdges(t, p[start + j - 1]))
                    break;
                p[start + j] = p[start + j - 1];
                --j;
            }
            if (i != j)
                p[start + j] = t;
        }
    }

    /// <summary>
    /// Walks sorted edges scanline by scanline, accumulating coverage. Ported from
    /// <c>stbtt__rasterize_sorted_edges</c> rasterizer-version-2 path
    /// </summary>
    /// <param name="result">Destination bitmap.</param>
    /// <param name="e">Sorted edge array with one trailing sentinel slot.</param>
    /// <param name="n">Number of real edges (the sentinel is at index <paramref name="n"/>).</param>
    /// <param name="offX">Destination X origin in pixels.</param>
    /// <param name="offY">Destination Y origin in pixels.</param>
    private static void RasterizeSortedEdges(Bitmap result, Edge[] e, int n, int offX, int offY)
    {
        ActiveEdge? active = null;
        float[] scanline = new float[result.W];
        float[] fill = new float[result.W + 1];

        int y = offY;
        int j = 0;
        int edgeIndex = 0;
        e[n].Y0 = offY + result.H + 1; // Sentinel stops edge insertion at the last scanline.

        while (j < result.H)
        {
            float scanYTop = y;
            float scanYBottom = y + 1.0f;

            Array.Clear(scanline, 0, result.W);
            Array.Clear(fill, 0, result.W + 1);

            // Retire edges that ended before this scanline.
            ActiveEdge? prev = null;
            ActiveEdge? cur = active;
            while (cur != null)
            {
                if (cur.Ey <= scanYTop)
                {
                    if (prev == null)
                        active = cur.Next;
                    else
                        prev.Next = cur.Next;
                    cur = cur.Next;
                }
                else
                {
                    prev = cur;
                    cur = cur.Next;
                }
            }

            // Activate edges that begin before the bottom of this scanline.
            while (e[edgeIndex].Y0 <= scanYBottom)
            {
                if (e[edgeIndex].Y0 != e[edgeIndex].Y1)
                {
                    ActiveEdge z = NewActive(e[edgeIndex], offX, scanYTop);
                    if (j == 0 && offY != 0 && z.Ey < scanYTop)
                        z.Ey = scanYTop; // Subpixel rounding can clip a hair past the first scanline.
                    z.Next = active;
                    active = z;
                }
                ++edgeIndex;
            }

            if (active != null)
                FillActiveEdgesNew(scanline, fill, result.W, active, scanYTop);

            float sum = 0.0f;
            for (int i = 0; i < result.W; i++)
            {
                sum += fill[i];
                float k = scanline[i] + sum;
                k = MathF.Abs(k) * 255.0f + 0.5f;
                int m = (int)k;
                if (m > 255)
                    m = 255;
                result.Pixels[j * result.Stride + i] = (byte)m;
            }

            for (ActiveEdge? z = active; z != null; z = z.Next)
                z.Fx += z.Fdx;

            ++y;
            ++j;
        }
    }

    /// <summary>
    /// Creates an active edge from a sorted edge at a scanline start. Ported from
    /// <c>stbtt__new_active</c> version 2.
    /// </summary>
    /// <param name="e">Sorted edge to activate.</param>
    /// <param name="offX">Destination X origin in pixels.</param>
    /// <param name="startPoint">Scanline top Y where the edge becomes active.</param>
    /// <returns>The new active edge.</returns>
    private static ActiveEdge NewActive(in Edge e, int offX, float startPoint)
    {
        float dxdy = (e.X1 - e.X0) / (e.Y1 - e.Y0);
        var z = new ActiveEdge
        {
            Fdx = dxdy,
            Fdy = dxdy != 0.0f ? 1.0f / dxdy : 0.0f,
            Fx = e.X0 + dxdy * (startPoint - e.Y0) - offX,
            Direction = e.Invert ? 1.0f : -1.0f,
            Sy = e.Y0,
            Ey = e.Y1
        };
        return z;
    }

    /// <summary>
    /// Accumulates coverage from all active edges for one scanline. Ported from
    /// <c>stbtt__fill_active_edges_new</c>.
    /// </summary>
    /// <param name="scanline">Per-pixel trapezoid coverage for this scanline.</param>
    /// <param name="fill">
    /// Per-pixel fill deltas. This corresponds to canonical <c>scanline_fill - 1</c>; index
    /// <c>x</c> here is canonical <c>scanline_fill[x - 1]</c>.
    /// </param>
    /// <param name="len">Bitmap width in pixels.</param>
    /// <param name="e">Head of the active edge list.</param>
    /// <param name="yTop">Top Y of the current scanline.</param>
    private static void FillActiveEdgesNew(float[] scanline, float[] fill, int len, ActiveEdge? e, float yTop)
    {
        float yBottom = yTop + 1.0f;

        while (e != null)
        {
            if (e.Fdx == 0)
            {
                // Vertical edge.
                float x0 = e.Fx;
                if (x0 < len)
                {
                    if (x0 >= 0)
                    {
                        HandleClippedEdge(scanline, (int)x0, e, x0, yTop, x0, yBottom);
                        HandleClippedEdge(fill, (int)x0 + 1, e, x0, yTop, x0, yBottom);
                    }
                    else
                    {
                        HandleClippedEdge(fill, 0, e, x0, yTop, x0, yBottom);
                    }
                }
            }
            else
            {
                float x0 = e.Fx;
                float dx = e.Fdx;
                float xb = x0 + dx;
                float dy = e.Fdy;
                float xTop;
                float xBottom;
                float sy0;
                float sy1;

                if (e.Sy > yTop)
                {
                    xTop = x0 + dx * (e.Sy - yTop);
                    sy0 = e.Sy;
                }
                else
                {
                    xTop = x0;
                    sy0 = yTop;
                }
                if (e.Ey < yBottom)
                {
                    xBottom = x0 + dx * (e.Ey - yTop);
                    sy1 = e.Ey;
                }
                else
                {
                    xBottom = xb;
                    sy1 = yBottom;
                }

                if (xTop >= 0 && xBottom >= 0 && xTop < len && xBottom < len)
                {
                    if ((int)xTop == (int)xBottom)
                    {
                        int x = (int)xTop;
                        float height = (sy1 - sy0) * e.Direction;
                        scanline[x] += PositionTrapezoidArea(height, xTop, x + 1.0f, xBottom, x + 1.0f);
                        fill[x + 1] += height;
                    }
                    else
                    {
                        if (xTop > xBottom)
                        {
                            // Flip vertically; the signed area is unchanged.
                            sy0 = yBottom - (sy0 - yTop);
                            sy1 = yBottom - (sy1 - yTop);
                            (sy0, sy1) = (sy1, sy0);
                            (xBottom, xTop) = (xTop, xBottom);
                            dx = -dx;
                            dy = -dy;
                            (x0, xb) = (xb, x0);
                        }

                        int x1 = (int)xTop;
                        int x2 = (int)xBottom;
                        float yCrossing = yTop + dy * (x1 + 1 - x0);
                        float yFinal = yTop + dy * (x2 - x0);

                        if (yCrossing > yBottom)
                            yCrossing = yBottom;

                        float sign = e.Direction;
                        float area = sign * (yCrossing - sy0);

                        scanline[x1] += SizedTriangleArea(area, x1 + 1 - xTop);

                        if (yFinal > yBottom)
                        {
                            int denom = x2 - (x1 + 1);
                            yFinal = yBottom;
                            if (denom != 0)
                                dy = (yFinal - yCrossing) / denom; // Guard the division in canonical issue #1316.
                        }

                        float step = sign * dy;
                        for (int x = x1 + 1; x < x2; ++x)
                        {
                            scanline[x] += area + step / 2;
                            area += step;
                        }
                        scanline[x2] += area + sign * PositionTrapezoidArea(sy1 - yFinal, x2, x2 + 1.0f, xBottom, x2 + 1.0f);
                        fill[x2 + 1] += sign * (sy1 - sy0);
                    }
                }
                else
                {
                    // Edge leaves the bitmap; use the canonical brute-force per-pixel clipping path.
                    for (int x = 0; x < len; ++x)
                    {
                        float y0 = yTop;
                        float x1 = x;
                        float x2 = x + 1;
                        float x3 = xb;
                        float y3 = yBottom;
                        float y1 = (x - x0) / dx + yTop;
                        float y2 = (x + 1 - x0) / dx + yTop;

                        if (x0 < x1 && x3 > x2)
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x1, y1);
                            HandleClippedEdge(scanline, x, e, x1, y1, x2, y2);
                            HandleClippedEdge(scanline, x, e, x2, y2, x3, y3);
                        }
                        else if (x3 < x1 && x0 > x2)
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x2, y2);
                            HandleClippedEdge(scanline, x, e, x2, y2, x1, y1);
                            HandleClippedEdge(scanline, x, e, x1, y1, x3, y3);
                        }
                        else if (x0 < x1 && x3 > x1)
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x1, y1);
                            HandleClippedEdge(scanline, x, e, x1, y1, x3, y3);
                        }
                        else if (x3 < x1 && x0 > x1)
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x1, y1);
                            HandleClippedEdge(scanline, x, e, x1, y1, x3, y3);
                        }
                        else if (x0 < x2 && x3 > x2)
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x2, y2);
                            HandleClippedEdge(scanline, x, e, x2, y2, x3, y3);
                        }
                        else if (x3 < x2 && x0 > x2)
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x2, y2);
                            HandleClippedEdge(scanline, x, e, x2, y2, x3, y3);
                        }
                        else
                        {
                            HandleClippedEdge(scanline, x, e, x0, y0, x3, y3);
                        }
                    }
                }
            }

            e = e.Next;
        }
    }

    /// <summary>
    /// Adds the coverage of one clipped edge segment to a scanline pixel. Ported from
    /// <c>stbtt__handle_clipped_edge</c>.
    /// </summary>
    /// <param name="scanline">Coverage array to accumulate into.</param>
    /// <param name="x">Pixel column to accumulate.</param>
    /// <param name="e">Active edge providing winding direction and vertical extent.</param>
    /// <param name="x0">Segment start X.</param>
    /// <param name="y0">Segment start Y.</param>
    /// <param name="x1">Segment end X.</param>
    /// <param name="y1">Segment end Y.</param>
    private static void HandleClippedEdge(float[] scanline, int x, ActiveEdge e, float x0, float y0, float x1, float y1)
    {
        if (y0 == y1)
            return;
        if (y0 > e.Ey)
            return;
        if (y1 < e.Sy)
            return;
        if (y0 < e.Sy)
        {
            x0 += (x1 - x0) * (e.Sy - y0) / (y1 - y0);
            y0 = e.Sy;
        }
        if (y1 > e.Ey)
        {
            x1 += (x1 - x0) * (e.Ey - y1) / (y1 - y0);
            y1 = e.Ey;
        }

        if (x0 <= x && x1 <= x)
            scanline[x] += e.Direction * (y1 - y0);
        else if (x0 >= x + 1 && x1 >= x + 1)
        {
            // Fully outside the pixel; no coverage.
        }
        else
            scanline[x] += e.Direction * (y1 - y0) * (1 - ((x0 - x) + (x1 - x)) / 2);
    }

    /// <summary>
    /// Returns the area of a trapezoid. Ported from <c>stbtt__sized_trapezoid_area</c>
    /// </summary>
    /// <param name="height">Trapezoid height.</param>
    /// <param name="topWidth">Width at the top.</param>
    /// <param name="bottomWidth">Width at the bottom.</param>
    /// <returns>The trapezoid area.</returns>
    private static float SizedTrapezoidArea(float height, float topWidth, float bottomWidth)
        => (topWidth + bottomWidth) / 2.0f * height;

    /// <summary>
    /// Returns the area of a trapezoid defined by top and bottom edge positions. Ported from
    /// <c>stbtt__position_trapezoid_area</c>.
    /// </summary>
    /// <param name="height">Trapezoid height.</param>
    /// <param name="tx0">Top-left position.</param>
    /// <param name="tx1">Top-right position.</param>
    /// <param name="bx0">Bottom-left position.</param>
    /// <param name="bx1">Bottom-right position.</param>
    /// <returns>The trapezoid area.</returns>
    private static float PositionTrapezoidArea(float height, float tx0, float tx1, float bx0, float bx1)
        => SizedTrapezoidArea(height, tx1 - tx0, bx1 - bx0);

    /// <summary>
    /// Returns the area of a triangle. Ported from <c>stbtt__sized_triangle_area</c>
    /// </summary>
    /// <param name="height">Triangle height.</param>
    /// <param name="width">Triangle base width.</param>
    /// <returns>The triangle area.</returns>
    private static float SizedTriangleArea(float height, float width) => height * width / 2;

    /// <summary>
    /// Box-prefilters the bitmap horizontally over the oversample kernel. Ported from
    /// <c>stbtt__h_prefilter</c>.
    /// </summary>
    /// <param name="pixels">Pixel buffer.</param>
    /// <param name="w">Bitmap width in pixels.</param>
    /// <param name="h">Bitmap height in pixels.</param>
    /// <param name="stride">Distance in bytes between consecutive rows.</param>
    /// <param name="kernelWidth">Oversampling kernel width.</param>
    private static void HorizontalPrefilter(byte[] pixels, int w, int h, int stride, uint kernelWidth)
    {
        // The canonical routine advances a row pointer between iterations; each row is filtered
        // independently here through an explicit row offset.
        for (int j = 0; j < h; j++)
            HorizontalPrefilterRow(pixels, w, stride, j, kernelWidth);
    }

    /// <summary>
    /// Applies one horizontal prefilter pass to a single bitmap row. Split from
    /// <see cref="HorizontalPrefilter"/> so rows are addressed without pointer arithmetic.
    /// </summary>
    /// <param name="pixels">Pixel buffer.</param>
    /// <param name="w">Bitmap width in pixels.</param>
    /// <param name="stride">Distance in bytes between consecutive rows.</param>
    /// <param name="row">Row index to filter.</param>
    /// <param name="kernelWidth">Oversampling kernel width.</param>
    private static void HorizontalPrefilterRow(byte[] pixels, int w, int stride, int row, uint kernelWidth)
    {
        int offset = row * stride;
        int safeW = w - (int)kernelWidth;
        var buffer = new byte[8];
        uint total = 0;
        int i;
        for (i = 0; i <= safeW; ++i)
        {
            total += (uint)(pixels[offset + i] - buffer[i & 7]);
            buffer[(i + (int)kernelWidth) & 7] = pixels[offset + i];
            pixels[offset + i] = (byte)(total / kernelWidth);
        }
        for (; i < w; ++i)
        {
            total -= buffer[i & 7];
            pixels[offset + i] = (byte)(total / kernelWidth);
        }
    }

    /// <summary>
    /// Box-prefilters the bitmap vertically over the oversample kernel. Ported from
    /// <c>stbtt__v_prefilter</c>.
    /// </summary>
    /// <param name="pixels">Pixel buffer.</param>
    /// <param name="w">Bitmap width in pixels.</param>
    /// <param name="h">Bitmap height in pixels.</param>
    /// <param name="stride">Distance in bytes between consecutive rows.</param>
    /// <param name="kernelWidth">Oversampling kernel width.</param>
    private static void VerticalPrefilter(byte[] pixels, int w, int h, int stride, uint kernelWidth)
    {
        int safeH = h - (int)kernelWidth;
        var buffer = new byte[8];

        for (int j = 0; j < w; ++j)
        {
            Array.Clear(buffer, 0, buffer.Length);
            uint total = 0;
            int i;
            for (i = 0; i <= safeH; ++i)
            {
                total += (uint)(pixels[i * stride + j] - buffer[i & 7]);
                buffer[(i + (int)kernelWidth) & 7] = pixels[i * stride + j];
                pixels[i * stride + j] = (byte)(total / kernelWidth);
            }
            for (; i < h; ++i)
            {
                total -= buffer[i & 7];
                pixels[i * stride + j] = (byte)(total / kernelWidth);
            }
        }
    }

    #endregion

    #region Kerning

    /// <summary>
    /// Reads the horizontal kern adjustment for a glyph pair from the legacy <c>kern</c> table.
    /// Ported from <c>stbtt__GetGlyphKernInfoAdvance</c> and <c>stbtt_GetGlyphKernAdvance</c>.
    /// The GPOS path is not ported; the
    /// canonical loader for this repository's embedded font uses the legacy table only.
    /// </summary>
    /// <param name="glyph1">Left glyph index.</param>
    /// <param name="glyph2">Right glyph index.</param>
    /// <returns>The kern adjustment in font units, or zero when no pair is found.</returns>
    public int GetGlyphKernAdvance(int glyph1, int glyph2)
    {
        if (_kern == 0)
            return 0;

        int data = _kern;
        if (U16(data + 2) < 1) // Number of sub-tables must be at least one.
            return 0;
        if (U16(data + 8) != 1) // First sub-table must be a horizontal format-0 table.
            return 0;

        int l = 0;
        int r = U16(data + 10) - 1;
        uint needle = ((uint)glyph1 << 16) | (uint)glyph2;
        while (l <= r)
        {
            int m = (l + r) >> 1;
            uint straw = U32(data + 18 + m * 6);
            if (needle < straw)
                r = m - 1;
            else if (needle > straw)
                l = m + 1;
            else
                return S16(data + 22 + m * 6);
        }
        return 0;
    }

    #endregion

    #region Big-endian font data access

    /// <summary>Reads an unsigned byte, returning zero for out-of-range offsets.</summary>
    /// <param name="i">Byte offset into <see cref="Data"/>.</param>
    /// <returns>The byte value.</returns>
    private byte ByteAt(long i) => i >= 0 && i < Data.Length ? Data[i] : (byte)0;

    /// <summary>Reads a big-endian unsigned 16-bit value, matching <c>ttUSHORT</c>.</summary>
    /// <param name="i">Byte offset into <see cref="Data"/>.</param>
    /// <returns>The value, or zero when out of range.</returns>
    private ushort U16(long i) => (ushort)((ByteAt(i) << 8) | ByteAt(i + 1));

    /// <summary>Reads a big-endian signed 16-bit value, matching <c>ttSHORT</c>.</summary>
    /// <param name="i">Byte offset into <see cref="Data"/>.</param>
    /// <returns>The value, or zero when out of range.</returns>
    private short S16(long i) => (short)U16(i);

    /// <summary>Reads a big-endian unsigned 32-bit value, matching <c>ttULONG</c>.</summary>
    /// <param name="i">Byte offset into <see cref="Data"/>.</param>
    /// <returns>The value, or zero when out of range.</returns>
    private uint U32(long i) => ((uint)ByteAt(i) << 24) | ((uint)ByteAt(i + 1) << 16) | ((uint)ByteAt(i + 2) << 8) | ByteAt(i + 3);

    /// <summary>
    /// Tests a four-byte tag at an offset, matching the <c>stbtt_tag4</c> macro
    /// </summary>
    /// <param name="data">Font file image.</param>
    /// <param name="i">Offset of the tag.</param>
    /// <param name="c0">First tag byte.</param>
    /// <param name="c1">Second tag byte.</param>
    /// <param name="c2">Third tag byte.</param>
    /// <param name="c3">Fourth tag byte.</param>
    /// <returns><see langword="true"/> when all four bytes match.</returns>
    private static bool Tag(byte[] data, long i, byte c0, byte c1, byte c2, byte c3)
        => i >= 0 && i + 3 < data.Length && data[i] == c0 && data[i + 1] == c1 && data[i + 2] == c2 && data[i + 3] == c3;

    /// <summary>
    /// Tests a four-character table tag, matching the <c>stbtt_tag</c> macro.
    /// </summary>
    /// <param name="data">Font file image.</param>
    /// <param name="i">Offset of the tag.</param>
    /// <param name="tag">Four-character tag.</param>
    /// <returns><see langword="true"/> when the tag matches.</returns>
    private static bool Tag(byte[] data, long i, string tag)
        => Tag(data, i, (byte)tag[0], (byte)tag[1], (byte)tag[2], (byte)tag[3]);

    /// <summary>
    /// Tests the sfnt version signatures, matching <c>stbtt__isfont</c>
    ///. OpenType-with-CFF is accepted here; <see cref="Init"/>
    /// then parses the CFF table when no <c>glyf</c> table exists.
    /// </summary>
    /// <param name="data">Font file image.</param>
    /// <param name="i">Candidate font offset.</param>
    /// <returns><see langword="true"/> when a known signature is present.</returns>
    private static bool IsFont(byte[] data, long i)
    {
        if (Tag(data, i, (byte)'1', 0, 0, 0))
            return true;
        if (Tag(data, i, "typ1"))
            return true;
        if (Tag(data, i, "OTTO"))
            return true;
        if (Tag(data, i, 0, 1, 0, 0))
            return true;
        if (Tag(data, i, "true"))
            return true;
        return false;
    }

    /// <summary>
    /// Finds a table offset in the sfnt directory, matching <c>stbtt__find_table</c>
    /// </summary>
    /// <param name="data">Font file image.</param>
    /// <param name="fontStart">Offset of the font within the file.</param>
    /// <param name="tag">Four-character table tag.</param>
    /// <returns>The table offset, or zero when absent.</returns>
    private static uint FindTable(byte[] data, int fontStart, string tag)
    {
        int numTables = U16At(data, fontStart + 4);
        int tableDir = fontStart + 12;
        for (int i = 0; i < numTables; i++)
        {
            int loc = tableDir + 16 * i;
            if (Tag(data, loc, tag))
                return U32At(data, loc + 8);
        }
        return 0;
    }

    /// <summary>Reads a big-endian unsigned 16-bit value from a raw array.</summary>
    /// <param name="data">Byte array.</param>
    /// <param name="i">Offset.</param>
    /// <returns>The value, or zero when out of range.</returns>
    private static ushort U16At(byte[] data, long i)
        => (i >= 0 && i + 1 < data.Length) ? (ushort)((data[i] << 8) | data[i + 1]) : (ushort)0;

    /// <summary>Reads a big-endian unsigned 32-bit value from a raw array.</summary>
    /// <param name="data">Byte array.</param>
    /// <param name="i">Offset.</param>
    /// <returns>The value, or zero when out of range.</returns>
    private static uint U32At(byte[] data, long i)
        => (i >= 0 && i + 3 < data.Length)
            ? ((uint)data[i] << 24) | ((uint)data[i + 1] << 16) | ((uint)data[i + 2] << 8) | data[i + 3]
            : 0u;

    #endregion
}
