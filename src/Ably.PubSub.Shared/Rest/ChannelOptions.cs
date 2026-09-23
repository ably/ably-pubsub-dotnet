using IO.Ably.Encryption;

namespace IO.Ably
{
    /// <summary>
    /// Channel options used for initialising channels.
    /// </summary>
    public class ChannelOptions
    {
        private ChannelModes _modes = new ChannelModes();
        private ChannelParams _params = new ChannelParams();

        internal ILogger Logger { get; set; }

        /// <summary>
        /// Indicates whether the channel is encrypted.
        /// </summary>
        public bool Encrypted { get; }

        /// <summary>
        /// If Encrypted it provides the <see cref="IO.Ably.CipherParams"/>.
        /// </summary>
        public CipherParams CipherParams { get; }

        /// <summary>
        /// Params allows custom parameters to be passed to the server when attaching the channel.
        /// In that list are 'delta' and 'rewind'. For more information about channel params visit
        /// https://ably.com/docs/realtime/channels/channel-parameters/overview.
        /// </summary>
        public ChannelParams Params
        {
            get => _params;
            set => _params = value ?? new ChannelParams();
        }

        /// <summary>
        /// Channel Modes like Params are passed to the server when attaching the channel.
        /// They let specify how the channel will behave and what is allowed. <see cref="ChannelMode"/>.
        /// </summary>
        public ChannelModes Modes
        {
            get => _modes;
            set => _modes = value ?? new ChannelModes();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelOptions"/> class.
        /// </summary>
        /// <param name="params"><see cref="IO.Ably.CipherParams"/>.</param>
        public ChannelOptions(CipherParams @params)
            : this(DefaultLogger.LoggerInstance, true, @params) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelOptions"/> class.
        /// </summary>
        /// <param name="encrypted">whether it is encrypted.</param>
        /// <param name="params">optional, <see cref="CipherParams"/>.</param>
        /// <param name="modes">optional, <see cref="ChannelModes"/>.</param>
        /// <param name="channelParams">optional, <see cref="ChannelParams"/>.</param>
        public ChannelOptions(
            bool encrypted = false,
            CipherParams @params = null,
            ChannelModes modes = null,
            ChannelParams channelParams = null)
            : this(null, encrypted, @params, modes, channelParams) { }

        internal ChannelOptions(
            ILogger logger,
            bool encrypted = false,
            CipherParams @params = null,
            ChannelModes modes = null,
            ChannelParams channelParams = null)
        {
            Logger = logger ?? DefaultLogger.LoggerInstance;
            Encrypted = encrypted;
            CipherParams = @params ?? Crypto.GetDefaultParams();
            Modes = modes;
            Params = channelParams;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChannelOptions"/> class.
        /// </summary>
        /// <param name="key">cipher key.</param>
        public ChannelOptions(byte[] key)
        {
            Logger = DefaultLogger.LoggerInstance;
            Encrypted = true;
            CipherParams = new CipherParams(key);
        }

        private bool Equals(ChannelOptions other)
        {
            return Encrypted == other.Encrypted && Equals(CipherParams, other.CipherParams);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((ChannelOptions)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Encrypted.GetHashCode() * 397) ^ (CipherParams?.GetHashCode() ?? 0);
            }
        }
    }
}
