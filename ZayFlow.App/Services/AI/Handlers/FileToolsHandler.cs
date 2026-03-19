using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// Tier 3: Advanced file tools — sync, diff, encrypt, secure delete, bulk metadata, regex search.
/// </summary>
public class FileToolsHandler
{
    private readonly ILogger<FileToolsHandler> _logger;

    public FileToolsHandler(ILogger<FileToolsHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>sync_folders — Mirror or merge two folders.</summary>
    public async Task<ActionResult> ExecuteSyncFoldersAsync(Dictionary<string, object> parameters, string resolvedSource, string resolvedDest, CancellationToken ct)
    {
        var mode = parameters.TryGetValue("mode", out var mObj) ? mObj?.ToString()?.ToLowerInvariant() ?? "mirror" : "mirror";

        if (!Directory.Exists(resolvedSource))
            return new ActionResult { Success = false, Message = $"Source not found: {resolvedSource}" };

        Directory.CreateDirectory(resolvedDest);

        var copied = 0;
        var skipped = 0;
        var deleted = 0;
        var sourceFiles = Directory.GetFiles(resolvedSource, "*", SearchOption.AllDirectories);

        foreach (var srcFile in sourceFiles)
        {
            if (ct.IsCancellationRequested) break;
            var relativePath = Path.GetRelativePath(resolvedSource, srcFile);
            var destFile = Path.Combine(resolvedDest, relativePath);
            var destDir = Path.GetDirectoryName(destFile)!;
            Directory.CreateDirectory(destDir);

            if (File.Exists(destFile))
            {
                var srcInfo = new FileInfo(srcFile);
                var dstInfo = new FileInfo(destFile);
                if (srcInfo.LastWriteTimeUtc <= dstInfo.LastWriteTimeUtc && srcInfo.Length == dstInfo.Length)
                {
                    skipped++;
                    continue;
                }
            }

            File.Copy(srcFile, destFile, overwrite: true);
            copied++;
        }

        // Mirror mode: delete files in dest that don't exist in source
        if (mode == "mirror")
        {
            foreach (var destFile in Directory.GetFiles(resolvedDest, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(resolvedDest, destFile);
                var srcFile = Path.Combine(resolvedSource, relativePath);
                if (!File.Exists(srcFile))
                {
                    File.Delete(destFile);
                    deleted++;
                }
            }
        }

        return new ActionResult
        {
            Success = true,
            Message = $"🔄 Sync complete ({mode}):\n  📄 Copied: {copied}\n  ⏭️ Skipped (up-to-date): {skipped}" +
                (deleted > 0 ? $"\n  🗑️ Deleted (mirror): {deleted}" : "")
        };
    }

    /// <summary>file_diff — Compare two text files line by line.</summary>
    public async Task<ActionResult> ExecuteFileDiffAsync(string resolvedFile1, string resolvedFile2, CancellationToken ct)
    {
        if (!File.Exists(resolvedFile1))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFile1}" };
        if (!File.Exists(resolvedFile2))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFile2}" };

        var lines1 = await File.ReadAllLinesAsync(resolvedFile1, ct);
        var lines2 = await File.ReadAllLinesAsync(resolvedFile2, ct);
        var maxLines = Math.Max(lines1.Length, lines2.Length);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📝 Diff: {Path.GetFileName(resolvedFile1)} vs {Path.GetFileName(resolvedFile2)}\n");

        var additions = 0;
        var deletions = 0;
        var changes = 0;

        for (int i = 0; i < maxLines && i < 200; i++)
        {
            var l1 = i < lines1.Length ? lines1[i] : null;
            var l2 = i < lines2.Length ? lines2[i] : null;

            if (l1 == l2) continue;
            if (l1 == null) { sb.AppendLine($"  + L{i + 1}: {l2}"); additions++; }
            else if (l2 == null) { sb.AppendLine($"  - L{i + 1}: {l1}"); deletions++; }
            else { sb.AppendLine($"  ~ L{i + 1}: {l1}\n       → {l2}"); changes++; }
        }

        if (additions + deletions + changes == 0)
            return new ActionResult { Success = true, Message = "✅ Files are identical!" };

        sb.Insert(0, $"Summary: +{additions} -{deletions} ~{changes} changes\n\n");
        return new ActionResult { Success = true, Message = sb.ToString() };
    }

    /// <summary>encrypt_decrypt — AES encrypt/decrypt a file.</summary>
    public async Task<ActionResult> ExecuteEncryptDecryptAsync(string resolvedFilePath, string action, string password, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" };
        if (string.IsNullOrWhiteSpace(password))
            return new ActionResult { Success = false, Message = "Password is required for encryption/decryption." };

        try
        {
            var isEncrypt = action is "encrypt" or "lock";
            using var deriveBytes = new Rfc2898DeriveBytes(password, 16, 100_000, HashAlgorithmName.SHA256);
            var key = deriveBytes.GetBytes(32);
            var iv = deriveBytes.GetBytes(16);

            var data = await File.ReadAllBytesAsync(resolvedFilePath, ct);
            byte[] result;

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            if (isEncrypt)
            {
                using var encryptor = aes.CreateEncryptor();
                result = encryptor.TransformFinalBlock(data, 0, data.Length);
                var outputPath = resolvedFilePath + ".zayenc";
                await File.WriteAllBytesAsync(outputPath, result, ct);
                return new ActionResult { Success = true, Message = $"🔒 Encrypted → {Path.GetFileName(outputPath)}\n⚠️ Keep your password safe!" };
            }
            else
            {
                using var decryptor = aes.CreateDecryptor();
                result = decryptor.TransformFinalBlock(data, 0, data.Length);
                var outputPath = resolvedFilePath.EndsWith(".zayenc") ? resolvedFilePath[..^7] : resolvedFilePath + ".dec";
                await File.WriteAllBytesAsync(outputPath, result, ct);
                return new ActionResult { Success = true, Message = $"🔓 Decrypted → {Path.GetFileName(outputPath)}" };
            }
        }
        catch (CryptographicException)
        {
            return new ActionResult { Success = false, Message = "❌ Wrong password or corrupted file." };
        }
    }

