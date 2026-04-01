---
name: fullstack-ai-dev
description: "**WORKFLOW SKILL** — Full-stack WPF desktop app AI developer and code generator for ZayFlow. USE FOR: generating new features end-to-end across all layers; creating Views, ViewModels, Services, Models, Actions; AI-powered code scaffolding; adding new AI intents; building MVVM components; wiring dependency injection; creating themes and converters; implementing Clean Architecture patterns. DO NOT USE FOR: simple bug fixes in a single file; non-code tasks like documentation. INVOKES: file system tools, terminal for builds, subagents for codebase exploration."
argument-hint: 'Describe the feature, component, or code to generate'
---

# Full-Stack Desktop App AI Developer & Code Generator

Generate production-ready features, components, and services across all layers of the ZayFlow WPF desktop application following established architecture patterns and conventions.

## When to Use

- Build a new feature end-to-end (UI → ViewModel → Service → Core → Infrastructure)
- Scaffold a new View + ViewModel pair with MVVM bindings
- Add a new AI intent with execution handler
- Create a new service interface in Core and implement it in Infrastructure
- Add new action types to the Actions layer
- Generate converters, themes, or custom controls
- Wire up dependency injection for new components

## Architecture Overview

```
ZayFlow.App          → UI layer: Views (XAML), ViewModels, Controls, Converters, Themes
ZayFlow.Core         → Domain: Interfaces (Abstractions/), Domain models, Core services
ZayFlow.Actions      → Execution: ActionExecutor, PreviewService, RiskAnalyzer, UndoManager
ZayFlow.Planner      → AI Planning: Intent parsing, plan generation
ZayFlow.Infrastructure → External: File system, OS integration, external APIs
ZayFlow.Backend      → API: Contracts, DTOs, Persistence, Backend services
```

**Dependency direction:** App → Core ← Infrastructure/Actions/Planner/Backend

## Procedure

### Step 1 — Analyze the Request

Determine which layers are affected:

| Request Type | Layers Touched |
|-------------|----------------|
| New UI feature | App (View + ViewModel) + Core (interface) + one or more implementation layers |
| New AI intent | Planner (intent routing) + Actions (executor) + App (UI feedback) |
| New service | Core (interface) + Infrastructure/Actions (implementation) + App (DI registration) |
| New UI component only | App (View + ViewModel or Control + Converter) |
| New data model | Core (Domain/) or Backend (Models/ + DTOs/) |

### Step 2 — Scaffold Bottom-Up (Core → Implementation → App)

**Always start from the Core layer and work upward.** This ensures interfaces exist before implementations.

#### 2a. Core Layer — Interface & Domain Models

Create interface in `ZayFlow.Core/Abstractions/`:

```csharp
// Pattern: I{ServiceName}.cs
public interface INewFeatureService
{
    Task<ResultType> DoWorkAsync(InputType input, CancellationToken cancellationToken = default);
}
```

Create domain models in `ZayFlow.Core/Domain/` if needed.

#### 2b. Implementation Layer — Service

Create sealed implementation in the appropriate project:

```csharp
// Pattern: sealed class with constructor DI, structured logging, ConfigureAwait(false)
public sealed class NewFeatureService : INewFeatureService
{
    private readonly ILogger<NewFeatureService> _logger;

    public NewFeatureService(ILogger<NewFeatureService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResultType> DoWorkAsync(InputType input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Implementation...
        _logger.LogInformation("Work completed. Input: {Input}", input);
        return result;
    }
}
```

#### 2c. App Layer — ViewModel

Create ViewModel in `ZayFlow.App/ViewModels/`:

```csharp
// Pattern: inherits ViewModelBase, DI constructor, RelayCommand, SetProperty
public class NewFeatureViewModel : ViewModelBase
{
    private readonly INewFeatureService _service;
    private readonly ILogger<NewFeatureViewModel> _logger;

    private string _someProperty = string.Empty;
    private bool _isBusy;

    public NewFeatureViewModel(
        INewFeatureService service,
        ILogger<NewFeatureViewModel>? logger = null)
    {
        _service = service;
        _logger = logger ?? NullLogger<NewFeatureViewModel>.Instance;

        ActionCommand = new RelayCommand(OnAction, _ => !IsBusy);
    }

    public ICommand ActionCommand { get; }

    public string SomeProperty
    {
        get => _someProperty;
        set => SetProperty(ref _someProperty, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                ((RelayCommand)ActionCommand).RaiseCanExecuteChanged();
        }
    }
}
```

