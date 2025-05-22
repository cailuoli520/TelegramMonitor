namespace TelegramMonitor;

public static class MessageFormatExtensions
{
    public static string FormatForMonitor(this Message message,
        ChatBase chat,
        User user,
        string messageText,
        IReadOnlyList<KeywordConfig> hitKeywords,
        string ad = null)
    {
        var mergedStyle = MergeKeywordStyles(hitKeywords);
        var styledText = ApplyStylesToText(messageText, mergedStyle);

        var adSection = !string.IsNullOrWhiteSpace(ad)
            ? $"<b>{SecurityElement.Escape(ad)}</b>"
            : string.Empty;

        var plainList = string.Join(", ", hitKeywords.Select(k => k.KeywordContent));

        var sb = new StringBuilder()
            .AppendLine($"<b>命中关键词：</b>#{plainList}")
            .AppendLine($"用户ID：<code>{user.id}</code>")
            .AppendLine($"用户：{user.GetTelegramUserLink()}  {user.GetTelegramUserName()}")
            .AppendLine($"来源：<code>【{chat.Title}】</code>  {chat.MainUsername?.Insert(0, "@") ?? "无"}")
            .AppendLine($"时间：<code>{message.Date.AddHours(8):yyyy-MM-dd HH:mm:ss}</code>")
            .AppendLine($"内容：{styledText}")
            .AppendLine($"链接：<a href=\"https://t.me/{chat.MainUsername ?? $"c/{chat.ID}"}/{message.id}\">【直达】</a>")
            .AppendLine("--------------------------------")
            .Append(" <a href=\"https://386868.xyz/?invite=1004\">tg成品号</a>&nbsp;&nbsp;&nbsp;&nbsp;<a href=\"https://t.me/sousoukan3bot\">代码搜索bot</a>");

        return sb.ToString();
    }

    private static KeywordConfig MergeKeywordStyles(IEnumerable<KeywordConfig> list)
    {
        var merged = new KeywordConfig();
        foreach (var k in list)
        {
            merged.IsBold |= k.IsBold;
            merged.IsItalic |= k.IsItalic;
            merged.IsUnderline |= k.IsUnderline;
            merged.IsStrikeThrough |= k.IsStrikeThrough;
            merged.IsQuote |= k.IsQuote;
            merged.IsMonospace |= k.IsMonospace;
            merged.IsSpoiler |= k.IsSpoiler;
        }
        return merged;
    }

    private static string ApplyStylesToText(string text, KeywordConfig cfg)
    {
        var result = text ?? string.Empty;

        if (cfg.IsQuote) result = $"<blockquote>{result}</blockquote>";
        if (cfg.IsSpoiler) result = $"<tg-spoiler>{result}</tg-spoiler>";
        if (cfg.IsMonospace) result = $"<code>{result}</code>";
        if (cfg.IsBold) result = $"<b>{result}</b>";
        if (cfg.IsItalic) result = $"<i>{result}</i>";
        if (cfg.IsUnderline) result = $"<u>{result}</u>";
        if (cfg.IsStrikeThrough) result = $"<s>{result}</s>";

        return result;
    }
}
