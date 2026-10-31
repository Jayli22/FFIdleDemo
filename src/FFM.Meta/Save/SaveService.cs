using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFM.Meta.Models;
using FFM.Sim.Data;

namespace FFM.Meta.Save
{
    public sealed class CareerSaveData
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = SaveService.CurrentSchemaVersion;
        [JsonPropertyName("careerSeed")] public ulong CareerSeed { get; set; } = 20260416UL;
        [JsonPropertyName("season")] public int Season { get; set; } = 1;
        [JsonPropertyName("week")] public int Week { get; set; } = 1;
        [JsonPropertyName("patchVersion")] public string PatchVersion { get; set; } = "v1.0";
        [JsonPropertyName("playerTeamId")] public string PlayerTeamId { get; set; } = string.Empty;
        [JsonPropertyName("cashBalance")] public int CashBalance { get; set; } = 150000;
        [JsonPropertyName("scoutingPointsAvailable")] public int ScoutingPointsAvailable { get; set; } = 100;
        [JsonPropertyName("teamTrainingFocus")] public TeamTrainingFocus TeamTrainingFocus { get; set; } = TeamTrainingFocus.Shooting;
        [JsonPropertyName("individualFocuses")] public Dictionary<string, IndividualTrainingFocus> IndividualFocuses { get; set; } = new();
        [JsonPropertyName("teams")] public List<TeamDef> Teams { get; set; } = new();
        [JsonPropertyName("standings")] public List<LeagueStandingEntry> Standings { get; set; } = new();
        [JsonPropertyName("scoutingBook")] public Dictionary<string, ScoutingEntry> ScoutingBook { get; set; } = new();
        [JsonPropertyName("transferMarket")] public List<TransferListing> TransferMarket { get; set; } = new();
        [JsonPropertyName("ledger")] public List<FinanceLedgerEntry> Ledger { get; set; } = new();
        [JsonPropertyName("lastMatchReport")] public PostMatchReport? LastMatchReport { get; set; }
        [JsonPropertyName("reigningChampionTeamId")] public string? ReigningChampionTeamId { get; set; }
        [JsonPropertyName("idleClock")] public IdleClockState IdleClock { get; set; } = new();
        [JsonPropertyName("facilities")] public ClubFacilityState Facilities { get; set; } = new();
        [JsonPropertyName("playerProgressions")] public Dictionary<string, PlayerProgressionData> PlayerProgressions { get; set; } = new();
    }

    public sealed class SaveService
    {
        public const int CurrentSchemaVersion = 2;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string Serialize(CareerSaveData save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            save.SchemaVersion = CurrentSchemaVersion;
            save.PlayerProgressions = PlayerProgressionExtensions.ExportAll();
            return JsonSerializer.Serialize(save, SerializerOptions);
        }

        public CareerSaveData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Save JSON cannot be empty.", nameof(json));
            }

            var save = JsonSerializer.Deserialize<CareerSaveData>(json, SerializerOptions)
                       ?? throw new InvalidDataException("Failed to deserialize CareerSaveData.");

            MigrateIfNeeded(save);
            PlayerProgressionExtensions.ImportAll(save.PlayerProgressions);
            return save;
        }

        public void SaveToFile(string path, CareerSaveData save)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = Serialize(save);
            File.WriteAllText(path, json);
        }

        public CareerSaveData LoadFromFile(string path)
        {
            string json = File.ReadAllText(path);
            return Deserialize(json);
        }

        public void MigrateIfNeeded(CareerSaveData save)
        {
            if (save.SchemaVersion < 1)
            {
                // Migration v0 -> v1: ensure defaults on newly introduced fields
                if (string.IsNullOrWhiteSpace(save.PatchVersion))
                {
                    save.PatchVersion = "v1.0";
                }
                if (save.CashBalance <= 0 && save.Ledger.Count == 0)
                {
                    save.CashBalance = 150000;
                }
                if (save.ScoutingPointsAvailable < 0)
                {
                    save.ScoutingPointsAvailable = 100;
                }
                save.SchemaVersion = 1;
            }

            if (save.SchemaVersion < 2)
            {
                save.IdleClock ??= new IdleClockState();
                save.Facilities ??= new ClubFacilityState();
                save.PlayerProgressions ??= new Dictionary<string, PlayerProgressionData>();
                save.SchemaVersion = 2;
            }
        }
    }
}