#### 2d. App Layer — View (XAML)

Create View in `ZayFlow.App/Views/`:

```xml
<UserControl x:Class="ZayFlow.App.Views.NewFeatureView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:ZayFlow.App.Converters">
    <UserControl.Resources>
        <converters:BoolToVisibilityConverter x:Key="BoolVis"/>
    </UserControl.Resources>
    <Grid Background="{DynamicResource WindowBackgroundBrush}">
        <!-- Use DynamicResource for all theme brushes -->
        <!-- Use {Binding PropertyName} for ViewModel binding -->
        <!-- Use {Binding Command} for ICommand binding -->
    </Grid>
</UserControl>
```

Codebehind pattern — view-specific logic only (scrolling, keyboard, drag/drop):

```csharp
public partial class NewFeatureView : UserControl
{
    public NewFeatureView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    // Unsubscribe collection listeners in OnUnloaded to prevent memory leaks
}
```

#### 2e. Register in DI Container

Add to `ConfigureServices()` in `ZayFlow.App/App.xaml.cs`:

```csharp
// Register interface → implementation as Singleton
services.AddSingleton<INewFeatureService, NewFeatureService>();
// Register ViewModel
services.AddSingleton<NewFeatureViewModel>();
```

### Step 3 — Add Theme Support (if UI is involved)

Add any new theme resources to BOTH theme files:
- `ZayFlow.App/Themes/LightTheme.xaml`
- `ZayFlow.App/Themes/DarkTheme.xaml`

Follow the naming convention: `{Element}{State}Color` for Colors, `{Element}{State}Brush` for Brushes.

### Step 4 — Build & Validate

Run the build to verify all layers compile:

```
dotnet build ZayFlow.sln -c Debug
```

Check for:
- Missing DI registrations (runtime `InvalidOperationException`)
- Namespace imports across project boundaries
- XAML binding errors in Output window

## Code Style Rules

| Area | Convention |
|------|-----------|
| **Naming** | `PascalCase` public, `_camelCase` private fields, `I` prefix for interfaces |
| **Classes** | `sealed` for service implementations, `abstract` for base classes |
| **Properties** | `SetProperty<T>(ref field, value)` in ViewModels |
| **Collections** | `ObservableCollection<T>` for UI, `IReadOnlyList<T>` for service returns |
| **Async** | `.ConfigureAwait(false)` in non-UI code, `CancellationToken` on all async methods |
| **Logging** | Structured: `_logger.LogInformation("Message {Param}", value)` |
| **DI** | Constructor injection, `Singleton` lifetime, register in `App.xaml.cs` |
| **Null checks** | `ArgumentNullException.ThrowIfNull()` or `?? throw new ArgumentNullException()` |
| **UI prefix** | Helper binding classes: `UI{Name}Item` (e.g., `UIAttachmentItem`, `UIChatMessage`) |
| **Commands** | `RelayCommand` with `CanExecute` predicates, `RaiseCanExecuteChanged()` on state change |
| **XAML Resources** | `{DynamicResource BrushName}` for themes, `{StaticResource Key}` for converters |
| **Target** | .NET 8.0-windows, C# latest, nullable enabled |

## AI Intent Generation Pattern

When adding a new AI intent:

1. Define the intent string constant and routing in `ZayFlow.Planner/Services/`
2. Create the handler method in the appropriate handler class (`FileToolsHandler`, `MediaHandler`, `NetworkHandler`) or a new handler in `ZayFlow.App/Services/`
3. Wire intent → handler mapping in `IntentExecutionService`
4. Add preview support if the action is destructive (via `PreviewService`)
5. Add undo support if the action is reversible (via `UndoManager`)

## Quality Checks

Before marking a feature complete, verify:

- [ ] All new interfaces are in `ZayFlow.Core/Abstractions/`
- [ ] All implementations are `sealed` with constructor null checks
- [ ] DI registration added to `App.xaml.cs`
- [ ] XAML uses `DynamicResource` for theme brushes (not hardcoded colors)
- [ ] Async methods accept `CancellationToken`
- [ ] ViewModel properties use `SetProperty<T>()` pattern
- [ ] Commands use `RelayCommand` with `CanExecute` guards
- [ ] Structured logging with named parameters
- [ ] Solution builds clean: `dotnet build ZayFlow.sln`
