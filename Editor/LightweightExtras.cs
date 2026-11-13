namespace VRVlog.LilToonExporter
{
    // The prototype choices under 自動で軽量化する, used only with it on.
    // MergeMeshes: MeshMergeLightweight. ThinOutlineMillimeters: outlines
    // thinner than this many millimetres are not drawn (OutlineLightweight);
    // 0 is off.
    internal sealed class LightweightExtras
    {
        internal bool MergeMeshes;
        internal float ThinOutlineMillimeters;

        internal bool OmitsOutlines => OutlineLightweight.ValidThreshold(ThinOutlineMillimeters) > 0f;
    }
}
