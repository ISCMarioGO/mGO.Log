## Estilos

| Estilo | Uso | Presentación |
|---|---|---|
| Standard | TXT detallado original de GO.Log | Timestamp, nivel, aplicación, categoría, EventId, mensaje, plantilla y contexto |
| Compact | Lectura rápida o consola | Timestamp, nivel abreviado TRC/DBG/INF/WRN/ERR/CRT, categoría y mensaje |
| Trace | Diagnóstico de flujos, inspirado en la referencia | Marcador, timestamp, columnas alineadas de proceso/hilo, nivel, tiempo de operación, categoría, profundidad y mensaje |

En Trace, los marcadores son `>` para Enter, `<` para Leave, `-` para mensajes normales, `!` para Warning y `@` para Error/Critical. La dirección tiene prioridad sobre severidad; la columna de nivel conserva la severidad real. Las columnas identifican `ProcessName/ProcessId:ThreadName/ThreadId`. Los valores largos se recortan con `~` exclusivamente en esas columnas; el evento y JSON conservan los valores capturados.

La categoría de `ILogger<T>` sustituye la columna de assembly/clase del sistema anterior. No se recorren stacks en cada mensaje. La duración se expresa en `us`, `ms` o `s` y corresponde al tiempo desde el inicio de la operación actual, no a la diferencia respecto del último mensaje de un hilo. Esto mantiene una interpretación estable cuando una operación cambia de hilo después de un `await`.

`File.Format=Json` continúa generando un objeto JSON válido por línea: `File.Text` no altera JSON ni introduce banners. Los campos nuevos son `EventKind`, `Operation` (Name, Depth, ElapsedMilliseconds) y `ExecutionContext` (ProcessId, ProcessName, ThreadId, ThreadName).

## Configuración

```json
{
  "GOLog": {
    "ApplicationName": "Orders.Worker",
    "MinimumLevel": "Debug",
    "IncludeExecutionContext": true,
    "File": {
      "Format": "Text",
      "Text": {
        "Style": "Trace",
        "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff 'UTC'",
        "IncludeProperties": true,
        "IncludeScopes": true,
        "IncludeExceptionDetails": true
      }
    },
    "Console": {
      "Enabled": true,
      "UseColors": true,
      "StandardErrorFromLevel": "Warning",
      "Text": { "Style": "Compact", "IncludeProperties": false, "IncludeScopes": false }
    }
  }
}
```

Con hosting configure también `builder.Logging.SetMinimumLevel(LogLevel.Debug)` para permitir eventos Debug. Las opciones `File.Text` y `Console.Text` se validan y copian al crear el motor/formateador. `TimestampFormat` usa las reglas de `DateTimeOffset.ToString`, siempre UTC y cultura invariante; las plantillas arbitrarias `msgFormat` del sistema anterior no se interpretan. Los tres presets son la API soportada.

## Operaciones

```csharp
using Microsoft.Extensions.Logging;

using (logger.BeginGOLogOperation("ProcessOrder", LogLevel.Information))
{
    logger.LogInformation("Processing order {OrderId}", orderId);
    using (logger.BeginGOLogOperation("SaveOrder", LogLevel.Information))
    {
        await repository.SaveAsync(orderId, cancellationToken);
    }
}
```

Cada scope registra Enter y Leave. El nivel predeterminado es Debug; puede elegir Information como en el ejemplo. Los filtros habituales siguen vigentes. Un Enter/Leave filtrado no elimina el contexto de operación de mensajes internos habilitados. La profundidad raíz es cero, con máximo visual de 64; las ramas asíncronas mantienen scopes separados. Incluso con `IncludeScopes=false`, el campo dedicado `Operation` permanece disponible.

