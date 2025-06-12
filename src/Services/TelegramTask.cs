namespace TelegramMonitor;

public class TelegramTask
{
    private readonly ILogger<TelegramTask> _logger;
    private readonly SystemCacheServices _systemCacheServices;
    private readonly TelegramClientManager _clientManager;

    private volatile bool _running;
    public bool IsMonitoring => _running && _clientManager.IsLoggedIn;

    public TelegramTask(
        ILogger<TelegramTask> logger,
        SystemCacheServices cache,
        TelegramClientManager clientManager)
    {
        _logger = logger;
        _systemCacheServices = cache;
        _clientManager = clientManager;
    }

    private ChatBase ChatBase(long id) => _clientManager.GetUpdateManager()?.Chats.GetValueOrDefault(id);

    private User User(long id) => _clientManager.GetUpdateManager()?.Users.GetValueOrDefault(id);

    private IPeerInfo Peer(Peer peer) => _clientManager.GetUpdateManager().UserOrChat(peer);

    public async Task<MonitorStartResult> StartTaskAsync()
    {
        if (_clientManager.GetSendChatId() == 0) return MonitorStartResult.MissingTarget;
        if (!_clientManager.IsLoggedIn) return MonitorStartResult.Error;
        if (IsMonitoring) return MonitorStartResult.AlreadyRunning;

        try
        {
            var client = await _clientManager.GetClientAsync();
            var manager = _clientManager.GetUpdateManagerAsync(HandleUpdateAsync);
            var dialogs = await client.Messages_GetAllDialogs();
            dialogs.CollectUsersChats(manager.Users, manager.Chats);

            // 检查频道信息
            var channels = dialogs.Chats
                .Where(c => c.Value is Channel)
                .Select(c => c.Value as Channel);
            _logger.LogDebug("已加载频道数: {Count}", channels.Count());

            if (client.User == null) return MonitorStartResult.NoUserInfo;

            _running = true;
            _logger.LogInformation("监控启动成功");
            return MonitorStartResult.Started;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "监控启动失败");
            _running = false;
            return MonitorStartResult.Error;
        }
    }

    public async Task StopTaskAsync()
    {
        _running = false;
        await _clientManager.StopUpdateManagerAsync();
        _logger.LogError("主动停止监控");
    }

    private async Task HandleUpdateAsync(Update update)
    {
        try
        {
            switch (update)
            {
                // 合并处理普通消息和频道消息
                case UpdateNewMessage unm:
                case UpdateNewChannelMessage uncm:
                    var message = unm?.message ?? uncm?.message;
                    if (message != null)
                    {
                        await message.HandleMessageAsync(_clientManager, _systemCacheServices, _logger);
                    }
                    break;

                case UpdateEditMessage uem:
                    _logger.LogInformation(
                        "{User} edited a message in {Chat}",
                        User(uem.message.From),
                        ChatBase(uem.message.Peer));
                    break;
                    
                // 添加频道消息编辑处理
                case UpdateEditChannelMessage uecm:
                    _logger.LogInformation(
                        "频道消息编辑: {ChatID}",
                        uecm.channel_id);
                    break;

                case UpdateDeleteChannelMessages udcm:
                    _logger.LogInformation("{Count} message(s) deleted in {Chat}",
                                           udcm.messages.Length,
                                           ChatBase(udcm.channel_id));
                    break;

                case UpdateDeleteMessages udm:
                    _logger.LogInformation("{Count} message(s) deleted",
                                           udm.messages.Length);
                    break;

                case UpdateUserTyping uut:
                    _logger.LogInformation("{User} is {Action}",
                                           User(uut.user_id), uut.action);
                    break;

                case UpdateChatUserTyping ucut:
                    _logger.LogInformation("{Peer} is {Action} in {Chat}",
                                           Peer(ucut.from_id), ucut.action,
                                           ChatBase(ucut.chat_id));
                    break;

                case UpdateChannelUserTyping ucut2:
                    _logger.LogInformation("{Peer} is {Action} in {Chat}",
                                           Peer(ucut2.from_id), ucut2.action,
                                           ChatBase(ucut2.channel_id));
                    break;

                case UpdateChatParticipants { participants: ChatParticipants cp }:
                    _logger.LogInformation("{Count} participants in {Chat}",
                                           cp.participants.Length,
                                           ChatBase(cp.chat_id));
                    break;

                case UpdateUserStatus uus:
                    _logger.LogInformation("{User} is now {Status}",
                                           User(uus.user_id),
                                           uus.status.GetType().Name[10..]);
                    break;

                case UpdateUserName uun:
                    _logger.LogInformation("{User} changed profile name: {FN} {LN}",
                                           User(uun.user_id),
                                           uun.first_name, uun.last_name);
                    break;

                case UpdateUser uu:
                    _logger.LogInformation("{User} changed infos/photo",
                                           User(uu.user_id));
                    break;

                default:
                    _logger.LogInformation(update.GetType().Name);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 Update 时发生异常");
        }
    }
}

// 添加频道消息处理扩展方法
public static class MessageExtensions
{
    public static async Task HandleMessageAsync(
        this Message message,
        TelegramClientManager clientManager,
        SystemCacheServices cacheServices,
        ILogger logger)
    {
        try
        {
            // 获取消息的Peer信息
            var peerInfo = clientManager.GetUpdateManager().UserOrChat(message.Peer);
            
            // 区分频道消息和普通消息
            string sourceType = message.Peer is PeerChannel ? "频道" : "聊天";
            
            logger.LogInformation("收到{SourceType}消息: [{Peer}] {Text}", 
                sourceType, 
                peerInfo?.Title ?? "未知来源",
                message.message);
            
            // 在这里添加实际的消息处理逻辑
            // 例如：cacheServices.ProcessMessage(message);
            
            // 如果需要回复消息
            if (clientManager.GetSendChatId() != 0)
            {
                var client = await clientManager.GetClientAsync();
                await client.SendMessageAsync(
                    new InputPeerChat(clientManager.GetSendChatId()),
                    $"收到来自 {peerInfo?.Title} 的消息: {message.message}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "处理消息时发生异常");
        }
    }
}
