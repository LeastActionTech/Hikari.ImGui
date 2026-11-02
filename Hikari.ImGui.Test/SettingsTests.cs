#nullable enable
using System;
using System.IO;
using NUnit.Framework;

namespace Hikari.ImGui.Test;

/// <summary>
/// Verifies the `.ini` settings pipeline with concrete state assertions: handler registration and lookup,
/// canonical window/table/dock record formatting, memory and disk round trips, the age-based cleanup and
/// discard semantics, and the per-frame load/save driver.
/// </summary>
/// <remarks>
/// <para>
/// Canonical reference: Dear ImGui <c>v1.92.9b-docking</c>. The settings section is
/// <c>ThirdParty/imgui/imgui.cpp:16368-16798</c>; handler registration is <c>imgui.cpp:4550-4562</c> and
/// the docking read half is <c>imgui.cpp:21728-21900</c>; the table handler is
/// <c>ThirdParty/imgui/imgui_tables.cpp:3924-4262</c>.
/// </para>
/// <para>
/// Every test drives a real <see cref="ImGuiContext"/> whose <c>IniFilename</c> is nulled so the run never
/// reads or writes a developer's settings file; the one disk test supplies its own temporary path.
/// </para>
/// </remarks>
[TestFixture]
public class SettingsTests : ImGuiTestFixture
{

    #region Fixture setup and helpers

    /// <summary>
    /// Applies the fixture-specific context options; the shared context lifecycle is owned by
    /// <see cref="ImGuiTestFixture"/>.
    /// </summary>
    protected override void ConfigureContext()
    {
        ImGuiIO io = _ctx.IO;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
    }


    #endregion

    #region Handler registry

    /// <summary>
    /// Verifies that the Window, Table, and Docking handlers are registered in canonical order with
    /// hashed type names, and that lookup and removal address them by name.
    /// (<c>ThirdParty/imgui/imgui.cpp:4550-4562</c>, <c>16430-16453</c>)
    /// </summary>
    [Test]
    public void SettingsHandlers_RegisterInCanonicalOrderAndResolveByName()
    {
        Assert.That(_ctx.SettingsHandlers.Count, Is.EqualTo(3));
        Assert.That(_ctx.SettingsHandlers[0].TypeName, Is.EqualTo("Window"));
        Assert.That(_ctx.SettingsHandlers[1].TypeName, Is.EqualTo("Table"));
        Assert.That(_ctx.SettingsHandlers[2].TypeName, Is.EqualTo("Docking"));
        Assert.That(_ctx.SettingsHandlers[0].TypeHash, Is.EqualTo(ImGui.ImHashStr("Window")));
        Assert.That(_ctx.SettingsHandlers[2].TypeHash, Is.EqualTo(ImGui.ImHashStr("Docking")));

        ImGuiSettingsHandler? windowHandler = ImGui.FindSettingsHandler("Window");
        Assert.That(windowHandler, Is.Not.Null);
        Assert.That(windowHandler!.WriteAllFn, Is.Not.Null);

        ImGui.RemoveSettingsHandler("Table");
        Assert.That(ImGui.FindSettingsHandler("Table"), Is.Null);
        Assert.That(_ctx.SettingsHandlers.Count, Is.EqualTo(2));
    }

    #endregion

    #region Window settings

    /// <summary>
    /// Verifies that writing the settings produces the canonical `[Window][name]` record and that loading
    /// it back recreates an entry with the live window's saved size.
    /// (<c>ThirdParty/imgui/imgui.cpp:16725-16796</c>, <c>16691-16711</c>)
    /// </summary>
    [Test]
    public void WindowSettings_RoundTripThroughMemory_PreservesRecord()
    {
        NewTestFrame();
        ImGui.SetNextWindowPos(new ImVec2(120.0f, 80.0f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new ImVec2(320.0f, 240.0f), ImGuiCond.Always);
        ImGui.Begin("SettingsRoundTrip");
        ImGui.End();
        ImGui.EndFrame();

        ImGuiWindow window = ImGui.FindWindowByName("SettingsRoundTrip")!;
        short expectedWidth = (short)window.SizeFull.X;
        short expectedHeight = (short)window.SizeFull.Y;

        string ini = ImGui.SaveIniSettingsToMemory();
        Assert.That(ini, Does.Contain("[Window][SettingsRoundTrip]"));
        Assert.That(ini, Does.Contain("Collapsed=0"));
        Assert.That(ini, Does.Contain("Size="));
        Assert.That(ini, Does.Contain("LastUsed="));

        ImGui.ClearIniSettings();
        Assert.That(_ctx.SettingsWindows, Is.Empty);

        ImGui.LoadIniSettingsFromMemory(ini);
        ImGuiWindowSettings? loaded = ImGui.FindWindowSettingsByID(ImGui.ImHashStr("SettingsRoundTrip"));
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Name, Is.EqualTo("SettingsRoundTrip"));
        Assert.That(loaded.Size.X, Is.EqualTo(expectedWidth));
        Assert.That(loaded.Size.Y, Is.EqualTo(expectedHeight));
    }

