using System.ComponentModel.DataAnnotations;

namespace AsbtCore.Broker.Core.Options
{
    public sealed class RpcOptions
    {
        [Required]
        public string HostName { get; set; }

        [Range(1, 65535)]
        public int Port { get; set; }

        [Required]
        public string VirtualHost { get; set; }

        [Required]
        public string UserName { get; set; }

        [Required]
        public string Password { get; set; }

        [Required]
        public string ClientProvidedName { get; set; }

        [Range(1, ushort.MaxValue)]
        public ushort PrefetchCount { get; set; } = 1;


        [Required]
        public string RoutePrefix { get; set; } = "rpc.";

        [Range(1, 3600)]
        public int DefaultTimeoutSeconds { get; set; } = 30;

    }
}
