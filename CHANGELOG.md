# Changelog

## 1.0.0 — 2026-10-16

- File.AppendOnStartup permite elegir entre archivo nuevo por ejecución (predeterminado) o continuación del último archivo compatible; respeta fecha, tamaño, formato y propiedad sin truncar contenido.
- Estilos TXT Standard/Compact/Trace configurables, columnas de proceso/hilo y marcadores de dirección/severidad inspirados en common.library.log.
- Scopes explícitos BeginGOLogOperation con duración monotónica, anidamiento asíncrono y eventos Enter/Leave estructurados.
- Destino de consola asíncrono opcional con stdout/stderr y colores desactivados al redirigir; escape de caracteres de control en texto.

- Motor propio compatible con `ILogger`, `ILoggerProvider`, `ISupportExternalScope` y `ILogger<T>`.
- Eventos inmutables, propiedades estructuradas, scopes, excepciones y correlación Activity.
- Canal acotado, lotes, políticas RejectNew/DropOldest y diagnósticos de pérdidas/fallos.
- Flush con barrera de aceptación, cierre del host y cancelación cooperativa.
- TXT y JSON Lines, rotación diaria UTC/por tamaño, retención con marcas de propiedad y lease.
- Enmascaramiento recursivo de propiedades y límites de captura.
- Integración DI/configuración, ejemplos Console/Web API, pruebas y benchmarks.
- Paquetes NuGet preparados; sin publicación remota.
