using System.Collections.Generic;
using UnityEngine;

namespace VF.Component
{
    // Stands in for VRCFury's component base class, which the exporter finds
    // by its full name without referencing VRCFury.
    public class VRCFuryComponent : MonoBehaviour
    {
    }
}

namespace VrVlogExporterTests.VrcFuryFixtures
{
    // Same members as VRCFury's feature component and its Fix Write Defaults
    // setting, under other names so they do not shadow an installed VRCFury.
    public class FeatureModel
    {
    }

    public class FixWriteDefaults : FeatureModel
    {
        public enum FixWriteDefaultsMode { Auto, ForceOff, ForceOn, Disabled }
        public FixWriteDefaultsMode mode = FixWriteDefaultsMode.Auto;
    }

    public class FeatureComponent : VF.Component.VRCFuryComponent
    {
        public FeatureModel content;

        public IList<FeatureModel> GetAllFeatures() =>
            content != null ? new List<FeatureModel> { content } : new List<FeatureModel>();
    }
}
