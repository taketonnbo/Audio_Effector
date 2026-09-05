using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ライブラリのアルバム一覧を表示するビュー
/// </summary>
public partial class LibraryView : UserControl
{
    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public LibraryView()
    {
        InitializeComponent();
    }

    private void GridScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is LibraryViewModel vm && vm.ExpandedAlbum != null)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                // クリックされた要素が SocketTray または AlbumTracksToggleButton の内部かどうかを判定
                var inTray = FindVisualAncestor<Border>(dep, b => b.Name == "SocketTray");
                var inToggle = FindVisualAncestor<ToggleButton>(dep, t => t.Name == "AlbumTracksToggleButton");

                if (inTray == null && inToggle == null)
                {
                    vm.CloseExpandedAlbum();
                }
            }
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match && (predicate == null || predicate(match)))
            {
                return match;
            }

            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }
        return null;
    }
}
