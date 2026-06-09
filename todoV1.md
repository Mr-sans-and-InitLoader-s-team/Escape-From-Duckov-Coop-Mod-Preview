# 联机稳定性修复 Todo V1

生成日期：2026-06-09

## 目标

这份 Todo 用来承接 `CODEBASE_AUDIT.md` 和 PR #1 的后续修复计划。优先级按“先稳定主链路，再补安全边界，再做工程卫生”的顺序排列，避免每轮修复互相打架。

## 状态标记

- `[ ]` 未开始
- `[~]` 进行中
- `[x]` 已完成
- `[blocked]` 被外部条件阻塞

## P0：合入并实测 PR #1

目标：确认当前联机稳定性补丁在真实游戏环境里站得住。

任务：

- [ ] 合入 PR #1：`[codex] 修复联机稳定性链路`
- [ ] LAN 实测：主机创建、客户端手动 IP 连接、断开重连、端口占用失败提示
- [ ] Steam 实测：创建房间、刷新房间、点击加入、密码错误、加入超时、成功连接
- [ ] 场景实测：Base 到 raid、同图可见队友、换图后异图代理清理、再同图重新创建
- [ ] AI 实测：主机进 raid 后敌人正常生成，客户端不本地刷怪但能收到 AI 快照
- [ ] 迟加入实测：客户端加入后能看到已存在玩家和 AI
- [ ] 回归 smoke：loot、伤害、聊天、天气/时间同步各跑一次

验收标准：

- 点击加入房间每个失败路径都有 UI 状态反馈。
- 主机/客户端不会在 LiteNetLib 启动失败后进入假联网状态。
- 同图玩家代理能创建、更新、清理。
- 客户端能稳定收到 AI 快照，敌人不因初始化竞态消失。

## P1：服务端权威校验，Loot + Damage

目标：补上 audit 里最高风险的未修项，减少坏包、误操作和半公开房间作弊风险。

本轮进度（PR #1 追加）：已实现“低误伤、强诊断、基础权威”的 Loot + Damage 服务端校验，不改变现有 RPC opcode 和 wire format。Loot 已覆盖 open/take/put/slot/split 的同图、距离、viewer、slot、item id、stack count 与快照体量校验；Damage 已覆盖 sender/target、同图、friendly fire、payload 数值、远距离近期攻击事件关联与 `[DAMAGE_AUTH]` 拒绝日志。完整库存版本、客户端物品来源账本和更严格投射物命中关联留到后续 PR。

任务：

- [~] 明确并记录当前 trust model：好友房、半公开房、公网房分别接受哪些风险
- [x] Loot open/take/put 前校验玩家与 lootbox 同场景
- [x] Loot open/take/put 前校验玩家与 lootbox 距离
- [x] Loot take/put 前校验 sender 是该 loot inventory 的注册 viewer
- [~] Loot take/put 前校验库存版本、源格子、目标格子、item id、stack count 合法
- [x] Loot 校验失败时返回 deny，并打轻量日志，不崩溃、不静默吞
- [x] Damage 请求前校验攻击者与目标同场景
- [x] Damage 请求前校验 friendly fire 状态
- [~] Damage 请求前关联近期服务端观察到的武器、投射物、近战事件
- [x] Damage 校验失败时拒绝并记录原因
- [x] 增加 loot/damage 诊断日志前缀，例如 `[LOOT_AUTH]`、`[DAMAGE_AUTH]`

建议拆分：

- PR 1：Loot 基础校验
- PR 2：Damage 基础校验
- PR 3：更强的近期事件关联与调参

验收标准：

- 非 viewer 不能远程 take/put。
- 异图玩家不能操作 loot 或伤害目标。
- 明显越距请求被拒绝。
- 正常好友 co-op loot/伤害流程不被破坏。

## P2：启动幂等与生命周期清理

目标：减少重载 mod、重进房、断线重连后的脏状态。

任务：

- [ ] `ModBehaviour.OnEnable()` 增加全局幂等保护，避免重复创建持久对象
- [ ] `COOPManager.InitManager()` 增加重复初始化保护或显式 Reset 语义
- [ ] `StopNetwork()` 清理 Scene、AI、Loot、Mod API、FriendlyFire、Weather、ExitSync 等 per-session 状态
- [ ] Direct 与 SteamP2P 模式切换时明确离开 lobby、清 endpoint mapper、清 pending join
- [ ] 断线重连时清理本地 remote proxy、AI replica、loot viewer、scene ready 状态
- [ ] 启动/停止日志统一走 `[NET_STATE]`，只打关键状态变化
- [ ] 给“重载 mod / 回到主菜单 / 再进地图 / 再联机”写一份手动测试脚本

验收标准：

