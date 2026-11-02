using VRVlog.LilToonExporter.Vendor.UniVRM10;

namespace VRVlog.LilToonExporter
{
    // The VRM presets by kind, and the VRChat Lip Sync slots that name a
    // mouth preset. A preset's name is its glTF key, so code that needs the
    // key uses preset.ToString().
    internal static class ExpressionPresetGroups
    {
        internal static readonly ExpressionPreset[] Emotions =
            { ExpressionPreset.happy, ExpressionPreset.angry, ExpressionPreset.sad, ExpressionPreset.relaxed, ExpressionPreset.surprised };
        internal static readonly ExpressionPreset[] Blinks =
            { ExpressionPreset.blink, ExpressionPreset.blinkLeft, ExpressionPreset.blinkRight };
        internal static readonly ExpressionPreset[] Mouths =
            { ExpressionPreset.aa, ExpressionPreset.ih, ExpressionPreset.ou, ExpressionPreset.ee, ExpressionPreset.oh };
        internal static readonly ExpressionPreset[] LookAts =
            { ExpressionPreset.lookUp, ExpressionPreset.lookDown, ExpressionPreset.lookLeft, ExpressionPreset.lookRight };

        // VisemeBlendShapes order: sil PP FF TH DD kk CH SS nn RR aa E I O U.
        internal static readonly (int Slot, ExpressionPreset Preset)[] VisemeSlots =
        {
            (10, ExpressionPreset.aa), (11, ExpressionPreset.ee), (12, ExpressionPreset.ih), (13, ExpressionPreset.oh), (14, ExpressionPreset.ou),
        };
    }
}
