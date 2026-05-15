using Client.Runners;

namespace Client;

public class Program
{
    public static async Task Main(string[] args)
    {
        using var host = HostConfiguration.BuildHost(args);
        
        await SmTestDtoServiceRunner.RunAsync(host);
    }
}
