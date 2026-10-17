# GO.Log 1.0.0

Motor propio de logging estructurado para .NET 8, compatible con `ILogger<T>`, con cola acotada, escritura asíncrona por lotes y archivos TXT o JSON Lines.

Incluye estilos de texto `Standard`, `Compact` y `Trace`, consola opcional con colores y scopes de operación con entrada/salida, anidamiento y duración.

Para registrar automáticamente inicio y fin de métodos, use decoradores con el logger de la clase:

```csharp
using Trace = GO.Log.Attributes.Trace;

[Trace.Debug]
public async Task ExecuteAsync()
{
    await Task.Yield();
    _logger.LogInformation("Executing operation");
}
```

La clase debe tener un campo/propiedad `_logger` de tipo `ILogger`, o indicarlo con `[Trace.Debug(nameof(Logger))]`. Habilite Debug en los filtros del host y GO.Log. La instrumentación se incluye en la compilación del paquete, espera la finalización asíncrona y no requiere llamadas manuales a Begin/End. [Uso y límites de los decoradores](docs/method-decorators.md).

## Requisitos e instalación

.NET 8 SDK y Visual Studio 2022 17.11 o posterior. `global.json` selecciona SDK 8.0.426, con parches posteriores de esa banda permitidos.

Los tres paquetes se generan localmente; no se han publicado a un registro remoto:

```powershell
dotnet add package GO.Log.Extensions.Hosting --version 1.0.0 --source ./artifacts/nuget
```

Referencias: `GO.Log.Extensions.Hosting` → `GO.Log` → `GO.Log.Abstractions`. El paquete de hosting incluye las dependencias transitivas necesarias.

## Configuración y ASP.NET Core

```csharp
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddGOLog(builder.Configuration.GetSection("GOLog"));
var app = builder.Build();
app.MapGet("/health", (ILogger<Program> logger) => {
    logger.LogInformation("Health checked for {Application}", "Sample.Api");
    return Results.Ok();
});
app.Run();
```

```json
{
  "GOLog": {
    "ApplicationName": "Sample.Api",
    "MinimumLevel": "Information",
    "File": { "Directory": "logs", "Format": "Json" },
    "Queue": { "Capacity": 4096, "BatchSize": 64, "FullMode": "RejectNew" }
  }
}
```

También se admite `builder.Logging.AddGOLog(options => { options.ApplicationName = "Sample.Api"; });`. Las opciones se validan al iniciar y se copian al crear el motor; no hay recarga dinámica. Los filtros estándar de `Microsoft.Extensions.Logging` y `GOLog.MinimumLevel` se aplican conjuntamente. Para Trace/Debug, configure también `builder.Logging.SetMinimumLevel(LogLevel.Trace)`.

## Console, propiedades y excepciones

```csharp
using GO.Log.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddGOLog(o => o.ApplicationName = "Sample.Console");
using var host = builder.Build();
await host.StartAsync();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
using (logger.BeginScope(new Dictionary<string, object?> { ["OrderId"] = 17 }))
{
    logger.LogInformation("User {UserId} connected with {Token}", 42, "sensitive");
    try { throw new InvalidOperationException("Example"); }
    catch (Exception ex) { logger.LogError(ex, "Failed order {OrderId}", 17); }
}
await host.Services.GetRequiredService<GOLoggerProvider>().FlushAsync();
await host.StopAsync();
```

Las excepciones incluyen tipo, mensaje, stack e inner exceptions en `ExceptionDetails`, sujetas al límite de longitud. El evento incluye plantilla, propiedades, scopes y TraceId/SpanId de `Activity.Current`. Las propiedades se copian durante `Log`, incluyendo objetos anidados; errores de getters o formateadores se contabilizan en `CaptureFailures` sin lanzar recursivamente otro log.

## Estilos y operaciones

Para usar columnas y marcadores de la biblioteca de referencia:

```csharp
using GO.Log.Configuration;

builder.Logging.AddGOLog(options =>
{
    options.File.Format = GOLogFileFormat.Text;
    options.File.Text.Style = GOLogTextStyle.Trace;
    options.Console.Enabled = true;
    options.Console.Text.Style = GOLogTextStyle.Compact;
});

// Information permite ver entrada/salida con el umbral predeterminado.
using (logger.BeginGOLogOperation("SaveOrder", LogLevel.Information))
{
    logger.LogInformation("Saving order {OrderId}", 17);
    await Task.Yield();
}
```

