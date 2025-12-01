using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TestPlanExecutor;

[McpServerToolType]
public static class TestPlanTools
{
    [McpServerTool(Name = "tests.list")]
    [Description("Listet Testplaene, die von diesem Server ausgefuehrt werden koennen.")]
    public static IEnumerable<object> List(TestPlanRunner runner)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] tests.list aufgerufen");
        return runner.ListPlans().Select(p => new
        {
            p.Name,
            p.Title,
            p.Description,
            defaultTarget = p.DefaultTarget,
            targetEnv = p.TargetEnvironmentVariable
        });
    }

    [McpServerTool(Name = "tests.run")]
    [Description("Fuehrt einen benannten Testplan aus.")]
    public static async Task<TestPlanResult> RunAsync(
        [Description("Name des Testplans, z. B. google-news")] string plan,
        TestPlanRunner runner,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [Tool] tests.run aufgerufen (plan={plan ?? "null"})");
        return await runner.RunAsync(plan, cancellationToken);
    }
}
