using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using H2CursorRouter.App;
using H2CursorRouter.App.ViewModels;
using Xunit;

namespace H2CursorRouter.App.Tests;

public sealed class MainWindowBindingTests
{
    [Fact]
    public void XamlFilesAreWellFormedXml()
    {
        XDocument.Load(FindRepositoryFile("src", "H2CursorRouter.App", "App.xaml"));
        XDocument.Load(FindRepositoryFile("src", "H2CursorRouter.App", "MainWindow.xaml"));
    }

    [Fact]
    public void MainWindowStaticResourcesAreDefinedInAppResources()
    {
        var appXaml = File.ReadAllText(FindRepositoryFile("src", "H2CursorRouter.App", "App.xaml"));
        var mainWindowXaml = File.ReadAllText(FindRepositoryFile("src", "H2CursorRouter.App", "MainWindow.xaml"));
        var definedKeys = Regex.Matches(appXaml + Environment.NewLine + mainWindowXaml, @"x:Key=""(?<name>[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var missing = Regex.Matches(mainWindowXaml, @"\{StaticResource\s+(?<name>[^}\s]+)")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(resourceName => !definedKeys.Contains(resourceName))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void MainWindowEventHandlersExistInCodeBehind()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "H2CursorRouter.App", "MainWindow.xaml"));
        var eventNames = new[]
        {
            "Loaded",
            "ContentRendered",
            "Closing",
            "Click",
            "SelectionChanged",
            "MouseDoubleClick",
            "DragDelta",
            "DragCompleted",
            "KeyDown",
            "PreviewMouseWheel",
            "PreviewMouseLeftButtonDown"
        };
        var handlers = eventNames
            .SelectMany(eventName => Regex.Matches(xaml, $@"\b{eventName}=""(?<name>[A-Za-z_][A-Za-z0-9_]*)"""))
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var methods = typeof(MainWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missing = handlers
            .Where(handler => !methods.Contains(handler))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(handlers);
        Assert.Empty(missing);
    }

    [Fact]
    public void XamlCommandBindingsResolveToMainViewModelCommands()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "H2CursorRouter.App", "MainWindow.xaml"));
        var commandNames = Regex.Matches(xaml, @"Command=""\{Binding\s+(?:DataContext\.)?(?<name>[A-Za-z0-9_]+Command)")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missing = commandNames
            .Where(commandName => typeof(MainViewModel).GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public) is null)
            .ToArray();

        Assert.NotEmpty(commandNames);
        Assert.Empty(missing);
    }

    [Fact]
    public void EditableDataGridsDisableImplicitDeleteRows()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "H2CursorRouter.App", "MainWindow.xaml"));
        var editableDataGrids = Regex.Matches(xaml, @"<DataGrid\b[^>]*\bIsReadOnly=""False""[^>]*/?>")
            .Select(match => match.Value)
            .ToArray();
        var deleteEnabled = editableDataGrids
            .Where(tag => !Regex.IsMatch(tag, @"\bCanUserDeleteRows=""False"""))
            .ToArray();

        Assert.NotEmpty(editableDataGrids);
        Assert.Empty(deleteEnabled);
    }

    [Theory]
    [InlineData("RuntimeStatus")]
    [InlineData("DashboardProfiles")]
    [InlineData("RoutingStateText")]
    [InlineData("ActiveLayoutName")]
    [InlineData("H2ConnectionStatus")]
    [InlineData("LastRoutingEvent")]
    [InlineData("StartWithWindows")]
    [InlineData("SelectedLayoutZones")]
    [InlineData("AvailableLayoutDisplays")]
    [InlineData("SelectedAvailableMonitor")]
    [InlineData("LayoutPreviewScale")]
    [InlineData("LayoutPreviewCanvasWidth")]
    [InlineData("LayoutPreviewCanvasHeight")]
    [InlineData("Layouts")]
    [InlineData("SelectedLayout")]
    [InlineData("SelectedZone")]
    [InlineData("HasSelectedZone")]
    [InlineData("SelectedLayoutPortals")]
    [InlineData("SelectedPortal")]
    [InlineData("Profiles")]
    [InlineData("FilteredProfiles")]
    [InlineData("SelectedProfile")]
    [InlineData("Devices")]
    [InlineData("SelectedDevice")]
    [InlineData("Presets")]
    [InlineData("SelectedPreset")]
    [InlineData("Monitors")]
    [InlineData("DisplayAliases")]
    [InlineData("DisplayPreviewCanvasWidth")]
    [InlineData("DisplayPreviewCanvasHeight")]
    [InlineData("ValidationErrors")]
    [InlineData("Logs")]
    [InlineData("AppVersion")]
    [InlineData("Updates")]
    [InlineData("AppDescription")]
    [InlineData("AppLicenseSummary")]
    [InlineData("ConfigPath")]
    [InlineData("LogDirectory")]
    public void MainViewModelKeepsCriticalBindingProperties(string propertyName)
    {
        Assert.NotNull(typeof(MainViewModel).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public));
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = relativeParts.Aggregate(directory.FullName, Path.Combine);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(relativeParts)}'.");
    }
}
