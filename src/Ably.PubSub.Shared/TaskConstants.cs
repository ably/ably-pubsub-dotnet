using System.Threading.Tasks;

namespace IO.Ably
{
    // https://github.com/StephenCleary/AsyncEx/blob/edb2c6b66d41471008a56e4098f9670b5143617e/src/Nito.AsyncEx.Tasks/TaskConstants.cs

    /// <summary>
    /// Provides completed Task constants.
    /// </summary>
    public static class TaskConstants
    {
        /// <summary>
        /// Gets a task that has been completed with the value <c>true</c>.
        /// </summary>
        public static Task<bool> BooleanTrue { get; } = Task.FromResult(true);

        /// <summary>
        /// Gets a task that has been completed with the value <c>false</c>.
        /// </summary>
        public static Task<bool> BooleanFalse { get; } = TaskConstants<bool>.Default;

        /// <summary>
        /// Gets a task that has been completed with the value <c>0</c>.
        /// </summary>
        public static Task<int> Int32Zero { get; } = TaskConstants<int>.Default;

        /// <summary>
        /// Gets a task that has been completed with the value <c>-1</c>.
        /// </summary>
        public static Task<int> Int32NegativeOne { get; } = Task.FromResult(-1);

        /// <summary>
        /// Gets a <see cref="Task"/> that has been completed.
        /// </summary>
        public static Task Completed { get; } = BooleanTrue;

        /// <summary>
        /// Gets a <see cref="Task"/> that will never complete.
        /// </summary>
        public static Task Never { get; } = TaskConstants<bool>.Never;

        /// <summary>
        /// Gets a task that has been canceled.
        /// </summary>
        public static Task Canceled { get; } = TaskConstants<bool>.Canceled;
    }

    /// <summary>
    /// Provides completed task constants.
    /// </summary>
    /// <typeparam name="T">The type of the task result.</typeparam>
    public static class TaskConstants<T>
    {
        private static Task<T> CanceledTask()
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetCanceled();
            return tcs.Task;
        }

        /// <summary>
        /// Gets a task that has been completed with the default value of <typeparamref name="T"/>.
        /// </summary>
        public static Task<T> Default { get; } = Task.FromResult(default(T));

        /// <summary>
        /// Gets a <see cref="Task"/> that will never complete.
        /// </summary>
        public static Task<T> Never { get; } = new TaskCompletionSource<T>().Task;

        /// <summary>
        /// Gets a task that has been canceled.
        /// </summary>
        public static Task<T> Canceled { get; } = CanceledTask();
    }
}
