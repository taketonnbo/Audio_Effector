using System;
using System.Windows.Input;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// ICommandインターフェースの汎用実装。
/// デリゲートを受け取ってコマンド処理を実行します。
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    /// <summary>
    /// コンストラクタ。
    /// </summary>
    /// <param name="execute">実行するアクション。</param>
    /// <param name="canExecute">実行可能かどうかを判定する述語（省略可）。</param>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// 引数なしアクション用のオーバーロードコンストラクタ。
    /// </summary>
    /// <param name="execute">実行するアクション。</param>
    /// <param name="canExecute">実行可能かどうかを判定する述語（省略可）。</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
