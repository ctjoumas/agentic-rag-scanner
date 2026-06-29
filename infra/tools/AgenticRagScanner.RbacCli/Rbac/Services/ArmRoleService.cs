using System.Text.RegularExpressions;

namespace AgenticRagScanner.RbacCli.Rbac.Services;

internal sealed partial class ArmRoleService(RbacExecutionContext context)
{
    private readonly RbacExecutionContext _context = context;

    public void AssignArmRole(string role, string scope, string principalId, string resourceLabel, bool assigneeIsObjectId = false)
    {
        string assigneeFlag = assigneeIsObjectId ? "--assignee-object-id" : "--assignee";
        string roleForAssignment = ResolveRoleForAssignment(role);

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(
            [
                "az",
                "role",
                "assignment",
                "list",
                assigneeFlag,
                principalId,
                "--role",
                roleForAssignment,
                "--scope",
                scope,
                "--query",
                "[].id",
                "-o",
                "json",
            ]);

        if (Ok && Json.HasValue && Json.Value.ValueKind == System.Text.Json.JsonValueKind.Array && Json.Value.GetArrayLength() > 0)
        {
            RbacExecutionContext.PrintWarning($"'{role}' already assigned on {resourceLabel}");
            return;
        }

        List<string> createArgs =
        [
            "az",
            "role",
            "assignment",
            "create",
            assigneeFlag,
            principalId,
            "--role",
            roleForAssignment,
            "--scope",
            scope,
        ];

        if (assigneeIsObjectId)
        {
            createArgs.Add("--assignee-principal-type");
            createArgs.Add(_context.PrincipalType);
        }

        var create = RbacExecutionContext.RunJson(createArgs);
        if (create.Ok)
        {
            RbacExecutionContext.PrintSuccess($"Assigned '{role}' on {resourceLabel}");
        }
        else
        {
            RbacExecutionContext.PrintError($"Failed to assign '{role}' on {resourceLabel}: {create.Message}");
        }
    }

    private static bool IsGuid(string value)
    {
        return MyRegex().IsMatch(value);
    }

    private string ResolveRoleForAssignment(string role)
    {
        if (IsGuid(role))
        {
            return role;
        }

        var (Ok, Json, Message) = RbacExecutionContext.RunJson(["az", "role", "definition", "list", "--name", role, "-o", "json"]);
        if (!Ok || !Json.HasValue || Json.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return role;
        }

        var first = Json.Value.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return role;
        }

        string? roleId = first.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        return !string.IsNullOrWhiteSpace(roleId) && IsGuid(roleId) ? roleId : role;
    }

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex MyRegex();
}

