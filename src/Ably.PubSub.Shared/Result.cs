using System;

namespace IO.Ably
{
    /// <summary>
    /// A unit type is a type that allows only one value (and thus can hold no information)
    /// Original source
    /// https://github.com/louthy/language-ext/blob/0eb922bf9ca33944a5aa0745d791255321a4b351/LanguageExt.Core/DataTypes/Unit/Unit.cs.
    /// </summary>
    [Serializable]
    public struct Unit : IEquatable<Unit>, IComparable<Unit>
    {
#pragma warning disable SA1600 // Elements should be documented
#pragma warning disable SA1129 // Do not use default value constructor
        /// <summary>
        /// Default value.
        /// </summary>
        public static readonly Unit Default = new Unit();

        /// <inheritdoc />
        public override int GetHashCode() =>
            0;

        /// <inheritdoc />
        public override bool Equals(object obj) =>
            obj is Unit;

        /// <summary>
        /// Returns ().
        /// </summary>
        /// <returns>().</returns>
        public override string ToString() =>
            "()";

        public bool Equals(Unit other) =>
            true;

        public static bool operator ==(Unit lhs, Unit rhs) =>
            true;

        public static bool operator !=(Unit lhs, Unit rhs) =>
            false;

        public static bool operator >(Unit lhs, Unit rhs) =>
            false;

        public static bool operator >=(Unit lhs, Unit rhs) =>
            true;

        public static bool operator <(Unit lhs, Unit rhs) =>
            false;

        public static bool operator <=(Unit lhs, Unit rhs) =>
            true;

        public int CompareTo(Unit other) =>
            0;

        public static Unit operator +(Unit a, Unit b) =>
            Default;
#pragma warning restore SA1600 // Elements should be documented
#pragma warning restore SA1129 // Do not use default value constructor
    }

    /// <summary>
    /// Result class representing the result of an operation.
    /// </summary>
    public class Result
    {
        /// <summary>
        /// Is the operation successful.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// Error if the operation failed.
        /// </summary>
        public ErrorInfo Error { get; }

        /// <summary>
        /// Has the operation failed.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// Initializes a new instance of the <see cref="Result"/> class.
        /// </summary>
        /// <param name="isSuccess">true if success.</param>
        /// <param name="error">error if failure.</param>
        /// <exception cref="InvalidOperationException">for invalid parameters.</exception>
        protected Result(bool isSuccess, ErrorInfo error)
        {
            if (isSuccess && error != null)
            {
                throw new InvalidOperationException();
            }

            if (!isSuccess && error == null)
            {
                throw new InvalidOperationException();
            }

            IsSuccess = isSuccess;
            Error = error;
        }

        /// <summary>
        /// Factory method to create a failed Result with message.
        /// </summary>
        /// <param name="message">error message.</param>
        /// <returns>Result.</returns>
        public static Result Fail(string message)
        {
            return new Result(false, new ErrorInfo { Message = message });
        }

        /// <summary>
        /// Factory method to create a failed Result with Error.
        /// </summary>
        /// <param name="error">Error.</param>
        /// <returns>true / false.</returns>
        public static Result Fail(ErrorInfo error)
        {
            return new Result(false, error);
        }

        /// <summary>
        /// Factory method to create a failed Result of T.
        /// </summary>
        /// <typeparam name="T">Type of value.</typeparam>
        /// <param name="error">Error of the failure.</param>
        /// <returns>failed Result of T.</returns>
        public static Result<T> Fail<T>(ErrorInfo error)
        {
            return new Result<T>(default(T), false, error);
        }

        /// <summary>
        /// Factory method to create a failed Result of T.
        /// </summary>
        /// <typeparam name="T">Type of value.</typeparam>
        /// <param name="other">Creates a failed result from another.</param>
        /// <returns>failed Result of T.</returns>
        public static Result<T> Fail<T>(Result other)
        {
            if (other.IsSuccess || other.Error == null)
            {
                throw new InvalidOperationException();
            }

            return Fail<T>(other.Error);
        }

        /// <summary>
        /// Factory method to create a successful Result.
        /// </summary>
        /// <returns>Result.</returns>
        public static Result Ok()
        {
            return new Result(true, null);
        }