Use los scopes con `using`, en orden LIFO y dentro del mismo flujo lógico. Disponer dos veces no duplica Leave. Leave indica salida del bloque, también al propagarse una excepción; **no significa éxito**. Registre fallos mediante `logger.LogError(exception, ...)` y no capture/reduzca excepciones solo para la presentación. No pase credenciales como nombres de operación ni registre automáticamente todos los argumentos/retornos de métodos.

El tiempo se mide con `Stopwatch` y se copia al evento; timestamp y duración cumplen funciones distintas. `ThreadId` es el hilo productor capturado durante `ILogger.Log`, no el consumidor ni una identidad estable de petición. Para correlación entre hilos y servicios use TraceId/SpanId de Activity.

## Consola

`GOConsoleSink` comparte consumidor y semántica de fallos con el sink de archivos. No escribe desde `ILogger.Log`. Por defecto envía Warning/Error/Critical a stderr, el resto a stdout. `StandardErrorFromLevel=None` envía todo a stdout. Warning usa amarillo, Error rojo y Critical rojo intenso; el resto gris. La secuencia de reset se incluye en la misma línea sin modificar `Console.ForegroundColor`.

Los colores se deshabilitan automáticamente al redirigir cada flujo o al inyectar `TextWriter`. Los escritores permanecen bajo propiedad del llamante; el sink los vacía y nunca los dispone. Los eventos se enmascaran antes de llegar a la consola. El formato TXT escapa CR/LF/TAB, controles ANSI y separadores Unicode de línea para evitar líneas o secuencias de terminal introducidas por datos del consumidor.

Al usar `Console.Enabled=true`, evite registrar además el proveedor Console estándar si no desea salida duplicada. `builder.Logging.ClearProviders()` seguido de `AddGOLog(...)` selecciona exclusivamente los destinos de GO.Log.

## Decisiones de adaptación

| Concepto de la referencia | Implementación/práctica en GO.Log |
|---|---|
| Columnas, dirección, identación y unidades temporales | Preset Trace, GOLogEventKind y snapshots de operación/proceso/hilo |
| Trace.Enter/Leave y atributos de métodos | BeginGOLogOperation explícito o decoradores Trace.Debug/Information con instrumentación MSBuild; scopes externos de ILogger, sin PostSharp ni estado global |
| Mensajes por severidad y filtros | Niveles oficiales de .NET, filtros del host y MinimumLevel; Message corresponde a Information |
| Consola adicional y colores | Sink opcional en el mismo consumidor, con detección de redirección y separación stdout/stderr |
| Cola y flush periódico | Canal acotado existente, lotes, conteo preciso y barreras FlushAsync |
| Rotación y limpieza de archivos | CreateNew, lease y retención solo con sidecar de propiedad; sin borrados amplios por prefijo |
| Mensaje final de cierre | Diagnósticos verificables Processed/Dropped/Failed; no se afirma ausencia de pérdidas solo por escribir una despedida |
| Captura completa de excepciones | Tipo, mensaje, stack e inner exceptions estructurados y sujetos a límites, con presentación configurable |

El archivado GZip, profiling global, hooks de assembly load/excepciones no controladas y configuración XML del sistema anterior no forman parte de esta adaptación de estilos. No se modifica el repositorio de referencia. La escritura sigue siendo asíncrona y los errores se contabilizan, conservando la arquitectura y restricciones de GO.Log.

Los decoradores `[Trace.Debug]` y demás niveles registran entrada/salida automáticamente al compilar la implementación del método. Admiten métodos privados, estáticos y async, utilizando un logger del consumidor. Véase [method-decorators.md](method-decorators.md).

## Verificación

Las pruebas nuevas cubren los tres estilos y marcadores, cultura invariante, límites/formatos inválidos, controles de terminal, scopes anidados y concurrentes con awaits, duración, captura del hilo productor, enmascaramiento, stdout/stderr, cancelación/fallos de consola, propiedad de TextWriter, lectura desde appsettings y persistencia física TXT/JSONL. El reporte técnico contiene los resultados y la cobertura medidos.
