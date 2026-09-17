using System;
using System.Threading;
using IO.Ably;
using IO.Ably.PubSub.Device;
using IO.Ably.Realtime;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Ably.Examples.Chat
{
    public class AblyMain : MonoBehaviour, IUiConsole
    {
        private AblyRealtime _ably;
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
        }

        private void ConnectClickHandler()
        {
            _clientOptions.ClientId = _clientId.text;
            if (_isConnected)
            {
                _ably.Close();
            }
            else
            {
                _ably.Connect();
            }
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
