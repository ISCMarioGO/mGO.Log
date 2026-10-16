# Configuración de GO.Log

Las opciones públicas están en `GO.Log.Configuration`. `AddGOLog(IConfiguration)` recibe la sección `GOLog`; `AddGOLog(Action<GOLogOptions>)` configura por código. Se capturan al crear el motor y no se recargan. Las validaciones se ejecutan también para uso directo sin host. Referencia completa de JSON: `samples/GO.Log.WebApi.Sample/appsettings.json`.

| Opción | Predeterminado | Semántica / validación |
|---|---|---|
| Enabled | true | Captura habilitada |
| ApplicationName | Application | Obligatorio, no vacío; incluido en cada evento; en nombres de archivo caracteres fuera de ASCII alfanumérico, guion o guion bajo se reemplazan por `_` |
| MinimumLevel | Information | Enum LogLevel válido; None deshabilita |
| IncludeScopes | true | Copia scopes del proveedor externo |
| IncludeActivityTracking | true | Captura Activity.Current TraceId/SpanId |
| IncludeExecutionContext | true | Captura ProcessId/ProcessName y ThreadId/ThreadName del productor |
| File.Enabled | true | Agrega el sink de archivo predeterminado |
| File.Directory | logs | Ruta no vacía; relativa al directorio de trabajo del proceso |
| File.FileNamePattern | {application}-{date}-{sequence}.log | Nombre sin separadores de ruta ni caracteres inválidos; exige date y sequence; solo esos tokens y application |
| File.AppendOnStartup | false | false crea un archivo por ejecución; true continúa el último archivo administrado compatible del día UTC, sin truncar y respetando tamaño/formato |
| File.Format | Json | Json (JSONL) o Text |
| File.Text.Style | Standard | Standard, Compact o Trace; aplica con File.Format=Text |
| File.Text.TimestampFormat | O | Formato DateTimeOffset UTC, cultura invariante, máximo 128 caracteres; sin controles ni separadores de línea |
| File.Text.IncludeProperties | true | Agrega propiedades al texto |
| File.Text.IncludeScopes | true | Agrega scopes capturados al texto |
| File.Text.IncludeExceptionDetails | true | Texto completo de excepción; false presenta tipo y mensaje |
| File.MaxFileSizeMB | 10 | MiB, finito, mayor que cero, hasta 1 000 000; permite fracciones |
| File.RetentionDays | 7 | 1..36500, según última escritura UTC; evaluado al rotar |
| File.MaxRetainedFiles | 30 | Mayor que cero, incluye activo |
| File.FlushIntervalSeconds | 1 | 1..86400; intervalo periódico/idle del procesador |
| Console.Enabled | false | Agrega GOConsoleSink al mismo consumidor |
| Console.UseColors | true | ANSI solo en consola interactiva; apagado para redirección o escritores inyectados |
| Console.StandardErrorFromLevel | Warning | Este nivel y superiores se escriben en stderr; None envía todo a stdout |
| Console.Text.Style | Compact | Mismos estilos que File.Text |
| Console.Text.TimestampFormat | O | Mismas reglas que File.Text |
| Console.Text.IncludeProperties / IncludeScopes / IncludeExceptionDetails | true | Las mismas opciones de presentación que File.Text |
| Queue.Capacity | 4096 | Mayor que cero, pendientes excluyendo lote activo |
| Queue.BatchSize | 64 | Mayor que cero, límite de cada llamada a sink |
| Queue.FullMode | RejectNew | RejectNew o DropOldest |
| Queue.ShutdownTimeoutSeconds | 10 | 1..86400, gracia antes de cancelación cooperativa |
| Security.MaskSensitiveData | true | Enmascara claves sensibles recursivamente |
| Security.SensitiveKeys | password, token, authorization, secret, apiKey, connectionString | Arreglo no nulo, claves no vacías, coincidencia exacta insensible a mayúsculas |
| Security.MaxPropertyDepth | 5 | 1..32, objetos al alcanzar límite se sustituyen por `[MaxDepth]` |
| Security.MaxMessageLength | 16384 | Mayor que cero; límite por cadena capturada, también plantilla y texto de excepción |
| Security.MaxProperties | 64 | Mayor que cero; máximo por objeto/array/estado y número de scopes |

El filtro estándar de logging del host puede eliminar eventos antes de llegar al proveedor. Para niveles Trace/Debug configure también `Logging:LogLevel:Default` o `SetMinimumLevel`. Las reglas por proveedor usan el alias `GOLog`. `LogLevel.None` nunca se encola.

No desactive el enmascaramiento para registrar credenciales. Los valores de propiedades sensibles se sustituyen por `***`; no se garantiza detectar secretos en texto libre. Recomendado: propiedades escalares y mensajes sin credenciales.

Para configuración avanzada de sinks:

```csharp
using GO.Log.Configuration;
using GO.Log.Providers;
using GO.Log.Sinks;
using GO.Log.Formatters;

var options = new GOLogOptions { ApplicationName = "Worker" };
await using var provider = new GOLoggerProvider(options,
    [new GOFileSink(options, new GOTextFormatter())]);
var logger = provider.CreateLogger("Jobs");
```

Una colección explícita de sinks reemplaza todos los destinos automáticos configurados en `File.Enabled` y `Console.Enabled`; el motor dispone todos los sinks recibidos. `GOConsoleSink` vacía sus escritores al cerrar y conserva su propiedad en el llamante; nunca dispone Console.Out/Error. Al deshabilitar File y Console y no registrar otros sinks se procesan eventos sin persistirlos.

Las opciones de presentación no eliminan datos del evento ni de otros destinos: `IncludeExceptionDetails=false` en TXT no oculta detalles en JSON. La política de enmascaramiento se aplica antes de llegar a todos los sinks. Ver [estilos y operación](log-styles.md).

## Archivo nuevo o continuación al reiniciar

```csharp
builder.Logging.AddGOLog(options =>
{
    options.File.AppendOnStartup = true; // false para un archivo nuevo por ejecución.
});
```

La apertura es diferida hasta el primer evento; crear un proveedor sin escribir eventos no crea un archivo vacío. En modo continuación se adquiere primero el mismo lease de escritor, se busca el archivo del día UTC actual de la misma aplicación/patrón y formato, y se elige por última escritura UTC (secuencia como desempate). Si el siguiente evento no cabe, se rota; si el archivo tiene una línea final incompleta, se conserva sin agregarle contenido y se abre otro. Los límites y la retención permanecen activos. Los archivos TXT pueden continuar aunque se cambie el estilo de presentación; para separar estilos, use patrones diferentes.

Los sidecars actuales registran propietario y formato. Los marcadores anteriores que solo contienen propietario siguen permitiendo retención, pero no continuación, porque su formato no puede verificarse. Los archivos sin propiedad válida nunca se abren para continuar. Para formateadores personalizados, conserve el mismo formateador entre reinicios o separe sus rutas/patrones; el formato declarado Json/Text es lo que identifica el sidecar.
