# FFM 数据模型与配置契约 (`docs/contracts/data_models.md`)

## 1. 选手属性模型 (`PlayerDef`)
所有核心属性刻度为 `1.0 – 20.0`（支持训练小数累积），`form` 为 `-3.0 .. +3.0`，`fatigue` 为 `0.0 .. 100.0`。

### JSON 字段契约
- `playerId`: `string`
- `name`: `string`
- `age`: `int` (16–30)
- `region`: `string` (`SEA | BR | MEA | VN | LATAM`)
- `mechanical`:
  - `aim`: 中距离瞄准精度
  - `recoil`: 连射后坐力控制
  - `reflex`: 遭遇战反应先手
  - `movement`: 移动速度与规避被命中率
  - `cqc`: 室内与 25m 内近战精度
  - `longRange`: 70m 外远距离抽靶精度
- `tactical`:
  - `awareness`: 敌情发现概率、先手分与威胁感知
  - `positioning`: 交火选掩体质量（softmax 温度）
  - `utility`: Gloo 墙放置率/角度质量与投掷物精度
  - `looting`: 物资搜集速度与高阶装备获取率
  - `decision`: IGL 效用评估噪声抑制与赛中指令理解
  - `comms`: IGL 指令下达延迟抑制与赛中指令响应
- `mental`:
  - `composure`: 决赛圈（EndGame）抗高压稳定性
  - `teamwork`: 补枪（Trade kill）触发率与倒地扶起速度
  - `determination`: 局外周训练成长倍率
- `hidden`:
  - `consistency`: 跨局正常发挥概率（抑制 `offDay`）
  - `bigMatch`: 总决赛（`isFinals`）额外状态加成
  - `injuryProne`: 高疲劳伤病风险系数
- `condition`:
  - `stamina`: 抑制每局疲劳增长并提升局内抗疲劳阈值
  - `form`: 近期竞技状态（`-3..+3`）
  - `fatigue`: 当前疲劳值（`0..100`）
- `roleProficiency`: `Record<Role, float>`
- `skillProficiency`: `Record<SkillId, float>`

## 2. 队伍与阵容模型 (`TeamDef` / `LineupDef` / `PlaybookDef`)
### JSON 字段契约
- `teamId`, `name`, `region`, `colorHex`, `synergy` (`0..100`), `players` (`PlayerDef[]`)
- `lineup`:
  - `starters`: 固定 4 名首发（`playerId`, `role`, `active`, `passives`）
  - `sub`: 最多 1 名替补 `playerId`
  - `igl`: 队内指挥 `playerId`（必须在 `starters` 中）
- `playbook`:
  - `drop`: `Hot | Cold | Flex`
  - `rotation`: `Early | Edge | Late`
  - `engage`: `Contest | Balanced | Avoid`
  - `endgame`: `HighGround | Building | GlooFort`

### 校验规则（`ConfigValidator`）
1. 首发必须恰好 4 人且不重复；替补 ≤ 1 人且不与首发重叠。
2. 4 名首发的主动技能（`active`）必须在队内唯一。
3. 每名首发 1 个主动技能 + 最多 3 个不重复被动技能（`passives`）。
4. 4 名首发中至少 3 人与队伍同赛区（`player.region == team.region`）。
5. `igl` 必须是 4 名首发之一。
