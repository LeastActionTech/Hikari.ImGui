// The MIT License (MIT)
// Copyright (c) 2014-2026 Omar Cornut
// Copyright (c) 2026 Least Action Technologies Inc.

#nullable enable

using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace Hikari.ImGui;

#region Callbacks

/// <summary>
/// Receives a window size-constraint request so the application can clamp or override the size of a
/// window queued with <c>SetNextWindowSizeConstraints()</c>
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
/// locations.
/// </summary>
/// <param name="file">The source file that declares the section.</param>
/// <param name="line">The source line that declares the section.</param>
/// <param name="section">The section label.</param>
public delegate void ImGuiDemoMarkerCallback(string file, int line, string section);

/// <summary>
/// Receives recoverable user errors reported through <c>ErrorCheck()</c> and its variants
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
/// indentation explicit.
/// </summary>
/// <remarks>
/// This is a plain value type with no identity; assignment copies the single component.
/// </remarks>
public struct ImVec1
{
    /// <summary>
    /// Gets or sets the single component.
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
/// geometry in settings.
/// </summary>
/// <remarks>
/// Coordinates saturate at the 16-bit range; the type is intended for persisted settings, not for
/// runtime layout arithmetic.
/// </remarks>
public struct ImVec2ih
{
    /// <summary>
    /// Gets or sets the horizontal component.
    /// </summary>
    public short X { get; set; }

    /// <summary>
    /// Gets or sets the vertical component.
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
/// entries.
/// </summary>
/// <remarks>
/// The current date is supplied by the platform backend. A default-constructed value is invalid;
/// <see cref="IsValid"/> reports whether all three components are non-zero.
/// </remarks>
public struct ImGuiPackedDate
{
    /// <summary>
    /// Gets or sets the year offset from 2000, stored in 7 bits.
    /// </summary>
    public ushort Year { get; set; }

    /// <summary>
    /// Gets or sets the month in the range 1.12, stored in 4 bits.
    /// </summary>
    public ushort Month { get; set; }

    /// <summary>
    /// Gets or sets the day of month in the range 1.31, stored in 5 bits.
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
    /// </summary>
    /// <returns><see langword="true"/> when all three components are non-zero.</returns>
    public bool IsValid()
    {
        return Year != 0 && Month != 0 && Day != 0;
    }

    /// <summary>
    /// Re-packs the date into its <c>YYYYMMDD</c> numeric form.
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
/// may emit character input.
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
    /// </summary>
    public uint[] Data { get; } = new uint[((int)ImGuiKey.NamedKey_COUNT + 31) >> 5];

    /// <summary>
    /// Clears every bit in the array.
    /// </summary>
    public void ClearAllBits()
    {
        Array.Clear(Data, 0, Data.Length);
    }

    /// <summary>
    /// Sets every bit in the array.
    /// </summary>
    public void SetAllBits()
    {
        Array.Fill(Data, uint.MaxValue);
    }

    /// <summary>
    /// Tests the bit for a named key.
    /// </summary>
    /// <param name="key">The named key, expressed as an <see cref="ImGuiKey"/> value.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    public bool TestBit(ImGuiKey key)
    {
        int index = (int)key - (int)ImGuiKey.NamedKey_BEGIN;
        return (Data[index >> 5] & (1u << (index & 31))) != 0;
    }

    /// <summary>
    /// Sets the bit for a named key.
    /// </summary>
    /// <param name="key">The named key, expressed as an <see cref="ImGuiKey"/> value.</param>
    public void SetBit(ImGuiKey key)
    {
        int index = (int)key - (int)ImGuiKey.NamedKey_BEGIN;
        Data[index >> 5] |= 1u << (index & 31);
    }

    /// <summary>
    /// Clears the bit for a named key.
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
/// </summary>
/// <remarks>
/// The backing word array grows only through <see cref="Create"/>. Indexing a bit outside the
/// created range throws, matching the canonical debug assertion.
/// </remarks>
public sealed class ImBitVector
{
    /// <summary>
    /// Gets the backing 32-bit words that hold the bits.
    /// </summary>
    public List<uint> Storage { get; } = new();

    /// <summary>
    /// Resizes the backing storage to hold <paramref name="size"/> bits and clears every bit.
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
    /// Releases the backing storage.
    /// </summary>
    public void Clear()
    {
        Storage.Clear();
    }

    /// <summary>
    /// Tests one bit.
    /// </summary>
    /// <param name="n">The zero-based bit index.</param>
    /// <returns><see langword="true"/> when the bit is set.</returns>
    public bool TestBit(int n)
    {
        return (Storage[n >> 5] & (1u << (n & 31))) != 0;
    }

    /// <summary>
    /// Sets one bit.
    /// </summary>
    /// <param name="n">The zero-based bit index.</param>
    public void SetBit(int n)
    {
        Storage[n >> 5] |= 1u << (n & 31);
    }

    /// <summary>
    /// Clears one bit.
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
/// <c>PushStyleVar()</c> can save and restore it.
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
    /// this in 8 bits).
    /// </summary>
    public uint Count { get; set; }

    /// <summary>
    /// Gets or sets the data type of the variable (canonical stores this in 8 bits).
    /// </summary>
    public ImGuiDataType DataType { get; set; }

    /// <summary>
    /// Gets or sets the byte offset of the variable inside <see cref="ImGuiStyle"/> (canonical
    /// stores this in 16 bits).
    /// </summary>
    public uint Offset { get; set; }
}

/// <summary>
/// One entry of the color stack: the modified color index and the value that must be restored by
/// <c>PopStyleColor()</c>.
/// </summary>
/// <remarks>
/// Instances are owned by <see cref="ImGuiContext.ColorStack"/> and mutated in place; they are
/// reference types so a stack element can be updated without replacing the whole entry.
/// </remarks>
public sealed class ImGuiColorMod
{
    /// <summary>
    /// Gets or sets the style color that was modified.
    /// </summary>
    public ImGuiCol Col { get; set; }

    /// <summary>
    /// Gets or sets the value that was present before the push and is restored on pop.
    /// </summary>
    public ImVec4 BackupValue { get; set; }
}

/// <summary>
/// One entry of the style-variable stack: the modified variable index and a backup of its previous
/// value.
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
    /// </summary>
    public ImGuiStyleVar VarIdx { get; set; }

    /// <summary>
    /// Gets the integer backup components used when the variable holds integer data.
    /// </summary>
    public int[] BackupInt { get; } = new int[2];

    /// <summary>
    /// Gets the floating-point backup components used when the variable holds float or vector data.
    /// </summary>
    public float[] BackupFloat { get; } = new float[2];
}

/// <summary>
/// Opaque eight-byte storage large enough to hold any <see cref="ImGuiDataType"/> value
/// </summary>
/// <remarks>
/// The buffer carries no type tag; callers must know the data type they stored. It is used for the
/// activation-time backup of drag/slider values and for the zero value of each data type.
/// </remarks>
public sealed class ImGuiDataTypeStorage
{
    /// <summary>
    /// Gets the raw eight-byte payload.
    /// </summary>
    public byte[] Data { get; } = new byte[8];
}

/// <summary>
/// Describes one <see cref="ImGuiDataType"/> for the scalar widget and input-text paths: the payload
/// size, a human-readable name, and the default printf-style print and scan formats
/// </summary>
/// <remarks>
/// The canonical structure is a plain aggregate populated by the file-scope <c>GDataTypeInfo</c>
/// table. This port keeps it as a reference type
/// with a constructor so the table can be built once as a static array, matching the canonical
/// one-time initialization.
/// </remarks>
public sealed class ImGuiDataTypeInfo
{
    /// <summary>
    /// Gets the size of the payload in bytes.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Gets the human-readable type name used by diagnostics and the metrics window.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the default printf-style format used to print a value of this type.
    /// </summary>
    public string PrintFmt { get; }

    /// <summary>
    /// Gets the format used to scan a value of this type back from text.
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
/// </summary>
/// <remarks>
/// The canonical type is a union of an integer, a float, and a pointer. This port exposes the three
/// interpretations as separate members; the storage holds no type tag, so only the member matching
/// the accessor used to create the entry is meaningful.
/// </remarks>
public sealed class ImGuiStoragePair
{
    /// <summary>
    /// Gets or sets the lookup key.
    /// </summary>
    public uint Key { get; set; }

    /// <summary>
    /// Gets or sets the integer interpretation of the value.
    /// </summary>
    public int ValueInt { get; set; }

    /// <summary>
    /// Gets or sets the floating-point interpretation of the value.
    /// </summary>
    public float ValueFloat { get; set; }

    /// <summary>
    /// Gets or sets the pointer interpretation of the value.
    /// </summary>
    public IntPtr ValuePointer { get; set; }
}

/// <summary>
/// A key-to-value store optimized for efficient lookup: entries are kept contiguous and sorted by
/// key so that reads use binary search and writes are rare
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
    /// Gets the sorted-by-key contiguous pair buffer.
    /// </summary>
    public List<ImGuiStoragePair> Data { get; } = new();

    /// <summary>
    /// Removes every stored pair.
    /// </summary>
    public void Clear()
    {
        Data.Clear();
    }

    /// <summary>
    /// Returns the integer stored for a key, or a default when the key is absent. The lookup never
    /// inserts.
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
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The integer to store.</param>
    public void SetInt(uint key, int value)
    {
        GetOrAddPair(key).ValueInt = value;
    }

    /// <summary>
    /// Returns the boolean stored for a key, or a default when the key is absent.
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The value returned when the key is absent.</param>
    /// <returns>The stored or default boolean.</returns>
    public bool GetBool(uint key, bool defaultValue = false)
    {
        return GetInt(key, defaultValue ? 1 : 0) != 0;
    }

    /// <summary>
    /// Stores a boolean for a key.
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The boolean to store.</param>
    public void SetBool(uint key, bool value)
    {
        SetInt(key, value ? 1 : 0);
    }

    /// <summary>
    /// Returns the float stored for a key, or a default when the key is absent. The lookup never
    /// inserts.
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
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The float to store.</param>
    public void SetFloat(uint key, float value)
    {
        GetOrAddPair(key).ValueFloat = value;
    }

    /// <summary>
    /// Returns the pointer stored for a key, or <see cref="IntPtr.Zero"/> when the key is absent.
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
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The integer used to initialize a newly inserted pair.</param>
    /// <returns>The stored pair, whose lifetime is bounded by the store.</returns>
    /// <remarks>
    /// A newly inserted pair carries <paramref name="defaultValue"/> in its integer interpretation, so a
    /// caller that reads the reference before writing observes the caller-supplied default rather than
    /// zero.
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
    /// </summary>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">The float used to initialize a newly inserted pair.</param>
    /// <returns>The stored pair, whose lifetime is bounded by the store.</returns>
    /// <remarks>
    /// A newly inserted pair carries <paramref name="defaultValue"/> in its floating-point interpretation,
    /// so a caller that reads the reference before writing observes the caller-supplied default rather
    /// than zero.
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
    /// </summary>
    public void BuildSortByKey()
    {
        Data.Sort(static (lhs, rhs) => lhs.Key.CompareTo(rhs.Key));
    }

    /// <summary>
    /// Exchanges the pair buffers of two stores.
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
/// requirement.
/// </summary>
/// <remarks>
/// The core stores one instance per named key in <see cref="ImGuiContext.KeysOwnerData"/>. The
/// canonical "no owner" value is zero.
/// </remarks>
public sealed class ImGuiKeyOwnerData
{
    /// <summary>
    /// Gets or sets the owner identifier observed during the current frame.
    /// </summary>
    public uint OwnerCurr { get; set; }

    /// <summary>
    /// Gets or sets the owner identifier to apply in the next frame.
    /// </summary>
    public uint OwnerNext { get; set; }

    /// <summary>
    /// Gets or sets whether reading this key requires an explicit owner until the end of the frame.
    /// </summary>
    public bool LockThisFrame { get; set; }

    /// <summary>
    /// Gets or sets whether reading this key requires an explicit owner until the key is released.
    /// When set, <see cref="LockThisFrame"/> is always set as well.
    /// </summary>
    public bool LockUntilRelease { get; set; }
}

/// <summary>
/// One entry of the key-chord routing table: the owner desired for a specific key-and-modifiers
/// combination.
/// </summary>
/// <remarks>
/// The canonical type is stored in dense lists owned by <see cref="ImGuiKeyRoutingTable"/>; this
/// port uses a reference type so an entry can be updated through the list indexer.
/// </remarks>
public sealed class ImGuiKeyRoutingData
{
    /// <summary>
    /// Gets or sets the index of the next entry that shares the same key.
    /// </summary>
    public short NextEntryIndex { get; set; } = -1;

    /// <summary>
    /// Gets or sets the modifier bits stored with the chord; modifiers require 16 bits.
    /// </summary>
    public ushort Mods { get; set; }

    /// <summary>
    /// Gets or sets the score computed for the current owner, used for debug display only.
    /// </summary>
    public ushort RoutingCurrScore { get; set; }

    /// <summary>
    /// Gets or sets the score computed for the next owner; lower is better and zero is perfect.
    /// </summary>
    public ushort RoutingNextScore { get; set; }

    /// <summary>
    /// Gets or sets the owner routing currently in effect. Zero means no owner.
    /// </summary>
    public uint RoutingCurr { get; set; }

    /// <summary>
    /// Gets or sets the owner routing to apply on the next frame. Zero means no owner.
    /// </summary>
    public uint RoutingNext { get; set; }
}

/// <summary>
/// The key-chord routing table: for each named key it stores a chain of entries that describe the
/// desired owner for every observed modifier combination
/// </summary>
/// <remarks>
/// Exactly one instance lives in <see cref="ImGuiContext.KeysRoutingTable"/>. The two entry lists
/// form a double buffer so that rebuilding the routing for the next frame does not reallocate.
/// </remarks>
public sealed class ImGuiKeyRoutingTable
{
    /// <summary>
    /// Gets the index of the first entry for each named key, or -1 when the key has no entries.
    /// </summary>
    public short[] Index { get; } = CreateIndex();

    /// <summary>
    /// Gets the routing entries for the current frame.
    /// </summary>
    public List<ImGuiKeyRoutingData> Entries { get; } = new();

    /// <summary>
    /// Gets the routing entries accumulated for the next frame.
    /// </summary>
    public List<ImGuiKeyRoutingData> EntriesNext { get; } = new();

    /// <summary>
    /// Resets every key index to -1 and empties both entry lists.
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
/// </summary>
public sealed class ImFontStackData
{
    /// <summary>
    /// Gets or sets the bound font, or <see langword="null"/>.
    /// </summary>
    public ImFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the font size before scaling, equivalent to <c>style.FontSizeBase</c>.
    /// </summary>
    public float FontSizeBeforeScaling { get; set; }

    /// <summary>
    /// Gets or sets the font size after scaling, equivalent to the context font size.
    /// </summary>
    public float FontSizeAfterScaling { get; set; }
}

#endregion

#region Text helpers

/// <summary>
/// A growable UTF-8 text buffer used for accumulating log and settings text
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
    /// </summary>
    public static readonly byte[] EmptyString = new byte[1];

    /// <summary>
    /// Gets the backing byte buffer.
    /// </summary>
    public List<byte> Buf { get; } = new();

    /// <summary>
    /// Removes every byte from the buffer.
    /// </summary>
    public void Clear()
    {
        Buf.Clear();
    }

    /// <summary>
    /// Appends the UTF-8 bytes of a string to the buffer.
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
    /// (, )
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
    /// </summary>
    public int Size => Buf.Count;

    /// <summary>
    /// Gets a value indicating whether the buffer holds no text.
    /// </summary>
    public bool Empty => Buf.Count == 0;
}

/// <summary>
/// A line index over an external text buffer: it records the byte offset at which each line begins
/// and the end offset of the final line.
/// </summary>
/// <remarks>
/// The index does not own the text it describes; the owner must append line offsets whenever it
/// appends text, or rebuild the index.
/// </remarks>
public sealed class ImGuiTextIndex
{
    /// <summary>
    /// Gets the byte offset at which each line begins.
    /// </summary>
    public List<int> Offsets { get; } = new();

    /// <summary>
    /// Gets or sets the end offset of the text described by this index, because the index does not
    /// own the text buffer.
    /// </summary>
    public int EndOffset { get; set; }

    /// <summary>
    /// Empties the line offsets and resets the end offset.
    /// </summary>
    public void Clear()
    {
        Offsets.Clear();
        EndOffset = 0;
    }

    /// <summary>
    /// Gets the number of indexed lines.
    /// </summary>
    /// <returns>The count of recorded line-start offsets.</returns>
    public int Size() => Offsets.Count;

    /// <summary>
    /// Gets the offset at which a line begins.
    /// </summary>
    /// <param name="line">The zero-based line index.</param>
    /// <returns>
    /// The line's start offset, or zero when the line index is beyond the indexed range, mirroring
    /// the canonical fallback to the base pointer.
    /// </returns>
    public int GetLineBegin(int line) => line < Offsets.Count ? Offsets[line] : 0;

    /// <summary>
    /// Gets the offset at which a line ends, excluding the line break.
    /// </summary>
    /// <param name="line">The zero-based line index.</param>
    /// <returns>
    /// The offset of the line break that terminates the line, or <see cref="EndOffset"/> for the final
    /// indexed line, mirroring the canonical <c>Offsets[n + 1] - 1</c> expression.
    /// </returns>
    public int GetLineEnd(int line) => line + 1 < Offsets.Count ? Offsets[line + 1] - 1 : EndOffset;

    /// <summary>
    /// Extends the line index after text has been appended to the indexed buffer.
    /// (, )
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
/// </summary>
/// <remarks>
/// Exactly one member is meaningful: the pointer constructor leaves <see cref="Index"/> at -1 and
/// the index constructor leaves <see cref="Ptr"/> at <see cref="IntPtr.Zero"/>.
/// </remarks>
public sealed class ImGuiPtrOrIndex
{
    /// <summary>
    /// Gets or sets the pointer form of the reference, or <see cref="IntPtr.Zero"/> when unused.
    /// </summary>
    public IntPtr Ptr { get; set; }

    /// <summary>
    /// Gets or sets the index form of the reference, or -1 when unused.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Initializes the record from a pointer.
    /// </summary>
    /// <param name="ptr">The pointer to store.</param>
    public ImGuiPtrOrIndex(IntPtr ptr)
    {
        Ptr = ptr;
        Index = -1;
    }

    /// <summary>
    /// Initializes the record from a pool index.
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
/// </summary>
public sealed class ImGuiShrinkWidthItem
{
    /// <summary>
    /// Gets or sets the item index being measured.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the current width.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Gets or sets the width before shrinking.
    /// </summary>
    public float InitialWidth { get; set; }
}

/// <summary>
/// Backs <c>IsItemDeactivated()</c> and <c>IsItemDeactivatedAfterEdit()</c> by remembering the item
/// that most recently deactivated and when.
/// </summary>
public sealed class ImGuiDeactivatedItemData
{
    /// <summary>
    /// Gets or sets the identifier of the item that recently deactivated.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the frame count at which the deactivation elapsed.
    /// </summary>
    public int ElapseFrame { get; set; }

    /// <summary>
    /// Gets or sets whether the item value had been edited before it deactivated.
    /// </summary>
    public bool HasBeenEditedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the deactivation data is still observable by the application.
    /// </summary>
    public bool IsAlive { get; set; }
}

/// <summary>
/// One entry of the focus-scope stack, pairing a focus scope with the window that declares it
/// </summary>
public sealed class ImGuiFocusScopeData
{
    /// <summary>
    /// Gets or sets the focus scope identifier.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the window that owns the focus scope.
    /// </summary>
    public uint WindowID { get; set; }
}

/// <summary>
/// One entry of the localization table: a localization key and its translated text
/// </summary>
public sealed class ImGuiLocEntry
{
    /// <summary>
    /// Gets or sets the localization key.
    /// </summary>
    public ImGuiLocKey Key { get; set; }

    /// <summary>
    /// Gets or sets the translated text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

#endregion

#region Group, menu, error, and window-stack records

/// <summary>
/// Saved cursor and interaction state for one <c>BeginGroup()</c>/<c>EndGroup()</c> nesting level
/// </summary>
/// <remarks>
/// Group state is deliberately not inherited by <c>Begin()</c>; each window starts with an empty
/// group stack.
/// </remarks>
public sealed class ImGuiGroupData
{
    /// <summary>
    /// Gets or sets the identifier of the window the group belongs to.
    /// </summary>
    public uint WindowID { get; set; }

    /// <summary>
    /// Gets or sets the cursor position backup.
    /// </summary>
    public ImVec2 BackupCursorPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor maximum-position backup.
    /// </summary>
    public ImVec2 BackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the previous-line cursor position backup.
    /// </summary>
    public ImVec2 BackupCursorPosPrevLine { get; set; }

    /// <summary>
    /// Gets or sets the indentation backup.
    /// </summary>
    public ImVec1 BackupIndent { get; set; }

    /// <summary>
    /// Gets or sets the group-offset backup.
    /// </summary>
    public ImVec1 BackupGroupOffset { get; set; }

    /// <summary>
    /// Gets or sets the current-line size backup.
    /// </summary>
    public ImVec2 BackupCurrLineSize { get; set; }

    /// <summary>
    /// Gets or sets the current-line text baseline offset backup.
    /// </summary>
    public float BackupCurrLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets the backup of the active-id aliveness value.
    /// </summary>
    public uint BackupActiveIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the backup of <c>AnyIdHasBeenEditedThisFrame</c>.
    /// </summary>
    public bool BackupAnyIdHasBeenEditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets the backup of the deactivated-id aliveness value.
    /// </summary>
    public bool BackupDeactivatedIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the backup of the hovered-id aliveness value.
    /// </summary>
    public bool BackupHoveredIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the backup of the cursor same-line flag.
    /// </summary>
    public bool BackupIsSameLine { get; set; }

    /// <summary>
    /// Gets or sets whether the group emits a parent item that can be hovered or activated.
    /// </summary>
    public bool EmitItem { get; set; }
}

/// <summary>
/// Column width measurement for menu items. This is a short-lived helper for the menu code rather
/// than a general column implementation.
/// </summary>
/// <remarks>
/// Widths are measured during a frame and committed for the next one; the offsets are locked by the
/// update pass.
/// </remarks>
public sealed class ImGuiMenuColumns
{
    /// <summary>
    /// Gets or sets the total measured width.
    /// </summary>
    public uint TotalWidth { get; set; }

    /// <summary>
    /// Gets or sets the committed width for the next frame.
    /// </summary>
    public uint NextTotalWidth { get; set; }

    /// <summary>
    /// Gets or sets the spacing between columns.
    /// </summary>
    public ushort Spacing { get; set; }

    /// <summary>
    /// Gets or sets the offset of the icon column, always zero in this version.
    /// </summary>
    public ushort OffsetIcon { get; set; }

    /// <summary>
    /// Gets or sets the offset of the label column, locked by the update pass.
    /// </summary>
    public ushort OffsetLabel { get; set; }

    /// <summary>
    /// Gets or sets the offset of the shortcut column.
    /// </summary>
    public ushort OffsetShortcut { get; set; }

    /// <summary>
    /// Gets or sets the offset of the mark column.
    /// </summary>
    public ushort OffsetMark { get; set; }

    /// <summary>
    /// Gets the width accumulators for the icon, label, shortcut, and mark columns.
    /// </summary>
    public ushort[] Widths { get; } = new ushort[4];
}

/// <summary>
/// Snapshot of every recoverable stack size, used to detect missing end calls and to repair the
/// frame instead of crashing.
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
    /// </summary>
    public short SizeOfWindowStack { get; set; }

    /// <summary>
    /// Gets or sets the saved identifier-stack size of the current window.
    /// </summary>
    public short SizeOfIDStack { get; set; }

    /// <summary>
    /// Gets or sets the saved tree depth of the current window, not the tree-node stack size.
    /// </summary>
    public short SizeOfTreeStack { get; set; }

    /// <summary>
    /// Gets or sets the saved color-stack size.
    /// </summary>
    public short SizeOfColorStack { get; set; }

    /// <summary>
    /// Gets or sets the saved style-variable stack size.
    /// </summary>
    public short SizeOfStyleVarStack { get; set; }

    /// <summary>
    /// Gets or sets the saved font-stack size.
    /// </summary>
    public short SizeOfFontStack { get; set; }

    /// <summary>
    /// Gets or sets the saved focus-scope stack size.
    /// </summary>
    public short SizeOfFocusScopeStack { get; set; }

    /// <summary>
    /// Gets or sets the saved group-stack size.
    /// </summary>
    public short SizeOfGroupStack { get; set; }

    /// <summary>
    /// Gets or sets the saved item-flags stack size.
    /// </summary>
    public short SizeOfItemFlagsStack { get; set; }

    /// <summary>
    /// Gets or sets the saved begin-popup stack size.
    /// </summary>
    public short SizeOfBeginPopupStack { get; set; }

    /// <summary>
    /// Gets or sets the saved disabled-stack size.
    /// </summary>
    public short SizeOfDisabledStack { get; set; }
}

/// <summary>
/// Saved state for each window pushed onto the window stack
/// </summary>
public sealed class ImGuiWindowStackData
{
    /// <summary>
    /// Gets or sets the window that was pushed on the stack.
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the backup of the last-item data taken before entering the window.
    /// </summary>
    public ImGuiLastItemData ParentLastItemDataBackup { get; set; } = new();

    /// <summary>
    /// Gets or sets the stack-size snapshot recorded when the window was entered.
    /// </summary>
    public ImGuiErrorRecoveryState StackSizesInBegin { get; set; } = new();

    /// <summary>
    /// Gets or sets whether a non-child window overrode the disabled state and must re-enable it on
    /// the way out.
    /// </summary>
    public bool DisabledOverrideReenable { get; set; }

    /// <summary>
    /// Gets or sets the alpha backup associated with the disabled override.
    /// </summary>
    public float DisabledOverrideReenableAlphaBackup { get; set; }

    /// <summary>
    /// Gets or sets the combo preview rect saved before opening a combo window.
    /// </summary>
    public ImRect ParentLastComboPreviewRect { get; set; }
}

/// <summary>
/// One entry of the tree-node stack, kept to support
/// <c>ImGuiTreeNodeFlags_NavLeftJumpsToParent</c>
/// </summary>
public sealed class ImGuiTreeNodeStackData
{
    /// <summary>
    /// Gets or sets the tree-node item identifier.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the tree-node flags for this node.
    /// </summary>
    public ImGuiTreeNodeFlags TreeFlags { get; set; }

    /// <summary>
    /// Gets or sets the item flags, used when navigation lands on the node.
    /// </summary>
    public ImGuiItemFlags ItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the navigation rectangle, used when navigation lands on the node.
    /// </summary>
    public ImRect NavRect { get; set; }

    /// <summary>
    /// Gets or sets the X coordinate at which tree lines start.
    /// </summary>
    public float DrawLinesX1 { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate at which lines to child nodes end.
    /// </summary>
    public float DrawLinesToNodesY2 { get; set; }

    /// <summary>
    /// Gets or sets the table column used for line drawing.
    /// </summary>
    public short DrawLinesTableColumn { get; set; }
}

#endregion

#region Item queue and stack records

/// <summary>
/// Status of the most recently submitted item, read by <c>IsItemHovered()</c>,
/// <c>IsItemActive()</c>, and the item-status helpers
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
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the item flags of the last submitted item.
    /// </summary>
    public ImGuiItemFlags ItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the status flags of the last submitted item.
    /// </summary>
    public ImGuiItemStatusFlags StatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the full item rectangle.
    /// </summary>
    public ImRect Rect { get; set; }

    /// <summary>
    /// Gets or sets the navigation scoring rectangle, which is not displayed.
    /// </summary>
    public ImRect NavRect { get; set; }

