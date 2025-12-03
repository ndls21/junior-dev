using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JuniorDev.Agents;
using JuniorDev.Contracts;

namespace JuniorDev.Agents.Sk;

/// <summary>
/// File metadata for analysis
/// </summary>
public sealed record FileMetadata(
    string Path,
    long Size,
    DateTimeOffset LastModified,
    string? Content = null);

/// <summary>
/// VCS helper service for repository analysis.
/// Provides file listing, content fetching, and metadata operations
/// with configurable limits and ignore patterns.
/// </summary>
public interface IVcsHelper
{
    /// <summary>
    /// Lists files under the given paths with optional ignore patterns and limits.
    /// </summary>
    Task<IReadOnlyList<FileMetadata>> ListFilesAsync(
        IEnumerable<string> paths,
        IEnumerable<string>? ignorePatterns = null,
        int? maxFiles = null,
        long? maxTotalBytes = null);

    /// <summary>
    /// Fetches content for the given file paths, respecting size limits.
    /// </summary>
    Task<IReadOnlyList<FileMetadata>> GetFileContentsAsync(
        IEnumerable<string> paths,
        long? maxFileBytes = null);

    /// <summary>
    /// Gets basic metadata for files without content.
    /// </summary>
    Task<IReadOnlyList<FileMetadata>> GetFileMetadataAsync(IEnumerable<string> paths);
}

/// <summary>
/// Default implementation of IVcsHelper using the file system.
/// Can be extended to work with Git or other VCS systems.
/// </summary>
public class FileSystemVcsHelper : IVcsHelper
{
    private readonly string _workspaceRoot;

    public FileSystemVcsHelper(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot ?? throw new ArgumentNullException(nameof(workspaceRoot));
    }

    public async Task<IReadOnlyList<FileMetadata>> ListFilesAsync(
        IEnumerable<string> paths,
        IEnumerable<string>? ignorePatterns = null,
        int? maxFiles = null,
        long? maxTotalBytes = null)
    {
        var results = new List<FileMetadata>();
        var ignorePatternsList = ignorePatterns?.ToList() ?? new List<string>();
        long totalBytes = 0;

        // Add common ignore patterns if none specified
        if (!ignorePatternsList.Any())
        {
            ignorePatternsList.AddRange(new[]
            {
                ".git/**",
                ".svn/**",
                ".hg/**",
                "node_modules/**",
                "bin/**",
                "obj/**",
                ".vs/**",
                "*.tmp",
                "*.log",
                "*.cache"
            });
        }

        foreach (var path in paths)
        {
            if (maxFiles.HasValue && results.Count >= maxFiles.Value)
                break;

            var fullPath = Path.Combine(_workspaceRoot, path);
            if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
                continue;

            var files = Directory.Exists(fullPath)
                ? Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                : new[] { fullPath };

            foreach (var file in files)
            {
                if (maxFiles.HasValue && results.Count >= maxFiles.Value)
                    break;

                // Check ignore patterns
                var relativePath = Path.GetRelativePath(_workspaceRoot, file);
                if (ShouldIgnore(relativePath, ignorePatternsList))
                    continue;

                try
                {
                    var info = new FileInfo(file);
                    var size = info.Length;

                    // Check total bytes limit
                    if (maxTotalBytes.HasValue && totalBytes + size > maxTotalBytes.Value)
                        continue;

                    results.Add(new FileMetadata(relativePath, size, info.LastWriteTimeUtc));
                    totalBytes += size;
                }
                catch (Exception)
                {
                    // Skip files we can't access
                    continue;
                }
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<FileMetadata>> GetFileContentsAsync(
        IEnumerable<string> paths,
        long? maxFileBytes = null)
    {
        var results = new List<FileMetadata>();

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(_workspaceRoot, path);
            if (!File.Exists(fullPath))
                continue;

            try
            {
                var info = new FileInfo(fullPath);
                var size = info.Length;

                // Check file size limit
                if (maxFileBytes.HasValue && size > maxFileBytes.Value)
                    continue;

                var content = await File.ReadAllTextAsync(fullPath);
                results.Add(new FileMetadata(path, size, info.LastWriteTimeUtc, content));
            }
            catch (Exception)
            {
                // Skip files we can't read
                continue;
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<FileMetadata>> GetFileMetadataAsync(IEnumerable<string> paths)
    {
        var results = new List<FileMetadata>();

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(_workspaceRoot, path);
            if (!File.Exists(fullPath))
                continue;

            try
            {
                var info = new FileInfo(fullPath);
                results.Add(new FileMetadata(path, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception)
            {
                // Skip files we can't access
                continue;
            }
        }

        return results;
    }

    private bool ShouldIgnore(string path, IEnumerable<string> ignorePatterns)
    {
        var normalizedPath = path.Replace('\\', '/');

        foreach (var pattern in ignorePatterns)
        {
            var normalizedPattern = pattern.Replace('\\', '/');

            // Simple glob matching - could be enhanced with proper glob library
            if (normalizedPattern.EndsWith("/**"))
            {
                var dirPattern = normalizedPattern.Substring(0, normalizedPattern.Length - 3);
                if (normalizedPath.StartsWith(dirPattern))
                    return true;
            }
            else if (normalizedPattern.Contains("*"))
            {
                // Simple wildcard matching
                var regexPattern = "^" + normalizedPattern.Replace("*", ".*") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(normalizedPath, regexPattern))
                    return true;
            }
            else
            {
                if (normalizedPath == normalizedPattern)
                    return true;
            }
        }

        return false;
    }
}