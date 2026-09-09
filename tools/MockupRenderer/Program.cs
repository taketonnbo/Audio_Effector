using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MockupRenderer;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            Console.WriteLine($"[MockupRenderer] Initializing WPF Application environment...");
            InitializeWpfApplication(options.Theme);

            Console.WriteLine($"[MockupRenderer] Building View: '{options.ViewName ?? options.XamlPath}'...");
            FrameworkElement element = BuildElement(options);

            Console.WriteLine($"[MockupRenderer] Rendering ({options.Width}x{options.Height}) to: '{options.OutputPath}'...");
            RenderToPng(element, options.OutputPath, options.Width, options.Height);

            Console.WriteLine($"[MockupRenderer] Successfully generated screenshot at: {Path.GetFullPath(options.OutputPath)}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[MockupRenderer] Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return 1;
        }
    }

    private static void InitializeWpfApplication(string theme)
    {
        if (Application.Current == null)
        {
            _ = new Application();
        }

        var app = Application.Current ?? new Application();
        app.Resources.MergedDictionaries.Clear();

        // Load DarkTheme or LightTheme from AudioEffector assembly
        string themePath = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? "pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml"
            : "pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml";

        try
        {
            var themeDict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Absolute) };
            app.Resources.MergedDictionaries.Add(themeDict);

            var scrollDict = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AudioEffector;component/Presentation/Themes/ScrollBarResources.xaml", UriKind.Absolute)
            };
            app.Resources.MergedDictionaries.Add(scrollDict);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MockupRenderer] Warning loading packaged theme dictionaries: {ex.Message}");
            // Fallback: load local colors if needed
            AddFallbackColors(app.Resources);
        }

        // Register standard converters
        app.Resources["BoolToVis"] = new AudioEffector.Presentation.Converters.BoolToVisConverter();
        app.Resources["IndexConverter"] = new AudioEffector.Presentation.Converters.IndexConverter();
    }

    private static void AddFallbackColors(ResourceDictionary resources)
    {
        resources["WindowBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x20));
        resources["PanelBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x1B, 0x20, 0x28));
        resources["WorkspaceBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x22));
        resources["ControlBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(0x23, 0x29, 0x34));
        resources["ControlBackgroundHighlightBrush"] = new SolidColorBrush(Color.FromRgb(0x2F, 0x37, 0x46));
        resources["TextForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8));
        resources["SecondaryTextForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0xA0, 0xAE, 0xC0));
        resources["MutedTextForegroundBrush"] = new SolidColorBrush(Color.FromRgb(0x71, 0x80, 0x96));
        resources["BorderBrush"] = new SolidColorBrush(Color.FromRgb(0x34, 0x3E, 0x4E));
        resources["NeonCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0xFF));
        resources["DarkNeonCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
    }

    private static FrameworkElement BuildElement(RenderOptions options)
    {
        // 1. If explicit XAML file is provided, load it
        if (!string.IsNullOrEmpty(options.XamlPath) && File.Exists(options.XamlPath))
        {
            string xamlContent = File.ReadAllText(options.XamlPath);
            if (XamlReader.Parse(xamlContent) is FrameworkElement customElement)
            {
                return customElement;
            }
            throw new InvalidOperationException($"Parsed XAML is not a FrameworkElement: {options.XamlPath}");
        }

        // 2. Preset Views
        return options.ViewName?.ToLowerInvariant() switch
        {
            "sidebar" or "sidebarcontrol" => new AudioEffector.Presentation.Views.SidebarControl(),
            "playqueue" or "playqueuesidepanel" => new AudioEffector.Presentation.Views.PlayQueueSidePanel(),
            "equalizer" or "equalizerview" => new AudioEffector.Presentation.Views.EqualizerView(),
            "overalllayout" or "mainwindow" => MockViewFactory.CreateOverallLayoutMock(),
            _ => MockViewFactory.CreateOverallLayoutMock()
        };
    }

    public static void RenderToPng(FrameworkElement element, string outputPath, int width, int height)
    {
        // Ensure element size
        element.Width = width;
        element.Height = height;

        // Wrap in a parent border to ensure proper background fill if not set
        Border container = new Border
        {
            Width = width,
            Height = height,
            Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"] ?? new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x20)),
            Child = element
        };

        container.Measure(new Size(width, height));
        container.Arrange(new Rect(0, 0, width, height));
        container.UpdateLayout();

        var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        renderBitmap.Render(container);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = File.Create(outputPath);
        encoder.Save(fileStream);
    }

    private static RenderOptions ParseArguments(string[] args)
    {
        var options = new RenderOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                options.ShowHelp = true;
                return options;
            }
            if (arg is "-v" or "--view" && i + 1 < args.Length)
            {
                options.ViewName = args[++i];
            }
            else if (arg is "-x" or "--xaml" && i + 1 < args.Length)
            {
                options.XamlPath = args[++i];
            }
            else if (arg is "-o" or "--output" && i + 1 < args.Length)
            {
                options.OutputPath = args[++i];
            }
            else if (arg is "-w" or "--width" && i + 1 < args.Length && int.TryParse(args[++i], out int w))
            {
                options.Width = w;
            }
            else if (arg is "--height" && i + 1 < args.Length && int.TryParse(args[++i], out int h))
            {
                options.Height = h;
            }
            else if (arg is "-t" or "--theme" && i + 1 < args.Length)
            {
                options.Theme = args[++i];
            }
        }
        return options;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("MockupRenderer - WPF Offscreen XAML Screenshot Tool");
        Console.WriteLine("Usage:");
        Console.WriteLine("  MockupRenderer [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -v, --view <name>     Preset view name (overalllayout, sidebar, playqueue, equalizer)");
        Console.WriteLine("  -x, --xaml <path>     Path to custom XAML file to render");
        Console.WriteLine("  -o, --output <path>   Output PNG path (default: mockup_output.png)");
        Console.WriteLine("  -w, --width <int>     Target width in pixels (default: 1280)");
        Console.WriteLine("      --height <int>    Target height in pixels (default: 720)");
        Console.WriteLine("  -t, --theme <name>    Theme name: Dark (default) or Light");
        Console.WriteLine("  -h, --help            Show this help message");
    }
}

public class RenderOptions
{
    public string? ViewName { get; set; } = "overalllayout";
    public string? XamlPath { get; set; }
    public string OutputPath { get; set; } = "mockup_output.png";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public string Theme { get; set; } = "Dark";
    public bool ShowHelp { get; set; }
}
