using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AudioEffector.Infrastructure.Ipc;

/// <summary>
/// Audio Effector（WPF 本体）と MCP サーバ間の Named Pipe IPC パイプ名を解決するヘルパーです。
/// </summary>
public static class IpcPipeNameResolver
{
    /// <summary>
    /// Named Pipe 名のバージョン付きプレフィックス。
    /// </summary>
    public const string PIPE_NAME_PREFIX = "AudioEffector.Ipc.v1.";

    /// <summary>
    /// 現在のログオンユーザー SID ハッシュおよび Windows セッション ID に基づく既定の Named Pipe 名を返します。
    /// </summary>
    /// <returns>ユーザーおよび Windows セッション単位で一意な Named Pipe 名（<c>AudioEffector.Ipc.v1.&lt;CurrentUserSidHash&gt;.s&lt;SessionId&gt;</c>）。</returns>
    public static string GetDefaultPipeName()
    {
        string userIdentifier = GetCurrentUserIdentifier();
        int sessionId = GetCurrentSessionId();
        return CreatePipeName(userIdentifier, sessionId);
    }

    /// <summary>
    /// 指定されたユーザー識別子（SID 等）と Windows セッション ID から決定的な Named Pipe 名を生成します。
    /// </summary>
    /// <param name="userIdentifier">ユーザー識別子。</param>
    /// <param name="sessionId">Windows セッション ID。</param>
    /// <returns>生成された Named Pipe 名。</returns>
    internal static string CreatePipeName(string userIdentifier, int sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentifier);

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(userIdentifier));
        string shortHash = Convert.ToHexString(hashBytes.AsSpan(0, 8)).ToLowerInvariant();
        return string.Create(CultureInfo.InvariantCulture, $"{PIPE_NAME_PREFIX}{shortHash}.s{sessionId}");
    }

    private static int GetCurrentSessionId()
    {
        using Process currentProcess = Process.GetCurrentProcess();
        return currentProcess.SessionId;
    }

    private static string GetCurrentUserIdentifier()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (!string.IsNullOrWhiteSpace(identity.User?.Value))
            {
                return identity.User.Value;
            }
        }
        catch (Exception)
        {
            // SID 取得に失敗する特殊環境ではユーザー名へフォールバックする
        }

        return $"{Environment.UserDomainName}\\{Environment.UserName}";
    }
}
