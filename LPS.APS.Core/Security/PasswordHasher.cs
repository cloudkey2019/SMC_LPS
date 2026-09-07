using System.Security.Cryptography;
using System.Text;

namespace LPS.APS.Core.Security;

/// <summary>
/// 密码哈希工具（纯计算，无 I/O）
/// 采用 PBKDF2-SHA256（OWASP 推荐），存储格式：<c>PBKDF2$&lt;迭代次数&gt;$&lt;盐Base64&gt;$&lt;哈希Base64&gt;</c>。
/// 兼容历史无盐 SHA256+Base64 格式：<see cref="Verify"/> 仍可校验旧哈希，<see cref="NeedsRehash"/> 判定是否需重哈希。
/// </summary>
public static class PasswordHasher
{
    /// <summary>PBKDF2 迭代次数（OWASP 2021 对 PBKDF2-SHA256 的推荐值）。</summary>
    private const int Iterations = 210_000;

    /// <summary>随机盐长度（字节）。</summary>
    private const int SaltSize = 16;

    /// <summary>派生密钥长度（字节）。</summary>
    private const int KeySize = 32;

    /// <summary>PBKDF2 存储格式前缀。</summary>
    private const string Prefix = "PBKDF2$";

    /// <summary>生成带随机盐的 PBKDF2 哈希。</summary>
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>校验密码（兼容旧无盐 SHA256+Base64 格式）。</summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        return storedHash.StartsWith(Prefix, StringComparison.Ordinal)
            ? VerifyPbkdf2(password, storedHash)
            : VerifyLegacy(password, storedHash);
    }

    /// <summary>判定存储哈希是否为旧格式、需要在下次成功登录时重哈希。</summary>
    public static bool NeedsRehash(string storedHash)
        => !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool VerifyPbkdf2(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyLegacy(string password, string storedHash)
    {
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(storedHash));
    }
}
