using mGO.Log.Configuration;
using Microsoft.Extensions.Options;

namespace mGO.Log.Extensions.Hosting.Configuration;

internal sealed class GOLogOptionsValidator : IValidateOptions<GOLogOptions>
{
    public ValidateOptionsResult Validate(string? name, GOLogOptions options)
    {
        try { options.Validate(); return ValidateOptionsResult.Success; }
        catch (ArgumentException ex) { return ValidateOptionsResult.Fail(ex.Message); }
    }
}
