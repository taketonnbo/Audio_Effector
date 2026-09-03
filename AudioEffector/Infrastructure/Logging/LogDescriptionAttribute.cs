using System;

namespace AudioEffector.Infrastructure.Logging;

/// <summary>
/// メソッドのログ出力時に付与する説明属性
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public class LogDescriptionAttribute : Attribute
{
    /// <summary>
    /// 処理内容の説明文字列
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// LogDescriptionAttributeの新しいインスタンスを初期化します
    /// </summary>
    /// <param name="description">処理内容の説明</param>
    public LogDescriptionAttribute(string description)
    {
        Description = description;
    }
}
