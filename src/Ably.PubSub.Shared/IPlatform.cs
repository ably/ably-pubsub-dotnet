using IO.Ably.Push;
using IO.Ably.Transport;

namespace IO.Ably
{
    /// <summary>
    /// This interface is implemented for each platform .NETFramework, NetStandard,
    /// iOS and Android. The library dynamically creates an instance of Platform in
    /// IoC.cs. It lets us deal with the differences in the various platforms.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "StyleCop.CSharp.DocumentationRules",
        "SA1600:Elements should be documented",
        Justification = "Internal interface")]
    internal interface IPlatform
    {
        Agent.PlatformRuntime PlatformId { get; }

        ITransportFactory TransportFactory { get; }

        IMobileDevice MobileDevice { get; set; }

        /// <summary>
        /// This method when implemented in each Platform class includes logic to subscribe to
        /// NetworkStatus changes from the operating system. It is then exposed through
        /// IoC.RegisterOsNetworkStateChanged to the rest of the library and should be called
        /// when the Realtime library is initialized only if the ClientOption `AutomaticNetworkStateMonitoring`
        /// is set to true.
        /// The implementation will only allow one registration to operating system network state events even
        /// thought this method can be called multiple times.
        /// </summary>
        void RegisterOsNetworkStateChanged();
    }
}
