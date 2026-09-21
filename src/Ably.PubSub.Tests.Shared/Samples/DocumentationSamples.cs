using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using IO.Ably.Encryption;
using IO.Ably.Realtime;

namespace IO.Ably.Tests.Samples
{
    // ReSharper disable All

    public static class DocumentationSamples
    {
        public static async Task AuthSamples1()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var tokenParams = new TokenParams { ClientId = "Bob" };
            string tokenRequest = await realtime.Auth.CreateTokenRequestAsync(tokenParams);
            // ... issue the TokenRequest to a client ...
        }

        public static async Task AuthSamples2()
        {
            var client = new AblyRealtime("{{API_KEY}}");
            try
            {
                var tokenParams = new TokenParams { ClientId = "bob" };
                TokenDetails tokenDetails = await client.Auth.AuthorizeAsync(tokenParams);
                Console.WriteLine($"Success; Token = {tokenDetails.Token}");
            }
            catch (AblyException e)
            {
                Console.WriteLine($"An error occurred; Error = {e.Message}");
            }
        }

        public static async Task AuthSample3()
        {
            var client = new AblyRealtime("{{API_KEY}}");
            try
            {
                var tokenParams = new TokenParams { ClientId = "bob" };
                string tokenRequest = await client.Auth.CreateTokenRequestAsync(tokenParams);
                Console.WriteLine("Success; token request issued");
            }
            catch (AblyException e)
            {
                Console.WriteLine($"An error occurred; err = {e.Message}");
            }
        }

        public static async Task AuthSample4()
        {
            var client = new AblyRealtime("{{API_KEY}}");
            try
            {
                var tokenParams = new TokenParams { ClientId = "bob" };
                TokenDetails tokenDetails = await client.Auth.RequestTokenAsync(tokenParams);
                Console.WriteLine($"Success; token = {tokenDetails.Token}");
            }
            catch (AblyException e)
            {
                Console.WriteLine($"An error occurred; err = {e.Message}");
            }
        }

        public static void ChannelSample1()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            channel.Subscribe(message =>
                        Console.WriteLine($"Message: {message.Name}:{message.Data} received"));
            channel.Publish("example", "message data");

