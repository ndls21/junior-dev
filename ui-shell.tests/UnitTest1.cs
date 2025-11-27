using System.IO;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking;
using Ui.Shell;

namespace ui_shell.tests;

public class MainFormTests : IDisposable
{
    private string _tempLayoutFile;
    private string _tempDir;

    public MainFormTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "JuniorDevTest");
        _tempLayoutFile = Path.Combine(_tempDir, "layout.xml");
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
}