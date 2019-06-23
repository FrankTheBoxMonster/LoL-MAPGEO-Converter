using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoFileReader {

        private static readonly string[] knownSamplerNames = { "DiffuseTexture",
                                                               "Diffuse_Texture",
                                                               "Bottom_Texture",
                                                               "FlipBook_Texture",
                                                               "GlowTexture",
                                                               "Mask_Textures",
                                                               "Mask_Texture"  // "Diffuse_Texture" should come before this one
                                                             };
        private static readonly string[] knownEmissiveColorNames = { "Emissive_Color", "Color_01" };


        private FileWrapper mapgeoFile;
        private FileWrapper binFile;

        private int version;

        private MapGeoFloatDataBlock[] floatDataBlocks;
        private MapGeoTriBlock[] triBlocks;
        private List<string> materialNames = new List<string>();
        private MapGeoObjectBlock[] objectBlocks;
        private Dictionary<string, MapGeoMaterial> materialTextureMap = new Dictionary<string, MapGeoMaterial>();  // <material name, material>



        public MapGeoFileReader(FileWrapper mapgeoFile, int version, FileWrapper binFile) {
            this.mapgeoFile = mapgeoFile;
            this.binFile = binFile;
            this.version = version;


            int unknownHeaderBytes = mapgeoFile.ReadInt();  // always zero?

            if(unknownHeaderBytes != 0) {
                Console.WriteLine("\nunknown header bytes are non-zero:  " + unknownHeaderBytes);
                Program.Pause();
            }


            int unknownBlockCount = mapgeoFile.ReadInt();

            for(int i = 0; i < unknownBlockCount; i++) {
                // values appear ot be mostly either 0x00, 0x02, or 0x03, but there's also some 0x01, 0x04, 0x07, 0x0e (unknown significance)
                for(int j = 0; j < 128; j++) {
                    mapgeoFile.ReadByte();  // these appear to be 32 ints but we'll just read 128 bytes for now
                }
            }



            Console.WriteLine("\nreading float data blocks:  " + mapgeoFile.GetFilePosition());
            this.ReadFloatDataBlocks();


            Console.WriteLine("\nreading tri blocks:  " + mapgeoFile.GetFilePosition());
            this.ReadTriBlocks();


            Console.WriteLine("\nreading object blocks:  " + mapgeoFile.GetFilePosition());
            this.ReadObjectBlocks();



            Console.WriteLine("\n\n");
            if(binFile == null) {
                Console.WriteLine("no materials .bin file provided, so materials will not be read");
            } else {
                this.ReadMaterials();
            }


            Console.WriteLine("\nlast read location:  " + mapgeoFile.GetFilePosition());
            Console.WriteLine("missed bytes:  " + (mapgeoFile.GetLength() - mapgeoFile.GetFilePosition()));
            Console.WriteLine();

            mapgeoFile.Close();

            if(binFile != null) {
                binFile.Close();
            }
        }


        #region ReadFloatDataBlocks()

        private void ReadFloatDataBlocks() {
            int floatDataBlockCount = mapgeoFile.ReadInt();
            Console.WriteLine("float data block count:  " + floatDataBlockCount);

            floatDataBlocks = new MapGeoFloatDataBlock[floatDataBlockCount];
            for(int i = 0; i < floatDataBlockCount; i++) {
                Console.WriteLine("reading float data block " + (i + 1) + "/" + floatDataBlockCount + ", " + ((i + 1f) / floatDataBlockCount * 100) + "% complete, offset " + mapgeoFile.GetFilePosition());


                MapGeoFloatDataBlock dataBlock = new MapGeoFloatDataBlock();
                floatDataBlocks[i] = dataBlock;


                int byteSize = mapgeoFile.ReadInt();
                int floatCount = byteSize / 4;  // 4 bytes per float

                dataBlock.data = new float[floatCount];
                for(int j = 0; j < floatCount; j++) {
                    dataBlock.data[j] = mapgeoFile.ReadFloat();
                }
            }
        }

        #endregion

        #region ReadTriBlocks()

        private void ReadTriBlocks() {
            int triBlockCount = mapgeoFile.ReadInt();
            Console.WriteLine("tri block count:  " + triBlockCount);

            triBlocks = new MapGeoTriBlock[triBlockCount];
            for(int i = 0; i < triBlockCount; i++) {
                Console.WriteLine("reading tri block " + (i + 1) + "/" + triBlockCount + ", " + ((i + 1f) / triBlockCount * 100) + "% complete, offset " + mapgeoFile.GetFilePosition());


                MapGeoTriBlock triBlock = new MapGeoTriBlock();
                triBlocks[i] = triBlock;


                int byteSize = mapgeoFile.ReadInt();
                int triCount = byteSize / 2 / 3;  // 2 bytes per vertex index, 3 indices per tri

                triBlock.tris = new MapGeoTri[triCount];
                for(int j = 0; j < triCount; j++) {
                    MapGeoTri tri = new MapGeoTri();
                    triBlock.tris[j] = tri;

                    tri.vertexIndices = new int[3];
                    for(int k = 0; k < 3; k++) {
                        tri.vertexIndices[k] = mapgeoFile.ReadShort();
                    }
                }
            }
        }

        #endregion

        #region ReadObjectBlocks()

        private void ReadObjectBlocks() {
            int objectBlockCount = mapgeoFile.ReadInt();
            Console.WriteLine("object block count:  " + objectBlockCount);

            objectBlocks = new MapGeoObjectBlock[objectBlockCount];
            for(int i = 0; i < objectBlockCount; i++) {
                Console.WriteLine("reading object block " + (i + 1) + "/" + objectBlockCount + ", " + ((i + 1f) / objectBlockCount * 100) + "% complete, offset " + mapgeoFile.GetFilePosition());


                MapGeoObjectBlock objectBlock = new MapGeoObjectBlock();
                objectBlocks[i] = objectBlock;


                int objectNameLength = mapgeoFile.ReadInt();
                objectBlock.objectName = mapgeoFile.ReadString(objectNameLength);

                int vertexCount = mapgeoFile.ReadInt();  // not important since we write submeshes, not full objects
                int type1 = mapgeoFile.ReadInt();
                int type2 = mapgeoFile.ReadInt();

                objectBlock.vertexBlockIndex = mapgeoFile.ReadInt();


                objectBlock.uvBlockIndex = -1;

                // these are likely useful in some other way, pretty sure we just added `type2` values as we found them without
                // actually trying to correlate them to anything
                // 
                // `type1` values is how float data block formats are determined:
                //   - 0x01:  block contains both vertex and UV data
                //   - 0x02:  block contains only vertex data, with UV data coming from a separate block (this is used for duplicate mesh objects, so that
                //            they can have separate positions without having separate UV coords, which makes the transformation matrix down below redundant)
                if(type1 == 0x01 && (type2 == 0x02 || type2 == 0x00 || type2 == 0x05 || type2 == 0x04)) {
                    objectBlock.uvBlockIndex = objectBlock.vertexBlockIndex;
                } else if(type1 == 0x02 && (type2 == 0x00 || type2 == 0x01 || type2 == 0x02)) {
                    objectBlock.uvBlockIndex = mapgeoFile.ReadInt();
                } else {
                    Console.WriteLine("\nunrecognized object type combination " + type1 + "/" + type2 + ", current offset " + mapgeoFile.GetFilePosition());
                    Program.Pause();
                }


                int totalTriIndexCount = mapgeoFile.ReadInt();  // total tri count = this value / 3 (again not really important since we only care about submeshes)
                objectBlock.triBlockIndex = mapgeoFile.ReadInt();

                int submeshCount = mapgeoFile.ReadInt();

                objectBlock.submeshes = new MapGeoSubmesh[submeshCount];
                for(int j = 0; j < submeshCount; j++) {
                    MapGeoSubmesh submesh = new MapGeoSubmesh();
                    objectBlock.submeshes[j] = submesh;


                    int unknown = mapgeoFile.ReadInt();  // always zero?

                    int materialNameLength = mapgeoFile.ReadInt();
                    submesh.materialName = mapgeoFile.ReadString(materialNameLength);

                    if(materialNames.Contains(submesh.materialName) == false) {
                        materialNames.Add(submesh.materialName);
                    }

                    // the file format handles tri starts based on per-vertex index (the tri corners), but we handle
                    // based on per-tri index (the total tri), because it's easier to write .obj files that way
                    int triIndexStartIndex = mapgeoFile.ReadInt();
                    submesh.triStartIndex = triIndexStartIndex / 3;

                    int triIndexCount = mapgeoFile.ReadInt();
                    submesh.triCount = triIndexCount / 3;


                    // both of these are non-zero ints, but their use is unknown, but they appear to be another set
                    // of `startIndex` and `count` values, not sure what for though since we don't need them for anything
                    int unknown1 = mapgeoFile.ReadInt();
                    int unknown2 = mapgeoFile.ReadInt();
                }


                // no idea what this section is, we just read it as bytes for now
                // 
                // in actuality, this is roughly a byte (absent in version 5) followed by a bunch of floats, followed
                // by another byte and more floats
                // 
                // these two lone bytes must be read as single bytes in order to make the surrounding bytes become valid floats
                // 
                // there is also a section of floats that always appears to be { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } },
                // this pattern obviously appears to be an identity matrix, which might show that the format supports the ability to transform
                // duplicate objects without needing to use separate vertex blocks like what is done in practice

                if(version == 6) {
                    mapgeoFile.ReadByte();  // necessary to fix version 5/6 differences, signifcance unknown, only known difference between versions
                }

                for(int j = 0; j < 24; j++) {  // 6 floats
                    mapgeoFile.ReadByte();
                }

                // here is the identity matrix section
                for(int j = 0; j < 64; j++) {  // 16 floats
                    mapgeoFile.ReadByte();
                }

                mapgeoFile.ReadByte();

                for(int j = 0; j < 108; j++) {  // 27 floats
                    mapgeoFile.ReadByte();
                }



                // for some reason, lightmap texture names are provided directly in mapgeo files, while color textures are in the material bin files

                int lightmapTextureNameLength = mapgeoFile.ReadInt();
                objectBlock.lightmapTextureName = mapgeoFile.ReadString(lightmapTextureNameLength);


                for(int j = 0; j < 16; j++) {  // 4 floats, possibly something with lightmap data
                    mapgeoFile.ReadByte();
                }
            }
        }

        #endregion


        #region ReadMaterials()

        private void ReadMaterials() {
            Console.WriteLine("reading materials .bin file");
            Console.WriteLine("material count:  " + materialNames.Count);


            // scanning the .bin file to try to figure out material texture mappings
            // 
            // this would be simpler to do with Fantome, but that wasn't an option in the original Java version of this converter
            // 
            // instead, it reads the entire file as a string, looks for material names, splits the string to get the definition
            // for each material, then scans each definition for the first instance of ".dds" and takes that string as its diffuse texture


            int binFileLength = binFile.GetLength();
            string binFileText = binFile.ReadString(binFileLength);


            // sorting the material names by length, descending
            // 
            // this is a hacky way to prevent `IndexOf()` collisions between "ABC" and "ABCDEF", which will
            // cause "ABC" to point to "ABCDEF" if the latter comes first in the file
            // 
            // by sorting by length first, we ensure that th elongest are found first, and we can then check if
            // the index was already found to prevent collisions

            Console.WriteLine("\nsorting materials by name");
            for(int i = 0; i < materialNames.Count; i++) {
                for(int j = i + 1; j < materialNames.Count; j++) {
                    string a = materialNames[i];
                    string b = materialNames[j];

                    if(a.Length < b.Length) {
                        materialNames[i] = b;
                        materialNames[j] = a;
                    }
                }
            }


            Console.WriteLine("scanning materials");
            List<int> materialNameStartIndices = new List<int>();
            for(int i = 0; i < materialNames.Count; i++) {
                Console.WriteLine("scanning for material " + (i + 1) + "/" + materialNames.Count + ", " + ((i + 1f) / materialNames.Count * 100) + "% complete");

                int index = -1;
                while(true) {
                    index = binFileText.IndexOf(materialNames[i], index + 1);
                    if(index < 0 || materialNameStartIndices.Contains(index) == false) {
                        break;
                    }
                }

                if(index < 0) {
                    Console.WriteLine("\n\n  couldn't find material \"" + materialNames[i] + "\" (will try to keep reading, but this material will not export properly)");
                    Program.Pause();
                }

                materialNameStartIndices.Add(index);  // even invalid indices need to be added to maintain the parallel lists
            }


            // now sorting the lists by index
            Console.WriteLine("\n\nsorting materials by index");
            for(int i = 0; i < materialNames.Count; i++) {
                for(int j = i + 1; j < materialNames.Count; j++) {
                    int a = materialNameStartIndices[i];
                    int b = materialNameStartIndices[j];

                    if(a > b) {
                        materialNameStartIndices[i] = b;
                        materialNameStartIndices[j] = a;

                        string temp = materialNames[i];
                        materialNames[i] = materialNames[j];
                        materialNames[j] = temp;
                    }
                }
            }


            // splitting the bin file text into each material's definition
            Console.WriteLine("\n\nsplitting bin file");

            Dictionary<string, string> binFileSplits = new Dictionary<string, string>();  // <material name, string split>
            int lastMaterialIndex = materialNames.Count - 1;

            for(int i = 0; i < lastMaterialIndex; i++) {  // the last material gets skipped because we have to handle it differently
                int startIndex = materialNameStartIndices[i];
                int endIndex = materialNameStartIndices[i + 1];
                int count = endIndex - startIndex + 1;

                string stringSplit = "";
                if(startIndex >= 0) {
                    stringSplit = binFileText.Substring(startIndex, count);
                } else {
                    // material name was not found (likely an incorrect bin file or an emissive color with no diffuse)
                    stringSplit = "";
                }

                binFileSplits.Add(materialNames[i], stringSplit);
            }


            string lastMaterialName = materialNames[lastMaterialIndex];
            int lastSplitIndex = materialNameStartIndices[lastMaterialIndex];

            // just take the entire remaining string (will likely contain a lot of garbage)
            // note:  this will likely contain a lot of irrelevant data, so this will likely fail if the last material is an emissive color
            string lastSplit = binFileText.Substring(lastSplitIndex);
            binFileSplits.Add(lastMaterialName, lastSplit);



            // now we look for .dds files
            // 
            // thought process:
            //   - the .bin file maps textures onto materials
            //   - textures are applied to samplers
            //   - samplers each have a name
            //   - we will look for known sampler names that will correspond to the textures we want
            //   - simple 1-texture samplers use the name "DiffuseTexture"
            //   - "flow map" samplers use the name "Diffuse_Texture", with an underscore (as well as "FlowMap" and "Noise_Texture", but only diffuse matters)
            //   - "blend" smaplers use "Bottom_Texture", "Middle_Texture", "Top_Texture", "Extras_Texture", and "MASK", however "Bottom_Texture" appears to
            //     be the most general and therefore we will just fallback to that with no blending
            // 
            // note that these names are likely just an arbitrary convention, so if this naming pattern is ever deviated from, the scanner will break,
            // although it's also likely for the scanner to break even without a change in pattern
            // 
            // given Riot's general tendency to not change things too often, we should be relatively safe
            // 
            // 
            // also note that this assumes all .dds file paths will be in the format "ASSETS/.../xxx.dds", which again is arbitrary and will break
            // the scanner if ever changed, but is highly unlikely to change any time soon


            int entryIndex = 0;
            foreach(KeyValuePair<string, string> entry in binFileSplits) {
                Console.WriteLine("scanning splits for texture " + (entryIndex + 1) + "/" + materialNames.Count + ", " + ((entryIndex + 1f) / materialNames.Count * 100) + "% complete");

                MapGeoMaterial material = new MapGeoMaterial();

                material.materialName = entry.Key;
                string stringSplit = entry.Value;

                material.textureName = "";
                material.ambientColor = new float[4] { 0, 0, 0, 1 };

                if(stringSplit != "") {
                    int samplerNameStartIndex = -1;
                    for(int i = 0; i < MapGeoFileReader.knownSamplerNames.Length; i++) {
                        samplerNameStartIndex = stringSplit.IndexOf(MapGeoFileReader.knownSamplerNames[i]);

                        if(samplerNameStartIndex >= 0) {
                            // found a sampler, so just use its texture

                            if(MapGeoFileReader.knownSamplerNames[i] == "FlipBook_Texture") {
                                Console.WriteLine("\n\n  \"FlipBook_Texture\" is in use for material \"" + material.materialName + "\"");
                                Program.Pause();
                            }

                            if(MapGeoFileReader.knownSamplerNames[i] == "GlowTexture") {
                                Console.WriteLine("\n\n  \"GlowTexture\" is in use for material \"" + material.materialName + "\"");
                                Program.Pause();
                            }

                            break;
                        } else {
                            // try another option
                        }
                    }

                    if(samplerNameStartIndex < 0) {
                        Console.WriteLine("\n\n  couldn't find sampler keys for material \"" + material.materialName + "\" (will try to find emissive colors)");
                        Program.Pause();
                    } else {
                        int startIndex = stringSplit.IndexOf("ASSETS/", samplerNameStartIndex);
                        int endIndex = stringSplit.IndexOf(".dds", startIndex) + 3;  // adding `+3` for the length of "dds" itself ('.' is already accounted)
                        int count = endIndex - startIndex + 1;

                        material.textureName = stringSplit.Substring(startIndex, count);
                    }



                    int emissiveColorStartIndex = -1;
                    string emissiveColorName = "";
                    for(int i = 0; i < MapGeoFileReader.knownEmissiveColorNames.Length; i++) {
                        emissiveColorStartIndex = stringSplit.IndexOf(MapGeoFileReader.knownEmissiveColorNames[i]);

                        if(emissiveColorStartIndex >= 0) {
                            // found an emissive color, so use this
                            emissiveColorName = MapGeoFileReader.knownEmissiveColorNames[i];
                            break;
                        } else {
                            // try another option
                        }
                    }

                    if(emissiveColorStartIndex < 0) {
                        if(samplerNameStartIndex < 0) {
                            Console.WriteLine("\n\n  still couldn't find emissive color keys for material \"" + material.materialName + "\" (will replace the material with a bright pink color)");
                            Program.Pause();

                            material.ambientColor[0] = 1.0f;
                            material.ambientColor[1] = 0.0f;
                            material.ambientColor[2] = 1.0f;
                            material.ambientColor[3] = 1.0f;
                        } else {
                            // as long as we have a diffuse texture then we don't need an emissive color fallback
                        }
                    } else {
                        // diffuse textures can have emissive colors too

                        int nameEndIndex = emissiveColorStartIndex + emissiveColorName.Length;
                        int valueTypeIndex = nameEndIndex + 4;  // have to skip over the hash for "value"

                        if(stringSplit[valueTypeIndex] != 0x0d) {
                            Console.WriteLine("\n\n  emissive color has unrecognized value type for material \"" + material.materialName + "\":  " + ((int) stringSplit[valueTypeIndex]).ToString("X2"));
                            Program.Pause();
                        } else {
                            // four floats, rgba
                            int colorOffset = valueTypeIndex + 1;  // type is one byte
                            for(int i = 0; i < 4; i++) {
                                int baseOffset = colorOffset + (i * 4);

                                byte[] bytes = new byte[4];

                                for(int j = 0; j < 4; j++) {
                                    bytes[j] = (byte) stringSplit[baseOffset + j];
                                }


                                // endianness cannot be set directly and is dependent on the computer's system architecture
                                if(System.BitConverter.IsLittleEndian == false) {
                                    // bytes are to be read in big-endian format, so reverse the array

                                    byte temp = bytes[3];
                                    bytes[3] = bytes[0];
                                    bytes[0] = temp;

                                    temp = bytes[2];
                                    bytes[2] = bytes[1];
                                    bytes[1] = temp;
                                }


                                float value = System.BitConverter.ToSingle(bytes, 0);

                                material.ambientColor[i] = value;
                            }
                        }
                    }
                } else {
                    // can't find a texture name if we couldn't find the material name earlier
                    material.textureName = "";
                }

                materialTextureMap.Add(material.materialName, material);
                entryIndex++;
            }
        }

        #endregion


        #region ConvertFiles()

        public void ConvertFiles() {
            Console.WriteLine("\n\nwriting .obj file");

            string folderPath = this.mapgeoFile.GetFolderPath();

            string baseFileName = this.mapgeoFile.GetName();
            string mtlFileName = baseFileName + ".mtl";  // storing this for later to use in the .obj file


            FileWrapper objFileWrapper = new FileWrapper(folderPath + baseFileName + ".obj");
            objFileWrapper.Clear();  // clearing any pre-existing file

            TextFileWriter objFile = new TextFileWriter(objFileWrapper);
            objFile.WriteLine("# .MAPGEO file converted to .OBJ format by FrankTheBoxMonster");
            objFile.WriteBlankLine();


            TextFileWriter mtlFile = null;
            if(binFile != null) {
                FileWrapper mtlFileWrapper = new FileWrapper(folderPath + mtlFileName);
                mtlFileWrapper.Clear();  // same as above

                mtlFile = new TextFileWriter(mtlFileWrapper);
                mtlFile.WriteLine("# .MTL file for an accompanying .OBJ file, converted from .MAPGEO file format by FrankTheBoxMonster");
            }


            if(mtlFile != null) {
                objFile.WriteLine("mtllib " + mtlFileName);
            }


            Dictionary<int, MapGeoVertexBlock> vertexBlocks = new Dictionary<int, MapGeoVertexBlock>();
            Dictionary<int, MapGeoUVBlock> uvBlocks = new Dictionary<int, MapGeoUVBlock>();

            // .obj files are 1-indexed, but normal people use 0-indexed arrays, so this value is
            // initialized to `1` to counter this offset
            int currentVertexTotal = 1;

            for(int i = 0; i < objectBlocks.Length; i++) {
                Console.WriteLine("writing .obj file entry " + (i + 1) + "/" + objectBlocks.Length + ", " + ((i + 1f) / objectBlocks.Length * 100) + "% complete");

                MapGeoObjectBlock objectBlock = objectBlocks[i];
                bool hasLightmap = (objectBlock.lightmapTextureName.Length > 0);


                MapGeoVertexBlock vertexBlock = null;
                MapGeoUVBlock uvBlock = null;

                if(vertexBlocks.TryGetValue(objectBlock.vertexBlockIndex, out vertexBlock) == false) {
                    MapGeoFloatDataBlock floatDataBlock = floatDataBlocks[objectBlock.vertexBlockIndex];

                    if(objectBlock.vertexBlockIndex != objectBlock.uvBlockIndex) {
                        vertexBlock = floatDataBlock.ToVertexOnlyBlock();

                        vertexBlocks.Add(objectBlock.vertexBlockIndex, vertexBlock);

                        // we'll get the UV block later
                    } else {
                        floatDataBlock.ToVertexAndUVBlock(out vertexBlock, out uvBlock, hasLightmap);

                        vertexBlocks.Add(objectBlock.vertexBlockIndex, vertexBlock);
                        uvBlocks.Add(objectBlock.vertexBlockIndex, uvBlock);
                    }
                }


                if(uvBlock == null) {
                    if(uvBlocks.TryGetValue(objectBlock.uvBlockIndex, out uvBlock) == false) {
                        // already tried getting the UV block from above in "vertex + UV" format, so if we still
                        // don't have a UV block then it must be a "UV only" format

                        MapGeoFloatDataBlock floatDataBlock = floatDataBlocks[objectBlock.uvBlockIndex];

                        uvBlock = floatDataBlock.ToUVOnlyBlock(hasLightmap);
                        uvBlocks.Add(objectBlock.uvBlockIndex, uvBlock);
                    }
                }


                MapGeoTriBlock triBlock = triBlocks[objectBlock.triBlockIndex];

                int totalVertexCount = vertexBlock.vertices.Length;
                int totalTriCount = triBlock.tris.Length;

                try {
                    objFile.WriteBlankLines(4);

                    objFile.WriteLine("g default");
                    objFile.WriteBlankLine();

                    for(int j = 0; j < totalVertexCount; j++) {
                        MapGeoVertex vertex = vertexBlock.vertices[j];

                        // have to negate X-coordinates due to how Maya's coordinate axes work
                        objFile.WriteLine("v " + (-1 * vertex.position[0]) + " " + vertex.position[1] + " " + vertex.position[2]);
                    }

                    objFile.WriteBlankLine();


                    for(int j = 0; j < totalVertexCount; j++) {
                        MapGeoUV uv = uvBlock.uvs[j];

                        // have to invert the V-coordinate due to how Riot handles their UV coordinates (flipped upside-down about the Y=0.5 axis)
                        objFile.WriteLine("vt " + uv.colorUV[0] + " " + (1f - uv.colorUV[1]));
                    }

                    objFile.WriteBlankLine();


                    for(int j = 0; j < totalVertexCount; j++) {
                        MapGeoVertex vertex = vertexBlock.vertices[j];

                        // have to negate X-coordinates due to how Maya's coordinate axes work
                        objFile.WriteLine("vn " + (-1 * vertex.normalDirection[0]) + " " + vertex.normalDirection[1] + " " + vertex.normalDirection[2]);
                    }

                    objFile.WriteBlankLine();


                    for(int j = 0; j < objectBlock.submeshes.Length; j++) {
                        MapGeoSubmesh submesh = objectBlock.submeshes[j];


                        objFile.WriteLine("s off");

                        string objectName = "polySurfaceMapGeoMesh" + (i + 1);
                        if(objectBlock.submeshes.Length > 1) {
                            objectName += "_" + (j + 1);
                        }
                        objFile.WriteLine("g " + objectName);

                        string materialName = objectName + "SG";  // need to save this for later
                        MapGeoMaterial material = materialTextureMap[submesh.materialName];
                        if(mtlFile != null) {
                            objFile.WriteLine("usemtl " + materialName);
                        }


                        for(int k = 0; k < submesh.triCount; k++) {
                            // due to having to negate the X-coordinates of all vertices, we need to flip the draw order of
                            // any two of the three vertices that make up each face to get them to face in the correct direction

                            int[] triIndices = new int[3];

                            // have to renumber tris so that they refer to their proper vertices
                            //     
                            // the .mapgeo format refers to vertices based on their index with in their vertex group, however
                            // the .obj format refers to vertices based on their index within the entire file, so we need to
                            // keep a running total for the vertex count and offset accordingly based on that

                            int triIndex = submesh.triStartIndex + k;
                            int baseIndex = triIndex * 3;
                            for(int m = 0; m < triIndices.Length; m++) {  // skipping from 'k' over 'L' and straight to 'm' because 'L' looks like a '1'
                                int currentIndex = triBlock.tris[triIndex].vertexIndices[m];
                                int offsetIndex = currentIndex + currentVertexTotal;  // remember that the 1-index offset is already accounted for here

                                triIndices[m] = offsetIndex;
                            }


                            // swap any two for the Maya normals fix
                            int temp = triIndices[0];
                            triIndices[0] = triIndices[1];
                            triIndices[1] = temp;


                            string line = "f";

                            for(int m = 0; m < triIndices.Length; m++) {
                                int index = triIndices[m];

                                line += " " + index + "/" + index + "/" + index;
                            }

                            objFile.WriteLine(line);
                        }



                        // now we need to add the material that we referenced earlier in the .obj file to the accompanying .mtl file
                        if(mtlFile != null) {
                            mtlFile.WriteBlankLines(4);

                            mtlFile.WriteLine("newmtl " + materialName);

                            // this corresponds to an illumination mode of "highlight on" (default export value is 4, but mode 4
                            // causes reflective surfaces, so using mode 2 disables these, and is also the value used by all examples of
                            // the .obj file format
                            // 
                            // we could also use mode 1 ("color on, ambient on") or mode 0 ("color on, ambient off")
                            mtlFile.WriteLine("illum 2");

                            /*mtlFile.WriteLine("Kd 0.00 0.00 0.00");

                            string line = "Ka";
                            for(int k = 0; k < 3; k++) {  // Riot defines colors with four floats but Maya and .obj seem to only want three
                                line += " " + material.ambientColor[k];
                            }
                            mtlFile.WriteLine(line);*/

                            /*mtlFile.WriteLine("Kd 0.00 0.00 0.00");
                            mtlFile.WriteLine("Ka 1.00 0.00 0.00");*/

                            string line = "";
                            for(int k = 0; k < 3; k++) {
                                line += " " + material.ambientColor[k];
                            }

                            mtlFile.WriteLine("Kd" + line);
                            mtlFile.WriteLine("Ka" + line);

                            mtlFile.WriteLine("Tf 1.00 1.00 1.00");


                            if(material.textureName != "") {
                                // fix for .tga files, carried over from .NVR, likely pointless in practice
                                if(material.textureName.ToLower().Contains(".dds") == false) {
                                    material.textureName = material.textureName.Substring(0, material.textureName.LastIndexOf('.')) + ".dds";
                                }

                                mtlFile.WriteLine("map_Kd textures/" + material.textureName);
                            }

                            mtlFile.WriteLine("Ni 1.00");
                        }
                    }


                    currentVertexTotal += totalVertexCount;
                } catch(System.Exception e) {
                    Console.WriteLine("\n\n  error writing .obj file entry " + (i + 1) + ":  " + e.ToString());
                    Program.Pause();
                    continue;
                }
            }


            if(objFile != null) {
                objFile.Close();
            }

            if(mtlFile != null) {
                mtlFile.Close();
            }

            Console.WriteLine(".obj file written");
        }

        #endregion
    }
}
