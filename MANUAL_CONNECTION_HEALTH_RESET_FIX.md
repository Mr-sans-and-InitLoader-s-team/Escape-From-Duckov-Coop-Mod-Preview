# 手动连接血量重置为40滴血问题修复

## 问题描述
手动点击连接后，客机玩家的血量会被重置为40滴血，无论之前血量有多高。

## 问题根因分析

通过代码分析发现，问题出现在两个关键位置：

### 1. HealthTool.cs - Server_HookOneHealth 方法
```csharp
// 2) 否则读取当前值；若 Max<=0（常见于克隆且 autoInit=false），用兜底 40f 起条并广播
float max = 0f, cur = 0f;
try
{
    max = h.MaxHealth;
}
catch
{
}

try
{
    cur = h.CurrentHealth;
}
catch
{
}

if (max <= 0f)  // ← 问题在这里
{
    max = 40f;  // ← 硬编码重置为40
    if (cur <= 0f) cur = max;
}
```

### 2. HealthM.cs - ApplyHealthAndEnsureBar 方法
```csharp
// 先把数值灌进去（内部会触发 OnMax/OnHealth）
ForceSetHealth(h, max > 0 ? max : 40f, cur > 0 ? cur : max > 0 ? max : 40f, false);
//                        ↑ 这里也有40f的兜底逻辑
```

## 问题触发流程

1. 客户端手动点击连接
2. 服务器接收到新连接，调用 `OnPeerConnected`
3. 服务器为客户端角色调用 `HealthTool.Server_HookOneHealth`
4. 由于客户端角色刚连接，`h.MaxHealth` 可能返回0或无效值
5. 触发 `if (max <= 0f)` 条件，将血量重置为40f
6. 通过 `ApplyHealthAndEnsureBar` 应用到客户端角色
7. 客户端看到自己血量变成40

## 修复方案

### 方案1：优先使用客户端自报的血量数据

修改 `HealthTool.cs` 中的 `Server_HookOneHealth` 方法，在使用兜底40f之前，先等待客户端自报血量：

```csharp
// 2) 否则读取当前值；若 Max<=0，先等待客户端自报，而不是立即使用兜底40f
float max = 0f, cur = 0f;
try
{
    max = h.MaxHealth;
}
catch
{
}

try
{
    cur = h.CurrentHealth;
}
catch
{
}

// 修改：只有在客户端是null（即主机自己）时才使用兜底40f
// 对于客户端，应该等待其自报血量数据
if (max <= 0f)
{
    if (peer == null) // 主机自己
    {
        max = 40f;
        if (cur <= 0f) cur = max;
    }
    else // 客户端，等待自报数据
    {
        // 不立即设置兜底值，等待客户端的 PLAYER_HEALTH_REPORT
        return;
    }
}
```

### 方案2：改进血量同步时机

修改连接流程，确保在血量同步之前，客户端已经发送了自己的血量状态：

1. 客户端连接成功后，立即发送当前血量状态
2. 服务器收到血量报告后，再进行血量同步
3. 避免在没有准确血量数据时使用兜底值

### 方案3：保守修复 - 提高兜底血量值

如果不想大幅修改逻辑，可以将兜底血量从40f改为更合理的值，比如100f：

```csharp
if (max <= 0f)
{
    max = 100f; // 从40f改为100f
    if (cur <= 0f) cur = max;
}
```

## 推荐修复方案

推荐使用**方案1**，因为它从根本上解决了问题：

1. 保留主机自己的兜底逻辑（因为主机血量应该是可靠的）
2. 对于客户端，等待其自报血量数据，避免使用不准确的兜底值
3. 确保血量同步的准确性

## 实施步骤

1. 修改 `HealthTool.cs` 中的 `Server_HookOneHealth` 方法
2. 确保客户端在连接后立即发送血量报告
3. 测试手动连接场景，验证血量不再被重置为40

## 测试验证

1. 客户端血量设置为非40的值（如80、120等）
2. 手动点击连接到服务器
3. 验证连接后血量保持原值，不被重置为40
4. 验证血量同步功能正常工作