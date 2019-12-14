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
        NormalDirection = 0x02,
        ColorUV = 0x07,
        LightmapUV = 0x0e
    }

    public enum MapGeoVertexPropertyFormat {
        Float2 = 0x01,
        Float3 = 0x02,
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
            }
        }
    }
}
