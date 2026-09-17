using System.Collections.Generic;
using System.Threading.Tasks;

namespace IO.Ably.Push
{
    /// <summary>
    /// Device Registrations APIs. For more information visit the Ably Rest Documentation: https://ably.com/docs/rest-api#post-device-registration.
    /// </summary>
    public interface IDeviceRegistrations
    {
        /// <summary>
        /// Update a device registration.
        /// </summary>
        /// <param name="details">Device to be updated. Only `clientId`, `metadata` and `Push.Recipient` can be updates. The rest of the values must match what the server contains.
        /// RestApi: https://ably.com/docs/rest-api#update-device-registration.</param>
        /// <returns>Updated Device Registration.</returns>
        Task<DeviceDetails> SaveAsync(DeviceDetails details); // The Rest API only allows clientId, metadata and push.recipient to be updates FYI: https://ably.com/docs/rest-api#update-device-registration

        /// <summary>
        /// Obtain the details for a device registered for receiving push registrations.
        /// RestApi: https://ably.com/docs/rest-api#get-device-registration.
        /// </summary>
        /// <param name="deviceId">Id of the device.</param>
        /// <returns>Returns a DeviceDetails class if the device is found or `null` if not.</returns>
        Task<Result<DeviceDetails>> GetAsync(string deviceId);

        /// <summary>
        /// Obtain the details for devices registered for receiving push registrations.
        /// RestApi: https://ably.com/docs/rest-api#list-device-registrations.
        /// </summary>
        /// <param name="request">Allows to filter by clientId or deviceId. For further information <see cref="ListDeviceDetailsRequest"/>.</param>
        /// <returns>A paginated list of DeviceDetails.</returns>
        Task<PaginatedResult<DeviceDetails>> List(ListDeviceDetailsRequest request);

        /// <summary>
        /// Removes a registered device.
        /// RestAPI: https://ably.com/docs/rest-api#delete-device-registration.
        /// </summary>
        /// <param name="details">DeviceDetails to be removed.</param>
        /// <returns>Task.</returns>
        Task RemoveAsync(DeviceDetails details);

        /// <summary>
        /// Removes a registered device.
        /// RestAPI: https://ably.com/docs/rest-api#delete-device-registration.
        /// </summary>
        /// <param name="deviceId">The deviceId of the device to be removed.</param>
        /// <returns>Task.</returns>
        Task RemoveAsync(string deviceId);

        /// <summary>
        /// Removes a registered devices based on a filter of parameters.
        /// RestAPI: https://ably.com/docs/rest-api#delete-device-registration.
        /// </summary>
        /// <param name="deleteFilter">Filter devices by deviceId or clientId.</param>
        /// <returns>Task.</returns>
        Task RemoveWhereAsync(Dictionary<string, string> deleteFilter);
    }
}
