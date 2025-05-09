namespace TelegramMonitor;

public static class TelegramExtensions
{
    public static string GetTelegramUserName(this User user) =>
        user?.ActiveUsernames != null && user.ActiveUsernames.Any()
            ? string.Join(" ", user.ActiveUsernames.Select(u => $"@{u}"))
            : string.Empty;

    public static string GetTelegramNickName(this User user)
    {
        if (user == null) return string.Empty;
        var fullName = $"{user.first_name}{user.last_name}".Trim();
        return string.IsNullOrEmpty(fullName) ? string.Empty : SecurityElement.Escape(fullName);
    }

    public static string GetTelegramUserLink(this User user)
    {
        if (user == null) return string.Empty;
        var name = user.GetTelegramNickName();
        if (string.IsNullOrEmpty(name)) name = user.id.ToString();
        return $"<a href=\"tg://user?id={user.id}\">{name}</a>";
    }

    public static bool CanSendMessages(this ChatBase chat) => chat switch
    {
        Chat small => !small.IsBanned(ChatBannedRights.Flags.send_messages),
        Channel ch when ch.IsChannel => !ch.IsBanned(ChatBannedRights.Flags.send_messages),
        Channel group => !group.IsBanned(ChatBannedRights.Flags.send_messages),
        _ => false
    };

    public static string GetChatType(this ChatBase chat) => chat switch
    {
        Chat => "Chat",
        Channel ch when ch.IsChannel => "Channel",
        Channel => "Group",
        _ => "Unknown"
    };

    public static async Task HandleMessageAsync(this MessageBase messageBase, TelegramClientManager clientManager,
        SystemCacheServices systemCacheServices, ILogger logger, bool edit = false)
    {
        if (edit) return;

        try
        {
            switch (messageBase)
            {
                case Message m:
                    await m.HandleTelegramMessageAsync(clientManager, systemCacheServices, logger);
                    break;

                case MessageService ms:
                    var updateManager = clientManager.GetUpdateManager();
                    logger.LogInformation("{From} in {Peer} [{Action}]",
                                         updateManager.UserOrChat(ms.from_id),
                                         updateManager.UserOrChat(ms.peer_id),
                                         ms.action.GetType().Name[13..]);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理消息时发生异常");
        }
    }

    public static async Task HandleTelegramMessageAsync(this Message message, TelegramClientManager clientManager,
        SystemCacheServices systemCacheServices, ILogger logger)
    {
        var updateManager = clientManager.GetUpdateManager();
        var chatBase = updateManager.Chats.GetValueOrDefault(message.peer_id);
        if (chatBase == null || !chatBase.IsGroup || string.IsNullOrWhiteSpace(message.message))
            return;

        var user = updateManager.Users.GetValueOrDefault(message.from_id);
        if (user == null) return;

        logger.LogInformation(
            "{Nick} (ID:{Uid}) 在 {Chat} 中发送：{Text}",
            user.GetTelegramNickName(), user.id,
            chatBase.Title, message.message);

        var ad = systemCacheServices.GetAdvertisement();
        var keywords = await systemCacheServices.GetKeywordsAsync() ?? new List<KeywordConfig>();

        var matchedUserKeywords = KeywordMatchExtensions.MatchUser(
                                      user.ID,
                                      user.ActiveUsernames?.ToList() ?? new List<string>(),
                                      keywords);

        if (matchedUserKeywords.Any(k => k.KeywordAction == KeywordAction.Exclude))
        {
            logger.LogInformation("{Nick} (ID:{Uid}) 在排除列表内，跳过",
                                   user.GetTelegramNickName(), user.id);
            return;
        }
        var client = await clientManager.GetClientAsync();
        var messageText = client.EntitiesToHtml(message.message, message.entities);

        if (matchedUserKeywords.Any(k => k.KeywordAction == KeywordAction.Monitor))
        {
            var content = message.FormatForMonitor(
                              chatBase, user, messageText,
                              matchedUserKeywords, ad);
            await message.SendMonitorMessageAsync(clientManager, logger, content);
            return;
        }

        var matchedKeywords = KeywordMatchExtensions.MatchText(message.message, keywords);

        if (matchedKeywords.Any(k => k.KeywordAction == KeywordAction.Exclude))
        {
            logger.LogInformation("消息包含排除关键词，跳过处理");
            return;
        }

        matchedKeywords = matchedKeywords
            .Where(k => k.KeywordAction == KeywordAction.Monitor)
            .ToList();

        if (matchedKeywords.Count == 0)
        {
            logger.LogInformation("无匹配关键词，跳过");
            return;
        }

        var msgContent = message.FormatForMonitor(
                             chatBase, user, messageText,
                             matchedKeywords, ad);

        await message.SendMonitorMessageAsync(clientManager, logger, msgContent);
    }

    public static async Task SendMonitorMessageAsync(this Message originalMessage,
        TelegramClientManager clientManager, ILogger logger, string content)
    {
        try
        {
            long sendChatId = clientManager.GetSendChatId();
            if (sendChatId == 0)
            {
                logger.LogWarning("未设置发送目标");
                return;
            }

            var updateManager = clientManager.GetUpdateManager();
            var chat = updateManager.Chats.GetValueOrDefault(sendChatId);
            if (chat == null)
            {
                logger.LogWarning("无法找到 ID 为 {Id} 的发送目标", sendChatId);
                return;
            }

            var client = await clientManager.GetClientAsync();
            var entities = client.HtmlToEntities(ref content, users: updateManager.Users);

            await client.SendMessageAsync(
                chat, content,
                preview: Client.LinkPreview.Disabled,
                entities: entities,
                media: originalMessage.media?.ToInputMedia());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送监控消息失败");
        }
    }
}