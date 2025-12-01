using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ui.Shell;

/// <summary>
/// Represents a single chat message in a transcript
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("messageId")]
    public string? MessageId { get; set; }

    public ChatMessage() { }

    public ChatMessage(string role, string content, DateTimeOffset? timestamp = null, string? messageId = null)
    {
        Role = role;
        Content = content;
        Timestamp = timestamp ?? DateTimeOffset.Now;
        MessageId = messageId ?? Guid.NewGuid().ToString();
    }
}

/// <summary>
/// Represents a complete chat transcript for a session
/// </summary>
public class ChatTranscript
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; set; }

    [JsonPropertyName("agentName")]
    public string AgentName { get; set; } = "Agent";

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset LastModified { get; set; }

    public ChatTranscript() { }

    public ChatTranscript(Guid sessionId, string agentName)
    {
        SessionId = sessionId;
        AgentName = agentName;
        CreatedAt = DateTimeOffset.Now;
        LastModified = DateTimeOffset.Now;
    }

    public void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        LastModified = DateTimeOffset.Now;
    }

    public void AddMessage(string role, string content, string? messageId = null)
    {
        AddMessage(new ChatMessage(role, content, null, messageId));
    }
}

/// <summary>
/// Configuration for transcript persistence
/// </summary>
public class TranscriptConfig
{
    public bool Enabled { get; set; } = true;
    public int MaxMessagesPerTranscript { get; set; } = 1000;
    public long MaxTranscriptSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB
    public TimeSpan MaxTranscriptAge { get; set; } = TimeSpan.FromDays(30);
    public int TranscriptContextMessages { get; set; } = 10;
    public string StorageDirectory { get; set; } = string.Empty; // Will be set to app data directory
}

/// <summary>
/// Handles persistence of chat transcripts to disk
/// </summary>
public class TranscriptStorage
{
    private readonly TranscriptConfig _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public TranscriptStorage(TranscriptConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Ensure storage directory exists
        if (string.IsNullOrEmpty(_config.StorageDirectory))
        {
            _config.StorageDirectory = GetDefaultStorageDirectory();
        }

        Directory.CreateDirectory(_config.StorageDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private string GetDefaultStorageDirectory()
    {
        // Use platform-specific app data directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "JuniorDev", "ChatTranscripts");
    }

    public string GetTranscriptFilePath(Guid sessionId)
    {
        return Path.Combine(_config.StorageDirectory, $"{sessionId}.json");
    }

    public bool TranscriptExists(Guid sessionId)
    {
        var filePath = GetTranscriptFilePath(sessionId);
        return File.Exists(filePath);
    }

    public ChatTranscript? LoadTranscript(Guid sessionId)
    {
        if (!_config.Enabled)
            return null;

        var filePath = GetTranscriptFilePath(sessionId);

        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            var transcript = JsonSerializer.Deserialize<ChatTranscript>(json, _jsonOptions);

            if (transcript == null)
                return null;

            // Validate the transcript
            if (transcript.SessionId != sessionId)
            {
                Console.WriteLine($"Transcript session ID mismatch: expected {sessionId}, got {transcript.SessionId}");
                return null;
            }

            // Apply size limits and pruning if needed
            PruneTranscript(transcript);

            return transcript;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load transcript for session {sessionId}: {ex.Message}");
            // Return null on corruption - we'll start fresh
            return null;
        }
    }

    public void SaveTranscript(ChatTranscript transcript)
    {
        if (!_config.Enabled || transcript == null)
            return;

        // Apply pruning before saving
        PruneTranscript(transcript);

        var filePath = GetTranscriptFilePath(transcript.SessionId);

        try
        {
            var json = JsonSerializer.Serialize(transcript, _jsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save transcript for session {transcript.SessionId}: {ex.Message}");
        }
    }

    private void PruneTranscript(ChatTranscript transcript)
    {
        // Prune by message count
        if (transcript.Messages.Count > _config.MaxMessagesPerTranscript)
        {
            var excess = transcript.Messages.Count - _config.MaxMessagesPerTranscript;
            transcript.Messages.RemoveRange(0, excess);
        }

        // Prune by age
        var cutoffDate = DateTimeOffset.Now - _config.MaxTranscriptAge;
        transcript.Messages.RemoveAll(m => m.Timestamp < cutoffDate);

        // Prune by size (rough estimate)
        while (transcript.Messages.Count > 1) // Keep at least one message
        {
            var estimatedSize = EstimateTranscriptSize(transcript);
            if (estimatedSize <= _config.MaxTranscriptSizeBytes)
                break;

            // Remove oldest message
            transcript.Messages.RemoveAt(0);
        }
    }

    private long EstimateTranscriptSize(ChatTranscript transcript)
    {
        // Rough estimation: average message is ~200 bytes including JSON overhead
        return transcript.Messages.Count * 200L;
    }

    public void DeleteTranscript(Guid sessionId)
    {
        var filePath = GetTranscriptFilePath(sessionId);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete transcript for session {sessionId}: {ex.Message}");
        }
    }

    public IEnumerable<Guid> GetExistingSessionIds()
    {
        if (!Directory.Exists(_config.StorageDirectory))
            yield break;

        foreach (var file in Directory.GetFiles(_config.StorageDirectory, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (Guid.TryParse(fileName, out var sessionId))
            {
                yield return sessionId;
            }
        }
    }
}