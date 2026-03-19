# ZayFlow - Quick Start Guide

## Step 1: Prerequisites

Before building ZayFlow, ensure you have:
- **Visual Studio 2022** (version 17.8 or later)
  - With "Desktop development with C#" workload
  - With "Universal Windows Platform development" workload
- **.NET 8 SDK** (included with Visual Studio 2022)
- **Windows 10 SDK** (build 19041 or later)

## Step 2: Add Required Assets

The application requires image assets to run. Navigate to:
```
ZayFlow.App\Assets\
```

You need to add these files (see Assets\README.md for details):
- TrayIcon.ico (16x16, 32x32 icon file)
- Square150x150Logo.png
- Square44x44Logo.png
- Wide310x150Logo.png
- StoreLogo.png
- SplashScreen.png

### Quick Placeholder Creation
For testing purposes, you can create simple placeholder images:
1. Create solid color PNG files with the required dimensions
2. For TrayIcon.ico, use an online ICO converter or create a simple icon in Paint

## Step 3: Open the Solution

1. Navigate to the project directory
2. Double-click `ZayFlow.sln` to open in Visual Studio 2022

## Step 4: Restore NuGet Packages

Visual Studio should automatically restore packages. If not:
1. Right-click the solution in Solution Explorer
2. Select "Restore NuGet Packages"
3. Wait for restoration to complete

## Step 5: Build the Solution

1. Select **Build > Build Solution** (or press Ctrl+Shift+B)
2. Ensure all 5 projects build successfully
3. Check the Output window for any errors

## Step 6: Run the Application

1. Set `ZayFlow.App` as the startup project (if not already)
2. Select the desired platform (x64 recommended)
3. Press F5 to run with debugging (or Ctrl+F5 without debugging)

## What to Expect

When you run the application:

1. **No main window appears** - this is expected! The app runs in the system tray.
2. **Look for the tray icon** in the Windows system tray (bottom-right corner)
3. **Click the tray icon** to open the main window
4. **The main window displays**:
   - Command input TextBox
   - Three buttons: Preview, Execute, Undo
   - A ListView for results (currently empty/placeholder)

## Testing the Application

### Test the UI
1. Click the tray icon to open the window
2. Type some text in the command input box
3. Click "Preview" - you should see placeholder preview items
4. Click "Execute" - you should see placeholder execution confirmation
5. Click "Undo" - you should see placeholder undo confirmation

### Check Logging
Open the Output window in Visual Studio (View > Output) and select "Debug" to see log messages:
- Application startup logs
- Command execution logs
- Tray icon interaction logs

## Common Issues

### Issue: Application won't start
**Solution**: Ensure all required assets are in the Assets folder, especially TrayIcon.ico

### Issue: Can't find the tray icon
**Solution**: 
- Check the Windows system tray (bottom-right)
- Click the ^ arrow to show hidden icons
- The icon may be hidden if TrayIcon.ico is missing

### Issue: Build errors about missing assets
**Solution**: Add placeholder images to the Assets folder as described above

### Issue: NuGet package restore fails
**Solution**:
- Check your internet connection
- Clear NuGet cache: Tools > NuGet Package Manager > Package Manager Settings > Clear All NuGet Cache(s)
- Retry package restore

## Project Structure Overview

```
ZayFlow/
├── ZayFlow.App          # Main WinUI 3 application (STARTUP PROJECT)
├── ZayFlow.Core         # Domain models (placeholder for now)
├── ZayFlow.Actions      # Action handlers (placeholder for now)
├── ZayFlow.Planner      # AI planning (placeholder for now)
└── ZayFlow.Infrastructure # Infrastructure (placeholder for now)
```

## Next Development Steps

This is **Step 1 - Foundation Setup**. The application currently has:
- ✅ Complete MVVM infrastructure
- ✅ System tray integration
- ✅ Dependency injection
- ✅ Structured logging
- ✅ Clean architecture structure

**Not yet implemented** (future steps):
- ❌ Actual command parsing
- ❌ File system operations
- ❌ AI integration
- ❌ Command execution logic
- ❌ Undo/redo functionality

## Getting Help

For detailed documentation, see README.md in the project root.

For architecture details, examine:
- `App.xaml.cs` - DI configuration
- `MainViewModel.cs` - MVVM pattern implementation
- `TrayIconService.cs` - System tray integration
- `RelayCommand.cs` - Command pattern implementation

## Ready to Build!

You're all set! Open the solution in Visual Studio 2022 and press F5 to run.
