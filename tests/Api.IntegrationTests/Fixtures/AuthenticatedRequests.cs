using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenDispatch.Contracts.Auth;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Logging in and attaching the token, in one call — every flow test past step 44 needs both,
/// and by step 47 four different test classes had each grown their own copy of
/// <c>LoginAsync</c>. Extracted here rather than left as a fifth.
/// </summary>
internal static class AuthenticatedRequests
{
    /// <summary>Logs in through the real endpoint and returns the token.</summary>
    public static async Task<string> LoginAsync(this HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        return body!.Token;
    }

    /// <summary>Attaches a bearer token to a request, so it reads as one line at the call site.</summary>
    public static HttpRequestMessage Authorized(this HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return request;
    }
}