- 重复启用不会生成多个 `COOP_MOD_1` / `COOP_MOD_`。
- 断线后重新连接不继承上一局的玩家、AI、loot、scene gate 状态。
- Steam/LAN 模式切换不会残留旧连接状态。

## P3：RPC 和消息输入防护

目标：PR #1 已经保护 receive 边界，这一轮继续保护高风险消息本身。

任务：

- [ ] 给 RPC 反序列化定义统一上限常量：数组长度、字符串长度、payload 大小
- [ ] Player status 消息增加玩家数量、装备数量、武器数量上限
- [ ] AI snapshot/state 消息增加 chunk 数量和单条字段长度上限
- [ ] Loot snapshot/delta 消息增加 item 数量、字符串长度、版本范围检查
- [ ] Mod API payload 增加 channel 长度和 payload size 上限
- [ ] Scene 消息增加 scene id、curtain guid、participant 数量上限
- [ ] 超限包记录 opcode、peer、字段名，然后丢弃
- [ ] 保持现有 opcode 和 wire format 不变，除非明确 bump `BuildInfo.ModVersion`

验收标准：

- 畸形或超大包不会导致 receive handler 抛出未捕获异常。
- 正常游戏数据不会被误判为超限。
- 日志能定位哪个 opcode、哪个字段触发拒绝。

## P4：自动化 Smoke 与诊断工具

目标：没有 Unity Editor 的情况下，也能让后续 PR 有基础回归信心。

任务：

- [ ] 增加 `scripts/build.ps1`，固定 `DUCKOV_GAME_DIRECTORY` 和 `.build/Mods` 输出
- [ ] 增加 `scripts/validate-localization.ps1`，解析 `Localization/*.json`
- [ ] 增加 `scripts/check-rpc-static.ps1`，扫描 opcode、handler、重复 reader recycle 等静态风险
- [ ] 增加 Mod API replay 的轻量单元或集成测试入口
- [ ] 增加 RPC serialize/deserialize round-trip 的非 Unity 测试样例
- [ ] 增加运行时日志说明文档，列出 `[NET_STATE]`、`[STEAM_JOIN]`、`[PLAYER_SYNC]`、`[AI_SYNC]`
- [ ] 建立手动测试清单：LAN、Steam、场景、AI、loot、伤害、聊天、天气/时间

验收标准：

- 新 PR 至少能跑 build + localization 校验。
- 常见联机问题有对应日志前缀可 grep。
- 手动测试清单能被复用，不靠临场记忆。

## P5：Localization 改真实 JSON Parser

目标：替换手写 `IndexOf` / `Substring` JSON 解析，避免合法 JSON 被误读。

任务：

- [ ] 用 `Newtonsoft.Json` 解析 localization 文件
- [ ] 保持当前 JSON 文件结构兼容
- [ ] 增加重复 key 检测
- [ ] 增加格式占位符检测，例如 `{0}`、`{1}` 是否跨语言一致
- [ ] 保留语言切换和 fallback 行为
- [ ] 用现有全部语言文件跑校验

验收标准：

- 包含转义引号、反斜杠、花括号的合法 JSON 字符串能正确读取。
- 所有当前 localization 文件可解析。
- 语言 fallback 行为不变。

## P6：Warning 清理与工程卫生

目标：把 build warning 降到足够少，让新 warning 重新变得显眼。

任务：

- [ ] 处理 nullable annotation 与 nullable context 不一致
- [ ] 清理 `COOPManager.StripAllHandItems()` 中的 unreachable code
- [ ] 删除确认废弃的 unused / never assigned fields
- [ ] 保留 Unity 序列化或反射需要的字段，并加注释说明
- [ ] 评估两个 csproj 的游戏 DLL 引用去重或集中化
- [ ] 将 warning count 写入 PR 验证说明

验收标准：

- warning 数量明显下降。
- 没有删除 Unity/Harmony/反射实际依赖的字段。
- 新增 warning 能在 CI/本地构建中被快速注意到。

## 建议执行顺序

1. P0：合入并实测 PR #1
2. P1-A：Loot 基础服务端校验
3. P1-B：Damage 基础服务端校验
4. P2：启动幂等与生命周期清理
5. P3：RPC/message 输入防护
6. P4：构建、JSON、RPC smoke 工具
7. P5：Localization parser
8. P6：warning 清理与工程卫生

## 每轮 PR 的默认完成定义

- [ ] 不改变现有 RPC opcode 和 wire format，除非明确说明并 bump 版本
- [ ] `dotnet build EscapeFromDuckovCoopMod.sln -c Release --no-restore` 通过
- [ ] `Localization/*.json` 解析通过
- [ ] 说明 warning 数量是否变化
- [ ] 至少完成相关手动 smoke test，或说明本机无法执行的原因
- [ ] PR 描述包含：改了什么、为什么、怎么验证、剩余风险