    /// <summary>
    /// Gets or sets the display rectangle; valid only when
    /// <see cref="ImGuiItemStatusFlags.HasDisplayRect"/> is set.
    /// </summary>
    public ImRect DisplayRect { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle at submit time; valid only when
    /// <see cref="ImGuiItemStatusFlags.HasClipRect"/> is set.
    /// </summary>
    public ImRect ClipRect { get; set; }

    /// <summary>
    /// Gets or sets the shortcut recorded at submit time; valid only when
    /// <see cref="ImGuiItemStatusFlags.HasShortcut"/> is set.
    /// </summary>
    public ImGuiKey Shortcut { get; set; }

    /// <summary>
    /// Copies every field of another last-item record into this one.
    /// </summary>
    /// <param name="source">The record to snapshot.</param>
    /// <remarks>
    /// Canonical <c>ImGuiLastItemData</c> is a value type, so the window-stack backup
    /// (<c>ImGuiWindowStackData::ParentLastItemDataBackup</c>, ) is a byte
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
    /// public item flags.
    /// </summary>
    public ImGuiNextItemDataFlags HasFlags { get; set; }

    /// <summary>
    /// Gets or sets the item flags to apply to the next item. Currently tested for
    /// <c>AllowOverlap</c> and <c>HasSelectionUserData</c>.
    /// </summary>
    public ImGuiItemFlags ItemFlagsSet { get; set; }

    /// <summary>
    /// Gets or sets the focus scope queued by <c>SetNextItemSelectionUserData()</c>.
    /// </summary>
    public uint FocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the selection user data queued by
    /// <c>SetNextItemSelectionUserData()</c>; -1 means invalid.
    /// </summary>
    public long SelectionUserData { get; set; } = -1;

    /// <summary>
    /// Gets or sets the width queued by <c>SetNextItemWidth()</c>.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    /// Gets or sets the shortcut queued by <c>SetNextItemShortcut()</c>.
    /// </summary>
    public ImGuiKey Shortcut { get; set; }

    /// <summary>
    /// Gets or sets the shortcut flags queued by <c>SetNextItemShortcut()</c>.
    /// </summary>
    public ImGuiInputFlags ShortcutFlags { get; set; }

    /// <summary>
    /// Gets or sets the open value queued by <c>SetNextItemOpen()</c>.
    /// </summary>
    public bool OpenVal { get; set; }

    /// <summary>
    /// Gets or sets the open condition queued by <c>SetNextItemOpen()</c>.
    /// </summary>
    public byte OpenCond { get; set; }

    /// <summary>
    /// Gets or sets the reference value used by
    /// <c>ImGuiInputTextFlags_ParseEmptyAsRefVal</c>.
    /// </summary>
    public ImGuiDataTypeStorage RefVal { get; set; } = new();

    /// <summary>
    /// Gets or sets the storage identifier queued by <c>SetNextItemStorageID()</c>.
    /// </summary>
    public uint StorageId { get; set; }

    /// <summary>
    /// Gets or sets the color marker queued by <c>SetNextItemColorMarker()</c>.
    /// </summary>
    public uint ColorMarker { get; set; }

    /// <summary>
    /// Clears the valid-value mask and the queued item flags.
    /// </summary>
    public void ClearFlags()
    {
        HasFlags = ImGuiNextItemDataFlags.None;
        ItemFlagsSet = ImGuiItemFlags.None;
    }
}

/// <summary>
/// Queued <c>SetNextWindow**</c> values applied when the next window begins
/// </summary>
/// <remarks>
/// Members below <see cref="HasFlags"/> are intentionally not cleared between windows, so callers
/// must always consult <see cref="HasFlags"/> rather than the value alone.
/// </remarks>
public sealed class ImGuiNextWindowData
{
    /// <summary>
    /// Gets or sets the bitmask of queued values that are valid.
    /// </summary>
    public ImGuiNextWindowDataFlags HasFlags { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowPos()</c>.
    /// </summary>
    public ImGuiCond PosCond { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowSize()</c>.
    /// </summary>
    public ImGuiCond SizeCond { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowCollapsed()</c>.
    /// </summary>
    public ImGuiCond CollapsedCond { get; set; }

    /// <summary>
    /// Gets or sets the condition queued by <c>SetNextWindowDockID()</c>.
    /// </summary>
    public ImGuiCond DockCond { get; set; }

    /// <summary>
    /// Gets or sets the requested window position.
    /// </summary>
    public ImVec2 PosVal { get; set; }

    /// <summary>
    /// Gets or sets the requested position pivot.
    /// </summary>
    public ImVec2 PosPivotVal { get; set; }

    /// <summary>
    /// Gets or sets the requested window size.
    /// </summary>
    public ImVec2 SizeVal { get; set; }

    /// <summary>
    /// Gets or sets the requested content size.
    /// </summary>
    public ImVec2 ContentSizeVal { get; set; }

    /// <summary>
    /// Gets or sets the requested scroll value.
    /// </summary>
    public ImVec2 ScrollVal { get; set; }

    /// <summary>
    /// Gets or sets the window-flags override. The canonical comment notes that it is honored only
    /// by <c>BeginTable()</c>.
    /// </summary>
    public ImGuiWindowFlags WindowFlags { get; set; }

    /// <summary>
    /// Gets or sets the child-flags override.
    /// </summary>
    public ImGuiChildFlags ChildFlags { get; set; }

    /// <summary>
    /// Gets or sets whether the window should undock when the queued dock position is applied.
    /// </summary>
    public bool PosUndock { get; set; }

    /// <summary>
    /// Gets or sets the requested collapsed state.
    /// </summary>
    public bool CollapsedVal { get; set; }

    /// <summary>
    /// Gets or sets the rectangle used when evaluating a size-constraint callback.
    /// </summary>
    public ImRect SizeConstraintRect { get; set; }

    /// <summary>
    /// Gets or sets the user size-constraint callback, or <see langword="null"/> when none is queued.
    /// </summary>
    public ImGuiSizeCallback? SizeCallback { get; set; }

    /// <summary>
    /// Gets or sets the user data passed to the size-constraint callback.
    /// </summary>
    public IntPtr SizeCallbackUserData { get; set; }

    /// <summary>
    /// Gets or sets the background alpha override.
    /// </summary>
    public float BgAlphaVal { get; set; }

    /// <summary>
    /// Gets or sets the requested viewport identifier.
    /// </summary>
    public uint ViewportId { get; set; }

    /// <summary>
    /// Gets or sets the requested dock node identifier.
    /// </summary>
    public uint DockId { get; set; }

    /// <summary>
    /// Gets or sets the requested window class.
    /// </summary>
    public ImGuiWindowClass WindowClass { get; set; } = new();

    /// <summary>
    /// Gets or sets the always-on menu-bar minimum offset, which is deliberately not cleared between
    /// windows.
    /// </summary>
    public ImVec2 MenuBarOffsetMinVal { get; set; }

    /// <summary>
    /// Gets or sets the requested refresh policy.
    /// </summary>
    public ImGuiWindowRefreshFlags RefreshFlagsVal { get; set; }

    /// <summary>
    /// Gets or sets window flags to set. Use <c>SetNextWindowFlags()</c>.
    /// </summary>
    public ImGuiWindowFlags WindowFlagsSet { get; set; }

    /// <summary>
    /// Gets or sets window flags to clear. Use <c>SetNextWindowFlags()</c>.
    /// </summary>
    public ImGuiWindowFlags WindowFlagsClear { get; set; }

    /// <summary>
    /// Gets or sets child flags to set. Use <c>SetNextWindowChildFlags()</c>.
    /// </summary>
    public ImGuiChildFlags ChildFlagsSet { get; set; }

    /// <summary>
    /// Gets or sets child flags to clear. Use <c>SetNextWindowChildFlags()</c>.
    /// </summary>
    public ImGuiChildFlags ChildFlagsClear { get; set; }

    /// <summary>
    /// Resets the valid-value mask to no queued data.
    /// </summary>
    public void ClearFlags()
    {
        HasFlags = ImGuiNextWindowDataFlags.None;
        WindowFlagsSet = WindowFlagsClear = ImGuiWindowFlags.None;
        ChildFlagsSet = ChildFlagsClear = ImGuiChildFlags.None;
    }

    /// <summary>
    /// Sets the queued dock identifier and condition flags.
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
/// </summary>
public sealed class ImGuiPopupData
{
    /// <summary>
    /// Gets or sets the popup identifier assigned by <c>OpenPopup()</c>.
    /// </summary>
    public uint PopupId { get; set; }

    /// <summary>
    /// Gets or sets the window resolved on <c>BeginPopup()</c>; it may remain unresolved.
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the navigation window to restore when the popup closes.
    /// </summary>
    public ImGuiWindow? RestoreNavWindow { get; set; }

    /// <summary>
    /// Gets or sets the navigation layer of the popup; -1 means none.
    /// </summary>
    public int ParentNavLayer { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame count at which the popup was opened.
    /// </summary>
    public int OpenFrameCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the parent identifier that distinguishes multiple menu sets.
    /// </summary>
    public uint OpenParentId { get; set; }

    /// <summary>
    /// Gets or sets the preferred popup position at open time.
    /// </summary>
    public ImVec2 OpenPopupPos { get; set; }

    /// <summary>
    /// Gets or sets the mouse position at open time.
    /// </summary>
    public ImVec2 OpenMousePos { get; set; }
}

/// <summary>
/// Best candidate produced by a navigation query, stored for initialization, local moves, local
/// visible moves, other moves, and tabbing.
/// </summary>
/// <remarks>
/// <see cref="RectRel"/> is deliberately not reset by <see cref="Clear"/> in canonical, because a
/// zero rectangle is a meaningful "no candidate" value for the other members.
/// </remarks>
public sealed class ImGuiNavItemData
{
    /// <summary>
    /// Gets or sets the best candidate window.
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the best candidate item identifier.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the best candidate focus scope identifier.
    /// </summary>
    public uint FocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the candidate bounding box in window-relative space.
    /// </summary>
    public ImRect RectRel { get; set; }

    /// <summary>
    /// Gets or sets the best candidate item flags.
    /// </summary>
    public ImGuiItemFlags ItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the box distance to the current navigation identifier.
    /// </summary>
    public float DistBox { get; set; }

    /// <summary>
    /// Gets or sets the center distance to the current navigation identifier.
    /// </summary>
    public float DistCenter { get; set; }

    /// <summary>
    /// Gets or sets the axial distance to the current navigation identifier.
    /// </summary>
    public float DistAxial { get; set; }

    /// <summary>
    /// Gets or sets the best candidate selection user data; -1 means invalid.
    /// </summary>
    public long SelectionUserData { get; set; } = -1;

    /// <summary>
    /// Resets every candidate field except <see cref="RectRel"/> and restores the distance fields to
    /// their maximum values.
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
/// callback to invoke.
/// </summary>
public sealed class ImGuiContextHook
{
    /// <summary>
    /// Gets or sets the unique hook identifier assigned by <c>AddContextHook()</c>.
    /// </summary>
    public uint HookId { get; set; }

    /// <summary>
    /// Gets or sets the hook event type.
    /// </summary>
    public ImGuiContextHookType Type { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the owning subsystem.
    /// </summary>
    public uint Owner { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked for the hook, or <see langword="null"/> when unused.
    /// </summary>
    public ImGuiContextHookCallback? Callback { get; set; }

    /// <summary>
    /// Gets or sets the opaque caller data passed to the callback.
    /// </summary>
    public IntPtr UserData { get; set; }
}

/// <summary>
/// Read-mostly data supplied to an <see cref="ImGuiSizeCallback"/> while the core resolves a
/// window's size.
/// </summary>
/// <remarks>
/// The core owns the instance and passes the same object to the callback. The application may write
/// only <see cref="DesiredSize"/>; the other members are read-only inputs.
/// </remarks>
public sealed class ImGuiSizeCallbackData
{
    /// <summary>
    /// Gets or sets the opaque pointer originally passed to
    /// <c>SetNextWindowSizeConstraints()</c>.
    /// </summary>
    public IntPtr UserData { get; set; }

    /// <summary>
    /// Gets or sets the window position for reference.
    /// </summary>
    public ImVec2 Pos { get; set; }

    /// <summary>
    /// Gets or sets the current window size.
    /// </summary>
    public ImVec2 CurrentSize { get; set; }

    /// <summary>
    /// Gets or sets the desired window size; writing this member restrains resizing.
    /// </summary>
    public ImVec2 DesiredSize { get; set; }
}

/// <summary>
/// Advanced docking hints applied to a window and inherited by its children
/// </summary>
/// <remarks>
/// Windows of different non-zero class identifiers cannot dock together. The class is copied into
/// the window when it begins, so later changes do not affect an already-open window.
/// </remarks>
public sealed class ImGuiWindowClass
{
    /// <summary>
    /// Gets or sets the user-defined class identifier; zero is the default class.
    /// </summary>
    public uint ClassId { get; set; }

    /// <summary>
    /// Gets or sets the parent viewport hint: -1 selects the default, zero requests no parent, and
    /// any other value requests a parent/child platform relationship.
    /// </summary>
    public uint ParentViewportId { get; set; } = uint.MaxValue;

    /// <summary>
    /// Gets or sets the parent window used when evaluating shortcut focus routes.
    /// </summary>
    public uint FocusRouteParentWindowId { get; set; }

    /// <summary>
    /// Gets or sets the viewport flags to set when this class owns a viewport.
    /// </summary>
    public ImGuiViewportFlags ViewportFlagsOverrideSet { get; set; }

    /// <summary>
    /// Gets or sets the viewport flags to clear when this class owns a viewport.
    /// </summary>
    public ImGuiViewportFlags ViewportFlagsOverrideClear { get; set; }

    /// <summary>
    /// Gets or sets the tab-item flags applied when the window is submitted into a dock tab bar.
    /// </summary>
    public ImGuiTabItemFlags TabItemFlagsOverrideSet { get; set; }

    /// <summary>
    /// Gets or sets the dock-node flags applied when the window is hosted by a dock node.
    /// </summary>
    public ImGuiDockNodeFlags DockNodeFlagsOverrideSet { get; set; }

    /// <summary>
    /// Gets or sets whether floating windows of this class always receive their own dock node.
    /// </summary>
    public bool DockingAlwaysTabBar { get; set; }

    /// <summary>
    /// Gets or sets whether windows of this class may dock and merge with unclassed windows.
    /// </summary>
    public bool DockingAllowUnclassed { get; set; } = true;

    /// <summary>
    /// Gets or sets opaque platform icon data.
    /// </summary>
    public IntPtr PlatformIconData { get; set; }
}

/// <summary>
/// Persistent per-window settings stored in the layout file
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
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the window position relative to its viewport; runtime positions are absolute.
    /// </summary>
    public ImVec2ih Pos { get; set; }

    /// <summary>
    /// Gets or sets the saved window size.
    /// </summary>
    public ImVec2ih Size { get; set; }

    /// <summary>
    /// Gets or sets the saved viewport position.
    /// </summary>
    public ImVec2ih ViewportPos { get; set; }

    /// <summary>
    /// Gets or sets the saved viewport identifier.
    /// </summary>
    public uint ViewportId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last known dock node, even when invisible, or zero.
    /// </summary>
    public uint DockId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the window class when one was specified.
    /// </summary>
    public uint ClassId { get; set; }

    /// <summary>
    /// Gets or sets the order within the dock node on same-frame reappearance.
    /// </summary>
    public short DockOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the packed last-used date.
    /// </summary>
    public ImGuiPackedDate LastUsedDate { get; set; }

    /// <summary>
    /// Gets or sets the saved collapsed state.
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Gets or sets the saved child-window state.
    /// </summary>
    public bool IsChild { get; set; }

    /// <summary>
    /// Gets or sets whether the entry was loaded from layout data and must be merged into the
    /// running context.
    /// </summary>
    public bool WantApply { get; set; }

    /// <summary>
    /// Gets or sets whether the entry must be invalidated and deleted.
    /// </summary>
    public bool WantDelete { get; set; }

    /// <summary>
    /// Gets or sets the window name carried by the settings entry.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Filtering and date-migration options accepted by
/// <see cref="ImGui.CleanupIniSettings"/>, used to prune stale `.ini` entries
/// </summary>
public sealed class ImGuiSettingsCleanupArgs
{
    /// <summary>
    /// Gets or sets a settings-type hash that restricts the operation to one handler, or zero to affect
    /// every handler that supports cleanup.
    /// </summary>
    public uint TypeHashFilter { get; set; }

    /// <summary>
    /// Gets or sets the age in months beyond which an entry is discarded; zero disables age-based discard.
    /// </summary>
    public int DiscardOlderThanMonths { get; set; }

    /// <summary>
    /// Gets or sets whether entries that carry no last-used date are discarded.
    /// </summary>
    public bool DiscardWhenMissingDate { get; set; }

    /// <summary>
    /// Gets or sets whether every entry is discarded, which is equivalent to calling
    /// <see cref="ImGui.ClearIniSettings"/> except that the <see cref="TypeHashFilter"/> still applies.
    /// </summary>
    public bool DiscardAll { get; set; }

    /// <summary>
    /// Gets or sets whether every supporting entry is stamped with the current session date.
    /// </summary>
    public bool SetCurrentSessionDateToAll { get; set; }

    /// <summary>
    /// Gets or sets whether only entries missing a date are stamped with the current session date.
    /// </summary>
    public bool SetCurrentSessionDateWhenMissingDate { get; set; }

    /// <summary>
    /// Gets or sets the unpacked date derived from <see cref="DiscardOlderThanMonths"/> once the cleanup
    /// pass begins; it is an implementation detail of the cleanup pass and not a caller option.
    /// </summary>
    public int _DiscardOlderThanDate { get; set; }
}

/// <summary>
/// One `.ini` section handler. Each handler owns one `[Type][Name]` section family and receives the
/// canonical read and write callbacks.
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
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hash of <see cref="TypeName"/>, used for identifier-based lookup.
    /// </summary>
    public uint TypeHash { get; set; }

    /// <summary>
    /// Gets or sets the callback that clears all settings data for this handler.
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler>? ClearAllFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked before reading begins, in registration order.
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler>? ReadInitFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when entering a new `[Type][Name]` entry, returning the entry
    /// object passed back to <see cref="ReadLineFn"/>, or null to ignore the entry's lines.
    /// </summary>
    public Func<ImGuiContext, ImGuiSettingsHandler, string, object?>? ReadOpenFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked for every line of text inside a read entry.
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler, object, string>? ReadLineFn { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked after reading ends, in registration order.
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler>? ApplyAllFn { get; set; }

    /// <summary>
    /// Gets or sets the callback that appends this handler's entries to the output buffer.
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler, ImGuiTextBuffer>? WriteAllFn { get; set; }

    /// <summary>
    /// Gets or sets the callback that prunes or stamps this handler's entries.
    /// </summary>
    public Action<ImGuiContext, ImGuiSettingsHandler, ImGuiSettingsCleanupArgs>? CleanupFn { get; set; }

    /// <summary>
    /// Gets or sets an opaque value association owned by the registering subsystem.
    /// </summary>
    public object? UserData { get; set; }
}

#endregion

#region Input events

/// <summary>
/// Payload of a mouse-position event.
/// </summary>
public sealed class ImGuiInputEventMousePos
{
    /// <summary>
    /// Gets or sets the absolute horizontal mouse position.
    /// </summary>
    public float PosX { get; set; }

    /// <summary>
    /// Gets or sets the absolute vertical mouse position.
    /// </summary>
    public float PosY { get; set; }

    /// <summary>
    /// Gets or sets the device that produced the event (mouse, touch, or pen).
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; }
}

/// <summary>
/// Payload of a mouse-wheel event.
/// </summary>
public sealed class ImGuiInputEventMouseWheel
{
    /// <summary>
    /// Gets or sets the horizontal wheel delta.
    /// </summary>
    public float WheelX { get; set; }

    /// <summary>
    /// Gets or sets the vertical wheel delta.
    /// </summary>
    public float WheelY { get; set; }

    /// <summary>
    /// Gets or sets the device that produced the event.
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; }
}

/// <summary>
/// Payload of a mouse-button transition event
/// </summary>
public sealed class ImGuiInputEventMouseButton
{
    /// <summary>
    /// Gets or sets the mouse button index.
    /// </summary>
    public int Button { get; set; }

    /// <summary>
    /// Gets or sets the new button state.
    /// </summary>
    public bool Down { get; set; }

    /// <summary>
    /// Gets or sets the device that produced the event.
    /// </summary>
    public ImGuiMouseSource MouseSource { get; set; }
}

/// <summary>
/// Payload of a mouse viewport-change event
/// </summary>
public sealed class ImGuiInputEventMouseViewport
{
    /// <summary>
    /// Gets or sets the reported hovered viewport identifier.
    /// </summary>
    public uint HoveredViewportID { get; set; }
}

/// <summary>
/// Payload of a keyboard or gamepad key transition event
/// </summary>
public sealed class ImGuiInputEventKey
{
    /// <summary>
    /// Gets or sets the key or gamepad button that changed.
    /// </summary>
    public ImGuiKey Key { get; set; }

    /// <summary>
    /// Gets or sets the new key state.
    /// </summary>
    public bool Down { get; set; }

    /// <summary>
    /// Gets or sets the analog value for analog gamepad keys.
    /// </summary>
    public float AnalogValue { get; set; }
}

/// <summary>
/// Payload of a Unicode text-input event
/// </summary>
public sealed class ImGuiInputEventText
{
    /// <summary>
    /// Gets or sets the Unicode codepoint that was typed.
    /// </summary>
    public uint Char { get; set; }
}

/// <summary>
/// Payload of an application focus event
/// </summary>
public sealed class ImGuiInputEventAppFocused
{
    /// <summary>
    /// Gets or sets the new application focus state.
    /// </summary>
    public bool Focused { get; set; }
}

/// <summary>
/// One queued input event, discriminated by <see cref="Type"/>, as produced by the backend and
/// drained during the frame.
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
    /// </summary>
    public ImGuiInputEventType Type { get; set; }

    /// <summary>
    /// Gets or sets the input source (mouse, keyboard, or gamepad).
    /// </summary>
    public ImGuiInputSource Source { get; set; }

    /// <summary>
    /// Gets or sets the sequentially increasing event identifier used to correlate events with other
    /// data.
    /// </summary>
    public uint EventId { get; set; }

    /// <summary>
    /// Gets the mouse-position payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MousePos"/>.
    /// </summary>
    public ImGuiInputEventMousePos MousePos { get; } = new();

    /// <summary>
    /// Gets the mouse-wheel payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MouseWheel"/>.
    /// </summary>
    public ImGuiInputEventMouseWheel MouseWheel { get; } = new();

    /// <summary>
    /// Gets the mouse-button payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MouseButton"/>.
    /// </summary>
    public ImGuiInputEventMouseButton MouseButton { get; } = new();

    /// <summary>
    /// Gets the mouse viewport payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.MouseViewport"/>.
    /// </summary>
    public ImGuiInputEventMouseViewport MouseViewport { get; } = new();

    /// <summary>
    /// Gets the key payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.Key"/>.
    /// </summary>
    public ImGuiInputEventKey Key { get; } = new();

    /// <summary>
    /// Gets the text payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.Text"/>.
    /// </summary>
    public ImGuiInputEventText Text { get; } = new();

    /// <summary>
    /// Gets the application-focus payload, valid when <see cref="Type"/> is
    /// <see cref="ImGuiInputEventType.Focus"/>.
    /// </summary>
    public ImGuiInputEventAppFocused AppFocused { get; } = new();

    /// <summary>
    /// Gets or sets whether the event was synthesized by the test engine rather than a backend.
    /// </summary>
    public bool AddedByTestEngine { get; set; }
}

#endregion

#region Input text

/// <summary>
/// Undo or redo record describing one contiguous text edit
/// </summary>
public sealed class StbUndoRecord
{
    /// <summary>
    /// Gets or sets the position of the edit.
    /// </summary>
    public int Where { get; set; }

    /// <summary>
    /// Gets or sets the number of inserted characters.
    /// </summary>
    public int InsertLength { get; set; }

    /// <summary>
    /// Gets or sets the number of deleted characters.
    /// </summary>
    public int DeleteLength { get; set; }

    /// <summary>
    /// Gets or sets the offset into the deleted-character buffer.
    /// </summary>
    public int CharStorage { get; set; }
}

/// <summary>
/// Undo and redo storage for one text-editing state
/// </summary>
public sealed class StbUndoState
{
    /// <summary>
    /// Gets the fixed ring of undo records.
    /// </summary>
    public StbUndoRecord[] UndoRec { get; } = CreateRecords();

    /// <summary>
    /// Gets the storage for characters removed by edits.
    /// </summary>
    public byte[] UndoChar { get; } = new byte[999];

    /// <summary>
    /// Gets or sets the current undo record index.
    /// </summary>
    public short UndoPoint { get; set; }

    /// <summary>
    /// Gets or sets the current redo record index.
    /// </summary>
    public short RedoPoint { get; set; }

    /// <summary>
    /// Gets or sets the current undo character-buffer index.
    /// </summary>
    public int UndoCharPoint { get; set; }

    /// <summary>
    /// Gets or sets the current redo character-buffer index.
    /// </summary>
    public int RedoCharPoint { get; set; }

    /// <summary>
    /// Creates the fixed undo-record array.
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
/// </summary>
/// <remarks>
/// The canonical type is configured as <c>STB_TexteditState</c> with an undo count of 99 and an
/// undo-character count of 999 for the <c>ImGuiInputTextState</c> string type.
/// </remarks>
public sealed class ImStbTexteditState
{
    /// <summary>
    /// Gets or sets the cursor position within the string.
    /// </summary>
    public int Cursor { get; set; }

    /// <summary>
    /// Gets or sets the selection start point.
    /// </summary>
    public int SelectStart { get; set; }

    /// <summary>
    /// Gets or sets the selection end point; equal endpoints mean no selection.
    /// </summary>
    public int SelectEnd { get; set; }

    /// <summary>
    /// Gets or sets the per-field insert mode.
    /// </summary>
    public byte InsertMode { get; set; }

    /// <summary>
    /// Gets or sets the page size in rows; it must be greater than zero for multiline page
    /// movement.
    /// </summary>
    public int RowCountPerPage { get; set; }

    /// <summary>
    /// Gets or sets the cursor-at-end-of-line flag, which is not implemented yet.
    /// </summary>
    public byte CursorAtEndOfLine { get; set; }

    /// <summary>
    /// Gets or sets whether the state was initialized by
    /// <c>stb_textedit_initialize_state</c>.
    /// </summary>
    public byte Initialized { get; set; }

    /// <summary>
    /// Gets or sets whether <see cref="PreferredX"/> holds a valid value.
    /// </summary>
    public byte HasPreferredX { get; set; }

    /// <summary>
    /// Gets or sets whether the editor runs in single-line mode.
    /// </summary>
    public byte SingleLine { get; set; }

    /// <summary>
    /// Gets or sets the first alignment padding byte.
    /// </summary>
    public byte Padding1 { get; set; }

    /// <summary>
    /// Gets or sets the second alignment padding byte.
    /// </summary>
    public byte Padding2 { get; set; }

    /// <summary>
    /// Gets or sets the third alignment padding byte.
    /// </summary>
    public byte Padding3 { get; set; }

    /// <summary>
    /// Gets or sets the preferred horizontal seek position for up/down cursor movement.
    /// </summary>
    public float PreferredX { get; set; }

    /// <summary>
    /// Gets or sets the undo and redo storage.
    /// </summary>
    public StbUndoState Undostate { get; set; } = new();
}

/// <summary>
/// Persistent editing state of the active <c>InputText()</c> field; it is the
/// <c>IMSTB_TEXTEDIT_STRING</c> type of the embedded text editor
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
    /// Gets or sets the parent context.
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the embedded text-editor state.
    /// </summary>
    public ImStbTexteditState Stb { get; set; } = new();

    /// <summary>
    /// Gets or sets a copy of the flags passed to <c>InputText()</c>.
    /// </summary>
    public ImGuiInputTextFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the widget identifier that owns this text state.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the UTF-8 length of <see cref="TextA"/> in bytes.
    /// </summary>
    public int TextLen { get; set; }

    /// <summary>
    /// Gets or sets the source of the text: the main buffer or the read-only user buffer.
    /// </summary>
    public string? TextSrc { get; set; }

    /// <summary>
    /// Gets the main UTF-8 buffer; its capacity is always at least the user buffer size.
    /// </summary>
    public List<byte> TextA { get; } = new();

    /// <summary>
    /// Gets the value restored when the user presses Escape, backed up when the field is focused.
    /// </summary>
    public List<byte> TextToRevertTo { get; } = new();

    /// <summary>
    /// Gets the temporary storage used to reconcile callback-driven undo state.
    /// </summary>
    public List<byte> CallbackTextBackup { get; } = new();

    /// <summary>
    /// Gets or sets the end-user buffer capacity, including the zero terminator.
    /// </summary>
    public int BufCapacity { get; set; }

    /// <summary>
    /// Gets or sets the horizontal manual offset and the vertical scroll amount.
    /// </summary>
    public ImVec2 Scroll { get; set; }

    /// <summary>
    /// Gets or sets the last line count, used for debug display only.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the word-wrapping width.
    /// </summary>
    public float WrapWidth { get; set; }

    /// <summary>
    /// Gets or sets the cursor blink timer.
    /// </summary>
    public float CursorAnim { get; set; }

    /// <summary>
    /// Gets or sets whether scrolling must follow the cursor.
    /// </summary>
    public bool CursorFollow { get; set; }

    /// <summary>
    /// Gets or sets whether vertical scrolling is centered on the cursor while a wrapped field is
    /// being resized.
    /// </summary>
    public bool CursorCenterY { get; set; }

    /// <summary>
    /// Gets or sets whether further drags are ignored after a double-click select-all.
    /// </summary>
    public bool SelectedAllMouseLock { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited since the field was activated.
    /// </summary>
    public bool EditedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited during the current frame.
    /// </summary>
    public bool EditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets whether the value was validated during the current frame.
    /// </summary>
    public bool ValidatedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets a request to reload the user buffer.
    /// </summary>
    public bool WantReloadUserBuf { get; set; }

    /// <summary>
    /// Gets or sets the direction of the last left/right cursor move.
    /// </summary>
    public sbyte LastMoveDirectionLR { get; set; }

    /// <summary>
    /// Gets or sets the pending reload selection start.
    /// </summary>
    public int ReloadSelectionStart { get; set; }

    /// <summary>
    /// Gets or sets the pending reload selection end.
    /// </summary>
    public int ReloadSelectionEnd { get; set; }

    /// <summary>
    /// Empties the text buffer and clamps the cursor. Canonical additionally zeroes the first byte of
    /// the buffer.
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
    /// </summary>
    public void ClearFreeMemory()
    {
        TextA.Clear();
        TextToRevertTo.Clear();
    }

    /// <summary>
    /// Forwards a movement, deletion, undo, or insert-mode key to the embedded editor state machine,
    /// requests cursor following, restarts the cursor blink, and records the horizontal direction of
    /// the move.
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
    /// state machine, and requests cursor following.
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
    /// <c>-1</c> when no such offset is pending.
    /// </summary>
    /// <returns>The preferred x offset, or <c>-1</c>.</returns>
    public float GetPreferredOffsetX()
    {
        return Stb.HasPreferredX != 0 ? Stb.PreferredX : -1.0f;
    }

    /// <summary>
    /// Returns the currently edited text decoded from the UTF-8 buffer.
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
    /// </summary>
    public void CursorAnimReset()
    {
        CursorAnim = -0.30f;
    }

    /// <summary>
    /// Clamps the cursor and selection to the text length.
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
    /// </summary>
    /// <returns><see langword="true"/> when the selection endpoints differ.</returns>
    public bool HasSelection()
    {
        return Stb.SelectStart != Stb.SelectEnd;
    }

    /// <summary>
    /// Collapses the selection onto the cursor.
    /// </summary>
    public void ClearSelection()
    {
        Stb.SelectStart = Stb.Cursor;
        Stb.SelectEnd = Stb.Cursor;
    }

    /// <summary>
    /// Returns the cursor position.
    /// </summary>
    /// <returns>The cursor offset in bytes.</returns>
    public int GetCursorPos()
    {
        return Stb.Cursor;
    }

    /// <summary>
    /// Returns the selection start position.
    /// </summary>
    /// <returns>The selection start offset in bytes.</returns>
    public int GetSelectionStart()
    {
        return Stb.SelectStart;
    }

    /// <summary>
    /// Returns the selection end position.
    /// </summary>
    /// <returns>The selection end offset in bytes.</returns>
    public int GetSelectionEnd()
    {
        return Stb.SelectEnd;
    }

    /// <summary>
    /// Sets the selection endpoints and moves the cursor to the start.
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
    /// </summary>
    public void ReloadUserBufAndSelectAll()
    {
        WantReloadUserBuf = true;
        ReloadSelectionStart = 0;
        ReloadSelectionEnd = int.MaxValue;
    }

    /// <summary>
    /// Requests a user-buffer reload that preserves the current selection.
    /// </summary>
    public void ReloadUserBufAndKeepSelection()
    {
        WantReloadUserBuf = true;
        ReloadSelectionStart = Stb.SelectStart;
        ReloadSelectionEnd = Stb.SelectEnd;
    }

    /// <summary>
    /// Requests a user-buffer reload that moves the cursor to the end.
    /// </summary>
    public void ReloadUserBufAndMoveToEnd()
    {
        WantReloadUserBuf = true;
        ReloadSelectionStart = ReloadSelectionEnd = int.MaxValue;
    }
}

/// <summary>
/// Backup of a text buffer that just deactivated, kept so the deactivation remains observable for a
/// frame.
/// </summary>
public sealed class ImGuiInputTextDeactivatedState
{
    /// <summary>
    /// Gets or sets the identifier of the widget that owned the text state.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the frame count at which the deactivation elapsed.
    /// </summary>
    public int ElapseFrame { get; set; }

    /// <summary>
    /// Gets the backed-up text buffer.
    /// </summary>
    public List<byte> TextA { get; } = new();

    /// <summary>
    /// Clears the identifier, frame, and text buffer.
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
/// processed.
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
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the single <c>ImGuiInputTextFlags_Callback*</c> value that triggered the
    /// callback.
    /// </summary>
    public ImGuiInputTextFlags EventFlag { get; set; }

    /// <summary>
    /// Gets or sets the flags originally passed to <c>InputText()</c>.
    /// </summary>
    public ImGuiInputTextFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the opaque pointer originally passed to <c>InputText()</c>.
    /// </summary>
    public IntPtr UserData { get; set; }

    /// <summary>
    /// Gets or sets the widget identifier.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the key that triggered a completion or history callback.
    /// </summary>
    public ImGuiKey EventKey { get; set; }

    /// <summary>
    /// Gets or sets the character supplied to a character-filter callback.
    /// </summary>
    public uint EventChar { get; set; }

    /// <summary>
    /// Gets or sets whether the field was just activated.
    /// </summary>
    public bool EventActivated { get; set; }

    /// <summary>
    /// Gets or sets whether the callback modified the text buffer and length.
    /// </summary>
    public bool BufDirty { get; set; }

    /// <summary>
    /// Gets or sets the text buffer. Canonical exposes a mutable <c>char*</c> that a resize callback
    /// may replace; this port exposes the managed byte array.
    /// </summary>
    public byte[]? Buf { get; set; }

    /// <summary>
    /// Gets or sets the text length in bytes, excluding the terminator.
    /// </summary>
    public int BufTextLen { get; set; }

    /// <summary>
    /// Gets or sets the buffer size in bytes, including the terminator.
    /// </summary>
    public int BufSize { get; set; }

    /// <summary>
    /// Gets or sets the cursor position.
    /// </summary>
    public int CursorPos { get; set; }

    /// <summary>
    /// Gets or sets the selection start position.
    /// </summary>
    public int SelectionStart { get; set; }

    /// <summary>
    /// Gets or sets the selection end position.
    /// </summary>
    public int SelectionEnd { get; set; }

    /// <summary>
    /// Selects the whole buffer.
    /// </summary>
    public void SelectAll()
    {
        SelectionStart = 0;
        CursorPos = BufTextLen;
        SelectionEnd = BufTextLen;
    }

    /// <summary>
    /// Sets the selection endpoints and moves the cursor to the start.
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
    /// </summary>
    public void ClearSelection()
    {
        SelectionStart = BufTextLen;
        SelectionEnd = BufTextLen;
    }

    /// <summary>
    /// Returns whether a selection is currently active.
    /// </summary>
    /// <returns><see langword="true"/> when the selection endpoints differ.</returns>
    public bool HasSelection()
    {
        return SelectionStart != SelectionEnd;
    }

    /// <summary>
    /// Deletes a byte range from the callback buffer. Mirrors canonical
    /// <c>ImGuiInputTextCallbackData::DeleteChars</c>.
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
    /// <c>ImGuiInputTextCallbackData::InsertChars</c>.
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
    /// (, declared )
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
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the viewport behavior flags.
    /// </summary>
    public ImGuiViewportFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the main-area position in desktop coordinates.
    /// </summary>
    public ImVec2 Pos { get; set; }

    /// <summary>
    /// Gets or sets the main-area size.
    /// </summary>
    public ImVec2 Size { get; set; }

    /// <summary>
    /// Gets or sets the density ratio between display and framebuffer pixels, which affects font
    /// rasterization.
    /// </summary>
    public ImVec2 FramebufferScale { get; set; }

    /// <summary>
    /// Gets or sets the work-area position, which is the main position minus task bars and similar
    /// decorations.
    /// </summary>
    public ImVec2 WorkPos { get; set; }

    /// <summary>
    /// Gets or sets the work-area size.
    /// </summary>
    public ImVec2 WorkSize { get; set; }

    /// <summary>
    /// Gets or sets the DPI scale, where 1.0 corresponds to 96 DPI.
    /// </summary>
    public float DpiScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the parent viewport identifier, or zero when there is no parent.
    /// </summary>
    public uint ParentViewportId { get; set; }

    /// <summary>
    /// Gets or sets the direct reference to the parent viewport, or <see langword="null"/>.
    /// </summary>
    public ImGuiViewport? ParentViewport { get; set; }

    /// <summary>
    /// Gets or sets the draw data produced for this viewport; valid after render and until the next
    /// frame begins.
    /// </summary>
    public ImDrawData? DrawData { get; set; }

    /// <summary>
    /// Gets or sets renderer-owned opaque data.
    /// </summary>
    public IntPtr RendererUserData { get; set; }

    /// <summary>
    /// Gets or sets platform-owned opaque data.
    /// </summary>
    public IntPtr PlatformUserData { get; set; }

    /// <summary>
    /// Gets or sets platform icon data.
    /// </summary>
    public IntPtr PlatformIconData { get; set; }

    /// <summary>
    /// Gets or sets the high-level platform window handle.
    /// </summary>
    public IntPtr PlatformHandle { get; set; }

    /// <summary>
    /// Gets or sets the low-level platform-native window handle.
    /// </summary>
    public IntPtr PlatformHandleRaw { get; set; }

    /// <summary>
    /// Gets or sets whether the platform window has been created.
    /// </summary>
    public bool PlatformWindowCreated { get; set; }

    /// <summary>
    /// Gets or sets whether the platform requested a move.
    /// </summary>
    public bool PlatformRequestMove { get; set; }

    /// <summary>
    /// Gets or sets whether the platform requested a resize.
    /// </summary>
    public bool PlatformRequestResize { get; set; }

    /// <summary>
    /// Gets or sets whether the platform requested a close.
    /// </summary>
    public bool PlatformRequestClose { get; set; }

    /// <summary>
    /// Returns the center of the main area.
    /// </summary>
    /// <returns>The center point of <see cref="Pos"/> and <see cref="Size"/>.</returns>
    public ImVec2 GetCenter()
    {
        return new ImVec2(Pos.X + (Size.X * 0.5f), Pos.Y + (Size.Y * 0.5f));
    }

    /// <summary>
    /// Returns the center of the work area.
    /// </summary>
    /// <returns>The center point of <see cref="WorkPos"/> and <see cref="WorkSize"/>.</returns>
    public ImVec2 GetWorkCenter()
    {
        return new ImVec2(WorkPos.X + (WorkSize.X * 0.5f), WorkPos.Y + (WorkSize.Y * 0.5f));
    }

    /// <summary>
    /// Returns the main bounding rectangle of the viewport.
    /// </summary>
    /// <returns>The viewport rectangle encompassing <see cref="Pos"/> and <see cref="Size"/>.</returns>
    public ImRect GetMainRect()
    {
        return new ImRect(Pos, Pos + Size);
    }

    /// <summary>
    /// Returns the work area bounding rectangle of the viewport.
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
/// </summary>
/// <remarks>
/// The extra members track z-order estimation, the background and foreground draw lists, per-frame
/// platform feedback, and the work-area insets used to avoid OS decorations.
/// </remarks>
public sealed class ImGuiViewportP : ImGuiViewport
{
    /// <summary>
    /// Returns a printable name for this viewport, used by the debug tools.
    /// </summary>
    /// <returns>The owning window's name, or <c>"n/a"</c> when this viewport has no window yet.</returns>
    public override string GetDebugName() => Window != null ? Window.Name : "n/a";

    /// <summary>
    /// Gets or sets the window that owns this viewport, when it is not allowed to host other
    /// windows.
    /// </summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>
    /// Gets or sets the viewport list index.
    /// </summary>
    public int Idx { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last frame on which a window activated this viewport.
    /// </summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last focus stamp observed for a window hosted by this viewport.
    /// </summary>
    public int LastFocusedStampCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the hash of the last window name hosted by this viewport.
    /// </summary>
    public uint LastNameHash { get; set; }

    /// <summary>
    /// Gets or sets the last platform position.
    /// </summary>
    public ImVec2 LastPos { get; set; }

    /// <summary>
    /// Gets or sets the last platform size.
    /// </summary>
    public ImVec2 LastSize { get; set; }

    /// <summary>
    /// Gets or sets the window opacity applied while dragging.
    /// </summary>
    public float Alpha { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the previously applied opacity.
    /// </summary>
    public float LastAlpha { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets whether the navigation window was non-null the last time this viewport was
    /// focused.
    /// </summary>
    public bool LastFocusedHadNavWindow { get; set; }

    /// <summary>
    /// Gets or sets the index of the platform monitor that contains this viewport, or -1.
    /// </summary>
    public short PlatformMonitor { get; set; } = -1;

    /// <summary>
    /// Gets the last frames on which the background and foreground draw lists were used.
    /// </summary>
    public float[] BgFgDrawListsLastTimeActive { get; } = new float[] { -1.0f, -1.0f };

    /// <summary>
    /// Gets the convenience background and foreground draw lists.
    /// </summary>
    public ImDrawList?[] BgFgDrawLists { get; } = new ImDrawList?[2];

    /// <summary>
    /// Gets or sets the final draw data built for this viewport.
    /// </summary>
    public ImDrawData DrawDataP { get; set; } = new();

    /// <summary>
    /// Gets or sets the temporary builder used while assembling the final draw data.
    /// </summary>
    public ImDrawDataBuilder DrawDataBuilder { get; set; } = new();

    /// <summary>
    /// Gets or sets the last position reported by the platform.
    /// </summary>
    public ImVec2 LastPlatformPos { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the last size reported by the platform.
    /// </summary>
    public ImVec2 LastPlatformSize { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the last size reported by the renderer.
    /// </summary>
    public ImVec2 LastRendererSize { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the work-area minimum inset locked for the frame.
    /// </summary>
    public ImVec2 WorkInsetMin { get; set; }

    /// <summary>
    /// Gets or sets the work-area maximum inset locked for the frame.
    /// </summary>
    public ImVec2 WorkInsetMax { get; set; }

    /// <summary>
    /// Gets or sets the work-area minimum inset accumulated for the frame.
    /// </summary>
    public ImVec2 BuildWorkInsetMin { get; set; }

    /// <summary>
    /// Gets or sets the work-area maximum inset accumulated for the frame.
    /// </summary>
    public ImVec2 BuildWorkInsetMax { get; set; }

    /// <summary>
    /// Clears the three platform request flags.
    /// </summary>
    public void ClearRequestFlags()
    {
        PlatformRequestClose = false;
        PlatformRequestMove = false;
        PlatformRequestResize = false;
    }

    /// <summary>
    /// Recomputes the public work position and size from the locked insets.
    /// </summary>
    public void UpdateWorkRect()
    {
        WorkPos = CalcWorkRectPos(WorkInsetMin);
        WorkSize = CalcWorkRectSize(WorkInsetMin, WorkInsetMax);
    }

    /// <summary>
    /// Computes a work-area position from an inset.
    /// </summary>
    /// <param name="insetMin">The minimum inset.</param>
    /// <returns>The inset-adjusted position.</returns>
    public ImVec2 CalcWorkRectPos(ImVec2 insetMin)
    {
        return new ImVec2(Pos.X + insetMin.X, Pos.Y + insetMin.Y);
    }

    /// <summary>
    /// Computes a work-area size from a pair of insets, clamping each axis at zero.
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
    /// </summary>
    /// <returns>The main-area rectangle.</returns>
    public new ImRect GetMainRect()
    {
        return new ImRect(Pos.X, Pos.Y, Pos.X + Size.X, Pos.Y + Size.Y);
    }

    /// <summary>
    /// Returns the locked work-area rectangle.
    /// </summary>
    /// <returns>The work-area rectangle.</returns>
    public new ImRect GetWorkRect()
    {
        return new ImRect(WorkPos.X, WorkPos.Y, WorkPos.X + WorkSize.X, WorkPos.Y + WorkSize.Y);
    }

    /// <summary>
    /// Returns the work-area rectangle currently being accumulated for the frame.
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
/// straddling displays.
/// </summary>
public sealed class ImGuiPlatformMonitor
{
    /// <summary>
    /// Gets or sets the upper-left corner of the displayed area.
    /// </summary>
    public ImVec2 MainPos { get; set; }

    /// <summary>
    /// Gets or sets the size of the displayed area.
    /// </summary>
    public ImVec2 MainSize { get; set; }

    /// <summary>
    /// Gets or sets the upper-left corner of the work area, excluding task and menu bars.
    /// </summary>
    public ImVec2 WorkPos { get; set; }

    /// <summary>
    /// Gets or sets the size of the work area.
    /// </summary>
    public ImVec2 WorkSize { get; set; }

    /// <summary>
    /// Gets or sets the DPI scale, where 1.0 corresponds to 96 DPI.
    /// </summary>
    public float DpiScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets backend-dependent monitor data.
    /// </summary>
    public IntPtr PlatformHandle { get; set; }
}

/// <summary>
/// Input-method-editor state published to the platform backend so it can position the IME window
/// </summary>
public sealed class ImGuiPlatformImeData
{
    /// <summary>
    /// Gets or sets whether a widget wants the IME to be visible.
    /// </summary>
    public bool WantVisible { get; set; }

    /// <summary>
    /// Gets or sets whether a widget wants text input; this mirrors the upcoming value of
    /// <c>io.WantTextInput</c>.
    /// </summary>
    public bool WantTextInput { get; set; }

    /// <summary>
    /// Gets or sets the input cursor position.
    /// </summary>
    public ImVec2 InputPos { get; set; }

    /// <summary>
    /// Gets or sets the input line height.
    /// </summary>
    public float InputLineHeight { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the viewport that owns the IME focus.
    /// </summary>
    public uint ViewportId { get; set; }
}

/// <summary>
/// The platform and renderer interface. A backend installs the callbacks it supports; the core binds
/// the data lists and invokes the callbacks at the canonical points in the frame lifecycle
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
    /// </summary>
    public Func<ImGuiContext, string?>? Platform_GetClipboardTextFn { get; set; }

    /// <summary>
    /// Gets or sets the handler that writes the OS clipboard, or <see langword="null"/> when the host
    /// has none.
    /// </summary>
    public Action<ImGuiContext, string>? Platform_SetClipboardTextFn { get; set; }

    /// <summary>
    /// Gets or sets opaque data forwarded to the clipboard handlers.
    /// </summary>
    public IntPtr Platform_ClipboardUserData { get; set; }

    /// <summary>
    /// Gets or sets the handler that opens a link, folder, or file in the OS shell; it returns
    /// <see langword="false"/> on failure.
    /// </summary>
    public Func<ImGuiContext, string, bool>? Platform_OpenInShellFn { get; set; }

    /// <summary>
    /// Gets or sets opaque data forwarded to the shell handler.
    /// </summary>
    public IntPtr Platform_OpenInShellUserData { get; set; }

    /// <summary>
    /// Gets or sets the handler that notifies the OS input method editor of the text-cursor position.
    /// </summary>
    public Action<ImGuiContext, ImGuiViewport, ImGuiPlatformImeData>? Platform_SetImeDataFn { get; set; }

    /// <summary>
    /// Gets or sets opaque data forwarded to the IME handler.
    /// </summary>
    public IntPtr Platform_ImeUserData { get; set; }

    /// <summary>
    /// Gets or sets the locale decimal point character, for example <c>'.'</c> or <c>','</c>.
    /// </summary>
    public ushort Platform_LocaleDecimalPoint { get; set; } = (ushort)'.';

    /// <summary>
    /// Gets or sets the session start date packed as <c>YYYYMMDD</c>, for example <c>20261231</c>.
    /// </summary>
    public int Platform_SessionDate { get; set; }

    #endregion

    #region Renderer resource handlers

    /// <summary>
    /// Gets or sets the maximum texture width the renderer supports, or zero when unknown.
    /// </summary>
    public int Renderer_TextureMaxWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum texture height the renderer supports, or zero when unknown.
    /// </summary>
    public int Renderer_TextureMaxHeight { get; set; }

    /// <summary>
    /// Gets or sets the renderer-owned opaque state pointer.
    /// </summary>
    public IntPtr Renderer_RenderState { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests a reset of the graphics render state.
    /// </summary>
    public ImDrawCallback? DrawCallback_ResetRenderState { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests linear texture sampling.
    /// </summary>
    public ImDrawCallback? DrawCallback_SetSamplerLinear { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests nearest-neighbour texture sampling.
    /// </summary>
    public ImDrawCallback? DrawCallback_SetSamplerNearest { get; set; }

    /// <summary>
    /// Gets or sets the draw callback that requests the backend use the sampler already associated to a texture.
    /// </summary>
    public ImDrawCallback? DrawCallback_SetSamplerFromTex { get; set; }

    #endregion

    #region Platform window handlers

    /// <summary>
    /// Gets or sets the handler that creates a platform window for a viewport.
    /// </summary>
    public Action<ImGuiViewport>? Platform_CreateWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that destroys a viewport's platform window.
    /// </summary>
    public Action<ImGuiViewport>? Platform_DestroyWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that shows a newly created platform window.
    /// </summary>
    public Action<ImGuiViewport>? Platform_ShowWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that positions a platform window's client area.
    /// </summary>
    public Action<ImGuiViewport, ImVec2>? Platform_SetWindowPos { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a platform window's client-area position.
    /// </summary>
    public Func<ImGuiViewport, ImVec2>? Platform_GetWindowPos { get; set; }

    /// <summary>
    /// Gets or sets the handler that sizes a platform window's client area.
    /// </summary>
    public Action<ImGuiViewport, ImVec2>? Platform_SetWindowSize { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a platform window's client-area size.
    /// </summary>
    public Func<ImGuiViewport, ImVec2>? Platform_GetWindowSize { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a viewport's framebuffer density; both components must be
    /// integers.
    /// </summary>
    public Func<ImGuiViewport, ImVec2>? Platform_GetWindowFramebufferScale { get; set; }

    /// <summary>
    /// Gets or sets the handler that moves a platform window to the front and focuses it.
    /// </summary>
    public Action<ImGuiViewport>? Platform_SetWindowFocus { get; set; }

    /// <summary>
    /// Gets or sets the handler that reports whether a platform window is focused.
    /// </summary>
    public Func<ImGuiViewport, bool>? Platform_GetWindowFocus { get; set; }

    /// <summary>
    /// Gets or sets the handler that reports whether a platform window is minimized.
    /// </summary>
    public Func<ImGuiViewport, bool>? Platform_GetWindowMinimized { get; set; }

    /// <summary>
    /// Gets or sets the handler that sets a platform window's title.
    /// </summary>
    public Action<ImGuiViewport, string>? Platform_SetWindowTitle { get; set; }

    /// <summary>
    /// Gets or sets the handler that sets a platform window's global transparency.
    /// </summary>
    public Action<ImGuiViewport, float>? Platform_SetWindowAlpha { get; set; }

    /// <summary>
    /// Gets or sets the optional general-purpose per-frame platform bookkeeping hook.
    /// </summary>
    public Action<ImGuiViewport>? Platform_UpdateWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that performs platform-side rendering for a viewport, receiving the
    /// argument passed to <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Platform_RenderWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that presents a viewport's platform-side buffers, receiving the
    /// argument passed to <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Platform_SwapBuffers { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads a viewport's DPI scale, where 1.0 is 96 DPI.
    /// </summary>
    public Func<ImGuiViewport, float>? Platform_GetWindowDpiScale { get; set; }

    /// <summary>
    /// Gets or sets the handler invoked when the viewport a window is outputting into changes, so the
    /// backend can adjust fonts or style.
    /// </summary>
    public Action<ImGuiViewport>? Platform_OnChangedViewport { get; set; }

    /// <summary>
    /// Gets or sets the handler that reads the initial work-area insets of a viewport as
    /// <c>(left, top, right, bottom)</c>.
    /// </summary>
    public Func<ImGuiViewport, ImVec4>? Platform_GetWindowWorkAreaInsets { get; set; }

    /// <summary>
    /// Gets or sets the handler a Vulkan renderer uses to create a platform surface for a viewport.
    /// </summary>
    public PlatformCreateVkSurfaceFn? Platform_CreateVkSurface { get; set; }

    #endregion

    #region Renderer window handlers

    /// <summary>
    /// Gets or sets the handler that creates the renderer resources for a viewport, called after
    /// <see cref="Platform_CreateWindow"/>.
    /// </summary>
    public Action<ImGuiViewport>? Renderer_CreateWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that destroys the renderer resources for a viewport, called before
    /// <see cref="Platform_DestroyWindow"/>.
    /// </summary>
    public Action<ImGuiViewport>? Renderer_DestroyWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that resizes the renderer resources for a viewport, called after
    /// <see cref="Platform_SetWindowSize"/>.
    /// </summary>
    public Action<ImGuiViewport, ImVec2>? Renderer_SetWindowSize { get; set; }

    /// <summary>
    /// Gets or sets the handler that renders a viewport's draw data, receiving the argument passed to
    /// <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Renderer_RenderWindow { get; set; }

    /// <summary>
    /// Gets or sets the handler that presents a viewport's renderer buffers, receiving the argument
    /// passed to <see cref="ImGui.RenderPlatformWindowsDefault"/>.
    /// </summary>
    public Action<ImGuiViewport, IntPtr>? Renderer_SwapBuffers { get; set; }

    #endregion

    #region Output lists

    /// <summary>
    /// Gets the monitor list. The application or platform backend owns and updates it; the core reads
    /// it for DPI queries and for clamping windows within one monitor.
    /// </summary>
    public List<ImGuiPlatformMonitor> Monitors { get; } = new();

    /// <summary>
    /// Gets the texture list, rebuilt by the core at the end of a frame. Backends read it mostly to
    /// destroy textures at shutdown.
    /// </summary>
    public List<ImTextureData> Textures { get; } = new();

    /// <summary>
    /// Gets the user-facing viewport list, rebuilt by the core at the end of a frame. The main
    /// viewport is first, followed by the secondary viewports.
    /// </summary>
    public List<ImGuiViewport> Viewports { get; } = new();

    #endregion

    #region Handler management

    /// <summary>
    /// Clears every platform handler and its user data, leaving the renderer handlers untouched.
    /// Mirrors canonical <c>ImGuiPlatformIO::ClearPlatformHandlers</c>
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
/// its preview.
/// </summary>
public sealed class ImGuiComboPreviewData
{
    /// <summary>
    /// Gets or sets the preview rectangle.
    /// </summary>
    public ImRect PreviewRect { get; set; }

    /// <summary>
    /// Gets or sets the cursor position backup.
    /// </summary>
    public ImVec2 BackupCursorPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor maximum-position backup.
    /// </summary>
    public ImVec2 BackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the previous-line cursor position backup.
    /// </summary>
    public ImVec2 BackupCursorPosPrevLine { get; set; }

    /// <summary>
    /// Gets or sets the previous-line text baseline offset backup.
    /// </summary>
    public float BackupPrevLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets the layout-type backup.
    /// </summary>
    public ImGuiLayoutType BackupLayout { get; set; }
}

#region Docking Types

/// <summary>
/// Type of docking operation queued in an <see cref="ImGuiDockRequest"/>
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
/// </summary>
public sealed class ImGuiDockRequest
{
    /// <summary>Gets or sets the type of docking operation requested.</summary>
    public ImGuiDockRequestType Type { get; set; } = ImGuiDockRequestType.None;

    /// <summary>Gets or sets the target window into or against which docking is performed.</summary>
    public ImGuiWindow? DockTargetWindow { get; set; }

    /// <summary>Gets or sets the target dock node receiving the operation.</summary>
    public ImGuiDockNode? DockTargetNode { get; set; }

    /// <summary>Gets or sets the window payload being docked.</summary>
    public ImGuiWindow? DockPayload { get; set; }

    /// <summary>Gets or sets the direction in which the target node is split.</summary>
    public ImGuiDir DockSplitDir { get; set; } = ImGuiDir.None;

    /// <summary>Gets or sets the size ratio allocated to the new split node.</summary>
    public float DockSplitRatio { get; set; } = 0.5f;

    /// <summary>Gets or sets a value indicating whether the split is performed on the outer perimeter of the dock tree.</summary>
    public bool DockSplitOuter { get; set; }

    /// <summary>Gets or sets the target window being undocked.</summary>
    public ImGuiWindow? UndockTargetWindow { get; set; }

    /// <summary>Gets or sets the dock node from which the window is being undocked.</summary>
    public ImGuiDockNode? UndockTargetNode { get; set; }
}

/// <summary>
/// Transient state while hovering drop targets and previewing dock splits
/// </summary>
public sealed class ImGuiDockPreviewData
{
    /// <summary>Gets or sets the simulated dock node used to calculate preview geometry.</summary>
    public ImGuiDockNode FutureNode { get; set; } = new(0);

    /// <summary>Gets or sets a value indicating whether docking into the previewed target is allowed.</summary>
    public bool IsDropAllowed { get; set; }

    /// <summary>Gets or sets a value indicating whether central tab docking is available in this target.</summary>
    public bool IsCenterAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether directional splitting along node sides is available.</summary>
    public bool IsSidesAvailable { get; set; }

    /// <summary>Gets or sets a value indicating whether the split direction was explicitly requested.</summary>
    public bool IsSplitDirExplicit { get; set; }

    /// <summary>Gets or sets the existing dock node being split by the operation.</summary>
    public ImGuiDockNode? SplitNode { get; set; }

    /// <summary>Gets or sets the preview split direction.</summary>
    public ImGuiDir SplitDir { get; set; } = ImGuiDir.None;

    /// <summary>Gets or sets the size ratio applied to the preview split.</summary>
    public float SplitRatio { get; set; }

    /// <summary>Gets the array of drop target bounding rectangles for each directional slot.</summary>
    public ImRect[] DropRectsDraw { get; } = new ImRect[(int)ImGuiDir.COUNT + 1];

    /// <summary>Initializes a new instance of the <see cref="ImGuiDockPreviewData"/> class.</summary>
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
/// </summary>
public sealed class ImGuiDockNodeSettings
{
    /// <summary>Gets or sets the unique node identifier.</summary>
    public uint ID { get; set; }

    /// <summary>Gets or sets the parent node identifier, or zero if root.</summary>
    public uint ParentNodeId { get; set; }

    /// <summary>Gets or sets the host window identifier.</summary>
    public uint ParentWindowId { get; set; }

    /// <summary>Gets or sets the selected tab identifier.</summary>
    public uint SelectedTabId { get; set; }

    /// <summary>Gets or sets the split axis index, or -1 if unassigned.</summary>
    public sbyte SplitAxis { get; set; } = -1;

    /// <summary>Gets or sets the hierarchy depth of the node.</summary>
    public sbyte Depth { get; set; }

    /// <summary>Gets or sets the persistent dock node flags.</summary>
    public ImGuiDockNodeFlags Flags { get; set; }

    /// <summary>Gets or sets the persisted position of the node.</summary>
    public ImVec2ih Pos { get; set; }

    /// <summary>Gets or sets the persisted size of the node.</summary>
    public ImVec2ih Size { get; set; }

    /// <summary>Gets or sets the reference sizing dimension for proportional splitting.</summary>
    public ImVec2ih SizeRef { get; set; }
}

/// <summary>
/// Reference counts used while pruning unused docking settings nodes
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
    /// </summary>
    public int CountWindows { get; set; }

    /// <summary>
    /// Gets or sets the number of window settings entries that reference this node's root.
    /// </summary>
    public int CountChildWindows { get; set; }

    /// <summary>
    /// Gets or sets the number of child settings nodes that reference this node as their parent.
    /// </summary>
    public int CountChildNodes { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the root node this entry belongs to.
    /// </summary>
    public uint RootId { get; set; }
}

/// <summary>
/// Contiguous pool collection indexed by key or slot, matching canonical Dear ImGui <c>ImPool&lt;T&gt;</c>
/// </summary>
/// <typeparam name="T">Pooled element reference type.</typeparam>
public sealed class ImPool<T> where T : class
{
    /// <summary>Contiguous list holding allocated pool entries and slots.</summary>
    private readonly List<T?> _data = new();

    /// <summary>Map associating unique element keys with their slot index in <see cref="_data"/>.</summary>
    private readonly Dictionary<uint, int> _map = new();

    /// <summary>Stack of free slot indices available for reuse.</summary>
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
/// </summary>
public sealed class ImGuiTabItem
{
    /// <summary>Gets or sets the unique tab identifier.</summary>
    public uint ID { get; set; }

    /// <summary>Gets or sets the configuration and state flags for the tab.</summary>
    public ImGuiTabItemFlags Flags { get; set; }

    /// <summary>Gets or sets the window associated with this tab item.</summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>Gets or sets the frame count when the tab was last visible.</summary>
    public int LastFrameVisible { get; set; } = -1;

    /// <summary>Gets or sets the frame count when the tab was last selected.</summary>
    public int LastFrameSelected { get; set; } = -1;

    /// <summary>Gets or sets the layout horizontal offset within the tab bar.</summary>
    public float Offset { get; set; }

    /// <summary>Gets or sets the total rendered width of the tab.</summary>
    public float Width { get; set; }

    /// <summary>Gets or sets the measured width of the tab content.</summary>
    public float ContentWidth { get; set; }

    /// <summary>Gets or sets the explicit requested width, or <c>-1.0f</c> for automatic sizing.</summary>
    public float RequestedWidth { get; set; } = -1.0f;

    /// <summary>Gets or sets the character offset into the tab names buffer.</summary>
    public int NameOffset { get; set; } = -1;

    /// <summary>Gets or sets the submission sequence order within the frame.</summary>
    public short BeginOrder { get; set; } = -1;

    /// <summary>Gets or sets the tab index determined during layout calculation.</summary>
    public short IndexDuringLayout { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether a close action was requested for this tab.</summary>
    public bool WantClose { get; set; }
}

/// <summary>
/// Internal tab-bar storage used by docking nodes and tab containers
/// </summary>
public sealed class ImGuiTabBar
{
    /// <summary>Gets or sets the parent window hosting the tab bar.</summary>
    public ImGuiWindow? Window { get; set; }

    /// <summary>Gets the list of tab items registered in the tab bar.</summary>
    public List<ImGuiTabItem> Tabs { get; } = new();

    /// <summary>Gets or sets the configuration flags for the tab bar.</summary>
    public ImGuiTabBarFlags Flags { get; set; }

    /// <summary>Gets or sets the unique identifier of the tab bar.</summary>
    public uint ID { get; set; }

    /// <summary>Gets or sets the identifier of the currently selected tab.</summary>
    public uint SelectedTabId { get; set; }

    /// <summary>Gets or sets the identifier of the tab queued to be selected next frame.</summary>
    public uint NextSelectedTabId { get; set; }

    /// <summary>Gets or sets the identifier of the tab queued for scrolling visibility.</summary>
    public uint NextScrollToTabId { get; set; }

    /// <summary>Gets or sets the identifier of the tab actively visible.</summary>
    public uint VisibleTabId { get; set; }

    /// <summary>Gets or sets the frame count when the tab bar was currently visible.</summary>
    public int CurrFrameVisible { get; set; } = -1;

    /// <summary>Gets or sets the frame count when the tab bar was previously visible.</summary>
    public int PrevFrameVisible { get; set; } = -1;

    /// <summary>Gets or sets the bounding rectangle of the tab bar in screen space.</summary>
    public ImRect BarRect { get; set; }

    /// <summary>Gets or sets the width of the tab bar bounding rectangle from the previous frame.</summary>
    public float BarRectPrevWidth { get; set; }

    /// <summary>Gets or sets the height of the current frame tab contents.</summary>
    public float CurrTabsContentsHeight { get; set; }

    /// <summary>Gets or sets the height of the previous frame tab contents.</summary>
    public float PrevTabsContentsHeight { get; set; }

    /// <summary>Gets or sets the combined width of all visible tabs.</summary>
    public float WidthAllTabs { get; set; }

    /// <summary>Gets or sets the ideal uncompressed width of all tabs.</summary>
    public float WidthAllTabsIdeal { get; set; }

    /// <summary>Gets or sets the animated horizontal scroll position.</summary>
    public float ScrollingAnim { get; set; }

    /// <summary>Gets or sets the target horizontal scroll position.</summary>
    public float ScrollingTarget { get; set; }

    /// <summary>Gets or sets the distance to reach visibility for the target tab.</summary>
    public float ScrollingTargetDistToVisibility { get; set; }

    /// <summary>Gets or sets the current scrolling velocity.</summary>
    public float ScrollingSpeed { get; set; }

    /// <summary>Gets or sets the left boundary coordinate of the scrollable region.</summary>
    public float ScrollingRectMinX { get; set; }

    /// <summary>Gets or sets the right boundary coordinate of the scrollable region.</summary>
    public float ScrollingRectMaxX { get; set; }

    /// <summary>Gets or sets the start position of the separator line.</summary>
    public float SeparatorMinX { get; set; }

    /// <summary>Gets or sets the end position of the separator line.</summary>
    public float SeparatorMaxX { get; set; }

    /// <summary>Gets or sets the identifier of the tab being dragged for reordering.</summary>
    public uint ReorderRequestTabId { get; set; }

    /// <summary>Gets or sets the position offset requested during tab reordering.</summary>
    public short ReorderRequestOffset { get; set; }

    /// <summary>Gets or sets the re-entrancy begin count.</summary>
    public sbyte BeginCount { get; set; }

    /// <summary>Gets or sets a value indicating whether layout calculation is required.</summary>
    public bool WantLayout { get; set; }

    /// <summary>Gets or sets a value indicating whether the visible tab was submitted this frame.</summary>
    public bool VisibleTabWasSubmitted { get; set; }

    /// <summary>Gets or sets a value indicating whether new tabs were added this frame.</summary>
    public bool TabsAddedNew { get; set; }

    /// <summary>Gets or sets a value indicating whether navigation scroll buttons are active.</summary>
    public bool ScrollButtonEnabled { get; set; }

    /// <summary>Gets or sets the count of active tabs submitted this frame.</summary>
    public short TabsActiveCount { get; set; }

    /// <summary>Gets or sets the index of the most recently submitted tab item.</summary>
    public short LastTabItemIdx { get; set; } = -1;

    /// <summary>Gets or sets the vertical item spacing applied to the tab bar.</summary>
    public float ItemSpacingY { get; set; }

    /// <summary>Gets or sets the frame padding applied to tab items.</summary>
    public ImVec2 FramePadding { get; set; }

    /// <summary>Gets or sets the saved cursor position restored after tab bar layout.</summary>
    public ImVec2 BackupCursorPos { get; set; }

    /// <summary>Gets the cached list of tab label strings.</summary>
    public List<string> TabsNames { get; } = new();
}

/// <summary>
/// Docking system node representing a leaf containing windows or an inner split node
/// </summary>
public sealed class ImGuiDockNode
{
    /// <summary>Gets or sets the unique node identifier.</summary>
    public uint ID { get; set; }

    /// <summary>Gets or sets the shared flags inherited from parent or dockspace.</summary>
    public ImGuiDockNodeFlags SharedFlags { get; set; }

    /// <summary>Gets or sets the local flags configured on this node.</summary>
    public ImGuiDockNodeFlags LocalFlags { get; set; }

    /// <summary>Gets or sets the accumulated flags from member windows.</summary>
    public ImGuiDockNodeFlags LocalFlagsInWindows { get; set; }

    /// <summary>Gets or sets the combined merged flags for the node.</summary>
    public ImGuiDockNodeFlags MergedFlags { get; set; }

    /// <summary>Gets or sets the runtime lifecycle state of the node.</summary>
    public ImGuiDockNodeState State { get; set; }

    /// <summary>Gets or sets the parent node within the dock hierarchy.</summary>
    public ImGuiDockNode? ParentNode { get; set; }

    /// <summary>Gets the array of child split nodes.</summary>
    public ImGuiDockNode?[] ChildNodes { get; } = new ImGuiDockNode?[2];

    /// <summary>Gets the list of windows hosted by this dock node.</summary>
    public List<ImGuiWindow> Windows { get; } = new();

    /// <summary>Gets or sets the tab bar managing windows docked in this node.</summary>
    public ImGuiTabBar? TabBar { get; set; }

    /// <summary>The screen-space position of the dock node.</summary>
    public ImVec2 Pos;

    /// <summary>The size of the dock node.</summary>
    public ImVec2 Size;

    /// <summary>The reference sizing dimension preserved for tree rebalancing.</summary>
    public ImVec2 SizeRef;

    /// <summary>Gets or sets the split axis dividing child nodes.</summary>
    public ImGuiAxis SplitAxis { get; set; } = ImGuiAxis.None;

    /// <summary>Gets or sets the background color used in the previous frame.</summary>
    public uint LastBgColor { get; set; }

    /// <summary>Gets or sets the window class filtering dock compatibility.</summary>
    public ImGuiWindowClass WindowClass { get; set; } = new();

    /// <summary>Gets or sets the underlying host window rendering the dock node.</summary>
    public ImGuiWindow? HostWindow { get; set; }

    /// <summary>Gets or sets the currently active and visible window in this node.</summary>
    public ImGuiWindow? VisibleWindow { get; set; }

    /// <summary>Gets or sets the central dock node child, if present.</summary>
    public ImGuiDockNode? CentralNode { get; set; }

    /// <summary>Gets or sets the single child node containing windows, when applicable.</summary>
    public ImGuiDockNode? OnlyNodeWithWindows { get; set; }

    /// <summary>Gets or sets the count of descendant nodes hosting windows.</summary>
    public int CountNodeWithWindows { get; set; }

    /// <summary>Gets or sets the frame count when the node was last alive.</summary>
    public int LastFrameAlive { get; set; } = -1;

    /// <summary>Gets or sets the frame count when the node was last active.</summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>Gets or sets the frame count when the node last had focus.</summary>
    public int LastFrameFocused { get; set; } = -1;

    /// <summary>Gets or sets the identifier of the child node last focused.</summary>
    public uint LastFocusedNodeId { get; set; }

    /// <summary>Gets or sets the identifier of the selected tab.</summary>
    public uint SelectedTabId { get; set; }

    /// <summary>Gets or sets the identifier of the tab requested to close.</summary>
    public uint WantCloseTabId { get; set; }

    /// <summary>Gets or sets the viewport identifier hosting this node.</summary>
    public uint RefViewportId { get; set; }

    /// <summary>Gets or sets the authority source controlling node position.</summary>
    public byte AuthorityForPos { get; set; }

    /// <summary>Gets or sets the authority source controlling node size.</summary>
    public byte AuthorityForSize { get; set; }

    /// <summary>Gets or sets the authority source controlling viewport assignment.</summary>
    public byte AuthorityForViewport { get; set; }

    /// <summary>Gets or sets a value indicating whether the node is currently visible.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Gets or sets a value indicating whether the node has active focus.</summary>
    public bool IsFocused { get; set; }

    /// <summary>Gets or sets a value indicating whether the background was drawn this frame.</summary>
    public bool IsBgDrawnThisFrame { get; set; }

    /// <summary>Gets or sets a value indicating whether the node displays a close button.</summary>
    public bool HasCloseButton { get; set; }

    /// <summary>Gets or sets a value indicating whether the node displays a window menu button.</summary>
    public bool HasWindowMenuButton { get; set; }

    /// <summary>Gets or sets a value indicating whether this node has a central node descendant.</summary>
    public bool HasCentralNodeChild { get; set; }

    /// <summary>Gets or sets a value indicating whether all docked windows should close.</summary>
    public bool WantCloseAll { get; set; }

    /// <summary>Gets or sets a value indicating whether node sizing is locked for one frame.</summary>
    public bool WantLockSizeOnce { get; set; }

    /// <summary>Gets or sets a value indicating whether a mouse move action is queued.</summary>
    public bool WantMouseMove { get; set; }

    /// <summary>Gets or sets a value indicating whether hidden tab bar state needs updating.</summary>
    public bool WantHiddenTabBarUpdate { get; set; }

    /// <summary>Gets or sets a value indicating whether hidden tab bar visibility is toggled.</summary>
    public bool WantHiddenTabBarToggle { get; set; }

    /// <summary>Initializes a new instance of the <see cref="ImGuiDockNode"/> class with the specified identifier.</summary>
    /// <param name="id">The unique identifier of the dock node.</param>
    public ImGuiDockNode(uint id)
    {
        ID = id;
    }

    /// <summary>Determines whether this node is a root node in the dock tree.</summary>
    /// <returns><see langword="true"/> if the node has no parent; otherwise, <see langword="false"/>.</returns>
    public bool IsRootNode() => ParentNode == null;

    /// <summary>Determines whether this node acts as an explicit dockspace.</summary>
    /// <returns><see langword="true"/> if configured as a dockspace; otherwise, <see langword="false"/>.</returns>
    public bool IsDockSpace() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_DockSpace) != 0;

    /// <summary>Determines whether this node is a floating top-level dock node.</summary>
    /// <returns><see langword="true"/> if floating and not a dockspace; otherwise, <see langword="false"/>.</returns>
    public bool IsFloatingNode() => ParentNode == null && (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_DockSpace) == 0;

    /// <summary>Determines whether this node is designated as the central node of a dockspace.</summary>
    /// <returns><see langword="true"/> if flagged as a central node; otherwise, <see langword="false"/>.</returns>
    public bool IsCentralNode() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_CentralNode) != 0;

    /// <summary>Determines whether this node currently hides its tab bar.</summary>
    /// <returns><see langword="true"/> if the tab bar is hidden; otherwise, <see langword="false"/>.</returns>
    public bool IsHiddenTabBar() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_HiddenTabBar) != 0;

    /// <summary>Determines whether this node is configured without a tab bar.</summary>
    /// <returns><see langword="true"/> if the tab bar is disabled; otherwise, <see langword="false"/>.</returns>
    public bool IsNoTabBar() => (MergedFlags & (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.ImGuiDockNodeFlags_NoTabBar) != 0;

    /// <summary>Determines whether this node is an internal split node with children.</summary>
    /// <returns><see langword="true"/> if child nodes are present; otherwise, <see langword="false"/>.</returns>
    public bool IsSplitNode() => ChildNodes[0] != null;

    /// <summary>Determines whether this node is a leaf node hosting windows directly.</summary>
    /// <returns><see langword="true"/> if no children are present; otherwise, <see langword="false"/>.</returns>
    public bool IsLeafNode() => ChildNodes[0] == null;

    /// <summary>Determines whether this node is empty with no children and no hosted windows.</summary>
    /// <returns><see langword="true"/> if completely empty; otherwise, <see langword="false"/>.</returns>
    public bool IsEmpty() => ChildNodes[0] == null && Windows.Count == 0;

    /// <summary>Computes the bounding rectangle of the dock node in screen space.</summary>
    /// <returns>An <see cref="ImRect"/> bounding the node.</returns>
    public ImRect Rect() => new(Pos.X, Pos.Y, Pos.X + Size.X, Pos.Y + Size.Y);

    /// <summary>Sets the local flags on this node and updates the merged flag cache.</summary>
    /// <param name="flags">The local flags to apply.</param>
    public void SetLocalFlags(ImGuiDockNodeFlags flags)
    {
        LocalFlags = flags;
        UpdateMergedFlags();
    }

    /// <summary>Recalculates the merged flags combining shared, local, and window-derived flags.</summary>
    public void UpdateMergedFlags()
    {
        MergedFlags = SharedFlags | LocalFlags | LocalFlagsInWindows;
    }
}

/// <summary>
/// Root context state managing the active docking node hierarchy, queued docking requests,
/// and node settings.
/// </summary>
public sealed class ImGuiDockContext
{
    /// <summary>Gets the dictionary of active dock nodes keyed by node ID.</summary>
    public Dictionary<uint, ImGuiDockNode> Nodes { get; } = new();

    /// <summary>Gets the list of pending dock requests to execute.</summary>
    public List<ImGuiDockRequest> Requests { get; } = new();

    /// <summary>Gets the persistent dock node settings collection.</summary>
    public List<ImGuiDockNodeSettings> NodesSettings { get; } = new();

    /// <summary>Gets or sets a value indicating whether a complete reconstruction of the dock tree is scheduled.</summary>
    public bool WantFullRebuild { get; set; }
}

#endregion

/// <summary>
/// Window-style colors preserved for docking purposes
/// </summary>
public sealed class ImGuiWindowDockStyle
{
    /// <summary>
    /// Gets the per-docking-element color overrides, indexed by
    /// <see cref="ImGuiWindowDockStyleCol"/>.
    /// </summary>
    public uint[] Colors { get; } = new uint[(int)ImGuiWindowDockStyleCol.COUNT];
}

/// <summary>
/// Per-column state for the legacy <c>Columns()</c> API
/// </summary>
public sealed class ImGuiOldColumnData
{
    /// <summary>
    /// Gets or sets the normalized column start offset, from 0.0 at the left to 1.0 at the right.
    /// </summary>
    public float OffsetNorm { get; set; }

    /// <summary>
    /// Gets or sets the normalized offset before an in-progress resize.
    /// </summary>
    public float OffsetNormBeforeResize { get; set; }

    /// <summary>
    /// Gets or sets the legacy column flags.
    /// </summary>
    public ImGuiOldColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the column clip rectangle.
    /// </summary>
    public ImRect ClipRect { get; set; }
}

/// <summary>
/// One legacy <c>Columns()</c> set, owning its per-column records and the draw-list splitter used to
/// interleave column output.
/// </summary>
public sealed class ImGuiOldColumns
{
    /// <summary>
    /// Gets or sets the columns-set identifier.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the legacy column flags.
    /// </summary>
    public ImGuiOldColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets whether the set is on its first frame.
    /// </summary>
    public bool IsFirstFrame { get; set; }

    /// <summary>
    /// Gets or sets whether a column is currently being resized.
    /// </summary>
    public bool IsBeingResized { get; set; }

    /// <summary>
    /// Gets or sets the current column index.
    /// </summary>
    public int Current { get; set; }

    /// <summary>
    /// Gets or sets the number of columns.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the minimum X offset from the host work rectangle.
    /// </summary>
    public float OffMinX { get; set; }

    /// <summary>
    /// Gets or sets the maximum X offset from the host work rectangle.
    /// </summary>
    public float OffMaxX { get; set; }

    /// <summary>
    /// Gets or sets the minimum line Y coordinate.
    /// </summary>
    public float LineMinY { get; set; }

    /// <summary>
    /// Gets or sets the maximum line Y coordinate.
    /// </summary>
    public float LineMaxY { get; set; }

    /// <summary>
    /// Gets or sets the host cursor Y position backed up at <c>BeginColumns()</c>.
    /// </summary>
    public float HostCursorPosY { get; set; }

    /// <summary>
    /// Gets or sets the host cursor maximum X position backed up at <c>BeginColumns()</c>.
    /// </summary>
    public float HostCursorMaxPosX { get; set; }

    /// <summary>
    /// Gets or sets the host clip rectangle backed up at <c>BeginColumns()</c>.
    /// </summary>
    public ImRect HostInitialClipRect { get; set; }

    /// <summary>
    /// Gets or sets the host clip rectangle backed up while column background is pushed.
    /// </summary>
    public ImRect HostBackupClipRect { get; set; }

    /// <summary>
    /// Gets or sets the host parent work rectangle backed up at <c>BeginColumns()</c>.
    /// </summary>
    public ImRect HostBackupParentWorkRect { get; set; }

    /// <summary>
    /// Gets the per-column records.
    /// </summary>
    public List<ImGuiOldColumnData> Columns { get; } = new();

    /// <summary>
    /// Gets or sets the draw-list splitter used to interleave column output.
    /// </summary>
    public ImDrawListSplitter Splitter { get; set; } = new();
}

/// <summary>
/// An owned copy of drag-and-drop payload data together with the source and target metadata
/// </summary>
/// <remarks>
/// The payload is valid only while a drag operation is active; <c>BeginDragDropSource()</c> owns and
/// copies the data, and the application must not free it.
/// </remarks>
public sealed class ImGuiPayload
{
    /// <summary>
    /// Gets or sets the payload data pointer supplied by the raw-pointer payload overload.
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
    /// </summary>
    public int DataSize { get; set; }

    /// <summary>
    /// Gets or sets the source item identifier.
    /// </summary>
    public uint SourceId { get; set; }

    /// <summary>
    /// Gets or sets the source parent identifier when available.
    /// </summary>
    public uint SourceParentId { get; set; }

    /// <summary>
    /// Gets or sets the frame timestamp of the data.
    /// </summary>
    public int DataFrameCount { get; set; }

    /// <summary>
    /// Gets or sets the short user-supplied data-type tag, at most 32 characters.
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the payload was accepted and the mouse is hovering the target.
    /// </summary>
    public bool Preview { get; set; }

    /// <summary>
    /// Gets or sets whether the payload was accepted and the mouse was released over the target.
    /// </summary>
    public bool Delivery { get; set; }

    /// <summary>
    /// Resets the payload to its empty state.
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
    /// </summary>
    /// <param name="type">The data-type tag to test.</param>
    /// <returns><see langword="true"/> when a payload is present and the tag matches.</returns>
    public bool IsDataType(string type)
    {
        return DataFrameCount != -1 && string.Equals(DataType, type, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns whether the payload is in the preview state.
    /// </summary>
    /// <returns>The preview flag.</returns>
    public bool IsPreview()
    {
        return Preview;
    }

    /// <summary>
    /// Returns whether the payload is in the delivery state.
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
/// description text lives.
/// </summary>
public sealed class ImGuiStackLevelInfo
{
    /// <summary>
    /// Gets or sets the identifier pushed at this level.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the number of frames this level has been queried for, or zero when it is not in
    /// progress. A level is abandoned once it has been queried for more than two frames, because the
    /// canonical query advances at most one level per frame and a level that never answers would otherwise
    /// stall the whole path forever.
    /// </summary>
    public sbyte QueryFrameCount { get; set; }

    /// <summary>
    /// Gets or sets whether a sub-query produced a description for this level.
    /// </summary>
    public bool QuerySuccess { get; set; }

    /// <summary>
    /// Gets or sets the data type used to push the level, or -1.
    /// </summary>
    public sbyte DataType { get; set; } = -1;

    /// <summary>
    /// Gets or sets the offset into the parent query's result-description buffer, or -1.
    /// </summary>
    public int DescOffset { get; set; } = -1;
}

/// <summary>
/// State of an identifier-stack debug query started by the debug tools
/// </summary>
public sealed class ImGuiDebugItemPathQuery
{
    /// <summary>
    /// Gets or sets the identifier being queried.
    /// </summary>
    public uint MainID { get; set; }

    /// <summary>
    /// Gets or sets whether the query is active, which disambiguates a genuine push of identifier
    /// zero.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets whether all sub-queries are complete, some of which may have failed.
    /// </summary>
    public bool Complete { get; set; }

    /// <summary>
    /// Gets or sets the current query step: -1 selects the stack-and-init phase and a non-negative
    /// value fills one stack level.
    /// </summary>
    public sbyte Step { get; set; }

    /// <summary>
    /// Gets the per-level query results.
    /// </summary>
    public List<ImGuiStackLevelInfo> Results { get; } = new();

    /// <summary>
    /// Gets the buffer that accumulates level descriptions.
    /// </summary>
    public ImGuiTextBuffer ResultsDescBuf { get; } = new();

    /// <summary>
    /// Gets the buffer that accumulates the result path.
    /// </summary>
    public ImGuiTextBuffer ResultPathBuf { get; } = new();
}

/// <summary>
/// Options and timing state of the identifier-stack debug tool
/// </summary>
public sealed class ImGuiIDStackTool
{
    /// <summary>
    /// Gets or sets whether non-ASCII characters are hex-encoded in the output.
    /// </summary>
    public bool OptHexEncodeNonAsciiChars { get; set; } = true;

    /// <summary>
    /// Gets or sets whether pressing Ctrl+C copies the output to the clipboard.
    /// </summary>
    public bool OptCopyToClipboardOnCtrlC { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which the tool was active, or -1.
    /// </summary>
    public int LastActiveFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the time of the last clipboard copy.
    /// </summary>
    public float CopyToClipboardLastTime { get; set; } = float.NegativeInfinity;
}

/// <summary>
/// One recorded frame of allocation activity, used to audit the engine's guidance that repeating frames
/// should not allocate.
/// </summary>
public sealed class ImGuiDebugAllocEntry
{
    /// <summary>
    /// Gets or sets the frame number the counts belong to.
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the number of allocations recorded in that frame.
    /// </summary>
    public short AllocCount { get; set; }

    /// <summary>
    /// Gets or sets the number of releases recorded in that frame.
    /// </summary>
    public short FreeCount { get; set; }
}

/// <summary>
/// Running allocation totals plus the last six frames that had any allocation activity
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
    /// </summary>
    public int TotalAllocCount { get; set; }

    /// <summary>
    /// Gets or sets the number of recorded releases since the context was created.
    /// </summary>
    public int TotalFreeCount { get; set; }

    /// <summary>
    /// Gets or sets the ring index of the most recently written entry.
    /// </summary>
    public short LastEntriesIdx { get; set; }

    /// <summary>
    /// Gets the six-entry ring of the most recent frames that had allocation activity.
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
/// which auxiliary windows it opens.
/// </summary>
/// <remarks>
/// The window itself is opened by its caller, not by a member of this record: canonical has no
/// <c>ShowMetricsWindow</c> field here, so the demo owns that toggle.
/// </remarks>
public sealed class ImGuiMetricsConfig
{
    /// <summary>
    /// Gets or sets whether the metrics window also shows the debug log window.
    /// </summary>
    public bool ShowDebugLog { get; set; }

    /// <summary>
    /// Gets or sets whether the metrics window also shows the identifier-stack tool.
    /// </summary>
    public bool ShowIDStackTool { get; set; }

    /// <summary>
    /// Gets or sets whether window rectangles are highlighted.
    /// </summary>
    public bool ShowWindowsRects { get; set; }

    /// <summary>
    /// Gets or sets whether the window list is ordered by begin order instead of display order.
    /// </summary>
    public bool ShowWindowsBeginOrder { get; set; }

    /// <summary>
    /// Gets or sets whether table rectangles are highlighted.
    /// </summary>
    public bool ShowTablesRects { get; set; }

    /// <summary>
    /// Gets or sets whether draw-command triangle meshes are drawn.
    /// </summary>
    public bool ShowDrawCmdMesh { get; set; } = true;

    /// <summary>
    /// Gets or sets whether draw-command bounding boxes are drawn.
    /// </summary>
    public bool ShowDrawCmdBoundingBoxes { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the text-encoding viewer is shown.
    /// </summary>
    public bool ShowTextEncodingViewer { get; set; }

    /// <summary>
    /// Gets or sets whether the used rectangle of the font texture is highlighted.
    /// </summary>
    public bool ShowTextureUsedRect { get; set; }

    /// <summary>
    /// Gets or sets whether docking nodes are highlighted.
    /// </summary>
    public bool ShowDockingNodes { get; set; }

    /// <summary>
    /// Gets or sets which window-rectangle preset is selected, or -1 for none.
    /// </summary>
    public int ShowWindowsRectsType { get; set; } = -1;

    /// <summary>
    /// Gets or sets which table-rectangle preset is selected, or -1 for none.
    /// </summary>
    public int ShowTablesRectsType { get; set; } = -1;

    /// <summary>
    /// Gets or sets the monitor whose bounds are highlighted, or -1 for none.
    /// </summary>
    public int HighlightMonitorIdx { get; set; } = -1;

    /// <summary>
    /// Gets or sets the viewport whose thumbnail is highlighted, or zero for none.
    /// </summary>
    public uint HighlightViewportID { get; set; }

    /// <summary>
    /// Gets or sets the age in months after which settings entries are reported as discardable.
    /// </summary>
    public int SettingsDiscardMonths { get; set; } = 6;

    /// <summary>
    /// Gets or sets whether old settings entries are highlighted.
    /// </summary>
    public bool SettingsHighlightOldEntries { get; set; }

    /// <summary>
    /// Gets or sets whether a preview of the selected font is drawn.
    /// </summary>
    public bool ShowFontPreview { get; set; } = true;
}

#endregion

#region Window temporary data

/// <summary>
/// Per-window frame-temporary data: the layout cursor, the active navigation layer, the local
/// parameter stacks, and the menu-column measurement for one window
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
    /// </summary>
    public ImVec2 CursorPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor position on the previous line.
    /// </summary>
    public ImVec2 CursorPosPrevLine { get; set; }

    /// <summary>
    /// Gets or sets the initial position after <c>Begin()</c>, generally the window position plus
    /// padding.
    /// </summary>
    public ImVec2 CursorStartPos { get; set; }

    /// <summary>
    /// Gets or sets the cursor maximum position, which grows during the frame and becomes the next
    /// frame's content size.
    /// </summary>
    public ImVec2 CursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the ideal maximum position used for auto-resize.
    /// </summary>
    public ImVec2 IdealMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the size of the current line.
    /// </summary>
    public ImVec2 CurrLineSize { get; set; }

    /// <summary>
    /// Gets or sets the size of the previous line.
    /// </summary>
    public ImVec2 PrevLineSize { get; set; }

    /// <summary>
    /// Gets or sets the baseline offset of the current line.
    /// </summary>
    public float CurrLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets the baseline offset of the previous line.
    /// </summary>
    public float PrevLineTextBaseOffset { get; set; }

    /// <summary>
    /// Gets or sets whether the next item is emitted on the same line.
    /// </summary>
    public bool IsSameLine { get; set; }

    /// <summary>
    /// Gets or sets whether <c>SetCursorPos()</c> was applied on the current line.
    /// </summary>
    public bool IsSetPos { get; set; }

    /// <summary>
    /// Gets or sets the indentation from the left of the window, pushed and popped by trees.
    /// </summary>
    public ImVec1 Indent { get; set; }

    /// <summary>
    /// Gets or sets the offset to the current column.
    /// </summary>
    public ImVec1 ColumnsOffset { get; set; }

    /// <summary>
    /// Gets or sets the offset applied by groups.
    /// </summary>
    public ImVec1 GroupOffset { get; set; }

    /// <summary>
    /// Gets or sets the precision loss of the cursor start position under large scrolling, read by
    /// the clipper.
    /// </summary>
    public ImVec2 CursorStartPosLossyness { get; set; }

    #endregion

    #region Keyboard/Gamepad navigation

    /// <summary>
    /// Gets or sets the current navigation layer, in the range 0.31, though only the first two
    /// layers are used.
    /// </summary>
    public ImGuiNavLayer NavLayerCurrent { get; set; }

    /// <summary>
    /// Gets or sets the mask of layers that were written to in the previous frame.
    /// </summary>
    public short NavLayersActiveMask { get; set; }

    /// <summary>
    /// Gets or sets the mask of layers written to so far in the current frame.
    /// </summary>
    public short NavLayersActiveMaskNext { get; set; }

    /// <summary>
    /// Gets or sets whether the current work location may scroll horizontally on left/right input.
    /// </summary>
    public bool NavIsScrollPushableX { get; set; }

    /// <summary>
    /// Gets or sets whether the navigation highlight is hidden for one frame.
    /// </summary>
    public bool NavHideHighlightOneFrame { get; set; }

    /// <summary>
    /// Gets or sets whether vertical scrolling is possible.
    /// </summary>
    public bool NavWindowHasScrollY { get; set; }

    #endregion

    #region Miscellaneous

    /// <summary>
    /// Gets or sets whether a menu bar is currently being appended.
    /// </summary>
    public bool MenuBarAppending { get; set; }

    /// <summary>
    /// Gets or sets the menu-bar offset, where X is the per-layer cursor and Y accounts for
    /// safe-area padding.
    /// </summary>
    public ImVec2 MenuBarOffset { get; set; }

    /// <summary>
    /// Gets or sets the simplified column measurement used for menu items.
    /// </summary>
    public ImGuiMenuColumns MenuColumns { get; set; } = new();

    /// <summary>
    /// Gets or sets the current tree depth.
    /// </summary>
    public int TreeDepth { get; set; }

    /// <summary>
    /// Gets or sets the bitmask of depths that have tree-node stack data.
    /// </summary>
    public uint TreeHasStackDataDepthMask { get; set; }

    /// <summary>
    /// Gets or sets the bitmask of depths still recording the Y2 coordinate for clipped nodes.
    /// </summary>
    public uint TreeRecordsClippedNodesY2Mask { get; set; }

    /// <summary>
    /// Gets the child windows of this window.
    /// </summary>
    public List<ImGuiWindow> ChildWindows { get; } = new();

    /// <summary>
    /// Gets or sets the persistent per-window storage for the current window.
    /// </summary>
    public ImGuiStorage? StateStorage { get; set; }

    /// <summary>
    /// Gets or sets the current legacy columns set.
    /// </summary>
    public ImGuiOldColumns? CurrentColumns { get; set; }

    /// <summary>
    /// Gets or sets the current table index into the table pool.
    /// </summary>
    public int CurrentTableIdx { get; set; }

    /// <summary>
    /// Gets or sets the current layout type.
    /// </summary>
    public ImGuiLayoutType LayoutType { get; set; }

    /// <summary>
    /// Gets or sets the parent window layout type captured at <c>Begin()</c>.
    /// </summary>
    public ImGuiLayoutType ParentLayoutType { get; set; }

    /// <summary>
    /// Gets or sets the dimming background color used by modal windows.
    /// </summary>
    public uint ModalDimBgColor { get; set; }

    #endregion

    #region Status flags

    /// <summary>
    /// Gets or sets the item status flags of the window item.
    /// </summary>
    public ImGuiItemStatusFlags WindowItemStatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the item status flags of the child item.
    /// </summary>
    public ImGuiItemStatusFlags ChildItemStatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the item status flags of the dock tab item.
    /// </summary>
    public ImGuiItemStatusFlags DockTabItemStatusFlags { get; set; }

    /// <summary>
    /// Gets or sets the rectangle of the dock tab item.
    /// </summary>
    public ImRect DockTabItemRect { get; set; }

    #endregion

    #region Local parameter stacks

    /// <summary>
    /// Gets or sets the current item width; positive values are pixels and negative values align to
    /// the right.
    /// </summary>
    public float ItemWidth { get; set; }

    /// <summary>
    /// Gets or sets the default item width.
    /// </summary>
    public float ItemWidthDefault { get; set; }

    /// <summary>
    /// Gets or sets the current text wrap position.
    /// </summary>
    public float TextWrapPos { get; set; }

    /// <summary>
    /// Gets the item-width restore stack.
    /// </summary>
    public List<float> ItemWidthStack { get; } = new();

    /// <summary>
    /// Gets the text-wrap restore stack.
    /// </summary>
    public List<float> TextWrapPosStack { get; } = new();

    #endregion
}

#endregion

#region ImGuiWindow

/// <summary>
/// Persistent and per-frame state for one window: identity, geometry, scrolling, visibility,
/// activation, the identifier stack, and the embedded draw list
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
    /// </summary>
    public ImGuiContext? Ctx { get; set; }

    /// <summary>
    /// Gets or sets the window name, which is owned by the window.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the window identifier, normally the hash of <see cref="Name"/>.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the current window flags.
    /// </summary>
    public ImGuiWindowFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the window flags captured in the previous frame.
    /// </summary>
    public ImGuiWindowFlags FlagsPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the child-window flags.
    /// </summary>
    public ImGuiChildFlags ChildFlags { get; set; }

    /// <summary>
    /// Gets or sets the advanced window-class hints.
    /// </summary>
    public ImGuiWindowClass WindowClass { get; set; } = new();

    /// <summary>
    /// Gets or sets the viewport that hosts the window; it is set by <c>Begin()</c> and may be
    /// <see langword="null"/> for inactive discarded windows.
    /// </summary>
    public ImGuiViewportP? Viewport { get; set; }

    /// <summary>
    /// Gets or sets the backup of the viewport identifier.
    /// </summary>
    public uint ViewportId { get; set; }

    /// <summary>
    /// Gets or sets the backup of the viewport position.
    /// </summary>
    public ImVec2 ViewportPos { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the index of a platform monitor the viewport may extend onto, or -1; the value
    /// is reset every frame.
    /// </summary>
    public int ViewportAllowPlatformMonitorExtend { get; set; } = -1;

    #endregion

    #region Geometry and decoration

    /// <summary>
    /// Gets or sets the window position, rounded up to the nearest pixel.
    /// </summary>
    public ImVec2 Pos { get; set; }

    /// <summary>
    /// Gets or sets the current size, equal to <see cref="SizeFull"/> or the collapsed title-bar
    /// size.
    /// </summary>
    public ImVec2 Size { get; set; }

    /// <summary>
    /// Gets or sets the size when the window is not collapsed.
    /// </summary>
    public ImVec2 SizeFull { get; set; }

    /// <summary>
    /// Gets or sets the content and scrollable client size from the previous frame.
    /// </summary>
    public ImVec2 ContentSize { get; set; }

    /// <summary>
    /// Gets or sets the ideal content size used for auto-resize.
    /// </summary>
    public ImVec2 ContentSizeIdeal { get; set; }

    /// <summary>
    /// Gets or sets the explicit content size queued by <c>SetNextWindowContentSize()</c>.
    /// </summary>
    public ImVec2 ContentSizeExplicit { get; set; }

    /// <summary>
    /// Gets or sets the window padding captured at <c>Begin()</c> time.
    /// </summary>
    public ImVec2 WindowPadding { get; set; }

    /// <summary>
    /// Gets or sets the window corner rounding captured at <c>Begin()</c> time.
    /// </summary>
    public float WindowRounding { get; set; }

    /// <summary>
    /// Gets or sets the window border size captured at <c>Begin()</c> time.
    /// </summary>
    public float WindowBorderSize { get; set; }

    /// <summary>
    /// Gets or sets the title-bar height.
    /// </summary>
    public float TitleBarHeight { get; set; }

    /// <summary>
    /// Gets or sets the menu-bar height.
    /// </summary>
    public float MenuBarHeight { get; set; }

    /// <summary>
    /// Gets or sets the left outer decoration offset.
    /// </summary>
    public float DecoOuterSizeX1 { get; set; }

    /// <summary>
    /// Gets or sets the top outer decoration offset, covering the title and menu bars.
    /// </summary>
    public float DecoOuterSizeY1 { get; set; }

    /// <summary>
    /// Gets or sets the right outer decoration offset.
    /// </summary>
    public float DecoOuterSizeX2 { get; set; }

    /// <summary>
    /// Gets or sets the bottom outer decoration offset.
    /// </summary>
    public float DecoOuterSizeY2 { get; set; }

    /// <summary>
    /// Gets or sets the inner decoration offset X1, applied over the inner rectangle.
    /// </summary>
    public float DecoInnerSizeX1 { get; set; }

    /// <summary>
    /// Gets or sets the inner decoration offset Y1.
    /// </summary>
    public float DecoInnerSizeY1 { get; set; }

    /// <summary>
    /// Gets or sets the size of the name buffer, which may exceed the name length.
    /// </summary>
    public int NameBufLen { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the move grip.
    /// </summary>
    public uint MoveId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the tab.
    /// </summary>
    public uint TabId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the corresponding item in the parent window.
    /// </summary>
    public uint ChildId { get; set; }

    /// <summary>
    /// Gets or sets the identifier used in the popup stack when the window is a popup or menu.
    /// </summary>
    public uint PopupId { get; set; }

    #endregion

    #region Scrolling

    /// <summary>
    /// Gets or sets the current scroll amount.
    /// </summary>
    public ImVec2 Scroll { get; set; }

    /// <summary>
    /// Gets or sets the maximum scroll amount.
    /// </summary>
    public ImVec2 ScrollMax { get; set; }

    /// <summary>
    /// Gets or sets the target scroll position with scrolling canceled out.
    /// </summary>
    public ImVec2 ScrollTarget { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the scroll centering ratio, where 0.0 is the top and 0.5 is centered.
    /// </summary>
    public ImVec2 ScrollTargetCenterRatio { get; set; } = new(0.5f, 0.5f);

    /// <summary>
    /// Gets or sets the snapping threshold, where zero disables snapping.
    /// </summary>
    public ImVec2 ScrollTargetEdgeSnapDist { get; set; }

    /// <summary>
    /// Gets or sets the size taken by each scrollbar on its smaller axis.
    /// </summary>
    public ImVec2 ScrollbarSizes { get; set; }

    /// <summary>
    /// Gets or sets whether the horizontal scrollbar is visible.
    /// </summary>
    public bool ScrollbarX { get; set; }

    /// <summary>
    /// Gets or sets whether the vertical scrollbar is visible.
    /// </summary>
    public bool ScrollbarY { get; set; }

    /// <summary>
    /// Gets or sets whether horizontal scrollbar visibility was auto-stabilized.
    /// </summary>
    public bool ScrollbarXStabilizeEnabled { get; set; }

    /// <summary>
    /// Gets or sets the history bitfield that stabilizes horizontal scrollbar visibility against
    /// feedback loops.
    /// </summary>
    public byte ScrollbarXStabilizeToggledHistory { get; set; }

    #endregion

    #region Visibility, activation, and hidden frames

    /// <summary>
    /// Gets or sets whether the window owns its viewport.
    /// </summary>
    public bool ViewportOwned { get; set; }

    /// <summary>
    /// Gets or sets whether the window was set active by <c>Begin()</c>; collapsed windows stay
    /// inactive.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>
    /// Gets or sets whether the window was active in a previous frame.
    /// </summary>
    public bool WasActive { get; set; }

    /// <summary>
    /// Gets or sets whether any widget accessed the window this frame.
    /// </summary>
    public bool WriteAccessed { get; set; }

    /// <summary>
    /// Gets or sets whether the window is collapsed to its title bar.
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Gets or sets a request to toggle the collapsed state.
    /// </summary>
    public bool WantCollapseToggle { get; set; }

    /// <summary>
    /// Gets or sets whether items can safely be clipped.
    /// </summary>
    public bool SkipItems { get; set; }

    /// <summary>
    /// Gets or sets whether the previous frame contents are reused and <c>Begin()</c> returns
    /// false.
    /// </summary>
    public bool SkipRefresh { get; set; }

    /// <summary>
    /// Gets or sets whether the current frame is the appearing or re-appearing frame.
    /// </summary>
    public bool Appearing { get; set; }

    /// <summary>
    /// Gets or sets whether the window is hidden, driven by the hidden-frame counters.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Gets or sets whether this is the fallback <c>"Debug##Default"</c> window.
    /// </summary>
    public bool IsFallbackWindow { get; set; }

    /// <summary>
    /// Gets or sets whether the window was created through an explicit child-window request rather
    /// than through docking.
    /// </summary>
    public bool IsExplicitChild { get; set; }

    /// <summary>
    /// Gets or sets whether the window has a close button.
    /// </summary>
    public bool HasCloseButton { get; set; }

    /// <summary>
    /// Gets or sets the border currently hovered for resizing, or -1.
    /// </summary>
    public sbyte ResizeBorderHovered { get; set; }

    /// <summary>
    /// Gets or sets the border currently held for resizing, or -1.
    /// </summary>
    public sbyte ResizeBorderHeld { get; set; }

    /// <summary>
    /// Gets or sets the number of <c>Begin()</c> calls this frame.
    /// </summary>
    public short BeginCount { get; set; }

    /// <summary>
    /// Gets or sets the number of <c>Begin()</c> calls in the previous frame.
    /// </summary>
    public short BeginCountPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the begin order within the immediate parent.
    /// </summary>
    public short BeginOrderWithinParent { get; set; }

    /// <summary>
    /// Gets or sets the begin order within the whole context.
    /// </summary>
    public short BeginOrderWithinContext { get; set; }

    /// <summary>
    /// Gets or sets the order within the focus-order list.
    /// </summary>
    public short FocusOrder { get; set; }

    /// <summary>
    /// Gets or sets the last automatic-positioning direction.
    /// </summary>
    public ImGuiDir AutoPosLastDirection { get; set; }

    /// <summary>
    /// Gets or sets the number of auto-fit frames remaining on the horizontal axis, or -1.
    /// </summary>
    public sbyte AutoFitFramesX { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of auto-fit frames remaining on the vertical axis, or -1.
    /// </summary>
    public sbyte AutoFitFramesY { get; set; } = -1;

    /// <summary>
    /// Gets or sets whether auto-fit may only grow the window.
    /// </summary>
    public bool AutoFitOnlyGrows { get; set; }

    /// <summary>
    /// Gets or sets the number of frames to hide the window while skipping items.
    /// </summary>
    public sbyte HiddenFramesCanSkipItems { get; set; }

    /// <summary>
    /// Gets or sets the number of frames to hide the window while still measuring items.
    /// </summary>
    public sbyte HiddenFramesCannotSkipItems { get; set; }

    /// <summary>
    /// Gets or sets the frame count until which the window is hidden at render time only.
    /// </summary>
    public sbyte HiddenFramesForRenderOnly { get; set; }

    /// <summary>
    /// Gets or sets the number of frames during which window interactions are disabled.
    /// </summary>
    public sbyte DisableInputsFrames { get; set; }

    /// <summary>
    /// Gets or sets the click-and-drag behavior on the window background.
    /// </summary>
    public ImGuiWindowBgClickFlags BgClickFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowPos()</c>.
    /// </summary>
    public ImGuiCond SetWindowPosAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowSize()</c>.
    /// </summary>
    public ImGuiCond SetWindowSizeAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowCollapsed()</c>.
    /// </summary>
    public ImGuiCond SetWindowCollapsedAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the conditions accepted by <c>SetNextWindowDock()</c>.
    /// </summary>
    public ImGuiCond SetWindowDockAllowFlags { get; set; }

    /// <summary>
    /// Gets or sets the stored position for a nonzero pivot, processed after the size is known.
    /// </summary>
    public ImVec2 SetWindowPosVal { get; set; } = new(float.MaxValue, float.MaxValue);

    /// <summary>
    /// Gets or sets the position pivot, where (0,0) is top-left, (0.5,0.5) is centered, and (1,1)
    /// is bottom-right.
    /// </summary>
    public ImVec2 SetWindowPosPivot { get; set; } = new(float.MaxValue, float.MaxValue);

    #endregion

    #region ID stack and temporary data

    /// <summary>
    /// Gets the identifier stack seeded with the window identifier.
    /// </summary>
    public List<uint> IDStack { get; } = new();

    /// <summary>
    /// Gets or sets the temporary per-window frame data.
    /// </summary>
    public ImGuiWindowTempData DC { get; set; } = new();

    #endregion

    #region Rectangles

    /// <summary>
    /// Gets or sets the outer clipped rectangle of the window.
    /// </summary>
    public ImRect OuterRectClipped { get; set; }

    /// <summary>
    /// Gets or sets the inner rectangle, which omits the title bar, menu bar, and scrollbars.
    /// </summary>
    public ImRect InnerRect { get; set; }

    /// <summary>
    /// Gets or sets the inner clip rectangle, the inner rectangle shrunk by half the padding and
    /// clipped to the viewport or parent.
    /// </summary>
    public ImRect InnerClipRect { get; set; }

    /// <summary>
    /// Gets or sets the whole scrolling region, reduced by containers and shrunk by the full
    /// padding.
    /// </summary>
    public ImRect WorkRect { get; set; }

    /// <summary>
    /// Gets or sets the backup of the work rectangle taken before entering a container.
    /// </summary>
    public ImRect ParentWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the current clipping and scissoring rectangle.
    /// </summary>
    public ImRect ClipRect { get; set; }

    /// <summary>
    /// Gets or sets the content-region rectangle, essentially the work rectangle without scroll
    /// handling.
    /// </summary>
    public ImRect ContentRegionRect { get; set; }

    /// <summary>
    /// Gets or sets the optional pass-through hole size.
    /// </summary>
    public ImVec2ih HitTestHoleSize { get; set; }

    /// <summary>
    /// Gets or sets the optional pass-through hole offset.
    /// </summary>
    public ImVec2ih HitTestHoleOffset { get; set; }

    #endregion

    #region Lifetime, storage, and fonts

    /// <summary>
    /// Gets or sets the last frame on which the window was active.
    /// </summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last frame on which the window was just focused.
    /// </summary>
    public int LastFrameJustFocused { get; set; } = -1;

    /// <summary>
    /// Gets or sets the last timestamp at which the window was active.
    /// </summary>
    public float LastTimeActive { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the persistent per-window storage, such as tree open and close state.
    /// </summary>
    public ImGuiStorage StateStorage { get; set; } = new();

    /// <summary>
    /// Gets the legacy columns storage.
    /// </summary>
    public List<ImGuiOldColumns> ColumnsStorage { get; } = new();

    /// <summary>
    /// Gets or sets the per-window font scale multiplier.
    /// </summary>
    public float FontWindowScale { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the accumulated font scale of parent windows.
    /// </summary>
    public float FontWindowScaleParents { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the copy of the computed font size captured at <c>Begin()</c> time.
    /// </summary>
    public float FontRefSize { get; set; }

    /// <summary>
    /// Gets or sets the offset of this window into the settings list, or -1.
    /// </summary>
    public int SettingsOffset { get; set; } = -1;

    #endregion

    #region Draw list, parents, and roots

    /// <summary>
    /// Gets or sets the window draw list, kept as a separate member for compatibility with
    /// canonical internal-header users.
    /// </summary>
    public ImDrawList DrawList { get; set; }

    /// <summary>
    /// Gets or sets the embedded draw-list instance.
    /// </summary>
    public ImDrawList DrawListInst { get; set; }

    /// <summary>
    /// Gets or sets the parent used for child, popup, and docked windows.
    /// </summary>
    public ImGuiWindow? ParentWindow { get; set; }

    /// <summary>
    /// Gets or sets the parent within the current begin stack.
    /// </summary>
    public ImGuiWindow? ParentWindowInBeginStack { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first-non-child ancestor, without crossing popups or dock nodes.
    /// </summary>
    public ImGuiWindow? RootWindow { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first-non-child ancestor, crossing popups.
    /// </summary>
    public ImGuiWindow? RootWindowPopupTree { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first-non-child ancestor, crossing dock nodes.
    /// </summary>
    public ImGuiWindow? RootWindowDockTree { get; set; }

    /// <summary>
    /// Gets or sets the self-or-ancestor that displays the active title-bar background.
    /// </summary>
    public ImGuiWindow? RootWindowForTitleBarHighlight { get; set; }

    /// <summary>
    /// Gets or sets the self-or-first ancestor without the navigation-flattened flag.
    /// </summary>
    public ImGuiWindow? RootWindowForNav { get; set; }

    /// <summary>
    /// Gets or sets the manual logical parent used for shortcut focus-route evaluation.
    /// </summary>
    public ImGuiWindow? ParentWindowForFocusRoute { get; set; }

    #endregion

    #region Navigation state

    /// <summary>
    /// Gets or sets the child window remembered when the menu bar is entered.
    /// </summary>
    public ImGuiWindow? NavLastChildNavWindow { get; set; }

    /// <summary>
    /// Gets the last known navigation identifier for each navigation layer.
    /// </summary>
    public uint[] NavLastIds { get; } = new uint[(int)ImGuiNavLayer.COUNT];

    /// <summary>
    /// Gets the reference rectangle for each navigation layer, in window-relative space.
    /// </summary>
    public ImRect[] NavRectRel { get; } = CreateNavRects();

    /// <summary>
    /// Gets the preferred X/Y scoring position for each navigation layer.
    /// </summary>
    public ImVec2[] NavPreferredScoringPosRel { get; } = CreateNavScoringPositions();

    /// <summary>
    /// Gets or sets the focus scope identifier captured at <c>Begin()</c> time.
    /// </summary>
    public uint NavRootFocusScopeId { get; set; }

    #endregion

    #region Memory management

    /// <summary>
    /// Gets or sets the backup of the last draw-list index count, used to preallocate when the
    /// window wakes.
    /// </summary>
    public int MemoryDrawListIdxCapacity { get; set; }

    /// <summary>
    /// Gets or sets the backup of the last draw-list vertex count, used to preallocate when the
    /// window wakes.
    /// </summary>
    public int MemoryDrawListVtxCapacity { get; set; }

    /// <summary>
    /// Gets or sets whether extraneous data has been garbage-collected.
    /// </summary>
    public bool MemoryCompacted { get; set; }

    #endregion

    #region Docking

    /// <summary>
    /// Gets or sets whether docking artifacts are visible; when set, the docking node reference is
    /// non-null.
    /// </summary>
    public bool DockIsActive { get; set; }

    /// <summary>
    /// Gets or sets whether the dock node is visible.
    /// </summary>
    public bool DockNodeIsVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the window is visible this frame because its tab is selected.
    /// </summary>
    public bool DockTabIsVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the tab requested a close.
    /// </summary>
    public bool DockTabWantClose { get; set; }

    /// <summary>
    /// Gets or sets the order within the dock node on reappearance.
    /// </summary>
    public short DockOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the window-style data preserved for docking.
    /// </summary>
    public ImGuiWindowDockStyle DockStyle { get; set; } = new();

    /// <summary>
    /// Gets or sets which dock node this window is docked into.
    /// </summary>
    public ImGuiDockNode? DockNode { get; set; }

    /// <summary>
    /// Gets or sets which dock node this window is hosting as a parent window.
    /// </summary>
    public ImGuiDockNode? DockNodeAsHost { get; set; }

    /// <summary>
    /// Gets or sets the backup of the last valid dock node identifier.
    /// </summary>
    public uint DockId { get; set; }

    #endregion

    /// <summary>
    /// Initializes a window for a context and name. Canonical additionally hashes the name, seeds
    /// <see cref="IDStack"/> with the window identifier, and computes the move and tab identifiers;
    /// this port leaves those steps to the windowing pipeline so that identifier hashing remains
    /// defined in a single place.
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
    /// </summary>
    /// <returns>The window rectangle.</returns>
    public ImRect Rect()
    {
        return new ImRect(Pos.X, Pos.Y, Pos.X + Size.X, Pos.Y + Size.Y);
    }

    /// <summary>
    /// Returns the title-bar rectangle.
    /// </summary>
    /// <returns>The title-bar rectangle.</returns>
    public ImRect TitleBarRect()
    {
        return new ImRect(Pos.X, Pos.Y, Pos.X + SizeFull.X, Pos.Y + TitleBarHeight);
    }

    /// <summary>
    /// Returns the menu-bar rectangle, directly below the title bar.
    /// </summary>
    /// <returns>The menu-bar rectangle.</returns>
    public ImRect MenuBarRect()
    {
        float y1 = Pos.Y + TitleBarHeight;
        return new ImRect(Pos.X, y1, Pos.X + SizeFull.X, y1 + MenuBarHeight);
    }

    /// <summary>
    /// Creates the per-layer navigation reference rectangles.
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
    /// </summary>
    /// <param name="str">The string to hash.</param>
    /// <returns>Hashed identifier.</returns>
    public uint GetID(string str)
    {
        return ImGui.GetIDWithSeed(str, null, IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from an integer seeded by this window's current ID stack top.
    /// </summary>
    /// <param name="n">The integer value to hash.</param>
    /// <returns>Hashed identifier.</returns>
    public uint GetID(int n)
    {
        return ImGui.GetIDWithSeed(n, IDStack[^1]);
    }

    /// <summary>
    /// Computes an identifier from an object reference seeded by this window's current ID stack top.
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
    /// </summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// Gets or sets whether <c>NewFrame()</c> was called and <c>EndFrame()</c> has not yet run.
    /// </summary>
    public bool WithinFrameScope { get; set; }

    /// <summary>
    /// Gets or sets whether the implicit debug window has been pushed.
    /// </summary>
    public bool WithinFrameScopeWithImplicitWindow { get; set; }

    /// <summary>
    /// Gets or sets whether test-engine item hooks are invoked.
    /// </summary>
    public bool TestEngineHookItems { get; set; }

    /// <summary>
    /// Gets or sets the global frame counter.
    /// </summary>
    public int FrameCount { get; set; }

    /// <summary>
    /// Gets or sets the frame count captured at <c>EndFrame()</c>.
    /// </summary>
    public int FrameCountEnded { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame count captured when the platform finished the frame.
    /// </summary>
    public int FrameCountPlatformEnded { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame count captured at <c>Render()</c>.
    /// </summary>
    public int FrameCountRendered { get; set; } = -1;

    /// <summary>
    /// Gets or sets the global time in seconds.
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// Gets or sets the context name used for multi-context debugging.
    /// </summary>
    public string ContextName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the main input and output state.
    /// </summary>
    public ImGuiIO IO { get; set; } = new();

    /// <summary>
    /// Gets or sets the platform and renderer interface for this context.
    /// </summary>
    public ImGuiPlatformIO PlatformIO { get; set; } = new();

    /// <summary>
    /// Gets or sets the style.
    /// </summary>
    public ImGuiStyle Style { get; set; } = new();

    /// <summary>
    /// Gets or sets the configuration flags observed at <c>NewFrame()</c>.
    /// </summary>
    public ImGuiConfigFlags ConfigFlagsCurrFrame { get; set; }

    /// <summary>
    /// Gets or sets the configuration flags observed in the previous frame.
    /// </summary>
    public ImGuiConfigFlags ConfigFlagsLastFrame { get; set; }

    /// <summary>
    /// Gets the font atlases used by the context.
    /// </summary>
    public List<ImFontAtlas> FontAtlases { get; } = new();

    /// <summary>
    /// Gets or sets the currently bound font.
    /// </summary>
    public ImFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the currently bound baked font at the bound size.
    /// </summary>
    public ImFontBaked? FontBaked { get; set; }

    /// <summary>
    /// Gets or sets the bound font size and line height.
    /// </summary>
    public float FontSize { get; set; }

    /// <summary>
    /// Gets or sets the font size before scaling.
    /// </summary>
    public float FontSizeBase { get; set; }

    /// <summary>
    /// Gets or sets the ratio of the baked font size to <see cref="FontSize"/>.
    /// </summary>
    public float FontBakedScale { get; set; }

    /// <summary>
    /// Gets or sets the current rasterizer density used when acquiring a baked font.
    /// </summary>
    public float FontRasterizerDensity { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the current pixel density used for font rasterization and display scaling.
    /// </summary>
    public float CurrentPixelDensity
    {
        get => FontRasterizerDensity;
        set => FontRasterizerDensity = value;
    }

    /// <summary>
    /// Gets or sets the label used to represent mixed / indeterminate values. Defaults to "Mixed".
    /// </summary>
    public string MixedValueLabel { get; set; } = "Mixed";

    /// <summary>
    /// Gets or sets the DPI scale of the current window or viewport.
    /// </summary>
    public float CurrentDpiScale { get; set; }

    /// <summary>
    /// Gets or sets the shared draw-list state returned by the public accessor.
    /// </summary>
    public ImDrawListSharedData DrawListSharedData { get; set; } = new();

    /// <summary>
    /// Gets or sets the child identifier currently being ended.
    /// </summary>
    public uint WithinEndChildID { get; set; }

    /// <summary>
    /// Gets or sets the popup identifier currently being ended.
    /// </summary>
    public uint WithinEndPopupID { get; set; }

    /// <summary>
    /// Gets or sets the opaque test-engine user data.
    /// </summary>
    public IntPtr TestEngine { get; set; }

    #endregion

    #region Inputs

    /// <summary>
    /// Gets the queued input events waiting to be trickled into the input state.
    /// </summary>
    public List<ImGuiInputEvent> InputEventsQueue { get; } = new();

    /// <summary>
    /// Gets the history of input events processed during the frame.
    /// </summary>
    public List<ImGuiInputEvent> InputEventsTrail { get; } = new();

    /// <summary>
    /// Gets or sets the mouse source assumed for the next mouse event.
    /// </summary>
    public ImGuiMouseSource InputEventsNextMouseSource { get; set; } = ImGuiMouseSource.Mouse;

    /// <summary>
    /// Gets or sets the next sequential input-event identifier.
    /// </summary>
    public uint InputEventsNextEventId { get; set; } = 1;

    #endregion

    #region Windows state

    /// <summary>
    /// Gets the windows sorted in display order, from back to front.
    /// </summary>
    public List<ImGuiWindow> Windows { get; } = new();

    /// <summary>
    /// Gets the root windows sorted in focus order, from back to front.
    /// </summary>
    public List<ImGuiWindow> WindowsFocusOrder { get; } = new();

    /// <summary>
    /// Gets the temporary sort buffer used at <c>EndFrame()</c> to order parents before children.
    /// </summary>
    public List<ImGuiWindow> WindowsTempSortBuffer { get; } = new();

    /// <summary>
    /// Gets the stack of pushed windows.
    /// </summary>
    public List<ImGuiWindowStackData> CurrentWindowStack { get; } = new();

    /// <summary>
    /// Gets the map from window identifier to <see cref="ImGuiWindow"/>.
    /// </summary>
    public Dictionary<uint, ImGuiWindow> WindowsById { get; } = new();

    /// <summary>
    /// Gets or sets the number of unique windows submitted this frame.
    /// </summary>
    public int WindowsActiveCount { get; set; }

    /// <summary>
    /// Gets or sets the hover padding around resizable windows.
    /// </summary>
    public float WindowsBorderHoverPadding { get; set; }

    /// <summary>
    /// Gets or sets the window identifier that triggers a debug break in <c>Begin()</c>.
    /// </summary>
    public uint DebugBreakInWindow { get; set; }

    /// <summary>
    /// Gets or sets the window currently being drawn into.
    /// </summary>
    public ImGuiWindow? CurrentWindow { get; set; }

    /// <summary>
    /// Gets or sets the window the mouse is hovering.
    /// </summary>
    public ImGuiWindow? HoveredWindow { get; set; }

    /// <summary>
    /// Gets or sets the hovered window ignoring the moving window.
    /// </summary>
    public ImGuiWindow? HoveredWindowUnderMovingWindow { get; set; }

    /// <summary>
    /// Gets or sets the hovered window filled even when mouse input is disabled.
    /// </summary>
    public ImGuiWindow? HoveredWindowBeforeClear { get; set; }

    /// <summary>
    /// Gets or sets the window clicked to preserve focus while being moved.
    /// </summary>
    public ImGuiWindow? MovingWindow { get; set; }

    /// <summary>
    /// Gets or sets the window on which the current wheel sequence started.
    /// </summary>
    public ImGuiWindow? WheelingWindow { get; set; }

    /// <summary>
    /// Gets or sets the reference mouse position for wheeling.
    /// </summary>
    public ImVec2 WheelingWindowRefMousePos { get; set; }

    /// <summary>
    /// Gets or sets the frame on which wheeling started.
    /// </summary>
    public int WheelingWindowStartFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the frame on which wheeling last scrolled.
    /// </summary>
    public int WheelingWindowScrolledFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the timer before the wheeling window is released.
    /// </summary>
    public float WheelingWindowReleaseTimer { get; set; }

    /// <summary>
    /// Gets or sets the wheel remainder carried to the next window.
    /// </summary>
    public ImVec2 WheelingWindowWheelRemainder { get; set; }

    /// <summary>
    /// Gets or sets the averaged wheel axis.
    /// </summary>
    public ImVec2 WheelingAxisAvg { get; set; }

    #endregion

    #region Item/widget state and tracking

    /// <summary>
    /// Gets or sets the identifier recorded when duplicate item identifiers are detected.
    /// </summary>
    public uint DebugDrawIdConflictsId { get; set; }

    /// <summary>
    /// Gets or sets the identifier recorded by the identifier-stack debug hook.
    /// </summary>
    public uint DebugHookIdInfoId { get; set; }

    /// <summary>
    /// Gets or sets the hovered widget identifier.
    /// </summary>
    public uint HoveredId { get; set; }

    /// <summary>
    /// Gets or sets the hovered widget identifier from the previous frame.
    /// </summary>
    public uint HoveredIdPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the count of items sharing the previous hovered identifier.
    /// </summary>
    public int HoveredIdPreviousFrameItemCount { get; set; }

    /// <summary>
    /// Gets or sets the contiguous hovering time.
    /// </summary>
    public float HoveredIdTimer { get; set; }

    /// <summary>
    /// Gets or sets the contiguous hover time during which the item was not active.
    /// </summary>
    public float HoveredIdNotActiveTimer { get; set; }

    /// <summary>
    /// Gets or sets whether the hovered item allows overlap.
    /// </summary>
    public bool HoveredIdAllowOverlap { get; set; }

    /// <summary>
    /// Gets or sets whether a rectangle test passed but was discarded because the item was disabled
    /// or inhibited by a popup.
    /// </summary>
    public bool HoveredIdIsDisabled { get; set; }

    /// <summary>
    /// Gets or sets whether item-add clipping is disabled for logging.
    /// </summary>
    public bool ItemUnclipByLog { get; set; }

    /// <summary>
    /// Gets or sets whether any identifier was edited this frame.
    /// </summary>
    public bool AnyIdHasBeenEditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets the active widget identifier.
    /// </summary>
    public uint ActiveId { get; set; }

    /// <summary>
    /// Gets or sets the active widget identifier seen this frame.
    /// </summary>
    public uint ActiveIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the active duration.
    /// </summary>
    public float ActiveIdTimer { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was just activated this frame.
    /// </summary>
    public bool ActiveIdIsJustActivated { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was selected on activation.
    /// </summary>
    public bool ActiveIdWasSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was the sole selection on activation.
    /// </summary>
    public bool ActiveIdWasSoleSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the active widget allows another widget to steal the active identifier.
    /// </summary>
    public bool ActiveIdAllowOverlap { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier survives a focus loss.
    /// </summary>
    public bool ActiveIdNoClearOnFocusLoss { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier previously led to a press.
    /// </summary>
    public bool ActiveIdHasBeenPressedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited during the active state.
    /// </summary>
    public bool ActiveIdHasBeenEditedBefore { get; set; }

    /// <summary>
    /// Gets or sets whether the value was edited during the current frame.
    /// </summary>
    public bool ActiveIdHasBeenEditedThisFrame { get; set; }

    /// <summary>
    /// Gets or sets whether the active identifier was activated through a shortcut.
    /// </summary>
    public bool ActiveIdFromShortcut { get; set; }

    /// <summary>
    /// Gets or sets the mouse button that activated the widget, or -1.
    /// </summary>
    public sbyte ActiveIdMouseButton { get; set; } = -1;

    /// <summary>
    /// Gets or sets the real item identifier when a disabled item was clicked.
    /// </summary>
    public uint ActiveIdDisabledId { get; set; }

    /// <summary>
    /// Gets or sets the click offset from the item's upper-left corner.
    /// </summary>
    public ImVec2 ActiveIdClickOffset { get; set; } = new(-1.0f, -1.0f);

    /// <summary>
    /// Gets or sets the input source that activated the widget.
    /// </summary>
    public ImGuiInputSource ActiveIdSource { get; set; }

    /// <summary>
    /// Gets or sets the window that owns the active identifier.
    /// </summary>
    public ImGuiWindow? ActiveIdWindow { get; set; }

    /// <summary>
    /// Gets or sets the active identifier from the previous frame.
    /// </summary>
    public uint ActiveIdPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the deactivation tracking record.
    /// </summary>
    public ImGuiDeactivatedItemData DeactivatedItemData { get; set; } = new();

    /// <summary>
    /// Gets or sets the value captured at activation for drag and slider widgets.
    /// </summary>
    public ImGuiDataTypeStorage ActiveIdValueOnActivation { get; set; } = new();

    /// <summary>
    /// Gets or sets the last nonzero active identifier, used for animation.
    /// </summary>
    public uint LastActiveId { get; set; }

    /// <summary>
    /// Gets or sets the time since the last activation.
    /// </summary>
    public float LastActiveIdTimer { get; set; }

    /// <summary>
    /// Gets or sets whether the last active identifier was selected.
    /// </summary>
    public bool LastActiveIdWasSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the last active identifier was the sole selection.
    /// </summary>
    public bool LastActiveIdWasSoleSelected { get; set; }

    #endregion

    #region Key ownership and shortcut routing

    /// <summary>
    /// Gets or sets the last time the key modifiers changed.
    /// </summary>
    public double LastKeyModsChangeTime { get; set; } = -1.0;

    /// <summary>
    /// Gets or sets the last time the key modifiers changed away from none.
    /// </summary>
    public double LastKeyModsChangeFromNoneTime { get; set; } = -1.0;

    /// <summary>
    /// Gets or sets the last time a keyboard key, as opposed to a mouse or gamepad key, was pressed.
    /// </summary>
    public double LastKeyboardKeyPressTime { get; set; } = -1.0;

    /// <summary>
    /// Gets or sets the lookup for keys that may emit character input.
    /// </summary>
    public ImBitArrayForNamedKeys KeysMayBeCharInput { get; set; } = new();

    /// <summary>
    /// Gets the per-key ownership data, one entry per named key.
    /// </summary>
    public ImGuiKeyOwnerData[] KeysOwnerData { get; } = CreateKeysOwnerData();

    /// <summary>
    /// Gets or sets the chord routing table.
    /// </summary>
    public ImGuiKeyRoutingTable KeysRoutingTable { get; set; } = new();

    /// <summary>
    /// Gets or sets the mask of navigation directions the active widget wants.
    /// </summary>
    public uint ActiveIdUsingNavDirMask { get; set; }

    /// <summary>
    /// Gets or sets whether the active widget consumes every keyboard key.
    /// </summary>
    public bool ActiveIdUsingAllKeyboardKeys { get; set; }

    /// <summary>
    /// Gets or sets the shortcut chord that triggers a debug break in shortcut routing.
    /// </summary>
    public ImGuiKey DebugBreakInShortcutRouting { get; set; }

    #endregion

    #region Next window/item data

    /// <summary>
    /// Gets or sets the focus scope of the items currently being appended.
    /// </summary>
    public uint CurrentFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the item flags of the items currently being appended.
    /// </summary>
    public ImGuiItemFlags CurrentItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the identifier targeted by the locate-item debug tool.
    /// </summary>
    public uint DebugLocateId { get; set; }

    /// <summary>
    /// Gets or sets the queued <c>SetNextItem**</c> values.
    /// </summary>
    public ImGuiNextItemData NextItemData { get; set; } = new();

    /// <summary>
    /// Gets or sets the status of the last submitted item.
    /// </summary>
    public ImGuiLastItemData LastItemData { get; set; } = new();

    /// <summary>
    /// Gets or sets the queued <c>SetNextWindow**</c> values.
    /// </summary>
    public ImGuiNextWindowData NextWindowData { get; set; } = new();

    /// <summary>
    /// Gets or sets whether debug group rectangles are drawn.
    /// </summary>
    public bool DebugShowGroupRects { get; set; }

    /// <summary>
    /// Gets or sets a request for a full garbage collection.
    /// </summary>
    public bool GcCompactAll { get; set; }

    #endregion

    #region Shared stacks

    /// <summary>
    /// Gets or sets the debug flash color index, kept adjacent to the color stack.
    /// </summary>
    public ImGuiCol DebugFlashStyleColorIdx { get; set; } = ImGuiCol.COUNT;

    /// <summary>
    /// Gets the color stack pushed by <c>PushStyleColor()</c>.
    /// </summary>
    public List<ImGuiColorMod> ColorStack { get; } = new();

    /// <summary>
    /// Gets the style-variable stack pushed by <c>PushStyleVar()</c>.
    /// </summary>
    public List<ImGuiStyleMod> StyleVarStack { get; } = new();

    /// <summary>
    /// Gets the font stack pushed by <c>PushFont()</c>.
    /// </summary>
    public List<ImFontStackData> FontStack { get; } = new();

    /// <summary>
    /// Gets the focus-scope stack pushed by <c>PushFocusScope()</c>.
    /// </summary>
    public List<ImGuiFocusScopeData> FocusScopeStack { get; } = new();

    /// <summary>
    /// Gets the item-flags stack pushed by <c>PushItemFlag()</c>.
    /// </summary>
    public List<ImGuiItemFlags> ItemFlagsStack { get; } = new();

    /// <summary>
    /// Gets the group stack pushed by <c>BeginGroup()</c>.
    /// </summary>
    public List<ImGuiGroupData> GroupStack { get; } = new();

    /// <summary>
    /// Gets the persistent open-popup stack.
    /// </summary>
    public List<ImGuiPopupData> OpenPopupStack { get; } = new();

    /// <summary>
    /// Gets the current begin-popup nesting stack.
    /// </summary>
    public List<ImGuiPopupData> BeginPopupStack { get; } = new();

    /// <summary>
    /// Gets the tree-node stack.
    /// </summary>
    public List<ImGuiTreeNodeStackData> TreeNodeStack { get; } = new();

    #endregion

    #region Viewports

    /// <summary>
    /// Gets the active viewports; there is always at least one.
    /// </summary>
    public List<ImGuiViewportP> Viewports { get; } = new();

    /// <summary>
    /// Gets or sets the current viewport.
    /// </summary>
    public ImGuiViewportP? CurrentViewport { get; set; }

    /// <summary>
    /// Gets or sets the viewport under the mouse.
    /// </summary>
    public ImGuiViewportP? MouseViewport { get; set; }

    /// <summary>
    /// Gets or sets the last viewport hovered by the mouse.
    /// </summary>
    public ImGuiViewportP? MouseLastHoveredViewport { get; set; }

    /// <summary>
    /// Gets or sets the last focused platform viewport identifier.
    /// </summary>
    public uint PlatformLastFocusedViewportId { get; set; }

    /// <summary>
    /// Gets or sets the fallback virtual monitor.
    /// </summary>
    public ImGuiPlatformMonitor FallbackMonitor { get; set; } = new();

    /// <summary>
    /// Gets or sets the bounding box of all platform monitors.
    /// </summary>
    public ImRect PlatformMonitorsFullWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the sequential viewport creation counter.
    /// </summary>
    public int ViewportCreatedCount { get; set; }

    /// <summary>
    /// Gets or sets the sequential platform-window creation counter.
    /// </summary>
    public int PlatformWindowsCreatedCount { get; set; }

    /// <summary>
    /// Gets or sets the stamp counter that tracks when the front-most window changes.
    /// </summary>
    public int ViewportFocusedStampCount { get; set; }

    #endregion

    #region Keyboard/Gamepad navigation

    /// <summary>
    /// Gets or sets whether the navigation cursor rectangle is visible.
    /// </summary>
    public bool NavCursorVisible { get; set; }

    /// <summary>
    /// Gets or sets whether the navigation-focused item is highlighted instead of the mouse-hovered
    /// item.
    /// </summary>
    public bool NavHighlightItemUnderNav { get; set; }

    /// <summary>
    /// Gets or sets whether the mouse position must be updated by navigation movement.
    /// </summary>
    public bool NavMousePosDirty { get; set; }

    /// <summary>
    /// Gets or sets whether the navigation widget was seen this frame.
    /// </summary>
    public bool NavIdIsAlive { get; set; }

    /// <summary>
    /// Gets or sets the focused item identifier for navigation.
    /// </summary>
    public uint NavId { get; set; }

    /// <summary>
    /// Gets or sets the focused window for navigation.
    /// </summary>
    public ImGuiWindow? NavWindow { get; set; }

    /// <summary>
    /// Gets or sets the focused focus scope.
    /// </summary>
    public uint NavFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the focused navigation layer.
    /// </summary>
    public ImGuiNavLayer NavLayer { get; set; } = ImGuiNavLayer.Main;

    /// <summary>
    /// Gets or sets the item flags of the focused navigation item.
    /// </summary>
    public ImGuiItemFlags NavIdItemFlags { get; set; }

    /// <summary>
    /// Gets or sets the pending navigation activation identifier.
    /// </summary>
    public uint NavActivateId { get; set; }

    /// <summary>
    /// Gets or sets the identifier whose navigation activation key is held down.
    /// </summary>
    public uint NavActivateDownId { get; set; }

    /// <summary>
    /// Gets or sets the identifier whose navigation activation key was pressed without repeat.
    /// </summary>
    public uint NavActivatePressedId { get; set; }

    /// <summary>
    /// Gets or sets the flags for the pending activation.
    /// </summary>
    public ImGuiActivateFlags NavActivateFlags { get; set; }

    /// <summary>
    /// Gets the reversed focus-scope stack for the current navigation identifier.
    /// </summary>
    public List<ImGuiFocusScopeData> NavFocusRoute { get; } = new();

    /// <summary>
    /// Gets or sets the identifier highlighted after a navigation activation.
    /// </summary>
    public uint NavHighlightActivatedId { get; set; }

    /// <summary>
    /// Gets or sets the timer for the activation highlight.
    /// </summary>
    public float NavHighlightActivatedTimer { get; set; }

    /// <summary>
    /// Gets or sets the item identifier for the pending open-context-menu action.
    /// </summary>
    public uint NavOpenContextMenuItemId { get; set; }

    /// <summary>
    /// Gets or sets the window identifier for the pending context menu.
    /// </summary>
    public uint NavOpenContextMenuWindowId { get; set; }

    /// <summary>
    /// Gets or sets the activation queued until the next frame.
    /// </summary>
    public uint NavNextActivateId { get; set; }

    /// <summary>
    /// Gets or sets the flags for the queued activation.
    /// </summary>
    public ImGuiActivateFlags NavNextActivateFlags { get; set; }

    /// <summary>
    /// Gets or sets the input source that drives navigation (keyboard or gamepad).
    /// </summary>
    public ImGuiInputSource NavInputSource { get; set; } = ImGuiInputSource.Keyboard;

    /// <summary>
    /// Gets or sets the last valid selection user data for the current window; -1 means invalid.
    /// </summary>
    public long NavLastValidSelectionUserData { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of frames during which the navigation cursor stays hidden.
    /// </summary>
    public sbyte NavCursorHideFrames { get; set; }

    #endregion

    #region Navigation: init and move requests

    /// <summary>
    /// Gets or sets whether any navigation request is pending.
    /// </summary>
    public bool NavAnyRequest { get; set; }

    /// <summary>
    /// Gets or sets whether an initialization request is pending for an appearing window.
    /// </summary>
    public bool NavInitRequest { get; set; }

    /// <summary>
    /// Gets or sets whether the initialization request originated from a move.
    /// </summary>
    public bool NavInitRequestFromMove { get; set; }

    /// <summary>
    /// Gets or sets the initialization request result.
    /// </summary>
    public ImGuiNavItemData NavInitResult { get; set; } = new();

    /// <summary>
    /// Gets or sets whether a move request was submitted and is processed on the next frame.
    /// </summary>
    public bool NavMoveSubmitted { get; set; }

    /// <summary>
    /// Gets or sets whether the move request is still scoring items.
    /// </summary>
    public bool NavMoveScoringItems { get; set; }

    /// <summary>
    /// Gets or sets whether the move request is forwarded to the next frame.
    /// </summary>
    public bool NavMoveForwardToNextFrame { get; set; }

    /// <summary>
    /// Gets or sets the move request flags.
    /// </summary>
    public ImGuiNavMoveFlags NavMoveFlags { get; set; }

    /// <summary>
    /// Gets or sets the scroll flags for the move.
    /// </summary>
    public ImGuiScrollFlags NavMoveScrollFlags { get; set; }

    /// <summary>
    /// Gets or sets the key modifiers of the move request.
    /// </summary>
    public ImGuiKey NavMoveKeyMods { get; set; }

    /// <summary>
    /// Gets or sets the direction of the move request.
    /// </summary>
    public ImGuiDir NavMoveDir { get; set; }

    /// <summary>
    /// Gets or sets the move direction used for debug display.
    /// </summary>
    public ImGuiDir NavMoveDirForDebug { get; set; }

    /// <summary>
    /// Gets or sets the clipping direction of the move.
    /// </summary>
    public ImGuiDir NavMoveClipDir { get; set; }

    /// <summary>
    /// Gets or sets the scoring rectangle in screen space.
    /// </summary>
    public ImRect NavScoringRect { get; set; }

    /// <summary>
    /// Gets or sets the region the clipper must keep submitted.
    /// </summary>
    public ImRect NavScoringNoClipRect { get; set; }

    /// <summary>
    /// Gets or sets the debug scoring counter.
    /// </summary>
    public int NavScoringDebugCount { get; set; }

    /// <summary>
    /// Gets or sets the tabbing direction, where negative is backward and positive is forward.
    /// </summary>
    public int NavTabbingDir { get; set; }

    /// <summary>
    /// Gets or sets the tabbing item counter.
    /// </summary>
    public int NavTabbingCounter { get; set; }

    /// <summary>
    /// Gets or sets the best move candidate within the navigation window.
    /// </summary>
    public ImGuiNavItemData NavMoveResultLocal { get; set; } = new();

    /// <summary>
    /// Gets or sets the best mostly-visible move candidate.
    /// </summary>
    public ImGuiNavItemData NavMoveResultLocalVisible { get; set; } = new();

    /// <summary>
    /// Gets or sets the best move candidate in the flattened hierarchy.
    /// </summary>
    public ImGuiNavItemData NavMoveResultOther { get; set; } = new();

    /// <summary>
    /// Gets or sets the first tabbing candidate.
    /// </summary>
    public ImGuiNavItemData NavTabbingResultFirst { get; set; } = new();

    #endregion

    #region Navigation: record of last move request

    /// <summary>
    /// Gets or sets the focus scope moved from.
    /// </summary>
    public uint NavJustMovedFromFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the identifier moved to.
    /// </summary>
    public uint NavJustMovedToId { get; set; }

    /// <summary>
    /// Gets or sets the focus scope moved to.
    /// </summary>
    public uint NavJustMovedToFocusScopeId { get; set; }

    /// <summary>
    /// Gets or sets the key modifiers of the last move.
    /// </summary>
    public ImGuiKey NavJustMovedToKeyMods { get; set; }

    /// <summary>
    /// Gets or sets whether the last move was a tabbing move.
    /// </summary>
    public bool NavJustMovedToIsTabbing { get; set; }

    /// <summary>
    /// Gets or sets whether the last move result carried selection data.
    /// </summary>
    public bool NavJustMovedToHasSelectionData { get; set; }

    /// <summary>
    /// Gets or sets whether navigation just moved into an initialization state.
    /// </summary>
    public bool NavJustMovedToIsInit { get; set; }

    #endregion

    #region Navigation: extra config options

    /// <summary>
    /// Gets or sets whether Tab and Shift+Tab tabbing is enabled.
    /// </summary>
    public bool ConfigNavEnableTabbing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the gamepad menu button may drive windowing.
    /// </summary>
    public bool ConfigNavWindowingWithGamepad { get; set; } = true;

    /// <summary>
    /// Gets or sets the next-window shortcut chord.
    /// </summary>
    public ImGuiKey ConfigNavWindowingKeyNext { get; set; }

    /// <summary>
    /// Gets or sets the previous-window shortcut chord.
    /// </summary>
    public ImGuiKey ConfigNavWindowingKeyPrev { get; set; }

    #endregion

    #region Navigation: windowing

    /// <summary>
    /// Gets or sets the target window during Ctrl+Tab windowing.
    /// </summary>
    public ImGuiWindow? NavWindowingTarget { get; set; }

    /// <summary>
    /// Gets or sets the last valid windowing target, retained for the fade-out animation.
    /// </summary>
    public ImGuiWindow? NavWindowingTargetAnim { get; set; }

    /// <summary>
    /// Gets or sets the internal Ctrl+Tab list window.
    /// </summary>
    public ImGuiWindow? NavWindowingListWindow { get; set; }

    /// <summary>
    /// Gets or sets the windowing animation timer.
    /// </summary>
    public float NavWindowingTimer { get; set; }

    /// <summary>
    /// Gets or sets the windowing highlight alpha.
    /// </summary>
    public float NavWindowingHighlightAlpha { get; set; }

    /// <summary>
    /// Gets or sets the input source that drives windowing.
    /// </summary>
    public ImGuiInputSource NavWindowingInputSource { get; set; }

    /// <summary>
    /// Gets or sets whether the menu layer toggle is held.
    /// </summary>
    public bool NavWindowingToggleLayer { get; set; }

    /// <summary>
    /// Gets or sets the key used for the windowing layer toggle.
    /// </summary>
    public ImGuiKey NavWindowingToggleKey { get; set; }

    /// <summary>
    /// Gets or sets the accumulated windowing move delta.
    /// </summary>
    public ImVec2 NavWindowingAccumDeltaPos { get; set; }

    /// <summary>
    /// Gets or sets the accumulated windowing resize delta.
    /// </summary>
    public ImVec2 NavWindowingAccumDeltaSize { get; set; }

    #endregion

    #region Render

    /// <summary>
    /// Gets or sets the fade ratio, in the range 0.1, for the dimming background.
    /// </summary>
    public float DimBgRatio { get; set; }

    #endregion

    #region Drag and drop

    /// <summary>
    /// Gets or sets whether a drag-and-drop operation is active.
    /// </summary>
    public bool DragDropActive { get; set; }

    /// <summary>
    /// Gets or sets whether execution is inside a drag-source block.
    /// </summary>
    public bool DragDropWithinSource { get; set; }

    /// <summary>
    /// Gets or sets whether execution is inside a drag-target block.
    /// </summary>
    public bool DragDropWithinTarget { get; set; }

    /// <summary>
    /// Gets or sets the drag-source flags.
    /// </summary>
    public ImGuiDragDropFlags DragDropSourceFlags { get; set; }

    /// <summary>
    /// Gets or sets the frame on which the drag started.
    /// </summary>
    public int DragDropSourceFrameCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the mouse button that started the drag, or -1.
    /// </summary>
    public int DragDropMouseButton { get; set; } = -1;

    /// <summary>
    /// Gets or sets the current drag payload.
    /// </summary>
    public ImGuiPayload DragDropPayload { get; set; } = new();

    /// <summary>
    /// Gets or sets the rectangle of the current target candidate.
    /// </summary>
    public ImRect DragDropTargetRect { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle at item draw time.
    /// </summary>
    public ImRect DragDropTargetClipRect { get; set; }

    /// <summary>
    /// Gets or sets the current target identifier.
    /// </summary>
    public uint DragDropTargetId { get; set; }

    /// <summary>
    /// Gets or sets the viewport-wide target identifier.
    /// </summary>
    public uint DragDropTargetFullViewport { get; set; }

    /// <summary>
    /// Gets or sets the accept flags for the current frame.
    /// </summary>
    public ImGuiDragDropFlags DragDropAcceptFlagsCurr { get; set; }

    /// <summary>
    /// Gets or sets the accept flags from the previous frame.
    /// </summary>
    public ImGuiDragDropFlags DragDropAcceptFlagsPrev { get; set; }

    /// <summary>
    /// Gets or sets the target item surface used for overlap resolution.
    /// </summary>
    public float DragDropAcceptIdCurrRectSurface { get; set; }

    /// <summary>
    /// Gets or sets the target identifier at accept time.
    /// </summary>
    public uint DragDropAcceptIdCurr { get; set; }

    /// <summary>
    /// Gets or sets the target identifier from the previous frame.
    /// </summary>
    public uint DragDropAcceptIdPrev { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which a target accepted the source.
    /// </summary>
    public int DragDropAcceptFrameCount { get; set; } = -1;

    /// <summary>
    /// Gets or sets the identifier that caused a button press while holding a payload.
    /// </summary>
    public uint DragDropHoldJustPressedId { get; set; }

    /// <summary>
    /// Gets the heap payload buffer.
    /// </summary>
    public List<byte> DragDropPayloadBufHeap { get; } = new();

    /// <summary>
    /// Gets the small inline payload buffer.
    /// </summary>
    public byte[] DragDropPayloadBufLocal { get; } = new byte[16];

    #endregion

    #region Clipper

    /// <summary>
    /// Gets or sets the count of stacked clipper temporary-data entries.
    /// </summary>
    public int ClipperTempDataStacked { get; set; }

    /// <summary>
    /// Gets the pool of clipper temporary-data entries, one per nesting level of active clippers.
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
    /// submitted.
    /// </summary>
    /// <remarks>
    /// <c>BeginTableEx()</c> assigns the innermost table and <c>EndTable()</c> restores the parent
    /// table from the stacked temporary data, so this member always names the table that owns the
    /// current column cursor.
    /// </remarks>
    public ImGuiTable? CurrentTable { get; set; }

    /// <summary>
    /// Gets or sets the table identifier that triggers a debug break in <c>BeginTable()</c>.
    /// </summary>
    public uint DebugBreakInTable { get; set; }

    /// <summary>
    /// Gets or sets the count of temporary table-data entries currently stacked.
    /// </summary>
    public int TablesTempDataStacked { get; set; }

    /// <summary>
    /// Gets the pool of temporary table-data entries, one per nesting level of stacked tables.
    /// </summary>
    /// <remarks>
    /// Canonical stores the pool in an <c>ImVector&lt;ImGuiTableTempData&gt;</c> that grows but is
    /// never shrunk during a frame; this port uses a list with the same append-and-index discipline.
    /// </remarks>
    public List<ImGuiTableTempData> TablesTempData { get; } = new();

    /// <summary>
    /// Gets the persistent table pool, addressed by the index stored in each window's
    /// <see cref="ImGuiWindowTempData.CurrentTableIdx"/>.
    /// </summary>
    /// <remarks>
    /// Canonical stores the tables in an <c>ImPool&lt;ImGuiTable&gt;</c> keyed by table identifier.
    /// This port uses a list keyed by index; table lookup by identifier is a linear scan because
    /// tables are removed only by explicit debug and garbage-collection paths.
    /// </remarks>
    public List<ImGuiTable> Tables { get; } = new();

    /// <summary>
    /// Gets or sets the frame on which each table was last active, stored as a structure of arrays
    /// for garbage collection.
    /// </summary>
    public List<float> TablesLastTimeActive { get; } = new();

    /// <summary>
    /// Gets the temporary draw-channel merge buffer.
    /// </summary>
    public List<ImDrawChannel> DrawChannelsTempMergeBuffer { get; } = new();

    #endregion

    #region Tab bars

    /// <summary>
    /// Gets or sets the active tab bar being submitted.
    /// </summary>
    public ImGuiTabBar? CurrentTabBar { get; set; }

    /// <summary>
    /// Gets the pool of persistent tab bars.
    /// </summary>
    public ImPool<ImGuiTabBar> TabBars { get; } = new();

    /// <summary>
    /// Gets the nested tab-bar stack.
    /// </summary>
    public List<ImGuiTabBar> CurrentTabBarStack { get; } = new();

    /// <summary>
    /// Gets the width-shrinking scratch buffer.
    /// </summary>
    public List<ImGuiShrinkWidthItem> ShrinkWidthBuffer { get; } = new();

    #endregion

    #region Multi-Select state

    /// <summary>
    /// Gets the single active box-selection state record.
    /// </summary>
    /// <remarks>
    /// Only one box-selection can be active at a time, so canonical stores the record directly on the
    /// context instead of in a pool.
    /// </remarks>
    public ImGuiBoxSelectState BoxSelectState { get; } = new();

    /// <summary>
    /// Gets or sets the temporary multi-select data for the scope currently being submitted, or
    /// <c>null</c> when no <c>BeginMultiSelect()</c> scope is open.
    /// </summary>
    /// <remarks>
    /// The value points into <see cref="MultiSelectTempData"/> at index
    /// <see cref="MultiSelectTempDataStacked"/> minus one, so nested scopes restore the parent on
    /// <c>EndMultiSelect()</c>.
    /// </remarks>
    public ImGuiMultiSelectTempData? CurrentMultiSelect { get; set; }

    /// <summary>
    /// Gets or sets the count of temporary multi-select data entries currently stacked.
    /// </summary>
    public int MultiSelectTempDataStacked { get; set; }

    /// <summary>
    /// Gets the stacked temporary multi-select data entries.
    /// </summary>
    /// <remarks>
    /// Entries are retained between frames so that the request buffer of each nesting level keeps its
    /// capacity, mirroring canonical <c>ImGuiMultiSelectTempData::Clear()</c>.
    /// </remarks>
    public List<ImGuiMultiSelectTempData> MultiSelectTempData { get; } = new();

    /// <summary>
    /// Gets the pool of persistent multi-select state records keyed by selection scope identifier.
    /// </summary>
    public ImPool<ImGuiMultiSelectState> MultiSelectStorage { get; } = new();

    #endregion

    #region Hover delay system

    /// <summary>
    /// Gets or sets the item currently inside the hover delay.
    /// </summary>
    public uint HoverItemDelayId { get; set; }

    /// <summary>
    /// Gets or sets the hover-delay item from the previous frame.
    /// </summary>
    public uint HoverItemDelayIdPreviousFrame { get; set; }

    /// <summary>
    /// Gets or sets the hover delay timer.
    /// </summary>
    public float HoverItemDelayTimer { get; set; }

    /// <summary>
    /// Gets or sets the grace time before the tooltip hover timer is cleared.
    /// </summary>
    public float HoverItemDelayClearTimer { get; set; }

    /// <summary>
    /// Gets or sets the item at which the mouse was once stationary.
    /// </summary>
    public uint HoverItemUnlockedStationaryId { get; set; }

    /// <summary>
    /// Gets or sets the window at which the mouse was once stationary.
    /// </summary>
    public uint HoverWindowUnlockedStationaryId { get; set; }

    #endregion

    #region Mouse state

    /// <summary>
    /// Gets or sets the current mouse cursor.
    /// </summary>
    public ImGuiMouseCursor MouseCursor { get; set; } = ImGuiMouseCursor.Arrow;

    /// <summary>
    /// Gets or sets the time the mouse has been stationary.
    /// </summary>
    public float MouseStationaryTimer { get; set; }

    /// <summary>
    /// Gets or sets the last valid mouse position.
    /// </summary>
    public ImVec2 MouseLastValidPos { get; set; }

    #endregion

    #region Widget state

    /// <summary>
    /// Gets or sets the active input-text state.
    /// </summary>
    public ImGuiInputTextState InputTextState { get; set; } = new();

    /// <summary>
    /// Gets or sets the temporary line index used with the input-text state.
    /// </summary>
    public ImGuiTextIndex InputTextLineIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the backup of a text state that just deactivated.
    /// </summary>
    public ImGuiInputTextDeactivatedState InputTextDeactivatedState { get; set; } = new();

    /// <summary>
    /// Gets or sets the backup of the baked password font.
    /// </summary>
    public ImFontBaked? InputTextPasswordFontBackupBaked { get; set; }

    /// <summary>
    /// Gets or sets the backup of the password font flags.
    /// </summary>
    public ImFontFlags InputTextPasswordFontBackupFlags { get; set; }

    /// <summary>
    /// Gets or sets the input-text identifier to reactivate on the next frame.
    /// </summary>
    public uint InputTextReactivateId { get; set; }

    /// <summary>
    /// Gets or sets the temporary text-input identifier used for Ctrl+Click sliders.
    /// </summary>
    public uint TempInputId { get; set; }

    /// <summary>
    /// Gets or sets the zero value used for every data type.
    /// </summary>
    public ImGuiDataTypeStorage DataTypeZeroValue { get; set; } = new();

    /// <summary>
    /// Gets or sets the current menu nesting depth.
    /// </summary>
    public int BeginMenuDepth { get; set; }

    /// <summary>
    /// Gets or sets the current combo nesting depth.
    /// </summary>
    public int BeginComboDepth { get; set; }

    /// <summary>
    /// Gets or sets the current color-edit identifier.
    /// </summary>
    public uint ColorEditCurrentID { get; set; }

    /// <summary>
    /// Gets or sets the saved color-edit identifier.
    /// </summary>
    public uint ColorEditSavedID { get; set; }

    /// <summary>
    /// Gets or sets the saved hue used for lossless color round trips.
    /// </summary>
    public float ColorEditSavedHue { get; set; }

    /// <summary>
    /// Gets or sets the saved saturation.
    /// </summary>
    public float ColorEditSavedSat { get; set; }

    /// <summary>
    /// Gets or sets the saved RGB color with a zero alpha.
    /// </summary>
    public uint ColorEditSavedColor { get; set; }

    /// <summary>
    /// Gets or sets the reference color captured when the picker opened.
    /// </summary>
    public ImVec4 ColorPickerRef { get; set; }

    /// <summary>
    /// Gets or sets the combo preview backup.
    /// </summary>
    public ImGuiComboPreviewData ComboPreviewData { get; set; } = new();

    /// <summary>
    /// Gets or sets the expected window resize border rectangle.
    /// </summary>
    public ImRect WindowResizeBorderExpectedRect { get; set; }

    /// <summary>
    /// Gets or sets whether relative window resizing is active.
    /// </summary>
    public bool WindowResizeRelativeMode { get; set; }

    /// <summary>
    /// Gets or sets the scrollbar page-seek mode.
    /// </summary>
    public short ScrollbarSeekMode { get; set; }

    /// <summary>
    /// Gets or sets the offset from the scrollbar click to the grab center.
    /// </summary>
    public float ScrollbarClickDeltaToGrabCenter { get; set; }

    /// <summary>
    /// Gets or sets the slider grab click offset.
    /// </summary>
    public float SliderGrabClickOffset { get; set; }

    /// <summary>
    /// Gets or sets the accumulated slider delta.
    /// </summary>
    public float SliderCurrentAccum { get; set; }

    /// <summary>
    /// Gets or sets whether the slider accumulator changed.
    /// </summary>
    public bool SliderCurrentAccumDirty { get; set; }

    /// <summary>
    /// Gets or sets whether the drag accumulator changed.
    /// </summary>
    public bool DragCurrentAccumDirty { get; set; }

    /// <summary>
    /// Gets or sets the high-precision drag accumulator.
    /// </summary>
    public float DragCurrentAccum { get; set; }

    /// <summary>
    /// Gets or sets the default drag speed ratio used when the requested speed is zero.
    /// </summary>
    public float DragSpeedDefaultRatio { get; set; } = 1.0f / 100.0f;

    /// <summary>
    /// Gets or sets the backup of the style alpha used by <c>BeginDisabled()</c>.
    /// </summary>
    public float DisabledAlphaBackup { get; set; }

    /// <summary>
    /// Gets or sets the disabled-stack depth.
    /// </summary>
    public short DisabledStackSize { get; set; }

    /// <summary>
    /// Gets or sets the tooltip override count.
    /// </summary>
    public short TooltipOverrideCount { get; set; }

    /// <summary>
    /// Gets or sets the window of the last tooltip submitted this frame.
    /// </summary>
    public ImGuiWindow? TooltipPreviousWindow { get; set; }

    /// <summary>
    /// Gets the data used by the default clipboard handler.
    /// </summary>
    public List<byte> ClipboardHandlerData { get; } = new();

    /// <summary>
    /// Gets the menu identifiers rendered at least once this frame.
    /// </summary>
    public List<uint> MenusIdSubmittedThisFrame { get; } = new();

    /// <summary>
    /// Gets or sets the state consumed and produced by <c>GetTypingSelectRequest()</c>.
    /// </summary>
    public ImGuiTypingSelectState TypingSelectState { get; set; } = new();

    #endregion

    #region Platform support

    /// <summary>
    /// Gets or sets the IME data updated during the frame.
    /// </summary>
    public ImGuiPlatformImeData PlatformImeData { get; set; } = new();

    /// <summary>
    /// Gets or sets the previous frame IME data, kept deliberately different to force submission.
    /// </summary>
    public ImGuiPlatformImeData PlatformImeDataPrev { get; set; } = new();

    #endregion

    #region Extensions

    /// <summary>
    /// Gets the user and extension-managed texture records.
    /// </summary>
    public List<ImTextureData> UserTextures { get; } = new();

    #endregion

    #region Docking

    /// <summary>
    /// Gets the docking subsystem context.
    /// </summary>
    public ImGuiDockContext DockContext { get; } = new();

    /// <summary>
    /// Gets or sets the custom or default handler for dock node window menus.
    /// </summary>
    public Action<ImGuiContext, ImGuiDockNode, ImGuiTabBar>? DockNodeWindowMenuHandler { get; set; }

    #endregion

    #region Settings

    /// <summary>
    /// Gets or sets the packed platform session date, when valid.
    /// </summary>
    public ImGuiPackedDate SessionDate { get; set; }

    /// <summary>
    /// Gets or sets whether layout settings were loaded.
    /// </summary>
    public bool SettingsLoaded { get; set; }

    /// <summary>
    /// Gets or sets the timer before layout settings are saved.
    /// </summary>
    public float SettingsDirtyTimer { get; set; }

    /// <summary>
    /// Gets or sets the in-memory layout data.
    /// </summary>
    public ImGuiTextBuffer SettingsIniData { get; set; } = new();

    /// <summary>
    /// Gets the registered `.ini` section handlers, in registration order.
    /// </summary>
    public List<ImGuiSettingsHandler> SettingsHandlers { get; } = new();

    /// <summary>
    /// Gets the window settings entries. Canonical stores them in a variable-length chunk stream;
    /// this port uses a plain list until the settings chunk realizes the stream.
    /// </summary>
    public List<ImGuiWindowSettings> SettingsWindows { get; } = new();

    /// <summary>
    /// Gets the table layout entries loaded from and saved to the layout file.
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
    /// </summary>
    public List<ImGuiContextHook> Hooks { get; } = new();

    /// <summary>
    /// Gets or sets the next available hook identifier.
    /// </summary>
    public uint HookIdNext { get; set; }

    /// <summary>
    /// Gets or sets the demo section-marker callback.
    /// </summary>
    public ImGuiDemoMarkerCallback? DemoMarkerCallback { get; set; }

    #endregion

    #region Localization

    /// <summary>
    /// Gets the localization text for each localization key, or <see langword="null"/> when the key
    /// has no registration.
    /// </summary>
    public string?[] LocalizationTable { get; } = new string?[(int)ImGuiLocKey.COUNT];

    #endregion

    #region Capture/Logging

    /// <summary>
    /// Gets or sets whether capture is active.
    /// </summary>
    public bool LogEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the next logged item starts a new line.
    /// </summary>
    public bool LogLineFirstItem { get; set; }

    /// <summary>
    /// Gets or sets the capture flags and type.
    /// </summary>
    public ImGuiLogFlags LogFlags { get; set; }

    /// <summary>
    /// Gets or sets the log window.
    /// </summary>
    public ImGuiWindow? LogWindow { get; set; }

    /// <summary>
    /// Gets or sets the log output writer, or <see langword="null"/> when logging in memory.
    /// Canonical stores a <c>FILE*</c>; this port stores a managed writer.
    /// </summary>
    public System.IO.TextWriter? LogFile { get; set; }

    /// <summary>
    /// Gets or sets the accumulation buffer used when logging to the clipboard.
    /// </summary>
    public ImGuiTextBuffer LogBuffer { get; set; } = new();

    /// <summary>
    /// Gets or sets the prefix for the next log line.
    /// </summary>
    public string? LogNextPrefix { get; set; }

    /// <summary>
    /// Gets or sets the suffix for the next log line.
    /// </summary>
    public string? LogNextSuffix { get; set; }

    /// <summary>
    /// Gets or sets the Y position of the current log line.
    /// </summary>
    public float LogLinePosY { get; set; } = float.MaxValue;

    /// <summary>
    /// Gets or sets the log depth reference.
    /// </summary>
    public int LogDepthRef { get; set; }

    /// <summary>
    /// Gets or sets the depth to expand in logs.
    /// </summary>
    public int LogDepthToExpand { get; set; } = 2;

    /// <summary>
    /// Gets or sets the default depth to expand.
    /// </summary>
    public int LogDepthToExpandDefault { get; set; } = 2;

    #endregion

    #region Error handling

    /// <summary>
    /// Gets or sets the error callback, or <see langword="null"/>.
    /// </summary>
    public ImGuiErrorCallback? ErrorCallback { get; set; }

    /// <summary>
    /// Gets or sets the opaque data supplied to the error callback.
    /// </summary>
    public IntPtr ErrorCallbackUserData { get; set; }

    /// <summary>
    /// Gets or sets the locked position of the error tooltip.
    /// </summary>
    public ImVec2 ErrorTooltipLockedPos { get; set; }

    /// <summary>
    /// Gets or sets whether the next reported error is the first of the frame.
    /// </summary>
    public bool ErrorFirst { get; set; } = true;

    /// <summary>
    /// Gets or sets the number of errors submitted this frame.
    /// </summary>
    public int ErrorCountCurrentFrame { get; set; }

    /// <summary>
    /// Gets or sets the stack-size snapshot taken at <c>NewFrame()</c>.
    /// </summary>
    public ImGuiErrorRecoveryState StackSizesInNewFrame { get; set; } = new();

    /// <summary>
    /// Gets or sets the alias to the current window's stack-size snapshot, or
    /// <see langword="null"/> when no window is current.
    /// </summary>
    public ImGuiErrorRecoveryState? StackSizesInBeginForCurrentWindow { get; set; }

    #endregion

    #region Debug tools

    /// <summary>
    /// Gets or sets the locked identifier-conflict count, preserved while Ctrl is held.
    /// </summary>
    public int DebugDrawIdConflictsCount { get; set; }

    /// <summary>
    /// Gets or sets the debug log flags.
    /// </summary>
    public ImGuiDebugLogFlags DebugLogFlags { get; set; } = ImGuiDebugLogFlags.EventError | ImGuiDebugLogFlags.OutputToTTY;

    /// <summary>
    /// Gets or sets the debug log buffer.
    /// </summary>
    public ImGuiTextBuffer DebugLogBuf { get; set; } = new();

    /// <summary>
    /// Gets or sets the debug log line index.
    /// </summary>
    public ImGuiTextIndex DebugLogIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the count of skipped errors.
    /// </summary>
    public int DebugLogSkippedErrors { get; set; }

    /// <summary>
    /// Gets or sets the debug log auto-disable flags.
    /// </summary>
    public ImGuiDebugLogFlags DebugLogAutoDisableFlags { get; set; }

    /// <summary>
    /// Gets or sets the number of frames remaining before the debug log auto-disables.
    /// </summary>
    public byte DebugLogAutoDisableFrames { get; set; }

    /// <summary>
    /// Gets or sets the number of frames remaining for the locate-item highlight.
    /// </summary>
    public byte DebugLocateFrames { get; set; }

    /// <summary>
    /// Gets or sets whether to break in the item-add pass for the located identifier.
    /// </summary>
    public bool DebugBreakInLocateId { get; set; }

    /// <summary>
    /// Gets or sets the debug break key chord.
    /// </summary>
    public ImGuiKey DebugBreakKeyChord { get; set; }

    /// <summary>
    /// Gets or sets the cull depth cycled by the begin-return-value debug tool, or -1.
    /// </summary>
    public sbyte DebugBeginReturnValueCullDepth { get; set; } = -1;

    /// <summary>
    /// Gets or sets whether the item picker is active.
    /// </summary>
    public bool DebugItemPickerActive { get; set; }

    /// <summary>
    /// Gets or sets the item-picker mouse button.
    /// </summary>
    public byte DebugItemPickerMouseButton { get; set; } = (byte)ImGuiMouseButton.Left;

    /// <summary>
    /// Gets or sets the identifier that triggers a debug break in the item picker.
    /// </summary>
    public uint DebugItemPickerBreakId { get; set; }

    /// <summary>
    /// Gets or sets the flash-color time.
    /// </summary>
    public float DebugFlashStyleColorTime { get; set; }

    /// <summary>
    /// Gets or sets the flash-color backup.
    /// </summary>
    public ImVec4 DebugFlashStyleColorBackup { get; set; }

    /// <summary>
    /// Gets or sets the metrics/debugger window options.
    /// </summary>
    public ImGuiMetricsConfig DebugMetricsConfig { get; set; } = new();

    /// <summary>
    /// Gets or sets the allocation-activity record shown by the metrics window.
    /// </summary>
    public ImGuiDebugAllocInfo DebugAllocInfo { get; set; } = new();

    /// <summary>
    /// Gets or sets the identifier-stack debug query state.
    /// </summary>
    public ImGuiDebugItemPathQuery DebugItemPathQuery { get; set; } = new();

    /// <summary>
    /// Gets or sets the identifier-stack tool options.
    /// </summary>
    public ImGuiIDStackTool DebugIDStackTool { get; set; } = new();

    /// <summary>
    /// Gets or sets the hovered docking node for debug inspection.
    /// </summary>
    public ImGuiDockNode? DebugHoveredDockNode { get; set; }

    /// <summary>
    /// Gets the identifier-conflict storage used when highlighting all conflicts is enabled.
    /// </summary>
    public ImGuiStorage DebugDrawIdConflictsAliveCount { get; } = new();

    /// <summary>
    /// Gets the identifier-conflict highlight set used when highlighting all conflicts is enabled.
    /// </summary>
    public ImGuiStorage DebugDrawIdConflictsHighlightSet { get; } = new();

    #endregion

    #region Miscellaneous

    /// <summary>
    /// Gets the framerate estimate over the last sixty frames.
    /// </summary>
    public float[] FramerateSecPerFrame { get; } = new float[60];

    /// <summary>
    /// Gets or sets the ring-buffer index for <see cref="FramerateSecPerFrame"/>.
    /// </summary>
    public int FramerateSecPerFrameIdx { get; set; }

    /// <summary>
    /// Gets or sets the number of framerate samples collected.
    /// </summary>
    public int FramerateSecPerFrameCount { get; set; }

    /// <summary>
    /// Gets or sets the accumulated frame time.
    /// </summary>
    public float FramerateSecPerFrameAccum { get; set; }

    /// <summary>
    /// Gets or sets the mouse-capture override for the next frame, or -1 for no override.
    /// </summary>
    public int WantCaptureMouseNextFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the keyboard-capture override for the next frame, or -1 for no override.
    /// </summary>
    public int WantCaptureKeyboardNextFrame { get; set; } = -1;

    /// <summary>
    /// Gets or sets the text-input override for the next frame, or -1 for no override.
    /// </summary>
    public int WantTextInputNextFrame { get; set; } = -1;

    /// <summary>
    /// Gets the temporary text buffer.
    /// </summary>
    public List<byte> TempBuffer { get; } = new();

    /// <summary>
    /// Gets or sets the temporary key-chord name buffer. Canonical stores a fixed
    /// <c>char[64]</c>; this port stores a managed string.
    /// </summary>
    public string TempKeychordName { get; set; } = string.Empty;

    #endregion

    /// <summary>
    /// Creates the per-key ownership array and initializes every entry to the "no owner" state.
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
    /// (,
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
    /// </summary>
    public ImGuiTypingSelectFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the number of valid characters in <see cref="SearchBuffer"/>.
    /// </summary>
    public int SearchBufferLen { get; set; }

    /// <summary>
    /// Gets or sets the search text. The full buffer is the search key unless
    /// <see cref="SingleCharMode"/> is set, in which case only <see cref="SingleCharSize"/> characters
    /// form the key.
    /// </summary>
    public string SearchBuffer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating that the buffer was modified this frame.
    /// </summary>
    public bool SelectRequest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the buffer holds one repeated character.
    /// </summary>
    public bool SingleCharMode { get; set; }

    /// <summary>
    /// Gets or sets the length of the first codepoint: one for ASCII, two for a UTF-16 surrogate pair.
    /// Canonical stores a UTF-8 byte length in an <c>ImS8</c>.
    /// </summary>
    public int SingleCharSize { get; set; }
}

/// <summary>
/// Holds the persistent typing-select search state across frames. This is the managed counterpart of
/// canonical <c>ImGuiTypingSelectState</c>.
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
    /// </summary>
    public ImGuiTypingSelectRequest Request { get; set; } = new();

    /// <summary>
    /// Gets or sets the persistent search text, capped at 63 characters by
    /// <c>GetTypingSelectRequest()</c>.
    /// </summary>
    public string SearchBuffer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the navigation focus scope in which the search was started.
    /// </summary>
    public uint FocusScope { get; set; }

    /// <summary>
    /// Gets or sets the frame number of the last accepted request.
    /// </summary>
    public int LastRequestFrame { get; set; }

    /// <summary>
    /// Gets or sets the time of the last accepted request, used for the reset timer.
    /// </summary>
    public float LastRequestTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that matching is locked to a single repeated character.
    /// </summary>
    public bool SingleCharModeLock { get; set; }

    /// <summary>
    /// Clears the search text and the single-character lock, deliberately preserving the remaining
    /// fields for debugging.
    /// </summary>
    public void Clear()
    {
        SearchBuffer = string.Empty;
        SingleCharModeLock = false;
    }
}

#endregion

#region Tables

// The column index typedefs
// ImGuiTableColumnIdx (ImS16) and ImGuiTableDrawChannelIdx (ImU16) are represented directly as
// `short` and `ushort`; the signed typedef's -1 "none" sentinel is preserved by the short members and
// the draw-channel typedef's (ImU8)-1 default is preserved as 255. Every canonical member is present
// in declaration order. Canonical packs several members into C bitfields; this port declares each of
// them as a separate semantic member, so the managed layout is deliberately not byte-for-byte equal
// to the canonical structure.

/// <summary>
/// One sort specification for a table column, exposed to the application through
/// <see cref="ImGuiTableSortSpecs.Specs"/>.
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
    /// </summary>
    public uint ColumnUserID { get; set; }

    /// <summary>
    /// Gets or sets the column index.
    /// </summary>
    public short ColumnIndex { get; set; }

    /// <summary>
    /// Gets or sets the position of this specification within its parent array, dense from zero.
    /// </summary>
    public short SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the current sort direction for the column.
    /// </summary>
    public ImGuiSortDirection SortDirection { get; set; }
}

/// <summary>
/// The public sorting interface returned by <c>TableGetSortSpecs()</c>, describing the active sort
/// order of a table.
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
    /// <see cref="SpecsCount"/> is zero.
    /// </summary>
    /// <remarks>
    /// The table points this at its <see cref="ImGuiTable.SortSpecsSingle"/> instance for a single
    /// sort and at the elements of <see cref="ImGuiTable.SortSpecsMulti"/> for a multi-sort.
    /// </remarks>
    public ImGuiTableColumnSortSpecs[]? Specs { get; set; }

    /// <summary>
    /// Gets or sets the number of active specifications: zero with tristate sorting, one for a normal
    /// sort, and more than one for a multi-sort.
    /// </summary>
    public int SpecsCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the specifications changed and the application should
    /// re-sort its data, after which it clears this flag.
    /// </summary>
    public bool SpecsDirty { get; set; }
}

/// <summary>
/// One angled-header request queued for <c>TableAngledHeadersRowEx()</c>.
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
    /// </summary>
    public short Index { get; set; }

    /// <summary>
    /// Gets or sets the text color of the angled header.
    /// </summary>
    public uint TextColor { get; set; }

    /// <summary>
    /// Gets or sets the base background color of the angled header.
    /// </summary>
    public uint BgColor0 { get; set; }

    /// <summary>
    /// Gets or sets the optional highlight background color of the angled header.
    /// </summary>
    public uint BgColor1 { get; set; }
}

/// <summary>
/// A transient per-row cell background request written by <c>TableSetBgColor()</c> and consumed while
/// the row is drawn.
/// </summary>
/// <remarks>
/// Canonical declares a 6-byte value struct stored in the table's row-cell span. This port declares a
/// reference type so <c>TableSetBgColor()</c> can update an existing entry in place.
/// </remarks>
public sealed class ImGuiTableCellData
{
    /// <summary>
    /// Gets or sets the resolved background color.
    /// </summary>
    public uint BgColor { get; set; }

    /// <summary>
    /// Gets or sets the column the background applies to.
    /// </summary>
    public short Column { get; set; }
}

/// <summary>
/// Setup data copied from <c>TableSetupColumn()</c> together with the previous-frame column used while
/// reconciling a column topology change.
/// </summary>
/// <remarks>
/// Canonical value-initializes one entry per unmatched setup request at
///. This port declares a reference type because the
/// reconcile path fills the entry and then reads it back within the same layout pass;
/// <see cref="ColumnOldData"/> is a separate object because canonical copies the previous column by
/// value, so the implementation calls <see cref="ImGuiTableColumn.CopyFrom"/> to reproduce that copy.
/// </remarks>
public sealed class ImGuiTableReconcileColumnData
{
    /// <summary>
    /// Gets or sets the column identifier resolved from the setup label.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the offset of the column name within the parent name buffer.
    /// </summary>
    public short NameOffset { get; set; }

    /// <summary>
    /// Gets or sets the column flags passed to <c>TableSetupColumn()</c>.
    /// </summary>
    public ImGuiTableColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the initial width or weight passed to <c>TableSetupColumn()</c>.
    /// </summary>
    public float InitWidthOrWeight { get; set; }

    /// <summary>
    /// Gets or sets the optional user data passed to <c>TableSetupColumn()</c>.
    /// </summary>
    public uint UserData { get; set; }

    /// <summary>
    /// Gets or sets the index of this request within the current table.
    /// </summary>
    public short ColumnNewIdx { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the matched column in the previous-frame table, or -1 when no match
    /// was found.
    /// </summary>
    public short ColumnOldIdx { get; set; } = -1;

    /// <summary>
    /// Gets the full backup of the matched previous-frame column, or a default-constructed column when
    /// no match was found.
    /// </summary>
    public ImGuiTableColumn ColumnOldData { get; } = new();
}

/// <summary>
/// Per-column state of a table, holding geometry, ordering, anchoring, and status flags
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
    /// </summary>
    public ImGuiTableColumnFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the final visible width, equal to <c>MaxX - MinX</c> and locked in
    /// <c>TableUpdateLayout()</c>.
    /// </summary>
    public float WidthGiven { get; set; }

    /// <summary>
    /// Gets or sets the absolute left position of the column.
    /// </summary>
    public float MinX { get; set; }

    /// <summary>
    /// Gets or sets the absolute right position of the column, including padding and spacing.
    /// </summary>
    public float MaxX { get; set; }

    /// <summary>
    /// Gets or sets the requested width for a fixed-width column; for a stretched column it is derived
    /// every layout from <see cref="StretchWeight"/>.
    /// </summary>
    public float WidthRequest { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the automatic, content-derived width.
    /// </summary>
    public float WidthAuto { get; set; }

    /// <summary>
    /// Gets or sets the maximum width at the current position; the value is overwritten by each
    /// table instance.
    /// </summary>
    public float WidthMax { get; set; }

    /// <summary>
    /// Gets or sets the stretch weight used when the column has the width-stretch flag.
    /// </summary>
    public float StretchWeight { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the raw width or weight value passed to <c>TableSetupColumn()</c>. For a width it
    /// is a content width without padding.
    /// </summary>
    public float InitStretchWeightOrWidth { get; set; }

    /// <summary>
    /// Gets or sets the clipping rectangle applied to the column.
    /// </summary>
    public ImRect ClipRect { get; set; }

    /// <summary>
    /// Gets or sets the hash of the column label, used for settings persistence and reconciliation.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the optional user data value passed to <c>TableSetupColumn()</c>.
    /// </summary>
    public uint UserData { get; set; }

    /// <summary>
    /// Gets or sets the minimum content X, approximately the column minimum plus cell padding and
    /// leading cell spacing.
    /// </summary>
    public float WorkMinX { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X, approximately the column maximum minus cell padding and
    /// trailing cell spacing.
    /// </summary>
    public float WorkMaxX { get; set; }

    /// <summary>
    /// Gets or sets the current item width for the column, preserved across rows.
    /// </summary>
    public float ItemWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X reached by frozen rows.
    /// </summary>
    public float ContentMaxXFrozen { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X reached by unfrozen rows.
    /// </summary>
    public float ContentMaxXUnfrozen { get; set; }

    /// <summary>
    /// Gets or sets the maximum content X actually used by header rows.
    /// </summary>
    public float ContentMaxXHeadersUsed { get; set; }

    /// <summary>
    /// Gets or sets the ideal unclipped header content width.
    /// </summary>
    public float ContentMaxXHeadersIdeal { get; set; }

    /// <summary>
    /// Gets or sets the offset of the column name within the parent name buffer.
    /// </summary>
    public short NameOffset { get; set; } = -1;

    /// <summary>
    /// Gets or sets the display order of the column, which the user may change by reordering.
    /// </summary>
    public short DisplayOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the column within the enabled set, which is never greater than the
    /// display order.
    /// </summary>
    public short IndexWithinEnabledSet { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the previous enabled column, or -1 when this is the first.
    /// </summary>
    public short PrevEnabledColumn { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the next enabled column, or -1 when this is the last.
    /// </summary>
    public short NextEnabledColumn { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of this column within the sort specifications, or -1 when the column is
    /// not sorted.
    /// </summary>
    public short SortOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the current index into the draw splitter channel list.
    /// </summary>
    /// <remarks>
    /// Canonical initializes this unsigned 16-bit channel index to the 8-bit sentinel
    /// <c>(ImU8)-1</c>, which is 255 rather than 65535.
    /// </remarks>
    public ushort DrawChannelCurrent { get; set; } = 255;

    /// <summary>
    /// Gets or sets the draw channel used for frozen rows.
    /// </summary>
    public ushort DrawChannelFrozen { get; set; } = 255;

    /// <summary>
    /// Gets or sets the draw channel used for unfrozen rows.
    /// </summary>
    public ushort DrawChannelUnfrozen { get; set; } = 255;

    /// <summary>
    /// Gets or sets a value indicating that the column is enabled, meaning it is both user-enabled and
    /// not disabled by flags.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the user has not hidden the column, independently of
    /// whether it is clipped by scrolling.
    /// </summary>
    public bool IsUserEnabled { get; set; }

    /// <summary>
    /// Gets or sets the pending user-enabled state applied by the next layout pass.
    /// </summary>
    public bool IsUserEnabledNextFrame { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the column overlaps the host clip rectangle horizontally.
    /// </summary>
    public bool IsVisibleX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the column is visible vertically.
    /// </summary>
    public bool IsVisibleY { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the layout requests the application to submit contents for
    /// this column.
    /// </summary>
    public bool IsRequestOutput { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that item submissions for this column are ignored.
    /// </summary>
    public bool IsSkipItems { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the automatic width is preserved across table
    /// re-initialization to avoid flicker.
    /// </summary>
    public bool IsPreserveWidthAuto { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the column was just constructed and still needs its
    /// defaults initialized.
    /// </summary>
    public bool IsJustCreated { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating that the column was matched while loading layout settings.
    /// </summary>
    public bool IsLoadedSettings { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the previous-frame column is still awaiting a reconcile
    /// match.
    /// </summary>
    public bool IsNeedReconcileSrc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the current-frame column is still awaiting a reconcile
    /// match.
    /// </summary>
    public bool IsNeedReconcileDst { get; set; }

    /// <summary>
    /// Gets or sets the navigation layer at the time the column was entered.
    /// </summary>
    public sbyte NavLayerCurrent { get; set; }

    /// <summary>
    /// Gets or sets the countdown of frames that still request an automatic width fit for this
    /// column.
    /// </summary>
    public byte AutoFitQueue { get; set; }

    /// <summary>
    /// Gets or sets the countdown of frames during which the column refuses to skip item submission.
    /// </summary>
    public byte CannotSkipItemsQueue { get; set; }

    /// <summary>
    /// Gets or sets the current sort direction, or <see cref="ImGuiSortDirection.None"/> when the
    /// column is not sorted.
    /// </summary>
    public ImGuiSortDirection SortDirection { get; set; }

    /// <summary>
    /// Gets or sets the number of sort directions available to the column, from zero to three.
    /// </summary>
    public byte SortDirectionsAvailCount { get; set; }

    /// <summary>
    /// Gets or sets the mask of sort directions available to the column, one bit per direction.
    /// </summary>
    public byte SortDirectionsAvailMask { get; set; }

    /// <summary>
    /// Gets or sets the ordered list of available sort directions, two bits per entry.
    /// </summary>
    public byte SortDirectionsAvailList { get; set; }

    /// <summary>
    /// Copies every field of another column into this one.
    /// </summary>
    /// <param name="source">The column to snapshot.</param>
    /// <remarks>
    /// Canonical copies a whole <c>ImGuiTableColumn</c> by value while restoring the previous frame's
    /// columns, while filling a reconcile request, and while writing the reconciled column back.
    /// Because this port represents the column as a reference type, a plain assignment
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
/// share one identifier.
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
    /// instance zero.
    /// </summary>
    public uint TableInstanceID { get; set; }

    /// <summary>
    /// Gets or sets the outer height measured on the last active frame.
    /// </summary>
    public float LastOuterHeight { get; set; }

    /// <summary>
    /// Gets or sets the height of the first consecutive header rows on the last active frame.
    /// </summary>
    public float LastTopHeadersRowHeight { get; set; }

    /// <summary>
    /// Gets or sets the height of the frozen section on the last active frame.
    /// </summary>
    public float LastFrozenHeight { get; set; }

    /// <summary>
    /// Gets or sets the index of the row hovered on the previous frame, or -1.
    /// </summary>
    public int HoveredRowLast { get; set; } = -1;

    /// <summary>
    /// Gets or sets the index of the row hovered on the current frame, set when the row is
    /// encountered, or -1.
    /// </summary>
    public int HoveredRowNext { get; set; } = -1;
}

/// <summary>
/// Transient per-nesting-level table state that lives only between <c>BeginTable()</c> and
/// <c>EndTable()</c> and is shared across tables of the same identifier
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
    /// </summary>
    public uint WindowID { get; set; }

    /// <summary>
    /// Gets or sets the index of the table in the context's persistent table pool.
    /// </summary>
    public int TableIndex { get; set; }

    /// <summary>
    /// Gets or sets the last timestamp at which this entry was used, for garbage collection.
    /// </summary>
    public float LastTimeActive { get; set; } = -1.0f;

    /// <summary>
    /// Gets or sets the extra width contributed by angled headers, consumed by <c>EndTable()</c>.
    /// </summary>
    public float AngledHeadersExtraWidth { get; set; }

    /// <summary>
    /// Gets the angled-header requests built by <c>TableAngledHeadersRow()</c>.
    /// </summary>
    public List<ImGuiTableHeaderData> AngledHeadersRequests { get; } = new();

    /// <summary>
    /// Gets the column reconcile requests queued by <c>TableSetupColumn()</c> and consumed by
    /// <c>TableUpdateLayout()</c>; the list is cleared every frame.
    /// </summary>
    public List<ImGuiTableReconcileColumnData> ReconcileColumnsRequests { get; } = new();

    /// <summary>
    /// Gets or sets an opaque handle to the previous frame's column arena while the column count
    /// changes; the port keeps the old columns in <see cref="OldColumnsData"/> and retains this as an
    /// inert sentinel of the canonical release lifecycle.
    /// </summary>
    public IntPtr OldColumnsRawData { get; set; }

    /// <summary>
    /// Gets or sets the detached view over the previous-frame columns while the column count changes.
    /// </summary>
    /// <remarks>
    /// Canonical assigns this span from the table's old arena before the arena is detached; this port
    /// keeps the old column list itself, so the list must be replaced (not cleared in place) by
    /// <c>TableBeginInitMemory()</c> to preserve the previous-frame snapshot.
    /// </remarks>
    public List<ImGuiTableColumn> OldColumnsData { get; set; } = new();

    /// <summary>
    /// Gets or sets the <c>outer_size</c> argument passed to <c>BeginTable()</c>.
    /// </summary>
    public ImVec2 UserOuterSize { get; set; }

    /// <summary>
    /// Gets the per-table draw splitter that isolates draw commands per column and per frozen row.
    /// </summary>
    public ImDrawListSplitter DrawSplitter { get; } = new();

    /// <summary>
    /// Gets or sets the backed-up inner window work rectangle.
    /// </summary>
    public ImRect HostBackupWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the backed-up inner window parent work rectangle.
    /// </summary>
    public ImRect HostBackupParentWorkRect { get; set; }

    /// <summary>
    /// Gets or sets the backed-up previous-line size of the host window.
    /// </summary>
    public ImVec2 HostBackupPrevLineSize { get; set; }

    /// <summary>
    /// Gets or sets the backed-up current-line size of the host window.
    /// </summary>
    public ImVec2 HostBackupCurrLineSize { get; set; }

    /// <summary>
    /// Gets or sets the backed-up cursor maximum position of the host window.
    /// </summary>
    public ImVec2 HostBackupCursorMaxPos { get; set; }

    /// <summary>
    /// Gets or sets the backed-up columns offset of the outer window.
    /// </summary>
    public ImVec1 HostBackupColumnsOffset { get; set; }

    /// <summary>
    /// Gets or sets the backed-up item width of the outer window.
    /// </summary>
    public float HostBackupItemWidth { get; set; }

    /// <summary>
    /// Gets or sets the backed-up size of the outer window's item-width stack.
    /// </summary>
    public int HostBackupItemWidthStackSize { get; set; }
}

/// <summary>
/// Persistent state of one table, owning its columns, display order, per-row cell backgrounds, and
/// the public sort specifications.
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
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the table flags after patching by <c>TableFixFlags()</c>.
    /// </summary>
    public ImGuiTableFlags Flags { get; set; }

    /// <summary>
    /// Gets or sets the opaque handle to the canonical column arena; the port allocates the column,
    /// display-order, and row-cell buffers directly and keeps this at zero.
    /// </summary>
    public IntPtr RawData { get; set; }

    /// <summary>
    /// Gets or sets the transient temporary data for this table, which points into the context's
    /// temporary-data pool while the table is active.
    /// </summary>
    public ImGuiTableTempData? TempData { get; set; }

    /// <summary>
    /// Gets the columns, indexed by column index. Canonical is a span into <see cref="RawData"/>.
    /// </summary>
    public List<ImGuiTableColumn> Columns { get; set; } = new();

    /// <summary>
    /// Gets the display order of each column, indexed by display order; values initialize to
    /// 0 through count minus 1. Canonical is a span into <see cref="RawData"/>.
    /// </summary>
    public List<short> DisplayOrderToIndex { get; set; } = new();

    /// <summary>
    /// Gets the cell background requests for the current row. Canonical is a span into
    /// <see cref="RawData"/>.
    /// </summary>
    public List<ImGuiTableCellData> RowCellData { get; set; } = new();

    /// <summary>
    /// Gets or sets the map from display order to enabled state. Canonical is an
    /// <c>ImBitArrayPtr</c> allocated in <see cref="RawData"/>.
    /// </summary>
    public ImBitVector EnabledMaskByDisplayOrder { get; set; } = new();

    /// <summary>
    /// Gets or sets the map from column index to enabled state, suitable for iterating columns without
    /// touching cold data. Canonical is an <c>ImBitArrayPtr</c> allocated in <see cref="RawData"/>.
    /// </summary>
    public ImBitVector EnabledMaskByIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the map from column index to visible state, meaning enabled and not clipped by
    /// scrolling. Canonical is an <c>ImBitArrayPtr</c> allocated in <see cref="RawData"/>.
    /// </summary>
    public ImBitVector VisibleMaskByIndex { get; set; } = new();

    /// <summary>
    /// Gets or sets the subset of table flags whose data was loaded from the layout file, so that
    /// unaltered data is not saved back.
    /// </summary>
    public ImGuiTableFlags SettingsLoadedFlags { get; set; }

    /// <summary>
    /// Gets or sets the index of this table's entry in the context's table settings list, or -1 when
    /// the table is new.
    /// </summary>
    public int SettingsOffset { get; set; }

    /// <summary>
    /// Gets or sets the last frame on which the table was used, which is -1 until its first use.
    /// </summary>
    public int LastFrameActive { get; set; } = -1;

    /// <summary>
    /// Gets or sets the number of columns declared to <c>BeginTable()</c>.
    /// </summary>
    public int ColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the current row.
    /// </summary>
    public int CurrentRow { get; set; }

    /// <summary>
    /// Gets or sets the index of the current column, or -1 when no column is current.
    /// </summary>
    public int CurrentColumn { get; set; }

    /// <summary>
    /// Gets or sets the count of <c>BeginTable()</c> calls with the same identifier on the current
    /// frame, generally zero.
    /// </summary>
    public short InstanceCurrent { get; set; }

    /// <summary>
    /// Gets or sets the instance, generally zero, that is being interacted with.
    /// </summary>
    public short InstanceInteracted { get; set; }

    /// <summary>
    /// Gets or sets the top Y position of the current row.
    /// </summary>
    public float RowPosY1 { get; set; }

    /// <summary>
    /// Gets or sets the bottom Y position of the current row.
    /// </summary>
    public float RowPosY2 { get; set; }

    /// <summary>
    /// Gets or sets the minimum row height submitted to <c>TableNextRow()</c>.
    /// </summary>
    public float RowMinHeight { get; set; }

    /// <summary>
    /// Gets or sets the top and bottom cell padding, reloaded whenever the row changes.
    /// </summary>
    public float RowCellPaddingY { get; set; }

    /// <summary>
    /// Gets or sets the text baseline propagated across the row.
    /// </summary>
    public float RowTextBaseline { get; set; }

    /// <summary>
    /// Gets or sets the row indent offset locked when the row started.
    /// </summary>
    public float RowIndentOffsetX { get; set; }

    /// <summary>
    /// Gets or sets the flags of the current row.
    /// </summary>
    public ImGuiTableRowFlags RowFlags { get; set; }

    /// <summary>
    /// Gets or sets the flags of the previous row.
    /// </summary>
    public ImGuiTableRowFlags LastRowFlags { get; set; }

    /// <summary>
    /// Gets or sets the counter for alternating row background colors, which excludes header rows and
    /// may be fast-forwarded by a clipper.
    /// </summary>
    public int RowBgColorCounter { get; set; }

    /// <summary>
    /// Gets the two background color overrides for the current row, indexed by background layer.
    /// </summary>
    public uint[] RowBgColor { get; } = new uint[2];

    /// <summary>
    /// Gets or sets the strong border color derived from the table border color style.
    /// </summary>
    public uint BorderColorStrong { get; set; }

    /// <summary>
    /// Gets or sets the light border color derived from the table border color style.
    /// </summary>
    public uint BorderColorLight { get; set; }

    /// <summary>
    /// Gets or sets the left X coordinate used by table borders.
    /// </summary>
    public float BorderX1 { get; set; }

    /// <summary>
    /// Gets or sets the right X coordinate used by table borders.
    /// </summary>
    public float BorderX2 { get; set; }

    /// <summary>
    /// Gets or sets the host indent X position at the start of the table.
    /// </summary>
    public float HostIndentX { get; set; }

    /// <summary>
    /// Gets or sets the minimum column width used by the layout.
    /// </summary>
    public float MinColumnWidth { get; set; }

    /// <summary>
    /// Gets or sets the outer horizontal padding locked in <c>BeginTable()</c>.
    /// </summary>
    public float OuterPaddingX { get; set; }

    /// <summary>
    /// Gets or sets the horizontal padding applied inside each bordered cell.
    /// </summary>
    public float CellPaddingX { get; set; }

    /// <summary>
    /// Gets or sets the spacing inserted before a cell.
    /// </summary>
    public float CellSpacingX1 { get; set; }

    /// <summary>
    /// Gets or sets the spacing inserted after a cell.
    /// </summary>
    public float CellSpacingX2 { get; set; }

    /// <summary>
    /// Gets or sets the <c>inner_width</c> argument passed to <c>BeginTable()</c>.
    /// </summary>
    public float InnerWidth { get; set; }

    /// <summary>
    /// Gets or sets the sum of the current column widths.
    /// </summary>
    public float ColumnsGivenWidth { get; set; }

    /// <summary>
    /// Gets or sets the sum of the ideal column widths at which nothing is clipped, used for
    /// auto-fitting and for content width submission to the outer window.
    /// </summary>
    public float ColumnsAutoFitWidth { get; set; }

    /// <summary>
    /// Gets or sets the sum of the weights of all enabled stretched columns.
    /// </summary>
    public float ColumnsStretchSumWeights { get; set; }

    /// <summary>
    /// Gets or sets the width queued for the column being resized, applied by the next instance-zero
    /// layout.
    /// </summary>
    public float ResizedColumnNextWidth { get; set; }

    /// <summary>
    /// Gets or sets the minimum content X2 locked while resizing a column down, to avoid feedback
    /// loops while still allowing growth.
    /// </summary>
    public float ResizeLockMinContentsX2 { get; set; }

    /// <summary>
    /// Gets or sets the reference scale used to rescale columns when the font size or DPI changes.
    /// </summary>
    public float RefScale { get; set; }

    /// <summary>
    /// Gets or sets the height set by the angled-header row and read by the layout.
    /// </summary>
    public float AngledHeadersHeight { get; set; }

    /// <summary>
    /// Gets or sets the X-to-Y slope set by the angled-header row and read by the layout.
    /// </summary>
    public float AngledHeadersSlope { get; set; }

    /// <summary>
    /// Gets or sets the outer rectangle; for a non-scrolling table its maximum Y is often large until
    /// <c>EndTable()</c>.
    /// </summary>
    public ImRect OuterRect { get; set; }

    /// <summary>
    /// Gets or sets the inner rectangle without decoration.
    /// </summary>
    public ImRect InnerRect { get; set; }

    /// <summary>
    /// Gets or sets the work rectangle shared with the host window.
    /// </summary>
    public ImRect WorkRect { get; set; }

    /// <summary>
    /// Gets or sets the inner clipping rectangle.
    /// </summary>
    public ImRect InnerClipRect { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle used to clip cell background fills on the CPU; it evolves while
    /// the frame crosses frozen-row boundaries.
    /// </summary>
    public ImRect BgClipRect { get; set; }

    /// <summary>
    /// Gets or sets the draw-command clip rectangle for the background layer 0 and 1 channel.
    /// </summary>
    public ImRect Bg0ClipRectForDrawCmd { get; set; }

    /// <summary>
    /// Gets or sets the draw-command clip rectangle for the background layer 2 channel.
    /// </summary>
    public ImRect Bg2ClipRectForDrawCmd { get; set; }

    /// <summary>
    /// Gets or sets the host clip rectangle, used to decide whether column draw calls can be merged
    /// into the current window's draw call.
    /// </summary>
    public ImRect HostClipRect { get; set; }

    /// <summary>
    /// Gets or sets the backup of the inner window clip rectangle taken while a background channel is
    /// pushed.
    /// </summary>
    public ImRect HostBackupInnerClipRect { get; set; }

    /// <summary>
    /// Gets or sets the parent window that hosts the table.
    /// </summary>
    public ImGuiWindow? OuterWindow { get; set; }

    /// <summary>
    /// Gets or sets the window that actually holds the table data, which is either the outer window or
    /// a child window.
    /// </summary>
    public ImGuiWindow? InnerWindow { get; set; }

    /// <summary>
    /// Gets the contiguous buffer that holds the column names.
    /// </summary>
    public ImGuiTextBuffer ColumnsNames { get; } = new();

    /// <summary>
    /// Gets or sets the shortcut to the temporary data's draw splitter while the table is active.
    /// </summary>
    public ImDrawListSplitter? DrawSplitter { get; set; }

    /// <summary>
    /// Gets the data for table instance zero.
    /// </summary>
    public ImGuiTableInstanceData InstanceDataFirst { get; } = new();

    /// <summary>
    /// Gets the data for table instances above zero.
    /// </summary>
    public List<ImGuiTableInstanceData> InstanceDataExtra { get; } = new();

    /// <summary>
    /// Gets the storage for a single active sort specification.
    /// </summary>
    public ImGuiTableColumnSortSpecs SortSpecsSingle { get; } = new();

    /// <summary>
    /// Gets the storage for multiple active sort specifications.
    /// </summary>
    public List<ImGuiTableColumnSortSpecs> SortSpecsMulti { get; } = new();

    /// <summary>
    /// Gets the public sort specifications returned by <c>TableGetSortSpecs()</c>.
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
    /// </summary>
    public short SortSpecsCount { get; set; }

    /// <summary>
    /// Gets or sets the number of enabled columns.
    /// </summary>
    public short ColumnsEnabledCount { get; set; }

    /// <summary>
    /// Gets or sets the number of enabled columns that use a fixed width.
    /// </summary>
    public short ColumnsEnabledFixedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of <c>TableSetupColumn()</c> calls made for the table.
    /// </summary>
    public short DeclColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the number of columns that request an angled header.
    /// </summary>
    public short AngledHeadersCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the column whose visible region is hovered, which equals the column
    /// count while hovering the empty region after the last column.
    /// </summary>
    public short HoveredColumnBody { get; set; }

    /// <summary>
    /// Gets or sets the index of the column whose right border is hovered for resizing.
    /// </summary>
    public short HoveredColumnBorder { get; set; }

    /// <summary>
    /// Gets or sets the index of the column that should be highlighted.
    /// </summary>
    public short HighlightColumnHeader { get; set; }

    /// <summary>
    /// Gets or sets the index of the single column requesting an automatic width fit.
    /// </summary>
    public short AutoFitSingleColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column being resized, reset when the instance count is zero.
    /// </summary>
    public short ResizedColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column resized on the previous frame.
    /// </summary>
    public short LastResizedColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column header being held.
    /// </summary>
    public short HeldHeaderColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column header held on the previous frame.
    /// </summary>
    public short LastHeldHeaderColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the column being reordered; the value is not cleared while the
    /// reorder is held.
    /// </summary>
    public short ReorderColumn { get; set; }

    /// <summary>
    /// Gets or sets the requested display order of the column being reordered.
    /// </summary>
    public short ReorderColumnDstOrder { get; set; }

    /// <summary>
    /// Gets or sets the index of the left-most enabled column.
    /// </summary>
    public short LeftMostEnabledColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the right-most enabled column.
    /// </summary>
    public short RightMostEnabledColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the left-most stretched column.
    /// </summary>
    public short LeftMostStretchedColumn { get; set; }

    /// <summary>
    /// Gets or sets the index of the right-most stretched column.
    /// </summary>
    public short RightMostStretchedColumn { get; set; }

    /// <summary>
    /// Gets or sets the column that was right-clicked, or -1 when the context menu was opened from a
    /// neutral position.
    /// </summary>
    public short ContextPopupColumn { get; set; }

    /// <summary>
    /// Gets or sets the requested frozen-row count.
    /// </summary>
    public short FreezeRowsRequest { get; set; }

    /// <summary>
    /// Gets or sets the actual frozen-row count, which is zero when there is no scrolling offset.
    /// </summary>
    public short FreezeRowsCount { get; set; }

    /// <summary>
    /// Gets or sets the requested frozen-column count.
    /// </summary>
    public short FreezeColumnsRequest { get; set; }

    /// <summary>
    /// Gets or sets the actual frozen-column count, which is zero when there is no scrolling offset.
    /// </summary>
    public short FreezeColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the index of the current row-cell background entry.
    /// </summary>
    public short RowCellDataCurrent { get; set; }

    /// <summary>
    /// Gets or sets the channel to which non-visible columns are redirected.
    /// </summary>
    public ushort DummyDrawChannel { get; set; }

    /// <summary>
    /// Gets or sets the background layer 2 draw channel for the frozen section.
    /// </summary>
    public ushort Bg2DrawChannelCurrent { get; set; }

    /// <summary>
    /// Gets or sets the background layer 2 draw channel for the unfrozen section.
    /// </summary>
    public ushort Bg2DrawChannelUnfrozen { get; set; }

    /// <summary>
    /// Gets or sets the navigation layer captured at <c>BeginTable()</c>.
    /// </summary>
    public sbyte NavLayer { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that <c>TableUpdateLayout()</c> has locked the column geometry.
    /// </summary>
    public bool IsLayoutLocked { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the table is between <c>TableBeginRow()</c> and
    /// <c>TableEndRow()</c>.
    /// </summary>
    public bool IsInsideRow { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the table buffers were freshly initialized this frame.
    /// </summary>
    public bool IsInitializing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that a column topology reconcile is in progress.
    /// </summary>
    public bool IsReconcileMode { get; set; }

    /// <summary>
    /// Gets or sets whether the table is newly created on this frame.
    /// </summary>
    public bool IsNewTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the sort specifications must be rebuilt.
    /// </summary>
    public bool IsSortSpecsDirty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the first row carried the header flag.
    /// </summary>
    public bool IsUsingHeaders { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the default context menu is open.
    /// </summary>
    public bool IsContextPopupOpen { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the default context menu is suppressed so the application
    /// can submit its own through <c>TableBeginContextMenuPopup()</c>.
    /// </summary>
    public bool DisableDefaultContextMenu { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that layout settings are requested to be loaded.
    /// </summary>
    public bool IsSettingsRequestLoad { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that table settings changed and must be written back into the
    /// settings data.
    /// </summary>
    public bool IsSettingsDirty { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the display order is unchanged from the default order.
    /// </summary>
    public bool IsDefaultDisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that enabled and visible states are unchanged from their
    /// defaults.
    /// </summary>
    public bool IsDefaultVisibility { get; set; }

    /// <summary>
    /// Gets or sets a request to call <c>TableResetSettings()</c> from <c>BeginTable()</c>.
    /// </summary>
    public bool IsResetAllRequest { get; set; }

    /// <summary>
    /// Gets or sets a request to reset the column display order.
    /// </summary>
    public bool IsResetDisplayOrderRequest { get; set; }

    /// <summary>
    /// Gets or sets a request to reset the column visibility.
    /// </summary>
    public bool IsResetVisibilityRequest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the frame has passed the frozen-row boundary.
    /// </summary>
    public bool IsUnfrozenRows { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the application did not explicitly choose a sizing policy.
    /// </summary>
    public bool IsDefaultSizingPolicy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that an active identifier was alive before the table started.
    /// </summary>
    public bool IsActiveIdAliveBeforeTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that an active identifier was created inside the table.
    /// </summary>
    public bool IsActiveIdInTable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that any instance of the table had a vertical scrollbar on the
    /// current frame.
    /// </summary>
    public bool HasScrollbarYCurr { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that any instance of the table had a vertical scrollbar on the
    /// previous frame.
    /// </summary>
    public bool HasScrollbarYPrev { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the transient buffers have been compacted by garbage
    /// collection.
    /// </summary>
    public bool MemoryCompacted { get; set; }

    /// <summary>
    /// Gets or sets the backup of the inner window skip-items state taken at the end of
    /// <c>BeginTable()</c>, because the state is overwritten per column during the frame.
    /// </summary>
    public bool HostSkipItems { get; set; }
}

/// <summary>
/// One serialized per-column table settings entry, stored inside an
/// <see cref="ImGuiTableSettings"/> record.
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
    /// </summary>
    public float WidthOrWeight { get; set; }

    /// <summary>
    /// Gets or sets the column identifier used to match the entry against live columns.
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the saved column index, or -1 when unset.
    /// </summary>
    public short Index { get; set; } = -1;

    /// <summary>
    /// Gets or sets the saved display order, or -1 when unset.
    /// </summary>
    public short DisplayOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the saved sort order, or -1 when the column is not sorted.
    /// </summary>
    public short SortOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the saved sort direction.
    /// </summary>
    public ImGuiSortDirection SortDirection { get; set; }

    /// <summary>
    /// Gets or sets the saved visibility, written as the visible field of the layout file; -1 means
    /// unset and 0 or 1 are the explicit states.
    /// </summary>
    public sbyte IsEnabled { get; set; } = -1;

    /// <summary>
    /// Gets or sets a value indicating that the saved width applies to a stretched column.
    /// </summary>
    public bool IsStretch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the entry was matched to a live column while loading.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Copies every field of another column settings entry into this one.
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
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// Gets or sets the table flags that indicate which data categories this entry saves, using the
    /// resizable, reorderable, sortable, and hideable flags.
    /// </summary>
    public ImGuiTableFlags SaveFlags { get; set; }

    /// <summary>
    /// Gets or sets the reference scale used to rescale columns when the font size or DPI changes.
    /// </summary>
    public float RefScale { get; set; }

    /// <summary>
    /// Gets or sets the number of columns in the saved entry.
    /// </summary>
    public short ColumnsCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of columns this entry can store; an entry may be recycled for a
    /// table with fewer columns but not more.
    /// </summary>
    public short ColumnsCountMax { get; set; }

    /// <summary>
    /// Gets or sets the packed date on which the entry was last used.
    /// </summary>
    public ImGuiPackedDate LastUsedDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating that the entry was loaded from the layout file and must be
    /// merged into an already running context.
    /// </summary>
    public bool WantApply { get; set; }

    /// <summary>
    /// Gets the trailing per-column entries. Canonical returns a pointer immediately after the header
    /// through <c>GetColumnSettings()</c>.
    /// </summary>
    public List<ImGuiTableColumnSettings> ColumnSettings { get; } = new();
}

#endregion
