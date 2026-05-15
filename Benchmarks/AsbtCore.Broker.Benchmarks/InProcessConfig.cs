using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace AsbtCore.Broker.Benchmarks;

/// <summary>
/// Custom config that forces all benchmarks to run in-process via
/// <see cref="InProcessEmitToolchain"/>. This avoids the BenchmarkDotNet
/// limitation where duplicate <c>.csproj</c> filenames in the repository
/// tree (e.g., git worktrees) cause a <see cref="System.NotSupportedException"/>.
/// </summary>
public sealed class InProcessConfig : ManualConfig
{
    public InProcessConfig()
    {
        AddJob(Job.MediumRun
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithId("InProcess"));
        AddColumnProvider(DefaultConfig.Instance.GetColumnProviders().ToArray());
        AddExporter(DefaultConfig.Instance.GetExporters().ToArray());
        AddLogger(DefaultConfig.Instance.GetLoggers().ToArray());
        AddDiagnoser(DefaultConfig.Instance.GetDiagnosers().ToArray());
        AddValidator(DefaultConfig.Instance.GetValidators().ToArray());
    }
}

/// <summary>
/// Applies <see cref="InProcessConfig"/> to a benchmark class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class InProcessJobAttribute : Attribute, IConfigSource
{
    public IConfig Config { get; } = new InProcessConfig();
}
