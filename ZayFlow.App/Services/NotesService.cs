using System.IO;
using System.Text.Json;
using ZayFlow.App.Models;

namespace ZayFlow.App.Services;

public interface INotesService
{
    Task<List<NoteItem>> GetNotesAsync(CancellationToken ct = default);
    Task SaveNotesAsync(IEnumerable<NoteItem> notes, CancellationToken ct = default);
}

public sealed class NotesService : INotesService
{
    private readonly string _notesPath;

    public NotesService()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZayFlow");
        Directory.CreateDirectory(appData);
        _notesPath = Path.Combine(appData, "notes.json");
    }

    public async Task<List<NoteItem>> GetNotesAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_notesPath))
            {
                return new List<NoteItem>();
            }

            var json = await File.ReadAllTextAsync(_notesPath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<NoteItem>();
            }

            return JsonSerializer.Deserialize<List<NoteItem>>(json) ?? new List<NoteItem>();
        }
        catch (JsonException)
        {
            // Corrupted JSON, return empty list and recreate file on next save
            return new List<NoteItem>();
        }
        catch (Exception)
        {
            // Any other error, return empty list
            return new List<NoteItem>();
        }
    }

    public async Task SaveNotesAsync(IEnumerable<NoteItem> notes, CancellationToken ct = default)
    {
        try
        {
            if (notes == null)
            {
                throw new ArgumentNullException(nameof(notes));
            }

            var json = JsonSerializer.Serialize(notes, new JsonSerializerOptions { WriteIndented = true });
            
            // Atomic write using temp file
            var tempPath = _notesPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            
            if (File.Exists(_notesPath))
            {
                File.Delete(_notesPath);
            }
            
            File.Move(tempPath, _notesPath);
        }
        catch (Exception ex)
        {
            // Log error or notify user
            throw new InvalidOperationException($"Failed to save notes: {ex.Message}", ex);
        }
    }
}
