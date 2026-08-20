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

/// <summary>Twitch Device Code OAuth and EventSub WebSocket client for channel.cheer.</summary>
public sealed class TwitchEventSubClient : IAsyncDisposable
{
    private const string DefaultWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
    private const int MessageIdCapacity = 512;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly TwitchTokenStore _store;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _socketGate = new();
    private readonly object _messageIdGate = new();
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _messageIdOrder = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _authorizationCts;
    private Task? _receiveTask;
    private string _channel = string.Empty;
    private bool _disposed;

    public TwitchConnectionState State { get; private set; } = TwitchConnectionState.Disabled;
    public string StatusMessage { get; private set; } = "未啟用";
    public string ChannelName => _channel;

    public event Action<TwitchStatus>? StatusChanged;
    public event Action<TwitchCheer>? CheerReceived;
    public event Action<string, string>? DeviceCodeReady;

    public TwitchEventSubClient(TwitchTokenStore store, HttpClient? http = null)
    {
        _store = store;
        _http = http ?? new HttpClient();
        _ownsHttpClient = http is null;
    }

    public async Task StartAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        clientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Stop();
            SetState(TwitchConnectionState.AuthorizationRequired, "請先設定 Twitch Client ID");
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopCore(updateStatus: false);
            var storedToken = _store.Load();
            if (storedToken is null || !string.Equals(storedToken.ClientId, clientId, StringComparison.Ordinal))
            {
                SetState(TwitchConnectionState.AuthorizationRequired, "需要 Twitch 授權");
                return;
            }

            TwitchTokenStore.Token token;
            try
            {
                token = await EnsureValidTokenAsync(storedToken, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
            {
                SetState(TwitchConnectionState.AuthorizationRequired, "Twitch 授權無效，請重新授權");
                return;
            }

            var lifetimeCts = new CancellationTokenSource();
            var session = await OpenSessionAsync(token, DefaultWebSocketUrl, true, cancellationToken).ConfigureAwait(false);
            lock (_socketGate)
            {
                _lifetimeCts = lifetimeCts;
                _socket = session.Socket;
            }

            _channel = token.ChannelName;
            SetState(TwitchConnectionState.Connected, ConnectedMessage());
            _receiveTask = Task.Run(() => ReceiveLoopAsync(token, session, lifetimeCts.Token), CancellationToken.None);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task AuthorizeAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        clientId = clientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("請先設定 Twitch Client ID");
        }

        Stop();
        using var authorizationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_socketGate)
        {
            _authorizationCts = authorizationCts;
        }
        cancellationToken = authorizationCts.Token;

        try
        {
            SetState(TwitchConnectionState.Authorizing, "正在取得 Twitch 授權…");
        using var deviceRequest = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/device")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = "bits:read"
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
        DeviceCodeReady?.Invoke(verificationUri, userCode);

        while (DateTime.UtcNow < expiresUtc)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);
            using var pollResponse = await _http.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scopes"] = "bits:read",
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
                ValidateAuthorization(validation, clientId);
                _store.Save(new TwitchTokenStore.Token(
                    accessToken,
                    refreshToken,
                    clientId,
                    DateTime.UtcNow.AddSeconds(validation.ExpiresIn),
                    validation.UserId,
                    validation.Login));
                ClearAuthorization(authorizationCts);
                await StartAsync(clientId, cancellationToken).ConfigureAwait(false);
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
        var receiveTask = _receiveTask;
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

