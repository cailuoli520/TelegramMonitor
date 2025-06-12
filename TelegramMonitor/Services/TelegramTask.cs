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
                case UpdateNewMessage unm:
                    await unm.message.HandleMessageAsync(_clientManager, _systemCacheServices, _logger);
                    break;

                case UpdateNewChannelMessage uncm:
                    await uncm.message.HandleMessageAsync(_clientManager, _systemCacheServices, _logger);
                    break;

                case UpdateEditMessage uem:
                    _logger.LogInformation(
                        "{User} edited a message in {Chat}",
                        User(uem.message.From),
                        ChatBase(uem.message.Peer));
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
            _logger.LogError(ex, "处理更新时出错");
        }
    }
}
