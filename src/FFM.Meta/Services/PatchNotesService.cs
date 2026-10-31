using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class PatchNotesService
    {
        private readonly List<PatchModifierDef> _patches = new()
        {
            new PatchModifierDef
            {
                PatchVersion = "v1.0",
                Title = "v1.0 · FFWS 全球标准竞技版本",
                Summary = "步枪、冲锋枪、霰弹枪与连狙均衡并存，五大体系主动技能冷却处于基准线。",
                WeaponCategoryDamageMultipliers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AR"] = 1.00f,
                    ["SMG"] = 1.00f,
                    ["SG"] = 1.00f,
                    ["SR"] = 1.00f,
                    ["DMR"] = 1.00f
                },
                SkillCooldownMultipliers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Dash"] = 1.00f,
                    ["Shield"] = 1.00f,
                    ["Heal"] = 1.00f,
                    ["Scan"] = 1.00f,
                    ["Deploy"] = 1.00f
                },
                TacticalHighlights = new List<string>
                {
                    "标准竞技平衡：Hot/Cold/Flex 三类跳点收益均衡。",
                    "凝冰墙基础耐久与手雷破墙倍率保持标准。"
                }
            },
            new PatchModifierDef
            {
                PatchVersion = "v1.1",
                Title = "v1.1 · 疾风冲锋版本 (SMG & Dash Buff)",
                Summary = "冲锋枪与霰弹枪近战伤害提升 +8%，突进类主动技能冷却缩短 12%，利好双突击强冲体系。",
                WeaponCategoryDamageMultipliers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AR"] = 0.98f,
                    ["SMG"] = 1.08f,
                    ["SG"] = 1.08f,
                    ["SR"] = 0.96f,
                    ["DMR"] = 0.97f
                },
                SkillCooldownMultipliers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Dash"] = 0.88f,
                    ["Shield"] = 1.05f,
                    ["Heal"] = 1.00f,
                    ["Scan"] = 0.95f,
                    ["Deploy"] = 1.00f
                },
                TacticalHighlights = new List<string>
                {
                    "SMG / SG 贴脸爆发增强，Rusher 与 SecondRusher 价值上升。",
                    "Tatsuya 冲刺技能冷却缩短，凝冰墙推进节奏加快。"
                }
            },
            new PatchModifierDef
            {
                PatchVersion = "v1.2",
                Title = "v1.2 · 铁壁高地版本 (DMR/SR & Shield Buff)",
                Summary = "射手步枪与狙击枪远距离伤害提升 +9%，护盾与治疗光环冷却缩短 10%，高坡架枪与阵地战收益上升。",
                WeaponCategoryDamageMultipliers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AR"] = 1.03f,
                    ["SMG"] = 0.95f,
                    ["SG"] = 0.95f,
                    ["SR"] = 1.09f,
                    ["DMR"] = 1.09f
                },
                SkillCooldownMultipliers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Dash"] = 1.06f,
                    ["Shield"] = 0.90f,
                    ["Heal"] = 0.90f,
                    ["Scan"] = 1.00f,
                    ["Deploy"] = 0.92f
                },
                TacticalHighlights = new List<string>
                {
                    "DMR / SR 远点抽靶统治力上升，Sniper 与 HighGround 决赛圈策略受益。",
                    "Chrono 护盾与 Alok/Dimitri 治疗体系续航强化。"
                }
            }
        };

        public IReadOnlyList<PatchModifierDef> GetAllPatches() => _patches;

        public PatchModifierDef GetPatchForSeason(int season)
        {
            int idx = Math.Max(0, season - 1) % _patches.Count;
            return _patches[idx];
        }

        public PatchModifierDef GetPatchByVersion(string version)
        {
            for (int i = 0; i < _patches.Count; i++)
            {
                if (string.Equals(_patches[i].PatchVersion, version, StringComparison.OrdinalIgnoreCase))
                {
                    return _patches[i];
                }
            }
            return _patches[0];
        }

        public void ApplyPatchToWeaponsAndSkills(WeaponsConfig weapons, SkillsConfig skills, PatchModifierDef patch)
        {
            if (weapons == null || skills == null || patch == null) return;

            for (int i = 0; i < weapons.Weapons.Count; i++)
            {
                var w = weapons.Weapons[i];
                if (patch.WeaponCategoryDamageMultipliers.TryGetValue(w.Category, out float mult))
                {
                    w.Damage = MathF.Round(w.Damage * mult * 10f) / 10f;
                }
            }

            for (int i = 0; i < skills.Skills.Count; i++)
            {
                var s = skills.Skills[i];
                if (patch.SkillCooldownMultipliers.TryGetValue(s.Category, out float cdMult))
                {
                    s.CooldownSec = MathF.Round(s.CooldownSec * cdMult * 10f) / 10f;
                }
            }
        }
    }
}
