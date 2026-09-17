using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IO.Ably.Push
{
    /// <summary>
    /// Push Admin APIs.
    /// </summary>
    public sealed class PushAdmin : IPushChannelSubscriptions, IDeviceRegistrations
    {
        private const string ChannelSubUrl = "/push/channelSubscriptions";

        private readonly ILogger _logger;
        private readonly AblyRest _restClient;

        internal PushAdmin(AblyRest restClient, ILogger logger)
        {
            _restClient = restClient;
            _logger = logger;
        }

        private ClientOptions Options => _restClient.Options;

        /// <summary>
        /// Exposes channel subscriptions apis.
        /// </summary>
        public IPushChannelSubscriptions ChannelSubscriptions => this;

        /// <summary>
        /// Exposes device registrations apis.
        /// </summary>
        public IDeviceRegistrations DeviceRegistrations => this;

        /// <summary>
        /// Register a new device
        /// The public api doesn't expose this method but it's much easier to put it here than to manually call it when needed.
        /// </summary>
        /// <param name="details">Device details needed for registration.</param>
        /// <returns>Updated device including a deviceIdentityToken assigned by the Push service.</returns>
        internal async Task<LocalDevice> RegisterDevice(DeviceDetails details)
        {
            ValidateDeviceDetails();
            var request = _restClient.CreateRequest("/push/deviceRegistrations", HttpMethod.Post);
            AddFullWaitIfNecessary(request);
            request.PostData = details;

            return await ExecuteRequest();

            void ValidateDeviceDetails()
            {
                if (details is null)
                {
                    throw new AblyException("DeviceDetails is null.", ErrorCodes.BadRequest);
                }

                if (details.Id.IsEmpty())
                {
                    throw new AblyException("DeviceDetails needs an non empty Id parameter.", ErrorCodes.BadRequest);
                }

                if (details.Platform.IsEmpty())
                {
                    throw new AblyException("DeviceDetails needs a valid Platform. Supported values are 'ios', 'android' or 'browser'.", ErrorCodes.BadRequest);
                }

                if (details.FormFactor.IsEmpty())
                {
                    throw new AblyException(
                        "DeviceDetails needs a valid FormFactor. Supporter values are 'phone', 'tablet', 'desktop', 'tv', 'watch', 'car' or 'embedded'.", ErrorCodes.BadRequest);
                }

                if (details.Push?.Recipient is null)
                {
                    throw new AblyException("A valid recipient is required to register a device.", ErrorCodes.BadRequest);
                }
            }

            async Task<LocalDevice> ExecuteRequest()
            {
                try
                {
                    var response = await _restClient.ExecuteRequest(request);

                    var jsonResponse = JObject.Parse(response.TextResponse);
                    var localDevice = jsonResponse.ToObject<LocalDevice>();
                    var deviceToken = (string)jsonResponse["deviceIdentityToken"]?["token"];
                    if (deviceToken != null)
                    {
                        localDevice.DeviceIdentityToken = deviceToken;
                    }

                    return localDevice;
                }
                catch (JsonReaderException jsonEx)
                {
                    _logger.Error("Error registering device. Invalid response", jsonEx);
                    var error = new ErrorInfo("Error registering device. Invalid response.", ErrorCodes.InternalError);
                    throw new AblyException(error, jsonEx);
                }
                catch (AblyException e)
                {
                    _logger.Error("Error registering Device", e);
                    throw;
                }
            }
        }

        /// <summary>
        /// Update device recipient information
        /// The public api doesn't expose this method but it's much easier to put it here than to manually call it when needed.
        /// </summary>
        /// <param name="details">Device details which contain the update.</param>
        internal async Task PatchDeviceRecipient(DeviceDetails details)
        {
            var body = JObject.FromObject(new
            {
                push = new { recipient = details.Push.Recipient },
            });

            ValidateDeviceDetails();
            var request = _restClient.CreateRequest($"/push/deviceRegistrations/{details.Id}", new HttpMethod("PATCH"));
            AddFullWaitIfNecessary(request);
            request.PostData = body;
            _ = await _restClient.ExecuteRequest(request);

            void ValidateDeviceDetails()
            {
                if (details is null)
                {
                    throw new AblyException("DeviceDetails is null.", ErrorCodes.BadRequest);
                }

                if (details.Id.IsEmpty())
                {
                    throw new AblyException("DeviceDetails needs an non empty Id parameter.", ErrorCodes.BadRequest);
                }

                if (details.Push?.Recipient is null)
                {
                    throw new AblyException("A valid recipient is required to patch device recipient.", ErrorCodes.BadRequest);
                }
            }
        }

        /// <summary>
        /// Publish a push notification message.
        /// </summary>
        /// <param name="recipient">Recipient. Best description of what is allowed can be found in the RestApi documentation: https://ably.com/docs/rest-api#post-device-registration.</param>
        /// <param name="payload">Message payload.</param>
        /// <returns>Task.</returns>
        public async Task PublishAsync(JObject recipient, JObject payload)
        {
            ValidateRequest();
            var request = _restClient.CreatePostRequest("/push/publish");
            AddFullWaitIfNecessary(request);
            var data = new JObject { { "recipient", recipient } };
            foreach (var property in payload.Properties())
            {
                data.Add(property.Name, property.Value);
            }

            request.PostData = data;

            _ = await _restClient.ExecuteRequest(request);

            void ValidateRequest()
            {
                if (recipient is null)
                {
                    throw new AblyException("Please provide a valid and non-empty recipient", ErrorCodes.BadRequest);
                }

                if (payload is null)
                {
                    throw new AblyException("Please provide a non-empty payload", ErrorCodes.BadRequest);
                }
            }
        }

        /// <inheritdoc />
        async Task<PushChannelSubscription> IPushChannelSubscriptions.SaveAsync(PushChannelSubscription subscription)
        {
            Validate();

            var request = _restClient.CreatePostRequest(ChannelSubUrl);
            AddFullWaitIfNecessary(request);

            if (subscription.DeviceId.IsNotEmpty() && subscription.DeviceId == _restClient.Device?.Id)
            {
                AddDeviceAuthenticationToRequest(request, _restClient.Device);
            }

            request.PostData = subscription;

            return await _restClient.ExecuteRequest<PushChannelSubscription>(request);

            void Validate()
            {
                if (subscription is null)
                {
                    throw new AblyException("Subscription cannot be null", ErrorCodes.BadRequest);
                }

                if (subscription.Channel.IsEmpty())
                {
                    throw new AblyException("Please provide a non-empty channel name.", ErrorCodes.BadRequest);
                }
            }
        }

        /// <inheritdoc />
        async Task<PaginatedResult<PushChannelSubscription>> IPushChannelSubscriptions.ListAsync(ListSubscriptionsRequest requestFilter)
        {
            var request = _restClient.CreateGetRequest(ChannelSubUrl);
            request.AddQueryParameters(requestFilter.ToQueryParams());

            return await _restClient.ExecutePaginatedRequest(request, ListChannelSubscriptions);

            async Task<PaginatedResult<PushChannelSubscription>> ListChannelSubscriptions(PaginatedRequestParams requestParams)
            {
                var paginatedRequest = _restClient.CreateGetRequest(ChannelSubUrl);
                paginatedRequest.AddQueryParameters(requestParams.GetParameters());
                return await _restClient.ExecutePaginatedRequest(paginatedRequest, ListChannelSubscriptions);
            }
        }

        /// <inheritdoc />
        async Task IPushChannelSubscriptions.RemoveAsync(PushChannelSubscription subscription)
        {
            Validate();

            var request = _restClient.CreateRequest(ChannelSubUrl, HttpMethod.Delete);
            AddFullWaitIfNecessary(request);
            request.AddQueryParameters(GetQueryParams());

            if (subscription.DeviceId.IsNotEmpty() && subscription.DeviceId == _restClient.Device?.Id)
            {
                AddDeviceAuthenticationToRequest(request, _restClient.Device);
            }

            _ = await _restClient.ExecuteRequest(request);

            void Validate()
            {
                if (subscription is null)
                {
                    throw new AblyException("Subscription should not be null", ErrorCodes.BadRequest);
                }

                if (subscription.Channel.IsEmpty())
                {
                    throw new AblyException(
                        "Please provide a subscription with a non-empty channel name.",
                        ErrorCodes.BadRequest);
                }
            }

            IEnumerable<KeyValuePair<string, string>> GetQueryParams()
            {
                yield return new KeyValuePair<string, string>("channel", subscription.Channel);
                if (subscription.DeviceId.IsNotEmpty())
                {
                    yield return new KeyValuePair<string, string>("deviceId", subscription.DeviceId);
                }

                if (subscription.ClientId.IsNotEmpty())
                {
                    yield return new KeyValuePair<string, string>("clientId", subscription.ClientId);
                }
            }
        }

        /// <inheritdoc />
        async Task IPushChannelSubscriptions.RemoveWhereAsync(IDictionary<string, string> removeParams)
        {
            Validate();

            var request = _restClient.CreateRequest(ChannelSubUrl, HttpMethod.Delete);
            AddFullWaitIfNecessary(request);
            request.AddQueryParameters(removeParams);

            _ = await _restClient.ExecuteRequest(request);

            void Validate()
            {
                if (removeParams is null)
                {
                    throw new AblyException("RemoveParams should not be null", ErrorCodes.BadRequest);
                }
            }
        }

        /// <inheritdoc />
        async Task<PaginatedResult<string>> IPushChannelSubscriptions.ListChannelsAsync(PaginatedRequestParams requestParams)
        {
            var request = _restClient.CreateGetRequest("/push/channels");
            request.AddQueryParameters(requestParams.GetParameters());

            return await _restClient.ExecutePaginatedRequest(request, ((IPushChannelSubscriptions)this).ListChannelsAsync);
        }

        /// <inheritdoc />
        async Task<DeviceDetails> IDeviceRegistrations.SaveAsync(DeviceDetails details)
        {
            Validate();

            var request = _restClient.CreateRequest("/push/deviceRegistrations/" + details.Id, HttpMethod.Put);
            AddFullWaitIfNecessary(request);
            var localDevice = _restClient.Device;
            if (localDevice != null && localDevice.Id == details.Id)
            {
                AddDeviceAuthenticationToRequest(request, localDevice);
            }

            request.PostData = details;
            var result = await _restClient.ExecuteRequest<DeviceDetails>(request);

            return result;

            void Validate()
            {
                if (details is null || details.Id.IsEmpty())
                {
                    throw new AblyException("Please provide a non-null DeviceDetails including a valid Id", ErrorCodes.BadRequest);
                }
            }
        }

        /// <inheritdoc />
        async Task<Result<DeviceDetails>> IDeviceRegistrations.GetAsync(string deviceId)
        {
            Validate();

            var request = _restClient.CreateGetRequest($"/push/deviceRegistrations/{deviceId}");
            AddFullWaitIfNecessary(request);
            AddDeviceAuthenticationToRequest(request, _restClient.Device);
            var response = await _restClient.ExecuteRequest(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Fail<DeviceDetails>(new ErrorInfo($"Device with Id '{deviceId}' is not found", ErrorCodes.NotFound, HttpStatusCode.NotFound));
            }

            return ParseResponse(request, response);

            void Validate()
            {
                if (deviceId.IsEmpty())
                {
                    throw new AblyException("Please provide a non-empty deviceId", ErrorCodes.BadRequest);
                }
            }

            Result<DeviceDetails> ParseResponse(AblyRequest ablyRequest, AblyResponse ablyResponse)
            {
                try
                {
                    return Result.Ok(_restClient.MessageHandler.ParseResponse<DeviceDetails>(ablyRequest, ablyResponse));
                }
                catch (Exception e)
                {
                    return Result.Fail<DeviceDetails>(new ErrorInfo($"Error parsing response for /push/deviceRegistrations/{deviceId}", ErrorCodes.InternalError, HttpStatusCode.InternalServerError, e));
                }
            }
        }

        /// <inheritdoc />
        async Task<PaginatedResult<DeviceDetails>> IDeviceRegistrations.List(ListDeviceDetailsRequest filterRequest)
        {
            Validate();

            const string url = "/push/deviceRegistrations";

            var request = _restClient.CreateGetRequest(url);
            request.AddQueryParameters(filterRequest.ToQueryParams());

            return await _restClient.ExecutePaginatedRequest(request, HandleConsequentPaginatedRequests);

            void Validate()
            {
                if (filterRequest is null)
                {
                    throw new AblyException(
                        "Please provide a non null request. You can use ListDeviceDetailsRequest.WithClientId or ListDeviceDetailsRequest.WithDeviceId to filter it further.", ErrorCodes.BadRequest);
                }
            }
        }

        private async Task<PaginatedResult<DeviceDetails>> HandleConsequentPaginatedRequests(PaginatedRequestParams requestParams)
        {
            var request = _restClient.CreateGetRequest("/push/deviceRegistrations");
            request.AddQueryParameters(requestParams.GetParameters());
            return await _restClient.ExecutePaginatedRequest(request, HandleConsequentPaginatedRequests);
        }

        /// <inheritdoc />
        async Task IDeviceRegistrations.RemoveAsync(DeviceDetails details)
        {
            await ((IDeviceRegistrations)this).RemoveAsync(details?.Id);
        }

        /// <inheritdoc />
        async Task IDeviceRegistrations.RemoveAsync(string deviceId)
        {
            Validate();

            var request = _restClient.CreateRequest($"/push/deviceRegistrations/{deviceId}", HttpMethod.Delete);
            AddFullWaitIfNecessary(request);
            await _restClient.ExecuteRequest(request);

            void Validate()
            {
                if (deviceId.IsEmpty())
                {
                    throw new AblyException("Please pass a non-empty deviceId to Remove", ErrorCodes.BadRequest);
                }
            }
        }

        /// <inheritdoc />
        async Task IDeviceRegistrations.RemoveWhereAsync(Dictionary<string, string> deleteFilter)
        {
            Validate();

            var request = _restClient.CreateRequest("/push/deviceRegistrations", HttpMethod.Delete);
            AddFullWaitIfNecessary(request);
            request.AddQueryParameters(deleteFilter);

            if (deleteFilter.ContainsKey("deviceId") && deleteFilter["deviceId"] == _restClient.Device?.Id)
            {
                AddDeviceAuthenticationToRequest(request, _restClient.Device);
            }

            await _restClient.ExecuteRequest(request);

            void Validate()
            {
                if (deleteFilter is null)
                {
                    throw new AblyException("DeleteFilter cannot be null.", ErrorCodes.BadRequest);
                }
            }
        }

        private void AddFullWaitIfNecessary(AblyRequest request)
        {
            if (Options.PushAdminFullWait)
            {
                request.AddQueryParameters(new[] { new KeyValuePair<string, string>("fullWait", "true") });
            }
        }

        internal static void AddDeviceAuthenticationToRequest(AblyRequest request, LocalDevice device)
        {
            if (device is null)
            {
                return;
            }

            if (device.DeviceIdentityToken.IsNotEmpty())
            {
                request.Headers.Add(Defaults.DeviceIdentityTokenHeader, device.DeviceIdentityToken);
            }
            else if (device.DeviceSecret.IsNotEmpty())
            {
                request.Headers.Add(Defaults.DeviceSecretHeader, device.DeviceSecret);
            }
        }
    }
}
