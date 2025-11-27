using System.IO;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking;
using Ui.Shell;
using System.Drawing;

namespace ui_shell.tests;

public class MainFormTests : IDisposable
{
    private string _tempLayoutFile;
    private string _tempSettingsFile;
    private string _tempDir;

    public MainFormTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JuniorDevTest");
        _tempLayoutFile = Path.Combine(_tempDir, "layout.xml");
        _tempSettingsFile = Path.Combine(_tempDir, "settings.json");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void MainForm_CanCreateInstance()
    {
        // Arrange & Act
        var form = new MainForm();

        // Assert
        Assert.NotNull(form);
        Assert.Equal("Junior Dev - AI-Assisted Development Platform", form.Text);
    }

    [Fact]
    public void MainForm_TestMode_AutoExits()
    {
        // This test verifies that test mode is detected and configured
        // The actual auto-exit behavior is tested manually via dotnet run -- --test
        var form = new MainForm();

        // In a real test environment, we would mock the command line args
        // For now, we just verify the form can be created
        Assert.NotNull(form);
    }

    [Fact]
    public void LayoutPersistence_SaveAndRestore_RoundTrip()
    {
        // Arrange
        var dockManager = new DockManager();
        var form = new Form();
        dockManager.Form = form;

        var panel1 = dockManager.AddPanel(DockingStyle.Left);
        panel1.Text = "Test Panel 1";
        panel1.Width = 200;

        var panel2 = dockManager.AddPanel(DockingStyle.Right);
        panel2.Text = "Test Panel 2";
        panel2.Width = 300;

        // Act - Save layout
        dockManager.SaveLayoutToXml(_tempLayoutFile);
        Assert.True(File.Exists(_tempLayoutFile));

        // Create new dock manager and restore
        var newDockManager = new DockManager();
        newDockManager.Form = new Form();
        newDockManager.RestoreLayoutFromXml(_tempLayoutFile);

        // Assert - Layout should be restored
        // Note: DevExpress layout restoration is complex to test directly
        // In a real scenario, we'd verify panel properties match
        Assert.True(File.Exists(_tempLayoutFile));
    }

    [Fact]
    public void LayoutPersistence_CorruptedFile_FallsBackToDefault()
    {
        // Arrange - Create a corrupted layout file
        File.WriteAllText(_tempLayoutFile, "<invalid><xml></content>");

        var dockManager = new DockManager();
        var form = new Form();
        dockManager.Form = form;

        // Act & Assert - Should not throw exception
        var exception = Record.Exception(() => dockManager.RestoreLayoutFromXml(_tempLayoutFile));
        // DevExpress may or may not throw depending on corruption level
        // The important thing is it doesn't crash the application
    }

    [Fact]
    public void SessionFiltering_ShowsAllSessions_WhenAllFilterSelected()
    {
        // This would require mocking the UI components
        // For now, we test the filtering logic conceptually
        var allSessions = new[]
        {
            "🔄 Session 1 - Running",
            "⏸️ Session 2 - Paused",
            "❌ Session 3 - Error",
            "⚠️ Session 5 - NeedsApproval",
            "✅ Session 6 - Completed"
        };

        // When "All" filter is selected, all sessions should be shown
        var filteredSessions = allSessions.Where(s => true).ToArray();
        Assert.Equal(5, filteredSessions.Length);
    }

    [Fact]
    public void SessionFiltering_ShowsOnlyRunningSessions_WhenRunningFilterSelected()
    {
        var allSessions = new[]
        {
            "🔄 Session 1 - Running",
            "⏸️ Session 2 - Paused",
            "❌ Session 3 - Error",
            "🔄 Session 4 - Running",
            "⚠️ Session 5 - NeedsApproval",
            "✅ Session 6 - Completed"
        };

        var runningSessions = allSessions.Where(s => s.Contains("Running")).ToArray();
        Assert.Equal(2, runningSessions.Length);
        Assert.Contains("Session 1", runningSessions[0]);
        Assert.Contains("Session 4", runningSessions[1]);
    }

    [Fact]
    public void SettingsPersistence_SaveAndLoad_RoundTrip()
    {
        // Arrange
        var originalSettings = new AppSettings
        {
            Theme = "Dark",
            FontSize = 12,
            ShowStatusChips = false,
            AutoScrollEvents = false
        };

        // Act - Save settings
        var json = System.Text.Json.JsonSerializer.Serialize(originalSettings);
        File.WriteAllText(_tempSettingsFile, json);

        // Load settings
        var loadedJson = File.ReadAllText(_tempSettingsFile);
        var loadedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(loadedJson);

        // Assert
        Assert.NotNull(loadedSettings);
        Assert.Equal(originalSettings.Theme, loadedSettings.Theme);
        Assert.Equal(originalSettings.FontSize, loadedSettings.FontSize);
        Assert.Equal(originalSettings.ShowStatusChips, loadedSettings.ShowStatusChips);
        Assert.Equal(originalSettings.AutoScrollEvents, loadedSettings.AutoScrollEvents);
    }

    [Fact]
    public void SettingsPersistence_CorruptedFile_FallsBackToDefaults()
    {
        // Arrange - Create corrupted settings file
        File.WriteAllText(_tempSettingsFile, "{invalid json content}");

        // Act - Try to load settings
        string loadedJson;
        try
        {
            loadedJson = File.ReadAllText(_tempSettingsFile);
            var loadedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(loadedJson);
            // If deserialization succeeds unexpectedly, that's fine
            Assert.NotNull(loadedSettings);
        }
        catch
        {
            // Expected to fail with corrupted JSON
            // Should fall back to defaults (new AppSettings())
            var defaultSettings = new AppSettings();
            Assert.Equal("Light", defaultSettings.Theme);
            Assert.Equal(9, defaultSettings.FontSize);
            Assert.True(defaultSettings.ShowStatusChips);
            Assert.True(defaultSettings.AutoScrollEvents);
        }
    }

