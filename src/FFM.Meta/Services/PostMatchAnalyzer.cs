using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class PostMatchAnalyzer
    {
        public PostMatchReport AnalyzeMatch(MatchResult result, TeamDef focusTeam)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (focusTeam == null) throw new ArgumentNullException(nameof(focusTeam));

            SquadPlacementResult? placement = null;
            for (int i = 0; i < result.Placements.Count; i++)
            {
                if (result.Placements[i].SquadId == focusTeam.TeamId)
                {
                    placement = result.Placements[i];
                    break;
                }
            }

            placement ??= new SquadPlacementResult
            {
                SquadId = focusTeam.TeamId,
                Placement = 12,
                Kills = 0,
                PlacementPoints = 0,
                Points = 0,
                Booyah = false
            };

            var report = new PostMatchReport
            {
                FocusSquadId = focusTeam.TeamId,
                Placement = placement.Placement,
                Kills = placement.Kills,
                TotalPoints = placement.Points,
                Booyah = placement.Booyah
            };

            BuildKeyMoments(result, focusTeam, placement, report);
            BuildCausalAttributions(result, focusTeam, placement, report);

            return report;
        }

        private static void BuildKeyMoments(
            MatchResult result,
            TeamDef focusTeam,
            SquadPlacementResult placement,
            PostMatchReport report)
        {
            // 1. Prioritize moments directly involving focusTeam
            for (int i = 0; i < result.KeyMoments.Count && report.KeyMoments.Count < 3; i++)
            {
                var km = result.KeyMoments[i];
                if (km.SquadId == focusTeam.TeamId)
                {
                    report.KeyMoments.Add(new KeyMomentItem
                    {
                        Tick = km.Tick,
                        Title = $"[{km.Type}] 战队关键节点",
                        Description = km.Text,
                        WorldX = 500f,
                        WorldY = 500f
                    });
                }
            }

            // 2. Include other global highlight moments if needed
            for (int i = 0; i < result.KeyMoments.Count && report.KeyMoments.Count < 3; i++)
            {
                var km = result.KeyMoments[i];
                if (km.SquadId != focusTeam.TeamId)
                {
                    report.KeyMoments.Add(new KeyMomentItem
                    {
                        Tick = km.Tick,
                        Title = $"[{km.Type}] 全场焦点事件",
                        Description = km.Text,
                        WorldX = 500f,
                        WorldY = 500f
                    });
                }
            }

            // 3. Ensure exactly 3 key moments are always populated
            while (report.KeyMoments.Count < 3)
            {
                int idx = report.KeyMoments.Count;
                int fallbackTick = idx == 0 ? 150 : (idx == 1 ? 950 : (int)(result.DurationSec * 10f));
                string title = idx == 0
                    ? "落地与初期搜刮阶段"
                    : (idx == 1 ? "中期蓝圈转移交火" : $"终局定格 (第 {placement.Placement} 名)");
                string desc = idx == 0
                    ? $"{focusTeam.Name} 执行 {focusTeam.Playbook.Drop} 跳点策略完成初期整备。"
                    : (idx == 1
                        ? $"{focusTeam.Name} 按 {focusTeam.Playbook.Rotation} 节奏进圈，累计拿下 {placement.Kills} 个淘汰。"
                        : $"{focusTeam.Name} 本局最终排名 #{placement.Placement}，共斩获 {placement.Points} 积分。");

                report.KeyMoments.Add(new KeyMomentItem
                {
                    Tick = fallbackTick,
                    Title = title,
                    Description = desc,
                    WorldX = 500f,
                    WorldY = 500f
                });
            }
        }

        private static void BuildCausalAttributions(
            MatchResult result,
            TeamDef focusTeam,
            SquadPlacementResult placement,
            PostMatchReport report)
        {
            int totalDmg = 0;
            int totalKnocks = 0;
            int totalGloo = 0;
            int totalGrenades = 0;
            int totalSkills = 0;
            int maxSurvivedSec = 0;
            int offDayCount = 0;

            float avgAim = 0f, avgCqc = 0f, avgPositioning = 0f, avgUtility = 0f, avgDecision = 0f;
            int starterCount = 0;

            for (int i = 0; i < focusTeam.Lineup.Starters.Count; i++)
            {
                string pid = focusTeam.Lineup.Starters[i].PlayerId;
                var pDef = focusTeam.FindPlayer(pid);
                if (pDef != null)
                {
                    avgAim += pDef.Mechanical.Aim;
                    avgCqc += pDef.Mechanical.Cqc;
                    avgPositioning += pDef.Tactical.Positioning;
                    avgUtility += pDef.Tactical.Utility;
                    avgDecision += pDef.Tactical.Decision;
                    starterCount++;
                }

                if (result.PlayerStats.TryGetValue(pid, out var st))
                {
                    totalDmg += st.Dmg;
                    totalKnocks += st.Knocks;
                    totalGloo += st.Gloo;
                    totalGrenades += st.Grenades;
                    totalSkills += st.SkillUses;
                    if (st.SurvivedSec > maxSurvivedSec) maxSurvivedSec = st.SurvivedSec;
                    if (st.OffDay) offDayCount++;
                }
            }

            if (starterCount > 0)
            {
                avgAim /= starterCount;
                avgCqc /= starterCount;
                avgPositioning /= starterCount;
                avgUtility /= starterCount;
                avgDecision /= starterCount;
            }

            // Insight 1: Mechanical / Firepower Dimension (枪法与正面火力归因)
            if (placement.Kills >= 5 || totalDmg >= 900)
            {
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Mechanical",
                    IsPositive = true,
                    Headline = "正面火力压制显著，多点开花斩获高淘汰",
                    CausalDetail = $"全队本局共造成 {totalDmg} 点伤害、{totalKnocks} 次击倒并转化为 {placement.Kills} 个淘汰分；首发瞄准({avgAim:F1})与近战({avgCqc:F1})在对枪回合中建立了高命中优势。",
                    RecommendedAction = "保持当前主突击手与副突击手的协同站位，可在有利地形大胆开启『主动接战』指令扩大淘汰分收益。"
                });
            }
            else
            {
                string offDayNote = offDayCount > 0 ? $"（含 {offDayCount} 名选手出现临场状态低迷 Off-Day）" : string.Empty;
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Mechanical",
                    IsPositive = false,
                    Headline = "正面对枪终结率不足，伤害未能有效转化为淘汰",
                    CausalDetail = $"全队本局总伤害 {totalDmg}、仅获 {placement.Kills} 淘汰{offDayNote}；近战({avgCqc:F1})与瞄准({avgAim:F1})在中近距离换血中处于下风。",
                    RecommendedAction = "建议下周团队训练切换为『射击训练 (Shooting)』，并为突击位安排『近战爆发 (CloseQuarters)』专项。"
                });
            }

            // Insight 2: Tactical / Utility & Survival Dimension (道具、凝冰墙与掩体生存归因)
            int utilityTotal = totalGloo + totalGrenades + totalSkills;
            if (totalGloo >= 4 && placement.Placement <= 5)
            {
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Tactical",
                    IsPositive = true,
                    Headline = "凝冰墙与主动技能衔接果断，有效化解交叉火力",
                    CausalDetail = $"全队在受击与推进中共立起 {totalGloo} 面凝冰墙、投掷 {totalGrenades} 颗手雷并释放 {totalSkills} 次技能（道具均值 {avgUtility:F1}），成功支撑队伍存活 {maxSurvivedSec} 秒进入前五。",
                    RecommendedAction = "决赛圈可继续信任『凝冰墙推进 (GlooPush)』或『高点压制』策略，比赛关键时刻手动触发『全员技能爆发』。"
                });
            }
            else
            {
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Tactical",
                    IsPositive = false,
                    Headline = "无掩体转移减员或凝冰墙自保触发偏慢",
                    CausalDetail = $"本局全队仅立起 {totalGloo} 面凝冰墙、存活 {maxSurvivedSec} 秒（排名 #{placement.Placement}）；选位({avgPositioning:F1})与道具运用({avgUtility:F1})在被多队抽靶时未能及时封烟立墙。",
                    RecommendedAction = "遭遇集火时请立即点击现场指令『封墙自保 (PopGlooAndHold)』，并在个人训练中提升『道具与搜刮 (UtilityAndLooting)』。"
                });
            }

            // Insight 3: Macro / Rotation & Playbook Dimension (跳点、进圈时机与指挥归因)
            if (placement.Placement <= 3)
            {
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Macro",
                    IsPositive = true,
                    Headline = $"进圈节奏与 {focusTeam.Playbook.Endgame} 终局决策精准",
                    CausalDetail = $"依托 {focusTeam.Playbook.Drop} 跳点与 {focusTeam.Playbook.Rotation} 进圈预设（指挥决策均值 {avgDecision:F1}），队伍成功避开中期乱战并在决赛圈占据主动，斩获 #{placement.Placement} 名（+{placement.PlacementPoints} 排名分）。",
                    RecommendedAction = "当前战术预设与阵容高度契合，后续面对强队高点卡圈时可结合『提前进圈 (RotateNow)』锁定核心房区。"
                });
            }
            else if (maxSurvivedSec < 120)
            {
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Macro",
                    IsPositive = false,
                    Headline = $"前期 {focusTeam.Playbook.Drop} 跳点撞车或过早接战导致崩盘",
                    CausalDetail = $"全队仅存活 {maxSurvivedSec} 秒即遭淘汰（排名 #{placement.Placement}）；前期跳点或 {focusTeam.Playbook.Engage} 接战策略过于激进，导致未进入中后期即丧失人数优势。",
                    RecommendedAction = "建议在赛前战术板将跳点调整为『Flex/Cold』或将交战策略改为『前六前避战 (AvoidUntilTop6)』以稳固排名下限。"
                });
            }
            else
            {
                report.AttributionInsights.Add(new AttributionInsightItem
                {
                    Dimension = "Macro",
                    IsPositive = false,
                    Headline = $"三四圈收缩阶段进圈受阻，{focusTeam.Playbook.Rotation} 转移时机偏慢",
                    CausalDetail = $"队伍存活至 {maxSurvivedSec} 秒但在排名 #{placement.Placement} 止步；中后期蓝圈收缩时被先入圈队伍卡住高点与圈边（指挥决策均值 {avgDecision:F1}）。",
                    RecommendedAction = "建议将进圈预设切换为『提前进圈 (Early)』，或在 Stage 3 刷新瞬间手动下达『提前进圈 (RotateNow)』指令。"
                });
            }
        }
    }
}
