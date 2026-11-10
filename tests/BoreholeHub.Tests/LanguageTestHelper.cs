using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BoreholeHub.Tests;

/// <summary>Helpers for tests that need the UI in English. The language is set through the real POST /Sprache.</summary>
public static class LanguageTestHelper
{
    /// <summary>Returns a client whose cookie jar already holds the "bh-lang" cookie for English.</summary>
    public static async Task<HttpClient> CreateEnglishClientAsync(WebAppFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string token = await GetAntiForgeryTokenAsync(client);

        var response = await client.PostAsync("/Sprache", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["lang"] = "en",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    /// <summary>Reads the antiforgery token from the language switch in the layout of the start page.</summary>
    public static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        var input = Regex.Match(html, "<input[^>]*__RequestVerificationToken[^>]*>").Value;
        return Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value;
    }
}
