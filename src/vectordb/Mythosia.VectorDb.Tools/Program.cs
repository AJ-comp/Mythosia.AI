using Mythosia.VectorDb;
using Mythosia.VectorDb.Qdrant;
using System.Globalization;

return await CliProgram.RunAsync(args);

internal static class CliProgram
{
    private static readonly IDesignTimeVectorStoreMigratorFactory[] DesignTimeFactories =
    {
        new QdrantVectorStoreMigratorFactory()
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        if (string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
            return await RunMigrationAsync(args[1], args.Skip(2).ToArray());

        if (string.Equals(args[0], "copy", StringComparison.OrdinalIgnoreCase))
            return await RunCopyAsync(args[1], args.Skip(2).ToArray());

        PrintUsage();
        return 1;
    }

    private static async Task<int> RunMigrationAsync(string provider, string[] args)
    {
        var factory = ResolveDesignTimeFactory(provider);
        if (factory == null)
        {
            Console.Error.WriteLine($"No design-time migrator factory found for provider '{provider}'.");
            return 1;
        }

        var endpoint = GetOption(args, "--endpoint");
        var source = GetOption(args, "--source");
        var target = GetOption(args, "--target");
        var apiKey = GetOption(args, "--api-key");
        var replace = HasFlag(args, "--replace");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Console.Error.WriteLine("Missing required option: --endpoint <host:port|url>");
            PrintUsage();
            return 1;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("Missing required option: --source <collection>");
            PrintUsage();
            return 1;
        }

        var connection = new VectorStoreMigrationConnection
        {
            Endpoint = endpoint,
            ApiKey = apiKey
        };
        connection.Properties["source"] = source;

        var migrator = factory.CreateMigrator(connection);
        var disposable = migrator as IDisposable;

        var request = new VectorStoreMigrationRequest
        {
            Source = source,
            Target = target,
            ReplaceOnSuccess = replace
        };

        try
        {
            Console.WriteLine();
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (replace)
            {
                Console.WriteLine("Warning: This command replaces the source collection after migration.");
                Console.WriteLine("Stop application writes, ingestion jobs, and background workers before continuing.");
                Console.WriteLine("If writes continue during migration, the final collection may not be consistent.");
            }
            else
            {
                Console.WriteLine("Warning: To guarantee migration consistency, stop writes to the source collection before execution.");
                Console.WriteLine("If writes continue during migration, the migrated staging collection may not match the latest source state.");
            }
            Console.ForegroundColor = prevColor;
            Console.WriteLine();

            var progress = new Progress<VectorStoreMigrationProgress>(p =>
            {
                var total = p.TotalRecords.HasValue
                    ? p.TotalRecords.Value.ToString(CultureInfo.InvariantCulture)
                    : "?";

                Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords.ToString(CultureInfo.InvariantCulture)}/{total} - {p.Message}");
            });

            var result = await migrator.MigrateAsync(request, progress);

            if (result.MigratedRecords == 0
                && string.Equals(result.Source, result.Target, StringComparison.Ordinal)
                && !result.ReplacedSource)
            {
                Console.WriteLine();
                var prevNoop = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Migration not required ===");
                Console.ForegroundColor = prevNoop;
                Console.WriteLine();
                Console.WriteLine($"  Collection  : {result.Source}");
                Console.WriteLine($"  Schema      : {result.SchemaKind} v{result.SchemaVersion}");
                Console.WriteLine("  Status      : already up to date");
                Console.WriteLine();
                return 0;
            }

            Console.WriteLine();
            var prevDone = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Migration completed successfully ===");
            Console.ForegroundColor = prevDone;
            Console.WriteLine();

            if (result.ReplacedSource)
            {
                Console.WriteLine($"  Collection : {result.Target}");
                Console.WriteLine($"  Mode       : replaced (source collection was recreated with new schema)");
            }
            else
            {
                Console.WriteLine($"  Source      : {result.Source}  (unchanged)");
                Console.WriteLine($"  Staging     : {result.Target}");
            }

            Console.WriteLine($"  Schema      : {result.SchemaKind} v{result.SchemaVersion}");
            Console.WriteLine($"  Records     : {result.MigratedRecords.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine();

            return 0;
        }
        finally
        {
            disposable?.Dispose();
        }
    }

    private static async Task<int> RunCopyAsync(string provider, string[] args)
    {
        var factory = ResolveDesignTimeFactory(provider);
        if (factory == null)
        {
            Console.Error.WriteLine($"No design-time migrator factory found for provider '{provider}'.");
            return 1;
        }

        var endpoint = GetOption(args, "--endpoint");
        var source = GetOption(args, "--source");
        var target = GetOption(args, "--target");
        var apiKey = GetOption(args, "--api-key");

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Console.Error.WriteLine("Missing required option: --endpoint <host:port|url>");
            PrintUsage();
            return 1;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("Missing required option: --source <collection>");
            PrintUsage();
            return 1;
        }

        var connection = new VectorStoreMigrationConnection
        {
            Endpoint = endpoint,
            ApiKey = apiKey
        };
        connection.Properties["source"] = source;

        var migrator = factory.CreateMigrator(connection);
        var disposable = migrator as IDisposable;

        try
        {
            if (migrator is not QdrantVectorStoreMigrator qdrantMigrator)
            {
                Console.Error.WriteLine($"Copy is not supported for provider '{provider}'.");
                return 1;
            }

            Console.WriteLine();
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: This command copies the source collection into a new collection.");
            Console.WriteLine("If writes continue during copy, the copied collection may not match the latest source state.");
            Console.ForegroundColor = prevColor;
            Console.WriteLine();

            var progress = new Progress<VectorStoreMigrationProgress>(p =>
            {
                var total = p.TotalRecords.HasValue
                    ? p.TotalRecords.Value.ToString(CultureInfo.InvariantCulture)
                    : "?";

                Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords.ToString(CultureInfo.InvariantCulture)}/{total} - {p.Message}");
            });

            var result = await qdrantMigrator.CopyAsync(source!, target, progress, CancellationToken.None);

            Console.WriteLine();
            var prevDone = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("=== Copy completed successfully ===");
            Console.ForegroundColor = prevDone;
            Console.WriteLine();
            Console.WriteLine($"  Source      : {result.Source}");
            Console.WriteLine($"  Copy        : {result.Target}");
            Console.WriteLine($"  Schema      : {result.SchemaKind} v{result.SchemaVersion}");
            Console.WriteLine($"  Records     : {result.MigratedRecords.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine();

            return 0;
        }
        finally
        {
            disposable?.Dispose();
        }
    }

    private static IDesignTimeVectorStoreMigratorFactory? ResolveDesignTimeFactory(string provider)
    {
        return DesignTimeFactories.FirstOrDefault(factory =>
            string.Equals(factory.ProviderName, provider, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  mythosia-vectordb migrate <provider> --endpoint <host:port|url> --source <collection> [--target <collection>] [--api-key <key>] [--replace]");
        Console.WriteLine("  mythosia-vectordb copy <provider> --endpoint <host:port|url> --source <collection> [--target <collection>] [--api-key <key>]");
    }
}
