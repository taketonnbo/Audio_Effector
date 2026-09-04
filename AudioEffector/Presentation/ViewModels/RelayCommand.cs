using System;
using System.Windows.Input;

namespace AudioEffector.Presentation.ViewModels;

/// <summary>
/// ICommandインターフェースの汎用実装
/// デリゲートを受け取ってコマンド処理を実行します
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    /// <summary>
    /// 引数を受け取るアクションを使用してインスタンスを初期化します
    /// </summary>
    /// <param name="execute">実行するアクション</param>
    /// <param name="canExecute">実行可能かどうかを判定する述語（省略可）</param>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// 引数なしアクションを使用してインスタンスを初期化します
    /// </summary>
    /// <param name="execute">実行するアクション</param>
    /// <param name="canExecute">実行可能かどうかを判定する述語（省略可）</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    /// <summary>
    /// 現在の状態でコマンドが実行可能かどうかを判定します
    /// </summary>
    /// <param name="parameter">コマンドパラメーター</param>
    /// <returns>実行可能な場合はtrue、それ以外はfalse</returns>
    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

    /// <summary>
    /// コマンドを実行します
    /// </summary>
    /// <param name="parameter">コマンドパラメーター</param>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// コマンドの実行可否状態が変化した際に発生するイベント
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
