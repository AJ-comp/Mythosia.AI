# 벡터 스토어 마이그레이션

Mythosia.AI는 버전 간 벡터 스토어 스키마를 업그레이드하는 마이그레이션 툴링을 포함합니다. 주로 이전 컬렉션 스키마(밀집 벡터만)를 현재 하이브리드 스키마(밀집 + 희소 벡터)로 업그레이드할 때 사용합니다.

## 마이그레이션이 필요한 경우

하이브리드 검색이 도입되기 전의 라이브러리 버전으로 Qdrant 컬렉션을 생성했다면, 해당 컬렉션은 **밀집 전용** 스키마 상태입니다. 이 상태에서 하이브리드 검색을 실행하면 실패하거나 잘못된 결과가 나옵니다.

마이그레이션은 컬렉션을 현재 **하이브리드 스키마**(스키마 버전 2)로 업그레이드합니다. 이 스키마는 레코드당 밀집 벡터와 희소 벡터를 모두 저장합니다.

## CLI 도구

마이그레이션 CLI 도구를 설치합니다:

```bash
dotnet tool install -g Mythosia.VectorDb.Tools
```

### 명령어

**`migrate`** — 컬렉션을 제자리에서 업그레이드합니다:

```bash
mythosia-vectordb migrate qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  [--api-key your-key] \
  [--replace]
```

- `--replace` 없음: `my-collection_migrated`라는 새 컬렉션 생성
- `--replace` 있음: 완료 시 원본 컬렉션 덮어씀 (파괴적)

**`copy`** — 스키마를 업그레이드하면서 컬렉션을 복사합니다:

```bash
mythosia-vectordb copy qdrant \
  --endpoint localhost:6334 \
  --source my-collection \
  --target my-collection-v2 \
  [--api-key your-key]
```

현재 스키마로 새 타겟 컬렉션을 생성하고 소스에서 모든 레코드를 복사합니다.

## 프로그래매틱 마이그레이션

코드에서 직접 `QdrantVectorStoreMigrator`를 사용합니다:

```csharp
using Mythosia.VectorDb.Qdrant;

var migrator = new QdrantVectorStoreMigrator(new QdrantOptions
{
    Host           = "localhost",
    Port           = 6334,
    CollectionName = "my-collection",
    Dimension      = 1536
});
```

### 마이그레이션 전 계획 확인

실행 전에 마이그레이션이 무엇을 할지 확인합니다:

```csharp
var plan = await migrator.PlanAsync(new VectorStoreMigrationRequest
{
    Source = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" },
    Target = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" }
});

Console.WriteLine($"현재 스키마: {plan.CurrentSchema}");
Console.WriteLine($"대상 스키마: {plan.TargetSchema}");
Console.WriteLine($"마이그레이션할 레코드: {plan.RecordCount}");
```

### 진행 상황과 함께 마이그레이션 실행

```csharp
var progress = new Progress<VectorStoreMigrationProgress>(p =>
{
    Console.WriteLine($"[{p.Stage}] {p.ProcessedRecords}/{p.TotalRecords} — {p.Message}");
});

var result = await migrator.MigrateAsync(
    new VectorStoreMigrationRequest
    {
        Source           = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" },
        Target           = new VectorStoreMigrationConnection { Endpoint = "localhost:6334" },
        ReplaceOnSuccess = false   // true = 완료 시 소스 덮어씀
    },
    progress: progress
);

Console.WriteLine($"마이그레이션: {result.MigratedRecords}개 레코드");
Console.WriteLine($"오류: {result.ErrorCount}개");
```

### 새 컬렉션으로 복사

소스를 건드리지 않고 스키마를 업그레이드하면서 복사합니다:

```csharp
var result = await migrator.CopyAsync(
    source:   "my-collection",
    target:   "my-collection-v2",
    progress: progress,
    cancellationToken: default
);
```

## 스키마 버전 관리

Mythosia.AI는 Qdrant의 특수 마커 레코드(ID `__mythosia_schema__`)를 사용해 스키마 버전을 내부적으로 추적합니다. 수동으로 관리할 필요가 없습니다.

| 스키마 버전 | 종류 | 설명 |
|------------|------|------|
| 1 | `dense` | 밀집 벡터만 (레거시) |
| 2 | `hybrid` | 밀집 + 희소 벡터 (현재) |

스키마 마커가 없는 컬렉션을 읽으면 버전 1(레거시)로 처리되고 마이그레이션 대상으로 표시됩니다.

## 지원 프로바이더

| 프로바이더 | Migrate | Copy |
|------------|---------|------|
| Qdrant | ✓ | ✓ |
| Pinecone | — | — |
| PostgreSQL | — | — |
| InMemory | — | — |
