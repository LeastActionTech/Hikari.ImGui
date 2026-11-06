// The MIT License (MIT)
// Copyright (c) 2026 Least Action Technologies Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

[TestFixture]
/// <summary>Exercises the draw-list, geometry, draw-data and font-atlas behavior of the pure C# ImGui port.</summary>
public class DrawTests
{
    private ImFontAtlas _atlas = null!;
    private ImDrawList _drawList = null!;

    [SetUp]
    /// <summary>Builds a fresh font atlas and draw list before each test.</summary>
    public void SetUp()
    {
        _atlas = new ImFontAtlas();
        _atlas.Build();
        _drawList = new ImDrawList(_atlas);
    }

    #region Primitives & Math Tests

    [Test]
    /// <summary>Verifies ImVec2 conversions and arithmetic against System.Numerics.Vector2.</summary>
    public void ImVec2_Vector2ConversionsAndArithmetic_AreAccurate()
    {
        ImVec2 a = new(10f, 20f);
        ImVec2 b = new(5f, 4f);

        Assert.That(a + b, Is.EqualTo(new ImVec2(15f, 24f)));
        Assert.That(a - b, Is.EqualTo(new ImVec2(5f, 16f)));
        Assert.That(a * 2f, Is.EqualTo(new ImVec2(20f, 40f)));
        Assert.That(a / 2f, Is.EqualTo(new ImVec2(5f, 10f)));

        Vector2 sysVec = a;
        Assert.That(sysVec.X, Is.EqualTo(10f));
        Assert.That(sysVec.Y, Is.EqualTo(20f));

        ImVec2 fromSys = sysVec;
        Assert.That(fromSys, Is.EqualTo(a));

        ImVec2 norm = new ImVec2(3f, 4f).Normalized();
        Assert.That(norm.X, Is.EqualTo(0.6f).Within(0.001f));
        Assert.That(norm.Y, Is.EqualTo(0.8f).Within(0.001f));
    }

    [Test]
    /// <summary>Verifies ImVec4 conversions and arithmetic against System.Numerics.Vector4.</summary>
    public void ImVec4_Vector4ConversionsAndArithmetic_AreAccurate()
    {
        ImVec4 v = new(1f, 2f, 3f, 4f);
        Vector4 sysVec = v;

        Assert.That(sysVec.X, Is.EqualTo(1f));
        Assert.That(sysVec.W, Is.EqualTo(4f));

        ImVec4 v2 = v * 2f;
        Assert.That(v2.X, Is.EqualTo(2f));
        Assert.That(v2.W, Is.EqualTo(8f));
    }

    [Test]
    /// <summary>Verifies packed ImColor values round-trip through canonical RGBA byte order.</summary>
    public void ImColor_PackAndUnpack_MatchesCanonicalRGBA()
    {
        // Pure Red: R=255, G=0, B=0, A=255 -> 0xFF0000FF
        uint redU32 = ImColor.Pack(255, 0, 0, 255);
        Assert.That(redU32, Is.EqualTo(0xFF0000FF));

        // Unpack red
        ImColor redCol = new(redU32);
        Assert.That(redCol.Value.X, Is.EqualTo(1.0f).Within(0.01f));
        Assert.That(redCol.Value.Y, Is.EqualTo(0.0f).Within(0.01f));
        Assert.That(redCol.Value.Z, Is.EqualTo(0.0f).Within(0.01f));
        Assert.That(redCol.Value.W, Is.EqualTo(1.0f).Within(0.01f));

        // Semi-transparent Green: R=0, G=255, B=0, A=128
        uint greenU32 = ImColor.Pack(0, 255, 0, 128);
        ImColor greenCol = new(greenU32);
        Assert.That(greenCol.Value.X, Is.EqualTo(0.0f).Within(0.01f));
        Assert.That(greenCol.Value.Y, Is.EqualTo(1.0f).Within(0.01f));
        Assert.That(greenCol.Value.Z, Is.EqualTo(0.0f).Within(0.01f));
        Assert.That(greenCol.Value.W, Is.EqualTo(128f / 255f).Within(0.01f));
    }

    [Test]
    /// <summary>Verifies ImRect extent, center and containment behavior.</summary>
    public void ImRect_GeometricPropertiesAndHitTesting_AreAccurate()
    {
        ImRect rect = new(10f, 20f, 110f, 70f);

        Assert.That(rect.GetWidth(), Is.EqualTo(100f));
        Assert.That(rect.GetHeight(), Is.EqualTo(50f));
        Assert.That(rect.GetSize(), Is.EqualTo(new ImVec2(100f, 50f)));
        Assert.That(rect.GetCenter(), Is.EqualTo(new ImVec2(60f, 45f)));
        Assert.That(rect.GetArea(), Is.EqualTo(5000f));

        // Contains point [Min, Max) half-open interval per 
        Assert.That(rect.Contains(new ImVec2(10f, 20f)), Is.True);
        Assert.That(rect.Contains(new ImVec2(50f, 40f)), Is.True);
        Assert.That(rect.Contains(new ImVec2(109.9f, 69.9f)), Is.True);
        Assert.That(rect.Contains(new ImVec2(110f, 70f)), Is.False); // Upper-right corner is exclusive
        Assert.That(rect.Contains(new ImVec2(9f, 20f)), Is.False);
        Assert.That(rect.Contains(new ImVec2(50f, 71f)), Is.False);

        // Overlaps
        ImRect overlapping = new(50f, 40f, 150f, 100f);
        ImRect disjoint = new(200f, 200f, 300f, 300f);
        Assert.That(rect.Overlaps(overlapping), Is.True);
        Assert.That(rect.Overlaps(disjoint), Is.False);

        // Expand
        ImRect expanded = rect;
        expanded.Expand(5f);
        Assert.That(expanded.Min, Is.EqualTo(new ImVec2(5f, 15f)));
        Assert.That(expanded.Max, Is.EqualTo(new ImVec2(115f, 75f)));

        // ClipWith
        ImRect clip = new(0f, 0f, 60f, 45f);
        rect.ClipWith(clip);
        Assert.That(rect.Min, Is.EqualTo(new ImVec2(10f, 20f)));
        Assert.That(rect.Max, Is.EqualTo(new ImVec2(60f, 45f)));
    }

    [Test]
    /// <summary>
    /// Verifies that a reserved rectangle produces the canonical six indices and four vertices and
    /// that the trailing command counts the emitted indices.
    /// </summary>
    public void PrimRectUV_EmitsCanonicalQuadAndUpdatesCommand()
    {
        _drawList.PrimReserve(6, 4);
        _drawList.PrimRectUV(
            new ImVec2(10f, 20f),
            new ImVec2(30f, 40f),
            new ImVec2(0f, 0f),
            new ImVec2(1f, 1f),
            0xFF112233u);

        Assert.That(_drawList.IdxBuffer, Is.EqualTo(new ushort[] { 0, 1, 2, 0, 2, 3 }));
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(_drawList.VtxBuffer[0].Pos, Is.EqualTo(new ImVec2(10f, 20f)));
        Assert.That(_drawList.VtxBuffer[1].Pos, Is.EqualTo(new ImVec2(30f, 20f)));
        Assert.That(_drawList.VtxBuffer[2].Pos, Is.EqualTo(new ImVec2(30f, 40f)));
        Assert.That(_drawList.VtxBuffer[3].Pos, Is.EqualTo(new ImVec2(10f, 40f)));
        Assert.That(_drawList.VtxBuffer[1].UV, Is.EqualTo(new ImVec2(1f, 0f)));
        Assert.That(_drawList.VtxBuffer[3].UV, Is.EqualTo(new ImVec2(0f, 1f)));
        Assert.That(_drawList.CmdBuffer[^1].ElemCount, Is.EqualTo(6u));
    }

    #endregion

    #region DrawList & Geometry Emission Tests

    /// <summary>
    /// Verifies that geometry past 65,535 vertices splits into a second command with a nonzero vertex offset
    /// when the backend advertises the capability, so 16-bit indices never wrap
    /// </summary>
    [Test]
    public void DrawList_LargeGeometryWithVtxOffsetSplitsWithoutWrappingIndices()
    {
        var drawList = new ImDrawList();
        drawList.Clear(ImDrawFlags.UseVtxOffset);
        // Each quad adds 4 vertices; 17,000 quads exceed 65,535 vertices.
        for (int n = 0; n < 17000; n++)
            drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(1, 1), 0xFFFFFFFF);

