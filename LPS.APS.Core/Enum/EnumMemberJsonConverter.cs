using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LPS.APS.Core.Enum;

/// <summary>
/// 以 EnumMemberAttribute.Value（契约字面量）序列化枚举的 JSON 转换器。
/// 反序列化兼容：契约字面量 / C# 成员名（均忽略大小写）/ 数字值。
/// 背景：STJ 内置 JsonStringEnumConverter 不读 EnumMember（实测输出 C# 成员名），
///       而 ScopeJsonV2（A 口径，2号位 2026-09-21 回执定案）需契约字面量
///       （如 NEW_ORDER_CTP / NORMAL / EXPEDITE），故自建统一转换器作单一真相。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class EnumMemberJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, System.Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
            {
                var numeric = reader.GetInt32();
                if (System.Enum.IsDefined(typeof(TEnum), numeric))
                {
                    return (TEnum)(object)numeric;
                }
                throw new JsonException($"未知数值 {numeric}，非 {typeof(TEnum).Name} 定义值");
            }
            case JsonTokenType.String:
            {
                var text = reader.GetString();
                if (text is null)
                {
                    throw new JsonException($"{typeof(TEnum).Name} 序列化值为 null（非合法枚举）");
                }
                foreach (var name in System.Enum.GetNames<TEnum>())
                {
                    var contract = typeof(TEnum).GetField(name)?.GetCustomAttribute<EnumMemberAttribute>(false)?.Value;
                    if (string.Equals(contract, text, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                    {
                        return System.Enum.Parse<TEnum>(name);
                    }
                }
                throw new JsonException($"未知 {typeof(TEnum).Name} 值 '{text}'（非契约字面量/成员名）");
            }
            default:
                throw new JsonException($"预期 {typeof(TEnum).Name} 为字符串或数字，实际 {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        var name = System.Enum.GetName(value);
        var contract = name is null
            ? null
            : typeof(TEnum).GetField(name)?.GetCustomAttribute<EnumMemberAttribute>(false)?.Value;
        writer.WriteStringValue(contract ?? name ?? value.ToString());
    }
}
