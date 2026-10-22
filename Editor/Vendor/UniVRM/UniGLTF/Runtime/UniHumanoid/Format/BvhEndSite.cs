using System.IO;

namespace VRVlog.LilToonExporter.Vendor.UniHumanoid
{
    public class BvhEndSite : BvhNode
    {
        public BvhEndSite() : base("")
        {
        }

        public override void Parse(StringReader r)
        {
            r.ReadLine(); // offset
        }
    }
}
