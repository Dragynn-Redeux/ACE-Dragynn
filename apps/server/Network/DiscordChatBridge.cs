using System;
using System.Text;
using System.Threading.Tasks;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace ACE.Server.Network;

public static class DiscordChatBridge
{
    private static readonly ILogger _log = Log.ForContext(typeof(DiscordChatBridge));
    private static IConfiguration _configuration;

    private static DiscordSocketClient DiscordClient = null;
    public static bool IsRunning { get; private set; }

    public static void Initialize(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public static async Task Start()
    {
        _log.Information("DiscordChatBridge.Start invoked. IsRunning: {IsRunning}", IsRunning);

        if (IsRunning)
        {
            _log.Information("DiscordChatBridge already running; Start() returning early.");
            return;
        }

        if (_configuration == null)
        {
            _log.Warning("DiscordChatBridge.Start called before Initialize. Skipping start.");
            return;
        }

        var loginToken = _configuration.GetSection("Discord").GetValue<string>("Token");
        var channelId = _configuration.GetSection("Discord").GetSection("CommunityChannels").GetSection("cg").GetValue<ulong>("ChannelId");

        var tokenPresent = !string.IsNullOrEmpty(loginToken);
        _log.Information("DiscordChatBridge configuration: LoginToken present: {TokenPresent}, ChannelId: {ChannelId}", tokenPresent, channelId);

        if (!tokenPresent || channelId == 0)
        {
            _log.Warning("DiscordChatBridge not started due to missing LoginToken or ChannelId. TokenPresent: {TokenPresent}, ChannelId: {ChannelId}", tokenPresent, channelId);
            return;
        }

        try
        {
            var config = new DiscordSocketConfig();
            config.GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent;
            config.GatewayIntents ^= GatewayIntents.GuildScheduledEvents | GatewayIntents.GuildInvites;

            DiscordClient = new DiscordSocketClient(config);

            DiscordClient.Log += DiscordLogMessageReceived;
            DiscordClient.MessageReceived += DiscordMessageReceived;

            _log.Information("Attempting Discord client login.");
            await DiscordClient.LoginAsync(TokenType.Bot, loginToken).ConfigureAwait(false);

            _log.Information("Starting Discord client connection.");
            await DiscordClient.StartAsync().ConfigureAwait(false);

            IsRunning = true;
            _log.Information("DiscordChatBridge started successfully. ChannelId: {ChannelId}", channelId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "DiscordChatBridge failed to start. Exception during Login/Start.");
            try
            {
                // best-effort cleanup
                if (DiscordClient != null)
                {
                    DiscordClient.Log -= DiscordLogMessageReceived;
                    DiscordClient.MessageReceived -= DiscordMessageReceived;
                }
            }
            catch (Exception cleanupEx)
            {
                _log.Warning(cleanupEx, "Error during DiscordChatBridge cleanup after failed start.");
            }
            DiscordClient = null;
            IsRunning = false;
        }
    }

    public static async void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        await DiscordClient.LogoutAsync();
        await DiscordClient.StopAsync();

        IsRunning = false;
    }

    private static Task DiscordMessageReceived(SocketMessage messageParam)
    {
        try
        {
            // Don't process the command if it was a system message
            var message = messageParam as SocketUserMessage;
            if (message == null)
            {
                return Task.CompletedTask;
            }

            var channelId = _configuration
            .GetSection("Discord")
            .GetSection("CommunityChannels")
            .GetSection("cg")
            .GetValue<ulong>("ChannelId");

            if (message.Author.IsBot || message.Channel.Id != channelId)
            {
                return Task.CompletedTask;
            }

            if (message.Author is SocketGuildUser author)
            {
                var authorName = author.DisplayName;
                authorName = authorName.Normalize(NormalizationForm.FormKC);

                var validLetters = "";
                foreach (var letter in authorName)
                {
                    if ((letter >= 32 && letter <= 126) || (letter >= 160 && letter <= 383)) //Basic Latin + Latin-1 Supplement + Latin Extended-A
                    {
                        validLetters += letter;
                    }
                }
                authorName = validLetters;

                authorName = authorName.Trim();
                authorName = authorName.TrimStart('+');
                authorName = authorName.Trim();
                authorName = authorName.TrimStart('+');

                var messageText = message.CleanContent;

                if (messageText.Length > 256)
                {
                    messageText = messageText.Substring(0, 250) + "[...]";
                }

                if (!string.IsNullOrWhiteSpace(authorName) && !string.IsNullOrWhiteSpace(messageText))
                {
                    authorName = $"[Discord] {authorName}";
                    foreach (var recipient in PlayerManager.GetAllOnline())
                    {
                        if (!recipient.GetCharacterOption(CharacterOption.ListenToGeneralChat))
                        {
                            continue;
                        }

                        if (recipient.IsOlthoiPlayer)
                        {
                            continue;
                        }

                        var gameMessageTurbineChat = new GameMessageTurbineChat(ChatNetworkBlobType.NETBLOB_EVENT_BINARY, ChatNetworkBlobDispatchType.ASYNCMETHOD_SENDTOROOMBYNAME, TurbineChatChannel.General, authorName, messageText, 0, ChatType.General);
                        recipient.Session.Network.EnqueueSend(gameMessageTurbineChat);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[DISCORD] Error handling Discord message. Ex: {ex}");
        }

        return Task.CompletedTask;
    }
    private static Task DiscordLogMessageReceived(LogMessage msg)
    {
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                _log.Error($"[DISCORD] ({msg.Severity}) {msg.Exception} {msg.Message}");
                break;
            case LogSeverity.Warning:
                _log.Warning($"[DISCORD] ({msg.Severity}) {msg.Exception} {msg.Message}");
                break;
            case LogSeverity.Info:
                _log.Information($"[DISCORD] {msg.Message}");
                break;
        }

        return Task.CompletedTask;
    }
}
