using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoFloatDataBlock {

        public float[] data;


        public MapGeoFloatDataBlock() {

        }


        public MapGeoVertexBlock ToVertexOnlyBlock() {
            int vertexCount = this.data.Length / 6;  // 6 floats per vertex

            MapGeoVertexBlock vertexBlock = new MapGeoVertexBlock();
            vertexBlock.vertices = new MapGeoVertex[vertexCount];

            for(int i = 0; i < vertexCount; i++) {
                MapGeoVertex vertex = new MapGeoVertex();
                vertexBlock.vertices[i] = vertex;


                int offset = i * 6;

                vertex.position = new float[3];
                for(int j = 0; j < 3; j++) {
                    vertex.position[j] = this.data[offset + j];
                }

                vertex.normalDirection = new float[3];
                for(int j = 0; j < 3; j++) {
                    vertex.normalDirection[j] = this.data[offset + 3 + j];
                }
            }

            return vertexBlock;
        }

        public MapGeoUVBlock ToUVOnlyBlock(bool hasLightmap) {
            int floatsPerUV = 4;
            if(hasLightmap == false) {
                floatsPerUV = 2;  // no-lightmap meshes lack the two lightmap UV coords
            }


            int uvCount = this.data.Length / floatsPerUV;

            MapGeoUVBlock uvBlock = new MapGeoUVBlock();
            uvBlock.uvs = new MapGeoUV[uvCount];

            for(int i = 0; i < uvCount; i++) {
                MapGeoUV uv = new MapGeoUV();
                uvBlock.uvs[i] = uv;


                int offset = i * floatsPerUV;

                uv.colorUV = new float[2];
                for(int j = 0; j < 2; j++) {
                    uv.colorUV[j] = this.data[offset + j];
                }

                uv.lightmapUV = new float[2];
                if(hasLightmap == true) {
                    for(int j = 0; j < 2; j++) {
                        uv.lightmapUV[j] = this.data[offset + 2 + j];
                    }
                } else {
                    uv.lightmapUV[0] = 0;
                    uv.lightmapUV[1] = 0;
                }
            }

            return uvBlock;
        }

        public void ToVertexAndUVBlock(out MapGeoVertexBlock vertexBlock, out MapGeoUVBlock uvBlock, bool hasLightmap) {
            // these are interwoven, so unfortunately we can't just read a vertex block followed by a UV block

            int floatsPerVertex = 10;  // 10 floats per vertex/UV combo
            if(hasLightmap == false) {
                floatsPerVertex = 8;  // no-lightmap meshes lack the two extra lightmap UV coords
            }


            int vertexCount = this.data.Length / floatsPerVertex;

            vertexBlock = new MapGeoVertexBlock();
            vertexBlock.vertices = new MapGeoVertex[vertexCount]; 

            uvBlock = new MapGeoUVBlock();
            uvBlock.uvs = new MapGeoUV[vertexCount];

            for(int i = 0; i < vertexCount; i++) {
                MapGeoVertex vertex = new MapGeoVertex();
                vertexBlock.vertices[i] = vertex;

                MapGeoUV uv = new MapGeoUV();
                uvBlock.uvs[i] = uv;


                int offset = i * floatsPerVertex;

                vertex.position = new float[3];
                for(int j = 0; j < 3; j++) {
                    vertex.position[j] = this.data[offset + j];
                }

                vertex.normalDirection = new float[3];
                for(int j = 0; j < 3; j++) {
                    vertex.normalDirection[j] = this.data[offset + 3 + j];
                }


                uv.colorUV = new float[2];
                for(int j = 0; j < 2; j++) {
                    uv.colorUV[j] = this.data[offset + 6 + j];
                }

                uv.lightmapUV = new float[2];
                if(hasLightmap == true) {
                    for(int j = 0; j < 2; j++) {
                        uv.lightmapUV[j] = this.data[offset + 8 + j];
                    }
                } else {
                    uv.lightmapUV[0] = 0;
                    uv.lightmapUV[1] = 0;
                }
            }
        }
    }
}
