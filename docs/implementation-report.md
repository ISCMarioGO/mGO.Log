# Reporte técnico — GO.Log 1.0.0

Fecha: 16 de octubre de 2026. Repositorio: `GO.Log`. Implementación sobre la raíz existente, sin solución anidada. Se conservó `LICENSE` sin modificaciones. `.gitignore` se amplió para `.vs/` y artefactos de BenchmarkDotNet; los directorios `bin`, `obj`, `logs` y `artifacts` ya estaban excluidos.

## Estructura y proyectos

```text
All.sln
Directory.Build.props
global.json
README.md
CHANGELOG.md
LICENSE
src/
  GO.Log.Abstractions/
    GO.Log.Abstractions.csproj
    Interfaces/              IGOLogSink, IGOLogFormatter
    Models/                  GOLogEntry, GOLogEventKind, GOLogExecutionContext, GOLogOperationContext
  GO.Log/
    GO.Log.csproj
    Configuration/           GOLogOptions, GOLogFileOptions, GOLogQueueOptions, GOLogSecurityOptions, GOLogTextOptions, GOLogConsoleOptions
    Models/                  QueuedLogEntry
    Providers/               GOLogger, GOLoggerProvider
    Queues/                  GOLogQueue
    Processing/              GOLogProcessor
    Sinks/                   GOFileSink, GOConsoleSink
    Formatters/              GOTextFormatter, GOJsonFormatter
    Diagnostics/             GOLogDiagnostics, GOLogOperation
    Extensions/              BeginGOLogOperation
    Security/                GOLogSnapshot
    Exceptions/              GOLogConfigurationException
    Internal/                OptionsSnapshot, GOLogOperationState, GOLogOperationEvent
  GO.Log.Extensions.Hosting/
    GO.Log.Extensions.Hosting.csproj
    Extensions/              AddGOLog
    Configuration/           GOLogOptionsValidator
    Services/                GOLogHostedService, GOLogProviderRegistration
tests/
  GO.Log.Tests/              GO.Log.Tests.csproj
  GO.Log.IntegrationTests/   GO.Log.IntegrationTests.csproj
  GO.Log.Benchmarks/         GO.Log.Benchmarks.csproj
  Get-CoverageSummary.ps1
samples/
  GO.Log.Console.Sample/    GO.Log.Console.Sample.csproj, appsettings, perfil de inicio
  GO.Log.WebApi.Sample/     GO.Log.WebApi.Sample.csproj, appsettings, perfil de inicio
docs/
  architecture.md
  configuration.md
  getting-started.md
  testing.md
  log-styles.md
  implementation-report.md
artifacts/                  generado e ignorado por Git
  nuget/
  startup-final/
  benchmarks-smoke/
  web-sample-smoke/
```

Exactamente ocho proyectos de código fuente, todos `net8.0`, registrados bajo las carpetas lógicas `src`, `tests`, `samples` de `All.sln`. Una sola solución del producto, en formato clásico de Visual Studio 2022. Las herramientas de build y BenchmarkDotNet pueden crear proyectos temporales dentro de directorios ignorados.

## Referencias

| Proyecto | Referencias a proyectos |
|---|---|
| GO.Log.Abstractions | Ninguna |
| GO.Log | GO.Log.Abstractions |
| GO.Log.Extensions.Hosting | GO.Log |
| GO.Log.Tests | GO.Log |
| GO.Log.IntegrationTests | GO.Log.Extensions.Hosting |
| GO.Log.Benchmarks | GO.Log |
| GO.Log.Console.Sample | GO.Log.Extensions.Hosting |
| GO.Log.WebApi.Sample | GO.Log.Extensions.Hosting |

Solo se utilizan dependencias oficiales de Microsoft para logging/hosting y paquetes de pruebas/benchmarking (xUnit, Microsoft.NET.Test.Sdk, Coverlet y BenchmarkDotNet). No se incorpora un motor de logging externo. Se usan `System.Threading.Channels`, `System.Text.Json` y colecciones inmutables de .NET 8.

## Componentes y comportamiento

