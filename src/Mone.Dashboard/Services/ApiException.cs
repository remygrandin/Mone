using System.Net;

namespace Mone.Dashboard.Services;

/// <summary>Thrown when the API returns a non-success status for a request the caller should handle.</summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public ApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>Thrown when the API returns 403 Forbidden — the user lacks the required permission/scope.</summary>
public sealed class ApiForbiddenException : ApiException
{
    public ApiForbiddenException(string? message = null)
        : base(HttpStatusCode.Forbidden, message ?? "You do not have permission to perform this action.")
    {
    }
}