    /// <summary>secure_delete — Overwrite file with random data before deleting.</summary>
    public async Task<ActionResult> ExecuteSecureDeleteAsync(string resolvedFilePath, CancellationToken ct)
    {
        if (!File.Exists(resolvedFilePath))
            return new ActionResult { Success = false, Message = $"File not found: {resolvedFilePath}" };

        var fi = new FileInfo(resolvedFilePath);
        var size = fi.Length;

        // Overwrite 3 passes
        var random = new byte[Math.Min(size, 64 * 1024)]; // 64KB buffer
        using var rng = RandomNumberGenerator.Create();
        for (int pass = 0; pass < 3; pass++)
        {
            using var stream = new FileStream(resolvedFilePath, FileMode.Open, FileAccess.Write);
            var remaining = size;
            while (remaining > 0)
            {
                var chunk = (int)Math.Min(remaining, random.Length);
                rng.GetBytes(random, 0, chunk);
                await stream.WriteAsync(random.AsMemory(0, chunk), ct);
                remaining -= chunk;
            }
        }

        File.Delete(resolvedFilePath);

        return new ActionResult
        {
            Success = true,
            Message = $"🔐 Securely deleted: {Path.GetFileName(resolvedFilePath)} ({size / 1024}KB, 3-pass overwrite)"
        };
    }

    /// <summary>bulk_metadata — Show metadata for all files in a folder.</summary>
    public Task<ActionResult> ExecuteBulkMetadataAsync(string resolvedPath, CancellationToken ct)
    {
        if (!Directory.Exists(resolvedPath))
            return Task.FromResult(new ActionResult { Success = false, Message = $"Folder not found: {resolvedPath}" });

        var files = Directory.GetFiles(resolvedPath)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Length)
            .Take(30)
            .ToList();

        if (files.Count == 0)
            return Task.FromResult(new ActionResult { Success = true, Message = "No files found." });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📋 File Metadata: {Path.GetFileName(resolvedPath)} ({files.Count} files)\n");
        sb.AppendLine($"{"Name",-35} {"Size",10} {"Modified",-18} {"Attr"}");
        sb.AppendLine(new string('─', 75));

        foreach (var f in files)
        {
            var sizeStr = f.Length < 1024 ? $"{f.Length}B" : f.Length < 1024 * 1024 ? $"{f.Length / 1024}KB" : $"{f.Length / 1024 / 1024}MB";
            var attrs = (f.IsReadOnly ? "R" : "") + (f.Attributes.HasFlag(FileAttributes.Hidden) ? "H" : "") + (f.Attributes.HasFlag(FileAttributes.System) ? "S" : "");
            sb.AppendLine($"{f.Name,-35} {sizeStr,10} {f.LastWriteTime,-18:yyyy-MM-dd HH:mm} {attrs}");
        }

        var totalSize = files.Sum(f => f.Length);
        sb.AppendLine($"\nTotal: {totalSize / 1024 / 1024}MB across {files.Count} files");

        return Task.FromResult(new ActionResult { Success = true, Message = sb.ToString() });
    }

    /// <summary>regex_search — Search file contents with regex pattern.</summary>
    public async Task<ActionResult> ExecuteRegexSearchAsync(string resolvedPath, string pattern, CancellationToken ct)
    {
        if (!Directory.Exists(resolvedPath))
            return new ActionResult { Success = false, Message = $"Folder not found: {resolvedPath}" };

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { return new ActionResult { Success = false, Message = $"Invalid regex: {ex.Message}" }; }

        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".cs", ".py", ".js", ".ts", ".json", ".xml", ".html", ".css",
            ".yaml", ".yml", ".toml", ".ini", ".cfg", ".log", ".csv", ".sql", ".sh", ".bat", ".ps1"
        };

        var matches = new List<(string File, int Line, string Text)>();
        var searchedFiles = 0;

        foreach (var filePath in Directory.GetFiles(resolvedPath, "*", SearchOption.AllDirectories))
        {
            if (ct.IsCancellationRequested || matches.Count > 100) break;
            if (!textExtensions.Contains(Path.GetExtension(filePath))) continue;
            if (new FileInfo(filePath).Length > 500_000) continue; // Skip files > 500KB

            searchedFiles++;
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath, ct);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var relPath = Path.GetRelativePath(resolvedPath, filePath);
                        var snippet = lines[i].Length > 100 ? lines[i][..100] + "..." : lines[i];
                        matches.Add((relPath, i + 1, snippet.Trim()));
                    }
                }
            }
            catch { }
        }

        if (matches.Count == 0)
            return new ActionResult { Success = true, Message = $"No matches for /{pattern}/ in {searchedFiles} files." };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🔍 Regex matches for /{pattern}/ ({matches.Count} matches in {searchedFiles} files):\n");
        foreach (var (file, line, text) in matches.Take(30))
            sb.AppendLine($"  {file}:{line} — {text}");

        if (matches.Count > 30)
            sb.AppendLine($"\n... and {matches.Count - 30} more matches");

        return new ActionResult { Success = true, Message = sb.ToString() };
    }
}
