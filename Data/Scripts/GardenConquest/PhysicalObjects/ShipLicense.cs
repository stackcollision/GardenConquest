using System;
using System.Collections.Generic;
using System.Text;

using Sandbox.Common.ObjectBuilders;
using VRage.ObjectBuilders;

namespace GardenConquest.PhysicalObjects {
    class ShipLicense {
        public static MyObjectBuilder_Component Builder = 
            new MyObjectBuilder_Component() { SubtypeName = "ShipLicense" };

        public static SerializableDefinitionId Definition = 
            new VRage.ObjectBuilders.SerializableDefinitionId(
                typeof(MyObjectBuilder_InventoryItem), "ShipLicense"
                );
    }
}
