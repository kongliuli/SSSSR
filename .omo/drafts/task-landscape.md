# 审批草稿 — 任务全景图

**status: awaiting-approval**

## 决策记录

| 决策 | 选择 |
|------|------|
| Topology | 5 组件（Global消除 / 警告清零 / 测试体系 / TODO补齐 / 架构加固） |
| 执行顺序 | 全并行（worker 自行选择起始组件） |
| 管线独立化 | 完整提取：`Proxy/` + `Encryption/` + `Obfs/` → `Shadowsocks.Proxy.Core` 独立库 |
| 测试策略 | 混合：新代码 TDD，旧代码 tests-after |
| 计划范围 | 一份 `.omo/plans/task-landscape.md` 覆盖全部 5 组件 |

## 待执行动作

`write .omo/plans/task-landscape.md`（使用 scaffold 脚本 + 追加 todos）

## 计划概要

### 组件 1：Global.cs 静态状态消除
- **输入**：56 处 Global 引用分布在 12 个文件中
- **输出**：0 处 Global 引用；`[Obsolete]` 字段可安全删除
- **关键路径**：代理管线解耦 (Handler/IPRangeSet/DnsUtil) → UpdateNodeChecker/SubscribeManager DI → SaveConfig/Load 持久化服务 → 桥接删除

### 组件 2：编译警告清零
- **输入**：81 个构建警告
- **输出**：核心警告归零（CS0618 随组件 1 完成自然消除）
- **关键路径**：VSTHRD002(死锁) → VSTHRD100(崩溃) → VSTHRD110 → 剩余低优先级

### 组件 3：测试体系构建
- **输入**：1 个测试项目，15 个测试方法，0 覆盖，无 CI 门控
- **输出**：CI 测试门控 + Coverlet 覆盖率 + Model/VM/Controller 核心测试 + 代理管线集成测试
- **关键路径**：CI 管道 → Coverlet → Mocking → 核心层测试 → 管线集成测试

### 组件 4：TODO/功能补齐
- **输入**：4 个 TODO 标记
- **输出**：全部解决
- **关键路径**：SOCKS5 验证(安全) → 错误翻译(UX) → 拖放矩阵(功能) → UDP Socket(新功能)

### 组件 5：架构加固 — 代理管线独立化
- **输入**：`Proxy/`(10 文件) + `Encryption/`(11 文件含子目录) + `Obfs/`(27 文件) — 与主项目紧耦合
- **输出**：`Shadowsocks.Proxy.Core` 独立 .NET 库 + 独立测试项目，主项目通过 NuGet/项目引用消费
- **TL;DR**：提取 → 接口抽象 → DI 注入 → 独立测试 → 主项目适配
