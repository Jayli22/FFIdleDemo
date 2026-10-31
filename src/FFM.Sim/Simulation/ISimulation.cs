using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;

namespace FFM.Sim.Simulation
{
    /// <summary>
    /// Public simulation contract between FFM.Sim and Godot Client / CLI Runner (§2.2).
    /// </summary>
    public interface ISimulation
    {
        int CurrentTick { get; }
        Phase CurrentPhase { get; }
        bool IsFinished { get; }
        MapData Map { get; }
        SnapshotBuffer PrevSnapshot { get; }
        SnapshotBuffer CurrSnapshot { get; }
        EventLog Events { get; }

        void Step();
        bool EnqueueCommand(TacticalCommand cmd);
        bool OverridePlayerDropPoi(int squadIndex, int poiId);
        MatchResult GetMatchResult();
    }
}