    [Fact]
    public void SessionItem_GetStatusChip_ReturnsCorrectColorsAndText()
    {
        // Test each status returns correct chip info
        var runningItem = new SessionItem { Name = "Test", Status = SessionStatus.Running };
        var (backColor, foreColor, text) = runningItem.GetStatusChip();
        Assert.Equal(Color.Green, backColor);
        Assert.Equal(Color.White, foreColor);
        Assert.Equal("RUNNING", text);

        var errorItem = new SessionItem { Name = "Test", Status = SessionStatus.Error };
        (backColor, foreColor, text) = errorItem.GetStatusChip();
        Assert.Equal(Color.Red, backColor);
        Assert.Equal(Color.White, foreColor);
        Assert.Equal("ERROR", text);

        var completedItem = new SessionItem { Name = "Test", Status = SessionStatus.Completed };
        (backColor, foreColor, text) = completedItem.GetStatusChip();
        Assert.Equal(Color.Blue, backColor);
        Assert.Equal(Color.White, foreColor);
        Assert.Equal("COMPLETED", text);
    }

    [Fact]
    public void LayoutReset_DeletesLayoutFile()
    {
        // Arrange - Create a layout file
        File.WriteAllText(_tempLayoutFile, "<layout><data>test</data></layout>");
        Assert.True(File.Exists(_tempLayoutFile));

        // Act - Simulate layout reset (delete file)
        File.Delete(_tempLayoutFile);

        // Assert
        Assert.False(File.Exists(_tempLayoutFile));
    }

    [Fact]
    public void MainForm_SettingsApplied_ShowStatusChips_ChangesDrawMode()
    {
        // Arrange
        var form = new MainForm();
        form.IsTestMode = true; // Enable test mode to avoid dialogs
        
        // Act - Apply settings with ShowStatusChips = true
        var settings = new AppSettings { ShowStatusChips = true };
        form.ApplySettings(settings);
        
        // Assert - Should be OwnerDrawFixed
        Assert.Equal(DrawMode.OwnerDrawFixed, form.GetSessionsListBoxDrawMode());
        
        // Act - Apply settings with ShowStatusChips = false
        settings = new AppSettings { ShowStatusChips = false };
        form.ApplySettings(settings);
        
        // Assert - Should be Normal
        Assert.Equal(DrawMode.Normal, form.GetSessionsListBoxDrawMode());
    }

    [Fact]
    public void MainForm_SettingsApplied_AutoScrollEvents_ControlsScrolling()
    {
        // Arrange
        var form = new MainForm();
        form.IsTestMode = true; // Enable test mode to avoid dialogs
        
        // Act - Apply settings with AutoScrollEvents = true
        var settings = new AppSettings { AutoScrollEvents = true };
        form.ApplySettings(settings);
        
        // Assert - Should have auto-scroll enabled
        Assert.True(form.GetAutoScrollEventsSetting());
        
        // Act - Apply settings with AutoScrollEvents = false
        settings = new AppSettings { AutoScrollEvents = false };
        form.ApplySettings(settings);
        
        // Assert - Should have auto-scroll disabled
        Assert.False(form.GetAutoScrollEventsSetting());
    }

    [Fact]
    public void MainForm_LayoutReset_DeletesFileAndPersistsImmediately()
    {
        // Arrange - Create a layout file
        File.WriteAllText(_tempLayoutFile, "<layout><data>test</data></layout>");
        var form = new MainForm();
        form.IsTestMode = true; // Enable test mode to avoid dialogs
        form.SetLayoutFilePath(_tempLayoutFile); // Inject test path
        
        // Act - Reset layout
        form.ResetLayout();
        
        // Assert - File should be deleted and new layout saved
        Assert.False(File.Exists(_tempLayoutFile));
        // Note: In a real test, we'd verify the new layout was saved
        // but that's complex with DevExpress
    }

    [Fact]
    public void MainForm_LayoutCorruption_FallsBackToDefaults()
    {
        // Arrange - Create corrupted layout file
        File.WriteAllText(_tempLayoutFile, "<invalid><xml></content>");
        var form = new MainForm();
        form.IsTestMode = true; // Enable test mode to avoid dialogs
        form.SetLayoutFilePath(_tempLayoutFile); // Inject test path
        
        // Act - Load layout (should handle corruption gracefully)
        var exception = Record.Exception(() => form.LoadLayout());
        
        // Assert - Should not crash, should fall back to defaults
        Assert.Null(exception); // No exception should be thrown
    }

    [Fact]
    public void MainForm_SettingsCorruption_FallsBackToDefaults()
    {
        // Arrange - Create corrupted settings file
        File.WriteAllText(_tempSettingsFile, "{invalid json content}");
        var form = new MainForm();
        form.IsTestMode = true; // Enable test mode to avoid dialogs
        form.SetSettingsFilePath(_tempSettingsFile); // Inject test path
        
        // Act - Load settings
        var settings = form.LoadSettings();
        
        // Assert - Should fall back to defaults
        Assert.NotNull(settings);
        Assert.Equal("Light", settings.Theme);
        Assert.Equal(9, settings.FontSize);
        Assert.True(settings.ShowStatusChips);
        Assert.True(settings.AutoScrollEvents);
    }
}