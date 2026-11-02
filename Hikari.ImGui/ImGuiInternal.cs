#nullable enable
/*================================================================================================
 * DEAR IMGUI INTERNAL STRUCTURES
 *================================================================================================
 * ROLE IN ENGINE:
 * Defines the canonical internal data structures of the Hikari.ImGui port of Dear ImGui
 * v1.92.9b-docking. These are the storage, window, item, navigation, input, and context records
 * that the frame pipeline and the widget layer mutate in place: ImGuiContext owns a frame's worth
 * of state, ImGuiWindow owns per-window persistent state, and the remaining records describe
 * queues, stacks, and scratch buffers used while a frame is submitted.
 *
 * SUBSYSTEM CONNECTIONS & DATA FLOW:
 *
 *    ┌──────────────────────────────────────────────┐
 *    │ ImGuiContext (one per UI context)            │
 *    │ • IO, Style, frame counters, stacks, windows │
 *    └──────────────────────────────────────────────┘
 *             │ CurrentWindow / NextWindowData      ▲ Windows[]
 *             ▼                                     │
 *    ┌──────────────────────────────────────────────┐
 *    │ ImGuiWindow                                  │
 *    │ • identity, geometry, scroll, DC, IDStack    │
 *    └──────────────────────────────────────────────┘
 *             │ NextItemData / LastItemData         ▲ OpenPopupStack
 *             ▼                                     │
 *    ┌──────────────────────────────────────────────┐
 *    │ Item, group, popup, and navigation records   │
 *    └──────────────────────────────────────────────┘
 *
 * STYLIZED USAGE EXAMPLE:
 *
 *   var context = new ImGuiContext();
 *   context.CurrentWindow = context.Windows[0];
 *   context.NextWindowData.PosVal = new ImVec2(10.0f, 10.0f);
 *   context.NextWindowData.HasFlags |= ImGuiNextWindowDataFlags.HasPos;
 *
 * THREADING & LIFECYCLE NOTES:
 * - Every type in this file is plain single-threaded mutable state. The owner context and the
 *   single frame-submitting thread are the only writers; no internal synchronization is provided.
 * - Reference-typed records (classes) are referenced by pointer-equivalent semantics from the
 *   owning context. Collection-valued members are owned by their declaring type and are never
 *   shared between contexts.
 *================================================================================================*/

using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace Hikari.ImGui;

#region Callbacks

/// <summary>
/// Receives a window size-constraint request so the application can clamp or override the size of a
/// window queued with <c>SetNextWindowSizeConstraints()</c>
/// (<c>ThirdParty/imgui/imgui.h:2808-2810</c>).
/// </summary>
/// <param name="data">The mutable constraint data for the window being sized; the callback may
/// overwrite <see cref="ImGuiSizeCallbackData.DesiredSize"/>.</param>
/// <remarks>
/// The callback runs on the frame-submitting thread during the window size resolution pass. The
/// <paramref name="data"/> instance is owned by the core and remains valid only for the duration of
/// the call, so the callback must not retain it.
/// </remarks>
public delegate void ImGuiSizeCallback(ImGuiSizeCallbackData data);

/// <summary>
/// Receives a context lifecycle or render event registered through <c>AddContextHook()</c>
/// (<c>ThirdParty/imgui/imgui_internal.h:2424</c>).
/// </summary>
/// <param name="ctx">The context that raised the hook.</param>
/// <param name="hook">The hook record being invoked.</param>
/// <remarks>
/// Hooks run synchronously on the frame-submitting thread at the point named by
/// <see cref="ImGuiContextHook.Type"/>. A hook must not start a nested frame or destroy the context.
/// </remarks>
public delegate void ImGuiContextHookCallback(ImGuiContext ctx, ImGuiContextHook hook);

/// <summary>
/// Receives text-editing events from <c>InputText()</c> when a callback is supplied
/// (<c>ThirdParty/imgui/imgui.h:2783-2785</c>).
/// </summary>
/// <param name="data">The shared input-text callback state for the active field.</param>
/// <returns>Zero to accept the edit, or one to cancel the operation as documented by the
/// <c>ImGuiInputTextFlags_Callback*</c> contract.</returns>
/// <remarks>
/// The callback runs on the frame-submitting thread while the field is being processed. The
/// <paramref name="data"/> instance is owned by the core and valid only for the duration of the
/// call.
/// </remarks>
public delegate int ImGuiInputTextCallback(ImGuiInputTextCallbackData data);

/// <summary>
/// Receives demo showcase section markers so tooling can associate UI sections with source
/// locations (<c>ThirdParty/imgui/imgui_internal.h:2438</c>).
/// </summary>
/// <param name="file">The source file that declares the section.</param>
/// <param name="line">The source line that declares the section.</param>
/// <param name="section">The section label.</param>
public delegate void ImGuiDemoMarkerCallback(string file, int line, string section);

/// <summary>
/// Receives recoverable user errors reported through <c>ErrorCheck()</c> and its variants
/// (<c>ThirdParty/imgui/imgui_internal.h:2316</c>).
/// </summary>
/// <param name="ctx">The context that reported the error.</param>
/// <param name="userData">The opaque application pointer supplied with the callback.</param>
/// <param name="msg">The formatted error message.</param>
/// <remarks>
/// The callback runs on the frame-submitting thread. It must not throw; a thrown exception would
/// propagate out of the frame pipeline and is not converted into an error by the core.
/// </remarks>
public delegate void ImGuiErrorCallback(ImGuiContext ctx, IntPtr userData, string msg);

#endregion

#region Math and packed-value helpers

/// <summary>
/// A one-dimensional vector used by layout code to make the transition between one- and two-axis
/// indentation explicit (<c>ThirdParty/imgui/imgui_internal.h:576-580</c>).
/// </summary>
/// <remarks>
/// This is a plain value type with no identity; assignment copies the single component.
/// </remarks>
public struct ImVec1
{
    /// <summary>
    /// Gets or sets the single component. (<c>ThirdParty/imgui/imgui_internal.h:578</c>)
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Initializes a vector with a component value.
    /// </summary>
    /// <param name="x">The component value.</param>
    public ImVec1(float x)
    {
        X = x;
    }
}

/// <summary>
/// A two-dimensional vector of half-size integers used for long-term packed storage of window
/// geometry in settings (<c>ThirdParty/imgui/imgui_internal.h:592-597</c>).
/// </summary>
/// <remarks>
/// Coordinates saturate at the 16-bit range; the type is intended for persisted settings, not for
/// runtime layout arithmetic.
/// </remarks>
public struct ImVec2ih
{
    /// <summary>
    /// Gets or sets the horizontal component. (<c>ThirdParty/imgui/imgui_internal.h:594</c>)
    /// </summary>
    public short X { get; set; }

    /// <summary>
    /// Gets or sets the vertical component. (<c>ThirdParty/imgui/imgui_internal.h:594</c>)
    /// </summary>
    public short Y { get; set; }

    /// <summary>
    /// Initializes a packed integer vector.
    /// </summary>
    /// <param name="x">The horizontal component.</param>
    /// <param name="y">The vertical component.</param>
    public ImVec2ih(short x, short y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Initializes a packed integer vector by truncating a floating-point vector.
    /// </summary>
    /// <param name="value">The source vector.</param>
    public ImVec2ih(ImVec2 value)
    {
        X = (short)value.X;
        Y = (short)value.Y;
    }
}

/// <summary>
/// A calendar date packed into 16 bits for compact storage of the last-used date of settings
/// entries (<c>ThirdParty/imgui/imgui_internal.h:849-861</c>).
/// </summary>
/// <remarks>
/// The current date is supplied by the platform backend. A default-constructed value is invalid;
/// <see cref="IsValid"/> reports whether all three components are non-zero.
/// </remarks>
public struct ImGuiPackedDate
{
    /// <summary>
    /// Gets or sets the year offset from 2000, stored in 7 bits.
    /// (<c>ThirdParty/imgui/imgui_internal.h:855</c>)
    /// </summary>
    public ushort Year { get; set; }

    /// <summary>
    /// Gets or sets the month in the range 1..12, stored in 4 bits.
    /// (<c>ThirdParty/imgui/imgui_internal.h:856</c>)
    /// </summary>
    public ushort Month { get; set; }

    /// <summary>
    /// Gets or sets the day of month in the range 1..31, stored in 5 bits.
    /// (<c>ThirdParty/imgui/imgui_internal.h:857</c>)
    /// </summary>
    public ushort Day { get; set; }

    /// <summary>
    /// Initializes a date value from a packed numeric form.
    /// </summary>
    /// <param name="yyyymmdd">The date encoded as <c>YYYYMMDD</c>.</param>
    public ImGuiPackedDate(uint yyyymmdd)
    {
        Year = (ushort)((yyyymmdd / 10000u) - 2000u);
        Month = (ushort)((yyyymmdd / 100u) % 100u);
        Day = (ushort)(yyyymmdd % 100u);
    }

    /// <summary>
    /// Returns whether the date carries a complete, non-zero year, month, and day.
    /// (<c>ThirdParty/imgui/imgui_internal.h:860</c>)
    /// </summary>
    /// <returns><see langword="true"/> when all three components are non-zero.</returns>
    public bool IsValid()
    {
        return Year != 0 && Month != 0 && Day != 0;
    }

    /// <summary>
    /// Re-packs the date into its <c>YYYYMMDD</c> numeric form.
    /// (<c>ThirdParty/imgui/imgui_internal.h:861</c>)
    /// </summary>
    /// <returns>The date encoded as <c>YYYYMMDD</c>, or zero when the date is invalid.</returns>
    /// <remarks>
    /// An invalid date unpacks to zero rather than to a year-2000 value so that callers can compare packed
    /// values directly when pruning old settings entries.
    /// </remarks>
    public int Unpack()
    {
        return IsValid() ? (((Year + 2000) * 10000) + (Month * 100) + Day) : 0;
    }

    /// <summary>
    /// Moves the date backwards by a whole number of months, clamping the day to the packed representation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:862</c>)
    /// </summary>
    /// <param name="m">The number of months to subtract; zero performs no change.</param>
    /// <remarks>
    /// Canonical decrements the year exactly when the month wraps past January; the day is intentionally left
    /// untouched, which the canonical comment calls out as sufficient for pruning `.ini` data.
    /// </remarks>
    public void SubtractMonths(int m)
    {
        while (m > 0)
        {
            Year -= (ushort)(Month == 1 ? 1 : 0);
            Month = (ushort)(Month == 1 ? 12 : Month - 1);
            m--;
        }
    }
}

#endregion

#region Bit storage helpers

/// <summary>
/// A fixed-size bit array indexed by named <see cref="ImGuiKey"/> values, used to record which keys
/// may emit character input (<c>ThirdParty/imgui/imgui_internal.h:670-681</c>).
/// </summary>
/// <remarks>
/// The canonical type is the template <c>ImBitArray&lt;ImGuiKey_NamedKey_COUNT, -ImGuiKey_NamedKey_BEGIN&gt;</c>;
/// this port stores the same number of bits in a managed word array. Bits outside the named-key
/// range are not addressable.
/// </remarks>
public sealed class ImBitArrayForNamedKeys
{
    /// <summary>
    /// Gets the backing 32-bit words. The word count is
    /// <c>(ImGuiKey.NamedKey_COUNT + 31) / 32</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:674</c>)
    /// </summary>
    public uint[] Data { get; } = new uint[((int)ImGuiKey.NamedKey_COUNT + 31) >> 5];

    /// <summary>
    /// Clears every bit in the array. (<c>ThirdParty/imgui/imgui_internal.h:676</c>)
    /// </summary>
    public void ClearAllBits()
    {
        Array.Clear(Data, 0, Data.Length);
    }

    /// <summary>
    /// Sets every bit in the array. (<c>ThirdParty/imgui/imgui_internal.h:677</c>)
    /// </summary>
    public void SetAllBits()
    {
        Array.Fill(Data, uint.MaxValue);
    }

    /// <summary>
    /// Tests the bit for a named key. (<c>ThirdParty/imgui/imgui_internal.h:678</c>)
    /// </summary>
    /// <param name="key">The named key, expressed as an <see cref="ImGuiKey"/> value.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    public bool TestBit(ImGuiKey key)
    {
        int index = (int)key - (int)ImGuiKey.NamedKey_BEGIN;
        return (Data[index >> 5] & (1u << (index & 31))) != 0;
    }

    /// <summary>
    /// Sets the bit for a named key. (<c>ThirdParty/imgui/imgui_internal.h:679</c>)
    /// </summary>
    /// <param name="key">The named key, expressed as an <see cref="ImGuiKey"/> value.</param>
    public void SetBit(ImGuiKey key)
    {
        int index = (int)key - (int)ImGuiKey.NamedKey_BEGIN;
        Data[index >> 5] |= 1u << (index & 31);
    }

    /// <summary>
    /// Clears the bit for a named key. (<c>ThirdParty/imgui/imgui_internal.h:680</c>)
    /// </summary>
    /// <param name="key">The named key, expressed as an <see cref="ImGuiKey"/> value.</param>
    public void ClearBit(ImGuiKey key)
    {
        int index = (int)key - (int)ImGuiKey.NamedKey_BEGIN;
        Data[index >> 5] &= ~(1u << (index & 31));
    }
}

/// <summary>
/// A compact growable one-bit-per-value vector used by the core for set-like bookkeeping
/// (<c>ThirdParty/imgui/imgui_internal.h:682-690</c>).
/// </summary>
/// <remarks>
/// The backing word array grows only through <see cref="Create"/>. Indexing a bit outside the
/// created range throws, matching the canonical debug assertion.
/// </remarks>
public sealed class ImBitVector
{
    /// <summary>
    /// Gets the backing 32-bit words that hold the bits.
    /// (<c>ThirdParty/imgui/imgui_internal.h:684</c>)
    /// </summary>
    public List<uint> Storage { get; } = new();

    /// <summary>
    /// Resizes the backing storage to hold <paramref name="size"/> bits and clears every bit.
    /// (<c>ThirdParty/imgui/imgui_internal.h:685</c>)
    /// </summary>
    /// <param name="size">The number of addressable bits.</param>
    public void Create(int size)
    {
        int wordCount = (size + 31) >> 5;
        Storage.Clear();
        for (int n = 0; n < wordCount; n++)
        {
            Storage.Add(0u);
        }
    }

    /// <summary>
    /// Releases the backing storage. (<c>ThirdParty/imgui/imgui_internal.h:686</c>)
    /// </summary>
    public void Clear()
    {
        Storage.Clear();
    }

    /// <summary>
    /// Tests one bit. (<c>ThirdParty/imgui/imgui_internal.h:687</c>)
    /// </summary>
    /// <param name="n">The zero-based bit index.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    public bool TestBit(int n)
    {
        return (Storage[n >> 5] & (1u << (n & 31))) != 0;
    }

    /// <summary>
    /// Sets one bit. (<c>ThirdParty/imgui/imgui_internal.h:688</c>)
    /// </summary>
    /// <param name="n">The zero-based bit index.</param>
    public void SetBit(int n)
    {
        Storage[n >> 5] |= 1u << (n & 31);
    }

    /// <summary>
    /// Clears one bit. (<c>ThirdParty/imgui/imgui_internal.h:689</c>)
    /// </summary>
    /// <param name="n">The zero-based bit index.</param>
    public void ClearBit(int n)
    {
        Storage[n >> 5] &= ~(1u << (n & 31));
    }
}

#endregion

#region Style support

/// <summary>
/// Describes how to locate one style variable inside <see cref="ImGuiStyle"/> so that
/// <c>PushStyleVar()</c> can save and restore it (<c>ThirdParty/imgui/imgui_internal.h:947-953</c>).
/// </summary>
/// <remarks>
/// Instances are produced by the internal style-variable lookup table and are value types;
/// <see cref="Offset"/> is the byte offset of the variable from the start of the style object and
/// <see cref="Count"/> is the number of scalar components (1 for float/int, 2 for a vector).
/// </remarks>
public struct ImGuiStyleVarInfo
{
    /// <summary>
    /// Gets or sets the number of components in the variable, always at least one (canonical stores
    /// this in 8 bits). (<c>ThirdParty/imgui/imgui_internal.h:949</c>)
    /// </summary>
    public uint Count { get; set; }

    /// <summary>
    /// Gets or sets the data type of the variable (canonical stores this in 8 bits).
    /// (<c>ThirdParty/imgui/imgui_internal.h:950</c>)
    /// </summary>
    public ImGuiDataType DataType { get; set; }

    /// <summary>
    /// Gets or sets the byte offset of the variable inside <see cref="ImGuiStyle"/> (canonical
    /// stores this in 16 bits). (<c>ThirdParty/imgui/imgui_internal.h:951</c>)
    /// </summary>
    public uint Offset { get; set; }
}

/// <summary>
/// One entry of the color stack: the modified color index and the value that must be restored by
/// <c>PopStyleColor()</c> (<c>ThirdParty/imgui/imgui_internal.h:956-960</c>).
/// </summary>
/// <remarks>
/// Instances are owned by <see cref="ImGuiContext.ColorStack"/> and mutated in place; they are
/// reference types so a stack element can be updated without replacing the whole entry.
/// </remarks>
public sealed class ImGuiColorMod
{
    /// <summary>
    /// Gets or sets the style color that was modified.
    /// (<c>ThirdParty/imgui/imgui_internal.h:958</c>)
    /// </summary>
    public ImGuiCol Col { get; set; }

    /// <summary>
    /// Gets or sets the value that was present before the push and is restored on pop.
    /// (<c>ThirdParty/imgui/imgui_internal.h:959</c>)
    /// </summary>
    public ImVec4 BackupValue { get; set; }
}

/// <summary>
/// One entry of the style-variable stack: the modified variable index and a backup of its previous
/// value (<c>ThirdParty/imgui/imgui_internal.h:963-970</c>).
/// </summary>
/// <remarks>
/// The canonical union reinterprets the backup storage as either integers or floats. This port
/// exposes both interpretations as separate arrays and documents that only the pair selected by
/// <see cref="VarIdx"/> is meaningful; the two arrays do not alias each other.
/// </remarks>
public sealed class ImGuiStyleMod
{
    /// <summary>
    /// Gets or sets the style variable that was modified.
    /// (<c>ThirdParty/imgui/imgui_internal.h:965</c>)
    /// </summary>
    public ImGuiStyleVar VarIdx { get; set; }

    /// <summary>
    /// Gets the integer backup components used when the variable holds integer data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:966</c>)
    /// </summary>
    public int[] BackupInt { get; } = new int[2];

    /// <summary>
    /// Gets the floating-point backup components used when the variable holds float or vector data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:966</c>)
    /// </summary>
    public float[] BackupFloat { get; } = new float[2];
}

/// <summary>
/// Opaque eight-byte storage large enough to hold any <see cref="ImGuiDataType"/> value
/// (<c>ThirdParty/imgui/imgui_internal.h:976-979</c>).
/// </summary>
/// <remarks>
/// The buffer carries no type tag; callers must know the data type they stored. It is used for the
/// activation-time backup of drag/slider values and for the zero value of each data type.
/// </remarks>
public sealed class ImGuiDataTypeStorage
{
    /// <summary>
    /// Gets the raw eight-byte payload. (<c>ThirdParty/imgui/imgui_internal.h:978</c>)
    /// </summary>
    public byte[] Data { get; } = new byte[8];
}

/// <summary>
/// Describes one <see cref="ImGuiDataType"/> for the scalar widget and input-text paths: the payload
/// size, a human-readable name, and the default printf-style print and scan formats
/// (<c>ThirdParty/imgui/imgui_internal.h:982-989</c>).
/// </summary>
/// <remarks>
/// The canonical structure is a plain aggregate populated by the file-scope <c>GDataTypeInfo</c>
/// table (<c>ThirdParty/imgui/imgui_widgets.cpp:2259-2276</c>). This port keeps it as a reference type
/// with a constructor so the table can be built once as a static array, matching the canonical
/// one-time initialization.
/// </remarks>
public sealed class ImGuiDataTypeInfo
{
    /// <summary>
    /// Gets the size of the payload in bytes. (<c>ThirdParty/imgui/imgui_internal.h:984</c>)
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Gets the human-readable type name used by diagnostics and the metrics window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:985</c>)
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the default printf-style format used to print a value of this type.
    /// (<c>ThirdParty/imgui/imgui_internal.h:986</c>)
    /// </summary>
    public string PrintFmt { get; }

    /// <summary>
    /// Gets the format used to scan a value of this type back from text.
    /// (<c>ThirdParty/imgui/imgui_internal.h:987</c>)
    /// </summary>
    public string ScanFmt { get; }

    /// <summary>
    /// Initializes a descriptor for one data type.
    /// </summary>
    /// <param name="size">The payload size in bytes.</param>
    /// <param name="name">The human-readable type name.</param>
    /// <param name="printFmt">The default print format.</param>
    /// <param name="scanFmt">The default scan format.</param>
    public ImGuiDataTypeInfo(int size, string name, string printFmt, string scanFmt)
    {
        Size = size;
        Name = name;
        PrintFmt = printFmt;
        ScanFmt = scanFmt;
    }
}

#endregion

#region Storage

/// <summary>
/// One key-and-value entry of <see cref="ImGuiStorage"/>, stored in ascending key order
/// (<c>ThirdParty/imgui/imgui.h:2946-2954</c>).
/// </summary>
/// <remarks>
/// The canonical type is a union of an integer, a float, and a pointer. This port exposes the three
/// interpretations as separate members; the storage holds no type tag, so only the member matching
/// the accessor used to create the entry is meaningful.
/// </remarks>
public sealed class ImGuiStoragePair
{
    /// <summary>
    /// Gets or sets the lookup key. (<c>ThirdParty/imgui/imgui.h:2949</c>)
    /// </summary>
    public uint Key { get; set; }

    /// <summary>
    /// Gets or sets the integer interpretation of the value.
    /// (<c>ThirdParty/imgui/imgui.h:2950</c>)
    /// </summary>
    public int ValueInt { get; set; }

    /// <summary>
    /// Gets or sets the floating-point interpretation of the value.
    /// (<c>ThirdParty/imgui/imgui.h:2950</c>)
    /// </summary>
    public float ValueFloat { get; set; }

    /// <summary>
    /// Gets or sets the pointer interpretation of the value.
    /// (<c>ThirdParty/imgui/imgui.h:2950</c>)
    /// </summary>
    public IntPtr ValuePointer { get; set; }
}

/// <summary>
/// A key-to-value store optimized for efficient lookup: entries are kept contiguous and sorted by
/// key so that reads use binary search and writes are rare
/// (<c>ThirdParty/imgui/imgui.h:2956-2999</c>).
/// </summary>
/// <remarks>
/// <para>
/// The store does not record a type per entry; the caller is responsible for using the same value
/// interpretation for a given key. <see cref="Data"/> must remain sorted ascending by
/// <see cref="ImGuiStoragePair.Key"/>.
/// </para>
/// <para>
/// The canonical <c>Get***Ref</c> accessors return pointers that any later insertion invalidates.
/// This port returns the stored <see cref="ImGuiStoragePair"/> instance instead; the returned
/// reference is invalidated in the same way, because membership is maintained in a list.
/// </para>
/// <para>
/// This type is plain single-threaded mutable state and provides no internal synchronization.
/// </para>
/// </remarks>
public sealed class ImGuiStorage
{
    /// <summary>
    /// Gets the sorted-by-key contiguous pair buffer. (<c>ThirdParty/imgui/imgui.h:2967</c>)
    /// </summary>
    public List<ImGuiStoragePair> Data { get; } = new();

    /// <summary>
    /// Removes every stored pair. (<c>ThirdParty/imgui/imgui.h:2972</c>)
    /// </summary>
    public void Clear()
    {
        Data.Clear();
    }

    /// <summary>
    /// Returns the integer stored for a key, or a default when the key is absent. The lookup never
    /// inserts. (<c>ThirdParty/imgui/imgui.cpp:3047</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The value returned when the key is absent.</param>
    /// <returns>The stored or default integer.</returns>
    public int GetInt(uint key, int defaultValue = 0)
    {
        int index = FindIndex(key);
        return index >= 0 ? Data[index].ValueInt : defaultValue;
    }

    /// <summary>
    /// Stores an integer for a key, inserting the pair in sorted position when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3107</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The integer to store.</param>
    public void SetInt(uint key, int value)
    {
        GetOrAddPair(key).ValueInt = value;
    }

    /// <summary>
    /// Returns the boolean stored for a key, or a default when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3055</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The value returned when the key is absent.</param>
    /// <returns>The stored or default boolean.</returns>
    public bool GetBool(uint key, bool defaultValue = false)
    {
        return GetInt(key, defaultValue ? 1 : 0) != 0;
    }

    /// <summary>
    /// Stores a boolean for a key. (<c>ThirdParty/imgui/imgui.cpp:3116</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The boolean to store.</param>
    public void SetBool(uint key, bool value)
    {
        SetInt(key, value ? 1 : 0);
    }

    /// <summary>
    /// Returns the float stored for a key, or a default when the key is absent. The lookup never
    /// inserts. (<c>ThirdParty/imgui/imgui.cpp:3060</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The value returned when the key is absent.</param>
    /// <returns>The stored or default float.</returns>
    public float GetFloat(uint key, float defaultValue = 0.0f)
    {
        int index = FindIndex(key);
        return index >= 0 ? Data[index].ValueFloat : defaultValue;
    }

    /// <summary>
    /// Stores a float for a key, inserting the pair in sorted position when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3121</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The float to store.</param>
    public void SetFloat(uint key, float value)
    {
        GetOrAddPair(key).ValueFloat = value;
    }

    /// <summary>
    /// Returns the pointer stored for a key, or <see cref="IntPtr.Zero"/> when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3068</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <returns>The stored pointer or <see cref="IntPtr.Zero"/>.</returns>
    public IntPtr GetVoidPtr(uint key)
    {
        int index = FindIndex(key);
        return index >= 0 ? Data[index].ValuePointer : IntPtr.Zero;
    }

    /// <summary>
    /// Stores a pointer for a key, inserting the pair in sorted position when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3130</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The pointer to store.</param>
    public void SetVoidPtr(uint key, IntPtr value)
    {
        GetOrAddPair(key).ValuePointer = value;
    }

    /// <summary>
    /// Returns the stored pair for a key, inserting a pair initialized to
    /// <paramref name="defaultValue"/> when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3077</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The integer used to initialize a newly inserted pair.</param>
    /// <returns>The stored pair, whose lifetime is bounded by the store.</returns>
    /// <remarks>
    /// A newly inserted pair carries <paramref name="defaultValue"/> in its integer interpretation, so a
    /// caller that reads the reference before writing observes the caller-supplied default rather than
    /// zero. (<c>ThirdParty/imgui/imgui.cpp:3077-3083</c>)
    /// </remarks>
    public ImGuiStoragePair GetIntRef(uint key, int defaultValue = 0)
    {
        int index = FindIndex(key);
        if (index >= 0)
            return Data[index];

        ImGuiStoragePair inserted = new() { Key = key, ValueInt = defaultValue };
        Data.Insert(~index, inserted);
        return inserted;
    }

    /// <summary>
    /// Returns the stored pair for a key, inserting a pair initialized to
    /// <paramref name="defaultValue"/> when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3090</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The float used to initialize a newly inserted pair.</param>
    /// <returns>The stored pair, whose lifetime is bounded by the store.</returns>
    /// <remarks>
    /// A newly inserted pair carries <paramref name="defaultValue"/> in its floating-point interpretation,
    /// so a caller that reads the reference before writing observes the caller-supplied default rather
    /// than zero. (<c>ThirdParty/imgui/imgui.cpp:3090-3095</c>)
    /// </remarks>
    public ImGuiStoragePair GetFloatRef(uint key, float defaultValue = 0.0f)
    {
        int index = FindIndex(key);
        if (index >= 0)
            return Data[index];

        ImGuiStoragePair inserted = new() { Key = key, ValueFloat = defaultValue };
        Data.Insert(~index, inserted);
        return inserted;
    }

    /// <summary>
    /// Returns the stored pair for a key interpreted as a boolean, inserting a pair initialized to
    /// <paramref name="defaultValue"/> when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3085-3088</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The boolean used to initialize a newly inserted pair.</param>
    /// <returns>The stored pair, whose lifetime is bounded by the store.</returns>
    /// <remarks>
    /// Canonical returns a <c>bool&amp;</c> aliasing the pair's integer interpretation; this port returns
    /// the pair instance, whose <see cref="ImGuiStoragePair.ValueInt"/> member carries the boolean value.
    /// The insertion and invalidation contract is identical to <see cref="GetIntRef"/>.
    /// </remarks>
    public ImGuiStoragePair GetBoolRef(uint key, bool defaultValue = false)
    {
        return GetIntRef(key, defaultValue ? 1 : 0);
    }

    /// <summary>
    /// Returns the stored pair for a key interpreted as a pointer, inserting a pair initialized to
    /// <paramref name="defaultValue"/> when the key is absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3098-3105</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The pointer used to initialize a newly inserted pair.</param>
    /// <returns>The stored pair, whose lifetime is bounded by the store.</returns>
    /// <remarks>
    /// Canonical returns a <c>void**</c> aliasing the pair's pointer interpretation; this port returns
    /// the pair instance, whose <see cref="ImGuiStoragePair.ValuePointer"/> member carries the pointer
    /// value. The insertion and invalidation contract is identical to <see cref="GetIntRef"/>.
    /// </remarks>
    public ImGuiStoragePair GetVoidPtrRef(uint key, IntPtr defaultValue = default)
    {
        int index = FindIndex(key);
        if (index >= 0)
            return Data[index];

        ImGuiStoragePair inserted = new() { Key = key, ValuePointer = defaultValue };
        Data.Insert(~index, inserted);
        return inserted;
    }

    /// <summary>
    /// Re-sorts the pair buffer by key. Intended for building many entries and sorting once.
    /// (<c>ThirdParty/imgui/imgui.cpp:3042</c>)
    /// </summary>
    public void BuildSortByKey()
    {
        Data.Sort(static (lhs, rhs) => lhs.Key.CompareTo(rhs.Key));
    }

    /// <summary>
    /// Exchanges the pair buffers of two stores.
    /// (<c>ThirdParty/imgui/imgui.h:2993</c>)
    /// </summary>
    /// <param name="other">The store whose pairs are exchanged with this store's pairs.</param>
    /// <remarks>
    /// Canonical <c>ImGuiStorage::Swap</c> swaps the <c>ImVector</c> backings directly. This port keeps
    /// the buffer object fixed so callers that cached it stay valid, and exchanges the pair entries
    /// instead: the shared prefix is swapped in place, then the surplus entries move from the longer
    /// buffer into the shorter one. Swap is application-facing rather than a per-frame path, so the
    /// list growth it can trigger is acceptable.
    /// </remarks>
    internal void SwapData(ImGuiStorage other)
    {
        int count = Data.Count;
        int otherCount = other.Data.Count;
        int common = Math.Min(count, otherCount);
        for (int n = 0; n < common; n++)
            (Data[n], other.Data[n]) = (other.Data[n], Data[n]);

        if (count > otherCount)
        {
            for (int n = common; n < count; n++)
                other.Data.Add(Data[n]);
            Data.RemoveRange(common, count - common);
        }
        else if (otherCount > count)
        {
            for (int n = common; n < otherCount; n++)
                Data.Add(other.Data[n]);
            other.Data.RemoveRange(common, otherCount - common);
        }
    }

    /// <summary>
    /// Overwrites the integer interpretation of every stored pair without changing keys.
    /// (<c>ThirdParty/imgui/imgui.cpp:3139</c>)
    /// </summary>
    /// <param name="value">The integer written to every pair.</param>
    public void SetAllInt(int value)
    {
        for (int n = 0; n < Data.Count; n++)
        {
            Data[n].ValueInt = value;
        }
    }

    /// <summary>
    /// Finds the index of a key with binary search.
    /// (<c>ThirdParty/imgui/imgui.cpp:3045-3053</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <returns>The pair index, or a negative value when the key is absent.</returns>
    private int FindIndex(uint key)
    {
        int low = 0;
        int high = Data.Count;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            uint candidate = Data[mid].Key;
            if (candidate < key)
            {
                low = mid + 1;
            }
            else if (candidate > key)
            {
                high = mid;
            }
            else
            {
                return mid;
            }
        }

        return ~low;
    }

    /// <summary>
    /// Returns the pair for a key, inserting it at the sorted position when absent.
    /// (<c>ThirdParty/imgui/imgui.cpp:3107-3135</c>)
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <returns>The existing or newly inserted pair.</returns>
    private ImGuiStoragePair GetOrAddPair(uint key)
    {
        int index = FindIndex(key);
        if (index >= 0)
        {
            return Data[index];
        }

        var pair = new ImGuiStoragePair { Key = key };
        Data.Insert(~index, pair);
        return pair;
    }
}

#endregion

#region Key ownership and routing

/// <summary>
/// Per-key ownership state for named keys, extending the plain key state with an explicit owner
/// requirement (<c>ThirdParty/imgui/imgui_internal.h:1655-1663</c>).
/// </summary>
/// <remarks>
/// The core stores one instance per named key in <see cref="ImGuiContext.KeysOwnerData"/>. The
/// canonical "no owner" value is zero.
/// </remarks>
public sealed class ImGuiKeyOwnerData
{
    /// <summary>
    /// Gets or sets the owner identifier observed during the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1657</c>)
    /// </summary>
    public uint OwnerCurr { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier to apply in the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1658</c>)
    /// </summary>
    public uint OwnerNext { get; set; }

    /// <summary>
    /// Gets or sets whether reading this key requires an explicit owner until the end of the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1659</c>)
    /// </summary>
    public bool LockThisFrame { get; set; }

    /// <summary>
    /// Gets or sets whether reading this key requires an explicit owner until the key is released.
    /// When set, <see cref="LockThisFrame"/> is always set as well.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1660</c>)
    /// </summary>
    public bool LockUntilRelease { get; set; }
}

/// <summary>
/// One entry of the key-chord routing table: the owner desired for a specific key-and-modifiers
/// combination (<c>ThirdParty/imgui/imgui_internal.h:1629-1641</c>).
/// </summary>
/// <remarks>
/// The canonical type is stored in dense lists owned by <see cref="ImGuiKeyRoutingTable"/>; this
/// port uses a reference type so an entry can be updated through the list indexer.
/// </remarks>
public sealed class ImGuiKeyRoutingData
{
    /// <summary>
    /// Gets or sets the index of the next entry that shares the same key.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1631</c>)
    /// </summary>
    public short NextEntryIndex { get; set; } = -1;

    /// <summary>
    /// Gets or sets the modifier bits stored with the chord; modifiers require 16 bits.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1632</c>)
    /// </summary>
    public ushort Mods { get; set; }

    /// <summary>
    /// Gets or sets the score computed for the current owner, used for debug display only.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1633</c>)
    /// </summary>
    public ushort RoutingCurrScore { get; set; }

    /// <summary>
    /// Gets or sets the score computed for the next owner; lower is better and zero is perfect.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1634</c>)
    /// </summary>
    public ushort RoutingNextScore { get; set; }

    /// <summary>
    /// Gets or sets the owner routing currently in effect. Zero means no owner.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1635</c>)
    /// </summary>
    public uint RoutingCurr { get; set; }

    /// <summary>
    /// Gets or sets the owner routing to apply on the next frame. Zero means no owner.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1636</c>)
    /// </summary>
    public uint RoutingNext { get; set; }
}

/// <summary>
/// The key-chord routing table: for each named key it stores a chain of entries that describe the
/// desired owner for every observed modifier combination
/// (<c>ThirdParty/imgui/imgui_internal.h:1643-1653</c>).
/// </summary>
/// <remarks>
/// Exactly one instance lives in <see cref="ImGuiContext.KeysRoutingTable"/>. The two entry lists
/// form a double buffer so that rebuilding the routing for the next frame does not reallocate.
/// </remarks>
public sealed class ImGuiKeyRoutingTable
{
    /// <summary>
    /// Gets the index of the first entry for each named key, or -1 when the key has no entries.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1645</c>)
    /// </summary>
    public short[] Index { get; } = CreateIndex();

    /// <summary>
    /// Gets the routing entries for the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1646</c>)
    /// </summary>
    public List<ImGuiKeyRoutingData> Entries { get; } = new();

    /// <summary>
    /// Gets the routing entries accumulated for the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1647</c>)
    /// </summary>
    public List<ImGuiKeyRoutingData> EntriesNext { get; } = new();

    /// <summary>
    /// Resets every key index to -1 and empties both entry lists.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1650</c>)
    /// </summary>
    public void Clear()
    {
        Array.Fill(Index, (short)-1);
        Entries.Clear();
        EntriesNext.Clear();
    }

    /// <summary>
    /// Empties both entry lists while preserving the per-key index values. This releases the entries
    /// without discarding the key mapping.
    /// </summary>
    public void ClearFreeMemory()
    {
        Entries.Clear();
        EntriesNext.Clear();
    }

    /// <summary>
    /// Creates the per-key index array initialized to -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1650</c>)
    /// </summary>
    /// <returns>The initialized index array.</returns>
    private static short[] CreateIndex()
    {
        var index = new short[(int)ImGuiKey.NamedKey_COUNT];
        Array.Fill(index, (short)-1);
        return index;
    }
}

#endregion

#region Font stack

/// <summary>
/// One entry of the font stack: the font that was bound and the sizes captured before and after
/// scaling, so <c>PopFont()</c> can restore the previous binding
/// (<c>ThirdParty/imgui/imgui_internal.h:936-941</c>).
/// </summary>
public sealed class ImFontStackData
{
    /// <summary>
    /// Gets or sets the bound font, or <see langword="null"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:938</c>)
    /// </summary>
    public ImFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the font size before scaling, equivalent to <c>style.FontSizeBase</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:939</c>)
    /// </summary>
    public float FontSizeBeforeScaling { get; set; }

    /// <summary>
    /// Gets or sets the font size after scaling, equivalent to the context font size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:940</c>)
    /// </summary>
    public float FontSizeAfterScaling { get; set; }
}

#endregion

#region Text helpers

/// <summary>
/// A growable UTF-8 text buffer used for accumulating log and settings text
/// (<c>ThirdParty/imgui/imgui.h:2926-2944</c>).
/// </summary>
/// <remarks>
/// The canonical buffer stores a zero-terminated <c>char</c> array; this port stores the same bytes
/// in a managed list. The store is single-threaded and owned by the type that declares it.
/// </remarks>
public sealed class ImGuiTextBuffer
{
    /// <summary>
    /// An empty, null-terminated stand-in returned by <see cref="CStr"/> when the buffer holds no text,
    /// mirroring the canonical static that lets a buffer be read safely before its first append.
    /// (<c>ThirdParty/imgui/imgui.h:2929</c>)
    /// </summary>
    public static readonly byte[] EmptyString = new byte[1];

    /// <summary>
    /// Gets the backing byte buffer. (<c>ThirdParty/imgui/imgui.h:2928</c>)
    /// </summary>
    public List<byte> Buf { get; } = new();

    /// <summary>
    /// Removes every byte from the buffer. (<c>ThirdParty/imgui/imgui.h:2939</c>)
    /// </summary>
    public void Clear()
    {
        Buf.Clear();
    }

    /// <summary>
    /// Appends the UTF-8 bytes of a string to the buffer.
    /// (<c>ThirdParty/imgui/imgui.h:2940</c>)
    /// </summary>
    /// <param name="text">The text to append.</param>
    public void Append(string text)
    {
        for (int n = 0; n < text.Length; n++)
        {
            Buf.Add((byte)text[n]);
        }
    }

    /// <summary>
    /// Appends a formatted string to the buffer.
    /// (<c>ThirdParty/imgui/imgui.h:2941</c>, <c>ThirdParty/imgui/imgui.cpp:3277-3286</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// Canonical <c>appendf</c> takes a C <c>printf</c> format; this port uses the managed composite-format
    /// syntax for the same purpose, which every caller already uses elsewhere. Formatting is performed with
    /// the invariant culture so that log and settings text does not vary with the host locale.
    /// </remarks>
    public void AppendF(string format, params object?[] args)
    {
        if (args.Length == 0)
        {
            Append(format);
            return;
        }
        Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
    }

    /// <summary>
    /// Appends a formatted string to the buffer, taking the argument array directly.
    /// (<c>ThirdParty/imgui/imgui.cpp:3286-3305</c>)
    /// </summary>
    /// <param name="format">A composite format string using the managed <c>{0}</c> placeholder syntax.</param>
    /// <param name="args">The objects to format into <paramref name="format"/>.</param>
    /// <remarks>
    /// The canonical <c>appendfv</c> exists so a varargs caller can forward an already-collected
    /// <c>va_list</c>; managed callers forward an array for the same reason.
    /// </remarks>
    public void AppendFV(string format, object?[] args) => AppendF(format, args);

    /// <summary>
    /// Returns the accumulated text as a string, or the empty string when nothing was appended.
    /// (<c>ThirdParty/imgui/imgui.h:2942</c>)
    /// </summary>
    /// <returns>The accumulated text.</returns>
    /// <remarks>
    /// The canonical <c>c_str()</c> returns a pointer into the buffer; this port materializes a managed
    /// string. Byte values are widened one-to-one, matching the byte-per-character encoding of
    /// <see cref="Append(string)"/>.
    /// </remarks>
    public string CStr()
    {
        if (Buf.Count == 0)
            return string.Empty;
        char[] chars = new char[Buf.Count];
        for (int n = 0; n < Buf.Count; n++)
            chars[n] = (char)Buf[n];
        return new string(chars);
    }

    /// <summary>
    /// Gets the number of text bytes currently held, excluding any terminator.
    /// (<c>ThirdParty/imgui/imgui.h:2935</c>)
    /// </summary>
    public int Size => Buf.Count;

    /// <summary>
    /// Gets a value indicating whether the buffer holds no text.
    /// (<c>ThirdParty/imgui/imgui.h:2936</c>)
    /// </summary>
    public bool Empty => Buf.Count == 0;
}

/// <summary>
/// A line index over an external text buffer: it records the byte offset at which each line begins
/// and the end offset of the final line (<c>ThirdParty/imgui/imgui_internal.h:837-847</c>).
/// </summary>
/// <remarks>
/// The index does not own the text it describes; the owner must append line offsets whenever it
/// appends text, or rebuild the index.
/// </remarks>
public sealed class ImGuiTextIndex
{
    /// <summary>
    /// Gets the byte offset at which each line begins.
    /// (<c>ThirdParty/imgui/imgui_internal.h:839</c>)
    /// </summary>
    public List<int> Offsets { get; } = new();

    /// <summary>
    /// Gets or sets the end offset of the text described by this index, because the index does not
    /// own the text buffer. (<c>ThirdParty/imgui/imgui_internal.h:840</c>)
    /// </summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// Empties the line offsets and resets the end offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:842</c>)
    /// </summary>
    public void Clear()
    {
        Offsets.Clear();
        EndOffset = 0;
    }

    /// <summary>
    /// Gets the number of indexed lines. (<c>ThirdParty/imgui/imgui_internal.h:848</c>)
    /// </summary>
    /// <returns>The count of recorded line-start offsets.</returns>
    public int Size() => Offsets.Count;

    /// <summary>
    /// Gets the offset at which a line begins.
    /// (<c>ThirdParty/imgui/imgui_internal.h:855</c>)
    /// </summary>
    /// <param name="line">The zero-based line index.</param>
    /// <returns>
    /// The line's start offset, or zero when the line index is beyond the indexed range, mirroring
    /// the canonical fallback to the base pointer.
    /// </returns>
    public int GetLineBegin(int line) => line < Offsets.Count ? Offsets[line] : 0;

    /// <summary>
    /// Gets the offset at which a line ends, excluding the line break.
    /// (<c>ThirdParty/imgui/imgui_internal.h:856</c>)
    /// </summary>
    /// <param name="line">The zero-based line index.</param>
    /// <returns>
    /// The offset of the line break that terminates the line, or <see cref="EndOffset"/> for the final
    /// indexed line, mirroring the canonical <c>Offsets[n + 1] - 1</c> expression.
    /// </returns>
    public int GetLineEnd(int line) => line + 1 < Offsets.Count ? Offsets[line + 1] - 1 : EndOffset;

    /// <summary>
    /// Extends the line index after text has been appended to the indexed buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:858</c>, <c>ThirdParty/imgui/imgui.cpp:3266-3278</c>)
    /// </summary>
    /// <param name="baseText">The text buffer the offsets refer to.</param>
    /// <param name="oldSize">The buffer length before the append.</param>
    /// <param name="newSize">The buffer length after the append.</param>
    /// <remarks>
    /// Ports canonical <c>ImGuiTextIndex::append</c> step for step: a no-op when nothing was appended,
    /// a start offset recorded when the buffer was empty or ended on a line break, then one start
    /// offset per line break found between the old and new ends, excluding a trailing break. The
    /// managed text is read by character index in place of the canonical pointer walk, so
    /// <paramref name="baseText"/> must be the buffer the offsets refer to.
    /// </remarks>
    public void Append(string baseText, int oldSize, int newSize)
    {
        ImAssert.Check(oldSize >= 0 && newSize >= oldSize && newSize >= EndOffset);
        if (oldSize == newSize)
            return;

        if (EndOffset == 0 || (EndOffset - 1 < baseText.Length && baseText[EndOffset - 1] == '\n'))
            Offsets.Add(EndOffset);

        for (int p = oldSize; p < newSize && p < baseText.Length; p++)
        {
            if (baseText[p] == '\n' && p + 1 < newSize)
                Offsets.Add(p + 1);
        }

        EndOffset = Math.Max(EndOffset, newSize);
    }
}

#endregion

#region Small stacked and scratch records

/// <summary>
/// Holds either a pointer or an index, whichever a tab-bar pool needs for a given bar
/// (<c>ThirdParty/imgui/imgui_internal.h:1495-1502</c>).
/// </summary>
/// <remarks>
/// Exactly one member is meaningful: the pointer constructor leaves <see cref="Index"/> at -1 and
/// the index constructor leaves <see cref="Ptr"/> at <see cref="IntPtr.Zero"/>.
/// </remarks>
public sealed class ImGuiPtrOrIndex
{
    /// <summary>
    /// Gets or sets the pointer form of the reference, or <see cref="IntPtr.Zero"/> when unused.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1497</c>)
    /// </summary>
    public IntPtr Ptr { get; set; }

    /// <summary>
    /// Gets or sets the index form of the reference, or -1 when unused.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1498</c>)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Initializes the record from a pointer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1500</c>)
    /// </summary>
    /// <param name="ptr">The pointer to store.</param>
    public ImGuiPtrOrIndex(IntPtr ptr)
    {
        Ptr = ptr;
        Index = -1;
    }

    /// <summary>
    /// Initializes the record from a pool index.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1501</c>)
    /// </summary>
    /// <param name="index">The index to store.</param>
    public ImGuiPtrOrIndex(int index)
    {
        Ptr = IntPtr.Zero;
        Index = index;
    }
}

/// <summary>
/// Scratch data used while shrinking columns to fit an available width
/// (<c>ThirdParty/imgui/imgui_internal.h:1488-1493</c>).
/// </summary>
public sealed class ImGuiShrinkWidthItem
{
    /// <summary>
    /// Gets or sets the item index being measured.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1490</c>)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the current width. (<c>ThirdParty/imgui/imgui_internal.h:1491</c>)
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Gets or sets the width before shrinking. (<c>ThirdParty/imgui/imgui_internal.h:1492</c>)
    /// </summary>
    public float InitialWidth { get; set; }
}

/// <summary>
/// Backs <c>IsItemDeactivated()</c> and <c>IsItemDeactivatedAfterEdit()</c> by remembering the item
/// that most recently deactivated and when (<c>ThirdParty/imgui/imgui_internal.h:1504-1512</c>).
/// </summary>
public sealed class ImGuiDeactivatedItemData
{
    /// <summary>
    /// Gets or sets the identifier of the item that recently deactivated.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1508</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the frame count at which the deactivation elapsed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1509</c>)
    /// </summary>
    public int ElapseFrame { get; set; }

    /// <summary>
    /// Gets or sets whether the item value had been edited before it deactivated.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1510</c>)
    /// </summary>
    public bool HasBeenEditedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the deactivation data is still observable by the application.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1511</c>)
    /// </summary>
    public bool IsAlive { get; set; }
}

/// <summary>
/// One entry of the focus-scope stack, pairing a focus scope with the window that declares it
/// (<c>ThirdParty/imgui/imgui_internal.h:1829-1834</c>).
/// </summary>
public sealed class ImGuiFocusScopeData
{
    /// <summary>
    /// Gets or sets the focus scope identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1832</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the window that owns the focus scope.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1833</c>)
    /// </summary>
    public uint WindowID { get; set; }
}

/// <summary>
/// One entry of the localization table: a localization key and its translated text
/// (<c>ThirdParty/imgui/imgui_internal.h:2294-2298</c>).
/// </summary>
public sealed class ImGuiLocEntry
{
    /// <summary>
    /// Gets or sets the localization key. (<c>ThirdParty/imgui/imgui_internal.h:2296</c>)
    /// </summary>
    public ImGuiLocKey Key { get; set; }

    /// <summary>
    /// Gets or sets the translated text. (<c>ThirdParty/imgui/imgui_internal.h:2297</c>)
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

#endregion

#region Group, menu, error, and window-stack records

/// <summary>
/// Saved cursor and interaction state for one <c>BeginGroup()</c>/<c>EndGroup()</c> nesting level
/// (<c>ThirdParty/imgui/imgui_internal.h:1211-1228</c>).
/// </summary>
/// <remarks>
/// Group state is deliberately not inherited by <c>Begin()</c>; each window starts with an empty
/// group stack.
/// </remarks>
public sealed class ImGuiGroupData
{
    /// <summary>
    /// Gets or sets the identifier of the window the group belongs to.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1214</c>)
    /// </summary>
    public uint WindowID { get; set; }

    /// <summary>
    /// Gets or sets the cursor position backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1215</c>)
    /// </summary>
    public ImVec2 BackupCursorPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor maximum-position backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1216</c>)
    /// </summary>
    public ImVec2 BackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the previous-line cursor position backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1217</c>)
    /// </summary>
    public ImVec2 BackupCursorPosPrevLine { get; set; }

    /// <summary>
    /// Gets or sets the indentation backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1218</c>)
    /// </summary>
    public ImVec1 BackupIndent { get; set; }

    /// <summary>
    /// Gets or sets the group-offset backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1219</c>)
    /// </summary>
    public ImVec1 BackupGroupOffset { get; set; }

    /// <summary>
    /// Gets or sets the current-line size backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1220</c>)
    /// </summary>
    public ImVec2 BackupCurrLineSize { get; set; }

    /// <summary>
    /// Gets or sets the current-line text baseline offset backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1221</c>)
    /// </summary>
    public float BackupCurrLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets the backup of the active-id aliveness value.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1222</c>)
    /// </summary>
    public uint BackupActiveIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the backup of <c>AnyIdHasBeenEditedThisFrame</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1223</c>)
    /// </summary>
    public bool BackupAnyIdHasBeenEditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets the backup of the deactivated-id aliveness value.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1224</c>)
    /// </summary>
    public bool BackupDeactivatedIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the backup of the hovered-id aliveness value.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1225</c>)
    /// </summary>
    public bool BackupHoveredIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the backup of the cursor same-line flag.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1226</c>)
    /// </summary>
    public bool BackupIsSameLine { get; set; }

    /// <summary>
    /// Gets or sets whether the group emits a parent item that can be hovered or activated.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1227</c>)
    /// </summary>
    public bool EmitItem { get; set; }
}

/// <summary>
/// Column width measurement for menu items. This is a short-lived helper for the menu code rather
/// than a general column implementation (<c>ThirdParty/imgui/imgui_internal.h:1230-1246</c>).
/// </summary>
/// <remarks>
/// Widths are measured during a frame and committed for the next one; the offsets are locked by the
/// update pass.
/// </remarks>
public sealed class ImGuiMenuColumns
{
    /// <summary>
    /// Gets or sets the total measured width.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1233</c>)
    /// </summary>
    public uint TotalWidth { get; set; }

    /// <summary>
    /// Gets or sets the committed width for the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1234</c>)
    /// </summary>
    public uint NextTotalWidth { get; set; }

    /// <summary>
    /// Gets or sets the spacing between columns.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1235</c>)
    /// </summary>
    public ushort Spacing { get; set; }

    /// <summary>
    /// Gets or sets the offset of the icon column, always zero in this version.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1236</c>)
    /// </summary>
    public ushort OffsetIcon { get; set; }

    /// <summary>
    /// Gets or sets the offset of the label column, locked by the update pass.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1237</c>)
    /// </summary>
    public ushort OffsetLabel { get; set; }

    /// <summary>
    /// Gets or sets the offset of the shortcut column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1238</c>)
    /// </summary>
    public ushort OffsetShortcut { get; set; }

    /// <summary>
    /// Gets or sets the offset of the mark column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1239</c>)
    /// </summary>
    public ushort OffsetMark { get; set; }

    /// <summary>
    /// Gets the width accumulators for the icon, label, shortcut, and mark columns.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1240</c>)
    /// </summary>
    public ushort[] Widths { get; } = new ushort[4];
}

/// <summary>
/// Snapshot of every recoverable stack size, used to detect missing end calls and to repair the
/// frame instead of crashing (<c>ThirdParty/imgui/imgui_internal.h:1460-1476</c>).
/// </summary>
/// <remarks>
/// The canonical type is a value structure with eleven <c>short</c> fields and a comment claiming a
/// size of 20 bytes; this port uses a reference type so that
/// <see cref="ImGuiContext.StackSizesInBeginForCurrentWindow"/> can alias the snapshot taken for the
/// current window.
/// </remarks>
public sealed class ImGuiErrorRecoveryState
{
    /// <summary>
    /// Gets or sets the saved window-stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1463</c>)
    /// </summary>
    public short SizeOfWindowStack { get; set; }

    /// <summary>
    /// Gets or sets the saved identifier-stack size of the current window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1464</c>)
    /// </summary>
    public short SizeOfIDStack { get; set; }

    /// <summary>
    /// Gets or sets the saved tree depth of the current window, not the tree-node stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1465</c>)
    /// </summary>
    public short SizeOfTreeStack { get; set; }

    /// <summary>
    /// Gets or sets the saved color-stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1466</c>)
    /// </summary>
    public short SizeOfColorStack { get; set; }

    /// <summary>
    /// Gets or sets the saved style-variable stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1467</c>)
    /// </summary>
    public short SizeOfStyleVarStack { get; set; }

    /// <summary>
    /// Gets or sets the saved font-stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1468</c>)
    /// </summary>
    public short SizeOfFontStack { get; set; }

    /// <summary>
    /// Gets or sets the saved focus-scope stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1469</c>)
    /// </summary>
    public short SizeOfFocusScopeStack { get; set; }

    /// <summary>
    /// Gets or sets the saved group-stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1470</c>)
    /// </summary>
    public short SizeOfGroupStack { get; set; }

    /// <summary>
    /// Gets or sets the saved item-flags stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1471</c>)
    /// </summary>
    public short SizeOfItemFlagsStack { get; set; }

    /// <summary>
    /// Gets or sets the saved begin-popup stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1472</c>)
    /// </summary>
    public short SizeOfBeginPopupStack { get; set; }

    /// <summary>
    /// Gets or sets the saved disabled-stack size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1473</c>)
    /// </summary>
    public short SizeOfDisabledStack { get; set; }
}

/// <summary>
/// Saved state for each window pushed onto the window stack
/// (<c>ThirdParty/imgui/imgui_internal.h:1478-1486</c>).
/// </summary>
public sealed class ImGuiWindowStackData
{
    /// <summary>
    /// Gets or sets the window that was pushed on the stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1481</c>)
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the backup of the last-item data taken before entering the window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1482</c>)
    /// </summary>
    public ImGuiLastItemData ParentLastItemDataBackup { get; set; } = new();

    /// <summary>
    /// Gets or sets the stack-size snapshot recorded when the window was entered.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1483</c>)
    /// </summary>
    public ImGuiErrorRecoveryState StackSizesInBegin { get; set; } = new();

    /// <summary>
    /// Gets or sets whether a non-child window overrode the disabled state and must re-enable it on
    /// the way out. (<c>ThirdParty/imgui/imgui_internal.h:1484</c>)
    /// </summary>
    public bool DisabledOverrideReenable { get; set; }

    /// <summary>
    /// Gets or sets the alpha backup associated with the disabled override.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1485</c>)
    /// </summary>
    public float DisabledOverrideReenableAlphaBackup { get; set; }

    /// <summary>
    /// Gets or sets the combo preview rect saved before opening a combo window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1548</c>)
    /// </summary>
    public ImRect ParentLastComboPreviewRect { get; set; }
}

/// <summary>
/// One entry of the tree-node stack, kept to support
/// <c>ImGuiTreeNodeFlags_NavLeftJumpsToParent</c>
/// (<c>ThirdParty/imgui/imgui_internal.h:1445-1458</c>).
/// </summary>
public sealed class ImGuiTreeNodeStackData
{
    /// <summary>
    /// Gets or sets the tree-node item identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1451</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the tree-node flags for this node.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1452</c>)
    /// </summary>
    public ImGuiTreeNodeFlags TreeFlags { get; set; }

    /// <summary>
    /// Gets or sets the item flags, used when navigation lands on the node.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1453</c>)
    /// </summary>
    public ImGuiItemFlags ItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the navigation rectangle, used when navigation lands on the node.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1454</c>)
    /// </summary>
    public ImRect NavRect { get; set; }

    /// <summary>
    /// Gets or sets the X coordinate at which tree lines start.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1455</c>)
    /// </summary>
    public float DrawLinesX1 { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate at which lines to child nodes end.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1456</c>)
    /// </summary>
    public float DrawLinesToNodesY2 { get; set; }

    /// <summary>
    /// Gets or sets the table column used for line drawing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1457</c>)
    /// </summary>
    public short DrawLinesTableColumn { get; set; }
}

#endregion

#region Item queue and stack records

/// <summary>
/// Status of the most recently submitted item, read by <c>IsItemHovered()</c>,
/// <c>IsItemActive()</c>, and the item-status helpers
/// (<c>ThirdParty/imgui/imgui_internal.h:1430-1443</c>).
/// </summary>
/// <remarks>
/// The core overwrites one instance per submitted item, so the data describes only the last item
/// submitted on the current thread. The rectangle members are valid only when the matching status
/// flag is set.
/// </remarks>
public sealed class ImGuiLastItemData
{
    /// <summary>
    /// Gets or sets the identifier of the last submitted item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1432</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the item flags of the last submitted item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1433</c>)
    /// </summary>
    public ImGuiItemFlags ItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the status flags of the last submitted item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1434</c>)
    /// </summary>
    public ImGuiItemStatusFlags StatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the full item rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1435</c>)
    /// </summary>
    public ImRect Rect { get; set; }

    /// <summary>
    /// Gets or sets the navigation scoring rectangle, which is not displayed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1436</c>)
    /// </summary>
    public ImRect NavRect { get; set; }

    /// <summary>
    /// Gets or sets the display rectangle; valid only when
    /// <see cref="ImGuiItemStatusFlags.HasDisplayRect"/> is set.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1438</c>)
    /// </summary>
    public ImRect DisplayRect { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle at submit time; valid only when
    /// <see cref="ImGuiItemStatusFlags.HasClipRect"/> is set.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1439</c>)
    /// </summary>
    public ImRect ClipRect { get; set; }

    /// <summary>
    /// Gets or sets the shortcut recorded at submit time; valid only when
    /// <see cref="ImGuiItemStatusFlags.HasShortcut"/> is set.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1440</c>)
    /// </summary>
    public ImGuiKey Shortcut { get; set; }

    /// <summary>
    /// Copies every field of another last-item record into this one.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1430-1443</c>)
    /// </summary>
    /// <param name="source">The record to snapshot.</param>
    /// <remarks>
    /// Canonical <c>ImGuiLastItemData</c> is a value type, so the window-stack backup
    /// (<c>ImGuiWindowStackData::ParentLastItemDataBackup</c>, <c>imgui_internal.h:1482</c>) is a byte
    /// copy and restoring it reinstates the parent's last item. This port represents the record as a
    /// reference type because the context owns exactly one instance that the item submission path
    /// mutates in place; a plain assignment would alias instead of snapshot, so every backup and
    /// restore site calls this method to reproduce the canonical value semantics without allocating.
    /// </remarks>
    public void CopyFrom(ImGuiLastItemData source)
    {
        ID = source.ID;
        ItemFlags = source.ItemFlags;
        StatusFlags = source.StatusFlags;
        Rect = source.Rect;
        NavRect = source.NavRect;
        DisplayRect = source.DisplayRect;
        ClipRect = source.ClipRect;
        Shortcut = source.Shortcut;
    }
}

/// <summary>
/// Queued <c>SetNextItem**</c> values applied to the next submitted item
/// (<c>ThirdParty/imgui/imgui_internal.h:1408-1427</c>).
/// </summary>
/// <remarks>
/// Members below <see cref="ItemFlagsSet"/> are not cleared by the item-add pass so that navigation
/// can still read them after the item was submitted; <see cref="HasFlags"/> is the authority for
/// which values are valid.
/// </remarks>
public sealed class ImGuiNextItemData
{
    /// <summary>
    /// Gets or sets the bitmask of queued values that are valid. The name avoids confusion with the
    /// public item flags. (<c>ThirdParty/imgui/imgui_internal.h:1410</c>)
    /// </summary>
    public ImGuiNextItemDataFlags HasFlags { get; set; }

    /// <summary>
    /// Gets or sets the item flags to apply to the next item. Currently tested for
    /// <c>AllowOverlap</c> and <c>HasSelectionUserData</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1411</c>)
    /// </summary>
    public ImGuiItemFlags ItemFlagsSet { get; set; }

    /// <summary>
    /// Gets or sets the focus scope queued by <c>SetNextItemSelectionUserData()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1414</c>)
    /// </summary>
    public uint FocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the selection user data queued by
    /// <c>SetNextItemSelectionUserData()</c>; -1 means invalid.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1415</c>)
    /// </summary>
    public long SelectionUserData { get; set; } = -1;

    /// <summary>
    /// Gets or sets the width queued by <c>SetNextItemWidth()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1416</c>)
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Gets or sets the shortcut queued by <c>SetNextItemShortcut()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1417</c>)
    /// </summary>
    public ImGuiKey Shortcut { get; set; }

    /// <summary>
    /// Gets or sets the shortcut flags queued by <c>SetNextItemShortcut()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1418</c>)
    /// </summary>
    public ImGuiInputFlags ShortcutFlags { get; set; }

    /// <summary>
    /// Gets or sets the open value queued by <c>SetNextItemOpen()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1419</c>)
    /// </summary>
    public bool OpenVal { get; set; }

    /// <summary>
    /// Gets or sets the open condition queued by <c>SetNextItemOpen()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1420</c>)
    /// </summary>
    public byte OpenCond { get; set; }

    /// <summary>
    /// Gets or sets the reference value used by
    /// <c>ImGuiInputTextFlags_ParseEmptyAsRefVal</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1421</c>)
    /// </summary>
    public ImGuiDataTypeStorage RefVal { get; set; } = new();

    /// <summary>
    /// Gets or sets the storage identifier queued by <c>SetNextItemStorageID()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1422</c>)
    /// </summary>
    public uint StorageId { get; set; }

    /// <summary>
    /// Gets or sets the color marker queued by <c>SetNextItemColorMarker()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1423</c>)
    /// </summary>
    public uint ColorMarker { get; set; }

    /// <summary>
    /// Clears the valid-value mask and the queued item flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1426</c>)
    /// </summary>
    public void ClearFlags()
    {
        HasFlags = ImGuiNextItemDataFlags.None;
        ItemFlagsSet = ImGuiItemFlags.None;
    }
}

/// <summary>
/// Queued <c>SetNextWindow**</c> values applied when the next window begins
/// (<c>ThirdParty/imgui/imgui_internal.h:1364-1395</c>).
/// </summary>
/// <remarks>
/// Members below <see cref="HasFlags"/> are intentionally not cleared between windows, so callers
/// must always consult <see cref="HasFlags"/> rather than the value alone.
/// </remarks>
public sealed class ImGuiNextWindowData
{
    /// <summary>
    /// Gets or sets the bitmask of queued values that are valid.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1367</c>)
    /// </summary>
    public ImGuiNextWindowDataFlags HasFlags { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowPos()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1370</c>)
    /// </summary>
    public ImGuiCond PosCond { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowSize()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1371</c>)
    /// </summary>
    public ImGuiCond SizeCond { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowCollapsed()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1372</c>)
    /// </summary>
    public ImGuiCond CollapsedCond { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowDockID()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1373</c>)
    /// </summary>
    public ImGuiCond DockCond { get; set; }

    /// <summary>
    /// Gets or sets the requested window position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1374</c>)
    /// </summary>
    public ImVec2 PosVal { get; set; }

    /// <summary>
    /// Gets or sets the requested position pivot.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1375</c>)
    /// </summary>
    public ImVec2 PosPivotVal { get; set; }

    /// <summary>
    /// Gets or sets the requested window size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1376</c>)
    /// </summary>
    public ImVec2 SizeVal { get; set; }

    /// <summary>
    /// Gets or sets the requested content size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1377</c>)
    /// </summary>
    public ImVec2 ContentSizeVal { get; set; }

    /// <summary>
    /// Gets or sets the requested scroll value.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1378</c>)
    /// </summary>
    public ImVec2 ScrollVal { get; set; }

    /// <summary>
    /// Gets or sets the window-flags override. The canonical comment notes that it is honored only
    /// by <c>BeginTable()</c>. (<c>ThirdParty/imgui/imgui_internal.h:1379</c>)
    /// </summary>
    public ImGuiWindowFlags WindowFlags { get; set; }

    /// <summary>
    /// Gets or sets the child-flags override.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1380</c>)
    /// </summary>
    public ImGuiChildFlags ChildFlags { get; set; }

    /// <summary>
    /// Gets or sets whether the window should undock when the queued dock position is applied.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1381</c>)
    /// </summary>
    public bool PosUndock { get; set; }

    /// <summary>
    /// Gets or sets the requested collapsed state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1382</c>)
    /// </summary>
    public bool CollapsedVal { get; set; }

    /// <summary>
    /// Gets or sets the rectangle used when evaluating a size-constraint callback.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1383</c>)
    /// </summary>
    public ImRect SizeConstraintRect { get; set; }

    /// <summary>
    /// Gets or sets the user size-constraint callback, or <see langword="null"/> when none is queued.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1384</c>)
    /// </summary>
    public ImGuiSizeCallback? SizeCallback { get; set; }

    /// <summary>
    /// Gets or sets the user data passed to the size-constraint callback.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1385</c>)
    /// </summary>
    public IntPtr SizeCallbackUserData { get; set; }

    /// <summary>
    /// Gets or sets the background alpha override.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1386</c>)
    /// </summary>
    public float BgAlphaVal { get; set; }

    /// <summary>
    /// Gets or sets the requested viewport identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1387</c>)
    /// </summary>
    public uint ViewportId { get; set; }

    /// <summary>
    /// Gets or sets the requested dock node identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1388</c>)
    /// </summary>
    public uint DockId { get; set; }

    /// <summary>
    /// Gets or sets the requested window class.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1389</c>)
    /// </summary>
    public ImGuiWindowClass WindowClass { get; set; } = new();

    /// <summary>
    /// Gets or sets the always-on menu-bar minimum offset, which is deliberately not cleared between
    /// windows. (<c>ThirdParty/imgui/imgui_internal.h:1390</c>)
    /// </summary>
    public ImVec2 MenuBarOffsetMinVal { get; set; }

    /// <summary>
    /// Gets or sets the requested refresh policy.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1391</c>)
    /// </summary>
    public ImGuiWindowRefreshFlags RefreshFlagsVal { get; set; }

    /// <summary>
    /// Gets or sets window flags to set. Use <c>SetNextWindowFlags()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1439</c>)
    /// </summary>
    public ImGuiWindowFlags WindowFlagsSet { get; set; }

    /// <summary>
    /// Gets or sets window flags to clear. Use <c>SetNextWindowFlags()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1440</c>)
    /// </summary>
    public ImGuiWindowFlags WindowFlagsClear { get; set; }

    /// <summary>
    /// Gets or sets child flags to set. Use <c>SetNextWindowChildFlags()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1441</c>)
    /// </summary>
    public ImGuiChildFlags ChildFlagsSet { get; set; }

    /// <summary>
    /// Gets or sets child flags to clear. Use <c>SetNextWindowChildFlags()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1442</c>)
    /// </summary>
    public ImGuiChildFlags ChildFlagsClear { get; set; }

    /// <summary>
    /// Resets the valid-value mask to no queued data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1394</c>)
    /// </summary>
    public void ClearFlags()
    {
        HasFlags = ImGuiNextWindowDataFlags.None;
        WindowFlagsSet = WindowFlagsClear = ImGuiWindowFlags.None;
        ChildFlagsSet = ChildFlagsClear = ImGuiChildFlags.None;
    }

    /// <summary>
    /// Sets the queued dock identifier and condition flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1396</c>)
    /// </summary>
    /// <param name="dockId">Target dock identifier.</param>
    /// <param name="cond">Apply condition.</param>
    public void SetDockId(uint dockId, ImGuiCond cond)
    {
        DockId = dockId;
        DockCond = cond;
        HasFlags |= ImGuiNextWindowDataFlags.HasDock;
    }
}

/// <summary>
/// One entry of the open-popup and begin-popup stacks
/// (<c>ThirdParty/imgui/imgui_internal.h:1525-1538</c>).
/// </summary>
public sealed class ImGuiPopupData
{
    /// <summary>
    /// Gets or sets the popup identifier assigned by <c>OpenPopup()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1528</c>)
    /// </summary>
    public uint PopupId { get; set; }

    /// <summary>
    /// Gets or sets the window resolved on <c>BeginPopup()</c>; it may remain unresolved.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1529</c>)
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the navigation window to restore when the popup closes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1530</c>)
    /// </summary>
    public ImGuiWindow? RestoreNavWindow { get; set; }

    /// <summary>
    /// Gets or sets the navigation layer of the popup; -1 means none.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1531</c>)
    /// </summary>
    public int ParentNavLayer { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame count at which the popup was opened.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1532</c>)
    /// </summary>
    public int OpenFrameCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the parent identifier that distinguishes multiple menu sets.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1533</c>)
    /// </summary>
    public uint OpenParentId { get; set; }

    /// <summary>
    /// Gets or sets the preferred popup position at open time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1534</c>)
    /// </summary>
    public ImVec2 OpenPopupPos { get; set; }

    /// <summary>
    /// Gets or sets the mouse position at open time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1535</c>)
    /// </summary>
    public ImVec2 OpenMousePos { get; set; }
}

/// <summary>
/// Best candidate produced by a navigation query, stored for initialization, local moves, local
/// visible moves, other moves, and tabbing (<c>ThirdParty/imgui/imgui_internal.h:1812-1827</c>).
/// </summary>
/// <remarks>
/// <see cref="RectRel"/> is deliberately not reset by <see cref="Clear"/> in canonical, because a
/// zero rectangle is a meaningful "no candidate" value for the other members.
/// </remarks>
public sealed class ImGuiNavItemData
{
    /// <summary>
    /// Gets or sets the best candidate window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1815</c>)
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the best candidate item identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1816</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the best candidate focus scope identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1817</c>)
    /// </summary>
    public uint FocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the candidate bounding box in window-relative space.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1818</c>)
    /// </summary>
    public ImRect RectRel { get; set; }

    /// <summary>
    /// Gets or sets the best candidate item flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1819</c>)
    /// </summary>
    public ImGuiItemFlags ItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the box distance to the current navigation identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1820</c>)
    /// </summary>
    public float DistBox { get; set; }

    /// <summary>
    /// Gets or sets the center distance to the current navigation identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1821</c>)
    /// </summary>
    public float DistCenter { get; set; }

    /// <summary>
    /// Gets or sets the axial distance to the current navigation identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1822</c>)
    /// </summary>
    public float DistAxial { get; set; }

    /// <summary>
    /// Gets or sets the best candidate selection user data; -1 means invalid.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1823</c>)
    /// </summary>
    public long SelectionUserData { get; set; } = -1;

    /// <summary>
    /// Resets every candidate field except <see cref="RectRel"/> and restores the distance fields to
    /// their maximum values. (<c>ThirdParty/imgui/imgui_internal.h:1826</c>)
    /// </summary>
    public void Clear()
    {
        Window = null;
        ID = 0;
        FocusScopeId = 0;
        ItemFlags = ImGuiItemFlags.None;
        SelectionUserData = -1;
        DistBox = float.MaxValue;
        DistCenter = float.MaxValue;
        DistAxial = float.MaxValue;
    }
}

#endregion

#region Hooks, callbacks, and settings records

/// <summary>
/// One registered context hook: the event type it observes, the owner that registered it, and the
/// callback to invoke (<c>ThirdParty/imgui/imgui_internal.h:2427-2436</c>).
/// </summary>
public sealed class ImGuiContextHook
{
    /// <summary>
    /// Gets or sets the unique hook identifier assigned by <c>AddContextHook()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2429</c>)
    /// </summary>
    public uint HookId { get; set; }

    /// <summary>
    /// Gets or sets the hook event type.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2430</c>)
    /// </summary>
    public ImGuiContextHookType Type { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the owning subsystem.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2431</c>)
    /// </summary>
    public uint Owner { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked for the hook, or <see langword="null"/> when unused.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2432</c>)
    /// </summary>
    public ImGuiContextHookCallback? Callback { get; set; }

    /// <summary>
    /// Gets or sets the opaque caller data passed to the callback.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2433</c>)
    /// </summary>
    public IntPtr UserData { get; set; }
}

/// <summary>
/// Read-mostly data supplied to an <see cref="ImGuiSizeCallback"/> while the core resolves a
/// window's size (<c>ThirdParty/imgui/imgui.h:2822-2830</c>).
/// </summary>
/// <remarks>
/// The core owns the instance and passes the same object to the callback. The application may write
/// only <see cref="DesiredSize"/>; the other members are read-only inputs.
/// </remarks>
public sealed class ImGuiSizeCallbackData
{
    /// <summary>
    /// Gets or sets the opaque pointer originally passed to
    /// <c>SetNextWindowSizeConstraints()</c>. (<c>ThirdParty/imgui/imgui.h:2826</c>)
    /// </summary>
    public IntPtr UserData { get; set; }

    /// <summary>
    /// Gets or sets the window position for reference.
    /// (<c>ThirdParty/imgui/imgui.h:2827</c>)
    /// </summary>
    public ImVec2 Pos { get; set; }

    /// <summary>
    /// Gets or sets the current window size.
    /// (<c>ThirdParty/imgui/imgui.h:2828</c>)
    /// </summary>
    public ImVec2 CurrentSize { get; set; }

    /// <summary>
    /// Gets or sets the desired window size; writing this member restrains resizing.
    /// (<c>ThirdParty/imgui/imgui.h:2829</c>)
    /// </summary>
    public ImVec2 DesiredSize { get; set; }
}

/// <summary>
/// Advanced docking hints applied to a window and inherited by its children
/// (<c>ThirdParty/imgui/imgui.h:2832-2853</c>).
/// </summary>
/// <remarks>
/// Windows of different non-zero class identifiers cannot dock together. The class is copied into
/// the window when it begins, so later changes do not affect an already-open window.
/// </remarks>
public sealed class ImGuiWindowClass
{
    /// <summary>
    /// Gets or sets the user-defined class identifier; zero is the default class.
    /// (<c>ThirdParty/imgui/imgui.h:2841</c>)
    /// </summary>
    public uint ClassId { get; set; }

    /// <summary>
    /// Gets or sets the parent viewport hint: -1 selects the default, zero requests no parent, and
    /// any other value requests a parent/child platform relationship.
    /// (<c>ThirdParty/imgui/imgui.h:2842</c>)
    /// </summary>
    public uint ParentViewportId { get; set; } = uint.MaxValue;

    /// <summary>
    /// Gets or sets the parent window used when evaluating shortcut focus routes.
    /// (<c>ThirdParty/imgui/imgui.h:2843</c>)
    /// </summary>
    public uint FocusRouteParentWindowId { get; set; }

    /// <summary>
    /// Gets or sets the viewport flags to set when this class owns a viewport.
    /// (<c>ThirdParty/imgui/imgui.h:2844</c>)
    /// </summary>
    public ImGuiViewportFlags ViewportFlagsOverrideSet { get; set; }

    /// <summary>
    /// Gets or sets the viewport flags to clear when this class owns a viewport.
    /// (<c>ThirdParty/imgui/imgui.h:2845</c>)
    /// </summary>
    public ImGuiViewportFlags ViewportFlagsOverrideClear { get; set; }

    /// <summary>
    /// Gets or sets the tab-item flags applied when the window is submitted into a dock tab bar.
    /// (<c>ThirdParty/imgui/imgui.h:2846</c>)
    /// </summary>
    public ImGuiTabItemFlags TabItemFlagsOverrideSet { get; set; }

    /// <summary>
    /// Gets or sets the dock-node flags applied when the window is hosted by a dock node.
    /// (<c>ThirdParty/imgui/imgui.h:2847</c>)
    /// </summary>
    public ImGuiDockNodeFlags DockNodeFlagsOverrideSet { get; set; }

    /// <summary>
    /// Gets or sets whether floating windows of this class always receive their own dock node.
    /// (<c>ThirdParty/imgui/imgui.h:2848</c>)
    /// </summary>
    public bool DockingAlwaysTabBar { get; set; }

    /// <summary>
    /// Gets or sets whether windows of this class may dock and merge with unclassed windows.
    /// (<c>ThirdParty/imgui/imgui.h:2849</c>)
    /// </summary>
    public bool DockingAllowUnclassed { get; set; } = true;

    /// <summary>
    /// Gets or sets opaque platform icon data.
    /// (<c>ThirdParty/imgui/imgui.h:2850</c>)
    /// </summary>
    public IntPtr PlatformIconData { get; set; }
}

/// <summary>
/// Persistent per-window settings stored in the layout file
/// (<c>ThirdParty/imgui/imgui_internal.h:2218-2239</c>).
/// </summary>
/// <remarks>
/// Canonical stores the variable-length name immediately after the structure in a chunk stream;
/// this port stores it in <see cref="Name"/>. Setting <see cref="WantDelete"/> invalidates the
/// entry, and <see cref="WantApply"/> marks an entry loaded from disk that has not yet been merged
/// into a running context.
/// </remarks>
public sealed class ImGuiWindowSettings
{
    /// <summary>
    /// Gets or sets the window identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2223</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the window position relative to its viewport; runtime positions are absolute.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2224</c>)
    /// </summary>
    public ImVec2ih Pos { get; set; }

    /// <summary>
    /// Gets or sets the saved window size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2225</c>)
    /// </summary>
    public ImVec2ih Size { get; set; }

    /// <summary>
    /// Gets or sets the saved viewport position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2226</c>)
    /// </summary>
    public ImVec2ih ViewportPos { get; set; }

    /// <summary>
    /// Gets or sets the saved viewport identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2227</c>)
    /// </summary>
    public uint ViewportId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last known dock node, even when invisible, or zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2228</c>)
    /// </summary>
    public uint DockId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the window class when one was specified.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2229</c>)
    /// </summary>
    public uint ClassId { get; set; }

    /// <summary>
    /// Gets or sets the order within the dock node on same-frame reappearance.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2230</c>)
    /// </summary>
    public short DockOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the packed last-used date.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2231</c>)
    /// </summary>
    public ImGuiPackedDate LastUsedDate { get; set; }

    /// <summary>
    /// Gets or sets the saved collapsed state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2232</c>)
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Gets or sets the saved child-window state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2233</c>)
    /// </summary>
    public bool IsChild { get; set; }

    /// <summary>
    /// Gets or sets whether the entry was loaded from layout data and must be merged into the
    /// running context. (<c>ThirdParty/imgui/imgui_internal.h:2234</c>)
    /// </summary>
    public bool WantApply { get; set; }

    /// <summary>
    /// Gets or sets whether the entry must be invalidated and deleted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2235</c>)
    /// </summary>
    public bool WantDelete { get; set; }

    /// <summary>
    /// Gets or sets the window name carried by the settings entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2238</c>)
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Filtering and date-migration options accepted by
/// <see cref="ImGui.CleanupIniSettings"/>, used to prune stale `.ini` entries
/// (<c>ThirdParty/imgui/imgui_internal.h:2241-2249</c>).
/// </summary>
public sealed class ImGuiSettingsCleanupArgs
{
    /// <summary>
    /// Gets or sets a settings-type hash that restricts the operation to one handler, or zero to affect
    /// every handler that supports cleanup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2243</c>)
    /// </summary>
    public uint TypeHashFilter { get; set; }

    /// <summary>
    /// Gets or sets the age in months beyond which an entry is discarded; zero disables age-based discard.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2244</c>)
    /// </summary>
    public int DiscardOlderThanMonths { get; set; }

    /// <summary>
    /// Gets or sets whether entries that carry no last-used date are discarded.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2245</c>)
    /// </summary>
    public bool DiscardWhenMissingDate { get; set; }

    /// <summary>
    /// Gets or sets whether every entry is discarded, which is equivalent to calling
    /// <see cref="ImGui.ClearIniSettings"/> except that the <see cref="TypeHashFilter"/> still applies.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2246</c>)
    /// </summary>
    public bool DiscardAll { get; set; }

    /// <summary>
    /// Gets or sets whether every supporting entry is stamped with the current session date.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2247</c>)
    /// </summary>
    public bool SetCurrentSessionDateToAll { get; set; }

    /// <summary>
    /// Gets or sets whether only entries missing a date are stamped with the current session date.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2248</c>)
    /// </summary>
    public bool SetCurrentSessionDateWhenMissingDate { get; set; }

    /// <summary>
    /// Gets or sets the unpacked date derived from <see cref="DiscardOlderThanMonths"/> once the cleanup
    /// pass begins; it is an implementation detail of the cleanup pass and not a caller option.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2249</c>)
    /// </summary>
    public int _DiscardOlderThanDate { get; set; }
}

/// <summary>
/// One `.ini` section handler. Each handler owns one `[Type][Name]` section family and receives the
/// canonical read and write callbacks (<c>ThirdParty/imgui/imgui_internal.h:2252-2266</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical stores raw C function pointers; this port stores managed delegates with the same names and
/// argument order, plus <see cref="UserData"/> for the handler's own state. A null delegate is the
/// equivalent of a null canonical pointer and is skipped.
/// </para>
/// <para>
/// Handlers are invoked in registration order for the read callbacks and in registration order for the
/// write callbacks, which is what makes the emitted `.ini` sections appear in the canonical
/// Window, Table, Docking order.
/// </para>
/// </remarks>
public sealed class ImGuiSettingsHandler
{
    /// <summary>
    /// Gets or sets the short type name written between the section brackets. Canonical disallows
    /// <c>'['</c> and <c>']'</c> in it.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2254</c>)
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hash of <see cref="TypeName"/>, used for identifier-based lookup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2255</c>)
    /// </summary>
    public uint TypeHash { get; set; }

    /// <summary>
    /// Gets or sets the callback that clears all settings data for this handler.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2256</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler>? ClearAllFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked before reading begins, in registration order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2257</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler>? ReadInitFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when entering a new `[Type][Name]` entry, returning the entry
    /// object passed back to <see cref="ReadLineFn"/>, or null to ignore the entry's lines.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2258</c>)
    /// </summary>
    public Func<ImGuiContext, ImGuiSettingsHandler, string, object?>? ReadOpenFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked for every line of text inside a read entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2259</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler, object, string>? ReadLineFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked after reading ends, in registration order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2260</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler>? ApplyAllFn { get; set; }

    /// <summary>
    /// Gets or sets the callback that appends this handler's entries to the output buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2261</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler, ImGuiTextBuffer>? WriteAllFn { get; set; }

    /// <summary>
    /// Gets or sets the callback that prunes or stamps this handler's entries.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2262</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler, ImGuiSettingsCleanupArgs>? CleanupFn { get; set; }

    /// <summary>
    /// Gets or sets an opaque value association owned by the registering subsystem.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2263</c>)
    /// </summary>
    public object? UserData { get; set; }
}

#endregion

#region Input events

/// <summary>
/// Payload of a mouse-position event (<c>ThirdParty/imgui/imgui_internal.h:1593</c>).
/// </summary>
public sealed class ImGuiInputEventMousePos
{
    /// <summary>
    /// Gets or sets the absolute horizontal mouse position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1593</c>)
    /// </summary>
    public float PosX { get; set; }

    /// <summary>
    /// Gets or sets the absolute vertical mouse position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1593</c>)
    /// </summary>
    public float PosY { get; set; }

    /// <summary>
    /// Gets or sets the device that produced the event (mouse, touch, or pen).
    /// (<c>ThirdParty/imgui/imgui_internal.h:1593</c>)
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; }
}

/// <summary>
/// Payload of a mouse-wheel event (<c>ThirdParty/imgui/imgui_internal.h:1594</c>).
/// </summary>
public sealed class ImGuiInputEventMouseWheel
{
    /// <summary>
    /// Gets or sets the horizontal wheel delta.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1594</c>)
    /// </summary>
    public float WheelX { get; set; }

    /// <summary>
    /// Gets or sets the vertical wheel delta.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1594</c>)
    /// </summary>
    public float WheelY { get; set; }

    /// <summary>
    /// Gets or sets the device that produced the event.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1594</c>)
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; }
}

/// <summary>
/// Payload of a mouse-button transition event
/// (<c>ThirdParty/imgui/imgui_internal.h:1595</c>).
/// </summary>
public sealed class ImGuiInputEventMouseButton
{
    /// <summary>
    /// Gets or sets the mouse button index.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1595</c>)
    /// </summary>
    public int Button { get; set; }

    /// <summary>
    /// Gets or sets the new button state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1595</c>)
    /// </summary>
    public bool Down { get; set; }

    /// <summary>
    /// Gets or sets the device that produced the event.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1595</c>)
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; }
}

/// <summary>
/// Payload of a mouse viewport-change event
/// (<c>ThirdParty/imgui/imgui_internal.h:1596</c>).
/// </summary>
public sealed class ImGuiInputEventMouseViewport
{
    /// <summary>
    /// Gets or sets the reported hovered viewport identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1596</c>)
    /// </summary>
    public uint HoveredViewportID { get; set; }
}

/// <summary>
/// Payload of a keyboard or gamepad key transition event
/// (<c>ThirdParty/imgui/imgui_internal.h:1597</c>).
/// </summary>
public sealed class ImGuiInputEventKey
{
    /// <summary>
    /// Gets or sets the key or gamepad button that changed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1597</c>)
    /// </summary>
    public ImGuiKey Key { get; set; }

    /// <summary>
    /// Gets or sets the new key state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1597</c>)
    /// </summary>
    public bool Down { get; set; }

    /// <summary>
    /// Gets or sets the analog value for analog gamepad keys.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1597</c>)
    /// </summary>
    public float AnalogValue { get; set; }
}

/// <summary>
/// Payload of a Unicode text-input event
/// (<c>ThirdParty/imgui/imgui_internal.h:1598</c>).
/// </summary>
public sealed class ImGuiInputEventText
{
    /// <summary>
    /// Gets or sets the Unicode codepoint that was typed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1598</c>)
    /// </summary>
    public uint Char { get; set; }
}

/// <summary>
/// Payload of an application focus event
/// (<c>ThirdParty/imgui/imgui_internal.h:1599</c>).
/// </summary>
public sealed class ImGuiInputEventAppFocused
{
    /// <summary>
    /// Gets or sets the new application focus state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1599</c>)
    /// </summary>
    public bool Focused { get; set; }
}

/// <summary>
/// One queued input event, discriminated by <see cref="Type"/>, as produced by the backend and
/// drained during the frame (<c>ThirdParty/imgui/imgui_internal.h:1601-1619</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical stores the seven payloads in an anonymous union, so exactly one member is meaningful
/// for a given <see cref="Type"/>. This port models the union as one reference-typed payload object
/// per event type; every payload object is allocated with the event, and callers must consult
/// <see cref="Type"/> to decide which payload is valid.
/// </para>
/// <para>
/// Instances are queued in <see cref="ImGuiContext.InputEventsQueue"/> and drained into the input
/// state during the frame. The canonical validation that the union is trivially copyable does not
/// apply to this managed representation.
/// </para>
/// </remarks>
public sealed class ImGuiInputEvent
{
    /// <summary>
    /// Gets or sets the discriminator that selects the meaningful payload.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1603</c>)
    /// </summary>
    public ImGuiInputEventType Type { get; set; }

    /// <summary>
    /// Gets or sets the input source (mouse, keyboard, or gamepad).
    /// (<c>ThirdParty/imgui/imgui_internal.h:1604</c>)
    /// </summary>
    public ImGuiInputSource Source { get; set; }

    /// <summary>
    /// Gets or sets the sequentially increasing event identifier used to correlate events with other
    /// data. (<c>ThirdParty/imgui/imgui_internal.h:1605</c>)
    /// </summary>
    public uint EventId { get; set; }

    /// <summary>
    /// Gets the mouse-position payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MousePos"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1608</c>)
    /// </summary>
    public ImGuiInputEventMousePos MousePos { get; } = new();

    /// <summary>
    /// Gets the mouse-wheel payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MouseWheel"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1609</c>)
    /// </summary>
    public ImGuiInputEventMouseWheel MouseWheel { get; } = new();

    /// <summary>
    /// Gets the mouse-button payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MouseButton"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1610</c>)
    /// </summary>
    public ImGuiInputEventMouseButton MouseButton { get; } = new();

    /// <summary>
    /// Gets the mouse viewport payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MouseViewport"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1611</c>)
    /// </summary>
    public ImGuiInputEventMouseViewport MouseViewport { get; } = new();

    /// <summary>
    /// Gets the key payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.Key"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1612</c>)
    /// </summary>
    public ImGuiInputEventKey Key { get; } = new();

    /// <summary>
    /// Gets the text payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.Text"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1613</c>)
    /// </summary>
    public ImGuiInputEventText Text { get; } = new();

    /// <summary>
    /// Gets the application-focus payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.Focus"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1614</c>)
    /// </summary>
    public ImGuiInputEventAppFocused AppFocused { get; } = new();

    /// <summary>
    /// Gets or sets whether the event was synthesized by the test engine rather than a backend.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1616</c>)
    /// </summary>
    public bool AddedByTestEngine { get; set; }
}

#endregion

#region Input text

/// <summary>
/// Undo or redo record describing one contiguous text edit
/// (<c>ThirdParty/imgui/imstb_textedit.h:324-331</c>).
/// </summary>
public sealed class StbUndoRecord
{
    /// <summary>
    /// Gets or sets the position of the edit.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:327</c>)
    /// </summary>
    public int Where { get; set; }

    /// <summary>
    /// Gets or sets the number of inserted characters.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:328</c>)
    /// </summary>
    public int InsertLength { get; set; }

    /// <summary>
    /// Gets or sets the number of deleted characters.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:329</c>)
    /// </summary>
    public int DeleteLength { get; set; }

    /// <summary>
    /// Gets or sets the offset into the deleted-character buffer.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:330</c>)
    /// </summary>
    public int CharStorage { get; set; }
}

/// <summary>
/// Undo and redo storage for one text-editing state
/// (<c>ThirdParty/imgui/imstb_textedit.h:333-340</c>).
/// </summary>
public sealed class StbUndoState
{
    /// <summary>
    /// Gets the fixed ring of undo records.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:336</c>)
    /// </summary>
    public StbUndoRecord[] UndoRec { get; } = CreateRecords();

    /// <summary>
    /// Gets the storage for characters removed by edits.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:337</c>)
    /// </summary>
    public byte[] UndoChar { get; } = new byte[999];

    /// <summary>
    /// Gets or sets the current undo record index.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:338</c>)
    /// </summary>
    public short UndoPoint { get; set; }

    /// <summary>
    /// Gets or sets the current redo record index.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:338</c>)
    /// </summary>
    public short RedoPoint { get; set; }

    /// <summary>
    /// Gets or sets the current undo character-buffer index.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:339</c>)
    /// </summary>
    public int UndoCharPoint { get; set; }

    /// <summary>
    /// Gets or sets the current redo character-buffer index.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:339</c>)
    /// </summary>
    public int RedoCharPoint { get; set; }

    /// <summary>
    /// Creates the fixed undo-record array.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:336</c>)
    /// </summary>
    /// <returns>The record array.</returns>
    private static StbUndoRecord[] CreateRecords()
    {
        var records = new StbUndoRecord[99];
        for (int n = 0; n < records.Length; n++)
        {
            records[n] = new StbUndoRecord();
        }

        return records;
    }
}

/// <summary>
/// The text-editing state consumed by the embedded stb_textedit implementation
/// (<c>ThirdParty/imgui/imstb_textedit.h:342-378</c>).
/// </summary>
/// <remarks>
/// The canonical type is configured as <c>STB_TexteditState</c> with an undo count of 99 and an
/// undo-character count of 999 for the <c>ImGuiInputTextState</c> string type.
/// </remarks>
public sealed class ImStbTexteditState
{
    /// <summary>
    /// Gets or sets the cursor position within the string.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:349</c>)
    /// </summary>
    public int Cursor { get; set; }

    /// <summary>
    /// Gets or sets the selection start point.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:352</c>)
    /// </summary>
    public int SelectStart { get; set; }

    /// <summary>
    /// Gets or sets the selection end point; equal endpoints mean no selection.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:353</c>)
    /// </summary>
    public int SelectEnd { get; set; }

    /// <summary>
    /// Gets or sets the per-field insert mode.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:359</c>)
    /// </summary>
    public byte InsertMode { get; set; }

    /// <summary>
    /// Gets or sets the page size in rows; it must be greater than zero for multiline page
    /// movement. (<c>ThirdParty/imgui/imstb_textedit.h:363</c>)
    /// </summary>
    public int RowCountPerPage { get; set; }

    /// <summary>
    /// Gets or sets the cursor-at-end-of-line flag, which is not implemented yet.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:371</c>)
    /// </summary>
    public byte CursorAtEndOfLine { get; set; }

    /// <summary>
    /// Gets or sets whether the state was initialized by
    /// <c>stb_textedit_initialize_state</c>. (<c>ThirdParty/imgui/imstb_textedit.h:372</c>)
    /// </summary>
    public byte Initialized { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="PreferredX"/> holds a valid value.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:373</c>)
    /// </summary>
    public byte HasPreferredX { get; set; }

    /// <summary>
    /// Gets or sets whether the editor runs in single-line mode.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:374</c>)
    /// </summary>
    public byte SingleLine { get; set; }

    /// <summary>
    /// Gets or sets the first alignment padding byte.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:375</c>)
    /// </summary>
    public byte Padding1 { get; set; }

    /// <summary>
    /// Gets or sets the second alignment padding byte.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:375</c>)
    /// </summary>
    public byte Padding2 { get; set; }

    /// <summary>
    /// Gets or sets the third alignment padding byte.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:375</c>)
    /// </summary>
    public byte Padding3 { get; set; }

    /// <summary>
    /// Gets or sets the preferred horizontal seek position for up/down cursor movement.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:376</c>)
    /// </summary>
    public float PreferredX { get; set; }

    /// <summary>
    /// Gets or sets the undo and redo storage.
    /// (<c>ThirdParty/imgui/imstb_textedit.h:377</c>)
    /// </summary>
    public StbUndoState Undostate { get; set; } = new();
}

/// <summary>
/// Persistent editing state of the active <c>InputText()</c> field; it is the
/// <c>IMSTB_TEXTEDIT_STRING</c> type of the embedded text editor
/// (<c>ThirdParty/imgui/imgui_internal.h:1263-1328</c>).
/// </summary>
/// <remarks>
/// Exactly one field may be actively edited at a time, so the context owns a single instance. The
/// text buffers are managed lists of UTF-8 bytes because C# has no zero-terminated mutable
/// <c>char*</c>; the canonical source comparisons against <c>TextA.Data</c> become comparisons
/// against <see cref="TextSrc"/>.
/// </remarks>
public sealed class ImGuiInputTextState
{
    /// <summary>
    /// Gets or sets the parent context. (<c>ThirdParty/imgui/imgui_internal.h:1275</c>)
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the embedded text-editor state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1276</c>)
    /// </summary>
    public ImStbTexteditState Stb { get; set; } = new();

    /// <summary>
    /// Gets or sets a copy of the flags passed to <c>InputText()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1277</c>)
    /// </summary>
    public ImGuiInputTextFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the widget identifier that owns this text state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1278</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the UTF-8 length of <see cref="TextA"/> in bytes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1280</c>)
    /// </summary>
    public int TextLen { get; set; }

    /// <summary>
    /// Gets or sets the source of the text: the main buffer or the read-only user buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1281</c>)
    /// </summary>
    public string? TextSrc { get; set; }

    /// <summary>
    /// Gets the main UTF-8 buffer; its capacity is always at least the user buffer size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1282</c>)
    /// </summary>
    public List<byte> TextA { get; } = new();

    /// <summary>
    /// Gets the value restored when the user presses Escape, backed up when the field is focused.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1283</c>)
    /// </summary>
    public List<byte> TextToRevertTo { get; } = new();

    /// <summary>
    /// Gets the temporary storage used to reconcile callback-driven undo state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1284</c>)
    /// </summary>
    public List<byte> CallbackTextBackup { get; } = new();

    /// <summary>
    /// Gets or sets the end-user buffer capacity, including the zero terminator.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1285</c>)
    /// </summary>
    public int BufCapacity { get; set; }

    /// <summary>
    /// Gets or sets the horizontal manual offset and the vertical scroll amount.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1286</c>)
    /// </summary>
    public ImVec2 Scroll { get; set; }

    /// <summary>
    /// Gets or sets the last line count, used for debug display only.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1287</c>)
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the word-wrapping width.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1288</c>)
    /// </summary>
    public float WrapWidth { get; set; }

    /// <summary>
    /// Gets or sets the cursor blink timer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1289</c>)
    /// </summary>
    public float CursorAnim { get; set; }

    /// <summary>
    /// Gets or sets whether scrolling must follow the cursor.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1290</c>)
    /// </summary>
    public bool CursorFollow { get; set; }

    /// <summary>
    /// Gets or sets whether vertical scrolling is centered on the cursor while a wrapped field is
    /// being resized. (<c>ThirdParty/imgui/imgui_internal.h:1291</c>)
    /// </summary>
    public bool CursorCenterY { get; set; }

    /// <summary>
    /// Gets or sets whether further drags are ignored after a double-click select-all.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1292</c>)
    /// </summary>
    public bool SelectedAllMouseLock { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited since the field was activated.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1293</c>)
    /// </summary>
    public bool EditedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited during the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1294</c>)
    /// </summary>
    public bool EditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets whether the value was validated during the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1354</c>)
    /// </summary>
    public bool ValidatedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets a request to reload the user buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1295</c>)
    /// </summary>
    public bool WantReloadUserBuf { get; set; }

    /// <summary>
    /// Gets or sets the direction of the last left/right cursor move.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1296</c>)
    /// </summary>
    public sbyte LastMoveDirectionLR { get; set; }

    /// <summary>
    /// Gets or sets the pending reload selection start.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1297</c>)
    /// </summary>
    public int ReloadSelectionStart { get; set; }

    /// <summary>
    /// Gets or sets the pending reload selection end.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1298</c>)
    /// </summary>
    public int ReloadSelectionEnd { get; set; }

    /// <summary>
    /// Empties the text buffer and clamps the cursor. Canonical additionally zeroes the first byte of
    /// the buffer. (<c>ThirdParty/imgui/imgui_internal.h:1302</c>)
    /// </summary>
    public void ClearText()
    {
        TextLen = 0;
        if (TextA.Count > 0)
        {
            TextA[0] = 0;
        }

        CursorClamp();
    }

    /// <summary>
    /// Releases the text and revert buffers.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1303</c>)
    /// </summary>
    public void ClearFreeMemory()
    {
        TextA.Clear();
        TextToRevertTo.Clear();
    }

    /// <summary>
    /// Forwards a movement, deletion, undo, or insert-mode key to the embedded editor state machine,
    /// requests cursor following, restarts the cursor blink, and records the horizontal direction of
    /// the move. (<c>ThirdParty/imgui/imgui_widgets.cpp:4327-4337</c>)
    /// </summary>
    /// <param name="key">
    /// One of the <c>ImStbTextedit</c> key codes, optionally combined with
    /// <see cref="ImStbTextedit.KeyShift"/>.
    /// </param>
    /// <remarks>
    /// Canonical declares this member on <c>ImGuiInputTextState</c> so that the embedded editor stays
    /// hidden from the internal header; this port keeps the same split, with the state machine living
    /// in <see cref="ImStbTextedit"/>. The last-direction bookkeeping is translated from canonical's
    /// <c>ImGuiDir_Left</c>/<c>ImGuiDir_Right</c> comparisons to <see cref="ImGuiDir"/> values.
    /// </remarks>
    public void OnKeyPressed(int key)
    {
        ImStbTextedit.Key(this, key);
        CursorFollow = true;
        CursorAnimReset();

        int keyWithoutShift = key & ~ImStbTextedit.KeyShift;
        if (keyWithoutShift == ImStbTextedit.KeyLeft || keyWithoutShift == ImStbTextedit.KeyLineStart ||
            keyWithoutShift == ImStbTextedit.KeyTextStart || keyWithoutShift == ImStbTextedit.KeyBackspace ||
            keyWithoutShift == ImStbTextedit.KeyWordLeft)
        {
            LastMoveDirectionLR = (sbyte)ImGuiDir.Left;
        }
        else if (keyWithoutShift == ImStbTextedit.KeyRight || keyWithoutShift == ImStbTextedit.KeyLineEnd ||
                 keyWithoutShift == ImStbTextedit.KeyTextEnd || keyWithoutShift == ImStbTextedit.KeyDelete ||
                 keyWithoutShift == ImStbTextedit.KeyWordRight)
        {
            LastMoveDirectionLR = (sbyte)ImGuiDir.Right;
        }
    }

    /// <summary>
    /// Encodes one Unicode codepoint as UTF-8, inserts it at the cursor through the embedded editor
    /// state machine, and requests cursor following. (<c>ThirdParty/imgui/imgui_widgets.cpp:4339-4348</c>)
    /// </summary>
    /// <param name="c">The Unicode codepoint to insert.</param>
    /// <remarks>
    /// The canonical member relies on <c>ImTextCharToUtf8</c>, which the port keeps in the widget
    /// layer; the encoding is reproduced inline here to avoid exposing another internal helper.
    /// </remarks>
    public void OnCharPressed(uint c)
    {
        Span<byte> utf8 = stackalloc byte[4];
        int length;
        if (c < 0x80)
        {
            utf8[0] = (byte)c;
            length = 1;
        }
        else if (c < 0x800)
        {
            utf8[0] = (byte)(0xC0 | (c >> 6));
            utf8[1] = (byte)(0x80 | (c & 0x3F));
            length = 2;
        }
        else if (c < 0x10000)
        {
            utf8[0] = (byte)(0xE0 | (c >> 12));
            utf8[1] = (byte)(0x80 | ((c >> 6) & 0x3F));
            utf8[2] = (byte)(0x80 | (c & 0x3F));
            length = 3;
        }
        else
        {
            utf8[0] = (byte)(0xF0 | (c >> 18));
            utf8[1] = (byte)(0x80 | ((c >> 12) & 0x3F));
            utf8[2] = (byte)(0x80 | ((c >> 6) & 0x3F));
            utf8[3] = (byte)(0x80 | (c & 0x3F));
            length = 4;
        }

        ImStbTextedit.Text(this, utf8[..length]);
        CursorFollow = true;
        CursorAnimReset();
    }

    /// <summary>
    /// Returns the horizontal preferred-cursor offset recorded by the last up/down move, or
    /// <c>-1</c> when no such offset is pending. (<c>ThirdParty/imgui/imgui_widgets.cpp:4359</c>)
    /// </summary>
    /// <returns>The preferred x offset, or <c>-1</c>.</returns>
    public float GetPreferredOffsetX()
    {
        return Stb.HasPreferredX != 0 ? Stb.PreferredX : -1.0f;
    }

    /// <summary>
    /// Returns the currently edited text decoded from the UTF-8 buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1307</c>)
    /// </summary>
    /// <returns>The edited text, or an empty string when no buffer is allocated.</returns>
    /// <remarks>
    /// Canonical returns a pointer into <c>TextA</c>; the port has no stable unmanaged buffer, so the
    /// text is decoded into a managed string for diagnostics and debug-node callers.
    /// </remarks>
    public string GetText()
    {
        if (TextA.Count == 0 || TextLen <= 0)
        {
            return string.Empty;
        }

        return System.Text.Encoding.UTF8.GetString(TextA.ToArray(), 0, TextLen);
    }

    /// <summary>
    /// Resets the cursor blink animation so the cursor is immediately visible.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1310</c>)
    /// </summary>
    public void CursorAnimReset()
    {
        CursorAnim = -0.30f;
    }

    /// <summary>
    /// Clamps the cursor and selection to the text length.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1311</c>)
    /// </summary>
    public void CursorClamp()
    {
        if (Stb.Cursor > TextLen)
        {
            Stb.Cursor = TextLen;
        }

        if (Stb.SelectStart > TextLen)
        {
            Stb.SelectStart = TextLen;
        }

        if (Stb.SelectEnd > TextLen)
        {
            Stb.SelectEnd = TextLen;
        }
    }

    /// <summary>
    /// Returns whether a selection is currently active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1312</c>)
    /// </summary>
    /// <returns><see langword="true"/> when the selection endpoints differ.</returns>
    public bool HasSelection()
    {
        return Stb.SelectStart != Stb.SelectEnd;
    }

    /// <summary>
    /// Collapses the selection onto the cursor.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1313</c>)
    /// </summary>
    public void ClearSelection()
    {
        Stb.SelectStart = Stb.Cursor;
        Stb.SelectEnd = Stb.Cursor;
    }

    /// <summary>
    /// Returns the cursor position. (<c>ThirdParty/imgui/imgui_internal.h:1314</c>)
    /// </summary>
    /// <returns>The cursor offset in bytes.</returns>
    public int GetCursorPos()
    {
        return Stb.Cursor;
    }

    /// <summary>
    /// Returns the selection start position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1315</c>)
    /// </summary>
    /// <returns>The selection start offset in bytes.</returns>
    public int GetSelectionStart()
    {
        return Stb.SelectStart;
    }

    /// <summary>
    /// Returns the selection end position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1316</c>)
    /// </summary>
    /// <returns>The selection end offset in bytes.</returns>
    public int GetSelectionEnd()
    {
        return Stb.SelectEnd;
    }

    /// <summary>
    /// Sets the selection endpoints and moves the cursor to the start.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1317</c>)
    /// </summary>
    /// <param name="start">The selection start offset in bytes.</param>
    /// <param name="end">The selection end offset in bytes.</param>
    public void SetSelection(int start, int end)
    {
        Stb.SelectStart = start;
        Stb.Cursor = Stb.SelectEnd = end;
    }

    /// <summary>
    /// Selects the whole text and clears the preferred cursor position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1318</c>)
    /// </summary>
    public void SelectAll()
    {
        Stb.SelectStart = 0;
        Stb.SelectEnd = TextLen;
        Stb.Cursor = TextLen;
        Stb.HasPreferredX = 0;
    }

    /// <summary>
    /// Requests a user-buffer reload that selects everything.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1325</c>)
    /// </summary>
    public void ReloadUserBufAndSelectAll()
    {
        WantReloadUserBuf = true;
        ReloadSelectionStart = 0;
        ReloadSelectionEnd = int.MaxValue;
    }

    /// <summary>
    /// Requests a user-buffer reload that preserves the current selection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1326</c>)
    /// </summary>
    public void ReloadUserBufAndKeepSelection()
    {
        WantReloadUserBuf = true;
        ReloadSelectionStart = Stb.SelectStart;
        ReloadSelectionEnd = Stb.SelectEnd;
    }

    /// <summary>
    /// Requests a user-buffer reload that moves the cursor to the end.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1327</c>)
    /// </summary>
    public void ReloadUserBufAndMoveToEnd()
    {
        WantReloadUserBuf = true;
        ReloadSelectionStart = ReloadSelectionEnd = int.MaxValue;
    }
}

/// <summary>
/// Backup of a text buffer that just deactivated, kept so the deactivation remains observable for a
/// frame (<c>ThirdParty/imgui/imgui_internal.h:1248-1258</c>).
/// </summary>
public sealed class ImGuiInputTextDeactivatedState
{
    /// <summary>
    /// Gets or sets the identifier of the widget that owned the text state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1252</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the frame count at which the deactivation elapsed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1253</c>)
    /// </summary>
    public int ElapseFrame { get; set; }

    /// <summary>
    /// Gets the backed-up text buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1254</c>)
    /// </summary>
    public List<byte> TextA { get; } = new();

    /// <summary>
    /// Clears the identifier, frame, and text buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1257</c>)
    /// </summary>
    public void ClearFreeMemory()
    {
        ID = 0;
        ElapseFrame = 0;
        TextA.Clear();
    }
}

/// <summary>
/// Shared state supplied to an <see cref="ImGuiInputTextCallback"/> while a text field is being
/// processed (<c>ThirdParty/imgui/imgui.h:2787-2820</c>).
/// </summary>
/// <remarks>
/// <para>
/// The core owns the instance and reuses it for every callback event of the active field. The widget
/// layer fills <see cref="Buf"/>, <see cref="BufTextLen"/>, and <see cref="BufSize"/> from the active
/// field's storage before invoking the callback and copies the buffer back when the callback marks it
/// dirty, so <see cref="DeleteChars"/> and <see cref="InsertChars"/> edit a private copy that the
/// widget layer reconciles after the callback returns.
/// </para>
/// <para>
/// Canonical keeps <c>Buf</c> aliased to the active field's internal <c>TextA</c> buffer; this port
/// copies it instead. The observable contract of the two editing methods is unchanged: they update
/// the text, the cursor, the selection, the text length, and the dirty flag so the widget layer
/// applies the edit.
/// </para>
/// </remarks>
public sealed class ImGuiInputTextCallbackData
{
    /// <summary>
    /// Gets or sets the parent context.
    /// (<c>ThirdParty/imgui/imgui.h:2789</c>)
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the single <c>ImGuiInputTextFlags_Callback*</c> value that triggered the
    /// callback. (<c>ThirdParty/imgui/imgui.h:2790</c>)
    /// </summary>
    public ImGuiInputTextFlags EventFlag { get; set; }

    /// <summary>
    /// Gets or sets the flags originally passed to <c>InputText()</c>.
    /// (<c>ThirdParty/imgui/imgui.h:2791</c>)
    /// </summary>
    public ImGuiInputTextFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the opaque pointer originally passed to <c>InputText()</c>.
    /// (<c>ThirdParty/imgui/imgui.h:2792</c>)
    /// </summary>
    public IntPtr UserData { get; set; }

    /// <summary>
    /// Gets or sets the widget identifier.
    /// (<c>ThirdParty/imgui/imgui.h:2793</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the key that triggered a completion or history callback.
    /// (<c>ThirdParty/imgui/imgui.h:2800</c>)
    /// </summary>
    public ImGuiKey EventKey { get; set; }

    /// <summary>
    /// Gets or sets the character supplied to a character-filter callback.
    /// (<c>ThirdParty/imgui/imgui.h:2801</c>)
    /// </summary>
    public uint EventChar { get; set; }

    /// <summary>
    /// Gets or sets whether the field was just activated.
    /// (<c>ThirdParty/imgui/imgui.h:2802</c>)
    /// </summary>
    public bool EventActivated { get; set; }

    /// <summary>
    /// Gets or sets whether the callback modified the text buffer and length.
    /// (<c>ThirdParty/imgui/imgui.h:2803</c>)
    /// </summary>
    public bool BufDirty { get; set; }

    /// <summary>
    /// Gets or sets the text buffer. Canonical exposes a mutable <c>char*</c> that a resize callback
    /// may replace; this port exposes the managed byte array.
    /// (<c>ThirdParty/imgui/imgui.h:2804</c>)
    /// </summary>
    public byte[]? Buf { get; set; }

    /// <summary>
    /// Gets or sets the text length in bytes, excluding the terminator.
    /// (<c>ThirdParty/imgui/imgui.h:2805</c>)
    /// </summary>
    public int BufTextLen { get; set; }

    /// <summary>
    /// Gets or sets the buffer size in bytes, including the terminator.
    /// (<c>ThirdParty/imgui/imgui.h:2806</c>)
    /// </summary>
    public int BufSize { get; set; }

    /// <summary>
    /// Gets or sets the cursor position.
    /// (<c>ThirdParty/imgui/imgui.h:2807</c>)
    /// </summary>
    public int CursorPos { get; set; }

    /// <summary>
    /// Gets or sets the selection start position.
    /// (<c>ThirdParty/imgui/imgui.h:2808</c>)
    /// </summary>
    public int SelectionStart { get; set; }

    /// <summary>
    /// Gets or sets the selection end position.
    /// (<c>ThirdParty/imgui/imgui.h:2809</c>)
    /// </summary>
    public int SelectionEnd { get; set; }

    /// <summary>
    /// Selects the whole buffer.
    /// (<c>ThirdParty/imgui/imgui.h:2816</c>)
    /// </summary>
    public void SelectAll()
    {
        SelectionStart = 0;
        CursorPos = BufTextLen;
        SelectionEnd = BufTextLen;
    }

    /// <summary>
    /// Sets the selection endpoints and moves the cursor to the start.
    /// (<c>ThirdParty/imgui/imgui.h:2817</c>)
    /// </summary>
    /// <param name="start">The selection start offset in bytes.</param>
    /// <param name="end">The selection end offset in bytes.</param>
    public void SetSelection(int start, int end)
    {
        SelectionStart = start;
        SelectionEnd = end;
        CursorPos = start;
    }

    /// <summary>
    /// Collapses the selection to the end of the buffer.
    /// (<c>ThirdParty/imgui/imgui.h:2818</c>)
    /// </summary>
    public void ClearSelection()
    {
        SelectionStart = BufTextLen;
        SelectionEnd = BufTextLen;
    }

    /// <summary>
    /// Returns whether a selection is currently active.
    /// (<c>ThirdParty/imgui/imgui.h:2819</c>)
    /// </summary>
    /// <returns><see langword="true"/> when the selection endpoints differ.</returns>
    public bool HasSelection()
    {
        return SelectionStart != SelectionEnd;
    }

    /// <summary>
    /// Deletes a byte range from the callback buffer. Mirrors canonical
    /// <c>ImGuiInputTextCallbackData::DeleteChars</c> (<c>ThirdParty/imgui/imgui_widgets.cpp:4374-4388</c>).
    /// </summary>
    /// <param name="pos">Byte offset of the first byte to delete.</param>
    /// <param name="bytesCount">Number of bytes to delete, including the whole trailing terminator
    /// when the deleted range reaches the end of the text.</param>
    /// <remarks>
    /// The remaining bytes, including the terminating zero, are moved down over the deleted range. The
    /// cursor is shifted by the deleted count when it lies past the range and clamped to the range
    /// start when it lies inside it, the selection is collapsed onto the cursor, and the dirty flag is
    /// set so the widget layer copies the buffer back into the active field.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no callback buffer is attached.</exception>
    public void DeleteChars(int pos, int bytesCount)
    {
        byte[] buffer = Buf ?? throw new InvalidOperationException("DeleteChars() requires a callback buffer.");
        ImAssert.Check(pos >= 0 && bytesCount >= 0 && pos + bytesCount <= BufTextLen,
            "DeleteChars() requires a range inside the buffer text.");

        // Move the tail, including the terminating zero, down over the deleted range.
        Array.Copy(buffer, pos + bytesCount, buffer, pos, BufTextLen - bytesCount - pos + 1);

        if (CursorPos >= pos + bytesCount)
            CursorPos -= bytesCount;
        else if (CursorPos >= pos)
            CursorPos = pos;
        SelectionStart = SelectionEnd = CursorPos;
        BufDirty = true;
        BufTextLen -= bytesCount;
    }

    /// <summary>
    /// Inserts UTF-8 bytes into the callback buffer at a byte offset. Mirrors canonical
    /// <c>ImGuiInputTextCallbackData::InsertChars</c> (<c>ThirdParty/imgui/imgui_widgets.cpp:4390-4432</c>).
    /// </summary>
    /// <param name="pos">Byte offset at which the new text is inserted.</param>
    /// <param name="newText">UTF-8 bytes to insert; an empty span is ignored.</param>
    /// <remarks>
    /// <para>
    /// When the flag <see cref="ImGuiInputTextFlags.CallbackResize"/> is not set, an insertion longer
    /// than the available capacity is truncated at the nearest UTF-8 codepoint boundary. When the flag
    /// is set, the callback buffer is replaced by a larger array so the whole insertion fits; the
    /// resized buffer is reported through <see cref="Buf"/>, <see cref="BufSize"/>, and the dirty flag,
    /// which is how the widget layer learns to grow the active field's storage.
    /// </para>
    /// <para>
    /// Canonical asserts that the callback buffer aliases the active field's internal buffer before
    /// growing it. This port gives the callback a private copy, so the assert is not applicable; the
    /// post-callback reconciliation performs the equivalent growth.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when no callback buffer is attached.</exception>
    public void InsertChars(int pos, ReadOnlySpan<byte> newText)
    {
        if (newText.Length == 0)
            return;
        byte[] buffer = Buf ?? throw new InvalidOperationException("InsertChars() requires a callback buffer.");

        bool isResizable = (Flags & ImGuiInputTextFlags.CallbackResize) != 0;
        bool isReadonly = (Flags & ImGuiInputTextFlags.ReadOnly) != 0;

        int newTextLength = newText.Length;
        int available = BufSize - 1 - BufTextLen;
        if (!isResizable && newTextLength > available)
            newTextLength = ImGui.ImTextFindValidUtf8CodepointEnd(newText, newTextLength, available);
        if (newTextLength == 0)
            return;

        // Grow the callback's buffer when the resize contract is enabled. Canonical grows the active
        // field's TextA storage here; the port replaces its copy and lets the widget layer reconcile.
        if (newTextLength + BufTextLen + 1 > BufSize && isResizable && !isReadonly)
        {
            int newBufSize = BufTextLen + Math.Clamp(newTextLength * 4, 32, Math.Max(256, newTextLength)) + 1;
            byte[] grown = new byte[newBufSize + 1];
            Array.Copy(buffer, grown, BufTextLen + 1);
            Buf = grown;
            BufSize = newBufSize;
            buffer = grown;
        }

        // Shift the tail up to make room, copy the insertion, and re-terminate the text.
        if (BufTextLen != pos)
            Array.Copy(buffer, pos, buffer, pos + newTextLength, BufTextLen - pos);
        newText[..newTextLength].CopyTo(buffer.AsSpan(pos));
        buffer[BufTextLen + newTextLength] = 0;

        BufDirty = true;
        BufTextLen += newTextLength;
        if (CursorPos >= pos)
            CursorPos += newTextLength;
        CursorPos = Math.Clamp(CursorPos, 0, BufTextLen);
        SelectionStart = SelectionEnd = CursorPos;
    }
}

#endregion

#region Viewports and platform support

/// <summary>
/// A platform window that the core renders into. One instance exists by default; multi-viewport
/// configurations create one per platform window
/// (<c>ThirdParty/imgui/imgui.h:4164-4211</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical coordinates are the same as native desktop coordinates, so a main viewport position of
/// <c>(0,0)</c> is the upper-left corner of the primary display when multi-viewport support is
/// enabled.
/// </para>
/// <para>
/// The user data and handle members are storage for the backend; the core never dereferences them.
/// The core creates and owns viewport instances and exposes them to the backend during platform
/// window update.
/// </para>
/// </remarks>
public class ImGuiViewport
{
    /// <summary>
    /// Returns a printable name for this viewport, used by the debug tools.
    /// (<c>ThirdParty/imgui/imgui.cpp:16838-16842</c>, declared <c>ThirdParty/imgui/imgui.h:4199</c>)
    /// </summary>
    /// <returns>The owning window's name, or a placeholder when there is none.</returns>
    /// <remarks>
    /// Canonical declares this on the public viewport type and casts to the internal representation to reach
    /// the owning window. The managed equivalent is a virtual member: every real viewport is an
    /// <see cref="ImGuiViewportP"/>, which overrides this, and the base returns the same placeholder for a
    /// viewport type the port does not otherwise produce.
    /// </remarks>
    public virtual string GetDebugName() => "n/a";

    /// <summary>
    /// Gets or sets the unique viewport identifier.
    /// (<c>ThirdParty/imgui/imgui.h:4166</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the viewport behavior flags.
    /// (<c>ThirdParty/imgui/imgui.h:4167</c>)
    /// </summary>
    public ImGuiViewportFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the main-area position in desktop coordinates.
    /// (<c>ThirdParty/imgui/imgui.h:4168</c>)
    /// </summary>
    public ImVec2 Pos { get; set; }

    /// <summary>
    /// Gets or sets the main-area size.
    /// (<c>ThirdParty/imgui/imgui.h:4169</c>)
    /// </summary>
    public ImVec2 Size { get; set; }

    /// <summary>
    /// Gets or sets the density ratio between display and framebuffer pixels, which affects font
    /// rasterization. (<c>ThirdParty/imgui/imgui.h:4170</c>)
    /// </summary>
    public ImVec2 FramebufferScale { get; set; }

    /// <summary>
    /// Gets or sets the work-area position, which is the main position minus task bars and similar
    /// decorations. (<c>ThirdParty/imgui/imgui.h:4171</c>)
    /// </summary>
    public ImVec2 WorkPos { get; set; }

    /// <summary>
    /// Gets or sets the work-area size.
    /// (<c>ThirdParty/imgui/imgui.h:4172</c>)
    /// </summary>
    public ImVec2 WorkSize { get; set; }

    /// <summary>
    /// Gets or sets the DPI scale, where 1.0 corresponds to 96 DPI.
    /// (<c>ThirdParty/imgui/imgui.h:4173</c>)
    /// </summary>
    public float DpiScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the parent viewport identifier, or zero when there is no parent.
    /// (<c>ThirdParty/imgui/imgui.h:4174</c>)
    /// </summary>
    public uint ParentViewportId { get; set; }

    /// <summary>
    /// Gets or sets the direct reference to the parent viewport, or <see langword="null"/>.
    /// (<c>ThirdParty/imgui/imgui.h:4175</c>)
    /// </summary>
    public ImGuiViewport? ParentViewport { get; set; }

    /// <summary>
    /// Gets or sets the draw data produced for this viewport; valid after render and until the next
    /// frame begins. (<c>ThirdParty/imgui/imgui.h:4176</c>)
    /// </summary>
    public ImDrawData? DrawData { get; set; }

    /// <summary>
    /// Gets or sets renderer-owned opaque data.
    /// (<c>ThirdParty/imgui/imgui.h:4183</c>)
    /// </summary>
    public IntPtr RendererUserData { get; set; }

    /// <summary>
    /// Gets or sets platform-owned opaque data.
    /// (<c>ThirdParty/imgui/imgui.h:4184</c>)
    /// </summary>
    public IntPtr PlatformUserData { get; set; }

    /// <summary>
    /// Gets or sets platform icon data.
    /// (<c>ThirdParty/imgui/imgui.h:4185</c>)
    /// </summary>
    public IntPtr PlatformIconData { get; set; }

    /// <summary>
    /// Gets or sets the high-level platform window handle.
    /// (<c>ThirdParty/imgui/imgui.h:4186</c>)
    /// </summary>
    public IntPtr PlatformHandle { get; set; }

    /// <summary>
    /// Gets or sets the low-level platform-native window handle.
    /// (<c>ThirdParty/imgui/imgui.h:4187</c>)
    /// </summary>
    public IntPtr PlatformHandleRaw { get; set; }

    /// <summary>
    /// Gets or sets whether the platform window has been created.
    /// (<c>ThirdParty/imgui/imgui.h:4188</c>)
    /// </summary>
    public bool PlatformWindowCreated { get; set; }

    /// <summary>
    /// Gets or sets whether the platform requested a move.
    /// (<c>ThirdParty/imgui/imgui.h:4189</c>)
    /// </summary>
    public bool PlatformRequestMove { get; set; }

    /// <summary>
    /// Gets or sets whether the platform requested a resize.
    /// (<c>ThirdParty/imgui/imgui.h:4190</c>)
    /// </summary>
    public bool PlatformRequestResize { get; set; }

    /// <summary>
    /// Gets or sets whether the platform requested a close.
    /// (<c>ThirdParty/imgui/imgui.h:4191</c>)
    /// </summary>
    public bool PlatformRequestClose { get; set; }

    /// <summary>
    /// Returns the center of the main area.
    /// (<c>ThirdParty/imgui/imgui.h:4197</c>)
    /// </summary>
    /// <returns>The center point of <see cref="Pos"/> and <see cref="Size"/>.</returns>
    public ImVec2 GetCenter()
    {
        return new ImVec2(Pos.X + (Size.X * 0.5f), Pos.Y + (Size.Y * 0.5f));
    }

    /// <summary>
    /// Returns the center of the work area.
    /// (<c>ThirdParty/imgui/imgui.h:4198</c>)
    /// </summary>
    /// <returns>The center point of <see cref="WorkPos"/> and <see cref="WorkSize"/>.</returns>
    public ImVec2 GetWorkCenter()
    {
        return new ImVec2(WorkPos.X + (WorkSize.X * 0.5f), WorkPos.Y + (WorkSize.Y * 0.5f));
    }

    /// <summary>
    /// Returns the main bounding rectangle of the viewport.
    /// (<c>ThirdParty/imgui/imgui.h:4195</c>)
    /// </summary>
    /// <returns>The viewport rectangle encompassing <see cref="Pos"/> and <see cref="Size"/>.</returns>
    public ImRect GetMainRect()
    {
        return new ImRect(Pos, Pos + Size);
    }

    /// <summary>
    /// Returns the work area bounding rectangle of the viewport.
    /// (<c>ThirdParty/imgui/imgui.h:4196</c>)
    /// </summary>
    /// <returns>The viewport work area rectangle encompassing <see cref="WorkPos"/> and <see cref="WorkSize"/>.</returns>
    public ImRect GetWorkRect()
    {
        return new ImRect(WorkPos, WorkPos + WorkSize);
    }
}

/// <summary>
/// Internal extension of <see cref="ImGuiViewport"/> that the core actually allocates: every
/// canonical viewport instance is a viewport-plus object
/// (<c>ThirdParty/imgui/imgui_internal.h:2166-2213</c>).
/// </summary>
/// <remarks>
/// The extra members track z-order estimation, the background and foreground draw lists, per-frame
/// platform feedback, and the work-area insets used to avoid OS decorations.
/// </remarks>
public sealed class ImGuiViewportP : ImGuiViewport
{
    /// <summary>
    /// Returns a printable name for this viewport, used by the debug tools.
    /// (<c>ThirdParty/imgui/imgui.cpp:16838-16842</c>)
    /// </summary>
    /// <returns>The owning window's name, or <c>"n/a"</c> when this viewport has no window yet.</returns>
    public override string GetDebugName() => Window != null ? Window.Name : "n/a";

    /// <summary>
    /// Gets or sets the window that owns this viewport, when it is not allowed to host other
    /// windows. (<c>ThirdParty/imgui/imgui_internal.h:2169</c>)
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the viewport list index.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2170</c>)
    /// </summary>
    public int Idx { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last frame on which a window activated this viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2171</c>)
    /// </summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last focus stamp observed for a window hosted by this viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2172</c>)
    /// </summary>
    public int LastFocusedStampCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the hash of the last window name hosted by this viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2173</c>)
    /// </summary>
    public uint LastNameHash { get; set; }

    /// <summary>
    /// Gets or sets the last platform position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2174</c>)
    /// </summary>
    public ImVec2 LastPos { get; set; }

    /// <summary>
    /// Gets or sets the last platform size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2175</c>)
    /// </summary>
    public ImVec2 LastSize { get; set; }

    /// <summary>
    /// Gets or sets the window opacity applied while dragging.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2176</c>)
    /// </summary>
    public float Alpha { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the previously applied opacity.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2177</c>)
    /// </summary>
    public float LastAlpha { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets whether the navigation window was non-null the last time this viewport was
    /// focused. (<c>ThirdParty/imgui/imgui_internal.h:2178</c>)
    /// </summary>
    public bool LastFocusedHadNavWindow { get; set; }

    /// <summary>
    /// Gets or sets the index of the platform monitor that contains this viewport, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2179</c>)
    /// </summary>
    public short PlatformMonitor { get; set; } = -1;

    /// <summary>
    /// Gets the last frames on which the background and foreground draw lists were used.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2180</c>)
    /// </summary>
    public float[] BgFgDrawListsLastTimeActive { get; } = new float[] { -1.0f, -1.0f };

    /// <summary>
    /// Gets the convenience background and foreground draw lists.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2181</c>)
    /// </summary>
    public ImDrawList?[] BgFgDrawLists { get; } = new ImDrawList?[2];

    /// <summary>
    /// Gets or sets the final draw data built for this viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2182</c>)
    /// </summary>
    public ImDrawData DrawDataP { get; set; } = new();

    /// <summary>
    /// Gets or sets the temporary builder used while assembling the final draw data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2183</c>)
    /// </summary>
    public ImDrawDataBuilder DrawDataBuilder { get; set; } = new();

    /// <summary>
    /// Gets or sets the last position reported by the platform.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2184</c>)
    /// </summary>
    public ImVec2 LastPlatformPos { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the last size reported by the platform.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2185</c>)
    /// </summary>
    public ImVec2 LastPlatformSize { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the last size reported by the renderer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2186</c>)
    /// </summary>
    public ImVec2 LastRendererSize { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the work-area minimum inset locked for the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2192</c>)
    /// </summary>
    public ImVec2 WorkInsetMin { get; set; }

    /// <summary>
    /// Gets or sets the work-area maximum inset locked for the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2193</c>)
    /// </summary>
    public ImVec2 WorkInsetMax { get; set; }

    /// <summary>
    /// Gets or sets the work-area minimum inset accumulated for the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2194</c>)
    /// </summary>
    public ImVec2 BuildWorkInsetMin { get; set; }

    /// <summary>
    /// Gets or sets the work-area maximum inset accumulated for the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2195</c>)
    /// </summary>
    public ImVec2 BuildWorkInsetMax { get; set; }

    /// <summary>
    /// Clears the three platform request flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2201</c>)
    /// </summary>
    public void ClearRequestFlags()
    {
        PlatformRequestClose = false;
        PlatformRequestMove = false;
        PlatformRequestResize = false;
    }

    /// <summary>
    /// Recomputes the public work position and size from the locked insets.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2206</c>)
    /// </summary>
    public void UpdateWorkRect()
    {
        WorkPos = CalcWorkRectPos(WorkInsetMin);
        WorkSize = CalcWorkRectSize(WorkInsetMin, WorkInsetMax);
    }

    /// <summary>
    /// Computes a work-area position from an inset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2204</c>)
    /// </summary>
    /// <param name="insetMin">The minimum inset.</param>
    /// <returns>The inset-adjusted position.</returns>
    public ImVec2 CalcWorkRectPos(ImVec2 insetMin)
    {
        return new ImVec2(Pos.X + insetMin.X, Pos.Y + insetMin.Y);
    }

    /// <summary>
    /// Computes a work-area size from a pair of insets, clamping each axis at zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2205</c>)
    /// </summary>
    /// <param name="insetMin">The minimum inset.</param>
    /// <param name="insetMax">The maximum inset.</param>
    /// <returns>The inset-adjusted size.</returns>
    public ImVec2 CalcWorkRectSize(ImVec2 insetMin, ImVec2 insetMax)
    {
        return new ImVec2(
            MathF.Max(0.0f, Size.X - insetMin.X - insetMax.X),
            MathF.Max(0.0f, Size.Y - insetMin.Y - insetMax.Y));
    }

    /// <summary>
    /// Returns the main-area rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2209</c>)
    /// </summary>
    /// <returns>The main-area rectangle.</returns>
    public new ImRect GetMainRect()
    {
        return new ImRect(Pos.X, Pos.Y, Pos.X + Size.X, Pos.Y + Size.Y);
    }

    /// <summary>
    /// Returns the locked work-area rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2210</c>)
    /// </summary>
    /// <returns>The work-area rectangle.</returns>
    public new ImRect GetWorkRect()
    {
        return new ImRect(WorkPos.X, WorkPos.Y, WorkPos.X + WorkSize.X, WorkPos.Y + WorkSize.Y);
    }

    /// <summary>
    /// Returns the work-area rectangle currently being accumulated for the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2211</c>)
    /// </summary>
    /// <returns>The building work-area rectangle.</returns>
    public ImRect GetBuildWorkRect()
    {
        ImVec2 pos = CalcWorkRectPos(BuildWorkInsetMin);
        ImVec2 size = CalcWorkRectSize(BuildWorkInsetMin, BuildWorkInsetMax);
        return new ImRect(pos.X, pos.Y, pos.X + size.X, pos.Y + size.Y);
    }
}

/// <summary>
/// User-provided bounds and density for one connected monitor, used to keep popups and tooltips from
/// straddling displays (<c>ThirdParty/imgui/imgui.h:4375-4383</c>).
/// </summary>
public sealed class ImGuiPlatformMonitor
{
    /// <summary>
    /// Gets or sets the upper-left corner of the displayed area.
    /// (<c>ThirdParty/imgui/imgui.h:4377</c>)
    /// </summary>
    public ImVec2 MainPos { get; set; }

    /// <summary>
    /// Gets or sets the size of the displayed area.
    /// (<c>ThirdParty/imgui/imgui.h:4377</c>)
    /// </summary>
    public ImVec2 MainSize { get; set; }

    /// <summary>
    /// Gets or sets the upper-left corner of the work area, excluding task and menu bars.
    /// (<c>ThirdParty/imgui/imgui.h:4378</c>)
    /// </summary>
    public ImVec2 WorkPos { get; set; }

    /// <summary>
    /// Gets or sets the size of the work area.
    /// (<c>ThirdParty/imgui/imgui.h:4378</c>)
    /// </summary>
    public ImVec2 WorkSize { get; set; }

    /// <summary>
    /// Gets or sets the DPI scale, where 1.0 corresponds to 96 DPI.
    /// (<c>ThirdParty/imgui/imgui.h:4379</c>)
    /// </summary>
    public float DpiScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets backend-dependent monitor data.
    /// (<c>ThirdParty/imgui/imgui.h:4380</c>)
    /// </summary>
    public IntPtr PlatformHandle { get; set; }
}

/// <summary>
/// Input-method-editor state published to the platform backend so it can position the IME window
/// (<c>ThirdParty/imgui/imgui.h:4385-4394</c>).
/// </summary>
public sealed class ImGuiPlatformImeData
{
    /// <summary>
    /// Gets or sets whether a widget wants the IME to be visible.
    /// (<c>ThirdParty/imgui/imgui.h:4388</c>)
    /// </summary>
    public bool WantVisible { get; set; }

    /// <summary>
    /// Gets or sets whether a widget wants text input; this mirrors the upcoming value of
    /// <c>io.WantTextInput</c>. (<c>ThirdParty/imgui/imgui.h:4389</c>)
    /// </summary>
    public bool WantTextInput { get; set; }

    /// <summary>
    /// Gets or sets the input cursor position.
    /// (<c>ThirdParty/imgui/imgui.h:4390</c>)
    /// </summary>
    public ImVec2 InputPos { get; set; }

    /// <summary>
    /// Gets or sets the input line height.
    /// (<c>ThirdParty/imgui/imgui.h:4391</c>)
    /// </summary>
    public float InputLineHeight { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the viewport that owns the IME focus.
    /// (<c>ThirdParty/imgui/imgui.h:4392</c>)
    /// </summary>
    public uint ViewportId { get; set; }
}

/// <summary>
/// The platform and renderer interface. A backend installs the callbacks it supports; the core binds
/// the data lists and invokes the callbacks at the canonical points in the frame lifecycle
/// (<c>ThirdParty/imgui/imgui.h:4253-4371</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical stores raw function pointers; this port stores managed delegates with the same
/// parameters and return values. A <see langword="null"/> delegate means the backend did not install
/// that handler, which every call site tests before invoking, exactly as canonical tests its
/// pointers.
/// </para>
/// <para>
/// The data lists are the core's output to the backend: <see cref="Textures"/> and
/// <see cref="Viewports"/> are rebuilt each frame, while <see cref="Monitors"/> is owned and updated
/// by the application or platform backend.
/// </para>
/// <para>
/// Obtain the instance for the current context through <see cref="ImGui.GetPlatformIO()"/>; it is
/// owned by the context and must not be replaced.
/// </para>
/// </remarks>
public sealed class ImGuiPlatformIO
{
    #region Clipboard, shell, and IME handlers

    /// <summary>
    /// Gets or sets the handler that reads the OS clipboard, or <see langword="null"/> when the host
    /// has none. Returns <see langword="null"/> when the clipboard does not hold text.
    /// (<c>ThirdParty/imgui/imgui.h:4260</c>)
    /// </summary>
    public Func<ImGuiContext, string?>? Platform_GetClipboardTextFn { get; set; }

    /// <summary>
    /// Gets or sets the handler that writes the OS clipboard, or <see langword="null"/> when the host
    /// has none. (<c>ThirdParty/imgui/imgui.h:4261</c>)
    /// </summary>
    public Action<ImGuiContext, string>? Platform_SetClipboardTextFn { get; set; }

    /// <summary>
    /// Gets or sets opaque data forwarded to the clipboard handlers.
    /// (<c>ThirdParty/imgui/imgui.h:4262</c>)
    /// </summary>
    public IntPtr Platform_ClipboardUserData { get; set; }

    /// <summary>
    /// Gets or sets the handler that opens a link, folder, or file in the OS shell; it returns
    /// <see langword="false"/> on failure. (<c>ThirdParty/imgui/imgui.h:4266</c>)
    /// </summary>
    public Func<ImGuiContext, string, bool>? Platform_OpenInShellFn { get; set; }

    /// <summary>
    /// Gets or sets opaque data forwarded to the shell handler.
    /// (<c>ThirdParty/imgui/imgui.h:4267</c>)
    /// </summary>
    public IntPtr Platform_OpenInShellUserData { get; set; }

    /// <summary>
    /// Gets or sets the handler that notifies the OS input method editor of the text-cursor position.
    /// (<c>ThirdParty/imgui/imgui.h:4274</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiViewport, ImGuiPlatformImeData>? Platform_SetImeDataFn { get; set; }

    /// <summary>
    /// Gets or sets opaque data forwarded to the IME handler.
    /// (<c>ThirdParty/imgui/imgui.h:4275</c>)
    /// </summary>
    public IntPtr Platform_ImeUserData { get; set; }

    /// <summary>
    /// Gets or sets the locale decimal point character, for example <c>'.'</c> or <c>','</c>.
    /// (<c>ThirdParty/imgui/imgui.h:4280</c>)
    /// </summary>
    public ushort Platform_LocaleDecimalPoint { get; set; } = (ushort)'.';

    /// <summary>
    /// Gets or sets the session start date packed as <c>YYYYMMDD</c>, for example <c>20261231</c>.
    /// (<c>ThirdParty/imgui/imgui.h:4284</c>)
    /// </summary>
    public int Platform_SessionDate { get; set; }

    #endregion

    #region Renderer resource handlers

    /// <summary>
    /// Gets or sets the maximum texture width the renderer supports, or zero when unknown.
    /// (<c>ThirdParty/imgui/imgui.h:4292</c>)
    /// </summary>
    public int Renderer_TextureMaxWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum texture height the renderer supports, or zero when unknown.
    /// (<c>ThirdParty/imgui/imgui.h:4293</c>)
    /// </summary>
    public int Renderer_TextureMaxHeight { get; set; }

    /// <summary>
    /// Gets or sets the renderer-owned opaque state pointer.
    /// (<c>ThirdParty/imgui/imgui.h:4296</c>)
    /// </summary>
    public IntPtr Renderer_RenderState { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests a reset of the graphics render state.
    /// (<c>ThirdParty/imgui/imgui.h:4299</c>)
    /// </summary>
    public ImDrawCallback? DrawCallback_ResetRenderState { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests linear texture sampling.
    /// (<c>ThirdParty/imgui/imgui.h:4300</c>)
    /// </summary>
    public ImDrawCallback? DrawCallback_SetSamplerLinear { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests nearest-neighbour texture sampling.
    /// (<c>ThirdParty/imgui/imgui.h:4301</c>)
    /// </summary>
    public ImDrawCallback? DrawCallback_SetSamplerNearest { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests the backend use the sampler already associated to a texture.
    /// (<c>ThirdParty/imgui/imgui.h:4394</c>)
    /// </summary>
    public ImDrawCallback? DrawCallback_SetSamplerFromTex { get; set; }

    #endregion

    #region Platform window handlers

    /// <summary>
    /// Gets or sets the handler that creates a platform window for a viewport.
    /// (<c>ThirdParty/imgui/imgui.h:4317</c>)
    /// </summary>
    public Action<ImGuiViewport>? Platform_CreateWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that destroys a viewport's platform window.
    /// (<c>ThirdParty/imgui/imgui.h:4318</c>)
    /// </summary>
    public Action<ImGuiViewport>? Platform_DestroyWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that shows a newly created platform window.
    /// (<c>ThirdParty/imgui/imgui.h:4319</c>)
    /// </summary>
    public Action<ImGuiViewport>? Platform_ShowWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that positions a platform window's client area.
    /// (<c>ThirdParty/imgui/imgui.h:4320</c>)
    /// </summary>
    public Action<ImGuiViewport, ImVec2>? Platform_SetWindowPos { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a platform window's client-area position.
    /// (<c>ThirdParty/imgui/imgui.h:4321</c>)
    /// </summary>
    public Func<ImGuiViewport, ImVec2>? Platform_GetWindowPos { get; set; }

    /// <summary>
    /// Gets or sets the handler that sizes a platform window's client area.
    /// (<c>ThirdParty/imgui/imgui.h:4322</c>)
    /// </summary>
    public Action<ImGuiViewport, ImVec2>? Platform_SetWindowSize { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a platform window's client-area size.
    /// (<c>ThirdParty/imgui/imgui.h:4323</c>)
    /// </summary>
    public Func<ImGuiViewport, ImVec2>? Platform_GetWindowSize { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a viewport's framebuffer density; both components must be
    /// integers. (<c>ThirdParty/imgui/imgui.h:4324</c>)
    /// </summary>
    public Func<ImGuiViewport, ImVec2>? Platform_GetWindowFramebufferScale { get; set; }

    /// <summary>
    /// Gets or sets the handler that moves a platform window to the front and focuses it.
    /// (<c>ThirdParty/imgui/imgui.h:4325</c>)
    /// </summary>
    public Action<ImGuiViewport>? Platform_SetWindowFocus { get; set; }

    /// <summary>
    /// Gets or sets the handler that reports whether a platform window is focused.
    /// (<c>ThirdParty/imgui/imgui.h:4326</c>)
    /// </summary>
    public Func<ImGuiViewport, bool>? Platform_GetWindowFocus { get; set; }

    /// <summary>
    /// Gets or sets the handler that reports whether a platform window is minimized.
    /// (<c>ThirdParty/imgui/imgui.h:4327</c>)
    /// </summary>
    public Func<ImGuiViewport, bool>? Platform_GetWindowMinimized { get; set; }

    /// <summary>
    /// Gets or sets the handler that sets a platform window's title.
    /// (<c>ThirdParty/imgui/imgui.h:4328</c>)
    /// </summary>
    public Action<ImGuiViewport, string>? Platform_SetWindowTitle { get; set; }

    /// <summary>
    /// Gets or sets the handler that sets a platform window's global transparency.
    /// (<c>ThirdParty/imgui/imgui.h:4329</c>)
    /// </summary>
    public Action<ImGuiViewport, float>? Platform_SetWindowAlpha { get; set; }

    /// <summary>
    /// Gets or sets the optional general-purpose per-frame platform bookkeeping hook.
    /// (<c>ThirdParty/imgui/imgui.h:4330</c>)
    /// </summary>
    public Action<ImGuiViewport>? Platform_UpdateWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that performs platform-side rendering for a viewport, receiving the
    /// argument passed to <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// (<c>ThirdParty/imgui/imgui.h:4331</c>)
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Platform_RenderWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that presents a viewport's platform-side buffers, receiving the
    /// argument passed to <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// (<c>ThirdParty/imgui/imgui.h:4332</c>)
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Platform_SwapBuffers { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a viewport's DPI scale, where 1.0 is 96 DPI.
    /// (<c>ThirdParty/imgui/imgui.h:4333</c>)
    /// </summary>
    public Func<ImGuiViewport, float>? Platform_GetWindowDpiScale { get; set; }

    /// <summary>
    /// Gets or sets the handler invoked when the viewport a window is outputting into changes, so the
    /// backend can adjust fonts or style. (<c>ThirdParty/imgui/imgui.h:4334</c>)
    /// </summary>
    public Action<ImGuiViewport>? Platform_OnChangedViewport { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads the initial work-area insets of a viewport as
    /// <c>(left, top, right, bottom)</c>. (<c>ThirdParty/imgui/imgui.h:4335</c>)
    /// </summary>
    public Func<ImGuiViewport, ImVec4>? Platform_GetWindowWorkAreaInsets { get; set; }

    /// <summary>
    /// Gets or sets the handler a Vulkan renderer uses to create a platform surface for a viewport.
    /// (<c>ThirdParty/imgui/imgui.h:4336</c>)
    /// </summary>
    public PlatformCreateVkSurfaceFn? Platform_CreateVkSurface { get; set; }

    #endregion

    #region Renderer window handlers

    /// <summary>
    /// Gets or sets the handler that creates the renderer resources for a viewport, called after
    /// <see cref="Platform_CreateWindow"/>. (<c>ThirdParty/imgui/imgui.h:4339</c>)
    /// </summary>
    public Action<ImGuiViewport>? Renderer_CreateWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that destroys the renderer resources for a viewport, called before
    /// <see cref="Platform_DestroyWindow"/>. (<c>ThirdParty/imgui/imgui.h:4340</c>)
    /// </summary>
    public Action<ImGuiViewport>? Renderer_DestroyWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that resizes the renderer resources for a viewport, called after
    /// <see cref="Platform_SetWindowSize"/>. (<c>ThirdParty/imgui/imgui.h:4341</c>)
    /// </summary>
    public Action<ImGuiViewport, ImVec2>? Renderer_SetWindowSize { get; set; }

    /// <summary>
    /// Gets or sets the handler that renders a viewport's draw data, receiving the argument passed to
    /// <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// (<c>ThirdParty/imgui/imgui.h:4342</c>)
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Renderer_RenderWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that presents a viewport's renderer buffers, receiving the argument
    /// passed to <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// (<c>ThirdParty/imgui/imgui.h:4343</c>)
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Renderer_SwapBuffers { get; set; }

    #endregion

    #region Output lists

    /// <summary>
    /// Gets the monitor list. The application or platform backend owns and updates it; the core reads
    /// it for DPI queries and for clamping windows within one monitor.
    /// (<c>ThirdParty/imgui/imgui.h:4347</c>)
    /// </summary>
    public List<ImGuiPlatformMonitor> Monitors { get; } = new();

    /// <summary>
    /// Gets the texture list, rebuilt by the core at the end of a frame. Backends read it mostly to
    /// destroy textures at shutdown. (<c>ThirdParty/imgui/imgui.h:4357</c>)
    /// </summary>
    public List<ImTextureData> Textures { get; } = new();

    /// <summary>
    /// Gets the user-facing viewport list, rebuilt by the core at the end of a frame. The main
    /// viewport is first, followed by the secondary viewports.
    /// (<c>ThirdParty/imgui/imgui.h:4362</c>)
    /// </summary>
    public List<ImGuiViewport> Viewports { get; } = new();

    #endregion

    #region Handler management

    /// <summary>
    /// Clears every platform handler and its user data, leaving the renderer handlers untouched.
    /// Mirrors canonical <c>ImGuiPlatformIO::ClearPlatformHandlers</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:16844-16863</c>).
    /// </summary>
    /// <remarks>
    /// A platform backend calls this at shutdown. The output lists and the locale and session-date
    /// values are not handlers and are left unchanged, matching canonical.
    /// </remarks>
    public void ClearPlatformHandlers()
    {
        Platform_GetClipboardTextFn = null;
        Platform_SetClipboardTextFn = null;
        Platform_OpenInShellFn = null;
        Platform_SetImeDataFn = null;
        Platform_ClipboardUserData = Platform_OpenInShellUserData = Platform_ImeUserData = IntPtr.Zero;
        Platform_CreateWindow = Platform_DestroyWindow = Platform_ShowWindow = null;
        Platform_SetWindowPos = Platform_SetWindowSize = null;
        Platform_GetWindowPos = Platform_GetWindowSize = Platform_GetWindowFramebufferScale = null;
        Platform_SetWindowFocus = null;
        Platform_GetWindowFocus = Platform_GetWindowMinimized = null;
        Platform_SetWindowTitle = null;
        Platform_SetWindowAlpha = null;
        Platform_UpdateWindow = null;
        Platform_RenderWindow = Platform_SwapBuffers = null;
        Platform_GetWindowDpiScale = null;
        Platform_OnChangedViewport = null;
        Platform_GetWindowWorkAreaInsets = null;
        Platform_CreateVkSurface = null;
    }

    /// <summary>
    /// Clears every renderer handler and the renderer resource limits, leaving the platform handlers
    /// untouched. Mirrors canonical <c>ImGuiPlatformIO::ClearRendererHandlers</c>
    /// (<c>ThirdParty/imgui/imgui.cpp:16865-16880</c>).
    /// </summary>
    /// <remarks>
    /// A renderer backend calls this at shutdown. The draw callbacks are renderer-owned and are
    /// cleared with the rest of the renderer surface.
    /// </remarks>
    public void ClearRendererHandlers()
    {
        Renderer_TextureMaxWidth = Renderer_TextureMaxHeight = 0;
        Renderer_RenderState = IntPtr.Zero;
        Renderer_CreateWindow = Renderer_DestroyWindow = null;
        Renderer_SetWindowSize = null;
        Renderer_RenderWindow = Renderer_SwapBuffers = null;
        DrawCallback_ResetRenderState = DrawCallback_SetSamplerLinear = DrawCallback_SetSamplerNearest = DrawCallback_SetSamplerFromTex = null;
    }

    #endregion

    /// <summary>
    /// Creates a platform surface for a Vulkan renderer. The result is the canonical
    /// <c>VkResult</c> and the created surface is returned through <paramref name="outVkSurface"/>.
    /// (<c>ThirdParty/imgui/imgui.h:4336</c>)
    /// </summary>
    /// <param name="viewport">Viewport whose surface is created.</param>
    /// <param name="vkInstance">Vulkan instance handle.</param>
    /// <param name="vkAllocators">Vulkan allocation callbacks, or <see cref="IntPtr.Zero"/>.</param>
    /// <param name="outVkSurface">Receives the created surface handle.</param>
    /// <returns>The Vulkan result code.</returns>
    public delegate int PlatformCreateVkSurfaceFn(ImGuiViewport viewport, ulong vkInstance, IntPtr vkAllocators, out ulong outVkSurface);
}

#endregion

#region Widget support records

/// <summary>
/// Backup of the combo preview layout, so a combo can restore the cursor and layout after drawing
/// its preview (<c>ThirdParty/imgui/imgui_internal.h:1203-1210</c>).
/// </summary>
public sealed class ImGuiComboPreviewData
{
    /// <summary>
    /// Gets or sets the preview rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1205</c>)
    /// </summary>
    public ImRect PreviewRect { get; set; }

    /// <summary>
    /// Gets or sets the cursor position backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1206</c>)
    /// </summary>
    public ImVec2 BackupCursorPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor maximum-position backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1207</c>)
    /// </summary>
    public ImVec2 BackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the previous-line cursor position backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1208</c>)
    /// </summary>
    public ImVec2 BackupCursorPosPrevLine { get; set; }

    /// <summary>
    /// Gets or sets the previous-line text baseline offset backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1209</c>)
    /// </summary>
    public float BackupPrevLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets the layout-type backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1210</c>)
    /// </summary>
    public ImGuiLayoutType BackupLayout { get; set; }
}

#region Docking Types

/// <summary>
/// Type of docking operation queued in an <see cref="ImGuiDockRequest"/>
/// (<c>ThirdParty/imgui/imgui.cpp:17992-17998</c>).
/// </summary>
public enum ImGuiDockRequestType
{
    None = 0,
    Dock,
    Undock,
    Split
}

/// <summary>
/// Queued docking/undocking request processed during the frame
/// (<c>ThirdParty/imgui/imgui.cpp:18000-18021</c>).
/// </summary>
public sealed class ImGuiDockRequest
{
    public ImGuiDockRequestType Type { get; set; } = ImGuiDockRequestType.None;
    public ImGuiWindow? DockTargetWindow { get; set; }
    public ImGuiDockNode? DockTargetNode { get; set; }
    public ImGuiWindow? DockPayload { get; set; }
    public ImGuiDir DockSplitDir { get; set; } = ImGuiDir.None;
    public float DockSplitRatio { get; set; } = 0.5f;
    public bool DockSplitOuter { get; set; }
    public ImGuiWindow? UndockTargetWindow { get; set; }
    public ImGuiDockNode? UndockTargetNode { get; set; }
}

/// <summary>
/// Transient state while hovering drop targets and previewing dock splits
/// (<c>ThirdParty/imgui/imgui.cpp:18023-18036</c>).
/// </summary>
public sealed class ImGuiDockPreviewData
{
    public ImGuiDockNode FutureNode { get; set; } = new(0);
    public bool IsDropAllowed { get; set; }
    public bool IsCenterAvailable { get; set; }
    public bool IsSidesAvailable { get; set; }
    public bool IsSplitDirExplicit { get; set; }
    public ImGuiDockNode? SplitNode { get; set; }
    public ImGuiDir SplitDir { get; set; } = ImGuiDir.None;
    public float SplitRatio { get; set; }
    public ImRect[] DropRectsDraw { get; } = new ImRect[(int)ImGuiDir.COUNT + 1];

    public ImGuiDockPreviewData()
    {
        for (int i = 0; i < DropRectsDraw.Length; i++)
        {
            DropRectsDraw[i] = new ImRect(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);
        }
    }
}

/// <summary>
/// Persistent layout settings for a docking node preserved across sessions
/// (<c>ThirdParty/imgui/imgui.cpp:18039-18052</c>).
/// </summary>
public sealed class ImGuiDockNodeSettings
{
    public uint ID { get; set; }
    public uint ParentNodeId { get; set; }
    public uint ParentWindowId { get; set; }
    public uint SelectedTabId { get; set; }
    public sbyte SplitAxis { get; set; } = -1;
    public sbyte Depth { get; set; }
    public ImGuiDockNodeFlags Flags { get; set; }
    public ImVec2ih Pos { get; set; }
    public ImVec2ih Size { get; set; }
    public ImVec2ih SizeRef { get; set; }
}

/// <summary>
/// Reference counts used while pruning unused docking settings nodes
/// (<c>ThirdParty/imgui/imgui.cpp:18367-18372</c>).
/// </summary>
/// <remarks>
/// One record tracks how many child nodes and how many windows reference a settings node, plus the
/// identifier of the root node the entry belongs to. The counts are accumulated in a single pass and then
/// consulted to decide which entries to remove or merge.
/// </remarks>
public sealed class ImGuiDockContextPruneNodeData
{
    /// <summary>
    /// Gets or sets the number of window settings entries that directly reference this node.
    /// (<c>ThirdParty/imgui/imgui.cpp:18369</c>)
    /// </summary>
    public int CountWindows { get; set; }

    /// <summary>
    /// Gets or sets the number of window settings entries that reference this node's root.
    /// (<c>ThirdParty/imgui/imgui.cpp:18369</c>)
    /// </summary>
    public int CountChildWindows { get; set; }

    /// <summary>
    /// Gets or sets the number of child settings nodes that reference this node as their parent.
    /// (<c>ThirdParty/imgui/imgui.cpp:18369</c>)
    /// </summary>
    public int CountChildNodes { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the root node this entry belongs to.
    /// (<c>ThirdParty/imgui/imgui.cpp:18370</c>)
    /// </summary>
    public uint RootId { get; set; }
}

/// <summary>
/// Contiguous pool collection indexed by key or slot, matching canonical Dear ImGui <c>ImPool&lt;T&gt;</c>
/// (<c>ThirdParty/imgui/imgui_internal.h:733-780</c>).
/// </summary>
/// <typeparam name="T">Pooled element reference type.</typeparam>
public sealed class ImPool<T> where T : class
{
    private readonly List<T?> _data = new();
    private readonly Dictionary<uint, int> _map = new();
    private readonly Stack<int> _freeIndices = new();

    /// <summary>
    /// Gets the count of live elements registered in the pool.
    /// </summary>
    public int Count => _map.Count;

    /// <summary>
    /// Gets the total capacity of the contiguous data buffer.
    /// </summary>
    public int Capacity => _data.Count;

    /// <summary>
    /// Gets the contiguous index of an element within the pool.
    /// </summary>
    /// <param name="item">Element to locate.</param>
    /// <returns>Zero-based index or -1 if not found.</returns>
    public int GetIndex(T item) => _data.IndexOf(item);

    /// <summary>
    /// Gets an element by its contiguous slot index.
    /// </summary>
    /// <param name="index">Slot index.</param>
    /// <returns>Pooled element or <c>null</c>.</returns>
    public T? GetByIndex(int index) => (index >= 0 && index < _data.Count) ? _data[index] : null;

    /// <summary>
    /// Checks whether an item exists in the pool.
    /// </summary>
    /// <param name="item">Element to test.</param>
    /// <returns><c>true</c> if present; otherwise <c>false</c>.</returns>
    public bool Contains(T item) => _data.Contains(item);

    /// <summary>
    /// Gets an element by its numeric key identifier.
    /// </summary>
    /// <param name="key">Numeric identifier key.</param>
    /// <returns>Pooled element or <c>null</c>.</returns>
    public T? GetByKey(uint key) => _map.TryGetValue(key, out int idx) ? _data[idx] : null;

    /// <summary>
    /// Gets an existing element by key or instantiates and registers a new one.
    /// </summary>
    /// <param name="key">Numeric identifier key.</param>
    /// <returns>Existing or newly added element.</returns>
    public T GetOrAddByKey(uint key)
    {
        if (_map.TryGetValue(key, out int idx))
            return _data[idx]!;

        T newItem = Activator.CreateInstance<T>();
        if (_freeIndices.Count > 0)
        {
            idx = _freeIndices.Pop();
            _data[idx] = newItem;
        }
        else
        {
            idx = _data.Count;
            _data.Add(newItem);
        }
        _map[key] = idx;
        return newItem;
    }

    /// <summary>
    /// Removes an element by key.
    /// </summary>
    /// <param name="key">Numeric identifier key.</param>
    /// <param name="item">Item reference to remove.</param>
    public void Remove(uint key, T item)
    {
        if (_map.TryGetValue(key, out int idx))
        {
            _map.Remove(key);
            _data[idx] = null;
            _freeIndices.Push(idx);
        }
    }

    /// <summary>
    /// Clears all elements from the pool.
    /// </summary>
    public void Clear()
    {
        _data.Clear();
        _map.Clear();
        _freeIndices.Clear();
    }
}

/// <summary>
/// Single tab item within an <see cref="ImGuiTabBar"/>
/// (<c>ThirdParty/imgui/imgui_internal.h:3104-3119</c>).
/// </summary>
public sealed class ImGuiTabItem
{
    public uint ID { get; set; }
    public ImGuiTabItemFlags Flags { get; set; }
    public ImGuiWindow? Window { get; set; }
    public int LastFrameVisible { get; set; } = -1;
    public int LastFrameSelected { get; set; } = -1;
    public float Offset { get; set; }
    public float Width { get; set; }
    public float ContentWidth { get; set; }
    public float RequestedWidth { get; set; } = -1.0f;
    public int NameOffset { get; set; } = -1;
    public short BeginOrder { get; set; } = -1;
    public short IndexDuringLayout { get; set; } = -1;
    public bool WantClose { get; set; }
}

/// <summary>
/// Internal tab-bar storage used by docking nodes and tab containers
/// (<c>ThirdParty/imgui/imgui_internal.h:3121-3160</c>).
/// </summary>
public sealed class ImGuiTabBar
{
    public ImGuiWindow? Window { get; set; }
    public List<ImGuiTabItem> Tabs { get; } = new();
    public ImGuiTabBarFlags Flags { get; set; }
    public uint ID { get; set; }
    public uint SelectedTabId { get; set; }
    public uint NextSelectedTabId { get; set; }
    public uint NextScrollToTabId { get; set; }
    public uint VisibleTabId { get; set; }
    public int CurrFrameVisible { get; set; } = -1;
    public int PrevFrameVisible { get; set; } = -1;
    public ImRect BarRect { get; set; }
    public float BarRectPrevWidth { get; set; }
    public float CurrTabsContentsHeight { get; set; }
    public float PrevTabsContentsHeight { get; set; }
    public float WidthAllTabs { get; set; }
    public float WidthAllTabsIdeal { get; set; }
    public float ScrollingAnim { get; set; }
    public float ScrollingTarget { get; set; }
    public float ScrollingTargetDistToVisibility { get; set; }
    public float ScrollingSpeed { get; set; }
    public float ScrollingRectMinX { get; set; }
    public float ScrollingRectMaxX { get; set; }
    public float SeparatorMinX { get; set; }
    public float SeparatorMaxX { get; set; }
    public uint ReorderRequestTabId { get; set; }
    public short ReorderRequestOffset { get; set; }
    public sbyte BeginCount { get; set; }
    public bool WantLayout { get; set; }
    public bool VisibleTabWasSubmitted { get; set; }
    public bool TabsAddedNew { get; set; }
    public bool ScrollButtonEnabled { get; set; }
    public short TabsActiveCount { get; set; }
    public short LastTabItemIdx { get; set; } = -1;
    public float ItemSpacingY { get; set; }
    public ImVec2 FramePadding { get; set; }
    public ImVec2 BackupCursorPos { get; set; }
    public List<string> TabsNames { get; } = new();
}

/// <summary>
/// Docking system node representing a leaf containing windows or an inner split node
/// (<c>ThirdParty/imgui/imgui_internal.h:2065-2126</c>).
/// </summary>
public sealed class ImGuiDockNode
{
    public uint ID { get; set; }
    public ImGuiDockNodeFlags SharedFlags { get; set; }
    public ImGuiDockNodeFlags LocalFlags { get; set; }
    public ImGuiDockNodeFlags LocalFlagsInWindows { get; set; }
    public ImGuiDockNodeFlags MergedFlags { get; set; }
    public ImGuiDockNodeState State { get; set; }
    public ImGuiDockNode? ParentNode { get; set; }
    public ImGuiDockNode?[] ChildNodes { get; } = new ImGuiDockNode?[2];
    public List<ImGuiWindow> Windows { get; } = new();
    public ImGuiTabBar? TabBar { get; set; }
    public ImVec2 Pos;
    public ImVec2 Size;
    public ImVec2 SizeRef;
    public ImGuiAxis SplitAxis { get; set; } = ImGuiAxis.None;
    public uint LastBgColor { get; set; }
    public ImGuiWindowClass WindowClass { get; set; } = new();
    public ImGuiWindow? HostWindow { get; set; }
    public ImGuiWindow? VisibleWindow { get; set; }
    public ImGuiDockNode? CentralNode { get; set; }
    public ImGuiDockNode? OnlyNodeWithWindows { get; set; }
    public int CountNodeWithWindows { get; set; }
    public int LastFrameAlive { get; set; } = -1;
    public int LastFrameActive { get; set; } = -1;
    public int LastFrameFocused { get; set; } = -1;
    public uint LastFocusedNodeId { get; set; }
    public uint SelectedTabId { get; set; }
    public uint WantCloseTabId { get; set; }
    public uint RefViewportId { get; set; }
    public byte AuthorityForPos { get; set; }
    public byte AuthorityForSize { get; set; }
    public byte AuthorityForViewport { get; set; }
    public bool IsVisible { get; set; }
    public bool IsFocused { get; set; }
    public bool IsBgDrawnThisFrame { get; set; }
    public bool HasCloseButton { get; set; }
    public bool HasWindowMenuButton { get; set; }
    public bool HasCentralNodeChild { get; set; }
    public bool WantCloseAll { get; set; }
    public bool WantLockSizeOnce { get; set; }
    public bool WantMouseMove { get; set; }
    public bool WantHiddenTabBarUpdate { get; set; }
    public bool WantHiddenTabBarToggle { get; set; }

    public ImGuiDockNode(uint id)
    {
        ID = id;
    }

    public bool IsRootNode() => ParentNode == null;
    public bool IsDockSpace() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_DockSpace) != 0;
    public bool IsFloatingNode() => ParentNode == null && (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_DockSpace) == 0;
    public bool IsCentralNode() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode) != 0;
    public bool IsHiddenTabBar() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_HiddenTabBar) != 0;
    public bool IsNoTabBar() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_NoTabBar) != 0;
    public bool IsSplitNode() => ChildNodes[0] != null;
    public bool IsLeafNode() => ChildNodes[0] == null;
    public bool IsEmpty() => ChildNodes[0] == null && Windows.Count == 0;
    public ImRect Rect() => new(Pos.X, Pos.Y, Pos.X + Size.X, Pos.Y + Size.Y);

    public void SetLocalFlags(ImGuiDockNodeFlags flags)
    {
        LocalFlags = flags;
        UpdateMergedFlags();
    }

    public void UpdateMergedFlags()
    {
        MergedFlags = SharedFlags | LocalFlags | LocalFlagsInWindows;
    }
}

/// <summary>
/// Root context state managing the active docking node hierarchy, queued docking requests,
/// and node settings (<c>ThirdParty/imgui/imgui_internal.h:2152-2159</c>).
/// </summary>
public sealed class ImGuiDockContext
{
    public Dictionary<uint, ImGuiDockNode> Nodes { get; } = new();
    public List<ImGuiDockRequest> Requests { get; } = new();
    public List<ImGuiDockNodeSettings> NodesSettings { get; } = new();
    public bool WantFullRebuild { get; set; }
}

#endregion

/// <summary>
/// Window-style colors preserved for docking purposes
/// (<c>ThirdParty/imgui/imgui_internal.h:2147-2150</c>).
/// </summary>
public sealed class ImGuiWindowDockStyle
{
    /// <summary>
    /// Gets the per-docking-element color overrides, indexed by
    /// <see cref="ImGuiWindowDockStyleCol"/>. (<c>ThirdParty/imgui/imgui_internal.h:2149</c>)
    /// </summary>
    public uint[] Colors { get; } = new uint[(int)ImGuiWindowDockStyleCol.COUNT];
}

/// <summary>
/// Per-column state for the legacy <c>Columns()</c> API
/// (<c>ThirdParty/imgui/imgui_internal.h:1898-1906</c>).
/// </summary>
public sealed class ImGuiOldColumnData
{
    /// <summary>
    /// Gets or sets the normalized column start offset, from 0.0 at the left to 1.0 at the right.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1900</c>)
    /// </summary>
    public float OffsetNorm { get; set; }

    /// <summary>
    /// Gets or sets the normalized offset before an in-progress resize.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1901</c>)
    /// </summary>
    public float OffsetNormBeforeResize { get; set; }

    /// <summary>
    /// Gets or sets the legacy column flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1902</c>)
    /// </summary>
    public ImGuiOldColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the column clip rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1903</c>)
    /// </summary>
    public ImRect ClipRect { get; set; }
}

/// <summary>
/// One legacy <c>Columns()</c> set, owning its per-column records and the draw-list splitter used to
/// interleave column output (<c>ThirdParty/imgui/imgui_internal.h:1908-1926</c>).
/// </summary>
public sealed class ImGuiOldColumns
{
    /// <summary>
    /// Gets or sets the columns-set identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1910</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the legacy column flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1911</c>)
    /// </summary>
    public ImGuiOldColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets whether the set is on its first frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1912</c>)
    /// </summary>
    public bool IsFirstFrame { get; set; }

    /// <summary>
    /// Gets or sets whether a column is currently being resized.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1913</c>)
    /// </summary>
    public bool IsBeingResized { get; set; }

    /// <summary>
    /// Gets or sets the current column index.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1914</c>)
    /// </summary>
    public int Current { get; set; }

    /// <summary>
    /// Gets or sets the number of columns.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1915</c>)
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the minimum X offset from the host work rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1916</c>)
    /// </summary>
    public float OffMinX { get; set; }

    /// <summary>
    /// Gets or sets the maximum X offset from the host work rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1916</c>)
    /// </summary>
    public float OffMaxX { get; set; }

    /// <summary>
    /// Gets or sets the minimum line Y coordinate.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1917</c>)
    /// </summary>
    public float LineMinY { get; set; }

    /// <summary>
    /// Gets or sets the maximum line Y coordinate.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1917</c>)
    /// </summary>
    public float LineMaxY { get; set; }

    /// <summary>
    /// Gets or sets the host cursor Y position backed up at <c>BeginColumns()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1918</c>)
    /// </summary>
    public float HostCursorPosY { get; set; }

    /// <summary>
    /// Gets or sets the host cursor maximum X position backed up at <c>BeginColumns()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1919</c>)
    /// </summary>
    public float HostCursorMaxPosX { get; set; }

    /// <summary>
    /// Gets or sets the host clip rectangle backed up at <c>BeginColumns()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1920</c>)
    /// </summary>
    public ImRect HostInitialClipRect { get; set; }

    /// <summary>
    /// Gets or sets the host clip rectangle backed up while column background is pushed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1921</c>)
    /// </summary>
    public ImRect HostBackupClipRect { get; set; }

    /// <summary>
    /// Gets or sets the host parent work rectangle backed up at <c>BeginColumns()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1922</c>)
    /// </summary>
    public ImRect HostBackupParentWorkRect { get; set; }

    /// <summary>
    /// Gets the per-column records.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1923</c>)
    /// </summary>
    public List<ImGuiOldColumnData> Columns { get; } = new();

    /// <summary>
    /// Gets or sets the draw-list splitter used to interleave column output.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1924</c>)
    /// </summary>
    public ImDrawListSplitter Splitter { get; set; } = new();
}

/// <summary>
/// An owned copy of drag-and-drop payload data together with the source and target metadata
/// (<c>ThirdParty/imgui/imgui.h:2856-2875</c>).
/// </summary>
/// <remarks>
/// The payload is valid only while a drag operation is active; <c>BeginDragDropSource()</c> owns and
/// copies the data, and the application must not free it.
/// </remarks>
public sealed class ImGuiPayload
{
    /// <summary>
    /// Gets or sets the payload data pointer supplied by the raw-pointer payload overload.
    /// (<c>ThirdParty/imgui/imgui.h:2859</c>)
    /// </summary>
    /// <remarks>
    /// Unlike canonical, this port does not copy raw bytes into the owned payload buffers: the pointer
    /// is exactly the caller-supplied <see cref="IntPtr"/> and remains valid only while the caller keeps
    /// that memory alive for the duration of the drag. Managed payloads submitted through
    /// <c>SetDragDropPayload(string, object)</c> leave this member as <see cref="IntPtr.Zero"/> and carry
    /// their data in <see cref="ObjectData"/> instead.
    /// </remarks>
    public IntPtr Data { get; set; }

    /// <summary>
    /// Gets or sets an optional managed object reference payload for managed internal payloads.
    /// </summary>
    public object? ObjectData { get; set; }

    /// <summary>
    /// Gets or sets the payload size in bytes.
    /// (<c>ThirdParty/imgui/imgui.h:2860</c>)
    /// </summary>
    public int DataSize { get; set; }

    /// <summary>
    /// Gets or sets the source item identifier.
    /// (<c>ThirdParty/imgui/imgui.h:2863</c>)
    /// </summary>
    public uint SourceId { get; set; }

    /// <summary>
    /// Gets or sets the source parent identifier when available.
    /// (<c>ThirdParty/imgui/imgui.h:2864</c>)
    /// </summary>
    public uint SourceParentId { get; set; }

    /// <summary>
    /// Gets or sets the frame timestamp of the data.
    /// (<c>ThirdParty/imgui/imgui.h:2865</c>)
    /// </summary>
    public int DataFrameCount { get; set; }

    /// <summary>
    /// Gets or sets the short user-supplied data-type tag, at most 32 characters.
    /// (<c>ThirdParty/imgui/imgui.h:2866</c>)
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the payload was accepted and the mouse is hovering the target.
    /// (<c>ThirdParty/imgui/imgui.h:2867</c>)
    /// </summary>
    public bool Preview { get; set; }

    /// <summary>
    /// Gets or sets whether the payload was accepted and the mouse was released over the target.
    /// (<c>ThirdParty/imgui/imgui.h:2868</c>)
    /// </summary>
    public bool Delivery { get; set; }

    /// <summary>
    /// Resets the payload to its empty state.
    /// (<c>ThirdParty/imgui/imgui.h:2871</c>)
    /// </summary>
    public void Clear()
    {
        SourceId = 0;
        SourceParentId = 0;
        Data = IntPtr.Zero;
        ObjectData = null;
        DataSize = 0;
        DataType = string.Empty;
        DataFrameCount = -1;
        Preview = false;
        Delivery = false;
    }

    /// <summary>
    /// Returns whether the payload was accepted for a given data-type tag.
    /// (<c>ThirdParty/imgui/imgui.h:2872</c>)
    /// </summary>
    /// <param name="type">The data-type tag to test.</param>
    /// <returns><see langword="true"/> when a payload is present and the tag matches.</returns>
    public bool IsDataType(string type)
    {
        return DataFrameCount != -1 && string.Equals(DataType, type, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns whether the payload is in the preview state.
    /// (<c>ThirdParty/imgui/imgui.h:2873</c>)
    /// </summary>
    /// <returns>The preview flag.</returns>
    public bool IsPreview()
    {
        return Preview;
    }

    /// <summary>
    /// Returns whether the payload is in the delivery state.
    /// (<c>ThirdParty/imgui/imgui.h:2874</c>)
    /// </summary>
    /// <returns>The delivery flag.</returns>
    public bool IsDelivery()
    {
        return Delivery;
    }
}

#endregion

#region Debug tool records

/// <summary>
/// One level of an item-path query, describing the identifier that was pushed and where its
/// description text lives (<c>ThirdParty/imgui/imgui_internal.h:2385-2394</c>).
/// </summary>
public sealed class ImGuiStackLevelInfo
{
    /// <summary>
    /// Gets or sets the identifier pushed at this level.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2387</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the number of frames this level has been queried for, or zero when it is not in
    /// progress. A level is abandoned once it has been queried for more than two frames, because the
    /// canonical query advances at most one level per frame and a level that never answers would otherwise
    /// stall the whole path forever. (<c>ThirdParty/imgui/imgui_internal.h:2388</c>)
    /// </summary>
    public sbyte QueryFrameCount { get; set; }

    /// <summary>
    /// Gets or sets whether a sub-query produced a description for this level.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2389</c>)
    /// </summary>
    public bool QuerySuccess { get; set; }

    /// <summary>
    /// Gets or sets the data type used to push the level, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2390</c>)
    /// </summary>
    public sbyte DataType { get; set; } = -1;

    /// <summary>
    /// Gets or sets the offset into the parent query's result-description buffer, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2391</c>)
    /// </summary>
    public int DescOffset { get; set; } = -1;
}

/// <summary>
/// State of an identifier-stack debug query started by the debug tools
/// (<c>ThirdParty/imgui/imgui_internal.h:2396-2408</c>).
/// </summary>
public sealed class ImGuiDebugItemPathQuery
{
    /// <summary>
    /// Gets or sets the identifier being queried.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2398</c>)
    /// </summary>
    public uint MainID { get; set; }

    /// <summary>
    /// Gets or sets whether the query is active, which disambiguates a genuine push of identifier
    /// zero. (<c>ThirdParty/imgui/imgui_internal.h:2399</c>)
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets whether all sub-queries are complete, some of which may have failed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2400</c>)
    /// </summary>
    public bool Complete { get; set; }

    /// <summary>
    /// Gets or sets the current query step: -1 selects the stack-and-init phase and a non-negative
    /// value fills one stack level. (<c>ThirdParty/imgui/imgui_internal.h:2401</c>)
    /// </summary>
    public sbyte Step { get; set; }

    /// <summary>
    /// Gets the per-level query results.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2402</c>)
    /// </summary>
    public List<ImGuiStackLevelInfo> Results { get; } = new();

    /// <summary>
    /// Gets the buffer that accumulates level descriptions.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2403</c>)
    /// </summary>
    public ImGuiTextBuffer ResultsDescBuf { get; } = new();

    /// <summary>
    /// Gets the buffer that accumulates the result path.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2404</c>)
    /// </summary>
    public ImGuiTextBuffer ResultPathBuf { get; } = new();
}

/// <summary>
/// Options and timing state of the identifier-stack debug tool
/// (<c>ThirdParty/imgui/imgui_internal.h:2410-2419</c>).
/// </summary>
public sealed class ImGuiIDStackTool
{
    /// <summary>
    /// Gets or sets whether non-ASCII characters are hex-encoded in the output.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2412</c>)
    /// </summary>
    public bool OptHexEncodeNonAsciiChars { get; set; } = true;

    /// <summary>
    /// Gets or sets whether pressing Ctrl+C copies the output to the clipboard.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2413</c>)
    /// </summary>
    public bool OptCopyToClipboardOnCtrlC { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which the tool was active, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2414</c>)
    /// </summary>
    public int LastActiveFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the time of the last clipboard copy.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2415</c>)
    /// </summary>
    public float CopyToClipboardLastTime { get; set; } = float.NegativeInfinity;
}

/// <summary>
/// One recorded frame of allocation activity, used to audit the engine's guidance that repeating frames
/// should not allocate (<c>ThirdParty/imgui/imgui_internal.h:2347-2352</c>).
/// </summary>
public sealed class ImGuiDebugAllocEntry
{
    /// <summary>
    /// Gets or sets the frame number the counts belong to.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2349</c>)
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the number of allocations recorded in that frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2350</c>)
    /// </summary>
    public short AllocCount { get; set; }

    /// <summary>
    /// Gets or sets the number of releases recorded in that frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2351</c>)
    /// </summary>
    public short FreeCount { get; set; }
}

/// <summary>
/// Running allocation totals plus the last six frames that had any allocation activity
/// (<c>ThirdParty/imgui/imgui_internal.h:2354-2362</c>).
/// </summary>
/// <remarks>
/// Canonical records six entries so the metrics window can show whether a repeat frame allocated without
/// keeping an unbounded history. The ring index points at the most recently written entry, not the next
/// free one, matching canonical.
/// </remarks>
public sealed class ImGuiDebugAllocInfo
{
    /// <summary>
    /// Gets or sets the number of recorded allocations since the context was created.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2356</c>)
    /// </summary>
    public int TotalAllocCount { get; set; }

    /// <summary>
    /// Gets or sets the number of recorded releases since the context was created.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2357</c>)
    /// </summary>
    public int TotalFreeCount { get; set; }

    /// <summary>
    /// Gets or sets the ring index of the most recently written entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2358</c>)
    /// </summary>
    public short LastEntriesIdx { get; set; }

    /// <summary>
    /// Gets the six-entry ring of the most recent frames that had allocation activity.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2359</c>)
    /// </summary>
    public ImGuiDebugAllocEntry[] LastEntriesBuf { get; } = CreateEntries();

    /// <summary>
    /// Creates the ring entries.
    /// </summary>
    /// <returns>Six freshly constructed entries.</returns>
    /// <remarks>
    /// The canonical buffer is a plain array cleared by the constructor's <c>memset</c>; a managed array of
    /// reference-typed entries must be filled explicitly so no slot is ever null.
    /// </remarks>
    private static ImGuiDebugAllocEntry[] CreateEntries()
    {
        ImGuiDebugAllocEntry[] entries = new ImGuiDebugAllocEntry[6];
        for (int n = 0; n < entries.Length; n++)
            entries[n] = new ImGuiDebugAllocEntry();
        return entries;
    }
}

/// <summary>
/// Persistent options of the metrics/debugger window, including which inspector nodes are expanded and
/// which auxiliary windows it opens (<c>ThirdParty/imgui/imgui_internal.h:2364-2383</c>).
/// </summary>
/// <remarks>
/// The window itself is opened by its caller, not by a member of this record: canonical has no
/// <c>ShowMetricsWindow</c> field here, so the demo owns that toggle.
/// </remarks>
public sealed class ImGuiMetricsConfig
{
    /// <summary>
    /// Gets or sets whether the metrics window also shows the debug log window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2366</c>)
    /// </summary>
    public bool ShowDebugLog { get; set; }

    /// <summary>
    /// Gets or sets whether the metrics window also shows the identifier-stack tool.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2367</c>)
    /// </summary>
    public bool ShowIDStackTool { get; set; }

    /// <summary>
    /// Gets or sets whether window rectangles are highlighted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2368</c>)
    /// </summary>
    public bool ShowWindowsRects { get; set; }

    /// <summary>
    /// Gets or sets whether the window list is ordered by begin order instead of display order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2369</c>)
    /// </summary>
    public bool ShowWindowsBeginOrder { get; set; }

    /// <summary>
    /// Gets or sets whether table rectangles are highlighted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2370</c>)
    /// </summary>
    public bool ShowTablesRects { get; set; }

    /// <summary>
    /// Gets or sets whether draw-command triangle meshes are drawn.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2371</c>)
    /// </summary>
    public bool ShowDrawCmdMesh { get; set; } = true;

    /// <summary>
    /// Gets or sets whether draw-command bounding boxes are drawn.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2372</c>)
    /// </summary>
    public bool ShowDrawCmdBoundingBoxes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the text-encoding viewer is shown.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2373</c>)
    /// </summary>
    public bool ShowTextEncodingViewer { get; set; }

    /// <summary>
    /// Gets or sets whether the used rectangle of the font texture is highlighted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2374</c>)
    /// </summary>
    public bool ShowTextureUsedRect { get; set; }

    /// <summary>
    /// Gets or sets whether docking nodes are highlighted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2375</c>)
    /// </summary>
    public bool ShowDockingNodes { get; set; }

    /// <summary>
    /// Gets or sets which window-rectangle preset is selected, or -1 for none.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2376</c>)
    /// </summary>
    public int ShowWindowsRectsType { get; set; } = -1;

    /// <summary>
    /// Gets or sets which table-rectangle preset is selected, or -1 for none.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2377</c>)
    /// </summary>
    public int ShowTablesRectsType { get; set; } = -1;

    /// <summary>
    /// Gets or sets the monitor whose bounds are highlighted, or -1 for none.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2378</c>)
    /// </summary>
    public int HighlightMonitorIdx { get; set; } = -1;

    /// <summary>
    /// Gets or sets the viewport whose thumbnail is highlighted, or zero for none.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2379</c>)
    /// </summary>
    public uint HighlightViewportID { get; set; }

    /// <summary>
    /// Gets or sets the age in months after which settings entries are reported as discardable.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2380</c>)
    /// </summary>
    public int SettingsDiscardMonths { get; set; } = 6;

    /// <summary>
    /// Gets or sets whether old settings entries are highlighted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2381</c>)
    /// </summary>
    public bool SettingsHighlightOldEntries { get; set; }

    /// <summary>
    /// Gets or sets whether a preview of the selected font is drawn.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2382</c>)
    /// </summary>
    public bool ShowFontPreview { get; set; } = true;
}

#endregion

#region Window temporary data

/// <summary>
/// Per-window frame-temporary data: the layout cursor, the active navigation layer, the local
/// parameter stacks, and the menu-column measurement for one window
/// (<c>ThirdParty/imgui/imgui_internal.h:2862-2925</c>).
/// </summary>
/// <remarks>
/// Canonical deliberately declares no constructor because the data is zero-cleared as part of
/// <see cref="ImGuiWindow"/> and reinitialized in <c>Begin()</c>. Members are grouped below using
/// the canonical comment sections.
/// </remarks>
public sealed class ImGuiWindowTempData
{
    #region Layout

    /// <summary>
    /// Gets or sets the current emitting position in absolute coordinates.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2873</c>)
    /// </summary>
    public ImVec2 CursorPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor position on the previous line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2874</c>)
    /// </summary>
    public ImVec2 CursorPosPrevLine { get; set; }

    /// <summary>
    /// Gets or sets the initial position after <c>Begin()</c>, generally the window position plus
    /// padding. (<c>ThirdParty/imgui/imgui_internal.h:2875</c>)
    /// </summary>
    public ImVec2 CursorStartPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor maximum position, which grows during the frame and becomes the next
    /// frame's content size. (<c>ThirdParty/imgui/imgui_internal.h:2876</c>)
    /// </summary>
    public ImVec2 CursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the ideal maximum position used for auto-resize.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2877</c>)
    /// </summary>
    public ImVec2 IdealMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the size of the current line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2878</c>)
    /// </summary>
    public ImVec2 CurrLineSize { get; set; }

    /// <summary>
    /// Gets or sets the size of the previous line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2879</c>)
    /// </summary>
    public ImVec2 PrevLineSize { get; set; }

    /// <summary>
    /// Gets or sets the baseline offset of the current line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2880</c>)
    /// </summary>
    public float CurrLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets the baseline offset of the previous line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2881</c>)
    /// </summary>
    public float PrevLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets whether the next item is emitted on the same line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2882</c>)
    /// </summary>
    public bool IsSameLine { get; set; }

    /// <summary>
    /// Gets or sets whether <c>SetCursorPos()</c> was applied on the current line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2883</c>)
    /// </summary>
    public bool IsSetPos { get; set; }

    /// <summary>
    /// Gets or sets the indentation from the left of the window, pushed and popped by trees.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2884</c>)
    /// </summary>
    public ImVec1 Indent { get; set; }

    /// <summary>
    /// Gets or sets the offset to the current column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2885</c>)
    /// </summary>
    public ImVec1 ColumnsOffset { get; set; }

    /// <summary>
    /// Gets or sets the offset applied by groups.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2886</c>)
    /// </summary>
    public ImVec1 GroupOffset { get; set; }

    /// <summary>
    /// Gets or sets the precision loss of the cursor start position under large scrolling, read by
    /// the clipper. (<c>ThirdParty/imgui/imgui_internal.h:2887</c>)
    /// </summary>
    public ImVec2 CursorStartPosLossyness { get; set; }

    #endregion

    #region Keyboard/Gamepad navigation

    /// <summary>
    /// Gets or sets the current navigation layer, in the range 0..31, though only the first two
    /// layers are used. (<c>ThirdParty/imgui/imgui_internal.h:2890</c>)
    /// </summary>
    public ImGuiNavLayer NavLayerCurrent { get; set; }

    /// <summary>
    /// Gets or sets the mask of layers that were written to in the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2891</c>)
    /// </summary>
    public short NavLayersActiveMask { get; set; }

    /// <summary>
    /// Gets or sets the mask of layers written to so far in the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2892</c>)
    /// </summary>
    public short NavLayersActiveMaskNext { get; set; }

    /// <summary>
    /// Gets or sets whether the current work location may scroll horizontally on left/right input.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2893</c>)
    /// </summary>
    public bool NavIsScrollPushableX { get; set; }

    /// <summary>
    /// Gets or sets whether the navigation highlight is hidden for one frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2894</c>)
    /// </summary>
    public bool NavHideHighlightOneFrame { get; set; }

    /// <summary>
    /// Gets or sets whether vertical scrolling is possible.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2895</c>)
    /// </summary>
    public bool NavWindowHasScrollY { get; set; }

    #endregion

    #region Miscellaneous

    /// <summary>
    /// Gets or sets whether a menu bar is currently being appended.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2898</c>)
    /// </summary>
    public bool MenuBarAppending { get; set; }

    /// <summary>
    /// Gets or sets the menu-bar offset, where X is the per-layer cursor and Y accounts for
    /// safe-area padding. (<c>ThirdParty/imgui/imgui_internal.h:2899</c>)
    /// </summary>
    public ImVec2 MenuBarOffset { get; set; }

    /// <summary>
    /// Gets or sets the simplified column measurement used for menu items.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2900</c>)
    /// </summary>
    public ImGuiMenuColumns MenuColumns { get; set; } = new();

    /// <summary>
    /// Gets or sets the current tree depth.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2901</c>)
    /// </summary>
    public int TreeDepth { get; set; }

    /// <summary>
    /// Gets or sets the bitmask of depths that have tree-node stack data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2902</c>)
    /// </summary>
    public uint TreeHasStackDataDepthMask { get; set; }

    /// <summary>
    /// Gets or sets the bitmask of depths still recording the Y2 coordinate for clipped nodes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2903</c>)
    /// </summary>
    public uint TreeRecordsClippedNodesY2Mask { get; set; }

    /// <summary>
    /// Gets the child windows of this window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2904</c>)
    /// </summary>
    public List<ImGuiWindow> ChildWindows { get; } = new();

    /// <summary>
    /// Gets or sets the persistent per-window storage for the current window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2905</c>)
    /// </summary>
    public ImGuiStorage? StateStorage { get; set; }

    /// <summary>
    /// Gets or sets the current legacy columns set.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2906</c>)
    /// </summary>
    public ImGuiOldColumns? CurrentColumns { get; set; }

    /// <summary>
    /// Gets or sets the current table index into the table pool.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2907</c>)
    /// </summary>
    public int CurrentTableIdx { get; set; }

    /// <summary>
    /// Gets or sets the current layout type.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2908</c>)
    /// </summary>
    public ImGuiLayoutType LayoutType { get; set; }

    /// <summary>
    /// Gets or sets the parent window layout type captured at <c>Begin()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2909</c>)
    /// </summary>
    public ImGuiLayoutType ParentLayoutType { get; set; }

    /// <summary>
    /// Gets or sets the dimming background color used by modal windows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2910</c>)
    /// </summary>
    public uint ModalDimBgColor { get; set; }

    #endregion

    #region Status flags

    /// <summary>
    /// Gets or sets the item status flags of the window item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2913</c>)
    /// </summary>
    public ImGuiItemStatusFlags WindowItemStatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the item status flags of the child item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2914</c>)
    /// </summary>
    public ImGuiItemStatusFlags ChildItemStatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the item status flags of the dock tab item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2915</c>)
    /// </summary>
    public ImGuiItemStatusFlags DockTabItemStatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the rectangle of the dock tab item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2916</c>)
    /// </summary>
    public ImRect DockTabItemRect { get; set; }

    #endregion

    #region Local parameter stacks

    /// <summary>
    /// Gets or sets the current item width; positive values are pixels and negative values align to
    /// the right. (<c>ThirdParty/imgui/imgui_internal.h:2920</c>)
    /// </summary>
    public float ItemWidth { get; set; }

    /// <summary>
    /// Gets or sets the default item width.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2921</c>)
    /// </summary>
    public float ItemWidthDefault { get; set; }

    /// <summary>
    /// Gets or sets the current text wrap position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2922</c>)
    /// </summary>
    public float TextWrapPos { get; set; }

    /// <summary>
    /// Gets the item-width restore stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2923</c>)
    /// </summary>
    public List<float> ItemWidthStack { get; } = new();

    /// <summary>
    /// Gets the text-wrap restore stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2924</c>)
    /// </summary>
    public List<float> TextWrapPosStack { get; } = new();

    #endregion
}

#endregion

#region ImGuiWindow

/// <summary>
/// Persistent and per-frame state for one window: identity, geometry, scrolling, visibility,
/// activation, the identifier stack, and the embedded draw list
/// (<c>ThirdParty/imgui/imgui_internal.h:2927-3076</c>).
/// </summary>
/// <remarks>
/// <para>
/// Windows are created by the windowing pipeline and stored in <see cref="ImGuiContext.Windows"/>.
/// The fixed identifier of a window is derived from its name; this port leaves the name-hash
/// computation to the windowing pipeline so that identifier generation stays in one place.
/// </para>
/// <para>
/// Members are grouped below using the canonical comment sections. The
/// <c>DockNode</c> and <c>DockNodeAsHost</c> members are intentionally absent because the docking
/// node type belongs to a later chunk; see the chunk report for the deferred list.
/// </para>
/// </remarks>
public sealed class ImGuiWindow
{
    #region Identity and viewport

    /// <summary>
    /// Gets or sets the parent context.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2930</c>)
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the window name, which is owned by the window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2931</c>)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the window identifier, normally the hash of <see cref="Name"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2932</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the current window flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2933</c>)
    /// </summary>
    public ImGuiWindowFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the window flags captured in the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2933</c>)
    /// </summary>
    public ImGuiWindowFlags FlagsPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the child-window flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2934</c>)
    /// </summary>
    public ImGuiChildFlags ChildFlags { get; set; }

    /// <summary>
    /// Gets or sets the advanced window-class hints.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2935</c>)
    /// </summary>
    public ImGuiWindowClass WindowClass { get; set; } = new();

    /// <summary>
    /// Gets or sets the viewport that hosts the window; it is set by <c>Begin()</c> and may be
    /// <see langword="null"/> for inactive discarded windows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2936</c>)
    /// </summary>
    public ImGuiViewportP? Viewport { get; set; }

    /// <summary>
    /// Gets or sets the backup of the viewport identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2937</c>)
    /// </summary>
    public uint ViewportId { get; set; }

    /// <summary>
    /// Gets or sets the backup of the viewport position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2938</c>)
    /// </summary>
    public ImVec2 ViewportPos { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the index of a platform monitor the viewport may extend onto, or -1; the value
    /// is reset every frame. (<c>ThirdParty/imgui/imgui_internal.h:2939</c>)
    /// </summary>
    public int ViewportAllowPlatformMonitorExtend { get; set; } = -1;

    #endregion

    #region Geometry and decoration

    /// <summary>
    /// Gets or sets the window position, rounded up to the nearest pixel.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2940</c>)
    /// </summary>
    public ImVec2 Pos { get; set; }

    /// <summary>
    /// Gets or sets the current size, equal to <see cref="SizeFull"/> or the collapsed title-bar
    /// size. (<c>ThirdParty/imgui/imgui_internal.h:2941</c>)
    /// </summary>
    public ImVec2 Size { get; set; }

    /// <summary>
    /// Gets or sets the size when the window is not collapsed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2942</c>)
    /// </summary>
    public ImVec2 SizeFull { get; set; }

    /// <summary>
    /// Gets or sets the content and scrollable client size from the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2943</c>)
    /// </summary>
    public ImVec2 ContentSize { get; set; }

    /// <summary>
    /// Gets or sets the ideal content size used for auto-resize.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2944</c>)
    /// </summary>
    public ImVec2 ContentSizeIdeal { get; set; }

    /// <summary>
    /// Gets or sets the explicit content size queued by <c>SetNextWindowContentSize()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2945</c>)
    /// </summary>
    public ImVec2 ContentSizeExplicit { get; set; }

    /// <summary>
    /// Gets or sets the window padding captured at <c>Begin()</c> time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2946</c>)
    /// </summary>
    public ImVec2 WindowPadding { get; set; }

    /// <summary>
    /// Gets or sets the window corner rounding captured at <c>Begin()</c> time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2947</c>)
    /// </summary>
    public float WindowRounding { get; set; }

    /// <summary>
    /// Gets or sets the window border size captured at <c>Begin()</c> time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2948</c>)
    /// </summary>
    public float WindowBorderSize { get; set; }

    /// <summary>
    /// Gets or sets the title-bar height.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2949</c>)
    /// </summary>
    public float TitleBarHeight { get; set; }

    /// <summary>
    /// Gets or sets the menu-bar height.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2949</c>)
    /// </summary>
    public float MenuBarHeight { get; set; }

    /// <summary>
    /// Gets or sets the left outer decoration offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2950</c>)
    /// </summary>
    public float DecoOuterSizeX1 { get; set; }

    /// <summary>
    /// Gets or sets the top outer decoration offset, covering the title and menu bars.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2950</c>)
    /// </summary>
    public float DecoOuterSizeY1 { get; set; }

    /// <summary>
    /// Gets or sets the right outer decoration offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2951</c>)
    /// </summary>
    public float DecoOuterSizeX2 { get; set; }

    /// <summary>
    /// Gets or sets the bottom outer decoration offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2951</c>)
    /// </summary>
    public float DecoOuterSizeY2 { get; set; }

    /// <summary>
    /// Gets or sets the inner decoration offset X1, applied over the inner rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2952</c>)
    /// </summary>
    public float DecoInnerSizeX1 { get; set; }

    /// <summary>
    /// Gets or sets the inner decoration offset Y1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2952</c>)
    /// </summary>
    public float DecoInnerSizeY1 { get; set; }

    /// <summary>
    /// Gets or sets the size of the name buffer, which may exceed the name length.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2953</c>)
    /// </summary>
    public int NameBufLen { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the move grip.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2954</c>)
    /// </summary>
    public uint MoveId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the tab.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2955</c>)
    /// </summary>
    public uint TabId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the corresponding item in the parent window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2956</c>)
    /// </summary>
    public uint ChildId { get; set; }

    /// <summary>
    /// Gets or sets the identifier used in the popup stack when the window is a popup or menu.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2957</c>)
    /// </summary>
    public uint PopupId { get; set; }

    #endregion

    #region Scrolling

    /// <summary>
    /// Gets or sets the current scroll amount.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2958</c>)
    /// </summary>
    public ImVec2 Scroll { get; set; }

    /// <summary>
    /// Gets or sets the maximum scroll amount.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2959</c>)
    /// </summary>
    public ImVec2 ScrollMax { get; set; }

    /// <summary>
    /// Gets or sets the target scroll position with scrolling canceled out.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2960</c>)
    /// </summary>
    public ImVec2 ScrollTarget { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the scroll centering ratio, where 0.0 is the top and 0.5 is centered.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2961</c>)
    /// </summary>
    public ImVec2 ScrollTargetCenterRatio { get; set; } = new(0.5f, 0.5f);

    /// <summary>
    /// Gets or sets the snapping threshold, where zero disables snapping.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2962</c>)
    /// </summary>
    public ImVec2 ScrollTargetEdgeSnapDist { get; set; }

    /// <summary>
    /// Gets or sets the size taken by each scrollbar on its smaller axis.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2963</c>)
    /// </summary>
    public ImVec2 ScrollbarSizes { get; set; }

    /// <summary>
    /// Gets or sets whether the horizontal scrollbar is visible.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2964</c>)
    /// </summary>
    public bool ScrollbarX { get; set; }

    /// <summary>
    /// Gets or sets whether the vertical scrollbar is visible.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2964</c>)
    /// </summary>
    public bool ScrollbarY { get; set; }

    /// <summary>
    /// Gets or sets whether horizontal scrollbar visibility was auto-stabilized.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2965</c>)
    /// </summary>
    public bool ScrollbarXStabilizeEnabled { get; set; }

    /// <summary>
    /// Gets or sets the history bitfield that stabilizes horizontal scrollbar visibility against
    /// feedback loops. (<c>ThirdParty/imgui/imgui_internal.h:2966</c>)
    /// </summary>
    public byte ScrollbarXStabilizeToggledHistory { get; set; }

    #endregion

    #region Visibility, activation, and hidden frames

    /// <summary>
    /// Gets or sets whether the window owns its viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2967</c>)
    /// </summary>
    public bool ViewportOwned { get; set; }

    /// <summary>
    /// Gets or sets whether the window was set active by <c>Begin()</c>; collapsed windows stay
    /// inactive. (<c>ThirdParty/imgui/imgui_internal.h:2968</c>)
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets whether the window was active in a previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2969</c>)
    /// </summary>
    public bool WasActive { get; set; }

    /// <summary>
    /// Gets or sets whether any widget accessed the window this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2970</c>)
    /// </summary>
    public bool WriteAccessed { get; set; }

    /// <summary>
    /// Gets or sets whether the window is collapsed to its title bar.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2971</c>)
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Gets or sets a request to toggle the collapsed state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2972</c>)
    /// </summary>
    public bool WantCollapseToggle { get; set; }

    /// <summary>
    /// Gets or sets whether items can safely be clipped.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2973</c>)
    /// </summary>
    public bool SkipItems { get; set; }

    /// <summary>
    /// Gets or sets whether the previous frame contents are reused and <c>Begin()</c> returns
    /// false. (<c>ThirdParty/imgui/imgui_internal.h:2974</c>)
    /// </summary>
    public bool SkipRefresh { get; set; }

    /// <summary>
    /// Gets or sets whether the current frame is the appearing or re-appearing frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2975</c>)
    /// </summary>
    public bool Appearing { get; set; }

    /// <summary>
    /// Gets or sets whether the window is hidden, driven by the hidden-frame counters.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2976</c>)
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets whether this is the fallback <c>"Debug##Default"</c> window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2977</c>)
    /// </summary>
    public bool IsFallbackWindow { get; set; }

    /// <summary>
    /// Gets or sets whether the window was created through an explicit child-window request rather
    /// than through docking. (<c>ThirdParty/imgui/imgui_internal.h:2978</c>)
    /// </summary>
    public bool IsExplicitChild { get; set; }

    /// <summary>
    /// Gets or sets whether the window has a close button.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2979</c>)
    /// </summary>
    public bool HasCloseButton { get; set; }

    /// <summary>
    /// Gets or sets the border currently hovered for resizing, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2980</c>)
    /// </summary>
    public sbyte ResizeBorderHovered { get; set; }

    /// <summary>
    /// Gets or sets the border currently held for resizing, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2981</c>)
    /// </summary>
    public sbyte ResizeBorderHeld { get; set; }

    /// <summary>
    /// Gets or sets the number of <c>Begin()</c> calls this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2982</c>)
    /// </summary>
    public short BeginCount { get; set; }

    /// <summary>
    /// Gets or sets the number of <c>Begin()</c> calls in the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2983</c>)
    /// </summary>
    public short BeginCountPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the begin order within the immediate parent.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2984</c>)
    /// </summary>
    public short BeginOrderWithinParent { get; set; }

    /// <summary>
    /// Gets or sets the begin order within the whole context.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2985</c>)
    /// </summary>
    public short BeginOrderWithinContext { get; set; }

    /// <summary>
    /// Gets or sets the order within the focus-order list.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2986</c>)
    /// </summary>
    public short FocusOrder { get; set; }

    /// <summary>
    /// Gets or sets the last automatic-positioning direction.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2987</c>)
    /// </summary>
    public ImGuiDir AutoPosLastDirection { get; set; }

    /// <summary>
    /// Gets or sets the number of auto-fit frames remaining on the horizontal axis, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2988</c>)
    /// </summary>
    public sbyte AutoFitFramesX { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of auto-fit frames remaining on the vertical axis, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2988</c>)
    /// </summary>
    public sbyte AutoFitFramesY { get; set; } = -1;

    /// <summary>
    /// Gets or sets whether auto-fit may only grow the window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2989</c>)
    /// </summary>
    public bool AutoFitOnlyGrows { get; set; }

    /// <summary>
    /// Gets or sets the number of frames to hide the window while skipping items.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2990</c>)
    /// </summary>
    public sbyte HiddenFramesCanSkipItems { get; set; }

    /// <summary>
    /// Gets or sets the number of frames to hide the window while still measuring items.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2991</c>)
    /// </summary>
    public sbyte HiddenFramesCannotSkipItems { get; set; }

    /// <summary>
    /// Gets or sets the frame count until which the window is hidden at render time only.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2992</c>)
    /// </summary>
    public sbyte HiddenFramesForRenderOnly { get; set; }

    /// <summary>
    /// Gets or sets the number of frames during which window interactions are disabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2993</c>)
    /// </summary>
    public sbyte DisableInputsFrames { get; set; }

    /// <summary>
    /// Gets or sets the click-and-drag behavior on the window background.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2994</c>)
    /// </summary>
    public ImGuiWindowBgClickFlags BgClickFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowPos()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2995</c>)
    /// </summary>
    public ImGuiCond SetWindowPosAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowSize()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2996</c>)
    /// </summary>
    public ImGuiCond SetWindowSizeAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowCollapsed()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2997</c>)
    /// </summary>
    public ImGuiCond SetWindowCollapsedAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowDock()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2998</c>)
    /// </summary>
    public ImGuiCond SetWindowDockAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the stored position for a nonzero pivot, processed after the size is known.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2999</c>)
    /// </summary>
    public ImVec2 SetWindowPosVal { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the position pivot, where (0,0) is top-left, (0.5,0.5) is centered, and (1,1)
    /// is bottom-right. (<c>ThirdParty/imgui/imgui_internal.h:3000</c>)
    /// </summary>
    public ImVec2 SetWindowPosPivot { get; set; } = new(float.MaxValue, float.MaxValue);

    #endregion

    #region ID stack and temporary data

    /// <summary>
    /// Gets the identifier stack seeded with the window identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3002</c>)
    /// </summary>
    public List<uint> IDStack { get; } = new();

    /// <summary>
    /// Gets or sets the temporary per-window frame data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3003</c>)
    /// </summary>
    public ImGuiWindowTempData DC { get; set; } = new();

    #endregion

    #region Rectangles

    /// <summary>
    /// Gets or sets the outer clipped rectangle of the window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3007</c>)
    /// </summary>
    public ImRect OuterRectClipped { get; set; }

    /// <summary>
    /// Gets or sets the inner rectangle, which omits the title bar, menu bar, and scrollbars.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3008</c>)
    /// </summary>
    public ImRect InnerRect { get; set; }

    /// <summary>
    /// Gets or sets the inner clip rectangle, the inner rectangle shrunk by half the padding and
    /// clipped to the viewport or parent. (<c>ThirdParty/imgui/imgui_internal.h:3009</c>)
    /// </summary>
    public ImRect InnerClipRect { get; set; }

    /// <summary>
    /// Gets or sets the whole scrolling region, reduced by containers and shrunk by the full
    /// padding. (<c>ThirdParty/imgui/imgui_internal.h:3010</c>)
    /// </summary>
    public ImRect WorkRect { get; set; }

    /// <summary>
    /// Gets or sets the backup of the work rectangle taken before entering a container.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3011</c>)
    /// </summary>
    public ImRect ParentWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the current clipping and scissoring rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3012</c>)
    /// </summary>
    public ImRect ClipRect { get; set; }

    /// <summary>
    /// Gets or sets the content-region rectangle, essentially the work rectangle without scroll
    /// handling. (<c>ThirdParty/imgui/imgui_internal.h:3013</c>)
    /// </summary>
    public ImRect ContentRegionRect { get; set; }

    /// <summary>
    /// Gets or sets the optional pass-through hole size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3014</c>)
    /// </summary>
    public ImVec2ih HitTestHoleSize { get; set; }

    /// <summary>
    /// Gets or sets the optional pass-through hole offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3015</c>)
    /// </summary>
    public ImVec2ih HitTestHoleOffset { get; set; }

    #endregion

    #region Lifetime, storage, and fonts

    /// <summary>
    /// Gets or sets the last frame on which the window was active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3017</c>)
    /// </summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last frame on which the window was just focused.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3018</c>)
    /// </summary>
    public int LastFrameJustFocused { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last timestamp at which the window was active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3019</c>)
    /// </summary>
    public float LastTimeActive { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the persistent per-window storage, such as tree open and close state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3020</c>)
    /// </summary>
    public ImGuiStorage StateStorage { get; set; } = new();

    /// <summary>
    /// Gets the legacy columns storage.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3021</c>)
    /// </summary>
    public List<ImGuiOldColumns> ColumnsStorage { get; } = new();

    /// <summary>
    /// Gets or sets the per-window font scale multiplier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3022</c>)
    /// </summary>
    public float FontWindowScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the accumulated font scale of parent windows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3023</c>)
    /// </summary>
    public float FontWindowScaleParents { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the copy of the computed font size captured at <c>Begin()</c> time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3024</c>)
    /// </summary>
    public float FontRefSize { get; set; }

    /// <summary>
    /// Gets or sets the offset of this window into the settings list, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3025</c>)
    /// </summary>
    public int SettingsOffset { get; set; } = -1;

    #endregion

    #region Draw list, parents, and roots

    /// <summary>
    /// Gets or sets the window draw list, kept as a separate member for compatibility with
    /// canonical internal-header users. (<c>ThirdParty/imgui/imgui_internal.h:3027</c>)
    /// </summary>
    public ImDrawList DrawList { get; set; }

    /// <summary>
    /// Gets or sets the embedded draw-list instance.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3028</c>)
    /// </summary>
    public ImDrawList DrawListInst { get; set; }

    /// <summary>
    /// Gets or sets the parent used for child, popup, and docked windows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3029</c>)
    /// </summary>
    public ImGuiWindow? ParentWindow { get; set; }

    /// <summary>
    /// Gets or sets the parent within the current begin stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3030</c>)
    /// </summary>
    public ImGuiWindow? ParentWindowInBeginStack { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first-non-child ancestor, without crossing popups or dock nodes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3031</c>)
    /// </summary>
    public ImGuiWindow? RootWindow { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first-non-child ancestor, crossing popups.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3032</c>)
    /// </summary>
    public ImGuiWindow? RootWindowPopupTree { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first-non-child ancestor, crossing dock nodes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3033</c>)
    /// </summary>
    public ImGuiWindow? RootWindowDockTree { get; set; }

    /// <summary>
    /// Gets or sets the self-or-ancestor that displays the active title-bar background.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3034</c>)
    /// </summary>
    public ImGuiWindow? RootWindowForTitleBarHighlight { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first ancestor without the navigation-flattened flag.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3035</c>)
    /// </summary>
    public ImGuiWindow? RootWindowForNav { get; set; }

    /// <summary>
    /// Gets or sets the manual logical parent used for shortcut focus-route evaluation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3036</c>)
    /// </summary>
    public ImGuiWindow? ParentWindowForFocusRoute { get; set; }

    #endregion

    #region Navigation state

    /// <summary>
    /// Gets or sets the child window remembered when the menu bar is entered.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3038</c>)
    /// </summary>
    public ImGuiWindow? NavLastChildNavWindow { get; set; }

    /// <summary>
    /// Gets the last known navigation identifier for each navigation layer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3039</c>)
    /// </summary>
    public uint[] NavLastIds { get; } = new uint[(int)ImGuiNavLayer.COUNT];

    /// <summary>
    /// Gets the reference rectangle for each navigation layer, in window-relative space.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3040</c>)
    /// </summary>
    public ImRect[] NavRectRel { get; } = CreateNavRects();

    /// <summary>
    /// Gets the preferred X/Y scoring position for each navigation layer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3041</c>)
    /// </summary>
    public ImVec2[] NavPreferredScoringPosRel { get; } = CreateNavScoringPositions();

    /// <summary>
    /// Gets or sets the focus scope identifier captured at <c>Begin()</c> time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3042</c>)
    /// </summary>
    public uint NavRootFocusScopeId { get; set; }

    #endregion

    #region Memory management

    /// <summary>
    /// Gets or sets the backup of the last draw-list index count, used to preallocate when the
    /// window wakes. (<c>ThirdParty/imgui/imgui_internal.h:3044</c>)
    /// </summary>
    public int MemoryDrawListIdxCapacity { get; set; }

    /// <summary>
    /// Gets or sets the backup of the last draw-list vertex count, used to preallocate when the
    /// window wakes. (<c>ThirdParty/imgui/imgui_internal.h:3045</c>)
    /// </summary>
    public int MemoryDrawListVtxCapacity { get; set; }

    /// <summary>
    /// Gets or sets whether extraneous data has been garbage-collected.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3046</c>)
    /// </summary>
    public bool MemoryCompacted { get; set; }

    #endregion

    #region Docking

    /// <summary>
    /// Gets or sets whether docking artifacts are visible; when set, the docking node reference is
    /// non-null. (<c>ThirdParty/imgui/imgui_internal.h:3049</c>)
    /// </summary>
    public bool DockIsActive { get; set; }

    /// <summary>
    /// Gets or sets whether the dock node is visible.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3050</c>)
    /// </summary>
    public bool DockNodeIsVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the window is visible this frame because its tab is selected.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3051</c>)
    /// </summary>
    public bool DockTabIsVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the tab requested a close.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3052</c>)
    /// </summary>
    public bool DockTabWantClose { get; set; }

    /// <summary>
    /// Gets or sets the order within the dock node on reappearance.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3053</c>)
    /// </summary>
    public short DockOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the window-style data preserved for docking.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3054</c>)
    /// </summary>
    public ImGuiWindowDockStyle DockStyle { get; set; } = new();

    /// <summary>
    /// Gets or sets which dock node this window is docked into.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3055</c>)
    /// </summary>
    public ImGuiDockNode? DockNode { get; set; }

    /// <summary>
    /// Gets or sets which dock node this window is hosting as a parent window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3056</c>)
    /// </summary>
    public ImGuiDockNode? DockNodeAsHost { get; set; }

    /// <summary>
    /// Gets or sets the backup of the last valid dock node identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3057</c>)
    /// </summary>
    public uint DockId { get; set; }

    #endregion

    /// <summary>
    /// Initializes a window for a context and name. Canonical additionally hashes the name, seeds
    /// <see cref="IDStack"/> with the window identifier, and computes the move and tab identifiers;
    /// this port leaves those steps to the windowing pipeline so that identifier hashing remains
    /// defined in a single place.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3060</c>)
    /// </summary>
    /// <param name="context">The owning context.</param>
    /// <param name="name">The window name.</param>
    public ImGuiWindow(ImGuiContext context, string name)
    {
        Ctx = context;
        Name = name;
        DrawListInst = new ImDrawList();
        DrawList = DrawListInst;
    }

    /// <summary>
    /// Returns the window rectangle from its position and size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3070</c>)
    /// </summary>
    /// <returns>The window rectangle.</returns>
    public ImRect Rect()
    {
        return new ImRect(Pos.X, Pos.Y, Pos.X + Size.X, Pos.Y + Size.Y);
    }

    /// <summary>
    /// Returns the title-bar rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3071</c>)
    /// </summary>
    /// <returns>The title-bar rectangle.</returns>
    public ImRect TitleBarRect()
    {
        return new ImRect(Pos.X, Pos.Y, Pos.X + SizeFull.X, Pos.Y + TitleBarHeight);
    }

    /// <summary>
    /// Returns the menu-bar rectangle, directly below the title bar.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3072</c>)
    /// </summary>
    /// <returns>The menu-bar rectangle.</returns>
    public ImRect MenuBarRect()
    {
        float y1 = Pos.Y + TitleBarHeight;
        return new ImRect(Pos.X, y1, Pos.X + SizeFull.X, y1 + MenuBarHeight);
    }

    /// <summary>
    /// Creates the per-layer navigation reference rectangles.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3040</c>)
    /// </summary>
    /// <returns>The rectangle array.</returns>
    private static ImRect[] CreateNavRects()
    {
        var rects = new ImRect[(int)ImGuiNavLayer.COUNT];
        for (int n = 0; n < rects.Length; n++)
        {
            rects[n] = new ImRect();
        }

        return rects;
    }

    /// <summary>
    /// Creates the per-layer preferred scoring positions, initialized to the maximum float value.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3041</c>)
    /// </summary>
    /// <returns>The scoring-position array.</returns>
    private static ImVec2[] CreateNavScoringPositions()
    {
        var positions = new ImVec2[(int)ImGuiNavLayer.COUNT];
        for (int n = 0; n < positions.Length; n++)
        {
            positions[n] = new ImVec2(float.MaxValue, float.MaxValue);
        }

        return positions;
    }

    /// <summary>
    /// Computes an identifier from a string seeded by this window's current ID stack top.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3060</c>)
    /// </summary>
    /// <param name="str">The string to hash.</param>
    /// <returns>Hashed identifier.</returns>
    public uint GetID(string str)
    {
        return ImGui.GetIDWithSeed(str, null, IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from an integer seeded by this window's current ID stack top.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3062</c>)
    /// </summary>
    /// <param name="n">The integer value to hash.</param>
    /// <returns>Hashed identifier.</returns>
    public uint GetID(int n)
    {
        return ImGui.GetIDWithSeed(n, IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from an object reference seeded by this window's current ID stack top.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3061</c>)
    /// </summary>
    /// <param name="ptr">The object reference to hash.</param>
    /// <returns>Hashed identifier.</returns>
    public uint GetID(object? ptr)
    {
        uint id = ImGui.HashPointerId(ptr, IDStack[^1]);
        if (ImGui.GetCurrentContext()!.DebugHookIdInfoId == id)
        {
            // Canonical reports the raw pointer. Managed objects have no stable address, so the port reports
            // the identity hash its identifier derivation is already based on, which is the same value the
            // path is keyed by and is stable for the object's lifetime.
            ImGui.DebugHookIdInfo(
                id,
                (int)ImGuiDataTypePrivate.ImGuiDataType_Pointer,
                ptr != null ? new IntPtr(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(ptr)) : IntPtr.Zero,
                IntPtr.Zero);
        }
        return id;
    }
}

#endregion

#region ImGuiContext

/// <summary>
/// The main UI context: it owns the input state, style, window lists, shared stacks, navigation
/// state, settings, hooks, and debug tool state for one independent Dear ImGui instance
/// (<c>ThirdParty/imgui/imgui_internal.h:2444-2859</c>).
/// </summary>
/// <remarks>
/// <para>
/// The context is created by <c>CreateContext()</c> and destroyed by <c>DestroyContext()</c>; the
/// frame-submitting thread is the only writer. Fields are grouped below using the canonical inline
/// section comments.
/// </para>
/// <para>
/// Members typed with structures owned by later chunks (docking, tables, tab bars, multi-select,
/// metrics, settings handlers, platform IO, box-select, typing-select, and debug allocation) are
/// intentionally absent; the chunk report enumerates every omitted member with its canonical line
/// and owning chunk.
/// </para>
/// </remarks>
public sealed class ImGuiContext
{
    #region Core context state

    /// <summary>
    /// Gets or sets whether context initialization completed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2446</c>)
    /// </summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// Gets or sets whether <c>NewFrame()</c> was called and <c>EndFrame()</c> has not yet run.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2447</c>)
    /// </summary>
    public bool WithinFrameScope { get; set; }

    /// <summary>
    /// Gets or sets whether the implicit debug window has been pushed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2448</c>)
    /// </summary>
    public bool WithinFrameScopeWithImplicitWindow { get; set; }

    /// <summary>
    /// Gets or sets whether test-engine item hooks are invoked.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2449</c>)
    /// </summary>
    public bool TestEngineHookItems { get; set; }

    /// <summary>
    /// Gets or sets the global frame counter.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2450</c>)
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the frame count captured at <c>EndFrame()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2451</c>)
    /// </summary>
    public int FrameCountEnded { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame count captured when the platform finished the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2452</c>)
    /// </summary>
    public int FrameCountPlatformEnded { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame count captured at <c>Render()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2453</c>)
    /// </summary>
    public int FrameCountRendered { get; set; } = -1;

    /// <summary>
    /// Gets or sets the global time in seconds.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2454</c>)
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// Gets or sets the context name used for multi-context debugging.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2455</c>)
    /// </summary>
    public string ContextName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the main input and output state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2456</c>)
    /// </summary>
    public ImGuiIO IO { get; set; } = new();

    /// <summary>
    /// Gets or sets the platform and renderer interface for this context.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2457</c>)
    /// </summary>
    public ImGuiPlatformIO PlatformIO { get; set; } = new();

    /// <summary>
    /// Gets or sets the style.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2458</c>)
    /// </summary>
    public ImGuiStyle Style { get; set; } = new();

    /// <summary>
    /// Gets or sets the configuration flags observed at <c>NewFrame()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2459</c>)
    /// </summary>
    public ImGuiConfigFlags ConfigFlagsCurrFrame { get; set; }

    /// <summary>
    /// Gets or sets the configuration flags observed in the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2460</c>)
    /// </summary>
    public ImGuiConfigFlags ConfigFlagsLastFrame { get; set; }

    /// <summary>
    /// Gets the font atlases used by the context.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2461</c>)
    /// </summary>
    public List<ImFontAtlas> FontAtlases { get; } = new();

    /// <summary>
    /// Gets or sets the currently bound font.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2462</c>)
    /// </summary>
    public ImFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the currently bound baked font at the bound size.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2463</c>)
    /// </summary>
    public ImFontBaked? FontBaked { get; set; }

    /// <summary>
    /// Gets or sets the bound font size and line height.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2464</c>)
    /// </summary>
    public float FontSize { get; set; }

    /// <summary>
    /// Gets or sets the font size before scaling.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2465</c>)
    /// </summary>
    public float FontSizeBase { get; set; }

    /// <summary>
    /// Gets or sets the ratio of the baked font size to <see cref="FontSize"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2466</c>)
    /// </summary>
    public float FontBakedScale { get; set; }

    /// <summary>
    /// Gets or sets the current rasterizer density used when acquiring a baked font.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2467</c>)
    /// </summary>
    public float FontRasterizerDensity { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the current pixel density used for font rasterization and display scaling.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2538</c>)
    /// </summary>
    public float CurrentPixelDensity
    {
        get => FontRasterizerDensity;
        set => FontRasterizerDensity = value;
    }

    /// <summary>
    /// Gets or sets the label used to represent mixed / indeterminate values. Defaults to "Mixed".
    /// (<c>ThirdParty/imgui/imgui_internal.h:2539</c>)
    /// </summary>
    public string MixedValueLabel { get; set; } = "Mixed";

    /// <summary>
    /// Gets or sets the DPI scale of the current window or viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2468</c>)
    /// </summary>
    public float CurrentDpiScale { get; set; }

    /// <summary>
    /// Gets or sets the shared draw-list state returned by the public accessor.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2469</c>)
    /// </summary>
    public ImDrawListSharedData DrawListSharedData { get; set; } = new();

    /// <summary>
    /// Gets or sets the child identifier currently being ended.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2470</c>)
    /// </summary>
    public uint WithinEndChildID { get; set; }

    /// <summary>
    /// Gets or sets the popup identifier currently being ended.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2471</c>)
    /// </summary>
    public uint WithinEndPopupID { get; set; }

    /// <summary>
    /// Gets or sets the opaque test-engine user data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2472</c>)
    /// </summary>
    public IntPtr TestEngine { get; set; }

    #endregion

    #region Inputs

    /// <summary>
    /// Gets the queued input events waiting to be trickled into the input state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2475</c>)
    /// </summary>
    public List<ImGuiInputEvent> InputEventsQueue { get; } = new();

    /// <summary>
    /// Gets the history of input events processed during the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2476</c>)
    /// </summary>
    public List<ImGuiInputEvent> InputEventsTrail { get; } = new();

    /// <summary>
    /// Gets or sets the mouse source assumed for the next mouse event.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2477</c>)
    /// </summary>
    public ImGuiMouseSource InputEventsNextMouseSource { get; set; } = ImGuiMouseSource.Mouse;

    /// <summary>
    /// Gets or sets the next sequential input-event identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2478</c>)
    /// </summary>
    public uint InputEventsNextEventId { get; set; } = 1;

    #endregion

    #region Windows state

    /// <summary>
    /// Gets the windows sorted in display order, from back to front.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2481</c>)
    /// </summary>
    public List<ImGuiWindow> Windows { get; } = new();

    /// <summary>
    /// Gets the root windows sorted in focus order, from back to front.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2482</c>)
    /// </summary>
    public List<ImGuiWindow> WindowsFocusOrder { get; } = new();

    /// <summary>
    /// Gets the temporary sort buffer used at <c>EndFrame()</c> to order parents before children.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2483</c>)
    /// </summary>
    public List<ImGuiWindow> WindowsTempSortBuffer { get; } = new();

    /// <summary>
    /// Gets the stack of pushed windows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2484</c>)
    /// </summary>
    public List<ImGuiWindowStackData> CurrentWindowStack { get; } = new();

    /// <summary>
    /// Gets the map from window identifier to <see cref="ImGuiWindow"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2485</c>)
    /// </summary>
    public Dictionary<uint, ImGuiWindow> WindowsById { get; } = new();

    /// <summary>
    /// Gets or sets the number of unique windows submitted this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2486</c>)
    /// </summary>
    public int WindowsActiveCount { get; set; }

    /// <summary>
    /// Gets or sets the hover padding around resizable windows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2487</c>)
    /// </summary>
    public float WindowsBorderHoverPadding { get; set; }

    /// <summary>
    /// Gets or sets the window identifier that triggers a debug break in <c>Begin()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2488</c>)
    /// </summary>
    public uint DebugBreakInWindow { get; set; }

    /// <summary>
    /// Gets or sets the window currently being drawn into.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2489</c>)
    /// </summary>
    public ImGuiWindow? CurrentWindow { get; set; }

    /// <summary>
    /// Gets or sets the window the mouse is hovering.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2490</c>)
    /// </summary>
    public ImGuiWindow? HoveredWindow { get; set; }

    /// <summary>
    /// Gets or sets the hovered window ignoring the moving window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2491</c>)
    /// </summary>
    public ImGuiWindow? HoveredWindowUnderMovingWindow { get; set; }

    /// <summary>
    /// Gets or sets the hovered window filled even when mouse input is disabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2492</c>)
    /// </summary>
    public ImGuiWindow? HoveredWindowBeforeClear { get; set; }

    /// <summary>
    /// Gets or sets the window clicked to preserve focus while being moved.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2493</c>)
    /// </summary>
    public ImGuiWindow? MovingWindow { get; set; }

    /// <summary>
    /// Gets or sets the window on which the current wheel sequence started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2494</c>)
    /// </summary>
    public ImGuiWindow? WheelingWindow { get; set; }

    /// <summary>
    /// Gets or sets the reference mouse position for wheeling.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2495</c>)
    /// </summary>
    public ImVec2 WheelingWindowRefMousePos { get; set; }

    /// <summary>
    /// Gets or sets the frame on which wheeling started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2496</c>)
    /// </summary>
    public int WheelingWindowStartFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame on which wheeling last scrolled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2497</c>)
    /// </summary>
    public int WheelingWindowScrolledFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the timer before the wheeling window is released.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2498</c>)
    /// </summary>
    public float WheelingWindowReleaseTimer { get; set; }

    /// <summary>
    /// Gets or sets the wheel remainder carried to the next window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2499</c>)
    /// </summary>
    public ImVec2 WheelingWindowWheelRemainder { get; set; }

    /// <summary>
    /// Gets or sets the averaged wheel axis.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2500</c>)
    /// </summary>
    public ImVec2 WheelingAxisAvg { get; set; }

    #endregion

    #region Item/widget state and tracking

    /// <summary>
    /// Gets or sets the identifier recorded when duplicate item identifiers are detected.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2503</c>)
    /// </summary>
    public uint DebugDrawIdConflictsId { get; set; }

    /// <summary>
    /// Gets or sets the identifier recorded by the identifier-stack debug hook.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2504</c>)
    /// </summary>
    public uint DebugHookIdInfoId { get; set; }

    /// <summary>
    /// Gets or sets the hovered widget identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2505</c>)
    /// </summary>
    public uint HoveredId { get; set; }

    /// <summary>
    /// Gets or sets the hovered widget identifier from the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2506</c>)
    /// </summary>
    public uint HoveredIdPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the count of items sharing the previous hovered identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2507</c>)
    /// </summary>
    public int HoveredIdPreviousFrameItemCount { get; set; }

    /// <summary>
    /// Gets or sets the contiguous hovering time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2508</c>)
    /// </summary>
    public float HoveredIdTimer { get; set; }

    /// <summary>
    /// Gets or sets the contiguous hover time during which the item was not active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2509</c>)
    /// </summary>
    public float HoveredIdNotActiveTimer { get; set; }

    /// <summary>
    /// Gets or sets whether the hovered item allows overlap.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2510</c>)
    /// </summary>
    public bool HoveredIdAllowOverlap { get; set; }

    /// <summary>
    /// Gets or sets whether a rectangle test passed but was discarded because the item was disabled
    /// or inhibited by a popup. (<c>ThirdParty/imgui/imgui_internal.h:2511</c>)
    /// </summary>
    public bool HoveredIdIsDisabled { get; set; }

    /// <summary>
    /// Gets or sets whether item-add clipping is disabled for logging.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2512</c>)
    /// </summary>
    public bool ItemUnclipByLog { get; set; }

    /// <summary>
    /// Gets or sets whether any identifier was edited this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2513</c>)
    /// </summary>
    public bool AnyIdHasBeenEditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets the active widget identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2514</c>)
    /// </summary>
    public uint ActiveId { get; set; }

    /// <summary>
    /// Gets or sets the active widget identifier seen this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2515</c>)
    /// </summary>
    public uint ActiveIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the active duration.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2516</c>)
    /// </summary>
    public float ActiveIdTimer { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was just activated this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2517</c>)
    /// </summary>
    public bool ActiveIdIsJustActivated { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was selected on activation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2518</c>)
    /// </summary>
    public bool ActiveIdWasSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was the sole selection on activation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2519</c>)
    /// </summary>
    public bool ActiveIdWasSoleSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the active widget allows another widget to steal the active identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2520</c>)
    /// </summary>
    public bool ActiveIdAllowOverlap { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier survives a focus loss.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2521</c>)
    /// </summary>
    public bool ActiveIdNoClearOnFocusLoss { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier previously led to a press.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2522</c>)
    /// </summary>
    public bool ActiveIdHasBeenPressedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited during the active state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2523</c>)
    /// </summary>
    public bool ActiveIdHasBeenEditedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited during the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2524</c>)
    /// </summary>
    public bool ActiveIdHasBeenEditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was activated through a shortcut.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2525</c>)
    /// </summary>
    public bool ActiveIdFromShortcut { get; set; }

    /// <summary>
    /// Gets or sets the mouse button that activated the widget, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2526</c>)
    /// </summary>
    public sbyte ActiveIdMouseButton { get; set; } = -1;

    /// <summary>
    /// Gets or sets the real item identifier when a disabled item was clicked.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2527</c>)
    /// </summary>
    public uint ActiveIdDisabledId { get; set; }

    /// <summary>
    /// Gets or sets the click offset from the item's upper-left corner.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2528</c>)
    /// </summary>
    public ImVec2 ActiveIdClickOffset { get; set; } = new(-1.0f, -1.0f);

    /// <summary>
    /// Gets or sets the input source that activated the widget.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2529</c>)
    /// </summary>
    public ImGuiInputSource ActiveIdSource { get; set; }

    /// <summary>
    /// Gets or sets the window that owns the active identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2530</c>)
    /// </summary>
    public ImGuiWindow? ActiveIdWindow { get; set; }

    /// <summary>
    /// Gets or sets the active identifier from the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2531</c>)
    /// </summary>
    public uint ActiveIdPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the deactivation tracking record.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2532</c>)
    /// </summary>
    public ImGuiDeactivatedItemData DeactivatedItemData { get; set; } = new();

    /// <summary>
    /// Gets or sets the value captured at activation for drag and slider widgets.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2533</c>)
    /// </summary>
    public ImGuiDataTypeStorage ActiveIdValueOnActivation { get; set; } = new();

    /// <summary>
    /// Gets or sets the last nonzero active identifier, used for animation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2534</c>)
    /// </summary>
    public uint LastActiveId { get; set; }

    /// <summary>
    /// Gets or sets the time since the last activation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2535</c>)
    /// </summary>
    public float LastActiveIdTimer { get; set; }

    /// <summary>
    /// Gets or sets whether the last active identifier was selected.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2536</c>)
    /// </summary>
    public bool LastActiveIdWasSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the last active identifier was the sole selection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2537</c>)
    /// </summary>
    public bool LastActiveIdWasSoleSelected { get; set; }

    #endregion

    #region Key ownership and shortcut routing

    /// <summary>
    /// Gets or sets the last time the key modifiers changed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2543</c>)
    /// </summary>
    public double LastKeyModsChangeTime { get; set; } = -1.0;

    /// <summary>
    /// Gets or sets the last time the key modifiers changed away from none.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2544</c>)
    /// </summary>
    public double LastKeyModsChangeFromNoneTime { get; set; } = -1.0;

    /// <summary>
    /// Gets or sets the last time a keyboard key, as opposed to a mouse or gamepad key, was pressed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2545</c>)
    /// </summary>
    public double LastKeyboardKeyPressTime { get; set; } = -1.0;

    /// <summary>
    /// Gets or sets the lookup for keys that may emit character input.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2546</c>)
    /// </summary>
    public ImBitArrayForNamedKeys KeysMayBeCharInput { get; set; } = new();

    /// <summary>
    /// Gets the per-key ownership data, one entry per named key.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2547</c>)
    /// </summary>
    public ImGuiKeyOwnerData[] KeysOwnerData { get; } = CreateKeysOwnerData();

    /// <summary>
    /// Gets or sets the chord routing table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2548</c>)
    /// </summary>
    public ImGuiKeyRoutingTable KeysRoutingTable { get; set; } = new();

    /// <summary>
    /// Gets or sets the mask of navigation directions the active widget wants.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2549</c>)
    /// </summary>
    public uint ActiveIdUsingNavDirMask { get; set; }

    /// <summary>
    /// Gets or sets whether the active widget consumes every keyboard key.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2550</c>)
    /// </summary>
    public bool ActiveIdUsingAllKeyboardKeys { get; set; }

    /// <summary>
    /// Gets or sets the shortcut chord that triggers a debug break in shortcut routing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2551</c>)
    /// </summary>
    public ImGuiKey DebugBreakInShortcutRouting { get; set; }

    #endregion

    #region Next window/item data

    /// <summary>
    /// Gets or sets the focus scope of the items currently being appended.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2555</c>)
    /// </summary>
    public uint CurrentFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the item flags of the items currently being appended.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2556</c>)
    /// </summary>
    public ImGuiItemFlags CurrentItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the identifier targeted by the locate-item debug tool.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2557</c>)
    /// </summary>
    public uint DebugLocateId { get; set; }

    /// <summary>
    /// Gets or sets the queued <c>SetNextItem**</c> values.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2558</c>)
    /// </summary>
    public ImGuiNextItemData NextItemData { get; set; } = new();

    /// <summary>
    /// Gets or sets the status of the last submitted item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2559</c>)
    /// </summary>
    public ImGuiLastItemData LastItemData { get; set; } = new();

    /// <summary>
    /// Gets or sets the queued <c>SetNextWindow**</c> values.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2560</c>)
    /// </summary>
    public ImGuiNextWindowData NextWindowData { get; set; } = new();

    /// <summary>
    /// Gets or sets whether debug group rectangles are drawn.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2561</c>)
    /// </summary>
    public bool DebugShowGroupRects { get; set; }

    /// <summary>
    /// Gets or sets a request for a full garbage collection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2562</c>)
    /// </summary>
    public bool GcCompactAll { get; set; }

    #endregion

    #region Shared stacks

    /// <summary>
    /// Gets or sets the debug flash color index, kept adjacent to the color stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2565</c>)
    /// </summary>
    public ImGuiCol DebugFlashStyleColorIdx { get; set; } = ImGuiCol.COUNT;

    /// <summary>
    /// Gets the color stack pushed by <c>PushStyleColor()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2566</c>)
    /// </summary>
    public List<ImGuiColorMod> ColorStack { get; } = new();

    /// <summary>
    /// Gets the style-variable stack pushed by <c>PushStyleVar()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2567</c>)
    /// </summary>
    public List<ImGuiStyleMod> StyleVarStack { get; } = new();

    /// <summary>
    /// Gets the font stack pushed by <c>PushFont()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2568</c>)
    /// </summary>
    public List<ImFontStackData> FontStack { get; } = new();

    /// <summary>
    /// Gets the focus-scope stack pushed by <c>PushFocusScope()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2569</c>)
    /// </summary>
    public List<ImGuiFocusScopeData> FocusScopeStack { get; } = new();

    /// <summary>
    /// Gets the item-flags stack pushed by <c>PushItemFlag()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2570</c>)
    /// </summary>
    public List<ImGuiItemFlags> ItemFlagsStack { get; } = new();

    /// <summary>
    /// Gets the group stack pushed by <c>BeginGroup()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2571</c>)
    /// </summary>
    public List<ImGuiGroupData> GroupStack { get; } = new();

    /// <summary>
    /// Gets the persistent open-popup stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2572</c>)
    /// </summary>
    public List<ImGuiPopupData> OpenPopupStack { get; } = new();

    /// <summary>
    /// Gets the current begin-popup nesting stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2573</c>)
    /// </summary>
    public List<ImGuiPopupData> BeginPopupStack { get; } = new();

    /// <summary>
    /// Gets the tree-node stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2574</c>)
    /// </summary>
    public List<ImGuiTreeNodeStackData> TreeNodeStack { get; } = new();

    #endregion

    #region Viewports

    /// <summary>
    /// Gets the active viewports; there is always at least one.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2577</c>)
    /// </summary>
    public List<ImGuiViewportP> Viewports { get; } = new();

    /// <summary>
    /// Gets or sets the current viewport.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2578</c>)
    /// </summary>
    public ImGuiViewportP? CurrentViewport { get; set; }

    /// <summary>
    /// Gets or sets the viewport under the mouse.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2579</c>)
    /// </summary>
    public ImGuiViewportP? MouseViewport { get; set; }

    /// <summary>
    /// Gets or sets the last viewport hovered by the mouse.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2580</c>)
    /// </summary>
    public ImGuiViewportP? MouseLastHoveredViewport { get; set; }

    /// <summary>
    /// Gets or sets the last focused platform viewport identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2581</c>)
    /// </summary>
    public uint PlatformLastFocusedViewportId { get; set; }

    /// <summary>
    /// Gets or sets the fallback virtual monitor.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2582</c>)
    /// </summary>
    public ImGuiPlatformMonitor FallbackMonitor { get; set; } = new();

    /// <summary>
    /// Gets or sets the bounding box of all platform monitors.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2583</c>)
    /// </summary>
    public ImRect PlatformMonitorsFullWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the sequential viewport creation counter.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2584</c>)
    /// </summary>
    public int ViewportCreatedCount { get; set; }

    /// <summary>
    /// Gets or sets the sequential platform-window creation counter.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2585</c>)
    /// </summary>
    public int PlatformWindowsCreatedCount { get; set; }

    /// <summary>
    /// Gets or sets the stamp counter that tracks when the front-most window changes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2586</c>)
    /// </summary>
    public int ViewportFocusedStampCount { get; set; }

    #endregion

    #region Keyboard/Gamepad navigation

    /// <summary>
    /// Gets or sets whether the navigation cursor rectangle is visible.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2589</c>)
    /// </summary>
    public bool NavCursorVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the navigation-focused item is highlighted instead of the mouse-hovered
    /// item. (<c>ThirdParty/imgui/imgui_internal.h:2590</c>)
    /// </summary>
    public bool NavHighlightItemUnderNav { get; set; }

    /// <summary>
    /// Gets or sets whether the mouse position must be updated by navigation movement.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2593</c>)
    /// </summary>
    public bool NavMousePosDirty { get; set; }

    /// <summary>
    /// Gets or sets whether the navigation widget was seen this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2594</c>)
    /// </summary>
    public bool NavIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the focused item identifier for navigation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2595</c>)
    /// </summary>
    public uint NavId { get; set; }

    /// <summary>
    /// Gets or sets the focused window for navigation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2596</c>)
    /// </summary>
    public ImGuiWindow? NavWindow { get; set; }

    /// <summary>
    /// Gets or sets the focused focus scope.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2597</c>)
    /// </summary>
    public uint NavFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the focused navigation layer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2598</c>)
    /// </summary>
    public ImGuiNavLayer NavLayer { get; set; } = ImGuiNavLayer.Main;

    /// <summary>
    /// Gets or sets the item flags of the focused navigation item.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2599</c>)
    /// </summary>
    public ImGuiItemFlags NavIdItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the pending navigation activation identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2600</c>)
    /// </summary>
    public uint NavActivateId { get; set; }

    /// <summary>
    /// Gets or sets the identifier whose navigation activation key is held down.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2601</c>)
    /// </summary>
    public uint NavActivateDownId { get; set; }

    /// <summary>
    /// Gets or sets the identifier whose navigation activation key was pressed without repeat.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2602</c>)
    /// </summary>
    public uint NavActivatePressedId { get; set; }

    /// <summary>
    /// Gets or sets the flags for the pending activation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2603</c>)
    /// </summary>
    public ImGuiActivateFlags NavActivateFlags { get; set; }

    /// <summary>
    /// Gets the reversed focus-scope stack for the current navigation identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2604</c>)
    /// </summary>
    public List<ImGuiFocusScopeData> NavFocusRoute { get; } = new();

    /// <summary>
    /// Gets or sets the identifier highlighted after a navigation activation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2605</c>)
    /// </summary>
    public uint NavHighlightActivatedId { get; set; }

    /// <summary>
    /// Gets or sets the timer for the activation highlight.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2606</c>)
    /// </summary>
    public float NavHighlightActivatedTimer { get; set; }

    /// <summary>
    /// Gets or sets the item identifier for the pending open-context-menu action.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2607</c>)
    /// </summary>
    public uint NavOpenContextMenuItemId { get; set; }

    /// <summary>
    /// Gets or sets the window identifier for the pending context menu.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2608</c>)
    /// </summary>
    public uint NavOpenContextMenuWindowId { get; set; }

    /// <summary>
    /// Gets or sets the activation queued until the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2609</c>)
    /// </summary>
    public uint NavNextActivateId { get; set; }

    /// <summary>
    /// Gets or sets the flags for the queued activation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2610</c>)
    /// </summary>
    public ImGuiActivateFlags NavNextActivateFlags { get; set; }

    /// <summary>
    /// Gets or sets the input source that drives navigation (keyboard or gamepad).
    /// (<c>ThirdParty/imgui/imgui_internal.h:2611</c>)
    /// </summary>
    public ImGuiInputSource NavInputSource { get; set; } = ImGuiInputSource.Keyboard;

    /// <summary>
    /// Gets or sets the last valid selection user data for the current window; -1 means invalid.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2612</c>)
    /// </summary>
    public long NavLastValidSelectionUserData { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of frames during which the navigation cursor stays hidden.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2613</c>)
    /// </summary>
    public sbyte NavCursorHideFrames { get; set; }

    #endregion

    #region Navigation: init and move requests

    /// <summary>
    /// Gets or sets whether any navigation request is pending.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2617</c>)
    /// </summary>
    public bool NavAnyRequest { get; set; }

    /// <summary>
    /// Gets or sets whether an initialization request is pending for an appearing window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2618</c>)
    /// </summary>
    public bool NavInitRequest { get; set; }

    /// <summary>
    /// Gets or sets whether the initialization request originated from a move.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2619</c>)
    /// </summary>
    public bool NavInitRequestFromMove { get; set; }

    /// <summary>
    /// Gets or sets the initialization request result.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2620</c>)
    /// </summary>
    public ImGuiNavItemData NavInitResult { get; set; } = new();

    /// <summary>
    /// Gets or sets whether a move request was submitted and is processed on the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2621</c>)
    /// </summary>
    public bool NavMoveSubmitted { get; set; }

    /// <summary>
    /// Gets or sets whether the move request is still scoring items.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2622</c>)
    /// </summary>
    public bool NavMoveScoringItems { get; set; }

    /// <summary>
    /// Gets or sets whether the move request is forwarded to the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2623</c>)
    /// </summary>
    public bool NavMoveForwardToNextFrame { get; set; }

    /// <summary>
    /// Gets or sets the move request flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2624</c>)
    /// </summary>
    public ImGuiNavMoveFlags NavMoveFlags { get; set; }

    /// <summary>
    /// Gets or sets the scroll flags for the move.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2625</c>)
    /// </summary>
    public ImGuiScrollFlags NavMoveScrollFlags { get; set; }

    /// <summary>
    /// Gets or sets the key modifiers of the move request.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2626</c>)
    /// </summary>
    public ImGuiKey NavMoveKeyMods { get; set; }

    /// <summary>
    /// Gets or sets the direction of the move request.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2627</c>)
    /// </summary>
    public ImGuiDir NavMoveDir { get; set; }

    /// <summary>
    /// Gets or sets the move direction used for debug display.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2628</c>)
    /// </summary>
    public ImGuiDir NavMoveDirForDebug { get; set; }

    /// <summary>
    /// Gets or sets the clipping direction of the move.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2629</c>)
    /// </summary>
    public ImGuiDir NavMoveClipDir { get; set; }

    /// <summary>
    /// Gets or sets the scoring rectangle in screen space.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2630</c>)
    /// </summary>
    public ImRect NavScoringRect { get; set; }

    /// <summary>
    /// Gets or sets the region the clipper must keep submitted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2631</c>)
    /// </summary>
    public ImRect NavScoringNoClipRect { get; set; }

    /// <summary>
    /// Gets or sets the debug scoring counter.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2632</c>)
    /// </summary>
    public int NavScoringDebugCount { get; set; }

    /// <summary>
    /// Gets or sets the tabbing direction, where negative is backward and positive is forward.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2633</c>)
    /// </summary>
    public int NavTabbingDir { get; set; }

    /// <summary>
    /// Gets or sets the tabbing item counter.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2634</c>)
    /// </summary>
    public int NavTabbingCounter { get; set; }

    /// <summary>
    /// Gets or sets the best move candidate within the navigation window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2635</c>)
    /// </summary>
    public ImGuiNavItemData NavMoveResultLocal { get; set; } = new();

    /// <summary>
    /// Gets or sets the best mostly-visible move candidate.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2636</c>)
    /// </summary>
    public ImGuiNavItemData NavMoveResultLocalVisible { get; set; } = new();

    /// <summary>
    /// Gets or sets the best move candidate in the flattened hierarchy.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2637</c>)
    /// </summary>
    public ImGuiNavItemData NavMoveResultOther { get; set; } = new();

    /// <summary>
    /// Gets or sets the first tabbing candidate.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2638</c>)
    /// </summary>
    public ImGuiNavItemData NavTabbingResultFirst { get; set; } = new();

    #endregion

    #region Navigation: record of last move request

    /// <summary>
    /// Gets or sets the focus scope moved from.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2641</c>)
    /// </summary>
    public uint NavJustMovedFromFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the identifier moved to.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2642</c>)
    /// </summary>
    public uint NavJustMovedToId { get; set; }

    /// <summary>
    /// Gets or sets the focus scope moved to.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2643</c>)
    /// </summary>
    public uint NavJustMovedToFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the key modifiers of the last move.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2644</c>)
    /// </summary>
    public ImGuiKey NavJustMovedToKeyMods { get; set; }

    /// <summary>
    /// Gets or sets whether the last move was a tabbing move.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2645</c>)
    /// </summary>
    public bool NavJustMovedToIsTabbing { get; set; }

    /// <summary>
    /// Gets or sets whether the last move result carried selection data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2646</c>)
    /// </summary>
    public bool NavJustMovedToHasSelectionData { get; set; }

    /// <summary>
    /// Gets or sets whether navigation just moved into an initialization state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2713</c>)
    /// </summary>
    public bool NavJustMovedToIsInit { get; set; }

    #endregion

    #region Navigation: extra config options

    /// <summary>
    /// Gets or sets whether Tab and Shift+Tab tabbing is enabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2651</c>)
    /// </summary>
    public bool ConfigNavEnableTabbing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the gamepad menu button may drive windowing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2652</c>)
    /// </summary>
    public bool ConfigNavWindowingWithGamepad { get; set; } = true;

    /// <summary>
    /// Gets or sets the next-window shortcut chord.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2653</c>)
    /// </summary>
    public ImGuiKey ConfigNavWindowingKeyNext { get; set; }

    /// <summary>
    /// Gets or sets the previous-window shortcut chord.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2654</c>)
    /// </summary>
    public ImGuiKey ConfigNavWindowingKeyPrev { get; set; }

    #endregion

    #region Navigation: windowing

    /// <summary>
    /// Gets or sets the target window during Ctrl+Tab windowing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2657</c>)
    /// </summary>
    public ImGuiWindow? NavWindowingTarget { get; set; }

    /// <summary>
    /// Gets or sets the last valid windowing target, retained for the fade-out animation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2658</c>)
    /// </summary>
    public ImGuiWindow? NavWindowingTargetAnim { get; set; }

    /// <summary>
    /// Gets or sets the internal Ctrl+Tab list window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2659</c>)
    /// </summary>
    public ImGuiWindow? NavWindowingListWindow { get; set; }

    /// <summary>
    /// Gets or sets the windowing animation timer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2660</c>)
    /// </summary>
    public float NavWindowingTimer { get; set; }

    /// <summary>
    /// Gets or sets the windowing highlight alpha.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2661</c>)
    /// </summary>
    public float NavWindowingHighlightAlpha { get; set; }

    /// <summary>
    /// Gets or sets the input source that drives windowing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2662</c>)
    /// </summary>
    public ImGuiInputSource NavWindowingInputSource { get; set; }

    /// <summary>
    /// Gets or sets whether the menu layer toggle is held.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2663</c>)
    /// </summary>
    public bool NavWindowingToggleLayer { get; set; }

    /// <summary>
    /// Gets or sets the key used for the windowing layer toggle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2664</c>)
    /// </summary>
    public ImGuiKey NavWindowingToggleKey { get; set; }

    /// <summary>
    /// Gets or sets the accumulated windowing move delta.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2665</c>)
    /// </summary>
    public ImVec2 NavWindowingAccumDeltaPos { get; set; }

    /// <summary>
    /// Gets or sets the accumulated windowing resize delta.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2666</c>)
    /// </summary>
    public ImVec2 NavWindowingAccumDeltaSize { get; set; }

    #endregion

    #region Render

    /// <summary>
    /// Gets or sets the fade ratio, in the range 0..1, for the dimming background.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2669</c>)
    /// </summary>
    public float DimBgRatio { get; set; }

    #endregion

    #region Drag and drop

    /// <summary>
    /// Gets or sets whether a drag-and-drop operation is active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2672</c>)
    /// </summary>
    public bool DragDropActive { get; set; }

    /// <summary>
    /// Gets or sets whether execution is inside a drag-source block.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2673</c>)
    /// </summary>
    public bool DragDropWithinSource { get; set; }

    /// <summary>
    /// Gets or sets whether execution is inside a drag-target block.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2674</c>)
    /// </summary>
    public bool DragDropWithinTarget { get; set; }

    /// <summary>
    /// Gets or sets the drag-source flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2675</c>)
    /// </summary>
    public ImGuiDragDropFlags DragDropSourceFlags { get; set; }

    /// <summary>
    /// Gets or sets the frame on which the drag started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2676</c>)
    /// </summary>
    public int DragDropSourceFrameCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the mouse button that started the drag, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2677</c>)
    /// </summary>
    public int DragDropMouseButton { get; set; } = -1;

    /// <summary>
    /// Gets or sets the current drag payload.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2678</c>)
    /// </summary>
    public ImGuiPayload DragDropPayload { get; set; } = new();

    /// <summary>
    /// Gets or sets the rectangle of the current target candidate.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2679</c>)
    /// </summary>
    public ImRect DragDropTargetRect { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle at item draw time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2680</c>)
    /// </summary>
    public ImRect DragDropTargetClipRect { get; set; }

    /// <summary>
    /// Gets or sets the current target identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2681</c>)
    /// </summary>
    public uint DragDropTargetId { get; set; }

    /// <summary>
    /// Gets or sets the viewport-wide target identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2682</c>)
    /// </summary>
    public uint DragDropTargetFullViewport { get; set; }

    /// <summary>
    /// Gets or sets the accept flags for the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2683</c>)
    /// </summary>
    public ImGuiDragDropFlags DragDropAcceptFlagsCurr { get; set; }

    /// <summary>
    /// Gets or sets the accept flags from the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2684</c>)
    /// </summary>
    public ImGuiDragDropFlags DragDropAcceptFlagsPrev { get; set; }

    /// <summary>
    /// Gets or sets the target item surface used for overlap resolution.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2685</c>)
    /// </summary>
    public float DragDropAcceptIdCurrRectSurface { get; set; }

    /// <summary>
    /// Gets or sets the target identifier at accept time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2686</c>)
    /// </summary>
    public uint DragDropAcceptIdCurr { get; set; }

    /// <summary>
    /// Gets or sets the target identifier from the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2687</c>)
    /// </summary>
    public uint DragDropAcceptIdPrev { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which a target accepted the source.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2688</c>)
    /// </summary>
    public int DragDropAcceptFrameCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the identifier that caused a button press while holding a payload.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2689</c>)
    /// </summary>
    public uint DragDropHoldJustPressedId { get; set; }

    /// <summary>
    /// Gets the heap payload buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2690</c>)
    /// </summary>
    public List<byte> DragDropPayloadBufHeap { get; } = new();

    /// <summary>
    /// Gets the small inline payload buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2691</c>)
    /// </summary>
    public byte[] DragDropPayloadBufLocal { get; } = new byte[16];

    #endregion

    #region Clipper

    /// <summary>
    /// Gets or sets the count of stacked clipper temporary-data entries.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2694</c>)
    /// </summary>
    public int ClipperTempDataStacked { get; set; }

    /// <summary>
    /// Gets the pool of clipper temporary-data entries, one per nesting level of active clippers.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2695</c>)
    /// </summary>
    /// <remarks>
    /// <c>Begin()</c> grows the pool and reuses existing entries across clippers and frames; <c>End()</c>
    /// only decrements <see cref="ClipperTempDataStacked"/>. The pool is emptied during
    /// <see cref="ImGui.Shutdown"/>.
    /// </remarks>
    public List<ImGuiListClipperData> ClipperTempData { get; } = new();

    #endregion

    #region Tables

    /// <summary>
    /// Gets or sets the active innermost table, or <see langword="null"/> when no table is being
    /// submitted. (<c>ThirdParty/imgui/imgui_internal.h:2698</c>)
    /// </summary>
    /// <remarks>
    /// <c>BeginTableEx()</c> assigns the innermost table and <c>EndTable()</c> restores the parent
    /// table from the stacked temporary data, so this member always names the table that owns the
    /// current column cursor.
    /// </remarks>
    public ImGuiTable? CurrentTable { get; set; }

    /// <summary>
    /// Gets or sets the table identifier that triggers a debug break in <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2699</c>)
    /// </summary>
    public uint DebugBreakInTable { get; set; }

    /// <summary>
    /// Gets or sets the count of temporary table-data entries currently stacked.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2700</c>)
    /// </summary>
    public int TablesTempDataStacked { get; set; }

    /// <summary>
    /// Gets the pool of temporary table-data entries, one per nesting level of stacked tables.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2701</c>)
    /// </summary>
    /// <remarks>
    /// Canonical stores the pool in an <c>ImVector&lt;ImGuiTableTempData&gt;</c> that grows but is
    /// never shrunk during a frame; this port uses a list with the same append-and-index discipline.
    /// </remarks>
    public List<ImGuiTableTempData> TablesTempData { get; } = new();

    /// <summary>
    /// Gets the persistent table pool, addressed by the index stored in each window's
    /// <see cref="ImGuiWindowTempData.CurrentTableIdx"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2702</c>)
    /// </summary>
    /// <remarks>
    /// Canonical stores the tables in an <c>ImPool&lt;ImGuiTable&gt;</c> keyed by table identifier.
    /// This port uses a list keyed by index; table lookup by identifier is a linear scan because
    /// tables are removed only by explicit debug and garbage-collection paths.
    /// </remarks>
    public List<ImGuiTable> Tables { get; } = new();

    /// <summary>
    /// Gets or sets the frame on which each table was last active, stored as a structure of arrays
    /// for garbage collection. (<c>ThirdParty/imgui/imgui_internal.h:2703</c>)
    /// </summary>
    public List<float> TablesLastTimeActive { get; } = new();

    /// <summary>
    /// Gets the temporary draw-channel merge buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2704</c>)
    /// </summary>
    public List<ImDrawChannel> DrawChannelsTempMergeBuffer { get; } = new();

    #endregion

    #region Tab bars

    /// <summary>
    /// Gets or sets the active tab bar being submitted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2708</c>)
    /// </summary>
    public ImGuiTabBar? CurrentTabBar { get; set; }

    /// <summary>
    /// Gets the pool of persistent tab bars.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2709</c>)
    /// </summary>
    public ImPool<ImGuiTabBar> TabBars { get; } = new();

    /// <summary>
    /// Gets the nested tab-bar stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2710</c>)
    /// </summary>
    public List<ImGuiTabBar> CurrentTabBarStack { get; } = new();

    /// <summary>
    /// Gets the width-shrinking scratch buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2710</c>)
    /// </summary>
    public List<ImGuiShrinkWidthItem> ShrinkWidthBuffer { get; } = new();

    #endregion

    #region Multi-Select state

    /// <summary>
    /// Gets the single active box-selection state record.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2713</c>)
    /// </summary>
    /// <remarks>
    /// Only one box-selection can be active at a time, so canonical stores the record directly on the
    /// context instead of in a pool.
    /// </remarks>
    public ImGuiBoxSelectState BoxSelectState { get; } = new();

    /// <summary>
    /// Gets or sets the temporary multi-select data for the scope currently being submitted, or
    /// <c>null</c> when no <c>BeginMultiSelect()</c> scope is open.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2714</c>)
    /// </summary>
    /// <remarks>
    /// The value points into <see cref="MultiSelectTempData"/> at index
    /// <see cref="MultiSelectTempDataStacked"/> minus one, so nested scopes restore the parent on
    /// <c>EndMultiSelect()</c>.
    /// </remarks>
    public ImGuiMultiSelectTempData? CurrentMultiSelect { get; set; }

    /// <summary>
    /// Gets or sets the count of temporary multi-select data entries currently stacked.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2715</c>)
    /// </summary>
    public int MultiSelectTempDataStacked { get; set; }

    /// <summary>
    /// Gets the stacked temporary multi-select data entries.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2716</c>)
    /// </summary>
    /// <remarks>
    /// Entries are retained between frames so that the request buffer of each nesting level keeps its
    /// capacity, mirroring canonical <c>ImGuiMultiSelectTempData::Clear()</c>.
    /// </remarks>
    public List<ImGuiMultiSelectTempData> MultiSelectTempData { get; } = new();

    /// <summary>
    /// Gets the pool of persistent multi-select state records keyed by selection scope identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2717</c>)
    /// </summary>
    public ImPool<ImGuiMultiSelectState> MultiSelectStorage { get; } = new();

    #endregion

    #region Hover delay system

    /// <summary>
    /// Gets or sets the item currently inside the hover delay.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2720</c>)
    /// </summary>
    public uint HoverItemDelayId { get; set; }

    /// <summary>
    /// Gets or sets the hover-delay item from the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2721</c>)
    /// </summary>
    public uint HoverItemDelayIdPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the hover delay timer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2722</c>)
    /// </summary>
    public float HoverItemDelayTimer { get; set; }

    /// <summary>
    /// Gets or sets the grace time before the tooltip hover timer is cleared.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2723</c>)
    /// </summary>
    public float HoverItemDelayClearTimer { get; set; }

    /// <summary>
    /// Gets or sets the item at which the mouse was once stationary.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2724</c>)
    /// </summary>
    public uint HoverItemUnlockedStationaryId { get; set; }

    /// <summary>
    /// Gets or sets the window at which the mouse was once stationary.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2725</c>)
    /// </summary>
    public uint HoverWindowUnlockedStationaryId { get; set; }

    #endregion

    #region Mouse state

    /// <summary>
    /// Gets or sets the current mouse cursor.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2728</c>)
    /// </summary>
    public ImGuiMouseCursor MouseCursor { get; set; } = ImGuiMouseCursor.Arrow;

    /// <summary>
    /// Gets or sets the time the mouse has been stationary.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2729</c>)
    /// </summary>
    public float MouseStationaryTimer { get; set; }

    /// <summary>
    /// Gets or sets the last valid mouse position.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2730</c>)
    /// </summary>
    public ImVec2 MouseLastValidPos { get; set; }

    #endregion

    #region Widget state

    /// <summary>
    /// Gets or sets the active input-text state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2733</c>)
    /// </summary>
    public ImGuiInputTextState InputTextState { get; set; } = new();

    /// <summary>
    /// Gets or sets the temporary line index used with the input-text state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2734</c>)
    /// </summary>
    public ImGuiTextIndex InputTextLineIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the backup of a text state that just deactivated.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2735</c>)
    /// </summary>
    public ImGuiInputTextDeactivatedState InputTextDeactivatedState { get; set; } = new();

    /// <summary>
    /// Gets or sets the backup of the baked password font.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2736</c>)
    /// </summary>
    public ImFontBaked? InputTextPasswordFontBackupBaked { get; set; }

    /// <summary>
    /// Gets or sets the backup of the password font flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2737</c>)
    /// </summary>
    public ImFontFlags InputTextPasswordFontBackupFlags { get; set; }

    /// <summary>
    /// Gets or sets the input-text identifier to reactivate on the next frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2738</c>)
    /// </summary>
    public uint InputTextReactivateId { get; set; }

    /// <summary>
    /// Gets or sets the temporary text-input identifier used for Ctrl+Click sliders.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2739</c>)
    /// </summary>
    public uint TempInputId { get; set; }

    /// <summary>
    /// Gets or sets the zero value used for every data type.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2740</c>)
    /// </summary>
    public ImGuiDataTypeStorage DataTypeZeroValue { get; set; } = new();

    /// <summary>
    /// Gets or sets the current menu nesting depth.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2741</c>)
    /// </summary>
    public int BeginMenuDepth { get; set; }

    /// <summary>
    /// Gets or sets the current combo nesting depth.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2742</c>)
    /// </summary>
    public int BeginComboDepth { get; set; }

    /// <summary>
    /// Gets or sets the current color-edit identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2743</c>)
    /// </summary>
    public uint ColorEditCurrentID { get; set; }

    /// <summary>
    /// Gets or sets the saved color-edit identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2744</c>)
    /// </summary>
    public uint ColorEditSavedID { get; set; }

    /// <summary>
    /// Gets or sets the saved hue used for lossless color round trips.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2745</c>)
    /// </summary>
    public float ColorEditSavedHue { get; set; }

    /// <summary>
    /// Gets or sets the saved saturation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2746</c>)
    /// </summary>
    public float ColorEditSavedSat { get; set; }

    /// <summary>
    /// Gets or sets the saved RGB color with a zero alpha.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2747</c>)
    /// </summary>
    public uint ColorEditSavedColor { get; set; }

    /// <summary>
    /// Gets or sets the reference color captured when the picker opened.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2748</c>)
    /// </summary>
    public ImVec4 ColorPickerRef { get; set; }

    /// <summary>
    /// Gets or sets the combo preview backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2749</c>)
    /// </summary>
    public ImGuiComboPreviewData ComboPreviewData { get; set; } = new();

    /// <summary>
    /// Gets or sets the expected window resize border rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2750</c>)
    /// </summary>
    public ImRect WindowResizeBorderExpectedRect { get; set; }

    /// <summary>
    /// Gets or sets whether relative window resizing is active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2751</c>)
    /// </summary>
    public bool WindowResizeRelativeMode { get; set; }

    /// <summary>
    /// Gets or sets the scrollbar page-seek mode.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2752</c>)
    /// </summary>
    public short ScrollbarSeekMode { get; set; }

    /// <summary>
    /// Gets or sets the offset from the scrollbar click to the grab center.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2753</c>)
    /// </summary>
    public float ScrollbarClickDeltaToGrabCenter { get; set; }

    /// <summary>
    /// Gets or sets the slider grab click offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2754</c>)
    /// </summary>
    public float SliderGrabClickOffset { get; set; }

    /// <summary>
    /// Gets or sets the accumulated slider delta.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2755</c>)
    /// </summary>
    public float SliderCurrentAccum { get; set; }

    /// <summary>
    /// Gets or sets whether the slider accumulator changed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2756</c>)
    /// </summary>
    public bool SliderCurrentAccumDirty { get; set; }

    /// <summary>
    /// Gets or sets whether the drag accumulator changed.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2757</c>)
    /// </summary>
    public bool DragCurrentAccumDirty { get; set; }

    /// <summary>
    /// Gets or sets the high-precision drag accumulator.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2758</c>)
    /// </summary>
    public float DragCurrentAccum { get; set; }

    /// <summary>
    /// Gets or sets the default drag speed ratio used when the requested speed is zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2759</c>)
    /// </summary>
    public float DragSpeedDefaultRatio { get; set; } = 1.0f / 100.0f;

    /// <summary>
    /// Gets or sets the backup of the style alpha used by <c>BeginDisabled()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2760</c>)
    /// </summary>
    public float DisabledAlphaBackup { get; set; }

    /// <summary>
    /// Gets or sets the disabled-stack depth.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2761</c>)
    /// </summary>
    public short DisabledStackSize { get; set; }

    /// <summary>
    /// Gets or sets the tooltip override count.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2762</c>)
    /// </summary>
    public short TooltipOverrideCount { get; set; }

    /// <summary>
    /// Gets or sets the window of the last tooltip submitted this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2763</c>)
    /// </summary>
    public ImGuiWindow? TooltipPreviousWindow { get; set; }

    /// <summary>
    /// Gets the data used by the default clipboard handler.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2764</c>)
    /// </summary>
    public List<byte> ClipboardHandlerData { get; } = new();

    /// <summary>
    /// Gets the menu identifiers rendered at least once this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2765</c>)
    /// </summary>
    public List<uint> MenusIdSubmittedThisFrame { get; } = new();

    /// <summary>
    /// Gets or sets the state consumed and produced by <c>GetTypingSelectRequest()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2766</c>)
    /// </summary>
    public ImGuiTypingSelectState TypingSelectState { get; set; } = new();

    #endregion

    #region Platform support

    /// <summary>
    /// Gets or sets the IME data updated during the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2769</c>)
    /// </summary>
    public ImGuiPlatformImeData PlatformImeData { get; set; } = new();

    /// <summary>
    /// Gets or sets the previous frame IME data, kept deliberately different to force submission.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2770</c>)
    /// </summary>
    public ImGuiPlatformImeData PlatformImeDataPrev { get; set; } = new();

    #endregion

    #region Extensions

    /// <summary>
    /// Gets the user and extension-managed texture records.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2774</c>)
    /// </summary>
    public List<ImTextureData> UserTextures { get; } = new();

    #endregion

    #region Docking

    /// <summary>
    /// Gets the docking subsystem context.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2775</c>)
    /// </summary>
    public ImGuiDockContext DockContext { get; } = new();

    /// <summary>
    /// Gets or sets the custom or default handler for dock node window menus.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2776</c>)
    /// </summary>
    public Action<ImGuiContext, ImGuiDockNode, ImGuiTabBar>? DockNodeWindowMenuHandler { get; set; }

    #endregion

    #region Settings

    /// <summary>
    /// Gets or sets the packed platform session date, when valid.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2779</c>)
    /// </summary>
    public ImGuiPackedDate SessionDate { get; set; }

    /// <summary>
    /// Gets or sets whether layout settings were loaded.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2780</c>)
    /// </summary>
    public bool SettingsLoaded { get; set; }

    /// <summary>
    /// Gets or sets the timer before layout settings are saved.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2781</c>)
    /// </summary>
    public float SettingsDirtyTimer { get; set; }

    /// <summary>
    /// Gets or sets the in-memory layout data.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2782</c>)
    /// </summary>
    public ImGuiTextBuffer SettingsIniData { get; set; } = new();

    /// <summary>
    /// Gets the registered `.ini` section handlers, in registration order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2783</c>)
    /// </summary>
    public List<ImGuiSettingsHandler> SettingsHandlers { get; } = new();

    /// <summary>
    /// Gets the window settings entries. Canonical stores them in a variable-length chunk stream;
    /// this port uses a plain list until the settings chunk realizes the stream.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2784</c>)
    /// </summary>
    public List<ImGuiWindowSettings> SettingsWindows { get; } = new();

    /// <summary>
    /// Gets the table layout entries loaded from and saved to the layout file.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2785</c>)
    /// </summary>
    /// <remarks>
    /// Canonical stores the entries in an <c>ImChunkStream&lt;ImGuiTableSettings&gt;</c> so that a
    /// header and its variable-length column array share one allocation; this port stores each entry
    /// as a plain object until the settings chunk realizes the stream. Each table binds one entry
    /// through <see cref="ImGuiTable.SettingsOffset"/>.
    /// </remarks>
    public List<ImGuiTableSettings> SettingsTables { get; } = new();

    #endregion

    #region Hooks

    /// <summary>
    /// Gets the registered context hooks.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2788</c>)
    /// </summary>
    public List<ImGuiContextHook> Hooks { get; } = new();

    /// <summary>
    /// Gets or sets the next available hook identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2789</c>)
    /// </summary>
    public uint HookIdNext { get; set; }

    /// <summary>
    /// Gets or sets the demo section-marker callback.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2790</c>)
    /// </summary>
    public ImGuiDemoMarkerCallback? DemoMarkerCallback { get; set; }

    #endregion

    #region Localization

    /// <summary>
    /// Gets the localization text for each localization key, or <see langword="null"/> when the key
    /// has no registration. (<c>ThirdParty/imgui/imgui_internal.h:2793</c>)
    /// </summary>
    public string?[] LocalizationTable { get; } = new string?[(int)ImGuiLocKey.COUNT];

    #endregion

    #region Capture/Logging

    /// <summary>
    /// Gets or sets whether capture is active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2796</c>)
    /// </summary>
    public bool LogEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the next logged item starts a new line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2797</c>)
    /// </summary>
    public bool LogLineFirstItem { get; set; }

    /// <summary>
    /// Gets or sets the capture flags and type.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2798</c>)
    /// </summary>
    public ImGuiLogFlags LogFlags { get; set; }

    /// <summary>
    /// Gets or sets the log window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2799</c>)
    /// </summary>
    public ImGuiWindow? LogWindow { get; set; }

    /// <summary>
    /// Gets or sets the log output writer, or <see langword="null"/> when logging in memory.
    /// Canonical stores a <c>FILE*</c>; this port stores a managed writer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2800</c>)
    /// </summary>
    public System.IO.TextWriter? LogFile { get; set; }

    /// <summary>
    /// Gets or sets the accumulation buffer used when logging to the clipboard.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2801</c>)
    /// </summary>
    public ImGuiTextBuffer LogBuffer { get; set; } = new();

    /// <summary>
    /// Gets or sets the prefix for the next log line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2802</c>)
    /// </summary>
    public string? LogNextPrefix { get; set; }

    /// <summary>
    /// Gets or sets the suffix for the next log line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2803</c>)
    /// </summary>
    public string? LogNextSuffix { get; set; }

    /// <summary>
    /// Gets or sets the Y position of the current log line.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2804</c>)
    /// </summary>
    public float LogLinePosY { get; set; } = float.MaxValue;

    /// <summary>
    /// Gets or sets the log depth reference.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2805</c>)
    /// </summary>
    public int LogDepthRef { get; set; }

    /// <summary>
    /// Gets or sets the depth to expand in logs.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2806</c>)
    /// </summary>
    public int LogDepthToExpand { get; set; } = 2;

    /// <summary>
    /// Gets or sets the default depth to expand.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2807</c>)
    /// </summary>
    public int LogDepthToExpandDefault { get; set; } = 2;

    #endregion

    #region Error handling

    /// <summary>
    /// Gets or sets the error callback, or <see langword="null"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2810</c>)
    /// </summary>
    public ImGuiErrorCallback? ErrorCallback { get; set; }

    /// <summary>
    /// Gets or sets the opaque data supplied to the error callback.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2811</c>)
    /// </summary>
    public IntPtr ErrorCallbackUserData { get; set; }

    /// <summary>
    /// Gets or sets the locked position of the error tooltip.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2812</c>)
    /// </summary>
    public ImVec2 ErrorTooltipLockedPos { get; set; }

    /// <summary>
    /// Gets or sets whether the next reported error is the first of the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2813</c>)
    /// </summary>
    public bool ErrorFirst { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of errors submitted this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2814</c>)
    /// </summary>
    public int ErrorCountCurrentFrame { get; set; }

    /// <summary>
    /// Gets or sets the stack-size snapshot taken at <c>NewFrame()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2815</c>)
    /// </summary>
    public ImGuiErrorRecoveryState StackSizesInNewFrame { get; set; } = new();

    /// <summary>
    /// Gets or sets the alias to the current window's stack-size snapshot, or
    /// <see langword="null"/> when no window is current.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2816</c>)
    /// </summary>
    public ImGuiErrorRecoveryState? StackSizesInBeginForCurrentWindow { get; set; }

    #endregion

    #region Debug tools

    /// <summary>
    /// Gets or sets the locked identifier-conflict count, preserved while Ctrl is held.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2820</c>)
    /// </summary>
    public int DebugDrawIdConflictsCount { get; set; }

    /// <summary>
    /// Gets or sets the debug log flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2821</c>)
    /// </summary>
    public ImGuiDebugLogFlags DebugLogFlags { get; set; } = ImGuiDebugLogFlags.EventError | ImGuiDebugLogFlags.OutputToTTY;

    /// <summary>
    /// Gets or sets the debug log buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2822</c>)
    /// </summary>
    public ImGuiTextBuffer DebugLogBuf { get; set; } = new();

    /// <summary>
    /// Gets or sets the debug log line index.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2823</c>)
    /// </summary>
    public ImGuiTextIndex DebugLogIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the count of skipped errors.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2824</c>)
    /// </summary>
    public int DebugLogSkippedErrors { get; set; }

    /// <summary>
    /// Gets or sets the debug log auto-disable flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2825</c>)
    /// </summary>
    public ImGuiDebugLogFlags DebugLogAutoDisableFlags { get; set; }

    /// <summary>
    /// Gets or sets the number of frames remaining before the debug log auto-disables.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2826</c>)
    /// </summary>
    public byte DebugLogAutoDisableFrames { get; set; }

    /// <summary>
    /// Gets or sets the number of frames remaining for the locate-item highlight.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2827</c>)
    /// </summary>
    public byte DebugLocateFrames { get; set; }

    /// <summary>
    /// Gets or sets whether to break in the item-add pass for the located identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2828</c>)
    /// </summary>
    public bool DebugBreakInLocateId { get; set; }

    /// <summary>
    /// Gets or sets the debug break key chord.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2829</c>)
    /// </summary>
    public ImGuiKey DebugBreakKeyChord { get; set; }

    /// <summary>
    /// Gets or sets the cull depth cycled by the begin-return-value debug tool, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2830</c>)
    /// </summary>
    public sbyte DebugBeginReturnValueCullDepth { get; set; } = -1;

    /// <summary>
    /// Gets or sets whether the item picker is active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2831</c>)
    /// </summary>
    public bool DebugItemPickerActive { get; set; }

    /// <summary>
    /// Gets or sets the item-picker mouse button.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2832</c>)
    /// </summary>
    public byte DebugItemPickerMouseButton { get; set; } = (byte)ImGuiMouseButton.Left;

    /// <summary>
    /// Gets or sets the identifier that triggers a debug break in the item picker.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2833</c>)
    /// </summary>
    public uint DebugItemPickerBreakId { get; set; }

    /// <summary>
    /// Gets or sets the flash-color time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2834</c>)
    /// </summary>
    public float DebugFlashStyleColorTime { get; set; }

    /// <summary>
    /// Gets or sets the flash-color backup.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2835</c>)
    /// </summary>
    public ImVec4 DebugFlashStyleColorBackup { get; set; }

    /// <summary>
    /// Gets or sets the metrics/debugger window options.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2836</c>)
    /// </summary>
    public ImGuiMetricsConfig DebugMetricsConfig { get; set; } = new();

    /// <summary>
    /// Gets or sets the allocation-activity record shown by the metrics window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2839</c>)
    /// </summary>
    public ImGuiDebugAllocInfo DebugAllocInfo { get; set; } = new();

    /// <summary>
    /// Gets or sets the identifier-stack debug query state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2837</c>)
    /// </summary>
    public ImGuiDebugItemPathQuery DebugItemPathQuery { get; set; } = new();

    /// <summary>
    /// Gets or sets the identifier-stack tool options.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2838</c>)
    /// </summary>
    public ImGuiIDStackTool DebugIDStackTool { get; set; } = new();

    /// <summary>
    /// Gets or sets the hovered docking node for debug inspection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2840</c>)
    /// </summary>
    public ImGuiDockNode? DebugHoveredDockNode { get; set; }

    /// <summary>
    /// Gets the identifier-conflict storage used when highlighting all conflicts is enabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2842</c>)
    /// </summary>
    public ImGuiStorage DebugDrawIdConflictsAliveCount { get; } = new();

    /// <summary>
    /// Gets the identifier-conflict highlight set used when highlighting all conflicts is enabled.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2843</c>)
    /// </summary>
    public ImGuiStorage DebugDrawIdConflictsHighlightSet { get; } = new();

    #endregion

    #region Miscellaneous

    /// <summary>
    /// Gets the framerate estimate over the last sixty frames.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2847</c>)
    /// </summary>
    public float[] FramerateSecPerFrame { get; } = new float[60];

    /// <summary>
    /// Gets or sets the ring-buffer index for <see cref="FramerateSecPerFrame"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2848</c>)
    /// </summary>
    public int FramerateSecPerFrameIdx { get; set; }

    /// <summary>
    /// Gets or sets the number of framerate samples collected.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2849</c>)
    /// </summary>
    public int FramerateSecPerFrameCount { get; set; }

    /// <summary>
    /// Gets or sets the accumulated frame time.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2850</c>)
    /// </summary>
    public float FramerateSecPerFrameAccum { get; set; }

    /// <summary>
    /// Gets or sets the mouse-capture override for the next frame, or -1 for no override.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2851</c>)
    /// </summary>
    public int WantCaptureMouseNextFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the keyboard-capture override for the next frame, or -1 for no override.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2852</c>)
    /// </summary>
    public int WantCaptureKeyboardNextFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the text-input override for the next frame, or -1 for no override.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2853</c>)
    /// </summary>
    public int WantTextInputNextFrame { get; set; } = -1;

    /// <summary>
    /// Gets the temporary text buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2854</c>)
    /// </summary>
    public List<byte> TempBuffer { get; } = new();

    /// <summary>
    /// Gets or sets the temporary key-chord name buffer. Canonical stores a fixed
    /// <c>char[64]</c>; this port stores a managed string.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2855</c>)
    /// </summary>
    public string TempKeychordName { get; set; } = string.Empty;

    #endregion

    /// <summary>
    /// Creates the per-key ownership array and initializes every entry to the "no owner" state.
    /// (<c>ThirdParty/imgui/imgui_internal.h:2547</c>)
    /// </summary>
    /// <returns>The ownership array.</returns>
    private static ImGuiKeyOwnerData[] CreateKeysOwnerData()
    {
        var data = new ImGuiKeyOwnerData[(int)ImGuiKey.NamedKey_COUNT];
        for (int n = 0; n < data.Length; n++)
        {
            data[n] = new ImGuiKeyOwnerData();
        }

        return data;
    }
}

#endregion

#region Canonical constants

/// <summary>
/// Holds canonical compile-time constants that belong to the internal-structure layer rather than
/// to a single type.
/// </summary>
/// <remarks>
/// The type is a container only; it is never instantiated.
/// </remarks>
internal static class ImGuiInternalConstants
{
    /// <summary>
    /// Gets the identifier assigned to the main viewport created by a context
    /// (<c>ThirdParty/imgui/imgui_internal.h:4758</c>,
    /// <c>#define IMGUI_VIEWPORT_DEFAULT_ID 0x11111111</c>).
    /// </summary>
    /// <remarks>
    /// The default viewport always exists and does not require multi-viewport support; the platform
    /// backends use this value to recognize it.
    /// </remarks>
    internal const uint ViewportDefaultId = 0x11111111;
}

#endregion

#region Typing-select state

/// <summary>
/// Describes one frame of typing-select input: the search text, its length, and whether the caller
/// must act on it. This is the managed counterpart of canonical <c>ImGuiTypingSelectRequest</c>
/// (<c>ThirdParty/imgui/imgui_internal.h:1849-1857</c>).
/// </summary>
/// <remarks>
/// Canonical stores the search text as a <c>char*</c> into the owning
/// <see cref="ImGuiTypingSelectState"/> buffer. The port stores a managed string and reports its
/// length in UTF-16 code units rather than UTF-8 bytes.
/// </remarks>
public sealed class ImGuiTypingSelectRequest
{
    /// <summary>
    /// Gets or sets the flags passed to <c>GetTypingSelectRequest()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1851</c>)
    /// </summary>
    public ImGuiTypingSelectFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the number of valid characters in <see cref="SearchBuffer"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1852</c>)
    /// </summary>
    public int SearchBufferLen { get; set; }

    /// <summary>
    /// Gets or sets the search text. The full buffer is the search key unless
    /// <see cref="SingleCharMode"/> is set, in which case only <see cref="SingleCharSize"/> characters
    /// form the key.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1853</c>)
    /// </summary>
    public string SearchBuffer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating that the buffer was modified this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1854</c>)
    /// </summary>
    public bool SelectRequest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the buffer holds one repeated character.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1855</c>)
    /// </summary>
    public bool SingleCharMode { get; set; }

    /// <summary>
    /// Gets or sets the length of the first codepoint: one for ASCII, two for a UTF-16 surrogate pair.
    /// Canonical stores a UTF-8 byte length in an <c>ImS8</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1856</c>)
    /// </summary>
    public int SingleCharSize { get; set; }
}

/// <summary>
/// Holds the persistent typing-select search state across frames. This is the managed counterpart of
/// canonical <c>ImGuiTypingSelectState</c> (<c>ThirdParty/imgui/imgui_internal.h:1860-1870</c>).
/// </summary>
/// <remarks>
/// Canonical stores a fixed <c>char SearchBuffer[64]</c>; the port stores a managed string whose
/// maximum length is enforced by <c>GetTypingSelectRequest()</c>. <see cref="Clear"/> deliberately
/// preserves the remaining fields for debugging, as canonical does.
/// </remarks>
public sealed class ImGuiTypingSelectState
{
    /// <summary>
    /// Gets or sets the request populated for the current frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1862</c>)
    /// </summary>
    public ImGuiTypingSelectRequest Request { get; set; } = new();

    /// <summary>
    /// Gets or sets the persistent search text, capped at 63 characters by
    /// <c>GetTypingSelectRequest()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1863</c>)
    /// </summary>
    public string SearchBuffer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the navigation focus scope in which the search was started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1864</c>)
    /// </summary>
    public uint FocusScope { get; set; }

    /// <summary>
    /// Gets or sets the frame number of the last accepted request.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1865</c>)
    /// </summary>
    public int LastRequestFrame { get; set; }

    /// <summary>
    /// Gets or sets the time of the last accepted request, used for the reset timer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1866</c>)
    /// </summary>
    public float LastRequestTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that matching is locked to a single repeated character.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1867</c>)
    /// </summary>
    public bool SingleCharModeLock { get; set; }

    /// <summary>
    /// Clears the search text and the single-character lock, deliberately preserving the remaining
    /// fields for debugging.
    /// (<c>ThirdParty/imgui/imgui_internal.h:1869</c>)
    /// </summary>
    public void Clear()
    {
        SearchBuffer = string.Empty;
        SingleCharModeLock = false;
    }
}

#endregion

#region Tables

// Canonical ground truth: ThirdParty/imgui/imgui_internal.h:3160-3486 and ThirdParty/imgui/imgui.h:2260-2278
// (Dear ImGui v1.92.9b-docking, commit b48d1af). The column index typedefs
// ImGuiTableColumnIdx (ImS16) and ImGuiTableDrawChannelIdx (ImU16) are represented directly as
// `short` and `ushort`; the signed typedef's -1 "none" sentinel is preserved by the short members and
// the draw-channel typedef's (ImU8)-1 default is preserved as 255. Every canonical member is present
// in declaration order. Canonical packs several members into C bitfields; this port declares each of
// them as a separate semantic member, so the managed layout is deliberately not byte-for-byte equal
// to the canonical structure.

/// <summary>
/// One sort specification for a table column, exposed to the application through
/// <see cref="ImGuiTableSortSpecs.Specs"/> (<c>ThirdParty/imgui/imgui.h:2270-2278</c>).
/// </summary>
/// <remarks>
/// Canonical declares a 12-byte value struct that the table copies into its single-sort or multi-sort
/// storage. This port declares a reference type because the table builds the exposed specification
/// array from the same instances and mutates their fields in place when sorting changes; a plain
/// assignment would alias instead of snapshot, so callers that need a value copy must copy each field
/// explicitly.
/// </remarks>
public sealed class ImGuiTableColumnSortSpecs
{
    /// <summary>
    /// Gets or sets the column identifier, copied from the column's <c>UserData</c> argument.
    /// (<c>ThirdParty/imgui/imgui.h:2272</c>)
    /// </summary>
    public uint ColumnUserID { get; set; }

    /// <summary>
    /// Gets or sets the column index. (<c>ThirdParty/imgui/imgui.h:2273</c>)
    /// </summary>
    public short ColumnIndex { get; set; }

    /// <summary>
    /// Gets or sets the position of this specification within its parent array, dense from zero.
    /// (<c>ThirdParty/imgui/imgui.h:2274</c>)
    /// </summary>
    public short SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the current sort direction for the column.
    /// (<c>ThirdParty/imgui/imgui.h:2275</c>)
    /// </summary>
    public ImGuiSortDirection SortDirection { get; set; }
}

/// <summary>
/// The public sorting interface returned by <c>TableGetSortSpecs()</c>, describing the active sort
/// order of a table (<c>ThirdParty/imgui/imgui.h:2260-2267</c>).
/// </summary>
/// <remarks>
/// Canonical holds a <c>const ImGuiTableColumnSortSpecs*</c> that points either at the table's single
/// specification or at its multi-sort vector. This port declares the type a reference type so the
/// application can clear <see cref="SpecsDirty"/> in place, and represents the pointed-to sequence as
/// a managed array view that the table rebuilds when sorting changes.
/// </remarks>
public sealed class ImGuiTableSortSpecs
{
    /// <summary>
    /// Gets or sets the active specification view, or <see langword="null"/> when
    /// <see cref="SpecsCount"/> is zero. (<c>ThirdParty/imgui/imgui.h:2262</c>)
    /// </summary>
    /// <remarks>
    /// The table points this at its <see cref="ImGuiTable.SortSpecsSingle"/> instance for a single
    /// sort and at the elements of <see cref="ImGuiTable.SortSpecsMulti"/> for a multi-sort.
    /// </remarks>
    public ImGuiTableColumnSortSpecs[]? Specs { get; set; }

    /// <summary>
    /// Gets or sets the number of active specifications: zero with tristate sorting, one for a normal
    /// sort, and more than one for a multi-sort. (<c>ThirdParty/imgui/imgui.h:2263</c>)
    /// </summary>
    public int SpecsCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the specifications changed and the application should
    /// re-sort its data, after which it clears this flag.
    /// (<c>ThirdParty/imgui/imgui.h:2264</c>)
    /// </summary>
    public bool SpecsDirty { get; set; }
}

/// <summary>
/// One angled-header request queued for <c>TableAngledHeadersRowEx()</c>.
/// (<c>ThirdParty/imgui/imgui_internal.h:3267-3273</c>)
/// </summary>
/// <remarks>
/// Canonical declares a 12-byte value struct that is pushed into a temporary vector. This port
/// declares a reference type so the per-column requests are appended and then read without copying;
/// the canonical value-copy semantics do not matter because requests are written once and consumed
/// within the same header row.
/// </remarks>
public sealed class ImGuiTableHeaderData
{
    /// <summary>
    /// Gets or sets the column index this request describes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3269</c>)
    /// </summary>
    public short Index { get; set; }

    /// <summary>
    /// Gets or sets the text color of the angled header.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3270</c>)
    /// </summary>
    public uint TextColor { get; set; }

    /// <summary>
    /// Gets or sets the base background color of the angled header.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3271</c>)
    /// </summary>
    public uint BgColor0 { get; set; }

    /// <summary>
    /// Gets or sets the optional highlight background color of the angled header.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3272</c>)
    /// </summary>
    public uint BgColor1 { get; set; }
}

/// <summary>
/// A transient per-row cell background request written by <c>TableSetBgColor()</c> and consumed while
/// the row is drawn (<c>ThirdParty/imgui/imgui_internal.h:3258-3262</c>).
/// </summary>
/// <remarks>
/// Canonical declares a 6-byte value struct stored in the table's row-cell span. This port declares a
/// reference type so <c>TableSetBgColor()</c> can update an existing entry in place.
/// </remarks>
public sealed class ImGuiTableCellData
{
    /// <summary>
    /// Gets or sets the resolved background color.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3260</c>)
    /// </summary>
    public uint BgColor { get; set; }

    /// <summary>
    /// Gets or sets the column the background applies to.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3261</c>)
    /// </summary>
    public short Column { get; set; }
}

/// <summary>
/// Setup data copied from <c>TableSetupColumn()</c> together with the previous-frame column used while
/// reconciling a column topology change (<c>ThirdParty/imgui/imgui_internal.h:3241-3254</c>).
/// </summary>
/// <remarks>
/// Canonical value-initializes one entry per unmatched setup request at
/// <c>ThirdParty/imgui/imgui_tables.cpp:1782</c>. This port declares a reference type because the
/// reconcile path fills the entry and then reads it back within the same layout pass;
/// <see cref="ColumnOldData"/> is a separate object because canonical copies the previous column by
/// value, so the implementation calls <see cref="ImGuiTableColumn.CopyFrom"/> to reproduce that copy.
/// </remarks>
public sealed class ImGuiTableReconcileColumnData
{
    /// <summary>
    /// Gets or sets the column identifier resolved from the setup label.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3244</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the offset of the column name within the parent name buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3245</c>)
    /// </summary>
    public short NameOffset { get; set; }

    /// <summary>
    /// Gets or sets the column flags passed to <c>TableSetupColumn()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3246</c>)
    /// </summary>
    public ImGuiTableColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the initial width or weight passed to <c>TableSetupColumn()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3247</c>)
    /// </summary>
    public float InitWidthOrWeight { get; set; }

    /// <summary>
    /// Gets or sets the optional user data passed to <c>TableSetupColumn()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3248</c>)
    /// </summary>
    public uint UserData { get; set; }

    /// <summary>
    /// Gets or sets the index of this request within the current table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3251</c>)
    /// </summary>
    public short ColumnNewIdx { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the matched column in the previous-frame table, or -1 when no match
    /// was found. (<c>ThirdParty/imgui/imgui_internal.h:3252</c>)
    /// </summary>
    public short ColumnOldIdx { get; set; } = -1;

    /// <summary>
    /// Gets the full backup of the matched previous-frame column, or a default-constructed column when
    /// no match was found. (<c>ThirdParty/imgui/imgui_internal.h:3253</c>)
    /// </summary>
    public ImGuiTableColumn ColumnOldData { get; } = new();
}

/// <summary>
/// Per-column state of a table, holding geometry, ordering, anchoring, and status flags
/// (<c>ThirdParty/imgui/imgui_internal.h:3175-3237</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical packs <c>IsPreserveWidthAuto</c>, <c>IsJustCreated</c>, <c>IsLoadedSettings</c>,
/// <c>IsNeedReconcileSrc</c>, and <c>IsNeedReconcileDst</c> into single-bit fields and
/// <c>AutoFitQueue</c>, <c>CannotSkipItemsQueue</c>, <c>SortDirection</c>,
/// <c>SortDirectionsAvailCount</c>, <c>SortDirectionsAvailMask</c>, and
/// <c>SortDirectionsAvailList</c> into multi-bit integer fields. This port declares each as its own
/// semantic member, so the managed layout is not byte-for-byte equal to the canonical structure.
/// </para>
/// <para>
/// Canonical stores columns in an arena and copies a whole column by value while restoring the
/// previous frame's layout and while reconciling setup requests. This port declares a reference type
/// so the layout path can mutate the table's columns in place; callers that need the canonical value
/// copy call <see cref="CopyFrom"/>.
/// </para>
/// <para>
/// The canonical <c>ImGuiTableColumnIdx</c> members (<see cref="DisplayOrder"/>,
/// <see cref="IndexWithinEnabledSet"/>, <see cref="PrevEnabledColumn"/>,
/// <see cref="NextEnabledColumn"/>, and <see cref="SortOrder"/>) and the canonical
/// <c>ImGuiTableDrawChannelIdx</c> members (<see cref="DrawChannelCurrent"/>,
/// <see cref="DrawChannelFrozen"/>, and <see cref="DrawChannelUnfrozen"/>) are represented as
/// <c>short</c> and <c>ushort</c> respectively.
/// </para>
/// </remarks>
public sealed class ImGuiTableColumn
{
    /// <summary>
    /// Gets or sets the column flags after patching by <c>TableSetupColumnFlags()</c>, which are not
    /// the raw flags supplied by the application.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3177</c>)
    /// </summary>
    public ImGuiTableColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the final visible width, equal to <c>MaxX - MinX</c> and locked in
    /// <c>TableUpdateLayout()</c>. (<c>ThirdParty/imgui/imgui_internal.h:3178</c>)
    /// </summary>
    public float WidthGiven { get; set; }

    /// <summary>
    /// Gets or sets the absolute left position of the column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3179</c>)
    /// </summary>
    public float MinX { get; set; }

    /// <summary>
    /// Gets or sets the absolute right position of the column, including padding and spacing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3180</c>)
    /// </summary>
    public float MaxX { get; set; }

    /// <summary>
    /// Gets or sets the requested width for a fixed-width column; for a stretched column it is derived
    /// every layout from <see cref="StretchWeight"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3181</c>)
    /// </summary>
    public float WidthRequest { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the automatic, content-derived width.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3182</c>)
    /// </summary>
    public float WidthAuto { get; set; }

    /// <summary>
    /// Gets or sets the maximum width at the current position; the value is overwritten by each
    /// table instance. (<c>ThirdParty/imgui/imgui_internal.h:3183</c>)
    /// </summary>
    public float WidthMax { get; set; }

    /// <summary>
    /// Gets or sets the stretch weight used when the column has the width-stretch flag.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3184</c>)
    /// </summary>
    public float StretchWeight { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the raw width or weight value passed to <c>TableSetupColumn()</c>. For a width it
    /// is a content width without padding.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3185</c>)
    /// </summary>
    public float InitStretchWeightOrWidth { get; set; }

    /// <summary>
    /// Gets or sets the clipping rectangle applied to the column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3186</c>)
    /// </summary>
    public ImRect ClipRect { get; set; }

    /// <summary>
    /// Gets or sets the hash of the column label, used for settings persistence and reconciliation.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3187</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the optional user data value passed to <c>TableSetupColumn()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3188</c>)
    /// </summary>
    public uint UserData { get; set; }

    /// <summary>
    /// Gets or sets the minimum content X, approximately the column minimum plus cell padding and
    /// leading cell spacing. (<c>ThirdParty/imgui/imgui_internal.h:3189</c>)
    /// </summary>
    public float WorkMinX { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X, approximately the column maximum minus cell padding and
    /// trailing cell spacing. (<c>ThirdParty/imgui/imgui_internal.h:3190</c>)
    /// </summary>
    public float WorkMaxX { get; set; }

    /// <summary>
    /// Gets or sets the current item width for the column, preserved across rows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3191</c>)
    /// </summary>
    public float ItemWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X reached by frozen rows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3192</c>)
    /// </summary>
    public float ContentMaxXFrozen { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X reached by unfrozen rows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3193</c>)
    /// </summary>
    public float ContentMaxXUnfrozen { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X actually used by header rows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3194</c>)
    /// </summary>
    public float ContentMaxXHeadersUsed { get; set; }

    /// <summary>
    /// Gets or sets the ideal unclipped header content width.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3195</c>)
    /// </summary>
    public float ContentMaxXHeadersIdeal { get; set; }

    /// <summary>
    /// Gets or sets the offset of the column name within the parent name buffer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3196</c>)
    /// </summary>
    public short NameOffset { get; set; } = -1;

    /// <summary>
    /// Gets or sets the display order of the column, which the user may change by reordering.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3197</c>)
    /// </summary>
    public short DisplayOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the column within the enabled set, which is never greater than the
    /// display order. (<c>ThirdParty/imgui/imgui_internal.h:3198</c>)
    /// </summary>
    public short IndexWithinEnabledSet { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the previous enabled column, or -1 when this is the first.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3199</c>)
    /// </summary>
    public short PrevEnabledColumn { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the next enabled column, or -1 when this is the last.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3200</c>)
    /// </summary>
    public short NextEnabledColumn { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of this column within the sort specifications, or -1 when the column is
    /// not sorted. (<c>ThirdParty/imgui/imgui_internal.h:3201</c>)
    /// </summary>
    public short SortOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the current index into the draw splitter channel list.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3202</c>)
    /// </summary>
    /// <remarks>
    /// Canonical initializes this unsigned 16-bit channel index to the 8-bit sentinel
    /// <c>(ImU8)-1</c>, which is 255 rather than 65535.
    /// </remarks>
    public ushort DrawChannelCurrent { get; set; } = 255;

    /// <summary>
    /// Gets or sets the draw channel used for frozen rows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3203</c>)
    /// </summary>
    public ushort DrawChannelFrozen { get; set; } = 255;

    /// <summary>
    /// Gets or sets the draw channel used for unfrozen rows.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3204</c>)
    /// </summary>
    public ushort DrawChannelUnfrozen { get; set; } = 255;

    /// <summary>
    /// Gets or sets a value indicating that the column is enabled, meaning it is both user-enabled and
    /// not disabled by flags. (<c>ThirdParty/imgui/imgui_internal.h:3205</c>)
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the user has not hidden the column, independently of
    /// whether it is clipped by scrolling.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3206</c>)
    /// </summary>
    public bool IsUserEnabled { get; set; }

    /// <summary>
    /// Gets or sets the pending user-enabled state applied by the next layout pass.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3207</c>)
    /// </summary>
    public bool IsUserEnabledNextFrame { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the column overlaps the host clip rectangle horizontally.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3208</c>)
    /// </summary>
    public bool IsVisibleX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the column is visible vertically.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3209</c>)
    /// </summary>
    public bool IsVisibleY { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the layout requests the application to submit contents for
    /// this column. (<c>ThirdParty/imgui/imgui_internal.h:3210</c>)
    /// </summary>
    public bool IsRequestOutput { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that item submissions for this column are ignored.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3211</c>)
    /// </summary>
    public bool IsSkipItems { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the automatic width is preserved across table
    /// re-initialization to avoid flicker.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3212</c>)
    /// </summary>
    public bool IsPreserveWidthAuto { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the column was just constructed and still needs its
    /// defaults initialized. (<c>ThirdParty/imgui/imgui_internal.h:3213</c>)
    /// </summary>
    public bool IsJustCreated { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating that the column was matched while loading layout settings.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3214</c>)
    /// </summary>
    public bool IsLoadedSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the previous-frame column is still awaiting a reconcile
    /// match. (<c>ThirdParty/imgui/imgui_internal.h:3215</c>)
    /// </summary>
    public bool IsNeedReconcileSrc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the current-frame column is still awaiting a reconcile
    /// match. (<c>ThirdParty/imgui/imgui_internal.h:3216</c>)
    /// </summary>
    public bool IsNeedReconcileDst { get; set; }

    /// <summary>
    /// Gets or sets the navigation layer at the time the column was entered.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3217</c>)
    /// </summary>
    public sbyte NavLayerCurrent { get; set; }

    /// <summary>
    /// Gets or sets the countdown of frames that still request an automatic width fit for this
    /// column. (<c>ThirdParty/imgui/imgui_internal.h:3218</c>)
    /// </summary>
    public byte AutoFitQueue { get; set; }

    /// <summary>
    /// Gets or sets the countdown of frames during which the column refuses to skip item submission.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3219</c>)
    /// </summary>
    public byte CannotSkipItemsQueue { get; set; }

    /// <summary>
    /// Gets or sets the current sort direction, or <see cref="ImGuiSortDirection.None"/> when the
    /// column is not sorted. (<c>ThirdParty/imgui/imgui_internal.h:3220</c>)
    /// </summary>
    public ImGuiSortDirection SortDirection { get; set; }

    /// <summary>
    /// Gets or sets the number of sort directions available to the column, from zero to three.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3221</c>)
    /// </summary>
    public byte SortDirectionsAvailCount { get; set; }

    /// <summary>
    /// Gets or sets the mask of sort directions available to the column, one bit per direction.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3222</c>)
    /// </summary>
    public byte SortDirectionsAvailMask { get; set; }

    /// <summary>
    /// Gets or sets the ordered list of available sort directions, two bits per entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3223</c>)
    /// </summary>
    public byte SortDirectionsAvailList { get; set; }

    /// <summary>
    /// Copies every field of another column into this one.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3175-3237</c>)
    /// </summary>
    /// <param name="source">The column to snapshot.</param>
    /// <remarks>
    /// Canonical copies a whole <c>ImGuiTableColumn</c> by value while restoring the previous frame's
    /// columns (<c>ThirdParty/imgui/imgui_tables.cpp:609</c>), while filling a reconcile request
    /// (<c>1806-1840</c>), and while writing the reconciled column back
    /// (<c>1845</c>). Because this port represents the column as a reference type, a plain assignment
    /// would alias instead of snapshot, so those sites call this method to reproduce the canonical
    /// value semantics without allocating.
    /// </remarks>
    public void CopyFrom(ImGuiTableColumn source)
    {
        Flags = source.Flags;
        WidthGiven = source.WidthGiven;
        MinX = source.MinX;
        MaxX = source.MaxX;
        WidthRequest = source.WidthRequest;
        WidthAuto = source.WidthAuto;
        WidthMax = source.WidthMax;
        StretchWeight = source.StretchWeight;
        InitStretchWeightOrWidth = source.InitStretchWeightOrWidth;
        ClipRect = source.ClipRect;
        ID = source.ID;
        UserData = source.UserData;
        WorkMinX = source.WorkMinX;
        WorkMaxX = source.WorkMaxX;
        ItemWidth = source.ItemWidth;
        ContentMaxXFrozen = source.ContentMaxXFrozen;
        ContentMaxXUnfrozen = source.ContentMaxXUnfrozen;
        ContentMaxXHeadersUsed = source.ContentMaxXHeadersUsed;
        ContentMaxXHeadersIdeal = source.ContentMaxXHeadersIdeal;
        NameOffset = source.NameOffset;
        DisplayOrder = source.DisplayOrder;
        IndexWithinEnabledSet = source.IndexWithinEnabledSet;
        PrevEnabledColumn = source.PrevEnabledColumn;
        NextEnabledColumn = source.NextEnabledColumn;
        SortOrder = source.SortOrder;
        DrawChannelCurrent = source.DrawChannelCurrent;
        DrawChannelFrozen = source.DrawChannelFrozen;
        DrawChannelUnfrozen = source.DrawChannelUnfrozen;
        IsEnabled = source.IsEnabled;
        IsUserEnabled = source.IsUserEnabled;
        IsUserEnabledNextFrame = source.IsUserEnabledNextFrame;
        IsVisibleX = source.IsVisibleX;
        IsVisibleY = source.IsVisibleY;
        IsRequestOutput = source.IsRequestOutput;
        IsSkipItems = source.IsSkipItems;
        IsPreserveWidthAuto = source.IsPreserveWidthAuto;
        IsJustCreated = source.IsJustCreated;
        IsLoadedSettings = source.IsLoadedSettings;
        IsNeedReconcileSrc = source.IsNeedReconcileSrc;
        IsNeedReconcileDst = source.IsNeedReconcileDst;
        NavLayerCurrent = source.NavLayerCurrent;
        AutoFitQueue = source.AutoFitQueue;
        CannotSkipItemsQueue = source.CannotSkipItemsQueue;
        SortDirection = source.SortDirection;
        SortDirectionsAvailCount = source.SortDirectionsAvailCount;
        SortDirectionsAvailMask = source.SortDirectionsAvailMask;
        SortDirectionsAvailList = source.SortDirectionsAvailList;
    }
}

/// <summary>
/// Per-instance table data that must survive across frames, used to synchronize multiple tables that
/// share one identifier (<c>ThirdParty/imgui/imgui_internal.h:3277-3287</c>).
/// </summary>
/// <remarks>
/// Canonical declares a 24-byte value struct whose first instance is embedded in the table and whose
/// remaining instances are pushed into a vector. This port declares a reference type because
/// <c>TableGetInstanceData()</c> returns the instance and the layout and hover paths mutate it
/// through that reference; canonical never copies a populated instance by value, so no
/// <c>CopyFrom</c> helper is provided.
/// </remarks>
public sealed class ImGuiTableInstanceData
{
    /// <summary>
    /// Gets or sets the identifier of this table instance, which equals the table identifier for
    /// instance zero. (<c>ThirdParty/imgui/imgui_internal.h:3279</c>)
    /// </summary>
    public uint TableInstanceID { get; set; }

    /// <summary>
    /// Gets or sets the outer height measured on the last active frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3280</c>)
    /// </summary>
    public float LastOuterHeight { get; set; }

    /// <summary>
    /// Gets or sets the height of the first consecutive header rows on the last active frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3281</c>)
    /// </summary>
    public float LastTopHeadersRowHeight { get; set; }

    /// <summary>
    /// Gets or sets the height of the frozen section on the last active frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3282</c>)
    /// </summary>
    public float LastFrozenHeight { get; set; }

    /// <summary>
    /// Gets or sets the index of the row hovered on the previous frame, or -1.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3283</c>)
    /// </summary>
    public int HoveredRowLast { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the row hovered on the current frame, set when the row is
    /// encountered, or -1. (<c>ThirdParty/imgui/imgui_internal.h:3284</c>)
    /// </summary>
    public int HoveredRowNext { get; set; } = -1;
}

/// <summary>
/// Transient per-nesting-level table state that lives only between <c>BeginTable()</c> and
/// <c>EndTable()</c> and is shared across tables of the same identifier
/// (<c>ThirdParty/imgui/imgui_internal.h:3419-3445</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical declares a POD value struct that is reused from the context's temporary-data pool. This
/// port declares a reference type because the pool owns one instance per nesting level and the table
/// holds a reference to it; <see cref="DrawSplitter"/> is a reference because the canonical member is
/// an embedded splitter whose lifetime follows the temporary data.
/// </para>
/// <para>
/// The canonical constructor zeroes the structure and sets <see cref="LastTimeActive"/> to -1. The
/// port preserves that initialization through field initializers.
/// </para>
/// </remarks>
public sealed class ImGuiTableTempData
{
    /// <summary>
    /// Gets or sets the identifier of the outer window that owns the table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3421</c>)
    /// </summary>
    public uint WindowID { get; set; }

    /// <summary>
    /// Gets or sets the index of the table in the context's persistent table pool.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3422</c>)
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// Gets or sets the last timestamp at which this entry was used, for garbage collection.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3423</c>)
    /// </summary>
    public float LastTimeActive { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the extra width contributed by angled headers, consumed by <c>EndTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3424</c>)
    /// </summary>
    public float AngledHeadersExtraWidth { get; set; }

    /// <summary>
    /// Gets the angled-header requests built by <c>TableAngledHeadersRow()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3425</c>)
    /// </summary>
    public List<ImGuiTableHeaderData> AngledHeadersRequests { get; } = new();

    /// <summary>
    /// Gets the column reconcile requests queued by <c>TableSetupColumn()</c> and consumed by
    /// <c>TableUpdateLayout()</c>; the list is cleared every frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3428</c>)
    /// </summary>
    public List<ImGuiTableReconcileColumnData> ReconcileColumnsRequests { get; } = new();

    /// <summary>
    /// Gets or sets an opaque handle to the previous frame's column arena while the column count
    /// changes; the port keeps the old columns in <see cref="OldColumnsData"/> and retains this as an
    /// inert sentinel of the canonical release lifecycle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3429</c>)
    /// </summary>
    public IntPtr OldColumnsRawData { get; set; }

    /// <summary>
    /// Gets or sets the detached view over the previous-frame columns while the column count changes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3430</c>)
    /// </summary>
    /// <remarks>
    /// Canonical assigns this span from the table's old arena before the arena is detached; this port
    /// keeps the old column list itself, so the list must be replaced (not cleared in place) by
    /// <c>TableBeginInitMemory()</c> to preserve the previous-frame snapshot.
    /// </remarks>
    public List<ImGuiTableColumn> OldColumnsData { get; set; } = new();

    /// <summary>
    /// Gets or sets the <c>outer_size</c> argument passed to <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3432</c>)
    /// </summary>
    public ImVec2 UserOuterSize { get; set; }

    /// <summary>
    /// Gets the per-table draw splitter that isolates draw commands per column and per frozen row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3433</c>)
    /// </summary>
    public ImDrawListSplitter DrawSplitter { get; } = new();

    /// <summary>
    /// Gets or sets the backed-up inner window work rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3435</c>)
    /// </summary>
    public ImRect HostBackupWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the backed-up inner window parent work rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3436</c>)
    /// </summary>
    public ImRect HostBackupParentWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the backed-up previous-line size of the host window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3437</c>)
    /// </summary>
    public ImVec2 HostBackupPrevLineSize { get; set; }

    /// <summary>
    /// Gets or sets the backed-up current-line size of the host window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3438</c>)
    /// </summary>
    public ImVec2 HostBackupCurrLineSize { get; set; }

    /// <summary>
    /// Gets or sets the backed-up cursor maximum position of the host window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3439</c>)
    /// </summary>
    public ImVec2 HostBackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the backed-up columns offset of the outer window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3440</c>)
    /// </summary>
    public ImVec1 HostBackupColumnsOffset { get; set; }

    /// <summary>
    /// Gets or sets the backed-up item width of the outer window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3441</c>)
    /// </summary>
    public float HostBackupItemWidth { get; set; }

    /// <summary>
    /// Gets or sets the backed-up size of the outer window's item-width stack.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3442</c>)
    /// </summary>
    public int HostBackupItemWidthStackSize { get; set; }
}

/// <summary>
/// Persistent state of one table, owning its columns, display order, per-row cell backgrounds, and
/// the public sort specifications (<c>ThirdParty/imgui/imgui_internal.h:3290-3412</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical allocates <c>Columns</c>, <c>DisplayOrderToIndex</c>, <c>RowCellData</c>, and three
/// enabled/visible bit arrays in a single arena referenced by <c>RawData</c>. This port keeps each of
/// those as a separately owned collection (lists for the spans and <see cref="ImBitVector"/> for the
/// masks, whose canonical type is <c>ImBitArrayPtr</c>), so <see cref="RawData"/> remains an opaque
/// handle used only to express the canonical allocate/detach lifecycle. Each mask must be sized with
/// <c>ImBitVector.Create(columnsCount)</c> during table memory initialization, mirroring canonical
/// <c>ImBitArrayGetStorageSizeInBytes()</c>. The negative-sentinel
/// <c>ImGuiTableColumnIdx</c> members and the unsigned <c>ImGuiTableDrawChannelIdx</c> members are
/// represented as <c>short</c> and <c>ushort</c> respectively.
/// </para>
/// <para>
/// Canonical packs <c>RowFlags</c> and <c>LastRowFlags</c> into 16-bit fields; this port declares each
/// as its own member of type <see cref="ImGuiTableRowFlags"/>, so the managed layout is deliberately
/// not byte-for-byte equal. The canonical constructor zeroes the structure and sets
/// <see cref="LastFrameActive"/> to -1, which the port preserves through field initializers.
/// </para>
/// </remarks>
public sealed class ImGuiTable
{
    /// <summary>
    /// Gets or sets the table identifier.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3292</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the table flags after patching by <c>TableFixFlags()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3293</c>)
    /// </summary>
    public ImGuiTableFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the opaque handle to the canonical column arena; the port allocates the column,
    /// display-order, and row-cell buffers directly and keeps this at zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3294</c>)
    /// </summary>
    public IntPtr RawData { get; set; }

    /// <summary>
    /// Gets or sets the transient temporary data for this table, which points into the context's
    /// temporary-data pool while the table is active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3295</c>)
    /// </summary>
    public ImGuiTableTempData? TempData { get; set; }

    /// <summary>
    /// Gets the columns, indexed by column index. Canonical is a span into <see cref="RawData"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3296</c>)
    /// </summary>
    public List<ImGuiTableColumn> Columns { get; set; } = new();

    /// <summary>
    /// Gets the display order of each column, indexed by display order; values initialize to
    /// 0 through count minus 1. Canonical is a span into <see cref="RawData"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3297</c>)
    /// </summary>
    public List<short> DisplayOrderToIndex { get; set; } = new();

    /// <summary>
    /// Gets the cell background requests for the current row. Canonical is a span into
    /// <see cref="RawData"/>. (<c>ThirdParty/imgui/imgui_internal.h:3298</c>)
    /// </summary>
    public List<ImGuiTableCellData> RowCellData { get; set; } = new();

    /// <summary>
    /// Gets or sets the map from display order to enabled state. Canonical is an
    /// <c>ImBitArrayPtr</c> allocated in <see cref="RawData"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3299</c>)
    /// </summary>
    public ImBitVector EnabledMaskByDisplayOrder { get; set; } = new();

    /// <summary>
    /// Gets or sets the map from column index to enabled state, suitable for iterating columns without
    /// touching cold data. Canonical is an <c>ImBitArrayPtr</c> allocated in <see cref="RawData"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3300</c>)
    /// </summary>
    public ImBitVector EnabledMaskByIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the map from column index to visible state, meaning enabled and not clipped by
    /// scrolling. Canonical is an <c>ImBitArrayPtr</c> allocated in <see cref="RawData"/>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3301</c>)
    /// </summary>
    public ImBitVector VisibleMaskByIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the subset of table flags whose data was loaded from the layout file, so that
    /// unaltered data is not saved back.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3302</c>)
    /// </summary>
    public ImGuiTableFlags SettingsLoadedFlags { get; set; }

    /// <summary>
    /// Gets or sets the index of this table's entry in the context's table settings list, or -1 when
    /// the table is new. (<c>ThirdParty/imgui/imgui_internal.h:3303</c>)
    /// </summary>
    public int SettingsOffset { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which the table was used, which is -1 until its first use.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3304</c>)
    /// </summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of columns declared to <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3305</c>)
    /// </summary>
    public int ColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the current row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3306</c>)
    /// </summary>
    public int CurrentRow { get; set; }

    /// <summary>
    /// Gets or sets the index of the current column, or -1 when no column is current.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3307</c>)
    /// </summary>
    public int CurrentColumn { get; set; }

    /// <summary>
    /// Gets or sets the count of <c>BeginTable()</c> calls with the same identifier on the current
    /// frame, generally zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3308</c>)
    /// </summary>
    public short InstanceCurrent { get; set; }

    /// <summary>
    /// Gets or sets the instance, generally zero, that is being interacted with.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3309</c>)
    /// </summary>
    public short InstanceInteracted { get; set; }

    /// <summary>
    /// Gets or sets the top Y position of the current row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3310</c>)
    /// </summary>
    public float RowPosY1 { get; set; }

    /// <summary>
    /// Gets or sets the bottom Y position of the current row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3311</c>)
    /// </summary>
    public float RowPosY2 { get; set; }

    /// <summary>
    /// Gets or sets the minimum row height submitted to <c>TableNextRow()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3312</c>)
    /// </summary>
    public float RowMinHeight { get; set; }

    /// <summary>
    /// Gets or sets the top and bottom cell padding, reloaded whenever the row changes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3313</c>)
    /// </summary>
    public float RowCellPaddingY { get; set; }

    /// <summary>
    /// Gets or sets the text baseline propagated across the row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3314</c>)
    /// </summary>
    public float RowTextBaseline { get; set; }

    /// <summary>
    /// Gets or sets the row indent offset locked when the row started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3315</c>)
    /// </summary>
    public float RowIndentOffsetX { get; set; }

    /// <summary>
    /// Gets or sets the flags of the current row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3316</c>)
    /// </summary>
    public ImGuiTableRowFlags RowFlags { get; set; }

    /// <summary>
    /// Gets or sets the flags of the previous row.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3317</c>)
    /// </summary>
    public ImGuiTableRowFlags LastRowFlags { get; set; }

    /// <summary>
    /// Gets or sets the counter for alternating row background colors, which excludes header rows and
    /// may be fast-forwarded by a clipper.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3318</c>)
    /// </summary>
    public int RowBgColorCounter { get; set; }

    /// <summary>
    /// Gets the two background color overrides for the current row, indexed by background layer.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3319</c>)
    /// </summary>
    public uint[] RowBgColor { get; } = new uint[2];

    /// <summary>
    /// Gets or sets the strong border color derived from the table border color style.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3320</c>)
    /// </summary>
    public uint BorderColorStrong { get; set; }

    /// <summary>
    /// Gets or sets the light border color derived from the table border color style.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3321</c>)
    /// </summary>
    public uint BorderColorLight { get; set; }

    /// <summary>
    /// Gets or sets the left X coordinate used by table borders.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3322</c>)
    /// </summary>
    public float BorderX1 { get; set; }

    /// <summary>
    /// Gets or sets the right X coordinate used by table borders.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3323</c>)
    /// </summary>
    public float BorderX2 { get; set; }

    /// <summary>
    /// Gets or sets the host indent X position at the start of the table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3324</c>)
    /// </summary>
    public float HostIndentX { get; set; }

    /// <summary>
    /// Gets or sets the minimum column width used by the layout.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3325</c>)
    /// </summary>
    public float MinColumnWidth { get; set; }

    /// <summary>
    /// Gets or sets the outer horizontal padding locked in <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3326</c>)
    /// </summary>
    public float OuterPaddingX { get; set; }

    /// <summary>
    /// Gets or sets the horizontal padding applied inside each bordered cell.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3327</c>)
    /// </summary>
    public float CellPaddingX { get; set; }

    /// <summary>
    /// Gets or sets the spacing inserted before a cell.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3328</c>)
    /// </summary>
    public float CellSpacingX1 { get; set; }

    /// <summary>
    /// Gets or sets the spacing inserted after a cell.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3329</c>)
    /// </summary>
    public float CellSpacingX2 { get; set; }

    /// <summary>
    /// Gets or sets the <c>inner_width</c> argument passed to <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3330</c>)
    /// </summary>
    public float InnerWidth { get; set; }

    /// <summary>
    /// Gets or sets the sum of the current column widths.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3331</c>)
    /// </summary>
    public float ColumnsGivenWidth { get; set; }

    /// <summary>
    /// Gets or sets the sum of the ideal column widths at which nothing is clipped, used for
    /// auto-fitting and for content width submission to the outer window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3332</c>)
    /// </summary>
    public float ColumnsAutoFitWidth { get; set; }

    /// <summary>
    /// Gets or sets the sum of the weights of all enabled stretched columns.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3333</c>)
    /// </summary>
    public float ColumnsStretchSumWeights { get; set; }

    /// <summary>
    /// Gets or sets the width queued for the column being resized, applied by the next instance-zero
    /// layout. (<c>ThirdParty/imgui/imgui_internal.h:3334</c>)
    /// </summary>
    public float ResizedColumnNextWidth { get; set; }

    /// <summary>
    /// Gets or sets the minimum content X2 locked while resizing a column down, to avoid feedback
    /// loops while still allowing growth.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3335</c>)
    /// </summary>
    public float ResizeLockMinContentsX2 { get; set; }

    /// <summary>
    /// Gets or sets the reference scale used to rescale columns when the font size or DPI changes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3336</c>)
    /// </summary>
    public float RefScale { get; set; }

    /// <summary>
    /// Gets or sets the height set by the angled-header row and read by the layout.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3337</c>)
    /// </summary>
    public float AngledHeadersHeight { get; set; }

    /// <summary>
    /// Gets or sets the X-to-Y slope set by the angled-header row and read by the layout.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3338</c>)
    /// </summary>
    public float AngledHeadersSlope { get; set; }

    /// <summary>
    /// Gets or sets the outer rectangle; for a non-scrolling table its maximum Y is often large until
    /// <c>EndTable()</c>. (<c>ThirdParty/imgui/imgui_internal.h:3339</c>)
    /// </summary>
    public ImRect OuterRect { get; set; }

    /// <summary>
    /// Gets or sets the inner rectangle without decoration.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3340</c>)
    /// </summary>
    public ImRect InnerRect { get; set; }

    /// <summary>
    /// Gets or sets the work rectangle shared with the host window.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3341</c>)
    /// </summary>
    public ImRect WorkRect { get; set; }

    /// <summary>
    /// Gets or sets the inner clipping rectangle.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3342</c>)
    /// </summary>
    public ImRect InnerClipRect { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle used to clip cell background fills on the CPU; it evolves while
    /// the frame crosses frozen-row boundaries.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3343</c>)
    /// </summary>
    public ImRect BgClipRect { get; set; }

    /// <summary>
    /// Gets or sets the draw-command clip rectangle for the background layer 0 and 1 channel.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3344</c>)
    /// </summary>
    public ImRect Bg0ClipRectForDrawCmd { get; set; }

    /// <summary>
    /// Gets or sets the draw-command clip rectangle for the background layer 2 channel.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3345</c>)
    /// </summary>
    public ImRect Bg2ClipRectForDrawCmd { get; set; }

    /// <summary>
    /// Gets or sets the host clip rectangle, used to decide whether column draw calls can be merged
    /// into the current window's draw call.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3346</c>)
    /// </summary>
    public ImRect HostClipRect { get; set; }

    /// <summary>
    /// Gets or sets the backup of the inner window clip rectangle taken while a background channel is
    /// pushed. (<c>ThirdParty/imgui/imgui_internal.h:3347</c>)
    /// </summary>
    public ImRect HostBackupInnerClipRect { get; set; }

    /// <summary>
    /// Gets or sets the parent window that hosts the table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3348</c>)
    /// </summary>
    public ImGuiWindow? OuterWindow { get; set; }

    /// <summary>
    /// Gets or sets the window that actually holds the table data, which is either the outer window or
    /// a child window. (<c>ThirdParty/imgui/imgui_internal.h:3349</c>)
    /// </summary>
    public ImGuiWindow? InnerWindow { get; set; }

    /// <summary>
    /// Gets the contiguous buffer that holds the column names.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3350</c>)
    /// </summary>
    public ImGuiTextBuffer ColumnsNames { get; } = new();

    /// <summary>
    /// Gets or sets the shortcut to the temporary data's draw splitter while the table is active.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3351</c>)
    /// </summary>
    public ImDrawListSplitter? DrawSplitter { get; set; }

    /// <summary>
    /// Gets the data for table instance zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3352</c>)
    /// </summary>
    public ImGuiTableInstanceData InstanceDataFirst { get; } = new();

    /// <summary>
    /// Gets the data for table instances above zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3353</c>)
    /// </summary>
    public List<ImGuiTableInstanceData> InstanceDataExtra { get; } = new();

    /// <summary>
    /// Gets the storage for a single active sort specification.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3354</c>)
    /// </summary>
    public ImGuiTableColumnSortSpecs SortSpecsSingle { get; } = new();

    /// <summary>
    /// Gets the storage for multiple active sort specifications.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3355</c>)
    /// </summary>
    public List<ImGuiTableColumnSortSpecs> SortSpecsMulti { get; } = new();

    /// <summary>
    /// Gets the public sort specifications returned by <c>TableGetSortSpecs()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3356</c>)
    /// </summary>
    /// <remarks>
    /// When the table rebuilds its sort specifications it points
    /// <see cref="ImGuiTableSortSpecs.Specs"/> at a one-element array holding
    /// <see cref="SortSpecsSingle"/> for a single sort, or at the elements of
    /// <see cref="SortSpecsMulti"/> for a multi-sort.
    /// </remarks>
    public ImGuiTableSortSpecs SortSpecs { get; } = new();

    /// <summary>
    /// Gets or sets the number of active sort specifications.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3357</c>)
    /// </summary>
    public short SortSpecsCount { get; set; }

    /// <summary>
    /// Gets or sets the number of enabled columns.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3358</c>)
    /// </summary>
    public short ColumnsEnabledCount { get; set; }

    /// <summary>
    /// Gets or sets the number of enabled columns that use a fixed width.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3359</c>)
    /// </summary>
    public short ColumnsEnabledFixedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of <c>TableSetupColumn()</c> calls made for the table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3360</c>)
    /// </summary>
    public short DeclColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the number of columns that request an angled header.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3361</c>)
    /// </summary>
    public short AngledHeadersCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the column whose visible region is hovered, which equals the column
    /// count while hovering the empty region after the last column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3362</c>)
    /// </summary>
    public short HoveredColumnBody { get; set; }

    /// <summary>
    /// Gets or sets the index of the column whose right border is hovered for resizing.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3363</c>)
    /// </summary>
    public short HoveredColumnBorder { get; set; }

    /// <summary>
    /// Gets or sets the index of the column that should be highlighted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3364</c>)
    /// </summary>
    public short HighlightColumnHeader { get; set; }

    /// <summary>
    /// Gets or sets the index of the single column requesting an automatic width fit.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3365</c>)
    /// </summary>
    public short AutoFitSingleColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column being resized, reset when the instance count is zero.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3366</c>)
    /// </summary>
    public short ResizedColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column resized on the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3367</c>)
    /// </summary>
    public short LastResizedColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column header being held.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3368</c>)
    /// </summary>
    public short HeldHeaderColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column header held on the previous frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3369</c>)
    /// </summary>
    public short LastHeldHeaderColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column being reordered; the value is not cleared while the
    /// reorder is held. (<c>ThirdParty/imgui/imgui_internal.h:3370</c>)
    /// </summary>
    public short ReorderColumn { get; set; }

    /// <summary>
    /// Gets or sets the requested display order of the column being reordered.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3371</c>)
    /// </summary>
    public short ReorderColumnDstOrder { get; set; }

    /// <summary>
    /// Gets or sets the index of the left-most enabled column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3372</c>)
    /// </summary>
    public short LeftMostEnabledColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the right-most enabled column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3373</c>)
    /// </summary>
    public short RightMostEnabledColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the left-most stretched column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3374</c>)
    /// </summary>
    public short LeftMostStretchedColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the right-most stretched column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3375</c>)
    /// </summary>
    public short RightMostStretchedColumn { get; set; }

    /// <summary>
    /// Gets or sets the column that was right-clicked, or -1 when the context menu was opened from a
    /// neutral position. (<c>ThirdParty/imgui/imgui_internal.h:3376</c>)
    /// </summary>
    public short ContextPopupColumn { get; set; }

    /// <summary>
    /// Gets or sets the requested frozen-row count.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3377</c>)
    /// </summary>
    public short FreezeRowsRequest { get; set; }

    /// <summary>
    /// Gets or sets the actual frozen-row count, which is zero when there is no scrolling offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3378</c>)
    /// </summary>
    public short FreezeRowsCount { get; set; }

    /// <summary>
    /// Gets or sets the requested frozen-column count.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3379</c>)
    /// </summary>
    public short FreezeColumnsRequest { get; set; }

    /// <summary>
    /// Gets or sets the actual frozen-column count, which is zero when there is no scrolling offset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3380</c>)
    /// </summary>
    public short FreezeColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the current row-cell background entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3381</c>)
    /// </summary>
    public short RowCellDataCurrent { get; set; }

    /// <summary>
    /// Gets or sets the channel to which non-visible columns are redirected.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3382</c>)
    /// </summary>
    public ushort DummyDrawChannel { get; set; }

    /// <summary>
    /// Gets or sets the background layer 2 draw channel for the frozen section.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3383</c>)
    /// </summary>
    public ushort Bg2DrawChannelCurrent { get; set; }

    /// <summary>
    /// Gets or sets the background layer 2 draw channel for the unfrozen section.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3384</c>)
    /// </summary>
    public ushort Bg2DrawChannelUnfrozen { get; set; }

    /// <summary>
    /// Gets or sets the navigation layer captured at <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3385</c>)
    /// </summary>
    public sbyte NavLayer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that <c>TableUpdateLayout()</c> has locked the column geometry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3386</c>)
    /// </summary>
    public bool IsLayoutLocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the table is between <c>TableBeginRow()</c> and
    /// <c>TableEndRow()</c>. (<c>ThirdParty/imgui/imgui_internal.h:3387</c>)
    /// </summary>
    public bool IsInsideRow { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the table buffers were freshly initialized this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3388</c>)
    /// </summary>
    public bool IsInitializing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that a column topology reconcile is in progress.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3389</c>)
    /// </summary>
    public bool IsReconcileMode { get; set; }

    /// <summary>
    /// Gets or sets whether the table is newly created on this frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3454</c>)
    /// </summary>
    public bool IsNewTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the sort specifications must be rebuilt.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3390</c>)
    /// </summary>
    public bool IsSortSpecsDirty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the first row carried the header flag.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3391</c>)
    /// </summary>
    public bool IsUsingHeaders { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the default context menu is open.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3392</c>)
    /// </summary>
    public bool IsContextPopupOpen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the default context menu is suppressed so the application
    /// can submit its own through <c>TableBeginContextMenuPopup()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3393</c>)
    /// </summary>
    public bool DisableDefaultContextMenu { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that layout settings are requested to be loaded.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3394</c>)
    /// </summary>
    public bool IsSettingsRequestLoad { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that table settings changed and must be written back into the
    /// settings data. (<c>ThirdParty/imgui/imgui_internal.h:3395</c>)
    /// </summary>
    public bool IsSettingsDirty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the display order is unchanged from the default order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3396</c>)
    /// </summary>
    public bool IsDefaultDisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that enabled and visible states are unchanged from their
    /// defaults. (<c>ThirdParty/imgui/imgui_internal.h:3397</c>)
    /// </summary>
    public bool IsDefaultVisibility { get; set; }

    /// <summary>
    /// Gets or sets a request to call <c>TableResetSettings()</c> from <c>BeginTable()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3398</c>)
    /// </summary>
    public bool IsResetAllRequest { get; set; }

    /// <summary>
    /// Gets or sets a request to reset the column display order.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3399</c>)
    /// </summary>
    public bool IsResetDisplayOrderRequest { get; set; }

    /// <summary>
    /// Gets or sets a request to reset the column visibility.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3400</c>)
    /// </summary>
    public bool IsResetVisibilityRequest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the frame has passed the frozen-row boundary.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3401</c>)
    /// </summary>
    public bool IsUnfrozenRows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the application did not explicitly choose a sizing policy.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3402</c>)
    /// </summary>
    public bool IsDefaultSizingPolicy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that an active identifier was alive before the table started.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3403</c>)
    /// </summary>
    public bool IsActiveIdAliveBeforeTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that an active identifier was created inside the table.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3404</c>)
    /// </summary>
    public bool IsActiveIdInTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that any instance of the table had a vertical scrollbar on the
    /// current frame. (<c>ThirdParty/imgui/imgui_internal.h:3405</c>)
    /// </summary>
    public bool HasScrollbarYCurr { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that any instance of the table had a vertical scrollbar on the
    /// previous frame. (<c>ThirdParty/imgui/imgui_internal.h:3406</c>)
    /// </summary>
    public bool HasScrollbarYPrev { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the transient buffers have been compacted by garbage
    /// collection. (<c>ThirdParty/imgui/imgui_internal.h:3407</c>)
    /// </summary>
    public bool MemoryCompacted { get; set; }

    /// <summary>
    /// Gets or sets the backup of the inner window skip-items state taken at the end of
    /// <c>BeginTable()</c>, because the state is overwritten per column during the frame.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3408</c>)
    /// </summary>
    public bool HostSkipItems { get; set; }
}

/// <summary>
/// One serialized per-column table settings entry, stored inside an
/// <see cref="ImGuiTableSettings"/> record (<c>ThirdParty/imgui/imgui_internal.h:3448-3471</c>).
/// </summary>
/// <remarks>
/// Canonical packs <c>SortDirection</c>, <c>IsEnabled</c>, <c>IsStretch</c>, and <c>IsLoaded</c> into
/// bitfields; this port declares each as its own semantic member, so the managed layout is not
/// byte-for-byte equal. Canonical stores these entries as a trailing value array inside the settings
/// chunk, so this port declares a reference type and exposes <see cref="CopyFrom"/> for callers that
/// must reproduce the canonical value copy.
/// </remarks>
public sealed class ImGuiTableColumnSettings
{
    /// <summary>
    /// Gets or sets the saved width for a fixed column or the saved weight for a stretched column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3450</c>)
    /// </summary>
    public float WidthOrWeight { get; set; }

    /// <summary>
    /// Gets or sets the column identifier used to match the entry against live columns.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3451</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the saved column index, or -1 when unset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3452</c>)
    /// </summary>
    public short Index { get; set; } = -1;

    /// <summary>
    /// Gets or sets the saved display order, or -1 when unset.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3453</c>)
    /// </summary>
    public short DisplayOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the saved sort order, or -1 when the column is not sorted.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3454</c>)
    /// </summary>
    public short SortOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the saved sort direction.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3455</c>)
    /// </summary>
    public ImGuiSortDirection SortDirection { get; set; }

    /// <summary>
    /// Gets or sets the saved visibility, written as the visible field of the layout file; -1 means
    /// unset and 0 or 1 are the explicit states.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3456</c>)
    /// </summary>
    public sbyte IsEnabled { get; set; } = -1;

    /// <summary>
    /// Gets or sets a value indicating that the saved width applies to a stretched column.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3457</c>)
    /// </summary>
    public bool IsStretch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the entry was matched to a live column while loading.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3458</c>)
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Copies every field of another column settings entry into this one.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3448-3471</c>)
    /// </summary>
    /// <param name="source">The entry to snapshot.</param>
    /// <remarks>
    /// Canonical stores the column entries as a trailing value array and recycles them by rewriting
    /// fields in place. Because this port represents an entry as a reference type, a plain assignment
    /// would alias instead of snapshot, so recycling paths that need the canonical value semantics
    /// call this method.
    /// </remarks>
    public void CopyFrom(ImGuiTableColumnSettings source)
    {
        WidthOrWeight = source.WidthOrWeight;
        ID = source.ID;
        Index = source.Index;
        DisplayOrder = source.DisplayOrder;
        SortOrder = source.SortOrder;
        SortDirection = source.SortDirection;
        IsEnabled = source.IsEnabled;
        IsStretch = source.IsStretch;
        IsLoaded = source.IsLoaded;
    }
}

/// <summary>
/// Serialized layout state for one table, persisted to and loaded from the layout file
/// (<c>ThirdParty/imgui/imgui_internal.h:3474-3486</c>).
/// </summary>
/// <remarks>
/// <para>
/// Canonical stores the header followed by its trailing <see cref="ImGuiTableColumnSettings"/> array
/// in one chunk of an <c>ImChunkStream</c>. This port declares a reference type with the column array
/// as <see cref="ColumnSettings"/>, mirroring the existing
/// <see cref="ImGuiWindowSettings"/> treatment of its variable-length name.
/// </para>
/// <para>
/// Canonical packs <c>WantApply</c> into a single bit; this port declares it as a
/// <see cref="bool"/> member, so the managed layout is deliberately not byte-for-byte equal. The
/// canonical constructor zeroes the structure, which the port reproduces with field initializers.
/// </para>
/// </remarks>
public sealed class ImGuiTableSettings
{
    /// <summary>
    /// Gets or sets the table identifier; zero invalidates and deletes the entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3476</c>)
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the table flags that indicate which data categories this entry saves, using the
    /// resizable, reorderable, sortable, and hideable flags.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3477</c>)
    /// </summary>
    public ImGuiTableFlags SaveFlags { get; set; }

    /// <summary>
    /// Gets or sets the reference scale used to rescale columns when the font size or DPI changes.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3478</c>)
    /// </summary>
    public float RefScale { get; set; }

    /// <summary>
    /// Gets or sets the number of columns in the saved entry.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3479</c>)
    /// </summary>
    public short ColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of columns this entry can store; an entry may be recycled for a
    /// table with fewer columns but not more.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3480</c>)
    /// </summary>
    public short ColumnsCountMax { get; set; }

    /// <summary>
    /// Gets or sets the packed date on which the entry was last used.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3481</c>)
    /// </summary>
    public ImGuiPackedDate LastUsedDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the entry was loaded from the layout file and must be
    /// merged into an already running context.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3482</c>)
    /// </summary>
    public bool WantApply { get; set; }

    /// <summary>
    /// Gets the trailing per-column entries. Canonical returns a pointer immediately after the header
    /// through <c>GetColumnSettings()</c>.
    /// (<c>ThirdParty/imgui/imgui_internal.h:3485</c>)
    /// </summary>
    public List<ImGuiTableColumnSettings> ColumnSettings { get; } = new();
}

#endregion
