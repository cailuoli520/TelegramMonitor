namespace TelegramMonitor;

public sealed class TelegramClientManager : ISingleton, IAsyncDisposable
{
    private readonly ILogger<TelegramClientManager> _logger;
    private Client _client;
    private UpdateManager _manager;

    private string _phone;
    private string _proxyUrl;
    private ProxyType _proxyType = ProxyType.None;
    private Client.TcpFactory _directTcp;
    private long _sendChatId;

    public TelegramClientManager(ILogger<TelegramClientManager> logger) => _logger = logger;

    public bool IsLoggedIn => _client is { Disconnected: false } && _client.User != null;
    public string GetPhone => _phone ?? string.Empty;

    public async Task<LoginState> LoginAsync(string phoneNumber, string loginInfo)
    {
        phoneNumber = (phoneNumber ?? string.Empty).Replace(" ", "").Trim();
        loginInfo = (loginInfo ?? string.Empty).Replace(" ", "").Trim();
        if (!phoneNumber.IsE164Phone())
            throw new ArgumentException("手机号码格式不正确", nameof(phoneNumber));

        if (phoneNumber != _phone && _client != null)
        {
            await _client.DisposeAsync();
            _client = null;
            _manager = null;
        }

        _phone = phoneNumber;

        EnsureClientCreated();
        ApplyProxy();
        var firstArg = string.IsNullOrWhiteSpace(loginInfo) ? phoneNumber : loginInfo;
        var result = await _client.Login(firstArg);

        while (result is "name")
            result = await _client.Login("by riniba");

        return result switch
        {
            "verification_code" => LoginState.WaitingForVerificationCode,
            "password" => LoginState.WaitingForPassword,
            null => IsLoggedIn ? LoginState.LoggedIn : LoginState.NotLoggedIn,
            _ => LoginState.NotLoggedIn
        };
    }

    public async Task<LoginState> ConnectAsync(string phoneNumber)
    {
        await _client.LoginUserIfNeeded();
        if (IsLoggedIn)
        {
            return LoginState.LoggedIn;
        }
        else
        {
            return await LoginAsync(phoneNumber, string.Empty);
        }
    }

    public async Task<LoginState> SetProxyAsync(ProxyType type, string url)
    {
        _proxyType = type;
        _proxyUrl = url;

        if (_client == null) return LoginState.NotLoggedIn;

        if (!IsLoggedIn)
        {
            ApplyProxy();
            return LoginState.NotLoggedIn;
        }

        string phone = _phone;

        await _client.DisposeAsync();
        _client = null;
        _manager = null;

        EnsureClientCreated();
        ApplyProxy();
        var result = await _client.Login(phone);

        while (result is "name")
            result = await _client.Login("by riniba");

        return result switch
        {
            "verification_code" => LoginState.WaitingForVerificationCode,
            "password" => LoginState.WaitingForPassword,
            null => IsLoggedIn ? LoginState.LoggedIn : LoginState.NotLoggedIn,
            _ => LoginState.NotLoggedIn
        };
    }

    public async Task<Client> GetClientAsync()
    {
        if (_client.Disconnected) await _client.Login(_phone);
        if (!IsLoggedIn) throw new InvalidOperationException("未登录");

        return _client;
    }

    public UpdateManager GetUpdateManager() => _manager;

    public UpdateManager GetUpdateManagerAsync(Func<Update, Task> onUpdate)
    {
        if (_manager != null) return _manager;
        _manager = _client.WithUpdateManager(onUpdate);

        return _manager;
    }

    public async Task StopUpdateManagerAsync()
    {
        if (_manager != null)
        {
            await _client.DisposeAsync();
            _manager = null;
            _client = null;
            await LoginAsync(_phone, string.Empty);
        }
    }

    public async Task<List<DisplayDialogs>> DialogsAsync()
    {
        if (_client == null)
            throw new InvalidOperationException("未登录");

        var dialogs = await _client.Messages_GetAllDialogs();

        var availableChats = dialogs.chats.Values
            .Where(c => c.IsActive && c.CanSendMessages())
            .ToList();

        return availableChats.Select(c => new DisplayDialogs
        {
            Id = c.ID,
            DisplayTitle = $"[{c.GetChatType()}]{c.Title}"
        }).ToList();
    }

    public void SetSendChatId(long chatId) => _sendChatId = chatId;

    public long GetSendChatId() => _sendChatId;

    private void EnsureClientCreated()
    {
        if (_client != null) return;
        _client = new Client(
            TelegramMonitorConstants.ApiId,
            TelegramMonitorConstants.ApiHash,
            ClientPath());
        _directTcp = _client.TcpHandler;
    }

    private void ApplyProxy()
    {
        if (_client == null) return;

        switch (_proxyType)
        {
            case ProxyType.Socks5:
                _client.TcpHandler = (host, port) =>
                {
                    var p = Socks5ProxyClient.Parse(_proxyUrl);
                    return Task.FromResult(p.CreateConnection(host, port));
                };
                _client.MTProxyUrl = null;
                break;

            case ProxyType.MTProxy:
                _client.MTProxyUrl = _proxyUrl;
                _client.TcpHandler = _directTcp;
                break;

            case ProxyType.None:
            default:
                _client.TcpHandler = _directTcp;
                _client.MTProxyUrl = null;
                break;
        }
    }

    private string ClientPath() =>
        Path.Combine(TelegramMonitorConstants.SessionPath, $"{_phone}.session");

    public async ValueTask DisposeAsync()
    {
        if (_client != null) await _client.DisposeAsync();
        _client = null;
        _manager = null;
    }
}