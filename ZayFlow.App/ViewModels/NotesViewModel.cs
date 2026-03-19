using System.Collections.ObjectModel;
using System.Windows.Input;
using ZayFlow.App.Commands;
using ZayFlow.App.Models;
using ZayFlow.App.Services;
using ZayFlow.App.Services.AI.Contracts;

namespace ZayFlow.App.ViewModels;

public sealed class NotesViewModel : ViewModelBase
{
    private readonly INotesService _notesService;
    private readonly IAIService _aiService;
    private readonly NotificationService _notificationService;

    private NoteItem? _selectedNote;
    private bool _isBusy;

    public NotesViewModel(INotesService notesService, IAIService aiService, NotificationService notificationService)
    {
        _notesService = notesService;
        _aiService = aiService;
        _notificationService = notificationService;

        Notes = new ObservableCollection<NoteItem>();

        CreateNoteCommand = new RelayCommand(_ => CreateNote());
        SaveNoteCommand = new RelayCommand(async _ => await SaveAsync(), _ => SelectedNote != null);
        RewriteCommand = new RelayCommand(async _ => await RewriteAsync(), _ => SelectedNote != null && !string.IsNullOrWhiteSpace(SelectedNote.Content));
        SummarizeCommand = new RelayCommand(async _ => await SummarizeAsync(), _ => SelectedNote != null && !string.IsNullOrWhiteSpace(SelectedNote.Content));
        ExpandIdeasCommand = new RelayCommand(async _ => await ExpandIdeasAsync(), _ => SelectedNote != null && !string.IsNullOrWhiteSpace(SelectedNote.Content));
        ConvertToTasksCommand = new RelayCommand(async _ => await ConvertToTasksAsync(), _ => SelectedNote != null && !string.IsNullOrWhiteSpace(SelectedNote.Content));

        _ = LoadAsync();
    }

    public ObservableCollection<NoteItem> Notes { get; }

    public ICommand CreateNoteCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand RewriteCommand { get; }
    public ICommand SummarizeCommand { get; }
    public ICommand ExpandIdeasCommand { get; }
    public ICommand ConvertToTasksCommand { get; }

    public NoteItem? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (SetProperty(ref _selectedNote, value))
            {
                (SaveNoteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RewriteCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (SummarizeCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ExpandIdeasCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ConvertToTasksCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private async Task LoadAsync()
    {
        var notes = await _notesService.GetNotesAsync();
        Notes.Clear();
        foreach (var note in notes.OrderByDescending(n => n.UpdatedAt))
        {
            Notes.Add(note);
        }

        SelectedNote = Notes.FirstOrDefault();
    }

    private void CreateNote()
    {
        var note = new NoteItem { Title = "New Note", Content = string.Empty, Folder = "General", UpdatedAt = DateTime.Now };
        Notes.Insert(0, note);
        SelectedNote = note;
    }

    private async Task SaveAsync()
    {
        if (SelectedNote == null)
        {
            return;
        }

        SelectedNote.UpdatedAt = DateTime.Now;
        await _notesService.SaveNotesAsync(Notes);
        _notificationService.ShowSuccess("Saved", "Note saved successfully.");
    }

    private async Task RewriteAsync()
    {
        await ApplyAiTransformationAsync("Rewrite this note professionally and keep the original meaning:");
    }

    private async Task SummarizeAsync()
    {
        await ApplyAiTransformationAsync("Summarize this note in concise bullet points:");
    }

    private async Task ExpandIdeasAsync()
    {
        await ApplyAiTransformationAsync("Expand these ideas into a detailed plan with steps:");
    }

    private async Task ConvertToTasksAsync()
    {
        await ApplyAiTransformationAsync("Convert this note into an actionable task list with priorities:");
    }

    private async Task ApplyAiTransformationAsync(string instruction)
    {
        if (SelectedNote == null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var prompt = $"{instruction}\n\n{SelectedNote.Content}";
            var generated = await _aiService.GenerateTextAsync(prompt);
            SelectedNote.Content = generated;
            SelectedNote.UpdatedAt = DateTime.Now;
            OnPropertyChanged(nameof(SelectedNote));
            await _notesService.SaveNotesAsync(Notes);
        }
        catch (Exception ex)
        {
            _notificationService.ShowError("AI service unavailable", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
