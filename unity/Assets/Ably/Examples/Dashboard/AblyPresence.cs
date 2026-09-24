using IO.Ably;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Ably.Examples.Chat
{
    public class AblyPresence
    {
        private PubSubRealtimeClient _ably;
        private readonly IUiConsole _uiConsole;

        private Button _presenceSubscribe;
        private Button _presenceUnsubscribe;
        private Button _getPresence;
        private Button _presenceMessageHistory;
        private Button _enterPresence;
        private Button _leavePresence;
        private InputField _channelName;
        private InputField _payload;

        private AblyPresence(PubSubRealtimeClient ably, IUiConsole uiConsole)
        {
            _ably = ably;
            _uiConsole = uiConsole;
        }

        internal static AblyPresence CreateInstance(PubSubRealtimeClient ably, IUiConsole uiConsole)
        {
            return new AblyPresence(ably, uiConsole);
        }

        // The client is recreated at connect time (so the latest ClientId is captured);
        // point this console at the current instance.
        internal void UpdateClient(PubSubRealtimeClient ably)
        {
            _ably = ably;
        }

        internal void RegisterUiComponents()
        {
            _presenceSubscribe = GameObject.Find("SubscribePresence").GetComponent<Button>();
            _presenceSubscribe.onClick.AddListener(SubscribeToPresence);

            _presenceUnsubscribe = GameObject.Find("UnsubscribePresence").GetComponent<Button>();
            _presenceUnsubscribe.onClick.AddListener(UnsubscribeFromPresence);

            _getPresence = GameObject.Find("GetPresence").GetComponent<Button>();
            _getPresence.onClick.AddListener(GetPresence);

            _presenceMessageHistory = GameObject.Find("PresenceHistory").GetComponent<Button>();
            _presenceMessageHistory.onClick.AddListener(GetPresenceMessageHistory);

            _enterPresence = GameObject.Find("EnterPresence").GetComponent<Button>();
            _enterPresence.onClick.AddListener(EnterPresence);

            _leavePresence = GameObject.Find("LeavePresence").GetComponent<Button>();
            _leavePresence.onClick.AddListener(LeavePresence);

            _channelName = GameObject.Find("PresenceChannel").GetComponent<InputField>();
            _payload = GameObject.Find("PresencePayload").GetComponent<InputField>();
            EnableUiComponents(false);
        }

        internal void SubscribeToPresence()
        {
            var channelName = _channelName.text;
            _ably.Channels.Get(channelName).Presence.Subscribe(message =>
            {
                _uiConsole.LogAndDisplay($"Received presence message <b>{message.Data}</b> from channel <b>{channelName}</b> with clientId <b>{message.ClientId}</b>");
            });
            _uiConsole.LogAndDisplay($"Successfully subscribed to channel <b>{channelName}</b> for <b>Presence</b> messages");
        }

        internal void UnsubscribeFromPresence()
        {
            var channelName = _channelName.text;
            _ably.Channels.Get(channelName).Presence.Unsubscribe();
            _uiConsole.LogAndDisplay($"Successfully unsubscribed from channel <b>{channelName}</b> for <b>Presence</b> messages");
        }

        internal async void GetPresence()
        {
            var channelName = _channelName.text;
            var presenceMessages = await _ably.Channels.Get(channelName).Presence.GetAsync();
            _uiConsole.LogAndDisplay($"#### <b>{channelName}</b> Presence ####");
            foreach (var presenceMessage in presenceMessages)
            {
                _uiConsole.LogAndDisplay($"ClientID - <b>{presenceMessage.ClientId}</b>, Message - <b>{presenceMessage.Data}</b>");
            }
            _uiConsole.LogAndDisplay($"#### <b>{channelName}</b> ####");
        }

        internal async void GetPresenceMessageHistory()
        {
            var channelName = _channelName.text;
            _uiConsole.LogAndDisplay($"#### <b>{channelName}</b> Presence Messages ####");
            var presenceHistoryPage = await _ably.Channels.Get(channelName).Presence.HistoryAsync();
            while (true)
            {
                foreach (var presenceMessage in presenceHistoryPage.Items)
                {
                    _uiConsole.LogAndDisplay($"ClientID - <b>{presenceMessage.ClientId}</b>, Message - <b>{presenceMessage.Data}</b>");
                }
                if (presenceHistoryPage.IsLast)
                {
                    break;
                }
                presenceHistoryPage = await presenceHistoryPage.NextAsync();
            };
            _uiConsole.LogAndDisplay($"#### <b>{channelName}</b> ####");
        }

        internal async void EnterPresence()
        {
            var channelName = _channelName.text;

            // async-await makes sure call is executed in the background and then result is posted on UnitySynchronizationContext/Main thread
            var result = await _ably.Channels.Get(channelName).Presence.EnterAsync(_payload.text);

            _uiConsole.LogAndDisplay(result.IsSuccess
                ? $"Successfully entered presence to channel <b>{channelName}</b> for clientID <b>{_ably.ClientId}</b>"
                : $"Error entering presence to channel <b>{channelName}</b>, failed with error <b>{result.Error.Message}</b>");
        }

        internal async void LeavePresence()
        {
            var channelName = _channelName.text;

            // async-await makes sure call is executed in the background and then result is posted on UnitySynchronizationContext/Main thread
            var result = await _ably.Channels.Get(channelName).Presence.LeaveAsync(_payload.text);

            _uiConsole.LogAndDisplay(result.IsSuccess
                ? $"Successfully left presence to channel <b>{channelName}</b> for clientID <b>{_ably.ClientId}</b>"
                : $"Error leaving presence to channel <b>{channelName}</b>, failed with error <b>{result.Error.Message}</b>");
        }

        internal void EnableUiComponents(bool isEnabled)
        {
            _channelName.interactable = isEnabled;
            _presenceSubscribe.interactable = isEnabled;
            _payload.interactable = isEnabled;
            _presenceSubscribe.interactable = isEnabled;
            _presenceUnsubscribe.interactable = isEnabled;
            _getPresence.interactable = isEnabled;
            _presenceMessageHistory.interactable = isEnabled;
            _enterPresence.interactable = isEnabled;
            _leavePresence.interactable = isEnabled;
        }
    }
}

