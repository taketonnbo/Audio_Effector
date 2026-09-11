using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace MockupRenderer;

/// <summary>本番XAMLからイベント接続だけを外し、サービスを起動せずに描画する。</summary>
public static class SourceViewLoader
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly HashSet<string> Events = new("Click Loaded Unloaded MouseLeftButtonDown MouseLeftButtonUp MouseDown MouseUp MouseMove MouseEnter MouseLeave PreviewMouseDown PreviewMouseLeftButtonDown PreviewMouseLeftButtonUp PreviewMouseMove MouseDoubleClick PreviewKeyDown KeyDown GotKeyboardFocus LostKeyboardFocus SelectionChanged TextChanged ValueChanged DragStarted DragCompleted DragDelta Drop DragOver SizeChanged".Split(' '));

    public static FrameworkElement Load(RenderOptions options)
    {
        string view = options.ViewName?.ToLowerInvariant() switch
        {
            "overalllayout" or "mainwindow" => "MainWindow", "sidebar" or "sidebarcontrol" => "SidebarControl",
            "playqueue" or "playqueuesidepanel" => "PlayQueueSidePanel", "equalizer" or "equalizerview" => "EqualizerView",
            _ => options.ViewName ?? "MainWindow"
        };
        string path = options.XamlPath ?? $"AudioEffector/Presentation/Views/{view}.xaml";
        if (view is not ("MainWindow" or "SettingsDialog" or "DeviceManagerDialog" or "InputBox" or "PlaylistSelectionDialog" or "MiniPlayerWindow" or "PlayQueueDialog"))
            Application.Current.Resources.MergedDictionaries.Add(LoadResources("AudioEffector/Presentation/Views/MainWindow.xaml"));
        if (view == "ShortcutInputBox")
            return new AudioEffector.Presentation.Views.ShortcutInputBox { Label = "再生 / 一時停止", Shortcut = new AudioEffector.Domain.Entities.AppSettings().PlayPauseShortcut };
        var element = (FrameworkElement)XamlReader.Parse(Prepare(XDocument.Load(path)).ToString());
        element.DataContext = SampleData.Create(options.State);
        if (view == "SidebarControl" && options.State == "expanded" && element.FindName("RootBorder") is Border sidebar) sidebar.Width = 200;
        if (view == "PlayQueueSidePanel" && element.FindName("PanelTransform") is TranslateTransform transform) transform.X = 0;
        return element is Window ? element : new Window { Content = element, DataContext = element.DataContext, Background = (Brush)Application.Current.Resources["PanelBackgroundBrush"] };
    }

    public static ResourceDictionary LoadResources(string path)
    {
        var doc = Prepare(XDocument.Load(path));
        var resources = doc.Root!.Elements().First(e => e.Name.LocalName.EndsWith(".Resources"));
        var dictionary = resources.Element(P + "ResourceDictionary") ?? new XElement(P + "ResourceDictionary", resources.Elements());
        foreach (var attr in doc.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            if (dictionary.Attribute(attr.Name) == null) dictionary.Add(new XAttribute(attr));
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static XDocument Prepare(XDocument doc)
    {
        doc = XDocument.Parse(System.Text.RegularExpressions.Regex.Replace(doc.ToString(), "clr-namespace:(AudioEffector[^\";]*)(?=\")", "clr-namespace:$1;assembly=AudioEffector"));
        foreach (var node in doc.Descendants())
        foreach (var attr in node.Attributes().ToArray())
        {
            if (attr.Name == XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml") || attr.Name.NamespaceName.Contains("expression/blend") || attr.Name.LocalName == "Ignorable" || Events.Contains(attr.Name.LocalName.Split('.').Last())) attr.Remove();
            else if (attr.IsNamespaceDeclaration && attr.Value.StartsWith("clr-namespace:AudioEffector") && !attr.Value.Contains(";assembly=")) attr.Value += ";assembly=AudioEffector";
            else if (attr.Value.StartsWith("pack://application:,,,/") && !attr.Value.Contains(";component/")) attr.Value = attr.Value.Replace("pack://application:,,,/", "pack://application:,,,/AudioEffector;component/");
        }
        return doc;
    }
}
