using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IO.Ably.Utils
{
    internal static class ActionUtils
    {
        // From unity codebase
        public static void SafeExecute(Action action, ILogger logger = null, string caller = null)
        {
            SafeExecute(action, logger, caller == null ? (Func<string>)null : () => caller);
        }

        public static async Task SafeExecute(Func<Task> action, ILogger logger = null, string caller = null)
        {
            await SafeExecute(action, logger, caller == null ? (Func<string>)null : () => caller);
        }

        private static async Task SafeExecute(Func<Task> action, ILogger logger, Func<string> callerGetter)
        {
            try
            {
                await action();
            }
            catch (Exception exc)
            {
                try
                {
                    if (logger != null)
                    {
                        string caller = null;
                        if (callerGetter != null)
                        {
                            try
                            {
                                caller = callerGetter();
                            }
                            catch (Exception)
                            {
                                // at least we tried to get the caller
                            }
                        }

                        foreach (var e in exc.FlattenAggregate())
                        {
                            logger.Warning($"Ignoring {e.GetType().FullName} exception thrown from an action called by {caller ?? string.Empty}.");
                        }
                    }
                }
                catch (Exception)
                {
                    // now really, really ignore.
                }
            }
        }

        private static void SafeExecute(Action action, ILogger logger, Func<string> callerGetter)
        {
            try
            {
                action();
            }
            catch (Exception exc)
            {
                try
                {
                    if (logger != null)
                    {
                        string caller = null;
                        if (callerGetter != null)
                        {
                            try
                            {
                                caller = callerGetter();
                            }
                            catch (Exception)
                            {
                                // at least we tried to get the caller
                            }
                        }

                        foreach (var e in exc.FlattenAggregate())
                        {
                            logger.Warning($"Ignoring {e.GetType().FullName} exception thrown from an action called by {caller ?? string.Empty}.");
                        }
                    }
                }
                catch (Exception)
                {
                    // now really, really ignore.
                }
            }
        }

        private static List<Exception> FlattenAggregate(this Exception exc)
        {
            var result = new List<Exception>();
            if (exc is AggregateException)
            {
                result.AddRange(exc.InnerException.FlattenAggregate());
            }
            else
            {
                result.Add(exc);
            }

            return result;
        }
    }
}
