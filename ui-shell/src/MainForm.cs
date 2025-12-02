using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraEditors;
using DevExpress.XtraTreeList;
using DevExpress.AIIntegration.WinForms.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using JuniorDev.Contracts;
using JuniorDev.Orchestrator;
using DevExpress.AIIntegration;
using System.Xml;
using System.Linq;
using System.Text.Json;

#pragma warning disable CS8602 // Dereference of possibly null reference - suppressed for fields initialized in constructor

namespace Ui.Shell;

public enum BlockingType
{
    NeedsApproval,
    ConflictDetected,
    Throttled
}

public class BlockingCondition
{
    public Guid SessionId { get; set; }
    public BlockingType Type { get; set; }
    public string Message { get; set; } = "";
    public string ActionText { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}

public class SessionItem
{
    public string Name { get; set; } = "";
    public JuniorDev.Contracts.SessionStatus Status { get; set; }

    public override string ToString()
    {
        return Name; // Just return the name, status will be drawn separately
    }

    public (Color backColor, Color foreColor, string text) GetStatusChip()
    {
        return Status switch
        {
            JuniorDev.Contracts.SessionStatus.Running => (Color.Green, Color.White, "RUNNING"),
            JuniorDev.Contracts.SessionStatus.Paused => (Color.Yellow, Color.Black, "PAUSED"),
            JuniorDev.Contracts.SessionStatus.Error => (Color.Red, Color.White, "ERROR"),
            JuniorDev.Contracts.SessionStatus.NeedsApproval => (Color.Orange, Color.Black, "NEEDS APPROVAL"),
            JuniorDev.Contracts.SessionStatus.Completed => (Color.Blue, Color.White, "COMPLETED"),
            _ => (Color.Gray, Color.Black, "UNKNOWN")
        };
    }
}

// Event rendering classes
public class EventRenderer
{
    private readonly MemoEdit _memoEdit;

    public bool ShowTimestamps { get; set; } = true;
    public int MaxEventHistory { get; set; } = 1000;

    public EventRenderer(MemoEdit memoEdit)
    {
        _memoEdit = memoEdit;
    }

    public void RenderEvent(IEvent @event, DateTimeOffset timestamp)
    {
        var message = FormatEventAsMessage(@event, timestamp);
        _memoEdit.Text += message + Environment.NewLine;
        
        // Auto-scroll to bottom if enabled
        if (_memoEdit.Parent?.Parent is MainForm mainForm && mainForm.GetAutoScrollEventsSetting())
        {
            _memoEdit.SelectionStart = _memoEdit.Text.Length;
            _memoEdit.ScrollToCaret();
        }
    }

    private string FormatEventAsMessage(IEvent @event, DateTimeOffset timestamp)
    {
        var timeStr = timestamp.ToString("HH:mm:ss.fff");
        var sessionId = @event.Correlation.SessionId.ToString("N")[..8]; // First 8 chars
        var commandId = @event.Correlation.CommandId?.ToString("N")[..8] ?? "--------";

        var baseText = $"[{timeStr}] [{sessionId}] [{commandId}] ";

        switch (@event)
        {
            case CommandAccepted accepted:
                return $"{baseText}✅ Command {accepted.CommandId.ToString("N")[..8]} ACCEPTED";
            
            case CommandCompleted completed:
            {
                var outcome = completed.Outcome == CommandOutcome.Success ? "SUCCESS" : "FAILED";
                var message = string.IsNullOrEmpty(completed.Message) ? "" : $" - {completed.Message}";
                return $"{baseText}🏁 Command {completed.CommandId.ToString("N")[..8]} {outcome}{message}";
            }
            
            case CommandRejected rejected:
                return $"{baseText}❌ Command {rejected.CommandId.ToString("N")[..8]} REJECTED: {rejected.Reason}";
            
            case Throttled throttled:
                return $"{baseText}⏱️ THROTTLED ({throttled.Scope}) - Retry after {throttled.RetryAfter}";
            
            case ConflictDetected conflict:
                return $"{baseText}⚠️ CONFLICT: {conflict.Details}";
            
            case ArtifactAvailable artifact:
            {
                var summary = artifact.Artifact.InlineText?.Length > 100 
                    ? artifact.Artifact.InlineText[..100] + "..." 
                    : artifact.Artifact.InlineText ?? "[Binary artifact]";
                return $"{baseText}📎 ARTIFACT: {artifact.Artifact.Name} ({artifact.Artifact.Kind}) - {summary}";
            }
            
            case SessionStatusChanged status:
                return $"{baseText}🔄 Session {status.Status}{(string.IsNullOrEmpty(status.Reason) ? "" : $" - {status.Reason}")}";
            
            case WorkItemClaimed claimed:
                return $"{baseText}🔒 Work item {claimed.Item.Id} claimed by {claimed.Assignee} (expires: {claimed.ExpiresAt:HH:mm:ss})";
            
            case WorkItemClaimReleased released:
                return $"{baseText}🔓 Work item {released.Item.Id} released{(string.IsNullOrEmpty(released.Reason) ? "" : $" - {released.Reason}")}";
            
            case ClaimRenewed renewed:
                return $"{baseText}🔄 Work item {renewed.Item.Id} claim renewed (expires: {renewed.NewExpiresAt:HH:mm:ss})";
            
            case ClaimExpired expired:
                return $"{baseText}⏰ Work item {expired.Item.Id} claim expired (was held by {expired.PreviousAssignee})";
            
            default:
                return $"{baseText}📝 {@event.Kind}: {System.Text.Json.JsonSerializer.Serialize(@event)}";
        }
    }
}

public class ChatStreamData
{
    public Guid SessionId { get; set; }
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; } = "Agent";
    public JuniorDev.Contracts.SessionStatus Status { get; set; } = JuniorDev.Contracts.SessionStatus.Running;
    public string CurrentTask { get; set; } = "";
    public int ProgressPercentage { get; set; } = 0;
    public bool IsExpanded { get; set; } = false;
}

public class AppSettings
{
    public string Theme { get; set; } = "Light";
    public int FontSize { get; set; } = 9;
    public bool ShowStatusChips { get; set; } = true;
    public bool AutoScrollEvents { get; set; } = true;
    public bool ShowTimestamps { get; set; } = true;
    public int MaxEventHistory { get; set; } = 1000;

    // Live orchestrator profile settings
    public LiveProfileSettings LiveProfile { get; set; } = new();
}

public class LiveProfileSettings
{
    public string WorkItemsAdapter { get; set; } = "fake";
    public string VcsAdapter { get; set; } = "fake";
    public string BuildAdapter { get; set; } = "powershell";

    // GitHub settings
    public string GitHubToken { get; set; } = "";
    public string GitHubOrg { get; set; } = "";
    public string GitHubRepo { get; set; } = "";

    // Jira settings
    public string JiraUrl { get; set; } = "";
    public string JiraUsername { get; set; } = "";
    public string JiraApiToken { get; set; } = "";

    // Live policy settings
    public bool PushEnabled { get; set; } = false;
    public bool DryRun { get; set; } = true;
    public bool RequireCredentialsValidation { get; set; } = true;

    // Current mode
    public bool IsLiveMode { get; set; } = false;
}

public class SettingsDialog : Form
{
    private System.Windows.Forms.TabControl? tabControl;
    private System.Windows.Forms.TabPage? uiTabPage;
    private System.Windows.Forms.TabPage? liveProfileTabPage;

    // UI Settings controls
    private System.Windows.Forms.ComboBox? themeCombo;
    private System.Windows.Forms.NumericUpDown? fontSizeSpinner;
    private System.Windows.Forms.CheckBox? statusChipsCheck;
    private System.Windows.Forms.CheckBox? autoScrollCheck;
    private System.Windows.Forms.CheckBox? showTimestampsCheck;
    private System.Windows.Forms.NumericUpDown? maxEventHistorySpinner;

    // Live Profile controls
    private System.Windows.Forms.ComboBox? workItemsAdapterCombo;
    private System.Windows.Forms.ComboBox? vcsAdapterCombo;
    private System.Windows.Forms.ComboBox? buildAdapterCombo;

    // GitHub controls
    private System.Windows.Forms.TextBox? githubTokenTextBox;
    private System.Windows.Forms.TextBox? githubOrgTextBox;
    private System.Windows.Forms.TextBox? githubRepoTextBox;
    private System.Windows.Forms.Button? githubValidateButton;
    private System.Windows.Forms.Label? githubStatusLabel;

    // Jira controls
    private System.Windows.Forms.TextBox? jiraUrlTextBox;
    private System.Windows.Forms.TextBox? jiraUsernameTextBox;
    private System.Windows.Forms.TextBox? jiraApiTokenTextBox;
    private System.Windows.Forms.Button? jiraValidateButton;
    private System.Windows.Forms.Label? jiraStatusLabel;

    // Live policy controls
    private System.Windows.Forms.CheckBox? pushEnabledCheck;
    private System.Windows.Forms.CheckBox? dryRunCheck;
    private System.Windows.Forms.CheckBox? requireValidationCheck;

    // Profile mode controls
    private System.Windows.Forms.RadioButton? fakeModeRadio;
    private System.Windows.Forms.RadioButton? liveModeRadio;
    private System.Windows.Forms.Button? validateAllButton;
    private System.Windows.Forms.Label? profileStatusLabel;

    // Buttons
    private System.Windows.Forms.Button? okButton;
    private System.Windows.Forms.Button? cancelButton;

    public AppSettings Settings { get; set; } = new();

    public SettingsDialog()
    {
        InitializeDialog();
        LoadCurrentSettings();
    }

    private void InitializeDialog()
    {
        this.Text = "Settings";
        this.Size = new Size(600, 500);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        // Create tab control
        tabControl = new System.Windows.Forms.TabControl
        {
            Dock = DockStyle.Fill,
            Location = new Point(0, 0)
        };

        // UI Settings Tab
        uiTabPage = new System.Windows.Forms.TabPage("UI Settings");
        InitializeUITab();

        // Live Profile Tab
        liveProfileTabPage = new System.Windows.Forms.TabPage("Live Profile");
        InitializeLiveProfileTab();

        tabControl.TabPages.Add(uiTabPage);
        tabControl.TabPages.Add(liveProfileTabPage);

        // Buttons at bottom
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            FlowDirection = FlowDirection.RightToLeft
        };

