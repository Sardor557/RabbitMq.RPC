using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

namespace AsbtCore.Broker.API.Services
{
    public static class MyResponseCompressionService
    {
        public static void AddMyResponseCompression(this IServiceCollection services)
        {
            services.AddResponseCompression(options =>
            {
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = new[] { "application/json", "text/tab-separated-values", "application/javascript", "text/csv", "text" };
            });

            services.Configure<BrotliCompressionProviderOptions>(options => { options.Level = CompressionLevel.Fastest; });
        }

    }
}