- Compatibilidad con `ILogger`, `ILoggerProvider`, `ISupportExternalScope`, `ILogger<T>`, categorías, EventId, niveles Trace a Critical y None.
- Captura inmutable de mensaje, plantilla, propiedades, scopes, excepción con stack/inner exceptions, timestamp UTC y Activity TraceId/SpanId. Copia previa a encolado; no quedan referencias al estado mutable del consumidor.
- Cola acotada, consumidor supervisado, escritura por lotes, políticas RejectNew/DropOldest y contabilidad explícita. Los productores no ejecutan I/O de archivo.
- Diagnósticos de eventos procesados/descartados/fallidos, fallos de sink y errores de captura; sin logging recursivo.
- `FlushAsync` con barrera de eventos aceptados, vaciado periódico y cierre cooperativo del host con plazo de gracia. Los eventos posteriores a una barrera pueden seguir pendientes cuando esta finaliza.
- Sinks/formateadores extensibles, TXT con saltos escapados y JSON Lines válido.
- Estilos Standard/Compact/Trace, columnas alineadas y marcadores de entrada/salida/severidad adaptados de common.library.log; configuración independiente para archivo y consola.
- Scopes explícitos BeginGOLogOperation, anidamiento compatible con awaits, tiempos monotónicos y contexto de proceso/hilo capturado en el productor; consola con stdout/stderr y colores solo en salida interactiva.
- Rotación por día UTC/tamaño UTF-8, archivos creados exclusivamente sin sobrescritura, lease de escritor, retención por días/cantidad con sidecars de propiedad y protección de archivos ajenos/reparse points.
- File.AppendOnStartup permite crear un archivo nuevo por ejecución o continuar el último compatible del día; conserva los bytes existentes y respeta tamaño, formato, ownership y fin de línea completo.
- Enmascaramiento recursivo de claves sensibles, comparación insensible a mayúsculas y límites por cadena, profundidad, propiedades y elementos.
- Configuración por delegado o `IConfiguration`, validación al iniciar, snapshot de opciones y registro DI sin motores duplicados.
- Console con todos los niveles, excepción y concurrencia; Web API con `/health`, `/test-log`, `/test-error`, `ILogger<T>`, scopes y correlación.

Las opciones disponibles y sus valores/validaciones se detallan en [configuration.md](configuration.md). La semántica de errores, barreras, ownership y ciclo de vida se detalla en [architecture.md](architecture.md).

## Validación ejecutada

Entorno: Windows, SDK .NET 8.0.426 y runtime .NET/ASP.NET Core 8.0.32. También se detectaron SDK 8.0.400 y 10.0.402. `global.json` fija la banda de .NET 8. Se desactivó `MSBuildEnableWorkloadResolver` en los comandos de validación para evitar la resolución de workloads móviles instalados en el equipo, ajenos al producto.

| Validación | Resultado |
|---|---|
| `dotnet restore All.sln` | Correcta, ocho proyectos |
| `dotnet build All.sln --configuration Release` | Correcta, cero advertencias y cero errores |
| `dotnet test All.sln --configuration Release` con Coverlet/TRX | 126 pruebas aprobadas: 96 unitarias y 30 de integración; cero fallidas/omitidas |
| Pruebas con Coverlet y TRX | Correctas; evidencias en `artifacts/startup-final` |
| Resumen conjunto de cobertura | Supera 80% en las tres bibliotecas |
| Empaquetado Release de las tres bibliotecas | Correcto |
| Inspección de los `.nupkg` | Versiones, autor, remoto real, README, DLL y XML de documentación verificados |
| Console ejecutado tras adaptar estilos | 116 líneas totales en archivo Trace, incluyendo Enter/Leave y eventos de hosting; salida Compact sin ANSI al redirigir, token enmascarado y cierre del host |
| Web API sample ejecutado en loopback | `/health` 200, `/test-log` 200, `/test-error` 500; propiedades, Activity e inner exception verificadas en archivos |
| BenchmarkDotNet, job Dry, FormatterBenchmarks | Smoke inicial TXT Standard/JSON completado; se agregaron escenarios Compact/Trace y se validó su formateo mediante pruebas, sin caracterización de rendimiento |
| `git diff --check` / revisión de LICENSE | Sin errores de whitespace; LICENSE intacto |

