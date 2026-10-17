# Inicio rápido

1. Instale .NET SDK 8.0.426 (o parche posterior de la banda 8.0.4xx). Abra `All.sln` con Visual Studio 2022 compatible con .NET 8.
2. Desde la raíz ejecute `dotnet restore All.sln` y `dotnet build All.sln --configuration Release`.
3. Ejecute `dotnet run --project samples/mGO.Log.Console.Sample --configuration Release`. Genera todos los niveles, excepción con inner exception y 100 eventos concurrentes, y cierra el host.
4. Ejecute `dotnet run --project samples/mGO.Log.WebApi.Sample --configuration Release -- --urls http://localhost:5080`. Visite `/health`, `/test-log` y `/test-error` (este último devuelve 500 deliberadamente). ASP.NET Core inicia una Activity por petición; mGO.Log captura la correlación.

Para desarrollo en Visual Studio, seleccione cualquiera de los samples como proyecto de inicio. Los samples cargan appsettings desde su directorio de salida (`AppContext.BaseDirectory`), también al ejecutarse desde la raíz. Los logs se escriben en `logs` relativo al directorio de trabajo; puede configurar una ruta absoluta. El perfil Web API usa `http://localhost:5080` y abre `/health`.

## Paquetes locales

```powershell
dotnet pack src/mGO.Log.Abstractions/mGO.Log.Abstractions.csproj --configuration Release --output artifacts/nuget
dotnet pack src/mGO.Log/mGO.Log.csproj --configuration Release --output artifacts/nuget
dotnet pack src/mGO.Log.Extensions.Hosting/mGO.Log.Extensions.Hosting.csproj --configuration Release --output artifacts/nuget
```

Agregue `artifacts/nuget` como origen local en su consumidor y use `mGO.Log.Extensions.Hosting` 1.0.0. El registro NuGet predeterminado también debe estar disponible para resolver dependencias oficiales de Microsoft. No se han publicado paquetes a ningún registro remoto.

En una aplicación con host, use `builder.Logging.AddGOLog(builder.Configuration.GetSection("GOLog"))` o el delegado mostrado en el README. `ClearProviders()` es opcional si desea que mGO.Log sea el único destino; sin esa llamada convive con los proveedores predeterminados. Puede solicitar `GOLoggerProvider` al contenedor para `FlushAsync` y diagnósticos.

En una aplicación sin host, cree `GOLoggerProvider`, llame a `CreateLogger` y utilice `await using`. No comparta un directorio/patrón entre procesos. No elimine las marcas de propiedad que acompañan a los archivos.