    /// <summary>
    /// Verifies that a window name's non-contributing `###` prefix is dropped unless
    /// <c>io.ConfigDebugIniSettings</c> asks to preserve the full label.
    /// (<c>ThirdParty/imgui/imgui.cpp:16603-16622</c>)
    /// </summary>
    [Test]
    public void CreateNewWindowSettings_StripsNonContributingPrefix()
    {
        _ctx.IO.ConfigDebugIniSettings = false;
        ImGuiWindowSettings stripped = ImGui.CreateNewWindowSettings("Visible###StableId");
        Assert.That(stripped.Name, Is.EqualTo("StableId"));

        _ctx.IO.ConfigDebugIniSettings = true;
        ImGuiWindowSettings preserved = ImGui.CreateNewWindowSettings("Visible###StableId");
        Assert.That(preserved.Name, Is.EqualTo("Visible###StableId"));
    }

    /// <summary>
    /// Verifies that clearing a window's settings invalidates its stored entry and marks the window as
    /// unsaved so it is not immediately rewritten.
    /// (<c>ThirdParty/imgui/imgui.cpp:16643-16657</c>)
    /// </summary>
    [Test]
    public void ClearWindowSettings_InvalidatesStoredEntry()
    {
        NewTestFrame();
        ImGui.Begin("ClearMe");
        ImGui.End();
        ImGui.EndFrame();
        ImGui.SaveIniSettingsToMemory();

        ImGuiWindowSettings? before = ImGui.FindWindowSettingsByID(ImGui.ImHashStr("ClearMe"));
        Assert.That(before, Is.Not.Null);

        ImGui.ClearWindowSettings("ClearMe");
        ImGuiWindowSettings? after = ImGui.FindWindowSettingsByID(ImGui.ImHashStr("ClearMe"));
        Assert.That(after, Is.Null, "A cleared entry is invalidated and no longer found.");
    }

    #endregion

    #region Table settings

