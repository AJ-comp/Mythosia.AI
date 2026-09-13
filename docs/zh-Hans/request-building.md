# 让每个请求的设置互相独立

文档摘要可能需要较低的Temperature，创意草稿则需要较高的值。准备草稿不应悄悄改变已经准备好的摘要请求。当不同调用需要不同设置，或需要从基础请求派生多个版本时，请使用`CreateRequest`。

需要同时获取完整答案、用量和来源时，使用 `await run.Result` 返回的 `AIRunResult`，字符串位于 `result.Text`，无需读取流。这是Mythosia.AI 8.0.0 的 API 变更；`GetCompletionAsync` 与 `StructuredStreamRun<T>.Result` 的返回类型保持不变。 [Run 结果与迁移](execution-api-transition.md#run-result).

只需完整答案和停止按钮时，将 `cancellationToken` 传给 `GetCompletionAsync`。进度事件或受支持的中途追加指令使用 Run。参阅[取消回答](completions.md#completion-cancellation)。

> `CreateRequest`示例需要Mythosia.AI 8.0.0 / Abstractions 4.0.0。最初引入Run和公共请求功能的旧7.1版本不包含构建器；旧包可继续使用原有服务重载。

## Before：共享服务设置

现有服务的`WithTemperature`会修改服务并返回同一实例。以下两个变量引用同一个服务，因此后设置的值会作用于两者。这些旧方法仍可用于配置服务默认值。

```csharp
var summary = service.WithTemperature(0.2f);
var creative = service.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // true
string answer = await summary.GetCompletionAsync("请解释这份文档。"); // 0.8
```

## After：从独立请求派生

`CreateRequest`会保存服务默认值。构建器的每个`With...`都返回新的构建器，不修改原对象。执行时直接使用该请求保存的设置，不会临时覆盖服务默认值。

```csharp
using Mythosia.AI.Builders;

AIRequestBuilder basis = service.CreateRequest("请解释这份文档。");
AIRequestBuilder summary = basis.WithTemperature(0.2f);
AIRequestBuilder creative = basis.WithTemperature(0.8f);

bool same = ReferenceEquals(summary, creative); // false
string answer = await summary.GetCompletionAsync();
// 使用0.2；creative和服务默认值保持不变。
```

请使用返回的构建器。仅调用`basis.WithTemperature(0.2f);`并丢弃返回值，不会改变`basis`。

构建器验证输入而不会静默修正：`WithTemperature`范围为0–2，`WithTopP`为0–1，惩罚为−2–2，拒绝NaN和无穷值。Token数、轮数、并发数和指定的超时必须为正。无效输入抛出`ArgumentException` / `ArgumentOutOfRangeException`。旧服务的Temperature方法仍会限制到有效范围。

## 各对象的职责

`AIService`管理提供商连接、默认值和现有会话状态。公开类型`Mythosia.AI.Builders.AIRequestBuilder`提供fluent API，内部类型`AIRequest`将确定的输入和设置传给执行层。用户无需调用`Build()`。`AIRequest`也不是回答结果：`GetCompletionAsync()`返回`Task<string>`，`StartRunAsync()`返回`Task<AIRun>`。

```text
AIService.CreateRequest(input)
    → AIRequestBuilder.With...()
    → GetCompletionAsync() / StartRunAsync()
    → AIRequest
    → provider
    → string / AIRun
```

## 用相同设置启动可控制的运行

只需要完整回答时使用`GetCompletionAsync()`。需要显示进度或向支持的运行追加指令时，使用`StartRunAsync()`。输入传给`CreateRequest`，不再传给构建器的执行方法。`run.StreamAsync()`继续观察该运行，`run.SteerAsync(...)`的模型支持条件保持不变。

```csharp
await using var run = await service
    .CreateRequest("请解释这份文档。")
    .WithMaxTokens(2048)
    .WithMaxRounds(10)
    .StartRunAsync(
        onText: text => Console.Write(text),
        cancellationToken: cancellationToken);

string answer = (await run.Result).Text;
```

本地工具可以通过`Task<T>` / `ValueTask<T>`返回对象，并接收库注入的`CancellationToken`。`run.Cancel()`或启动令牌的取消会传递给配合取消的工具，仅停止流读取则不会。异常会记录为失败；取消时跳过排队调用，清理仍会等待已启动且忽略令牌的工具。参见[结果、错误与取消](function-calling.md#tool-execution-contract)。

[Run](execution-api-transition.md) · [Streaming](streaming.md)

## 复用配置档和上下文

`WithProfile`复制现有的`AIRequestProfile`，`WithContext`复制`AIRequestContext`。之后修改原对象不会影响已准备的请求。构建器可设置采样、系统指令、无状态模式、函数调用策略，以及受支持的推理、网页和文件搜索。提供商能力检查仍然适用，构建器不会使不支持的选项变得可用。

`WithFunctions(params FunctionDefinition[])`向请求添加复制的函数定义。导入`Mythosia.AI.Extensions`后，也可用`WithFunctions(toolInstance)`和`WithStaticFunctions<T>()`注册现有的特性函数。在`CreateRequest`之前注册的是服务默认值，之后注册的仅用于请求。服务待处理的下一次调用功能和策略在`CreateRequest`时被捕获并消费；需要复用时请保留返回的构建器。

```csharp
var request = service
    .CreateRequest("请将这个问题改写为适合搜索的形式。")
    .WithProfile(RequestProfiles.QueryRewrite)
    .WithContext(new AIRequestContext
    {
        SystemMessageSuffix = "\n请保留原意。"
    });

string rewritten = await request.GetCompletionAsync();
```

[AIRequestProfile](request-profiles.md) · [AIRequestContext](request-contexts.md) · [WithReasoning / WithWebSearch / WithFileSearch](reasoning-and-search.md)

## 哪些内容被复制，哪些状态仍然共享

公共设置和提供商默认值在调用`CreateRequest`时保存。之后更改服务默认值不会改变已准备的请求。内置消息内容、受支持的选项集合、配置档、上下文和策略都会复制。函数处理程序、动态上下文回调和自定义消息内容保留引用。请勿修改自定义内容，并注意委托仍可读取外部状态。动态上下文回调在执行时运行。

捕获完成后，即使释放原始 `JsonDocument` 或修改原始 `JsonNode`，请求元数据和函数调用参数中保存的 JSON 值也不会改变；每次执行都使用独立副本。工具架构的 `Items` 链存在循环或嵌套超过 64 层时，会在捕获阶段（`CreateRequest` 或 `WithFunctions`）抛出 `ArgumentException`，以便在执行前正常报告无效架构，而不是耗尽进程堆栈。

复制还会保留数组的维数和起始索引，以及标准 `Dictionary<,>`、`SortedDictionary<,>` 和 `SortedList<,>` 的键比较规则。因此，原本不区分大小写的键查找在请求中仍然如此。空的 `default(JsonElement)` 值（`Undefined`）也会原样保留。未知的自定义元数据对象仍保留引用，由其所有者负责保持不变或协调访问。

标准`ReadOnlyCollection<T>`和`ReadOnlyDictionary<TKey, TValue>`在强类型数组或字典中也会保留原始类型。复制支持的底层集合时，会保留只读视图、共享引用和循环引用。`Hashtable`及非泛型`SortedList`也会保留键比较规则。

构建器不是独立会话。它使用执行时服务的活动会话历史，创建时不会冻结历史。有状态调用仍会更新共享会话。不想读取或累积历史时，请使用`WithStatelessMode()`。每个服务只允许一个活动Run的限制保持不变。请求设置独立不代表同一服务支持并行执行；独立会话的并发任务应使用不同服务。

## 现有调用和扩展

`GetCompletionAsync`和现有服务入口继续受支持。`BeginMessage()` / `MessageChain`保留原来的可变消息构建方式，执行层使用新的请求路径。需要分支复用设置时请选择`CreateRequest`。构建器API属于`AIService`及其提供商实现，不会为`IAIService`新增必需成员。仅使用抽象接口或RAG包装器的代码继续使用现有配置档、上下文和执行API。

[用共享支持定义构建模型功能选项](model-capabilities.md).
