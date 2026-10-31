# FFM 模拟接口与快照契约 (`docs/contracts/simulation_api.md`)

## 1. `ISimulation` 接口契约
```csharp
public interface ISimulation
{
    void Init(MatchConfig cfg, ConfigBundle balance, MapData map);
    bool Step();                              // 推进 1 tick (0.1s)，翻转双缓冲快照，Finished 返回 false
    ISimSnapshot PrevSnapshot { get; }        // T-1 时刻只读快照（用于渲染帧插值起点）
    ISimSnapshot CurrSnapshot { get; }        // T 时刻只读快照（用于渲染帧插值终点）
    ReadOnlySpan<SimEvent> DrainEvents();     // 零分配消费新产生事件
    bool Issue(Command cmd, int squadIdx);    // 下达赛中指令
    MatchResult Result { get; }               // Finished 后读取结算结果
}
```

## 2. `ISimSnapshot` 与双缓冲零拷贝约束
- `Units`: `ReadOnlySpan<UnitDto>`（固定长度 48）
- `Squads`: `ReadOnlySpan<SquadDto>`（固定长度 12）
- `Walls`: `ReadOnlySpan<GlooDto>`（容量 64，按 `WallCount` 切片）
- `Projectiles`: `ReadOnlySpan<ProjectileDto>`（容量 32，按 `ProjectileCount` 切片）
- `Vehicles`: `ReadOnlySpan<VehicleDto>`（容量 16，按 `VehicleCount` 切片）
- `Engagements`: `ReadOnlySpan<EngagementDto>`（容量 12，按 `EngagementCount` 切片）
- `CopyTo(SnapshotBuffer target)`: 零堆内存分配深拷贝，供 `SimHost` 100 ticks（10s）环形回看缓冲使用。

## 3. 指令执行公式契约（§4.7）
- **Formula CMD-01（指令延迟 ticks）**：
  $$\text{delayTicks} = \text{round}\big(\text{baseDelayTicks} + (20 - \text{IGL.Decision}) \times k_1 + (20 - \text{IGL.Comms}) \times k_{\text{comms}} + (100 - \text{synergy}) \times k_2\big)$$
- **Formula CMD-02（指令执行概率）**：
  $$p_{\text{exec}} = \text{clamp}\big(p_0 + \text{IGL.Decision} \times k_3 + \text{IGL.Comms} \times k_4 + \text{synergy} \times k_5, 0.15, 0.99\big)$$
