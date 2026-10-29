using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sonrisa.Application.Abstractions;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Api.Controllers;

/// <summary>
/// The local stand-in for a Slack incoming webhook (spec §2.6, T63). Anonymous like a real webhook, and served only
/// when <c>Channels:Slack:MockEnabled</c> is on. The delivery worker posts to it from inside the Compose network.
/// <list type="bullet">
/// <item><c>POST /mock/slack/hooks/{name}</c> → 200, the message is stored for the admin view;</item>
/// <item><c>POST /mock/slack/fail</c> → 500 (retry path, AC10);</item>
/// <item><c>POST /mock/slack/reject</c> → 404 (final-failure path, AC10).</item>
/// </list>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("mock/slack")]
public sealed class SlackMockReceiverController(SlackOptions options, ISlackMockStore store,
    ILogger<SlackMockReceiverController> logger) : ControllerBase
{
    /// <summary>A Slack message is small; anything larger is refused.</summary>
    private const int MaxRequestBytes = 40_000;

    [HttpPost("hooks/{name:regex(^[[a-zA-Z0-9_-]]{{1,64}}$)}")]
    public async Task<IActionResult> Receive(string name, CancellationToken ct)
    {
        if (!options.MockEnabled)
        {
            return NotFound();
        }

        if (Request.ContentLength is > MaxRequestBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            var buffer = new char[MaxRequestBytes + 1];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (read > MaxRequestBytes)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            body = new string(buffer, 0, read);
        }

        await store.AddAsync(name, TextOf(body), ct);
        logger.LogInformation("Slack mock: message for hook {Hook} stored ({Length} characters).", name, body.Length);
        return Content("ok");
    }

    /// <summary>
    /// What Slack would show: the <c>text</c> of the JSON payload, so the admin view reads like the channel. A body that
    /// is not such JSON is kept as received.
    /// </summary>
    private static string TextOf(string body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("text", out var text)
                && text.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return text.GetString() ?? body;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not JSON: stored as is.
        }

        return body;
    }

    [HttpPost("fail")]
    public IActionResult Fail() => options.MockEnabled
        ? StatusCode(StatusCodes.Status500InternalServerError, "mock failure")
        : NotFound();

    [HttpPost("reject")]
    public IActionResult Reject() => NotFound();
}
