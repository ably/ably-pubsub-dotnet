using System.Threading.Tasks;

namespace IO.Ably
{
    internal static class TaskUtils
    {
        // http://stackoverflow.com/a/30857843/29555
        public static Task<object> Convert<T>(this Task<T> task)
        {
            var res = new TaskCompletionSource<object>();

            return task.ContinueWith(
                t =>
                    {
                        if (t.IsCanceled)
                        {
                            res.TrySetCanceled();
                        }
                        else if (t.IsFaulted && t.Exception != null)
                        {
                            res.TrySetException(t.Exception);
                        }
                        else
                        {
                            res.TrySetResult(t.Result);
                        }

                        return res.Task;
                    },
                TaskContinuationOptions.ExecuteSynchronously).Unwrap();
        }
    }
}
