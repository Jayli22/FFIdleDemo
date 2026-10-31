using System;
using FFM.Sim.Core;

namespace FFM.Sim.Map
{
    /// <summary>
    /// Zero-GC preallocated A* pathfinder on the 64x64 macro navigation grid (§0.6 Two-Tier Pathfinding).
    /// Uses generation stamps so each search resets in O(1) with 0 heap allocations.
    /// </summary>
    public sealed class CoarseAStar
    {
        private const int GridDim = MapData.MacroGridSize; // 64
        private const int TotalNodes = GridDim * GridDim;  // 4096

        private readonly int[] _gScore = new int[TotalNodes];
        private readonly int[] _fScore = new int[TotalNodes];
        private readonly int[] _cameFrom = new int[TotalNodes];
        private readonly int[] _visitedStamp = new int[TotalNodes];
        private readonly int[] _closedStamp = new int[TotalNodes];
        private readonly int[] _openHeap = new int[TotalNodes + 1];
        private int _heapCount;
        private int _searchStamp;

        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };
        private static readonly int[] StepCostMult = { 10, 10, 10, 10, 14, 14, 14, 14 };

        /// <summary>
        /// Computes a macro path from startWorld to goalWorld and fills waypointsBuffer (up to maxWaypoints).
        /// Returns the number of waypoints written. Zero GC allocations.
        /// </summary>
        public int FindPath(MapData map, Vec2 startWorld, Vec2 goalWorld, Vec2[] waypointsBuffer)
        {
            if (waypointsBuffer.Length == 0) return 0;

            MapData.WorldToMacroCell(startWorld, out int startX, out int startY);
            MapData.WorldToMacroCell(goalWorld, out int goalX, out int goalY);

            FindNearestWalkableMacro(map, ref startX, ref startY);
            FindNearestWalkableMacro(map, ref goalX, ref goalY);

            if (startX == goalX && startY == goalY)
            {
                waypointsBuffer[0] = map.ClampToWalkableWorld(goalWorld);
                return 1;
            }

            _searchStamp++;
            if (_searchStamp == int.MaxValue)
            {
                Array.Clear(_visitedStamp, 0, _visitedStamp.Length);
                Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _searchStamp = 1;
            }

            _heapCount = 0;
            int startIdx = startY * GridDim + startX;
            int goalIdx = goalY * GridDim + goalX;

            _visitedStamp[startIdx] = _searchStamp;
            _gScore[startIdx] = 0;
            _fScore[startIdx] = Heuristic(startX, startY, goalX, goalY);
            _cameFrom[startIdx] = -1;
            HeapPush(startIdx);

            int bestReachedIdx = startIdx;
            int bestHeuristic = _fScore[startIdx];
            int iterations = 0;

            while (_heapCount > 0 && iterations < 1024)
            {
                iterations++;
                int current = HeapPop();
                if (current == goalIdx)
                {
                    bestReachedIdx = goalIdx;
                    break;
                }

                if (_closedStamp[current] == _searchStamp) continue;
                _closedStamp[current] = _searchStamp;

                int cx = current % GridDim;
                int cy = current / GridDim;

                int hToGoal = Heuristic(cx, cy, goalX, goalY);
                if (hToGoal < bestHeuristic)
                {
                    bestHeuristic = hToGoal;
                    bestReachedIdx = current;
                }

                for (int dir = 0; dir < 8; dir++)
                {
                    int nx = cx + Dx[dir];
                    int ny = cy + Dy[dir];
                    if ((uint)nx >= GridDim || (uint)ny >= GridDim) continue;

                    byte macroCost = map.GetMacroCost(nx, ny);
                    if (macroCost == 0) continue;

                    int neighborIdx = ny * GridDim + nx;
                    if (_closedStamp[neighborIdx] == _searchStamp) continue;

                    int tentativeG = _gScore[current] + macroCost * StepCostMult[dir];
                    if (_visitedStamp[neighborIdx] != _searchStamp || tentativeG < _gScore[neighborIdx])
                    {
                        _visitedStamp[neighborIdx] = _searchStamp;
                        _cameFrom[neighborIdx] = current;
                        _gScore[neighborIdx] = tentativeG;
                        _fScore[neighborIdx] = tentativeG + Heuristic(nx, ny, goalX, goalY);
                        HeapPush(neighborIdx);
                    }
                }
            }

            // Reconstruct path into waypointsBuffer
            int pathLen = 0;
            int trace = bestReachedIdx;
            while (trace != -1 && trace != startIdx && pathLen < waypointsBuffer.Length)
            {
                int tx = trace % GridDim;
                int ty = trace / GridDim;
                waypointsBuffer[pathLen++] = MapData.MacroCellToWorldCenter(tx, ty);
                trace = _cameFrom[trace];
            }

            if (pathLen == 0)
            {
                waypointsBuffer[0] = map.ClampToWalkableWorld(goalWorld);
                return 1;
            }

            // Reverse in-place so waypoint[0] is the first step from start
            for (int i = 0; i < pathLen / 2; i++)
            {
                (waypointsBuffer[i], waypointsBuffer[pathLen - 1 - i]) =
                    (waypointsBuffer[pathLen - 1 - i], waypointsBuffer[i]);
            }

            // Replace final waypoint with exact walkable goal position
            waypointsBuffer[pathLen - 1] = map.ClampToWalkableWorld(goalWorld);
            return pathLen;
        }

        private static void FindNearestWalkableMacro(MapData map, ref int mx, ref int my)
        {
            if (map.GetMacroCost(mx, my) > 0) return;
            for (int r = 1; r < GridDim; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int nx = mx + dx;
                        int ny = my + dy;
                        if (map.GetMacroCost(nx, ny) > 0)
                        {
                            mx = nx;
                            my = ny;
                            return;
                        }
                    }
                }
            }
        }

        private static int Heuristic(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int diag = Math.Min(dx, dy);
            int straight = Math.Max(dx, dy) - diag;
            return (diag * 14 + straight * 10) * 10;
        }

        private void HeapPush(int nodeIdx)
        {
            if (_heapCount >= TotalNodes) return;
            int i = ++_heapCount;
            _openHeap[i] = nodeIdx;
            while (i > 1)
            {
                int parent = i >> 1;
                if (_fScore[_openHeap[i]] >= _fScore[_openHeap[parent]]) break;
                (_openHeap[i], _openHeap[parent]) = (_openHeap[parent], _openHeap[i]);
                i = parent;
            }
        }

        private int HeapPop()
        {
            int top = _openHeap[1];
            _openHeap[1] = _openHeap[_heapCount--];
            int i = 1;
            while (true)
            {
                int left = i << 1;
                int right = left + 1;
                int smallest = i;
                if (left <= _heapCount && _fScore[_openHeap[left]] < _fScore[_openHeap[smallest]])
                {
                    smallest = left;
                }
                if (right <= _heapCount && _fScore[_openHeap[right]] < _fScore[_openHeap[smallest]])
                {
                    smallest = right;
                }
                if (smallest == i) break;
                (_openHeap[i], _openHeap[smallest]) = (_openHeap[smallest], _openHeap[i]);
                i = smallest;
            }
            return top;
        }
    }
}