`>` indica entrada, `<` salida, `-` mensaje, `!` Warning y `@` Error/Critical. La duración usa `Stopwatch` y la profundidad sigue scopes lógicos a través de `await`; el hilo capturado identifica al productor. `Standard` mantiene el formato TXT original y JSON conserva campos estructurados, sin cabeceras de texto ni códigos de color. Consulte [log-styles.md](docs/log-styles.md) para las opciones completas.

## Rotación y retención

`File.AppendOnStartup` selecciona el comportamiento al iniciar:

```json
{
  "GOLog": {
    "File": { "AppendOnStartup": true }
  }
}
```

- `false` (predeterminado): crea un archivo nuevo al escribir el primer evento de cada ejecución.
- `true`: continúa el último archivo administrado compatible del día UTC actual, agregando líneas al final sin borrar lo anterior. Si no existe uno adecuado, crea uno nuevo.

También puede usar `builder.Logging.AddGOLog(options => options.File.AppendOnStartup = true);`. La continuación respeta la rotación por fecha/tamaño, la exclusión de escritores y la retención. No se continúa un archivo de otro formato, sin metadatos de formato o con una línea final incompleta. Los archivos de versiones anteriores con marcador sin formato siguen sujetos a retención, pero se crea un archivo nuevo la primera vez que se activa esta opción.

Rotación diaria UTC y antes de superar `MaxFileSizeMB` (MiB). Un evento individual mayor al límite ocupa su propio archivo y puede superarlo. El patrón exige `{date}` y `{sequence}`; admite `{application}`. Los nombres nuevos se crean con `CreateNew`, sin sobrescribir. La retención se evalúa en cada rotación, por edad de última escritura UTC y cantidad, preservando el archivo activo.

Solo se eliminan archivos con nombre compatible y sidecar `.golog-managed` que identifica al propietario. No elimine esos sidecars si desea conservar la retención automática. Un lease `.golog-*.lock` evita dos escritores con la misma identidad. **No se soporta logging multiproceso compartido**; use directorios independientes. Los patrones de distintos motores no deben solaparse.

## Saturación, flush y cierre

`RejectNew` rechaza eventos entrantes cuando se llena la cola. `DropOldest` elimina el evento pendiente más antiguo; nunca el lote que ya se está escribiendo. Los descartes se cuentan explícitamente. La capacidad excluye el lote en procesamiento.

`FlushAsync` espera los eventos aceptados antes de su llamada hasta que estén procesados, fallidos o descartados, y vacía buffers de los sinks. Los eventos posteriores no forman parte de esa barrera. No garantiza persistencia física ante un corte de energía. Los errores de escritura se consultan en diagnósticos; un error al vaciar buffers hace fallar el flush. Cancelar el token cancela la espera del llamante.

El host cierra el motor mediante `StopAsync`. Tras el plazo de gracia se cancela el procesamiento y se contabiliza lo abandonado. Los sinks personalizados deben respetar cancelación; uno que se bloquee indefinidamente puede retrasar el cierre. `DisposeAsync` es preferible al cierre síncrono de `ILoggerProvider.Dispose`.

## Seguridad y rendimiento

Se enmascaran claves sensibles sin distinguir mayúsculas y minúsculas, también en objetos anidados y scopes, antes de encolar. Las cadenas sensibles conocidas de propiedades se sustituyen también en el mensaje renderizado. **No se garantiza eliminar secretos incrustados en texto libre**, excepciones o representaciones personalizadas. No registre credenciales como texto libre. Se limita longitud, profundidad y número de propiedades/elementos.

`ILogger.Log` realiza captura, formateo del mensaje y enmascaramiento; el I/O de archivo se ejecuta en un consumidor supervisado. La reflexión y serialización de objetos tienen costo: prefiera propiedades escalares. Hay benchmarks separados de captura, formateo y escritura real; no se prometen cifras sin ejecutarlos.

Los contadores `Processed`, `Dropped`, `Failed`, `SinkFailures` y `CaptureFailures` están disponibles en `GOLoggerProvider.Diagnostics`. Un fallo de sink no impide intentar otros destinos; no hay reintentos que puedan duplicar escrituras parciales. Cero sinks implica procesamiento sin persistencia. `LastError` conserva solo el tipo para evitar filtración de datos.

## Ejecución y documentación

```powershell
dotnet restore All.sln
dotnet build All.sln --configuration Release
dotnet test All.sln --configuration Release
dotnet run --project samples/GO.Log.Console.Sample
dotnet run --project samples/GO.Log.WebApi.Sample
```

Web API: `/health`, `/test-log` y `/test-error`. Detalles: [arquitectura](docs/architecture.md), [opciones](docs/configuration.md), [inicio](docs/getting-started.md), [pruebas](docs/testing.md) y [reporte técnico](docs/implementation-report.md).
