namespace IO.Ably.Realtime.Workflow
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "StyleCop.CSharp.DocumentationRules",
        "SA1600:Elements should be documented",
        Justification = "Internal interface.")]
    internal interface IQueueCommand
    {
        void QueueCommand(params RealtimeCommand[] commands);
    }
}
