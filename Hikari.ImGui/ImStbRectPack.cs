#nullable enable
// ROLE IN ENGINE
//   Rectangle packer for the font atlas. This is a direct managed port of the skyline
//   bottom-left packer that Dear ImGui bundles as imstb_rectpack.h, used to assign every
//   glyph bitmap, the white texel block and the baked line region a non-overlapping position
//   inside the atlas texture.
//
// SUBSYSTEM CONNECTIONS & DATA FLOW
//
//   ┌───────────────────────────────────────────┐
//   │ ImFontAtlas (pack add / repack / grow)    │
//   │ • requests rectangles and reads positions │
//   └───────────────────────────────────────────┘
//                          │ width/height in, x/y out
//                          ▼
//   ┌───────────────────────────────────────────┐
//   │ ImStbrpContext                            │
//   │ • skyline nodes, free list, active list   │
//   └───────────────────────────────────────────┘
//
// THREADING & LIFECYCLE NOTES
//   A packer context is not thread-safe. The owning atlas drives it from a single thread; nodes
//   must stay valid between InitTarget and the PackRects calls that follow it.

using System;
using System.Collections.Generic;

namespace Hikari.ImGui;

#region Packer input type

/// <summary>
/// One rectangle request and its assigned position. Mirrors canonical <c>stbrp_rect</c>
/// (<c>ThirdParty/imgui/imstb_rectpack.h:119-131</c>). All coordinates increase down and to the
/// right, and the assigned position is the rectangle's top-left corner.
/// </summary>
internal struct ImStbrpRect
{
    /// <summary>Caller-defined identifier copied back unchanged; unused by the packer itself.</summary>
    public int Id;

    /// <summary>Requested width in pixels.</summary>
    public int W;

    /// <summary>Requested height in pixels.</summary>
    public int H;

    /// <summary>Assigned left coordinate, or <see cref="ImStbrpContext.MaxValue"/> when unpacked.</summary>
    public int X;

    /// <summary>Assigned top coordinate, or <see cref="ImStbrpContext.MaxValue"/> when unpacked.</summary>
    public int Y;

    /// <summary>Non-zero when the rectangle was successfully packed.</summary>
    public int WasPacked;
}

#endregion

#region Skyline packer context

/// <summary>
/// Skyline bottom-left rectangle packer. Ports the canonical implementation section of
/// <c>imstb_rectpack.h</c> (<c>ThirdParty/imgui/imstb_rectpack.h:209-585</c>) with the node
/// linked lists stored as parallel integer arrays instead of pointers. Only the canonical default
/// bottom-left heuristic is implemented; canonical <c>stbrp_setup_heuristic</c>
/// (<c>imstb_rectpack.h:233-243</c>) also offers a best-fit variant that Dear ImGui never selects, so
/// that unreachable branch is intentionally omitted rather than carried as unused code.
/// </summary>
internal sealed class ImStbrpContext
{
    /// <summary>Sentinel coordinate meaning "this rectangle was not packed".</summary>
    public const int MaxValue = 0x7fffffff;

    /// <summary>Link value referring to the context's active-list head rather than a node's next field.</summary>
    private const int LinkActiveHead = -1;

    /// <summary>Link value meaning "no link exists"; used while the best-position search runs.</summary>
    private const int LinkNone = int.MinValue;

    /// <summary>X coordinate of each skyline node and of the two trailing extra nodes.</summary>
    private int[] _nodeX = Array.Empty<int>();

    /// <summary>Y coordinate of each skyline node and of the two trailing extra nodes.</summary>
    private int[] _nodeY = Array.Empty<int>();

    /// <summary>Next-node index of each skyline node, or -1 for the sentinel tail.</summary>
    private int[] _nodeNext = Array.Empty<int>();

    /// <summary>Index of the first free (reusable) node, or -1 when the free list is empty.</summary>
    private int _freeHead;

    /// <summary>Index of the first active skyline node; never -1 once initialized.</summary>
    private int _activeHead;

    /// <summary>Target width in pixels that rectangles are packed into.</summary>
    public int Width { get; private set; }

    /// <summary>Target height in pixels that rectangles are packed into.</summary>
    public int Height { get; private set; }

