using System;
using System.Reflection;
using System.Threading.Tasks;

namespace IO.Ably.Transport
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
            _completionSource.TrySetException(new AblyException(ex));
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
            _completionSource.TrySetException(new AblyException(ex));
        }
    }
}
