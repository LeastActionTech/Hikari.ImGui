#nullable enable
using System;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the canonical drag-and-drop subsystem in <see cref="ImGui"/>, covering drag-source
/// activation and payload latching, payload submission conditions, target submission, payload
/// acceptance with preview/delivery transitions, the default drop-target highlight, and the docking
/// window payload path.
/// </summary>
/// <remarks>
/// <para>
/// Canonical reference: Dear ImGui <c>v1.92.9b-docking</c> at
/// <c>ThirdParty/imgui/imgui.cpp:15718-16110</c> for the state machine and
/// <c>ThirdParty/imgui/imgui.h:2856-2878</c> for <c>ImGuiPayload</c>.
/// </para>
/// <para>
/// Every test drives a real <see cref="ImGuiContext"/> with queued mouse position and button events.
/// A drag is started by hovering the source item, pressing the left button, and then moving the
/// pointer past the drag threshold while the button is held; the source block then runs on every
/// subsequent frame until the button is released.
/// </para>
/// </remarks>
[TestFixture]
public class DragDropTests : ImGuiTestFixture
{
    /// <summary>Window origin shared by every fixed-geometry window in this fixture.</summary>
    private static readonly ImVec2 WindowOrigin = new(100.0f, 100.0f);

    /// <summary>Source button size; large enough that a 40-pixel drag stays inside its bounds.</summary>
    private static readonly ImVec2 SourceSize = new(120.0f, 40.0f);

    /// <summary>Target button size, submitted below the source button.</summary>
    private static readonly ImVec2 TargetSize = new(120.0f, 40.0f);

