using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ShuBingDesktopPet.Services;

public enum TwitchConnectionState
{
    Disabled,
    AuthorizationRequired,
    Authorizing,
    Connecting,
    Connected,
    Reconnecting,
    Error
}

public sealed record TwitchStatus(TwitchConnectionState State, string Message, string ChannelName);

/// <summary>Twitch Device Code OAuth and EventSub WebSocket client for cheers and custom reward redemptions.</summary>
public sealed class TwitchEventSubClient : IAsyncDisposable
{
    private const string DefaultWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
    private const int MessageIdCapacity = 512;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TwitchTokenStore _store;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _socketGate = new();
    private readonly object _stateGate = new();
    private readonly object _messageIdGate = new();
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _messageIdOrder = new();
    private readonly Queue<(Action<TwitchStatus>? Handler, TwitchStatus Status)> _statusNotifications = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _startupCts;
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _authorizationCts;
    private Task? _receiveTask;
    private string _channel = string.Empty;
    private volatile bool _disposed;
    private long _operationVersion;
    private bool _dispatchingStatus;

    public TwitchConnectionState State { get; private set; } = TwitchConnectionState.Disabled;
    public string StatusMessage { get; private set; } = "未啟用";
    public string ChannelName => _channel;

    public event Action<TwitchStatus>? StatusChanged;
    public event Action<TwitchCheer>? CheerReceived;
    public event Action<TwitchChannelPointsRedemption>? ChannelPointsRewardRedeemed;
    public event Action<string, string>? DeviceCodeReady;

    public TwitchEventSubClient(TwitchTokenStore store, HttpClient? http = null)
    {
        _store = store;
        _http = http ?? new HttpClient();
        _ownsHttpClient = http is null;
    }

    public async Task StartAsync(string clientId, bool enableChannelPoints = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        clientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            var emptyClientVersion = StopCore(updateStatus: false);
            SetStateIfCurrent(emptyClientVersion, TwitchConnectionState.AuthorizationRequired, "請先設定 Twitch Client ID");
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? startupCts = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var operationVersion = StopCore(updateStatus: false);
            startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_socketGate)
            {
                if (Volatile.Read(ref _operationVersion) != operationVersion || _disposed)
                {
                    return;
                }

                _startupCts = startupCts;
            }

            var startupToken = startupCts.Token;
            var storedToken = _store.Load();
            if (storedToken is null || !string.Equals(storedToken.ClientId, clientId, StringComparison.Ordinal))
            {
                SetStateIfCurrent(operationVersion, TwitchConnectionState.AuthorizationRequired, "需要 Twitch 授權");
                return;
            }

            TwitchTokenStore.Token token;
            try
            {
                token = await EnsureValidTokenAsync(storedToken, enableChannelPoints, startupToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
            {
                SetStateIfCurrent(operationVersion, TwitchConnectionState.AuthorizationRequired, "Twitch 授權無效，請重新授權");
                return;
            }

            var lifetimeCts = new CancellationTokenSource();
            WebSocketSession? session = null;
            var activated = false;
            try
            {
                session = await OpenSessionAsync(
                    token,
                    DefaultWebSocketUrl,
                    true,
                    enableChannelPoints,
                    operationVersion,
                    startupToken).ConfigureAwait(false);

                lock (_socketGate)
                {
                    if (ReferenceEquals(_startupCts, startupCts) &&
                        Volatile.Read(ref _operationVersion) == operationVersion &&
                        !startupToken.IsCancellationRequested &&
                        !_disposed)
                    {
                        _startupCts = null;
                        _channel = token.ChannelName;
                        _lifetimeCts = lifetimeCts;
                        _socket = session.Socket;
                        _receiveTask = Task.Run(
                            () => ReceiveLoopAsync(token, session, enableChannelPoints, lifetimeCts, operationVersion),
                            CancellationToken.None);
                        activated = true;
                    }
                }

                if (!activated)
                {
                    return;
                }

                SetStateIfCurrent(operationVersion, TwitchConnectionState.Connected, ConnectedMessage());
            }
            finally
            {
                if (!activated)
                {
                    lifetimeCts.Dispose();
                    AbortAndDispose(session?.Socket);
                }
            }
        }
        catch (OperationCanceledException) when (startupCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            // Stop() cancels an in-progress startup; that is a normal lifecycle transition.
        }
        finally
        {
            if (startupCts is not null)
            {
                lock (_socketGate)
                {
                    if (ReferenceEquals(_startupCts, startupCts))
                    {
                        _startupCts = null;
                    }
                }

                startupCts.Dispose();
            }

            _lifecycleGate.Release();
        }
    }

