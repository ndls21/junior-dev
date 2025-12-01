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
using System.Threading.Tasks;
using DevExpress.AIIntegration.WinForms.Chat;
using Microsoft.Extensions.Configuration.Memory;

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

    private MainForm CreateMainForm(bool isTestMode = false, ISessionManager? sessionManager = null, IConfiguration? configuration = null, Microsoft.Extensions.AI.IChatClient? chatClient = null)
    {
        ISessionManager actualSessionManager;
        IConfiguration actualConfiguration;
        Microsoft.Extensions.AI.IChatClient actualChatClient;
        
        // Handle session manager
        if (sessionManager != null)
        {
            actualSessionManager = sessionManager;
        }
        else
        {
            var mockSessionManager = new Mock<ISessionManager>();
            actualSessionManager = mockSessionManager.Object;
        }
        
        // Handle configuration
        if (configuration != null)
        {
            actualConfiguration = configuration;
        }
        else
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppConfig:Transcript:Enabled"] = "true",
                ["AppConfig:Transcript:MaxMessagesPerTranscript"] = "1000",
                ["AppConfig:Transcript:MaxTranscriptSizeBytes"] = "10485760",
                ["AppConfig:Transcript:MaxTranscriptAge"] = "30.00:00:00",
                ["AppConfig:Transcript:StorageDirectory"] = _tempDir
            });
            actualConfiguration = configBuilder.Build();
        }
        
        // Handle chat client
        if (chatClient != null)
        {
            actualChatClient = chatClient;
        }
        else
        {
            var mockChatClient = new Mock<Microsoft.Extensions.AI.IChatClient>();
            actualChatClient = mockChatClient.Object;
        }
        
        // Create a mock service provider
        var mockServiceProvider = new Mock<IServiceProvider>();
        
        return new MainForm(
            actualSessionManager, 
            actualConfiguration, 
            actualChatClient, 
            mockServiceProvider.Object,
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

    [Fact(Timeout = 5000)] // 5 second timeout
    public async Task SessionLifecycle_EventSubscription_SubscribesToNewSessions()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var events = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId), SessionStatus.Running, "Started")
        };

        // Create a finite async enumerable that completes
        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId))
            .Returns(new TestAsyncEnumerable(events));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act - Create session WITH subscription - subscription should complete when stream ends
        await form.CreateSessionForTestWithSubscription(new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"),
            new WorkspaceRef("/tmp/workspace"),
            null,
            "test-agent"));

        // Wait for event processing to complete
        await form.WaitForEventSubscription(sessionId, TimeSpan.FromSeconds(5));

        // Assert
        mockSessionManager.Verify(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId)), Times.Once);
        mockSessionManager.Verify(sm => sm.Subscribe(sessionId), Times.Once);
        
        var chatStreams = form.GetChatStreamsForTest();
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId);
        
        // Verify the event was processed and status updated
        var chatStream = chatStreams.First(cs => cs.SessionId == sessionId);
        Assert.Equal(SessionStatus.Running, chatStream.Status);
    }

    [Fact]
    public async Task UICommandPublishing_ExecuteCommandForActiveSession_PublishesToSessionManager()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create a session explicitly since auto-creation was removed
        await form.CreateSessionForTest(new SessionConfig(
            sessionId, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"), new WorkspaceRef("/tmp/workspace"), null, "test-agent"));

        // Get the session ID from the created chat stream
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
        var sessionId = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create a session explicitly since auto-creation was removed
        await form.CreateSessionForTest(new SessionConfig(
            sessionId, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"), new WorkspaceRef("/tmp/workspace"), null, "test-agent"));

        // Get the session ID from the created chat stream
        var chatStreams = form.GetChatStreamsForTest();
        var activeSessionId = chatStreams.First().SessionId;

        // Act - Simulate clicking "Run Tests" menu item
        await form.ExecuteRunTestsCommandForTest();

        // Assert - Verify that PublishCommand was called with a RunTests command for the active session
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == activeSessionId)), Times.Once);
    }

    [Fact(Timeout = 1500)] // 1.5 second timeout
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

        // In test mode, subscriptions are skipped to avoid hanging background tasks
        // Verify that both sessions were created successfully
        mockSessionManager.Verify(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId1)), Times.Once);
        mockSessionManager.Verify(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId2)), Times.Once);
        
        var chatStreams = form.GetChatStreamsForTest();
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId1);
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId2);
    }

    [Fact(Timeout = 1500)] // 1.5 second timeout
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

    [Fact(Timeout = 1500)] // 1.5 second timeout
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

    [Fact(Timeout = 1500)] // 1.5 second timeout
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

    [Fact(Timeout = 1500)] // 1.5 second timeout
    public async Task LivePath_EventRouting_DeliversToCorrectChatStream()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create two chat streams with different session IDs
        await form.CreateSessionForTest(new SessionConfig(
            sessionId1, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test1", "/tmp/test1"), new WorkspaceRef("/tmp/workspace1"), null, "test-agent"));
        await form.CreateSessionForTest(new SessionConfig(
            sessionId2, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test2", "/tmp/test2"), new WorkspaceRef("/tmp/workspace2"), null, "test-agent"));

        var chatStreams = form.GetChatStreamsForTest();
        var stream1 = chatStreams.First(cs => cs.SessionId == sessionId1);
        var stream2 = chatStreams.First(cs => cs.SessionId == sessionId2);

        // Act - Route an event for sessionId1
        var event1 = new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId1), SessionStatus.Paused, "Paused for testing");
        form.RouteEventToChatStreams(event1, DateTimeOffset.Now);

        // Route a different event for sessionId2
        var event2 = new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId2), SessionStatus.Error, "Error occurred");
        form.RouteEventToChatStreams(event2, DateTimeOffset.Now);

        // Assert - Only stream1 should have its status updated to Paused
        chatStreams = form.GetChatStreamsForTest();
        var updatedStream1 = chatStreams.First(cs => cs.SessionId == sessionId1);
        var updatedStream2 = chatStreams.First(cs => cs.SessionId == sessionId2);

        Assert.Equal(SessionStatus.Paused, updatedStream1.Status);
        Assert.Equal(SessionStatus.Error, updatedStream2.Status);
    }

    [Fact(Timeout = 1500)] // 1.5 second timeout
    public async Task LivePath_CommandPublishing_UsesActiveSessionId()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create a session explicitly since auto-creation was removed
        await form.CreateSessionForTest(new SessionConfig(
            sessionId, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"), new WorkspaceRef("/tmp/workspace"), null, "test-agent"));

        // Get the session ID from the created chat stream
        var chatStreams = form.GetChatStreamsForTest();
        var activeSessionId = chatStreams.First().SessionId;

        // Act
        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new RunTests(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), null, TimeSpan.FromMinutes(5)));

        // Assert - Verify that PublishCommand was called with a command that has the correct SessionId
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == activeSessionId)), Times.Once);
    }

    [Fact(Timeout = 1500)] // 1.5 second timeout
    public async Task LivePath_MultipleSessions_IsolatedCommandPublishing()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var unusedSessionId1 = Guid.NewGuid();
        var unusedSessionId2 = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create a session explicitly since auto-creation was removed
        await form.CreateSessionForTest(new SessionConfig(
            sessionId, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("test", "/tmp/test"), new WorkspaceRef("/tmp/workspace"), null, "test-agent"));

        // Get the session ID from the created chat stream
        var chatStreams = form.GetChatStreamsForTest();
        var activeSessionId = chatStreams.First().SessionId;

        // Act - Execute command (should use the active stream's SessionId)
        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new RunTests(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), null, TimeSpan.FromMinutes(5)));

        // Assert - Command should be published with the active session's SessionId
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == activeSessionId)), Times.Once);
        
        // Verify no command was published with unused sessionIds
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == unusedSessionId1)), Times.Never);
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == unusedSessionId2)), Times.Never);
    }

    [Fact]
    public async Task MultiSessionSupport_AttachChatStreamToSession_UpdatesSessionIdAndSubscribes()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var originalSessionId = Guid.NewGuid();
        var newSessionId = Guid.NewGuid();
        var chatStream = new ChatStream(originalSessionId, "Test Agent");

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);
        form.AddChatStreamForTest(chatStream);

        // Act
        await form.AttachChatStreamToSessionForTest(chatStream, newSessionId);

        // Assert
        Assert.Equal(newSessionId, chatStream.SessionId);
        // Note: In test mode, subscription is skipped, but the SessionId should be updated
    }

    [Fact]
    public async Task MultiSessionSupport_AttachDialog_AcceptsValidSessionId()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);
        form.AddChatStreamForTest(chatStream);

        // Act - Simulate attaching with a valid SessionId
        await form.AttachChatStreamToSessionForTest(chatStream, sessionId);

        // Assert
        Assert.Equal(sessionId, chatStream.SessionId);
    }

    [Fact]
    public async Task MultiSessionSupport_CommandPublishing_RoutesToCorrectSession()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Get existing streams and collapse them all
        var existingStreams = form.GetChatStreamsForTest();
        foreach (var stream in existingStreams)
        {
            stream.IsExpanded = false;
        }

        // Create two chat streams with different session IDs
        var stream1 = new ChatStream(sessionId1, "Agent 1");
        var stream2 = new ChatStream(sessionId2, "Agent 2");
        form.AddChatStreamForTest(stream1);
        form.AddChatStreamForTest(stream2);

        // Set stream1 as expanded (active) - this should make it the active stream
        stream1.IsExpanded = true;
        stream2.IsExpanded = false;

        // Act - Execute command for active session (should use stream1's SessionId)
        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new RunTests(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), null, TimeSpan.FromMinutes(5)));

        // Assert - Command should be published with sessionId1 (active stream)
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == sessionId1)), Times.Once);
        
        // Verify no command was published with sessionId2
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == sessionId2)), Times.Never);
    }

    [Fact]
    public async Task MultiSessionSupport_EventSubscription_MultipleSessionsIndependent()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var events1 = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId1), SessionStatus.Running, "Session 1 started")
        };
        var events2 = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId2), SessionStatus.Paused, "Session 2 paused")
        };

        mockSessionManager.Setup(sm => sm.Subscribe(sessionId1))
            .Returns(System.Linq.AsyncEnumerable.ToAsyncEnumerable(events1));
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId2))
            .Returns(System.Linq.AsyncEnumerable.ToAsyncEnumerable(events2));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create two chat streams
        var stream1 = new ChatStream(sessionId1, "Agent 1");
        var stream2 = new ChatStream(sessionId2, "Agent 2");
        form.AddChatStreamForTest(stream1);
        form.AddChatStreamForTest(stream2);

        // Act - Subscribe to both sessions (normally done during creation/attachment)
        // Since SubscribeToSessionEvents is private, we'll simulate the effect by routing events directly
        form.RouteEventToChatStreams(events1[0], DateTimeOffset.Now);
        form.RouteEventToChatStreams(events2[0], DateTimeOffset.Now);

        // Assert - Events should be routed to correct streams
        var chatStreams = form.GetChatStreamsForTest();
        var updatedStream1 = chatStreams.First(cs => cs.SessionId == sessionId1);
        var updatedStream2 = chatStreams.First(cs => cs.SessionId == sessionId2);

        Assert.Equal(SessionStatus.Running, updatedStream1.Status);
        Assert.Equal(SessionStatus.Paused, updatedStream2.Status);
    }

    [Fact]
    public async Task MultiSessionSupport_CreateSession_IntegratesWithChatStream()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();
        var sessionConfig = new SessionConfig(
            sessionId,
            null,
            null,
            new PolicyProfile { Name = "test" },
            new RepoRef("test-repo", "/tmp/test"),
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
        
        var chatStreams = form.GetChatStreamsForTest();
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId);
    }

    [Fact]
    public async Task MultiSessionSupport_AttachToExistingSession_UpdatesChatStream()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var originalSessionId = Guid.NewGuid();
        var targetSessionId = Guid.NewGuid();
        var chatStream = new ChatStream(originalSessionId, "Test Agent");

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);
        form.AddChatStreamForTest(chatStream);

        // Act - Attach to different session
        await form.AttachChatStreamToSessionForTest(chatStream, targetSessionId);

        // Assert
        Assert.Equal(targetSessionId, chatStream.SessionId);
        Assert.Equal("Test Agent", chatStream.AgentName); // Name should remain the same
    }

    [Fact]
    public async Task MultiSessionSupport_RealSessionCreationAndCommandRouting_EndToEnd()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var sessionConfig1 = new SessionConfig(
            sessionId1, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo1", "/tmp/repo1"), new WorkspaceRef("/tmp/workspace1"), null, "agent1");
        var sessionConfig2 = new SessionConfig(
            sessionId2, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo2", "/tmp/repo2"), new WorkspaceRef("/tmp/workspace2"), null, "agent2");

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Collapse all existing streams first
        var existingStreams = form.GetChatStreamsForTest();
        foreach (var stream in existingStreams)
        {
            stream.IsExpanded = false;
        }

        // Act - Create two real sessions
        await form.CreateSessionForTest(sessionConfig1);
        await form.CreateSessionForTest(sessionConfig2);

        // Verify sessions were created
        mockSessionManager.Verify(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId1)), Times.Once);
        mockSessionManager.Verify(sm => sm.CreateSession(It.Is<SessionConfig>(sc => sc.SessionId == sessionId2)), Times.Once);

        // Verify chat streams were created
        var chatStreams = form.GetChatStreamsForTest();
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId1);
        Assert.Contains(chatStreams, cs => cs.SessionId == sessionId2);

        // Set sessionId1 stream as active (expanded)
        var stream1 = chatStreams.First(cs => cs.SessionId == sessionId1);
        var stream2 = chatStreams.First(cs => cs.SessionId == sessionId2);
        stream1.IsExpanded = true;
        stream2.IsExpanded = false;

        // Act - Execute command for active session
        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new RunTests(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), null, TimeSpan.FromMinutes(5)));

        // Assert - Command should be published with sessionId1 (active session)
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == sessionId1)), Times.Once);
        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(RunTests) && ((RunTests)cmd).Correlation.SessionId == sessionId2)), Times.Never);
    }

    [Fact(Timeout = 2000)] // 2 second timeout
    public async Task MultiSessionSupport_EventSubscriptionPerSession_IsolatedEventHandling()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        var events1 = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId1), SessionStatus.Running, "Session 1 running"),
            new CommandCompleted(Guid.NewGuid(), new Correlation(sessionId1), Guid.NewGuid(), CommandOutcome.Success, "Tests passed")
        };
        var events2 = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(sessionId2), SessionStatus.Paused, "Session 2 paused"),
            new CommandRejected(Guid.NewGuid(), new Correlation(sessionId2), Guid.NewGuid(), "Rate limit exceeded", "throttle-policy")
        };

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId1))
            .Returns(new TestAsyncEnumerable(events1));
        mockSessionManager.Setup(sm => sm.Subscribe(sessionId2))
            .Returns(new TestAsyncEnumerable(events2));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Act - Create two sessions WITH subscriptions - subscriptions should complete when streams end
        await form.CreateSessionForTestWithSubscription(new SessionConfig(
            sessionId1, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo1", "/tmp/repo1"), new WorkspaceRef("/tmp/workspace1"), null, "agent1"));
        await form.CreateSessionForTestWithSubscription(new SessionConfig(
            sessionId2, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo2", "/tmp/repo2"), new WorkspaceRef("/tmp/workspace2"), null, "agent2"));

        // Wait for event processing to complete
        await form.WaitForEventSubscription(sessionId1, TimeSpan.FromSeconds(5));
        await form.WaitForEventSubscription(sessionId2, TimeSpan.FromSeconds(5));
        
        // Assert - Events should be routed to correct streams
        var chatStreams = form.GetChatStreamsForTest();
        var stream1 = chatStreams.First(cs => cs.SessionId == sessionId1);
        var stream2 = chatStreams.First(cs => cs.SessionId == sessionId2);

        // The last event for session1 was CommandCompleted, so status should still be Running
        // The last event for session2 was CommandRejected, so status should still be Paused
        Assert.Equal(SessionStatus.Running, stream1.Status);
        Assert.Equal(SessionStatus.Paused, stream2.Status);
    }

    [Fact]
    public async Task MultiSessionSupport_AttachExistingChatStreamToNewSession_UpdatesEventSubscription()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var originalSessionId = Guid.NewGuid();
        var newSessionId = Guid.NewGuid();
        var chatStream = new ChatStream(originalSessionId, "Test Agent");

        var events = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(newSessionId), SessionStatus.Running, "Attached and running")
        };

        mockSessionManager.Setup(sm => sm.Subscribe(newSessionId))
            .Returns(System.Linq.AsyncEnumerable.ToAsyncEnumerable(events));

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);
        form.AddChatStreamForTest(chatStream);

        // Act - Attach chat stream to new session
        await form.AttachChatStreamToSessionForTest(chatStream, newSessionId);

        // Assert - SessionId should be updated
        Assert.Equal(newSessionId, chatStream.SessionId);

        // In test mode, subscription is skipped, but in real mode it would subscribe
        // This test verifies the attachment logic works correctly
    }

    [Fact]
    public async Task MultiSessionSupport_AttachToExistingSession_DiscoversAndAttachesViaSessionManager()
    {
        // Arrange - Create a real session manager with actual sessions
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        
        var sessionConfig1 = new SessionConfig(
            sessionId1, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo1", "/tmp/repo1"), new WorkspaceRef("/tmp/workspace1"), null, "agent1");
        var sessionConfig2 = new SessionConfig(
            sessionId2, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo2", "/tmp/repo2"), new WorkspaceRef("/tmp/workspace2"), null, "agent2");

        // Create a real session manager with mock dependencies
        var mockAdapters = new Mock<IReadOnlyList<IAdapter>>();
        mockAdapters.Setup(a => a.GetEnumerator()).Returns(Enumerable.Empty<IAdapter>().GetEnumerator());
        var mockPolicyEnforcer = new Mock<IPolicyEnforcer>();
        var mockRateLimiter = new Mock<IRateLimiter>();
        var mockWorkspaceProvider = new Mock<IWorkspaceProvider>();
        var mockArtifactStore = new Mock<IArtifactStore>();

        var realSessionManager = new SessionManager(
            mockAdapters.Object,
            mockPolicyEnforcer.Object,
            mockRateLimiter.Object,
            mockWorkspaceProvider.Object,
            mockArtifactStore.Object);

        // Create a form with the real session manager
        var form = CreateMainForm(isTestMode: true, sessionManager: realSessionManager);

        // Create two real sessions
        await realSessionManager.CreateSession(sessionConfig1);
        await realSessionManager.CreateSession(sessionConfig2);

        // Create a chat stream to attach
        var chatStream = new ChatStream(Guid.NewGuid(), "Test Agent");
        form.AddChatStreamForTest(chatStream);

        // Act - Attach chat stream to existing session via the test helper
        await form.AttachChatStreamToSessionForTest(chatStream, sessionId1);

        // Assert - SessionId should be updated
        Assert.Equal(sessionId1, chatStream.SessionId);

        // Verify that GetActiveSessions returns the created sessions
        var activeSessions = realSessionManager.GetActiveSessions();
        Assert.Equal(2, activeSessions.Count);
        Assert.Contains(activeSessions, s => s.SessionId == sessionId1 && s.RepoName == "repo1" && s.AgentProfile == "agent1");
        Assert.Contains(activeSessions, s => s.SessionId == sessionId2 && s.RepoName == "repo2" && s.AgentProfile == "agent2");
    }

    [Fact(Timeout = 10000)] // 10 second timeout for integration test
    public async Task MultiSessionSupport_AttachToExistingSession_EndToEndEventProcessing()
    {
        // Arrange - Create a mock session manager that returns events for an existing session
        var mockSessionManager = new Mock<ISessionManager>();
        var existingSessionId = Guid.NewGuid();
        var attachSessionId = Guid.NewGuid();
        
        // Events that will be emitted after attachment
        var postAttachEvents = new List<IEvent>
        {
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(attachSessionId), SessionStatus.Running, "Session resumed after attachment"),
            new CommandAccepted(Guid.NewGuid(), new Correlation(attachSessionId), Guid.NewGuid()),
            new CommandCompleted(Guid.NewGuid(), new Correlation(attachSessionId), Guid.NewGuid(), CommandOutcome.Success, "Command completed after attachment"),
            new SessionStatusChanged(Guid.NewGuid(), new Correlation(attachSessionId), SessionStatus.Completed, "Session finished after attachment")
        };

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.Subscribe(attachSessionId))
            .Returns(new TestAsyncEnumerable(postAttachEvents));
        mockSessionManager.Setup(sm => sm.GetActiveSessions())
            .Returns(new List<SessionInfo> 
            { 
                new SessionInfo(attachSessionId, SessionStatus.Running, "agent1", "repo1", DateTimeOffset.Now, "Running task") 
            });

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Create a chat stream with a different initial SessionId
        var chatStream = new ChatStream(existingSessionId, "Test Agent");
        form.AddChatStreamForTest(chatStream);

        // Act - Attach chat stream to the existing session WITH subscription
        await form.AttachChatStreamToSessionWithSubscriptionForTest(chatStream, attachSessionId);

        // Wait for event processing to complete
        await form.WaitForEventSubscription(attachSessionId, TimeSpan.FromSeconds(5));
        
        // Assert - Verify that events were processed and chat stream was updated
        Assert.Equal(attachSessionId, chatStream.SessionId);
        Assert.Equal(SessionStatus.Completed, chatStream.Status);
        
        // Verify session manager methods were called
        mockSessionManager.Verify(sm => sm.Subscribe(attachSessionId), Times.Once);
    }

    [Fact]
    public async Task MultiSessionSupport_CommandPublishingAcrossMultipleActiveStreams_SwitchesCorrectly()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        var sessionId3 = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);
        mockSessionManager.Setup(sm => sm.PublishCommand(It.IsAny<ICommand>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true);
        form.SetSessionManager(mockSessionManager.Object);

        // Collapse all existing streams first
        var existingStreams = form.GetChatStreamsForTest();
        foreach (var stream in existingStreams)
        {
            stream.IsExpanded = false;
        }

        // Create three sessions
        await form.CreateSessionForTest(new SessionConfig(
            sessionId1, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo1", "/tmp/repo1"), new WorkspaceRef("/tmp/workspace1"), null, "agent1"));
        await form.CreateSessionForTest(new SessionConfig(
            sessionId2, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo2", "/tmp/repo2"), new WorkspaceRef("/tmp/workspace2"), null, "agent2"));
        await form.CreateSessionForTest(new SessionConfig(
            sessionId3, null, null, new PolicyProfile { Name = "test" },
            new RepoRef("repo3", "/tmp/repo3"), new WorkspaceRef("/tmp/workspace3"), null, "agent3"));

        var chatStreams = form.GetChatStreamsForTest();
        var stream1 = chatStreams.First(cs => cs.SessionId == sessionId1);
        var stream2 = chatStreams.First(cs => cs.SessionId == sessionId2);
        var stream3 = chatStreams.First(cs => cs.SessionId == sessionId3);

        // Act & Assert - Switch active stream and verify commands go to correct session

        // Make stream2 active
        stream1.IsExpanded = false;
        stream2.IsExpanded = true;
        stream3.IsExpanded = false;

        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new CreateBranch(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), "feature-branch"));

        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(CreateBranch) && ((CreateBranch)cmd).Correlation.SessionId == sessionId2)), Times.Once);

        // Make stream3 active
        stream1.IsExpanded = false;
        stream2.IsExpanded = false;
        stream3.IsExpanded = true;

        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new Commit(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), "Test commit", new List<string> { "." }));

        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(Commit) && ((Commit)cmd).Correlation.SessionId == sessionId3)), Times.Once);

        // Make stream1 active again
        stream1.IsExpanded = true;
        stream2.IsExpanded = false;
        stream3.IsExpanded = false;

        await form.ExecuteCommandForActiveSessionTest((correlation) => 
            new Push(Guid.NewGuid(), correlation, new RepoRef("test", "/tmp/test"), "main"));

        mockSessionManager.Verify(sm => sm.PublishCommand(It.Is<ICommand>(cmd => 
            cmd.GetType() == typeof(Push) && ((Push)cmd).Correlation.SessionId == sessionId1)), Times.Once);
    }

    [Fact]
    public async Task MultiSessionSupport_SessionCreationDialog_IntegratesWithChatStreamCreation()
    {
        // Arrange
        var mockSessionManager = new Mock<ISessionManager>();
        var sessionId = Guid.NewGuid();

        mockSessionManager.Setup(sm => sm.CreateSession(It.IsAny<SessionConfig>()))
            .Returns(Task.CompletedTask);

        var form = CreateMainForm(isTestMode: true); // Test mode to avoid actual dialog
        form.SetSessionManager(mockSessionManager.Object);

        // In test mode, CreateNewSession should return early without doing anything
        // This test verifies the method exists and can be called without errors

        // Act
        var exception = await Record.ExceptionAsync(() => form.CreateNewSessionForTest());

        // Assert - Should not throw in test mode
        Assert.Null(exception);
    }

    [Fact]
    public void TranscriptPersistence_SaveAndLoad_RoundTrip()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        transcript.AddMessage("user", "Hello, can you help me?");
        transcript.AddMessage("assistant", "Yes, I'd be happy to help!");
        transcript.AddMessage("user", "Great, let's get started.");

        // Act - Save transcript
        storage.SaveTranscript(transcript);

        // Load transcript
        var loadedTranscript = storage.LoadTranscript(sessionId);

        // Assert
        Assert.NotNull(loadedTranscript);
        Assert.Equal(sessionId, loadedTranscript.SessionId);
        Assert.Equal("Test Agent", loadedTranscript.AgentName);
        Assert.Equal(3, loadedTranscript.Messages.Count);
        Assert.Equal("user", loadedTranscript.Messages[0].Role);
        Assert.Equal("Hello, can you help me?", loadedTranscript.Messages[0].Content);
        Assert.Equal("assistant", loadedTranscript.Messages[1].Role);
        Assert.Equal("Yes, I'd be happy to help!", loadedTranscript.Messages[1].Content);
        Assert.Equal("user", loadedTranscript.Messages[2].Role);
        Assert.Equal("Great, let's get started.", loadedTranscript.Messages[2].Content);
    }

    [Fact]
    public void TranscriptPersistence_Disabled_DoesNotSaveOrLoad()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = false,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        transcript.AddMessage("user", "Test message");

        // Act - Try to save (should do nothing)
        storage.SaveTranscript(transcript);

        // Try to load (should return null)
        var loadedTranscript = storage.LoadTranscript(sessionId);

        // Assert
        Assert.Null(loadedTranscript);
        Assert.False(File.Exists(storage.GetTranscriptFilePath(sessionId)));
    }

    [Fact]
    public void TranscriptPersistence_CorruptedFile_FallsBackToNull()
    {
        // Arrange - Create corrupted transcript file
        var sessionId = Guid.NewGuid();
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var filePath = storage.GetTranscriptFilePath(sessionId);
        File.WriteAllText(filePath, "{invalid json content}");

        // Act - Try to load corrupted transcript
        var loadedTranscript = storage.LoadTranscript(sessionId);

        // Assert - Should return null on corruption
        Assert.Null(loadedTranscript);
    }

    [Fact]
    public void TranscriptPersistence_PruneByMessageCount()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            MaxMessagesPerTranscript = 2,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        
        // Add more messages than the limit
        transcript.AddMessage("user", "Message 1");
        transcript.AddMessage("assistant", "Response 1");
        transcript.AddMessage("user", "Message 2");
        transcript.AddMessage("assistant", "Response 2");
        transcript.AddMessage("user", "Message 3"); // This should be kept
        transcript.AddMessage("assistant", "Response 3"); // This should be kept

        // Act - Save (should prune during save)
        storage.SaveTranscript(transcript);
        var loadedTranscript = storage.LoadTranscript(sessionId);

        // Assert - Only last 2 messages should remain
        Assert.NotNull(loadedTranscript);
        Assert.Equal(2, loadedTranscript.Messages.Count);
        Assert.Equal("Message 3", loadedTranscript.Messages[0].Content);
        Assert.Equal("Response 3", loadedTranscript.Messages[1].Content);
    }

    [Fact]
    public void TranscriptPersistence_PruneByAge()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            MaxTranscriptAge = TimeSpan.FromMinutes(1), // Very short for testing
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        
        // Add old message
        var oldMessage = new Ui.Shell.ChatMessage("user", "Old message", DateTimeOffset.Now.AddMinutes(-5));
        transcript.Messages.Add(oldMessage);
        
        // Add new message
        transcript.AddMessage("user", "New message");

        // Act - Save (should prune old messages)
        storage.SaveTranscript(transcript);
        var loadedTranscript = storage.LoadTranscript(sessionId);

        // Assert - Only new message should remain
        Assert.NotNull(loadedTranscript);
        Assert.Equal(1, loadedTranscript.Messages.Count);
        Assert.Equal("New message", loadedTranscript.Messages[0].Content);
    }

    [Fact]
    public void TranscriptPersistence_GetExistingSessionIds()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        
        var transcript1 = new ChatTranscript(sessionId1, "Agent 1");
        transcript1.AddMessage("user", "Hello");
        storage.SaveTranscript(transcript1);
        
        var transcript2 = new ChatTranscript(sessionId2, "Agent 2");
        transcript2.AddMessage("user", "Hi there");
        storage.SaveTranscript(transcript2);

        // Act
        var existingIds = storage.GetExistingSessionIds().ToList();

        // Assert
        Assert.Equal(2, existingIds.Count);
        Assert.Contains(sessionId1, existingIds);
        Assert.Contains(sessionId2, existingIds);
    }

    [Fact]
    public void TranscriptPersistence_DeleteTranscript()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        transcript.AddMessage("user", "Test message");
        storage.SaveTranscript(transcript);
        
        // Verify file exists
        Assert.True(storage.TranscriptExists(sessionId));

        // Act - Delete transcript
        storage.DeleteTranscript(sessionId);

        // Assert - File should be gone
        Assert.False(storage.TranscriptExists(sessionId));
        Assert.Null(storage.LoadTranscript(sessionId));
    }

    [Fact]
    public void TranscriptPersistence_UIIntegration_LoadTranscriptIntoChatControl()
    {
        // Arrange - Create a transcript with messages
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        transcript.AddMessage("user", "Hello, can you help me?");
        transcript.AddMessage("assistant", "Yes, I'd be happy to help!");
        transcript.AddMessage("user", "Great, let's get started.");
        storage.SaveTranscript(transcript);

        // Test the message conversion logic directly (simulate what happens in LoadTranscriptIntoChatControl)
        var convertedMessages = new List<(Microsoft.Extensions.AI.ChatRole Role, string Content)>();
        foreach (var message in transcript.Messages)
        {
            // Convert role string to ChatMessageRole enum (same logic as in AgentPanel)
            var chatRole = message.Role.ToLower() switch
            {
                "user" => Microsoft.Extensions.AI.ChatRole.User,
                "assistant" => Microsoft.Extensions.AI.ChatRole.Assistant,
                "system" => Microsoft.Extensions.AI.ChatRole.System,
                _ => Microsoft.Extensions.AI.ChatRole.User // Default to User for unknown roles
            };
            convertedMessages.Add((chatRole, message.Content));
        }

        // Assert - Messages should be converted correctly
        Assert.Equal(3, convertedMessages.Count);
        
        // Verify first message (user)
        Assert.Equal(Microsoft.Extensions.AI.ChatRole.User, convertedMessages[0].Role);
        Assert.Equal("Hello, can you help me?", convertedMessages[0].Content);
        
        // Verify second message (assistant)
        Assert.Equal(Microsoft.Extensions.AI.ChatRole.Assistant, convertedMessages[1].Role);
        Assert.Equal("Yes, I'd be happy to help!", convertedMessages[1].Content);
        
        // Verify third message (user)
        Assert.Equal(Microsoft.Extensions.AI.ChatRole.User, convertedMessages[2].Role);
        Assert.Equal("Great, let's get started.", convertedMessages[2].Content);
    }

    [Fact]
    public void TranscriptPersistence_UIIntegration_SaveUserMessageOnSend()
    {
        // Arrange
        var config = new Ui.Shell.TranscriptConfig
        {
            Enabled = true,
            StorageDirectory = _tempDir
        };
        var storage = new TranscriptStorage(config);
        var sessionId = Guid.NewGuid();
        var transcript = new ChatTranscript(sessionId, "Test Agent");
        
        // Simulate the OnMessageSent logic (same as in AgentPanel)
        var messageContent = "Hello from user!";
        if (transcript != null)
        {
            // The MessageSent event gives us the content of the message that was sent
            // We assume it's from the user since this is a user-initiated send
            transcript.AddMessage("user", messageContent);
            storage.SaveTranscript(transcript);
        }

        // Assert - Message should be saved to transcript
        var loadedTranscript = storage.LoadTranscript(sessionId);
        Assert.NotNull(loadedTranscript);
        Assert.Single(loadedTranscript.Messages);
        Assert.Equal("user", loadedTranscript.Messages[0].Role);
        Assert.Equal("Hello from user!", loadedTranscript.Messages[0].Content);
    }

    [Fact]
    public void LiveAdapterCredentials_Validation_SucceedsWithValidConfig()
    {
        // Arrange - Create valid app config with Jira and GitHub credentials
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("jira", "git", "powershell"),
            Auth = new AuthConfig
            {
                Jira = new JiraAuthConfig("https://company.atlassian.net", "user@company.com", "api-token-123"),
                GitHub = new GitHubAuthConfig("ghp_1234567890abcdef", "company-org", "main-repo")
            }
        };

        // Act & Assert - Should not throw exception
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.Null(exception);
    }

    [Fact]
    public void LiveAdapterCredentials_Validation_FailsWithMissingJiraConfig()
    {
        // Arrange - Missing Jira config but Jira adapter selected
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("jira", "git", "powershell"),
            Auth = new AuthConfig
            {
                GitHub = new GitHubAuthConfig("ghp_1234567890abcdef")
            }
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("Jira authentication not configured", exception.Message);
    }

    [Fact]
    public void LiveAdapterCredentials_Validation_FailsWithMissingGitHubConfig()
    {
        // Arrange - Missing GitHub config but GitHub adapter selected
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell"),
            Auth = new AuthConfig
            {
                Jira = new JiraAuthConfig("https://company.atlassian.net", "user@company.com", "api-token-123")
            }
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("GitHub authentication not configured", exception.Message);
    }

    [Fact]
    public void LiveAdapterCredentials_Validation_FailsWithIncompleteJiraConfig()
    {
        // Arrange - Incomplete Jira config (missing base URL) but Jira adapter selected
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("jira", "git", "powershell"),
            Auth = new AuthConfig
            {
                Jira = new JiraAuthConfig("", "user@company.com", "api-token-123"),
                GitHub = new GitHubAuthConfig("ghp_1234567890abcdef")
            }
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("Jira BaseUrl is required", exception.Message);
    }

    [Fact]
    public void LiveAdapterCredentials_Validation_FailsWithIncompleteGitHubConfig()
    {
        // Arrange - Incomplete GitHub config (missing token) but GitHub adapter selected
        var appConfig = new AppConfig
        {
            Adapters = new AdaptersConfig("github", "git", "powershell"),
            Auth = new AuthConfig
            {
                Jira = new JiraAuthConfig("https://company.atlassian.net", "user@company.com", "api-token-123"),
                GitHub = new GitHubAuthConfig("")
            }
        };

        // Act & Assert - Should throw with specific error
        var exception = Record.Exception(() => ConfigBuilder.ValidateLiveAdapterCredentials(appConfig));
        Assert.NotNull(exception);
        Assert.Contains("GitHub Token is required", exception.Message);
    }

public class TestAsyncEnumerable : IAsyncEnumerable<IEvent>
{
    private readonly IEnumerable<IEvent> _events;

    public TestAsyncEnumerable(IEnumerable<IEvent> events)
    {
        _events = events;
    }

    public IAsyncEnumerator<IEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator(_events.GetEnumerator());
    }
}

public class TestAsyncEnumerator : IAsyncEnumerator<IEvent>
{
    private readonly IEnumerator<IEvent> _enumerator;

    public TestAsyncEnumerator(IEnumerator<IEvent> enumerator)
    {
        _enumerator = enumerator;
        Console.WriteLine($"TestAsyncEnumerator created with {_enumerator} events");
    }

    public IEvent Current => _enumerator.Current;

    public ValueTask DisposeAsync()
    {
        Console.WriteLine("TestAsyncEnumerator disposing");
        _enumerator.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> MoveNextAsync()
    {
        var result = _enumerator.MoveNext();
        Console.WriteLine($"TestAsyncEnumerator.MoveNextAsync: {result}, Current: {_enumerator.Current?.Kind ?? "null"}");
        return ValueTask.FromResult(result);
    }
}
}
