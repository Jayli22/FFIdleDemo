# FFM 交火、状态与技能公式契约 (`docs/contracts/combat.md`)

> 所有代码实现旁必须标注对应公式编号（`CMB-01` ~ `CMB-12`）。

## CMB-01：遭遇发现判定（每 tick）
当两支不同小队的存活单位 $A, B$ 距离 $d \le \text{weapon.rangeMeters}$ 且网格视线通过（`LineOfSight` 无高地阻挡且无 Gloo 墙阻挡）时：
$$p_{\text{detect}} = \text{clamp}\big(\text{detectionBasePerTick} + \text{Awareness} \times \text{detectionAwarenessScale} \times (1 - 0.5 \cdot \text{targetCover}), 0.02, 0.95\big)$$

## CMB-02：遭遇先手分（Initiative Score）
$$\text{score}_{\text{init}} = 0.5 \cdot \text{Awareness} + 0.3 \cdot \text{Reflex} + 10.0 \cdot \text{posAdv} + \mathcal{N}(0, 2.0)$$
其中 $\text{posAdv} = \max(0, \Delta\text{height}) \times 0.3 + \text{cover} \times 0.4$。高分者获得当前交火回合（5 ticks）的先手开火权。

## CMB-03：距离段有效瞄准（`effAim`）与职业修正（`roleMod`）
- 近距（$d \le 25\text{m}$ 或双方处于室内 cell）：
  $$\text{effAim} = 0.4 \cdot \text{Aim} + 0.6 \cdot \text{CQC}$$
- 中距（$25\text{m} < d \le 70\text{m}$）：
  $$\text{effAim} = \text{Aim}$$
- 远距（$d > 70\text{m}$）：
  $$\text{effAim} = 0.4 \cdot \text{Aim} + 0.6 \cdot \text{LongRange}$$
- 职业熟练度修正：
  $$\text{roleMod} = 0.90 + \text{roleProficiency}[\text{role}] \times 0.008$$

## CMB-04：决赛圈抗压修正（`pressureMod`）
- 当处于 `Phase.EndGame`（或场上存活小队 $\le 4$）时：
  $$\text{pressureMod} = 1.0 - \max(0, 16.0 - \text{Composure}) \times 0.015$$
- 其他阶段：$\text{pressureMod} = 1.0$。

## CMB-05：单发命中率与后坐力衰减（`p_hit`）
- 基础命中率：
  $$p_{\text{base}} = \text{weapon.GetBaseAccuracy}(d) \times \left(0.5 + \frac{\text{effAim}}{40.0}\right) \times \text{roleMod} \times (1 - 0.6 \cdot \text{targetCover}) \times \left(1 - \frac{\text{targetMovement} - 10.0}{60.0}\right) \times \text{heightMod} \times \text{pressureMod} \times \text{stabilityMod}$$
- 连射第 $n$ 发（$n \ge 0$）后坐力衰减：
  $$p_{\text{hit}}(n) = \text{clamp}\left(p_{\text{base}} \times \left(1 - n \cdot \frac{20.0 - \text{Recoil}}{400.0}\right), 0.02, 0.95\right)$$

## CMB-06：单发伤害与护甲减免
$$\text{dmg} = \text{weapon.damage} \times (1.0 - \text{armorTier} \times 0.12) \times \text{shieldSkillMod}$$

## CMB-07：补枪反击（Trade Kill）
当队友在 $20\text{m}$ 内被击倒时，存活且有视线的队友立即触发一次额外反击开火的概率：
$$p_{\text{trade}} = \text{clamp}\left(0.15 + \frac{\text{Teamwork}}{60.0} + \frac{\text{synergy}}{300.0} + \frac{\text{roleProficiency}[\text{SecondRusher}]}{100.0}, 0.10, 0.85\right)$$

## CMB-08：击倒流血与扶起耗时
- 击倒后倒地流血上限：$30.0\text{s}$（300 ticks）。
- 队友在 $3.5\text{m}$ 内且未受攻击时执行扶起所需秒数：
  $$\text{reviveSec} = \text{clamp}\big(6.0 - (\text{Teamwork} - 10.0) \times 0.12 - (\text{role} == \text{Support} ? 1.0 : 0.0), 3.5, 7.5\big)$$

## CMB-09：跨局发挥稳定性（`Consistency` & `BigMatch`）
- 每局开局判定是否正常发挥：$p_{\text{normal}} = \text{clamp}(\text{Consistency} / 25.0, 0.25, 0.96)$。
- 若未通过判定则触发 `offDay = true`：机械组全部属性与 `Composure` 临时扣减 $2.0 + \text{Uniform}(-1.0, 1.0)$。
- 若 `context.isFinals == true`：全属性叠加 $(\text{BigMatch} - 10.0) \times 0.30$。
- 选手近期状态修正：全属性叠加 $\text{form} \times 0.35$。

## CMB-10：跨局疲劳累积与体能阈值（`Stamina` & `Fatigue`）
- 每局结束新增疲劳：
  $$\Delta\text{fatigue} = \max(2.0, 11.0 - \text{Stamina} \times 0.35)$$
- 局内疲劳惩罚起效阈值：
  $$\text{fatigueThreshold} = 25.0 + \text{Stamina} \times 1.0$$
- 当 $\text{fatigueIn} > \text{fatigueThreshold}$ 时，`Reflex`、`Aim`、`Awareness` 扣减：
  $$\text{penalty} = \frac{\text{fatigueIn} - \text{fatigueThreshold}}{12.0}$$

## CMB-11：Gloo 墙触发与角度质量（`Utility`）
- 被攻击且当前掩体 $\text{cover} < 0.3$ 且持有 Gloo 墙时，每回合放置概率：
  $$p_{\text{gloo}} = \text{clamp}\left(0.60 \times \frac{\text{Utility}}{20.0}, 0.08, 0.85\right)$$
- 封墙提供的有效掩体值：$\text{glooCover} = 0.65 + 0.25 \times (\text{Utility} / 20.0)$（最高 0.90），持续时间 $25\text{s}$。

## CMB-12：破障手雷触发与伤害（`Utility` & `Grenadier`）
- 交火目标处于高掩体（$\text{cover} \ge 0.65$ 或 Gloo 墙后）且距离 $12\text{m} \le d \le 48\text{m}$ 时触发概率：
  $$p_{\text{throw}} = \text{clamp}\big(0.18 + (0.5 \cdot \text{Utility} + 0.5 \cdot \text{roleProficiency}[\text{Grenadier}]) \times 0.025, 0.10, 0.75\big)$$
- 爆炸有效伤害：$\text{dmg}_{\text{grenade}} = \text{baseDamage} \times (0.55 + (0.5 \cdot \text{Utility} + 0.5 \cdot \text{roleProficiency}[\text{Grenadier}]) / 25.0)$，并对范围内的敌方 Gloo 墙造成破坏。