            byte[] key = Crypto.GenerateRandomKey();
            var cipherParams = Crypto.GetDefaultParams(key);
            var encryptedChannel = realtime.Channels.Get("channelName", new ChannelOptions(cipherParams));
        }

        public static void ChannelSample2()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("chatroom");
            channel.Attach((success, error) =>
            {
                Console.WriteLine("'chatroom' exists and is now available globally");
            });
        }

        public static void ChannelSample3()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("chatroom");
            channel.Subscribe(message => Console.WriteLine($"Message received:{message.Data}"));
            channel.Publish("action", "boom");
        }

        public static async Task ChannelSample4()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("chatroom");
            channel.On(ChannelEvent.Attached, args => Console.WriteLine($"channel {channel.Name} is now attached"));
            channel.On(args => Console.WriteLine($"channel state is {channel.State}"));

            void ChannelStateListener(ChannelStateChange args) => Console.WriteLine($"channel state is {channel.State}");

            // remove the listener registered for a single event
            channel.Off(ChannelEvent.Attached, ChannelStateListener);

            // remove the listener registered for all events
            channel.Off(ChannelStateListener);

            var privateChannel = realtime.Channels.Get("private:chatroom");
            privateChannel.Attach((_, error) =>
            {
                if (error != null)
                {
                    Console.WriteLine($"Attach failed: {error.Message}");
                }
            });

            channel.Subscribe("myEvent", message =>
            {
                Console.WriteLine($"message received for event {message.Name}");
                Console.WriteLine($"message data: {message.Data}");
            });

            channel.Publish("event", "payload", (success, error) =>
            {
                if (error != null)
                {
                    Console.WriteLine($"Unable to publish message. Reason: {error.Message}");
                }
                else
                {
                    Console.WriteLine("Message published successfully");
                }
            });

            var result = await channel.PublishAsync("event", "payload");
            if (result.IsFailure)
            {
                Console.WriteLine($"Unable to publish message. Reason: {result.Error.Message}");
            }
            else
            {
                Console.WriteLine("Message published successfully");
            }
        }

        public static async Task ChannelHistory()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("chatroom");
            var history = await channel.HistoryAsync();
            Console.WriteLine($"{history.Items.Count} messages received in the first page");
            if (history.HasNext)
            {
                var nextPage = await history.NextAsync();
            }
        }

        public static async Task StatsExample()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var query = new StatsRequestParams { Unit = StatsIntervalGranularity.Hour };
            var results = await realtime.StatsAsync(query);
            Stats thisHour = results.Items[0];
            Console.WriteLine($"Published this hour {thisHour.Inbound.All.All}");
        }

        public static async Task PresenceExample()
        {
            var options = new ClientOptions("{{API_KEY}}") { ClientId = "bob" };
            var realtime = new AblyRealtime(options);
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            channel.Presence.Subscribe(member => Console.WriteLine($"Member {member.ClientId} : {member.Action}"));
            await channel.Presence.EnterAsync(null);

            // Subscribe to presence 'Enter' and 'Update' events
            channel.Presence.Subscribe(member =>
            {
                switch (member.Action)
                {
                    case PresenceAction.Enter:
                    case PresenceAction.Update:
                        {
                            Console.WriteLine(member.Data); // => travelling North
                            break;
                        }
                }
            });

            // Enter this client with data and update once entered
            await channel.Presence.EnterAsync("not moving");
            await channel.Presence.EnterAsync("travelling North");

            IEnumerable<PresenceMessage> presence = await channel.Presence.GetAsync();
            Console.WriteLine($"There are {presence.Count()} members on this channel");
            Console.WriteLine($"The first member has client ID: {presence.First().ClientId}");

            PaginatedResult<PresenceMessage> resultPage = await channel.Presence.HistoryAsync(true);
            Console.WriteLine(resultPage.Items.Count + " presence events received in first page");
            if (resultPage.HasNext)
            {
                PaginatedResult<PresenceMessage> nextPage = await resultPage.NextAsync();
                Console.WriteLine(nextPage.Items.Count + " presence events received in 2nd page");
            }
        }

        public static async Task PresenceExamples2()
        {
            // request a wildcard token
            var rest = new AblyRest("{{API_KEY}}");
            var @params = new TokenParams { ClientId = "*" };
            var options = new ClientOptions
            {
                TokenDetails = await rest.Auth.RequestTokenAsync(@params),
            };

            var realtime = new AblyRealtime(options);
            var channel = realtime.Channels.Get("realtime-chat");

            channel.Presence.Subscribe(member =>
                    {
                        Console.WriteLine($"{member.ClientId} entered realtime-chat");
                    });

            await channel.Presence.EnterClientAsync("Bob", null); // => Bob entered realtime-chat
            await channel.Presence.EnterClientAsync("Mary", null); // => Mary entered realtime-chat
        }

        public static void HistoryExamples()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            channel.Publish("example", "message data", async (success, error) =>
            {
                PaginatedResult<Message> resultPage = await channel.HistoryAsync(null);
                Message lastMessage = resultPage.Items[0];
                Console.WriteLine($"Last message: {lastMessage.Id} - {lastMessage.Data}");
            });
        }

        public static async Task HistoryExample2()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            await channel.AttachAsync();
            PaginatedResult<Message> resultPage = await channel.HistoryAsync();
            Message lastMessage = resultPage.Items[0];
            Console.WriteLine($"Last message before attach: {lastMessage.Data}");

            // Part of the _paginated_result sample
            PaginatedResult<Message> firstPage = await channel.HistoryAsync(null);
            Message firstMessage = firstPage.Items[0];
            Console.WriteLine($"Page 0 item 0: {firstMessage.Data}");
            if (firstPage.HasNext)
            {
                var nextPage = await firstPage.NextAsync();
                Console.WriteLine($"Page 1 item 1:{nextPage.Items[1].Data}");
                Console.WriteLine($"More pages?: {nextPage.HasNext}");
            }
        }

        public static void EncryptionExample()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            var key = Crypto.GenerateRandomKey();
            var options = new ChannelOptions(key);
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}", options);
            channel.Subscribe(message =>
                {
                    Console.WriteLine($"Decrypted data: {message.Data}");
                });
            channel.Publish("unencrypted", "encrypted secret payload");
        }

        public static void EncryptionExample2()
        {
            var @params = Crypto.GetDefaultParams();
            var options = new ChannelOptions(@params);
            var realtime = new AblyRealtime("{{API_KEY}}");
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}", options);
        }

        public static void EncryptionExample3()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            byte[] key = Crypto.GenerateRandomKey(128);
            var options = new ChannelOptions(key);
            var channel = realtime.Channels.Get("{{RANDOM_CHANNEL_NAME}}", options);
        }

        public static void ConnectionExamples()
        {
            var realtime = new AblyRealtime("{{API_KEY}}");
            realtime.Connection.On(ConnectionEvent.Connected, args => Console.WriteLine("Connected, that was easy"));
            void Action(ConnectionStateChange args) => Console.WriteLine($"New state is {args.Current}");
            realtime.Connection.On(Action);
            realtime.Connection.Off(Action);
        }

        public static void RestInit()
        {
            var rest = new AblyRest(new ClientOptions { AuthUrl = new Uri("https://my.website/auth") });
        }

        public static async Task RestWithClientId()
        {
            var rest = new AblyRest(new ClientOptions { Key = "{{API_KEY}}" });
            var tokenParams = new TokenParams { ClientId = "Bob" };
            string tokenRequest = await rest.Auth.CreateTokenRequestAsync(tokenParams);
            // ... issue the TokenRequest to a client ...
        }

        public static void NotifyNetworkChanges()
        {
            Connection.NotifyOperatingSystemNetworkState(NetworkState.Online, DefaultLogger.LoggerInstance);
            Connection.NotifyOperatingSystemNetworkState(NetworkState.Offline, DefaultLogger.LoggerInstance);
        }

        public static async Task RestAuthorizeSample()
        {
            var client = new AblyRest("{{API_KEY}}");
            try
            {
                var tokenParams = new TokenParams { ClientId = "bob" };
                TokenDetails tokenDetails = await client.Auth.AuthorizeAsync(tokenParams);
                Console.WriteLine($"Success; token = {tokenDetails.Token}");
            }
            catch (AblyException e)
            {
                Console.WriteLine($"An error occurred; err = {e.Message}");
            }

            try
            {
                var tokenParams = new TokenParams { ClientId = "bob" };
                var tokenRequest = await client.Auth.CreateTokenRequestAsync(tokenParams);
                Console.WriteLine("Success; token request issued");
            }
            catch (AblyException e)
            {
                Console.WriteLine($"An error occurred; err = {e.Message}");
            }

            try
            {
                var tokenParams = new TokenParams { ClientId = "bob" };
                var tokenDetails = await client.Auth.RequestTokenAsync(tokenParams);
                Console.WriteLine($"Success; token = {tokenDetails.Token}");
            }
            catch (AblyException e)
            {
                Console.WriteLine($"An error occurred; err = {e.Message}");
            }
        }

        public static async Task RestChannelSamples()
        {
            var rest = new AblyRest("{{API_KEY}}");
            var channel = rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            await channel.PublishAsync("example", "message data");
            PaginatedResult<Message> resultPage = await channel.HistoryAsync();
            Console.WriteLine($"Last published message ID: {resultPage.Items[0].Id}");

            byte[] key = null;
            CipherParams cipherParams = Crypto.GetDefaultParams(key);
            var options = new ChannelOptions(cipherParams);
            var encryptedChannel = rest.Channels.Get("channelName", options);
        }

        public static async Task RestChannelHistory()
        {
            var rest = new AblyRest("{{API_KEY}}");
            var channel = rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}");

            PaginatedResult<Message> resultPage = await channel.HistoryAsync();
            Console.WriteLine($"{resultPage.Items.Count} messages received in first page");
            if (resultPage.HasNext)
            {
                PaginatedResult<Message> nextPage = await resultPage.NextAsync();
                Console.WriteLine($"{nextPage.Items.Count} messages received in second page");
            }
        }

        public static async Task RestEncryption()
        {
            var rest = new AblyRest("{{API_KEY}}");
            var key = Crypto.GenerateRandomKey();
            var options = new ChannelOptions(key);
            var channel = rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}", options);
            await channel.PublishAsync("unencrypted", "encrypted secret payload");

            CipherParams cipherParams = Crypto.GetDefaultParams(key);
            rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}", new ChannelOptions(cipherParams));
        }

        public static void RestGenerateRandomKey()
        {
            var rest = new AblyRest("{{API_KEY}}");
            byte[] key = Crypto.GenerateRandomKey(128);
            var options = new ChannelOptions(key);
            var channel = rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}", options);
        }

        public static async Task RestHistorySamples()
        {
            var rest = new AblyRest("{{API_KEY}}");
            var channel = rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            await channel.PublishAsync("example", "message data");
            PaginatedResult<Message> resultPage = await channel.HistoryAsync();
            Message recentMessage = resultPage.Items[0];
            Console.WriteLine($"Most recent message: {recentMessage.Id} - {recentMessage.Data}");
        }

        public static async Task RestPresenceSamples()
        {
            var rest = new AblyRest("{{API_KEY}}");
            var channel = rest.Channels.Get("{{RANDOM_CHANNEL_NAME}}");
            PaginatedResult<PresenceMessage> membersPage = await channel.Presence.GetAsync();
            Console.WriteLine($"{membersPage.Items.Count} members in first page");
            if (membersPage.HasNext)
            {
                PaginatedResult<PresenceMessage> nextPage = await membersPage.NextAsync();
                Console.WriteLine($"{nextPage.Items.Count} members on 2nd page");
            }

            // History
            PaginatedResult<PresenceMessage> eventsPage = await channel.Presence.HistoryAsync();
            Console.WriteLine($"{eventsPage.Items.Count} presence events received in first page");
            if (eventsPage.HasNext)
            {
                PaginatedResult<PresenceMessage> nextPage = await eventsPage.NextAsync();
                Console.WriteLine($"{nextPage.Items.Count} presence events received in 2nd page");
            }
        }

        public static async Task RestStatsSamples()
        {
            var rest = new AblyRest("{{API_KEY}}");
            PaginatedResult<Stats> results = await rest.StatsAsync(new StatsRequestParams { Unit = StatsIntervalGranularity.Hour });
            Stats thisHour = results.Items[0];
            Console.WriteLine($"Published this hour {thisHour.Inbound.All.All.Count}");
        }
    }

    // ReSharper restore All
}
