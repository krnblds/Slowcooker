namespace Slowcooker.Storage.Postgres;

public sealed class PostgresStorageOptions
{
    /// <summary>Schema that holds the <c>jobs</c> and <c>recurring_jobs</c> tables. Default "slowcooker".</summary>
    public string Schema { get; set; } = "slowcooker";

    /// <summary>Create the schema and tables on first use. Default true. Turn off if you manage migrations yourself (see <see cref="PostgresJobStorage.SchemaSql"/>).</summary>
    public bool AutoCreateSchema { get; set; } = true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Schema) || Schema.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new InvalidOperationException("PostgresStorageOptions.Schema must be a simple identifier (letters, digits, underscore).");
    }
}