    /// <summary>Payload type used by the source/target lifecycle tests.</summary>
    private const string PayloadTypeAccept = "DND_ACCEPT";


    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigInputTrickleEventQueue = false;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
    }


    /// <summary>
    /// Runs one frame that begins a fixed-geometry window and invokes <paramref name="body"/> while
    /// that window is current.
    /// </summary>
    /// <param name="body">The item submissions and assertions to run while the window is current.</param>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    private static void WindowFrame(Action body, float width = 600.0f, float height = 400.0f)
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(WindowOrigin, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(width, height), ImGuiCond.Always);
        ImGui.Begin("Window");
        body();
        ImGui.End();
        ImGui.Render();
    }

    /// <summary>Runs one frame with an empty fixed-geometry window so window identity and input state settle.</summary>
    private static void WarmupFrame()
    {
        WindowFrame(() => { });
    }

    /// <summary>Returns the center of the last submitted item in absolute screen coordinates.</summary>
    /// <returns>The item rectangle center.</returns>
    private static ImVec2 LastItemCenter()
    {
        return (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
    }

    /// <summary>
    /// Hovers <paramref name="sourceCenter"/>, presses the left button, then moves 40 pixels to the
    /// right so the source item is active and past the drag threshold. The source block inside
    /// <paramref name="body"/> therefore runs on the final frame.
    /// </summary>
    /// <param name="body">Frame body submitting the source and target items.</param>
    /// <param name="sourceCenter">Source item center captured by an earlier layout frame.</param>
    private void StartDrag(Action body, ImVec2 sourceCenter)
    {
        _ctx.IO.AddMousePosEvent(sourceCenter.X, sourceCenter.Y);
        WindowFrame(body);
        _ctx.IO.AddMouseButtonEvent(0, true);
        WindowFrame(body);
        _ctx.IO.AddMousePosEvent(sourceCenter.X + 40.0f, sourceCenter.Y);
        WindowFrame(body);
    }

    /// <summary>
    /// Moves the pointer onto <paramref name="targetCenter"/> while the drag continues, which makes
    /// the target item publish <c>ImGuiItemStatusFlags_HoveredRect</c> and accept the payload.
    /// </summary>
    /// <param name="body">Frame body submitting the source and target items.</param>
    /// <param name="targetCenter">Target item center captured by an earlier layout frame.</param>
    private void MoveToTarget(Action body, ImVec2 targetCenter)
    {
        _ctx.IO.AddMousePosEvent(targetCenter.X, targetCenter.Y);
        WindowFrame(body);
    }

    #region Source lifecycle

    /// <summary>
    /// Verifies that a drag source latches its active item identifier, managed payload object, source
    /// flags, mouse button, and payload frame count. Canonical <c>BeginDragDropSource</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:15758-15875</c>) and the managed payload path.
    /// </summary>
    [Test]
    public void BeginDragDropSource_LatchesSourceAndManagedPayload()
    {
        WarmupFrame();

        uint sourceId = 0;
        uint sourceParentId = 0;
        ImVec2 sourceCenter = default;
        bool sourceActive = false;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceId = ImGui.GetItemID();
            sourceParentId = _ctx.CurrentWindow!.ID;
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                sourceActive = true;
                ImGui.SetDragDropPayload("DND_LATCH", 42);
                ImGui.EndDragDropSource();
            }
        }

        // Capture item geometry on a neutral frame before any pointer interaction.
        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        Assert.That(sourceActive, Is.True, "The source block must run once the pointer is dragging.");
        Assert.That(_ctx.DragDropActive, Is.True);
        Assert.That(_ctx.DragDropWithinSource, Is.False, "EndDragDropSource() must close the source scope.");
        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo("DND_LATCH"));
        Assert.That(_ctx.DragDropPayload.SourceId, Is.EqualTo(sourceId));
        Assert.That(_ctx.DragDropPayload.SourceParentId, Is.EqualTo(sourceParentId));
        Assert.That(_ctx.DragDropPayload.ObjectData, Is.EqualTo(42), "The managed overload stores the object reference.");
        Assert.That(_ctx.DragDropPayload.Data, Is.EqualTo(IntPtr.Zero), "The managed overload clears the raw pointer.");
        Assert.That(_ctx.DragDropPayload.DataSize, Is.EqualTo(0));
        Assert.That(_ctx.DragDropPayload.DataFrameCount, Is.EqualTo(_ctx.FrameCount));
        Assert.That(_ctx.DragDropSourceFlags, Is.EqualTo(ImGuiDragDropFlags.SourceNoPreviewTooltip));
        Assert.That(_ctx.DragDropMouseButton, Is.EqualTo(0), "The drag is carried by the left button that activated the item.");
        Assert.That(ImGui.GetDragDropPayload()!.DataType, Is.EqualTo("DND_LATCH"));
    }

    /// <summary>
    /// Verifies that the default source flags open a drag-and-drop preview tooltip named with the
    /// canonical <c>##Tooltip_DragDrop_</c> prefix, and that <c>EndDragDropSource()</c> closes it.
    /// Canonical <c>BeginDragDropSource</c> (<c>ThirdParty/imgui/imgui.cpp:15858-15869</c>).
    /// </summary>
    [Test]
    public void BeginDragDropSource_DefaultFlagsOpenDragDropPreviewTooltip()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        string? previewWindowName = null;
        bool sourceActive = false;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                sourceActive = true;
                previewWindowName = _ctx.CurrentWindow?.Name;
                ImGui.SetDragDropPayload("DND_PREVIEW", 1);
                ImGui.EndDragDropSource();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        Assert.That(sourceActive, Is.True);
        Assert.That(previewWindowName, Is.Not.Null);
        Assert.That(previewWindowName, Does.StartWith("##Tooltip_DragDrop"),
            "A source without SourceNoPreviewTooltip opens the drag-and-drop preview tooltip.");
        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo("DND_PREVIEW"));
    }

    #endregion

    #region Payload submission

    /// <summary>
    /// Verifies that <see cref="ImGuiCond.Once"/> copies the payload fields only on the frame the drag
    /// starts, while the payload frame count still advances every frame, and that
    /// <see cref="ImGuiCond.Always"/> overwrites them. Canonical <c>SetDragDropPayload</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:15906-15931</c>).
    /// </summary>
    [Test]
    public void SetDragDropPayload_OnceDoesNotOverwriteAfterFirstFrame()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        string typeToSubmit = "DND_FIRST";
        ImGuiCond condToSubmit = ImGuiCond.Once;
        int firstFrameCount = -1;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload(typeToSubmit, IntPtr.Zero, 0, condToSubmit);
                ImGui.EndDragDropSource();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo("DND_FIRST"));
        firstFrameCount = _ctx.DragDropPayload.DataFrameCount;

        // A second dragging frame with cond Once must not overwrite the latched payload fields.
        typeToSubmit = "DND_SECOND";
        WindowFrame(Body);

        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo("DND_FIRST"),
            "ImGuiCond.Once must keep the first frame's payload type.");
        Assert.That(_ctx.DragDropPayload.DataFrameCount, Is.EqualTo(_ctx.FrameCount),
            "The payload frame count advances even when the data is not copied.");
        Assert.That(_ctx.DragDropPayload.DataFrameCount, Is.GreaterThan(firstFrameCount));

        // ImGuiCond.Always refreshes the payload on the same drag.
        condToSubmit = ImGuiCond.Always;
        typeToSubmit = "DND_THIRD";
        WindowFrame(Body);

        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo("DND_THIRD"));
    }

    #endregion

    #region Target acceptance

    /// <summary>
    /// Verifies that <c>AcceptDragDropPayload()</c> rejects a mismatched payload type and, with
    /// <see cref="ImGuiDragDropFlags.AcceptBeforeDelivery"/>, returns the payload before delivery with
    /// <c>Preview</c> set only from the second hovered frame onward, then reports delivery once the
    /// button is released. Canonical <c>AcceptDragDropPayload</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:16027-16078</c>).
    /// </summary>
    [Test]
    public void AcceptDragDropPayload_RejectsWrongTypeAndHonorsAcceptBeforeDelivery()
    {
        WarmupFrame();

        uint sourceId = 0;
        uint targetId = 0;
        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        bool wrongTypeAccepted = true;
        bool firstHoverAccepted = false;
        bool firstHoverPreview = true;
        bool firstHoverDelivery = true;
        bool secondHoverAccepted = false;
        bool secondHoverPreview = false;
        bool secondHoverDelivery = true;
        bool releaseAccepted = false;
        bool releaseDelivery = false;
        int phase = 0;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceId = ImGui.GetItemID();
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload(PayloadTypeAccept, 7);
                ImGui.EndDragDropSource();
            }

            ImGui.Button("target", TargetSize);
            targetId = ImGui.GetItemID();
            targetCenter = LastItemCenter();
            if (ImGui.BeginDragDropTarget())
            {
                if (phase == 3)
                {
                    wrongTypeAccepted = ImGui.AcceptDragDropPayload("DND_OTHER") != null;
                    ImGuiPayload? payload = ImGui.AcceptDragDropPayload(
                        PayloadTypeAccept, ImGuiDragDropFlags.AcceptBeforeDelivery);
                    firstHoverAccepted = payload != null;
                    firstHoverPreview = payload?.Preview ?? true;
                    firstHoverDelivery = payload?.Delivery ?? true;
                }
                else if (phase == 4)
                {
                    ImGuiPayload? payload = ImGui.AcceptDragDropPayload(
                        PayloadTypeAccept, ImGuiDragDropFlags.AcceptBeforeDelivery);
                    secondHoverAccepted = payload != null;
                    secondHoverPreview = payload?.Preview ?? false;
                    secondHoverDelivery = payload?.Delivery ?? true;
                }
                else if (phase == 5)
                {
                    ImGuiPayload? payload = ImGui.AcceptDragDropPayload(
                        PayloadTypeAccept, ImGuiDragDropFlags.AcceptBeforeDelivery);
                    releaseAccepted = payload != null;
                    releaseDelivery = payload?.Delivery ?? false;
                }
                ImGui.EndDragDropTarget();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        phase = 3;
        MoveToTarget(Body, targetCenter);

        Assert.That(_ctx.DragDropPayload.SourceId, Is.EqualTo(sourceId));
        Assert.That(_ctx.DragDropTargetId, Is.EqualTo(targetId), "The target identifier must be the hovered item.");

        phase = 4;
        WindowFrame(Body);

        phase = 5;
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(Body);

        Assert.That(wrongTypeAccepted, Is.False, "A mismatched payload type must be rejected.");
        Assert.That(firstHoverAccepted, Is.True, "AcceptBeforeDelivery returns the payload while it is only previewed.");
        Assert.That(firstHoverPreview, Is.False, "Preview requires the target to have accepted on the previous frame.");
        Assert.That(firstHoverDelivery, Is.False);
        Assert.That(secondHoverAccepted, Is.True);
        Assert.That(secondHoverPreview, Is.True, "The second hovered frame is the previously accepted frame.");
        Assert.That(secondHoverDelivery, Is.False);
        Assert.That(releaseAccepted, Is.True);
        Assert.That(releaseDelivery, Is.True, "Releasing the source mouse button delivers the payload.");
        Assert.That(_ctx.DragDropActive, Is.False, "EndDragDropTarget() clears the drag after delivery.");
        Assert.That(ImGui.GetDragDropPayload(), Is.Null);
    }

    /// <summary>
    /// Verifies that without <see cref="ImGuiDragDropFlags.AcceptBeforeDelivery"/> the payload is not
    /// returned while it is merely previewed, and is returned once the button is released. Canonical
    /// <c>AcceptDragDropPayload</c> (<c>ThirdParty/imgui/imgui.cpp:16070-16077</c>).
    /// </summary>
    [Test]
    public void AcceptDragDropPayload_WithoutBeforeDeliveryReturnsNullUntilRelease()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        bool previewFrameAccepted = true;
        bool releaseFrameAccepted = false;
        bool releaseFrameDelivery = false;
        int phase = 0;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload(PayloadTypeAccept, 11);
                ImGui.EndDragDropSource();
            }

            ImGui.Button("target", TargetSize);
            targetCenter = LastItemCenter();
            if (ImGui.BeginDragDropTarget())
            {
                ImGuiPayload? payload = ImGui.AcceptDragDropPayload(PayloadTypeAccept);
                if (phase == 3)
                    previewFrameAccepted = payload != null;
                else if (phase == 5)
                {
                    releaseFrameAccepted = payload != null;
                    releaseFrameDelivery = payload?.Delivery ?? false;
                }
                ImGui.EndDragDropTarget();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);
        phase = 3;
        MoveToTarget(Body, targetCenter);

        Assert.That(previewFrameAccepted, Is.False,
            "Without AcceptBeforeDelivery a previewed payload is not returned.");

        phase = 5;
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(Body);

        Assert.That(releaseFrameAccepted, Is.True);
        Assert.That(releaseFrameDelivery, Is.True);
    }

    /// <summary>
    /// Verifies that <c>BeginDragDropTarget()</c> requires an active drag and a hovered last item, and
    /// that during a drag it publishes the hovered item's identifier and clip rectangle. Canonical
    /// <c>BeginDragDropTarget</c> (<c>ThirdParty/imgui/imgui.cpp:15990-16025</c>).
    /// </summary>
    [Test]
    public void BeginDragDropTarget_RequiresActiveDragAndHoveredItem()
    {
        WarmupFrame();

        bool targetWithoutDrag = true;
        uint targetId = 0;
        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        bool targetDuringDrag = false;
        uint publishedTargetId = 0;
        ImRect publishedClipRect = default;
        ImRect expectedClipRect = default;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload(PayloadTypeAccept, 3);
                ImGui.EndDragDropSource();
            }

            ImGui.Button("target", TargetSize);
            targetId = ImGui.GetItemID();
            expectedClipRect = _ctx.CurrentWindow!.ClipRect;
            targetCenter = LastItemCenter();
            targetDuringDrag = ImGui.BeginDragDropTarget();
            if (targetDuringDrag)
            {
                publishedTargetId = _ctx.DragDropTargetId;
                publishedClipRect = _ctx.DragDropTargetClipRect;
                ImGui.EndDragDropTarget();
            }
        }

        // A frame with no drag active must decline the target request.
        WindowFrame(() =>
        {
            ImGui.Button("target", TargetSize);
            targetWithoutDrag = ImGui.BeginDragDropTarget();
        });

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);
        MoveToTarget(Body, targetCenter);

        Assert.That(targetWithoutDrag, Is.False, "BeginDragDropTarget() requires an active drag.");
        Assert.That(targetDuringDrag, Is.True, "The hovered target accepts the drag.");
        Assert.That(publishedTargetId, Is.EqualTo(targetId));
        Assert.That(publishedClipRect, Is.EqualTo(expectedClipRect));
        Assert.That(_ctx.DragDropTargetFullViewport, Is.EqualTo(0u), "Item targets are not viewport-wide.");
    }

    /// <summary>
    /// Verifies that <c>BeginDragDropTargetCustom()</c> accepts an explicit rectangle for a distinct
    /// identifier and rejects the drag source's own identifier. Canonical
    /// <c>BeginDragDropTargetCustom</c> (<c>ThirdParty/imgui/imgui.cpp:15946-15963</c>).
    /// </summary>
    [Test]
    public void BeginDragDropTargetCustom_MapsRectangleAndRejectsSourceIdentifier()
    {
        WarmupFrame();

        uint sourceId = 0;
        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        bool customAccepted = false;
        bool customSourceRejected = true;
        uint customTargetId = 0;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceId = ImGui.GetItemID();
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload(PayloadTypeAccept, 5);
                ImGui.EndDragDropSource();
            }

            ImGui.Button("target", TargetSize);
            targetCenter = LastItemCenter();
            ImRect rect = new(targetCenter - new ImVec2(8.0f, 8.0f), targetCenter + new ImVec2(8.0f, 8.0f));
            customAccepted = ImGui.BeginDragDropTargetCustom(rect, 0xC0FFEEu);
            if (customAccepted)
            {
                customTargetId = _ctx.DragDropTargetId;
                ImGui.EndDragDropTarget();
            }
            customSourceRejected = !ImGui.BeginDragDropTargetCustom(rect, sourceId);
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);
        MoveToTarget(Body, targetCenter);

        Assert.That(customAccepted, Is.True);
        Assert.That(customTargetId, Is.EqualTo(0xC0FFEEu));
        Assert.That(customSourceRejected, Is.True, "A target cannot use the drag source's identifier.");
    }

    #endregion

    #region Docking payload

    /// <summary>
    /// Verifies that the docking payload path still works: the managed overload submits
    /// <c>IMGUI_PAYLOAD_TYPE_WINDOW</c> with the dragged window as its object, and a target accepts it
    /// by that type while rejecting other types. Canonical <c>BeginDockableDragDropSource</c> and
    /// <c>BeginDockableDragDropTarget</c> (<c>ThirdParty/imgui/imgui.cpp:21566-21703</c>).
    /// </summary>
    [Test]
    public void DockingWindowPayload_FlowsThroughManagedOverload()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        ImGuiWindow? payloadWindow = null;
        bool wrongTypeRejected = true;
        bool windowPayloadAccepted = false;
        object? acceptedWindow = null;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                payloadWindow = _ctx.CurrentWindow;
                ImGui.SetDragDropPayload(ImGui.IMGUI_PAYLOAD_TYPE_WINDOW, payloadWindow!);
                ImGui.EndDragDropSource();
            }

            ImGui.Button("target", TargetSize);
            targetCenter = LastItemCenter();
            if (ImGui.BeginDragDropTarget())
            {
                wrongTypeRejected = ImGui.AcceptDragDropPayload("DND_OTHER") == null;
                ImGuiPayload? payload = ImGui.AcceptDragDropPayload(
                    ImGui.IMGUI_PAYLOAD_TYPE_WINDOW,
                    ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
                windowPayloadAccepted = payload != null;
                acceptedWindow = payload?.ObjectData;
                ImGui.EndDragDropTarget();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);
        MoveToTarget(Body, targetCenter);

        Assert.That(payloadWindow, Is.Not.Null);
        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo(ImGui.IMGUI_PAYLOAD_TYPE_WINDOW));
        Assert.That(wrongTypeRejected, Is.True);
        Assert.That(windowPayloadAccepted, Is.True);
        Assert.That(acceptedWindow, Is.SameAs(payloadWindow),
            "The docking payload carries the dragged window object reference.");
    }

    #endregion

    #region Source teardown

    /// <summary>
    /// Verifies that <c>EndDragDropSource()</c> discards a drag whose source never submitted a
    /// payload, so no empty payload survives into the target pass. Canonical
    /// <c>EndDragDropSource</c> (<c>ThirdParty/imgui/imgui.cpp:15886-15889</c>).
    /// </summary>
    [Test]
    public void EndDragDropSource_WithoutPayloadDiscardsDrag()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        bool sourceActive = false;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                sourceActive = true;
                // Deliberately no SetDragDropPayload().
                ImGui.EndDragDropSource();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        Assert.That(sourceActive, Is.True);
        Assert.That(_ctx.DragDropActive, Is.False, "A source without a payload discards the drag.");
        Assert.That(_ctx.DragDropPayload.DataFrameCount, Is.EqualTo(-1));
        Assert.That(_ctx.DragDropPayload.DataType, Is.Empty);
        Assert.That(ImGui.GetDragDropPayload(), Is.Null);
    }

    #endregion

    #region Color payloads

    /// <summary>
    /// Verifies that <c>ColorButton()</c> publishes the canonical four-component color payload while
    /// it is dragged and that <c>ColorEdit3()</c> accepts that payload and copies the red, green, and
    /// blue components into its caller buffer. Canonical <c>ColorButton</c>
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6562-6574</c>) and the <c>ColorEdit4</c> drag target
    /// (<c>ThirdParty/imgui/imgui_widgets.cpp:6066-6086</c>).
    /// </summary>
    [Test]
    public void ColorButton_ToColorEdit3_RoundTripsColorPayload()
    {
        WarmupFrame();

        float[] targetColor = { 0.0f, 0.0f, 0.0f };
        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        string? draggedPayloadType = null;
        float[]? draggedPayloadValues = null;

        void Body()
        {
            ImGui.ColorButton("sourceColor", new ImVec4(0.25f, 0.5f, 0.75f, 1.0f), ImGuiColorEditFlags.None, new ImVec2(120.0f, 40.0f));
            sourceCenter = LastItemCenter();

            ImGui.ColorEdit3("targetColor", targetColor);
            targetCenter = LastItemCenter();
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        // The source has published its color payload while the drag is live.
        draggedPayloadType = _ctx.DragDropPayload.DataType;
        draggedPayloadValues = _ctx.DragDropPayload.ObjectData as float[];

        MoveToTarget(Body, targetCenter);
        _ctx.IO.AddMouseButtonEvent(0, false);
        WindowFrame(Body);

        Assert.That(draggedPayloadType, Is.EqualTo(ImGui.IMGUI_PAYLOAD_TYPE_COLOR_4F),
            "A ColorButton without NoAlpha publishes the four-component color payload.");
        Assert.That(draggedPayloadValues, Is.Not.Null);
        Assert.That(draggedPayloadValues!.Length, Is.EqualTo(4));
        Assert.That(draggedPayloadValues[0], Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(draggedPayloadValues[1], Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(draggedPayloadValues[2], Is.EqualTo(0.75f).Within(1e-5f));

        Assert.That(targetColor[0], Is.EqualTo(0.25f).Within(1e-5f), "The drop copies red into the target.");
        Assert.That(targetColor[1], Is.EqualTo(0.5f).Within(1e-5f), "The drop copies green into the target.");
        Assert.That(targetColor[2], Is.EqualTo(0.75f).Within(1e-5f), "The drop copies blue into the target.");
    }

    #endregion

    #region Wildcard and managed conditions

    /// <summary>
    /// Verifies that <c>AcceptDragDropPayload(null)</c> accepts any payload type, matching the
    /// canonical null-tag wildcard (<c>ThirdParty/imgui/imgui.cpp:16033</c>).
    /// </summary>
    [Test]
    public void AcceptDragDropPayload_NullTypeAcceptsAnyPayload()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        ImVec2 targetCenter = default;
        bool acceptedWithNull = false;
        string? acceptedType = null;
        int? acceptedValue = null;
        int phase = 0;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload("DND_ANY_TYPE", 99);
                ImGui.EndDragDropSource();
            }

            ImGui.Button("target", TargetSize);
            targetCenter = LastItemCenter();
            if (ImGui.BeginDragDropTarget())
            {
                if (phase >= 3)
                {
                    ImGuiPayload? payload = ImGui.AcceptDragDropPayload(null, ImGuiDragDropFlags.AcceptBeforeDelivery);
                    if (payload != null)
                    {
                        acceptedWithNull = true;
                        acceptedType = payload.DataType;
                        acceptedValue = (int?)payload.ObjectData;
                    }
                }
                ImGui.EndDragDropTarget();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);
        phase = 3;
        MoveToTarget(Body, targetCenter);

        Assert.That(acceptedWithNull, Is.True, "A null type tag must accept any payload.");
        Assert.That(acceptedType, Is.EqualTo("DND_ANY_TYPE"));
        Assert.That(acceptedValue, Is.EqualTo(99));
    }

    /// <summary>
    /// Verifies that the managed <c>SetDragDropPayload(string, object, ImGuiCond)</c> overload with
    /// <see cref="ImGuiCond.Once"/> latches the first object reference for the whole drag while the
    /// payload frame count advances, that <see cref="ImGuiCond.Always"/> refreshes it, and that an
    /// exactly 32-character type tag is accepted rather than rejected.
    /// </summary>
    [Test]
    public void SetDragDropPayload_ManagedOnceLatchesObjectReference()
    {
        WarmupFrame();

        ImVec2 sourceCenter = default;
        string typeToSubmit = new('T', 32);
        object objectToSubmit = 111;
        ImGuiCond condToSubmit = ImGuiCond.Once;
        int firstFrameCount = -1;

        void Body()
        {
            ImGui.Button("source", SourceSize);
            sourceCenter = LastItemCenter();
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
            {
                ImGui.SetDragDropPayload(typeToSubmit, objectToSubmit, condToSubmit);
                ImGui.EndDragDropSource();
            }
        }

        WindowFrame(Body);
        StartDrag(Body, sourceCenter);

        Assert.That(_ctx.DragDropPayload.DataType, Is.EqualTo(typeToSubmit),
            "A 32-character payload type is at the canonical boundary and must be preserved.");
        Assert.That(_ctx.DragDropPayload.ObjectData, Is.EqualTo(111));
        firstFrameCount = _ctx.DragDropPayload.DataFrameCount;

        objectToSubmit = 222;
        WindowFrame(Body);

        Assert.That(_ctx.DragDropPayload.ObjectData, Is.EqualTo(111),
            "ImGuiCond.Once must keep the first object reference.");
        Assert.That(_ctx.DragDropPayload.DataFrameCount, Is.EqualTo(_ctx.FrameCount),
            "The payload frame count advances even when the object is not refreshed.");
        Assert.That(_ctx.DragDropPayload.DataFrameCount, Is.GreaterThan(firstFrameCount));

        condToSubmit = ImGuiCond.Always;
        WindowFrame(Body);

        Assert.That(_ctx.DragDropPayload.ObjectData, Is.EqualTo(222),
            "ImGuiCond.Always refreshes the object reference on the same drag.");
    }

    #endregion
}