    /// <summary>Width quantization applied when the node count cannot cover the target width.</summary>
    public int Align { get; private set; }

    /// <summary>Number of caller-supplied nodes; the context allocates two extra sentinel nodes.</summary>
    public int NumNodes { get; private set; }

    /// <summary>
    /// Result of a best-position search: the link the new node should be inserted at, plus the
    /// candidate position.
    /// </summary>
    private struct FindResult
    {
        /// <summary>Link to insert at; <see cref="LinkNone"/> when no position fits.</summary>
        public int PrevLink;

        /// <summary>Candidate left coordinate.</summary>
        public int X;

        /// <summary>Candidate top coordinate.</summary>
        public int Y;
    }

    /// <summary>
    /// Initializes the packer for a target of the given size. Mirrors canonical
    /// <c>stbrp_init_target</c> (<c>ThirdParty/imgui/imstb_rectpack.h:265-288</c>).
    /// </summary>
    /// <param name="width">Target width in pixels; must be positive.</param>
    /// <param name="height">Target height in pixels; must be positive.</param>
    /// <param name="nodeCount">
    /// Number of caller-supplied skyline nodes. The canonical best-practice value is
    /// <c>width / 2</c>; the context allocates two extra nodes internally.
    /// </param>
    public void InitTarget(int width, int height, int nodeCount)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (nodeCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(nodeCount));

        int total = nodeCount + 2;
        if (_nodeX.Length < total)
        {
            _nodeX = new int[total];
            _nodeY = new int[total];
            _nodeNext = new int[total];
        }

        for (int i = 0; i < nodeCount - 1; i++)
            _nodeNext[i] = i + 1;
        _nodeNext[nodeCount - 1] = -1;

        Width = width;
        Height = height;
        NumNodes = nodeCount;
        _freeHead = 0;
        _activeHead = nodeCount;

        // Node 0 is the full width; node 1 is a sentinel that stands in for the target width.
        _nodeX[nodeCount] = 0;
        _nodeY[nodeCount] = 0;
        _nodeNext[nodeCount] = nodeCount + 1;
        _nodeX[nodeCount + 1] = width;
        _nodeY[nodeCount + 1] = 1 << 30;
        _nodeNext[nodeCount + 1] = -1;

