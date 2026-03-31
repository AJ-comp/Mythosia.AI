# Mythosia.VectorDb.Tools

Package Manager Console tools for **Mythosia VectorDb** providers.

Supports Qdrant collection migration from dense-only to hybrid schema, and raw collection copying.

## Installation

Install via NuGet Package Manager or PMC:

```powershell
Install-Package Mythosia.VectorDb.Tools
```

After installation, commands are available directly in the **Package Manager Console**.

## Usage

```bash
mythosia-vectordb migrate <provider> --endpoint <host:port|url> --source <collection> [--target <collection>] [--api-key <key>] [--replace]
mythosia-vectordb copy <provider> --endpoint <host:port|url> --source <collection> [--target <collection>] [--api-key <key>]
```

The command prints a warning and immediately starts execution.

## Examples

### Basic migration

```bash
mythosia-vectordb migrate qdrant --endpoint localhost:6334 --source docs
```

Creates `docs_migrate` as the migrated staging collection. If that name already exists, the tool uses `docs_migrate2`, `docs_migrate3`, and so on. The original `docs` collection is unchanged.

### Custom target

```bash
mythosia-vectordb migrate qdrant --endpoint localhost:6334 --source docs --target docs_v2
```

### Qdrant Cloud

```bash
mythosia-vectordb migrate qdrant --endpoint https://example-cluster.qdrant.io --source docs --api-key your-api-key
```

### Replace source collection

```bash
mythosia-vectordb migrate qdrant --endpoint localhost:6334 --source docs --replace
```

Migrates into a staging collection, then replaces the original collection with the migrated data.

### Copy collection

```bash
mythosia-vectordb copy qdrant --endpoint localhost:6334 --source docs
```

Creates `docs_copy`. If that name already exists, the tool uses `docs_copy2`, `docs_copy3`, and so on.
The source collection schema, payload, vectors, and schema marker are copied as-is.

## Options

| Option | Description |
| --- | --- |
| `--endpoint` | **Required.** Qdrant host:port or URL. |
| `--source` | **Required.** Source collection name. |
| `--target` | Target collection name. Defaults to `<source>_migrate`, then `<source>_migrate2`, `<source>_migrate3`, and so on if needed. |
| `--api-key` | Qdrant API key for authenticated deployments. |
| `--replace` | Replaces the source collection with the migrated result. |

For `copy`, `--target` defaults to `<source>_copy`, then `<source>_copy2`, `<source>_copy3`, and so on if needed.

## Consistency

A yellow warning is printed before migration starts.

Stop all writes before running migration:

- application write traffic
- ingestion jobs
- background workers

If writes continue during migration, the migrated collection may not match the latest source state.

## License

See repository root for license information.
