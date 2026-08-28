using System.Data.Odbc;
using Microsoft.Extensions.Options;

namespace PsaWeb.Sage50;

internal sealed class OdbcSageConnectionFactory : ISageConnectionFactory
{
    private readonly SageOptions _options;

    public OdbcSageConnectionFactory(IOptions<SageOptions> options)
    {
        _options = options.Value;
    }

    public OdbcConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión a Sage 50. Configure '{SageOptions.SectionName}:ConnectionString'.");
        }

        return new OdbcConnection(_options.ConnectionString);
    }
}
