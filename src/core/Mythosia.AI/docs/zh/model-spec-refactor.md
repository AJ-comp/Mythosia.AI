# [To-Be] 模型元数据（ModelSpec）重构提案

## 背景

- 当前 `AIService.Model` 以 `string` 保持。
- 各服务通过字符串比较判断模型功能和令牌限制。
- 添加新模型很快，但逻辑分散，维护成本不断上升。

## 目标

- **灵活性**: 原样接受未注册/自定义模型字符串
- **集中化**: 在一处管理已知模型的元数据
- **兼容性**: 最小化对现有API/行为的破坏性变更

## 提案结构（混合方式）

- `Model` 保持为 **string**
- 引入 **ModelSpec（元数据）** 作为可选项
- 仅将已知模型resolve为ModelSpec
- 未知字符串 **按现有逻辑** 处理

### ModelSpec字段示例（草案）

- ModelId (string)
- Provider
- MaxOutputTokens（或 MaxTokensLimit）
- Capabilities
  - Vision支持
  - Reasoning/Thinking支持
  - Function Calling支持
- 可选默认值: DefaultMaxTokens 等

> 注: 提供商特有配置（ReasoningEffort, ThinkingBudget等）仍 **保留在服务类中**。

## 解析规则

1. 设置 `Model` 时，尝试通过 **ModelSpecRegistry** 解析
2. **已解析** → 基于ModelSpec应用capability/limit
3. **未解析** → 使用现有的各服务字符串解析逻辑
4. 未知字符串也必须正常工作

## 预期效果

- 已知模型元数据集中化 → **提高可维护性**
- 保持自定义模型的 **灵活性**
- 支持渐进式迁移

## 迁移草案

1. 引入 `ModelSpec`/`ModelSpecRegistry` 骨架
2. 逐步注册已知模型
3. 在各服务解析逻辑中添加 **Spec优先 + 回退**
4. 加强测试（已知模型元数据、未知字符串路径）

## 待讨论

- 为未知模型提供用户自定义元数据的方式（可选）
- 别名模型处理策略
- Spec中应包含的最小元数据范围
