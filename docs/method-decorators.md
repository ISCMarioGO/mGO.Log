# Decoradores de métodos

GO.Log permite registrar automáticamente entrada y salida mediante `[Trace.Debug]`, `[Trace.Information]` y los demás niveles. La instrumentación se ejecuta durante MSBuild; el atributo no depende de interceptores de DI, reflexión por llamada, singletons globales ni PostSharp. Funciona también al llamar directamente a un método privado o estático.

## Uso

```csharp
using Microsoft.Extensions.Logging;
using Trace = GO.Log.Attributes.Trace;

public sealed class OrderService(ILogger<OrderService> logger)
{
    private readonly ILogger<OrderService> _logger = logger;

    [Trace.Debug]
    public async Task<int> SaveAsync(int orderId)
    {
        await Task.Yield();
        _logger.LogInformation("Saving order {OrderId}", orderId);
        return orderId;
    }
}
```

El alias `using Trace = GO.Log.Attributes.Trace;` evita ambigüedades con `System.Diagnostics.Trace`. Configure tanto los filtros del host como GO.Log para Debug:

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddGOLog(options => options.MinimumLevel = LogLevel.Debug);
```

Para el ejemplo se emiten Enter y Leave con operación `OrderService.SaveAsync`; el scope incluye los mensajes internos. El preset de texto Trace los representa con `>` y `<`, anidamiento y duración; JSON conserva `EventKind` y `Operation`. El tiempo usa Stopwatch y termina después del último await, incluso al salir por excepción o cancelación. Leave indica salida, sin afirmar éxito; registre la excepción con `LogError` donde se maneje.

## Variantes

| Atributo | Nivel |
|---|---|
| `[Trace.Trace]` | Trace |
| `[Trace.Debug]` | Debug |
| `[Trace.Information]` | Information |
| `[Trace.Warning]` | Warning |
| `[Trace.Error]` | Error |
| `[Trace.Critical]` | Critical |

El logger predeterminado es el miembro `_logger`. Puede indicar otro campo, propiedad o parámetro de tipo `ILogger`/`ILogger<T>` y un nombre de operación personalizado:

```csharp
[Trace.Information(nameof(Logger), operationName: "Orders.Save")]
public void Save() { /* implementación de la aplicación */ }

[Trace.Debug(nameof(logger))]
public static int Calculate(ILogger logger, int value) => value * 2;
```

En el primer ejemplo la clase debe exponer una propiedad o campo `Logger` accesible. El identificador debe ser un nombre simple; para estáticos debe existir un logger estático o un parámetro como en el segundo ejemplo. No hay búsqueda automática de servicios ni variables globales.

Alternativa con nivel explícito:

```csharp
using GO.Log.Attributes;

[GOTrace(LogLevel.Debug, loggerMemberName: nameof(_logger), operationName: "Orders.Save")]
public void Save() { /* implementación de la aplicación */ }
```

La configuración de scopes y enmascaramiento del proveedor sigue vigente. No se capturan automáticamente argumentos ni valores de retorno. Use propiedades estructuradas explícitas para los datos que decida registrar.

## Compilación

Al consumir los paquetes, `GO.Log` incluye el target `buildTransitive/GO.Log.targets` y herramientas oficiales de Roslyn para analizar los atributos. No se agregan motores de logging externos ni dependencias de compilador a las referencias de runtime de la aplicación. Se conservan los ocho proyectos de la solución.

Antes de CoreCompile se generan copias instrumentadas solo de los archivos afectados dentro de `obj/<configuración>/<framework>/golog-trace`. Se compilan esas copias; los archivos originales no se modifican. Las directivas `#line` mantienen la ubicación original de las líneas de código del usuario en diagnósticos y depuración. En referencias de proyecto dentro de este repositorio, `Directory.Build.targets` importa el mismo target que se distribuye por NuGet.

Se validó con .NET SDK 8.0.426 y MSBuild 17.11.9 de Visual Studio 2022. Los métodos deben compilarse mediante los targets de MSBuild incluidos, con C# compatible con .NET 8. La compilación requiere las herramientas empaquetadas y permite ejecutar tareas MSBuild. No basta con copiar solo la DLL o compilar con csc omitiendo los targets.

La tarea instrumenta archivos C# presentes en Compile antes de CoreCompile. No modifica ensamblados precompilados ni fuentes que un generador agregue posteriormente dentro del compilador.

Para desactivar deliberadamente la instrumentación en un proyecto:

```xml
<PropertyGroup>
  <GOLogTraceInstrumentation>false</GOLogTraceInstrumentation>
</PropertyGroup>
```

En ese modo los atributos permanecen como metadatos y no producen logs. Se requiere recompilar al agregar/quitar atributos o cambiar esta propiedad; el modo de diseño del editor no reescribe archivos y Hot Reload no sustituye la compilación completa de la instrumentación.

## Semántica y límites

- Métodos C# con cuerpo en bloque o expresión: síncronos, privados, estáticos, virtuales y genéricos. El atributo se aplica a la implementación, no al contrato abstracto.
- Métodos `async`: el using de instrumentación forma parte de la máquina de estados del compilador, con cierre en finally.
- Métodos no async que devuelven exactamente `Task`, `Task<T>`, `ValueTask` o `ValueTask<T>`: se devuelven wrappers que registran Leave al completar/fallar/cancelarse. Conservan resultados y excepciones; no se garantiza identidad de referencia del Task. ValueTask se consume una sola vez. Los errores síncronos previos a devolver la tarea siguen siendo síncronos. No se envuelve un return de una lambda o función local anidada.
- Iteradores con yield: la ejecución empieza al avanzar la enumeración y termina al completarla o disponerla. No se registra al crear un enumerable sin consumirlo. Los iteradores asíncronos siguen la misma regla; disponga siempre el enumerador.
- Los scopes fluyen por el contexto de ejecución habitual de .NET y las operaciones anidadas conservan profundidad. Suprimir explícitamente ExecutionContext puede cortar esa correlación.
- Constructores, propiedades/accessors, operadores, funciones locales decoradas, declaraciones sin cuerpo y atributos derivados personalizados no se instrumentan; cuando un atributo de tracing aparece en esas declaraciones se rechaza en compilación. No se soportan Task/ValueTask devueltos por referencia. Otros tipos de retorno se tratan como ejecución síncrona del cuerpo; no se rastrea trabajo diferido de awaitables personalizados.
- El método que accede al logger debe poder usar el miembro indicado. Un logger inexistente/inaccesible o de tipo incompatible produce un error de compilación; un logger nulo produce un error de argumentos al ejecutar. Los filtros de nivel siguen determinando qué eventos se escriben.

## Validación

Las pruebas compilan métodos decorados reales y verifican entrada/salida, múltiples returns, expresiones, métodos privados/estáticos/genéricos, await, tareas devueltas sin async, anulabilidad, excepciones, cancelación, anidamiento y enumeradores. `tests/Verify-TracePackage.ps1` genera un consumidor temporal en obj, usa únicamente los tres NuGet locales con caché aislada y desactiva imports del repositorio; también comprueba el error GOLOGTRACE001 de una declaración abstracta decorada. No publica paquetes.
