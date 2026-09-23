using System;

namespace IO.Ably.Realtime.Workflow
{
    internal abstract class RealtimeCommand
    {
        public Guid Id { get; } = Guid.NewGuid();

        public DateTimeOffset Created { get; } = DateTimeOffset.UtcNow;

        public string Name => GetType().Name;

        public string TriggeredByMessage { get; private set; } = string.Empty;

        public string Explain()
        {
            var data = ExplainData();
            if (data.IsNotEmpty())
            {
                data = " Data: " + data;
            }

            return $"{GetType().Name}:{data} Meta:{Id}|{Created:s}|TriggeredBy: {TriggeredByMessage}";
        }

        public void RecordTrigger(RealtimeCommand trigger)
        {
            TriggeredByMessage += $"{trigger.Name}:{trigger.Id}";
        }

        public void RecordTrigger(string message) =>
            TriggeredByMessage += message;

        protected abstract string ExplainData();

        public override string ToString()
        {
            return $"{Name}: {Explain()}";
        }
    }

    internal static class RealtimeCommandExtensions
    {
        public static T TriggeredBy<T>(this T command, RealtimeCommand triggerCommand)
            where T : RealtimeCommand
        {
            command.RecordTrigger(triggerCommand);
            return command;
        }

        public static T TriggeredBy<T>(this T command, string message)
            where T : RealtimeCommand
        {
            command.RecordTrigger(message);
            return command;
        }
    }
}
