namespace Xueqing.Mcp;

public enum XueqingMcpMode
{
    Production,
    Prototype,
}

public sealed record XueqingMcpOptions(
    XueqingMcpMode Mode,
    string? ProfilePath,
    string? DataRoot)
{
    public static XueqingMcpOptions Parse(string[] args)
    {
        var prototype = false;
        string? profilePath = Environment.GetEnvironmentVariable("XUEQING_DEPLOYMENT_PROFILE");
        string? dataRoot = Environment.GetEnvironmentVariable("XUEQING_DATA_ROOT");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--prototype":
                    prototype = true;
                    break;
                case "--profile" when i + 1 < args.Length:
                    profilePath = args[++i];
                    break;
                case "--data-root" when i + 1 < args.Length:
                    dataRoot = args[++i];
                    break;
            }
        }

        return new XueqingMcpOptions(
            prototype ? XueqingMcpMode.Prototype : XueqingMcpMode.Production,
            string.IsNullOrWhiteSpace(profilePath) ? null : Path.GetFullPath(profilePath),
            string.IsNullOrWhiteSpace(dataRoot) ? null : Path.GetFullPath(dataRoot));
    }
}
