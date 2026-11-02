#nullable enable
/*================================================================================================
 * DEAR IMGUI TYPES, STYLE, AND INPUT/OUTPUT STATE
 *================================================================================================
 * ROLE IN ENGINE:
 * Defines the canonical value types, enums, and shared configuration structures used by the
 * Hikari.ImGui port of Dear ImGui v1.92.9b-docking. ImGuiStyle holds the runtime metric and color
 * palette that the widget layer reads while drawing, and ImGuiIO carries host configuration, queued
 * input, and per-frame output flags that the host backend writes and the widget layer consumes.
 *
 * SUBSYSTEM CONNECTIONS & DATA FLOW:
 *
 *    ┌────────────────────────────────────┐
 *    │ Host backend (Hikari.ImGui.Demo)   │
 *    │ • submits Add* input events        │
 *    │ • writes DisplaySize and DeltaTime │
 *    └────────────────────────────────────┘
 *                                  │ Add* events and configuration
 *                                  ▼
 *    ┌───────────────────────────────────────────────┐
 *    │ ImGuiIO                                       │
 *    │ • KeysData[ImGuiKey.NamedKey_COUNT] key state │
 *    │ • mouse state, character queue, output flags  │
 *    └───────────────────────────────────────────────┘
 *                                  │ per-frame input snapshot
 *                                  ▼
 *    ┌───────────────────────────────────────────────┐
 *    │ ImGui context and widget layer (later chunks) │
 *    │ • reads ImGuiStyle metrics and Colors[]       │
 *    └───────────────────────────────────────────────┘
 *
 *   ImGuiStyle is constructed once by the host and read by the widget layer:
 *
 *    ┌────────────────────────────────────────────────────────┐
 *    │ ImGuiStyle                                             │
 *    │ • metric fields, ScaleAllSizes, canonical dark palette │
 *    └────────────────────────────────────────────────────────┘
 *
 * STYLIZED USAGE EXAMPLE:
 *
 *   var io = new ImGuiIO();
 *   io.DisplaySize = new ImVec2(1920.0f, 1080.0f);
 *   io.AddKeyEvent(ImGuiKey.Space, true);
 *   io.AddInputCharacter('h');
 *
 *   var style = new ImGuiStyle();
 *   style.ScaleAllSizes(1.5f);
 *   ImVec4 accent = style.Colors[(int)ImGuiCol.ButtonActive];
 *
 * THREADING & LIFECYCLE NOTES:
 * - ImGuiIO and ImGuiStyle are plain single-threaded structures. The host backend mutates them on
 *   the main application thread before a frame is submitted, and the widget layer reads them during
 *   frame submission. No internal synchronization is provided.
 *================================================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hikari.ImGui;

#region Canonical Enums (88 Enums from imgui.h and imgui_internal.h)

/// <summary>
/// Specifies flags that control how draw list primitives round their corners and close their paths.
/// </summary>
[Flags]
public enum ImDrawFlags
{
    None = 0,
    ImDrawFlags_None = None,

    // Rounding for AddRectXXX(), PathRect()
    RoundTopLeft = 0x10,
    ImDrawFlags_RoundTopLeft = RoundTopLeft,
    RoundTopRight = 0x20,
    ImDrawFlags_RoundTopRight = RoundTopRight,
    RoundBottomLeft = 0x40,
    ImDrawFlags_RoundBottomLeft = RoundBottomLeft,
    RoundBottomRight = 0x80,
    ImDrawFlags_RoundBottomRight = RoundBottomRight,
    RoundNone = 0x100,
    ImDrawFlags_RoundNone = RoundNone,
    RoundAll = RoundTopLeft | RoundTopRight | RoundBottomLeft | RoundBottomRight,
    ImDrawFlags_RoundAll = RoundAll,
    RoundTop = RoundTopLeft | RoundTopRight,
    ImDrawFlags_RoundTop = RoundTop,
    RoundBottom = RoundBottomLeft | RoundBottomRight,
    ImDrawFlags_RoundBottom = RoundBottom,
    RoundLeft = RoundBottomLeft | RoundTopLeft,
    ImDrawFlags_RoundLeft = RoundLeft,
    RoundRight = RoundBottomRight | RoundTopRight,
    ImDrawFlags_RoundRight = RoundRight,

    // Stroke Options
    Closed = 0x200,
    ImDrawFlags_Closed = Closed,
    JoinMiter = 0x400,
    ImDrawFlags_JoinMiter = JoinMiter,
    CapSquare = 0x800,
    ImDrawFlags_CapSquare = CapSquare,

    // Stroke/Fill Anti-aliasing
    AAFill = 0x1000,
    ImDrawFlags_AAFill = AAFill,
    AALines = 0x2000,
    ImDrawFlags_AALines = AALines,
    AALineEnds = 0x4000,
    ImDrawFlags_AALineEnds = AALineEnds,

    // Stroke Position relative to shape outline
    StrokeInside = 0x20000,
    ImDrawFlags_StrokeInside = StrokeInside,
    StrokeCenter = 0x40000,
    ImDrawFlags_StrokeCenter = StrokeCenter,
    StrokeCenterBiased = 0x60000,
    ImDrawFlags_StrokeCenterBiased = StrokeCenterBiased,
    StrokeOutside = 0x80000,
    ImDrawFlags_StrokeOutside = StrokeOutside,
    StrokeLegacy = 0xE0000,
    ImDrawFlags_StrokeLegacy = StrokeLegacy,

    // Other Options
    TextNoPixelSnap = 0x100000,
    ImDrawFlags_TextNoPixelSnap = TextNoPixelSnap,
    UseTexForRoundCorners = 0x200000,
    ImDrawFlags_UseTexForRoundCorners = UseTexForRoundCorners,
    UseVtxOffset = 0x400000,
    ImDrawFlags_UseVtxOffset = UseVtxOffset,
    AllowTexForRoundCorners_ = 0x800000,
    ImDrawFlags_AllowTexForRoundCorners_ = AllowTexForRoundCorners_,

    // Internal bitmasks
    RoundMask_ = RoundAll | RoundNone,
    ImDrawFlags_RoundMask_ = RoundMask_,
    AllowInPushScope_ = AAFill | AALines | AALineEnds | StrokeLegacy | TextNoPixelSnap | UseTexForRoundCorners | UseVtxOffset | RoundMask_,
    ImDrawFlags_AllowInPushScope_ = AllowInPushScope_,
    AllowInFrameScope_ = AllowInPushScope_ | AllowTexForRoundCorners_,
    ImDrawFlags_AllowInFrameScope_ = AllowInFrameScope_,
    StrokeMask_ = 0xE0000,
    ImDrawFlags_StrokeMask_ = StrokeMask_,
    InvalidMask_ = unchecked((int)0x8000000F), // == 0x8000000F as signed integer
    ImDrawFlags_InvalidMask_ = InvalidMask_,

    // Obsolete aliases (renamed in 1.93.0)
    RoundCornersTopLeft = RoundTopLeft,
    ImDrawFlags_RoundCornersTopLeft = RoundCornersTopLeft,
    RoundCornersTopRight = RoundTopRight,
    ImDrawFlags_RoundCornersTopRight = RoundCornersTopRight,
    RoundCornersBottomLeft = RoundBottomLeft,
    ImDrawFlags_RoundCornersBottomLeft = RoundCornersBottomLeft,
    RoundCornersBottomRight = RoundBottomRight,
    ImDrawFlags_RoundCornersBottomRight = RoundCornersBottomRight,
    RoundCornersNone = RoundNone,
    ImDrawFlags_RoundCornersNone = RoundCornersNone,
    RoundCornersAll = RoundAll,
    ImDrawFlags_RoundCornersAll = RoundCornersAll,
    RoundCornersDefault_ = RoundAll,
    ImDrawFlags_RoundCornersDefault_ = RoundCornersDefault_,
    RoundCornersTop = RoundTop,
    ImDrawFlags_RoundCornersTop = RoundCornersTop,
    RoundCornersBottom = RoundBottom,
    ImDrawFlags_RoundCornersBottom = RoundCornersBottom,
    RoundCornersLeft = RoundLeft,
    ImDrawFlags_RoundCornersLeft = RoundCornersLeft,
    RoundCornersRight = RoundRight,
    ImDrawFlags_RoundCornersRight = RoundCornersRight,
    RoundCornersMask_ = RoundMask_,
    ImDrawFlags_RoundCornersMask_ = RoundCornersMask_,
}

/// <summary>
/// Specifies flags that control draw list rendering features such as anti-aliasing and vertex offsets.
/// Merged into <see cref="ImDrawFlags"/> in 1.93.0.
/// </summary>
[Flags]
public enum ImDrawListFlags : uint
{
    None = 0,
    ImDrawListFlags_None = None,
    AntiAliasedFill = 0x40000000,
    ImDrawListFlags_AntiAliasedFill = AntiAliasedFill,
    AntiAliasedLines = 0x80000000,
    ImDrawListFlags_AntiAliasedLines = AntiAliasedLines,
    AntiAliasedLinesUseTex = 0x80000000,
    ImDrawListFlags_AntiAliasedLinesUseTex = AntiAliasedLinesUseTex,
    AllowVtxOffset = 0x20000000,
    ImDrawListFlags_AllowVtxOffset = AllowVtxOffset,
    TextNoPixelSnap = 0x10000000,
    ImDrawListFlags_TextNoPixelSnap = TextNoPixelSnap,
}

/// <summary>
/// Specifies flags that control text rendering and clipping within draw lists.
/// </summary>
[Flags]
public enum ImDrawTextFlags
{
    None = 0,
    ImDrawTextFlags_None = None,
    CpuFineClip = 1,
    ImDrawTextFlags_CpuFineClip = CpuFineClip,
    WrapKeepBlanks = 2,
    ImDrawTextFlags_WrapKeepBlanks = WrapKeepBlanks,
    StopOnNewLine = 4,
    ImDrawTextFlags_StopOnNewLine = StopOnNewLine,
}

/// <summary>
/// Specifies flags that control font atlas texture construction.
/// </summary>
[Flags]
public enum ImFontAtlasFlags
{
    None = 0,
    ImFontAtlasFlags_None = None,
    NoPowerOfTwoHeight = 1,
    ImFontAtlasFlags_NoPowerOfTwoHeight = NoPowerOfTwoHeight,
    NoMouseCursors = 2,
    ImFontAtlasFlags_NoMouseCursors = NoMouseCursors,
    NoBakedLines = 4,
    ImFontAtlasFlags_NoBakedLines = NoBakedLines,
    NoBakedRoundCorners = 8,
    ImFontAtlasFlags_NoBakedRoundCorners = NoBakedRoundCorners,
}

/// <summary>
/// Specifies flags that control font loading and glyph baking behavior.
/// </summary>
[Flags]
public enum ImFontFlags
{
    None = 0,
    ImFontFlags_None = None,
    NoLoadError = 2,
    ImFontFlags_NoLoadError = NoLoadError,
    NoLoadGlyphs = 4,
    ImFontFlags_NoLoadGlyphs = NoLoadGlyphs,
    LockBakedSizes = 8,
    ImFontFlags_LockBakedSizes = LockBakedSizes,
    ImplicitRefSize = 0x10,
    ImFontFlags_ImplicitRefSize = ImplicitRefSize,
}

/// <summary>
/// Specifies flags that describe how an item was activated.
/// </summary>
[Flags]
public enum ImGuiActivateFlags
{
    None = 0,
    ImGuiActivateFlags_None = None,
    PreferInput = 1,
    ImGuiActivateFlags_PreferInput = PreferInput,
    PreferTweak = 2,
    ImGuiActivateFlags_PreferTweak = PreferTweak,
    TryToPreserveState = 4,
    ImGuiActivateFlags_TryToPreserveState = TryToPreserveState,
    FromTabbing = 8,
    ImGuiActivateFlags_FromTabbing = FromTabbing,
    FromShortcut = 0x10,
    ImGuiActivateFlags_FromShortcut = FromShortcut,
    FromFocusApi = 0x20,
    ImGuiActivateFlags_FromFocusApi = FromFocusApi,
}

/// <summary>
/// Identifies a single layout or navigation axis.
/// </summary>
public enum ImGuiAxis
{
    None = -1,
    ImGuiAxis_None = None,
    X = 0,
    ImGuiAxis_X = X,
    Y = 1,
    ImGuiAxis_Y = Y,
}

/// <summary>
/// Specifies the capabilities reported by a platform or renderer backend.
/// </summary>
[Flags]
public enum ImGuiBackendFlags
{
    None = 0,
    ImGuiBackendFlags_None = None,
    HasGamepad = 1,
    ImGuiBackendFlags_HasGamepad = HasGamepad,
    HasMouseCursors = 2,
    ImGuiBackendFlags_HasMouseCursors = HasMouseCursors,
    HasSetMousePos = 4,
    ImGuiBackendFlags_HasSetMousePos = HasSetMousePos,
    RendererHasVtxOffset = 8,
    ImGuiBackendFlags_RendererHasVtxOffset = RendererHasVtxOffset,
    RendererHasTextures = 0x10,
    ImGuiBackendFlags_RendererHasTextures = RendererHasTextures,
    RendererHasViewports = 0x400,
    ImGuiBackendFlags_RendererHasViewports = RendererHasViewports,
    PlatformHasViewports = 0x800,
    ImGuiBackendFlags_PlatformHasViewports = PlatformHasViewports,
    HasMouseHoveredViewport = 0x1000,
    ImGuiBackendFlags_HasMouseHoveredViewport = HasMouseHoveredViewport,
    HasParentViewport = 0x2000,
    ImGuiBackendFlags_HasParentViewport = HasParentViewport,
}

/// <summary>
/// Specifies public flags that alter button behavior.
/// </summary>
[Flags]
public enum ImGuiButtonFlags
{
    None = 0,
    ImGuiButtonFlags_None = None,
    MouseButtonLeft = 1,
    ImGuiButtonFlags_MouseButtonLeft = MouseButtonLeft,
    MouseButtonRight = 2,
    ImGuiButtonFlags_MouseButtonRight = MouseButtonRight,
    MouseButtonMiddle = 4,
    ImGuiButtonFlags_MouseButtonMiddle = MouseButtonMiddle,
    MouseButtonMask_ = 7,
    ImGuiButtonFlags_MouseButtonMask_ = MouseButtonMask_,
    EnableNav = 8,
    ImGuiButtonFlags_EnableNav = EnableNav,
    AllowOverlap = 0x1000,
    ImGuiButtonFlags_AllowOverlap = AllowOverlap,
}

/// <summary>
/// Specifies internal flags that alter button behavior.
/// </summary>
[Flags]
public enum ImGuiButtonFlagsPrivate
{
    ImGuiButtonFlags_PressedOnClick = 0x10,
    ImGuiButtonFlags_PressedOnClickRelease = 0x20,
    ImGuiButtonFlags_PressedOnClickReleaseAnywhere = 0x40,
    ImGuiButtonFlags_PressedOnRelease = 0x80,
    ImGuiButtonFlags_PressedOnDoubleClick = 0x100,
    ImGuiButtonFlags_PressedOnDragDropHold = 0x200,
    ImGuiButtonFlags_FlattenChildren = 0x800,
    ImGuiButtonFlags_AlignTextBaseLine = 0x8000,
    ImGuiButtonFlags_NoKeyModsAllowed = 0x10000,
    ImGuiButtonFlags_NoHoldingActiveId = 0x20000,
    ImGuiButtonFlags_NoNavFocus = 0x40000,
    ImGuiButtonFlags_NoHoveredOnFocus = 0x80000,
    ImGuiButtonFlags_NoSetKeyOwner = 0x100000,
    ImGuiButtonFlags_NoTestKeyOwner = 0x200000,
    ImGuiButtonFlags_NoFocus = 0x400000,
    ImGuiButtonFlags_PressedOnMask_ = 0x3F0,
    ImGuiButtonFlags_PressedOnDefault_ = 0x20,
}

/// <summary>
/// Specifies flags that alter child region behavior.
/// </summary>
[Flags]
public enum ImGuiChildFlags
{
    None = 0,
    ImGuiChildFlags_None = None,
    Borders = 1,
    ImGuiChildFlags_Borders = Borders,
    AlwaysUseWindowPadding = 2,
    ImGuiChildFlags_AlwaysUseWindowPadding = AlwaysUseWindowPadding,
    ResizeX = 4,
    ImGuiChildFlags_ResizeX = ResizeX,
    ResizeY = 8,
    ImGuiChildFlags_ResizeY = ResizeY,
    AutoResizeX = 0x10,
    ImGuiChildFlags_AutoResizeX = AutoResizeX,
    AutoResizeY = 0x20,
    ImGuiChildFlags_AutoResizeY = AutoResizeY,
    AlwaysAutoResize = 0x40,
    ImGuiChildFlags_AlwaysAutoResize = AlwaysAutoResize,
    FrameStyle = 0x80,
    ImGuiChildFlags_FrameStyle = FrameStyle,
    NavFlattened = 0x100,
    ImGuiChildFlags_NavFlattened = NavFlattened,
}

/// <summary>
/// Identifies each color slot in the style palette.
/// </summary>
public enum ImGuiCol
{
    Text = 0,
    ImGuiCol_Text = Text,
    TextDisabled = 1,
    ImGuiCol_TextDisabled = TextDisabled,
    TextMixedValue = TextDisabled,
    ImGuiCol_TextMixedValue = TextDisabled,
    WindowBg = 2,
    ImGuiCol_WindowBg = WindowBg,
    ChildBg = 3,
    ImGuiCol_ChildBg = ChildBg,
    PopupBg = 4,
    ImGuiCol_PopupBg = PopupBg,
    Border = 5,
    ImGuiCol_Border = Border,
    BorderShadow = 6,
    ImGuiCol_BorderShadow = BorderShadow,
    FrameBg = 7,
    ImGuiCol_FrameBg = FrameBg,
    FrameBgHovered = 8,
    ImGuiCol_FrameBgHovered = FrameBgHovered,
    FrameBgActive = 9,
    ImGuiCol_FrameBgActive = FrameBgActive,
    TitleBg = 0xA,
    ImGuiCol_TitleBg = TitleBg,
    TitleBgActive = 0xB,
    ImGuiCol_TitleBgActive = TitleBgActive,
    TitleBgCollapsed = 0xC,
    ImGuiCol_TitleBgCollapsed = TitleBgCollapsed,
    MenuBarBg = 0xD,
    ImGuiCol_MenuBarBg = MenuBarBg,
    ScrollbarBg = 0xE,
    ImGuiCol_ScrollbarBg = ScrollbarBg,
    ScrollbarGrab = 0xF,
    ImGuiCol_ScrollbarGrab = ScrollbarGrab,
    ScrollbarGrabHovered = 0x10,
    ImGuiCol_ScrollbarGrabHovered = ScrollbarGrabHovered,
    ScrollbarGrabActive = 0x11,
    ImGuiCol_ScrollbarGrabActive = ScrollbarGrabActive,
    CheckMark = 0x12,
    ImGuiCol_CheckMark = CheckMark,
    CheckboxSelectedBg = 0x13,
    ImGuiCol_CheckboxSelectedBg = CheckboxSelectedBg,
    SliderGrab = 0x14,
    ImGuiCol_SliderGrab = SliderGrab,
    SliderGrabActive = 0x15,
    ImGuiCol_SliderGrabActive = SliderGrabActive,
    Button = 0x16,
    ImGuiCol_Button = Button,
    ButtonHovered = 0x17,
    ImGuiCol_ButtonHovered = ButtonHovered,
    ButtonActive = 0x18,
    ImGuiCol_ButtonActive = ButtonActive,
    Header = 0x19,
    ImGuiCol_Header = Header,
    HeaderHovered = 0x1A,
    ImGuiCol_HeaderHovered = HeaderHovered,
    HeaderActive = 0x1B,
    ImGuiCol_HeaderActive = HeaderActive,
    Separator = 0x1C,
    ImGuiCol_Separator = Separator,
    SeparatorHovered = 0x1D,
    ImGuiCol_SeparatorHovered = SeparatorHovered,
    SeparatorActive = 0x1E,
    ImGuiCol_SeparatorActive = SeparatorActive,
    ResizeGrip = 0x1F,
    ImGuiCol_ResizeGrip = ResizeGrip,
    ResizeGripHovered = 0x20,
    ImGuiCol_ResizeGripHovered = ResizeGripHovered,
    ResizeGripActive = 0x21,
    ImGuiCol_ResizeGripActive = ResizeGripActive,
    InputTextCursor = 0x22,
    ImGuiCol_InputTextCursor = InputTextCursor,
    TabHovered = 0x23,
    ImGuiCol_TabHovered = TabHovered,
    Tab = 0x24,
    ImGuiCol_Tab = Tab,
    TabSelected = 0x25,
    ImGuiCol_TabSelected = TabSelected,
    TabSelectedOverline = 0x26,
    ImGuiCol_TabSelectedOverline = TabSelectedOverline,
    TabDimmed = 0x27,
    ImGuiCol_TabDimmed = TabDimmed,
    TabDimmedSelected = 0x28,
    ImGuiCol_TabDimmedSelected = TabDimmedSelected,
    TabDimmedSelectedOverline = 0x29,
    ImGuiCol_TabDimmedSelectedOverline = TabDimmedSelectedOverline,
    DockingPreview = 0x2A,
    ImGuiCol_DockingPreview = DockingPreview,
    DockingEmptyBg = 0x2B,
    ImGuiCol_DockingEmptyBg = DockingEmptyBg,
    PlotLines = 0x2C,
    ImGuiCol_PlotLines = PlotLines,
    PlotLinesHovered = 0x2D,
    ImGuiCol_PlotLinesHovered = PlotLinesHovered,
    PlotHistogram = 0x2E,
    ImGuiCol_PlotHistogram = PlotHistogram,
    PlotHistogramHovered = 0x2F,
    ImGuiCol_PlotHistogramHovered = PlotHistogramHovered,
    TableHeaderBg = 0x30,
    ImGuiCol_TableHeaderBg = TableHeaderBg,
    TableBorderStrong = 0x31,
    ImGuiCol_TableBorderStrong = TableBorderStrong,
    TableBorderLight = 0x32,
    ImGuiCol_TableBorderLight = TableBorderLight,
    TableRowBg = 0x33,
    ImGuiCol_TableRowBg = TableRowBg,
    TableRowBgAlt = 0x34,
    ImGuiCol_TableRowBgAlt = TableRowBgAlt,
    TextLink = 0x35,
    ImGuiCol_TextLink = TextLink,
    TextSelectedBg = 0x36,
    ImGuiCol_TextSelectedBg = TextSelectedBg,
    TreeLines = 0x37,
    ImGuiCol_TreeLines = TreeLines,
    DragDropTarget = 0x38,
    ImGuiCol_DragDropTarget = DragDropTarget,
    DragDropTargetBg = 0x39,
    ImGuiCol_DragDropTargetBg = DragDropTargetBg,
    UnsavedMarker = 0x3A,
    ImGuiCol_UnsavedMarker = UnsavedMarker,
    NavCursor = 0x3B,
    ImGuiCol_NavCursor = NavCursor,
    NavWindowingHighlight = 0x3C,
    ImGuiCol_NavWindowingHighlight = NavWindowingHighlight,
    NavWindowingDimBg = 0x3D,
    ImGuiCol_NavWindowingDimBg = NavWindowingDimBg,
    ModalWindowDimBg = 0x3E,
    ImGuiCol_ModalWindowDimBg = ModalWindowDimBg,
    COUNT = 0x3F,
    ImGuiCol_COUNT = COUNT,
    TabUnfocused = 0x27,
    ImGuiCol_TabUnfocused = TabUnfocused,
    TabUnfocusedActive = 0x28,
    ImGuiCol_TabUnfocusedActive = TabUnfocusedActive,
    NavHighlight = 0x3B,
    ImGuiCol_NavHighlight = NavHighlight,
}

/// <summary>
/// Specifies flags that alter color editor and color picker behavior.
/// </summary>
[Flags]
public enum ImGuiColorEditFlags
{
    None = 0,
    ImGuiColorEditFlags_None = None,
    NoAlpha = 2,
    ImGuiColorEditFlags_NoAlpha = NoAlpha,
    NoPicker = 4,
    ImGuiColorEditFlags_NoPicker = NoPicker,
    NoOptions = 8,
    ImGuiColorEditFlags_NoOptions = NoOptions,
    NoSmallPreview = 0x10,
    ImGuiColorEditFlags_NoSmallPreview = NoSmallPreview,
    NoInputs = 0x20,
    ImGuiColorEditFlags_NoInputs = NoInputs,
    NoTooltip = 0x40,
    ImGuiColorEditFlags_NoTooltip = NoTooltip,
    NoLabel = 0x80,
    ImGuiColorEditFlags_NoLabel = NoLabel,
    NoSidePreview = 0x100,
    ImGuiColorEditFlags_NoSidePreview = NoSidePreview,
    NoDragDrop = 0x200,
    ImGuiColorEditFlags_NoDragDrop = NoDragDrop,
    NoBorder = 0x400,
    ImGuiColorEditFlags_NoBorder = NoBorder,
    NoColorMarkers = 0x800,
    ImGuiColorEditFlags_NoColorMarkers = NoColorMarkers,
    AlphaOpaque = 0x1000,
    ImGuiColorEditFlags_AlphaOpaque = AlphaOpaque,
    AlphaNoBg = 0x2000,
    ImGuiColorEditFlags_AlphaNoBg = AlphaNoBg,
    AlphaPreviewHalf = 0x4000,
    ImGuiColorEditFlags_AlphaPreviewHalf = AlphaPreviewHalf,
    AlphaBar = 0x40000,
    ImGuiColorEditFlags_AlphaBar = AlphaBar,
    HDR = 0x80000,
    ImGuiColorEditFlags_HDR = HDR,
    DisplayRGB = 0x100000,
    ImGuiColorEditFlags_DisplayRGB = DisplayRGB,
    DisplayHSV = 0x200000,
    ImGuiColorEditFlags_DisplayHSV = DisplayHSV,
    DisplayHex = 0x400000,
    ImGuiColorEditFlags_DisplayHex = DisplayHex,
    Uint8 = 0x800000,
    ImGuiColorEditFlags_Uint8 = Uint8,
    Float = 0x1000000,
    ImGuiColorEditFlags_Float = Float,
    PickerHueBar = 0x2000000,
    ImGuiColorEditFlags_PickerHueBar = PickerHueBar,
    PickerHueWheel = 0x4000000,
    ImGuiColorEditFlags_PickerHueWheel = PickerHueWheel,
    PickerNoRotate = 0x8000000,
    ImGuiColorEditFlags_PickerNoRotate = PickerNoRotate,
    InputRGB = 0x10000000,
    ImGuiColorEditFlags_InputRGB = InputRGB,
    InputHSV = 0x20000000,
    ImGuiColorEditFlags_InputHSV = InputHSV,
    DefaultOptions_ = 0x12900000,
    ImGuiColorEditFlags_DefaultOptions_ = DefaultOptions_,
    AlphaMask_ = 0x7002,
    ImGuiColorEditFlags_AlphaMask_ = AlphaMask_,
    DisplayMask_ = 0x700000,
    ImGuiColorEditFlags_DisplayMask_ = DisplayMask_,
    DataTypeMask_ = 0x1800000,
    ImGuiColorEditFlags_DataTypeMask_ = DataTypeMask_,
    PickerMask_ = 0x6000000,
    ImGuiColorEditFlags_PickerMask_ = PickerMask_,
    InputMask_ = 0x30000000,
    ImGuiColorEditFlags_InputMask_ = InputMask_,
}

/// <summary>
/// Specifies public flags that alter combo box behavior.
/// </summary>
[Flags]
public enum ImGuiComboFlags
{
    None = 0,
    ImGuiComboFlags_None = None,
    PopupAlignLeft = 1,
    ImGuiComboFlags_PopupAlignLeft = PopupAlignLeft,
    HeightSmall = 2,
    ImGuiComboFlags_HeightSmall = HeightSmall,
    HeightRegular = 4,
    ImGuiComboFlags_HeightRegular = HeightRegular,
    HeightLarge = 8,
    ImGuiComboFlags_HeightLarge = HeightLarge,
    HeightLargest = 0x10,
    ImGuiComboFlags_HeightLargest = HeightLargest,
    NoArrowButton = 0x20,
    ImGuiComboFlags_NoArrowButton = NoArrowButton,
    NoPreview = 0x40,
    ImGuiComboFlags_NoPreview = NoPreview,
    WidthFitPreview = 0x80,
    ImGuiComboFlags_WidthFitPreview = WidthFitPreview,
    HeightMask_ = 0x1E,
    ImGuiComboFlags_HeightMask_ = HeightMask_,
}

/// <summary>
/// Specifies internal flags that alter combo box behavior.
/// </summary>
[Flags]
public enum ImGuiComboFlagsPrivate
{
    ImGuiComboFlags_CustomPreview = 0x100000,
}

/// <summary>
/// Specifies the conditions under which a requested size, position, or setting is applied.
/// </summary>
public enum ImGuiCond
{
    None = 0,
    ImGuiCond_None = None,
    Always = 1,
    ImGuiCond_Always = Always,
    Once = 2,
    ImGuiCond_Once = Once,
    FirstUseEver = 4,
    ImGuiCond_FirstUseEver = FirstUseEver,
    Appearing = 8,
    ImGuiCond_Appearing = Appearing,
}

/// <summary>
/// Specifies global configuration options for the UI context.
/// </summary>
[Flags]
public enum ImGuiConfigFlags
{
    None = 0,
    ImGuiConfigFlags_None = None,
    NavEnableKeyboard = 1,
    ImGuiConfigFlags_NavEnableKeyboard = NavEnableKeyboard,
    NavEnableGamepad = 2,
    ImGuiConfigFlags_NavEnableGamepad = NavEnableGamepad,
    NoMouse = 0x10,
    ImGuiConfigFlags_NoMouse = NoMouse,
    NoMouseCursorChange = 0x20,
    ImGuiConfigFlags_NoMouseCursorChange = NoMouseCursorChange,
    NoKeyboard = 0x40,
    ImGuiConfigFlags_NoKeyboard = NoKeyboard,
    DockingEnable = 0x80,
    ImGuiConfigFlags_DockingEnable = DockingEnable,
    ViewportsEnable = 0x400,
    ImGuiConfigFlags_ViewportsEnable = ViewportsEnable,
    IsSRGB = 0x100000,
    ImGuiConfigFlags_IsSRGB = IsSRGB,
    IsTouchScreen = 0x200000,
    ImGuiConfigFlags_IsTouchScreen = IsTouchScreen,
    NavNoCaptureKeyboard = 8,
    ImGuiConfigFlags_NavNoCaptureKeyboard = NavNoCaptureKeyboard,
    DpiEnableScaleFonts = 0x4000,
    ImGuiConfigFlags_DpiEnableScaleFonts = DpiEnableScaleFonts,
    DpiEnableScaleViewports = 0x8000,
    ImGuiConfigFlags_DpiEnableScaleViewports = DpiEnableScaleViewports,
}

/// <summary>
/// Identifies the lifecycle point at which a context hook runs.
/// </summary>
public enum ImGuiContextHookType
{
    NewFramePre = 0,
    ImGuiContextHookType_NewFramePre = NewFramePre,
    NewFramePost = 1,
    ImGuiContextHookType_NewFramePost = NewFramePost,
    EndFramePre = 2,
    ImGuiContextHookType_EndFramePre = EndFramePre,
    EndFramePost = 3,
    ImGuiContextHookType_EndFramePost = EndFramePost,
    RenderPre = 4,
    ImGuiContextHookType_RenderPre = RenderPre,
    RenderPost = 5,
    ImGuiContextHookType_RenderPost = RenderPost,
    Shutdown = 6,
    ImGuiContextHookType_Shutdown = Shutdown,
    PendingRemoval_ = 7,
    ImGuiContextHookType_PendingRemoval_ = PendingRemoval_,
}

/// <summary>
/// Identifies which side of a window owns a persisted setting when windows or viewports are merged.
/// </summary>
public enum ImGuiDataAuthority
{
    Auto = 0,
    ImGuiDataAuthority_Auto = Auto,
    DockNode = 1,
    ImGuiDataAuthority_DockNode = DockNode,
    Window = 2,
    ImGuiDataAuthority_Window = Window,
}

/// <summary>
/// Identifies the primitive numeric data type used by a widget.
/// </summary>
public enum ImGuiDataType
{
    S8 = 0,
    ImGuiDataType_S8 = S8,
    U8 = 1,
    ImGuiDataType_U8 = U8,
    S16 = 2,
    ImGuiDataType_S16 = S16,
    U16 = 3,
    ImGuiDataType_U16 = U16,
    S32 = 4,
    ImGuiDataType_S32 = S32,
    U32 = 5,
    ImGuiDataType_U32 = U32,
    S64 = 6,
    ImGuiDataType_S64 = S64,
    U64 = 7,
    ImGuiDataType_U64 = U64,
    Float = 8,
    ImGuiDataType_Float = Float,
    Double = 9,
    ImGuiDataType_Double = Double,
    Bool = 0xA,
    ImGuiDataType_Bool = Bool,
    String = 0xB,
    ImGuiDataType_String = String,
    COUNT = 0xC,
    ImGuiDataType_COUNT = COUNT,
}

/// <summary>
/// Specifies internal data type qualifiers.
/// </summary>
public enum ImGuiDataTypePrivate
{
    ImGuiDataType_Pointer = 0xC,
    ImGuiDataType_ID = 0xD,
}

/// <summary>
/// Specifies the categories of debug log output.
/// </summary>
[Flags]
public enum ImGuiDebugLogFlags
{
    None = 0,
    ImGuiDebugLogFlags_None = None,
    EventError = 1,
    ImGuiDebugLogFlags_EventError = EventError,
    EventActiveId = 2,
    ImGuiDebugLogFlags_EventActiveId = EventActiveId,
    EventFocus = 4,
    ImGuiDebugLogFlags_EventFocus = EventFocus,
    EventPopup = 8,
    ImGuiDebugLogFlags_EventPopup = EventPopup,
    EventNav = 0x10,
    ImGuiDebugLogFlags_EventNav = EventNav,
    EventClipper = 0x20,
    ImGuiDebugLogFlags_EventClipper = EventClipper,
    EventSelection = 0x40,
    ImGuiDebugLogFlags_EventSelection = EventSelection,
    EventIO = 0x80,
    ImGuiDebugLogFlags_EventIO = EventIO,
    EventFont = 0x100,
    ImGuiDebugLogFlags_EventFont = EventFont,
    EventInputRouting = 0x200,
    ImGuiDebugLogFlags_EventInputRouting = EventInputRouting,
    EventDocking = 0x400,
    ImGuiDebugLogFlags_EventDocking = EventDocking,
    EventViewport = 0x800,
    ImGuiDebugLogFlags_EventViewport = EventViewport,
    EventTable = 0x1000,
    ImGuiDebugLogFlags_EventTable = EventTable,
    EventMask_ = 0x1FFF,
    ImGuiDebugLogFlags_EventMask_ = EventMask_,
    OutputToTTY = 0x100000,
    ImGuiDebugLogFlags_OutputToTTY = OutputToTTY,
    OutputToDebugger = 0x200000,
    ImGuiDebugLogFlags_OutputToDebugger = OutputToDebugger,
    OutputToTestEngine = 0x400000,
    ImGuiDebugLogFlags_OutputToTestEngine = OutputToTestEngine,
}

/// <summary>
/// Identifies a cardinal direction used by layout and docking.
/// </summary>
public enum ImGuiDir
{
    None = -1,
    ImGuiDir_None = None,
    Left = 0,
    ImGuiDir_Left = Left,
    Right = 1,
    ImGuiDir_Right = Right,
    Up = 2,
    ImGuiDir_Up = Up,
    Down = 3,
    ImGuiDir_Down = Down,
    COUNT = 4,
    ImGuiDir_COUNT = COUNT,
}

/// <summary>
/// Specifies public flags that alter dock node behavior.
/// </summary>
[Flags]
public enum ImGuiDockNodeFlags
{
    None = 0,
    ImGuiDockNodeFlags_None = None,
    KeepAliveOnly = 1,
    ImGuiDockNodeFlags_KeepAliveOnly = KeepAliveOnly,
    NoDockingOverCentralNode = 4,
    ImGuiDockNodeFlags_NoDockingOverCentralNode = NoDockingOverCentralNode,
    PassthruCentralNode = 8,
    ImGuiDockNodeFlags_PassthruCentralNode = PassthruCentralNode,
    NoDockingSplit = 0x10,
    ImGuiDockNodeFlags_NoDockingSplit = NoDockingSplit,
    NoResize = 0x20,
    ImGuiDockNodeFlags_NoResize = NoResize,
    AutoHideTabBar = 0x40,
    ImGuiDockNodeFlags_AutoHideTabBar = AutoHideTabBar,
    NoUndocking = 0x80,
    ImGuiDockNodeFlags_NoUndocking = NoUndocking,
    NoDockingInCentralNode = 4,
    ImGuiDockNodeFlags_NoDockingInCentralNode = NoDockingInCentralNode,
    DockSpace = 0x400,
    ImGuiDockNodeFlags_DockSpace = DockSpace,
    CentralNode = 0x800,
    ImGuiDockNodeFlags_CentralNode = CentralNode,
    NoTabBar = 0x1000,
    ImGuiDockNodeFlags_NoTabBar = NoTabBar,
    HiddenTabBar = 0x2000,
    ImGuiDockNodeFlags_HiddenTabBar = HiddenTabBar,
    NoWindowMenuButton = 0x4000,
    ImGuiDockNodeFlags_NoWindowMenuButton = NoWindowMenuButton,
    NoCloseButton = 0x8000,
    ImGuiDockNodeFlags_NoCloseButton = NoCloseButton,
    NoResizeX = 0x10000,
    ImGuiDockNodeFlags_NoResizeX = NoResizeX,
    NoResizeY = 0x20000,
    ImGuiDockNodeFlags_NoResizeY = NoResizeY,
    DockedWindowsInFocusRoute = 0x40000,
    ImGuiDockNodeFlags_DockedWindowsInFocusRoute = DockedWindowsInFocusRoute,
    NoDockingSplitOther = 0x80000,
    ImGuiDockNodeFlags_NoDockingSplitOther = NoDockingSplitOther,
    NoDockingOverMe = 0x100000,
    ImGuiDockNodeFlags_NoDockingOverMe = NoDockingOverMe,
    NoDockingOverOther = 0x200000,
    ImGuiDockNodeFlags_NoDockingOverOther = NoDockingOverOther,
    NoDockingOverEmpty = 0x400000,
    ImGuiDockNodeFlags_NoDockingOverEmpty = NoDockingOverEmpty,
    NoDocking = 0x780010,
    ImGuiDockNodeFlags_NoDocking = NoDocking,
}

/// <summary>Specifies internal flags that alter dock node behavior.</summary>
[Flags]
public enum ImGuiDockNodeFlagsPrivate : uint
{
    DockSpace = 0x400,
    ImGuiDockNodeFlags_DockSpace = DockSpace,
    CentralNode = 0x800,
    ImGuiDockNodeFlags_CentralNode = CentralNode,
    NoTabBar = 0x1000,
    ImGuiDockNodeFlags_NoTabBar = NoTabBar,
    HiddenTabBar = 0x2000,
    ImGuiDockNodeFlags_HiddenTabBar = HiddenTabBar,
    NoWindowMenuButton = 0x4000,
    ImGuiDockNodeFlags_NoWindowMenuButton = NoWindowMenuButton,
    NoCloseButton = 0x8000,
    ImGuiDockNodeFlags_NoCloseButton = NoCloseButton,
    NoResizeX = 0x10000,
    ImGuiDockNodeFlags_NoResizeX = NoResizeX,
    NoResizeY = 0x20000,
    ImGuiDockNodeFlags_NoResizeY = NoResizeY,
    DockedWindowsInFocusRoute = 0x40000,
    ImGuiDockNodeFlags_DockedWindowsInFocusRoute = DockedWindowsInFocusRoute,
    NoDockingSplitOther = 0x80000,
    ImGuiDockNodeFlags_NoDockingSplitOther = NoDockingSplitOther,
    NoDockingOverMe = 0x100000,
    ImGuiDockNodeFlags_NoDockingOverMe = NoDockingOverMe,
    NoDockingOverOther = 0x200000,
    ImGuiDockNodeFlags_NoDockingOverOther = NoDockingOverOther,
    NoDockingOverEmpty = 0x400000,
    ImGuiDockNodeFlags_NoDockingOverEmpty = NoDockingOverEmpty,
    NoDocking = 0x780010,
    ImGuiDockNodeFlags_NoDocking = NoDocking,
    SharedFlagsInheritMask_ = unchecked((uint)~0),
    ImGuiDockNodeFlags_SharedFlagsInheritMask_ = SharedFlagsInheritMask_,
    NoResizeFlagsMask_ = 0x30020,
    ImGuiDockNodeFlags_NoResizeFlagsMask_ = NoResizeFlagsMask_,
    LocalFlagsTransferMask_ = 0x3F870,
    ImGuiDockNodeFlags_LocalFlagsTransferMask_ = LocalFlagsTransferMask_,
    SavedFlagsMask_ = 0x3FC20,
    ImGuiDockNodeFlags_SavedFlagsMask_ = SavedFlagsMask_,
}

/// <summary>
/// Identifies the lifecycle state of a dock node.
/// </summary>
public enum ImGuiDockNodeState
{
    Unknown = 0,
    ImGuiDockNodeState_Unknown = Unknown,
    HostWindowHiddenBecauseSingleWindow = 1,
    ImGuiDockNodeState_HostWindowHiddenBecauseSingleWindow = HostWindowHiddenBecauseSingleWindow,
    HostWindowHiddenBecauseWindowsAreResizing = 2,
    ImGuiDockNodeState_HostWindowHiddenBecauseWindowsAreResizing = HostWindowHiddenBecauseWindowsAreResizing,
    HostWindowVisible = 3,
    ImGuiDockNodeState_HostWindowVisible = HostWindowVisible,
}

/// <summary>
/// Specifies flags that alter drag-and-drop source and target behavior.
/// </summary>
[Flags]
public enum ImGuiDragDropFlags
{
    None = 0,
    ImGuiDragDropFlags_None = None,
    SourceNoPreviewTooltip = 1,
    ImGuiDragDropFlags_SourceNoPreviewTooltip = SourceNoPreviewTooltip,
    SourceNoDisableHover = 2,
    ImGuiDragDropFlags_SourceNoDisableHover = SourceNoDisableHover,
    SourceNoHoldToOpenOthers = 4,
    ImGuiDragDropFlags_SourceNoHoldToOpenOthers = SourceNoHoldToOpenOthers,
    SourceAllowNullID = 8,
    ImGuiDragDropFlags_SourceAllowNullID = SourceAllowNullID,
    SourceExtern = 0x10,
    ImGuiDragDropFlags_SourceExtern = SourceExtern,
    PayloadAutoExpire = 0x20,
    ImGuiDragDropFlags_PayloadAutoExpire = PayloadAutoExpire,
    PayloadNoCrossContext = 0x40,
    ImGuiDragDropFlags_PayloadNoCrossContext = PayloadNoCrossContext,
    PayloadNoCrossProcess = 0x80,
    ImGuiDragDropFlags_PayloadNoCrossProcess = PayloadNoCrossProcess,
    AcceptBeforeDelivery = 0x400,
    ImGuiDragDropFlags_AcceptBeforeDelivery = AcceptBeforeDelivery,
    AcceptNoDrawDefaultRect = 0x800,
    ImGuiDragDropFlags_AcceptNoDrawDefaultRect = AcceptNoDrawDefaultRect,
    AcceptNoPreviewTooltip = 0x1000,
    ImGuiDragDropFlags_AcceptNoPreviewTooltip = AcceptNoPreviewTooltip,
    AcceptDrawAsHovered = 0x2000,
    ImGuiDragDropFlags_AcceptDrawAsHovered = AcceptDrawAsHovered,
    AcceptPeekOnly = 0xC00,
    ImGuiDragDropFlags_AcceptPeekOnly = AcceptPeekOnly,
}

/// <summary>
/// Specifies flags that alter how a focus request is resolved.
/// </summary>
[Flags]
public enum ImGuiFocusRequestFlags
{
    None = 0,
    ImGuiFocusRequestFlags_None = None,
    RestoreFocusedChild = 1,
    ImGuiFocusRequestFlags_RestoreFocusedChild = RestoreFocusedChild,
    UnlessBelowModal = 2,
    ImGuiFocusRequestFlags_UnlessBelowModal = UnlessBelowModal,
}

/// <summary>
/// Specifies flags that alter window focus queries.
/// </summary>
[Flags]
public enum ImGuiFocusedFlags
{
    None = 0,
    ImGuiFocusedFlags_None = None,
    ChildWindows = 1,
    ImGuiFocusedFlags_ChildWindows = ChildWindows,
    RootWindow = 2,
    ImGuiFocusedFlags_RootWindow = RootWindow,
    AnyWindow = 4,
    ImGuiFocusedFlags_AnyWindow = AnyWindow,
    NoPopupHierarchy = 8,
    ImGuiFocusedFlags_NoPopupHierarchy = NoPopupHierarchy,
    DockHierarchy = 0x10,
    ImGuiFocusedFlags_DockHierarchy = DockHierarchy,
    RootAndChildWindows = 3,
    ImGuiFocusedFlags_RootAndChildWindows = RootAndChildWindows,
}

/// <summary>
/// Specifies flags that alter item and window hover queries.
/// </summary>
[Flags]
public enum ImGuiHoveredFlags
{
    None = 0,
    ImGuiHoveredFlags_None = None,
    ChildWindows = 1,
    ImGuiHoveredFlags_ChildWindows = ChildWindows,
    RootWindow = 2,
    ImGuiHoveredFlags_RootWindow = RootWindow,
    AnyWindow = 4,
    ImGuiHoveredFlags_AnyWindow = AnyWindow,
    NoPopupHierarchy = 8,
    ImGuiHoveredFlags_NoPopupHierarchy = NoPopupHierarchy,
    DockHierarchy = 0x10,
    ImGuiHoveredFlags_DockHierarchy = DockHierarchy,
    AllowWhenBlockedByPopup = 0x20,
    ImGuiHoveredFlags_AllowWhenBlockedByPopup = AllowWhenBlockedByPopup,
    AllowWhenBlockedByActiveItem = 0x80,
    ImGuiHoveredFlags_AllowWhenBlockedByActiveItem = AllowWhenBlockedByActiveItem,
    AllowWhenOverlappedByItem = 0x100,
    ImGuiHoveredFlags_AllowWhenOverlappedByItem = AllowWhenOverlappedByItem,
    AllowWhenOverlappedByWindow = 0x200,
    ImGuiHoveredFlags_AllowWhenOverlappedByWindow = AllowWhenOverlappedByWindow,
    AllowWhenDisabled = 0x400,
    ImGuiHoveredFlags_AllowWhenDisabled = AllowWhenDisabled,
    NoNavOverride = 0x800,
    ImGuiHoveredFlags_NoNavOverride = NoNavOverride,
    AllowWhenOverlapped = 0x300,
    ImGuiHoveredFlags_AllowWhenOverlapped = AllowWhenOverlapped,
    RectOnly = 0x3A0,
    ImGuiHoveredFlags_RectOnly = RectOnly,
    RootAndChildWindows = 3,
    ImGuiHoveredFlags_RootAndChildWindows = RootAndChildWindows,
    ForTooltip = 0x1000,
    ImGuiHoveredFlags_ForTooltip = ForTooltip,
    Stationary = 0x2000,
    ImGuiHoveredFlags_Stationary = Stationary,
    DelayNone = 0x4000,
    ImGuiHoveredFlags_DelayNone = DelayNone,
    DelayShort = 0x8000,
    ImGuiHoveredFlags_DelayShort = DelayShort,
    DelayNormal = 0x10000,
    ImGuiHoveredFlags_DelayNormal = DelayNormal,
    NoSharedDelay = 0x20000,
    ImGuiHoveredFlags_NoSharedDelay = NoSharedDelay,
}

/// <summary>
/// Specifies internal masks and combinations used by hover queries.
/// </summary>
[Flags]
public enum ImGuiHoveredFlagsPrivate
{
    ImGuiHoveredFlags_DelayMask_ = 0x3C000,
    ImGuiHoveredFlags_AllowedMaskForIsWindowHovered = 0x30BF,
    ImGuiHoveredFlags_AllowedMaskForIsItemHovered = 0x3FFA0,
}

/// <summary>
/// Identifies the kind of queued input event.
/// </summary>
public enum ImGuiInputEventType
{
    None = 0,
    ImGuiInputEventType_None = None,
    MousePos = 1,
    ImGuiInputEventType_MousePos = MousePos,
    MouseWheel = 2,
    ImGuiInputEventType_MouseWheel = MouseWheel,
    MouseButton = 3,
    ImGuiInputEventType_MouseButton = MouseButton,
    MouseViewport = 4,
    ImGuiInputEventType_MouseViewport = MouseViewport,
    Key = 5,
    ImGuiInputEventType_Key = Key,
    Text = 6,
    ImGuiInputEventType_Text = Text,
    Focus = 7,
    ImGuiInputEventType_Focus = Focus,
    COUNT = 8,
    ImGuiInputEventType_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter input routing and repeat behavior.
/// </summary>
[Flags]
public enum ImGuiInputFlags
{
    None = 0,
    ImGuiInputFlags_None = None,
    Repeat = 1,
    ImGuiInputFlags_Repeat = Repeat,
    RouteActive = 0x400,
    ImGuiInputFlags_RouteActive = RouteActive,
    RouteFocused = 0x800,
    ImGuiInputFlags_RouteFocused = RouteFocused,
    RouteGlobal = 0x1000,
    ImGuiInputFlags_RouteGlobal = RouteGlobal,
    RouteAlways = 0x2000,
    ImGuiInputFlags_RouteAlways = RouteAlways,
    RouteOverFocused = 0x4000,
    ImGuiInputFlags_RouteOverFocused = RouteOverFocused,
    RouteOverActive = 0x8000,
    ImGuiInputFlags_RouteOverActive = RouteOverActive,
    RouteUnlessBgFocused = 0x10000,
    ImGuiInputFlags_RouteUnlessBgFocused = RouteUnlessBgFocused,
    RouteFromRootWindow = 0x20000,
    ImGuiInputFlags_RouteFromRootWindow = RouteFromRootWindow,
    Tooltip = 0x40000,
    ImGuiInputFlags_Tooltip = Tooltip,
}

/// <summary>
/// Specifies internal flags for input routing and repeat behavior.
/// </summary>
[Flags]
public enum ImGuiInputFlagsPrivate
{
    ImGuiInputFlags_RepeatRateDefault = 2,
    ImGuiInputFlags_RepeatRateNavMove = 4,
    ImGuiInputFlags_RepeatRateNavTweak = 8,
    ImGuiInputFlags_RepeatUntilRelease = 0x10,
    ImGuiInputFlags_RepeatUntilKeyModsChange = 0x20,
    ImGuiInputFlags_RepeatUntilKeyModsChangeFromNone = 0x40,
    ImGuiInputFlags_RepeatUntilOtherKeyPress = 0x80,
    ImGuiInputFlags_LockThisFrame = 0x100000,
    ImGuiInputFlags_LockUntilRelease = 0x200000,
    ImGuiInputFlags_CondHovered = 0x400000,
    ImGuiInputFlags_CondActive = 0x800000,
    ImGuiInputFlags_CondDefault_ = 0xC00000,
    ImGuiInputFlags_RepeatRateMask_ = 0xE,
    ImGuiInputFlags_RepeatUntilMask_ = 0xF0,
    ImGuiInputFlags_RepeatMask_ = 0xFF,
    ImGuiInputFlags_CondMask_ = 0xC00000,
    ImGuiInputFlags_RouteTypeMask_ = 0x3C00,
    ImGuiInputFlags_RouteOptionsMask_ = 0x3C000,
    ImGuiInputFlags_SupportedByIsKeyPressed = 0xFF,
    ImGuiInputFlags_SupportedByIsMouseClicked = 1,
    ImGuiInputFlags_SupportedByShortcut = 0x3FCFF,
    ImGuiInputFlags_SupportedBySetNextItemShortcut = 0x7FCFF,
    ImGuiInputFlags_SupportedBySetKeyOwner = 0x300000,
    ImGuiInputFlags_SupportedBySetItemKeyOwner = 0xF00000,
}

/// <summary>
/// Identifies the peripheral or source that produced an input event.
/// </summary>
public enum ImGuiInputSource
{
    None = 0,
    ImGuiInputSource_None = None,
    Mouse = 1,
    ImGuiInputSource_Mouse = Mouse,
    Keyboard = 2,
    ImGuiInputSource_Keyboard = Keyboard,
    Gamepad = 3,
    ImGuiInputSource_Gamepad = Gamepad,
    COUNT = 4,
    ImGuiInputSource_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter text input behavior.
/// </summary>
[Flags]
public enum ImGuiInputTextFlags
{
    None = 0,
    ImGuiInputTextFlags_None = None,
    CharsDecimal = 1,
    ImGuiInputTextFlags_CharsDecimal = CharsDecimal,
    CharsHexadecimal = 2,
    ImGuiInputTextFlags_CharsHexadecimal = CharsHexadecimal,
    CharsScientific = 4,
    ImGuiInputTextFlags_CharsScientific = CharsScientific,
    CharsUppercase = 8,
    ImGuiInputTextFlags_CharsUppercase = CharsUppercase,
    CharsNoBlank = 0x10,
    ImGuiInputTextFlags_CharsNoBlank = CharsNoBlank,
    AllowTabInput = 0x20,
    ImGuiInputTextFlags_AllowTabInput = AllowTabInput,
    EnterReturnsTrue = 0x40,
    ImGuiInputTextFlags_EnterReturnsTrue = EnterReturnsTrue,
    EscapeClearsAll = 0x80,
    ImGuiInputTextFlags_EscapeClearsAll = EscapeClearsAll,
    CtrlEnterForNewLine = 0x100,
    ImGuiInputTextFlags_CtrlEnterForNewLine = CtrlEnterForNewLine,
    ReadOnly = 0x200,
    ImGuiInputTextFlags_ReadOnly = ReadOnly,
    Password = 0x400,
    ImGuiInputTextFlags_Password = Password,
    AlwaysOverwrite = 0x800,
    ImGuiInputTextFlags_AlwaysOverwrite = AlwaysOverwrite,
    AutoSelectAll = 0x1000,
    ImGuiInputTextFlags_AutoSelectAll = AutoSelectAll,
    ParseEmptyRefVal = 0x2000,
    ImGuiInputTextFlags_ParseEmptyRefVal = ParseEmptyRefVal,
    DisplayEmptyRefVal = 0x4000,
    ImGuiInputTextFlags_DisplayEmptyRefVal = DisplayEmptyRefVal,
    NoHorizontalScroll = 0x8000,
    ImGuiInputTextFlags_NoHorizontalScroll = NoHorizontalScroll,
    NoUndoRedo = 0x10000,
    ImGuiInputTextFlags_NoUndoRedo = NoUndoRedo,
    ElideLeft = 0x20000,
    ImGuiInputTextFlags_ElideLeft = ElideLeft,
    CallbackCompletion = 0x40000,
    ImGuiInputTextFlags_CallbackCompletion = CallbackCompletion,
    CallbackHistory = 0x80000,
    ImGuiInputTextFlags_CallbackHistory = CallbackHistory,
    CallbackAlways = 0x100000,
    ImGuiInputTextFlags_CallbackAlways = CallbackAlways,
    CallbackCharFilter = 0x200000,
    ImGuiInputTextFlags_CallbackCharFilter = CallbackCharFilter,
    CallbackResize = 0x400000,
    ImGuiInputTextFlags_CallbackResize = CallbackResize,
    CallbackEdit = 0x800000,
    ImGuiInputTextFlags_CallbackEdit = CallbackEdit,
    WordWrap = 0x1000000,
    ImGuiInputTextFlags_WordWrap = WordWrap,
}

/// <summary>
/// Specifies internal flags for text input behavior.
/// </summary>
[Flags]
public enum ImGuiInputTextFlagsPrivate
{
    ImGuiInputTextFlags_Multiline = 0x4000000,
    ImGuiInputTextFlags_TempInput = 0x8000000,
    ImGuiInputTextFlags_LocalizeDecimalPoint = 0x10000000,
}

/// <summary>
/// Specifies public flags that alter item behavior.
/// </summary>
[Flags]
public enum ImGuiItemFlags
{
    None = 0,
    ImGuiItemFlags_None = None,
    NoTabStop = 1,
    ImGuiItemFlags_NoTabStop = NoTabStop,
    NoNav = 2,
    ImGuiItemFlags_NoNav = NoNav,
    NoNavDefaultFocus = 4,
    ImGuiItemFlags_NoNavDefaultFocus = NoNavDefaultFocus,
    ButtonRepeat = 8,
    ImGuiItemFlags_ButtonRepeat = ButtonRepeat,
    AutoClosePopups = 0x10,
    ImGuiItemFlags_AutoClosePopups = AutoClosePopups,
    AllowDuplicateId = 0x20,
    ImGuiItemFlags_AllowDuplicateId = AllowDuplicateId,
    Disabled = 0x40,
    ImGuiItemFlags_Disabled = Disabled,
    LiveEditOnInputText = 0x80,
    ImGuiItemFlags_LiveEditOnInputText = LiveEditOnInputText,
    LiveEditOnInputScalar = 0x100,
    ImGuiItemFlags_LiveEditOnInputScalar = LiveEditOnInputScalar,
    LiveEditOnInput = 0x180,
    ImGuiItemFlags_LiveEditOnInput = LiveEditOnInput,
    MixedValue = 0x200,
    ImGuiItemFlags_MixedValue = MixedValue,
}

/// <summary>
/// Specifies internal flags that alter item behavior.
/// </summary>
[Flags]
public enum ImGuiItemFlagsPrivate
{
    ImGuiItemFlags_ReadOnly = 0x800,
    ImGuiItemFlags_MixedValue = 0x200,
    ImGuiItemFlags_NoWindowHoverableCheck = 0x2000,
    ImGuiItemFlags_AllowOverlap = 0x4000,
    ImGuiItemFlags_NoNavDisableMouseHover = 0x8000,
    ImGuiItemFlags_NoMarkEdited = 0x10000,
    ImGuiItemFlags_NoFocus = 0x20000,
    ImGuiItemFlags_Inputable = 0x100000,
    ImGuiItemFlags_HasSelectionUserData = 0x200000,
    ImGuiItemFlags_IsMultiSelect = 0x400000,
    ImGuiItemFlags_Default_ = 0x90,
}

/// <summary>
/// Specifies the status information reported by the last submitted item.
/// </summary>
[Flags]
public enum ImGuiItemStatusFlags
{
    None = 0,
    ImGuiItemStatusFlags_None = None,
    HoveredRect = 1,
    ImGuiItemStatusFlags_HoveredRect = HoveredRect,
    Hovered = HoveredRect, // Convenience alias for HoveredRect
    HasDisplayRect = 2,
    ImGuiItemStatusFlags_HasDisplayRect = HasDisplayRect,
    Edited = 4,
    ImGuiItemStatusFlags_Edited = Edited,
    Active = 0x4000,       // Interaction status flags
    Clicked = 0x8000,      // Interaction status flags
    ToggledSelection = 8,
    ImGuiItemStatusFlags_ToggledSelection = ToggledSelection,
    ToggledOpen = 0x10,
    ImGuiItemStatusFlags_ToggledOpen = ToggledOpen,
    HasDeactivated = 0x20,
    ImGuiItemStatusFlags_HasDeactivated = HasDeactivated,
    Deactivated = 0x40,
    ImGuiItemStatusFlags_Deactivated = Deactivated,
    HoveredWindow = 0x80,
    ImGuiItemStatusFlags_HoveredWindow = HoveredWindow,
    Visible = 0x100,
    ImGuiItemStatusFlags_Visible = Visible,
    HasClipRect = 0x200,
    ImGuiItemStatusFlags_HasClipRect = HasClipRect,
    HasShortcut = 0x400,
    ImGuiItemStatusFlags_HasShortcut = HasShortcut,
    EditedInternal = 0x800,
    ImGuiItemStatusFlags_EditedInternal = EditedInternal,
    Opened = 0x200000,
    ImGuiItemStatusFlags_Opened = Opened,
    Checkable = 0x400000,
    ImGuiItemStatusFlags_Checkable = Checkable,
    Checked = 0x800000,
    ImGuiItemStatusFlags_Checked = Checked,
    Inputable = 0x1000000,
    ImGuiItemStatusFlags_Inputable = Inputable,
}

/// <summary>
/// Identifies every named key, mouse alias, and modifier combination recognized by the port.
/// </summary>
public enum ImGuiKey
{
    None = 0,
    ImGuiKey_None = None,
    NamedKey_BEGIN = 0x200,
    ImGuiKey_NamedKey_BEGIN = NamedKey_BEGIN,
    Tab = 0x200,
    ImGuiKey_Tab = Tab,
    LeftArrow = 0x201,
    ImGuiKey_LeftArrow = LeftArrow,
    RightArrow = 0x202,
    ImGuiKey_RightArrow = RightArrow,
    UpArrow = 0x203,
    ImGuiKey_UpArrow = UpArrow,
    DownArrow = 0x204,
    ImGuiKey_DownArrow = DownArrow,
    PageUp = 0x205,
    ImGuiKey_PageUp = PageUp,
    PageDown = 0x206,
    ImGuiKey_PageDown = PageDown,
    Home = 0x207,
    ImGuiKey_Home = Home,
    End = 0x208,
    ImGuiKey_End = End,
    Insert = 0x209,
    ImGuiKey_Insert = Insert,
    Delete = 0x20A,
    ImGuiKey_Delete = Delete,
    Backspace = 0x20B,
    ImGuiKey_Backspace = Backspace,
    Space = 0x20C,
    ImGuiKey_Space = Space,
    Enter = 0x20D,
    ImGuiKey_Enter = Enter,
    Escape = 0x20E,
    ImGuiKey_Escape = Escape,
    LeftCtrl = 0x20F,
    ImGuiKey_LeftCtrl = LeftCtrl,
    LeftShift = 0x210,
    ImGuiKey_LeftShift = LeftShift,
    LeftAlt = 0x211,
    ImGuiKey_LeftAlt = LeftAlt,
    LeftSuper = 0x212,
    ImGuiKey_LeftSuper = LeftSuper,
    RightCtrl = 0x213,
    ImGuiKey_RightCtrl = RightCtrl,
    RightShift = 0x214,
    ImGuiKey_RightShift = RightShift,
    RightAlt = 0x215,
    ImGuiKey_RightAlt = RightAlt,
    RightSuper = 0x216,
    ImGuiKey_RightSuper = RightSuper,
    Menu = 0x217,
    ImGuiKey_Menu = Menu,
    _0 = 0x218,
    ImGuiKey_0 = _0,
    _1 = 0x219,
    ImGuiKey_1 = _1,
    _2 = 0x21A,
    ImGuiKey_2 = _2,
    _3 = 0x21B,
    ImGuiKey_3 = _3,
    _4 = 0x21C,
    ImGuiKey_4 = _4,
    _5 = 0x21D,
    ImGuiKey_5 = _5,
    _6 = 0x21E,
    ImGuiKey_6 = _6,
    _7 = 0x21F,
    ImGuiKey_7 = _7,
    _8 = 0x220,
    ImGuiKey_8 = _8,
    _9 = 0x221,
    ImGuiKey_9 = _9,
    A = 0x222,
    ImGuiKey_A = A,
    B = 0x223,
    ImGuiKey_B = B,
    C = 0x224,
    ImGuiKey_C = C,
    D = 0x225,
    ImGuiKey_D = D,
    E = 0x226,
    ImGuiKey_E = E,
    F = 0x227,
    ImGuiKey_F = F,
    G = 0x228,
    ImGuiKey_G = G,
    H = 0x229,
    ImGuiKey_H = H,
    I = 0x22A,
    ImGuiKey_I = I,
    J = 0x22B,
    ImGuiKey_J = J,
    K = 0x22C,
    ImGuiKey_K = K,
    L = 0x22D,
    ImGuiKey_L = L,
    M = 0x22E,
    ImGuiKey_M = M,
    N = 0x22F,
    ImGuiKey_N = N,
    O = 0x230,
    ImGuiKey_O = O,
    P = 0x231,
    ImGuiKey_P = P,
    Q = 0x232,
    ImGuiKey_Q = Q,
    R = 0x233,
    ImGuiKey_R = R,
    S = 0x234,
    ImGuiKey_S = S,
    T = 0x235,
    ImGuiKey_T = T,
    U = 0x236,
    ImGuiKey_U = U,
    V = 0x237,
    ImGuiKey_V = V,
    W = 0x238,
    ImGuiKey_W = W,
    X = 0x239,
    ImGuiKey_X = X,
    Y = 0x23A,
    ImGuiKey_Y = Y,
    Z = 0x23B,
    ImGuiKey_Z = Z,
    F1 = 0x23C,
    ImGuiKey_F1 = F1,
    F2 = 0x23D,
    ImGuiKey_F2 = F2,
    F3 = 0x23E,
    ImGuiKey_F3 = F3,
    F4 = 0x23F,
    ImGuiKey_F4 = F4,
    F5 = 0x240,
    ImGuiKey_F5 = F5,
    F6 = 0x241,
    ImGuiKey_F6 = F6,
    F7 = 0x242,
    ImGuiKey_F7 = F7,
    F8 = 0x243,
    ImGuiKey_F8 = F8,
    F9 = 0x244,
    ImGuiKey_F9 = F9,
    F10 = 0x245,
    ImGuiKey_F10 = F10,
    F11 = 0x246,
    ImGuiKey_F11 = F11,
    F12 = 0x247,
    ImGuiKey_F12 = F12,
    F13 = 0x248,
    ImGuiKey_F13 = F13,
    F14 = 0x249,
    ImGuiKey_F14 = F14,
    F15 = 0x24A,
    ImGuiKey_F15 = F15,
    F16 = 0x24B,
    ImGuiKey_F16 = F16,
    F17 = 0x24C,
    ImGuiKey_F17 = F17,
    F18 = 0x24D,
    ImGuiKey_F18 = F18,
    F19 = 0x24E,
    ImGuiKey_F19 = F19,
    F20 = 0x24F,
    ImGuiKey_F20 = F20,
    F21 = 0x250,
    ImGuiKey_F21 = F21,
    F22 = 0x251,
    ImGuiKey_F22 = F22,
    F23 = 0x252,
    ImGuiKey_F23 = F23,
    F24 = 0x253,
    ImGuiKey_F24 = F24,
    Apostrophe = 0x254,
    ImGuiKey_Apostrophe = Apostrophe,
    Comma = 0x255,
    ImGuiKey_Comma = Comma,
    Minus = 0x256,
    ImGuiKey_Minus = Minus,
    Period = 0x257,
    ImGuiKey_Period = Period,
    Slash = 0x258,
    ImGuiKey_Slash = Slash,
    Semicolon = 0x259,
    ImGuiKey_Semicolon = Semicolon,
    Equal = 0x25A,
    ImGuiKey_Equal = Equal,
    LeftBracket = 0x25B,
    ImGuiKey_LeftBracket = LeftBracket,
    Backslash = 0x25C,
    ImGuiKey_Backslash = Backslash,
    RightBracket = 0x25D,
    ImGuiKey_RightBracket = RightBracket,
    GraveAccent = 0x25E,
    ImGuiKey_GraveAccent = GraveAccent,
    CapsLock = 0x25F,
    ImGuiKey_CapsLock = CapsLock,
    ScrollLock = 0x260,
    ImGuiKey_ScrollLock = ScrollLock,
    NumLock = 0x261,
    ImGuiKey_NumLock = NumLock,
    PrintScreen = 0x262,
    ImGuiKey_PrintScreen = PrintScreen,
    Pause = 0x263,
    ImGuiKey_Pause = Pause,
    Keypad0 = 0x264,
    ImGuiKey_Keypad0 = Keypad0,
    Keypad1 = 0x265,
    ImGuiKey_Keypad1 = Keypad1,
    Keypad2 = 0x266,
    ImGuiKey_Keypad2 = Keypad2,
    Keypad3 = 0x267,
    ImGuiKey_Keypad3 = Keypad3,
    Keypad4 = 0x268,
    ImGuiKey_Keypad4 = Keypad4,
    Keypad5 = 0x269,
    ImGuiKey_Keypad5 = Keypad5,
    Keypad6 = 0x26A,
    ImGuiKey_Keypad6 = Keypad6,
    Keypad7 = 0x26B,
    ImGuiKey_Keypad7 = Keypad7,
    Keypad8 = 0x26C,
    ImGuiKey_Keypad8 = Keypad8,
    Keypad9 = 0x26D,
    ImGuiKey_Keypad9 = Keypad9,
    KeypadDecimal = 0x26E,
    ImGuiKey_KeypadDecimal = KeypadDecimal,
    KeypadDivide = 0x26F,
    ImGuiKey_KeypadDivide = KeypadDivide,
    KeypadMultiply = 0x270,
    ImGuiKey_KeypadMultiply = KeypadMultiply,
    KeypadSubtract = 0x271,
    ImGuiKey_KeypadSubtract = KeypadSubtract,
    KeypadAdd = 0x272,
    ImGuiKey_KeypadAdd = KeypadAdd,
    KeypadEnter = 0x273,
    ImGuiKey_KeypadEnter = KeypadEnter,
    KeypadEqual = 0x274,
    ImGuiKey_KeypadEqual = KeypadEqual,
    AppBack = 0x275,
    ImGuiKey_AppBack = AppBack,
    AppForward = 0x276,
    ImGuiKey_AppForward = AppForward,
    Oem102 = 0x277,
    ImGuiKey_Oem102 = Oem102,
    GamepadStart = 0x278,
    ImGuiKey_GamepadStart = GamepadStart,
    GamepadBack = 0x279,
    ImGuiKey_GamepadBack = GamepadBack,
    GamepadFaceLeft = 0x27A,
    ImGuiKey_GamepadFaceLeft = GamepadFaceLeft,
    GamepadFaceRight = 0x27B,
    ImGuiKey_GamepadFaceRight = GamepadFaceRight,
    GamepadFaceUp = 0x27C,
    ImGuiKey_GamepadFaceUp = GamepadFaceUp,
    GamepadFaceDown = 0x27D,
    ImGuiKey_GamepadFaceDown = GamepadFaceDown,
    GamepadDpadLeft = 0x27E,
    ImGuiKey_GamepadDpadLeft = GamepadDpadLeft,
    GamepadDpadRight = 0x27F,
    ImGuiKey_GamepadDpadRight = GamepadDpadRight,
    GamepadDpadUp = 0x280,
    ImGuiKey_GamepadDpadUp = GamepadDpadUp,
    GamepadDpadDown = 0x281,
    ImGuiKey_GamepadDpadDown = GamepadDpadDown,
    GamepadL1 = 0x282,
    ImGuiKey_GamepadL1 = GamepadL1,
    GamepadR1 = 0x283,
    ImGuiKey_GamepadR1 = GamepadR1,
    GamepadL2 = 0x284,
    ImGuiKey_GamepadL2 = GamepadL2,
    GamepadR2 = 0x285,
    ImGuiKey_GamepadR2 = GamepadR2,
    GamepadL3 = 0x286,
    ImGuiKey_GamepadL3 = GamepadL3,
    GamepadR3 = 0x287,
    ImGuiKey_GamepadR3 = GamepadR3,
    GamepadLStickLeft = 0x288,
    ImGuiKey_GamepadLStickLeft = GamepadLStickLeft,
    GamepadLStickRight = 0x289,
    ImGuiKey_GamepadLStickRight = GamepadLStickRight,
    GamepadLStickUp = 0x28A,
    ImGuiKey_GamepadLStickUp = GamepadLStickUp,
    GamepadLStickDown = 0x28B,
    ImGuiKey_GamepadLStickDown = GamepadLStickDown,
    GamepadRStickLeft = 0x28C,
    ImGuiKey_GamepadRStickLeft = GamepadRStickLeft,
    GamepadRStickRight = 0x28D,
    ImGuiKey_GamepadRStickRight = GamepadRStickRight,
    GamepadRStickUp = 0x28E,
    ImGuiKey_GamepadRStickUp = GamepadRStickUp,
    GamepadRStickDown = 0x28F,
    ImGuiKey_GamepadRStickDown = GamepadRStickDown,
    MouseLeft = 0x290,
    ImGuiKey_MouseLeft = MouseLeft,
    MouseRight = 0x291,
    ImGuiKey_MouseRight = MouseRight,
    MouseMiddle = 0x292,
    ImGuiKey_MouseMiddle = MouseMiddle,
    MouseX1 = 0x293,
    ImGuiKey_MouseX1 = MouseX1,
    MouseX2 = 0x294,
    ImGuiKey_MouseX2 = MouseX2,
    MouseWheelX = 0x295,
    ImGuiKey_MouseWheelX = MouseWheelX,
    MouseWheelY = 0x296,
    ImGuiKey_MouseWheelY = MouseWheelY,
    ReservedForModCtrl = 0x297,
    ImGuiKey_ReservedForModCtrl = ReservedForModCtrl,
    ReservedForModShift = 0x298,
    ImGuiKey_ReservedForModShift = ReservedForModShift,
    ReservedForModAlt = 0x299,
    ImGuiKey_ReservedForModAlt = ReservedForModAlt,
    ReservedForModSuper = 0x29A,
    ImGuiKey_ReservedForModSuper = ReservedForModSuper,
    NamedKey_END = 0x29B,
    ImGuiKey_NamedKey_END = NamedKey_END,
    NamedKey_COUNT = 0x9B,
    ImGuiKey_NamedKey_COUNT = NamedKey_COUNT,
    ImGuiMod_None = 0,
    ImGuiMod_Ctrl = 0x1000,
    ImGuiMod_Shift = 0x2000,
    ImGuiMod_Alt = 0x4000,
    ImGuiMod_Super = 0x8000,
    ImGuiMod_Mask_ = 0xF000,
    ImGuiMod_Shortcut = 0x1000,
}

/// <summary>
/// Identifies the orientation of a layout stack.
/// </summary>
public enum ImGuiLayoutType
{
    Horizontal = 0,
    ImGuiLayoutType_Horizontal = Horizontal,
    Vertical = 1,
    ImGuiLayoutType_Vertical = Vertical,
}

/// <summary>
/// Specifies flags that alter list clipper behavior.
/// </summary>
[Flags]
public enum ImGuiListClipperFlags
{
    None = 0,
    ImGuiListClipperFlags_None = None,
    NoSetTableRowCounters = 1,
    ImGuiListClipperFlags_NoSetTableRowCounters = NoSetTableRowCounters,
}

/// <summary>
/// Identifies a localized string used by built-in widgets.
/// </summary>
public enum ImGuiLocKey
{
    VersionStr = 0,
    ImGuiLocKey_VersionStr = VersionStr,
    TableSizeOne = 1,
    ImGuiLocKey_TableSizeOne = TableSizeOne,
    TableSizeAllFit = 2,
    ImGuiLocKey_TableSizeAllFit = TableSizeAllFit,
    TableSizeAllDefault = 3,
    ImGuiLocKey_TableSizeAllDefault = TableSizeAllDefault,
    TableReset = 4,
    ImGuiLocKey_TableReset = TableReset,
    TableResetOrder = 5,
    ImGuiLocKey_TableResetOrder = TableResetOrder,
    TableResetVisibility = 6,
    ImGuiLocKey_TableResetVisibility = TableResetVisibility,
    WindowingMainMenuBar = 7,
    ImGuiLocKey_WindowingMainMenuBar = WindowingMainMenuBar,
    WindowingPopup = 8,
    ImGuiLocKey_WindowingPopup = WindowingPopup,
    WindowingUntitled = 9,
    ImGuiLocKey_WindowingUntitled = WindowingUntitled,
    OpenLink_s = 0xA,
    ImGuiLocKey_OpenLink_s = OpenLink_s,
    CopyLink = 0xB,
    ImGuiLocKey_CopyLink = CopyLink,
    DockingHideTabBar = 0xC,
    ImGuiLocKey_DockingHideTabBar = DockingHideTabBar,
    DockingHoldShiftToDock = 0xD,
    ImGuiLocKey_DockingHoldShiftToDock = DockingHoldShiftToDock,
    DockingDragToUndockOrMoveNode = 0xE,
    ImGuiLocKey_DockingDragToUndockOrMoveNode = DockingDragToUndockOrMoveNode,
    COUNT = 0xF,
    ImGuiLocKey_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter logging behavior.
/// </summary>
[Flags]
public enum ImGuiLogFlags
{
    None = 0,
    ImGuiLogFlags_None = None,
    OutputTTY = 1,
    ImGuiLogFlags_OutputTTY = OutputTTY,
    OutputFile = 2,
    ImGuiLogFlags_OutputFile = OutputFile,
    OutputBuffer = 4,
    ImGuiLogFlags_OutputBuffer = OutputBuffer,
    OutputClipboard = 8,
    ImGuiLogFlags_OutputClipboard = OutputClipboard,
    OutputMask_ = 0xF,
    ImGuiLogFlags_OutputMask_ = OutputMask_,
}

/// <summary>
/// Identifies a mouse button index.
/// </summary>
public enum ImGuiMouseButton
{
    Left = 0,
    ImGuiMouseButton_Left = Left,
    Right = 1,
    ImGuiMouseButton_Right = Right,
    Middle = 2,
    ImGuiMouseButton_Middle = Middle,
    COUNT = 5,
    ImGuiMouseButton_COUNT = COUNT,
}

/// <summary>
/// Identifies a mouse cursor shape.
/// </summary>
public enum ImGuiMouseCursor
{
    None = -1,
    ImGuiMouseCursor_None = None,
    Arrow = 0,
    ImGuiMouseCursor_Arrow = Arrow,
    TextInput = 1,
    ImGuiMouseCursor_TextInput = TextInput,
    ResizeAll = 2,
    ImGuiMouseCursor_ResizeAll = ResizeAll,
    ResizeNS = 3,
    ImGuiMouseCursor_ResizeNS = ResizeNS,
    ResizeEW = 4,
    ImGuiMouseCursor_ResizeEW = ResizeEW,
    ResizeNESW = 5,
    ImGuiMouseCursor_ResizeNESW = ResizeNESW,
    ResizeNWSE = 6,
    ImGuiMouseCursor_ResizeNWSE = ResizeNWSE,
    Hand = 7,
    ImGuiMouseCursor_Hand = Hand,
    Wait = 8,
    ImGuiMouseCursor_Wait = Wait,
    Progress = 9,
    ImGuiMouseCursor_Progress = Progress,
    NotAllowed = 0xA,
    ImGuiMouseCursor_NotAllowed = NotAllowed,
    COUNT = 0xB,
    ImGuiMouseCursor_COUNT = COUNT,
}

/// <summary>
/// Identifies the peripheral that produced mouse input.
/// </summary>
public enum ImGuiMouseSource
{
    Mouse = 0,
    ImGuiMouseSource_Mouse = Mouse,
    TouchScreen = 1,
    ImGuiMouseSource_TouchScreen = TouchScreen,
    Pen = 2,
    ImGuiMouseSource_Pen = Pen,
    COUNT = 3,
    ImGuiMouseSource_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter multi-selection behavior.
/// </summary>
[Flags]
public enum ImGuiMultiSelectFlags
{
    None = 0,
    ImGuiMultiSelectFlags_None = None,
    SingleSelect = 1,
    ImGuiMultiSelectFlags_SingleSelect = SingleSelect,
    NoSelectAll = 2,
    ImGuiMultiSelectFlags_NoSelectAll = NoSelectAll,
    NoRangeSelect = 4,
    ImGuiMultiSelectFlags_NoRangeSelect = NoRangeSelect,
    NoAutoSelect = 8,
    ImGuiMultiSelectFlags_NoAutoSelect = NoAutoSelect,
    NoAutoClear = 0x10,
    ImGuiMultiSelectFlags_NoAutoClear = NoAutoClear,
    NoAutoClearOnReselect = 0x20,
    ImGuiMultiSelectFlags_NoAutoClearOnReselect = NoAutoClearOnReselect,
    BoxSelect1d = 0x40,
    ImGuiMultiSelectFlags_BoxSelect1d = BoxSelect1d,
    BoxSelect2d = 0x80,
    ImGuiMultiSelectFlags_BoxSelect2d = BoxSelect2d,
    BoxSelectNoScroll = 0x100,
    ImGuiMultiSelectFlags_BoxSelectNoScroll = BoxSelectNoScroll,
    ClearOnEscape = 0x200,
    ImGuiMultiSelectFlags_ClearOnEscape = ClearOnEscape,
    ClearOnClickVoid = 0x400,
    ImGuiMultiSelectFlags_ClearOnClickVoid = ClearOnClickVoid,
    ScopeWindow = 0x800,
    ImGuiMultiSelectFlags_ScopeWindow = ScopeWindow,
    ScopeRect = 0x1000,
    ImGuiMultiSelectFlags_ScopeRect = ScopeRect,
    SelectOnAuto = 0x2000,
    ImGuiMultiSelectFlags_SelectOnAuto = SelectOnAuto,
    SelectOnClickAlways = 0x4000,
    ImGuiMultiSelectFlags_SelectOnClickAlways = SelectOnClickAlways,
    SelectOnClickRelease = 0x8000,
    ImGuiMultiSelectFlags_SelectOnClickRelease = SelectOnClickRelease,
    NavWrapX = 0x10000,
    ImGuiMultiSelectFlags_NavWrapX = NavWrapX,
    NoSelectOnRightClick = 0x20000,
    ImGuiMultiSelectFlags_NoSelectOnRightClick = NoSelectOnRightClick,
    SelectOnMask_ = 0xE000,
    ImGuiMultiSelectFlags_SelectOnMask_ = SelectOnMask_,
    CheckboxMode_ = 0x100000,
    ImGuiMultiSelectFlags_CheckboxMode_ = CheckboxMode_,
}

/// <summary>
/// Identifies the navigation layer used for focus and activation routing.
/// </summary>
public enum ImGuiNavLayer
{
    Main = 0,
    ImGuiNavLayer_Main = Main,
    Menu = 1,
    ImGuiNavLayer_Menu = Menu,
    COUNT = 2,
    ImGuiNavLayer_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter navigation move behavior.
/// </summary>
[Flags]
public enum ImGuiNavMoveFlags
{
    None = 0,
    ImGuiNavMoveFlags_None = None,
    LoopX = 1,
    ImGuiNavMoveFlags_LoopX = LoopX,
    LoopY = 2,
    ImGuiNavMoveFlags_LoopY = LoopY,
    WrapX = 4,
    ImGuiNavMoveFlags_WrapX = WrapX,
    WrapY = 8,
    ImGuiNavMoveFlags_WrapY = WrapY,
    WrapMask_ = 0xF,
    ImGuiNavMoveFlags_WrapMask_ = WrapMask_,
    AllowCurrentNavId = 0x10,
    ImGuiNavMoveFlags_AllowCurrentNavId = AllowCurrentNavId,
    AlsoScoreVisibleSet = 0x20,
    ImGuiNavMoveFlags_AlsoScoreVisibleSet = AlsoScoreVisibleSet,
    ScrollToEdgeY = 0x40,
    ImGuiNavMoveFlags_ScrollToEdgeY = ScrollToEdgeY,
    Forwarded = 0x80,
    ImGuiNavMoveFlags_Forwarded = Forwarded,
    DebugNoResult = 0x100,
    ImGuiNavMoveFlags_DebugNoResult = DebugNoResult,
    FocusApi = 0x200,
    ImGuiNavMoveFlags_FocusApi = FocusApi,
    IsTabbing = 0x400,
    ImGuiNavMoveFlags_IsTabbing = IsTabbing,
    IsPageMove = 0x800,
    ImGuiNavMoveFlags_IsPageMove = IsPageMove,
    Activate = 0x1000,
    ImGuiNavMoveFlags_Activate = Activate,
    NoSelect = 0x2000,
    ImGuiNavMoveFlags_NoSelect = NoSelect,
    NoSetNavCursorVisible = 0x4000,
    ImGuiNavMoveFlags_NoSetNavCursorVisible = NoSetNavCursorVisible,
    NoClearActiveId = 0x8000,
    ImGuiNavMoveFlags_NoClearActiveId = NoClearActiveId,
}

/// <summary>
/// Specifies flags that alter how the navigation cursor is rendered.
/// </summary>
[Flags]
public enum ImGuiNavRenderCursorFlags
{
    None = 0,
    ImGuiNavRenderCursorFlags_None = None,
    Compact = 2,
    ImGuiNavRenderCursorFlags_Compact = Compact,
    AlwaysDraw = 4,
    ImGuiNavRenderCursorFlags_AlwaysDraw = AlwaysDraw,
    NoRounding = 8,
    ImGuiNavRenderCursorFlags_NoRounding = NoRounding,
    ImGuiNavHighlightFlags_None = 0,
    ImGuiNavHighlightFlags_Compact = Compact,
    ImGuiNavHighlightFlags_AlwaysDraw = AlwaysDraw,
    ImGuiNavHighlightFlags_NoRounding = NoRounding,
}

/// <summary>
/// Specifies which per-item overrides have been requested for the next item.
/// </summary>
[Flags]
public enum ImGuiNextItemDataFlags
{
    None = 0,
    ImGuiNextItemDataFlags_None = None,
    HasWidth = 1,
    ImGuiNextItemDataFlags_HasWidth = HasWidth,
    HasOpen = 2,
    ImGuiNextItemDataFlags_HasOpen = HasOpen,
    HasShortcut = 4,
    ImGuiNextItemDataFlags_HasShortcut = HasShortcut,
    HasRefVal = 8,
    ImGuiNextItemDataFlags_HasRefVal = HasRefVal,
    HasStorageID = 0x10,
    ImGuiNextItemDataFlags_HasStorageID = HasStorageID,
    HasColorMarker = 0x20,
    ImGuiNextItemDataFlags_HasColorMarker = HasColorMarker,
}

/// <summary>
/// Specifies which per-window overrides have been requested for the next window.
/// </summary>
[Flags]
public enum ImGuiNextWindowDataFlags
{
    None = 0,
    ImGuiNextWindowDataFlags_None = None,
    HasPos = 1,
    ImGuiNextWindowDataFlags_HasPos = HasPos,
    HasSize = 2,
    ImGuiNextWindowDataFlags_HasSize = HasSize,
    HasContentSize = 4,
    ImGuiNextWindowDataFlags_HasContentSize = HasContentSize,
    HasCollapsed = 8,
    ImGuiNextWindowDataFlags_HasCollapsed = HasCollapsed,
    HasSizeConstraint = 0x10,
    ImGuiNextWindowDataFlags_HasSizeConstraint = HasSizeConstraint,
    HasFocus = 0x20,
    ImGuiNextWindowDataFlags_HasFocus = HasFocus,
    HasBgAlpha = 0x40,
    ImGuiNextWindowDataFlags_HasBgAlpha = HasBgAlpha,
    HasScroll = 0x80,
    ImGuiNextWindowDataFlags_HasScroll = HasScroll,
    HasWindowFlags = 0x100,
    ImGuiNextWindowDataFlags_HasWindowFlags = HasWindowFlags,
    HasChildFlags = 0x200,
    ImGuiNextWindowDataFlags_HasChildFlags = HasChildFlags,
    HasRefreshPolicy = 0x400,
    ImGuiNextWindowDataFlags_HasRefreshPolicy = HasRefreshPolicy,
    HasViewport = 0x800,
    ImGuiNextWindowDataFlags_HasViewport = HasViewport,
    HasDock = 0x1000,
    ImGuiNextWindowDataFlags_HasDock = HasDock,
    HasWindowClass = 0x2000,
    ImGuiNextWindowDataFlags_HasWindowClass = HasWindowClass,
}

/// <summary>
/// Specifies flags for the legacy column layout.
/// </summary>
[Flags]
public enum ImGuiOldColumnFlags
{
    None = 0,
    ImGuiOldColumnFlags_None = None,
    NoBorder = 1,
    ImGuiOldColumnFlags_NoBorder = NoBorder,
    NoResize = 2,
    ImGuiOldColumnFlags_NoResize = NoResize,
    NoPreserveWidths = 4,
    ImGuiOldColumnFlags_NoPreserveWidths = NoPreserveWidths,
    NoForceWithinWindow = 8,
    ImGuiOldColumnFlags_NoForceWithinWindow = NoForceWithinWindow,
    GrowParentContentsSize = 0x10,
    ImGuiOldColumnFlags_GrowParentContentsSize = GrowParentContentsSize,
}

/// <summary>
/// Identifies the plot style used by plot widgets.
/// </summary>
public enum ImGuiPlotType
{
    Lines = 0,
    ImGuiPlotType_Lines = Lines,
    Histogram = 1,
    ImGuiPlotType_Histogram = Histogram,
}

/// <summary>
/// Specifies flags that alter popup behavior.
/// </summary>
[Flags]
public enum ImGuiPopupFlags
{
    None = 0,
    ImGuiPopupFlags_None = None,
    MouseButtonLeft = 4,
    ImGuiPopupFlags_MouseButtonLeft = MouseButtonLeft,
    MouseButtonRight = 8,
    ImGuiPopupFlags_MouseButtonRight = MouseButtonRight,
    MouseButtonMiddle = 0xC,
    ImGuiPopupFlags_MouseButtonMiddle = MouseButtonMiddle,
    NoReopen = 0x20,
    ImGuiPopupFlags_NoReopen = NoReopen,
    NoOpenOverExistingPopup = 0x80,
    ImGuiPopupFlags_NoOpenOverExistingPopup = NoOpenOverExistingPopup,
    NoOpenOverItems = 0x100,
    ImGuiPopupFlags_NoOpenOverItems = NoOpenOverItems,
    AnyPopupId = 0x400,
    ImGuiPopupFlags_AnyPopupId = AnyPopupId,
    AnyPopupLevel = 0x800,
    ImGuiPopupFlags_AnyPopupLevel = AnyPopupLevel,
    AnyPopup = 0xC00,
    ImGuiPopupFlags_AnyPopup = AnyPopup,
    MouseButtonShift_ = 2,
    ImGuiPopupFlags_MouseButtonShift_ = MouseButtonShift_,
    MouseButtonMask_ = 0xC,
    ImGuiPopupFlags_MouseButtonMask_ = MouseButtonMask_,
    InvalidMask_ = 3,
    ImGuiPopupFlags_InvalidMask_ = InvalidMask_,
}

/// <summary>
/// Specifies how popup positions are constrained relative to their parent.
/// </summary>
public enum ImGuiPopupPositionPolicy
{
    Default = 0,
    ImGuiPopupPositionPolicy_Default = Default,
    ComboBox = 1,
    ImGuiPopupPositionPolicy_ComboBox = ComboBox,
    Tooltip = 2,
    ImGuiPopupPositionPolicy_Tooltip = Tooltip,
}

/// <summary>
/// Specifies flags that alter scrolling behavior.
/// </summary>
[Flags]
public enum ImGuiScrollFlags
{
    None = 0,
    ImGuiScrollFlags_None = None,
    KeepVisibleEdgeX = 1,
    ImGuiScrollFlags_KeepVisibleEdgeX = KeepVisibleEdgeX,
    KeepVisibleEdgeY = 2,
    ImGuiScrollFlags_KeepVisibleEdgeY = KeepVisibleEdgeY,
    KeepVisibleCenterX = 4,
    ImGuiScrollFlags_KeepVisibleCenterX = KeepVisibleCenterX,
    KeepVisibleCenterY = 8,
    ImGuiScrollFlags_KeepVisibleCenterY = KeepVisibleCenterY,
    AlwaysCenterX = 0x10,
    ImGuiScrollFlags_AlwaysCenterX = AlwaysCenterX,
    AlwaysCenterY = 0x20,
    ImGuiScrollFlags_AlwaysCenterY = AlwaysCenterY,
    NoScrollParent = 0x40,
    ImGuiScrollFlags_NoScrollParent = NoScrollParent,
    MaskX_ = 0x15,
    ImGuiScrollFlags_MaskX_ = MaskX_,
    MaskY_ = 0x2A,
    ImGuiScrollFlags_MaskY_ = MaskY_,
}

/// <summary>
/// Specifies public flags that alter selectable behavior.
/// </summary>
[Flags]
public enum ImGuiSelectableFlags
{
    None = 0,
    ImGuiSelectableFlags_None = None,
    NoAutoClosePopups = 1,
    ImGuiSelectableFlags_NoAutoClosePopups = NoAutoClosePopups,
    SpanAllColumns = 2,
    ImGuiSelectableFlags_SpanAllColumns = SpanAllColumns,
    AllowDoubleClick = 4,
    ImGuiSelectableFlags_AllowDoubleClick = AllowDoubleClick,
    Disabled = 8,
    ImGuiSelectableFlags_Disabled = Disabled,
    AllowOverlap = 0x10,
    ImGuiSelectableFlags_AllowOverlap = AllowOverlap,
    Highlight = 0x20,
    ImGuiSelectableFlags_Highlight = Highlight,
    SelectOnNav = 0x40,
    ImGuiSelectableFlags_SelectOnNav = SelectOnNav,
}

/// <summary>
/// Specifies internal flags that alter selectable behavior.
/// </summary>
[Flags]
public enum ImGuiSelectableFlagsPrivate
{
    ImGuiSelectableFlags_NoHoldingActiveID = 0x100000,
    ImGuiSelectableFlags_SelectOnClick = 0x400000,
    ImGuiSelectableFlags_SelectOnRelease = 0x800000,
    ImGuiSelectableFlags_SpanAvailWidth = 0x1000000,
    ImGuiSelectableFlags_SetNavIdOnHover = 0x2000000,
    ImGuiSelectableFlags_NoPadWithHalfSpacing = 0x4000000,
    ImGuiSelectableFlags_NoSetKeyOwner = 0x8000000,
}

/// <summary>
/// Identifies the kind of multi-selection request.
/// </summary>
public enum ImGuiSelectionRequestType
{
    None = 0,
    ImGuiSelectionRequestType_None = None,
    SetAll = 1,
    ImGuiSelectionRequestType_SetAll = SetAll,
    SetRange = 2,
    ImGuiSelectionRequestType_SetRange = SetRange,
}

/// <summary>
/// Specifies internal flags derived while drawing a separator.
/// </summary>
[Flags]
public enum ImGuiSeparatorFlags
{
    None = 0,
    ImGuiSeparatorFlags_None = None,
    Horizontal = 1,
    ImGuiSeparatorFlags_Horizontal = Horizontal,
    Vertical = 2,
    ImGuiSeparatorFlags_Vertical = Vertical,
    SpanAllColumns = 4,
    ImGuiSeparatorFlags_SpanAllColumns = SpanAllColumns,
}

/// <summary>
/// Specifies public flags that alter slider and drag behavior.
/// </summary>
[Flags]
public enum ImGuiSliderFlags
{
    None = 0,
    ImGuiSliderFlags_None = None,
    Logarithmic = 0x20,
    ImGuiSliderFlags_Logarithmic = Logarithmic,
    NoRoundToFormat = 0x40,
    ImGuiSliderFlags_NoRoundToFormat = NoRoundToFormat,
    NoInput = 0x80,
    ImGuiSliderFlags_NoInput = NoInput,
    WrapAround = 0x100,
    ImGuiSliderFlags_WrapAround = WrapAround,
    ClampOnInput = 0x200,
    ImGuiSliderFlags_ClampOnInput = ClampOnInput,
    ClampZeroRange = 0x400,
    ImGuiSliderFlags_ClampZeroRange = ClampZeroRange,
    NoSpeedTweaks = 0x800,
    ImGuiSliderFlags_NoSpeedTweaks = NoSpeedTweaks,
    ColorMarkers = 0x1000,
    ImGuiSliderFlags_ColorMarkers = ColorMarkers,
    AlwaysClamp = 0x600,
    ImGuiSliderFlags_AlwaysClamp = AlwaysClamp,
    InvalidMask_ = 0x7000000F,
    ImGuiSliderFlags_InvalidMask_ = InvalidMask_,
}

/// <summary>
/// Specifies internal flags that alter slider and drag behavior.
/// </summary>
[Flags]
public enum ImGuiSliderFlagsPrivate
{
    ImGuiSliderFlags_Vertical = 0x100000,
    ImGuiSliderFlags_ReadOnly = 0x200000,
}

/// <summary>
/// Identifies the direction in which a table column is sorted.
/// </summary>
public enum ImGuiSortDirection
{
    None = 0,
    ImGuiSortDirection_None = None,
    Ascending = 1,
    ImGuiSortDirection_Ascending = Ascending,
    Descending = 2,
    ImGuiSortDirection_Descending = Descending,
}

/// <summary>
/// Identifies a style variable that can be pushed and popped during a frame.
/// </summary>
public enum ImGuiStyleVar
{
    Alpha = 0,
    ImGuiStyleVar_Alpha = Alpha,
    DisabledAlpha = 1,
    ImGuiStyleVar_DisabledAlpha = DisabledAlpha,
    WindowPadding = 2,
    ImGuiStyleVar_WindowPadding = WindowPadding,
    WindowRounding = 3,
    ImGuiStyleVar_WindowRounding = WindowRounding,
    WindowBorderSize = 4,
    ImGuiStyleVar_WindowBorderSize = WindowBorderSize,
    WindowMinSize = 5,
    ImGuiStyleVar_WindowMinSize = WindowMinSize,
    WindowTitleAlign = 6,
    ImGuiStyleVar_WindowTitleAlign = WindowTitleAlign,
    ChildRounding = 7,
    ImGuiStyleVar_ChildRounding = ChildRounding,
    ChildBorderSize = 8,
    ImGuiStyleVar_ChildBorderSize = ChildBorderSize,
    PopupRounding = 9,
    ImGuiStyleVar_PopupRounding = PopupRounding,
    PopupBorderSize = 0xA,
    ImGuiStyleVar_PopupBorderSize = PopupBorderSize,
    FramePadding = 0xB,
    ImGuiStyleVar_FramePadding = FramePadding,
    FrameRounding = 0xC,
    ImGuiStyleVar_FrameRounding = FrameRounding,
    FrameBorderSize = 0xD,
    ImGuiStyleVar_FrameBorderSize = FrameBorderSize,
    ItemSpacing = 0xE,
    ImGuiStyleVar_ItemSpacing = ItemSpacing,
    ItemInnerSpacing = 0xF,
    ImGuiStyleVar_ItemInnerSpacing = ItemInnerSpacing,
    IndentSpacing = 0x10,
    ImGuiStyleVar_IndentSpacing = IndentSpacing,
    CellPadding = 0x11,
    ImGuiStyleVar_CellPadding = CellPadding,
    ScrollbarSize = 0x12,
    ImGuiStyleVar_ScrollbarSize = ScrollbarSize,
    ScrollbarRounding = 0x13,
    ImGuiStyleVar_ScrollbarRounding = ScrollbarRounding,
    ScrollbarPadding = 0x14,
    ImGuiStyleVar_ScrollbarPadding = ScrollbarPadding,
    GrabMinSize = 0x15,
    ImGuiStyleVar_GrabMinSize = GrabMinSize,
    GrabRounding = 0x16,
    ImGuiStyleVar_GrabRounding = GrabRounding,
    ImageRounding = 0x17,
    ImGuiStyleVar_ImageRounding = ImageRounding,
    ImageBorderSize = 0x18,
    ImGuiStyleVar_ImageBorderSize = ImageBorderSize,
    TabRounding = 0x19,
    ImGuiStyleVar_TabRounding = TabRounding,
    TabBorderSize = 0x1A,
    ImGuiStyleVar_TabBorderSize = TabBorderSize,
    TabMinWidthBase = 0x1B,
    ImGuiStyleVar_TabMinWidthBase = TabMinWidthBase,
    TabMinWidthShrink = 0x1C,
    ImGuiStyleVar_TabMinWidthShrink = TabMinWidthShrink,
    TabBarBorderSize = 0x1D,
    ImGuiStyleVar_TabBarBorderSize = TabBarBorderSize,
    TabBarOverlineSize = 0x1E,
    ImGuiStyleVar_TabBarOverlineSize = TabBarOverlineSize,
    TableAngledHeadersAngle = 0x1F,
    ImGuiStyleVar_TableAngledHeadersAngle = TableAngledHeadersAngle,
    TableAngledHeadersTextAlign = 0x20,
    ImGuiStyleVar_TableAngledHeadersTextAlign = TableAngledHeadersTextAlign,
    TreeLinesSize = 0x21,
    ImGuiStyleVar_TreeLinesSize = TreeLinesSize,
    TreeLinesRounding = 0x22,
    ImGuiStyleVar_TreeLinesRounding = TreeLinesRounding,
    MenuItemRounding = 0x23,
    ImGuiStyleVar_MenuItemRounding = MenuItemRounding,
    SelectableRounding = 0x24,
    ImGuiStyleVar_SelectableRounding = SelectableRounding,
    DragDropTargetRounding = 0x25,
    ImGuiStyleVar_DragDropTargetRounding = DragDropTargetRounding,
    ButtonTextAlign = 0x26,
    ImGuiStyleVar_ButtonTextAlign = ButtonTextAlign,
    SelectableTextAlign = 0x27,
    ImGuiStyleVar_SelectableTextAlign = SelectableTextAlign,
    SeparatorSize = 0x28,
    ImGuiStyleVar_SeparatorSize = SeparatorSize,
    SeparatorTextBorderSize = 0x29,
    ImGuiStyleVar_SeparatorTextBorderSize = SeparatorTextBorderSize,
    SeparatorTextAlign = 0x2A,
    ImGuiStyleVar_SeparatorTextAlign = SeparatorTextAlign,
    SeparatorTextPadding = 0x2B,
    ImGuiStyleVar_SeparatorTextPadding = SeparatorTextPadding,
    DockingSeparatorSize = 0x2C,
    ImGuiStyleVar_DockingSeparatorSize = DockingSeparatorSize,
    COUNT = 0x2D,
    ImGuiStyleVar_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter tab bar behavior.
/// </summary>
[Flags]
public enum ImGuiTabBarFlags
{
    None = 0,
    ImGuiTabBarFlags_None = None,
    Reorderable = 1,
    ImGuiTabBarFlags_Reorderable = Reorderable,
    AutoSelectNewTabs = 2,
    ImGuiTabBarFlags_AutoSelectNewTabs = AutoSelectNewTabs,
    TabListPopupButton = 4,
    ImGuiTabBarFlags_TabListPopupButton = TabListPopupButton,
    NoCloseWithMiddleMouseButton = 8,
    ImGuiTabBarFlags_NoCloseWithMiddleMouseButton = NoCloseWithMiddleMouseButton,
    NoTabListScrollingButtons = 0x10,
    ImGuiTabBarFlags_NoTabListScrollingButtons = NoTabListScrollingButtons,
    NoTooltip = 0x20,
    ImGuiTabBarFlags_NoTooltip = NoTooltip,
    DrawSelectedOverline = 0x40,
    ImGuiTabBarFlags_DrawSelectedOverline = DrawSelectedOverline,
    FittingPolicyMixed = 0x80,
    ImGuiTabBarFlags_FittingPolicyMixed = FittingPolicyMixed,
    FittingPolicyShrink = 0x100,
    ImGuiTabBarFlags_FittingPolicyShrink = FittingPolicyShrink,
    FittingPolicyScroll = 0x200,
    ImGuiTabBarFlags_FittingPolicyScroll = FittingPolicyScroll,
    FittingPolicyMask_ = 0x380,
    ImGuiTabBarFlags_FittingPolicyMask_ = FittingPolicyMask_,
    FittingPolicyDefault_ = 0x80,
    ImGuiTabBarFlags_FittingPolicyDefault_ = FittingPolicyDefault_,
}

/// <summary>
/// Specifies internal flags for tab bar behavior.
/// </summary>
[Flags]
public enum ImGuiTabBarFlagsPrivate
{
    ImGuiTabBarFlags_DockNode = 0x100000,
    ImGuiTabBarFlags_IsFocused = 0x200000,
    ImGuiTabBarFlags_SaveSettings = 0x400000,
}

/// <summary>
/// Specifies public flags that alter tab item behavior.
/// </summary>
[Flags]
public enum ImGuiTabItemFlags
{
    None = 0,
    ImGuiTabItemFlags_None = None,
    UnsavedDocument = 1,
    ImGuiTabItemFlags_UnsavedDocument = UnsavedDocument,
    SetSelected = 2,
    ImGuiTabItemFlags_SetSelected = SetSelected,
    NoCloseWithMiddleMouseButton = 4,
    ImGuiTabItemFlags_NoCloseWithMiddleMouseButton = NoCloseWithMiddleMouseButton,
    NoPushId = 8,
    ImGuiTabItemFlags_NoPushId = NoPushId,
    NoTooltip = 0x10,
    ImGuiTabItemFlags_NoTooltip = NoTooltip,
    NoReorder = 0x20,
    ImGuiTabItemFlags_NoReorder = NoReorder,
    Leading = 0x40,
    ImGuiTabItemFlags_Leading = Leading,
    Trailing = 0x80,
    ImGuiTabItemFlags_Trailing = Trailing,
    NoAssumedClosure = 0x100,
    ImGuiTabItemFlags_NoAssumedClosure = NoAssumedClosure,
    Unsorted = 0x800000,
    ImGuiTabItemFlags_Unsorted = Unsorted,
}

/// <summary>
/// Specifies internal flags for tab item behavior.
/// </summary>
[Flags]
public enum ImGuiTabItemFlagsPrivate
{
    SectionMask_ = 0xC0,
    ImGuiTabItemFlags_SectionMask_ = SectionMask_,
    NoCloseButton = 0x100000,
    ImGuiTabItemFlags_NoCloseButton = NoCloseButton,
    Button = 0x200000,
    ImGuiTabItemFlags_Button = Button,
    Invisible = 0x400000,
    ImGuiTabItemFlags_Invisible = Invisible,
    Unsorted = 0x800000,
    ImGuiTabItemFlags_Unsorted = Unsorted,
}

/// <summary>
/// Identifies which table background layer a color applies to.
/// </summary>
public enum ImGuiTableBgTarget
{
    None = 0,
    ImGuiTableBgTarget_None = None,
    RowBg0 = 1,
    ImGuiTableBgTarget_RowBg0 = RowBg0,
    RowBg1 = 2,
    ImGuiTableBgTarget_RowBg1 = RowBg1,
    CellBg = 3,
    ImGuiTableBgTarget_CellBg = CellBg,
}

/// <summary>
/// Specifies flags that alter table column behavior.
/// </summary>
[Flags]
public enum ImGuiTableColumnFlags
{
    None = 0,
    ImGuiTableColumnFlags_None = None,
    Disabled = 1,
    ImGuiTableColumnFlags_Disabled = Disabled,
    DefaultHide = 2,
    ImGuiTableColumnFlags_DefaultHide = DefaultHide,
    DefaultSort = 4,
    ImGuiTableColumnFlags_DefaultSort = DefaultSort,
    WidthStretch = 8,
    ImGuiTableColumnFlags_WidthStretch = WidthStretch,
    WidthFixed = 0x10,
    ImGuiTableColumnFlags_WidthFixed = WidthFixed,
    NoResize = 0x20,
    ImGuiTableColumnFlags_NoResize = NoResize,
    NoReorder = 0x40,
    ImGuiTableColumnFlags_NoReorder = NoReorder,
    NoHide = 0x80,
    ImGuiTableColumnFlags_NoHide = NoHide,
    NoClip = 0x100,
    ImGuiTableColumnFlags_NoClip = NoClip,
    NoSort = 0x200,
    ImGuiTableColumnFlags_NoSort = NoSort,
    NoSortAscending = 0x400,
    ImGuiTableColumnFlags_NoSortAscending = NoSortAscending,
    NoSortDescending = 0x800,
    ImGuiTableColumnFlags_NoSortDescending = NoSortDescending,
    NoHeaderLabel = 0x1000,
    ImGuiTableColumnFlags_NoHeaderLabel = NoHeaderLabel,
    NoHeaderWidth = 0x2000,
    ImGuiTableColumnFlags_NoHeaderWidth = NoHeaderWidth,
    PreferSortAscending = 0x4000,
    ImGuiTableColumnFlags_PreferSortAscending = PreferSortAscending,
    PreferSortDescending = 0x8000,
    ImGuiTableColumnFlags_PreferSortDescending = PreferSortDescending,
    IndentEnable = 0x10000,
    ImGuiTableColumnFlags_IndentEnable = IndentEnable,
    IndentDisable = 0x20000,
    ImGuiTableColumnFlags_IndentDisable = IndentDisable,
    AngledHeader = 0x40000,
    ImGuiTableColumnFlags_AngledHeader = AngledHeader,
    IsEnabled = 0x1000000,
    ImGuiTableColumnFlags_IsEnabled = IsEnabled,
    IsVisible = 0x2000000,
    ImGuiTableColumnFlags_IsVisible = IsVisible,
    IsSorted = 0x4000000,
    ImGuiTableColumnFlags_IsSorted = IsSorted,
    IsHovered = 0x8000000,
    ImGuiTableColumnFlags_IsHovered = IsHovered,
    WidthMask_ = 0x18,
    ImGuiTableColumnFlags_WidthMask_ = WidthMask_,
    IndentMask_ = 0x30000,
    ImGuiTableColumnFlags_IndentMask_ = IndentMask_,
    StatusMask_ = 0xF000000,
    ImGuiTableColumnFlags_StatusMask_ = StatusMask_,
    NoDirectResize_ = 0x40000000,
    ImGuiTableColumnFlags_NoDirectResize_ = NoDirectResize_,
}

/// <summary>
/// Specifies flags that alter table behavior.
/// </summary>
[Flags]
public enum ImGuiTableFlags
{
    None = 0,
    ImGuiTableFlags_None = None,
    Resizable = 1,
    ImGuiTableFlags_Resizable = Resizable,
    Reorderable = 2,
    ImGuiTableFlags_Reorderable = Reorderable,
    Hideable = 4,
    ImGuiTableFlags_Hideable = Hideable,
    Sortable = 8,
    ImGuiTableFlags_Sortable = Sortable,
    NoSavedSettings = 0x10,
    ImGuiTableFlags_NoSavedSettings = NoSavedSettings,
    ContextMenuInBody = 0x20,
    ImGuiTableFlags_ContextMenuInBody = ContextMenuInBody,
    RowBg = 0x40,
    ImGuiTableFlags_RowBg = RowBg,
    BordersInnerH = 0x80,
    ImGuiTableFlags_BordersInnerH = BordersInnerH,
    BordersOuterH = 0x100,
    ImGuiTableFlags_BordersOuterH = BordersOuterH,
    BordersInnerV = 0x200,
    ImGuiTableFlags_BordersInnerV = BordersInnerV,
    BordersOuterV = 0x400,
    ImGuiTableFlags_BordersOuterV = BordersOuterV,
    BordersH = 0x180,
    ImGuiTableFlags_BordersH = BordersH,
    BordersV = 0x600,
    ImGuiTableFlags_BordersV = BordersV,
    BordersInner = 0x280,
    ImGuiTableFlags_BordersInner = BordersInner,
    BordersOuter = 0x500,
    ImGuiTableFlags_BordersOuter = BordersOuter,
    Borders = 0x780,
    ImGuiTableFlags_Borders = Borders,
    NoBordersInBody = 0x800,
    ImGuiTableFlags_NoBordersInBody = NoBordersInBody,
    NoBordersInBodyUntilResize = 0x1000,
    ImGuiTableFlags_NoBordersInBodyUntilResize = NoBordersInBodyUntilResize,
    SizingFixedFit = 0x2000,
    ImGuiTableFlags_SizingFixedFit = SizingFixedFit,
    SizingFixedSame = 0x4000,
    ImGuiTableFlags_SizingFixedSame = SizingFixedSame,
    SizingStretchProp = 0x6000,
    ImGuiTableFlags_SizingStretchProp = SizingStretchProp,
    SizingStretchSame = 0x8000,
    ImGuiTableFlags_SizingStretchSame = SizingStretchSame,
    NoHostExtendX = 0x10000,
    ImGuiTableFlags_NoHostExtendX = NoHostExtendX,
    NoHostExtendY = 0x20000,
    ImGuiTableFlags_NoHostExtendY = NoHostExtendY,
    NoKeepColumnsVisible = 0x40000,
    ImGuiTableFlags_NoKeepColumnsVisible = NoKeepColumnsVisible,
    PreciseWidths = 0x80000,
    ImGuiTableFlags_PreciseWidths = PreciseWidths,
    NoClip = 0x100000,
    ImGuiTableFlags_NoClip = NoClip,
    PadOuterX = 0x200000,
    ImGuiTableFlags_PadOuterX = PadOuterX,
    NoPadOuterX = 0x400000,
    ImGuiTableFlags_NoPadOuterX = NoPadOuterX,
    NoPadInnerX = 0x800000,
    ImGuiTableFlags_NoPadInnerX = NoPadInnerX,
    ScrollX = 0x1000000,
    ImGuiTableFlags_ScrollX = ScrollX,
    ScrollY = 0x2000000,
    ImGuiTableFlags_ScrollY = ScrollY,
    SortMulti = 0x4000000,
    ImGuiTableFlags_SortMulti = SortMulti,
    SortTristate = 0x8000000,
    ImGuiTableFlags_SortTristate = SortTristate,
    HighlightHoveredColumn = 0x10000000,
    ImGuiTableFlags_HighlightHoveredColumn = HighlightHoveredColumn,
    SizingMask_ = 0xE000,
    ImGuiTableFlags_SizingMask_ = SizingMask_,
}

/// <summary>
/// Specifies flags that alter table row behavior.
/// </summary>
[Flags]
public enum ImGuiTableRowFlags
{
    None = 0,
    ImGuiTableRowFlags_None = None,
    Headers = 1,
    ImGuiTableRowFlags_Headers = Headers,
}

/// <summary>
/// Specifies flags that alter text output behavior.
/// </summary>
[Flags]
public enum ImGuiTextFlags
{
    None = 0,
    ImGuiTextFlags_None = None,
    NoWidthForLargeClippedText = 1,
    ImGuiTextFlags_NoWidthForLargeClippedText = NoWidthForLargeClippedText,
}

/// <summary>
/// Specifies flags that alter tooltip behavior.
/// </summary>
[Flags]
public enum ImGuiTooltipFlags
{
    None = 0,
    ImGuiTooltipFlags_None = None,
    OverridePrevious = 2,
    ImGuiTooltipFlags_OverridePrevious = OverridePrevious,
}

/// <summary>
/// Specifies flags that alter tree node behavior and appearance.
/// </summary>
[Flags]
public enum ImGuiTreeNodeFlags
{
    None = 0,
    ImGuiTreeNodeFlags_None = None,
    Selected = 1,
    ImGuiTreeNodeFlags_Selected = Selected,
    Framed = 2,
    ImGuiTreeNodeFlags_Framed = Framed,
    AllowOverlap = 4,
    ImGuiTreeNodeFlags_AllowOverlap = AllowOverlap,
    NoTreePushOnOpen = 8,
    ImGuiTreeNodeFlags_NoTreePushOnOpen = NoTreePushOnOpen,
    NoAutoOpenOnLog = 0x10,
    ImGuiTreeNodeFlags_NoAutoOpenOnLog = NoAutoOpenOnLog,
    DefaultOpen = 0x20,
    ImGuiTreeNodeFlags_DefaultOpen = DefaultOpen,
    OpenOnDoubleClick = 0x40,
    ImGuiTreeNodeFlags_OpenOnDoubleClick = OpenOnDoubleClick,
    OpenOnArrow = 0x80,
    ImGuiTreeNodeFlags_OpenOnArrow = OpenOnArrow,
    Leaf = 0x100,
    ImGuiTreeNodeFlags_Leaf = Leaf,
    Bullet = 0x200,
    ImGuiTreeNodeFlags_Bullet = Bullet,
    FramePadding = 0x400,
    ImGuiTreeNodeFlags_FramePadding = FramePadding,
    SpanAvailWidth = 0x800,
    ImGuiTreeNodeFlags_SpanAvailWidth = SpanAvailWidth,
    SpanFullWidth = 0x1000,
    ImGuiTreeNodeFlags_SpanFullWidth = SpanFullWidth,
    SpanLabelWidth = 0x2000,
    ImGuiTreeNodeFlags_SpanLabelWidth = SpanLabelWidth,
    SpanAllColumns = 0x4000,
    ImGuiTreeNodeFlags_SpanAllColumns = SpanAllColumns,
    LabelSpanAllColumns = 0x8000,
    ImGuiTreeNodeFlags_LabelSpanAllColumns = LabelSpanAllColumns,
    NavLeftJumpsToParent = 0x20000,
    ImGuiTreeNodeFlags_NavLeftJumpsToParent = NavLeftJumpsToParent,
    CollapsingHeader = 0x1A,
    ImGuiTreeNodeFlags_CollapsingHeader = CollapsingHeader,
    DrawLinesNone = 0x40000,
    ImGuiTreeNodeFlags_DrawLinesNone = DrawLinesNone,
    DrawLinesFull = 0x80000,
    ImGuiTreeNodeFlags_DrawLinesFull = DrawLinesFull,
    DrawLinesToNodes = 0x100000,
    ImGuiTreeNodeFlags_DrawLinesToNodes = DrawLinesToNodes,
}

/// <summary>
/// Specifies internal flags for tree node behavior.
/// </summary>
[Flags]
public enum ImGuiTreeNodeFlagsPrivate
{
    ImGuiTreeNodeFlags_NoNavFocus = 0x8000000,
    ImGuiTreeNodeFlags_ClipLabelForTrailingButton = 0x10000000,
    ImGuiTreeNodeFlags_UpsideDownArrow = 0x20000000,
    ImGuiTreeNodeFlags_OpenOnMask_ = 0xC0,
    ImGuiTreeNodeFlags_DrawLinesMask_ = 0x1C0000,
}

/// <summary>
/// Specifies flags that alter typing-select behavior.
/// </summary>
[Flags]
public enum ImGuiTypingSelectFlags
{
    None = 0,
    ImGuiTypingSelectFlags_None = None,
    AllowBackspace = 1,
    ImGuiTypingSelectFlags_AllowBackspace = AllowBackspace,
    AllowSingleCharMode = 2,
    ImGuiTypingSelectFlags_AllowSingleCharMode = AllowSingleCharMode,
}

/// <summary>
/// Specifies flags that describe a platform viewport.
/// </summary>
[Flags]
public enum ImGuiViewportFlags
{
    None = 0,
    ImGuiViewportFlags_None = None,
    IsPlatformWindow = 1,
    ImGuiViewportFlags_IsPlatformWindow = IsPlatformWindow,
    IsPlatformMonitor = 2,
    ImGuiViewportFlags_IsPlatformMonitor = IsPlatformMonitor,
    OwnedByApp = 4,
    ImGuiViewportFlags_OwnedByApp = OwnedByApp,
    NoDecoration = 8,
    ImGuiViewportFlags_NoDecoration = NoDecoration,
    NoTaskBarIcon = 0x10,
    ImGuiViewportFlags_NoTaskBarIcon = NoTaskBarIcon,
    NoFocusOnAppearing = 0x20,
    ImGuiViewportFlags_NoFocusOnAppearing = NoFocusOnAppearing,
    NoFocusOnClick = 0x40,
    ImGuiViewportFlags_NoFocusOnClick = NoFocusOnClick,
    NoInputs = 0x80,
    ImGuiViewportFlags_NoInputs = NoInputs,
    NoRendererClear = 0x100,
    ImGuiViewportFlags_NoRendererClear = NoRendererClear,
    NoAutoMerge = 0x200,
    ImGuiViewportFlags_NoAutoMerge = NoAutoMerge,
    TopMost = 0x400,
    ImGuiViewportFlags_TopMost = TopMost,
    CanHostOtherWindows = 0x800,
    ImGuiViewportFlags_CanHostOtherWindows = CanHostOtherWindows,
    IsMinimized = 0x1000,
    ImGuiViewportFlags_IsMinimized = IsMinimized,
    IsFocused = 0x2000,
    ImGuiViewportFlags_IsFocused = IsFocused,
}

/// <summary>
/// Specifies how a click on the window background is routed.
/// </summary>
[Flags]
public enum ImGuiWindowBgClickFlags
{
    None = 0,
    ImGuiWindowBgClickFlags_None = None,
    Move = 1,
    ImGuiWindowBgClickFlags_Move = Move,
}

/// <summary>
/// Identifies the color slots used by docked window styles.
/// </summary>
public enum ImGuiWindowDockStyleCol
{
    Text = 0,
    ImGuiWindowDockStyleCol_Text = Text,
    TabHovered = 1,
    ImGuiWindowDockStyleCol_TabHovered = TabHovered,
    TabFocused = 2,
    ImGuiWindowDockStyleCol_TabFocused = TabFocused,
    TabSelected = 3,
    ImGuiWindowDockStyleCol_TabSelected = TabSelected,
    TabSelectedOverline = 4,
    ImGuiWindowDockStyleCol_TabSelectedOverline = TabSelectedOverline,
    TabDimmed = 5,
    ImGuiWindowDockStyleCol_TabDimmed = TabDimmed,
    TabDimmedSelected = 6,
    ImGuiWindowDockStyleCol_TabDimmedSelected = TabDimmedSelected,
    TabDimmedSelectedOverline = 7,
    ImGuiWindowDockStyleCol_TabDimmedSelectedOverline = TabDimmedSelectedOverline,
    UnsavedMarker = 8,
    ImGuiWindowDockStyleCol_UnsavedMarker = UnsavedMarker,
    COUNT = 9,
    ImGuiWindowDockStyleCol_COUNT = COUNT,
}

/// <summary>
/// Specifies flags that alter window behavior and decoration.
/// </summary>
[Flags]
public enum ImGuiWindowFlags
{
    None = 0,
    ImGuiWindowFlags_None = None,
    NoTitleBar = 1,
    ImGuiWindowFlags_NoTitleBar = NoTitleBar,
    NoResize = 2,
    ImGuiWindowFlags_NoResize = NoResize,
    NoMove = 4,
    ImGuiWindowFlags_NoMove = NoMove,
    NoScrollbar = 8,
    ImGuiWindowFlags_NoScrollbar = NoScrollbar,
    NoScrollWithMouse = 0x10,
    ImGuiWindowFlags_NoScrollWithMouse = NoScrollWithMouse,
    NoCollapse = 0x20,
    ImGuiWindowFlags_NoCollapse = NoCollapse,
    AlwaysAutoResize = 0x40,
    ImGuiWindowFlags_AlwaysAutoResize = AlwaysAutoResize,
    NoBackground = 0x80,
    ImGuiWindowFlags_NoBackground = NoBackground,
    NoSavedSettings = 0x100,
    ImGuiWindowFlags_NoSavedSettings = NoSavedSettings,
    NoMouseInputs = 0x200,
    ImGuiWindowFlags_NoMouseInputs = NoMouseInputs,
    MenuBar = 0x400,
    ImGuiWindowFlags_MenuBar = MenuBar,
    HorizontalScrollbar = 0x800,
    ImGuiWindowFlags_HorizontalScrollbar = HorizontalScrollbar,
    NoFocusOnAppearing = 0x1000,
    ImGuiWindowFlags_NoFocusOnAppearing = NoFocusOnAppearing,
    NoBringToFrontOnFocus = 0x2000,
    ImGuiWindowFlags_NoBringToFrontOnFocus = NoBringToFrontOnFocus,
    AlwaysVerticalScrollbar = 0x4000,
    ImGuiWindowFlags_AlwaysVerticalScrollbar = AlwaysVerticalScrollbar,
    AlwaysHorizontalScrollbar = 0x8000,
    ImGuiWindowFlags_AlwaysHorizontalScrollbar = AlwaysHorizontalScrollbar,
    NoNavInputs = 0x10000,
    ImGuiWindowFlags_NoNavInputs = NoNavInputs,
    NoNavFocus = 0x20000,
    ImGuiWindowFlags_NoNavFocus = NoNavFocus,
    UnsavedDocument = 0x40000,
    ImGuiWindowFlags_UnsavedDocument = UnsavedDocument,
    NoDocking = 0x80000,
    ImGuiWindowFlags_NoDocking = NoDocking,
    NoNav = 0x30000,
    ImGuiWindowFlags_NoNav = NoNav,
    NoDecoration = 0x2B,
    ImGuiWindowFlags_NoDecoration = NoDecoration,
    NoInputs = 0x30200,
    ImGuiWindowFlags_NoInputs = NoInputs,
    DockNodeHost = 0x800000,
    ImGuiWindowFlags_DockNodeHost = DockNodeHost,
    ChildWindow = 0x1000000,
    ImGuiWindowFlags_ChildWindow = ChildWindow,
    Tooltip = 0x2000000,
    ImGuiWindowFlags_Tooltip = Tooltip,
    Popup = 0x4000000,
    ImGuiWindowFlags_Popup = Popup,
    Modal = 0x8000000,
    ImGuiWindowFlags_Modal = Modal,
    ChildMenu = 0x10000000,
    ImGuiWindowFlags_ChildMenu = ChildMenu,
}

/// <summary>
/// Specifies conditions under which a window requests a refresh.
/// </summary>
[Flags]
public enum ImGuiWindowRefreshFlags
{
    None = 0,
    ImGuiWindowRefreshFlags_None = None,
    TryToAvoidRefresh = 1,
    ImGuiWindowRefreshFlags_TryToAvoidRefresh = TryToAvoidRefresh,
    RefreshOnHover = 2,
    ImGuiWindowRefreshFlags_RefreshOnHover = RefreshOnHover,
    RefreshOnFocus = 4,
    ImGuiWindowRefreshFlags_RefreshOnFocus = RefreshOnFocus,
}

/// <summary>
/// Identifies the pixel format of a texture.
/// </summary>
public enum ImTextureFormat
{
    RGBA32 = 0,
    ImTextureFormat_RGBA32 = RGBA32,
    Alpha8 = 1,
    ImTextureFormat_Alpha8 = Alpha8,
}

/// <summary>
/// Identifies the upload status of a texture.
/// </summary>
public enum ImTextureStatus
{
    OK = 0,
    ImTextureStatus_OK = OK,
    Destroyed = 1,
    ImTextureStatus_Destroyed = Destroyed,
    WantCreate = 2,
    ImTextureStatus_WantCreate = WantCreate,
    WantUpdates = 3,
    ImTextureStatus_WantUpdates = WantUpdates,
    WantDestroy = 4,
    ImTextureStatus_WantDestroy = WantDestroy,
}

/// <summary>
/// Identifies a character class used by text wrapping and filtering.
/// </summary>
public enum ImWcharClass
{
    Blank = 0,
    ImWcharClass_Blank = Blank,
    Punct = 1,
    ImWcharClass_Punct = Punct,
    Other = 2,
    ImWcharClass_Other = Other,
}

#endregion

#region Core Math & Primitives (ImVec2, ImVec4, ImColor, ImRect)

/// <summary>
/// 2D vector for 2D positions and sizes, matching canonical Dear ImGui ImVec2.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImVec2 : IEquatable<ImVec2>
{
    /// <summary>Gets or sets the horizontal component.</summary>
    public float X;
    /// <summary>Gets or sets the vertical component.</summary>
    public float Y;

    /// <summary>Represents the vector with both components set to zero.</summary>
    public static readonly ImVec2 Zero = new(0f, 0f);
    /// <summary>Represents the vector with both components set to one.</summary>
    public static readonly ImVec2 One = new(1f, 1f);

    /// <summary>Initializes a two-component vector.</summary>
    /// <param name="x">The horizontal component.</param>
    /// <param name="y">The vertical component.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImVec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets or sets the component by 0-based coordinate index (0 = X, 1 = Y).</summary>
    /// <param name="index">The 0-based component index.</param>
    /// <returns>The component value.</returns>
    public float this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => index == 0 ? X : Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (index == 0) X = value;
            else Y = value;
        }
    }

    /// <summary>Gets or sets the component by axis (X or Y).</summary>
    /// <param name="axis">The component axis.</param>
    /// <returns>The component value.</returns>
    public float this[ImGuiAxis axis]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => axis == ImGuiAxis.Y ? Y : X;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (axis == ImGuiAxis.Y) Y = value;
            else X = value;
        }
    }

    /// <summary>Converts the vector to a System.Numerics vector.</summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted System.Numerics vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(ImVec2 v) => new(v.X, v.Y);

    /// <summary>Converts a System.Numerics vector to this vector type.</summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImVec2(Vector2 v) => new(v.X, v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator +(ImVec2 lhs, ImVec2 rhs) => new(lhs.X + rhs.X, lhs.Y + rhs.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator -(ImVec2 lhs, ImVec2 rhs) => new(lhs.X - rhs.X, lhs.Y - rhs.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator *(ImVec2 lhs, float rhs) => new(lhs.X * rhs, lhs.Y * rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator *(float lhs, ImVec2 rhs) => new(lhs * rhs.X, lhs * rhs.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator *(ImVec2 lhs, ImVec2 rhs) => new(lhs.X * rhs.X, lhs.Y * rhs.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator /(ImVec2 lhs, float rhs) => new(lhs.X / rhs, lhs.Y / rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator /(ImVec2 lhs, ImVec2 rhs) => new(lhs.X / rhs.X, lhs.Y / rhs.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec2 operator -(ImVec2 v) => new(-v.X, -v.Y);

    /// <summary>Determines whether two vectors have equal components.</summary>
    /// <param name="lhs">The left vector.</param>
    /// <param name="rhs">The right vector.</param>
    /// <returns>True when the components are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ImVec2 lhs, ImVec2 rhs) => lhs.X == rhs.X && lhs.Y == rhs.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ImVec2 lhs, ImVec2 rhs) => !(lhs == rhs);

    /// <summary>Returns the squared magnitude of the vector.</summary>
    /// <returns>The squared magnitude.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float LengthSquared() => X * X + Y * Y;

    /// <summary>Returns the magnitude of the vector.</summary>
    /// <returns>The magnitude.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float Length() => MathF.Sqrt(LengthSquared());

    /// <summary>Returns a unit-length copy of the vector.</summary>
    /// <returns>The normalized vector, or the zero vector when the magnitude is zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 Normalized()
    {
        float len = Length();
        return len > 0f ? this / len : Zero;
    }

    /// <summary>Determines whether this vector equals another vector.</summary>
    /// <param name="other">The vector to compare with.</param>
    /// <returns>True when the vectors are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(ImVec2 other) => this == other;

    /// <summary>Determines whether this vector equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when the object is an equal vector.</returns>
    public override readonly bool Equals(object? obj) => obj is ImVec2 other && Equals(other);

    /// <summary>Returns a hash code derived from the vector components.</summary>
    /// <returns>The hash code.</returns>
    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Returns a readable representation of the vector.</summary>
    /// <returns>The formatted vector text.</returns>
    public override readonly string ToString() => $"({X}, {Y})";
}

/// <summary>
/// 4D vector for clipping rectangles, colors, and coordinates, matching canonical Dear ImGui ImVec4.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImVec4 : IEquatable<ImVec4>
{
    /// <summary>Gets or sets the horizontal component.</summary>
    public float X;
    /// <summary>Gets or sets the vertical component.</summary>
    public float Y;
    /// <summary>Gets or sets the depth component.</summary>
    public float Z;
    /// <summary>Gets or sets the fourth component.</summary>
    public float W;

    /// <summary>Represents the vector with every component set to zero.</summary>
    public static readonly ImVec4 Zero = new(0f, 0f, 0f, 0f);
    /// <summary>Represents the vector with every component set to one.</summary>
    public static readonly ImVec4 One = new(1f, 1f, 1f, 1f);

    /// <summary>Initializes a four-component vector.</summary>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <param name="w">The fourth component.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImVec4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Converts the vector to a System.Numerics vector.</summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted System.Numerics vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector4(ImVec4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>Converts a System.Numerics vector to this vector type.</summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImVec4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec4 operator +(ImVec4 lhs, ImVec4 rhs) => new(lhs.X + rhs.X, lhs.Y + rhs.Y, lhs.Z + rhs.Z, lhs.W + rhs.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec4 operator -(ImVec4 lhs, ImVec4 rhs) => new(lhs.X - rhs.X, lhs.Y - rhs.Y, lhs.Z - rhs.Z, lhs.W - rhs.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec4 operator *(ImVec4 lhs, float rhs) => new(lhs.X * rhs, lhs.Y * rhs, lhs.Z * rhs, lhs.W * rhs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ImVec4 operator /(ImVec4 lhs, float rhs) => new(lhs.X / rhs, lhs.Y / rhs, lhs.Z / rhs, lhs.W / rhs);

    /// <summary>Determines whether two vectors have equal components.</summary>
    /// <param name="lhs">The left vector.</param>
    /// <param name="rhs">The right vector.</param>
    /// <returns>True when the components are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ImVec4 lhs, ImVec4 rhs) => lhs.X == rhs.X && lhs.Y == rhs.Y && lhs.Z == rhs.Z && lhs.W == rhs.W;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ImVec4 lhs, ImVec4 rhs) => !(lhs == rhs);

    /// <summary>Determines whether this vector equals another vector.</summary>
    /// <param name="other">The vector to compare with.</param>
    /// <returns>True when the vectors are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(ImVec4 other) => this == other;

    /// <summary>Determines whether this vector equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when the object is an equal vector.</returns>
    public override readonly bool Equals(object? obj) => obj is ImVec4 other && Equals(other);

    /// <summary>Returns a hash code derived from the vector components.</summary>
    /// <returns>The hash code.</returns>
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <summary>Returns a readable representation of the vector.</summary>
    /// <returns>The formatted vector text.</returns>
    public override readonly string ToString() => $"({X}, {Y}, {Z}, {W})";
}

/// <summary>
/// Helper color structure matching canonical Dear ImGui ImColor.
/// Converts between packed 32-bit RGBA colors and float RGBA representations.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImColor : IEquatable<ImColor>
{
    /// <summary>Gets or sets the red, green, blue, and alpha components of the color.</summary>
    public ImVec4 Value;

    /// <summary>Initializes a color from normalized floating-point components.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImColor(float r, float g, float b, float a = 1f)
    {
        Value = new ImVec4(r, g, b, a);
    }

    /// <summary>Initializes a color from byte components.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImColor(byte r, byte g, byte b, byte a = 255)
    {
        Value = new ImVec4(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    /// <summary>Initializes a color from a packed 32-bit value.</summary>
    /// <param name="u32">The packed color value.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImColor(uint u32)
    {
        float a = ((u32 >> 24) & 0xFF) / 255f;
        float b = ((u32 >> 16) & 0xFF) / 255f;
        float g = ((u32 >> 8) & 0xFF) / 255f;
        float r = (u32 & 0xFF) / 255f;
        Value = new ImVec4(r, g, b, a);
    }

    /// <summary>Packs normalized floating-point components into a 32-bit color value.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    /// <returns>The packed 32-bit color value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack(float r, float g, float b, float a = 1f)
    {
        uint cr = (uint)Math.Clamp((int)(r * 255f + 0.5f), 0, 255);
        uint cg = (uint)Math.Clamp((int)(g * 255f + 0.5f), 0, 255);
        uint cb = (uint)Math.Clamp((int)(b * 255f + 0.5f), 0, 255);
        uint ca = (uint)Math.Clamp((int)(a * 255f + 0.5f), 0, 255);
        return (ca << 24) | (cb << 16) | (cg << 8) | cr;
    }

    /// <summary>Packs byte components into a 32-bit color value.</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    /// <returns>The packed 32-bit color value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pack(byte r, byte g, byte b, byte a = 255)
    {
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    /// <summary>Packs the color into a 32-bit value.</summary>
    /// <returns>The packed 32-bit color value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly uint ToU32() => Pack(Value.X, Value.Y, Value.Z, Value.W);

    /// <summary>Converts the color to its packed 32-bit value.</summary>
    /// <param name="col">The color to convert.</param>
    /// <returns>The packed 32-bit color value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator uint(ImColor col) => col.ToU32();

    /// <summary>Converts a packed 32-bit value to a color.</summary>
    /// <param name="u32">The packed color value.</param>
    /// <returns>The converted color.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImColor(uint u32) => new(u32);

    /// <summary>Converts a four-component vector to a color.</summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted color.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImColor(ImVec4 v) => new(v.X, v.Y, v.Z, v.W);

    /// <summary>Converts the color to a four-component vector.</summary>
    /// <param name="col">The color to convert.</param>
    /// <returns>The color components as a vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImVec4(ImColor col) => col.Value;

    /// <summary>Creates a color from hue, saturation, and value components.</summary>
    /// <param name="h">The hue, in the range zero through one.</param>
    /// <param name="s">The saturation, in the range zero through one.</param>
    /// <param name="v">The value, in the range zero through one.</param>
    /// <param name="a">The alpha component.</param>
    /// <returns>The converted color.</returns>
    public static ImColor HSV(float h, float s, float v, float a = 1f)
    {
        if (s <= 0f)
            return new ImColor(v, v, v, a);

        h = (h - MathF.Floor(h)) * 6f;
        int i = (int)h;
        float f = h - i;
        float p = v * (1f - s);
        float q = v * (1f - s * f);
        float t = v * (1f - s * (1f - f));

        return i switch
        {
            0 => new ImColor(v, t, p, a),
            1 => new ImColor(q, v, p, a),
            2 => new ImColor(p, v, t, a),
            3 => new ImColor(p, q, v, a),
            4 => new ImColor(t, p, v, a),
            _ => new ImColor(v, p, q, a),
        };
    }

    /// <summary>Determines whether this color equals another color.</summary>
    /// <param name="other">The color to compare with.</param>
    /// <returns>True when the colors are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(ImColor other) => Value == other.Value;

    /// <summary>Determines whether this color equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when the object is an equal color.</returns>
    public override readonly bool Equals(object? obj) => obj is ImColor other && Equals(other);

    /// <summary>Returns a hash code derived from the color components.</summary>
    /// <returns>The hash code.</returns>
    public override readonly int GetHashCode() => Value.GetHashCode();

    /// <summary>Returns a readable representation of the color.</summary>
    /// <returns>The formatted color text.</returns>
    public override readonly string ToString() => $"RGBA({Value.X:F2}, {Value.Y:F2}, {Value.Z:F2}, {Value.W:F2})";
}

/// <summary>
/// 2D axis-aligned bounding box matching canonical Dear ImGui ImRect.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ImRect : IEquatable<ImRect>
{
    /// <summary>Gets or sets the minimum corner of the rectangle.</summary>
    public ImVec2 Min;
    /// <summary>Gets or sets the maximum corner of the rectangle.</summary>
    public ImVec2 Max;

    /// <summary>Initializes a rectangle from its minimum and maximum corners.</summary>
    /// <param name="min">The minimum corner.</param>
    /// <param name="max">The maximum corner.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImRect(ImVec2 min, ImVec2 max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>Initializes a rectangle from four corner coordinates.</summary>
    /// <param name="x1">The horizontal coordinate of the minimum corner.</param>
    /// <param name="y1">The vertical coordinate of the minimum corner.</param>
    /// <param name="x2">The horizontal coordinate of the maximum corner.</param>
    /// <param name="y2">The vertical coordinate of the maximum corner.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImRect(float x1, float y1, float x2, float y2)
    {
        Min = new ImVec2(x1, y1);
        Max = new ImVec2(x2, y2);
    }

    /// <summary>Initializes a rectangle from a vector holding its corner coordinates.</summary>
    /// <param name="v">The vector holding the minimum and maximum corner coordinates.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImRect(ImVec4 v)
    {
        Min = new ImVec2(v.X, v.Y);
        Max = new ImVec2(v.Z, v.W);
    }

    /// <summary>Converts the rectangle to a vector holding its corner coordinates.</summary>
    /// <param name="r">The rectangle to convert.</param>
    /// <returns>The corner coordinates as a vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImVec4(ImRect r) => new(r.Min.X, r.Min.Y, r.Max.X, r.Max.Y);

    /// <summary>Converts a vector holding corner coordinates to a rectangle.</summary>
    /// <param name="v">The vector holding the corner coordinates.</param>
    /// <returns>The converted rectangle.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator ImRect(ImVec4 v) => new(v);

    /// <summary>Returns the center point of the rectangle.</summary>
    /// <returns>The center point.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 GetCenter() => new((Min.X + Max.X) * 0.5f, (Min.Y + Max.Y) * 0.5f);

    /// <summary>Returns the width and height of the rectangle.</summary>
    /// <returns>The rectangle size.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 GetSize() => new(Max.X - Min.X, Max.Y - Min.Y);

    /// <summary>Returns the width of the rectangle.</summary>
    /// <returns>The rectangle width.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetWidth() => Max.X - Min.X;

    /// <summary>Returns the height of the rectangle.</summary>
    /// <returns>The rectangle height.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetHeight() => Max.Y - Min.Y;

    /// <summary>Returns the area of the rectangle.</summary>
    /// <returns>The rectangle area.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly float GetArea() => (Max.X - Min.X) * (Max.Y - Min.Y);

    /// <summary>Returns the top-left corner of the rectangle.</summary>
    /// <returns>The top-left corner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 GetTL() => Min;

    /// <summary>Returns the top-right corner of the rectangle.</summary>
    /// <returns>The top-right corner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 GetTR() => new(Max.X, Min.Y);

    /// <summary>Returns the bottom-left corner of the rectangle.</summary>
    /// <returns>The bottom-left corner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 GetBL() => new(Min.X, Max.Y);

    /// <summary>Returns the bottom-right corner of the rectangle.</summary>
    /// <returns>The bottom-right corner.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ImVec2 GetBR() => Max;

    /// <summary>Determines whether the rectangle contains a point.</summary>
    /// <param name="p">The point to test.</param>
    /// <returns>True when the point lies inside the half-open rectangle.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(ImVec2 p) => p.X >= Min.X && p.Y >= Min.Y && p.X < Max.X && p.Y < Max.Y;

    /// <summary>Determines whether the rectangle fully contains another rectangle.</summary>
    /// <param name="r">The rectangle to test.</param>
    /// <returns>True when the other rectangle lies inside this rectangle.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(ImRect r) => r.Min.X >= Min.X && r.Min.Y >= Min.Y && r.Max.X <= Max.X && r.Max.Y <= Max.Y;

    /// <summary>Determines whether the rectangle overlaps another rectangle.</summary>
    /// <param name="r">The rectangle to test.</param>
    /// <returns>True when the rectangles overlap.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Overlaps(ImRect r) => r.Min.Y < Max.Y && r.Max.Y > Min.Y && r.Min.X < Max.X && r.Max.X > Min.X;

    /// <summary>Expands the rectangle to include a point.</summary>
    /// <param name="p">The point to include.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ImVec2 p)
    {
        if (Min.X > p.X) Min.X = p.X;
        if (Min.Y > p.Y) Min.Y = p.Y;
        if (Max.X < p.X) Max.X = p.X;
        if (Max.Y < p.Y) Max.Y = p.Y;
    }

    /// <summary>Expands the rectangle to include another rectangle.</summary>
    /// <param name="r">The rectangle to include.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ImRect r)
    {
        if (Min.X > r.Min.X) Min.X = r.Min.X;
        if (Min.Y > r.Min.Y) Min.Y = r.Min.Y;
        if (Max.X < r.Max.X) Max.X = r.Max.X;
        if (Max.Y < r.Max.Y) Max.Y = r.Max.Y;
    }

    /// <summary>Expands the rectangle horizontally to include a coordinate.</summary>
    /// <param name="x">The horizontal coordinate to include.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddX(float x)
    {
        if (Min.X > x) Min.X = x;
        if (Max.X < x) Max.X = x;
    }

    /// <summary>Expands the rectangle vertically to include a coordinate.</summary>
    /// <param name="y">The vertical coordinate to include.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddY(float y)
    {
        if (Min.Y > y) Min.Y = y;
        if (Max.Y < y) Max.Y = y;
    }

    /// <summary>Expands the rectangle outward by a uniform amount.</summary>
    /// <param name="amount">The amount added to every side.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Expand(float amount)
    {
        Min.X -= amount;
        Min.Y -= amount;
        Max.X += amount;
        Max.Y += amount;
    }

    /// <summary>Expands the rectangle outward by a per-axis amount.</summary>
    /// <param name="amount">The amount added to each axis.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Expand(ImVec2 amount)
    {
        Min.X -= amount.X;
        Min.Y -= amount.Y;
        Max.X += amount.X;
        Max.Y += amount.Y;
    }

    /// <summary>Moves the rectangle by the supplied offset.</summary>
    /// <param name="d">The offset applied to both corners.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Translate(ImVec2 d)
    {
        Min.X += d.X;
        Min.Y += d.Y;
        Max.X += d.X;
        Max.Y += d.Y;
    }

    /// <summary>Clamps the rectangle to the intersection with another rectangle.</summary>
    /// <param name="r">The rectangle to clip against.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClipWith(ImRect r)
    {
        Min = new ImVec2(MathF.Max(Min.X, r.Min.X), MathF.Max(Min.Y, r.Min.Y));
        Max = new ImVec2(MathF.Min(Max.X, r.Max.X), MathF.Min(Max.Y, r.Max.Y));
    }

    /// <summary>Clamps the rectangle to another rectangle and normalizes the result.</summary>
    /// <param name="r">The rectangle to clip against.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClipWithFull(ImRect r)
    {
        Min = new ImVec2(Math.Clamp(Min.X, r.Min.X, r.Max.X), Math.Clamp(Min.Y, r.Min.Y, r.Max.Y));
        Max = new ImVec2(Math.Clamp(Max.X, r.Min.X, r.Max.X), Math.Clamp(Max.Y, r.Min.Y, r.Max.Y));
    }

    /// <summary>Rounds both corners to the nearest integer coordinates toward negative infinity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Floor()
    {
        Min.X = MathF.Floor(Min.X);
        Min.Y = MathF.Floor(Min.Y);
        Max.X = MathF.Floor(Max.X);
        Max.Y = MathF.Floor(Max.Y);
    }

    /// <summary>Determines whether the rectangle has inverted corner ordering.</summary>
    /// <returns>True when a minimum coordinate exceeds its matching maximum coordinate.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool IsInverted() => Min.X > Max.X || Min.Y > Max.Y;

    /// <summary>Determines whether this rectangle equals another rectangle.</summary>
    /// <param name="other">The rectangle to compare with.</param>
    /// <returns>True when the corners are equal.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(ImRect other) => Min == other.Min && Max == other.Max;

    /// <summary>Determines whether this rectangle equals another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True when the object is an equal rectangle.</returns>
    public override readonly bool Equals(object? obj) => obj is ImRect other && Equals(other);

    /// <summary>Returns a hash code derived from the rectangle corners.</summary>
    /// <returns>The hash code.</returns>
    public override readonly int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>Returns a readable representation of the rectangle.</summary>
    /// <returns>The formatted rectangle text.</returns>
    public override readonly string ToString() => $"[{Min} to {Max}] ({GetWidth()}x{GetHeight()})";
}

#endregion

#region Configuration & Context Types (ImGuiStyle, ImGuiIO)

/// <summary>
/// Holds the runtime style metrics and color palette used by the Hikari.ImGui port.
/// </summary>
/// <remarks>
/// <para>
/// Field names, declaration order, and default values follow canonical Dear ImGui
/// <c>ImGuiStyle</c>, declared in <c>ThirdParty/imgui/imgui.h:2394-2496</c>, with defaults taken from
/// <c>ImGuiStyle::ImGuiStyle()</c> in <c>ThirdParty/imgui/imgui.cpp:1535-1627</c>. Metric values are
/// expressed in unscaled logical units; call <see cref="ScaleAllSizes"/> to apply a global display
/// scale.
/// </para>
/// <para>
/// This type is plain single-threaded mutable state. The host application changes it during
/// initialization or between frames, and the widget layer reads it while a frame is being submitted.
/// No internal synchronization is provided.
/// </para>
/// </remarks>
public sealed class ImGuiStyle
{
    /// <summary>
    /// Gets or sets the base font size before external global scale factors are applied. A value of
    /// zero selects the first loaded font size on the first frame. Mirrors <c>ImGuiStyle::FontSizeBase</c>.
    /// </summary>
    public float FontSizeBase { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the main global font scale factor, which the application or the end user may
    /// change. Mirrors <c>ImGuiStyle::FontScaleMain</c>.
    /// </summary>
    public float FontScaleMain { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the additional global font scale factor derived from monitor contents scale.
    /// When <see cref="ImGuiIO.ConfigDpiScaleFonts"/> is enabled, this value is overwritten when the
    /// monitor DPI changes. Mirrors <c>ImGuiStyle::FontScaleDpi</c>.
    /// </summary>
    public float FontScaleDpi { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the global alpha multiplier applied to every rendered element.
    /// </summary>
    public float Alpha { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the additional alpha multiplier applied by disabled scopes. The value multiplies
    /// the current <see cref="Alpha"/>.
    /// </summary>
    public float DisabledAlpha { get; set; } = 0.60f;

    /// <summary>
    /// Gets or sets the padding applied inside a window border.
    /// </summary>
    public ImVec2 WindowPadding { get; set; } = new(8.0f, 8.0f);

    /// <summary>
    /// Gets or sets the radius of window corner rounding. Zero produces rectangular windows.
    /// </summary>
    public float WindowRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around windows.
    /// </summary>
    public float WindowBorderSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the hit-testing extent outside and inside a resizing border. The value also
    /// extends the region used to determine the hovered window.
    /// </summary>
    public float WindowBorderHoverPadding { get; set; } = 4.0f;

    /// <summary>
    /// Gets or sets the global minimum window size.
    /// </summary>
    public ImVec2 WindowMinSize { get; set; } = new(32.0f, 32.0f);

    /// <summary>
    /// Gets or sets the alignment of the title bar text within its available rectangle.
    /// </summary>
    public ImVec2 WindowTitleAlign { get; set; } = new(0.0f, 0.5f);

    /// <summary>
    /// Gets or sets the side of the collapsing and docking button in the window title bar.
    /// </summary>
    public ImGuiDir WindowMenuButtonPosition { get; set; } = ImGuiDir.Left;

    /// <summary>
    /// Gets or sets the radius of child window corner rounding. Zero produces rectangular child windows.
    /// </summary>
    public float ChildRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around child windows.
    /// </summary>
    public float ChildBorderSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the radius of popup window corner rounding. Tooltip windows use
    /// <see cref="WindowRounding"/> instead.
    /// </summary>
    public float PopupRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around popup and tooltip windows.
    /// </summary>
    public float PopupBorderSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the padding applied inside framed rectangles used by most widgets.
    /// </summary>
    public ImVec2 FramePadding { get; set; } = new(4.0f, 3.0f);

    /// <summary>
    /// Gets or sets the radius of frame corner rounding. Zero produces rectangular frames.
    /// </summary>
    public float FrameRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around frames.
    /// </summary>
    public float FrameBorderSize { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the horizontal and vertical spacing between widgets and lines.
    /// </summary>
    public ImVec2 ItemSpacing { get; set; } = new(8.0f, 4.0f);

    /// <summary>
    /// Gets or sets the horizontal and vertical spacing within the elements of a composed widget,
    /// such as a slider and its label.
    /// </summary>
    public ImVec2 ItemInnerSpacing { get; set; } = new(4.0f, 4.0f);

    /// <summary>
    /// Gets or sets the padding within a table cell. The horizontal component is locked for the
    /// entire table, while the vertical component may change between rows.
    /// </summary>
    public ImVec2 CellPadding { get; set; } = new(4.0f, 2.0f);

    /// <summary>
    /// Gets or sets the expansion applied to reactive bounding boxes for touch-based input.
    /// </summary>
    public ImVec2 TouchExtraPadding { get; set; } = new(0.0f, 0.0f);

    /// <summary>
    /// Gets or sets the horizontal indentation applied when entering a tree node or hierarchy level.
    /// </summary>
    public float IndentSpacing { get; set; } = 21.0f;

    /// <summary>
    /// Gets or sets the minimum horizontal spacing between two columns.
    /// </summary>
    public float ColumnsMinSpacing { get; set; } = 6.0f;

    /// <summary>
    /// Gets or sets the width of a vertical scrollbar and the height of a horizontal scrollbar.
    /// </summary>
    public float ScrollbarSize { get; set; } = 14.0f;

    /// <summary>
    /// Gets or sets the radius of scrollbar grab corner rounding.
    /// </summary>
    public float ScrollbarRounding { get; set; } = 9.0f;

    /// <summary>
    /// Gets or sets the padding of a scrollbar grab within its frame, shared by both axes.
    /// </summary>
    public float ScrollbarPadding { get; set; } = 2.0f;

    /// <summary>
    /// Gets or sets the minimum width and height of a grab box used by sliders and scrollbars.
    /// </summary>
    public float GrabMinSize { get; set; } = 12.0f;

    /// <summary>
    /// Gets or sets the radius of grab corner rounding. Zero produces rectangular slider grabs.
    /// </summary>
    public float GrabRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the size in pixels of the dead zone around zero on logarithmic sliders that cross zero.
    /// </summary>
    public float LogSliderDeadzone { get; set; } = 4.0f;

    /// <summary>
    /// Gets or sets the rounding applied to image draws.
    /// </summary>
    public float ImageRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around image draws.
    /// </summary>
    public float ImageBorderSize { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the radius of the upper corners of a tab. Zero produces rectangular tabs.
    /// </summary>
    public float TabRounding { get; set; } = 5.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around tabs.
    /// </summary>
    public float TabBorderSize { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the minimum tab width used to make tabs larger than their contents.
    /// </summary>
    public float TabMinWidthBase { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the minimum tab width after shrinking under the mixed fitting policy.
    /// </summary>
    public float TabMinWidthShrink { get; set; } = 80.0f;

    /// <summary>
    /// Gets or sets the minimum width for showing the close button on a selected tab. A value of
    /// -1 always shows it, 0 shows it while hovered, and <see cref="float.MaxValue"/> never shrinks it.
    /// </summary>
    public float TabCloseButtonMinWidthSelected { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the minimum width for showing the close button on an unselected tab. A value of
    /// -1 always shows it, 0 shows it while hovered, and <see cref="float.MaxValue"/> never shows it.
    /// </summary>
    public float TabCloseButtonMinWidthUnselected { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the tab-bar separator that denotes focus by taking the active tab color.
    /// </summary>
    public float TabBarBorderSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the thickness of the tab-bar overline that highlights the selected tab bar.
    /// </summary>
    public float TabBarOverlineSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the angle of angled table headers, in radians. Canonical values range from -50 to +50 degrees.
    /// </summary>
    public float TableAngledHeadersAngle { get; set; } = 35.0f * (MathF.PI / 180.0f);

    /// <summary>
    /// Gets or sets the alignment of angled table headers within their cell.
    /// </summary>
    public ImVec2 TableAngledHeadersTextAlign { get; set; } = new(0.5f, 0.0f);

    /// <summary>
    /// Gets or sets the default way tree-node connector lines are drawn.
    /// </summary>
    public ImGuiTreeNodeFlags TreeLinesFlags { get; set; } = ImGuiTreeNodeFlags.DrawLinesNone;

    /// <summary>
    /// Gets or sets the thickness of the outlines used while drawing tree-node connector lines.
    /// </summary>
    public float TreeLinesSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the radius of the lines that connect child nodes to the vertical tree line.
    /// </summary>
    public float TreeLinesRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the radius of menu item and menu bar rounding.
    /// </summary>
    public float MenuItemRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the radius of selectable item rounding. Modifying this value is discouraged
    /// because contiguous selections will not render correctly.
    /// </summary>
    public float SelectableRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the radius of the drag-and-drop target frame. A negative value defers to
    /// <see cref="FrameRounding"/>.
    /// </summary>
    public float DragDropTargetRounding { get; set; } = 0.0f;

    /// <summary>
    /// Gets or sets the thickness of the drag-and-drop target border.
    /// </summary>
    public float DragDropTargetBorderSize { get; set; } = 2.0f;

    /// <summary>
    /// Gets or sets the size by which the drag-and-drop target expands beyond the actual target item.
    /// </summary>
    public float DragDropTargetPadding { get; set; } = 3.0f;

    /// <summary>
    /// Gets or sets the size of the red, green, blue, and alpha markers used by color editors and
    /// color-marker drags and sliders.
    /// </summary>
    public float ColorMarkerSize { get; set; } = 3.0f;

    /// <summary>
    /// Gets or sets the side on which the color button appears in the color editor widget.
    /// </summary>
    public ImGuiDir ColorButtonPosition { get; set; } = ImGuiDir.Right;

    /// <summary>
    /// Gets or sets the alignment of button text when the button is larger than the text.
    /// </summary>
    public ImVec2 ButtonTextAlign { get; set; } = new(0.5f, 0.5f);

    /// <summary>
    /// Gets or sets the alignment of selectable text. Keeping the text left-aligned allows multiple
    /// items to share a line.
    /// </summary>
    public ImVec2 SelectableTextAlign { get; set; } = new(0.0f, 0.0f);

    /// <summary>
    /// Gets or sets the thickness of the text caret drawn in text input widgets.
    /// </summary>
    public float InputTextCursorSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the thickness of the separator border. The value must be at least one.
    /// </summary>
    public float SeparatorSize { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the thickness of the border drawn around separator text.
    /// </summary>
    public float SeparatorTextBorderSize { get; set; } = 3.0f;

    /// <summary>
    /// Gets or sets the alignment of text within a separator.
    /// </summary>
    public ImVec2 SeparatorTextAlign { get; set; } = new(0.0f, 0.5f);

    /// <summary>
    /// Gets or sets the horizontal offset of separator text from each separator edge plus spacing on
    /// the other axis.
    /// </summary>
    public ImVec2 SeparatorTextPadding { get; set; } = new(20.0f, 3.0f);

    /// <summary>
    /// Gets or sets the margin by which regular windows are kept visible when moved near display edges.
    /// </summary>
    public ImVec2 DisplayWindowPadding { get; set; } = new(19.0f, 19.0f);

    /// <summary>
    /// Gets or sets the margin by which windows, menus, popups, and tooltips avoid display edges.
    /// </summary>
    public ImVec2 DisplaySafeAreaPadding { get; set; } = new(3.0f, 3.0f);

    /// <summary>
    /// Gets or sets a value indicating whether a docking node draws its own close button for all
    /// docked windows.
    /// </summary>
    public bool DockingNodeHasCloseButton { get; set; } = true;

    /// <summary>
    /// Gets or sets the thickness of the resizing border between docked windows.
    /// </summary>
    public float DockingSeparatorSize { get; set; } = 2.0f;

    /// <summary>
    /// Gets or sets the scale applied to the software-rendered mouse cursor when
    /// <see cref="ImGuiIO.MouseDrawCursor"/> is enabled.
    /// </summary>
    public float MouseCursorScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets a value indicating whether anti-aliased lines and borders are enabled.
    /// </summary>
    public bool AntiAliasedLines { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether anti-aliased lines use textures where possible.
    /// Obsolete since 1.93.0; texture-based line rendering is now standard.
    /// </summary>
    [Obsolete("Obsolete since 1.93.0; texture-based line rendering is now standard.")]
    public bool AntiAliasedLinesUseTex { get; set; } = true;

    /// <summary>
    /// Gets or sets whether anti-aliased line ends are enabled using textures. Defaults to true.
    /// (<c>ThirdParty/imgui/imgui.h:2044</c>)
    /// </summary>
    public bool AntiAliasedLineEnds { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether filled shapes such as rounded rectangles and circles
    /// are anti-aliased.
    /// </summary>
    public bool AntiAliasedFill { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum error (in pixels) for bezier curves, circles and ellipses tessellation. Defaults to 1.25f.
    /// (<c>ThirdParty/imgui/imgui.h:2046</c>)
    /// </summary>
    public float CurveTessellationMaxError { get; set; } = 1.25f;

    /// <summary>
    /// Gets or sets the tessellation tolerance used when sampling Bezier curves without an explicit
    /// segment count. Decreasing the value raises quality at the cost of more geometry.
    /// </summary>
    public float CurveTessellationTol { get; set; } = 1.25f;

    /// <summary>
    /// Gets or sets the maximum pixel error allowed when approximating circles and rounded corners
    /// without an explicit segment count. Obsoleted in 1.93.0 in favor of <see cref="CurveTessellationMaxError"/>.
    /// </summary>
    [Obsolete("Obsolete in 1.93.0; use CurveTessellationMaxError instead.")]
    public float CircleTessellationMaxError
    {
        get => CurveTessellationMaxError;
        set => CurveTessellationMaxError = value;
    }

    /// <summary>
    /// Gets the style color palette indexed by <see cref="ImGuiCol"/>.
    /// </summary>
    public ImVec4[] Colors { get; } = new ImVec4[(int)ImGuiCol.COUNT];

    /// <summary>
    /// Gets or sets the delay before a stationary item may be considered hovered.
    /// </summary>
    public float HoverStationaryDelay { get; set; } = 0.15f;

    /// <summary>
    /// Gets or sets the short hover delay used with stationary hover detection.
    /// </summary>
    public float HoverDelayShort { get; set; } = 0.15f;

    /// <summary>
    /// Gets or sets the normal hover delay used for tooltip activation.
    /// </summary>
    public float HoverDelayNormal { get; set; } = 0.40f;

    /// <summary>
    /// Gets or sets the default hover flags applied when a tooltip is requested from mouse input.
    /// </summary>
    public ImGuiHoveredFlags HoverFlagsForTooltipMouse { get; set; } =
        ImGuiHoveredFlags.Stationary | ImGuiHoveredFlags.DelayShort | ImGuiHoveredFlags.AllowWhenDisabled;

    /// <summary>
    /// Gets or sets the default hover flags applied when a tooltip is requested from keyboard or
    /// gamepad navigation.
    /// </summary>
    public ImGuiHoveredFlags HoverFlagsForTooltipNav { get; set; } =
        ImGuiHoveredFlags.NoSharedDelay | ImGuiHoveredFlags.DelayNormal | ImGuiHoveredFlags.AllowWhenDisabled;

    /// <summary>
    /// Tracks the reference scale applied by <see cref="ScaleAllSizes"/>. Mirrors the internal
    /// canonical field <c>ImGuiStyle::_MainScale</c>.
    /// </summary>
    internal float _MainScale = 1.0f;

    /// <summary>
    /// Holds the base font size requested for the next frame. Mirrors the internal canonical field
    /// <c>ImGuiStyle::_NextFrameFontSizeBase</c>.
    /// </summary>
    internal float _NextFrameFontSizeBase = 0.0f;

    /// <summary>
    /// Initializes a new style instance with canonical defaults and the canonical dark color theme.
    /// </summary>
    /// <remarks>
    /// The property initializers carry the canonical values from <c>ImGuiStyle::ImGuiStyle()</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1535-1627</c>). The constructor applies
    /// <see cref="SetDarkTheme"/> last, matching the canonical call to <c>ImGui::StyleColorsDark</c>.
    /// </remarks>
    public ImGuiStyle()
    {
        SetDarkTheme();
    }

    /// <summary>
    /// Assigns the canonical Dear ImGui dark theme to every color slot.
    /// </summary>
    /// <remarks>
    /// The values reproduce <c>ImGui::StyleColorsDark</c>
    /// (<c>ThirdParty/imgui/imgui_draw.cpp:187-260</c>). Derived slots such as
    /// <see cref="ImGuiCol.CheckboxSelectedBg"/> and the tab colors are computed with the canonical
    /// interpolation expressions instead of hardcoded values.
    /// </remarks>
    public void SetDarkTheme()
    {
        ImVec4[] colors = Colors;

        colors[(int)ImGuiCol.Text] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.TextDisabled] = new(0.58f, 0.58f, 0.58f, 1.00f);
        colors[(int)ImGuiCol.WindowBg] = new(0.18f, 0.18f, 0.18f, 1.00f);
        colors[(int)ImGuiCol.ChildBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.PopupBg] = new(0.08f, 0.08f, 0.08f, 0.94f);
        colors[(int)ImGuiCol.Border] = new(0.43f, 0.43f, 0.50f, 0.50f);
        colors[(int)ImGuiCol.BorderShadow] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.FrameBg] = new(0.10f, 0.10f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.FrameBgHovered] = new(0.19f, 0.25f, 0.31f, 1.00f);
        colors[(int)ImGuiCol.FrameBgActive] = new(0.21f, 0.33f, 0.47f, 1.00f);
        colors[(int)ImGuiCol.TitleBg] = new(0.04f, 0.04f, 0.04f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new(0.16f, 0.29f, 0.48f, 1.00f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new(0.00f, 0.00f, 0.00f, 0.51f);
        colors[(int)ImGuiCol.MenuBarBg] = new(0.14f, 0.14f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new(0.02f, 0.02f, 0.02f, 0.53f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new(0.31f, 0.31f, 0.31f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = new(0.41f, 0.41f, 0.41f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = new(0.51f, 0.51f, 0.51f, 1.00f);
        colors[(int)ImGuiCol.CheckMark] = new(0.26f, 0.59f, 0.98f, 1.00f);
        // Canonical ImGui::StyleColorsDark line 211: CheckboxSelectedBg = ImLerp(FrameBg, FrameBgHovered, 0.65f).
        colors[(int)ImGuiCol.CheckboxSelectedBg] = Lerp(colors[(int)ImGuiCol.FrameBg], colors[(int)ImGuiCol.FrameBgHovered], 0.65f);
        colors[(int)ImGuiCol.SliderGrab] = new(0.24f, 0.52f, 0.88f, 1.00f);
        colors[(int)ImGuiCol.SliderGrabActive] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.Button] = new(0.26f, 0.59f, 0.98f, 0.40f);
        colors[(int)ImGuiCol.ButtonHovered] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.ButtonActive] = new(0.06f, 0.53f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.Header] = new(0.26f, 0.59f, 0.98f, 0.31f);
        colors[(int)ImGuiCol.HeaderHovered] = new(0.26f, 0.59f, 0.98f, 0.80f);
        colors[(int)ImGuiCol.HeaderActive] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.Separator] = colors[(int)ImGuiCol.Border];
        colors[(int)ImGuiCol.SeparatorHovered] = new(0.10f, 0.40f, 0.75f, 0.78f);
        colors[(int)ImGuiCol.SeparatorActive] = new(0.10f, 0.40f, 0.75f, 1.00f);
        colors[(int)ImGuiCol.ResizeGrip] = new(0.26f, 0.59f, 0.98f, 0.20f);
        colors[(int)ImGuiCol.ResizeGripHovered] = new(0.26f, 0.59f, 0.98f, 0.67f);
        colors[(int)ImGuiCol.ResizeGripActive] = new(0.26f, 0.59f, 0.98f, 0.95f);
        colors[(int)ImGuiCol.InputTextCursor] = colors[(int)ImGuiCol.Text];
        colors[(int)ImGuiCol.TabHovered] = colors[(int)ImGuiCol.HeaderHovered];
        colors[(int)ImGuiCol.Tab] = Lerp(colors[(int)ImGuiCol.Header], colors[(int)ImGuiCol.TitleBgActive], 0.80f);
        colors[(int)ImGuiCol.TabSelected] = Lerp(colors[(int)ImGuiCol.HeaderActive], colors[(int)ImGuiCol.TitleBgActive], 0.60f);
        colors[(int)ImGuiCol.TabSelectedOverline] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TabDimmed] = Lerp(colors[(int)ImGuiCol.Tab], colors[(int)ImGuiCol.TitleBg], 0.80f);
        colors[(int)ImGuiCol.TabDimmedSelected] = Lerp(colors[(int)ImGuiCol.TabSelected], colors[(int)ImGuiCol.TitleBg], 0.40f);
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = new(0.50f, 0.50f, 0.50f, 0.00f);
        colors[(int)ImGuiCol.DockingPreview] = new(
            colors[(int)ImGuiCol.HeaderActive].X,
            colors[(int)ImGuiCol.HeaderActive].Y,
            colors[(int)ImGuiCol.HeaderActive].Z,
            colors[(int)ImGuiCol.HeaderActive].W * 0.7f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new(0.10f, 0.10f, 0.10f, 1.00f);
        colors[(int)ImGuiCol.PlotLines] = new(0.61f, 0.61f, 0.61f, 1.00f);
        colors[(int)ImGuiCol.PlotLinesHovered] = new(1.00f, 0.43f, 0.35f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogram] = new(0.90f, 0.70f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.PlotHistogramHovered] = new(1.00f, 0.60f, 0.00f, 1.00f);
        colors[(int)ImGuiCol.TableHeaderBg] = new(0.28f, 0.28f, 0.29f, 1.00f);
        colors[(int)ImGuiCol.TableBorderStrong] = new(0.40f, 0.40f, 0.45f, 1.00f);
        colors[(int)ImGuiCol.TableBorderLight] = new(0.32f, 0.32f, 0.34f, 1.00f);
        colors[(int)ImGuiCol.TableRowBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.TableRowBgAlt] = new(1.00f, 1.00f, 1.00f, 0.06f);
        colors[(int)ImGuiCol.TextLink] = colors[(int)ImGuiCol.HeaderActive];
        colors[(int)ImGuiCol.TextSelectedBg] = new(0.26f, 0.59f, 0.98f, 0.35f);
        colors[(int)ImGuiCol.TreeLines] = colors[(int)ImGuiCol.Border];
        colors[(int)ImGuiCol.DragDropTarget] = new(1.00f, 1.00f, 0.00f, 0.90f);
        colors[(int)ImGuiCol.DragDropTargetBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        colors[(int)ImGuiCol.UnsavedMarker] = new(1.00f, 1.00f, 1.00f, 1.00f);
        colors[(int)ImGuiCol.NavCursor] = new(0.26f, 0.59f, 0.98f, 1.00f);
        colors[(int)ImGuiCol.NavWindowingHighlight] = new(1.00f, 1.00f, 1.00f, 0.70f);
        colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.80f, 0.80f, 0.80f, 0.20f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new(0.80f, 0.80f, 0.80f, 0.35f);
    }

    /// <summary>
    /// Scales every spacing, padding, and thickness metric by the supplied factor.
    /// </summary>
    /// <param name="scaleFactor">The factor applied to each scalable style metric. Values are truncated toward zero.</param>
    /// <remarks>
    /// <para>
    /// The operation reproduces <c>ImGuiStyle::ScaleAllSizes</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1628-1679</c>). Every scaled metric is truncated toward zero,
    /// matching the canonical <c>ImTrunc</c> semantics in <c>imgui_internal.h:544-545</c>.
    /// </para>
    /// <para>
    /// The operation is lossy because every size is rounded to an integer. Apply it once to a freshly
    /// initialized style rather than scaling repeatedly.
    /// </para>
    /// </remarks>
    public void ScaleAllSizes(float scaleFactor)
    {
        _MainScale *= scaleFactor;
        WindowPadding = Trunc(WindowPadding * scaleFactor);
        WindowRounding = Trunc(WindowRounding * scaleFactor);
        WindowBorderSize = Trunc(WindowBorderSize * scaleFactor);
        WindowMinSize = Trunc(WindowMinSize * scaleFactor);
        WindowBorderHoverPadding = Trunc(WindowBorderHoverPadding * scaleFactor);
        ChildRounding = Trunc(ChildRounding * scaleFactor);
        ChildBorderSize = Trunc(ChildBorderSize * scaleFactor);
        PopupRounding = Trunc(PopupRounding * scaleFactor);
        PopupBorderSize = Trunc(PopupBorderSize * scaleFactor);
        FramePadding = Trunc(FramePadding * scaleFactor);
        FrameBorderSize = Trunc(FrameBorderSize * scaleFactor);
        FrameRounding = Trunc(FrameRounding * scaleFactor);
        ItemSpacing = Trunc(ItemSpacing * scaleFactor);
        ItemInnerSpacing = Trunc(ItemInnerSpacing * scaleFactor);
        CellPadding = Trunc(CellPadding * scaleFactor);
        TouchExtraPadding = Trunc(TouchExtraPadding * scaleFactor);
        IndentSpacing = Trunc(IndentSpacing * scaleFactor);
        ColumnsMinSpacing = Trunc(ColumnsMinSpacing * scaleFactor);
        ScrollbarSize = Trunc(ScrollbarSize * scaleFactor);
        ScrollbarRounding = Trunc(ScrollbarRounding * scaleFactor);
        ScrollbarPadding = Trunc(ScrollbarPadding * scaleFactor);
        GrabMinSize = Trunc(GrabMinSize * scaleFactor);
        GrabRounding = Trunc(GrabRounding * scaleFactor);
        LogSliderDeadzone = Trunc(LogSliderDeadzone * scaleFactor);
        ImageRounding = Trunc(ImageRounding * scaleFactor);
        ImageBorderSize = Trunc(ImageBorderSize * scaleFactor);
        TabRounding = Trunc(TabRounding * scaleFactor);
        TabBorderSize = Trunc(TabBorderSize * scaleFactor);
        TabMinWidthBase = Trunc(TabMinWidthBase * scaleFactor);
        TabMinWidthShrink = Trunc(TabMinWidthShrink * scaleFactor);
        TabCloseButtonMinWidthSelected = (TabCloseButtonMinWidthSelected > 0.0f && TabCloseButtonMinWidthSelected != float.MaxValue)
            ? Trunc(TabCloseButtonMinWidthSelected * scaleFactor)
            : TabCloseButtonMinWidthSelected;
        TabCloseButtonMinWidthUnselected = (TabCloseButtonMinWidthUnselected > 0.0f && TabCloseButtonMinWidthUnselected != float.MaxValue)
            ? Trunc(TabCloseButtonMinWidthUnselected * scaleFactor)
            : TabCloseButtonMinWidthUnselected;
        TabBarBorderSize = Trunc(TabBarBorderSize * scaleFactor);
        TabBarOverlineSize = Trunc(TabBarOverlineSize * scaleFactor);
        TreeLinesSize = Trunc(TreeLinesSize * scaleFactor);
        TreeLinesRounding = Trunc(TreeLinesRounding * scaleFactor);
        MenuItemRounding = Trunc(MenuItemRounding * scaleFactor);
        SelectableRounding = Trunc(SelectableRounding * scaleFactor);
        DragDropTargetRounding = Trunc(DragDropTargetRounding * scaleFactor);
        DragDropTargetBorderSize = Trunc(DragDropTargetBorderSize * scaleFactor);
        DragDropTargetPadding = Trunc(DragDropTargetPadding * scaleFactor);
        ColorMarkerSize = Trunc(ColorMarkerSize * scaleFactor);
        InputTextCursorSize = Trunc(InputTextCursorSize * scaleFactor);
        SeparatorSize = Trunc(SeparatorSize * scaleFactor);
        SeparatorTextBorderSize = Trunc(SeparatorTextBorderSize * scaleFactor);
        SeparatorTextPadding = Trunc(SeparatorTextPadding * scaleFactor);
        DockingSeparatorSize = Trunc(DockingSeparatorSize * scaleFactor);
        DisplayWindowPadding = Trunc(DisplayWindowPadding * scaleFactor);
        DisplaySafeAreaPadding = Trunc(DisplaySafeAreaPadding * scaleFactor);
        MouseCursorScale = Trunc(MouseCursorScale * scaleFactor);
    }

    /// <summary>
    /// Gets the packed 32-bit color for the specified palette index with an additional alpha multiplier.
    /// </summary>
    /// <param name="idx">The palette index whose color is packed.</param>
    /// <param name="alphaMul">The additional alpha multiplier applied on top of the global style alpha.</param>
    /// <returns>The color packed into a 32-bit RGBA value, or opaque white when the index is out of range.</returns>
    public uint GetColorU32(ImGuiCol idx, float alphaMul = 1.0f)
    {
        int i = (int)idx;
        if ((uint)i >= (uint)Colors.Length)
            return 0xFFFFFFFF;

        ImVec4 c = Colors[i];
        float a = Math.Clamp(c.W * Alpha * alphaMul, 0.0f, 1.0f);
        return ImColor.Pack(c.X, c.Y, c.Z, a);
    }

    /// <summary>
    /// Truncates a floating-point style metric toward zero, matching canonical <c>ImTrunc(float)</c>.
    /// </summary>
    /// <param name="value">The metric value to truncate.</param>
    /// <returns>The truncated metric value.</returns>
    private static float Trunc(float value) => (float)(int)value;

    /// <summary>
    /// Truncates both components of a vector style metric toward zero, matching canonical
    /// <c>ImTrunc(const ImVec2&amp;)</c>.
    /// </summary>
    /// <param name="value">The vector metric to truncate.</param>
    /// <returns>The truncated vector metric.</returns>
    private static ImVec2 Trunc(ImVec2 value) => new((float)(int)value.X, (float)(int)value.Y);

    /// <summary>
    /// Linearly interpolates between two colors, matching canonical <c>ImLerp</c>.
    /// </summary>
    /// <param name="a">The color returned when the interpolation factor is zero.</param>
    /// <param name="b">The color returned when the interpolation factor is one.</param>
    /// <param name="t">The interpolation factor.</param>
    /// <returns>The interpolated color.</returns>
    private static ImVec4 Lerp(ImVec4 a, ImVec4 b, float t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t,
        a.W + (b.W - a.W) * t);
}

/// <summary>
/// Stores the current input state of a single named key, matching canonical <c>ImGuiKeyData</c>.
/// </summary>
/// <remarks>
/// The canonical declaration is in <c>ThirdParty/imgui/imgui.h:2509-2514</c>. Instances are stored in
/// <see cref="ImGuiIO.KeysData"/> and indexed with <c>key - ImGuiKey.NamedKey_BEGIN</c>.
/// </remarks>
public struct ImGuiKeyData
{
    /// <summary>
    /// Gets or sets a value indicating whether the key is currently held down.
    /// </summary>
    public bool Down;

    /// <summary>
    /// Gets or sets how long the key has been held, in seconds. Negative values indicate that the
    /// key is not pressed, zero means the key was just pressed, and positive values mean the key is
    /// being held.
    /// </summary>
    public float DownDuration;

    /// <summary>
    /// Gets or sets the duration the key had been held on the previous frame.
    /// </summary>
    public float DownDurationPrev;

    /// <summary>
    /// Gets or sets the analog value reported for analog keys, in the range zero through one.
    /// </summary>
    public float AnalogValue;
}

/// <summary>
/// Carries host configuration, queued input, and per-frame output flags for the Hikari.ImGui port.
/// </summary>
/// <remarks>
/// <para>
/// Field names, declaration order, and default values follow canonical Dear ImGui <c>ImGuiIO</c>,
/// declared in <c>ThirdParty/imgui/imgui.h:2517-2900</c>, with defaults taken from the
/// <c>ImGuiIO::ImGuiIO()</c> constructor in <c>ThirdParty/imgui/imgui.cpp:1680-1800</c>.
/// </para>
/// <para>
/// The host backend writes configuration and calls the <c>Add*</c> input methods before a frame is
/// submitted, then reads the output flags. This port applies queued events to the current input state
/// immediately, so the canonical per-frame event queue is not modeled yet; the event methods document
/// their canonical counterparts for the later input-processing chunk.
/// </para>
/// <para>
/// This type is plain single-threaded mutable state and provides no internal synchronization.
/// </para>
/// </remarks>
public sealed class ImGuiIO
{
    /// <summary>
    /// Gets or sets the configuration flags that enable optional features such as keyboard navigation,
    /// docking, and multi-viewport support.
    /// </summary>
    public ImGuiConfigFlags ConfigFlags { get; set; } = ImGuiConfigFlags.None;

    /// <summary>
    /// Gets or sets the backend capability flags that communicate which optional features the backend supports.
    /// </summary>
    public ImGuiBackendFlags BackendFlags { get; set; } = ImGuiBackendFlags.None;

    /// <summary>
    /// Gets or sets the main display size in pixels, equivalent to the main viewport size.
    /// </summary>
    public ImVec2 DisplaySize { get; set; } = new(-1.0f, -1.0f);

    /// <summary>
    /// Gets or sets the density ratio between display coordinates and framebuffer coordinates, which
    /// affects font rasterization and the draw data framebuffer scale.
    /// </summary>
    public ImVec2 DisplayFramebufferScale { get; set; } = new(1.0f, 1.0f);

    /// <summary>
    /// Gets or sets the time elapsed since the previous frame, in seconds.
    /// </summary>
    public float DeltaTime { get; set; } = 1.0f / 60.0f;

    /// <summary>
    /// Gets or sets the minimum interval between automatic layout saves to the settings file, in seconds.
    /// </summary>
    public float IniSavingRate { get; set; } = 5.0f;

    /// <summary>
    /// Gets or sets the path of the layout settings file. A null value disables automatic loading and saving.
    /// </summary>
    public string? IniFilename { get; set; } = "imgui.ini";

    /// <summary>
    /// Gets or sets the path of the log file used when logging to a file without an explicit path.
    /// </summary>
    public string? LogFilename { get; set; } = "imgui_log.txt";

    /// <summary>
    /// Gets or sets caller-owned data that the host associates with this input/output structure.
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// Gets or sets the font atlas owned by the host. The atlas supplies rasterized glyphs to the draw layer.
    /// </summary>
    public ImFontAtlas Fonts { get; set; }

    /// <summary>
    /// Gets or sets the font used at the start of a frame. A null value selects the first font in the atlas.
    /// </summary>
    public ImFont? FontDefault { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user may scale the text of individual windows.
    /// </summary>
    public bool FontAllowUserScaling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the activate and cancel gamepad buttons are swapped to
    /// the Nintendo-style layout.
    /// </summary>
    public bool ConfigNavSwapGamepadButtons { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether directional navigation teleports the mouse cursor.
    /// </summary>
    public bool ConfigNavMoveSetMousePos { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether keyboard capture is requested while navigation is active.
    /// </summary>
    public bool ConfigNavCaptureKeyboard { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether pressing Escape clears the focused item and navigation state.
    /// </summary>
    public bool ConfigNavEscapeClearFocusItem { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether pressing Escape also clears the focused window.
    /// </summary>
    public bool ConfigNavEscapeClearFocusWindow { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether navigation input makes the navigation cursor visible.
    /// </summary>
    public bool ConfigNavCursorVisibleAuto { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the navigation cursor is always visible.
    /// </summary>
    public bool ConfigNavCursorVisibleAlways { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether docking disables window splitting.
    /// </summary>
    public bool ConfigDockingNoSplit { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether docking disables merging windows into a shared tab bar.
    /// </summary>
    public bool ConfigDockingNoDockingOver { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether docking requires the Shift key to be held.
    /// </summary>
    public bool ConfigDockingWithShift { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every floating window is displayed inside a docking node.
    /// </summary>
    public bool ConfigDockingAlwaysTabBar { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether docked payloads are rendered transparently.
    /// </summary>
    public bool ConfigDockingTransparentPayload { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether floating windows always create their own platform viewport.
    /// </summary>
    public bool ConfigViewportsNoAutoMerge { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether secondary viewports omit the task bar icon.
    /// </summary>
    public bool ConfigViewportsNoTaskBarIcon { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether secondary viewports omit operating-system window decorations.
    /// </summary>
    public bool ConfigViewportsNoDecoration { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether secondary viewports omit the default parent relationship.
    /// </summary>
    public bool ConfigViewportsNoDefaultParent { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether focusing a platform window transfers focus to the
    /// corresponding Hikari.ImGui window.
    /// </summary>
    public bool ConfigViewportsPlatformFocusSetsImGuiFocus { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the DPI scale factor is copied into the style when
    /// the monitor DPI changes.
    /// </summary>
    public bool ConfigDpiScaleFonts { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Hikari.ImGui and platform windows are scaled when the
    /// monitor DPI changes.
    /// </summary>
    public bool ConfigDpiScaleViewports { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether macOS-style keyboard and text-editing behaviors are enabled.
    /// </summary>
    public bool ConfigMacOSXBehaviors { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether events submitted during one frame are spread over
    /// several frames to improve interaction at low framerates.
    /// </summary>
    public bool ConfigInputTrickleEventQueue { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the text input caret blinks.
    /// </summary>
    public bool ConfigInputTextCursorBlink { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether pressing Enter in a single-line text field keeps the
    /// field active and selects all text.
    /// </summary>
    public bool ConfigInputTextEnterKeepActive { get; set; }

    /// <summary>
    /// Gets or sets the default flags applied to color edit and color picker widgets.
    /// </summary>
    public ImGuiColorEditFlags ConfigColorEditFlags { get; set; } = ImGuiColorEditFlags.DefaultOptions_;

    /// <summary>
    /// Gets or sets a value indicating whether a click without movement turns a drag widget into text input.
    /// </summary>
    public bool ConfigDragClickToInputText { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether windows can be resized from their edges and corners.
    /// </summary>
    public bool ConfigWindowsResizeFromEdges { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether windows can be moved only by dragging their title bar.
    /// </summary>
    public bool ConfigWindowsMoveFromTitleBarOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether pressing Control and C copies the focused window contents.
    /// </summary>
    public bool ConfigWindowsCopyContentsWithCtrlC { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether clicking outside a scrollbar grab scrolls by page
    /// rather than moving the grab to the clicked position.
    /// </summary>
    public bool ConfigScrollbarScrollByPage { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the last-used date is stored in layout settings.
    /// </summary>
    public bool ConfigIniSettingsSaveLastUsedDate { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of months after which unused layout entries are discarded on load.
    /// </summary>
    public int ConfigIniSettingsAutoDiscardMonths { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether extra comments are written into the layout settings file.
    /// </summary>
    public bool ConfigDebugIniSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the library draws a software mouse cursor on behalf of
    /// a platform that cannot provide one.
    /// </summary>
    public bool MouseDrawCursor { get; set; }

    /// <summary>
    /// Gets or sets the interval in seconds after which unused transient memory buffers are released.
    /// A negative value disables automatic release.
    /// </summary>
    public float ConfigMemoryCompactTimer { get; set; } = 60.0f;

    /// <summary>
    /// Gets or sets the interval within which consecutive clicks count as a double click, in seconds.
    /// </summary>
    public float MouseDoubleClickTime { get; set; } = 0.30f;

    /// <summary>
    /// Gets or sets the maximum distance between consecutive clicks for them to count as a double
    /// click, in pixels.
    /// </summary>
    public float MouseDoubleClickMaxDist { get; set; } = 6.0f;

    /// <summary>
    /// Gets or sets the delay used to disambiguate a delayed single click from a double click, in seconds.
    /// </summary>
    public float MouseSingleClickDelay { get; set; } = 0.50f;

    /// <summary>
    /// Gets or sets the distance the mouse must travel before a press becomes a drag, in pixels.
    /// </summary>
    public float MouseDragThreshold { get; set; } = 6.0f;

    /// <summary>
    /// Gets or sets the delay before a held key or button begins repeating, in seconds.
    /// </summary>
    public float KeyRepeatDelay { get; set; } = 0.275f;

    /// <summary>
    /// Gets or sets the interval between key or button repeats, in seconds.
    /// </summary>
    public float KeyRepeatRate { get; set; } = 0.050f;

    /// <summary>
    /// Gets or sets a value indicating whether recoverable error handling is enabled.
    /// </summary>
    public bool ConfigErrorRecovery { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether recoverable errors raise assertions.
    /// </summary>
    public bool ConfigErrorRecoveryEnableAssert { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether recoverable errors are written to the debug log.
    /// </summary>
    public bool ConfigErrorRecoveryEnableDebugLog { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether recoverable errors display an explanatory tooltip.
    /// </summary>
    public bool ConfigErrorRecoveryEnableTooltip { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether debugger break tools are exposed.
    /// </summary>
    public bool ConfigDebugIsDebuggerPresent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether conflicting widget identifiers are highlighted.
    /// </summary>
    public bool ConfigDebugHighlightIdConflicts { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the identifier conflict popup exposes an item picker button.
    /// </summary>
    public bool ConfigDebugHighlightIdConflictsShowItemPicker { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the first call to a begin function returns false to
    /// exercise caller error handling.
    /// </summary>
    public bool ConfigDebugBeginReturnValueOnce { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether begin calls periodically return false to exercise
    /// caller error handling.
    /// </summary>
    public bool ConfigDebugBeginReturnValueLoop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether focus-loss input clearing is suppressed.
    /// </summary>
    public bool ConfigDebugIgnoreFocusLoss { get; set; }

    /// <summary>
    /// Gets or sets whether ImDrawList vector rendering defaults to legacy stroke mode (+0.5f offset).
    /// (<c>ThirdParty/imgui/imgui.h:2671</c>)
    /// </summary>
    public bool ConfigDebugDrawListDefaultsToStrokeLegacy { get; set; }

    /// <summary>
    /// Gets or sets the informational name of the platform backend displayed in the about window.
    /// </summary>
    public string? BackendPlatformName { get; set; }

    /// <summary>
    /// Gets or sets the informational name of the renderer backend displayed in the about window.
    /// </summary>
    public string? BackendRendererName { get; set; }

    /// <summary>
    /// Gets or sets caller-owned data reserved for the platform backend.
    /// </summary>
    public object? BackendPlatformUserData { get; set; }

    /// <summary>
    /// Gets or sets caller-owned data reserved for the renderer backend.
    /// </summary>
    public object? BackendRendererUserData { get; set; }

    /// <summary>
    /// Gets or sets caller-owned data reserved for the language binding backend.
    /// </summary>
    public object? BackendLanguageUserData { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether mouse input is being consumed by Hikari.ImGui. Hosts
    /// use this flag to decide whether to forward mouse input to the application.
    /// </summary>
    public bool WantCaptureMouse { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether keyboard input is being consumed by Hikari.ImGui.
    /// Hosts use this flag to decide whether to forward keyboard input to the application.
    /// </summary>
    public bool WantCaptureKeyboard { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether textual keyboard input is requested, which hosts on
    /// mobile and console platforms use to display an on-screen keyboard.
    /// </summary>
    public bool WantTextInput { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the mouse position was changed by navigation and the
    /// backend should reposition the mouse on the next frame.
    /// </summary>
    public bool WantSetMousePos { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host should save layout settings manually. The
    /// host clears the flag after saving.
    /// </summary>
    public bool WantSaveIniSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether keyboard and gamepad navigation is currently allowed.
    /// </summary>
    public bool NavActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the navigation highlight is currently visible.
    /// </summary>
    public bool NavVisible { get; set; }

    /// <summary>
    /// Gets or sets the rolling estimate of application framerate, in frames per second.
    /// </summary>
    public float Framerate { get; set; }

    /// <summary>
    /// Gets or sets the number of vertices produced by the last render pass.
    /// </summary>
    public int MetricsRenderVertices { get; set; }

    /// <summary>
    /// Gets or sets the number of indices produced by the last render pass.
    /// </summary>
    public int MetricsRenderIndices { get; set; }

    /// <summary>
    /// Gets or sets the number of visible windows in the last frame.
    /// </summary>
    public int MetricsRenderWindows { get; set; }

    /// <summary>
    /// Gets or sets the number of active windows in the last frame.
    /// </summary>
    public int MetricsActiveWindows { get; set; }

    /// <summary>
    /// Gets the mouse displacement since the previous frame. The value is zero when either position
    /// is invalid.
    /// </summary>
    public ImVec2 MouseDelta { get; internal set; } = new(0.0f, 0.0f);

    /// <summary>
    /// Gets or sets the parent UI context that owns input processing. Mirrors the canonical
    /// <c>ImGuiIO::Ctx</c> pointer.
    /// </summary>
    public object? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the mouse position in pixels. The sentinel value
    /// <c>(-float.MaxValue, -float.MaxValue)</c> indicates that no mouse is available.
    /// </summary>
    public ImVec2 MousePos { get; set; } = new(-float.MaxValue, -float.MaxValue);

    /// <summary>
    /// Gets the mouse button down state. Index zero is the left button and index one is the right
    /// button. The array length matches the canonical <c>ImGuiMouseButton</c> count.
    /// </summary>
    public bool[] MouseDown { get; } = new bool[5];

    /// <summary>
    /// Gets or sets the vertical mouse wheel delta. Positive values scroll up and negative values
    /// scroll down.
    /// </summary>
    public float MouseWheel { get; set; }

    /// <summary>
    /// Gets or sets the horizontal mouse wheel delta. Positive values scroll left and negative values
    /// scroll right.
    /// </summary>
    public float MouseWheelH { get; set; }

    /// <summary>
    /// Gets or sets the input peripheral that produced the current mouse events.
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; } = ImGuiMouseSource.Mouse;

    /// <summary>
    /// Gets or sets the identifier of the viewport currently hovered by the operating-system mouse.
    /// </summary>
    public uint MouseHoveredViewport { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Control modifier is held.
    /// </summary>
    public bool KeyCtrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Shift modifier is held.
    /// </summary>
    public bool KeyShift { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Alt modifier is held.
    /// </summary>
    public bool KeyAlt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Super or Windows modifier is held.
    /// </summary>
    public bool KeySuper { get; set; }

    /// <summary>
    /// Gets or sets the merged key modifier flags, combining the Control, Shift, Alt, and Super
    /// modifiers into a single <see cref="ImGuiKey"/> value. The field is updated while input is processed.
    /// </summary>
    public ImGuiKey KeyMods { get; set; } = ImGuiKey.None;

    /// <summary>
    /// Gets the per-key input state for every named key. Index entries with
    /// <c>key - ImGuiKey.NamedKey_BEGIN</c>, matching canonical <c>ImGuiIO::KeysData</c>.
    /// </summary>
    public ImGuiKeyData[] KeysData { get; } = new ImGuiKeyData[(int)ImGuiKey.NamedKey_COUNT];

    /// <summary>
    /// Gets or sets a value indicating whether mouse capture is skipped when a click is expected to
    /// close a popup.
    /// </summary>
    public bool WantCaptureMouseUnlessPopupClose { get; set; }

    /// <summary>
    /// Gets or sets the mouse position captured on the previous frame.
    /// </summary>
    public ImVec2 MousePosPrev { get; set; } = new(-float.MaxValue, -float.MaxValue);

    /// <summary>
    /// Gets the mouse position recorded when each button was last pressed.
    /// </summary>
    public ImVec2[] MouseClickedPos { get; } = new ImVec2[5];

    /// <summary>
    /// Gets the time of the last press of each mouse button.
    /// </summary>
    public double[] MouseClickedTime { get; } = new double[5];

    /// <summary>
    /// Gets the per-button state that is set while a press edge is pending.
    /// </summary>
    public bool[] MouseClicked { get; } = new bool[5];

    /// <summary>
    /// Gets the per-button state that is set while a double-click edge is pending.
    /// </summary>
    public bool[] MouseDoubleClicked { get; } = new bool[5];

    /// <summary>
    /// Gets the number of consecutive clicks recorded for each mouse button.
    /// </summary>
    public ushort[] MouseClickedCount { get; } = new ushort[5];

    /// <summary>
    /// Gets the most recent consecutive click count recorded for each mouse button. The value remains
    /// valid after the button is released.
    /// </summary>
    public ushort[] MouseClickedLastCount { get; } = new ushort[5];

    /// <summary>
    /// Gets the per-button state that is set while a release edge is pending.
    /// </summary>
    public bool[] MouseReleased { get; } = new bool[5];

    /// <summary>
    /// Gets the time of the last release of each mouse button.
    /// </summary>
    public double[] MouseReleasedTime { get; } = new double[5];

    /// <summary>
    /// Gets the per-button state indicating whether the press began inside a Hikari.ImGui window or
    /// over a popup-blocked region.
    /// </summary>
    public bool[] MouseDownOwned { get; } = new bool[5];

    /// <summary>
    /// Gets the per-button state indicating whether the press began inside a Hikari.ImGui window.
    /// </summary>
    public bool[] MouseDownOwnedUnlessPopupClose { get; } = new bool[5];

    /// <summary>
    /// Gets or sets a value indicating whether holding Shift swaps the wheel axes on non-macOS systems.
    /// </summary>
    public bool MouseWheelRequestAxisSwap { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current click is a simulated right click produced
    /// by a Control-click on macOS.
    /// </summary>
    public bool MouseCtrlLeftAsRightClick { get; set; }

    /// <summary>
    /// Gets how long each mouse button has been held, in seconds.
    /// </summary>
    public float[] MouseDownDuration { get; } = new float[5];

    /// <summary>
    /// Gets how long each mouse button had been held on the previous frame, in seconds.
    /// </summary>
    public float[] MouseDownDurationPrev { get; } = new float[5];

    /// <summary>
    /// Gets the maximum absolute distance each mouse button has traveled from its press position.
    /// </summary>
    public ImVec2[] MouseDragMaxDistanceAbs { get; } = new ImVec2[5];

    /// <summary>
    /// Gets the squared maximum distance each mouse button has traveled from its press position.
    /// </summary>
    public float[] MouseDragMaxDistanceSqr { get; } = new float[5];

    /// <summary>
    /// Gets or sets the touch or pen pressure, in the range zero through one.
    /// </summary>
    public float PenPressure { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the application window has lost focus.
    /// </summary>
    public bool AppFocusLost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host is currently accepting key, mouse, and text events.
    /// </summary>
    public bool AppAcceptingEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets a pending high surrogate from a UTF-16 character event.
    /// </summary>
    public ushort InputQueueSurrogate { get; set; }

    /// <summary>
    /// Gets the queue of input characters collected during the current frame. The queue is filled
    /// through the <c>AddInputCharacter</c> methods and cleared at the end of a frame.
    /// </summary>
    public List<uint> InputQueueCharacters { get; } = new();

    /// <summary>
    /// Gets or sets the obsolete global font scale factor retained for compatibility. New code uses
    /// <see cref="ImGuiStyle.FontScaleMain"/> instead.
    /// </summary>
    public float FontGlobalScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the obsolete clipboard read callback retained for legacy backends. New
    /// integrations provide the clipboard through the platform interface instead.
    /// </summary>
    public Func<object?, string?>? GetClipboardTextFn { get; set; }

    /// <summary>
    /// Gets or sets the obsolete clipboard write callback retained for legacy backends. New
    /// integrations provide the clipboard through the platform interface instead.
    /// </summary>
    public Action<object?, string>? SetClipboardTextFn { get; set; }

    /// <summary>
    /// Gets or sets caller-owned data passed to the obsolete clipboard callbacks.
    /// </summary>
    public object? ClipboardUserData { get; set; }

    /// <summary>
    /// Initializes a new input/output instance with canonical defaults.
    /// </summary>
    /// <param name="fonts">The font atlas to assign, or null to create an empty atlas.</param>
    /// <remarks>
    /// The default values mirror <c>ImGuiIO::ImGuiIO()</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1680-1800</c>). The per-key and per-button duration arrays are
    /// initialized to the canonical not-pressed sentinel of negative one.
    /// </remarks>
    public ImGuiIO(ImFontAtlas? fonts = null)
    {
        Fonts = fonts ?? new ImFontAtlas();

        for (int i = 0; i < MouseDown.Length; i++)
        {
            MouseClickedPos[i] = new ImVec2(-float.MaxValue, -float.MaxValue);
            MouseDragMaxDistanceAbs[i] = new ImVec2(0.0f, 0.0f);
            MouseDownDuration[i] = -1.0f;
            MouseDownDurationPrev[i] = -1.0f;
        }

        for (int i = 0; i < KeysData.Length; i++)
        {
            KeysData[i].DownDuration = -1.0f;
            KeysData[i].DownDurationPrev = -1.0f;
        }
    }

    /// <summary>
    /// Queues a key press or release event for a named key.
    /// </summary>
    /// <param name="key">The translated named key. Legacy native key codes are not accepted.</param>
    /// <param name="down">True to press the key and false to release it.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddKeyEvent</c> (<c>ThirdParty/imgui/imgui.cpp:1963-1967</c>),
    /// which forwards to <see cref="AddKeyAnalogEvent"/> with a digital analog value.
    /// </remarks>
    public void AddKeyEvent(ImGuiKey key, bool down)
    {
        AddKeyAnalogEvent(key, down, down ? 1.0f : 0.0f);
    }

    /// <summary>
    /// Queues a key press or release event that also carries an analog value.
    /// </summary>
    /// <param name="key">The translated named key. Legacy native key codes are not accepted.</param>
    /// <param name="down">True to press the key and false to release it.</param>
    /// <param name="analogValue">The analog value reported for analog keys, in the range zero through one.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddKeyAnalogEvent</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1926-1967</c>), including the macOS Control and Super key swap,
    /// the duplicate-event filter against the newest queued key event or the live key state, and the
    /// enqueueing of an <see cref="ImGuiInputEventType.Key"/> event. The queued event is consumed by
    /// <c>ImGui.UpdateInputEvents</c> during the next <c>ImGui.NewFrame</c>; the live
    /// <see cref="KeysData"/> entry is only written at that point.
    /// </remarks>
    public void AddKeyAnalogEvent(ImGuiKey key, bool down, float analogValue)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        if (key == ImGuiKey.None || !AppAcceptingEvents)
            return;

        if (ConfigMacOSXBehaviors)
        {
            key = key switch
            {
                ImGuiKey.ImGuiMod_Super => ImGuiKey.ImGuiMod_Ctrl,
                ImGuiKey.ImGuiMod_Ctrl => ImGuiKey.ImGuiMod_Super,
                ImGuiKey.LeftSuper => ImGuiKey.LeftCtrl,
                ImGuiKey.RightSuper => ImGuiKey.RightCtrl,
                ImGuiKey.LeftCtrl => ImGuiKey.LeftSuper,
                ImGuiKey.RightCtrl => ImGuiKey.RightSuper,
                _ => key,
            };
        }

        // Canonical rejects legacy native key codes and alias keys here
        // (ThirdParty/imgui/imgui.cpp:1931-1933): only named keys and the four modifier pseudo-keys
        // carry input state, and mouse alias keys are inferred from the mouse event stream. The
        // canonical checks are assertions; this port also returns early so a misbehaving host cannot
        // queue an event that the key-state lookup cannot represent.
        ImAssert.Check(ImGui.IsNamedKeyOrMod(key), "The backend must submit a named key or a modifier pseudo-key.");
        ImAssert.Check(!ImGui.IsAliasKey(key), "Mouse alias keys are inferred from AddMouse* events and cannot be submitted.");
        if (!ImGui.IsNamedKeyOrMod(key) || ImGui.IsAliasKey(key))
            return;

        // Filter duplicates: key modifiers and gamepad analog values are commonly re-submitted every
        // frame with an unchanged value.
        // The live state is read through ImGui.GetKeyData, which maps a modifier flag (for example ImGuiMod_Ctrl)
        // to its reserved slot (ReservedForModCtrl). Indexing KeysData with the raw flag value would read the wrong
        // record, so a modifier release would be discarded as a duplicate (imgui.cpp:1946-1951).
        ImGuiInputEvent? latestEvent = FindLatestInputEvent(g, ImGuiInputEventType.Key, (int)key);
        ref ImGuiKeyData keyData = ref ImGui.GetKeyData(key);
        bool latestKeyDown = latestEvent != null ? latestEvent.Key.Down : keyData.Down;
        float latestKeyAnalog = latestEvent != null ? latestEvent.Key.AnalogValue : keyData.AnalogValue;
        if (latestKeyDown == down && latestKeyAnalog == analogValue)
            return;

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.Key,
            Source = ImGui.IsGamepadKey(key) ? ImGuiInputSource.Gamepad : ImGuiInputSource.Keyboard,
            EventId = g.InputEventsNextEventId++,
        };
        e.Key.Key = key;
        e.Key.Down = down;
        e.Key.AnalogValue = analogValue;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Feeds the current mouse position from the host window.
    /// </summary>
    /// <param name="x">The horizontal mouse coordinate, in display pixels.</param>
    /// <param name="y">The vertical mouse coordinate, in display pixels.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddMousePosEvent</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1998-2024</c>). The position is floored with canonical
    /// <c>ImFloor</c> semantics before it is enqueued, the canonical duplicate filter compares against
    /// the newest queued position or the live mouse position, and the latched mouse source is copied
    /// into the event.
    /// </remarks>
    public void AddMousePosEvent(float x, float y)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        if (!AppAcceptingEvents)
            return;

        // Apply the same flooring that UpdateMouseInputs applies, because the duplicate filter below
        // compares floored values against the live position.
        ImVec2 pos = new(x > -float.MaxValue ? MathF.Floor(x) : x, y > -float.MaxValue ? MathF.Floor(y) : y);

        ImGuiInputEvent? latestEvent = FindLatestInputEvent(g, ImGuiInputEventType.MousePos);
        ImVec2 latestPos = latestEvent != null
            ? new ImVec2(latestEvent.MousePos.PosX, latestEvent.MousePos.PosY)
            : MousePos;
        if (latestPos.X == pos.X && latestPos.Y == pos.Y)
            return;

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.MousePos,
            Source = ImGuiInputSource.Mouse,
            EventId = g.InputEventsNextEventId++,
        };
        e.MousePos.PosX = pos.X;
        e.MousePos.PosY = pos.Y;
        e.MousePos.MouseSource = g.InputEventsNextMouseSource;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Feeds a mouse button change from the host window.
    /// </summary>
    /// <param name="button">The mouse button index. Zero is the left button and one is the right button.</param>
    /// <param name="down">True when the button is pressed and false when it is released.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddMouseButtonEvent</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:2032-2078</c>), including the macOS Ctrl-plus-left-click
    /// aliasing into a right click for both the press and the held-button release, the duplicate
    /// filter against the newest queued button event or the live state, and the enqueueing of a
    /// <see cref="ImGuiInputEventType.MouseButton"/> event.
    /// </remarks>
    public void AddMouseButtonEvent(int button, bool down)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        ImAssert.Check(button >= 0 && button < MouseDown.Length);
        if (!AppAcceptingEvents)
            return;

        // On macOS, a physical Control click is converted into a right click. The held-button state is
        // latched so the matching release is redirected as well.
        if (ConfigMacOSXBehaviors && button == 0 && MouseCtrlLeftAsRightClick)
        {
            button = 1;
            if (!down)
                MouseCtrlLeftAsRightClick = false;
        }

        ImGuiInputEvent? latestEvent = FindLatestInputEvent(g, ImGuiInputEventType.MouseButton, button);
        bool latestButtonDown = latestEvent != null
            ? latestEvent.MouseButton.Down
            : (uint)button < (uint)MouseDown.Length && MouseDown[button];
        if (latestButtonDown == down)
            return;

        // On macOS the initial Control press is aliased into a right-button press.
        if (ConfigMacOSXBehaviors && button == 0 && down)
        {
            ImGuiInputEvent? latestSuperEvent = FindLatestInputEvent(g, ImGuiInputEventType.Key, (int)ImGuiKey.ImGuiMod_Super);
            if (latestSuperEvent != null ? latestSuperEvent.Key.Down : KeySuper)
            {
                MouseCtrlLeftAsRightClick = true;
                AddMouseButtonEvent(1, true);
                return;
            }
        }

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.MouseButton,
            Source = ImGuiInputSource.Mouse,
            EventId = g.InputEventsNextEventId++,
        };
        e.MouseButton.Button = button;
        e.MouseButton.Down = down;
        e.MouseButton.MouseSource = g.InputEventsNextMouseSource;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Feeds a mouse wheel change from the host window.
    /// </summary>
    /// <param name="wheelX">The horizontal wheel delta. Positive values scroll left.</param>
    /// <param name="wheelY">The vertical wheel delta. Positive values scroll up.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddMouseWheelEvent</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:2081-2098</c>). Zero deltas are discarded and the event is
    /// enqueued with the latched mouse source.
    /// </remarks>
    public void AddMouseWheelEvent(float wheelX, float wheelY)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        if (!AppAcceptingEvents || (wheelX == 0.0f && wheelY == 0.0f))
            return;

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.MouseWheel,
            Source = ImGuiInputSource.Mouse,
            EventId = g.InputEventsNextEventId++,
        };
        e.MouseWheel.WheelX = wheelX;
        e.MouseWheel.WheelY = wheelY;
        e.MouseWheel.MouseSource = g.InputEventsNextMouseSource;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Feeds a mouse input source change such as a switch from a mouse to a touch screen or pen.
    /// </summary>
    /// <param name="source">The input peripheral that produced the current events.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddMouseSourceEvent</c> (<c>ThirdParty/imgui/imgui.cpp:2102-2108</c>).
    /// The source is not an event of its own: it is latched on the context and copied into subsequent
    /// mouse event payloads so that filtered duplicate events cannot change the reported source.
    /// </remarks>
    public void AddMouseSourceEvent(ImGuiMouseSource source)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        g.InputEventsNextMouseSource = source;
    }

    /// <summary>
    /// Feeds the identifier of the viewport currently hovered by the operating-system mouse.
    /// </summary>
    /// <param name="viewportId">The hovered viewport identifier.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddMouseViewportEvent</c> (<c>ThirdParty/imgui/imgui.cpp:2110-2126</c>).
    /// The canonical duplicate filter compares against the newest queued viewport event or the live
    /// hovered viewport before the event is enqueued.
    /// </remarks>
    public void AddMouseViewportEvent(uint viewportId)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        if (!AppAcceptingEvents)
            return;

        ImGuiInputEvent? latestEvent = FindLatestInputEvent(g, ImGuiInputEventType.MouseViewport);
        uint latestViewportId = latestEvent != null ? latestEvent.MouseViewport.HoveredViewportID : MouseHoveredViewport;
        if (latestViewportId == viewportId)
            return;

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.MouseViewport,
            Source = ImGuiInputSource.Mouse,
        };
        e.MouseViewport.HoveredViewportID = viewportId;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Feeds an application focus change event.
    /// </summary>
    /// <param name="focused">True when the application gained focus and false when it lost focus.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddFocusEvent</c> (<c>ThirdParty/imgui/imgui.cpp:2128-2143</c>).
    /// The focus change is enqueued rather than applied immediately; <c>ImGui.UpdateInputEvents</c>
    /// consumes it during the next frame, updates <see cref="AppFocusLost"/>, and clears keyboard and
    /// mouse state when focus is lost and <see cref="ConfigDebugIgnoreFocusLoss"/> is disabled.
    /// </remarks>
    public void AddFocusEvent(bool focused)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        ImGuiInputEvent? latestEvent = FindLatestInputEvent(g, ImGuiInputEventType.Focus);
        bool latestFocused = latestEvent != null ? latestEvent.AppFocused.Focused : !AppFocusLost;
        if (latestFocused == focused || (ConfigDebugIgnoreFocusLoss && !focused))
            return;

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.Focus,
            EventId = g.InputEventsNextEventId++,
        };
        e.AppFocused.Focused = focused;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Queues a Unicode character for text input.
    /// </summary>
    /// <param name="c">The Unicode code point to queue.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddInputCharacter</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1798-1809</c>). A zero code point is ignored, and a
    /// <see cref="ImGuiInputEventType.Text"/> event is enqueued for
    /// <c>ImGui.UpdateInputEvents</c> to append to <see cref="InputQueueCharacters"/>.
    /// </remarks>
    public void AddInputCharacter(uint c)
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        if (g == null)
            return;

        if (c == 0 || !AppAcceptingEvents)
            return;

        ImGuiInputEvent e = new()
        {
            Type = ImGuiInputEventType.Text,
            Source = ImGuiInputSource.Keyboard,
            EventId = g.InputEventsNextEventId++,
        };
        e.Text.Char = c;
        g.InputEventsQueue.Add(e);
    }

    /// <summary>
    /// Queues a UTF-16 code unit for text input, combining surrogate pairs into a single code point.
    /// </summary>
    /// <param name="c">The UTF-16 code unit to queue.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddInputCharacterUTF16</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1809-1841</c>). Invalid surrogate sequences produce the Unicode
    /// replacement character.
    /// </remarks>
    public void AddInputCharacterUTF16(ushort c)
    {
        if ((c == 0 && InputQueueSurrogate == 0) || !AppAcceptingEvents)
            return;

        if ((c & 0xFC00) == 0xD800)
        {
            // High surrogate: save it until the low surrogate arrives.
            if (InputQueueSurrogate != 0)
            {
                AddInputCharacter(0xFFFD);
            }

            InputQueueSurrogate = c;
            return;
        }

        uint codePoint = c;
        if (InputQueueSurrogate != 0)
        {
            if ((c & 0xFC00) != 0xDC00)
            {
                AddInputCharacter(0xFFFD);
            }
            else
            {
                codePoint = (uint)(((InputQueueSurrogate - 0xD800) << 10) + (c - 0xDC00) + 0x10000);
            }

            InputQueueSurrogate = 0;
        }

        AddInputCharacter(codePoint);
    }

    /// <summary>
    /// Queues every Unicode code point contained in a string.
    /// </summary>
    /// <param name="str">The string whose characters are queued.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::AddInputCharactersUTF8</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1843-1854</c>), which decodes UTF-8 bytes. The .NET string type
    /// already stores text as UTF-16, so this port reads UTF-16 code units and combines surrogate
    /// pairs into single code points. Iteration stops at a null terminator, and an unpaired surrogate
    /// produces the Unicode replacement character.
    /// </remarks>
    public void AddInputCharactersUTF8(string str)
    {
        if (string.IsNullOrEmpty(str) || !AppAcceptingEvents)
            return;

        for (int i = 0; i < str.Length; i++)
        {
            char ch = str[i];
            if (ch == '\0')
                break;

            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < str.Length && char.IsLowSurrogate(str[i + 1]))
                {
                    AddInputCharacter((uint)char.ConvertToUtf32(ch, str[i + 1]));
                    i += 1;
                }
                else
                {
                    AddInputCharacter(0xFFFD);
                }
            }
            else if (char.IsLowSurrogate(ch))
            {
                AddInputCharacter(0xFFFD);
            }
            else
            {
                AddInputCharacter(ch);
            }
        }
    }

    /// <summary>
    /// Provides native key code and scan code metadata for a previously submitted key event.
    /// </summary>
    /// <param name="key">The translated named key whose metadata is provided.</param>
    /// <param name="nativeKeycode">The backend native key code.</param>
    /// <param name="nativeScancode">The backend native scan code.</param>
    /// <param name="nativeLegacyIndex">The legacy native index, or negative one when unavailable.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::SetKeyEventNativeData</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1973-1983</c>), which stores the values but does not yet consume
    /// them. The parameters are retained so later input chunks can surface them through the key data.
    /// </remarks>
    public void SetKeyEventNativeData(ImGuiKey key, int nativeKeycode, int nativeScancode, int nativeLegacyIndex = -1)
    {
        _ = key;
        _ = nativeKeycode;
        _ = nativeScancode;
        _ = nativeLegacyIndex;
    }

    /// <summary>
    /// Sets the master flag that controls whether key, mouse, and text events are accepted.
    /// </summary>
    /// <param name="acceptingEvents">True to accept events and false to ignore them.</param>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::SetAppAcceptingEvents</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1986-1989</c>). Hosts suspend event acceptance while a native
    /// dialog box interrupts the application loop.
    /// </remarks>
    public void SetAppAcceptingEvents(bool acceptingEvents)
    {
        AppAcceptingEvents = acceptingEvents;
    }

    /// <summary>
    /// Discards every event that has been queued but not yet processed.
    /// </summary>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::ClearEventsQueue</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1857-1863</c>), which discards every event queued on the context
    /// that has not yet been consumed by <c>ImGui.UpdateInputEvents</c>.
    /// </remarks>
    public void ClearEventsQueue()
    {
        ImGuiContext? g = Ctx as ImGuiContext;
        ImAssert.Check(g != null);
        g?.InputEventsQueue.Clear();
    }

    /// <summary>
    /// Releases every keyboard key, clears the modifier state, and discards pending character input.
    /// </summary>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::ClearInputKeys</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1865-1879</c>). Mouse keys are left untouched; use
    /// <see cref="ClearInputMouse"/> for mouse state.
    /// </remarks>
    public void ClearInputKeys()
    {
        for (int key = (int)ImGuiKey.NamedKey_BEGIN; key < (int)ImGuiKey.NamedKey_END; key++)
        {
            if (key >= (int)ImGuiKey.MouseLeft && key <= (int)ImGuiKey.MouseWheelY)
                continue;

            int index = key - (int)ImGuiKey.NamedKey_BEGIN;
            KeysData[index].Down = false;
            KeysData[index].DownDuration = -1.0f;
            KeysData[index].DownDurationPrev = -1.0f;
        }

        KeyCtrl = false;
        KeyShift = false;
        KeyAlt = false;
        KeySuper = false;
        KeyMods = ImGuiKey.None;
        InputQueueCharacters.Clear();
    }

    /// <summary>
    /// Releases every mouse button, clears the mouse position, and resets the wheel deltas.
    /// </summary>
    /// <remarks>
    /// Mirrors canonical <c>ImGuiIO::ClearInputMouse</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1882-1900</c>).
    /// </remarks>
    public void ClearInputMouse()
    {
        for (int key = (int)ImGuiKey.MouseLeft; key <= (int)ImGuiKey.MouseWheelY; key++)
        {
            int index = key - (int)ImGuiKey.NamedKey_BEGIN;
            KeysData[index].Down = false;
            KeysData[index].DownDuration = -1.0f;
            KeysData[index].DownDurationPrev = -1.0f;
        }

        MousePos = new ImVec2(-float.MaxValue, -float.MaxValue);
        for (int i = 0; i < MouseDown.Length; i++)
        {
            MouseDown[i] = false;
            MouseDownDuration[i] = -1.0f;
            MouseDownDurationPrev[i] = -1.0f;
        }

        MouseWheel = 0.0f;
        MouseWheelH = 0.0f;
    }

    /// <summary>
    /// Finds the newest queued input event of a given type, optionally filtered by key or button index.
    /// </summary>
    /// <param name="g">The context owning the pending input-event queue.</param>
    /// <param name="type">The event type to search for.</param>
    /// <param name="arg">
    /// For a key event, the key to match; for a mouse-button event, the button index to match; ignored
    /// for every other event type.
    /// </param>
    /// <returns>The newest matching queued event, or null when no queued event matches.</returns>
    /// <remarks>
    /// Ports the canonical file-scope helper <c>FindLatestInputEvent</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:1902-1920</c>). The <c>Add*</c> producers use it to filter
    /// duplicate events against the newest queued event instead of the live input state, because the
    /// live state is only updated when the queue is consumed at the start of the next frame.
    /// </remarks>
    private static ImGuiInputEvent? FindLatestInputEvent(ImGuiContext g, ImGuiInputEventType type, int arg = -1)
    {
        for (int n = g.InputEventsQueue.Count - 1; n >= 0; n--)
        {
            ImGuiInputEvent e = g.InputEventsQueue[n];
            if (e.Type != type)
                continue;
            if (type == ImGuiInputEventType.Key && (int)e.Key.Key != arg)
                continue;
            if (type == ImGuiInputEventType.MouseButton && e.MouseButton.Button != arg)
                continue;
            return e;
        }

        return null;
    }
}

#endregion
