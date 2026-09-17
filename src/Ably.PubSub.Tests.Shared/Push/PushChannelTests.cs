using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Push;
using IO.Ably.Tests.Infrastructure;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Push
{
    public static class PushChannelTests
    {
        [Trait("spec", "RSH7")]
        public class WhenPlatformSupportsPushNotifications : AblyRealtimeSpecs
        {
            [Fact]
            public void RealtimeChannel_ShouldInitialisePushChannel()
            {
                var channel = GetRealtimeClient().Channels.Get("test");
                channel.Push.Should().NotBeNull();
                channel.Push.ChannelName.Should().Be(channel.Name);
            }

            [Fact]
            public void RestChannel_ShouldInitialisePushChannel()
            {
                var restClient = GetRealtimeClient().RestClient;
                var channel = restClient.Channels.Get("test");
                channel.Push.Should().NotBeNull();
                channel.Push.ChannelName.Should().Be(channel.Name);
            }

            [Fact]
            public void RestChannel_WhenRestClientInitialisedDirectly_ShouldInitialisePushChannel()
            {
                var channel = GetRestClient().Channels.Get("test");
                channel.Push.Should().NotBeNull();
                channel.Push.ChannelName.Should().Be(channel.Name);
            }

            private AblyRealtime GetRealtimeClient()
            {
                return new AblyRealtime(new ClientOptions(ValidKey) { AutoConnect = false }, mobileDevice: new FakeMobileDevice());
            }

            private AblyRest GetRestClient()
            {
                return new AblyRest(new ClientOptions(ValidKey), mobileDevice: new FakeMobileDevice());
            }

            public WhenPlatformSupportsPushNotifications(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        [Trait("spec", "RSH7")]
        public class WhenPlatformDoesNotSupportPushNotifications : AblyRealtimeSpecs
        {
            [Fact]
            public void RealtimeChannel_ShouldReturnNull()
            {
                var channel = GetRealtimeClient().Channels.Get("test");
                channel.Push.Should().BeNull();
            }

            private AblyRealtime GetRealtimeClient()
            {
                return new AblyRealtime(new ClientOptions(ValidKey) { AutoConnect = false });
            }

            public WhenPlatformDoesNotSupportPushNotifications(ITestOutputHelper output)
                : base(output)
            {
            }
        }

        public class GeneralTests : MockHttpRestSpecs
        {
            [Fact]
            [Trait("spec", "RSH7a")]
            [Trait("spec", "RSH7a1")]
            public void SubscribeDevice_ShouldThrowWhenLocalDeviceIsMissingDeviceIdentityToken()
            {
                var client = GetRestClient(mobileDevice: new FakeMobileDevice());
                client.Device = new LocalDevice();
                var pushChannel = client.Channels.Get("test").Push;

                Func<Task> subscribeFunc = () => pushChannel.SubscribeDevice();

                subscribeFunc.Should().ThrowAsync<AblyException>().WithMessage("Cannot Subscribe device to channel*");
            }

            [Fact]
            [Trait("spec", "RSH7a")]
            [Trait("spec", "RSH7a2")]
            public async Task SubscribeDevice_ShouldSendARequestTo_PushChannelSubscriptions_WithCorrectParameters()
            {
                const string channelName = "testChannel";
                const string deviceId = "deviceId";

                var taskAwaiter = new TaskCompletionAwaiter();

                async Task<AblyResponse> RequestHandler(AblyRequest request)
                {
                    request.Url.Should().Be("/push/channelSubscriptions");
                    var postData = (PushChannelSubscription)request.PostData;
                    postData.Should().NotBeNull();
                    postData.Channel.Should().Be(channelName);
                    postData.DeviceId.Should().Be(deviceId);
                    postData.ClientId.Should().BeNullOrEmpty();

                    taskAwaiter.SetCompleted();
                    return new AblyResponse() { TextResponse = JsonConvert.SerializeObject(new PushChannelSubscription()) };
                }

                var client = GetRestClient(RequestHandler, mobileDevice: new FakeMobileDevice());

                client.Device = new LocalDevice()
                {
                    Id = deviceId,
                    DeviceIdentityToken = "identityToken"
                };
                var pushChannel = client.Channels.Get(channelName).Push;

                await pushChannel.SubscribeDevice();

                (await taskAwaiter).Should().BeTrue("Didn't validate function");
            }

            [Fact]
            [Trait("spec", "RSH7a")]
            [Trait("spec", "RSH7a3")]
            public async Task SubscribeDevice_ShouldUsePushDeviceAuthentication()
            {
                const string deviceIdentityToken = "identityToken";
                var taskAwaiter = new TaskCompletionAwaiter();

                async Task<AblyResponse> RequestHandler(AblyRequest request)
                {
                    request.Headers.Should().ContainKey(Defaults.DeviceIdentityTokenHeader).WhoseValue.Should()
                        .Be(deviceIdentityToken);

                    taskAwaiter.SetCompleted();
                    return new AblyResponse()
                        { TextResponse = JsonConvert.SerializeObject(new PushChannelSubscription()) };
                }

                var client = GetRestClient(RequestHandler, mobileDevice: new FakeMobileDevice());

                client.Device = new LocalDevice()
                {
                    Id = "id",
                    DeviceIdentityToken = deviceIdentityToken
                };

                var pushChannel = client.Channels.Get("test").Push;

                await pushChannel.SubscribeDevice();

                (await taskAwaiter).Should().BeTrue("Didn't validate function");
            }

            [Fact]
            [Trait("spec", "RSH7b")]
            [Trait("spec", "RSH7b1")]
            public void SubscribeClient_ShouldThrowWhenLocalDeviceIsMissingDeviceIdentityToken()
            {
                var client = GetRestClient(mobileDevice: new FakeMobileDevice());
                client.Device = new LocalDevice();
                var pushChannel = client.Channels.Get("test").Push;

                Func<Task> subscribeFunc = () => pushChannel.SubscribeClient();

                subscribeFunc.Should().ThrowAsync<AblyException>().WithMessage("Cannot Subscribe device to channel*");
            }

            [Fact]
            [Trait("spec", "RSH7b")]
            [Trait("spec", "RSH7b1")]
            public void SubscribeClient_ShouldThrowWhenLocalDeviceIsMissingClientId()
            {
                var client = GetRestClient(mobileDevice: new FakeMobileDevice());
                client.Device = new LocalDevice() { DeviceIdentityToken = "token" };
                var pushChannel = client.Channels.Get("test").Push;

                Func<Task> subscribeFunc = () => pushChannel.SubscribeClient();

                subscribeFunc.Should().ThrowAsync<AblyException>()
                    .WithMessage("Cannot Subscribe clientId to channel*");
            }

            [Fact]
            [Trait("spec", "RSH7b")]
            [Trait("spec", "RSH7b2")]
            public async Task SubscribeChannel_ShouldSendARequestTo_PushChannelSubscriptions_WithCorrectParameters()
            {
                const string channelName = "testChannel";
                const string clientId = "client101";

                var taskAwaiter = new TaskCompletionAwaiter();

                async Task<AblyResponse> RequestHandler(AblyRequest request)
                {
                    request.Url.Should().Be("/push/channelSubscriptions");
                    var postData = (PushChannelSubscription)request.PostData;
                    postData.Should().NotBeNull();
                    postData.Channel.Should().Be(channelName);
                    postData.ClientId.Should().Be(clientId);
                    postData.DeviceId.Should().BeNullOrEmpty();

                    taskAwaiter.SetCompleted();
                    return new AblyResponse() { TextResponse = JsonConvert.SerializeObject(new PushChannelSubscription()) };
                }

                var client = GetRestClient(RequestHandler, mobileDevice: new FakeMobileDevice());

                client.Device = new LocalDevice()
                {
                    ClientId = clientId,
                    DeviceIdentityToken = "identityToken"
                };
                var pushChannel = client.Channels.Get(channelName).Push;

                await pushChannel.SubscribeClient();

                (await taskAwaiter).Should().BeTrue("Didn't validate function");
            }

            [Fact]
            [Trait("spec", "RSH7c")]
            [Trait("spec", "RSH7c1")]
            public void UnsubscribeDevice_ShouldThrowWhenLocalDeviceIsMissingDeviceIdentityToken()
            {
                var client = GetRestClient(mobileDevice: new FakeMobileDevice());
                client.Device = new LocalDevice();
                var pushChannel = client.Channels.Get("test").Push;

                Func<Task> subscribeFunc = () => pushChannel.UnsubscribeDevice();

                subscribeFunc.Should().ThrowAsync<AblyException>().WithMessage("Cannot Subscribe device to channel*");
            }

            [Fact]
            [Trait("spec", "RSH7c")]
            [Trait("spec", "RSH7c2")]
            [Trait("spec", "RSH7c3")]
            public async Task UnsubscribeDevice_ShouldSendADeleteRequestTo_PushChannelSubscriptions_WithCorrectParameters_And_AuthHeader()
            {
                const string channelName = "testChannel";
                const string deviceId = "deviceId";
                const string deviceIdentityToken = "token";

                var taskAwaiter = new TaskCompletionAwaiter();

                async Task<AblyResponse> RequestHandler(AblyRequest request)
                {
                    // RSH7c2, check the correct request is made
                    request.Url.Should().Be("/push/channelSubscriptions");
                    request.Method.Should().Be(HttpMethod.Delete);
                    var queryParams = request.QueryParameters;
                    queryParams.Should().ContainKey("deviceId").WhoseValue.Should().Be(deviceId);
                    queryParams.Should().ContainKey("channel").WhoseValue.Should().Be(channelName);
                    queryParams.Should().NotContainKey("clientId");

                    // Check the auth header RSH7c3
                    request.Headers.Should().ContainKey(Defaults.DeviceIdentityTokenHeader).WhoseValue.Should()
                        .Be(deviceIdentityToken);

                    taskAwaiter.SetCompleted();
                    return new AblyResponse() { TextResponse = JsonConvert.SerializeObject(new PushChannelSubscription()) };
                }

                var client = GetRestClient(RequestHandler, mobileDevice: new FakeMobileDevice());

                client.Device = new LocalDevice()
                {
                    Id = deviceId,
                    DeviceIdentityToken = deviceIdentityToken
                };
                var pushChannel = client.Channels.Get(channelName).Push;

                await pushChannel.UnsubscribeDevice();

                (await taskAwaiter).Should().BeTrue("Didn't validate function");
            }

            [Fact]
            [Trait("spec", "RSH7d")]
            [Trait("spec", "RSH7d1")]
            public void UnsubscribeClient_ShouldThrowWhenLocalDeviceIsMissingDeviceIdentityToken()
            {
                var client = GetRestClient(mobileDevice: new FakeMobileDevice());
                client.Device = new LocalDevice();
                var pushChannel = client.Channels.Get("test").Push;

                Func<Task> subscribeFunc = () => pushChannel.UnsubscribeClient();

                subscribeFunc.Should().ThrowAsync<AblyException>().WithMessage("Cannot Unsubscribe client from channel*");
            }

            [Fact]
            [Trait("spec", "RSH7d")]
            [Trait("spec", "RSH7d1")]
            public void UnsubscribeClient_ShouldThrowWhenLocalDeviceIsMissingClientId()
            {
                var client = GetRestClient(mobileDevice: new FakeMobileDevice());
                client.Device = new LocalDevice() { DeviceIdentityToken = "token" };
                var pushChannel = client.Channels.Get("test").Push;

                Func<Task> subscribeFunc = () => pushChannel.UnsubscribeClient();

                subscribeFunc.Should().ThrowAsync<AblyException>().WithMessage("Cannot Unsubscribe client from channel*");
            }

            [Fact]
            [Trait("spec", "RSH7d")]
            [Trait("spec", "RSH7d2")]
            public async Task UnsubscribeClient_ShouldSendADeleteRequestTo_PushChannelSubscriptions_WithCorrectParameters()
            {
                const string channelName = "testChannel";
                const string clientId = "clientId";
                const string deviceIdentityToken = "token";

                var taskAwaiter = new TaskCompletionAwaiter();

                async Task<AblyResponse> RequestHandler(AblyRequest request)
                {
                    request.Url.Should().Be("/push/channelSubscriptions");
                    request.Method.Should().Be(HttpMethod.Delete);
                    var queryParams = request.QueryParameters;
                    queryParams.Should().ContainKey("clientId").WhoseValue.Should().Be(clientId);
                    queryParams.Should().ContainKey("channel").WhoseValue.Should().Be(channelName);
                    queryParams.Should().NotContainKey("deviceId");

                    taskAwaiter.SetCompleted();
                    return new AblyResponse() { TextResponse = JsonConvert.SerializeObject(new PushChannelSubscription()) };
                }

                var client = GetRestClient(RequestHandler, mobileDevice: new FakeMobileDevice());

                client.Device = new LocalDevice()
                {
                    DeviceIdentityToken = deviceIdentityToken,
                    ClientId = clientId
                };

                var pushChannel = client.Channels.Get(channelName).Push;

                await pushChannel.UnsubscribeClient();

                (await taskAwaiter).Should().BeTrue("Didn't validate function");
            }

            [Fact]
            [Trait("spec", "RSH7e")]
            public async Task ListSubscriptions_ShouldCallApiWithCorrectParameters()
            {
                const string channelName = "testChannel";
                const string clientId = "clientId";
                const string deviceid = "deviceId";
                const string deviceIdentityToken = "token";

                var taskAwaiter = new TaskCompletionAwaiter();

                async Task<AblyResponse> RequestHandler(AblyRequest request)
                {
                    request.Url.Should().Be("/push/channelSubscriptions");
                    request.Method.Should().Be(HttpMethod.Get);
                    var queryParams = request.QueryParameters;
                    queryParams.Should().ContainKey("clientId").WhoseValue.Should().Be(clientId);
                    queryParams.Should().ContainKey("channel").WhoseValue.Should().Be(channelName);
                    queryParams.Should().ContainKey("deviceId").WhoseValue.Should().Be(deviceid);
                    queryParams.Should().ContainKey("concatFilters").WhoseValue.Should().Be("true");

                    taskAwaiter.SetCompleted();
                    return new AblyResponse { TextResponse = JsonConvert.SerializeObject(new List<PushChannelSubscription>()) };
                }

                var client = GetRestClient(RequestHandler, mobileDevice: new FakeMobileDevice());

                client.Device = new LocalDevice()
                {
                    DeviceIdentityToken = deviceIdentityToken,
                    ClientId = clientId,
                    Id = deviceid
                };

                var pushChannel = client.Channels.Get(channelName).Push;

                var subscriptions = await pushChannel.ListSubscriptions();
                subscriptions.Should().NotBeNull();

                (await taskAwaiter).Should().BeTrue("Didn't validate function");
            }

            public GeneralTests(ITestOutputHelper output)
                : base(output)
            {
            }
        }
    }
}
