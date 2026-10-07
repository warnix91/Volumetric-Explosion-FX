namespace VolumetricExplosionFX.Core
{
    public enum VolumeCameraSelection { Near, Far, Both }
    public static class VolumeCameraRange
    {
        public static VolumeCameraSelection Select(double depth,double reach,double split)
        {
            if(depth+reach<split) return VolumeCameraSelection.Near;
            if(depth-reach>=split) return VolumeCameraSelection.Far;
            return VolumeCameraSelection.Both;
        }
    }
}