        SetupAllowOutOfMemory(false);
    }

    /// <summary>
    /// Selects whether the packer may exhaust its node storage. Mirrors canonical
    /// <c>stbrp_setup_allow_out_of_mem</c> (<c>ThirdParty/imgui/imstb_rectpack.h:245-263</c>).
    /// </summary>
    /// <param name="allowOutOfMemory">
    /// When true the packer keeps pixel-accurate alignment and may fail on pathological input; when
    /// false it quantizes rectangle widths so the node list is always sufficient.
    /// </param>
    public void SetupAllowOutOfMemory(bool allowOutOfMemory)
    {
        Align = allowOutOfMemory ? 1 : (Width + NumNodes - 1) / NumNodes;
    }

    /// <summary>
    /// Packs an array of rectangles into the target. Mirrors canonical <c>stbrp_pack_rects</c>
    /// (<c>ThirdParty/imgui/imstb_rectpack.h:546-584</c>). Rectangles are sorted by decreasing
    /// height then decreasing width, packed, then restored to their original order.
    /// </summary>
    /// <param name="rects">Rectangles to pack; their <c>X</c>, <c>Y</c> and <c>WasPacked</c> fields
    /// receive the result. The array is reordered while packing.</param>
    /// <param name="count">Number of entries to pack from the start of <paramref name="rects"/>.</param>
    /// <returns><see langword="true"/> when every rectangle was packed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rects"/> is null.</exception>
    public bool PackRects(ImStbrpRect[] rects, int count)
    {
        if (rects == null)
            throw new ArgumentNullException(nameof(rects));

        // The 'WasPacked' field temporarily stores the original index so the sort can be undone.
        for (int i = 0; i < count; i++)
            rects[i].WasPacked = i;

        Array.Sort(rects, 0, count, HeightThenWidthComparer.Instance);

        for (int i = 0; i < count; i++)
        {
            if (rects[i].W == 0 || rects[i].H == 0)
            {
                rects[i].X = 0;
                rects[i].Y = 0;
                continue;
            }

            FindResult placed = SkylinePackRectangle(rects[i].W, rects[i].H);
            if (placed.PrevLink != LinkNone)
            {
                rects[i].X = placed.X;
                rects[i].Y = placed.Y;
            }
            else
            {
                rects[i].X = MaxValue;
                rects[i].Y = MaxValue;
            }
        }

        Array.Sort(rects, 0, count, OriginalOrderComparer.Instance);

        bool allPacked = true;
        for (int i = 0; i < count; i++)
        {
            bool packed = !(rects[i].X == MaxValue && rects[i].Y == MaxValue);
            rects[i].WasPacked = packed ? 1 : 0;
            if (!packed)
                allPacked = false;
        }
        return allPacked;
    }

    /// <summary>
    /// Sorts rectangles by decreasing height, breaking ties by decreasing width. Ports canonical
    /// <c>rect_height_compare</c> (<c>ThirdParty/imgui/imstb_rectpack.h:528-537</c>).
    /// </summary>
    private sealed class HeightThenWidthComparer : IComparer<ImStbrpRect>
    {
        /// <summary>Shared stateless instance; the comparison is deterministic and holds no state.</summary>
        public static readonly HeightThenWidthComparer Instance = new();

        /// <summary>Compares two rectangles for the packing sort order.</summary>
        /// <param name="a">First rectangle.</param>
        /// <param name="b">Second rectangle.</param>
        /// <returns>A negative, zero or positive value following <see cref="IComparer{T}"/>.</returns>
        public int Compare(ImStbrpRect a, ImStbrpRect b)
        {
            if (a.H != b.H)
                return b.H.CompareTo(a.H);
            return b.W.CompareTo(a.W);
        }
    }

    /// <summary>
    /// Restores the caller's original rectangle order using the stashed index. Ports canonical
    /// <c>rect_original_order</c> (<c>ThirdParty/imgui/imstb_rectpack.h:539-544</c>).
    /// </summary>
    private sealed class OriginalOrderComparer : IComparer<ImStbrpRect>
    {
        /// <summary>Shared stateless instance.</summary>
        public static readonly OriginalOrderComparer Instance = new();

        /// <summary>Compares two rectangles by their stashed original index.</summary>
        /// <param name="a">First rectangle.</param>
        /// <param name="b">Second rectangle.</param>
        /// <returns>A negative, zero or positive value following <see cref="IComparer{T}"/>.</returns>
        public int Compare(ImStbrpRect a, ImStbrpRect b) => a.WasPacked.CompareTo(b.WasPacked);
    }

    /// <summary>
    /// Reads the node referenced by a link. Ports the dereference of canonical
    /// <c>stbrp_node**</c>.
    /// </summary>
    /// <param name="link">Link value: <see cref="LinkActiveHead"/> or a node index.</param>
    /// <returns>The referenced node index.</returns>
    private int GetLink(int link) => link == LinkActiveHead ? _activeHead : _nodeNext[link];

    /// <summary>Writes the node referenced by a link. Ports the assignment through canonical
    /// <c>stbrp_node**</c>.</summary>
    /// <param name="link">Link value: <see cref="LinkActiveHead"/> or a node index.</param>
    /// <param name="node">Node index to store.</param>
    private void SetLink(int link, int node)
    {
        if (link == LinkActiveHead)
            _activeHead = node;
        else
            _nodeNext[link] = node;
    }

    /// <summary>
    /// Computes the lowest Y the given span can rest at, plus the waste area under it. Ports
    /// canonical <c>stbrp__skyline_find_min_y</c> (<c>ThirdParty/imgui/imstb_rectpack.h:291-339</c>).
    /// </summary>
    /// <param name="first">Index of the first skyline node covering the span's left edge.</param>
    /// <param name="x0">Left edge of the span.</param>
    /// <param name="width">Span width in pixels.</param>
    /// <param name="waste">Receives the waste area in pixels under the chosen placement.</param>
    /// <returns>The lowest Y the span can rest at.</returns>
    private int SkylineFindMinY(int first, int x0, int width, out int waste)
    {
        int x1 = x0 + width;
        int minY = 0;
        int wasteArea = 0;
        int visitedWidth = 0;
        int node = first;

        while (_nodeX[node] < x1)
        {
            if (_nodeY[node] > minY)
            {
                // Raising the floor accounts for all waste up to the previous minY, then adds the
                // waste under the newly covered strip.
                wasteArea += visitedWidth * (_nodeY[node] - minY);
                minY = _nodeY[node];
                if (_nodeX[node] < x0)
                    visitedWidth += _nodeX[_nodeNext[node]] - x0;
                else
                    visitedWidth += _nodeX[_nodeNext[node]] - _nodeX[node];
            }
            else
            {
                int underWidth = _nodeX[_nodeNext[node]] - _nodeX[node];
                if (underWidth + visitedWidth > width)
                    underWidth = width - visitedWidth;
                wasteArea += underWidth * (minY - _nodeY[node]);
                visitedWidth += underWidth;
            }
            node = _nodeNext[node];
        }

        waste = wasteArea;
        return minY;
    }

    /// <summary>
    /// Finds the best skyline position for a rectangle. Ports canonical
    /// <c>stbrp__skyline_find_best_pos</c> (<c>ThirdParty/imgui/imstb_rectpack.h:347-447</c>).
    /// </summary>
    /// <param name="width">Rectangle width in pixels.</param>
    /// <param name="height">Rectangle height in pixels.</param>
    /// <returns>The insertion link and candidate position; <c>PrevLink</c> is <see cref="LinkNone"/>
    /// when nothing fits.</returns>
    private FindResult SkylineFindBestPos(int width, int height)
    {
        int bestX = 0;
        int bestY = 1 << 30;
        int best = LinkNone;

        // Align to a multiple of Align so the node list is guaranteed sufficient.
        width = width + Align - 1;
        width -= width % Align;

        if (width > Width || height > Height)
        {
            FindResult none;
            none.PrevLink = LinkNone;
            none.X = 0;
            none.Y = 0;
            return none;
        }

        int node = _activeHead;
        int prev = LinkActiveHead;
        while (_nodeX[node] + width <= Width)
        {
            // Canonical default bottom-left heuristic: pick the lowest skyline candidate. The best-fit
            // branch that also weighs wasted area and right alignment is never selected by Dear ImGui.
            int y = SkylineFindMinY(node, _nodeX[node], width, out _);
            if (y < bestY)
            {
                bestY = y;
                best = prev;
            }
            prev = node;
            node = _nodeNext[node];
        }

        bestX = best == LinkNone ? 0 : _nodeX[GetLink(best)];

        FindResult result;
        result.PrevLink = best;
        result.X = bestX;
        result.Y = bestY;
        return result;
    }

    /// <summary>
    /// Places one rectangle and splices the resulting skyline nodes. Ports canonical
    /// <c>stbrp__skyline_pack_rectangle</c> (<c>ThirdParty/imgui/imstb_rectpack.h:449-526</c>).
    /// </summary>
    /// <param name="width">Rectangle width in pixels.</param>
    /// <param name="height">Rectangle height in pixels.</param>
    /// <returns>The placement, or a result whose <c>PrevLink</c> is <see cref="LinkNone"/> on failure.</returns>
    private FindResult SkylinePackRectangle(int width, int height)
    {
        FindResult res = SkylineFindBestPos(width, height);

        if (res.PrevLink == LinkNone || res.Y + height > Height || _freeHead == -1)
        {
            res.PrevLink = LinkNone;
            return res;
        }

        int node = _freeHead;
        _nodeX[node] = res.X;
        _nodeY[node] = res.Y + height;
        _freeHead = _nodeNext[node];

        int cur = GetLink(res.PrevLink);
        if (_nodeX[cur] < res.X)
        {
            int next = _nodeNext[cur];
            _nodeNext[cur] = node;
            cur = next;
        }
        else
        {
            SetLink(res.PrevLink, node);
        }

        // Free every skyline node fully covered by the new rectangle.
        while (_nodeNext[cur] != -1 && _nodeX[_nodeNext[cur]] <= res.X + width)
        {
            int next = _nodeNext[cur];
            _nodeNext[cur] = _freeHead;
            _freeHead = cur;
            cur = next;
        }

        _nodeNext[node] = cur;
        if (_nodeX[cur] < res.X + width)
            _nodeX[cur] = res.X + width;

        return res;
    }
}

#endregion
