using System.Linq;
using IO.Ably;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Ably.Examples.Chat
{
    internal class AblyChannel
    {
        private AblyRealtime _ably;
        private readonly IUiConsole _uiConsole;

        private Button _subscribe;
        private Button _unsubscribe;
        private Button _listChannels;
        private Button _messageHistory;
        private Button _publish;
        private InputField _channelName;
        private InputField _eventName;
        private InputField _payload;

        private AblyChannel(AblyRealtime ably, IUiConsole uiConsole)
        {
            _ably = ably;
            _uiConsole = uiConsole;
        }

        internal static AblyChannel CreateInstance(AblyRealtime ably, IUiConsole uiConsole)
        {
            return new AblyChannel(ably, uiConsole);
        }

        // The client is recreated at connect time (so the latest ClientId is captured);
        // point this console at the current instance.
        internal void UpdateClient(AblyRealtime ably)
        {
            _ably = ably;
        }

        internal void RegisterUiComponents()
        {
            _subscribe = GameObject.Find("Subscribe").GetComponent<Button>();
            _subscribe.onClick.AddListener(SubscribeToChannel);

            _unsubscribe = GameObject.Find("Unsubscribe").GetComponent<Button>();
            _unsubscribe.onClick.AddListener(UnsubscribeFromChannel);

            _listChannels = GameObject.Find("ListChannels").GetComponent<Button>();
            _listChannels.onClick.AddListener(ListChannels);

            _messageHistory = GameObject.Find("MessageHistory").GetComponent<Button>();
            _messageHistory.onClick.AddListener(LoadChannelMessageHistory);

            _publish = GameObject.Find("Publish").GetComponent<Button>();
            _publish.onClick.AddListener(PublishMessage);

            _channelName = GameObject.Find("ChannelName").GetComponent<InputField>();
            _eventName = GameObject.Find("EventName").GetComponent<InputField>();
            _payload = GameObject.Find("Payload").GetComponent<InputField>();
            EnableUiComponents(false);
        }

        private void SubscribeToChannel()
        {
            var channelName = _channelName.text;
            var eventName = _eventName.text;
            _ably.Channels.Get(channelName).Subscribe(eventName, message =>
            {
                _uiConsole.LogAndDisplay($"Received message <b>{message.Data}</b> from channel <b>{channelName}</b>");
            });
            _uiConsole.LogAndDisplay($"Successfully subscribed to channel <b>{channelName}</b>");
        }

        private void UnsubscribeFromChannel()
        {
            var channelName = _channelName.text;
            _ably.Channels.Get(channelName).Unsubscribe();
            _uiConsole.LogAndDisplay($"Successfully unsubscribed from channel <b>{channelName}</b>");
        }

        private void ListChannels()
        {
            var channelNames = string.Join(", ", _ably.Channels.Select(channel => channel.Name));
            _uiConsole.LogAndDisplay($"Channel Names - <b>{channelNames}</b>");
        }

        private async void LoadChannelMessageHistory()
        {
            var channelName = _channelName.text;
            _uiConsole.LogAndDisplay($"#### <b>{channelName}</b> ####");
            var historyPage = await _ably.Channels.Get(channelName).HistoryAsync();
            while (true)
            {
                foreach (var message in historyPage.Items)
                {
                    _uiConsole.LogAndDisplay(message.Data.ToString());
                }
                if (historyPage.IsLast)
                {
                    break;
                }
                historyPage = await historyPage.NextAsync();
            };
            _uiConsole.LogAndDisplay($"#### <b>{channelName}</b> ####");
        }

        private async void PublishMessage()
        {
            var channelName = _channelName.text;
            var eventName = _eventName.text;
            var payload = _payload.text;
            // async-await makes sure call is executed in the background and then result is posted on UnitySynchronizationContext/Main thread
            var result = await _ably.Channels.Get(channelName).PublishAsync(eventName, payload);
            _uiConsole.LogAndDisplay(result.IsSuccess
                ? $"Successfully published message <b>{payload}</b> to channel <b>{channelName}</b>"
                : $"Error publishing message <b>{payload}</b> to channel <b>{channelName}</b>, failed with error <b>{result.Error.Message}</b>");
        }

        internal void EnableUiComponents(bool isEnabled)
        {
            _channelName.interactable = isEnabled;
            _eventName.interactable = isEnabled;
            _payload.interactable = isEnabled;
            _subscribe.interactable = isEnabled;
            _unsubscribe.interactable = isEnabled;
            _listChannels.interactable = isEnabled;
            _messageHistory.interactable = isEnabled;
            _publish.interactable = isEnabled;
        }
    }
}
