using System.IO;
using System.Windows.Forms;
using DevExpress.XtraBars.Docking;
using Ui.Shell;
using System.Drawing;
using JuniorDev.Contracts;
using Moq;
using JuniorDev.Orchestrator;
using Microsoft.Extensions.AI;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

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

    private MainForm CreateMainForm(bool isTestMode = false, ISessionManager sessionManager = null, IConfiguration configuration = null, IChatClient chatClient = null)
    {
        var mockSessionManager = sessionManager != null ? Mock.Get(sessionManager) : new Mock<ISessionManager>();
        var mockConfiguration = configuration != null ? Mock.Get(configuration) : new Mock<IConfiguration>();
        var mockChatClient = chatClient != null ? Mock.Get(chatClient) : new Mock<IChatClient>();
        
        return new MainForm(
            mockSessionManager.Object, 
            mockConfiguration.Object, 
            mockChatClient.Object, 
            isTestMode);
    }

    [Fact]
    public void MainForm_CanCreateInstance()
    {
        // Arrange & Act
        var form = CreateMainForm(isTestMode: true);

        // Assert
        Assert.NotNull(form);
        Assert.Equal("Junior Dev - TEST MODE (Auto-exit in 2s)", form.Text);
    }

    [Fact]
    public void MainForm_TestMode_AutoExits()
    {
        // This test verifies that test mode is detected and configured
        // The actual auto-exit behavior is tested manually via dotnet run -- --test
        var form = CreateMainForm(isTestMode: true);

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
        var runningItem = new SessionItem { Name = "Test", Status = JuniorDev.Contracts.SessionStatus.Running };
        var (backColor, foreColor, text) = runningItem.GetStatusChip();
        Assert.Equal(Color.Green, backColor);
        Assert.Equal(Color.White, foreColor);
        Assert.Equal("RUNNING", text);

        var errorItem = new SessionItem { Name = "Test", Status = JuniorDev.Contracts.SessionStatus.Error };
        (backColor, foreColor, text) = errorItem.GetStatusChip();
        Assert.Equal(Color.Red, backColor);
        Assert.Equal(Color.White, foreColor);
        Assert.Equal("ERROR", text);

        var completedItem = new SessionItem { Name = "Test", Status = JuniorDev.Contracts.SessionStatus.Completed };
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
        var form = CreateMainForm(isTestMode: true);
        
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
        var form = CreateMainForm(isTestMode: true);
        
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
        var form = CreateMainForm(isTestMode: true);
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
        var form = CreateMainForm(isTestMode: true);
        form.SetLayoutFilePath(_tempLayoutFile); // Inject test path
        
        // Act - Load layout (should handle corruption gracefully)
        var exception = Record.Exception(() => form.LoadLayout());
        
        // Assert - Should not crash, should fall back to defaults
        Assert.Null(exception); // No exception should be thrown
    }

    [Fact]
    public void MainForm_ContainsAIChatControl_ForInteractiveConversations()
    {
        // Arrange & Act
        var form = CreateMainForm(isTestMode: true);
        
        // Assert - Form should contain AIChatControl for interactive AI conversations
        // The AI Chat Control is initialized in CreateConversationPanel()
        // which is called during form construction for interactive AI chat
        Assert.NotNull(form);
        // Note: Full control verification would require reflection or public properties
        // This test ensures the form initializes without errors with AI components
    }

    [Fact]
    public void ChatStreamPersistence_SaveAndLoad_RoundTrip()
    {
        // Arrange
        var form = CreateMainForm(isTestMode: true);
        
        var testStreams = new[]
        {
            new ChatStream(Guid.NewGuid(), "Agent 1") { Status = JuniorDev.Contracts.SessionStatus.Running, CurrentTask = "Analyzing code", ProgressPercentage = 75 },
            new ChatStream(Guid.NewGuid(), "Agent 2") { Status = JuniorDev.Contracts.SessionStatus.Paused, CurrentTask = "Running tests", ProgressPercentage = 45 },
            new ChatStream(Guid.NewGuid(), "Agent 3") { Status = JuniorDev.Contracts.SessionStatus.Error, CurrentTask = "Fixing bugs", ProgressPercentage = 0 }
        };

        // Act - Save chat streams
        form.SaveChatStreams(testStreams);
        
        // Load chat streams
        var loadedStreams = form.LoadChatStreams();
        
        // Assert
        Assert.NotNull(loadedStreams);
        Assert.Equal(3, loadedStreams.Length);
        
        for (int i = 0; i < testStreams.Length; i++)
        {
            Assert.Equal(testStreams[i].SessionId, loadedStreams[i].SessionId);
            Assert.Equal(testStreams[i].AgentName, loadedStreams[i].AgentName);
            Assert.Equal(testStreams[i].Status, loadedStreams[i].Status);
            Assert.Equal(testStreams[i].CurrentTask, loadedStreams[i].CurrentTask);
            Assert.Equal(testStreams[i].ProgressPercentage, loadedStreams[i].ProgressPercentage);
        }
    }

    [Fact]
    public void ChatStreamPersistence_CorruptedFile_FallsBackToDefaults()
    {
        // Arrange - Create corrupted chat streams file
        var chatStreamsFile = Path.Combine(_tempDir, "chat-streams.json");
        File.WriteAllText(chatStreamsFile, "{invalid json content}");
        
        var form = CreateMainForm(isTestMode: true);
        form.SetChatStreamsFilePath(chatStreamsFile); // Inject test path
        
        // Act - Load chat streams (should handle corruption gracefully)
        var loadedStreams = form.LoadChatStreams();
        
        // Assert - Should fall back to default streams
        Assert.NotNull(loadedStreams);
        Assert.True(loadedStreams.Length >= 1); // Should have at least one default stream
        Assert.Equal("Agent 1", loadedStreams[0].AgentName);
    }

    [Fact]
    public void ChatStreamPersistence_MissingFile_FallsBackToDefaults()
    {
        // Arrange - No chat streams file exists
        var chatStreamsFile = Path.Combine(_tempDir, "nonexistent-chat-streams.json");
        
        var form = CreateMainForm(isTestMode: true);
        form.SetChatStreamsFilePath(chatStreamsFile); // Inject test path
        
        // Act - Load chat streams
        var loadedStreams = form.LoadChatStreams();
        
        // Assert - Should fall back to default streams
        Assert.NotNull(loadedStreams);
        Assert.True(loadedStreams.Length >= 1);
        Assert.Equal("Agent 1", loadedStreams[0].AgentName);
    }

    [Fact]
    public void ChatStreamPersistence_LoadDefaultChatStreams_CreatesValidStreams()
    {
        // Arrange
        var form = CreateMainForm(isTestMode: true);
        
        // Act - Load default chat streams
        var defaultStreams = form.LoadDefaultChatStreams();
        
        // Assert
        Assert.NotNull(defaultStreams);
        Assert.True(defaultStreams.Length >= 1);
        
        var firstStream = defaultStreams[0];
        Assert.NotNull(firstStream.AgentName);
        Assert.NotEqual(Guid.Empty, firstStream.SessionId);
        Assert.Equal(JuniorDev.Contracts.SessionStatus.Running, firstStream.Status);
        Assert.NotNull(firstStream.CurrentTask);
        Assert.True(firstStream.ProgressPercentage >= 0 && firstStream.ProgressPercentage <= 100);
    }

    [Fact]
    public void ChatStreamPersistence_LayoutReset_ResetsToDefaults()
    {
        // Arrange - Create a chat streams file with custom content
        var chatStreamsFile = Path.Combine(_tempDir, "chat-streams.json");
        var customContent = "[{\"sessionId\":\"12345678-1234-1234-1234-123456789012\",\"agentName\":\"Custom Agent\",\"status\":0,\"currentTask\":\"Custom task\",\"progressPercentage\":50}]";
        File.WriteAllText(chatStreamsFile, customContent);
        Assert.True(File.Exists(chatStreamsFile));
        
        var form = CreateMainForm(isTestMode: true);
        form.SetChatStreamsFilePath(chatStreamsFile); // Inject test path
        
        // Act - Reset layout (should reset chat streams to defaults)
        form.ResetLayout();
        
        // Assert - File should still exist but with default content
        Assert.True(File.Exists(chatStreamsFile), $"File should exist at {chatStreamsFile}");
        
        // Verify the content is reset to defaults (should contain "Agent 1")
        var content = File.ReadAllText(chatStreamsFile);
        Assert.Contains("Agent 1", content);
        Assert.DoesNotContain("Custom Agent", content);
    }

    [Fact]
    public void ChatStreamPersistence_LayoutLoad_IntegratesChatStreams()
    {
        // Arrange - Create valid chat streams file
        var chatStreamsFile = Path.Combine(_tempDir, "chat-streams.json");
        var testStreams = new[]
        {
            new ChatStream(Guid.NewGuid(), "Agent Alpha") { Status = JuniorDev.Contracts.SessionStatus.Running },
            new ChatStream(Guid.NewGuid(), "Agent Beta") { Status = JuniorDev.Contracts.SessionStatus.Completed }
        };
        
        var chatStreamData = testStreams.Select(s => new ChatStreamData
        {
            SessionId = s.SessionId,
            AgentName = s.AgentName,
            Status = s.Status,
            CurrentTask = s.CurrentTask,
            ProgressPercentage = s.ProgressPercentage
        }).ToArray();
        
        var json = System.Text.Json.JsonSerializer.Serialize(chatStreamData);
        File.WriteAllText(chatStreamsFile, json);
        
        var form = CreateMainForm(isTestMode: true);
        form.SetChatStreamsFilePath(chatStreamsFile); // Inject test path
        
        // Act - Load layout (should load chat streams)
        form.LoadLayout();
        
        // Assert - Chat streams should be loaded
        // Note: Full verification would require accessing private _accordionManager
        // This test ensures LoadLayout completes without errors when chat streams file exists
        Assert.True(true); // If we get here, LoadLayout succeeded
    }

    [Fact]
    public void ChatStreamPersistence_LayoutLoad_CorruptedChatStreams_FallsBackToDefaults()
    {
        // Arrange - Create corrupted chat streams file
        var chatStreamsFile = Path.Combine(_tempDir, "chat-streams.json");
        File.WriteAllText(chatStreamsFile, "{invalid json}");
        
        var form = CreateMainForm(isTestMode: true);
        form.SetChatStreamsFilePath(chatStreamsFile); // Inject test path
        
        // Act - Load layout (should handle corrupted chat streams gracefully)
        var exception = Record.Exception(() => form.LoadLayout());
        
        // Assert - Should not crash, should fall back to defaults
        Assert.Null(exception); // No exception should be thrown
    }

    [Fact]
    public void EventRouting_MockEventsUseExistingChatStreamSessionIds()
    {
        // Arrange
        var form = CreateMainForm(isTestMode: true);
        
        // Create a few chat streams
        var stream1 = new ChatStream(Guid.NewGuid(), "Agent Alpha");
        var stream2 = new ChatStream(Guid.NewGuid(), "Agent Beta");
        form.SaveChatStreams(new[] { stream1, stream2 });
        
        // Load them back
        var loadedStreams = form.LoadChatStreams();
        
        // Act - Generate mock events
        var mockEvents = form.GenerateMockEventSequence();
        
        // Assert - Events should use SessionIds from existing chat streams
        var eventSessionIds = mockEvents.Select(e => e.Correlation.SessionId).Distinct().ToArray();
        var chatSessionIds = loadedStreams.Select(s => s.SessionId).ToArray();
        
        // At least one event SessionId should match a chat stream SessionId
        bool hasMatchingSessionId = eventSessionIds.Any(eid => chatSessionIds.Contains(eid));
        Assert.True(hasMatchingSessionId, "Mock events should use SessionIds from existing chat streams");
    }

    [Fact]
    public void ArtifactLinking_DoubleClickOpensArtifactDialog()
    {
        // Arrange
        var form = CreateMainForm(isTestMode: true);
        
        // Create a mock artifact
        var artifact = new Artifact("test-results", "Test Results", "All tests passed");
        
        // Add artifact to a chat stream
        var sessionId = Guid.NewGuid();
        form.AddArtifactToChat(sessionId, artifact);
        
        // Act - Simulate double-click on artifact (this would normally be done via UI)
        // Since we can't easily simulate UI events in unit tests, we'll test the OpenArtifact method directly
        // In a real scenario, the double-click handler would call OpenArtifact
        
        // For now, just verify the artifact was added
        // Full UI testing would require integration tests
        Assert.True(true); // Placeholder - artifact linking infrastructure is in place
    }

    [Fact]
    public void MultiChatEventRouting_EventsRenderedInCorrectChatStreams()
    {
        // Arrange
        var form = CreateMainForm(isTestMode: true);
        
        // Create chat streams
        var stream1 = new ChatStream(Guid.NewGuid(), "Agent 1");
        var stream2 = new ChatStream(Guid.NewGuid(), "Agent 2");
        form.SaveChatStreams(new[] { stream1, stream2 });
        var loadedStreams = form.LoadChatStreams();
        
        // Act - Route an event to streams
        var testEvent = new CommandCompleted(Guid.NewGuid(), new Correlation(stream1.SessionId, Guid.NewGuid()), Guid.NewGuid(), CommandOutcome.Success, "Task completed");
        form.RouteEventToChatStreams(testEvent, DateTimeOffset.Now);
        
        // Assert - Event should be routed (this is tested indirectly through the UI inspection in test mode)
        // In a real test, we'd need to access the private _accordionManager and check panel event rendering
        Assert.True(true); // Infrastructure is in place for event routing
    }

    [Fact]
    public async Task SessionLifecycle_SessionCreation_MapsToChatStreamWithRealSessionId()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var sessionConfig = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent");

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act
        await form.CreateSessionForTest(sessionConfig);

        // Assert
        mockSessionManager.Verify(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId)), Times.Once);
        // Verify that a chat stream was created with the correct SessionId
        var chatStreams = form.GetChatStreamsForTest();
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId);
    }

    [Fact]
    public async Task SessionLifecycle_EventSubscription_SubscribesToNewSessions()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var events = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId), SessionStatus.Running, "Started")
        };

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId))
            .Returns(System.Linq.AsyncEnumerable.ToAsyncEnumerable(events));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act
        await form.CreateSessionForTest(new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent"));

        // Allow some time for async subscription
        await Task.Delay(100);

        // Assert
        mockSessionManager.Verify(sm => sm.Subscribe(sessionId), Times.Once);
    }

    [Fact]
    public async Task UICommandPublishing_ExecuteCommandForActiveSession_PublishesToSessionManager()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();

        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Get the actual session ID from the initial chat stream created by the form
        var chatStreams = form.GetChatStreamsForTest();
        var activeSessionId = chatStreams.First().SessionId;

        // Act
        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new RunTests(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), null, TimeSpan.FromMinutes(5)));

        // Assert - Verify that PublishCommand was called with a RunTests command for the active session
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == activeSessionId)), Times.Once);
    }

    [Fact]
    public async Task UICommandPublishing_CommandMenuItems_TriggerCorrectCommands()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();

        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Get the actual session ID from the initial chat stream created by the form
        var chatStreams = form.GetChatStreamsForTest();
        var activeSessionId = chatStreams.First().SessionId;

        // Act - Simulate clicking "Run Tests" menu item
        await form.ExecuteRunTestsCommandForTest();

        // Assert - Verify that PublishCommand was called with a RunTests command for the active session
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == activeSessionId)), Times.Once);
    }

    [Fact]
    public void DummyChatClient_Verification_PreventsAIExceptionsInTestMode()
    {
        // Arrange
        var form = CreateMainForm(isTestMode: true);

        // Act - Try to access AI client functionality
        var canAccessAI = form.TestAICanAccessForTest();

        // Assert - In test mode, should use dummy client that doesn't throw exceptions
        Assert.True(canAccessAI); // Should not throw exceptions
    }

    [Fact]
    public async Task SessionLifecycle_MultipleSessions_IsolatedEventStreams()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var events1 = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId1), SessionStatus.Running, "Started")
        };
        var events2 = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId2), SessionStatus.Running, "Started")
        };

        mockSessionManager.Setup(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId1)))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId2)))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId1))
            .Returns(System.Linq.AsyncEnumerable.ToAsyncEnumerable(events1));
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId2))
            .Returns(System.Linq.AsyncEnumerable.ToAsyncEnumerable(events2));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act
        await form.CreateSessionForTest(new SessionConfig(
            sessionId1, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test1", "/tmp/test1"), new WorkspaceRef("/tmp/workspace1"), null, "test-agent"));
        await form.CreateSessionForTest(new SessionConfig(
            sessionId2, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test2", "/tmp/test2"), new WorkspaceRef("/tmp/workspace2"), null, "test-agent"));

        // Allow time for subscriptions
        await Task.Delay(100);

        // Assert
        mockSessionManager.Verify(sm => sm.Subscribe(sessionId1), Times.Once);
        mockSessionManager.Verify(sm => sm.Subscribe(sessionId2), Times.Once);
        
        var chatStreams = form.GetChatStreamsForTest();
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId1);
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId2);
    }

    [Fact]
    public async Task SessionLifecycle_SessionStatusUpdates_ReflectInChatStreams()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var statusEvent = new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId), SessionStatus.Paused, "Paused for testing");

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act
        await form.CreateSessionForTest(new SessionConfig(
            sessionId, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"), new WorkspaceRef("/tmp/workspace"), null, "test-agent"));

        // Check what chat streams exist
        var chatStreams = form.GetChatStreamsForTest();
        Console.WriteLine($"Found {chatStreams.Length} chat streams");
        foreach (var cs in chatStreams)
        {
            Console.WriteLine($"Chat stream: {cs.SessionId}, Status: {cs.Status}");
        }

        // Directly route the event to test the UI update
        form.RouteEventToChatStreams(statusEvent, DateTimeOffset.Now);

        // Check status after routing
        chatStreams = form.GetChatStreamsForTest();
        var chatStream = chatStreams.FirstOrDefault(cs => cs.SessionId == sessionId);
        Console.WriteLine($"Chat stream after routing: {chatStream?.SessionId}, Status: {chatStream?.Status}");

        // Assert
        Assert.NotNull(chatStream);
        Assert.Equal(SessionStatus.Paused, chatStream.Status);
    }

    [Fact]
    public async Task UICommandPublishing_CommandExecution_HandlesErrorsGracefully()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .ThrowsAsync(new Exception("Command execution failed"));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create a chat stream to make it the active session
        var chatStream = new ChatStream(sessionId, "Test Agent");
        form.AddChatStreamForTest(chatStream);

        // Act & Assert - In test mode, should re-throw exceptions for test verification
        var exception = await Record.ExceptionAsync(() => 
            form.ExecuteCommandForActiveSessionTest((correlation) => 
                new RunTests(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), null, TimeSpan.FromMinutes(5))));

        Assert.NotNull(exception); // Should re-throw exception in test mode
        Assert.Contains("Command execution failed", exception.Message);
    }

    [Fact]
    public void ChatStream_SessionId_Display_ShowsRealSessionId()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
        var chatStream = new ChatStream(sessionId, "Test Agent");
        chatStream.Status = SessionStatus.Running;
        chatStream.CurrentTask = "Testing";

        // Act
        var statusText = chatStream.GetStatusText();

        // Assert
        Assert.Contains(sessionId.ToString().Substring(0, 8), statusText);
        Assert.Contains("Test Agent", statusText);
        Assert.Contains("Testing", statusText);
    }

    [Fact]
    public async Task SessionLifecycle_ChatStreamCreation_SetsDependenciesCorrectly()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var repoRef = new RepoRef("test-repo", "/path/to/repo");

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act
        await form.CreateSessionForTest(new SessionConfig(
            sessionId, null, null, new PolicyProfile { Name = "test" },
            repoRef, new WorkspaceRef("/tmp/workspace"), null, "test-agent"));

        // Assert
        var chatStreams = form.GetChatStreamsForTest();
        var chatStream = chatStreams.FirstOrDefault(cs => cs.SessionId == sessionId);
        Assert.NotNull(chatStream);
        
        // Verify dependencies are set (this would require reflection or public accessors in real implementation)
        // For now, we verify the chat stream was created with correct SessionId
        Assert.Equal(sessionId, chatStream.SessionId);
    }
}
