param([string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/nuget'))

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'mGO.Log.IntegrationTests/obj/trace-package-smoke'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null
$packageSource = [IO.Path]::GetFullPath($PackageDirectory)
$project = Join-Path $workspace 'TraceConsumer.csproj'
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <DefaultItemExcludes>$(DefaultItemExcludes);packages/**</DefaultItemExcludes>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="mGO.Log.Extensions.Hosting" Version="1.0.0" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath $project
$program = @'
using mGO.Log.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Trace = mGO.Log.Attributes.Trace;

var logs = Path.Combine(AppContext.BaseDirectory, "logs-" + Guid.NewGuid().ToString("N"));
var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddGOLog(o => { o.ApplicationName = "PackageConsumer"; o.MinimumLevel = LogLevel.Debug; o.File.Directory = logs; });
using var host = builder.Build();
await host.StartAsync();
var worker = new Worker(host.Services.GetRequiredService<ILogger<Worker>>());
if (await worker.WorkAsync() != 42) throw new Exception("Result changed");
await host.StopAsync();
var entries = Directory.GetFiles(logs, "*.log").SelectMany(File.ReadAllLines).Select(s => JsonSerializer.Deserialize<JsonElement>(s)).ToArray();
var own = entries.Where(e => e.GetProperty("Category").GetString() == "Worker").ToArray();
if (own.Count(e => e.GetProperty("EventKind").GetString() == "Enter") != 2 || own.Count(e => e.GetProperty("EventKind").GetString() == "Leave") != 2)
    throw new Exception("NuGet decorator instrumentation did not run");
Console.WriteLine("NuGet consumer verified: nested Debug/Information decorators emitted four events.");
internal sealed class Worker(ILogger<Worker> logger)
{
    private readonly ILogger<Worker> _logger = logger;
    [Trace.Debug]
    public async Task<int> WorkAsync() { await Task.Yield(); return Complete(); }
    [Trace.Information]
    private int Complete() => 42;
}
'@
$sourcePath = Join-Path $workspace 'Program.cs'
$program | Set-Content -LiteralPath $sourcePath
$cache = Join-Path $workspace ('packages/' + [Guid]::NewGuid().ToString('N'))
$properties = @('-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false', "-p:RestorePackagesPath=$cache")
$config = Join-Path $workspace 'NuGet.Config'
$escapedSource = [System.Security.SecurityElement]::Escape($packageSource)
@"
<configuration><packageSources><clear /><add key="Local" value="$escapedSource" /><add key="NuGet" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
"@ | Set-Content -LiteralPath $config
dotnet restore $project --configfile $config @properties
if ($LASTEXITCODE -ne 0) { throw 'Package consumer restore failed.' }
dotnet build $project --configuration Release --no-restore @properties
if ($LASTEXITCODE -ne 0) { throw 'Package consumer build failed.' }
dotnet (Join-Path $workspace 'bin/Release/net8.0/TraceConsumer.dll')
if ($LASTEXITCODE -ne 0) { throw 'Package consumer did not execute the decorated methods correctly.' }

# Confirm unsupported declarations fail explicitly instead of silently accepting inert attributes.
($program + "`ninternal abstract class Unsupported { [Trace.Debug] public abstract void Execute(); }") | Set-Content -LiteralPath $sourcePath
$negativeOutput = & dotnet build $project --configuration Release --no-restore @properties 2>&1 | Out-String
if ($LASTEXITCODE -eq 0 -or $negativeOutput -notmatch 'GOLOGTRACE001') { throw "Expected GOLOGTRACE001 for an abstract method: $negativeOutput" }
$program | Set-Content -LiteralPath $sourcePath
Write-Output 'Negative build verified: methods without a body are rejected with GOLOGTRACE001.'
exit 0