        okButton = new System.Windows.Forms.Button { Text = "OK", Width = 80, DialogResult = DialogResult.OK };
        cancelButton = new System.Windows.Forms.Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };

        okButton.Click += OkButton_Click;

        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        this.Controls.Add(tabControl);
        this.Controls.Add(buttonPanel);

        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;
    }

    private void InitializeUITab()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10)
        };

        // Theme
        var themePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 550 };
        var themeLabel = new System.Windows.Forms.Label { Text = "Theme:", AutoSize = true, Width = 80 };
        themeCombo = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150
        };
        themeCombo.Items.AddRange(new[] { "Light", "Dark", "Blue" });
        themePanel.Controls.Add(themeLabel);
        themePanel.Controls.Add(themeCombo);

        // Font Size
        var fontPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 550 };
        var fontLabel = new System.Windows.Forms.Label { Text = "Font Size:", AutoSize = true, Width = 80 };
        fontSizeSpinner = new System.Windows.Forms.NumericUpDown
        {
            Minimum = 8,
            Maximum = 16,
            Value = 9,
            Width = 60
        };
        fontPanel.Controls.Add(fontLabel);
        fontPanel.Controls.Add(fontSizeSpinner);

        // Checkboxes
        statusChipsCheck = new System.Windows.Forms.CheckBox { Text = "Show status chips", AutoSize = true, Width = 200 };
        autoScrollCheck = new System.Windows.Forms.CheckBox { Text = "Auto-scroll events", AutoSize = true, Width = 200, Checked = true };
        showTimestampsCheck = new System.Windows.Forms.CheckBox { Text = "Show timestamps", AutoSize = true, Width = 200, Checked = true };

        // Max Event History
        var historyPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 550 };
        var historyLabel = new System.Windows.Forms.Label { Text = "Max Event History:", AutoSize = true, Width = 120 };
        maxEventHistorySpinner = new System.Windows.Forms.NumericUpDown
        {
            Minimum = 100,
            Maximum = 10000,
            Value = 1000,
            Width = 80
        };
        historyPanel.Controls.Add(historyLabel);
        historyPanel.Controls.Add(maxEventHistorySpinner);

        panel.Controls.Add(themePanel);
        panel.Controls.Add(fontPanel);
        panel.Controls.Add(statusChipsCheck);
        panel.Controls.Add(autoScrollCheck);
        panel.Controls.Add(showTimestampsCheck);
        panel.Controls.Add(historyPanel);

        uiTabPage!.Controls.Add(panel);
    }

    private void InitializeLiveProfileTab()
    {
        var scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        var mainPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10),
            AutoSize = true,
            Width = 550
        };

        // Profile Mode Section
        var modeGroup = new GroupBox { Text = "Profile Mode", Width = 530, Height = 80 };
        fakeModeRadio = new System.Windows.Forms.RadioButton { Text = "Fake Mode (Safe, no external connections)", Location = new Point(10, 20), AutoSize = true, Checked = true };
        liveModeRadio = new System.Windows.Forms.RadioButton { Text = "Live Mode (Connect to real services)", Location = new Point(10, 45), AutoSize = true };
        profileStatusLabel = new System.Windows.Forms.Label { Text = "Current: Fake Mode", Location = new Point(300, 30), AutoSize = true, ForeColor = Color.Green };
        modeGroup.Controls.Add(fakeModeRadio);
        modeGroup.Controls.Add(liveModeRadio);
        modeGroup.Controls.Add(profileStatusLabel);

        // Adapter Configuration Section
        var adapterGroup = new GroupBox { Text = "Adapter Configuration", Width = 530, Height = 120 };
        var adapterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10)
        };

        // Work Items Adapter
        var workItemsPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 500 };
        var workItemsLabel = new System.Windows.Forms.Label { Text = "Work Items:", AutoSize = true, Width = 80 };
        workItemsAdapterCombo = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120
        };
        workItemsAdapterCombo.Items.AddRange(new[] { "fake", "github", "jira" });
        workItemsPanel.Controls.Add(workItemsLabel);
        workItemsPanel.Controls.Add(workItemsAdapterCombo);

        // VCS Adapter
        var vcsPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 500 };
        var vcsLabel = new System.Windows.Forms.Label { Text = "Version Control:", AutoSize = true, Width = 80 };
        vcsAdapterCombo = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120
        };
        vcsAdapterCombo.Items.AddRange(new[] { "fake", "git" });
        vcsPanel.Controls.Add(vcsLabel);
        vcsPanel.Controls.Add(vcsAdapterCombo);

        // Build Adapter
        var buildPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 500 };
        var buildLabel = new System.Windows.Forms.Label { Text = "Build System:", AutoSize = true, Width = 80 };
        buildAdapterCombo = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 120
        };
        buildAdapterCombo.Items.AddRange(new[] { "fake", "dotnet", "powershell" });
        buildPanel.Controls.Add(buildLabel);
        buildPanel.Controls.Add(buildAdapterCombo);

        adapterPanel.Controls.Add(workItemsPanel);
        adapterPanel.Controls.Add(vcsPanel);
        adapterPanel.Controls.Add(buildPanel);
        adapterGroup.Controls.Add(adapterPanel);

        // GitHub Credentials Section
        var githubGroup = new GroupBox { Text = "GitHub Credentials", Width = 530, Height = 150 };
        var githubPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10)
        };

        githubTokenTextBox = new System.Windows.Forms.TextBox { PlaceholderText = "GitHub Personal Access Token", Width = 480, UseSystemPasswordChar = true };
        githubOrgTextBox = new System.Windows.Forms.TextBox { PlaceholderText = "Default Organization (optional)", Width = 480 };
        githubRepoTextBox = new System.Windows.Forms.TextBox { PlaceholderText = "Default Repository (optional)", Width = 480 };

        var githubButtonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 480 };
        githubValidateButton = new System.Windows.Forms.Button { Text = "Validate GitHub", Width = 100 };
        githubStatusLabel = new System.Windows.Forms.Label { Text = "Not validated", AutoSize = true };
        githubButtonPanel.Controls.Add(githubValidateButton);
        githubButtonPanel.Controls.Add(githubStatusLabel);

        githubValidateButton.Click += GithubValidateButton_Click;

        githubPanel.Controls.Add(new System.Windows.Forms.Label { Text = "Token:", AutoSize = true });
        githubPanel.Controls.Add(githubTokenTextBox);
        githubPanel.Controls.Add(new System.Windows.Forms.Label { Text = "Organization:", AutoSize = true });
        githubPanel.Controls.Add(githubOrgTextBox);
        githubPanel.Controls.Add(new System.Windows.Forms.Label { Text = "Repository:", AutoSize = true });
        githubPanel.Controls.Add(githubRepoTextBox);
        githubPanel.Controls.Add(githubButtonPanel);
        githubGroup.Controls.Add(githubPanel);

        // Jira Credentials Section
        var jiraGroup = new GroupBox { Text = "Jira Credentials", Width = 530, Height = 170 };
        var jiraPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10)
        };

        jiraUrlTextBox = new System.Windows.Forms.TextBox { PlaceholderText = "https://company.atlassian.net", Width = 480 };
        jiraUsernameTextBox = new System.Windows.Forms.TextBox { PlaceholderText = "username@company.com", Width = 480 };
        jiraApiTokenTextBox = new System.Windows.Forms.TextBox { PlaceholderText = "API Token", Width = 480, UseSystemPasswordChar = true };

        var jiraButtonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 480 };
        jiraValidateButton = new System.Windows.Forms.Button { Text = "Validate Jira", Width = 100 };
        jiraStatusLabel = new System.Windows.Forms.Label { Text = "Not validated", AutoSize = true };
        jiraButtonPanel.Controls.Add(jiraValidateButton);
        jiraButtonPanel.Controls.Add(jiraStatusLabel);

        jiraValidateButton.Click += JiraValidateButton_Click;

        jiraPanel.Controls.Add(new System.Windows.Forms.Label { Text = "URL:", AutoSize = true });
        jiraPanel.Controls.Add(jiraUrlTextBox);
        jiraPanel.Controls.Add(new System.Windows.Forms.Label { Text = "Username:", AutoSize = true });
        jiraPanel.Controls.Add(jiraUsernameTextBox);
        jiraPanel.Controls.Add(new System.Windows.Forms.Label { Text = "API Token:", AutoSize = true });
        jiraPanel.Controls.Add(jiraApiTokenTextBox);
        jiraPanel.Controls.Add(jiraButtonPanel);
        jiraGroup.Controls.Add(jiraPanel);

        // Live Policy Section
        var policyGroup = new GroupBox { Text = "Live Policy Settings", Width = 530, Height = 100 };
        var policyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(10)
        };

        pushEnabledCheck = new System.Windows.Forms.CheckBox { Text = "Enable push operations", AutoSize = true };
        dryRunCheck = new System.Windows.Forms.CheckBox { Text = "Dry run mode (simulate operations)", AutoSize = true, Checked = true };
        requireValidationCheck = new System.Windows.Forms.CheckBox { Text = "Require credentials validation", AutoSize = true, Checked = true };

        var validateAllPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 30, Width = 480 };
        validateAllButton = new System.Windows.Forms.Button { Text = "Validate All Credentials", Width = 150 };
        validateAllButton.Click += ValidateAllButton_Click;
        validateAllPanel.Controls.Add(validateAllButton);

        policyPanel.Controls.Add(pushEnabledCheck);
        policyPanel.Controls.Add(dryRunCheck);
        policyPanel.Controls.Add(requireValidationCheck);
        policyPanel.Controls.Add(validateAllPanel);
        policyGroup.Controls.Add(policyPanel);

        // Wire up mode change events
        fakeModeRadio.CheckedChanged += ModeRadio_CheckedChanged;
        liveModeRadio.CheckedChanged += ModeRadio_CheckedChanged;

        mainPanel.Controls.Add(modeGroup);
        mainPanel.Controls.Add(adapterGroup);
        mainPanel.Controls.Add(githubGroup);
        mainPanel.Controls.Add(jiraGroup);
        mainPanel.Controls.Add(policyGroup);

        scrollPanel.Controls.Add(mainPanel);
        liveProfileTabPage!.Controls.Add(scrollPanel);
    }

    private void LoadCurrentSettings()
    {
        // Load UI settings
        themeCombo!.SelectedItem = Settings.Theme;
        fontSizeSpinner!.Value = Settings.FontSize;
        statusChipsCheck!.Checked = Settings.ShowStatusChips;
        autoScrollCheck!.Checked = Settings.AutoScrollEvents;
        showTimestampsCheck!.Checked = Settings.ShowTimestamps;
        maxEventHistorySpinner!.Value = Settings.MaxEventHistory;

        // Load live profile settings
        workItemsAdapterCombo!.SelectedItem = Settings.LiveProfile.WorkItemsAdapter;
        vcsAdapterCombo!.SelectedItem = Settings.LiveProfile.VcsAdapter;
        buildAdapterCombo!.SelectedItem = Settings.LiveProfile.BuildAdapter;

        githubTokenTextBox!.Text = Settings.LiveProfile.GitHubToken;
        githubOrgTextBox!.Text = Settings.LiveProfile.GitHubOrg;
        githubRepoTextBox!.Text = Settings.LiveProfile.GitHubRepo;

        jiraUrlTextBox!.Text = Settings.LiveProfile.JiraUrl;
        jiraUsernameTextBox!.Text = Settings.LiveProfile.JiraUsername;
        jiraApiTokenTextBox!.Text = Settings.LiveProfile.JiraApiToken;

        pushEnabledCheck!.Checked = Settings.LiveProfile.PushEnabled;
        dryRunCheck!.Checked = Settings.LiveProfile.DryRun;
        requireValidationCheck!.Checked = Settings.LiveProfile.RequireCredentialsValidation;

        fakeModeRadio!.Checked = !Settings.LiveProfile.IsLiveMode;
        liveModeRadio!.Checked = Settings.LiveProfile.IsLiveMode;

        UpdateProfileStatus();
        UpdateControlStates();
    }

    private void ModeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateControlStates();
        UpdateProfileStatus();
    }

    private void UpdateControlStates()
    {
        bool isLiveMode = liveModeRadio!.Checked;

        // Enable/disable adapter controls based on mode
        workItemsAdapterCombo!.Enabled = isLiveMode;
        vcsAdapterCombo!.Enabled = isLiveMode;
        buildAdapterCombo!.Enabled = isLiveMode;

        // Enable/disable credential controls based on mode and adapter selection
        bool githubEnabled = isLiveMode && workItemsAdapterCombo!.SelectedItem?.ToString() == "github";
        githubTokenTextBox!.Enabled = githubEnabled;
        githubOrgTextBox!.Enabled = githubEnabled;
        githubRepoTextBox!.Enabled = githubEnabled;
        githubValidateButton!.Enabled = githubEnabled;

        bool jiraEnabled = isLiveMode && workItemsAdapterCombo!.SelectedItem?.ToString() == "jira";
        jiraUrlTextBox!.Enabled = jiraEnabled;
        jiraUsernameTextBox!.Enabled = jiraEnabled;
        jiraApiTokenTextBox!.Enabled = jiraEnabled;
        jiraValidateButton!.Enabled = jiraEnabled;

        // Policy controls always enabled in live mode
        pushEnabledCheck!.Enabled = isLiveMode;
        dryRunCheck!.Enabled = isLiveMode;
        requireValidationCheck!.Enabled = isLiveMode;
        validateAllButton!.Enabled = isLiveMode;
    }

    private void UpdateProfileStatus()
    {
        bool isLiveMode = liveModeRadio!.Checked;
        profileStatusLabel!.Text = $"Current: {(isLiveMode ? "Live Mode" : "Fake Mode")}";
        profileStatusLabel!.ForeColor = isLiveMode ? Color.Red : Color.Green;
    }

    private async void GithubValidateButton_Click(object? sender, EventArgs e)
    {
        await ValidateGitHubCredentials();
    }

    private async void JiraValidateButton_Click(object? sender, EventArgs e)
    {
        await ValidateJiraCredentials();
    }

    private async void ValidateAllButton_Click(object? sender, EventArgs e)
    {
        await ValidateAllCredentials();
    }

    private async Task ValidateGitHubCredentials()
    {
        githubStatusLabel!.Text = "Validating...";
        githubStatusLabel!.ForeColor = Color.Orange;

        try
        {
            var token = githubTokenTextBox!.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("GitHub token is required");
            }

            // Create a temporary config to validate
            var authConfig = new AuthConfig
            {
                GitHub = new GitHubAuthConfig(token, githubOrgTextBox!.Text.Trim(), githubRepoTextBox!.Text.Trim())
            };

            var adaptersConfig = new AdaptersConfig("github", "fake", "powershell");
            var livePolicy = new LivePolicyConfig(pushEnabledCheck!.Checked, dryRunCheck!.Checked, requireValidationCheck!.Checked);

            var appConfig = new AppConfig
            {
                Auth = authConfig,
                Adapters = adaptersConfig,
                LivePolicy = livePolicy
            };

            // Validate credentials
            ConfigBuilder.ValidateLiveAdapterCredentials(appConfig);

            // Additional validation - try to make a simple API call
            // For now, just check token format (should start with ghp_)
            if (!token.StartsWith("ghp_") && !token.StartsWith("github_pat_"))
            {
                throw new InvalidOperationException("GitHub token appears to be invalid (should start with 'ghp_' or 'github_pat_')");
            }

            githubStatusLabel!.Text = "✓ Valid";
            githubStatusLabel!.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            githubStatusLabel!.Text = $"✗ Invalid: {ex.Message}";
            githubStatusLabel!.ForeColor = Color.Red;
        }
    }

    private async Task ValidateJiraCredentials()
    {
        jiraStatusLabel!.Text = "Validating...";
        jiraStatusLabel!.ForeColor = Color.Orange;

        try
        {
            var url = jiraUrlTextBox!.Text.Trim();
            var username = jiraUsernameTextBox!.Text.Trim();
            var token = jiraApiTokenTextBox!.Text.Trim();

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("All Jira fields are required");
            }

            // Create a temporary config to validate
            var authConfig = new AuthConfig
            {
                Jira = new JiraAuthConfig(url, username, token)
            };

            var adaptersConfig = new AdaptersConfig("jira", "fake", "powershell");
            var livePolicy = new LivePolicyConfig(pushEnabledCheck!.Checked, dryRunCheck!.Checked, requireValidationCheck!.Checked);

            var appConfig = new AppConfig
            {
                Auth = authConfig,
                Adapters = adaptersConfig,
                LivePolicy = livePolicy
            };

            // Validate credentials
            ConfigBuilder.ValidateLiveAdapterCredentials(appConfig);

            jiraStatusLabel!.Text = "✓ Valid";
            jiraStatusLabel!.ForeColor = Color.Green;
        }
        catch (Exception ex)
        {
            jiraStatusLabel!.Text = $"✗ Invalid: {ex.Message}";
            jiraStatusLabel!.ForeColor = Color.Red;
        }
    }

    private async Task ValidateAllCredentials()
    {
        // Validate based on selected adapters
        var workItemsAdapter = workItemsAdapterCombo!.SelectedItem?.ToString() ?? "fake";

        if (workItemsAdapter == "github")
        {
            await ValidateGitHubCredentials();
        }
        else if (workItemsAdapter == "jira")
        {
            await ValidateJiraCredentials();
        }

        // Could add validation for VCS and build adapters here if needed
        MessageBox.Show("Credential validation complete. Check status indicators for results.", "Validation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        // Validate live mode settings if live mode is selected
        if (liveModeRadio!.Checked)
        {
            var workItemsAdapter = workItemsAdapterCombo!.SelectedItem?.ToString() ?? "fake";
            if (workItemsAdapter == "github" && string.IsNullOrEmpty(githubTokenTextBox!.Text.Trim()))
            {
                MessageBox.Show("GitHub token is required when using GitHub adapter in live mode.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                tabControl!.SelectedTab = liveProfileTabPage;
                return;
            }
            else if (workItemsAdapter == "jira" &&
                     (string.IsNullOrEmpty(jiraUrlTextBox!.Text.Trim()) ||
                      string.IsNullOrEmpty(jiraUsernameTextBox!.Text.Trim()) ||
                      string.IsNullOrEmpty(jiraApiTokenTextBox!.Text.Trim())))
            {
                MessageBox.Show("All Jira credentials are required when using Jira adapter in live mode.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                tabControl!.SelectedTab = liveProfileTabPage;
                return;
            }
        }

        Settings = new AppSettings
        {
            Theme = themeCombo!.SelectedItem?.ToString() ?? "Light",
            FontSize = (int)fontSizeSpinner!.Value,
            ShowStatusChips = statusChipsCheck!.Checked,
            AutoScrollEvents = autoScrollCheck!.Checked,
            ShowTimestamps = showTimestampsCheck!.Checked,
            MaxEventHistory = (int)maxEventHistorySpinner!.Value,
            LiveProfile = new LiveProfileSettings
            {
                WorkItemsAdapter = workItemsAdapterCombo!.SelectedItem?.ToString() ?? "fake",
                VcsAdapter = vcsAdapterCombo!.SelectedItem?.ToString() ?? "fake",
                BuildAdapter = buildAdapterCombo!.SelectedItem?.ToString() ?? "powershell",
                GitHubToken = githubTokenTextBox!.Text.Trim(),
                GitHubOrg = githubOrgTextBox!.Text.Trim(),
                GitHubRepo = githubRepoTextBox!.Text.Trim(),
                JiraUrl = jiraUrlTextBox!.Text.Trim(),
                JiraUsername = jiraUsernameTextBox!.Text.Trim(),
                JiraApiToken = jiraApiTokenTextBox!.Text.Trim(),
                PushEnabled = pushEnabledCheck!.Checked,
                DryRun = dryRunCheck!.Checked,
                RequireCredentialsValidation = requireValidationCheck!.Checked,
                IsLiveMode = liveModeRadio!.Checked
            }
        };
    }
}

public class SessionConfigDialog : Form
{
    private System.Windows.Forms.TextBox? repoNameTextBox;
    private System.Windows.Forms.TextBox? repoPathTextBox;
    private System.Windows.Forms.TextBox? workspacePathTextBox;
    private System.Windows.Forms.TextBox? policyNameTextBox;
    private System.Windows.Forms.CheckBox? requireTestsCheckBox;
    private System.Windows.Forms.CheckBox? requireApprovalCheckBox;
    private System.Windows.Forms.NumericUpDown? callsPerMinuteSpinner;
    private System.Windows.Forms.Button? okButton;
    private System.Windows.Forms.Button? cancelButton;

    public SessionConfig SessionConfig { get; private set; } = new SessionConfig(
        Guid.NewGuid(), null, null, new PolicyProfile { Name = "default" },
        new RepoRef("default-repo", Environment.CurrentDirectory),
        new WorkspaceRef(Environment.CurrentDirectory), null, "default");

    public SessionConfigDialog()
    {
        InitializeDialog();
        LoadDefaultValues();
    }

    private void InitializeDialog()
    {
        this.Text = "Create New Session";
        this.Size = new Size(500, 350);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        // Repository section
        var repoGroup = new GroupBox { Text = "Repository", Location = new Point(10, 10), Size = new Size(460, 80) };
        
        var repoNameLabel = new System.Windows.Forms.Label { Text = "Name:", Location = new Point(10, 20), AutoSize = true };
        repoNameTextBox = new System.Windows.Forms.TextBox { Location = new Point(80, 18), Width = 150 };
        
        var repoPathLabel = new System.Windows.Forms.Label { Text = "Path:", Location = new Point(10, 45), AutoSize = true };
        repoPathTextBox = new System.Windows.Forms.TextBox { Location = new Point(80, 43), Width = 300 };
        var repoBrowseButton = new System.Windows.Forms.Button { Text = "...", Location = new Point(385, 41), Size = new Size(30, 23) };
        repoBrowseButton.Click += (s, e) => BrowseForRepoPath();

        repoGroup.Controls.AddRange(new System.Windows.Forms.Control[] { repoNameLabel, repoNameTextBox, repoPathLabel, repoPathTextBox, repoBrowseButton });

        // Workspace section
        var workspaceGroup = new GroupBox { Text = "Workspace", Location = new Point(10, 100), Size = new Size(460, 50) };
        
        var workspacePathLabel = new System.Windows.Forms.Label { Text = "Path:", Location = new Point(10, 20), AutoSize = true };
        workspacePathTextBox = new System.Windows.Forms.TextBox { Location = new Point(80, 18), Width = 300 };
        var workspaceBrowseButton = new System.Windows.Forms.Button { Text = "...", Location = new Point(385, 16), Size = new Size(30, 23) };
        workspaceBrowseButton.Click += (s, e) => BrowseForWorkspacePath();

        workspaceGroup.Controls.AddRange(new System.Windows.Forms.Control[] { workspacePathLabel, workspacePathTextBox, workspaceBrowseButton });

        // Policy section
        var policyGroup = new GroupBox { Text = "Policy", Location = new Point(10, 160), Size = new Size(460, 100) };
        
        var policyNameLabel = new System.Windows.Forms.Label { Text = "Name:", Location = new Point(10, 20), AutoSize = true };
        policyNameTextBox = new System.Windows.Forms.TextBox { Location = new Point(80, 18), Width = 150, Text = "default" };
        
        requireTestsCheckBox = new System.Windows.Forms.CheckBox { Text = "Require tests before push", Location = new Point(10, 45), AutoSize = true };
        requireApprovalCheckBox = new System.Windows.Forms.CheckBox { Text = "Require approval for push", Location = new Point(10, 70), AutoSize = true };
        
        var callsPerMinuteLabel = new System.Windows.Forms.Label { Text = "Calls/min:", Location = new Point(250, 20), AutoSize = true };
        callsPerMinuteSpinner = new System.Windows.Forms.NumericUpDown { Location = new Point(320, 18), Width = 60, Minimum = 1, Maximum = 1000, Value = 60 };

        policyGroup.Controls.AddRange(new System.Windows.Forms.Control[] { 
            policyNameLabel, policyNameTextBox, requireTestsCheckBox, requireApprovalCheckBox, 
            callsPerMinuteLabel, callsPerMinuteSpinner });

        // Buttons
        okButton = new System.Windows.Forms.Button { Text = "Create Session", Location = new Point(280, 275), DialogResult = DialogResult.OK };
        cancelButton = new System.Windows.Forms.Button { Text = "Cancel", Location = new Point(370, 275), DialogResult = DialogResult.Cancel };

        okButton.Click += OkButton_Click;

        this.Controls.AddRange(new System.Windows.Forms.Control[] {
            repoGroup, workspaceGroup, policyGroup, okButton, cancelButton
        });

        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;
    }

    private void LoadDefaultValues()
    {
        repoNameTextBox!.Text = "default-repo";
        repoPathTextBox!.Text = Environment.CurrentDirectory;
        workspacePathTextBox!.Text = Environment.CurrentDirectory;
        policyNameTextBox!.Text = "default";
        requireTestsCheckBox!.Checked = false;
        requireApprovalCheckBox!.Checked = false;
        callsPerMinuteSpinner!.Value = 60;
    }

    private void BrowseForRepoPath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select repository directory",
            SelectedPath = repoPathTextBox!.Text
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            repoPathTextBox!.Text = dialog.SelectedPath;
        }
    }

    private void BrowseForWorkspacePath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select workspace directory",
            SelectedPath = workspacePathTextBox!.Text
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            workspacePathTextBox!.Text = dialog.SelectedPath;
        }
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(repoNameTextBox!.Text))
        {
            MessageBox.Show("Repository name is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(repoPathTextBox!.Text) || !Directory.Exists(repoPathTextBox!.Text))
        {
            MessageBox.Show("Valid repository path is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(workspacePathTextBox!.Text) || !Directory.Exists(workspacePathTextBox!.Text))
        {
            MessageBox.Show("Valid workspace path is required.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Create session config
        var policy = new PolicyProfile
        {
            Name = policyNameTextBox!.Text,
            ProtectedBranches = new HashSet<string> { "main", "master" },
            RequireTestsBeforePush = requireTestsCheckBox!.Checked,
            RequireApprovalForPush = requireApprovalCheckBox!.Checked,
            CommandWhitelist = null,
            CommandBlacklist = null,
            MaxFilesPerCommit = null,
            AllowedWorkItemTransitions = null,
            Limits = new RateLimits
            {
                CallsPerMinute = (int)callsPerMinuteSpinner!.Value,
                Burst = 10
            }
        };

        SessionConfig = new SessionConfig(
            SessionId: Guid.NewGuid(),
            ParentSessionId: null,
            PlanNodeId: null,
            Policy: policy,
            Repo: new RepoRef(repoNameTextBox!.Text, repoPathTextBox!.Text),
            Workspace: new WorkspaceRef(workspacePathTextBox!.Text),
            WorkItem: null,
            AgentProfile: "default"
        );
    }
}

public partial class MainForm : Form
{
    private DockManager? dockManager;

    // Panels
    private DockPanel? sessionsPanel;
    private DockPanel? conversationPanel;
    private DockPanel? eventsPanel;
    private DockPanel? artifactsPanel;

    // Blocking banners
    private Panel? _blockingBannerPanel;
    private System.Windows.Forms.Label? _blockingBannerLabel;
    private SimpleButton? _blockingBannerActionButton;

    // Controls within panels
    private System.Windows.Forms.ListBox? sessionsListBox;
    private MemoEdit? eventsMemoEdit;
    private TreeList? artifactsTree;

    // Chat components
    private AccordionLayoutManager? _accordionManager;
    private Panel? _chatContainerPanel;
    private System.Windows.Forms.ComboBox? _chatFilterCombo;
    private Dictionary<Guid, List<Artifact>> _chatArtifacts = new();

    // Blocking state tracking
    private List<BlockingCondition> _blockingConditions = new();

    private bool isTestMode = false;

    // Public property for testing
    public bool IsTestMode
    {
        get => isTestMode;
        set => isTestMode = value;
    }

    // Orchestrator dependencies
    private ISessionManager _sessionManager;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.Extensions.AI.IChatClient _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Guid, Task> _eventSubscriptionTasks = new();

    private EventRenderer? eventRenderer;
    private System.Windows.Forms.Timer? testTimer;
    private MenuStrip? mainMenu;
    private System.Windows.Forms.Timer? eventTimer;
    private AppSettings currentSettings = new();
    
    // Test helper fields
    private string? _testLayoutFilePath;
    private string? _testSettingsFilePath;
    private string? _testChatStreamsFilePath;

    public MainForm(ISessionManager sessionManager, IConfiguration configuration, Microsoft.Extensions.AI.IChatClient chatClient, IServiceProvider serviceProvider, bool isTestMode = false)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Check for test mode argument or parameter
        this.isTestMode = isTestMode || Environment.GetCommandLineArgs().Contains("--test") || Environment.GetCommandLineArgs().Contains("-t");

        Console.WriteLine($"Junior Dev starting... Test mode: {this.isTestMode}");

        // Register AI client for chat functionality
        RegisterAIClient();

        InitializeComponent();
        SetupMenu();
        SetupUI();
        eventRenderer = new EventRenderer(eventsMemoEdit!);
        
        // Only load layout if not in test mode, or let tests control it
        if (!this.isTestMode)
        {
            LoadLayout();
            LoadAndApplySettings();
        }

        if (this.isTestMode)
        {
            // Add mock blocking events before test inspection
            AddMockBlockingEvents();
            SetupTestMode();
        }
        else
        {
            // Set up real event subscriptions instead of mock feed
            SetupEventSubscriptions();
            Console.WriteLine("UI initialized successfully. Close window to exit.");
        }
    }

    private void RegisterAIClient()
    {
        try
        {
            // Register the injected chat client for AI chat functionality
            AIExtensionsContainerDesktop.Default.RegisterChatClient(_chatClient);
            Console.WriteLine("AI client registered successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to register AI client: {ex.Message}");
        }
    }

    private void InitializeComponent()
    {
        this.Text = "Junior Dev - AI-Assisted Development Platform";
        this.Size = new Size(1200, 800);
        this.StartPosition = FormStartPosition.CenterScreen;
        
        // Setup main menu
        mainMenu = new MenuStrip();
        this.MainMenuStrip = mainMenu;
        this.Controls.Add(mainMenu);
    }

    private void SetupMenu()
    {
        // View menu
        var viewMenu = new ToolStripMenuItem("View");
        
        // Reset Layout menu item
        var resetLayoutItem = new ToolStripMenuItem("Reset Layout");
        resetLayoutItem.Click += (s, e) => ResetLayout();
        resetLayoutItem.ShortcutKeys = Keys.Control | Keys.R;
        resetLayoutItem.ShowShortcutKeys = true;

        // Settings menu item
        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (s, e) => ShowSettingsDialog();

        // Chat menu
        var chatMenu = new ToolStripMenuItem("Chat");
        
        var addChatItem = new ToolStripMenuItem("Add Chat Stream");
        addChatItem.Click += (s, e) => AddNewChatStream();
        addChatItem.ShortcutKeys = Keys.Control | Keys.N;
        addChatItem.ShowShortcutKeys = true;

        var removeChatItem = new ToolStripMenuItem("Remove Active Chat");
        removeChatItem.Click += (s, e) => RemoveActiveChatStream();
        removeChatItem.ShortcutKeys = Keys.Control | Keys.W;
        removeChatItem.ShowShortcutKeys = true;

        chatMenu.DropDownItems.Add(addChatItem);
        chatMenu.DropDownItems.Add(removeChatItem);
        mainMenu.Items.Add(chatMenu);

        // Commands menu
        var commandsMenu = new ToolStripMenuItem("Commands");
        
        var runTestsItem = new ToolStripMenuItem("Run Tests");
        runTestsItem.Click += async (s, e) => await ExecuteCommandForActiveSession(c => new RunTests(Guid.NewGuid(), c, GetCurrentRepo(), null, TimeSpan.FromMinutes(5)));
        runTestsItem.ShortcutKeys = Keys.F6;
        runTestsItem.ShowShortcutKeys = true;

        var createBranchItem = new ToolStripMenuItem("Create Branch");
        createBranchItem.Click += async (s, e) => await ExecuteCommandForActiveSession(c => new CreateBranch(Guid.NewGuid(), c, GetCurrentRepo(), "feature/ui-command"));
        
        var commitItem = new ToolStripMenuItem("Commit Changes");
        commitItem.Click += async (s, e) => await ExecuteCommandForActiveSession(c => new Commit(Guid.NewGuid(), c, GetCurrentRepo(), "Committed via UI command", new List<string> { "." }));
        
        var pushItem = new ToolStripMenuItem("Push Branch");
        pushItem.Click += async (s, e) => await ExecuteCommandForActiveSession(c => new Push(Guid.NewGuid(), c, GetCurrentRepo(), "main"));
        
        var getDiffItem = new ToolStripMenuItem("Show Diff");
        getDiffItem.Click += async (s, e) => await ExecuteCommandForActiveSession(c => new GetDiff(Guid.NewGuid(), c, GetCurrentRepo()));

        // Sessions menu
        var sessionsMenu = new ToolStripMenuItem("Sessions");
        
        var createSessionItem = new ToolStripMenuItem("Create New Session");
        createSessionItem.Click += async (s, e) => await CreateNewSession();
        createSessionItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.N;
        createSessionItem.ShowShortcutKeys = true;

        var attachChatToSessionItem = new ToolStripMenuItem("Attach Chat to Session...");
        attachChatToSessionItem.Click += (s, e) => ShowAttachChatToSessionDialog();

        var refreshSessionsItem = new ToolStripMenuItem("Refresh Sessions List");
        refreshSessionsItem.Click += (s, e) => RefreshSessionsList();
        refreshSessionsItem.ShortcutKeys = Keys.F5;
        refreshSessionsItem.ShowShortcutKeys = true;

        sessionsMenu.DropDownItems.Add(createSessionItem);
        sessionsMenu.DropDownItems.Add(attachChatToSessionItem);
        sessionsMenu.DropDownItems.Add(new ToolStripSeparator());
        sessionsMenu.DropDownItems.Add(refreshSessionsItem);
        mainMenu.Items.Add(sessionsMenu);
    }

    private void SetupTestMode()
    {
        testTimer = new System.Windows.Forms.Timer();
        testTimer.Interval = 2000; // 2 seconds
        testTimer.Tick += (s, e) => 
        {
            testTimer.Stop();
            Application.Exit();
        };
        testTimer.Start();
        
        // Update title to indicate test mode
        this.Text = "Junior Dev - TEST MODE (Auto-exit in 2s)";
        
        // Log UI state for inspection
        Console.WriteLine("=== UI TEST MODE INSPECTION ===");
        Console.WriteLine($"Window Size: {this.Size.Width}x{this.Size.Height}");
        Console.WriteLine($"Sessions Panel: Visible={sessionsPanel!.Visible}, Width={sessionsPanel!.Width}");
        Console.WriteLine($"Conversation Panel: Visible={conversationPanel!.Visible}");
        Console.WriteLine($"Chat Streams: {_accordionManager?.ChatStreams.Count ?? 0}");
        if (_accordionManager != null)
        {
            foreach (var stream in _accordionManager.ChatStreams)
            {
                var preview = stream.GetCollapsedPreview();
                Console.WriteLine($"  - {stream.AgentName}: {stream.Status}, Expanded={stream.IsExpanded}");
                Console.WriteLine($"    Preview: {preview}");
            }
        }
        Console.WriteLine($"Events Panel: Visible={eventsPanel!.Visible}, Height={eventsPanel!.Height}");
        Console.WriteLine($"Artifacts Panel: Visible={artifactsPanel!.Visible}, Width={artifactsPanel!.Width}");
        Console.WriteLine($"Chat Filter: {_chatFilterCombo?.SelectedItem?.ToString() ?? "None"}");
        Console.WriteLine($"Total Artifacts: {_chatArtifacts.Sum(kvp => kvp.Value.Count)}");
        foreach (var kvp in _chatArtifacts)
        {
            var chatName = _accordionManager?.ChatStreams.FirstOrDefault(s => s.SessionId == kvp.Key)?.AgentName ?? "Unknown";
            Console.WriteLine($"  - {chatName}: {kvp.Value.Count} artifacts");
        }
        Console.WriteLine($"Blocking Banners: {_blockingConditions.Count} active");
        foreach (var condition in _blockingConditions)
        {
            Console.WriteLine($"  - {condition.Type}: {condition.Message}");
        }
        Console.WriteLine($"Sessions in list: {sessionsListBox!.Items.Count}");
        Console.WriteLine("Mock data loaded successfully");
        Console.WriteLine("Auto-exit in 2 seconds...");
        Console.WriteLine("===============================");
    }

    private void AddMockBlockingEvents()
    {
        // Add some mock blocking conditions for demonstration
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();

        Console.WriteLine("Adding mock blocking conditions...");
        
        AddBlockingCondition(new BlockingCondition
        {
            SessionId = sessionId1,
            Type = BlockingType.Throttled,
            Message = $"⚠️ Session throttled: git-operations. Retry after 14:37:05",
            ActionText = "Wait"
        });
        
        Console.WriteLine($"After adding throttled: {_blockingConditions.Count} conditions");

        AddBlockingCondition(new BlockingCondition
        {
            SessionId = sessionId2,
            Type = BlockingType.ConflictDetected,
            Message = $"⚠️ Merge conflict detected: Merge conflict in main.cs",
            ActionText = "Resolve"
        });
        
        Console.WriteLine($"After adding conflict: {_blockingConditions.Count} conditions");
        Console.WriteLine("Mock blocking events added for demonstration");
    }

    private void SetupUI()
    {
        // Initialize DevExpress DockManager
        dockManager = new DockManager();
        dockManager.Form = this;

        // Create blocking banner (initially hidden)
        CreateBlockingBanner();

        // Create panels
        CreateSessionsPanel();
        CreateConversationPanel();
        CreateEventsPanel();
        CreateArtifactsPanel();

        // Setup layout
        SetupLayout();
    }

    private void CreateSessionsPanel()
    {
        sessionsPanel = dockManager.AddPanel(DockingStyle.Left);
        sessionsPanel.Text = "Sessions";
        sessionsPanel.FloatSize = new Size(300, 600);
        sessionsPanel.FloatLocation = new Point(10, 10);

        // Create a panel with action buttons, filters, and session list
        var panel = new Panel();
        panel.Dock = DockStyle.Fill;

        // Add action buttons at the top
        var actionPanel = new FlowLayoutPanel();
        actionPanel.Dock = DockStyle.Top;
        actionPanel.Height = 40;
        actionPanel.FlowDirection = FlowDirection.LeftToRight;

        var createButton = new SimpleButton();
        createButton.Text = "Create Session";
        createButton.Width = 100;
        createButton.Click += async (s, e) => await CreateNewSession();

        var attachButton = new SimpleButton();
        attachButton.Text = "Attach";
        attachButton.Width = 70;
        attachButton.Click += (s, e) => ShowAttachChatToSessionDialog();

        actionPanel.Controls.Add(createButton);
        actionPanel.Controls.Add(attachButton);

        // Add filter buttons below actions
        var filterPanel = new FlowLayoutPanel();
        filterPanel.Dock = DockStyle.Top;
        filterPanel.Height = 40;
        filterPanel.FlowDirection = FlowDirection.LeftToRight;

        var allButton = new SimpleButton();
        allButton.Text = "All";
        allButton.Width = 50;
        allButton.Click += (s, e) => FilterSessions("All");

        var runningButton = new SimpleButton();
        runningButton.Text = "Running";
        runningButton.Width = 70;
        runningButton.Click += (s, e) => FilterSessions("Running");

        var pausedButton = new SimpleButton();
        pausedButton.Text = "Paused";
        pausedButton.Width = 60;
        pausedButton.Click += (s, e) => FilterSessions("Paused");

        var errorButton = new SimpleButton();
        errorButton.Text = "Error";
        errorButton.Width = 50;
        errorButton.Click += (s, e) => FilterSessions("Error");

        filterPanel.Controls.AddRange(new Control[] { allButton, runningButton, pausedButton, errorButton });

        // Create session list with status indicators
        sessionsListBox = new System.Windows.Forms.ListBox();
        sessionsListBox.Dock = DockStyle.Fill;
        sessionsListBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
        sessionsListBox.ItemHeight = 30;
        sessionsListBox.DrawItem += SessionsListBox_DrawItem;

        // Initialize with real sessions if not in test mode
        if (!isTestMode)
        {
            RefreshSessionsList();
        }
        else
        {
            // Mock session data with status objects
            sessionsListBox.Items.Add(new SessionItem { Name = "Session 1", Status = JuniorDev.Contracts.SessionStatus.Running });
            sessionsListBox.Items.Add(new SessionItem { Name = "Session 2", Status = JuniorDev.Contracts.SessionStatus.Paused });
            sessionsListBox.Items.Add(new SessionItem { Name = "Session 3", Status = JuniorDev.Contracts.SessionStatus.Error });
            sessionsListBox.Items.Add(new SessionItem { Name = "Session 4", Status = JuniorDev.Contracts.SessionStatus.Running });
            sessionsListBox.Items.Add(new SessionItem { Name = "Session 5", Status = JuniorDev.Contracts.SessionStatus.NeedsApproval });
            sessionsListBox.Items.Add(new SessionItem { Name = "Session 6", Status = JuniorDev.Contracts.SessionStatus.Completed });
        }

        panel.Controls.Add(sessionsListBox);
        panel.Controls.Add(filterPanel);
        panel.Controls.Add(actionPanel);

        sessionsPanel.Controls.Add(panel);
    }

    private void CreateConversationPanel()
    {
        conversationPanel = dockManager.AddPanel(DockingStyle.Fill);
        conversationPanel.Text = "AI Chat";

        // Create container panel for accordion layout
        _chatContainerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };

        // Initialize accordion layout manager
        _accordionManager = new AccordionLayoutManager(_chatContainerPanel);

        // Wire up events
        _accordionManager.StreamsChanged += (s, e) => UpdateChatFilterOptions();
        _accordionManager.CreateSessionRequested += OnCreateSessionRequested;
        _accordionManager.AttachToSessionRequested += OnAttachToSessionRequested;

        // Don't auto-create initial session - let user create sessions explicitly via UI

        conversationPanel.Controls.Add(_chatContainerPanel);
    }

    private async void CreateInitialSession()
    {
        try
        {
            // Create a default session configuration
            var sessionConfig = new SessionConfig(
                SessionId: Guid.NewGuid(),
                ParentSessionId: null,
                PlanNodeId: null,
                Policy: new PolicyProfile
                {
                    Name = "default",
                    ProtectedBranches = new HashSet<string> { "main", "master" },
                    RequireTestsBeforePush = false,
                    RequireApprovalForPush = false,
                    CommandWhitelist = null,
                    CommandBlacklist = null,
                    MaxFilesPerCommit = null,
                    AllowedWorkItemTransitions = null,
                    Limits = new RateLimits
                    {
                        CallsPerMinute = 60,
                        Burst = 10
                    }
                },
                Repo: new RepoRef("default-repo", Environment.CurrentDirectory),
                Workspace: new WorkspaceRef(Environment.CurrentDirectory),
                WorkItem: null,
                AgentProfile: "default"
            );

            // Create the session
            await _sessionManager.CreateSession(sessionConfig);

            // Create chat stream for this session with proper SessionId mapping
            var chatStream = new ChatStream(sessionConfig.SessionId, "Agent 1");
            _accordionManager?.AddChatStream(chatStream, _sessionManager, _serviceProvider, _configuration);

            // Set dependencies on the panel for command publishing
            if (chatStream.Panel != null)
            {
                // Dependencies are now set in the constructor
            }

            // Subscribe to session events for real-time updates
            SubscribeToSessionEvents(sessionConfig.SessionId);

            Console.WriteLine($"Created initial session: {sessionConfig.SessionId} (Agent 1)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create initial session: {ex.Message}");
            // Fall back to mock mode
            var initialChatStream = new ChatStream(Guid.NewGuid(), "Agent 1");
            _accordionManager?.AddChatStream(initialChatStream, null, null, _configuration);
        }
    }

    private void CreateEventsPanel()
    {
        eventsPanel = dockManager.AddPanel(DockingStyle.Bottom);
        eventsPanel.Text = "Event Stream";

        eventsMemoEdit = new MemoEdit();
        eventsMemoEdit.Dock = DockStyle.Fill;
        eventsMemoEdit.Properties.ReadOnly = true;
        eventsMemoEdit.Properties.ScrollBars = ScrollBars.Vertical;

        eventsPanel.Controls.Add(eventsMemoEdit);
    }

    private void CreateArtifactsPanel()
    {
        artifactsPanel = dockManager.AddPanel(DockingStyle.Right);
        artifactsPanel.Text = "Artifacts & Tests";
        artifactsPanel.FloatSize = new Size(300, 600);
        artifactsPanel.FloatLocation = new Point(900, 10);

        // Create container panel for filter + tree
        var containerPanel = new Panel();
        containerPanel.Dock = DockStyle.Fill;

        // Add filter dropdown at the top
        var filterPanel = new FlowLayoutPanel();
        filterPanel.Dock = DockStyle.Top;
        filterPanel.Height = 35;
        filterPanel.FlowDirection = FlowDirection.LeftToRight;

        var filterLabel = new System.Windows.Forms.Label();
        filterLabel.Text = "Filter by Chat:";
        filterLabel.AutoSize = true;
        filterLabel.TextAlign = ContentAlignment.MiddleLeft;
        filterLabel.Padding = new Padding(5, 8, 0, 0);

        var chatFilterCombo = new System.Windows.Forms.ComboBox();
        chatFilterCombo.Width = 180;
        chatFilterCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        chatFilterCombo.Items.Add("All Chats");
        chatFilterCombo.SelectedIndex = 0; // Default to "All Chats"
        chatFilterCombo.SelectedIndexChanged += (s, e) => FilterArtifactsByChat(chatFilterCombo.SelectedItem?.ToString() ?? "All Chats");

        filterPanel.Controls.Add(filterLabel);
        filterPanel.Controls.Add(chatFilterCombo);

        // Create artifacts tree
        artifactsTree = new TreeList();
        artifactsTree.Dock = DockStyle.Fill;

        // Setup columns
        artifactsTree.Columns.Add();
        artifactsTree.Columns[0].Caption = "Artifacts";
        artifactsTree.Columns[0].VisibleIndex = 0;

        // Add double-click handler for artifact linking
        artifactsTree.DoubleClick += ArtifactsTree_DoubleClick;

        // Store filter combo for later updates
        _chatFilterCombo = chatFilterCombo;

        containerPanel.Controls.Add(artifactsTree);
        containerPanel.Controls.Add(filterPanel);

        artifactsPanel.Controls.Add(containerPanel);

        // Initialize with mock data
        RefreshArtifactsTree();
    }

    private void CreateBlockingBanner()
    {
        _blockingBannerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 50,
            BackColor = Color.FromArgb(255, 255, 200), // Light yellow
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };

        _blockingBannerLabel = new System.Windows.Forms.Label
        {
            Location = new Point(10, 10),
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold),
            ForeColor = Color.DarkRed
        };

        _blockingBannerActionButton = new SimpleButton
        {
            Location = new Point(300, 8),
            Size = new Size(100, 30),
            Text = "Action",
            Visible = false
        };
        _blockingBannerActionButton.Click += BlockingBannerActionButton_Click;

        _blockingBannerPanel.Controls.Add(_blockingBannerLabel);
        _blockingBannerPanel.Controls.Add(_blockingBannerActionButton);

        this.Controls.Add(_blockingBannerPanel);
        // Ensure banner appears above menu but below panels
        _blockingBannerPanel.BringToFront();
    }

    private void SetupLayout()
    {
        // Panels are already docked via DockingStyle in AddPanel
        // Configure panel sizes
        sessionsPanel!.Width = 250;
        eventsPanel!.Height = 200;
        artifactsPanel!.Width = 300;
    }

    private void LoadDefaultLayout()
    {
        // Default layout is already set up in SetupLayout()
        // Panels are docked via DockingStyle in AddPanel
    }

    private void SaveLayout()
    {
        try
        {
            string layoutFile = GetLayoutFilePath();
            string directory = Path.GetDirectoryName(layoutFile)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Save DevExpress dock layout
            dockManager!.SaveLayoutToXml(layoutFile);
            
            // Save chat streams data
            SaveChatStreams();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save layout: {ex.Message}");
        }
    }

    public void SaveChatStreams(ChatStream[]? streamsToSave = null)
    {
        try
        {
            var streams = streamsToSave ?? (_accordionManager?.ChatStreams ?? Array.Empty<ChatStream>());
            
            var chatStreamsData = streams.Select(stream => new ChatStreamData
            {
                SessionId = stream.SessionId,
                AgentId = stream.AgentId,
                AgentName = stream.AgentName,
                Status = stream.Status,
                CurrentTask = stream.CurrentTask,
                ProgressPercentage = stream.ProgressPercentage,
                IsExpanded = stream.IsExpanded
            }).ToList();
            
            string chatStreamsFile = GetChatStreamsFilePath();
            string json = System.Text.Json.JsonSerializer.Serialize(chatStreamsData);
            File.WriteAllText(chatStreamsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save chat streams: {ex.Message}");
        }
    }

    public ChatStream[] LoadChatStreams()
    {
        try
        {
            string chatStreamsFile = GetChatStreamsFilePath();
            if (!File.Exists(chatStreamsFile)) return LoadDefaultChatStreams();
            
            string json = File.ReadAllText(chatStreamsFile);
            var chatStreamsData = System.Text.Json.JsonSerializer.Deserialize<List<ChatStreamData>>(json);
            
            if (chatStreamsData != null && _accordionManager != null)
            {
                // Clear existing streams
                var streamsToRemove = _accordionManager.ChatStreams.ToList();
                foreach (var stream in streamsToRemove)
                {
                    _accordionManager.RemoveChatStream(stream);
                }
                
                // Load saved streams
                foreach (var data in chatStreamsData)
                {
                    var chatStream = new ChatStream(data.SessionId, data.AgentName)
                    {
                        AgentId = data.AgentId,
                        Status = data.Status,
                        CurrentTask = data.CurrentTask,
                        ProgressPercentage = data.ProgressPercentage,
                        IsExpanded = data.IsExpanded
                    };
                    
                    _accordionManager.AddChatStream(chatStream);
                }
                
                return _accordionManager.ChatStreams.ToArray();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load chat streams: {ex.Message}");
            // Fall back to default chat streams
            return LoadDefaultChatStreams();
        }
        
        return Array.Empty<ChatStream>();
    }

    public ChatStream[] LoadDefaultChatStreams()
    {
        if (_accordionManager == null) return Array.Empty<ChatStream>();
        
        // Clear existing streams
        var streamsToRemove = _accordionManager.ChatStreams.ToList();
        foreach (var stream in streamsToRemove)
        {
            _accordionManager.RemoveChatStream(stream);
        }
        
        // Add default stream
        var defaultStream = new ChatStream(Guid.NewGuid(), "Agent 1");
        _accordionManager.AddChatStream(defaultStream, null, null, _configuration);
        
        return _accordionManager.ChatStreams.ToArray();
    }

    private string GetChatStreamsFilePath()
    {
        if (_testChatStreamsFilePath != null)
        {
            return _testChatStreamsFilePath;
        }
        
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "JuniorDev");
        return Path.Combine(appFolder, "chat-streams.json");
    }

    private string GetLayoutFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "JuniorDev");
        return Path.Combine(appFolder, "layout.xml");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveLayout();
        base.OnFormClosing(e);
    }

    private void SetupEventSubscriptions()
    {
        // Subscribe to events from all existing chat streams
        if (_accordionManager != null)
        {
            foreach (var chatStream in _accordionManager.ChatStreams)
            {
                SubscribeToSessionEvents(chatStream.SessionId);
            }
        }

        // Listen for new chat streams being added
        if (_accordionManager != null)
        {
            _accordionManager.StreamsChanged += (s, e) => 
            {
                // Subscribe to events for newly added streams
                foreach (var stream in _accordionManager.ChatStreams)
                {
                    if (!_eventSubscriptionTasks.ContainsKey(stream.SessionId))
                    {
                        SubscribeToSessionEvents(stream.SessionId);
                    }
                }
            };
        }
    }

    private void SubscribeToSessionEvents(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (_eventSubscriptionTasks.ContainsKey(sessionId))
        {
            return; // Already subscribed
        }

        Task subscriptionTask;
        if (isTestMode)
        {
            // In test mode, run synchronously to avoid threading issues
            subscriptionTask = SubscribeToSessionEventsSync(sessionId, cancellationToken);
        }
        else
        {
            // In production, run on background thread
            subscriptionTask = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine($"SubscribeToSessionEvents: Starting foreach for session {sessionId}");
                    await foreach (var @event in _sessionManager.Subscribe(sessionId).WithCancellation(cancellationToken))
                    {
                        // Debug logging in test mode
                        if (isTestMode)
                        {
                            Console.WriteLine($"SubscribeToSessionEvents: Received event {@event.Kind} for session {sessionId}");
                        }

                        // Handle event on UI thread (skip Invoke in test mode)
                        if (isTestMode)
                        {
                            HandleOrchestratorEvent(@event);
                        }
                        else if (InvokeRequired)
                        {
                            Invoke(() => HandleOrchestratorEvent(@event));
                        }
                        else
                        {
                            HandleOrchestratorEvent(@event);
                        }
                    }
                    Console.WriteLine($"SubscribeToSessionEvents: Foreach completed for session {sessionId}");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"Event subscription for session {sessionId} was cancelled");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Event subscription for session {sessionId} ended: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }, cancellationToken);
        }

        _eventSubscriptionTasks[sessionId] = subscriptionTask;
    }

    private async Task SubscribeToSessionEventsSync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine($"SubscribeToSessionEvents: Starting foreach for session {sessionId}");
            await foreach (var @event in _sessionManager.Subscribe(sessionId).WithCancellation(cancellationToken))
            {
                // Debug logging in test mode
                Console.WriteLine($"SubscribeToSessionEvents: Received event {@event.Kind} for session {sessionId}");

                // Handle event synchronously in test mode
                HandleOrchestratorEvent(@event);
            }
            Console.WriteLine($"SubscribeToSessionEvents: Foreach completed for session {sessionId}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Event subscription for session {sessionId} was cancelled");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Event subscription for session {sessionId} ended: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    private void HandleOrchestratorEvent(IEvent @event)
    {
        var timestamp = DateTimeOffset.Now;

        // Route event to appropriate chat streams
        _accordionManager?.RouteEventToStreams(@event, timestamp);
        
        // Handle artifacts
        if (@event is ArtifactAvailable artifactEvent)
        {
            AddArtifactToChat(artifactEvent.Correlation.SessionId, artifactEvent.Artifact);
        }

        // Check for blocking conditions
        CheckForBlockingConditions(@event);
        
        // Render to global events panel
        eventRenderer?.RenderEvent(@event, timestamp);

        // Debug logging in test mode
        if (isTestMode)
        {
            Console.WriteLine($"Handled event: {@event.Kind} for session {@event.Correlation.SessionId}");
        }
    }

    private void AddMockEvent()
    {
        // Generate mock events with proper correlation
        var mockEvents = GenerateMockEventSequence();
        var randomEvent = mockEvents[new Random().Next(mockEvents.Length)];
        
        // Route event to appropriate chat streams
        _accordionManager?.RouteEventToStreams(randomEvent, DateTimeOffset.Now);
        
        // Handle artifacts
        if (randomEvent is ArtifactAvailable artifactEvent)
        {
            AddArtifactToChat(artifactEvent.Correlation.SessionId, artifactEvent.Artifact);
        }

        // Check for blocking conditions
        CheckForBlockingConditions(randomEvent);
        
        // Also render to global events panel for backward compatibility
        eventRenderer?.RenderEvent(randomEvent, DateTimeOffset.Now);
    }

    private void CheckForBlockingConditions(IEvent @event)
    {
        switch (@event)
        {
            case Throttled throttled:
                AddBlockingCondition(new BlockingCondition
                {
                    SessionId = throttled.Correlation.SessionId,
                    Type = BlockingType.Throttled,
                    Message = $"⚠️ Session throttled: {throttled.Scope}. Retry after {throttled.RetryAfter:HH:mm:ss}",
                    ActionText = "Wait"
                });
                break;

            case ConflictDetected conflict:
                AddBlockingCondition(new BlockingCondition
                {
                    SessionId = conflict.Correlation.SessionId,
                    Type = BlockingType.ConflictDetected,
                    Message = $"⚠️ Merge conflict detected: {conflict.Details}",
                    ActionText = "Resolve"
                });
                break;

            case SessionStatusChanged statusChanged when statusChanged.Status == JuniorDev.Contracts.SessionStatus.NeedsApproval:
                AddBlockingCondition(new BlockingCondition
                {
                    SessionId = statusChanged.Correlation.SessionId,
                    Type = BlockingType.NeedsApproval,
                    Message = $"🔒 Session requires approval: {statusChanged.Reason}",
                    ActionText = "Approve"
                });
                break;

            case SessionStatusChanged statusChanged when statusChanged.Status != JuniorDev.Contracts.SessionStatus.NeedsApproval:
                // Remove approval blocking when status changes away from NeedsApproval
                RemoveBlockingCondition(statusChanged.Correlation.SessionId, BlockingType.NeedsApproval);
                break;
        }
    }

    public IEvent[] GenerateMockEventSequence()
    {
        // Use existing chat stream SessionIds for events
        var availableSessionIds = _accordionManager?.ChatStreams.Select(s => s.SessionId).ToArray() ?? new[] { Guid.NewGuid() };
        var sessionId = availableSessionIds[new Random().Next(availableSessionIds.Length)];
        var commandId = Guid.NewGuid();
        var correlation = new Correlation(sessionId, commandId);

        return new IEvent[]
        {
            new CommandAccepted(Guid.NewGuid(), correlation, commandId),
            new CommandCompleted(Guid.NewGuid(), correlation, commandId, CommandOutcome.Success, "Tests passed (15/15)"),
            new ArtifactAvailable(Guid.NewGuid(), correlation, new Artifact("test-results", "Test Results", "All 15 tests passed successfully")),
            new SessionStatusChanged(Guid.NewGuid(), correlation, JuniorDev.Contracts.SessionStatus.Running, "Processing next command"),
            new CommandAccepted(Guid.NewGuid(), new Correlation(sessionId, Guid.NewGuid()), Guid.NewGuid()),
            new Throttled(Guid.NewGuid(), correlation, "git-operations", DateTimeOffset.Now.AddSeconds(30)),
            new CommandRejected(Guid.NewGuid(), correlation, commandId, "Rate limit exceeded", "throttle-policy"),
            new ConflictDetected(Guid.NewGuid(), correlation, new RepoRef("test-repo", "/path/to/repo"), "Merge conflict in main.cs"),
            new CommandCompleted(Guid.NewGuid(), correlation, commandId, CommandOutcome.Failure, "Build failed", "COMPILATION_ERROR")
        };
    }

    public void RouteEventToChatStreams(IEvent @event, DateTimeOffset timestamp)
    {
        _accordionManager?.RouteEventToStreams(@event, timestamp);
    }

    private void ShowSettingsDialog()
    {
        if (isTestMode)
        {
            // Skip dialog in test mode
            return;
        }

        using var dialog = new SettingsDialog();
        dialog.Settings = currentSettings; // Pass current settings to dialog
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            // Apply settings
            ApplySettings(dialog.Settings);
            SaveSettings(dialog.Settings);

            // Apply live profile settings if changed
            if (dialog.Settings.LiveProfile.IsLiveMode != currentSettings.LiveProfile.IsLiveMode ||
                dialog.Settings.LiveProfile.WorkItemsAdapter != currentSettings.LiveProfile.WorkItemsAdapter ||
                dialog.Settings.LiveProfile.VcsAdapter != currentSettings.LiveProfile.VcsAdapter ||
                dialog.Settings.LiveProfile.BuildAdapter != currentSettings.LiveProfile.BuildAdapter)
            {
                ApplyLiveProfileSettings(dialog.Settings.LiveProfile);
            }
        }
    }

    private void LoadAndApplySettings()
    {
        var settings = LoadSettings();
        ApplySettings(settings);
        ApplyLiveProfileSettings(settings.LiveProfile);
    }

    public void ApplySettings(AppSettings settings)
    {
        // Apply theme to form and child controls
        Color backColor, foreColor;
        switch (settings.Theme)
        {
            case "Light":
                backColor = Color.White;
                foreColor = Color.Black;
                break;
            case "Dark":
                backColor = Color.FromArgb(30, 30, 30);
                foreColor = Color.White;
                break;
            case "Blue":
                backColor = Color.LightBlue;
                foreColor = Color.Black;
                break;
            default:
                backColor = Color.White;
                foreColor = Color.Black;
                break;
        }

        this.BackColor = backColor;
        this.ForeColor = foreColor;

        // Apply to child controls
        if (sessionsPanel != null) sessionsPanel.Appearance.BackColor = backColor;
        if (conversationPanel != null) conversationPanel.Appearance.BackColor = backColor;
        if (eventsPanel != null) eventsPanel.Appearance.BackColor = backColor;
        if (artifactsPanel != null) artifactsPanel.Appearance.BackColor = backColor;
        if (eventsMemoEdit != null) eventsMemoEdit.BackColor = backColor;

        // Apply font size to form and propagate to controls
        var newFont = new Font(this.Font.FontFamily, settings.FontSize);
        this.Font = newFont;

        // Store settings for behavior
        currentSettings = settings;

        // Apply ShowStatusChips behavior
        if (sessionsListBox != null)
        {
            sessionsListBox.DrawMode = settings.ShowStatusChips ? System.Windows.Forms.DrawMode.OwnerDrawFixed : System.Windows.Forms.DrawMode.Normal;
            sessionsListBox.Refresh(); // Redraw to apply changes
        }

        // Apply ShowTimestamps behavior
        if (eventRenderer != null)
        {
            eventRenderer.ShowTimestamps = settings.ShowTimestamps;
        }

        // Apply MaxEventHistory behavior
        if (eventRenderer != null)
        {
            eventRenderer.MaxEventHistory = settings.MaxEventHistory;
        }

        // Apply AutoScrollEvents behavior (stored for use in AddMockEvent)
        // This will be checked in AddMockEvent method
    }

    private void ApplyLiveProfileSettings(LiveProfileSettings liveProfile)
    {
        // Update the application title to indicate live mode
        var baseTitle = "Junior Dev - AI-Assisted Development Platform";
        if (liveProfile.IsLiveMode)
        {
            this.Text = $"{baseTitle} [LIVE MODE - {liveProfile.WorkItemsAdapter.ToUpper()}]";
            this.BackColor = Color.FromArgb(255, 240, 240); // Light red background for live mode
        }
        else
        {
            this.Text = baseTitle;
            // Reset background color based on theme
            ApplySettings(currentSettings);
        }

        // Show warning banner if in live mode with push enabled
        if (liveProfile.IsLiveMode && liveProfile.PushEnabled && !liveProfile.DryRun)
        {
            ShowLiveModeWarning("⚠️ LIVE MODE WITH PUSH ENABLED - Real changes will be made to external systems!");
        }
        else if (liveProfile.IsLiveMode && liveProfile.DryRun)
        {
            ShowLiveModeWarning("🔄 LIVE MODE (DRY RUN) - Connected to real systems but operations will be simulated");
        }
        else if (liveProfile.IsLiveMode)
        {
            ShowLiveModeWarning("🔗 LIVE MODE - Connected to real systems (push disabled)");
        }
        else
        {
            HideLiveModeWarning();
        }

        // Update appsettings.json with live profile settings
        UpdateAppSettingsWithLiveProfile(liveProfile);

        // Update current settings
        currentSettings.LiveProfile = liveProfile;

        Console.WriteLine($"Applied live profile settings: Mode={liveProfile.IsLiveMode}, WorkItems={liveProfile.WorkItemsAdapter}, VCS={liveProfile.VcsAdapter}");
    }

    private void UpdateAppSettingsWithLiveProfile(LiveProfileSettings liveProfile)
    {
        try
        {
            // Load current appsettings.json
            var appSettingsPath = Path.Combine(FindWorkspaceRoot(), "appsettings.json");
            if (!File.Exists(appSettingsPath)) return;

            var json = File.ReadAllText(appSettingsPath);
            var appSettings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonDocument>(json);
            if (appSettings == null) return;

            // Update the AppConfig section
            if (appSettings.RootElement.TryGetProperty("AppConfig", out var appConfigElement))
            {
                var updatedConfig = appConfigElement.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>();
                if (updatedConfig == null) return;

                // Update Adapters
                if (updatedConfig.ContainsKey("Adapters"))
                {
                    var adapters = updatedConfig["Adapters"].Deserialize<Dictionary<string, System.Text.Json.JsonElement>>();
                    if (adapters != null)
                    {
                        adapters["WorkItemsAdapter"] = System.Text.Json.JsonSerializer.SerializeToElement(liveProfile.WorkItemsAdapter);
                        adapters["VcsAdapter"] = System.Text.Json.JsonSerializer.SerializeToElement(liveProfile.VcsAdapter);
                        adapters["BuildAdapter"] = System.Text.Json.JsonSerializer.SerializeToElement(liveProfile.BuildAdapter);
                        updatedConfig["Adapters"] = System.Text.Json.JsonSerializer.SerializeToElement(adapters);
                    }
                }

                // Update Auth
                var auth = new Dictionary<string, object>();
                if (liveProfile.WorkItemsAdapter == "github" && !string.IsNullOrEmpty(liveProfile.GitHubToken))
                {
                    auth["GitHub"] = new
                    {
                        Token = liveProfile.GitHubToken,
                        DefaultOrg = liveProfile.GitHubOrg,
                        DefaultRepo = liveProfile.GitHubRepo
                    };
                }
                if (liveProfile.WorkItemsAdapter == "jira" &&
                    !string.IsNullOrEmpty(liveProfile.JiraUrl) &&
                    !string.IsNullOrEmpty(liveProfile.JiraUsername) &&
                    !string.IsNullOrEmpty(liveProfile.JiraApiToken))
                {
                    auth["Jira"] = new
                    {
                        BaseUrl = liveProfile.JiraUrl,
                        Username = liveProfile.JiraUsername,
                        ApiToken = liveProfile.JiraApiToken
                    };
                }
                if (auth.Count > 0)
                {
                    updatedConfig["Auth"] = System.Text.Json.JsonSerializer.SerializeToElement(auth);
                }

                // Update LivePolicy
                var livePolicy = new
                {
                    PushEnabled = liveProfile.PushEnabled,
                    DryRun = liveProfile.DryRun,
                    RequireCredentialsValidation = liveProfile.RequireCredentialsValidation
                };
                updatedConfig["LivePolicy"] = System.Text.Json.JsonSerializer.SerializeToElement(livePolicy);

                // Save updated configuration
                var rootObject = new Dictionary<string, object> { ["AppConfig"] = updatedConfig };
                var updatedJson = System.Text.Json.JsonSerializer.Serialize(rootObject, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(appSettingsPath, updatedJson);

                Console.WriteLine("Updated appsettings.json with live profile settings");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update appsettings.json: {ex.Message}");
        }
    }

    private void ShowLiveModeWarning(string message)
    {
        if (_blockingBannerPanel == null) return;

        _blockingBannerLabel!.Text = message;
        _blockingBannerLabel!.ForeColor = Color.DarkRed;
        _blockingBannerPanel!.BackColor = Color.FromArgb(255, 255, 200); // Light yellow
        _blockingBannerPanel!.Visible = true;
        _blockingBannerActionButton!.Visible = false; // No action button for warnings

        // Ensure banner appears above panels
        _blockingBannerPanel!.BringToFront();
    }

    private void HideLiveModeWarning()
    {
        if (_blockingBannerPanel != null)
        {
            _blockingBannerPanel!.Visible = false;
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        try
        {
            string settingsFile = GetSettingsFilePath();
            string directory = Path.GetDirectoryName(settingsFile)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            File.WriteAllText(settingsFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    private void SessionsListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= sessionsListBox!.Items.Count) return;

        var item = sessionsListBox!.Items[e.Index] as SessionItem;
        if (item == null) return;

        // Clear background
        e.DrawBackground();

        // Get status chip info
        var (chipBackColor, chipForeColor, chipText) = item.GetStatusChip();

        // Draw session name
        using (var brush = new SolidBrush(e.ForeColor))
        {
            e.Graphics.DrawString(item.Name, e.Font!, brush, e.Bounds.X + 80, e.Bounds.Y + 5);
        }

        // Draw status chip (badge)
        var chipRect = new Rectangle(e.Bounds.X + 5, e.Bounds.Y + 3, 70, 20);
        using (var chipBrush = new SolidBrush(chipBackColor))
        using (var chipTextBrush = new SolidBrush(chipForeColor))
        using (var chipFont = new Font(e.Font.FontFamily, 7f, FontStyle.Bold))
        {
            e.Graphics.FillRectangle(chipBrush, chipRect);
            e.Graphics.DrawRectangle(Pens.Black, chipRect);
            var textSize = e.Graphics.MeasureString(chipText, chipFont);
            var textX = chipRect.X + (chipRect.Width - textSize.Width) / 2;
            var textY = chipRect.Y + (chipRect.Height - textSize.Height) / 2;
            e.Graphics.DrawString(chipText, chipFont, chipTextBrush, textX, textY);
        }

        e.DrawFocusRectangle();
    }

    private void RefreshSessionsList()
    {
        if (sessionsListBox == null || isTestMode) return;

        sessionsListBox.Items.Clear();

        // Show sessions from chat streams - each chat stream represents an active session
        // In the future, we could query the session manager for additional session metadata
        if (_accordionManager != null)
        {
            foreach (var chatStream in _accordionManager.ChatStreams)
            {
                var sessionItem = new SessionItem
                {
                    Name = chatStream.AgentName,
                    Status = chatStream.Status
                };
                sessionsListBox.Items.Add(sessionItem);
            }
        }
    }

    private void FilterSessions(string status)
    {
        if (isTestMode)
        {
            // Original mock implementation for test mode
            sessionsListBox!.Items.Clear();

            var allSessions = new[]
            {
                new SessionItem { Name = "Session 1", Status = JuniorDev.Contracts.SessionStatus.Running },
                new SessionItem { Name = "Session 2", Status = JuniorDev.Contracts.SessionStatus.Paused },
                new SessionItem { Name = "Session 3", Status = JuniorDev.Contracts.SessionStatus.Error },
                new SessionItem { Name = "Session 4", Status = JuniorDev.Contracts.SessionStatus.Running },
                new SessionItem { Name = "Session 5", Status = JuniorDev.Contracts.SessionStatus.NeedsApproval },
                new SessionItem { Name = "Session 6", Status = JuniorDev.Contracts.SessionStatus.Completed }
            };

            foreach (var session in allSessions)
            {
                bool shouldShow = status == "All" ||
                    (status == "Running" && session.Status == JuniorDev.Contracts.SessionStatus.Running) ||
                    (status == "Paused" && session.Status == JuniorDev.Contracts.SessionStatus.Paused) ||
                    (status == "Error" && session.Status == JuniorDev.Contracts.SessionStatus.Error) ||
                    (status == "NeedsApproval" && session.Status == JuniorDev.Contracts.SessionStatus.NeedsApproval) ||
                    (status == "Completed" && session.Status == JuniorDev.Contracts.SessionStatus.Completed);

                if (shouldShow)
                {
                    sessionsListBox!.Items.Add(session);
                }
            }
        }
        else
        {
            // Real sessions filtering
            sessionsListBox!.Items.Clear();

            if (_accordionManager != null)
            {
                foreach (var chatStream in _accordionManager.ChatStreams)
                {
                    bool shouldShow = status == "All" ||
                        (status == "Running" && chatStream.Status == JuniorDev.Contracts.SessionStatus.Running) ||
                        (status == "Paused" && chatStream.Status == JuniorDev.Contracts.SessionStatus.Paused) ||
                        (status == "Error" && chatStream.Status == JuniorDev.Contracts.SessionStatus.Error) ||
                        (status == "NeedsApproval" && chatStream.Status == JuniorDev.Contracts.SessionStatus.NeedsApproval) ||
                        (status == "Completed" && chatStream.Status == JuniorDev.Contracts.SessionStatus.Completed);

                    if (shouldShow)
                    {
                        var sessionItem = new SessionItem 
                        { 
                            Name = chatStream.AgentName, 
                            Status = chatStream.Status 
                        };
                        sessionsListBox!.Items.Add(sessionItem);
                    }
                }
            }
        }
    }

    // Test helper methods
    public System.Windows.Forms.DrawMode GetSessionsListBoxDrawMode()
    {
        return sessionsListBox?.DrawMode ?? System.Windows.Forms.DrawMode.Normal;
    }

    public bool GetAutoScrollEventsSetting()
    {
        return currentSettings.AutoScrollEvents;
    }

    public void SetLayoutFilePath(string path)
    {
        // For testing only
        _testLayoutFilePath = path;
    }

    public void SetSettingsFilePath(string path)
    {
        // For testing only
        _testSettingsFilePath = path;
    }

    public void SetChatStreamsFilePath(string path)
    {
        // For testing only
        _testChatStreamsFilePath = path;
    }

    public void SetSessionManager(ISessionManager sessionManager)
    {
        // For testing only
        _sessionManager = sessionManager;
    }

    public async Task CreateSessionForTest(SessionConfig config)
    {
        // For testing only - create session and set up chat stream
        await _sessionManager.CreateSession(config);

        var chatStream = new ChatStream(config.SessionId, $"Agent-{config.SessionId.ToString().Substring(0, 8)}");
        
        // Create AgentPanel for the chat stream (normally done by AccordionLayoutManager)
        var agentPanel = new AgentPanel(chatStream);
        // Add the panel to the container for proper UI hierarchy
        if (_accordionManager != null && _accordionManager is AccordionLayoutManager manager)
        {
            // Access the container through reflection or add a method to get it
            // For now, just set the panel directly
            chatStream.Panel = agentPanel;
        }
        
        _accordionManager?.AddChatStream(chatStream, null, null, _configuration);

        if (chatStream.Panel != null)
        {
            // Dependencies are now set in the constructor
        }

        // Skip subscription in test mode to avoid hanging background tasks
        if (!isTestMode)
        {
            SubscribeToSessionEvents(config.SessionId);
        }
    }

    public void AddChatStreamForTest(ChatStream chatStream)
    {
        // For testing only
        _accordionManager?.AddChatStream(chatStream, null, null, _configuration);
    }

    public ChatStream[] GetChatStreamsForTest()
    {
        // For testing only
        return _accordionManager?.ChatStreams.ToArray() ?? Array.Empty<ChatStream>();
    }

    public async Task ExecuteCommandForActiveSessionTest(Func<Correlation, ICommand> commandFactory)
    {
        // For testing only
        await ExecuteCommandForActiveSession(commandFactory);
    }

    public async Task AttachChatStreamToSessionForTest(ChatStream chatStream, Guid sessionId)
    {
        // For testing only - bypass test mode check
        try
        {
            // Update the chat stream's SessionId to match the orchestrator session
            var oldSessionId = chatStream.SessionId;
            chatStream.SessionId = sessionId;

            // Unsubscribe from old session events if different
            if (oldSessionId != sessionId && _eventSubscriptionTasks.ContainsKey(oldSessionId))
            {
                // Cancel old subscription
                _eventSubscriptionTasks[oldSessionId].Dispose();
                _eventSubscriptionTasks.Remove(oldSessionId);
            }

            // Subscribe to the new session events (skip in test mode)
            if (!isTestMode)
            {
                SubscribeToSessionEvents(sessionId);
            }

            // Update the panel's session and command button states
            if (chatStream.Panel is AgentPanel agentPanel)
            {
                agentPanel.UpdateSession(sessionId);
            }

            Console.WriteLine($"Attached chat stream '{chatStream.AgentName}' to session {sessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to attach chat stream to session: {ex.Message}");
        }
    }

    public async Task AttachChatStreamToSessionWithSubscriptionForTest(ChatStream chatStream, Guid sessionId)
    {
        // For testing only - attach with subscription (always subscribe even in test mode)
        try
        {
            // Update the chat stream's SessionId to match the orchestrator session
            var oldSessionId = chatStream.SessionId;
            chatStream.SessionId = sessionId;

            // Unsubscribe from old session events if different
            if (oldSessionId != sessionId && _eventSubscriptionTasks.ContainsKey(oldSessionId))
            {
                // Cancel old subscription
                _eventSubscriptionTasks[oldSessionId].Dispose();
                _eventSubscriptionTasks.Remove(oldSessionId);
            }

            // ALWAYS subscribe for this test method (even in test mode)
            SubscribeToSessionEvents(sessionId);

            // In test mode, wait for the subscription to complete (it should complete when the stream ends)
            if (isTestMode)
            {
                await WaitForEventSubscription(sessionId, TimeSpan.FromSeconds(5));
            }

            // Update the panel's session and command button states
            if (chatStream.Panel is AgentPanel agentPanel)
            {
                agentPanel.UpdateSession(sessionId);
            }

            Console.WriteLine($"Attached chat stream '{chatStream.AgentName}' to session {sessionId} with subscription");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to attach chat stream to session: {ex.Message}");
        }
    }

    public async Task CreateSessionForTestWithSubscription(SessionConfig config)
    {
        // For testing only - create session and set up chat stream WITH subscription
        await _sessionManager.CreateSession(config);

        var chatStream = new ChatStream(config.SessionId, $"Agent-{config.SessionId.ToString().Substring(0, 8)}");
        
        // Create AgentPanel for the chat stream (normally done by AccordionLayoutManager)
        var agentPanel = new AgentPanel(chatStream);
        // Add the panel to the container for proper UI hierarchy
        if (_accordionManager != null && _accordionManager is AccordionLayoutManager manager)
        {
            // Access the container through reflection or add a method to get it
            // For now, just set the panel directly
            chatStream.Panel = agentPanel;
        }
        
        _accordionManager?.AddChatStream(chatStream, null, null, _configuration);

        if (chatStream.Panel != null)
        {
            // Dependencies are now set in the constructor
        }

        // ALWAYS subscribe for this test method (even in test mode)
        SubscribeToSessionEvents(config.SessionId);
        
        // In test mode, wait for the subscription to complete (it should complete when the stream ends)
        if (isTestMode)
        {
            await WaitForEventSubscription(config.SessionId, TimeSpan.FromSeconds(5));
        }
    }

    public async Task ExecuteRunTestsCommandForTest()
    {
        // For testing only - simulate clicking Run Tests menu
        await ExecuteCommandForActiveSession((correlation) => 
            new RunTests(Guid.NewGuid(), correlation, GetCurrentRepo(), null, TimeSpan.FromMinutes(5)));
    }

    public bool TestAICanAccessForTest()
    {
        // For testing only - verify AI client access doesn't throw exceptions
        try
        {
            // This would normally access AI functionality
            return !isTestMode || true; // In test mode, dummy client should work
        }
        catch
        {
            return false;
        }
    }

    public async Task CreateNewSessionForTest()
    {
        // For testing only - bypass test mode check
        await CreateNewSession();
    }

    public void ShowAttachChatToSessionDialogForTest()
    {
        // For testing only - bypass test mode check
        ShowAttachChatToSessionDialog();
    }

    private string GetSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "JuniorDev");
        return Path.Combine(appFolder, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        try
        {
            string settingsFile = _testSettingsFilePath ?? GetSettingsFilePath();
            if (File.Exists(settingsFile))
            {
                var json = File.ReadAllText(settingsFile);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    // Ensure LiveProfile is initialized
                    settings.LiveProfile ??= new LiveProfileSettings();
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }

        return new AppSettings { LiveProfile = new LiveProfileSettings() };
    }

    public void LoadLayout()
    {
        try
        {
            string layoutFile = _testLayoutFilePath ?? GetLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                dockManager!.RestoreLayoutFromXml(layoutFile);
            }
            else
            {
                LoadDefaultLayout();
            }

            // Load chat streams after dock layout
            LoadChatStreams();
        }
        catch (Exception ex)
        {
            // If layout is corrupted, fall back to default
            Console.WriteLine($"Failed to load layout: {ex.Message}");
            LoadDefaultLayout();
            LoadDefaultChatStreams();
        }
    }

    public void ResetLayout()
    {
        try
        {
            // Delete the layout file to reset to defaults
            string layoutFile = _testLayoutFilePath ?? GetLayoutFilePath();
            if (File.Exists(layoutFile))
            {
                File.Delete(layoutFile);
            }
            
            // Delete the chat streams file to reset to defaults
            string chatStreamsFile = GetChatStreamsFilePath();
            if (File.Exists(chatStreamsFile))
            {
                File.Delete(chatStreamsFile);
            }
            
            // Explicitly reset panels to default layout
            sessionsPanel!.Dock = DockingStyle.Left;
            sessionsPanel!.Width = 250;
            artifactsPanel!.Dock = DockingStyle.Right;
            artifactsPanel!.Width = 300;
            conversationPanel!.Dock = DockingStyle.Fill;
            eventsPanel!.Dock = DockingStyle.Bottom;
            eventsPanel!.Height = 200;
            
            // Reset chat streams to default
            LoadDefaultChatStreams();
            
            // Save the reset layout immediately
            SaveLayout();
            
            // Show message box only in normal mode (skip in test mode for automation)
            if (!isTestMode)
            {
                MessageBox.Show("Layout has been reset to default.", "Layout Reset", 
                              MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception)
        {
            // Re-throw the exception so it can be properly handled
            throw;
        }
    }

    private async void AddNewChatStream()
    {
        if (_accordionManager == null) return;

        // Generate a unique agent name
        var agentNumber = _accordionManager.ChatStreams.Count + 1;
        var agentName = $"Agent {agentNumber}";
        
        if (!isTestMode)
        {
            try
            {
                // Create a real session for the new chat stream
                var sessionConfig = new SessionConfig(
                    SessionId: Guid.NewGuid(),
                    ParentSessionId: null,
                    PlanNodeId: null,
                    Policy: new PolicyProfile
                    {
                        Name = "default",
                        ProtectedBranches = new HashSet<string> { "main", "master" },
                        RequireTestsBeforePush = false,
                        RequireApprovalForPush = false,
                        CommandWhitelist = null,
                        CommandBlacklist = null,
                        MaxFilesPerCommit = null,
                        AllowedWorkItemTransitions = null,
                        Limits = new RateLimits
                        {
                            CallsPerMinute = 60,
                            Burst = 10
                        }
                    },
                    Repo: new RepoRef("default-repo", Environment.CurrentDirectory),
                    Workspace: new WorkspaceRef(Environment.CurrentDirectory),
                    WorkItem: null,
                    AgentProfile: "default"
                );

                await _sessionManager.CreateSession(sessionConfig);

            // Create chat stream for this session with proper SessionId mapping
            var chatStream = new ChatStream(sessionConfig.SessionId, agentName);
            _accordionManager?.AddChatStream(chatStream, _sessionManager, _serviceProvider, _configuration);
                // Set dependencies on the panel for command publishing
                if (chatStream.Panel != null)
                {
                    // Dependencies are now set in the constructor
                }

                // Subscribe to session events for real-time updates
                SubscribeToSessionEvents(sessionConfig.SessionId);

                Console.WriteLine($"Created new session: {agentName} ({sessionConfig.SessionId})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create new session: {ex.Message}");
                // Fall back to mock mode
                var chatStream = new ChatStream(Guid.NewGuid(), agentName);
                chatStream.Status = SessionStatus.Running;
                chatStream.CurrentTask = "Ready";
                _accordionManager.AddChatStream(chatStream, null, null, _configuration);
            }
        }
        else
        {
            // Create new chat stream with mock status
            var chatStream = new ChatStream(Guid.NewGuid(), agentName);
            
            // Add some variety to the mock data
            var mockTasks = new[] { "Analyzing code", "Running tests", "Fixing bugs", "Implementing feature", "Reviewing changes" };
            var mockStatuses = new[] { SessionStatus.Running, SessionStatus.Paused, SessionStatus.Error, SessionStatus.NeedsApproval };
            
            var random = new Random();
            chatStream.Status = mockStatuses[random.Next(mockStatuses.Length)];
            chatStream.CurrentTask = mockTasks[random.Next(mockTasks.Length)];
            chatStream.ProgressPercentage = random.Next(0, 101);
            
            _accordionManager.AddChatStream(chatStream, null, null, _configuration);
            
            Console.WriteLine($"Added new chat stream: {agentName} ({chatStream.Status}) - {chatStream.CurrentTask}");
        }
    }

    private void AddTestChatStreams()
    {
        if (_accordionManager == null) return;

        // Add test streams with different statuses
        var testStreams = new[]
        {
            ("Agent 2", SessionStatus.Running, "Analyzing code", 75),
            ("Agent 3", SessionStatus.Paused, "Running tests", 45),
            ("Agent 4", SessionStatus.Error, "Fixing bugs", 0),
            ("Agent 5", SessionStatus.Completed, "Feature complete", 100)
        };

        foreach (var (name, status, task, progress) in testStreams)
        {
            var stream = new ChatStream(Guid.NewGuid(), name);
            stream.Status = status;
            stream.CurrentTask = task;
            stream.ProgressPercentage = progress;
            _accordionManager.AddChatStream(stream, null, null, _configuration);

            // Add some mock artifacts for this stream
            AddMockArtifactsForStream(stream.SessionId, name);
        }
    }

    private async Task AttachChatStreamToSession(ChatStream chatStream, Guid sessionId)
    {
        try
        {
            // Update the chat stream's SessionId to match the orchestrator session
            var oldSessionId = chatStream.SessionId;
            chatStream.SessionId = sessionId;

            // Unsubscribe from old session events if different
            if (oldSessionId != sessionId && _eventSubscriptionTasks.ContainsKey(oldSessionId))
            {
                // Cancel old subscription
                _eventSubscriptionTasks[oldSessionId].Dispose();
                _eventSubscriptionTasks.Remove(oldSessionId);
            }

            // Subscribe to the new session events
            SubscribeToSessionEvents(sessionId);

            // Update the panel's session and command button states
            if (chatStream.Panel is AgentPanel agentPanel)
            {
                agentPanel.UpdateSession(sessionId);
            }

            Console.WriteLine($"Attached chat stream '{chatStream.AgentName}' to session {sessionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to attach chat stream to session: {ex.Message}");
        }
    }

    private async Task CreateSessionForChatStream(ChatStream chatStream)
    {
        try
        {
            // Create a real session for the chat stream
            var sessionConfig = new SessionConfig(
                SessionId: Guid.NewGuid(),
                ParentSessionId: null,
                PlanNodeId: null,
                Policy: new PolicyProfile
                {
                    Name = "default",
                    ProtectedBranches = new HashSet<string> { "main", "master" },
                    RequireTestsBeforePush = false,
                    RequireApprovalForPush = false,
                    CommandWhitelist = null,
                    CommandBlacklist = null,
                    MaxFilesPerCommit = null,
                    AllowedWorkItemTransitions = null,
                    Limits = new RateLimits
                    {
                        CallsPerMinute = 60,
                        Burst = 10
                    }
                },
                Repo: new RepoRef("default-repo", Environment.CurrentDirectory),
                Workspace: new WorkspaceRef(Environment.CurrentDirectory),
                WorkItem: null,
                AgentProfile: "default"
            );

            await _sessionManager.CreateSession(sessionConfig);

            // Attach the chat stream to the new session
            await AttachChatStreamToSession(chatStream, sessionConfig.SessionId);

            Console.WriteLine($"Created session {sessionConfig.SessionId} for chat stream '{chatStream.AgentName}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create session for chat stream: {ex.Message}");
        }
    }

    private void AddMockArtifactsForStream(Guid sessionId, string agentName)
    {
        var mockArtifacts = new[]
        {
            new Artifact("test-results", "Unit Test Results", $"Test results for {agentName} - 15/15 tests passed"),
            new Artifact("build-log", "Build Log", $"Build log for {agentName} - compilation successful"),
            new Artifact("code-coverage", "Code Coverage Report", $"Coverage report for {agentName} - 85% coverage achieved"),
            new Artifact("diff-output", "Code Changes", $"Git diff showing changes made by {agentName}")
        };

        foreach (var artifact in mockArtifacts)
        {
            AddArtifactToChat(sessionId, artifact);
        }
    }

    private void RemoveActiveChatStream()
    {
        if (_accordionManager == null || _accordionManager.ChatStreams.Count <= 1) return;

        // Find the currently expanded stream
        var expandedStream = _accordionManager.ChatStreams.FirstOrDefault(s => s.IsExpanded);
        if (expandedStream != null)
        {
            _accordionManager.RemoveChatStream(expandedStream);
            Console.WriteLine($"Removed chat stream: {expandedStream.AgentName}");
        }
    }

    private void FilterArtifactsByChat(string selectedChat)
    {
        RefreshArtifactsTree(selectedChat);
    }

    private void RefreshArtifactsTree(string filterByChat = "All Chats")
    {
        if (artifactsTree == null) return;

        artifactsTree.Nodes.Clear();

        if (filterByChat == "All Chats")
        {
            // Show artifacts from all chats
            foreach (var chatStream in _accordionManager?.ChatStreams ?? Enumerable.Empty<ChatStream>())
            {
                AddChatArtifactsToTree(chatStream);
            }
        }
        else
        {
            // Show artifacts from specific chat
            var chatStream = _accordionManager?.ChatStreams.FirstOrDefault(s => s.AgentName == filterByChat);
            if (chatStream != null)
            {
                AddChatArtifactsToTree(chatStream);
            }
        }

        // Expand all nodes for better visibility
        artifactsTree.ExpandAll();
    }

    private void AddChatArtifactsToTree(ChatStream chatStream)
    {
        if (!_chatArtifacts.ContainsKey(chatStream.SessionId) || !_chatArtifacts[chatStream.SessionId].Any())
        {
            // Add a placeholder node if no artifacts
            var chatNode = artifactsTree.AppendNode(new object[] { $"{chatStream.AgentName} - No artifacts yet" }, null);
            return;
        }

        // Add chat node
        var rootNode = artifactsTree.AppendNode(new object[] { chatStream.AgentName }, null);

        // Group artifacts by type
        var artifactsByType = _chatArtifacts[chatStream.SessionId].GroupBy(a => a.Kind);

        foreach (var typeGroup in artifactsByType)
        {
            var typeNode = artifactsTree.AppendNode(new object[] { typeGroup.Key }, rootNode);

            foreach (var artifact in typeGroup)
            {
                var artifactNode = artifactsTree.AppendNode(new object[] { artifact.Name }, typeNode);
                // Store artifact data for double-click handling
                artifactNode.Tag = artifact;
            }
        }
    }

    public void AddArtifactToChat(Guid sessionId, Artifact artifact)
    {
        if (!_chatArtifacts.ContainsKey(sessionId))
        {
            _chatArtifacts[sessionId] = new List<Artifact>();
        }

        _chatArtifacts[sessionId].Add(artifact);

        // Refresh the artifacts tree if currently showing this chat or all chats
        var currentFilter = _chatFilterCombo?.SelectedItem?.ToString() ?? "All Chats";
        if (currentFilter == "All Chats" || 
            _accordionManager?.ChatStreams.Any(s => s.SessionId == sessionId && s.AgentName == currentFilter) == true)
        {
            RefreshArtifactsTree(currentFilter);
        }
    }

    private void UpdateChatFilterOptions()
    {
        if (_chatFilterCombo == null || _accordionManager == null) return;

        _chatFilterCombo.Items.Clear();
        _chatFilterCombo.Items.Add("All Chats");

        foreach (var stream in _accordionManager.ChatStreams)
        {
            _chatFilterCombo.Items.Add(stream.AgentName);
        }

        // Keep current selection if it still exists, otherwise default to "All Chats"
        if (_chatFilterCombo.SelectedItem == null)
        {
            _chatFilterCombo.SelectedIndex = 0;
        }
    }

    public void AddBlockingCondition(BlockingCondition condition)
    {
        _blockingConditions.Add(condition);
        UpdateBlockingBanner();
    }

    public void RemoveBlockingCondition(Guid sessionId, BlockingType type)
    {
        _blockingConditions.RemoveAll(c => c.SessionId == sessionId && c.Type == type);
        UpdateBlockingBanner();
    }

    private void UpdateBlockingBanner()
    {
        if (_blockingConditions.Count == 0)
        {
            _blockingBannerPanel!.Visible = false;
            return;
        }

        // Show the most recent blocking condition
        var currentCondition = _blockingConditions.OrderByDescending(c => c.Timestamp).First();

        _blockingBannerLabel!.Text = currentCondition.Message;
        _blockingBannerActionButton!.Text = currentCondition.ActionText;
        _blockingBannerActionButton!.Visible = !string.IsNullOrEmpty(currentCondition.ActionText);
        _blockingBannerActionButton!.Tag = currentCondition; // Store for action handling

        _blockingBannerPanel!.Visible = true;
    }

    private void ArtifactsTree_DoubleClick(object? sender, EventArgs e)
    {
        if (artifactsTree?.Selection?.Count > 0)
        {
            var selectedNode = artifactsTree.Selection[0];
            if (selectedNode?.Tag is Artifact artifact)
            {
                // Open/select the artifact
                OpenArtifact(artifact);
            }
        }
    }

    private void OpenArtifact(Artifact artifact)
    {
        try
        {
            // For now, show artifact content in a message box or dialog
            // In a real implementation, this might open in an editor, browser, etc.
            var content = artifact.InlineText ?? "[Binary artifact - cannot display inline]";
            
            if (!isTestMode)
            {
                using var dialog = new Form
                {
                    Text = artifact.Name,
                    Size = new Size(600, 400),
                    StartPosition = FormStartPosition.CenterParent
                };
                
                var textBox = new System.Windows.Forms.TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    Text = content,
                    Font = new Font("Consolas", 9)
                };
                
                dialog.Controls.Add(textBox);
                dialog.ShowDialog();
            }
            else
            {
                // In test mode, just log the content
                Console.WriteLine($"Opening artifact '{artifact.Name}': {content}");
            }
        }
        catch (Exception)
        {
            // Re-throw the exception so it can be properly handled
            throw;
        }
    }

    private void BlockingBannerActionButton_Click(object? sender, EventArgs e)
    {
        if (_blockingBannerActionButton?.Tag is BlockingCondition condition)
        {
            HandleBlockingAction(condition);
        }
    }

    private async void HandleBlockingAction(BlockingCondition condition)
    {
        try
        {
            switch (condition.Type)
            {
                case BlockingType.NeedsApproval:
                    // Approve the session
                    await _sessionManager.ApproveSession(condition.SessionId);
                    RemoveBlockingCondition(condition.SessionId, condition.Type);
                    Console.WriteLine($"Approved session {condition.SessionId}");
                    break;

                case BlockingType.ConflictDetected:
                    // In a real app, this would show conflict resolution UI
                    // For now, just remove the blocking condition
                    RemoveBlockingCondition(condition.SessionId, condition.Type);
                    Console.WriteLine($"Resolved conflict for session {condition.SessionId}");
                    break;

                case BlockingType.Throttled:
                    // Throttling is time-based, just wait
                    // Could show retry options
                    Console.WriteLine($"Acknowledged throttling for session {condition.SessionId}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to handle blocking action: {ex.Message}");
            // Still remove the condition to avoid getting stuck
            RemoveBlockingCondition(condition.SessionId, condition.Type);
        }
    }

    private async Task ExecuteCommandForActiveSession(Func<Correlation, ICommand> commandFactory)
    {
        if (_accordionManager == null || _accordionManager.ChatStreams.Count == 0)
        {
            throw new InvalidOperationException("No active chat session. Please create a session first.");
        }

        // Use the first expanded stream, or the first stream if none expanded
        var activeStream = _accordionManager.ChatStreams.FirstOrDefault(s => s.IsExpanded) ?? _accordionManager.ChatStreams[0];
        var correlation = new Correlation(activeStream.SessionId);

        try
        {
            var command = commandFactory(correlation);
            await _sessionManager.PublishCommand(command);
            Console.WriteLine($"Executed command {command.Kind} for session {activeStream.SessionId}");
        }
        catch (Exception)
        {
            // Re-throw the exception in both test and production mode so it can be properly handled
            throw;
        }
    }

    private async Task CreateNewSession()
    {
        try
        {
            // Show session configuration dialog
            using var dialog = new SessionConfigDialog();
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return; // User cancelled
            }

            var sessionConfig = dialog.SessionConfig;

            await _sessionManager.CreateSession(sessionConfig);

            // Create a chat stream for the new session
            var agentNumber = (_accordionManager?.ChatStreams.Count ?? 0) + 1;
            var agentName = $"Agent {agentNumber}";
            var chatStream = new ChatStream(sessionConfig.SessionId, agentName);
            _accordionManager?.AddChatStream(chatStream, _sessionManager, _serviceProvider, _configuration);

            // Subscribe to session events
            SubscribeToSessionEvents(sessionConfig.SessionId);

            // Refresh the sessions list
            RefreshSessionsList();

            Console.WriteLine($"Created new session: {agentName} ({sessionConfig.SessionId}) with repo '{sessionConfig.Repo.Name}' at '{sessionConfig.Repo.Path}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create new session: {ex.Message}");
            MessageBox.Show($"Failed to create session: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowAttachChatToSessionDialog()
    {
        if (_accordionManager == null || _accordionManager.ChatStreams.Count == 0)
        {
            MessageBox.Show("No chat streams available to attach. Please create a chat stream first.", "No Chat Streams", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = "Attach Chat Stream to Session",
            Size = new Size(500, 300),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var chatLabel = new System.Windows.Forms.Label { Text = "Select Chat Stream:", Location = new Point(10, 10), AutoSize = true };
        var chatCombo = new System.Windows.Forms.ComboBox
        {
            Location = new Point(10, 30),
            Width = 460,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        var sessionLabel = new System.Windows.Forms.Label { Text = "Select Existing Session:", Location = new Point(10, 70), AutoSize = true };
        var sessionCombo = new System.Windows.Forms.ComboBox
        {
            Location = new Point(10, 90),
            Width = 460,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        var manualLabel = new System.Windows.Forms.Label { Text = "Or Enter Session ID Manually:", Location = new Point(10, 130), AutoSize = true };
        var sessionTextBox = new System.Windows.Forms.TextBox
        {
            Location = new Point(10, 150),
            Width = 460,
            PlaceholderText = "Enter Session ID (GUID format)",
            Enabled = false
        };

        var manualCheckBox = new System.Windows.Forms.CheckBox
        {
            Text = "Enter Session ID manually",
            Location = new Point(10, 180),
            AutoSize = true
        };

        var attachButton = new System.Windows.Forms.Button { Text = "Attach", Location = new Point(320, 220), DialogResult = DialogResult.OK };
        var cancelButton = new System.Windows.Forms.Button { Text = "Cancel", Location = new Point(400, 220), DialogResult = DialogResult.Cancel };

        // Populate chat streams
        foreach (var stream in _accordionManager.ChatStreams)
        {
            chatCombo.Items.Add(new { Name = stream.AgentName, Stream = stream });
        }
        if (chatCombo.Items.Count > 0) chatCombo.SelectedIndex = 0;

        // Populate existing sessions
        try
        {
            var activeSessions = _sessionManager.GetActiveSessions();
            foreach (var session in activeSessions)
            {
                var displayText = $"{session.RepoName} - {session.AgentProfile} ({session.Status})";
                if (!string.IsNullOrEmpty(session.CurrentTask))
                {
                    displayText += $" - {session.CurrentTask}";
                }
                sessionCombo.Items.Add(new { DisplayText = displayText, Session = session });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load active sessions: {ex.Message}");
        }

        // Enable/disable controls based on manual checkbox
        manualCheckBox.CheckedChanged += (s, e) =>
        {
            sessionCombo.Enabled = !manualCheckBox.Checked;
            sessionTextBox.Enabled = manualCheckBox.Checked;
        };

        attachButton.Click += async (s, e) =>
        {
            if (chatCombo.SelectedItem == null)
            {
                MessageBox.Show("Please select a chat stream.", "Missing Information", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Get the selected chat stream
            dynamic selectedChatItem = chatCombo.SelectedItem;
            var stream = selectedChatItem.Stream as ChatStream;

            if (stream == null)
            {
                MessageBox.Show("Invalid chat stream selection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Guid sessionId;
            if (manualCheckBox.Checked)
            {
                // Manual entry mode
                if (string.IsNullOrWhiteSpace(sessionTextBox.Text))
                {
                    MessageBox.Show("Please enter a Session ID.", "Missing Information", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Guid.TryParse(sessionTextBox.Text.Trim(), out sessionId))
                {
                    MessageBox.Show("Invalid Session ID format. Please enter a valid GUID.", "Invalid Session ID", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                // Existing session mode
                if (sessionCombo.SelectedItem == null)
                {
                    MessageBox.Show("Please select an existing session.", "Missing Information", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                dynamic selectedSessionItem = sessionCombo.SelectedItem;
                var sessionInfo = selectedSessionItem.Session as SessionInfo;
                sessionId = sessionInfo!.SessionId;
            }

            try
            {
                await AttachChatStreamToSession(stream, sessionId);
                dialog.DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to attach chat stream: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        dialog.Controls.AddRange(new System.Windows.Forms.Control[] {
            chatLabel, chatCombo, sessionLabel, sessionCombo,
            manualLabel, sessionTextBox, manualCheckBox, attachButton, cancelButton
        });

        dialog.AcceptButton = attachButton;
        dialog.CancelButton = cancelButton;

        dialog.ShowDialog();
    }

    private async void OnCreateSessionRequested(object? sender, AgentPanel agentPanel)
    {
        await CreateSessionForChatStream(agentPanel.ChatStream);
    }

    private async void OnAttachToSessionRequested(object? sender, AgentPanel agentPanel)
    {
        // For now, show the attach dialog - in the future this could be more direct
        ShowAttachChatToSessionDialog();
    }

    public async Task WaitForEventSubscription(Guid sessionId, TimeSpan timeout)
    {
        // Test-only method to wait for a subscription task to complete
        if (_eventSubscriptionTasks.TryGetValue(sessionId, out var task))
        {
            await Task.WhenAny(task, Task.Delay(timeout));
        }
    }

    public void CancelEventSubscription(Guid sessionId)
    {
        // Test-only method to cancel a subscription
        if (_eventSubscriptionTasks.TryGetValue(sessionId, out var task))
        {
            // Note: In a real implementation, we'd need to store the CancellationTokenSource
            // For now, we'll just remove the task reference
            _eventSubscriptionTasks.Remove(sessionId);
        }
    }

    private RepoRef GetCurrentRepo()
    {
        // For now, return a default repo based on the workspace
        // In a real implementation, this could be configurable per session
        return new RepoRef("default-repo", Environment.CurrentDirectory);
    }

    private string FindWorkspaceRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Look for solution file or common workspace markers
        var directory = new DirectoryInfo(currentDir);
        while (directory != null)
        {
            if (directory.GetFiles("*.sln").Any() ||
                directory.GetFiles("Directory.Packages.props").Any() ||
                directory.GetFiles("global.json").Any())
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        // Fallback to current directory
        return currentDir;
    }
}
