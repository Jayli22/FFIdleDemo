using System;
using FFM.Client.Sim;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.Camera
{
    public enum CameraViewMode : byte
    {
        FollowSquad = 0,
        TopDownMacro = 1,
        FreeControl = 2
    }

    /// <summary>
    /// 3D Broadcast Camera Rig supporting FollowSquad, TopDownMacro, and FreeControl modes
    /// with smooth spring-damped transitions and zoom-dependent pitch (§6.4, WP-14).
    /// </summary>
    public partial class CameraRig : Node3D
    {
        private Camera3D? _camera;
        private Vector3 _focusTarget = new(1000f, 0f, 1000f);
        private Vector3 _smoothedFocus = new(1000f, 0f, 1000f);
        private float _targetDistance = 230f;
        private float _smoothedDistance = 230f;
        private int _followedSquadIndex;
        private CameraViewMode _mode = CameraViewMode.FollowSquad;
        private bool _isDraggingPan;
        private float _rightClickDragDistance;

        public Camera3D? Camera => _camera;
        public CameraViewMode Mode => _mode;
        public int FollowedSquadIndex => _followedSquadIndex;
        public float CurrentDistance => _smoothedDistance;
        public Vector3 CurrentFocus => _smoothedFocus;

        public event Action<Vector3>? GroundLeftClicked;
        public event Action<Vector3>? GroundRightClicked;

        public override void _Ready()
        {
            _camera = new Camera3D
            {
                Name = "BroadcastCamera3D",
                Fov = 46f,
                Near = 2.0f,
                Far = 4500f,
                Current = true
            };
            AddChild(_camera);
            UpdateCameraTransform();
        }

        public void SetMode(CameraViewMode mode)
        {
            _mode = mode;
            if (_mode == CameraViewMode.TopDownMacro)
            {
                _focusTarget = new Vector3(1000f, 0f, 1000f);
                _targetDistance = 1280f;
            }
            else if (_mode == CameraViewMode.FollowSquad && _targetDistance > 450f)
            {
                _targetDistance = 230f;
            }
        }

        public void CycleMode()
        {
            CameraViewMode next = _mode switch
            {
                CameraViewMode.FollowSquad => CameraViewMode.TopDownMacro,
                CameraViewMode.TopDownMacro => CameraViewMode.FreeControl,
                _ => CameraViewMode.FollowSquad
            };
            SetMode(next);
        }

        public void FollowSquad(int squadIndex, float preferredDistance = 225f)
        {
            _followedSquadIndex = Math.Clamp(squadIndex, 0, SnapshotBuffer.MaxSquads - 1);
            _mode = CameraViewMode.FollowSquad;
            _targetDistance = Math.Clamp(preferredDistance, 110f, 520f);
        }

        public void FocusWorldPoint(Vector3 worldPos, float preferredDistance = 210f)
        {
            _focusTarget = new Vector3(
                Math.Clamp(worldPos.X, 80f, MapData.MapSizeMeters - 80f),
                worldPos.Y,
                Math.Clamp(worldPos.Z, 80f, MapData.MapSizeMeters - 80f));
            _targetDistance = Math.Clamp(preferredDistance, 100f, 1450f);
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb)
            {
                if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
                {
                    _targetDistance = Math.Clamp(_targetDistance * 0.86f, 95f, 1550f);
                }
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
                {
                    _targetDistance = Math.Clamp(_targetDistance * 1.16f, 95f, 1550f);
                }
                else if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    if (TryProjectScreenToGround(mb.Position, out Vector3 groundHit))
                    {
                        GroundLeftClicked?.Invoke(groundHit);
                    }
                }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    if (mb.Pressed)
                    {
                        _isDraggingPan = true;
                        _rightClickDragDistance = 0f;
                    }
                    else
                    {
                        bool wasQuickClick = _isDraggingPan && _rightClickDragDistance <= 6.5f;
                        _isDraggingPan = false;
                        if (wasQuickClick && TryProjectScreenToGround(mb.Position, out Vector3 pinHit))
                        {
                            GroundRightClicked?.Invoke(pinHit);
                        }
                    }
                }
            }
            else if (@event is InputEventMouseMotion mm && _isDraggingPan)
            {
                _rightClickDragDistance += mm.Relative.Length();
                if (_rightClickDragDistance > 4.0f)
                {
                    _mode = CameraViewMode.FreeControl;
                    float panScale = _smoothedDistance * 0.0022f;
                    _focusTarget.X = Math.Clamp(_focusTarget.X - mm.Relative.X * panScale, 50f, MapData.MapSizeMeters - 50f);
                    _focusTarget.Z = Math.Clamp(_focusTarget.Z - mm.Relative.Y * panScale, 50f, MapData.MapSizeMeters - 50f);
                }
            }
        }

        public bool TryProjectScreenToGround(Vector2 screenPos, out Vector3 groundPos)
        {
            groundPos = Vector3.Zero;
            if (_camera == null) return false;

            Vector3 rayOrigin = _camera.ProjectRayOrigin(screenPos);
            Vector3 rayDir = _camera.ProjectRayNormal(screenPos);
            if (MathF.Abs(rayDir.Y) < 0.001f) return false;

            float planeY = _smoothedFocus.Y;
            float t = (planeY - rayOrigin.Y) / rayDir.Y;
            if (t <= 0f || t > 6000f) return false;

            Vector3 hit = rayOrigin + rayDir * t;
            groundPos = new Vector3(
                Math.Clamp(hit.X, 20f, MapData.MapSizeMeters - 20f),
                planeY,
                Math.Clamp(hit.Z, 20f, MapData.MapSizeMeters - 20f));
            return true;
        }

        public void UpdateRig(InterpolatedFrameState frame, float dt)
        {
            HandleKeyboardPan(dt);

            if (_mode == CameraViewMode.FollowSquad)
            {
                if (TryComputeSquadCentroid(frame, _followedSquadIndex, out Vector3 centroid))
                {
                    _focusTarget = centroid;
                }
            }
            else if (_mode == CameraViewMode.TopDownMacro)
            {
                // Center slightly toward current safe zone in late stages
                if (frame.ZoneStage >= 2)
                {
                    _focusTarget = new Vector3(1000f, 0f, 1000f).Lerp(frame.ZoneCenter3D, 0.45f);
                }
                else
                {
                    _focusTarget = new Vector3(1000f, 0f, 1000f);
                }
            }

            float lerpT = 1f - MathF.Exp(-7.5f * dt);
            _smoothedFocus = _smoothedFocus.Lerp(_focusTarget, lerpT);
            _smoothedDistance = Mathf.Lerp(_smoothedDistance, _targetDistance, lerpT);

            UpdateCameraTransform();
        }

        private void HandleKeyboardPan(float dt)
        {
            Vector2 move = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Y -= 1f;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Y += 1f;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1f;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1f;

            if (move.LengthSquared() > 0.01f)
            {
                _mode = CameraViewMode.FreeControl;
                move = move.Normalized();
                float panSpeed = Math.Clamp(_smoothedDistance * 0.85f, 180f, 950f);
                _focusTarget.X = Math.Clamp(_focusTarget.X + move.X * panSpeed * dt, 50f, MapData.MapSizeMeters - 50f);
                _focusTarget.Z = Math.Clamp(_focusTarget.Z + move.Y * panSpeed * dt, 50f, MapData.MapSizeMeters - 50f);
            }
        }

        private void UpdateCameraTransform()
        {
            if (_camera == null) return;

            // Pitch transitions from 52 deg (close isometric) to 78 deg (macro top-down)
            float t = Math.Clamp((_smoothedDistance - 150f) / 1100f, 0f, 1f);
            float pitchDeg = Mathf.Lerp(54f, 78f, t);
            float pitchRad = Mathf.DegToRad(pitchDeg);

            float height = MathF.Sin(pitchRad) * _smoothedDistance;
            float backOffset = MathF.Cos(pitchRad) * _smoothedDistance;

            Vector3 camPos = _smoothedFocus + new Vector3(0f, height, backOffset);
            _camera.LookAtFromPosition(camPos, _smoothedFocus, Vector3.Up);
        }

        public static bool TryComputeSquadCentroid(InterpolatedFrameState frame, int squadIndex, out Vector3 centroid)
        {
            centroid = Vector3.Zero;
            if ((uint)squadIndex >= SnapshotBuffer.MaxSquads) return false;

            int count = 0;
            Vector3 sum = Vector3.Zero;
            int firstUnit = squadIndex * 4;
            for (int m = 0; m < 4; m++)
            {
                ref readonly InterpolatedUnitState u = ref frame.Units[firstUnit + m];
                if (u.State == FFM.Sim.Data.UnitState.Alive || u.State == FFM.Sim.Data.UnitState.Knocked)
                {
                    sum += u.WorldPos3D;
                    count++;
                }
            }

            if (count == 0) return false;
            centroid = sum / count;
            return true;
        }
    }
}
