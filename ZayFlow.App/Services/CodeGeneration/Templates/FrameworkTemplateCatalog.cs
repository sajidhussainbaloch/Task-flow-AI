namespace ZayFlow.App.Services.CodeGeneration.Templates;

/// <summary>
/// Static catalog of framework templates for 15+ known project types.
/// Provides concrete file lists, content markers, folder conventions, and verification goals
/// so the planning layer knows exactly what to generate instead of guessing.
/// </summary>
public sealed class FrameworkTemplateCatalog
{
    private static readonly Dictionary<string, FrameworkTemplate> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flutter"] = BuildFlutter(),
        ["react"] = BuildReact(),
        ["vue"] = BuildVue(),
        ["angular"] = BuildAngular(),
        ["nextjs"] = BuildNextJs(),
        ["dotnet-console"] = BuildDotNetConsole(),
        ["dotnet-web"] = BuildDotNetWeb(),
        ["python-flask"] = BuildPythonFlask(),
        ["python-django"] = BuildPythonDjango(),
        ["python-package"] = BuildPythonPackage(),
        ["rust"] = BuildRust(),
        ["go"] = BuildGo(),
        ["swift-ios"] = BuildSwiftIos(),
        ["kotlin-android"] = BuildKotlinAndroid(),
        ["express"] = BuildExpress(),
        ["electron"] = BuildElectron(),
        ["spring-boot"] = BuildSpringBoot(),
        ["php-laravel"] = BuildPhpLaravel(),
    };

    /// <summary>Get a template by framework ID. Returns null if not found.</summary>
    public FrameworkTemplate? Get(string frameworkId)
    {
        return Templates.GetValueOrDefault(frameworkId);
    }

    /// <summary>Get all available framework templates.</summary>
    public IReadOnlyDictionary<string, FrameworkTemplate> GetAll() => Templates;

    /// <summary>
    /// Detect a framework from the user's language and goal text.
    /// Returns the best-matching template, or null if no known framework is detected.
    /// </summary>
    public FrameworkTemplate? TryDetect(string language, string goal)
    {
        if (string.IsNullOrWhiteSpace(language) && string.IsNullOrWhiteSpace(goal))
            return null;

        var combined = $"{language} {goal}".ToLowerInvariant();

        FrameworkTemplate? bestMatch = null;
        int bestScore = 0;

        foreach (var (_, template) in Templates)
        {
            int score = 0;
            foreach (var keyword in template.DetectionKeywords)
            {
                if (combined.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    score++;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = template;
            }
        }

        return bestScore > 0 ? bestMatch : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Flutter
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildFlutter() => new()
    {
        FrameworkId = "flutter",
        DisplayName = "Flutter",
        PrimaryLanguage = "dart",
        MinimumFileCount = 6,
        DetectionKeywords = ["flutter", "dart", "pubspec"],
        BootstrapCommand = "flutter create .",
        RunCommand = "flutter run",
        TestCommand = "flutter test",
        BuildCommand = "flutter build",
        BootstrapHint = "After file creation, 'flutter create .' generates platform folders (android/, ios/, web/, windows/) and resolves packages.",
        RequiredFiles =
        [
            new TemplateFile
            {
                RelativePath = "pubspec.yaml",
                Description = "Project manifest with dependencies and Flutter SDK constraint",
                Required = true,
                Language = "yaml",
                ContentMarkers = ["name:", "flutter:", "sdk: flutter", "dependencies:"]
            },
            new TemplateFile
            {
                RelativePath = "lib/main.dart",
                Description = "Application entry point with runApp() and root widget",
                Required = true,
                Language = "dart",
                ContentMarkers = ["import 'package:flutter/material.dart'", "runApp(", "MaterialApp("]
            },
        ],
        RecommendedFiles =
        [
            new TemplateFile
            {
                RelativePath = "lib/screens/home_screen.dart",
                Description = "Home screen widget separated from main",
                Required = false,
                Language = "dart",
                ContentMarkers = ["StatefulWidget", "Scaffold("]
            },
            new TemplateFile
            {
                RelativePath = "lib/widgets/app_drawer.dart",
                Description = "Reusable navigation drawer or widget",
                Required = false,
                Language = "dart",
                ContentMarkers = ["StatelessWidget"]
            },
            new TemplateFile
            {
                RelativePath = "test/widget_test.dart",
                Description = "Widget test for the app",
                Required = false,
                Language = "dart",
                ContentMarkers = ["testWidgets(", "import 'package:flutter_test/flutter_test.dart'"]
            },
            new TemplateFile
            {
                RelativePath = "analysis_options.yaml",
                Description = "Dart analysis configuration",
                Required = false,
                Language = "yaml",
                ContentMarkers = ["include: package:flutter_lints/flutter.yaml"]
            },
            new TemplateFile
            {
                RelativePath = "README.md",
                Description = "Project readme with setup instructions",
                Required = false,
                Language = "markdown",
                ContentMarkers = []
            },
            new TemplateFile
            {
                RelativePath = ".gitignore",
                Description = "Git ignore rules for Flutter projects",
                Required = false,
                Language = "text",
                ContentMarkers = [".dart_tool/", "build/"]
            },
        ],
        FolderConventions = ["lib/", "lib/screens/", "lib/widgets/", "test/"],
        AcceptanceCriteria =
        [
            "pubspec.yaml must declare flutter SDK dependency",
            "lib/main.dart must call runApp() with a MaterialApp or CupertinoApp",
            "At least one screen widget must exist in lib/screens/",
            "Test file must import flutter_test and contain at least one testWidgets call",
            "analysis_options.yaml should enable recommended lints",
        ],
        VerificationGoals =
        [
            "All Dart files have correct imports matching actual file paths",
            "pubspec.yaml is valid YAML with required flutter keys",
            "Widget tree is consistent (referenced widgets exist)",
            "No placeholder or stub code (TODO, pass, ...)",
        ],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // React (Create React App style)
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildReact() => new()
    {
        FrameworkId = "react",
        DisplayName = "React",
        PrimaryLanguage = "javascript",
        MinimumFileCount = 5,
        DetectionKeywords = ["react", "react.js", "reactjs", "jsx", "create react app"],
        BootstrapCommand = "npm install",
        RunCommand = "npm start",
        TestCommand = "npm test",
        BuildCommand = "npm run build",
        BootstrapHint = "Run 'npm install' to install dependencies, then 'npm start' to launch the dev server.",
        RequiredFiles =
        [
            new TemplateFile
            {
                RelativePath = "package.json",
                Description = "NPM package manifest with React dependencies and scripts",
                Required = true,
                Language = "json",
                ContentMarkers = ["\"react\"", "\"react-dom\"", "\"scripts\""]
            },
            new TemplateFile
            {
                RelativePath = "src/index.js",
                Description = "Application entry point that renders the root component",
                Required = true,
                Language = "javascript",
                ContentMarkers = ["import React", "import ReactDOM", "createRoot("]
            },
            new TemplateFile
            {
                RelativePath = "src/App.js",
                Description = "Root application component",
                Required = true,
                Language = "javascript",
                ContentMarkers = ["function App", "export default App"]
            },
            new TemplateFile
            {
                RelativePath = "public/index.html",
                Description = "HTML template with root div mount point",
                Required = true,
                Language = "html",
                ContentMarkers = ["<div id=\"root\">", "<!DOCTYPE html>"]
            },
        ],
        RecommendedFiles =
        [
            new TemplateFile
            {
                RelativePath = "src/App.css",
                Description = "Main application styles",
                Required = false,
                Language = "css",
                ContentMarkers = [".App"]
            },
            new TemplateFile
            {
                RelativePath = "src/components/Header.js",
                Description = "Reusable header component",
                Required = false,
                Language = "javascript",
                ContentMarkers = ["export default"]
            },
            new TemplateFile
            {
                RelativePath = "README.md",
                Description = "Project readme",
                Required = false,
                Language = "markdown",
                ContentMarkers = []
            },
            new TemplateFile
            {
                RelativePath = ".gitignore",
                Description = "Git ignore rules",
                Required = false,
                Language = "text",
                ContentMarkers = ["node_modules"]
            },
        ],
        FolderConventions = ["src/", "src/components/", "public/"],
        AcceptanceCriteria =
        [
            "package.json must include react and react-dom as dependencies",
            "src/index.js must import and render the App component into the root div",
            "src/App.js must export a valid React component",
            "public/index.html must have a div with id 'root'",
        ],
        VerificationGoals =
        [
            "All imports resolve to actual files in the project",
            "package.json is valid JSON with required scripts",
            "Components export default correctly",
            "No placeholder text or TODO stubs",
        ],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Vue.js
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildVue() => new()
    {
        FrameworkId = "vue",
        DisplayName = "Vue.js",
        PrimaryLanguage = "javascript",
        MinimumFileCount = 5,
        DetectionKeywords = ["vue", "vue.js", "vuejs", "vue3", "vite vue"],
        BootstrapCommand = "npm install",
        RunCommand = "npm run dev",
        TestCommand = "npm run test",
        BuildCommand = "npm run build",
        BootstrapHint = "Run 'npm install' then 'npm run dev' for the Vite dev server.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "package.json", Description = "NPM manifest with Vue dependency", Required = true, Language = "json", ContentMarkers = ["\"vue\"", "\"scripts\""] },
            new TemplateFile { RelativePath = "src/main.js", Description = "App entry point mounting Vue instance", Required = true, Language = "javascript", ContentMarkers = ["createApp(", "import App"] },
            new TemplateFile { RelativePath = "src/App.vue", Description = "Root single-file component", Required = true, Language = "vue", ContentMarkers = ["<template>", "<script>", "<style>"] },
            new TemplateFile { RelativePath = "index.html", Description = "HTML shell with app mount point", Required = true, Language = "html", ContentMarkers = ["<div id=\"app\">"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "vite.config.js", Description = "Vite configuration", Required = false, Language = "javascript", ContentMarkers = ["defineConfig("] },
            new TemplateFile { RelativePath = "src/components/HelloWorld.vue", Description = "Sample component", Required = false, Language = "vue", ContentMarkers = ["<template>"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore rules", Required = false, Language = "text", ContentMarkers = ["node_modules"] },
        ],
        FolderConventions = ["src/", "src/components/", "public/"],
        AcceptanceCriteria = ["package.json includes vue dependency", "App.vue is a valid single-file component with template/script/style sections", "main.js creates and mounts Vue app"],
        VerificationGoals = ["All component imports resolve", "package.json is valid JSON", "Vue SFC structure is correct"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Angular
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildAngular() => new()
    {
        FrameworkId = "angular",
        DisplayName = "Angular",
        PrimaryLanguage = "typescript",
        MinimumFileCount = 7,
        DetectionKeywords = ["angular", "ng ", "angular.js", "angularjs"],
        BootstrapCommand = "npm install",
        RunCommand = "ng serve",
        TestCommand = "ng test",
        BuildCommand = "ng build",
        BootstrapHint = "Run 'npm install' then 'ng serve' to launch the Angular dev server.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "package.json", Description = "NPM manifest with Angular dependencies", Required = true, Language = "json", ContentMarkers = ["\"@angular/core\"", "\"scripts\""] },
            new TemplateFile { RelativePath = "tsconfig.json", Description = "TypeScript configuration", Required = true, Language = "json", ContentMarkers = ["\"compilerOptions\""] },
            new TemplateFile { RelativePath = "angular.json", Description = "Angular CLI workspace configuration", Required = true, Language = "json", ContentMarkers = ["\"projects\""] },
            new TemplateFile { RelativePath = "src/main.ts", Description = "Application bootstrap entry point", Required = true, Language = "typescript", ContentMarkers = ["bootstrapApplication(", "import { AppComponent }"] },
            new TemplateFile { RelativePath = "src/app/app.component.ts", Description = "Root application component", Required = true, Language = "typescript", ContentMarkers = ["@Component(", "selector:"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "src/app/app.component.html", Description = "Root component template", Required = false, Language = "html", ContentMarkers = [] },
            new TemplateFile { RelativePath = "src/app/app.component.css", Description = "Root component styles", Required = false, Language = "css", ContentMarkers = [] },
            new TemplateFile { RelativePath = "src/index.html", Description = "Main HTML page", Required = false, Language = "html", ContentMarkers = ["<app-root>"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["src/", "src/app/", "src/assets/"],
        AcceptanceCriteria = ["package.json includes @angular/core", "AppComponent has @Component decorator", "main.ts bootstraps the application"],
        VerificationGoals = ["TypeScript compiles (imports resolve)", "Angular decorators are syntactically correct", "Template references match component selectors"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Next.js
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildNextJs() => new()
    {
        FrameworkId = "nextjs",
        DisplayName = "Next.js",
        PrimaryLanguage = "javascript",
        MinimumFileCount = 4,
        DetectionKeywords = ["next.js", "nextjs", "next js", "next app"],
        BootstrapCommand = "npm install",
        RunCommand = "npm run dev",
        TestCommand = "npm test",
        BuildCommand = "npm run build",
        BootstrapHint = "Run 'npm install' then 'npm run dev' for the Next.js dev server.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "package.json", Description = "NPM manifest with Next.js dependency", Required = true, Language = "json", ContentMarkers = ["\"next\"", "\"react\"", "\"scripts\""] },
            new TemplateFile { RelativePath = "app/layout.js", Description = "Root layout component", Required = true, Language = "javascript", ContentMarkers = ["export default", "<html"] },
            new TemplateFile { RelativePath = "app/page.js", Description = "Home page component", Required = true, Language = "javascript", ContentMarkers = ["export default"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "next.config.js", Description = "Next.js configuration", Required = false, Language = "javascript", ContentMarkers = ["module.exports"] },
            new TemplateFile { RelativePath = "app/globals.css", Description = "Global styles", Required = false, Language = "css", ContentMarkers = [] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = ["node_modules", ".next"] },
        ],
        FolderConventions = ["app/", "public/"],
        AcceptanceCriteria = ["package.json includes next and react", "App router layout exists at app/layout.js", "At least one page exists at app/page.js"],
        VerificationGoals = ["All imports resolve", "Layout wraps children correctly", "Page exports a default function component"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // .NET Console App
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildDotNetConsole() => new()
    {
        FrameworkId = "dotnet-console",
        DisplayName = ".NET Console App",
        PrimaryLanguage = "csharp",
        MinimumFileCount = 2,
        DetectionKeywords = ["c# console", "csharp console", ".net console", "dotnet console", "c# app", "c# project"],
        BootstrapCommand = "dotnet restore",
        RunCommand = "dotnet run",
        TestCommand = "dotnet test",
        BuildCommand = "dotnet build",
        BootstrapHint = "Run 'dotnet restore' then 'dotnet run' to build and execute.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "Program.cs", Description = "Application entry point", Required = true, Language = "csharp", ContentMarkers = [] },
            new TemplateFile { RelativePath = "*.csproj", Description = ".NET project file with SDK and target framework", Required = true, Language = "xml", ContentMarkers = ["<Project Sdk=", "<TargetFramework>"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore for .NET", Required = false, Language = "text", ContentMarkers = ["bin/", "obj/"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = [],
        AcceptanceCriteria = [".csproj has valid Sdk attribute and TargetFramework", "Program.cs has a main entry point or top-level statements"],
        VerificationGoals = [".csproj is valid XML", "Program.cs compiles (proper using directives)", "No placeholder code"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // .NET Web API (ASP.NET Core)
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildDotNetWeb() => new()
    {
        FrameworkId = "dotnet-web",
        DisplayName = "ASP.NET Core Web API",
        PrimaryLanguage = "csharp",
        MinimumFileCount = 4,
        DetectionKeywords = ["asp.net", "web api", "dotnet web", ".net api", ".net web", "c# api", "c# web"],
        BootstrapCommand = "dotnet restore",
        RunCommand = "dotnet run",
        TestCommand = "dotnet test",
        BuildCommand = "dotnet build",
        BootstrapHint = "Run 'dotnet restore' then 'dotnet run'. API will be available at https://localhost:5001.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "Program.cs", Description = "Web host configuration and pipeline setup", Required = true, Language = "csharp", ContentMarkers = ["WebApplication.CreateBuilder(", "app.MapControllers("] },
            new TemplateFile { RelativePath = "*.csproj", Description = ".NET project file", Required = true, Language = "xml", ContentMarkers = ["<Project Sdk=", "Microsoft.AspNetCore"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "Controllers/WeatherController.cs", Description = "Sample API controller", Required = false, Language = "csharp", ContentMarkers = ["[ApiController]", "[Route("] },
            new TemplateFile { RelativePath = "appsettings.json", Description = "Application configuration", Required = false, Language = "json", ContentMarkers = ["\"Logging\""] },
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = ["bin/", "obj/"] },
        ],
        FolderConventions = ["Controllers/"],
        AcceptanceCriteria = ["Program.cs sets up web application builder", ".csproj references ASP.NET Core SDK", "At least one controller with REST endpoints"],
        VerificationGoals = ["Controller routing attributes are valid", "Program.cs pipeline is syntactically correct", "appsettings.json is valid JSON"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Python + Flask
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildPythonFlask() => new()
    {
        FrameworkId = "python-flask",
        DisplayName = "Python Flask",
        PrimaryLanguage = "python",
        MinimumFileCount = 3,
        DetectionKeywords = ["flask", "python web", "python api", "python server"],
        BootstrapCommand = "pip install -r requirements.txt",
        RunCommand = "python app.py",
        TestCommand = "python -m pytest",
        BuildCommand = "",
        BootstrapHint = "Run 'pip install -r requirements.txt' then 'python app.py' to start the Flask server.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "app.py", Description = "Flask application with routes", Required = true, Language = "python", ContentMarkers = ["from flask import Flask", "app = Flask(", "@app.route("] },
            new TemplateFile { RelativePath = "requirements.txt", Description = "Python package dependencies", Required = true, Language = "text", ContentMarkers = ["flask"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "templates/index.html", Description = "Jinja2 HTML template", Required = false, Language = "html", ContentMarkers = [] },
            new TemplateFile { RelativePath = "static/style.css", Description = "Static CSS", Required = false, Language = "css", ContentMarkers = [] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = ["__pycache__", "venv/"] },
        ],
        FolderConventions = ["templates/", "static/"],
        AcceptanceCriteria = ["app.py creates Flask instance and defines routes", "requirements.txt lists flask as dependency", "At least one route returns a response"],
        VerificationGoals = ["Flask app object is created", "Route decorators have valid paths", "requirements.txt lists all imported packages"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Python + Django
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildPythonDjango() => new()
    {
        FrameworkId = "python-django",
        DisplayName = "Django",
        PrimaryLanguage = "python",
        MinimumFileCount = 6,
        DetectionKeywords = ["django", "django rest", "django api"],
        BootstrapCommand = "pip install -r requirements.txt",
        RunCommand = "python manage.py runserver",
        TestCommand = "python manage.py test",
        BuildCommand = "",
        BootstrapHint = "Run 'pip install -r requirements.txt' then 'python manage.py runserver'.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "manage.py", Description = "Django management script", Required = true, Language = "python", ContentMarkers = ["django", "execute_from_command_line("] },
            new TemplateFile { RelativePath = "requirements.txt", Description = "Python dependencies", Required = true, Language = "text", ContentMarkers = ["django"] },
            new TemplateFile { RelativePath = "config/settings.py", Description = "Django settings", Required = true, Language = "python", ContentMarkers = ["INSTALLED_APPS", "DATABASES", "SECRET_KEY"] },
            new TemplateFile { RelativePath = "config/urls.py", Description = "Root URL configuration", Required = true, Language = "python", ContentMarkers = ["urlpatterns", "path("] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "config/wsgi.py", Description = "WSGI config for deployment", Required = false, Language = "python", ContentMarkers = ["get_wsgi_application("] },
            new TemplateFile { RelativePath = "app/views.py", Description = "Application views", Required = false, Language = "python", ContentMarkers = ["def "] },
            new TemplateFile { RelativePath = "app/models.py", Description = "Database models", Required = false, Language = "python", ContentMarkers = ["from django.db import models"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["config/", "app/", "templates/"],
        AcceptanceCriteria = ["manage.py can invoke Django commands", "settings.py defines INSTALLED_APPS and DATABASES", "urls.py defines at least one URL pattern"],
        VerificationGoals = ["Django settings has required keys", "URL patterns are syntactically valid", "Views are defined in the correct app module"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Python Package
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildPythonPackage() => new()
    {
        FrameworkId = "python-package",
        DisplayName = "Python Package",
        PrimaryLanguage = "python",
        MinimumFileCount = 4,
        DetectionKeywords = ["python package", "python library", "python module", "pypi", "pip package"],
        BootstrapCommand = "pip install -e .",
        RunCommand = "python -m mypackage",
        TestCommand = "python -m pytest",
        BuildCommand = "python -m build",
        BootstrapHint = "Run 'pip install -e .' for editable install, then 'python -m pytest' for tests.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "pyproject.toml", Description = "Modern Python package configuration", Required = true, Language = "toml", ContentMarkers = ["[build-system]", "[project]", "name ="] },
            new TemplateFile { RelativePath = "src/__init__.py", Description = "Package init", Required = true, Language = "python", ContentMarkers = [] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "src/core.py", Description = "Main package logic", Required = false, Language = "python", ContentMarkers = [] },
            new TemplateFile { RelativePath = "tests/test_core.py", Description = "Unit tests", Required = false, Language = "python", ContentMarkers = ["def test_", "import pytest"] },
            new TemplateFile { RelativePath = "README.md", Description = "Package readme", Required = false, Language = "markdown", ContentMarkers = [] },
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = ["__pycache__", "dist/"] },
        ],
        FolderConventions = ["src/", "tests/"],
        AcceptanceCriteria = ["pyproject.toml has build-system and project metadata", "Package directory has __init__.py", "At least one test file"],
        VerificationGoals = ["pyproject.toml is valid TOML", "Package imports resolve", "Tests use proper assertion syntax"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Rust
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildRust() => new()
    {
        FrameworkId = "rust",
        DisplayName = "Rust",
        PrimaryLanguage = "rust",
        MinimumFileCount = 3,
        DetectionKeywords = ["rust", "cargo", "rustlang"],
        BootstrapCommand = "cargo fetch",
        RunCommand = "cargo run",
        TestCommand = "cargo test",
        BuildCommand = "cargo build",
        BootstrapHint = "Run 'cargo build' to compile, 'cargo run' to execute.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "Cargo.toml", Description = "Cargo project manifest", Required = true, Language = "toml", ContentMarkers = ["[package]", "name =", "edition ="] },
            new TemplateFile { RelativePath = "src/main.rs", Description = "Application entry point", Required = true, Language = "rust", ContentMarkers = ["fn main()"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = ["/target"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["src/"],
        AcceptanceCriteria = ["Cargo.toml has [package] with name and edition", "src/main.rs has fn main() entry point"],
        VerificationGoals = ["Cargo.toml is valid TOML", "Rust syntax is correct (braces match, semicolons present)", "fn main() exists"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Go
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildGo() => new()
    {
        FrameworkId = "go",
        DisplayName = "Go",
        PrimaryLanguage = "go",
        MinimumFileCount = 2,
        DetectionKeywords = ["golang", "go lang", "go project", "go app"],
        BootstrapCommand = "go mod tidy",
        RunCommand = "go run .",
        TestCommand = "go test ./...",
        BuildCommand = "go build",
        BootstrapHint = "Run 'go mod tidy' to resolve dependencies, 'go run .' to execute.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "go.mod", Description = "Go module definition", Required = true, Language = "go", ContentMarkers = ["module "] },
            new TemplateFile { RelativePath = "main.go", Description = "Application entry point", Required = true, Language = "go", ContentMarkers = ["package main", "func main()"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = [] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = [],
        AcceptanceCriteria = ["go.mod has module directive", "main.go has package main and func main()"],
        VerificationGoals = ["Go syntax is correct", "Module path is valid", "All imports exist"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Swift / iOS
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildSwiftIos() => new()
    {
        FrameworkId = "swift-ios",
        DisplayName = "Swift / iOS (SwiftUI)",
        PrimaryLanguage = "swift",
        MinimumFileCount = 4,
        DetectionKeywords = ["swift", "ios", "swiftui", "xcode", "iphone app", "ipad app", "apple"],
        BootstrapCommand = "",
        RunCommand = "open *.xcodeproj",
        TestCommand = "",
        BuildCommand = "xcodebuild",
        BootstrapHint = "Open the .xcodeproj in Xcode and run on a simulator or device.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "Package.swift", Description = "Swift Package Manager manifest", Required = true, Language = "swift", ContentMarkers = ["import PackageDescription", "Package("] },
            new TemplateFile { RelativePath = "Sources/App.swift", Description = "SwiftUI App entry point", Required = true, Language = "swift", ContentMarkers = ["@main", "struct", ": App", "WindowGroup"] },
            new TemplateFile { RelativePath = "Sources/ContentView.swift", Description = "Main content view", Required = true, Language = "swift", ContentMarkers = ["struct ContentView", ": View", "var body"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "Sources/Models/Item.swift", Description = "Data model", Required = false, Language = "swift", ContentMarkers = ["struct"] },
            new TemplateFile { RelativePath = "Tests/AppTests.swift", Description = "Unit tests", Required = false, Language = "swift", ContentMarkers = ["import XCTest", "func test"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["Sources/", "Sources/Models/", "Tests/"],
        AcceptanceCriteria = ["App.swift has @main attribute and WindowGroup", "ContentView has body property returning some View", "Package.swift defines the target"],
        VerificationGoals = ["Swift syntax is correct", "SwiftUI view protocol conformance is valid", "All imports resolve"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Kotlin / Android
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildKotlinAndroid() => new()
    {
        FrameworkId = "kotlin-android",
        DisplayName = "Kotlin Android (Jetpack Compose)",
        PrimaryLanguage = "kotlin",
        MinimumFileCount = 5,
        DetectionKeywords = ["android", "kotlin", "jetpack compose", "android app", "kotlin app"],
        BootstrapCommand = "./gradlew build",
        RunCommand = "./gradlew installDebug",
        TestCommand = "./gradlew test",
        BuildCommand = "./gradlew assembleDebug",
        BootstrapHint = "Open in Android Studio or run './gradlew build'. Requires Android SDK.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "build.gradle.kts", Description = "Root Gradle build script", Required = true, Language = "kotlin", ContentMarkers = ["plugins {", "android {"] },
            new TemplateFile { RelativePath = "settings.gradle.kts", Description = "Gradle settings", Required = true, Language = "kotlin", ContentMarkers = ["rootProject.name"] },
            new TemplateFile { RelativePath = "app/src/main/AndroidManifest.xml", Description = "Android manifest", Required = true, Language = "xml", ContentMarkers = ["<manifest", "<application", "<activity"] },
            new TemplateFile { RelativePath = "app/src/main/java/com/example/app/MainActivity.kt", Description = "Main activity with Compose", Required = true, Language = "kotlin", ContentMarkers = ["class MainActivity", "setContent {"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "app/src/main/java/com/example/app/ui/theme/Theme.kt", Description = "Compose theme", Required = false, Language = "kotlin", ContentMarkers = ["@Composable"] },
            new TemplateFile { RelativePath = "gradle.properties", Description = "Gradle properties", Required = false, Language = "text", ContentMarkers = [] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["app/", "app/src/main/java/", "app/src/main/res/"],
        AcceptanceCriteria = ["build.gradle.kts has Android plugin", "AndroidManifest.xml declares the main activity", "MainActivity uses Jetpack Compose setContent"],
        VerificationGoals = ["Kotlin syntax is correct", "Gradle scripts are syntactically valid", "Compose annotations are used correctly"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Express.js (Node.js backend)
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildExpress() => new()
    {
        FrameworkId = "express",
        DisplayName = "Express.js (Node.js)",
        PrimaryLanguage = "javascript",
        MinimumFileCount = 3,
        DetectionKeywords = ["express", "express.js", "expressjs", "node api", "node server", "node backend", "node.js api"],
        BootstrapCommand = "npm install",
        RunCommand = "node server.js",
        TestCommand = "npm test",
        BuildCommand = "",
        BootstrapHint = "Run 'npm install' then 'node server.js' to start the Express server.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "package.json", Description = "NPM manifest with Express dependency", Required = true, Language = "json", ContentMarkers = ["\"express\"", "\"scripts\""] },
            new TemplateFile { RelativePath = "server.js", Description = "Express server with routes", Required = true, Language = "javascript", ContentMarkers = ["require('express')", "app.listen(", "app.get("] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "routes/index.js", Description = "Route definitions", Required = false, Language = "javascript", ContentMarkers = ["router."] },
            new TemplateFile { RelativePath = ".env", Description = "Environment variables", Required = false, Language = "text", ContentMarkers = ["PORT="] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
            new TemplateFile { RelativePath = ".gitignore", Description = "Git ignore", Required = false, Language = "text", ContentMarkers = ["node_modules"] },
        ],
        FolderConventions = ["routes/"],
        AcceptanceCriteria = ["package.json includes express", "server.js creates Express app and listens on a port", "At least one GET/POST route exists"],
        VerificationGoals = ["All require() calls reference installed packages", "Routes have valid HTTP methods and paths", "Server listens on a configurable port"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Electron
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildElectron() => new()
    {
        FrameworkId = "electron",
        DisplayName = "Electron Desktop App",
        PrimaryLanguage = "javascript",
        MinimumFileCount = 4,
        DetectionKeywords = ["electron", "electron app", "desktop app javascript", "electron.js"],
        BootstrapCommand = "npm install",
        RunCommand = "npm start",
        TestCommand = "npm test",
        BuildCommand = "npm run build",
        BootstrapHint = "Run 'npm install' then 'npm start' to launch the Electron app.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "package.json", Description = "NPM manifest with Electron", Required = true, Language = "json", ContentMarkers = ["\"electron\"", "\"main\":", "\"scripts\""] },
            new TemplateFile { RelativePath = "main.js", Description = "Electron main process", Required = true, Language = "javascript", ContentMarkers = ["BrowserWindow", "app.whenReady(", "loadFile("] },
            new TemplateFile { RelativePath = "index.html", Description = "Renderer HTML page", Required = true, Language = "html", ContentMarkers = ["<!DOCTYPE html>"] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "renderer.js", Description = "Renderer process script", Required = false, Language = "javascript", ContentMarkers = [] },
            new TemplateFile { RelativePath = "preload.js", Description = "Preload script for IPC bridge", Required = false, Language = "javascript", ContentMarkers = ["contextBridge"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = [],
        AcceptanceCriteria = ["package.json has electron dependency and main entry", "main.js creates a BrowserWindow", "index.html is loaded by the main process"],
        VerificationGoals = ["Electron API usage is correct", "IPC bridge is properly configured if preload exists", "package.json main field points to main.js"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // Spring Boot (Java/Kotlin)
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildSpringBoot() => new()
    {
        FrameworkId = "spring-boot",
        DisplayName = "Spring Boot",
        PrimaryLanguage = "java",
        MinimumFileCount = 4,
        DetectionKeywords = ["spring", "spring boot", "springboot", "java api", "java web", "java backend"],
        BootstrapCommand = "./mvnw install",
        RunCommand = "./mvnw spring-boot:run",
        TestCommand = "./mvnw test",
        BuildCommand = "./mvnw package",
        BootstrapHint = "Run './mvnw spring-boot:run' to start (or use Gradle: './gradlew bootRun').",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "pom.xml", Description = "Maven POM with Spring Boot parent", Required = true, Language = "xml", ContentMarkers = ["spring-boot-starter", "<parent>"] },
            new TemplateFile { RelativePath = "src/main/java/com/example/app/Application.java", Description = "Spring Boot application class", Required = true, Language = "java", ContentMarkers = ["@SpringBootApplication", "SpringApplication.run("] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "src/main/java/com/example/app/controller/HelloController.java", Description = "REST controller", Required = false, Language = "java", ContentMarkers = ["@RestController", "@GetMapping"] },
            new TemplateFile { RelativePath = "src/main/resources/application.properties", Description = "Application configuration", Required = false, Language = "text", ContentMarkers = ["server.port"] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["src/main/java/", "src/main/resources/", "src/test/java/"],
        AcceptanceCriteria = ["pom.xml has spring-boot-starter parent", "Application.java has @SpringBootApplication", "At least one REST controller exists"],
        VerificationGoals = ["Java syntax is correct", "Spring annotations are properly used", "pom.xml is valid XML"],
    };

    // ═══════════════════════════════════════════════════════════════════════════════
    // PHP / Laravel
    // ═══════════════════════════════════════════════════════════════════════════════
    private static FrameworkTemplate BuildPhpLaravel() => new()
    {
        FrameworkId = "php-laravel",
        DisplayName = "Laravel (PHP)",
        PrimaryLanguage = "php",
        MinimumFileCount = 5,
        DetectionKeywords = ["laravel", "php web", "php api", "php framework"],
        BootstrapCommand = "composer install",
        RunCommand = "php artisan serve",
        TestCommand = "php artisan test",
        BuildCommand = "",
        BootstrapHint = "Run 'composer install' then 'php artisan serve' to start the development server.",
        RequiredFiles =
        [
            new TemplateFile { RelativePath = "composer.json", Description = "Composer package manifest", Required = true, Language = "json", ContentMarkers = ["\"laravel/framework\""] },
            new TemplateFile { RelativePath = "artisan", Description = "Laravel CLI entry point", Required = true, Language = "php", ContentMarkers = ["Artisan::handle("] },
            new TemplateFile { RelativePath = "routes/web.php", Description = "Web route definitions", Required = true, Language = "php", ContentMarkers = ["Route::get("] },
        ],
        RecommendedFiles =
        [
            new TemplateFile { RelativePath = "app/Http/Controllers/Controller.php", Description = "Base controller", Required = false, Language = "php", ContentMarkers = ["class Controller"] },
            new TemplateFile { RelativePath = ".env", Description = "Environment configuration", Required = false, Language = "text", ContentMarkers = ["APP_NAME=", "APP_KEY="] },
            new TemplateFile { RelativePath = "README.md", Description = "Project readme", Required = false, Language = "markdown", ContentMarkers = [] },
        ],
        FolderConventions = ["app/", "app/Http/Controllers/", "routes/", "resources/views/"],
        AcceptanceCriteria = ["composer.json requires laravel/framework", "routes/web.php defines at least one route", "artisan file exists as CLI entry"],
        VerificationGoals = ["PHP syntax is correct", "Routes use valid HTTP verbs", "Controller classes extend base controller"],
    };
}