    /// <summary>
    /// Verifies the canonical table record header and column line, and that the lines parse back into the
    /// same widths, visibility, order, and sort direction.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:4207-4248</c>, <c>4179-4205</c>)
    /// </summary>
    [Test]
    public void TableSettings_RoundTripThroughMemory_PreservesColumns()
    {
        ImGuiTableSettings settings = new()
        {
            ID = 0x0BADF00Du,
            ColumnsCount = 2,
            ColumnsCountMax = 2,
            SaveFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.Hideable | ImGuiTableFlags.Reorderable | ImGuiTableFlags.Sortable,
            RefScale = 1.5f,
            LastUsedDate = new ImGuiPackedDate(20260615u),
        };
        settings.ColumnSettings.Add(new ImGuiTableColumnSettings
        {
            ID = 0x11111111u,
            WidthOrWeight = 100.0f,
            Index = 0,
            DisplayOrder = 0,
            SortOrder = 0,
            SortDirection = ImGuiSortDirection.Ascending,
            IsEnabled = 1,
            IsStretch = false,
        });
        settings.ColumnSettings.Add(new ImGuiTableColumnSettings
        {
            ID = 0x22222222u,
            WidthOrWeight = 0.75f,
            Index = 1,
            DisplayOrder = 1,
            SortOrder = -1,
            IsEnabled = 0,
            IsStretch = true,
        });
        _ctx.SettingsTables.Add(settings);

        string ini = ImGui.SaveIniSettingsToMemory();
        Assert.That(ini, Does.Contain("[Table][0x0BADF00D,2]"));
        Assert.That(ini, Does.Contain("RefScale=1.5"));
        Assert.That(ini, Does.Contain("Column 0 "));
        Assert.That(ini, Does.Contain("Width=100"));
        Assert.That(ini, Does.Contain("Visible=1"));
        Assert.That(ini, Does.Contain("Order=0"));
        Assert.That(ini, Does.Contain("Sort=0v"));
        Assert.That(ini, Does.Contain("ID=0x11111111"));
        Assert.That(ini, Does.Contain("Weight=0.7500"));
        Assert.That(ini, Does.Contain("Visible=0"));

        ImGui.ClearIniSettings();
        Assert.That(_ctx.SettingsTables, Is.Empty);

        ImGui.LoadIniSettingsFromMemory(ini);
        ImGuiTableSettings? loaded = ImGui.TableSettingsFindByID(0x0BADF00Du);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.ColumnsCount, Is.EqualTo(2));
        Assert.That(loaded.RefScale, Is.EqualTo(1.5f).Within(0.0001f));
        Assert.That(loaded.ColumnSettings[0].ID, Is.EqualTo(0x11111111u));
        Assert.That(loaded.ColumnSettings[0].WidthOrWeight, Is.EqualTo(100.0f).Within(0.0001f));
        Assert.That(loaded.ColumnSettings[0].IsStretch, Is.False);
        Assert.That(loaded.ColumnSettings[0].SortDirection, Is.EqualTo(ImGuiSortDirection.Ascending));
        Assert.That(loaded.ColumnSettings[1].ID, Is.EqualTo(0x22222222u));
        Assert.That(loaded.ColumnSettings[1].IsStretch, Is.True);
        Assert.That(loaded.ColumnSettings[1].IsEnabled, Is.EqualTo(0));
    }

    /// <summary>
    /// Verifies that a malformed `[Table]` header is rejected so the parser ignores the entry's lines,
    /// matching canonical's zero-tolerance read-open.
    /// (<c>ThirdParty/imgui/imgui_tables.cpp:4167-4177</c>)
    /// </summary>
    [Test]
    public void TableSettings_ReadOpen_RejectsMalformedHeader()
    {
        ImGui.LoadIniSettingsFromMemory("[Table][not-a-number]\nColumn 0 Width=100\n");

        Assert.That(_ctx.SettingsTables, Is.Empty);
    }

    #endregion

    #region Docking settings

    /// <summary>
    /// Verifies that the docking section writes a depth-indented `[Docking][Data]` block and that loading
    /// it rebuilds the same node hierarchy.
    /// (<c>ThirdParty/imgui/imgui.cpp:21829-21900</c>, <c>18431-18460</c>)
    /// </summary>
    [Test]
    public void DockingSettings_RoundTripThroughMemory_RebuildsHierarchy()
    {
        const uint rootId = 0x700u;
        ImGui.DockBuilderAddNode(rootId, ImGuiDockNodeFlags.None);
        ImGui.DockBuilderSetNodePos(rootId, new ImVec2(100.0f, 50.0f));
        ImGui.DockBuilderSetNodeSize(rootId, new ImVec2(600.0f, 400.0f));
        ImGui.DockBuilderSplitNode(rootId, ImGuiDir.Left, 0.3f, out uint childLeft, out uint childRight);
        Assert.That(childLeft, Is.Not.EqualTo(0u));
        Assert.That(childRight, Is.Not.EqualTo(0u));
        ImGui.DockBuilderFinish(rootId);

        // A window-settings entry is what keeps the node hierarchy alive through the canonical prune pass.
        _ctx.SettingsWindows.Add(new ImGuiWindowSettings
        {
            ID = ImGui.ImHashStr("DockedWindow"),
            Name = "DockedWindow",
            DockId = rootId,
        });

        string ini = ImGui.SaveIniSettingsToMemory();
        Assert.That(ini, Does.Contain("[Docking][Data]"));
        Assert.That(ini, Does.Contain("DockNode"));
        Assert.That(ini, Does.Contain("ID=0x00000700"));
        Assert.That(ini, Does.Contain("Parent=0x00000700"));

        ImGui.ClearIniSettings();
        Assert.That(_ctx.DockContext.Nodes.Count, Is.EqualTo(0));

        ImGui.LoadIniSettingsFromMemory(ini);
        Assert.That(_ctx.DockContext.NodesSettings.Count, Is.EqualTo(3));
        Assert.That(_ctx.DockContext.Nodes.Count, Is.EqualTo(3));
        Assert.That(ImGui.DockBuilderGetNode(rootId), Is.Not.Null);
    }

    #endregion

    #region Cleanup and discard

    /// <summary>
    /// Verifies that <see cref="ImGui.CleanupIniSettings"/> derives the same cutoff from the platform
    /// session date that the metrics window displays, and that entries older than it are invalidated.
    /// (<c>ThirdParty/imgui/imgui.cpp:16464-16482</c>, <c>16665-16677</c>)
    /// </summary>
    [Test]
    public void CleanupIniSettings_OlderThanMonths_InvalidatesOldEntriesOnly()
    {
        _ctx.PlatformIO.Platform_SessionDate = 20260615;
        _ctx.IO.ConfigIniSettingsSaveLastUsedDate = true;

        ImGuiWindowSettings oldEntry = ImGui.CreateNewWindowSettings("Old");
        oldEntry.LastUsedDate = new ImGuiPackedDate(20200101u);
        ImGuiWindowSettings recentEntry = ImGui.CreateNewWindowSettings("Recent");
        recentEntry.LastUsedDate = new ImGuiPackedDate(20260601u);

        ImGuiSettingsCleanupArgs args = new() { DiscardOlderThanMonths = 6 };
        ImGui.CleanupIniSettings(args);

        Assert.That(args._DiscardOlderThanDate, Is.EqualTo(20251215));
        Assert.That(oldEntry.WantDelete, Is.True);
        Assert.That(recentEntry.WantDelete, Is.False);

        string ini = ImGui.SaveIniSettingsToMemory();
        Assert.That(ini, Does.Not.Contain("[Window][Old]"));
        Assert.That(ini, Does.Contain("[Window][Recent]"));
    }

    /// <summary>
    /// Verifies that an entry without a last-used date is discarded and can be stamped with the session
    /// date instead, depending on the requested mode.
    /// (<c>ThirdParty/imgui/imgui.cpp:16665-16677</c>)
    /// </summary>
    [Test]
    public void CleanupIniSettings_MissingDate_DiscardsOrStamps()
    {
        _ctx.SessionDate = new ImGuiPackedDate(20260615u);

        ImGuiWindowSettings stamped = ImGui.CreateNewWindowSettings("StampMe");
        ImGui.CleanupIniSettings(new ImGuiSettingsCleanupArgs { SetCurrentSessionDateWhenMissingDate = true });
        Assert.That(stamped.WantDelete, Is.False);
        Assert.That(stamped.LastUsedDate.Unpack(), Is.EqualTo(20260615));

        ImGuiWindowSettings discarded = ImGui.CreateNewWindowSettings("DiscardMe");
        discarded.LastUsedDate = default;
        ImGui.CleanupIniSettings(new ImGuiSettingsCleanupArgs { DiscardWhenMissingDate = true });
        Assert.That(discarded.WantDelete, Is.True);
    }

    #endregion

    #region Frame driver and disk round trip

    /// <summary>
    /// Verifies that the per-frame driver loads once, reports to the host when no filename is configured,
    /// and writes to disk once the dirty delay elapses when a filename is configured.
    /// (<c>ThirdParty/imgui/imgui.cpp:16388-16408</c>, <c>16573-16586</c>)
    /// </summary>
    [Test]
    public void UpdateSettings_FlushesToDiskAfterDirtyDelay()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hikari_settings_{Guid.NewGuid():N}.ini");
        try
        {
            _ctx.IO.IniFilename = path;
            _ctx.IO.IniSavingRate = 0.05f;

            NewTestFrame();
            ImGui.SetNextWindowPos(new ImVec2(64.0f, 32.0f), ImGuiCond.Always);
            ImGui.Begin("DiskRoundTrip");
            ImGui.End();
            ImGui.EndFrame();

            ImGui.MarkIniSettingsDirty();
            // Drive enough frames for the delayed save to fire.
            for (int frame = 0; frame < 8 && !File.Exists(path); frame++)
            {
                NewTestFrame();
                ImGui.EndFrame();
            }

            Assert.That(File.Exists(path), Is.True, "The delayed save must create the configured file.");
            string diskText = File.ReadAllText(path);
            Assert.That(diskText, Does.Contain("[Window][DiskRoundTrip]"));

            string memoryText = ImGui.SaveIniSettingsToMemory();
            Assert.That(memoryText, Is.EqualTo(diskText));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that with no configured filename the driver asks the host to save to memory instead of
    /// touching the file system.
    /// (<c>ThirdParty/imgui/imgui.cpp:16401-16409</c>)
    /// </summary>
    [Test]
    public void UpdateSettings_WithoutFilename_RequestsHostSave()
    {
        _ctx.IO.IniFilename = null;
        _ctx.IO.WantSaveIniSettings = false;

        NewTestFrame();
        ImGui.MarkIniSettingsDirty();
        _ctx.SettingsDirtyTimer = 0.001f;
        ImGui.EndFrame();

        NewTestFrame();
        ImGui.EndFrame();

        Assert.That(_ctx.IO.WantSaveIniSettings, Is.True);
    }

    #endregion
}
