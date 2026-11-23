using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Domain.Entities;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// ライブラリのアルバム一覧を表示するビュー
/// </summary>
public partial class LibraryView : UserControl
{
    private Border? _pressedTrackRow;

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

    /// <summary>
    /// リスト表示の展開楽曲行で左ボタンが押下された際、子ボタン・チェックボックス以外の領域であれば押下行を記録します
    /// </summary>
    private void ListTrackRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedTrackRow = ShouldTriggerRowPlay(e.OriginalSource as DependencyObject)
            ? sender as Border
            : null;
    }

    /// <summary>
    /// 押下中の展開楽曲行からカーソルが離れた場合、ドラッグキャンセル扱いとして押下記録をクリアします
    /// </summary>
    private void ListTrackRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(_pressedTrackRow, sender))
        {
            _pressedTrackRow = null;
        }
    }

    /// <summary>
    /// リスト表示の展開楽曲行クリック時、同一行上での押下・離上かつ子ボタン・チェックボックス以外の領域であれば楽曲を再生します
    /// </summary>
    private void ListTrackRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pressedRow = _pressedTrackRow;
        _pressedTrackRow = null;

        if (!ReferenceEquals(pressedRow, sender) || !ShouldTriggerRowPlay(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: AudioEffector.Domain.Entities.Track track } &&
            DataContext is LibraryViewModel vm &&
            vm.PlayTrackCommand.CanExecute(track))
        {
            vm.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }

    /// <summary>
    /// クリック元要素が楽曲行クリック再生を発火すべき対象かどうかを判定します
    /// </summary>
    /// <param name="originalSource">クリックされた元の要素</param>
    /// <returns>子ボタンやチェックボックス以外の領域であれば true</returns>
    public static bool ShouldTriggerRowPlay(DependencyObject? originalSource)
    {
        return originalSource != null && FindVisualAncestor<ButtonBase>(originalSource) == null;
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
