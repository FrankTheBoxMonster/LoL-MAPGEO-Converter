using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoObjectBlock {

        public string objectName;
        public int[] floatDataBlockIndices;
        public int vertexFormatBlockIndex;
        public int triBlockIndex;
        public MapGeoSubmesh[] submeshes;
        public string lightmapTextureName;
        
        public string bakedPaintTextureName;
        public float bakedPaintTextureScaleU;
        public float bakedPaintTextureScaleV;
        public float bakedPaintTextureOffsetU;
        public float bakedPaintTextureOffsetV;


        // in order of bytes read, the matrix looks like this:
        // 
        // [  0  4  8 12 
        //    1  5  9 13
        //    2  6 10 14 
        //    3  7 11 15 ]
        // 
        // X-coords are also negated like everything else, but this should still only be handled at the very end
        public float[] transformationMatrix;


        public int unknownByte1;
        public int layerBitmask;

        public int v11UnknownByte;


        public MapGeoVertexBlock vertexBlock;


        public MapGeoObjectBlock() {

        }
    }
}
