# ZayFlow Project Reference

## Solution Structure

| Project | Purpose | Key Folders |
|---------|---------|-------------|
| `ZayFlow.App` | WPF UI layer | Views/, ViewModels/, Services/, Controls/, Converters/, Themes/, Models/ |
| `ZayFlow.Core` | Domain & abstractions | Abstractions/, Domain/, Services/ |
| `ZayFlow.Actions` | Execution engine | Services/ (ActionExecutor, PreviewService, RiskAnalyzer, UndoManager) |
| `ZayFlow.Planner` | AI intent & planning | Services/ (RuleBasedPlanner, intent routing) |
| `ZayFlow.Infrastructure` | OS & file system | Services/ (FileScanner, external integrations) |
| `ZayFlow.Backend` | API backend | Contracts/, DTOs/, Models/, Persistence/, Services/ |

## Tech Stack

- **.NET 8.0-windows** / C# latest / Nullable enabled
- **WPF** with MVVM (ViewModelBase + RelayCommand)
- **Microsoft.Extensions.DependencyInjection** — all Singleton registrations in `App.xaml.cs`
- **Groq API** (Llama 3.3 70B) for AI
- **Segoe Fluent Icons** for iconography
- Dark/Light themes with `DynamicResource` binding

## File Naming Conventions

| Type | Location | Naming |
|------|----------|--------|
| Interface | `ZayFlow.Core/Abstractions/` | `I{Name}.cs` |
| Domain model | `ZayFlow.Core/Domain/` | `{Name}.cs` |
| ViewModel | `ZayFlow.App/ViewModels/` | `{Name}ViewModel.cs` |
| View | `ZayFlow.App/Views/` | `{Name}View.xaml` + `.xaml.cs` |
| Service impl | `{Layer}/Services/` | `{Name}.cs` (sealed) |
| Converter | `ZayFlow.App/Converters/` | `{Name}Converter.cs` |
| Control | `ZayFlow.App/Controls/` | `{Name}.cs` or `{Name}.xaml` |
| DTO | `ZayFlow.Backend/DTOs/` | `{Name}Dto.cs` |

## DI Registration Location

All services → `ZayFlow.App/App.xaml.cs` → `ConfigureServices()` method.
Pattern: `services.AddSingleton<IInterface, Implementation>();`

## Key Base Classes

- `ViewModelBase` — INotifyPropertyChanged with `SetProperty<T>()` and `OnPropertyChanged()`
- `RelayCommand` — ICommand with `Execute`, `CanExecute`, `RaiseCanExecuteChanged()`

## Theme Resources

Both files must stay in sync:
- `ZayFlow.App/Themes/LightTheme.xaml`
- `ZayFlow.App/Themes/DarkTheme.xaml`

Key resource names: `WindowBackgroundBrush`, `CardBackgroundBrush`, `AccentBrush`, `PrimaryTextBrush`, `SecondaryTextBrush`, `BorderBrush`
