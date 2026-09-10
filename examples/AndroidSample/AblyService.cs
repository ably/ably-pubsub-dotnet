using System;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading;
using IO.Ably;
using IO.Ably.Realtime;

namespace AndroidSample
{
    public class AblyService : IObservable<LogMessage>, IObservable<string>, ILoggerSink
    {
        private AblyRealtime _ably;
        private readonly ISubject<LogMessage> _logSubject = new Subject<LogMessage>();
        private readonly ISubject<string> _connectionSubject = new Subject<string>();

        public void Init()
        {
            _ably = new AblyRealtime(new ClientOptions("lNj80Q.iGyVcQ:2QKX7FFASfX-7H9H")
            {
                LogHandler = this,
                LogLevel = LogLevel.Debug,
                AutoConnect = false,
                UseBinaryProtocol = false
            });
            _ably.Connection.On(change =>
            {
                if (change.Current == ConnectionState.Connected)
                    foreach (var channel in _ably.Channels)
                        channel.Attach();

                _connectionSubject.OnNext(change.Current.ToString());
            });
        }

        public void Connect()
        {
            _ably.Connect();
        }

        public void SendMessage(string channel, string name, string value)
        {
            _ably.Channels.Get(channel).Publish(name, value);
        }

        public IObservable<Message> SubsrcibeToChannel(string channelName)
        {
            var subject = new Subject<Message>();
            _ably.Channels.Get(channelName).Subscribe(subject.OnNext);
            return subject;
        }

        public void LogEvent(LogLevel level, string message)
        {
            Android.Util.Log.Debug("ably", $"[{level}] {message}");
            _logSubject.OnNext(new LogMessage(message, level.ToString()));
        }

        public IDisposable Subscribe(IObserver<LogMessage> observer)
        {
            return _logSubject.Subscribe(observer.NotifyOn(SynchronizationContext.Current));
        }

        public IDisposable Subscribe(IObserver<string> observer)
        {
            return _connectionSubject.Subscribe(observer.NotifyOn(SynchronizationContext.Current));
        }
    }



}