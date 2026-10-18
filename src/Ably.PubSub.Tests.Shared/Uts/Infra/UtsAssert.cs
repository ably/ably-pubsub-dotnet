using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit.Sdk;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// Assertions the pseudocode needs and FluentAssertions doesn't have, plus the caller-attributed
    /// failure every UTS helper raises.
    /// </summary>
    public static class UtsAssert
    {
        /// <summary>
        /// The specs' <c>ASSERT observed CONTAINS_IN_ORDER [a, b, c]</c>: every expected item appears in
        /// <paramref name="observed"/>, in this order, with anything else allowed in between. A
        /// subsequence, not a contiguous run and not an exact filtered list: an expected item that
        /// occurs twice in <paramref name="observed"/> is fine.
        /// </summary>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="observed">What was recorded.</param>
        /// <param name="expected">The subsequence that must appear.</param>
        /// <param name="callerFilePath">Filled in by the compiler.</param>
        /// <param name="callerLineNumber">Filled in by the compiler.</param>
        public static void ContainsInOrder<T>(
            IEnumerable<T> observed,
            IEnumerable<T> expected,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            var observedList = observed.ToList();
            var expectedList = expected.ToList();
            var matched = 0;
            foreach (var item in observedList)
            {
                if (matched < expectedList.Count && EqualityComparer<T>.Default.Equals(item, expectedList[matched]))
                {
                    matched++;
                }
            }

            if (matched != expectedList.Count)
            {
                throw Failure(
                    $"expected [{string.Join(", ", observedList)}] to contain in order " +
                    $"[{string.Join(", ", expectedList)}], but only the first {matched} matched",
                    callerFilePath,
                    callerLineNumber);
            }
        }

        /// <summary>The specs' <c>FAIL("msg")</c>, attributed to the caller.</summary>
        /// <param name="message">Why the test fails.</param>
        /// <param name="callerFilePath">Filled in by the compiler.</param>
        /// <param name="callerLineNumber">Filled in by the compiler.</param>
        public static void Fail(
            string message,
            [CallerFilePath] string callerFilePath = "",
            [CallerLineNumber] int callerLineNumber = 0)
        {
            throw Failure(message, callerFilePath, callerLineNumber);
        }

        /// <summary>
        /// Builds the exception every UTS helper throws: an xUnit failure whose message starts with the
        /// calling test's <c>file:line</c>, so a timeout inside a helper points at the test line that
        /// asked for the wait, not at the helper.
        /// </summary>
        internal static XunitException Failure(
            string message,
            string callerFilePath,
            int callerLineNumber,
            Exception inner = null)
        {
            var location = string.IsNullOrEmpty(callerFilePath)
                ? string.Empty
                : $"{System.IO.Path.GetFileName(callerFilePath)}:{callerLineNumber}: ";
            return inner == null
                ? new XunitException(location + message)
                : new UtsHelperException(location + message, inner);
        }

        /// <summary>A caller-attributed failure that keeps the error that caused it.</summary>
        public sealed class UtsHelperException : XunitException
        {
            public UtsHelperException(string message, Exception inner)
                : base(message + Environment.NewLine + "Cause: " + inner.GetType().Name + ": " + inner.Message)
            {
                Cause = inner;
            }

            /// <summary>The error that made the helper fail, for example the last error of a poll.</summary>
            public Exception Cause { get; }
        }
    }
}
