using Eshop.Domain.Customers;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Eshop.Api;

/// <summary>
/// The one shape of a refused request: ProblemDetails whose <c>title</c> is the Thai message to show and whose
/// <c>error</c> extension is the kebab-case code the web app (and tests) switch on, plus any extra fields
/// (e.g. <c>reapplyFrom</c>, <c>minimumBid</c>). Field-level form errors use <c>TypedResults.ValidationProblem</c>.
/// </summary>
internal static class Refusal
{
    public static ProblemHttpResult Of(int statusCode, string error, string title, params (string Key, object? Value)[] extra)
    {
        var extensions = new Dictionary<string, object?> { ["error"] = error };
        foreach (var (key, value) in extra)
        {
            extensions[key] = value;
        }

        return TypedResults.Problem(statusCode: statusCode, title: title, extensions: extensions);
    }

    public static ProblemHttpResult BadRequest(string error, string title, params (string Key, object? Value)[] extra) =>
        Of(StatusCodes.Status400BadRequest, error, title, extra);

    public static ProblemHttpResult Conflict(string error, string title, params (string Key, object? Value)[] extra) =>
        Of(StatusCodes.Status409Conflict, error, title, extra);

    /// <summary>A wrong, expired or locked SMS code (sign-in and Offer acceptance).</summary>
    public static ProblemHttpResult SmsCode(SmsCodeCheck check) =>
        check switch
        {
            SmsCodeCheck.Expired => BadRequest("code-expired", "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่"),
            SmsCodeCheck.Locked => BadRequest("code-locked", "ใส่รหัสผิดเกินจำนวนครั้งที่กำหนด กรุณาขอรหัสใหม่"),
            _ => BadRequest("code-invalid", "รหัสไม่ถูกต้อง"),
        };
}
