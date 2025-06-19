namespace TelegramMonitor.Extensions
{
    /// <summary>
    /// Message formatting helper with fix for channel posts (user.id == 0).
    /// Compatible with WTelegram versions that do NOT expose `Message.post`.
    /// </summary>
    public static class MessageFormatExtensions
    {
        /// <summary>
        /// Render monitor HTML for a Telegram message.
        /// The parameter list keeps与原始项目一致; 如果你的项目签名不同，请对应更改。
        /// </summary>
        public static string FormatForMonitor(
            this MessageBase message,
            User user,
            ChatBase chat,
            string plainText      = null,
            IReadOnlyList<KeywordConfig> hitKeywords = null,
            string ad             = null)
        {
            //------------------------------------------------------------------
            // Build timestamp line (UTC+8 as per original)
            //------------------------------------------------------------------
            var sb = new StringBuilder()
                .AppendLine($"时间：<code>{message.Date.AddHours(8):yyyy-MM-dd HH:mm:ss}</code>");

            //------------------------------------------------------------------
            // Detect whether the message comes from a channel itself.
            // Condition: chat is Channel && (user is null OR user.id == 0)
            //------------------------------------------------------------------
            var channel        = chat as Channel;
            bool isChannelPost = channel != null && (user == null || user.id == 0);

            string senderIdDisplay;
            string senderNameDisplay;

            if (isChannelPost)
            {
                // 公共频道就用 @username；私有频道 fallback 到 /c 链接
                senderIdDisplay = !string.IsNullOrEmpty(channel.MainUsername)
                    ? $"@{channel.MainUsername}"
                    : $"https://t.me/c/{channel.ID}";

                senderNameDisplay = SecurityElement.Escape(channel.Title);
            }
            else
            {
                senderIdDisplay  = user?.id.ToString() ?? "<unknown>";
                senderNameDisplay = user != null
                    ? $"{user.GetTelegramUserLink()}  {user.GetTelegramUserName()}"
                    : "<unknown>";
            }

            sb.AppendLine($"用户ID：<code>{senderIdDisplay}</code>")
              .AppendLine($"用户：{senderNameDisplay}");

            //------------------------------------------------------------------
            // Message text + keyword styling (保持原项目逻辑，可删除此段)
            //------------------------------------------------------------------
            if (!string.IsNullOrEmpty(plainText))
            {
                // 合并关键词样式
                var mergedStyle = MergeKeywordStyles(hitKeywords ?? Array.Empty<KeywordConfig>());
                var styledText  = ApplyStylesToText(plainText, mergedStyle);

                sb.AppendLine().AppendLine(styledText);

                if (!string.IsNullOrEmpty(ad))
                    sb.AppendLine($"<b>{SecurityElement.Escape(ad)}</b>");

                if (hitKeywords?.Count > 0)
                    sb.AppendLine($"命中关键词：{string.Join(", ", hitKeywords.Select(k => k.KeywordContent))}");
            }

            return sb.ToString();
        }

        // -------------------------- helpers -------------------------------
        private static KeywordConfig MergeKeywordStyles(IEnumerable<KeywordConfig> list)
        {
            var merged = new KeywordConfig();
            foreach (var k in list)
            {
                merged.IsBold         |= k.IsBold;
                merged.IsItalic       |= k.IsItalic;
                merged.IsUnderline    |= k.IsUnderline;
                merged.IsStrikeThrough|= k.IsStrikeThrough;
                merged.IsQuote        |= k.IsQuote;
                merged.IsMonospace    |= k.IsMonospace;
                merged.IsSpoiler      |= k.IsSpoiler;
            }
            return merged;
        }

        private static string ApplyStylesToText(string text, KeywordConfig cfg)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var result = WebUtility.HtmlEncode(text);

            if (cfg.IsQuote)         result = $"<blockquote>{result}</blockquote>";
            if (cfg.IsSpoiler)       result = $"<tg-spoiler>{result}</tg-spoiler>";
            if (cfg.IsMonospace)     result = $"<code>{result}</code>";
            if (cfg.IsBold)          result = $"<b>{result}</b>";
            if (cfg.IsItalic)        result = $"<i>{result}</i>";
            if (cfg.IsUnderline)     result = $"<u>{result}</u>";
            if (cfg.IsStrikeThrough) result = $"<s>{result}</s>";

            return result;
        }
    }

    // Dummy KeywordConfig definition (remove if original project already defines it)
    public class KeywordConfig
    {
        public string KeywordContent { get; set; } = string.Empty;
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderline { get; set; }
        public bool IsStrikeThrough { get; set; }
        public bool IsQuote { get; set; }
        public bool IsMonospace { get; set; }
        public bool IsSpoiler { get; set; }
    }
}
