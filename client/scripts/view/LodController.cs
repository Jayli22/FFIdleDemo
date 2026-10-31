using Godot;

namespace FFM.Client.View
{
    public enum UnitLodLevel : byte
    {
        Lod0Close = 0,
        Lod1Medium = 1,
        Lod2Macro = 2
    }

    /// <summary>
    /// Computes the active LOD tier based on camera distance to keep all 48 units readable
    /// from close follow-cam up to 2000m macro tactical view (§6.3, WP-13).
    /// </summary>
    public static class LodController
    {
        public const float Lod0MaxDistance = 260f;
        public const float Lod1MaxDistance = 580f;

        public static UnitLodLevel Evaluate(float cameraDistance)
        {
            if (cameraDistance <= Lod0MaxDistance)
            {
                return UnitLodLevel.Lod0Close;
            }
            if (cameraDistance <= Lod1MaxDistance)
            {
                return UnitLodLevel.Lod1Medium;
            }
            return UnitLodLevel.Lod2Macro;
        }
    }
}