       /// <summary>
       /// Factory method to create a successful Result of T with value.
       /// </summary>
       /// <typeparam name="T">Type of value.</typeparam>
       /// <param name="value">successful value held in the result.</param>
       /// <returns>Result.</returns>
        public static Result<T> Ok<T>(T value)
        {
            return new Result<T>(value, true, null);
        }

        /// <summary>
        /// Combines a number of results. If any of them has failed it returns failure otherwise
        /// returns a successful result.
        /// </summary>
        /// <param name="results">Results to check.</param>
        /// <returns>Result.</returns>
        public static Result Combine(params Result[] results)
        {
            foreach (Result result in results)
            {
                if (result.IsFailure)
                {
                    return result;
                }
            }

            return Ok();
        }
    }

    /// <summary>
    /// Class representing a <see cref="Result"/> containing a value.
    /// </summary>
    /// <typeparam name="T">Type of value.</typeparam>
    public class Result<T> : Result
    {
        private readonly T _value;

        /// <summary>
        /// When successful exposes the value
        /// held in the result object.
        /// </summary>
        public T Value
        {
            get
            {
                if (!IsSuccess)
                {
                    throw new InvalidOperationException();
                }

                return _value;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Result{T}"/> class.
        /// </summary>
        /// <param name="value">value.</param>
        /// <param name="isSuccess">if successful.</param>
        /// <param name="error">Error object.</param>
        protected internal Result(T value, bool isSuccess, ErrorInfo error)
            : base(isSuccess, error)
        {
            _value = value;
        }
    }

    /// <summary>
    /// Extension methods to help with chaining result calls.
    /// </summary>
    public static class ResultExtension
    {
        /// <summary>
        /// Map takes a result and if successful executes the mapper function on the value.
        /// </summary>
        /// <param name="initial">Initial Result of T.</param>
        /// <param name="mapper">Mapper function that transforms the value.</param>
        /// <typeparam name="T">Initial type of the Value wrapped in the Result.</typeparam>
        /// <typeparam name="TResult">The new type returned from the mapper function.</typeparam>
        /// <returns>a new result.</returns>
        public static Result<TResult> Map<T, TResult>(this Result<T> initial, Func<T, TResult> mapper)
        {
            if (initial.IsSuccess)
            {
                return Result.Ok(mapper(initial.Value));
            }

            return Result.Fail<TResult>(initial);
        }

        /// <summary>
        /// Helper method for Result of T. It will execute the provided action if
        /// the result is successful.
        /// </summary>
        /// <param name="result">The Result we check.</param>
        /// <param name="successFunc">The action that is executed when the result is successful.</param>
        /// <typeparam name="T">Type of Value wrapped by the Result.</typeparam>
        /// <returns>The original passed object to facilitate chaining.</returns>
        public static Result<T> IfSuccess<T>(this Result<T> result, Action<T> successFunc)
        {
            if (result.IsSuccess)
            {
                successFunc(result.Value);
            }

            return result;
        }

        /// <summary>
        /// Helper method for Result. It is only executed if the result is failed and passed the ErrorInfo
        /// to the Action.
        /// </summary>
        /// <param name="result">the Result we check.</param>
        /// <param name="failAction">the action that is executed in case of a failure.</param>
        /// <returns>the same Result object to facilitate chaining.</returns>
        public static Result IfFailure(this Result result, Action<ErrorInfo> failAction)
        {
            if (result.IsFailure)
            {
                failAction(result.Error);
            }

            return result;
        }

        /// <summary>
        /// Map takes a result and if successful executes the mapper function on the value.
        /// </summary>
        /// <param name="initial">Initial Result of T.</param>
        /// <param name="bindFunc">Bind function that takes the value and returns a new Result.</param>
        /// <typeparam name="T">Initial type of the Value wrapped in the Result.</typeparam>
        /// <typeparam name="TResult">The new type returned from the bind function.</typeparam>
        /// <returns>a new result.</returns>
        public static Result<TResult> Bind<T, TResult>(this Result<T> initial, Func<T, Result<TResult>> bindFunc)
        {
            if (initial.IsSuccess)
            {
                return bindFunc(initial.Value);
            }

            return Result.Fail<TResult>(initial);
        }
    }
}
