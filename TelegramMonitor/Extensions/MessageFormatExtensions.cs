namespace TelegramMonitor;

public static class MessageFormatExtensions
{
    /// <summary>
    /// Format a Telegram message into the HTML snippet used by the monitor, with a fix
    /// for channel posts (user id = 0) so that channel info is shown instead of 0.
    /// </summary>
    public static string FormatForMonitor(this Message message,
        ChatBase chat,
        User user,
        string messageText,
        IReadOnlyList<KeywordConfig> hitKeywords,
        string ad = null)
    {
        // Merge and apply keyword styles to the message text
        var mergedStyle = MergeKeywordStyles(hitKeywords);
        var styledText  = ApplyStylesToText(messageText, mergedStyle);

        var adSection = !string.IsNullOrWhiteSpace(ad)
            ? $"<b>{SecurityElement.Escape(ad)}</b>"
            : string.Empty;

        var plainList = string.Join(", ", hitKeywords.Select(k => k.KeywordContent));

        // —— Detect if this message is a channel post ——
        var isChannelPost = message.post ?? false;

        string senderIdDisplay;
        string senderNameDisplay;

        if (isChannelPost && chat is Channel channel)
        {
            // Message originates from the channel itself
            if (!string.IsNullOrEmpty(channel.MainUsername))
            {
                // Public channel – use @username
                senderIdDisplay = $"@{channel.MainUsername}";
            }
            else
            {
                // Private channel – fallback link
                senderIdDisplay = $"https://t.me/c/{channel.ID}";
            }

            senderNameDisplay = SecurityElement.Escape(channel.Title);
        }
        else
        {
            // Group / supergroup / private chat
            senderIdDisplay  = user?.id.ToString() ?? "<unknown>";
            senderNameDisplay = $"{user.GetTelegramUserLink()}  {user.GetTelegramUserName()}";
        }

        // Build the output
        var sb = new StringBuilder()
            .AppendLine($"内容：{styledText}")
            .AppendLine($"来源：<code>【{chat.Title}】</code>  {chat.MainUsername?.Insert(0, "@") ?? "无"}")
            .AppendLine($"时间：<code>{message.Date.AddHours(8):yyyy-MM-dd HH:mm:ss}</code>")
            .AppendLine($"用户ID：<code>{senderIdDisplay}</code>")
            .AppendLine($"用户：{senderNameDisplay}")
            .AppendLine($"链接：<a href=\"https://t.me/{chat.MainUsername ?? $"c/{chat.ID}"}/{message.id}\">【直达】</a>");

        return sb.ToString();
    }

    // Existing helper methods remain unchanged
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

        if (cfg.IsQuote)        result = $"<blockquote>{result}</blockquote>";
        if (cfg.IsSpoiler)      result = $"<tg-spoiler>{result}</tg-spoiler>";
        if (cfg.IsMonospace)    result = $"<code>{result}</code>";
        if (cfg.IsBold)         result = $"<b>{result}</b>";
        if (cfg.IsItalic)       result = $"<i>{result}</i>";
        if (cfg.IsUnderline)    result = $"<u>{result}</u>";
        if (cfg.IsStrikeThrough)result = $"<s>{result}</s>";

        return result;
    }
}
