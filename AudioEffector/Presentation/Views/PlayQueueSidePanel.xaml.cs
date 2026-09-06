using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AudioEffector.Presentation.ViewModels;

namespace AudioEffector.Presentation.Views;

/// <summary>
/// メインウィンドウの右端からスライド表示される再生キューパネル
/// </summary>
public partial class PlayQueueSidePanel : UserControl
{
    private const double PanelWidth = 380.0;
    private readonly DoubleAnimation _slideInAnimation;
    private readonly DoubleAnimation _slideOutAnimation;
    private MainViewModel? _subscribedViewModel;

    /// <summary>
    /// インスタンスを初期化します
    /// </summary>
    public PlayQueueSidePanel()
    {
        InitializeComponent();

        // 起動時の初期状態: 完全に非表示（Collapsed）としてビジュアルツリー・レイアウト・アイテム生成を抑止
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        PanelTransform.X = PanelWidth;

        _slideInAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _slideOutAnimation = new DoubleAnimation
        {
            To = PanelWidth,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        _slideOutAnimation.Completed += OnSlideOutCompleted;

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SubscribeViewModel(DataContext as MainViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SubscribeViewModel(e.NewValue as MainViewModel);
    }

    private void SubscribeViewModel(MainViewModel? vm)
    {
        if (_subscribedViewModel == vm)
        {
            return;
        }

        UnsubscribeViewModel();

        if (vm != null)
        {
            _subscribedViewModel = vm;
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            // 初期状態の反映（アニメーションなし）
            UpdatePanelState(vm.IsPlayQueuePanelOpen, animate: false);
        }
    }

    private void UnsubscribeViewModel()
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlayQueuePanelOpen) && sender is MainViewModel vm)
        {
            UpdatePanelState(vm.IsPlayQueuePanelOpen, animate: true);
        }
    }

    private void UpdatePanelState(bool isOpen, bool animate)
    {
        if (isOpen)
        {
            // 開く場合: 即座に Visible 化してスライドインアニメーションを実行
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;

            if (animate)
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, _slideInAnimation);
            }
            else
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                PanelTransform.X = 0;
            }
        }
        else
        {
            // 閉じる場合
            if (animate && Visibility == Visibility.Visible)
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, _slideOutAnimation);
            }
            else
            {
                PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
                PanelTransform.X = PanelWidth;
                Visibility = Visibility.Collapsed;
                IsHitTestVisible = false;
            }
        }
    }

    private void OnSlideOutCompleted(object? sender, EventArgs e)
    {
        // アニメーション完了時に閉じている状態であれば Collapsed にして描画とヒットテストを完全停止
        if (_subscribedViewModel != null && !_subscribedViewModel.IsPlayQueuePanelOpen)
        {
            PanelTransform.BeginAnimation(TranslateTransform.XProperty, null);
            PanelTransform.X = PanelWidth;
            Visibility = Visibility.Collapsed;
            IsHitTestVisible = false;
        }
    }
}
