namespace LPS.APS.Core.Models;

/// <summary>
/// 排程快照元数据
/// </summary>
public class SnapshotInfo
{
    /// <summary>快照文件完整路径</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>原始JSON大小（字节）</summary>
    public long OriginalSize { get; set; }

    /// <summary>压缩后文件大小（字节）</summary>
    public long CompressedSize { get; set; }

    /// <summary>SHA256 哈希值（小写hex）</summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>快照创建时间</summary>
    public DateTime CreatedAt { get; set; }
}
