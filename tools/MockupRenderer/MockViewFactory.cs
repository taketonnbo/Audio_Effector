using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MockupRenderer;

public static class MockViewFactory
{
    public static FrameworkElement CreateOverallLayoutMock()
    {
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // TitleBar
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main Body
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) }); // PlayerBar
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); // Spectrum Strip

        // --- Row 0: Custom Title Bar ---
        var titleBar = CreateTitleBar();
        Grid.SetRow(titleBar, 0);
        rootGrid.Children.Add(titleBar);

        // --- Row 1: 3-Pane Body ---
        var bodyGrid = Create3PaneBody();
        Grid.SetRow(bodyGrid, 1);
        rootGrid.Children.Add(bodyGrid);

        // --- Row 2: Persistent Player Bar ---
        var playerBar = CreatePlayerBar();
        Grid.SetRow(playerBar, 2);
        rootGrid.Children.Add(playerBar);

        // --- Row 3: Spectrum Strip ---
        var spectrum = CreateSpectrumStrip();
        Grid.SetRow(spectrum, 3);
        rootGrid.Children.Add(spectrum);

        return rootGrid;
    }

    private static FrameworkElement CreateTitleBar()
    {
        var grid = new Grid
        {
            Background = GetBrush("TitleBarBackgroundBrush", 0x10, 0x14, 0x1B)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Left
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Center
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Right

        // Left: Back/Forward + App Title
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        leftPanel.Children.Add(CreateNavButton("←", true));
        leftPanel.Children.Add(CreateNavButton("→", false));

        var titleText = new TextBlock
        {
            Text = "Audio Effector",
            Foreground = GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        leftPanel.Children.Add(titleText);
        Grid.SetColumn(leftPanel, 0);
        grid.Children.Add(leftPanel);

        // Right: Search Box + Window Controls
        var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

        // Search Bar Mock
        var searchBorder = new Border
        {
            Background = GetBrush("ControlBackgroundBrush", 0x23, 0x29, 0x34),
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Width = 180,
            Height = 22,
            Margin = new Thickness(0, 0, 16, 0),
            Padding = new Thickness(6, 2, 6, 2)
        };
        searchBorder.Child = new TextBlock
        {
            Text = "🔍 検索...",
            Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightPanel.Children.Add(searchBorder);

        // Window Controls Mock (Minimize, Maximize, Close)
        rightPanel.Children.Add(CreateControlBtn("─"));
        rightPanel.Children.Add(CreateControlBtn("□"));
        rightPanel.Children.Add(CreateControlBtn("✕"));

        Grid.SetColumn(rightPanel, 2);
        grid.Children.Add(rightPanel);

        return grid;
    }

    private static FrameworkElement Create3PaneBody()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); // Col 0: Left Sidebar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: Center
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 2: Splitter
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); // Col 3: Right Panel

        // --- Left Sidebar ---
        var sidebar = CreateSidebarPane();
        Grid.SetColumn(sidebar, 0);
        grid.Children.Add(sidebar);

        // --- Center Workspace ---
        var workspace = CreateWorkspacePane();
        Grid.SetColumn(workspace, 1);
        grid.Children.Add(workspace);

        // --- Splitter ---
        var splitter = new Border
        {
            Width = 1,
            Background = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E)
        };
        Grid.SetColumn(splitter, 2);
        grid.Children.Add(splitter);

        // --- Right Side Panel ---
        var sidePanel = CreateRightSidePanel();
        Grid.SetColumn(sidePanel, 3);
        grid.Children.Add(sidePanel);

        return grid;
    }

    private static FrameworkElement CreateSidebarPane()
    {
        var border = new Border
        {
            Background = GetBrush("PanelBackgroundBrush", 0x1B, 0x20, 0x28),
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(12, 16, 12, 16)
        };

        var stack = new StackPanel();

        // Section: Library
        stack.Children.Add(CreateSectionHeader("ライブラリ"));
        stack.Children.Add(CreateSidebarItem("🎵 すべての曲", false));
        stack.Children.Add(CreateSidebarItem("💿 アルバム", true)); // Active
        stack.Children.Add(CreateSidebarItem("👤 アーティスト", false));
        stack.Children.Add(CreateSidebarItem("📁 フォルダ", false));
        stack.Children.Add(CreateSidebarItem("⭐ お気に入り", false));

        // Section: Playlists
        var plHeader = new Grid { Margin = new Thickness(0, 20, 0, 8) };
        plHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        plHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        plHeader.Children.Add(new TextBlock { Text = "プレイリスト", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11, FontWeight = FontWeights.Bold });
        
        var plActions = new StackPanel { Orientation = Orientation.Horizontal };
        plActions.Children.Add(new TextBlock { Text = "🔍", Margin = new Thickness(0, 0, 6, 0), Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11 });
        plActions.Children.Add(new TextBlock { Text = "＋", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11 });
        Grid.SetColumn(plActions, 1);
        plHeader.Children.Add(plActions);
        stack.Children.Add(plHeader);

        stack.Children.Add(CreateSidebarItem("🎧 お気に入りベスト", false));
        stack.Children.Add(CreateSidebarItem("🚗 ドライブ用BGM", false));
        stack.Children.Add(CreateSidebarItem("🌙 リラックス夜想曲", false));
        stack.Children.Add(CreateSidebarItem("⚡ ハイテンションワークアウト", false));

        border.Child = stack;
        return border;
    }

    private static FrameworkElement CreateWorkspacePane()
    {
        var grid = new Grid
        {
            Background = GetBrush("WorkspaceBackgroundBrush", 0x14, 0x1A, 0x22)
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); // Breadcrumbs
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // Breadcrumbs Bar
        var breadcrumbBorder = new Border
        {
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0, 16, 0)
        };
        var breadcrumbs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        breadcrumbs.Children.Add(new TextBlock { Text = "ライブラリ", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 12 });
        breadcrumbs.Children.Add(new TextBlock { Text = "  ›  ", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 12 });
        breadcrumbs.Children.Add(new TextBlock { Text = "アルバム", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 12 });
        breadcrumbs.Children.Add(new TextBlock { Text = "  ›  ", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 12 });
        breadcrumbs.Children.Add(new TextBlock { Text = "Abbey Road", Foreground = GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8), FontSize = 12, FontWeight = FontWeights.SemiBold });
        breadcrumbBorder.Child = breadcrumbs;
        Grid.SetRow(breadcrumbBorder, 0);
        grid.Children.Add(breadcrumbBorder);

        // Album Grid Content
        var scroll = new ScrollViewer { Padding = new Thickness(20), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var wrap = new WrapPanel();

        wrap.Children.Add(CreateAlbumCard("Abbey Road", "The Beatles", "1969 • 17曲", 0x2C, 0x35, 0x45));
        wrap.Children.Add(CreateAlbumCard("The Dark Side of the Moon", "Pink Floyd", "1973 • 10曲", 0x20, 0x27, 0x34));
        wrap.Children.Add(CreateAlbumCard("Random Access Memories", "Daft Punk", "2013 • 13曲", 0x1A, 0x25, 0x35));
        wrap.Children.Add(CreateAlbumCard("A Night at the Opera", "Queen", "1975 • 12曲", 0x30, 0x22, 0x35));
        wrap.Children.Add(CreateAlbumCard("Thriller", "Michael Jackson", "1982 • 9曲", 0x35, 0x20, 0x20));
        wrap.Children.Add(CreateAlbumCard("Hotel California", "Eagles", "1976 • 9曲", 0x35, 0x2E, 0x20));
        wrap.Children.Add(CreateAlbumCard("Back in Black", "AC/DC", "1980 • 10曲", 0x1E, 0x20, 0x24));
        wrap.Children.Add(CreateAlbumCard("Rumours", "Fleetwood Mac", "1977 • 11曲", 0x25, 0x2C, 0x30));

        scroll.Content = wrap;
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        return grid;
    }

    private static FrameworkElement CreateRightSidePanel()
    {
        var border = new Border
        {
            Background = GetBrush("PanelBackgroundBrush", 0x1B, 0x20, 0x28)
        };
        var panelGrid = new Grid();
        panelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); // Tab Header
        panelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Queue / Lyrics

        // Tab Header
        var tabBorder = new Border
        {
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var tabStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        tabStack.Children.Add(CreateTabItem("再生キュー", true));
        tabStack.Children.Add(CreateTabItem("機器転送", false));
        tabStack.Children.Add(CreateTabItem("詳細情報", false));
        tabStack.Children.Add(CreateTabItem("歌詞", false));
        tabBorder.Child = tabStack;
        Grid.SetRow(tabBorder, 0);
        panelGrid.Children.Add(tabBorder);

        // Queue Content Mock
        var queueStack = new StackPanel { Margin = new Thickness(12) };
        queueStack.Children.Add(new TextBlock { Text = "▶ 次に再生 (予約キュー)", Foreground = GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) });
        queueStack.Children.Add(CreateQueueItem("1", "Come Together", "The Beatles", "4:19", true));
        queueStack.Children.Add(CreateQueueItem("2", "Something", "The Beatles", "3:02", false));
        queueStack.Children.Add(CreateQueueItem("3", "Maxwell's Silver Hammer", "The Beatles", "3:27", false));
        
        queueStack.Children.Add(new TextBlock { Text = "アルバムの残り曲", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) });
        queueStack.Children.Add(CreateQueueItem("4", "Oh! Darling", "The Beatles", "3:27", false));
        queueStack.Children.Add(CreateQueueItem("5", "Octopus's Garden", "The Beatles", "2:51", false));
        queueStack.Children.Add(CreateQueueItem("6", "I Want You", "The Beatles", "7:47", false));

        Grid.SetRow(queueStack, 1);
        panelGrid.Children.Add(queueStack);

        border.Child = panelGrid;
        return border;
    }

    private static FrameworkElement CreatePlayerBar()
    {
        var border = new Border
        {
            Background = GetBrush("PanelBackgroundBrush", 0x1B, 0x20, 0x28),
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 8, 16, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) }); // Track Info
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Controls & Timeline
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); // Volume & Toggles

        // Col 0: Track Info
        var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var artBorder = new Border
        {
            Width = 48,
            Height = 48,
            Background = GetBrush("ControlBackgroundHighlightBrush", 0x2F, 0x37, 0x46),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 12, 0)
        };
        artBorder.Child = new TextBlock { Text = "💿", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(artBorder);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock { Text = "Come Together", Foreground = GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8), FontSize = 14, FontWeight = FontWeights.SemiBold });
        textStack.Children.Add(new TextBlock { Text = "The Beatles — Abbey Road", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) });
        infoPanel.Children.Add(textStack);
        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        // Col 1: Playback Controls & Timeline
        var centerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 480 };
        var ctrlStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
        ctrlStack.Children.Add(CreateCtrlIcon("🔀", 14));
        ctrlStack.Children.Add(CreateCtrlIcon("⏮", 16));
        ctrlStack.Children.Add(CreatePlayBtn());
        ctrlStack.Children.Add(CreateCtrlIcon("⏭", 16));
        ctrlStack.Children.Add(CreateCtrlIcon("🔁", 14));
        centerStack.Children.Add(ctrlStack);

        // Timeline
        var timeGrid = new Grid();
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        timeGrid.Children.Add(new TextBlock { Text = "1:24", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11, Margin = new Thickness(0, 0, 8, 0) });
        
        var sliderTrack = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 4 };
        sliderTrack.Children.Add(new Border { Background = GetBrush("ControlBackgroundHighlightBrush", 0x2F, 0x37, 0x46), CornerRadius = new CornerRadius(2) });
        var prog = new Border { Background = GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF), CornerRadius = new CornerRadius(2), Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        sliderTrack.Children.Add(prog);
        Grid.SetColumn(sliderTrack, 1);
        timeGrid.Children.Add(sliderTrack);

        var totalTime = new TextBlock { Text = "4:19", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(totalTime, 2);
        timeGrid.Children.Add(totalTime);

        centerStack.Children.Add(timeGrid);
        Grid.SetColumn(centerStack, 1);
        grid.Children.Add(centerStack);

        // Col 2: Volume & Action Toggles
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        rightStack.Children.Add(new TextBlock { Text = "🔊", Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        
        var volTrack = new Grid { VerticalAlignment = VerticalAlignment.Center, Width = 80, Height = 4, Margin = new Thickness(0, 0, 16, 0) };
        volTrack.Children.Add(new Border { Background = GetBrush("ControlBackgroundHighlightBrush", 0x2F, 0x37, 0x46), CornerRadius = new CornerRadius(2) });
        volTrack.Children.Add(new Border { Background = GetBrush("DarkNeonCyanBrush", 0x00, 0xE5, 0xFF), CornerRadius = new CornerRadius(2), Width = 55, HorizontalAlignment = HorizontalAlignment.Left });
        rightStack.Children.Add(volTrack);

        rightStack.Children.Add(CreateCtrlIcon("EQ", 11));
        rightStack.Children.Add(CreateCtrlIcon("💬", 14));
        rightStack.Children.Add(CreateCtrlIcon("☰", 16));

        Grid.SetColumn(rightStack, 2);
        grid.Children.Add(rightStack);

        border.Child = grid;
        return border;
    }

    private static FrameworkElement CreateSpectrumStrip()
    {
        var border = new Border
        {
            Background = GetBrush("WindowBackgroundBrush", 0x16, 0x19, 0x20),
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 4, 0, 4)
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        var heights = new double[] { 8, 14, 22, 28, 18, 12, 25, 30, 22, 16, 10, 15, 24, 19, 14, 26, 28, 20, 12, 18, 25, 21, 15, 27, 24, 18, 14, 20, 23, 17, 11, 8 };
        
        foreach (var h in heights)
        {
            panel.Children.Add(new Border
            {
                Width = 4,
                Height = h,
                Margin = new Thickness(2, 0, 2, 0),
                Background = GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF),
                CornerRadius = new CornerRadius(1)
            });
        }

        border.Child = panel;
        return border;
    }

    // Helper builders
    private static FrameworkElement CreateAlbumCard(string title, string artist, string meta, byte r, byte g, byte b)
    {
        var border = new Border
        {
            Width = 160,
            Margin = new Thickness(10),
            Background = GetBrush("ControlBackgroundBrush", 0x23, 0x29, 0x34),
            BorderBrush = GetBrush("BorderBrush", 0x34, 0x3E, 0x4E),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10)
        };

        var stack = new StackPanel();
        var art = new Border
        {
            Height = 140,
            Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 0, 8)
        };
        art.Child = new TextBlock { Text = "💿", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        stack.Children.Add(art);

        stack.Children.Add(new TextBlock { Text = title, Foreground = GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8), FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = artist, Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = meta, Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });

        border.Child = stack;
        return border;
    }

    private static FrameworkElement CreateSidebarItem(string text, bool isActive)
    {
        var border = new Border
        {
            Background = isActive ? GetBrush("ControlBackgroundHighlightBrush", 0x2F, 0x37, 0x46) : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 2)
        };
        var tb = new TextBlock
        {
            Text = text,
            Foreground = isActive ? GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF) : GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8),
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 13
        };
        border.Child = tb;
        return border;
    }

    private static FrameworkElement CreateQueueItem(string num, string title, string artist, string duration, bool isNowPlaying)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var numText = new TextBlock
        {
            Text = isNowPlaying ? "▶" : num,
            Foreground = isNowPlaying ? GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF) : GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(numText, 0);
        grid.Children.Add(numText);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = isNowPlaying ? GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF) : GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8),
            FontWeight = isNowPlaying ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 12
        });
        textStack.Children.Add(new TextBlock { Text = artist, Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 10 });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var durText = new TextBlock { Text = duration, Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(durText, 2);
        grid.Children.Add(durText);

        return grid;
    }

    private static FrameworkElement CreateTabItem(string text, bool isActive)
    {
        var border = new Border
        {
            BorderBrush = isActive ? GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF) : Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        border.Child = new TextBlock
        {
            Text = text,
            Foreground = isActive ? GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF) : GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96),
            FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
            FontSize = 12
        };
        return border;
    }

    private static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = GetBrush("MutedTextForegroundBrush", 0x71, 0x80, 0x96),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 4)
        };
    }

    private static FrameworkElement CreateNavButton(string icon, bool isEnabled)
    {
        return new Border
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 4, 0),
            Child = new TextBlock
            {
                Text = icon,
                Foreground = isEnabled ? GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8) : GetBrush("DisabledTextForegroundBrush", 0x4A, 0x55, 0x68),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static FrameworkElement CreateControlBtn(string symbol)
    {
        return new Border
        {
            Width = 32,
            Height = 24,
            Child = new TextBlock
            {
                Text = symbol,
                Foreground = GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static FrameworkElement CreateCtrlIcon(string icon, double size)
    {
        return new Border
        {
            Width = 28,
            Height = 28,
            Margin = new Thickness(6, 0, 6, 0),
            Child = new TextBlock
            {
                Text = icon,
                Foreground = GetBrush("TextForegroundBrush", 0xF0, 0xF4, 0xF8),
                FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static FrameworkElement CreatePlayBtn()
    {
        var border = new Border
        {
            Width = 36,
            Height = 36,
            Background = GetBrush("NeonCyanBrush", 0x00, 0xFF, 0xFF),
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(10, 0, 10, 0)
        };
        border.Child = new TextBlock
        {
            Text = "⏸",
            Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x1B)),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return border;
    }

    private static Brush GetBrush(string resourceKey, byte defR, byte defG, byte defB)
    {
        if (Application.Current?.Resources[resourceKey] is Brush b)
        {
            return b;
        }
        return new SolidColorBrush(Color.FromRgb(defR, defG, defB));
    }
}
