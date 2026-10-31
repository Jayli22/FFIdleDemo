using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class ScoutingService
    {
        public void InvestScoutingPoints(ScoutingEntry entry, int points)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (points <= 0) return;

            entry.ProgressPoints = Math.Clamp(entry.ProgressPoints + points, 0, 100);
            if (entry.ProgressPoints >= 100)
            {
                entry.Stage = ScoutingStage.Exact;
            }
            else if (entry.ProgressPoints >= 50)
            {
                entry.Stage = ScoutingStage.Range;
            }
            else
            {
                entry.Stage = ScoutingStage.Unknown;
            }
        }

        public ScoutedPlayerView BuildPlayerView(PlayerDef player, ScoutingStage stage)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));

            float rating = player.CalculateTrueRating();
            string tier = rating >= 14.2f ? "S" : (rating >= 12.6f ? "A" : (rating >= 11.2f ? "B" : "C"));

            var view = new ScoutedPlayerView
            {
                PlayerId = player.PlayerId,
                Name = player.Name,
                Age = player.Age,
                Stage = stage,
                OverallTierTag = tier,
                EstimatedMarketValue = EstimateMarketValue(player),
                EstimatedWeeklySalary = EstimateWeeklySalary(player)
            };

            float halfWidth = stage switch
            {
                ScoutingStage.Unknown => 3.5f,
                ScoutingStage.Range => 1.5f,
                _ => 0f
            };
            bool isExact = stage == ScoutingStage.Exact;

            AddAttr(view, "aim", player.Mechanical.Aim, halfWidth, isExact);
            AddAttr(view, "recoil", player.Mechanical.Recoil, halfWidth, isExact);
            AddAttr(view, "reflex", player.Mechanical.Reflex, halfWidth, isExact);
            AddAttr(view, "movement", player.Mechanical.Movement, halfWidth, isExact);
            AddAttr(view, "cqc", player.Mechanical.Cqc, halfWidth, isExact);
            AddAttr(view, "longRange", player.Mechanical.LongRange, halfWidth, isExact);

            AddAttr(view, "awareness", player.Tactical.Awareness, halfWidth, isExact);
            AddAttr(view, "positioning", player.Tactical.Positioning, halfWidth, isExact);
            AddAttr(view, "utility", player.Tactical.Utility, halfWidth, isExact);
            AddAttr(view, "looting", player.Tactical.Looting, halfWidth, isExact);
            AddAttr(view, "decision", player.Tactical.Decision, halfWidth, isExact);
            AddAttr(view, "comms", player.Tactical.Comms, halfWidth, isExact);

            AddAttr(view, "composure", player.Mental.Composure, halfWidth, isExact);
            AddAttr(view, "teamwork", player.Mental.Teamwork, halfWidth, isExact);
            AddAttr(view, "determination", player.Mental.Determination, halfWidth, isExact);

            // Hidden traits are NEVER exposed as raw numbers (§1.2), only as natural language scouting hints at Range/Exact stages
            if (stage >= ScoutingStage.Range)
            {
                if (player.Hidden.Consistency >= 13.5f)
                    view.HiddenTraitHints.Add("发挥稳健：极少出现临场失常 (High Consistency)");
                else if (player.Hidden.Consistency <= 9.0f)
                    view.HiddenTraitHints.Add("状态起伏：受情绪影响较大，偶有神鬼莫测表现");

                if (stage == ScoutingStage.Exact)
                {
                    if (player.Hidden.BigMatch >= 13.5f)
                        view.HiddenTraitHints.Add("大赛心脏：季后赛与冠军冲刺阶段超常发挥 (Big Match Player)");
                    else if (player.Hidden.BigMatch <= 9.0f)
                        view.HiddenTraitHints.Add("关键局紧张：大赛高压环境下枪法略有变形");

                    if (player.Hidden.InjuryProne >= 12.0f)
                        view.HiddenTraitHints.Add("耐受偏弱：高负荷训练易累积疲劳，需注意轮换休息");

                    if ( view.HiddenTraitHints.Count == 0 )
                        view.HiddenTraitHints.Add("特质均衡：心理素质与抗压能力处于职业平均水准");
                }
            }

            return view;
        }

        public static int EstimateMarketValue(PlayerDef player)
        {
            float rating = player.CalculateTrueRating();
            float ageBonus = player.Age <= 19 ? 1.25f : (player.Age <= 22 ? 1.10f : 0.85f);
            int raw = (int)MathF.Round(MathF.Max(15f, (rating - 8.5f) * 28f * ageBonus));
            return (raw / 5) * 5 * 1000; // e.g., $45,000 .. $250,000
        }

        public static int EstimateWeeklySalary(PlayerDef player)
        {
            float rating = player.CalculateTrueRating();
            int raw = (int)MathF.Round(MathF.Max(2f, (rating - 8.0f) * 1.8f));
            return raw * 500; // e.g., $2,500 .. $12,000 / week
        }

        private static void AddAttr(ScoutedPlayerView view, string name, float trueVal, float halfWidth, bool isExact)
        {
            view.Attributes[name] = new ScoutedAttributeView
            {
                AttributeName = name,
                MinEstimate = isExact ? trueVal : Math.Clamp(trueVal - halfWidth, 1f, 20f),
                MaxEstimate = isExact ? trueVal : Math.Clamp(trueVal + halfWidth, 1f, 20f),
                IsExact = isExact
            };
        }
    }
}
