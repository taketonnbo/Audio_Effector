using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// 右側タブ下部拡張領域に配置される LRC 自動生成パネルのコードビハインドです
/// </summary>
public partial class LrcGeneratorView : UserControl
{
    /// <summary>
    /// <see cref="LrcGeneratorView"/> の新しいインスタンスを初期化します
    /// </summary>
    public LrcGeneratorView()
    {
        InitializeComponent();
    }

    private void TextFileDropZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not LrcGeneratorViewModel vm)
        {
            return;
        }

        // 内部ボタンクリックのバブルイベントではファイル選択を再発火しない
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (vm.SelectTextFileCommand.CanExecute(null))
        {
            vm.SelectTextFileCommand.Execute(null);
        }
    }

    private void TextFileDropZone_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (TryExtractFirstDroppedFilePath(e.Data, out string filePath)
            && IsSupportedTextDropExtension(filePath))
        {
            e.Effects = DragDropEffects.Copy;
            SetDropZoneHighlight(isHighlighted: true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            SetDropZoneHighlight(isHighlighted: false);
        }

        e.Handled = true;
    }

    private void TextFileDropZone_DragLeave(object sender, DragEventArgs e)
    {
        SetDropZoneHighlight(isHighlighted: false);
    }

    private async void TextFileDropZone_Drop(object sender, DragEventArgs e)
    {
        SetDropZoneHighlight(isHighlighted: false);
        if (DataContext is not LrcGeneratorViewModel vm)
        {
            return;
        }

        if (TryExtractFirstDroppedFilePath(e.Data, out string filePath))
        {
            e.Handled = true;
            try
            {
                await vm.InspectAndSelectTextFileAsync(filePath).ConfigureAwait(false);
            }
            catch
            {
                // 想定外例外で UI スレッドを落とさない（VM 側でエラー表示済み）
            }
        }
    }

    private void PreviewLineBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not LrcGeneratorViewModel vm)
        {
            return;
        }

        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null
            || FindAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (sender is FrameworkElement element && element.DataContext is LrcPreviewLineViewModel line && !line.IsEditing)
        {
            if (vm.SeekToPreviewLineCommand.CanExecute(line))
            {
                vm.SeekToPreviewLineCommand.Execute(line);
            }
        }
    }

    private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not LrcPreviewLineViewModel line)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            line.CommitEditCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            line.CancelEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SetDropZoneHighlight(bool isHighlighted)
    {
        if (TextFileDropZone is null)
        {
            return;
        }

        if (isHighlighted)
        {
            TextFileDropZone.SetResourceReference(Border.BorderBrushProperty, "NeonCyanBrush");
            TextFileDropZone.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundHighlightBrush");
        }
        else
        {
            TextFileDropZone.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            TextFileDropZone.SetResourceReference(Border.BackgroundProperty, "ControlBackgroundBrush");
        }
    }

    private static bool TryExtractFirstDroppedFilePath(IDataObject? data, out string filePath)
    {
        filePath = string.Empty;
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && !string.IsNullOrWhiteSpace(files[0]))
        {
            filePath = files[0];
            return true;
        }

        return false;
    }

    private static bool IsSupportedTextDropExtension(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".lrc", StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
