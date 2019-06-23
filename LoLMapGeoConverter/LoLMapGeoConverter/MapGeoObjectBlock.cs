using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoObjectBlock {

        public string objectName;
        public int vertexBlockIndex;
        public int uvBlockIndex;
        public int triBlockIndex;
        public MapGeoSubmesh[] submeshes;
        public string lightmapTextureName;


        public MapGeoObjectBlock() {

        }
    }
}
