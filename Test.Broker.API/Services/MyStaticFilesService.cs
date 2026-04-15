namespace AsbtCore.Broker.API.Services
{
    public static class MyStaticFilesService
    {
        public static void UseMyStaticFiles(this WebApplication app)
        {
            var options = new DefaultFilesOptions();
            options.DefaultFileNames.Clear();
            options.DefaultFileNames.Add("index.html");
            app.UseDefaultFiles(options);
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
        }
    }
}
