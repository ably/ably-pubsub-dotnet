using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Ably.PubSub;

namespace Assets.Tests.AblySandbox
{
    public static class DateHelper
    {
        public static DateTimeOffset CreateDate(int year, int month, int day, int hours = 0, int minutes = 0, int seconds = 0)
        {
            return new DateTimeOffset(year, month, day, hours, minutes, seconds, TimeSpan.Zero);
        }
    }

    public static class StringExtensions
    {
        public static string AddRandomSuffix(this string str)
        {
            if (str.IsEmpty())
            {
                return str;
            }

            return str + "_" + Guid.NewGuid().ToString("D").Substring(0, 8);
        }
    }

    internal static class TestHelpers
    {
        public static async Task WaitFor(int timeoutMs, int taskCount, Action<Action> action, Action onFail = null)
        {
            var tsc = new TaskCompletionAwaiter(timeoutMs, taskCount);

            void Done()
            {
                tsc.Tick();
            }

            action(Done);
            var success = await tsc.Task;
            if (!success)
            {
                var msg = $"Timeout of {timeoutMs}ms exceeded.";
                if (taskCount > 1)
                {
                    msg += $" Completed {taskCount - tsc.TaskCount} of {taskCount} tasks.";
                }

                onFail?.Invoke();

                throw new Exception(msg);
            }
        }
    }
}
