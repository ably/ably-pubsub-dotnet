using System;
using System.Threading;
using System.Threading.Tasks;

namespace IO.Ably
{
    /// <summary>
    /// Contains Extension methods working with Task objects.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Waits for the task to complete, unwrapping any exceptions.
        /// </summary>
        /// <param name="task">The task. May not be <c>null</c>.</param>
        public static void WaitAndUnwrapException(this Task task)
        {
            task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Helps chain results that return a Task without having to await the first one.
        /// </summary>
        /// <typeparam name="T">Type of the input Task.</typeparam>
        /// <typeparam name="TResult">Type of the returned result.</typeparam>
        /// <param name="start">The first task.</param>
        /// <param name="mapFunction">The function used to map to the resulting type.</param>
        /// <returns>return Task of TResult.</returns>
        internal static async Task<TResult> MapAsync<T, TResult>(this Task<T> start, Func<T, TResult> mapFunction)
        {
            var value = await start;
            return mapFunction(value);
        }

        /// <summary>
        /// Helps timeout an async execution after a specified period.
        /// </summary>
        /// <typeparam name="T">Type of Task.</typeparam>
        /// <param name="task">The task we want to timeout.</param>
        /// <param name="timeout">The period after which the timeout will occur.</param>
        /// <param name="timeoutResult">The value returned if the timeout occurs.</param>
        /// <returns>will either return the actual result if the tasks executes in time or the value provided by <paramref name="timeoutResult"/>.</returns>
        internal static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout, T timeoutResult)
        {
            using (var cts = new CancellationTokenSource())
            {
                var delayTask = Task.Delay(timeout, cts.Token);

                var resultTask = await Task.WhenAny(task, delayTask);
                if (resultTask == delayTask)
                {
                    // Operation cancelled
                    return timeoutResult;
                }

                // Cancel the timer task so that it does not fire
                cts.Cancel();

                return await task;
            }
        }
    }
}