    public async Task AuthorizeAsync(string clientId, bool enableChannelPoints = false, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        clientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("請先設定 Twitch Client ID");
        }

        var operationVersion = StopCore(updateStatus: false);
        using var authorizationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_socketGate)
        {
            if (Volatile.Read(ref _operationVersion) != operationVersion || _disposed)
            {
                return;
            }

            _authorizationCts = authorizationCts;
        }
        cancellationToken = authorizationCts.Token;

        var scopes = RequiredScopes(enableChannelPoints);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetStateIfCurrent(operationVersion, TwitchConnectionState.Authorizing, "正在取得 Twitch 授權…");
            using var deviceRequest = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/device")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scopes"] = scopes
                })
            };
            using var deviceResponse = await _http.SendAsync(deviceRequest, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessWithMessageAsync(deviceResponse, "無法開始 Twitch 授權", cancellationToken).ConfigureAwait(false);
            using var deviceDocument = JsonDocument.Parse(await deviceResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var device = deviceDocument.RootElement;
            var deviceCode = GetRequiredString(device, "device_code");
            var userCode = GetRequiredString(device, "user_code");
            var verificationUri = GetRequiredString(device, "verification_uri");
            var intervalSeconds = device.TryGetProperty("interval", out var intervalElement) && intervalElement.TryGetInt32(out var interval)
                ? Math.Clamp(interval, 1, 60)
                : 5;
            var expiresUtc = DateTime.UtcNow.AddSeconds(device.GetProperty("expires_in").GetInt32());
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCurrentOperation(operationVersion))
            {
                DeviceCodeReady?.Invoke(verificationUri, userCode);
            }

            while (DateTime.UtcNow < expiresUtc)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);
                using var pollResponse = await _http.PostAsync(
                    "https://id.twitch.tv/oauth2/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = clientId,
                        ["scopes"] = scopes,
                        ["device_code"] = deviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    }),
                    cancellationToken).ConfigureAwait(false);
                var responseText = await pollResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (pollResponse.IsSuccessStatusCode)
                {
                    using var tokenDocument = JsonDocument.Parse(responseText);
                    var tokenData = tokenDocument.RootElement;
                    var accessToken = GetRequiredString(tokenData, "access_token");
                    var refreshToken = GetRequiredString(tokenData, "refresh_token");
                    var validation = await ValidateAsync(accessToken, cancellationToken).ConfigureAwait(false);
                    ValidateAuthorization(validation, clientId, enableChannelPoints);
                    var token = new TwitchTokenStore.Token(
                        accessToken,
                        refreshToken,
                        clientId,
                        DateTime.UtcNow.AddSeconds(validation.ExpiresIn),
                        validation.UserId,
                        validation.Login);
                    lock (_socketGate)
                    {
                        if (!ReferenceEquals(_authorizationCts, authorizationCts) ||
                            Volatile.Read(ref _operationVersion) != operationVersion ||
                            _disposed)
                        {
                            return;
                        }

                        _store.Save(token);
                    }
                    ClearAuthorization(authorizationCts);
                    await StartAsync(clientId, enableChannelPoints, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var error = ReadOAuthError(responseText);
                if (error == "authorization_pending")
                {
                    continue;
                }
                if (error == "slow_down")
                {
                    intervalSeconds = Math.Min(60, intervalSeconds + 5);
                    continue;
                }
                if (error is "expired_token" or "access_denied")
                {
                    throw new InvalidOperationException("Twitch 授權已過期或被拒絕");
                }
                throw new InvalidOperationException("Twitch 授權失敗");
            }

            throw new TimeoutException("Twitch 授權逾時");
        }
        finally
        {
            ClearAuthorization(authorizationCts);
        }
    }

    public void Stop() => StopCore(updateStatus: true);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var receiveTask = Volatile.Read(ref _receiveTask);
        StopCore(updateStatus: false);
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
            }
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        _lifecycleGate.Release();
        _lifecycleGate.Dispose();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(
        TwitchTokenStore.Token initialToken,
        WebSocketSession initialSession,
        bool enableChannelPoints,
        CancellationTokenSource lifetimeCts,
        long operationVersion)
    {
        var cancellationToken = lifetimeCts.Token;
        var token = initialToken;
        var session = initialSession;
        var retryAttempt = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var json = await ReceiveWithKeepaliveTimeoutAsync(session, cancellationToken).ConfigureAwait(false);
                    retryAttempt = 0;
                    if (TwitchEventSubProtocol.TryParseCheer(json, out var cheer) && IsNewMessageId(cheer.MessageId))
                    {
                        CheerReceived?.Invoke(cheer);
                        continue;
                    }
                    if (enableChannelPoints &&
                        TwitchEventSubProtocol.TryParseChannelPointsRedemption(json, out var redemption) &&
                        IsNewMessageId(redemption.MessageId))
                    {
                        ChannelPointsRewardRedeemed?.Invoke(redemption);
                        continue;
                    }
                    if (!TwitchEventSubProtocol.TryParseReconnect(json, out var reconnect))
                    {
                        continue;
                    }

                    SetStateIfCurrent(operationVersion, TwitchConnectionState.Reconnecting, "Twitch 正在無縫換線…");
                    var replacement = await OpenSessionAsync(
                        token,
                        reconnect.ReconnectUrl,
                        false,
                        enableChannelPoints,
                        operationVersion,
                        cancellationToken).ConfigureAwait(false);
                    if (!TrySwapSocket(replacement.Socket, operationVersion))
                    {
                        AbortAndDispose(replacement.Socket);
                        return;
                    }
                    AbortAndDispose(session.Socket);
                    session = replacement;
                    SetStateIfCurrent(operationVersion, TwitchConnectionState.Connected, ConnectedMessage());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is WebSocketException or TimeoutException or IOException or HttpRequestException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    AbortAndDispose(session.Socket);
                    SetStateIfCurrent(operationVersion, TwitchConnectionState.Reconnecting, "Twitch 連線中斷，正在重試…");
                    retryAttempt++;
                    try
                    {
                        await Task.Delay(RetryDelay(retryAttempt), cancellationToken).ConfigureAwait(false);
                        token = await EnsureValidTokenAsync(token, enableChannelPoints, cancellationToken).ConfigureAwait(false);
                        session = await OpenSessionAsync(
                            token,
                            DefaultWebSocketUrl,
                            true,
                            enableChannelPoints,
                            operationVersion,
                            cancellationToken).ConfigureAwait(false);
                        if (!TrySwapSocket(session.Socket, operationVersion))
                        {
                            AbortAndDispose(session.Socket);
                            return;
                        }
                        SetStateIfCurrent(operationVersion, TwitchConnectionState.Connected, ConnectedMessage());
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception reconnectException) when (reconnectException is WebSocketException or TimeoutException or IOException or HttpRequestException or InvalidOperationException)
                    {
                        SetStateIfCurrent(operationVersion, TwitchConnectionState.Reconnecting, "Twitch 重連失敗，稍後再試…");
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        SetStateIfCurrent(operationVersion, TwitchConnectionState.Error, "Twitch 連線錯誤：" + ex.Message);
                    }
                    return;
                }
            }
        }
        finally
        {
            ClientWebSocket? activeSocket = null;
            lock (_socketGate)
            {
                if (ReferenceEquals(_lifetimeCts, lifetimeCts))
                {
                    activeSocket = _socket;
                    _socket = null;
                    _lifetimeCts = null;
                    _receiveTask = null;
                }
            }

            AbortAndDispose(activeSocket);
            lifetimeCts.Dispose();
        }
    }

    private async Task<WebSocketSession> OpenSessionAsync(
        TwitchTokenStore.Token token,
        string url,
        bool createSubscription,
        bool enableChannelPoints,
        long operationVersion,
        CancellationToken cancellationToken)
    {
        SetStateIfCurrent(
            operationVersion,
            createSubscription ? TwitchConnectionState.Connecting : TwitchConnectionState.Reconnecting,
            createSubscription ? "正在連線 Twitch…" : "Twitch 正在無縫換線…");
        var socket = new ClientWebSocket();
        try
        {
            using var welcomeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            welcomeCts.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new Uri(url), welcomeCts.Token).ConfigureAwait(false);
            var welcomeJson = await ReceiveAsync(socket, welcomeCts.Token).ConfigureAwait(false);
            if (!TwitchEventSubProtocol.TryParseWelcome(welcomeJson, out var welcome))
            {
                throw new InvalidOperationException("Twitch welcome 無效");
            }
            if (createSubscription)
            {
                await SubscribeAsync(token, welcome.SessionId, enableChannelPoints, welcomeCts.Token).ConfigureAwait(false);
            }
            return new WebSocketSession(socket, welcome.KeepaliveTimeoutSeconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AbortAndDispose(socket);
            throw new TimeoutException("等待 Twitch WebSocket welcome 逾時");
        }
        catch
        {
            AbortAndDispose(socket);
            throw;
        }
    }

    private async Task SubscribeAsync(
        TwitchTokenStore.Token token,
        string sessionId,
        bool enableChannelPoints,
        CancellationToken cancellationToken)
    {
        await CreateSubscriptionAsync(
            token,
            sessionId,
            "channel.cheer",
            "無法建立 Twitch Cheer 訂閱",
            cancellationToken).ConfigureAwait(false);

        if (enableChannelPoints)
        {
            await CreateSubscriptionAsync(
                token,
                sessionId,
                "channel.channel_points_custom_reward_redemption.add",
                "無法建立 Twitch 頻道點數訂閱",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateSubscriptionAsync(
        TwitchTokenStore.Token token,
        string sessionId,
        string subscriptionType,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Client-Id", token.ClientId);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                type = subscriptionType,
                version = "1",
                condition = new { broadcaster_user_id = token.BroadcasterUserId },
                transport = new { method = "websocket", session_id = sessionId }
            }),
            Encoding.UTF8,
            "application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessWithMessageAsync(response, errorMessage, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TwitchTokenStore.Token> EnsureValidTokenAsync(
        TwitchTokenStore.Token token,
        bool enableChannelPoints,
        CancellationToken cancellationToken)
    {
        if (token.ExpiresUtc <= DateTime.UtcNow.AddMinutes(5))
        {
            token = await RefreshAsync(token, enableChannelPoints, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var validation = await ValidateAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
            ValidateAuthorization(validation, token.ClientId, enableChannelPoints);
            token = token with
            {
                ExpiresUtc = DateTime.UtcNow.AddSeconds(validation.ExpiresIn),
                BroadcasterUserId = validation.UserId,
                ChannelName = validation.Login
            };
            _store.Save(token);
            _channel = token.ChannelName;
            return token;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            token = await RefreshAsync(token, enableChannelPoints, cancellationToken).ConfigureAwait(false);
            _store.Save(token);
            _channel = token.ChannelName;
            return token;
        }
    }

    private async Task<TwitchTokenStore.Token> RefreshAsync(
        TwitchTokenStore.Token token,
        bool enableChannelPoints,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(
            "https://id.twitch.tv/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = token.ClientId,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessWithMessageAsync(response, "Twitch token 更新失敗", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var data = document.RootElement;
        var validation = await ValidateAsync(GetRequiredString(data, "access_token"), cancellationToken).ConfigureAwait(false);
        ValidateAuthorization(validation, token.ClientId, enableChannelPoints);
        var refreshed = token with
        {
            AccessToken = GetRequiredString(data, "access_token"),
            RefreshToken = data.TryGetProperty("refresh_token", out var refreshElement) ? refreshElement.GetString() ?? token.RefreshToken : token.RefreshToken,
            ExpiresUtc = DateTime.UtcNow.AddSeconds(validation.ExpiresIn),
            BroadcasterUserId = validation.UserId,
            ChannelName = validation.Login
        };
        _store.Save(refreshed);
        return refreshed;
    }

    private async Task<TokenValidation> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessWithMessageAsync(response, "Twitch token 驗證失敗", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var data = document.RootElement;
        var scopes = data.TryGetProperty("scopes", out var scopesElement) && scopesElement.ValueKind == JsonValueKind.Array
            ? scopesElement.EnumerateArray().Select(scope => scope.GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
        return new TokenValidation(
            GetRequiredString(data, "client_id"),
            GetRequiredString(data, "user_id"),
            GetRequiredString(data, "login"),
            data.GetProperty("expires_in").GetInt32(),
            scopes);
    }

    private static void ValidateAuthorization(TokenValidation validation, string expectedClientId, bool enableChannelPoints)
    {
        if (!string.Equals(validation.ClientId, expectedClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Twitch token 與 Client ID 不相符");
        }
        if (!validation.Scopes.Contains("bits:read", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Twitch token 缺少 bits:read 權限");
        }
        if (enableChannelPoints && !validation.Scopes.Contains("channel:read:redemptions", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Twitch token 缺少 channel:read:redemptions 權限，請重新授權");
        }
    }

    private static string RequiredScopes(bool enableChannelPoints) => enableChannelPoints
        ? "bits:read channel:read:redemptions"
        : "bits:read";

    private async Task<string> ReceiveWithKeepaliveTimeoutAsync(WebSocketSession session, CancellationToken cancellationToken)
    {
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveCts.CancelAfter(TimeSpan.FromSeconds(session.KeepaliveTimeoutSeconds + 5));
        try
        {
            return await ReceiveAsync(session.Socket, receiveCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Twitch keepalive 逾時");
        }
    }

    private static async Task<string> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("Twitch WebSocket 已關閉");
            }
            if (result.MessageType == WebSocketMessageType.Text)
            {
                stream.Write(buffer, 0, result.Count);
                if (stream.Length > 1_048_576)
                {
                    throw new InvalidOperationException("Twitch 訊息過大");
                }
            }
        }
        while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private bool IsNewMessageId(string messageId)
    {
        lock (_messageIdGate)
        {
            if (!_messageIds.Add(messageId))
            {
                return false;
            }
            _messageIdOrder.Enqueue(messageId);
            while (_messageIdOrder.Count > MessageIdCapacity)
            {
                _messageIds.Remove(_messageIdOrder.Dequeue());
            }
            return true;
        }
    }

    private bool TrySwapSocket(ClientWebSocket replacement, long operationVersion)
    {
        lock (_socketGate)
        {
            if (Volatile.Read(ref _operationVersion) != operationVersion || _lifetimeCts is null)
            {
                return false;
            }

            _socket = replacement;
            return true;
        }
    }

    private long StopCore(bool updateStatus)
    {
        var operationVersion = Interlocked.Increment(ref _operationVersion);
        CancellationTokenSource? startupCts;
        CancellationTokenSource? lifetimeCts;
        CancellationTokenSource? authorizationCts;
        ClientWebSocket? socket;
        lock (_socketGate)
        {
            startupCts = _startupCts;
            lifetimeCts = _lifetimeCts;
            authorizationCts = _authorizationCts;
            socket = _socket;
            _startupCts = null;
            _lifetimeCts = null;
            _authorizationCts = null;
            _socket = null;
        }

        TryCancel(startupCts);
        TryCancel(lifetimeCts);
        TryCancel(authorizationCts);
        AbortAndDispose(socket);
        if (updateStatus)
        {
            SetStateIfCurrent(operationVersion, TwitchConnectionState.Disabled, "已中斷 Twitch");
        }

        return operationVersion;
    }

    private void ClearAuthorization(CancellationTokenSource authorizationCts)
    {
        lock (_socketGate)
        {
            if (ReferenceEquals(_authorizationCts, authorizationCts))
            {
                _authorizationCts = null;
            }
        }
    }

    private void SetState(TwitchConnectionState state, string message)
    {
        var shouldDispatch = false;
        lock (_stateGate)
        {
            shouldDispatch = EnqueueStateChange(state, message);
        }

        if (shouldDispatch)
        {
            DispatchStatusNotifications();
        }
    }

    private void SetStateIfCurrent(long operationVersion, TwitchConnectionState state, string message)
    {
        var shouldDispatch = false;
        lock (_stateGate)
        {
            if (Volatile.Read(ref _operationVersion) == operationVersion)
            {
                shouldDispatch = EnqueueStateChange(state, message);
            }
        }

        if (shouldDispatch)
        {
            DispatchStatusNotifications();
        }
    }

    private bool IsCurrentOperation(long operationVersion) =>
        Volatile.Read(ref _operationVersion) == operationVersion;

    private bool EnqueueStateChange(TwitchConnectionState state, string message)
    {
        State = state;
        StatusMessage = message;
        _statusNotifications.Enqueue((StatusChanged, new TwitchStatus(state, message, ChannelName)));
        if (_dispatchingStatus)
        {
            return false;
        }

        _dispatchingStatus = true;
        return true;
    }

    private void DispatchStatusNotifications()
    {
        while (true)
        {
            (Action<TwitchStatus>? Handler, TwitchStatus Status) notification;
            lock (_stateGate)
            {
                if (_statusNotifications.Count == 0)
                {
                    _dispatchingStatus = false;
                    return;
                }

                notification = _statusNotifications.Dequeue();
            }

            try
            {
                notification.Handler?.Invoke(notification.Status);
            }
            catch
            {
                lock (_stateGate)
                {
                    _dispatchingStatus = false;
                }
                throw;
            }
        }
    }

    private string ConnectedMessage() => string.IsNullOrWhiteSpace(_channel) ? "已連線 Twitch" : "已連線 Twitch：" + _channel;
    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Clamp(attempt, 1, 5))));

    private static void TryCancel(CancellationTokenSource? cancellationTokenSource)
    {
        try
        {
            cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void AbortAndDispose(ClientWebSocket? socket)
    {
        if (socket is null)
        {
            return;
        }
        try
        {
            socket.Abort();
            socket.Dispose();
        }
        catch
        {
            // Shutdown and reconnect cleanup are best effort.
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Twitch 回應缺少 {propertyName}")
            : value;
    }

    private static string ReadOAuthError(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            return root.TryGetProperty("message", out var message)
                ? message.GetString() ?? string.Empty
                : root.TryGetProperty("error", out var error) ? error.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static async Task EnsureSuccessWithMessageAsync(HttpResponseMessage response, string prefix, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var detail = ReadOAuthError(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}：{detail}",
            null,
            response.StatusCode);
    }

    private sealed record TokenValidation(string ClientId, string UserId, string Login, int ExpiresIn, IReadOnlyList<string> Scopes);
    private sealed record WebSocketSession(ClientWebSocket Socket, int KeepaliveTimeoutSeconds);
}
