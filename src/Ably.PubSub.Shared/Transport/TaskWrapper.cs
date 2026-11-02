using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Ably.PubSub.Transport
{
    /// <summary>This trivial class wraps legacy callback-style API into a Task API.</summary>
    internal class TaskWrapper
    {
        private readonly TaskCompletionSource<Result> _completionSource = new TaskCompletionSource<Result>();

        public Task<Result> Task => _completionSource.Task;

        public void Callback(bool res, ErrorInfo ei)
        {
            if (res)
            {
                _completionSource.TrySetResult(Result.Ok());
            }
            else if (ei != null)
            {
                _completionSource.TrySetResult(Result.Fail(ei));
            }
            else
            {
                _completionSource.TrySetException(new Exception("Unexpected exception thrown by the TaskWrapper."));
            }
        }

        public void SetException(Exception ex)
        {
            _completionSource.TrySetException(ToAblyException(ex));
        }

        public static Task<Result<T>> Wrap<T>(Action<Action<T, ErrorInfo>> toWrapMethod)
        {
            var wrapper = new TaskWrapper<T>();
            try
            {
                toWrapMethod(wrapper.Callback);
            }
            catch (Exception ex)
            {
                wrapper.SetException(ex);
            }

            return wrapper.Task;
        }

        public static Task<Result<T>> Wrap<T, TResult>(Func<Action<T, ErrorInfo>, TResult> toWrapMethod)
        {
            var wrapper = new TaskWrapper<T>();
            try
            {
                toWrapMethod(wrapper.Callback);
            }
            catch (Exception ex)
            {
                wrapper.SetException(ex);
            }

            return wrapper.Task;
        }

        public static Task<Result> Wrap(Action<Action<bool, ErrorInfo>> toWrapMethod)
        {
            var wrapper = new TaskWrapper();
            try
            {
                toWrapMethod(wrapper.Callback);
            }
            catch (Exception ex)
            {
                wrapper.SetException(ex);
            }

            return wrapper.Task;
        }

        /// <summary>
        /// An <see cref="AblyException"/> already carries the <see cref="ErrorInfo"/> of the failure, such as the
        /// refusal of an operation in the current connection or channel state (RTL6c4), so it is passed through as is.
        /// Any other exception is unexpected and is wrapped (code 50000, the original as the inner exception).
        /// </summary>
        internal static AblyException ToAblyException(Exception ex) => ex as AblyException ?? new AblyException(ex);
    }

    internal class TaskWrapper<T>
    {
        private readonly TaskCompletionSource<Result<T>> _completionSource = new TaskCompletionSource<Result<T>>();

        public Task<Result<T>> Task => _completionSource.Task;

        public void Callback(T res, ErrorInfo ei)
        {
            if (ei != null)
            {
                _completionSource.TrySetResult(Result.Fail<T>(ei));
            }
            else if (typeof(T).GetTypeInfo().IsValueType && IsNotDefaultValue(res))
            {
                _completionSource.TrySetResult(Result.Ok(res));
            }
            else if (typeof(T).GetTypeInfo().IsValueType == false && res != null)
            {
                _completionSource.TrySetResult(Result.Ok(res));
            }
            else
            {
                _completionSource.TrySetException(new Exception("Unexpected Exception from the TaskWrapper")); // Something bad happened
            }
        }

        private static bool IsNotDefaultValue(object res)
        {
            if (res is TimeSpan)
            {
                var span = (TimeSpan)res;
                return span != TimeSpan.MinValue;
            }

            return Equals(res, default(T)) == false;
        }

        public void SetException(Exception ex)
        {
            _completionSource.TrySetException(TaskWrapper.ToAblyException(ex));
        }
    }

    /// <summary>
    /// Wraps the result-carrying publish callback into a Task API. Unlike <see cref="TaskWrapper{T}"/>,
    /// a successful completion may carry a null <see cref="PublishResult"/> (the server sent no result).
    /// </summary>
    internal class PublishTaskWrapper
    {
        private readonly TaskCompletionSource<Result<PublishResult>> _completionSource = new TaskCompletionSource<Result<PublishResult>>();

        public Task<Result<PublishResult>> Task => _completionSource.Task;

        public void Callback(PublishResult result, ErrorInfo error)
        {
            _completionSource.TrySetResult(error != null ? Result.Fail<PublishResult>(error) : Result.Ok(result));
        }

        public void SetException(Exception ex)
        {
            _completionSource.TrySetException(TaskWrapper.ToAblyException(ex));
        }
    }
}
