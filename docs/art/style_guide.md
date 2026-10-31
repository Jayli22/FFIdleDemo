# Free Fire 电竞战队经理 · 视觉风格指南与资产预算规范 (WP-17)

## 1. 核心视觉支柱
- **电竞转播风 (Broadcast Clean)**：高对比度深灰底 UI (`#0E1116` / `#161B22`)，配以亮色系战术强调色（FF 橙 `#FF9F1C`、冠军金 `#FFD166`、危险红 `#EF476F`）。
- **低多边形战术沙盘 (Low-Poly Tactical Diorama)**：地形采用清晰的高度层级染色（0/1/2 三层高地色阶明确区分），建筑物采用顶面半透明可消隐的简洁几何体，保证在鸟瞰（TopDown）与跟随（Follow）视角下一眼看懂掩体、高地与视线遮挡。
- **12 支同场战队高辨识色板**：每支参赛队伍拥有唯一主色 (`ColorHex`)，通过 Godot `GeometryInstance3D.SetInstanceShaderParameter("team_color", ...)` 注入角色材质与头顶銘牌，杜绝为每支战队复制材质实例。

## 2. 18 支联赛队伍标准色板
| 战队 ID | 队名 | 赛区 | 标准色值 |
|---|---|---|---|
| `t_evos` | EVOS Divine | SEA | `#2274A5` |
| `t_buriram` | Buriram United | SEA | `#003F88` |
| `t_fluxo` | Fluxo Esports | BR | `#7B2CBF` |
| `t_loud` | LOUD GG | BR | `#00CC66` |
| `t_falcons` | Team Falcons | MEA | `#2EC4B6` |
| `t_rrq` | RRQ Kazu | SEA | `#FF9F1C` |
| `t_onic` | ONIC Olympus | SEA | `#F7B801` |
| `t_cggg` | CGGG Thailand | SEA | `#E71D36` |
| `t_pain` | paiN Gaming | BR | `#D90429` |
| `t_corinthians` | Corinthians FF | BR | `#8D99AE` |
| `t_wag` | WAG Esports | VN | `#FB5607` |
| `t_pbox` | PBox Flash | VN | `#FF006E` |
| `t_indostars` | Indostars | SEA | `#3A86FF` |
| `t_attack` | Attack All Around | SEA | `#8338EC` |
| `t_magic` | Magic Squad | BR | `#06D6A0` |
| `t_allglory` | All Glory Gaming | LATAM | `#118AB2` |
| `t_ignite` | Ignite Esports | LATAM | `#EF476F` |
| `t_alpha` | Alpha7 Lite | BR | `#8AC926` |

## 3. 资产性能与命名硬性预算 (Asset Lint Rules)
1. **单模型面数上限**：任何 `.obj` / `.gltf` / `.glb` 模型网格不得超过 **3,000 Triangles**（角色基础 Capsule/Low-Poly 建议 ≤ 800 Tris，建筑体块 ≤ 400 Tris）。
2. **贴图尺寸上限**：所有纹理贴图最大尺寸不超过 **2048×2048**（单文件字节上限 ≤ 4 MB）。
3. **材质球与实例化规范**：全工程共享材质数量受控，禁止按战队硬编码独立材质文件（如 `team_1.tres` ~ `team_18.tres`），战队颜色统一走 Per-Instance Uniform (`instance uniform vec4 team_color`)。
4. **三级 LOD 渲染规范**：
   - `LOD0` (`CameraDistance < 80m`)：完整 3D 胶囊角色 + 武器朝向 + 冰墙/弹道特效 + 完整 HUD 血条/护甲/技能冷却环。
   - `LOD1` (`80m <= CameraDistance <= 200m`)：简化角色体块 + 姓名与血条 + 战队色标。
   - `LOD2` (`CameraDistance > 200m`)：纯 2D/3D 战术队标圆点 + 存活编号，隐藏 3D 骨骼/武器细节与高频粒子。
