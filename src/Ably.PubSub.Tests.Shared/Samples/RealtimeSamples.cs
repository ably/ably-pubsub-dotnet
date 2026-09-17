using System.Linq;
using System.Threading.Tasks;

using IO.Ably.Encryption;
using IO.Ably.Realtime;
using IO.Ably.Rest;
using IO.Ably.Transport;

using Xunit;

namespace IO.Ably.Tests.GithubSamples
{
    // ReSharper disable all

    public class RealtimeSamples
    {
        private const string PlaceholderKey = "key.placeholder:placeholder";

        [Fact]
        public void InitializeClient()
        {
            // If you do not have an API key, [sign up for a free API key now](https://www.ably.com/sign-up)
            var realtimeBasic = new AblyRealtime(PlaceholderKey);
            var realtimeToken = new AblyRealtime(new ClientOptions { Token = "token" });
        }

        [Fact]
        public void SuccessfulConnection()
        {
            var realtime = new AblyRealtime(PlaceholderKey);

            realtime.Connection.On(ConnectionEvent.Connected, args =>
            {
                // Do stuff
            });

            // Or ...
            realtime.Connection.ConnectionStateChanged += (s, args) =>
            {
                if (args.Current == ConnectionState.Connected)
                {
                    // Do stuff
                }
            };
        }

        [Fact]
        public void AutoConnectOff()
        {
            var realtime = new AblyRealtime(new ClientOptions(PlaceholderKey) { AutoConnect = false });
            realtime.Connect();

            realtime.Connection.On(args =>
            {
                var currentState = args.Current;      // Current state the connection transitioned to
                var previousState = args.Previous;    // Previous state
                var error = args.Reason;              // If the connection error-ed the 'Reason' object will be populated.
            });
        }

        [Fact(Skip = "Used to make sure samples compile")]
        public async Task ChannelSubscribe()
        {
            var realtime = new AblyRealtime(new ClientOptions(PlaceholderKey) { AutoConnect = false });
            IRealtimeChannel channel = realtime.Channels.Get("test");

            // Or ...
            IRealtimeChannel channel2 = realtime.Channels.Get("shortcut");

            channel.Subscribe(message =>
            {
                var name = message.Name;
                var data = message.Data;
            });

            channel.On(args =>
            {
                var state = args.Current;    // Current channel State
                var error = args.Error;      // If the channel error-ed it will be reflected here
            });

            channel.On(ChannelEvent.Attached, args =>
            {
                // Do stuff when channel is attached
            });

            channel.Publish("greeting", "Hello World!");
            channel.Publish("greeting", "Hello World!", (success, error) =>
            {
                // If 'Publish' succeeded 'success' is 'true'
                // If 'Publish' failed 'success' is 'false' and 'error' will contain the specific error
            });

            var result = await channel.PublishAsync("greeting", "Hello World!");

            // You can check if the message failed
            if (result.IsFailure)
            {
                var error = result.Error;    // The error reason can be accessed as well
            }

            var secret = Crypto.GenerateRandomKey();
            var encryptedChannel = realtime.Channels.Get("encrypted", new ChannelOptions(secret));
            encryptedChannel.Subscribe(message =>
            {
                var data = message.Data;    // Sensitive data (encrypted before published)
            });

            encryptedChannel.Publish("name (not encrypted)", "sensitive data (encrypted before published)");
        }

        [Fact(Skip = "Just need to make sure it compiles")]
        public async Task ChannelHistory()
        {
            var realtime = new AblyRealtime(PlaceholderKey);
            IRealtimeChannel channel = realtime.Channels.Get("test");
            var history = await channel.HistoryAsync();

            // Loop through current history page
            foreach (var message in history.Items)
            {
                // Do something with message
            }

            var nextPage = await history.NextAsync();

            var presenceHistory = await channel.Presence.HistoryAsync();

            // Loop through the presence messages
            foreach (var presence in presenceHistory.Items)
            {
                // Do something with the messages
            }

            var presenceNextPage = await presenceHistory.NextAsync();
        }

        [Fact(Skip = "Making sure the samples compile")]
        public async Task RestApiSamples()
        {
            var client = new AblyRest(PlaceholderKey);
            IRestChannel channel = client.Channels.Get("test");

            try
            {
                await channel.PublishAsync("name", "data");
            }
            catch (AblyException)
            {
                // Log error
            }

            // History
            var historyPage = await channel.HistoryAsync();
            foreach (var message in historyPage.Items)
            {
                // Do something with each message
            }

            // Get next page
            var nextHistoryPage = await historyPage.NextAsync();

            // Current presence
            var presence = await channel.Presence.GetAsync();
            var first = presence.Items.FirstOrDefault();
            var clientId = first.ClientId;    // 'clientId' of the first member present
            var nextPresencePage = await presence.NextAsync();
            foreach (var presenceMessage in nextPresencePage.Items)
            {
                // Do stuff with next page presence messages
            }

            // Presence history
            var presenceHistory = await channel.Presence.HistoryAsync();
            foreach (var presenceMessage in presenceHistory.Items)
            {
                // Do stuff with presence messages
            }

            var nextPage = await presenceHistory.NextAsync();
            foreach (var presenceMessage in nextPage.Items)
            {
                // Do stuff with next page messages
            }

            // Publishing encrypted messages
            var secret = Crypto.GenerateRandomKey();
            IRestChannel encryptedChannel = client.Channels.Get("encryptedChannel", new ChannelOptions(secret));
            await encryptedChannel.PublishAsync("name", "sensitive data"); // Data will be encrypted before publish
            var history = await encryptedChannel.HistoryAsync();
            var data = history.Items.First().Data;

            // "sensitive data" the message will be automatically decrypted once received

            // Generate a token
            var token = await client.Auth.RequestTokenAsync();
            var tokenString = token.Token; // "xVLyHw.CLchevH3hF....MDh9ZC_Q"
            var tokenClient = new AblyRest(new ClientOptions { TokenDetails = token });

            var tokenRequest = await client.Auth.CreateTokenRequestAsync();

            // Stats
            var stats = await client.StatsAsync();
            var firstItem = stats.Items.First();
            var nextStatsPage = await stats.NextAsync();

            var time = await client.TimeAsync();
        }

        [Fact(Skip = "Making sure the samples compile")]
        public async Task TransportSamples()
        {
            const int maxBufferSize = 64 * 1024;
            var options = new ClientOptions();
            var websocketOptions = new MsWebSocketOptions { SendBufferInBytes = maxBufferSize, ReceiveBufferInBytes = maxBufferSize };
            options.TransportFactory = new MsWebSocketTransport.TransportFactory(websocketOptions);
            var realtime = new AblyRealtime(options);
        }
    }

    // ReSharper restore All
}
