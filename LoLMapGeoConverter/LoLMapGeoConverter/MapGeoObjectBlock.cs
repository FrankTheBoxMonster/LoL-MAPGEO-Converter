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


        public MapGeoVertexBlock vertexBlock;


        public MapGeoObjectBlock() {

        }


        public float[] ApplyTransformationMatrix(float[] originalPosition, bool isDirection) {
            // this is the only vector math we really do, so we can get away with not needing a proper Vector3 implementation


            float[] result = new float[3];

            result[0] = (transformationMatrix[0] * originalPosition[0]) + (transformationMatrix[4] * originalPosition[1]) + (transformationMatrix[8] * originalPosition[2]);
            result[1] = (transformationMatrix[1] * originalPosition[0]) + (transformationMatrix[5] * originalPosition[1]) + (transformationMatrix[9] * originalPosition[2]);
            result[2] = (transformationMatrix[2] * originalPosition[0]) + (transformationMatrix[6] * originalPosition[1]) + (transformationMatrix[10] * originalPosition[2]);

            if(isDirection == false) {
                // directions don't have translations applied
                // 
                // direction vectors set the w-component of the original position to 0 instead of 1 so that the translations naturally cancel out

                result[0] += transformationMatrix[12];
                result[1] += transformationMatrix[13];
                result[2] += transformationMatrix[14];
            } else {
                // need to normalize the direction
                float squareMagnitude = (result[0] * result[0]) + (result[1] * result[1]) + (result[2] * result[2]);
                float magnitude = (float) System.Math.Sqrt(squareMagnitude);

                result[0] /= magnitude;
                result[1] /= magnitude;
                result[2] /= magnitude;
            }


            return result;
        }
    }
}
