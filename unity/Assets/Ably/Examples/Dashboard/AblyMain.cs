using System;
using System.Threading;
using Ably.PubSub;
using Ably.PubSub.Device;
using Ably.PubSub.Realtime;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Ably.Examples.Chat
{
    public class AblyMain : MonoBehaviour, IUiConsole
    {
        private PubSubRealtimeClient _ably;
        private ClientOptions _clientOptions;

        private Text _textContent;
        private InputField _clientId;
        private Button _connectButton;
        private Button _connectionStatus;

        private static string _apiKey = "Your_Api_Key_Here";

        private AblyChannel _ablyChannelUiConsole;
        private AblyPresence _ablyPresenceUiConsole;

        private bool _isConnected;

        void Start()
        {
            InitializeAbly();
            CreateAblyClient();
            RegisterUiComponents();
            _ablyChannelUiConsole = AblyChannel.CreateInstance(_ably, this);
            _ablyChannelUiConsole.RegisterUiComponents();
            _ablyPresenceUiConsole = AblyPresence.CreateInstance(_ably, this);
            _ablyPresenceUiConsole.RegisterUiComponents();
        }

        // Add components 
        private void RegisterUiComponents()
        {
            _textContent = GameObject.Find("TxtConsole").GetComponent<Text>();
            _clientId = GameObject.Find("ClientId").GetComponent<InputField>();
            _connectButton = GameObject.Find("ConnectBtn").GetComponent<Button>();
            _connectButton.onClick.AddListener(ConnectClickHandler);
            _connectionStatus = GameObject.Find("ConnectionStatus").GetComponent<Button>();
        }

        private void InitializeAbly()
        {
            _clientOptions = new ClientOptions
            {
                Key = _apiKey,
                AutoConnect = false,
                // this will make sure to post callbacks on UnitySynchronization Context Main Thread
                CustomContext = SynchronizationContext.Current
            };

            // The client is (re)created at connect time, once ClientId is known — see
            // CreateAblyClient and ConnectClickHandler. PubSubDevice.CreateClient clones
            // the options, so anything the client must capture (e.g. ClientId) has to be
            // set on _clientOptions BEFORE the client is created.
        }

        private void CreateAblyClient()
        {
            // Dispose of any client this one replaces, so it is not left connected.
            if (_ably != null && _ably.Connection.State != ConnectionState.Closed)
            {
                _ably.Close();
            }

            _ably = PubSubDevice.CreateClient(_clientOptions);
            _ably.Connection.On(args =>
            {
                LogAndDisplay($"Connection State is <b>{args.Current}</b>");
                _connectionStatus.GetComponentInChildren<Text>().text = args.Current.ToString();
                var connectionStatusBtnImage = _connectionStatus.GetComponent<Image>();
                switch (args.Current)
                {
                    case ConnectionState.Initialized:
                        connectionStatusBtnImage.color = Color.white;
                        break;
                    case ConnectionState.Connecting:
                        connectionStatusBtnImage.color = Color.gray;
                        break;
                    case ConnectionState.Connected:
                        connectionStatusBtnImage.color = Color.green;
                        break;
                    case ConnectionState.Disconnected:
                        connectionStatusBtnImage.color = Color.yellow;
                        break;
                    case ConnectionState.Closing:
                        connectionStatusBtnImage.color = Color.yellow;
                        break;
                    case ConnectionState.Closed:
                    case ConnectionState.Failed:
                    case ConnectionState.Suspended:
                        connectionStatusBtnImage.color = Color.red;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                _isConnected = args.Current == ConnectionState.Connected;
                _ablyChannelUiConsole.EnableUiComponents(_isConnected);
                _ablyPresenceUiConsole.EnableUiComponents(_isConnected);
                _connectButton.GetComponentInChildren<Text>().text = _isConnected ? "Disconnect" : "Connect";
            });

            // The consoles hold a reference to the client; point them at the new instance.
            // (Null-conditional: on the initial Start() call they are not created yet.)
            _ablyChannelUiConsole?.UpdateClient(_ably);
            _ablyPresenceUiConsole?.UpdateClient(_ably);
        }

        private void ConnectClickHandler()
        {
            if (_isConnected)
            {
                _ably.Close();
                return;
            }

            _clientOptions.ClientId = _clientId.text; // set BEFORE the door clones the options
            CreateAblyClient(); // recreate so the latest ClientId is captured
            _ably.Connect();
        }

        public void LogAndDisplay(string message)
        {
            Debug.Log(message);
            _textContent.text = $"{_textContent.text}\n{message}";
        }

    }

    internal interface IUiConsole
    {
        void LogAndDisplay(string message);
    }
}
