using System;
using System.Collections.Generic;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class PlaybookService
    {
        public void UpdatePlaybook(
            TeamDef team,
            DropStyle drop,
            RotationStyle rotation,
            EngageStyle engage,
            EndgameStyle endgame)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));
            team.Playbook.Drop = drop.ToString();
            team.Playbook.Rotation = rotation.ToString();
            team.Playbook.Engage = engage.ToString();
            team.Playbook.Endgame = endgame.ToString();
        }

        /// <summary>
        /// Evaluates how well the team's current 4 starters fit the configured 4-dimension Playbook.
        /// Returns human-readable coaching tips to help the player understand why a tactic works or struggles.
        /// </summary>
        public List<string> EvaluateTacticalFit(TeamDef team)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));
            var notes = new List<string>();

            float avgCqc = 0f, avgLongRange = 0f, avgPositioning = 0f, avgLooting = 0f, avgDecision = 0f, avgUtility = 0f;
            int count = 0;

            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                var p = team.FindPlayer(team.Lineup.Starters[i].PlayerId);
                if (p == null) continue;
                avgCqc += p.Mechanical.Cqc;
                avgLongRange += p.Mechanical.LongRange;
                avgPositioning += p.Tactical.Positioning;
                avgLooting += p.Tactical.Looting;
                avgDecision += p.Tactical.Decision;
                avgUtility += p.Tactical.Utility;
                count++;
            }

            if (count > 0)
            {
                avgCqc /= count;
                avgLongRange /= count;
                avgPositioning /= count;
                avgLooting /= count;
                avgDecision /= count;
                avgUtility /= count;
            }

            DropStyle drop = team.Playbook.GetDropStyle();
            RotationStyle rot = team.Playbook.GetRotationStyle();
            EngageStyle eng = team.Playbook.GetEngageStyle();
            EndgameStyle end = team.Playbook.GetEndgameStyle();

            if (drop == DropStyle.Hot && avgCqc < 11.5f)
            {
                notes.Add($"【跳点风险】当前选择 Hot 一级点硬刚，但首发近战均值 ({avgCqc:F1}) 偏低，落地Roll点容易减员；建议改用 Flex/Cold 或加强近战训练。");
            }
            else if (drop == DropStyle.Hot && avgCqc >= 13.0f)
            {
                notes.Add($"【跳点契合】首发近战爆发力出色 ({avgCqc:F1})，Hot 跳点能充分发挥前期清场优势。");
            }

            if (rot == RotationStyle.Early && avgDecision >= 12.5f)
            {
                notes.Add($"【进圈优势】指挥与决策均值 ({avgDecision:F1}) 较高，Early 提前进圈能高效抢占中心房区。");
            }
            else if (rot == RotationStyle.Late && avgLooting >= 12.5f)
            {
                notes.Add($"【发育优势】搜刮效率 ({avgLooting:F1}) 突出，Late 慢进圈可确保全队满配三甲再接战。");
            }

            if (eng == EngageStyle.Contest && avgCqc < 12.0f)
            {
                notes.Add($"【交战提醒】主动找架 (Contest) 对近战与道具要求极高，当前近战 ({avgCqc:F1}) 建议搭配 Balanced 接战策略。");
            }
            else if (eng == EngageStyle.Avoid && avgPositioning >= 12.5f)
            {
                notes.Add($"【运营契合】选位意识 ({avgPositioning:F1}) 扎实，避战运营 (Avoid) 能稳定保住排名分下限。");
            }

            if (end == EndgameStyle.HighGround && avgLongRange >= 12.5f)
            {
                notes.Add($"【决赛圈契合】远距离抽靶能力 ({avgLongRange:F1}) 强，决赛圈抢高坡 (HighGround) 压制力极佳。");
            }
            else if (end == EndgameStyle.GlooFort && avgUtility < 11.5f)
            {
                notes.Add($"【决赛圈风险】凝冰墙堡垒推进 (GlooFort) 依赖高道具属性支撑，当前道具均值 ({avgUtility:F1}) 偏低。");
            }

            if (notes.Count == 0)
            {
                notes.Add("【战术均衡】当前四项战术预设与首发阵容属性匹配度良好，无明显短板冲突。");
            }

            return notes;
        }
    }
}
