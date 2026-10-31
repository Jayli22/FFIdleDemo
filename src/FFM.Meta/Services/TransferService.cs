using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Core;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class TransferService
    {
        private static readonly string[] CandidateNames =
        {
            "RazerX", "Kobra", "ViperSEA", "StormBR", "Noxus", "Zephyr", "BlazeTH", "PhantomID",
            "Kronos", "FalconVN", "ApexMY", "ShadowPH", "TitanLAT", "PulseSG", "Drako", "Lynx"
        };

        public List<TransferListing> GenerateMarketListings(int season, ref Rng rng, int count = 10)
        {
            var listings = new List<TransferListing>(count);

            for (int i = 0; i < count; i++)
            {
                string id = $"fa_s{season}_{i + 1:D2}";
                string name = CandidateNames[(season * 5 + i) % CandidateNames.Length] + (i >= CandidateNames.Length ? $"{i}" : "");
                int age = rng.NextInt(16, 25);
                float baseTier = rng.NextFloat(10.5f, 15.2f);

                var p = new PlayerDef
                {
                    PlayerId = id,
                    Name = name,
                    Age = age,
                    Region = "SEA",
                    Mechanical = new MechanicalAttributes
                    {
                        Aim = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.2f)),
                        Recoil = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.2f)),
                        Reflex = ClampAttr(baseTier + (age <= 19 ? 1.2f : -0.4f) + rng.NextFloat(-1.8f, 1.8f)),
                        Movement = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.0f)),
                        Cqc = ClampAttr(baseTier + rng.NextFloat(-2.2f, 2.5f)),
                        LongRange = ClampAttr(baseTier + rng.NextFloat(-2.2f, 2.5f))
                    },
                    Tactical = new TacticalAttributes
                    {
                        Awareness = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.0f)),
                        Positioning = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.2f)),
                        Utility = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.2f)),
                        Looting = ClampAttr(baseTier + rng.NextFloat(-1.8f, 1.8f)),
                        Decision = ClampAttr(baseTier + (age >= 22 ? 1.2f : -0.3f) + rng.NextFloat(-1.8f, 2.0f)),
                        Comms = ClampAttr(baseTier + rng.NextFloat(-1.8f, 2.0f))
                    },
                    Mental = new MentalAttributes
                    {
                        Composure = ClampAttr(baseTier + rng.NextFloat(-2.0f, 2.2f)),
                        Teamwork = ClampAttr(baseTier + rng.NextFloat(-1.8f, 2.0f)),
                        Determination = ClampAttr(rng.NextFloat(10.0f, 18.0f))
                    },
                    Hidden = new HiddenAttributes
                    {
                        Consistency = ClampAttr(rng.NextFloat(9.5f, 16.5f)),
                        BigMatch = ClampAttr(rng.NextFloat(9.0f, 17.0f)),
                        InjuryProne = ClampAttr(rng.NextFloat(3.0f, 13.0f))
                    },
                    Condition = new PlayerCondition
                    {
                        Stamina = ClampAttr(rng.NextFloat(11.0f, 16.5f)),
                        Form = 0f,
                        Fatigue = 0f
                    }
                };

                p.RoleProficiency["Rusher"] = ClampAttr(p.Mechanical.Cqc);
                p.RoleProficiency["SecondRusher"] = ClampAttr((p.Mechanical.Cqc + p.Mental.Teamwork) * 0.5f);
                p.RoleProficiency["Sniper"] = ClampAttr(p.Mechanical.LongRange);
                p.RoleProficiency["Support"] = ClampAttr(p.Tactical.Comms);
                p.RoleProficiency["Grenadier"] = ClampAttr(p.Tactical.Utility);

                p.SkillProficiency["sk_dash"] = 12f;
                p.SkillProficiency["sk_shield"] = 12f;
                p.SkillProficiency["sk_heal"] = 12f;
                p.SkillProficiency["sk_scan"] = 12f;
                p.SkillProficiency["sk_deploy"] = 12f;

                int buyout = (i % 3 == 0) ? 0 : ScoutingService.EstimateMarketValue(p);
                int salary = ScoutingService.EstimateWeeklySalary(p);

                listings.Add(new TransferListing
                {
                    Player = p,
                    CurrentTeamId = (i % 3 == 0) ? string.Empty : $"club_{(i % 5) + 1}",
                    BuyoutFee = buyout,
                    AskedWeeklySalary = salary
                });
            }

            return listings;
        }

        public bool TrySignPlayer(
            TeamDef team,
            List<TransferListing> market,
            string playerId,
            EconomyService economy,
            List<FinanceLedgerEntry> ledger,
            ref int cashBalance,
            int season,
            int week,
            out string message)
        {
            if (team.Players.Count >= 6)
            {
                message = "战队大名单已满（最多 6 名选手），请先解约替补选手。";
                return false;
            }

            int idx = -1;
            for (int i = 0; i < market.Count; i++)
            {
                if (market[i].Player.PlayerId == playerId)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                message = "未在转会市场找到该选手。";
                return false;
            }

            var listing = market[idx];
            if (cashBalance < listing.BuyoutFee)
            {
                message = $"俱乐部资金不足（需转会费 ${listing.BuyoutFee:N0}，当前余额 ${cashBalance:N0}）。";
                return false;
            }

            if (listing.BuyoutFee > 0)
            {
                economy.RecordTransaction(
                    ledger,
                    ref cashBalance,
                    season,
                    week,
                    "TransferBuyout",
                    -listing.BuyoutFee,
                    $"签约选手 {listing.Player.Name} 转会买断费");
            }

            var signed = listing.Player.Clone();
            team.Players.Add(signed);
            if (string.IsNullOrEmpty(team.Lineup.Sub))
            {
                team.Lineup.Sub = signed.PlayerId;
            }

            market.RemoveAt(idx);
            message = $"成功签约 {signed.Name}（周薪 ${listing.AskedWeeklySalary:N0}）。";
            return true;
        }

        public bool TryReleasePlayer(TeamDef team, string playerId, out string message)
        {
            if (team.Players.Count <= 4)
            {
                message = "战队至少需保留 4 名首发选手，无法继续解约。";
                return false;
            }

            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                if (team.Lineup.Starters[i].PlayerId == playerId)
                {
                    message = "无法直接解约当前首发选手，请先将其轮换至替补席。";
                    return false;
                }
            }

            for (int i = 0; i < team.Players.Count; i++)
            {
                if (team.Players[i].PlayerId == playerId)
                {
                    string name = team.Players[i].Name;
                    team.Players.RemoveAt(i);
                    if (team.Lineup.Sub == playerId)
                    {
                        team.Lineup.Sub = null;
                    }
                    message = $"已与选手 {name} 解除合约。";
                    return true;
                }
            }

            message = "未在战队名单中找到该选手。";
            return false;
        }

        private static float ClampAttr(float v) => MathF.Round(Math.Clamp(v, 5f, 19.5f) * 10f) / 10f;
    }
}
