using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class EconomyService
    {
        public int BaseWeeklySponsorship { get; set; } = 32000;

        public void RecordTransaction(
            List<FinanceLedgerEntry> ledger,
            ref int cashBalance,
            int season,
            int week,
            string category,
            int amount,
            string description)
        {
            cashBalance += amount;
            ledger.Add(new FinanceLedgerEntry
            {
                Season = season,
                Week = week,
                Category = category,
                Amount = amount,
                BalanceAfter = cashBalance,
                Description = description
            });
        }

        public int CalculateTeamWeeklyPayroll(TeamDef team)
        {
            if (team == null) return 0;
            int total = 0;
            for (int i = 0; i < team.Players.Count; i++)
            {
                total += ScoutingService.EstimateWeeklySalary(team.Players[i]);
            }
            return total;
        }

        public void ProcessWeeklyFinances(
            TeamDef playerTeam,
            List<FinanceLedgerEntry> ledger,
            ref int cashBalance,
            int season,
            int week,
            int matchDayPointsEarned)
        {
            // 1. Weekly Sponsorship + Broadcast revenue
            int sponsorIncome = BaseWeeklySponsorship + matchDayPointsEarned * 350;
            RecordTransaction(
                ledger,
                ref cashBalance,
                season,
                week,
                "Sponsorship",
                sponsorIncome,
                $"第 {week} 周赞助商底薪与积分转播分成 (+{matchDayPointsEarned} pts)");

            // 2. Weekly Roster Payroll
            int payroll = CalculateTeamWeeklyPayroll(playerTeam);
            RecordTransaction(
                ledger,
                ref cashBalance,
                season,
                week,
                "Payroll",
                -payroll,
                $"第 {week} 周战队选手薪资支出 ({playerTeam.Players.Count} 人)");
        }

        public void AwardFinalsPrize(
            List<FinanceLedgerEntry> ledger,
            ref int cashBalance,
            int season,
            int week,
            int finalPlacement)
        {
            int prize = finalPlacement switch
            {
                1 => 250000,
                2 => 140000,
                3 => 90000,
                4 => 60000,
                5 => 45000,
                6 => 35000,
                _ => 20000
            };

            RecordTransaction(
                ledger,
                ref cashBalance,
                season,
                week,
                "PrizePool",
                prize,
                $"S{season} 赛季总决赛第 #{finalPlacement} 名奖金结算");
        }
    }
}