Las pruebas de integración incluyen peticiones HTTP reales con Kestrel en puerto efímero, archivos temporales, rotación diaria con `TimeProvider`, concurrencia y fallos de acceso. La saturación y las barreras se prueban con sinks controlados, sin depender de velocidades de disco. Las suites liberan sus handles y limpian recursos temporales.

Cobertura de líneas medida por unión de ambas suites, deduplicando módulo/archivo/número de línea:

| Módulo | Líneas cubiertas / ejecutables | Cobertura |
|---|---|---|
| GO.Log | 545 / 555 | 98.20% |
| GO.Log.Abstractions | 55 / 55 | 100% |
| GO.Log.Extensions.Hosting | 26 / 26 | 100% |

Estos porcentajes son de líneas, no una afirmación de cobertura completa de ramas ni de ausencia de errores. `tests/Get-CoverageSummary.ps1` reproduce el cálculo y aplica el umbral de 80%. No se mezclaron corridas fallidas anteriores en esa medición.

La ampliación de estilos agrega 45 casos de prueba. Se revisó el repositorio de referencia únicamente en lectura; los conceptos incorporados y las decisiones de adaptación se describen en [log-styles.md](log-styles.md).

La opción de inicio agrega otros 12 casos de integración: continuación en TXT/JSON, selección del archivo más reciente, rotación por fecha/tamaño al reiniciar y tras reanudar, formatos distintos, líneas incompletas, archivos ajenos/marcadores antiguos, archivos vacíos y lease en ambos modos. El modo predeterminado sigue creando archivos nuevos; true continúa solo archivos con metadatos compatibles, sin truncar. Los marcadores anteriores sin formato conservan su elegibilidad para retención, pero no para continuación.

## Paquetes generados

```text
artifacts/nuget/GO.Log.Abstractions.1.0.0.nupkg
artifacts/nuget/GO.Log.1.0.0.nupkg
artifacts/nuget/GO.Log.Extensions.Hosting.1.0.0.nupkg
```

Metadatos: Authors/Company MarioGO, versión 1.0.0, descripciones por biblioteca, README y copyright. RepositoryUrl proviene del remoto existente `https://github.com/ISCMarioGO/GO.Log`. Los cinco proyectos restantes tienen `IsPackable=false`. No se publicaron paquetes ni se realizaron commits/push.

## Limitaciones y pendientes

- No se soporta logging compartido multiproceso; use rutas independientes y patrones sin solapamiento. El lease protege la misma identidad de escritor, no combina eventos entre procesos.
- No hay recarga dinámica de opciones; se requiere crear otro motor para aplicar cambios.
- No se garantiza eliminar secretos en texto libre ni representaciones personalizadas. Los límites pueden truncar detalles de excepción y objetos.
- Un evento mayor al máximo de archivo se conserva entero en su archivo y puede exceder ese máximo.
- La retención se aplica al rotar/crear archivos, no mediante un barrido independiente durante inactividad. Archivos sin sidecar válido nunca se eliminan automáticamente.
- `FlushAsync` no garantiza fsync/durabilidad física, ni reintenta eventos fallidos. Un fallo después de una escritura parcial puede dejar líneas parciales o parte de un lote; no se reintenta para evitar duplicados.
- Los sinks personalizados deben respetar cancelación y liberar recursos; código del consumidor bloqueado en getters, formateadores o sinks puede retrasar captura/cierre.
- Se ejecutó un smoke test de formatos; las corridas completas de captura y throughput en disco quedan disponibles para caracterizar hardware de producción. No se presentan cifras de rendimiento como resultados representativos.
- Publicación a un registro remoto pendiente de una solicitud explícita; la entrega local está empaquetada y validada.

No quedan pendientes funcionales conocidos dentro del alcance implementado de GO.Log 1.0.0.
