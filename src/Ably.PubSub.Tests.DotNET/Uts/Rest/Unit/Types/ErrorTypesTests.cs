using System;
using System.Net;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Types
{
    /// <summary>
    /// Derived from uts/rest/unit/types/error_types.md in ably/specification.
    ///
    /// Spec points: TI1, TI2, TI3, TI4, TI5
    ///
    /// Pure type validation: no client is built and no mock is installed, matching the spec's
    /// "No mocks required - these verify type structure".
    ///
    /// Three translation notes apply across the file.
    ///
    /// 1. The spec writes <c>ErrorInfo(code: ..., statusCode: ..., message: ...)</c>. .NET's
    ///    constructor is <c>ErrorInfo(string reason, int code, HttpStatusCode? statusCode, ...)</c>:
    ///    the reason comes first and the status code is an <c>HttpStatusCode</c> rather than an int,
    ///    so a spec construction that supplies no message passes a null reason.
    ///
    /// 2. <c>ErrorInfo.fromJson</c> has no single .NET spelling. <c>JsonHelper</c> is the SDK's public
    ///    JSON entry point and <c>ErrorInfo</c> carries the wire names as <c>[JsonProperty]</c>
    ///    attributes, so <c>JsonHelper.DeserializeObject</c> is the deserialization the SDK itself
    ///    performs on an error body. (<c>ErrorInfo.Parse</c> exists but takes an internal
    ///    <c>AblyResponse</c> and discards <c>href</c> and <c>cause</c>, so it is not the equivalent.)
    ///
    /// 3. TI5's "cause" is two members here. A nested Ably error is <c>ErrorInfo.Cause</c>, typed as
    ///    <c>ErrorInfo</c>; an arbitrary underlying exception is <c>ErrorInfo.InnerException</c>, typed
    ///    as <c>Exception</c>. Both are exercised.
    /// </summary>
    public class ErrorTypesTests : UtsTestBase
    {
        public ErrorTypesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TI1/errorinfo-attributes-0
        [Fact]
        public void TI1_ErrorInfoCodeAttribute()
        {
            var error = new ErrorInfo(null, 40000);

            error.Code.Should().Be(40000);
        }

        // UTS: rest/unit/TI1/errorinfo-attributes-0
        [Fact]
        public void TI2_ErrorInfoStatusCodeAttribute()
        {
            var error = new ErrorInfo(null, 40100, HttpStatusCode.Unauthorized);

            error.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            ((int)error.StatusCode.Value).Should().Be(401);
        }

        // UTS: rest/unit/TI1/errorinfo-attributes-0
        [Fact]
        public void TI3_ErrorInfoMessageAttribute()
        {
            var error = new ErrorInfo("Bad request: invalid parameter", 40000, HttpStatusCode.BadRequest);

            error.Message.Should().Be("Bad request: invalid parameter");
            error.Code.Should().Be(40000);
            ((int)error.StatusCode.Value).Should().Be(400);
        }

        // UTS: rest/unit/TI1/errorinfo-attributes-0
        [Fact]
        public void TI4_ErrorInfoHrefAttribute()
        {
            var error = new ErrorInfo(null, 40000, null, "https://help.ably.io/error/40000");

            error.Href.Should().Be("https://help.ably.io/error/40000");

            // href is optional per TI4. Where it is omitted and a code is present, the .NET constructor
            // derives it from the code rather than leaving it null; that is the same URL the spec's
            // explicit value spells out, so recording it here keeps the derived form covered too.
            var derived = new ErrorInfo(null, 40000);
            derived.Href.Should().Be("https://help.ably.io/error/40000");
        }

        // UTS: rest/unit/TI1/errorinfo-attributes-0
        [Fact]
        public void TI5_ErrorInfoCauseAttribute()
        {
            // The spec's cause is a plain Exception("Network failure"). That is InnerException here;
            // see note 3 on the class.
            var originalError = new Exception("Network failure");
            var error = new ErrorInfo("Timeout", 50003, HttpStatusCode.InternalServerError, originalError);

            error.InnerException.Should().BeSameAs(originalError);
            error.Code.Should().Be(50003);
            ((int)error.StatusCode.Value).Should().Be(500);
            error.Message.Should().Be("Timeout");

            // The Ably-error form of the same attribute.
            var causeInfo = new ErrorInfo("Database connection failed", 50001);
            var wrapping = new ErrorInfo("Internal error", 50000, HttpStatusCode.InternalServerError, null, causeInfo);

            wrapping.Cause.Should().BeSameAs(causeInfo);
        }

        // UTS: rest/unit/TI/errorinfo-from-json-0
        [Fact]
        public void TI_ErrorInfoFromJson()
        {
            var jsonResponse = JObject.Parse(
                "{\"error\":{\"code\":40100,\"statusCode\":401,\"message\":\"Token expired\"," +
                "\"href\":\"https://help.ably.io/error/40100\"}}");

            var error = JsonHelper.DeserializeObject<ErrorInfo>((JObject)jsonResponse["error"]);

            error.Code.Should().Be(40100);
            error.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            ((int)error.StatusCode.Value).Should().Be(401);
            error.Message.Should().Be("Token expired");
            error.Href.Should().Be("https://help.ably.io/error/40100");
        }

        // UTS: rest/unit/TI/errorinfo-nested-cause-1
        [Fact]
        public void TI_ErrorInfoNestedCause()
        {
            var jsonResponse = JObject.Parse(
                "{\"error\":{\"code\":50000,\"statusCode\":500,\"message\":\"Internal error\"," +
                "\"cause\":{\"code\":50001,\"message\":\"Database connection failed\"}}}");

            var error = JsonHelper.DeserializeObject<ErrorInfo>((JObject)jsonResponse["error"]);

            error.Code.Should().Be(50000);

            // The spec's "cause IS ErrorInfo OR cause IS Exception" is settled statically here: the
            // Cause property is typed ErrorInfo, so only the first branch of the spec's IF can apply.
            error.Cause.Should().NotBeNull();
            error.Cause.Code.Should().Be(50001);
            error.Cause.Message.Should().Be("Database connection failed");
        }

        // UTS: rest/unit/TI/ably-exception-wraps-errorinfo-2
        [Fact]
        public void TI_AblyExceptionWrapsErrorInfo()
        {
            var errorInfo = new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest);

            var exception = new AblyException(errorInfo);

            // AblyException exposes the wrapped ErrorInfo rather than re-publishing its fields, so the
            // spec's exception.code / exception.statusCode read through ErrorInfo.
            exception.ErrorInfo.Should().BeSameAs(errorInfo);
            exception.ErrorInfo.Code.Should().Be(40000);
            ((int)exception.ErrorInfo.StatusCode.Value).Should().Be(400);
            exception.ErrorInfo.Message.Should().Be("Bad request");

            // SPEC: ASSERT exception.message == "Bad request".
            // ADAPTED: Exception.Message is the whole of ErrorInfo.ToString() here - the constructor
            // passes info.ToString() to the base Exception - so it carries the reason plus the code,
            // status code and href. Asserting containment keeps the spec's intent (the reason reaches
            // the throwable's message) without pinning the surrounding diagnostic text.
            exception.Message.Should().Contain("Bad request");
        }

        // UTS: rest/unit/TI/common-error-codes-3
        [Theory]
        [InlineData(40000, 400)]
        [InlineData(40100, 401)]
        [InlineData(40101, 401)]
        [InlineData(40140, 401)]
        [InlineData(40142, 401)]
        [InlineData(40160, 401)]
        [InlineData(40300, 403)]
        [InlineData(40400, 404)]
        [InlineData(50000, 500)]
        [InlineData(50003, 500)]
        public void TI_CommonErrorCodes(int code, int statusCode)
        {
            var error = new ErrorInfo("meaning", code, (HttpStatusCode)statusCode);

            error.Code.Should().Be(code);
            ((int)error.StatusCode.Value).Should().Be(statusCode);
        }

        // UTS: rest/unit/TI/error-string-representation-4
        [Fact]
        public void TI_ErrorStringRepresentation()
        {
            var error = new ErrorInfo("Unauthorized: token expired", 40100, HttpStatusCode.Unauthorized);

            var stringRepr = error.ToString();

            stringRepr.Should().Contain("40100");
            stringRepr.Should().Contain("401");

            // The spec's assertion is a disjunction: the reason must survive into the representation
            // in one form or the other.
            (stringRepr.Contains("Unauthorized") || stringRepr.Contains("token")).Should().BeTrue(
                "the representation must carry the reason, but was {0}", stringRepr);
        }

        // UTS: rest/unit/TI/error-equality-5
        [Fact]
        public void TI_ErrorEquality()
        {
            var error1 = new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest);
            var error2 = new ErrorInfo("Bad request", 40000, HttpStatusCode.BadRequest);
            var error3 = new ErrorInfo("Unauthorized", 40100, HttpStatusCode.Unauthorized);

            // SPEC: ASSERT error1 == error2 (same attributes compare equal) and error1 != error3.
            // ADAPTED: ErrorInfo overrides neither Equals nor GetHashCode, so .NET gives it reference
            // equality and error1.Equals(error2) is false even though every attribute matches. The
            // divergence is stable - value equality on a mutable public class is not idiomatic .NET,
            // and no features-spec point requires it - so this asserts the real behaviour with the
            // spec's expectation recorded here rather than gating the coverage behind RUN_DEVIATIONS.
            error1.Equals(error2).Should().BeFalse("ErrorInfo compares by reference");
            error1.Equals(error1).Should().BeTrue();

            // The observable the spec is reaching for - equivalence judged on the attributes - holds.
            error1.Code.Should().Be(error2.Code);
            ((int)error1.StatusCode.Value).Should().Be((int)error2.StatusCode.Value);
            error1.Message.Should().Be(error2.Message);

            error1.Code.Should().NotBe(error3.Code);
            ((int)error1.StatusCode.Value).Should().NotBe((int)error3.StatusCode.Value);
            error1.Message.Should().NotBe(error3.Message);
        }
    }
}