        Assert.That(drawList.CmdBuffer.Count, Is.GreaterThan(1), "The vertex buffer must split.");
        bool sawOffset = false;
        foreach (ImDrawCmd cmd in drawList.CmdBuffer)
            if (cmd.VtxOffset > 0)
                sawOffset = true;
        Assert.That(sawOffset, Is.True, "A later command must carry a nonzero vertex offset.");
        foreach (ushort idx in drawList.IdxBuffer)
            Assert.That(idx, Is.LessThan(1 << 16));
    }

    /// <summary>
    /// Verifies that a frame reset applies the shared initial flags instead of a fixed default, so that
    /// style anti-aliasing and the vertex-offset capability survive into the next frame
    /// (, ).
    /// </summary>
    [Test]
    public void DrawList_ClearAppliesSharedInitialFlags()
    {
        var drawList = new ImDrawList();
        drawList.Clear(ImDrawFlags.UseVtxOffset);
        Assert.That(drawList.Flags & ImDrawFlags.UseVtxOffset, Is.EqualTo(ImDrawFlags.UseVtxOffset));
        Assert.That(drawList.Flags & ImDrawFlags.AAFill, Is.EqualTo(ImDrawFlags.None),
            "Style-disabled anti-aliasing must not be reintroduced by a frame reset.");
    }

    [Test]
    /// <summary>Verifies a texture-eligible integer-thickness line emits canonical textured counts and baked UVs.</summary>
    public void DrawList_AddLine_TexturedPathEmitsCanonicalCountsAndBakedUvs()
    {
        _drawList.AddLine(new ImVec2(0, 0), new ImVec2(100, 0), 0xFFFFFFFF, 2.0f);

        // A zero-fractional integer width below IM_DRAWLIST_TEX_LINES_WIDTH_MAX takes the textured
        // path.: two outer vertices per point and six indices per segment.
        Assert.That(_atlas.TexUvLines.Length, Is.EqualTo(ImFontAtlas.TexLinesWidthMax + 1 + ImFontAtlas.TexLinesDetailedWidthCount));
        Assert.That((_drawList.Flags & ImDrawFlags.AALines) != 0, Is.True);
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(6));
        Assert.That(_drawList.CmdBuffer.Count, Is.EqualTo(1));
        Assert.That(_drawList.CmdBuffer[0].ElemCount, Is.EqualTo(6));

        // Both edge vertices carry the opaque stroke color and sample the super-sampled line UV span
        // for width 2.0 (u0, u1, v, 1/thickness).
        int texIdx = (int)((2.0f - 1.0f) * ImFontAtlas.TexLinesSampleCount + 0.5f) + (ImFontAtlas.TexLinesWidthMax + 1);
        ImVec4 baked = _atlas.TexUvLines[texIdx];
        ImVec2 leftUv = new(baked.X, baked.Z);
        ImVec2 rightUv = new(baked.Y, baked.Z);
        Assert.That(_drawList.VtxBuffer[0].UV, Is.EqualTo(leftUv));
        Assert.That(_drawList.VtxBuffer[1].UV, Is.EqualTo(rightUv));
        Assert.That(_drawList.VtxBuffer[2].UV, Is.EqualTo(leftUv));
        Assert.That(_drawList.VtxBuffer[3].UV, Is.EqualTo(rightUv));
        foreach (ImDrawVert vertex in _drawList.VtxBuffer)
        {
            Assert.That(vertex.Col, Is.EqualTo(0xFFFFFFFF));
        }
    }

    [Test]
    /// <summary>Verifies that non-AA lines emit 4 vertices and 6 indices per segment matching canonical Dear ImGui.</summary>
    public void DrawList_AddLine_WithoutTexturedLinesUsesCenterAndFringeVertices()
    {
        _drawList.Flags &= ~ImDrawFlags.AALines;
        _drawList.AddLine(new ImVec2(10, 10), new ImVec2(60, 10), 0xFFFFFFFF, 1.0f);

        // In canonical Dear ImGui, non-AA lines emit a single quad (4 vertices, 6 indices).
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(6));
    }

    [Test]
    /// <summary>Verifies ImFontAtlasFlags.NoBakedLines leaves the line UV table empty and disables the textured path.</summary>
    public void DrawList_NoBakedLines_DisablesTexturedLinePath()
    {
        ImFontAtlas atlas = new() { Flags = ImFontAtlasFlags.NoBakedLines };
        atlas.Build();
        Assert.That(atlas.TexUvLines.Length, Is.EqualTo(0));

        ImDrawList drawList = new(atlas);
        Assert.That((drawList.Flags & ImDrawFlags.AALines) != 0, Is.False);

        // With no baked table an integer-width line falls back to the non-AA quad path: 4 vertices, 6 indices.
        drawList.AddLine(new ImVec2(0, 0), new ImVec2(100, 0), 0xFFFFFFFF, 1.0f);
        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(6));
    }

    [Test]
    /// <summary>Verifies AddLineH and AddLineV emit pixel-aligned rectangle geometry for integer coordinates.</summary>
    public void DrawList_AddLineHV_EmitAxisAlignedStrokes()
    {
        _drawList.Clear();
        _drawList.AddLineH(0f, 100f, 20f, 0xFFFFFFFF);
        _drawList.AddLineV(30f, 0f, 50f, 0xFFFFFFFF);

        // Each integer-aligned line emits a pixel-aligned rect (4 vertices, 6 indices).
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(8));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(12));
        Assert.That(_drawList.VtxBuffer[0].Pos.X, Is.EqualTo(0.0f).Within(1e-4f));
        Assert.That(_drawList.VtxBuffer[0].Pos.Y, Is.EqualTo(20.0f).Within(1e-4f));
    }

    [Test]
    /// <summary>Verifies the SIMD reciprocal square root stays within the tolerance of both canonical paths.</summary>
    public void Rsqrt_ApproximatesExactReciprocalSquareRoot()
    {
        float[] values = { 0.25f, 1.0f, 2.0f, 10.0f, 1000.0f, 12345.678f };
        foreach (float x in values)
        {
            float exact = 1.0f / MathF.Sqrt(x);

            // The x86 path is the raw hardware estimate (roughly 12-bit accuracy); the ARM path adds
            // one Newton-Raphson refinement. A 1e-3 relative tolerance covers both.
            Assert.That(ImDrawList.Rsqrt(x), Is.EqualTo(exact).Within(exact * 1e-3f), $"x={x}");
        }
    }

    [Test]
    /// <summary>Verifies AddRectFilled emits a single axis-aligned quad.</summary>
    public void DrawList_AddRectFilled_EmitsCorrectQuad()
    {
        _drawList.AddRectFilled(new ImVec2(10, 10), new ImVec2(50, 50), 0xFF0000FF);

        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(6));

        // Verify vertex bounds
        Assert.That(_drawList.VtxBuffer[0].Pos, Is.EqualTo(new ImVec2(10, 10)));
        Assert.That(_drawList.VtxBuffer[2].Pos, Is.EqualTo(new ImVec2(50, 50)));
        Assert.That(_drawList.VtxBuffer[0].Col, Is.EqualTo(0xFF0000FF));
    }

    [Test]
    /// <summary>
    /// Verifies <see cref="ImDrawList.AddRect"/> rejects the reserved legacy corner-flag bits and the
    /// swapped <c>thickness</c>/<c>flags</c> misuse that canonical guards with
    /// <c>IM_ASSERT_USER_ERROR_RET</c>: no geometry is emitted and the canonical message is reported,
    /// while valid flags still draw.
    /// </summary>
    public void DrawList_AddRect_RejectsInvalidLegacyCornerFlags()
    {
        // The reserved mask is the canonical ~0x7FFFFFF0 (== 0x8000000F); a narrower value would let
        // legacy hard-coded corner flags through unnoticed.
        Assert.That((int)ImDrawFlags.InvalidMask_, Is.EqualTo(unchecked((int)0x8000000F)));

        ImGuiContext context = ImGui.CreateContext();
        context.IO.IniFilename = null; // Tests must never read or write the developer's.ini file.
        try
        {
            context.IO.ConfigErrorRecoveryEnableAssert = false;
            List<string> messages = new();
            context.ErrorCallback = (_, _, message) => messages.Add(message);

            // Bit 0 is reserved: this is a legacy hard-coded ImDrawCornerFlags value.
            _drawList.AddRect(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF, 0.0f, 1.0f, (ImDrawFlags)1);

            Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(0), "invalid flags must emit no geometry");
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("Incorrect parameter. Did you swap 'thickness' and 'flags'?"));

            _drawList.AddRect(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF, 0.0f, 1.0f, ImDrawFlags.RoundCornersAll);
            Assert.That(_drawList.VtxBuffer.Count, Is.GreaterThan(0), "valid flags must still emit geometry");
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    [Test]
    /// <summary>
    /// Verifies <see cref="ImDrawList.AddPolyline"/> rejects the reserved legacy corner-flag bits before
    /// emitting any line segment, mirroring the canonical guard.
    /// </summary>
    public void DrawList_AddPolyline_RejectsInvalidLegacyCornerFlags()
    {
        ImGuiContext context = ImGui.CreateContext();
        context.IO.IniFilename = null; // Tests must never read or write the developer's.ini file.
        try
        {
            context.IO.ConfigErrorRecoveryEnableAssert = false;
            List<string> messages = new();
            context.ErrorCallback = (_, _, message) => messages.Add(message);

            ImVec2[] points = { new(0, 0), new(10, 10), new(20, 0) };
            _drawList.AddPolyline(points, 0xFFFFFFFF, 2.0f, (ImDrawFlags)1);

            Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(0), "invalid flags must emit no geometry");
            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Is.EqualTo("Incorrect parameter. Did you swap 'thickness' and 'flags'?"));
        }
        finally
        {
            ImGui.DestroyContext(context);
        }
    }

    [Test]
    /// <summary>Verifies AddTriangleFilled emits anti-aliased filled geometry.</summary>
    public void DrawList_AddTriangleFilled_EmitsTriangle()
    {
        _drawList.AddTriangleFilled(new ImVec2(0, 0), new ImVec2(50, 100), new ImVec2(100, 0), 0xFF00FF00);

        // Anti-aliased convex fill.: 2 vertices per point and a triangle
        // fan plus one fringe quad per edge.
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(6));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(21));
        Assert.That(_drawList.CmdBuffer[0].ElemCount, Is.EqualTo(21));
    }

    [Test]
    /// <summary>Verifies the automatic circle segment count matches the canonical formula.</summary>
    public void DrawList_CircleAutoSegmentCount_MatchesCanonicalTable()
    {
        // At error 0.30 px and radius 50 the canonical formula yields the even count 30
        Assert.That(_drawList.CalcCircleAutoSegmentCount(50.0f), Is.EqualTo(30));

        _drawList.Clear();
        _drawList.AddCircleFilled(new ImVec2(100, 100), 50f, 0xFFFFFFFF);

        // PathArcToFastEx samples 30 evenly spaced points on the circle and the fill appends the
        // closing duplicate, which AddCircleFilled removes, leaving 48 vertices.
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(96));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(426));
    }

    [Test]
    /// <summary>Verifies AddNgon and AddNgonFilled honor an explicit side count.</summary>
    public void DrawList_AddNgon_HonorsExplicitSegmentCount()
    {
        _drawList.Clear();
        _drawList.AddNgonFilled(new ImVec2(50, 50), 40f, 0xFFFFFFFF, 6);

        // Six sides produce six inner and six outer fringe vertices.
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(12));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(48));
    }

    [Test]
    /// <summary>Verifies AddQuadFilled and AddEllipseFilled emit filled geometry.</summary>
    public void DrawList_QuadAndEllipse_RespectTessellation()
    {
        _drawList.Clear();
        _drawList.AddQuadFilled(new ImVec2(0, 0), new ImVec2(10, 0), new ImVec2(10, 10), new ImVec2(0, 10), 0xFFFFFFFF);
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(8));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(30));

        _drawList.Clear();
        _drawList.AddEllipseFilled(new ImVec2(100, 100), new ImVec2(40, 20), 0xFFFFFFFF, 0.0f, 12);
        // Twelve segments plus the closing duplicate removed -> 12 points -> 24 fringe vertices.
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(24));
    }

    [Test]
    /// <summary>Verifies AddBezierQuadratic auto-tessellates into multiple path points.</summary>
    public void DrawList_BezierQuadratic_AutoTessellates()
    {
        _drawList.Clear();
        _drawList.AddBezierQuadratic(new ImVec2(0, 100), new ImVec2(50, 0), new ImVec2(100, 100), 0xFFFFFFFF, 1.0f);

        // Auto-tessellation subdivides until the control polygon is flat; the stroke then emits at
        // least three vertices per sampled point ( and 857-858).
        Assert.That(_drawList.VtxBuffer.Count, Is.GreaterThanOrEqualTo(9));
    }

    [Test]
    /// <summary>Verifies AddConvexPolyFilled and AddConcavePolyFilled emit canonical fringe geometry.</summary>
    public void DrawList_PolyFilled_ConvexAndConcaveEmitFringe()
    {
        ImVec2[] convex = { new(0, 0), new(50, 0), new(50, 50), new(0, 50) };
        _drawList.Clear();
        _drawList.AddConvexPolyFilled(convex, 0xFFFFFFFF);
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(8));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(30));

        // An arrow-head polygon is concave; ear clipping must still fill it and add the same fringe.
        ImVec2[] concave = { new(0, 0), new(60, 30), new(0, 60), new(20, 30) };
        _drawList.Clear();
        _drawList.AddConcavePolyFilled(concave, 0xFFFFFFFF);
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(8));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(30));
    }

    [Test]
    /// <summary>Verifies PathStroke routes through the anti-aliased polyline path.</summary>
    public void DrawList_PathStroke_RoutesThroughAddPolyline()
    {
        _drawList.Clear();
        _drawList.PathLineTo(new ImVec2(0, 0));
        _drawList.PathLineTo(new ImVec2(100, 0));
        _drawList.PathStroke(0xFFFFFFFF, 1.0f);

        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(6));
    }

    [Test]
    /// <summary>Verifies AddCallback attaches a user callback to a dedicated command.</summary>
    public void DrawList_AddCallback_AttachesUserCallback()
    {
        _drawList.Clear();
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        _drawList.AddCallback(static (_, _) => { });

        // AddCallback forces a trailing command after the callback, so the callback lives one
        // position before the end.
        Assert.That(_drawList.CmdBuffer[^2].UserCallback, Is.Not.Null);
        Assert.That(_drawList.CmdBuffer[^2].UserCallbackDataOffset, Is.EqualTo(-1));
    }

    [Test]
    /// <summary>Verifies the 64K vertex-offset split starts a new command with a nonzero base.</summary>
    public void DrawList_LargeMesh_SplitsAtVtxOffset()
    {
        _drawList.Clear();
        _drawList.Flags |= ImDrawFlags.UseVtxOffset;
        for (int i = 0; i < 17000; i++)
            _drawList.AddRectFilled(new ImVec2(i, 0), new ImVec2(i + 1, 1), 0xFFFFFFFF);

        bool hasOffset = false;
        foreach (ImDrawCmd cmd in _drawList.CmdBuffer)
        {
            if (cmd.VtxOffset > 0)
                hasOffset = true;
        }
        Assert.That(_drawList.CmdBuffer.Count, Is.GreaterThan(1));
        Assert.That(hasOffset, Is.True);
    }

    [Test]
    /// <summary>Verifies ImDrawCmd.GetTexID resolves the texture reference identifier.</summary>
    public void DrawCmd_GetTexID_ResolvesTextureReference()
    {
        ImDrawCmd cmd = new(new ImVec4(0, 0, 10, 10), new ImTextureRef((IntPtr)7), 0, 0, 6);
        Assert.That(cmd.GetTexID(), Is.EqualTo((IntPtr)7));
    }

    [Test]
    /// <summary>Verifies ImDrawData.DeIndexAllBuffers converts indexed geometry losslessly.</summary>
    public void DrawData_DeIndexAllBuffers_PreservesGeometry()
    {
        _drawList.Clear();
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        ImDrawData drawData = new();
        drawData.AddDrawList(_drawList);
        drawData.DeIndexAllBuffers();

        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(0));
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(6));
        Assert.That(drawData.TotalVtxCount, Is.EqualTo(6));
    }

    [Test]
    /// <summary>Verifies clip-rectangle intersection and stack restoration.</summary>
    public void DrawList_ClipRectStack_IntersectsAndRestoresCorrectly()
    {
        _drawList.PushClipRect(new ImVec2(0, 0), new ImVec2(100, 100));
        Assert.That(_drawList.GetClipRectMin(), Is.EqualTo(new ImVec2(0, 0)));
        Assert.That(_drawList.GetClipRectMax(), Is.EqualTo(new ImVec2(100, 100)));

        _drawList.AddRectFilled(new ImVec2(10, 10), new ImVec2(20, 20), 0xFFFFFFFF);

        // Push intersecting child clip rect
        _drawList.PushClipRect(new ImVec2(50, 50), new ImVec2(150, 150), intersectWithCurrent: true);
        Assert.That(_drawList.GetClipRectMin(), Is.EqualTo(new ImVec2(50, 50)));
        Assert.That(_drawList.GetClipRectMax(), Is.EqualTo(new ImVec2(100, 100)));

        _drawList.AddRectFilled(new ImVec2(60, 60), new ImVec2(70, 70), 0xFFFFFFFF);

        _drawList.PopClipRect();
        Assert.That(_drawList.GetClipRectMin(), Is.EqualTo(new ImVec2(0, 0)));
        Assert.That(_drawList.GetClipRectMax(), Is.EqualTo(new ImVec2(100, 100)));
    }

    [Test]
    /// <summary>Verifies ImDrawData aggregates command-list totals and scale-clip behavior.</summary>
    public void DrawData_AggregatesDrawListsProperly()
    {
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        _drawList.AddLine(new ImVec2(0, 0), new ImVec2(20, 20), 0xFFFFFFFF);

        ImDrawData drawData = new();
        drawData.AddDrawList(_drawList);

        Assert.That(drawData.CmdListsCount, Is.EqualTo(1));
        Assert.That(drawData.TotalVtxCount, Is.EqualTo(_drawList.VtxBuffer.Count));
        Assert.That(drawData.TotalIdxCount, Is.EqualTo(_drawList.IdxBuffer.Count));
    }

    [Test]
    /// <summary>Verifies channel geometry merges back in channel order and keeps distinct texture bindings.</summary>
    public void DrawListSplitter_SplitAndMerge_PreservesChannelOrder()
    {
        _drawList.Clear();
        _drawList.PushClipRectFullScreen();
        _drawList.ChannelsSplit(2);

        // Channel 1 records its rectangle first, channel 0 records after it. Distinct texture
        // references keep the two commands from coalescing so their merged order is observable.
        _drawList.ChannelsSetCurrent(1);
        _drawList.PushTexture(new ImTextureRef((IntPtr)2));
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        _drawList.PopTexture();

        _drawList.ChannelsSetCurrent(0);
        _drawList.PushTexture(new ImTextureRef((IntPtr)1));
        _drawList.AddRectFilled(new ImVec2(20, 0), new ImVec2(30, 10), 0xFFFFFFFF);
        _drawList.PopTexture();

        _drawList.ChannelsMerge();

        Assert.That(_drawList.Splitter.ChannelCount, Is.EqualTo(1));

        List<ImDrawCmd> geometryCommands = new();
        foreach (ImDrawCmd cmd in _drawList.CmdBuffer)
        {
            if (cmd.ElemCount > 0)
                geometryCommands.Add(cmd);
        }

        Assert.That(geometryCommands.Count, Is.EqualTo(2));
        Assert.That(geometryCommands[0].GetTexID(), Is.EqualTo((IntPtr)1));
        Assert.That(geometryCommands[0].ElemCount, Is.EqualTo(6u));
        Assert.That(geometryCommands[0].IdxOffset, Is.EqualTo(0u));
        Assert.That(geometryCommands[1].GetTexID(), Is.EqualTo((IntPtr)2));
        Assert.That(geometryCommands[1].ElemCount, Is.EqualTo(6u));
        Assert.That(geometryCommands[1].IdxOffset, Is.EqualTo(6u));
    }

    [Test]
    /// <summary>Verifies PrimUnreserve releases the tail vertices, indices and command element count.</summary>
    public void DrawList_PrimUnreserve_RestoresBufferSizes()
    {
        _drawList.Clear();
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        _drawList.AddRectFilled(new ImVec2(20, 0), new ImVec2(30, 10), 0xFFFFFFFF);

        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(8));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(12));
        Assert.That(_drawList.CmdBuffer[^1].ElemCount, Is.EqualTo(12u));

        _drawList.PrimUnreserve(6, 4);

        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(4));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(6));
        Assert.That(_drawList.CmdBuffer[^1].ElemCount, Is.EqualTo(6u));
    }

    [Test]
    /// <summary>Verifies CloneOutput copies buffers without aliasing the source draw list.</summary>
    public void DrawList_CloneOutput_ProducesIndependentCopy()
    {
        _drawList.Clear();
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        _drawList.Flags |= ImDrawFlags.UseVtxOffset;

        ImDrawList clone = _drawList.CloneOutput();

        Assert.That(clone.VtxBuffer.Count, Is.EqualTo(_drawList.VtxBuffer.Count));
        Assert.That(clone.IdxBuffer.Count, Is.EqualTo(_drawList.IdxBuffer.Count));
        Assert.That(clone.CmdBuffer.Count, Is.EqualTo(_drawList.CmdBuffer.Count));
        Assert.That(clone.Flags, Is.EqualTo(_drawList.Flags));
        Assert.That(clone.VtxBuffer, Is.Not.SameAs(_drawList.VtxBuffer));
        Assert.That(clone.IdxBuffer, Is.Not.SameAs(_drawList.IdxBuffer));
        Assert.That(clone.CmdBuffer, Is.Not.SameAs(_drawList.CmdBuffer));

        int clonedVtxCount = clone.VtxBuffer.Count;
        _drawList.AddRectFilled(new ImVec2(40, 0), new ImVec2(50, 10), 0xFFFFFFFF);

        Assert.That(clone.VtxBuffer.Count, Is.EqualTo(clonedVtxCount));
        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(clonedVtxCount + 4));
    }

    [Test]
    /// <summary>Verifies the frame reset matches canonical _ResetForNewFrame: one empty command, no clip push, splitter cleared.</summary>
    public void DrawList_Clear_ResetsToCanonicalEmptyFrame()
    {
        _drawList.Clear();
        _drawList.PushClipRectFullScreen();
        _drawList.ChannelsSplit(2);
        _drawList.ChannelsSetCurrent(1);
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);

        _drawList.Clear();

        Assert.That(_drawList.VtxBuffer.Count, Is.EqualTo(0));
        Assert.That(_drawList.IdxBuffer.Count, Is.EqualTo(0));
        Assert.That(_drawList.CmdBuffer.Count, Is.EqualTo(1));
        Assert.That(_drawList.CmdBuffer[0].ElemCount, Is.EqualTo(0u));
        Assert.That(_drawList.CmdBuffer[0].UserCallback, Is.Null);
        // No clip rectangle is pushed, so the command header keeps the canonical zeroed rectangle.
        Assert.That(_drawList.CmdBuffer[0].ClipRect, Is.EqualTo(new ImVec4(0, 0, 0, 0)));
        Assert.That(_drawList.GetClipRectMin(), Is.EqualTo(ImVec2.Zero));
        Assert.That(_drawList.GetClipRectMax(), Is.EqualTo(ImVec2.Zero));
        Assert.That(_drawList.Splitter.ChannelCount, Is.EqualTo(1));
        Assert.That(_drawList.Splitter.CurrentChannel, Is.EqualTo(0));
    }

    [Test]
    /// <summary>Verifies ImDrawData aggregation skips empty draw lists like canonical AddDrawListToDrawDataEx.</summary>
    public void DrawData_AddDrawList_SkipsEmptyLists()
    {
        ImDrawList emptyList = new(_atlas);
        ImDrawData drawData = new();
        drawData.AddDrawList(emptyList);

        Assert.That(drawData.CmdListsCount, Is.EqualTo(0));
        Assert.That(drawData.TotalVtxCount, Is.EqualTo(0));
        Assert.That(drawData.TotalIdxCount, Is.EqualTo(0));

        _drawList.Clear();
        _drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        drawData.AddDrawList(_drawList);

        Assert.That(drawData.CmdListsCount, Is.EqualTo(1));
        Assert.That(drawData.TotalVtxCount, Is.EqualTo(4));
        Assert.That(drawData.TotalIdxCount, Is.EqualTo(6));
    }

    #endregion

    #region Font Atlas & Typography Tests

    [Test]
    /// <summary>Verifies the atlas builds a valid texture with a solid white texel.</summary>
    public void FontAtlas_Build_ProducesValidDimensionsAndWhitePixel()
    {
        ImFontAtlas atlas = new();
        bool built = atlas.Build();

        Assert.That(built, Is.True);
        Assert.That(atlas.TexWidth, Is.GreaterThan(0));
        Assert.That(atlas.TexHeight, Is.GreaterThan(0));

        atlas.GetTexDataAsRGBA32(out byte[] pixels, out int width, out int height);
        Assert.That(pixels.Length, Is.EqualTo(width * height * 4));

        // Top-left pixel (0,0) must be solid white (255, 255, 255, 255)
        Assert.That(pixels[0], Is.EqualTo((byte)255));
        Assert.That(pixels[1], Is.EqualTo((byte)255));
        Assert.That(pixels[2], Is.EqualTo((byte)255));
        Assert.That(pixels[3], Is.EqualTo((byte)255));

        // TexUvWhitePixel must be within the top-left white rectangle
        Assert.That(atlas.TexUvWhitePixel.X, Is.GreaterThan(0f));
        Assert.That(atlas.TexUvWhitePixel.Y, Is.GreaterThan(0f));
    }

    [Test]
    /// <summary>Verifies TrueType-derived glyph metrics and canonical text measurement.</summary>
    public void ImFont_GlyphLookupAndTextMeasurement_OperateAccurately()
    {
        ImFontAtlas atlas = new();
        // The ProggyClean pixel-clean metrics below belong to the bitmap face, which must be
        // requested explicitly because a no-context default now resolves to the vector face
        atlas.AddFontDefaultBitmap();
        atlas.Build();
        var font = atlas.Fonts[0];

        // ProggyClean is monospaced: every glyph advances 7 px at the default 13 px size. These are
        // genuine TrueType metrics (896 font units * 13/1664), not the former constant 8 px advance.
        var glyphA = font.FindGlyph('A');
        Assert.That(glyphA.Visible, Is.True);
        Assert.That(glyphA.AdvanceX, Is.EqualTo(7.0f));

        var glyphH = font.FindGlyph('H');
        var glyphI = font.FindGlyph('i');
        Assert.That(glyphH.AdvanceX, Is.EqualTo(7.0f));
        Assert.That(glyphI.AdvanceX, Is.EqualTo(7.0f));

        // Per-glyph box offsets must differ: they come from each glyph's glyf bounding box.
        Assert.That(glyphH.X1 - glyphH.X0, Is.EqualTo(6.0f));
        Assert.That(glyphI.X1 - glyphI.X0, Is.EqualTo(2.0f));
        Assert.That(glyphI.X0, Is.GreaterThan(glyphH.X0));

        // "Hamburgefonstiv" = 15 glyphs * 7 px = 105 px wide, one 13 px line tall.
        var hamburgefonstiv = font.CalcTextSizeA(13.0f, float.MaxValue, 0f, "Hamburgefonstiv");
        Assert.That(hamburgefonstiv.X, Is.EqualTo(105.0f));
        Assert.That(hamburgefonstiv.Y, Is.EqualTo(13.0f));

        // Multiline measurement: "Hi\nWorld" is 5 glyphs wide (World), 26 px tall.
        var multiSize = font.CalcTextSizeA(13.0f, float.MaxValue, 0f, "Hi\nWorld");
        Assert.That(multiSize.X, Is.EqualTo(35.0f));
        Assert.That(multiSize.Y, Is.EqualTo(26.0f));
    }

    [Test]
    /// <summary>Verifies rasterized glyph bitmaps contain non-empty coverage.</summary>
    public void FontAtlas_GlyphBitmaps_ContainRasterizedCoverage()
    {
        ImFontAtlas atlas = new();
        atlas.Build();
        atlas.GetTexDataAsRGBA32(out byte[] pixels, out int width, out int height);

        var font = atlas.Fonts[0];
        var glyphH = font.FindGlyph('H');
        Assert.That(glyphH.Visible, Is.True);

        // Walk the glyph's UV rectangle and confirm at least one pixel carries coverage. An empty
        // bitmap would indicate the rasterizer silently produced nothing.
        int px0 = (int)(glyphH.U0 * width);
        int py0 = (int)(glyphH.V0 * height);
        int px1 = (int)MathF.Ceiling(glyphH.U1 * width);
        int py1 = (int)MathF.Ceiling(glyphH.V1 * height);
        int covered = 0;
        for (int y = py0; y < py1; y++)
        {
            for (int x = px0; x < px1; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] > 0)
                    covered++;
            }
        }
        Assert.That(covered, Is.GreaterThan(0));
    }

    [Test]
    /// <summary>Verifies RasterizerMultiply scales rasterized coverage and remains a no-op at 1.0.</summary>
    public void FontAtlas_RasterizerMultiply_ScalesRasterizedCoverage()
    {
        // Canonical multiplies each coverage byte by the source's RasterizerMultiply and clamps the
        // result (, applied from the glyph bake at 5339).
        // A factor of 1.0 must leave the bitmap untouched.
        ImFontAtlas defaultAtlas = new();
        ImFont defaultFont = defaultAtlas.AddFontDefaultVector(new ImFontConfig());
        defaultAtlas.Build();

        ImFontAtlas sharpAtlas = new();
        ImFont sharpFont = sharpAtlas.AddFontDefaultVector(new ImFontConfig { RasterizerMultiply = 1.0f });
        sharpAtlas.Build();

        ImFontAtlas dimAtlas = new();
        ImFont dimFont = dimAtlas.AddFontDefaultVector(new ImFontConfig { RasterizerMultiply = 0.5f });
        dimAtlas.Build();

        long defaultCoverage = SumGlyphAlpha(defaultAtlas, defaultFont, 'H');
        long sharpCoverage = SumGlyphAlpha(sharpAtlas, sharpFont, 'H');
        long dimCoverage = SumGlyphAlpha(dimAtlas, dimFont, 'H');

        Assert.That(sharpCoverage, Is.GreaterThan(0));
        Assert.That(sharpCoverage, Is.EqualTo(defaultCoverage),
            "an explicit factor of 1.0 must behave like the default factor");
        Assert.That(dimCoverage, Is.LessThan(sharpCoverage),
            "a factor of 0.5 must measurably reduce rasterized coverage");
        Assert.That(dimCoverage, Is.EqualTo(sharpCoverage / 2.0).Within(sharpCoverage * 0.12));
    }

    [Test]
    /// <summary>Verifies a merged source supplies a codepoint absent from the first source through lazy baking.</summary>
    public void FontAtlas_MergedSource_SuppliesCodepointThroughLazyBaking()
    {
        // The synthetic CFF face maps only 'A' and 'B'; the merged ProggyForever source supplies 'H'.
        // Canonical scans every source in load order., so a
        // codepoint that only the merged source contains must bake from that source rather than
        // falling back on the first source.
        ImFontAtlas atlas = new();
        ImFontConfig baseConfig = new()
        {
            SizePixels = 13.0f,
            GlyphRanges = new uint[] { 'A', 'A', 0 },
        };
        ImFont font = atlas.AddFontFromMemoryTTF(BuildCffOpenTypeFace(), 13.0f, baseConfig);

        ImFontConfig mergedConfig = new()
        {
            MergeMode = true,
            GlyphRanges = new uint[] { 'B', 'B', 0 },
        };
        mergedConfig.FontData = ImGuiDefaultFontData.DecompressProggyForever();
        atlas.AddFont(mergedConfig);

        atlas.Build();

        ImFontBaked baked = font.GetFontBaked(13.0f);
        Assert.That(font.Sources.Count, Is.EqualTo(2));
        Assert.That(baked.IsGlyphLoaded('H'), Is.False,
            "'H' must not be preloaded before the lazy request");

        ImFontGlyph glyph = baked.FindGlyph('H');
        Assert.That(glyph.Visible, Is.True);
        Assert.That(glyph.AdvanceX, Is.GreaterThan(0.0f));
        Assert.That(glyph.SourceIdx, Is.EqualTo(1u),
            "'H' must be baked from the merged second source");
    }

    [Test]
    /// <summary>Verifies a face lacking every ellipsis candidate auto-bakes a three-dot glyph at U+0085.</summary>
    public void FontAtlas_MissingEllipsis_AutoBakesThreeDotGlyphAtU0085()
    {
        // The synthetic face maps only the period, so no ellipsis candidate exists. Canonical's
        // special-glyph setup therefore requests U+0085 and sets EllipsisAutoBake
        // three periods spaced one pixel apart (3771-3813, invoked at 4586-4589).
        ImFontAtlas atlas = new();
        byte[] face = BuildCffOpenTypeFace(BuildCffTestCmap('.', new ushort[] { 1 }));
        ImFont font = atlas.AddFontFromMemoryTTF(face, 13.0f, new ImFontConfig
        {
            SizePixels = 13.0f,
            EllipsisChar = 0x2026,
        });

        Assert.That(font.IsGlyphInFont(0x2026), Is.False);
        Assert.That(font.IsGlyphInFont(0x0085), Is.False);
        Assert.That(font.EllipsisChar, Is.EqualTo(0x0085u));
        Assert.That(font.EllipsisAutoBake, Is.True);

        atlas.Build();

        ImFontBaked baked = font.GetFontBaked(13.0f);
        ImFontGlyph? synthesized = baked.FindGlyphNoFallback(0x0085);
        Assert.That(synthesized, Is.Not.Null, "the synthesized glyph must be present in the baked lookup");
        Assert.That(baked.FindGlyphNoFallback(0x2026), Is.Null,
            "a codepoint the face does not contain must not be synthesized");

        ImFontGlyph dot = baked.FindGlyphNoFallback('.')!.Value;
        ImFontGlyph ellipsis = baked.FindGlyph(0x0085);
        float dotStep = (dot.X1 - dot.X0) + 1.0f;

        Assert.That(ellipsis.Codepoint, Is.EqualTo(0x0085u));
        Assert.That(ellipsis.Visible, Is.True);
        Assert.That(ellipsis.X1 - ellipsis.X0, Is.EqualTo(dotStep * 3.0f - 1.0f).Within(0.001f));
        Assert.That(ellipsis.AdvanceX, Is.GreaterThan(0.0f));
        Assert.That(ellipsis.AdvanceX,
            Is.EqualTo(MathF.Max(dot.AdvanceX, dot.X0 + dotStep * 3.0f - 1.0f)).Within(0.001f));

        // The packed rectangle must hold three copied periods, not merely three-dot geometry.
        atlas.GetTexDataAsRGBA32(out byte[] pixels, out int width, out int height);
        long dotCoverage = SumRectAlpha(pixels, width, height, dot);
        long ellipsisCoverage = SumRectAlpha(pixels, width, height, ellipsis);
        Assert.That(dotCoverage, Is.GreaterThan(0));
        Assert.That(ellipsisCoverage, Is.EqualTo(dotCoverage * 3));
    }

    [Test]
    /// <summary>Verifies the lazy ellipsis synthesis fires only while EllipsisAutoBake is set.</summary>
    public void FontAtlas_EllipsisSynthesis_RequiresEllipsisAutoBake()
    {
        // Canonical documents that clearing EllipsisAutoBake suppresses the three-period synthesis so a
        // caller can substitute an alternative ellipsis codepoint
        ImFontAtlas bakedAtlas = new();
        ImFont bakedFont = bakedAtlas.AddFontFromMemoryTTF(
            BuildCffOpenTypeFace(BuildCffTestCmap('.', new ushort[] { 1 })),
            13.0f, new ImFontConfig { SizePixels = 13.0f });
        Assert.That(bakedFont.EllipsisAutoBake, Is.True, "a face without an ellipsis must request auto-baking");
        bakedAtlas.Build();
        Assert.That(bakedFont.GetFontBaked(13.0f).FindGlyphNoFallback(0x0085), Is.Not.Null);

        ImFontAtlas plainAtlas = new();
        ImFont plainFont = plainAtlas.AddFontFromMemoryTTF(
            BuildCffOpenTypeFace(BuildCffTestCmap('.', new ushort[] { 1 })),
            13.0f, new ImFontConfig { SizePixels = 13.0f });
        plainFont.EllipsisAutoBake = false;
        plainAtlas.Build();
        Assert.That(plainFont.GetFontBaked(13.0f).FindGlyphNoFallback(0x0085), Is.Null,
            "no synthesized ellipsis may be produced while EllipsisAutoBake is cleared");
    }

    [Test]
    /// <summary>Verifies ellipsis auto-detection prefers U+2026 when the face contains it.</summary>
    public void FontAtlas_EllipsisAutoDetection_PrefersU2026WhenFaceContainsIt()
    {
        // Canonical tries the source's explicit ellipsis, then U+2026, then U+0085, and keeps the first
        // candidate the face actually contains.
        ImFontAtlas atlas = new();
        byte[] face = BuildCffOpenTypeFace(BuildCffTestCmap('\u2026', new ushort[] { 1 }));
        ImFont font = atlas.AddFontFromMemoryTTF(face, 13.0f, new ImFontConfig { SizePixels = 13.0f });

        Assert.That(font.IsGlyphInFont(0x2026), Is.True);
        Assert.That(font.EllipsisChar, Is.EqualTo(0x2026u));
        Assert.That(font.EllipsisAutoBake, Is.False);

        atlas.Build();
        ImFontBaked baked = font.GetFontBaked(13.0f);
        ImFontGlyph ellipsis = baked.FindGlyph(0x2026);
        Assert.That(ellipsis.Codepoint, Is.EqualTo(0x2026u));
        Assert.That(ellipsis.Visible, Is.True);
    }

    [Test]
    /// <summary>Verifies ellipsis auto-detection falls back to U+0085 when U+2026 is absent.</summary>
    public void FontAtlas_EllipsisAutoDetection_UsesU0085WhenU2026Absent()
    {
        ImFontAtlas atlas = new();
        byte[] face = BuildCffOpenTypeFace(BuildCffTestCmap('\u0085', new ushort[] { 1 }));
        ImFont font = atlas.AddFontFromMemoryTTF(face, 13.0f, new ImFontConfig { SizePixels = 13.0f });

        Assert.That(font.IsGlyphInFont(0x2026), Is.False);
        Assert.That(font.IsGlyphInFont(0x0085), Is.True);
        Assert.That(font.EllipsisChar, Is.EqualTo(0x0085u));
        Assert.That(font.EllipsisAutoBake, Is.False, "a real U+0085 glyph must not be synthesized");
    }

    [Test]
    /// <summary>Verifies a source's exclusion ranges make lazy resolution skip it in favor of another source.</summary>
    public void FontAtlas_GlyphExcludeRanges_SkipsExcludingSourceForAcceptedSource()
    {
        // The bundled ProggyForever face contains 'A', so canonical would normally bake 'A' from the
        // first source that has it. Excluding 'A' on that source must push resolution to the merged
        // synthetic face, whose cmap maps 'A' to glyph 1
        // (ImFontAtlasBuildAcceptCodepointForSource, , honored
        // in the source loop at 4596).
        ImFontAtlas atlas = new();
        ImFontConfig baseConfig = new()
        {
            SizePixels = 13.0f,
            GlyphRanges = new uint[] { 'B', 'B', 0 },
            GlyphExcludeRanges = new uint[] { 'A', 'A', 0 },
        };
        ImFont font = atlas.AddFontFromMemoryTTF(
            ImGuiDefaultFontData.DecompressProggyForever(), 13.0f, baseConfig);

        ImFontConfig mergedConfig = new()
        {
            MergeMode = true,
            GlyphRanges = new uint[] { 'B', 'B', 0 },
            FontData = BuildCffOpenTypeFace(),
        };
        atlas.AddFont(mergedConfig);
        atlas.Build();

        // The excluding face genuinely contains 'A': exclusion decides which source supplies the
        // glyph, not what the face contains (canonical ImFont::IsGlyphInFont, ).
        Assert.That(font.IsGlyphInFont('A'), Is.True);
        Assert.That(font.Sources[0].StbFont, Is.Not.Null);
        Assert.That(font.Sources[0].StbFont!.FindGlyphIndex('A'), Is.Not.Zero,
            "the excluding source face must contain 'A' for the exclusion to be meaningful");
        Assert.That(font.Sources[0].GlyphExcludeRanges, Is.Not.SameAs(baseConfig.GlyphExcludeRanges),
            "the attached source must own a defensive copy of the exclusion list");

        ImFontBaked baked = font.GetFontBaked(13.0f);
        Assert.That(baked.IsGlyphLoaded('A'), Is.False, "'A' must not be preloaded before the lazy request");

        ImFontGlyph glyph = baked.FindGlyph('A');
        Assert.That(glyph.Visible, Is.True);
        Assert.That(glyph.SourceIdx, Is.EqualTo(1u),
            "'A' must come from the merged source, not from the excluding source");
    }

    [Test]
    /// <summary>Verifies exclusion ranges do not affect sources that accept the codepoint or have no list.</summary>
    public void FontAtlas_GlyphExcludeRanges_OutsideRangeIsStillAccepted()
    {
        // An exclusion list that covers only 'A' must not prevent the same source from supplying 'B',
        // matching canonical's inclusive per-pair comparison.
        ImFontAtlas atlas = new();
        ImFontConfig config = new()
        {
            SizePixels = 13.0f,
            GlyphRanges = new uint[] { 'B', 'B', 0 },
            GlyphExcludeRanges = new uint[] { 'A', 'A', 0 },
        };
        ImFont font = atlas.AddFontFromMemoryTTF(
            ImGuiDefaultFontData.DecompressProggyForever(), 13.0f, config);
        atlas.Build();

        ImFontGlyph glyph = font.GetFontBaked(13.0f).FindGlyph('B');
        Assert.That(glyph.Visible, Is.True);
        Assert.That(glyph.SourceIdx, Is.EqualTo(0u));
    }

    [Test]
    /// <summary>Verifies a remapped codepoint resolves to the target glyph and is reported present.</summary>
    public void FontAtlas_AddRemapChar_ResolvesToTargetGlyph()
    {
        // The synthetic face maps 'A' to a visible glyph and contains no U+0100. Canonical resolves the
        // remap inside the lazy load and stores the requested codepoint on the glyph while the outline
        // comes from the target. IsGlyphInFont applies
        // the same remap before asking the face (5397).
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontFromMemoryTTF(BuildCffOpenTypeFace(), 13.0f,
            new ImFontConfig { SizePixels = 13.0f });
        Assert.That(font.IsGlyphInFont('A'), Is.True);
        Assert.That(font.IsGlyphInFont(0x0100), Is.False, "the face must not contain the remap source");

        font.AddRemapChar(0x0100, 'A');
        Assert.That(font.IsGlyphInFont(0x0100), Is.True, "a remapped codepoint must be reported present");

        atlas.Build();
        ImFontBaked baked = font.GetFontBaked(13.0f);
        ImFontGlyph target = baked.FindGlyph('A');
        ImFontGlyph remapped = baked.FindGlyph(0x0100);

        Assert.That(remapped.Visible, Is.True);
        Assert.That(remapped.Codepoint, Is.EqualTo(0x0100u),
            "the stored glyph must report the requested codepoint, not its remap target");
        Assert.That(remapped.AdvanceX, Is.EqualTo(target.AdvanceX),
            "the remapped glyph must use the target glyph's advance");
        Assert.That(remapped.X0, Is.EqualTo(target.X0));
        Assert.That(remapped.Y0, Is.EqualTo(target.Y0));
        Assert.That(remapped.X1, Is.EqualTo(target.X1));
        Assert.That(remapped.Y1, Is.EqualTo(target.Y1));

        // Canonical stores a zero target instead of erasing the pair, so remapping the source to
        // codepoint zero makes it unresolvable again.
        font.AddRemapChar(0x0100, 0);
        Assert.That(font.IsGlyphInFont(0x0100), Is.False);
    }

    [Test]
    /// <summary>Verifies ordinary lookups are unchanged when no remap applies to them.</summary>
    public void FontAtlas_RemapChar_LeavesOrdinaryLookupUnchanged()
    {
        // A remap installed for one codepoint must not alter lookups of other codepoints
        // atlas with no remap so the assertion covers both the glyph identity and its geometry.
        ImFontAtlas controlAtlas = new();
        ImFont controlFont = controlAtlas.AddFontFromMemoryTTF(BuildCffOpenTypeFace(), 13.0f,
            new ImFontConfig { SizePixels = 13.0f });
        controlAtlas.Build();
        ImFontGlyph control = controlFont.GetFontBaked(13.0f).FindGlyph('B');

        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontFromMemoryTTF(BuildCffOpenTypeFace(), 13.0f,
            new ImFontConfig { SizePixels = 13.0f });
        font.AddRemapChar(0x0100, 'A');
        atlas.Build();

        ImFontBaked baked = font.GetFontBaked(13.0f);
        ImFontGlyph ordinary = baked.FindGlyph('B');
        Assert.That(ordinary.Visible, Is.True);
        Assert.That(ordinary.Codepoint, Is.EqualTo((uint)'B'),
            "an ordinary codepoint must keep its own glyph identity");
        Assert.That(ordinary.AdvanceX, Is.EqualTo(control.AdvanceX));
        Assert.That(ordinary.X0, Is.EqualTo(control.X0));
        Assert.That(ordinary.Y0, Is.EqualTo(control.Y0));
        Assert.That(ordinary.X1, Is.EqualTo(control.X1));
        Assert.That(ordinary.Y1, Is.EqualTo(control.Y1));

        // The remap target stays directly reachable, and an unpaired codepoint stays absent.
        Assert.That(baked.FindGlyph('A').Codepoint, Is.EqualTo((uint)'A'));
        Assert.That(font.IsGlyphInFont('B'), Is.True);
        Assert.That(font.IsGlyphInFont(0x0101), Is.False, "only the remapped codepoint may change");
        Assert.That(baked.FindGlyphNoFallback(0x0101), Is.Null);
    }

    [Test]
    /// <summary>Verifies UpdateNewFrame rejects a repeated or rewound frame count.</summary>
    public void FontAtlas_UpdateNewFrame_RejectsNonIncreasingFrameCount()
    {
        // Canonical asserts FrameCount < frame_count because bake ages are derived from the counter
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();
        Assert.That(atlas.FrameCount, Is.EqualTo(-1), "a built atlas starts before any frame");

        atlas.UpdateNewFrame(0);
        Assert.That(atlas.FrameCount, Is.EqualTo(0));
        atlas.UpdateNewFrame(4);
        Assert.That(atlas.FrameCount, Is.EqualTo(4));

        Assert.Throws<ArgumentOutOfRangeException>(() => atlas.UpdateNewFrame(4),
            "a repeated frame count must be rejected");
        Assert.Throws<ArgumentOutOfRangeException>(() => atlas.UpdateNewFrame(3),
            "a rewound frame count must be rejected");
        Assert.That(atlas.FrameCount, Is.EqualTo(4), "a rejected update must not change the counter");
    }

    /// <summary>Sums the alpha coverage inside a glyph's packed texture rectangle.</summary>
    /// <param name="atlas">Atlas owning the texture.</param>
    /// <param name="font">Font whose glyph is measured.</param>
    /// <param name="c">Character to measure at the font's reference size.</param>
    /// <returns>Total alpha across the glyph's texture rectangle.</returns>
    private static long SumGlyphAlpha(ImFontAtlas atlas, ImFont font, char c)
    {
        atlas.GetTexDataAsRGBA32(out byte[] pixels, out int width, out int height);
        return SumRectAlpha(pixels, width, height, font.FindGlyph(c));
    }

    /// <summary>Sums the alpha bytes of a rectangle derived from a glyph's UV coordinates.</summary>
    /// <param name="pixels">RGBA32 pixel buffer.</param>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="glyph">Glyph whose UV rectangle is measured.</param>
    /// <returns>Total alpha across the rectangle.</returns>
    private static long SumRectAlpha(byte[] pixels, int width, int height, ImFontGlyph glyph)
    {
        int x0 = (int)(glyph.U0 * width);
        int y0 = (int)(glyph.V0 * height);
        int x1 = (int)MathF.Ceiling(glyph.U1 * width);
        int y1 = (int)MathF.Ceiling(glyph.V1 * height);
        long total = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
                total += pixels[(y * width + x) * 4 + 3];
        }
        return total;
    }

    [Test]
    /// <summary>Verifies the embedded stb_compress stream expands to a valid 41208-byte TTF.</summary>
    public void FontData_DecompressProggyClean_ProducesValidTtf()
    {
        byte[] ttf = ImGuiDefaultFontData.DecompressProggyClean();

        Assert.That(ttf.Length, Is.EqualTo(41208));
        // sfnt version 1.0: 0x00010000 big-endian.
        Assert.That(ttf[0], Is.EqualTo((byte)0x00));
        Assert.That(ttf[1], Is.EqualTo((byte)0x01));
        Assert.That(ttf[2], Is.EqualTo((byte)0x00));
        Assert.That(ttf[3], Is.EqualTo((byte)0x00));

        // The initialized font must resolve the ASCII range and report real advances.
        ImStbTrueTypeFont? stb = ImStbTrueTypeFont.Init(ttf, 0);
        Assert.That(stb, Is.Not.Null);
        int glyphH = stb!.FindGlyphIndex('H');
        Assert.That(glyphH, Is.GreaterThan(0));
        Assert.That(stb.GetGlyphHMetrics(glyphH, out int advance, out _), Is.True);
        Assert.That(advance, Is.EqualTo(896));
    }

    [Test]
    /// <summary>Verifies the embedded ProggyForever stb_compress stream expands to a valid 18556-byte TTF.</summary>
    public void FontData_DecompressProggyForever_ProducesValidTtf()
    {
        byte[] ttf = ImGuiDefaultFontData.DecompressProggyForever();

        Assert.That(ttf.Length, Is.EqualTo(18556));
        // sfnt version 1.0: 0x00010000 big-endian.
        Assert.That(ttf[0], Is.EqualTo((byte)0x00));
        Assert.That(ttf[1], Is.EqualTo((byte)0x01));
        Assert.That(ttf[2], Is.EqualTo((byte)0x00));
        Assert.That(ttf[3], Is.EqualTo((byte)0x00));

        // The initialized font must resolve the ASCII range and report real advances. ProggyForever
        // uses 440 font units per glyph over an 828-unit ascent-to-descent span, unlike ProggyClean's
        // 896 units over 1664.
        ImStbTrueTypeFont? stb = ImStbTrueTypeFont.Init(ttf, 0);
        Assert.That(stb, Is.Not.Null);
        int glyphH = stb!.FindGlyphIndex('H');
        Assert.That(glyphH, Is.GreaterThan(0));
        Assert.That(stb.GetGlyphHMetrics(glyphH, out int advance, out _), Is.True);
        Assert.That(advance, Is.EqualTo(440));
    }

    [Test]
    /// <summary>Verifies a synthetic OpenType/CFF face initializes and its Type2 charstrings decode to the expected outlines.</summary>
    public void Init_CffOpenTypeFace_DecodesType2Charstrings()
    {
        // No CFF/OTF asset ships with the canonical checkout, so the test builds a minimal but
        // structurally complete OpenType/CFF image in memory. It carries three glyphs: an empty
        //.notdef, a rectangle drawn by a local subroutine (glyph 1, 'A'), and a cubic curve
        // (glyph 2, 'B').
        byte[] otf = BuildCffOpenTypeFace();

        ImStbTrueTypeFont? font = ImStbTrueTypeFont.Init(otf, 0);
        Assert.That(font, Is.Not.Null, "a CFF face must initialize now that the Type2 path is ported");

        // The cmap and metric tables are read through the shared, unchanged directory paths.
        Assert.That(font!.FindGlyphIndex('A'), Is.EqualTo(1));
        Assert.That(font.FindGlyphIndex('B'), Is.EqualTo(2));
        Assert.That(font.GetGlyphHMetrics(1, out int advanceA, out int lsbA), Is.True);
        Assert.That(advanceA, Is.EqualTo(500));
        Assert.That(lsbA, Is.EqualTo(100));

        // Glyph 1: 100,100 rmoveto; -107 callsubr; endchar. The subroutine adds three relative
        // lines, and endchar closes the square back to its start with a fourth line.
        ImStbTrueTypeFont.Vertex[] rectangle = font.GetGlyphShape(1);
        Assert.That(rectangle.Length, Is.EqualTo(5), "rectangle charstring must produce four sides plus the close");
        Assert.That(rectangle[0].Type, Is.EqualTo(1)); // move
        Assert.That((rectangle[0].X, rectangle[0].Y), Is.EqualTo((100, 100)));
        Assert.That((rectangle[4].X, rectangle[4].Y), Is.EqualTo((100, 100)), "endchar must close the contour");
        Assert.That(font.GetGlyphBox(1, out int x0, out int y0, out int x1, out int y1), Is.True);
        Assert.That((x0, y0, x1, y1), Is.EqualTo((100, 100, 400, 400)));

        // Glyph 2: one relative cubic curve, whose control points participate in the bounds.
        ImStbTrueTypeFont.Vertex[] curve = font.GetGlyphShape(2);
        Assert.That(curve.Length, Is.EqualTo(3), "curve charstring must produce a move, a cubic, and the close");
        Assert.That(curve[1].Type, Is.EqualTo(4)); // cubic
        Assert.That((curve[1].Cx, curve[1].Cy, curve[1].Cx1, curve[1].Cy1), Is.EqualTo((100, 0, 100, 100)));
        Assert.That(font.GetGlyphBox(2, out int bx0, out int by0, out int bx1, out int by1), Is.True);
        Assert.That((bx0, by0, bx1, by1), Is.EqualTo((0, 0, 200, 100)));

        // Rasterizing at 0.01 scale maps the 300x300 square exactly onto a 3x3 pixel block.
        byte[] pixels = new byte[9];
        font.MakeGlyphBitmapSubpixel(pixels, 3, 3, 3, 0.01f, 0.01f, 0f, 0f, 1);
        foreach (byte coverage in pixels)
            Assert.That(coverage, Is.EqualTo(255), "the square must cover the whole 3x3 block");
    }

    [Test]
    /// <summary>Verifies the Type2 interpreter against a real OpenType/CFF program from a host font.</summary>
    /// <remarks>
    /// A real CFF program exercises operators the synthetic regression face does not: hint and
    /// counter mask skipping, hlineto/vlineto, hv/vh/vv/hh curveto, rcurveline/rlinecurve, the flex
    /// family, and CID FDArray/FDSelect selection. This test is host-dependent; it probes a short
    /// list of well-known font locations and skips with <see cref="Assert.Ignore(string)"/> when no
    /// OpenType/CFF file is installed. Assertions are limited to properties that hold for any CFF
    /// face, so the outcome is deterministic for whichever font is found.
    /// </remarks>
    public void Init_HostCffFont_DecodesRealCharstringProgram()
    {
        string? path = FindHostCffFont();
        if (path is null)
        {
            Assert.Ignore("no host OpenType/CFF font available");
            return; // Unreachable; the ignore exception ends the test.
        }

        byte[] data = File.ReadAllBytes(path);
        ImStbTrueTypeFont? font = ImStbTrueTypeFont.Init(data, 0);
        Assert.That(font, Is.Not.Null, $"{path} must initialize as a CFF face");

        int glyph = font!.FindGlyphIndex('A');
        Assert.That(glyph, Is.GreaterThan(0), $"{path} must map 'A' to a real glyph");

        ImStbTrueTypeFont.Vertex[] vertices = font.GetGlyphShape(glyph);
        Assert.That(vertices.Length, Is.GreaterThan(0), $"{path} 'A' must have an outline");
        Assert.That(vertices[0].Type, Is.EqualTo(1), "a real outline must start with a move vertex");

        Assert.That(font.GetGlyphBox(glyph, out int x0, out int y0, out int x1, out int y1), Is.True);
        Assert.That(x1 - x0, Is.GreaterThan(0), "the glyph box must be non-degenerate in X");
        Assert.That(y1 - y0, Is.GreaterThan(0), "the glyph box must be non-degenerate in Y");
        TestContext.WriteLine(
            $"host CFF face {path}: 'A' -> glyph {glyph}, {vertices.Length} outline vertices, box ({x0},{y0})-({x1},{y1})");

        // The decoded outline must rasterize to visible coverage at a typical pixel size.
        float scale = font.ScaleForPixelHeight(32.0f);
        font.GetGlyphBitmapBoxSubpixel(glyph, scale, scale, 0f, 0f, out int px0, out int py0, out int px1, out int py1);
        int width = px1 - px0;
        int height = py1 - py0;
        Assert.That(width, Is.GreaterThan(0));
        Assert.That(height, Is.GreaterThan(0));

        byte[] pixels = new byte[width * height];
        font.MakeGlyphBitmapSubpixel(pixels, width, height, width, scale, scale, 0f, 0f, glyph);
        int covered = 0;
        foreach (byte coverage in pixels)
        {
            if (coverage != 0)
                covered++;
        }
        Assert.That(covered, Is.GreaterThan(0), "the rasterized host glyph must produce visible pixels");
    }

    /// <summary>
    /// Probes well-known font directories for an installed OpenType/CFF file. Latin text faces are
    /// preferred so the common <c>'A'</c> codepoint is expected to map to a real glyph.
    /// </summary>
    /// <returns>The first matching font path, or <see langword="null"/> when none is installed.</returns>
    private static string? FindHostCffFont()
    {
        string[] knownNames =
        {
            "STIXGeneral.otf",
            "DejaVuSans.otf",
            "NotoSans-Regular.otf",
            "Arial.otf",
            "LiberationSans-Regular.otf",
            "FreeSans.otf",
        };
        string[] knownDirectories =
        {
            "/System/Library/Fonts/Supplemental",
            "/System/Library/Fonts",
            "/Library/Fonts",
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            @"C:\Windows\Fonts",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        };

        foreach (string directory in knownDirectories)
        {
            foreach (string name in knownNames)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // Fall back to any OpenType file in the same locations, including subdirectories such as
        // /usr/share/fonts/truetype.
        foreach (string directory in knownDirectories)
        {
            if (!Directory.Exists(directory))
                continue;
            try
            {
                foreach (string candidate in Directory.EnumerateFiles(directory, "*.otf", SearchOption.AllDirectories))
                    return candidate;
            }
            catch (IOException)
            {
                // An unreadable font directory must not fail the probe.
            }
            catch (UnauthorizedAccessException)
            {
                // An unreadable font directory must not fail the probe.
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a structurally complete OpenType/CFF font image with three glyphs for the CFF
    /// regression test.
    /// </summary>
    /// <returns>The font image bytes, beginning with the <c>OTTO</c> sfnt version.</returns>
    private static byte[] BuildCffOpenTypeFace() => BuildCffOpenTypeFace(BuildCffTestCmap());

    /// <summary>
    /// Builds the synthetic CFF OpenType face with an explicit character map so tests can control
    /// exactly which codepoints the face contains without baking.
    /// </summary>
    /// <param name="cmap">Character map table the face exposes.</param>
    /// <returns>The OpenType face bytes.</returns>
    private static byte[] BuildCffOpenTypeFace(byte[] cmap)
    {
        // head: only presence is required; the port reads indexToLocFormat only for glyf faces.
        byte[] head = new byte[54];

        // hhea: ascent 800, descent -200, line gap 90, three long horizontal metrics.
        byte[] hhea = new byte[36];
        WriteI16(hhea, 4, 800);
        WriteI16(hhea, 6, -200);
        WriteI16(hhea, 8, 90);
        WriteU16(hhea, 34, 3);

        // hmtx: (advance, left side bearing) for glyphs 0 through 2.
        byte[] hmtx = new byte[12];
        WriteI16(hmtx, 0, 0);
        WriteI16(hmtx, 2, 0);
        WriteI16(hmtx, 4, 500);
        WriteI16(hmtx, 6, 100);
        WriteI16(hmtx, 8, 600);
        WriteI16(hmtx, 10, 50);

        // maxp version 0.5: four version bytes followed by the glyph count.
        byte[] maxp = new byte[6];
        WriteU32(maxp, 0, 0x00005000);
        WriteU16(maxp, 4, 3);

        (string Tag, byte[] Data)[] tables =
        {
            ("cmap", cmap),
            ("CFF ", BuildCffTestTable()),
            ("head", head),
            ("hhea", hhea),
            ("hmtx", hmtx),
            ("maxp", maxp),
        };

        int offset = 12 + 16 * tables.Length;
        int[] offsets = new int[tables.Length];
        for (int i = 0; i < tables.Length; i++)
        {
            offset = (offset + 3) & ~3;
            offsets[i] = offset;
            offset += tables[i].Data.Length;
        }

        byte[] font = new byte[offset];
        WriteU32(font, 0, 0x4F54544F); // 'OTTO'
        WriteU16(font, 4, tables.Length);
        for (int i = 0; i < tables.Length; i++)
        {
            int record = 12 + 16 * i;
            WriteTag(font, record, tables[i].Tag);
            WriteU32(font, record + 8, (uint)offsets[i]);
            WriteU32(font, record + 12, (uint)tables[i].Data.Length);
            Array.Copy(tables[i].Data, 0, font, offsets[i], tables[i].Data.Length);
        }
        return font;
    }

    /// <summary>
    /// Builds the CFF table used by the CFF regression test: header, name/top-dict/string/global
    /// subroutine INDEXes, a three-item CharStrings INDEX, a Private dictionary, and one local
    /// subroutine.
    /// </summary>
    /// <returns>The CFF table bytes.</returns>
    private static byte[] BuildCffTestTable()
    {
        // Glyph 0 (.notdef): endchar only.
        byte[] charStringNotdef = { 0x0E };
        // Glyph 1 ('A'): 100,100 rmoveto; -107 callsubr (bias 107 selects item 0); endchar.
        byte[] charStringRectangle = { 0xEF, 0xEF, 0x15, 0x20, 0x0A, 0x0E };
        // Glyph 2 ('B'): 0,0 rmoveto; 100,0,0,100,100,0 rrcurveto; endchar.
        byte[] charStringCurve = { 0x8B, 0x8B, 0x15, 0xEF, 0x8B, 0x8B, 0xEF, 0xEF, 0x8B, 0x08, 0x0E };
        // Subroutine 0: three relative sides of a 300x300 square, then return.
        byte[] subroutine = { 247, 192, 139, 0x05, 139, 247, 192, 0x05, 251, 192, 139, 0x05, 0x0B };

        byte[] header = { 1, 0, 4, 1 };
        byte[] nameIndex = BuildCffIndex(new byte[] { (byte)'A' });
        byte[] charStringsIndex = BuildCffIndex(charStringNotdef, charStringRectangle, charStringCurve);
        byte[] subrIndex = BuildCffIndex(subroutine);

        // The top dictionary stores its offsets as five-byte 32-bit integers, so its encoded size
        // (and therefore the INDEX wrapping it) is fixed.
        const int TopDictIndexLength = 2 + 1 + 2 + 17;
        int charStringsOffset = 4 + nameIndex.Length + TopDictIndexLength + 2 + 2;
        int privateOffset = charStringsOffset + charStringsIndex.Length;
        const int PrivateSize = 6;
        const int SubrsOffset = PrivateSize; // The subroutine INDEX follows the Private dictionary.

        byte[] topDict = new byte[17];
        int p = 0;
        topDict[p++] = 29; // 32-bit integer
        WriteU32(topDict, p, (uint)charStringsOffset);
        p += 4;
        topDict[p++] = 17; // CharStrings
        topDict[p++] = 29;
        WriteU32(topDict, p, PrivateSize);
        p += 4;
        topDict[p++] = 29;
        WriteU32(topDict, p, (uint)privateOffset);
        p += 4;
        topDict[p++] = 18; // Private
        byte[] topDictIndex = BuildCffIndex(topDict);
        Assert.That(topDictIndex.Length, Is.EqualTo(TopDictIndexLength));

        byte[] privateDict = new byte[PrivateSize];
        privateDict[0] = 29;
        WriteU32(privateDict, 1, SubrsOffset);
        privateDict[5] = 19; // Subrs

        int length = 4 + nameIndex.Length + topDictIndex.Length + 2 + 2
            + charStringsIndex.Length + privateDict.Length + subrIndex.Length;
        byte[] cff = new byte[length];
        int o = 0;
        Array.Copy(header, 0, cff, o, header.Length);
        o += header.Length;
        Array.Copy(nameIndex, 0, cff, o, nameIndex.Length);
        o += nameIndex.Length;
        Array.Copy(topDictIndex, 0, cff, o, topDictIndex.Length);
        o += topDictIndex.Length;
        o += 2; // String INDEX: count 0.
        o += 2; // Global subroutine INDEX: count 0.
        Array.Copy(charStringsIndex, 0, cff, o, charStringsIndex.Length);
        o += charStringsIndex.Length;
        Array.Copy(privateDict, 0, cff, o, privateDict.Length);
        o += privateDict.Length;
        Array.Copy(subrIndex, 0, cff, o, subrIndex.Length);
        o += subrIndex.Length;
        Assert.That(o, Is.EqualTo(length), "the CFF table layout must be self-consistent");
        return cff;
    }

    /// <summary>
    /// Builds the Windows Unicode cmap used by the CFF regression test, mapping <c>'A'</c> to glyph 1
    /// and <c>'B'</c> to glyph 2.
    /// </summary>
    /// <returns>The cmap table bytes.</returns>
    private static byte[] BuildCffTestCmap() => BuildCffTestCmap('A', new ushort[] { 1, 2 });

    /// <summary>
    /// Builds a Windows Unicode format 6 cmap mapping a contiguous run of codepoints to glyph indexes.
    /// Used by special-glyph tests that need a face containing a specific ellipsis codepoint.
    /// </summary>
    /// <param name="firstCode">First codepoint in the mapped run.</param>
    /// <param name="glyphIds">Glyph index for each consecutive codepoint.</param>
    /// <returns>The cmap table bytes.</returns>
    private static byte[] BuildCffTestCmap(char firstCode, ushort[] glyphIds)
    {
        int subtableLength = 10 + 2 * glyphIds.Length;
        byte[] cmap = new byte[12 + subtableLength];
        WriteU16(cmap, 0, 0);        // version
        WriteU16(cmap, 2, 1);        // one encoding record
        WriteU16(cmap, 4, 3);        // Windows platform
        WriteU16(cmap, 6, 1);        // Unicode BMP encoding
        WriteU32(cmap, 8, 12);       // subtable offset relative to the cmap table
        const int sub = 12;
        WriteU16(cmap, sub, 6);              // format 6
        WriteU16(cmap, sub + 2, subtableLength);
        WriteU16(cmap, sub + 4, 0);          // language
        WriteU16(cmap, sub + 6, firstCode);  // first code
        WriteU16(cmap, sub + 8, glyphIds.Length);
        for (int i = 0; i < glyphIds.Length; i++)
            WriteU16(cmap, sub + 10 + 2 * i, glyphIds[i]);
        return cmap;
    }

    /// <summary>
    /// Builds a CFF INDEX with one-byte offsets from the supplied items. Every test item is small
    /// enough that the one-byte offset size suffices.
    /// </summary>
    /// <param name="items">Index items in order.</param>
    /// <returns>The INDEX bytes: count, offset size, offsets, then item data.</returns>
    private static byte[] BuildCffIndex(params byte[][] items)
    {
        int dataLength = 0;
        foreach (byte[] item in items)
            dataLength += item.Length;

        byte[] index = new byte[2 + 1 + (items.Length + 1) + dataLength];
        WriteU16(index, 0, items.Length);
        index[2] = 1; // Offset size: one byte per offset.
        int next = 1;
        index[3] = (byte)next;
        for (int i = 0; i < items.Length; i++)
        {
            next += items[i].Length;
            index[3 + i + 1] = (byte)next;
        }

        int cursor = 3 + items.Length + 1;
        foreach (byte[] item in items)
        {
            Array.Copy(item, 0, index, cursor, item.Length);
            cursor += item.Length;
        }
        return index;
    }

    /// <summary>Writes a big-endian unsigned 16-bit value into a byte array.</summary>
    /// <param name="data">Destination array.</param>
    /// <param name="offset">Offset of the value.</param>
    /// <param name="value">Value to write.</param>
    private static void WriteU16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    /// <summary>Writes a big-endian signed 16-bit value into a byte array.</summary>
    /// <param name="data">Destination array.</param>
    /// <param name="offset">Offset of the value.</param>
    /// <param name="value">Value to write.</param>
    private static void WriteI16(byte[] data, int offset, int value) => WriteU16(data, offset, value & 0xFFFF);

    /// <summary>Writes a big-endian unsigned 32-bit value into a byte array.</summary>
    /// <param name="data">Destination array.</param>
    /// <param name="offset">Offset of the value.</param>
    /// <param name="value">Value to write.</param>
    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    /// <summary>Writes a four-character table tag into a byte array.</summary>
    /// <param name="data">Destination array.</param>
    /// <param name="offset">Offset of the tag.</param>
    /// <param name="tag">Four-character tag.</param>
    private static void WriteTag(byte[] data, int offset, string tag)
    {
        for (int i = 0; i < 4; i++)
            data[offset + i] = (byte)tag[i];
    }

    [Test]
    /// <summary>Verifies AddFontDefaultVector loads the scalable face with canonical configuration and metrics.</summary>
    public void AddFontDefaultVector_LoadsScalableFaceWithCanonicalMetrics()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultVector();
        atlas.Build();

        // Canonical names the face, sets the 13 px implicit reference size, snaps horizontal
        // advances by default, and applies the 1.015 rasterizer scale plus the 0.5/16 pixel offset
        ImFontConfig source = font.Sources[0];
        Assert.That(source.Name, Is.EqualTo("ProggyForever.ttf"));
        Assert.That(source.SizePixels, Is.EqualTo(13.0f));
        Assert.That((source.Flags & ImFontFlags.ImplicitRefSize) != 0, Is.True);
        Assert.That(source.PixelSnapH, Is.True);
        Assert.That(source.ExtraSizeScale, Is.EqualTo(1.015f));
        Assert.That(source.GlyphOffset.Y, Is.EqualTo(0.40625f));

        // A default configuration requests PixelSnapH, so the vector face's 440 font units *
        // (13 / 828) * 1.015 = 7.011836 px is rounded to a whole 7 px advance, matching canonical
        // ImFontAtlasBakedAddFontGlyph.
        var glyphA = font.FindGlyph('A');
        Assert.That(glyphA.Visible, Is.True);
        Assert.That(glyphA.AdvanceX, Is.EqualTo(7.0f));
        Assert.That(font.FindGlyph('H').AdvanceX, Is.EqualTo(7.0f));
        Assert.That(font.FindGlyph('i').AdvanceX, Is.EqualTo(7.0f));

        // "Hamburgefonstiv" = 15 glyphs * 7 px, one 13 px line tall.
        var hamburgefonstiv = font.CalcTextSizeA(13.0f, float.MaxValue, 0f, "Hamburgefonstiv");
        Assert.That(hamburgefonstiv.X, Is.EqualTo(105.0f));
        Assert.That(hamburgefonstiv.Y, Is.EqualTo(13.0f));
    }

    [Test]
    /// <summary>Verifies a non-snapping vector configuration keeps fractional advances.</summary>
    public void AddFontDefaultVector_WithoutPixelSnapH_KeepsFractionalAdvances()
    {
        ImFontAtlas atlas = new();
        // A caller-supplied configuration suppresses the default PixelSnapH selection, so the
        // 1.015 ExtraSizeScale reaches the advance unmodified.
        ImFont font = atlas.AddFontDefaultVector(new ImFontConfig());
        atlas.Build();

        Assert.That(font.Sources[0].PixelSnapH, Is.False);
        Assert.That(font.FindGlyph('A').AdvanceX, Is.EqualTo(7.011836f).Within(0.0005f));
        Assert.That(font.CalcTextSizeA(13.0f, float.MaxValue, 0f, "Hamburgefonstiv").X,
            Is.EqualTo(105.17754f).Within(0.001f));
    }

    [Test]
    /// <summary>Verifies the default-font selection heuristic switches faces at the canonical 15 px threshold.</summary>
    public void AddFontDefault_SelectsFaceAtCanonicalSizeThreshold()
    {
        // Zero or less means "no owner context", which canonical resolves to the vector face.
        ImFontAtlas noContext = new();
        ImFont noContextFont = noContext.AddFontDefault(null, 0.0f);
        Assert.That(noContextFont.Sources[0].Name, Is.EqualTo("ProggyForever.ttf"));

        ImFontAtlas negative = new();
        ImFont negativeFont = negative.AddFontDefault(null, -1.0f);
        Assert.That(negativeFont.Sources[0].Name, Is.EqualTo("ProggyForever.ttf"));

        // 15 px and above select the vector face; anything smaller positive keeps the bitmap face.
        ImFontAtlas atThreshold = new();
        ImFont vectorFont = atThreshold.AddFontDefault(null, 15.0f);
        Assert.That(vectorFont.Sources[0].Name, Is.EqualTo("ProggyForever.ttf"));

        ImFontAtlas belowThreshold = new();
        ImFont bitmapFont = belowThreshold.AddFontDefault(null, 14.0f);
        Assert.That(bitmapFont.Sources[0].Name, Is.EqualTo("ProggyClean.ttf"));

        // The bitmap face keeps ProggyClean's 896-unit advance at the default 13 px reference size.
        belowThreshold.Build();
        Assert.That(bitmapFont.FindGlyph('A').AdvanceX, Is.EqualTo(7.0f));
    }

    [Test]
    /// <summary>Verifies a caller-supplied font configuration is not mutated, matching canonical copy semantics.</summary>
    public void AddFontDefaultVector_DoesNotMutateCallerConfig()
    {
        ImFontAtlas atlas = new();
        ImFontConfig config = new() { SizePixels = 13.0f };
        float originalExtraSizeScale = config.ExtraSizeScale;
        ImVec2 originalGlyphOffset = config.GlyphOffset;

        atlas.AddFontDefaultVector(config);

        // Canonical copies the template by value; repeated calls with one instance must not compound
        // ExtraSizeScale or GlyphOffset.
        Assert.That(config.ExtraSizeScale, Is.EqualTo(originalExtraSizeScale));
        Assert.That(config.GlyphOffset, Is.EqualTo(originalGlyphOffset));
        Assert.That(config.FontData, Is.Null);
        Assert.That(config.Name, Is.Empty);
    }

    [Test]
    /// <summary>Verifies the canonical UTF-8 decoder handles a multibyte codepoint.</summary>
    public void ImTextCharFromUtf8_DecodesMultibyteCodepoints()
    {
        // U+00E9 "e acute" is encoded as 0xC3 0xA9 in UTF-8.
        ReadOnlySpan<byte> utf8 = new byte[] { 0xC3, 0xA9 };
        int i = 0;
        int consumed = ImFont.ImTextCharFromUtf8(utf8, ref i, out uint codepoint);

        Assert.That(consumed, Is.EqualTo(2));
        Assert.That(i, Is.EqualTo(2));
        Assert.That(codepoint, Is.EqualTo(0x00E9u));
    }

    [Test]
    /// <summary>Verifies AddText emits one quad per visible glyph.</summary>
    public void DrawList_AddText_EmitsExpectedQuadsForPrintableCharacters()
    {
        ImFontAtlas atlas = new();
        atlas.Build();
        var font = atlas.Fonts[0];
        ImDrawList drawList = new(atlas);
        // Canonical frame reset leaves no clip rectangle, so the caller must push the frame clip
        // rectangle before emitting geometry, exactly as ImGui::NewFrame does.
        drawList.PushClipRectFullScreen();

        // "Hi" has 2 visible characters -> 2 quads -> 8 vertices, 12 indices
        drawList.AddText(font, 13.0f, new ImVec2(10, 10), 0xFFFFFFFF, "Hi");

        Assert.That(drawList.VtxBuffer.Count, Is.EqualTo(8));
        Assert.That(drawList.IdxBuffer.Count, Is.EqualTo(12));
    }

    [Test]
    /// <summary>
    /// Verifies the convenience AddText overload resolves its font through the shared atlas. A draw
    /// list bound at construction emits one quad per visible glyph; a draw list created without a
    /// shared atlas has no font to resolve and silently emits no text geometry at all. This pins the
    /// binding contract a host must satisfy before emitting text, mirroring canonical ImGui where an
    /// ImDrawList always holds an ImDrawListSharedData carrying the current font.
    /// </summary>
    public void DrawList_AddTextWithoutSharedAtlas_ResolvesNoFontAndEmitsNoText()
    {
        ImFontAtlas atlas = new();
        atlas.Build();

        ImDrawList bound = new(atlas);
        bound.PushClipRectFullScreen();
        bound.AddText(new ImVec2(10, 10), 0xFFFFFFFF, "Hi");

        // "Hi" has 2 visible characters -> 2 quads -> 8 vertices, 12 indices.
        Assert.That(bound.VtxBuffer.Count, Is.EqualTo(8), "A bound draw list resolves the atlas default font.");
        Assert.That(bound.IdxBuffer.Count, Is.EqualTo(12));

        ImDrawList unbound = new();
        unbound.PushClipRectFullScreen();
        unbound.AddText(new ImVec2(10, 10), 0xFFFFFFFF, "Hi");

        Assert.That(unbound.VtxBuffer.Count, Is.EqualTo(0), "An unbound draw list has no font to resolve.");
        Assert.That(unbound.IdxBuffer.Count, Is.EqualTo(0));
    }

    [Test]
    /// <summary>Verifies two requested sizes bake independently, with distinct advances and line heights.</summary>
    public void ImFont_PerSizeBaking_ProducesIndependentMetrics()
    {
        ImFontAtlas atlas = new();
        // The ProggyClean bitmap face is used because its integer metrics make the two sizes easy
        // to distinguish: 896 font units are 7 px at 13 px and 14 px at 26 px.
        atlas.AddFontDefaultBitmap();
        atlas.Build();
        ImFont font = atlas.Fonts[0];

        ImFontBaked baked13 = font.GetFontBaked(13.0f);
        ImFontBaked baked26 = font.GetFontBaked(26.0f);

        Assert.That(baked13, Is.Not.SameAs(baked26));
        Assert.That(baked13.Size, Is.EqualTo(13.0f));
        Assert.That(baked26.Size, Is.EqualTo(26.0f));

        // Each size bakes its own advance rather than scaling one reference bake.
        Assert.That(baked13.GetCharAdvance('H'), Is.EqualTo(7.0f));
        Assert.That(baked26.GetCharAdvance('H'), Is.EqualTo(14.0f));
        Assert.That(baked26.GetCharAdvance('H'), Is.Not.EqualTo(baked13.GetCharAdvance('H')));

        // Line heights follow the requested size independently.
        Assert.That(font.CalcTextSizeA(13.0f, float.MaxValue, 0f, "Hi").Y, Is.EqualTo(13.0f));
        Assert.That(font.CalcTextSizeA(26.0f, float.MaxValue, 0f, "Hi").Y, Is.EqualTo(26.0f));

        // Both bakes are cached and stable across repeated requests.
        Assert.That(font.GetFontBaked(13.0f), Is.SameAs(baked13));
        Assert.That(font.GetFontBaked(26.0f), Is.SameAs(baked26));
    }

    [Test]
    /// <summary>Verifies every packed glyph rectangle is disjoint and its UVs lie inside [0,1].</summary>
    public void FontAtlas_PackedLayout_HasNonOverlappingRectsAndUnitUvs()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();
        ImFont font = atlas.Fonts[0];

        // Force both sizes to pack their glyph rectangles.
        font.GetFontBaked(13.0f).FindGlyph('A');
        font.GetFontBaked(26.0f).FindGlyph('A');

        ImFontBaked[] bakes = { font.GetFontBaked(13.0f), font.GetFontBaked(26.0f) };
        int width = atlas.TexWidth;
        int height = atlas.TexHeight;
        Assert.That(width, Is.GreaterThan(0));
        Assert.That(height, Is.GreaterThan(0));

        var rects = new List<(int X0, int Y0, int X1, int Y1)>();
        foreach (ImFontBaked baked in bakes)
        {
            foreach (ImFontGlyph glyph in baked.Glyphs)
            {
                if (glyph.PackId < 0 || !glyph.Visible)
                    continue;

                // UVs must stay inside the unit square.
                Assert.That(glyph.U0, Is.InRange(0.0f, 1.0f));
                Assert.That(glyph.V0, Is.InRange(0.0f, 1.0f));
                Assert.That(glyph.U1, Is.InRange(0.0f, 1.0f));
                Assert.That(glyph.V1, Is.InRange(0.0f, 1.0f));

                // Reconstructing pixel bounds must land inside the texture and be non-degenerate.
                int x0 = (int)MathF.Round(glyph.U0 * width);
                int y0 = (int)MathF.Round(glyph.V0 * height);
                int x1 = (int)MathF.Round(glyph.U1 * width);
                int y1 = (int)MathF.Round(glyph.V1 * height);
                Assert.That(x0, Is.GreaterThanOrEqualTo(0));
                Assert.That(y0, Is.GreaterThanOrEqualTo(0));
                Assert.That(x1, Is.LessThanOrEqualTo(width));
                Assert.That(y1, Is.LessThanOrEqualTo(height));
                Assert.That(x1, Is.GreaterThan(x0));
                Assert.That(y1, Is.GreaterThan(y0));

                rects.Add((x0, y0, x1, y1));
            }
        }

        Assert.That(rects.Count, Is.GreaterThan(0));
        for (int i = 0; i < rects.Count; i++)
        {
            for (int j = i + 1; j < rects.Count; j++)
            {
                bool disjoint = rects[i].X1 <= rects[j].X0 || rects[j].X1 <= rects[i].X0
                    || rects[i].Y1 <= rects[j].Y0 || rects[j].Y1 <= rects[i].Y0;
                Assert.That(disjoint, Is.True, $"rectangles {i} and {j} overlap");
            }
        }
    }

    [Test]
    /// <summary>Verifies a full create is requested once, then lazy bakes queue partial update rectangles.</summary>
    public void FontAtlas_LazyBake_QueuesIncrementalTextureUpdates()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();

        ImTextureData texData = atlas.TexData!;
        Assert.That(texData.Status, Is.EqualTo(ImTextureStatus.WantCreate));
        // A full create uploads the whole surface, so no partial rectangles are recorded.
        Assert.That(texData.Updates.Count, Is.EqualTo(0));

        // Simulate a backend that uploaded the create and recorded it as current.
        texData.SetStatus(ImTextureStatus.OK);

        // Baking a new size writes new pixels and must queue only the changed regions.
        ImFontBaked baked26 = atlas.Fonts[0].GetFontBaked(26.0f);
        baked26.FindGlyph('M');
        Assert.That(texData.Status, Is.EqualTo(ImTextureStatus.WantUpdates));
        Assert.That(texData.Updates.Count, Is.GreaterThan(0));
        foreach (ImTextureRect rect in texData.Updates)
        {
            Assert.That(rect.W, Is.GreaterThan(0));
            Assert.That(rect.H, Is.GreaterThan(0));
            Assert.That(rect.X + rect.W, Is.LessThanOrEqualTo(atlas.TexWidth));
            Assert.That(rect.Y + rect.H, Is.LessThanOrEqualTo(atlas.TexHeight));
        }
    }

    [Test]
    /// <summary>Verifies a texture repack refreshes a bound draw list's UVs without an explicit rebind.</summary>
    public void FontAtlas_Repack_RefreshesBoundDrawListUvs()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();

        // The draw list registers with the atlas when it is constructed.
        ImDrawList drawList = new(atlas);
        int heightBefore = atlas.TexHeight;

        // Force the atlas to grow and repack through lazy large-size bakes; no UpdateAtlas call
        // is made, so the draw list must have been refreshed by the atlas itself.
        ImFont font = atlas.Fonts[0];
        for (int size = 32; size <= 96; size += 16)
        {
            ImFontBaked baked = font.GetFontBaked(size);
            foreach (char c in "HamburgefonstivWXYZ0123")
                baked.FindGlyph(c);
        }

        Assert.That(atlas.TexHeight, Is.GreaterThan(heightBefore), "the atlas must have repacked");

        // Untextured geometry samples the refreshed white texel.
        drawList.Clear();
        drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(10, 10), 0xFFFFFFFF);
        Assert.That(drawList.VtxBuffer[0].UV, Is.EqualTo(atlas.TexUvWhitePixel));

        // Textured lines sample the refreshed line table.
        drawList.Clear();
        drawList.AddLine(new ImVec2(0, 0), new ImVec2(100, 0), 0xFFFFFFFF, 2.0f);
        int lineIdx = (int)((2.0f - 1.0f) * ImFontAtlas.TexLinesSampleCount + 0.5f) + (ImFontAtlas.TexLinesWidthMax + 1);
        ImVec4 line = atlas.TexUvLines[lineIdx];
        Assert.That(drawList.VtxBuffer[0].UV, Is.EqualTo(new ImVec2(line.X, line.Z)));
        Assert.That(drawList.VtxBuffer[1].UV, Is.EqualTo(new ImVec2(line.Y, line.Z)));
    }

    [Test]
    /// <summary>Verifies an idle bake is evicted once the frame threshold passes and its surface is reusable.</summary>
    public void FontAtlas_IdleBake_IsEvictedAndItsRectangleReused()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();

        ImFont font = atlas.Fonts[0];
        ImFontBaked reference = font.GetFontBaked(font.LegacySize);
        ImFontBaked idle = font.GetFontBaked(96.0f);
        BakeBroadGlyphSet(idle);

        // Remember the peak texture the idle bake forced the atlas to reach.
        int peakWidth = atlas.TexWidth;
        int peakHeight = atlas.TexHeight;
        int bakedCountBefore = font.BakedList.Count;
        Assert.That(font.BakedList, Does.Contain(idle));

        // Three frames pass without touching the idle bake, so its LastUsedFrame plus the unused-frame
        // threshold (2) falls behind. The reference size stays in use on every frame.
        for (int frame = 1; frame <= 3; frame++)
        {
            atlas.UpdateNewFrame(frame);
            font.GetFontBaked(font.LegacySize);
        }
        Assert.That(atlas.FrameCount, Is.EqualTo(3));

        atlas.BuildDiscardBakes(2);

        Assert.That(font.BakedList, Does.Not.Contain(idle), "the idle bake must be evicted");
        Assert.That(font.BakedList, Does.Contain(reference), "the in-use bake must survive");
        Assert.That(font.BakedList.Count, Is.EqualTo(bakedCountBefore - 1));
        Assert.That(idle.WantDestroy, Is.True);
        Assert.That(idle.Glyphs, Is.Empty, "the evicted bake must release its glyph output");

        // Compacting drops the discarded rectangles and repacks only the live ones, so the evicted
        // surface must no longer be part of the atlas footprint.
        atlas.CompactCache();
        Assert.That(atlas.TexWidth * atlas.TexHeight, Is.LessThan(peakWidth * peakHeight),
            "the evicted bake's surface must have been returned to the packer");

        // Re-baking the same size reuses the reclaimed surface instead of growing past the old peak.
        ImFontBaked replacement = font.GetFontBaked(96.0f);
        Assert.That(replacement, Is.Not.SameAs(idle), "a fresh set must be created after eviction");
        BakeBroadGlyphSet(replacement);
        Assert.That(atlas.TexWidth * atlas.TexHeight, Is.LessThanOrEqualTo(peakWidth * peakHeight),
            "the reclaimed rectangle must absorb the replacement bake without exceeding the old peak");
    }

    [Test]
    /// <summary>Verifies a bake used every frame and a bake whose font is locked are both retained.</summary>
    public void FontAtlas_ActiveAndLockedBakes_AreRetained()
    {
        // A bake fetched on the latest frame is not idle even though it was created frames ago.
        ImFontAtlas activeAtlas = new();
        activeAtlas.AddFontDefaultBitmap();
        activeAtlas.Build();
        ImFont activeFont = activeAtlas.Fonts[0];
        ImFontBaked active = activeFont.GetFontBaked(36.0f);
        active.FindGlyph('A');

        for (int frame = 1; frame <= 4; frame++)
        {
            activeAtlas.UpdateNewFrame(frame);
            activeFont.GetFontBaked(36.0f).FindGlyph('A');
        }

        activeAtlas.BuildDiscardBakes(2);
        Assert.That(activeFont.BakedList, Does.Contain(active), "a bake used on the latest frame must be retained");
        Assert.That(active.WantDestroy, Is.False);

        // A font flagged LockBakedSizes keeps every baked size, including one never fetched again.
        ImFontAtlas lockedAtlas = new();
        lockedAtlas.AddFontDefaultBitmap();
        lockedAtlas.Build();
        ImFont lockedFont = lockedAtlas.Fonts[0];
        ImFontBaked locked = lockedFont.GetFontBaked(48.0f);
        locked.FindGlyph('B');
        lockedFont.Flags |= ImFontFlags.LockBakedSizes;

        lockedAtlas.UpdateNewFrame(1);
        lockedAtlas.UpdateNewFrame(2);
        lockedAtlas.UpdateNewFrame(3);
        lockedAtlas.BuildDiscardBakes(2);

        Assert.That(lockedFont.BakedList, Does.Contain(locked), "a lock-flagged font must retain its bakes");
        Assert.That(locked.WantDestroy, Is.False);
    }

    [Test]
    /// <summary>Verifies ClearFreeMemory empties every buffer, clears the flags and releases the splitter.</summary>
    public void DrawList_ClearFreeMemory_EmptiesBuffersFlagsAndSplitter()
    {
        ImDrawList drawList = new(_atlas);
        drawList.ChannelsSplit(3);
        drawList.ChannelsSetCurrent(1);
        drawList.AddRectFilled(new ImVec2(0, 0), new ImVec2(20, 20), 0xFFFFFFFF);
        drawList.ChannelsSetCurrent(0);
        drawList.AddLine(new ImVec2(0, 0), new ImVec2(30, 30), 0xFFFFFFFF, 2.0f);
        drawList.AddText(new ImVec2(0, 0), 0xFFFFFFFF, "clear");

        Assert.That(drawList.CmdBuffer, Is.Not.Empty);
        Assert.That(drawList.IdxBuffer, Is.Not.Empty);
        Assert.That(drawList.VtxBuffer, Is.Not.Empty);
        Assert.That(drawList.Splitter.ChannelCount, Is.EqualTo(3));

        drawList.ClearFreeMemory();

        Assert.That(drawList.CmdBuffer, Is.Empty);
        Assert.That(drawList.IdxBuffer, Is.Empty);
        Assert.That(drawList.VtxBuffer, Is.Empty);
        Assert.That(drawList.Flags, Is.EqualTo(ImDrawFlags.None));
        Assert.That(drawList.Splitter.Channels, Is.Empty);
        Assert.That(drawList.Splitter.ChannelCount, Is.EqualTo(1));
        Assert.That(drawList.Splitter.CurrentChannel, Is.EqualTo(0));
    }

    [Test]
    /// <summary>Verifies packing pressure reclaims an idle bake's rectangles instead of growing the texture.</summary>
    public void FontAtlas_PackingPressure_EvictsIdleBakeBeforeGrowing()
    {
        ImFontAtlas atlas = new();
        atlas.AddFontDefaultBitmap();
        atlas.Build();

        ImFont font = atlas.Fonts[0];
        ImFontBaked idle = font.GetFontBaked(72.0f);
        BakeBroadGlyphSet(idle);

        // Age the large bake past the two-frame threshold the packing path uses while keeping the
        // reference size in use.
        for (int frame = 1; frame <= 3; frame++)
        {
            atlas.UpdateNewFrame(frame);
            font.GetFontBaked(font.LegacySize);
        }

        // Forbid growth so the only way to satisfy further requests is to reclaim the idle bake.
        int widthAtCap = atlas.TexWidth;
        int heightAtCap = atlas.TexHeight;
        atlas.TexMaxWidth = widthAtCap;
        atlas.TexMaxHeight = heightAtCap;

        // Fill the remaining free surface until packing pressure returns the idle bake to the packer.
        int packedRects = 0;
        for (int i = 0; i < 200 && font.BakedList.Contains(idle); i++)
        {
            if (atlas.AddCustomRect(96, 96) == -1)
                break;
            packedRects++;
        }

        Assert.That(packedRects, Is.GreaterThan(0), "the fill loop must have placed at least one rectangle");
        Assert.That(font.BakedList, Does.Not.Contain(idle),
            "packing pressure must evict the idle bake instead of growing the texture");
        Assert.That(idle.WantDestroy, Is.True);
        Assert.That(atlas.TexWidth, Is.EqualTo(widthAtCap), "reclaiming must avoid growing past the cap");
        Assert.That(atlas.TexHeight, Is.EqualTo(heightAtCap), "reclaiming must avoid growing past the cap");
    }

    [Test]
    /// <summary>Verifies each added font receives a distinct, increasing identifier from the atlas counter.</summary>
    public void FontIdentity_AddFont_AssignsDistinctIncreasingFontIds()
    {
        ImFontAtlas atlas = new();
        ImFont first = atlas.AddFontDefaultBitmap();
        ImFont second = atlas.AddFontDefaultVector();

        Assert.That(first.FontId, Is.EqualTo(1u));
        Assert.That(second.FontId, Is.EqualTo(2u));
        Assert.That(second.FontId, Is.GreaterThan(first.FontId));
        Assert.That(atlas.FontNextUniqueID, Is.EqualTo(3), "the font counter must advance once per created font");
    }

    [Test]
    /// <summary>Verifies the font's legacy size is captured from the configured source size at add time.</summary>
    public void FontIdentity_LegacySize_CapturesConfiguredSize()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap(new ImFontConfig { SizePixels = 21.5f });

        Assert.That(font.LegacySize, Is.EqualTo(21.5f));
        Assert.That(font.Sources[0].SizePixels, Is.EqualTo(21.5f));
    }

    [Test]
    /// <summary>Verifies a baked identifier is stable across lookups, distinct per size and density, and canonical.</summary>
    public void FontIdentity_BakedIds_AreStableAndDistinctPerSize()
    {
        ImFontAtlas atlas = new();
        ImFont font = atlas.AddFontDefaultBitmap();
        atlas.Build();

        ImFontBaked baked = font.GetFontBaked(13.0f);
        uint identifier = baked.BakedId;
        // Canonical ImFontAtlasBakedGetId hashes (FontId, size, density) through ImHashData, the
        // seed-zero CRC32c hash; font 1 at 13 px and density 1 therefore has this fixed value.
        Assert.That(identifier, Is.EqualTo(0x4E6A80D6u));

        ImFontBaked sameSize = font.GetFontBaked(13.0f);
        Assert.That(sameSize, Is.SameAs(baked), "the same size must resolve to the same bake");
        Assert.That(sameSize.BakedId, Is.EqualTo(identifier), "the identifier must not change while the bake lives");

        ImFontBaked larger = font.GetFontBaked(26.0f);
        Assert.That(larger.BakedId, Is.Not.EqualTo(identifier), "a different size must receive a different identifier");

        ImFontBaked denser = font.GetFontBaked(26.0f, 2.0f);
        Assert.That(denser.BakedId, Is.Not.EqualTo(larger.BakedId), "a different density must receive a different identifier");
        Assert.That(font.GetFontBaked(26.0f, 2.0f).BakedId, Is.EqualTo(denser.BakedId));
    }

    [Test]
    /// <summary>Verifies the atlas counters start at one, advance, and reject mutation while locked.</summary>
    public void FontAtlas_Locked_RejectsFontAddAndBuild()
    {
        ImFontAtlas atlas = new();
        Assert.That(atlas.TexNextUniqueID, Is.EqualTo(1));
        Assert.That(atlas.FontNextUniqueID, Is.EqualTo(1));

        atlas.AddFontDefaultBitmap();
        Assert.That(atlas.FontNextUniqueID, Is.EqualTo(2));

        atlas.Build();
        Assert.That(atlas.TexData!.UniqueID, Is.EqualTo(1), "the first texture must take identifier one");
        Assert.That(atlas.TexNextUniqueID, Is.EqualTo(2), "the texture counter must advance once per created texture");
        Assert.That(atlas.Locked, Is.False, "Build must release the lock before returning");

        // Force the lock the way a frame or rebuild scope would, then prove both mutators reject.
        atlas.Locked = true;
        Assert.Throws<InvalidOperationException>(() => atlas.AddFont(new ImFontConfig()));
        Assert.Throws<InvalidOperationException>(() => atlas.Build());
        Assert.That(atlas.Fonts.Count, Is.EqualTo(1), "a rejected add must not change the font list");
        atlas.Locked = false;
    }

    [Test]
    /// <summary>Verifies AddChar and GetBit round-trip a code point through the packed bit array.</summary>
    public void ImFontGlyphRangesBuilder_AddCharAndGetBit_RoundTrip()
    {
        ImFontGlyphRangesBuilder builder = new();

        // One word per 32 code points, bit n at 1u << (n & 31): the canonical packing at
        Assert.That(builder.UsedChars.Length, Is.EqualTo(34816));
        Assert.That(builder.GetBit('A'), Is.False);

        builder.AddChar('A');
        Assert.That(builder.GetBit('A'), Is.True);
        Assert.That(builder.GetBit('B'), Is.False);
        Assert.That(builder.UsedChars['A' >> 5], Is.EqualTo(1u << ('A' & 31)));

        // A code point on a different word must not alias the first one.
        builder.AddChar(0x1F600);
        Assert.That(builder.GetBit(0x1F600), Is.True);
        Assert.That(builder.GetBit(0x1F601), Is.False);

        builder.Clear();
        Assert.That(builder.GetBit('A'), Is.False);
        Assert.That(builder.GetBit(0x1F600), Is.False);
    }

    [Test]
    /// <summary>Verifies AddText records ASCII, a Latin-1 code point, and a surrogate pair.</summary>
    public void ImFontGlyphRangesBuilder_AddText_HandlesAsciiLatin1AndSurrogatePairs()
    {
        ImFontGlyphRangesBuilder builder = new();

        // U+00E9 is Latin-1 Supplement; U+1F600 is astral and therefore a UTF-16 surrogate pair.
        builder.AddText("A\u00E9\U0001F600");

        Assert.That(builder.GetBit('A'), Is.True);
        Assert.That(builder.GetBit(0x00E9), Is.True);
        Assert.That(builder.GetBit(0x1F600), Is.True);

        // The two surrogate halves must be combined into the astral code point, not recorded alone.
        Assert.That(builder.GetBit(0xD83D), Is.False);
        Assert.That(builder.GetBit(0xDE00), Is.False);
    }

    [Test]
    /// <summary>Verifies AddRanges records a multi-range list and clamps to the maximum code point.</summary>
    public void ImFontGlyphRangesBuilder_AddRanges_AddsMultipleRangesAndClamps()
    {
        ImFontGlyphRangesBuilder builder = new();

        builder.AddRanges(new uint[] { 'A', 'C', 'x', 'z', 0 });

        Assert.That(builder.GetBit('A'), Is.True);
        Assert.That(builder.GetBit('B'), Is.True);
        Assert.That(builder.GetBit('C'), Is.True);
        Assert.That(builder.GetBit('D'), Is.False);
        Assert.That(builder.GetBit('x'), Is.True);
        Assert.That(builder.GetBit('z'), Is.True);
        Assert.That(builder.GetBit('w'), Is.False);

        // A range crossing IM_UNICODE_CODEPOINT_MAX is truncated, not rejected
        builder.AddRanges(new uint[] { 0x10FFF0, 0x11FFFF, 0 });
        Assert.That(builder.GetBit(0x10FFF0), Is.True);
        Assert.That(builder.GetBit(0x10FFFF), Is.True);
    }

    [Test]
    /// <summary>Verifies BuildRanges coalesces adjacent code points and terminates with zero.</summary>
    public void ImFontGlyphRangesBuilder_BuildRanges_CoalescesRunsAndTerminates()
    {
        ImFontGlyphRangesBuilder builder = new();
        builder.AddChar('A');
        builder.AddChar('B');
        builder.AddChar('C');
        builder.AddChar('E');

        // "A.C" and "E.E" are two runs, each an inclusive start/end pair, then the zero terminator
        Assert.That(builder.BuildRanges(), Is.EqualTo(new uint[] { 'A', 'C', 'E', 'E', 0 }));
    }

    [Test]
    /// <summary>Verifies the canonical default glyph range round-trips through the builder unchanged.</summary>
    public void ImFontGlyphRangesBuilder_BuildRanges_DefaultRangeRoundTrips()
    {
        ImFontGlyphRangesBuilder builder = new();
        ImFontAtlas atlas = new();
        builder.AddRanges(atlas.GetGlyphRangesDefault());

        Assert.That(builder.BuildRanges(), Is.EqualTo(atlas.GetGlyphRangesDefault()));
    }

    /// <summary>Bakes a broad ASCII glyph set so a test bake occupies a measurable surface.</summary>
    /// <param name="baked">Baked set to populate.</param>
    private static void BakeBroadGlyphSet(ImFontBaked baked)
    {
        const string glyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        foreach (char c in glyphs)
            baked.FindGlyph(c);
    }

    #endregion
}
