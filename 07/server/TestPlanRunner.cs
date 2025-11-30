using System.Diagnostics;

namespace TestPlanExecutor;

public class TestPlanRunner
{
    private const string DefaultTarget = "https://news.google.com";
    private readonly HttpClient _httpClient;

    public TestPlanRunner(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IReadOnlyList<TestPlanInfo> ListPlans() => new[]
    {
        new TestPlanInfo(
            Name: "google-news",
            Title: "Erreichbarkeit Google News pruefen",
            Description: "GET auf Google News, Redirects folgen, Status/Content pruefen.",
            DefaultTarget: DefaultTarget,
            TargetEnvironmentVariable: "TESTPLAN_TARGET_URL")
    };

    public async Task<TestPlanResult> RunAsync(string planName, CancellationToken cancellationToken = default)
    {
        return planName switch
        {
            "google-news" => await RunGoogleNewsAsync(cancellationToken),
            _ => new TestPlanResult(
                PlanName: planName,
                Status: "not-found",
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                Steps: new[]
                {
                    new TestStepResult("validate-plan", "failed", $"Unbekannter Plan: {planName}", 0)
                },
                Target: null,
                Summary: "Plan nicht gefunden.")
        };
    }

    private async Task<TestPlanResult> RunGoogleNewsAsync(CancellationToken cancellationToken)
    {
        var steps = new List<TestStepResult>();
        var started = DateTimeOffset.UtcNow;
        var target = ResolveTarget();

        Uri? targetUri = null;
        HttpResponseMessage? response = null;
        string? body = null;

        try
        {
            await AddStepAsync("resolve-target", steps, () =>
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
                {
                    throw new InvalidOperationException($"Ungueltige URL: {target}");
                }

                if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                {
                    throw new InvalidOperationException("Nur http/https sind erlaubt.");
                }

                targetUri = uri;
                return Task.FromResult($"Target={uri}");
            });
        }
        catch
        {
            targetUri = null;
        }

        if (targetUri is not null)
        {
            try
            {
                await AddStepAsync("http-get", steps, async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
                    request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
                    request.Headers.UserAgent.ParseAdd("mcp-testplan/1.0");

                    var result = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                    var status = (int)result.StatusCode;

                    response = result;
                    if (status >= 500)
                    {
                        throw new HttpRequestException($"Remote Fehler {status}.");
                    }

                    return $"Status {(int)result.StatusCode} {result.ReasonPhrase}";
                });
            }
            catch
            {
                response = null;
            }
        }

        if (response is not null)
        {
            try
            {
                await AddStepAsync("status-check", steps, () =>
                {
                    var status = (int)response.StatusCode;
                    if (status is >= 200 and < 400)
                    {
                        return Task.FromResult($"Status ok ({status}).");
                    }

                    throw new InvalidOperationException($"Unerwarteter Status {status}.");
                });
            }
            catch
            {
            }
        }

        if (response is not null)
        {
            try
            {
                body = await AddStepAsync("content-fetch", steps, async () =>
                {
                    var text = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (text.Length > 12000)
                    {
                        text = text[..12000] + "...(kuerzt)";
                    }

                    return text;
                });
            }
            catch
            {
                body = null;
            }
        }

        if (body is not null)
        {
            try
            {
                await AddStepAsync("content-check", steps, () =>
                {
                    var hasTitle = body.Contains("Google News", StringComparison.OrdinalIgnoreCase);
                    var hasNewsWord = body.Contains("News", StringComparison.OrdinalIgnoreCase);

                    if (hasTitle || hasNewsWord)
                    {
                        return Task.FromResult("Content enthaelt News/Google News.");
                    }

                    throw new InvalidOperationException("Kein Hinweis auf News/Google News gefunden.");
                });
            }
            catch
            {
            }
        }

        var finished = DateTimeOffset.UtcNow;
        var success = steps.All(s => s.Status == "success");
        var summary = success
            ? "Plan erfolgreich: Google News erreichbar und Inhalte gefunden."
            : "Plan fehlgeschlagen: Mindestens ein Schritt schlug fehl.";

        return new TestPlanResult(
            PlanName: "google-news",
            Status: success ? "passed" : "failed",
            StartedAt: started,
            CompletedAt: finished,
            Steps: steps,
            Target: targetUri?.ToString(),
            Summary: summary);
    }

    private static string ResolveTarget()
    {
        var overrideTarget = Environment.GetEnvironmentVariable("TESTPLAN_TARGET_URL");
        if (!string.IsNullOrWhiteSpace(overrideTarget))
        {
            return overrideTarget.Trim();
        }

        return DefaultTarget;
    }

    private static async Task<T> AddStepAsync<T>(
        string name,
        ICollection<TestStepResult> steps,
        Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            sw.Stop();
            steps.Add(new TestStepResult(name, "success", SafeDetails(result), sw.ElapsedMilliseconds));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var detail = ex switch
            {
                HttpRequestException httpEx when httpEx.StatusCode.HasValue => $"HTTP {(int)httpEx.StatusCode.Value}: {httpEx.Message}",
                _ => ex.Message
            };
            steps.Add(new TestStepResult(name, "failed", detail, sw.ElapsedMilliseconds));
            throw;
        }
    }

    private static string SafeDetails(object? value)
    {
        if (value is null) return "(leer)";
        var text = value.ToString() ?? "(leer)";
        return text.Length > 400 ? text[..400] + "...(kuerzt)" : text;
    }
}

public sealed record TestPlanInfo(
    string Name,
    string Title,
    string Description,
    string DefaultTarget,
    string TargetEnvironmentVariable);

public sealed record TestPlanResult(
    string PlanName,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<TestStepResult> Steps,
    string? Target,
    string Summary);

public sealed record TestStepResult(
    string Name,
    string Status,
    string Details,
    long DurationMs);
