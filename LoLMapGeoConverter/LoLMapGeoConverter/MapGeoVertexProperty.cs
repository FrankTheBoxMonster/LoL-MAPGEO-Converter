using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoVertexProperty {

        public MapGeoVertexPropertyName name;
        public MapGeoVertexPropertyFormat format;


        public MapGeoVertexProperty() {

        }
    }

    public enum MapGeoVertexPropertyName {
        Position = 0x00,
        NormalDirection = 0x02,  // supposedly Normal is 0x01 and FogCoord is 0x02 but this doesn't seem to be true in practice, might have been changed at some point
        SecondaryColor = 0x04,  // only ever seen this on Odyssey_Yasuo.mapgeo (added sometime after the initial 10.6 PBE patch, originally wasn't present)
        ColorUV = 0x07,  // same as NormalDirection, apparently Texcoord0 is 0x05 and Texcoord2 is 0x07, but this is again not true in practice
        LightmapUV = 0x0e
    }

    public enum MapGeoVertexPropertyFormat {
        Float2 = 0x01,
        Float3 = 0x02,
        //Float4 = 0x03,  // never actually used?
        Color32BGRA = 0x04,  // only ever seen this on Odyssey_Yasuo.mapgeo (added sometime after the initial 10.6 PBE patch, originally wasn't present)
    }


    public static class MapGeoVertexPropertyFormatExtensions {
        public static int GetByteSize(this MapGeoVertexPropertyFormat format) {
            switch(format) {
                default:
                    throw new System.Exception("unrecognized MapGeoVertexPropertyFormat " + format);
                    break;

                case MapGeoVertexPropertyFormat.Float2:
                    return 2 * 4;
                case MapGeoVertexPropertyFormat.Float3:
                    return 3 * 4;
                case MapGeoVertexPropertyFormat.Color32BGRA:
                    return 4;
            }
        }
    }
}
