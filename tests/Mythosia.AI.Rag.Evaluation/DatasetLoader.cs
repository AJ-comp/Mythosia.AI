using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mythosia.AI.Rag.Evaluation;

public static class DatasetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static LoadedDataset Load(string path, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var rootDirectory = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(dataRoot));
        if (!Directory.Exists(rootDirectory)) throw new DirectoryNotFoundException(rootDirectory);
        using var json = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        RejectDuplicateProperties(json.RootElement);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The dataset must be a JSON object.");

        EvaluationDataset dataset;
        if (json.RootElement.TryGetProperty("sources", out _))
        {
            var legacy = json.RootElement.Deserialize<LegacyDataset>(JsonOptions)
                ?? throw new InvalidDataException("The legacy dataset is empty.");
            if (legacy.Sources is null || legacy.Cases is null)
                throw new InvalidDataException("Legacy sources and cases must be arrays.");
            dataset = new EvaluationDataset
            {
                Id = System.IO.Path.GetFileNameWithoutExtension(path),
                Version = "legacy-1",
                Description = legacy.Description ?? "",
                Documents = legacy.Sources.Select(source => new EvaluationDocument { Id = source, Path = source }).ToList(),
                Cases = legacy.Cases.Select(item => item is null ? throw new InvalidDataException("A legacy case cannot be null.") : new EvaluationCase
                {
                    Id = item.Id,
                    Query = item.Query,
                    Category = item.Category,
                    Judgments = LegacyJudgments(item.RelevantSources)
                }).ToList()
            };
        }
        else
        {
            if (!json.RootElement.TryGetProperty("schemaVersion", out _))
                throw new InvalidDataException("A versioned dataset must declare schemaVersion.");
            if (!json.RootElement.TryGetProperty("version", out _))
                throw new InvalidDataException("A versioned dataset must declare version.");
            if (json.RootElement.TryGetProperty("cases", out var rawCases) && rawCases.ValueKind == JsonValueKind.Array)
                foreach (var item in rawCases.EnumerateArray())
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("judgments", out _))
                        throw new InvalidDataException("Each case must declare judgments (an empty object explicitly denotes no answer).");
            dataset = json.RootElement.Deserialize<EvaluationDataset>(JsonOptions)
                ?? throw new InvalidDataException("The dataset is empty.");
        }

        ValidateDataset(dataset);
        var documents = new List<LoadedDocument>();
        foreach (var document in dataset.Documents)
        {
            string? sourcePath = null;
            var content = document.Text;
            if (document.Path is not null)
            {
                sourcePath = ResolveDocumentPath(rootDirectory, document.Path);
                content = File.ReadAllText(sourcePath, new UTF8Encoding(false, true));
            }
            documents.Add(new LoadedDocument(document.Id, content!,
                new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(document.Metadata, StringComparer.Ordinal)), sourcePath));
        }
        return new LoadedDataset(dataset, documents.AsReadOnly(), CreateFingerprint(dataset, documents));
    }

    private static Dictionary<string, int> LegacyJudgments(List<string>? sources)
    {
        if (sources is null) throw new InvalidDataException("Each legacy case must declare relevantSources (an empty array is allowed).");
        var judgments = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            Required(source, "Legacy relevant source");
            if (!judgments.TryAdd(source, 1)) throw new InvalidDataException($"Duplicate relevant source '{source}'.");
        }
        return judgments;
    }

    private static void ValidateDataset(EvaluationDataset dataset)
    {
        if (dataset.SchemaVersion != 1) throw new InvalidDataException($"Unsupported dataset schemaVersion {dataset.SchemaVersion}; supported: 1.");
        Required(dataset.Id, "Dataset id");
        Required(dataset.Version, "Dataset version");
        if (dataset.Documents is null || dataset.Documents.Count == 0) throw new InvalidDataException("The dataset must contain documents.");
        if (dataset.Cases is null || dataset.Cases.Count == 0) throw new InvalidDataException("The dataset must contain cases.");
        var documents = new Dictionary<string, EvaluationDocument>(StringComparer.Ordinal);
        foreach (var document in dataset.Documents)
        {
            if (document is null) throw new InvalidDataException("A document cannot be null.");
            Required(document.Id, "Document id");
            if (!documents.TryAdd(document.Id, document)) throw new InvalidDataException($"Duplicate document id '{document.Id}'.");
            if ((document.Path is null) == (document.Text is null))
                throw new InvalidDataException($"Document '{document.Id}' must declare exactly one of path or text.");
            ValidateMetadata(document.Metadata, $"Document '{document.Id}' metadata");
        }
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in dataset.Cases)
        {
            if (item is null) throw new InvalidDataException("A case cannot be null.");
            Required(item.Id, "Case id");
            Required(item.Query, $"Case '{item.Id}' query");
            Required(item.Category, $"Case '{item.Id}' category");
            if (!caseIds.Add(item.Id)) throw new InvalidDataException($"Duplicate case id '{item.Id}'.");
            if (item.Judgments is null) throw new InvalidDataException($"Case '{item.Id}' judgments cannot be null.");
            if (item.Filter is not null) ValidateMetadata(item.Filter, $"Case '{item.Id}' filter");
            foreach (var (documentId, grade) in item.Judgments)
            {
                if (!documents.TryGetValue(documentId, out var document))
                    throw new InvalidDataException($"Case '{item.Id}' references unknown document '{documentId}'.");
                if (grade < 0 || grade > 30) throw new InvalidDataException($"Case '{item.Id}' grade for '{documentId}' must be an integer from 0 to 30.");
                if (grade > 0 && item.Filter is not null && item.Filter.Any(pair =>
                        !document.Metadata.TryGetValue(pair.Key, out var value) || !string.Equals(pair.Value, value, StringComparison.Ordinal)))
                    throw new InvalidDataException($"Case '{item.Id}' relevant document '{documentId}' does not satisfy its filter.");
            }
        }
    }

    private static void ValidateMetadata(Dictionary<string, string>? metadata, string name)
    {
        if (metadata is null) throw new InvalidDataException($"{name} cannot be null.");
        foreach (var (key, value) in metadata)
        {
            Required(key, $"{name} key");
            if (value is null) throw new InvalidDataException($"{name} value for '{key}' cannot be null.");
        }
    }

    private static void Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"{name} cannot be blank.");
    }

    private static string ResolveDocumentPath(string root, string path)
    {
        Required(path, "Document path");
        // Both separators are recognized even when fixtures move between Windows and Unix.
        var portablePath = path.Replace('\\', '/');
        if (System.IO.Path.IsPathRooted(path) || portablePath.StartsWith('/') || portablePath.Contains(':') || portablePath.Split('/').Contains(".."))
            throw new InvalidDataException($"Document path must be relative and remain within the data root: '{path}'.");
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, portablePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var prefix = System.IO.Path.TrimEndingDirectorySeparator(root) + System.IO.Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Document path leaves the data root: '{path}'.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Dataset document was not found: '{path}'.", fullPath);
        for (var current = fullPath; !string.Equals(current, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); current = System.IO.Path.GetDirectoryName(current)!)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Document paths cannot traverse symbolic links or reparse points: '{path}'.");
        }
        return fullPath;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"Duplicate JSON property '{property.Name}'.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static string CreateFingerprint(EvaluationDataset dataset, List<LoadedDocument> documents)
    {
        // Ordered lists preserve ingestion/query order; dictionaries are canonicalized independently of JSON key order.
        var canonical = new
        {
            dataset.SchemaVersion, dataset.Id, dataset.Version,
            Documents = documents.Select(document => new { document.Id, document.Text, Metadata = document.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() }).ToArray(),
            Cases = dataset.Cases.Select(item => new
            {
                item.Id, item.Query, item.Category, item.Language,
                Judgments = item.Judgments.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
                Filter = (item.Filter ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }

    private sealed record LegacyDataset
    {
        public string? Description { get; init; }
        public List<string>? Sources { get; init; }
        public List<LegacyCase>? Cases { get; init; }
    }

    private sealed record LegacyCase
    {
        public string Id { get; init; } = "";
        public string Query { get; init; } = "";
        public string Category { get; init; } = "uncategorized";
        public List<string>? RelevantSources { get; init; }
    }
}
