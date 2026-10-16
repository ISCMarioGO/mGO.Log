# Pruebas y benchmarks

Ejecute desde la raíz con SDK .NET 8:

```powershell
dotnet restore All.sln
dotnet build All.sln --configuration Release
dotnet test All.sln --configuration Release
dotnet test All.sln --configuration Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
```

Las suites xUnit se dividen en:

- Unitarias: niveles/None, filtros y deshabilitado, captura inmutable, scopes, Activity, excepciones, formatos TXT/JSONL, límites, claves sensibles, validaciones, saturación determinista en ambas políticas, contabilidad concurrente, flush, cancelación y cierre cooperativo.
- Integración: archivos reales temporales, UTF-8/JSONL/TXT, tamaño, día UTC mediante reloj inyectado, retención, protección de archivos ajenos, reinicio, exclusión de escritores simultáneos, concurrencia, archivos inaccesibles, DI sin duplicados, configuración y aplicación ASP.NET Core con Kestrel en loopback y puerto efímero.
- Estilos y tracing: Standard/Compact/Trace, símbolos de severidad/dirección, contexto productor, scopes de operación con awaits y ramas concurrentes, duraciones invariantes, escape de controles, presentación de excepciones y configuración JSON. Consola con stdout/stderr, escritores compartidos bajo propiedad del llamante, cancelación, fallos y ausencia de ANSI al redirigir.
- Reinicio configurable: archivo nuevo por defecto y continuación del último compatible en TXT/JSON, conservación byte a byte, rotación diaria y por tamaño al reiniciar y después de reanudar, archivos vacíos/incompletos, separación de formatos, exclusión de archivos ajenos y compatibilidad con marcadores antiguos.

Las pruebas crean directorios únicos y los eliminan después de liberar los handles. Las pruebas de saturación bloquean el primer lote mediante una señal, sin depender de sleeps. El cambio de día usa `TimeProvider`; no cambia el reloj del sistema. La prueba de cierre programa una gracia de un segundo y verifica cancelación del sink y contabilidad de pendientes.

Coverlet genera `coverage.cobertura.xml`. Para cobertura conjunta, combine por módulo/clase/archivo/línea la unión de las líneas visitadas por ambas suites; no promedie porcentajes entre proyectos. El objetivo es al menos 80% de líneas de los componentes críticos del motor. Consulte el reporte técnico para los resultados medidos de esta entrega.

`./tests/Get-CoverageSummary.ps1 -ResultsDirectory artifacts/coverage` calcula esa unión por módulo/archivo/línea y falla si falta alguna biblioteca o un módulo no alcanza el 80%. Para evitar mezclar corridas antiguas con nuevas, use un directorio de resultados vacío por verificación.

## BenchmarkDotNet

```powershell
dotnet run --project tests/GO.Log.Benchmarks --configuration Release -- --filter '*CaptureBenchmarks*'
dotnet run --project tests/GO.Log.Benchmarks --configuration Release -- --filter '*FormatterBenchmarks*'
dotnet run --project tests/GO.Log.Benchmarks --configuration Release -- --filter '*DiskThroughputBenchmarks*'
```

`CaptureBenchmarks` mide captura Information/Error/estructurada, con sink sin I/O y vaciado fuera de la medición; invalida la corrida si detecta pérdidas. `FormatterBenchmarks` compara TXT Standard/Compact/Trace y JSON de un evento. Ambos usan `MemoryDiagnoser` para asignaciones y GC. `DiskThroughputBenchmarks` incluye captura, creación de archivos, procesamiento, escritura, flush, cierre y limpieza de 1000 eventos, con lotes de 1/64/256 y ambos formatos. Su atribución por operación refleja ese costo completo, no solo el I/O. No compare estos resultados directamente con los de microoperaciones.

Ejecute las corridas completas en hardware dedicado, sin depurador y con condiciones de disco documentadas. Un smoke test de BenchmarkDotNet demuestra ejecutabilidad, no caracteriza rendimiento. No hay cifras de throughput prometidas ni inventadas.