        _lifecycleGate.Dispose();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(TwitchTokenStore.Token initialToken, WebSocketSession initialSession, CancellationToken cancellationToken)
    {
        var token = initialToken;
        var session = initialSession;
        var retryAttempt = 0;

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
                if (!TwitchEventSubProtocol.TryParseReconnect(json, out var reconnect))
                {
                    continue;
                }

                SetState(TwitchConnectionState.Reconnecting, "Twitch 正在無縫換線…");
                var replacement = await OpenSessionAsync(token, reconnect.ReconnectUrl, false, cancellationToken).ConfigureAwait(false);
                SwapSocket(replacement.Socket);
                AbortAndDispose(session.Socket);
                session = replacement;
                SetState(TwitchConnectionState.Connected, ConnectedMessage());
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
                SetState(TwitchConnectionState.Reconnecting, "Twitch 連線中斷，正在重試…");
                retryAttempt++;
                try
                {
                    await Task.Delay(RetryDelay(retryAttempt), cancellationToken).ConfigureAwait(false);
                    token = await EnsureValidTokenAsync(token, cancellationToken).ConfigureAwait(false);
                    session = await OpenSessionAsync(token, DefaultWebSocketUrl, true, cancellationToken).ConfigureAwait(false);
                    SwapSocket(session.Socket);
                    SetState(TwitchConnectionState.Connected, ConnectedMessage());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception reconnectException) when (reconnectException is WebSocketException or TimeoutException or IOException or HttpRequestException or InvalidOperationException)
                {
                    SetState(TwitchConnectionState.Reconnecting, "Twitch 重連失敗，稍後再試…");
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetState(TwitchConnectionState.Error, "Twitch 連線錯誤：" + ex.Message);
                }
                return;
            }
        }
    }

    private async Task<WebSocketSession> OpenSessionAsync(TwitchTokenStore.Token token, string url, bool createSubscription, CancellationToken cancellationToken)
    {
        SetState(
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
                await SubscribeAsync(token, welcome.SessionId, welcomeCts.Token).ConfigureAwait(false);
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

    private async Task SubscribeAsync(TwitchTokenStore.Token token, string sessionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Client-Id", token.ClientId);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                type = "channel.cheer",
                version = "1",
                condition = new { broadcaster_user_id = token.BroadcasterUserId },
                transport = new { method = "websocket", session_id = sessionId }
            }),
            Encoding.UTF8,
            "application/json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessWithMessageAsync(response, "無法建立 Twitch Cheer 訂閱", cancellationToken).ConfigureAwait(false);
    }

    private async Task<TwitchTokenStore.Token> EnsureValidTokenAsync(TwitchTokenStore.Token token, CancellationToken cancellationToken)
    {
        if (token.ExpiresUtc <= DateTime.UtcNow.AddMinutes(5))
        {
            token = await RefreshAsync(token, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var validation = await ValidateAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
            ValidateAuthorization(validation, token.ClientId);
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
            token = await RefreshAsync(token, cancellationToken).ConfigureAwait(false);
            _store.Save(token);
            _channel = token.ChannelName;
            return token;
        }
    }

    private async Task<TwitchTokenStore.Token> RefreshAsync(TwitchTokenStore.Token token, CancellationToken cancellationToken)
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
        ValidateAuthorization(validation, token.ClientId);
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

    private static void ValidateAuthorization(TokenValidation validation, string expectedClientId)
    {
        if (!string.Equals(validation.ClientId, expectedClientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Twitch token 與 Client ID 不相符");
        }
        if (!validation.Scopes.Contains("bits:read", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Twitch token 缺少 bits:read 權限");
        }
    }

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

    private void SwapSocket(ClientWebSocket replacement)
    {
        lock (_socketGate)
        {
            _socket = replacement;
        }
    }

    private void StopCore(bool updateStatus)
    {
        CancellationTokenSource? lifetimeCts;
        CancellationTokenSource? authorizationCts;
        ClientWebSocket? socket;
        lock (_socketGate)
        {
            lifetimeCts = _lifetimeCts;
            authorizationCts = _authorizationCts;
            socket = _socket;
            _lifetimeCts = null;
            _authorizationCts = null;
            _socket = null;
        }
        try
        {
            lifetimeCts?.Cancel();
            authorizationCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        AbortAndDispose(socket);
        lifetimeCts?.Dispose();
        if (updateStatus)
        {
            SetState(TwitchConnectionState.Disabled, "已中斷 Twitch");
        }
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
        State = state;
        StatusMessage = message;
        StatusChanged?.Invoke(new TwitchStatus(state, message, ChannelName));
    }

    private string ConnectedMessage() => string.IsNullOrWhiteSpace(_channel) ? "已連線 Twitch" : "已連線 Twitch：" + _channel;
    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Clamp(attempt, 1, 5))));

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
