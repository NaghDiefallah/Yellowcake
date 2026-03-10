namespace Yellowcake.Tests.Infrastructure;

public sealed class ScopedEnvironmentVariable : IDisposable
{
    private readonly string _name;
    private readonly string? _originalValue;

    public ScopedEnvironmentVariable(string name, string value)
    {
        _name = name;
        _originalValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}
