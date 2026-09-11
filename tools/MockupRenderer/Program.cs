using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
        finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
    }

    private static void InitializeWpfApplication(string theme)
    {
        if (Application.Current == null)
        {
            _ = new Application();
        }

        var app = Application.Current ?? new Application();
        app.Resources = SourceViewLoader.LoadResources("AudioEffector/App.xaml");

        // Load DarkTheme or LightTheme from AudioEffector assembly
        string themePath = theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? "pack://application:,,,/AudioEffector;component/Presentation/Themes/LightTheme.xaml"
            : "pack://application:,,,/AudioEffector;component/Presentation/Themes/DarkTheme.xaml";

        try
        {
            var themeDict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Absolute) };
            app.Resources.MergedDictionaries[0] = themeDict;

        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("本番テーマの読み込みに失敗しました。", ex);
        }

    }

    private static FrameworkElement BuildElement(RenderOptions options)
    {
        return SourceViewLoader.Load(options);
    }

    public static void RenderToPng(FrameworkElement element, string outputPath, int width, int height)
    {
        Window? hostWindow = element as Window;
        if (element is Window window)
        {
            var content = (FrameworkElement)window.Content;
            content.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, window.Foreground);
            content.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, window.FontFamily);
            content.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, window.FontSize);
            window.Content = null;
            content.DataContext = window.DataContext;
            content.Resources.MergedDictionaries.Add(window.Resources);
            element = content;
        }
        // Ensure element size
        element.Width = Math.Max(0, width - element.Margin.Left - element.Margin.Right);
        element.Height = Math.Max(0, height - element.Margin.Top - element.Margin.Bottom);

        // Wrap in a parent border to ensure proper background fill if not set
        Border container = new Border
        {
            Width = width,
            Height = height,
            Background = hostWindow?.Background ?? (Brush)Application.Current.Resources["WindowBackgroundBrush"],
            Child = element
        };
        if (hostWindow != null) hostWindow.Content = container;

        container.Measure(new Size(width, height));
        container.Arrange(new Rect(0, 0, width, height));
        container.UpdateLayout();

        // 初期バインディングと最長600msの表示切替を完了させてから撮影する。
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
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
            else if (arg is "--state" && i + 1 < args.Length)
            {
                options.State = args[++i];
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
        Console.WriteLine("  -v, --view <name>     Production view class name or overalllayout/sidebar/playqueue/equalizer");
        Console.WriteLine("      --state <name>    Fixed display state (see captures.json)");
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
    public string State { get; set; } = "default";
    public string? ViewName { get; set; } = "overalllayout";
    public string? XamlPath { get; set; }
    public string OutputPath { get; set; } = "mockup_output.png";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public string Theme { get; set; } = "Dark";
    public bool ShowHelp { get; set; }
}
