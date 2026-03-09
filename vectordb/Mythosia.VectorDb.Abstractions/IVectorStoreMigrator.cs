using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.VectorDb
{
    public interface IDesignTimeVectorStoreMigratorFactory
    {
        string ProviderName { get; }

        IVectorStoreMigrator CreateMigrator(VectorStoreMigrationConnection connection);
    }

    public interface IVectorStoreMigrator
    {
        string ProviderName { get; }

        Task<VectorStoreMigrationPlan> PlanAsync(VectorStoreMigrationRequest request, CancellationToken cancellationToken = default);

        Task<VectorStoreMigrationResult> MigrateAsync(
            VectorStoreMigrationRequest request,
            IProgress<VectorStoreMigrationProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }

    public class VectorStoreMigrationConnection
    {
        public string Endpoint { get; set; } = string.Empty;

        public string? ApiKey { get; set; }

        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class VectorStoreMigrationRequest
    {
        public string Source { get; set; } = string.Empty;

        public string? Target { get; set; }

        public bool ReplaceOnSuccess { get; set; }
    }

    public class VectorStoreMigrationPlan
    {
        public string ProviderName { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public int SchemaVersion { get; set; }

        public string SchemaKind { get; set; } = string.Empty;

        public int TargetSchemaVersion { get; set; }

        public string TargetSchemaKind { get; set; } = string.Empty;

        public bool MigrationRequired { get; set; }

        public bool ReplaceOnSuccess { get; set; }
    }

    public class VectorStoreMigrationProgress
    {
        public string Stage { get; set; } = string.Empty;

        public long ProcessedRecords { get; set; }

        public long? TotalRecords { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    public class VectorStoreMigrationResult
    {
        public string ProviderName { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string Target { get; set; } = string.Empty;

        public int SchemaVersion { get; set; }

        public string SchemaKind { get; set; } = string.Empty;

        public long? TotalRecords { get; set; }

        public long MigratedRecords { get; set; }

        public bool ReplacedSource { get; set; }
    }
}
