using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoFileReader {

        private const bool createMergedLayerFile = false;

        private static readonly string[] knownSamplerNames = { "DiffuseTexture",
                                                               "Diffuse_Texture",
                                                               "Bottom_Texture",
                                                               "FlipBook_Texture",
                                                               "GlowTexture",
                                                               "Glow_Texture",
                                                               "Mask_Textures",
                                                               "Mask_Texture",  // "Diffuse_Texture" should come before this one
                                                               "Scrolling_Texture"  // used with "ScrollingColor" samplers and comes with a "Color" param
                                                             };
        private static readonly string[] knownEmissiveColorNames = { "Emissive_Color",
                                                                     "Color_01",
                                                                     "Color",
                                                                     "ColorTop",  // used for horizontal emissive gradients (giving this one priority over the bottom color because it's so far been top is brither and bottom is darker, so the top one looks better and less dreary)
                                                                     "ColorBottom"  // used for horizontal emissive gradients (see above)
                                                                   };
        private static readonly string valueKeyHash = "\xca\xd3\x5e\x42";  // fnv1a32 hash of "value" --> 0x425ed3ca, string reads it backwards from what an int would read due to going one byte at a time rather than 4 bytes together as little endian

        private static readonly float[] identityMatrix = { 1, 0, 0, 0,
                                                           0, 1, 0, 0,
                                                           0, 0, 1, 0,
                                                           0, 0, 0, 1
                                                         };


        private FileWrapper mapgeoFile;
        private FileWrapper binFile;

        private int version;

        private MapGeoVertexFormatBlock[] vertexFormatBlocks;
        private MapGeoFloatDataBlock[] floatDataBlocks;
        private MapGeoTriBlock[] triBlocks;
        private List<string> materialNames = new List<string>();
        private MapGeoObjectBlock[] objectBlocks;
        private Dictionary<string, MapGeoMaterial> materialTextureMap = new Dictionary<string, MapGeoMaterial>();  // <material name, material>

        private Dictionary<int, int> layerBitmaskObjectCounts = new Dictionary<int, int>();  // <layer bitmask value, frequency>
        private int foundLayersBitmask = 0;  // stores how many distinct layers have been encountered (objects with non-0xff layer bitmasks get OR'd into here)

        private const int maxLayerCount = 8;
        private const int globalLayerBitmaskValue = 0xff;

        private Dictionary<int, char[]> layerObjectChars = new Dictionary<int, char[]>();  // dict[layer index] = char array, so that we don't have to concatenate constantly
        private Dictionary<int, string> layerObjectStrings = new Dictionary<int, string>();  // dict[layer index] = string of chars for whether the layer has the object at string[object index]
        private Dictionary<int, int> layerDuplicateReferences = new Dictionary<int, int>();  // dict[layer index] = duplicate layer reference index, -1 if unique or first

        private const char layerObjectStringCharPresent = '1';
        private const char layerObjectStringCharAbsent = '0';



        public MapGeoFileReader(FileWrapper mapgeoFile, int version, FileWrapper binFile) {
            this.mapgeoFile = mapgeoFile;
            this.binFile = binFile;
            this.version = version;


            if(version < 7) {
                int unknownHeaderBytes = mapgeoFile.ReadByte();  // moon said this was a bool that toggles on a global `SEPARATE_POINT_LIGHTS` property

                if(unknownHeaderBytes != 0) {
                    Console.WriteLine("\nunknown header bytes are non-zero:  " + unknownHeaderBytes);
                    Program.Pause();
                }
            } else {
                // version 7 removed this byte from the header
            }


            if(version >= 9) {
                int unknownHeaderFlags1 = mapgeoFile.ReadInt();  // added on version 9, significance unknown, so far only found a value of zero, possibly related to the object block data that was also added for this version

                if(unknownHeaderFlags1 != 0) {
                    Console.WriteLine("\nunknown v9 header flags 1 are non-zero:  " + unknownHeaderFlags1);
                    Program.Pause();
                }
            }

            if(version >= 10) {
                int unknownHeaderFlags2 = mapgeoFile.ReadInt();  // added on version 10, significance unknown, so far only found a value of zero, this is in addition to the similar contiguous value that was already added in version 9, unlike version 9 however this value is the only known change for version 10, although it's possible that it's related to the extra object block data added in version 11

                if(unknownHeaderFlags2 != 0) {
                    Console.WriteLine("\nunknown v10 header flags 2 are non-zero:  " + unknownHeaderFlags2);
                    Program.Pause();
                }
            }


            Console.WriteLine("\nreading vertex format blocks:  " + mapgeoFile.GetFilePosition());
            this.ReadVertexFormatBlocks();


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
            Program.Pause();

            mapgeoFile.Close();

            if(binFile != null) {
                binFile.Close();
            }
        }


        #region ReadVertexFormatBlocks()

        private void ReadVertexFormatBlocks() {
            // each object block is given what's pretty much a start index into this list, and then moves through it sequentially based on
            // the number of float data blocks that the object block uses
            // 
            // format blocks contain a list of vertex properties, defined as Pair<property name, byte format>, which appear in the format block
            // in the order that they are defined individually for each vertex in the float data blocks that use this format block
            // 
            // most of the time, a file will have a single format block with position/normal/UV data, as well as a pair of format blocks that
            // instead split up the data into position/normal in one block and UV data in the other (rarely, position is alone, and instead
            // you get normal/UV put in the second)
            // 
            // some maps also use lightmap data, which often means that they end up with sets of format blocks that have color + lightmap UVs and
            // other sets that have just color UVs with no lightmap data
            // 
            // these format blocks have room for up to 15 properties, and always contain exactly that many in the file, with the unused values simply
            // being filled in by default values that go completely ignored

            int vertexFormatBlockCount = mapgeoFile.ReadInt();
            vertexFormatBlocks = new MapGeoVertexFormatBlock[vertexFormatBlockCount];

            for(int i = 0; i < vertexFormatBlockCount; i++) {
                MapGeoVertexFormatBlock formatBlock = new MapGeoVertexFormatBlock();
                vertexFormatBlocks[i] = formatBlock;

                int unknown = mapgeoFile.ReadInt();  // this is apparently a type value for the format block itself, "dynamic", "static", or "streamed"
                if(unknown != 0) {
                    Console.WriteLine("\nvertex format block " + i + " had non-zero unknown byte:  " + unknown);
                    Program.Pause();
                }

                int definedPropertyCount = mapgeoFile.ReadInt();
                int placeholderPropertyCount = 15 - definedPropertyCount;

                formatBlock.properties = new MapGeoVertexProperty[definedPropertyCount];
                for(int j = 0; j < definedPropertyCount; j++) {
                    MapGeoVertexProperty property = new MapGeoVertexProperty();
                    formatBlock.properties[j] = property;

                    property.name = (MapGeoVertexPropertyName) mapgeoFile.ReadInt();
                    property.format = (MapGeoVertexPropertyFormat) mapgeoFile.ReadInt();


                    if(System.Enum.IsDefined(typeof(MapGeoVertexPropertyName), property.name) == false) {
                        Console.WriteLine("\nvertex format block " + i + " property " + j + " had unknown property name:  " + property.name);
                        Program.Pause();
                    }

                    if(System.Enum.IsDefined(typeof(MapGeoVertexPropertyFormat), property.format) == false) {
                        Console.WriteLine("\nvertex format block " + i + " property " + j + " had unknown property format:  " + property.format);
                        Program.Pause();
                    }
                }

                for(int j = 0; j < placeholderPropertyCount; j++) {
                    mapgeoFile.ReadInt();  // defaults to 0x00 (property name "position")
                    mapgeoFile.ReadInt();  // defaults to 0x03 (property format "float4"?)
                }
            }
        }

        #endregion

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


            for(int i = 0; i < maxLayerCount; i++) {
                layerObjectChars[i] = new char[objectBlockCount];  // so that we don't have to concatenate constantly
                layerDuplicateReferences[i] = -1;
            }


            objectBlocks = new MapGeoObjectBlock[objectBlockCount];
            for(int i = 0; i < objectBlockCount; i++) {
                Console.WriteLine("reading object block " + (i + 1) + "/" + objectBlockCount + ", " + ((i + 1f) / objectBlockCount * 100) + "% complete, offset " + mapgeoFile.GetFilePosition());

                MapGeoObjectBlock objectBlock = new MapGeoObjectBlock();
                objectBlocks[i] = objectBlock;


                int objectNameLength = mapgeoFile.ReadInt();
                objectBlock.objectName = mapgeoFile.ReadString(objectNameLength);

                int vertexCount = mapgeoFile.ReadInt();  // not important since we write submeshes, not full objects
                int floatDataBlockCount = mapgeoFile.ReadInt();
                objectBlock.vertexFormatBlockIndex = mapgeoFile.ReadInt();

                objectBlock.floatDataBlockIndices = new int[floatDataBlockCount];
                for(int j = 0; j < floatDataBlockCount; j++) {
                    objectBlock.floatDataBlockIndices[j] = mapgeoFile.ReadInt();
                }


                int totalTriIndexCount = mapgeoFile.ReadInt();  // total tri count = this value / 3 (again not really important since we only care about submeshes)
                objectBlock.triBlockIndex = mapgeoFile.ReadInt();


                int submeshCount = mapgeoFile.ReadInt();  // apparently this has a hard limit of 32 according to moon

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
                // 
                // starting with the TFT maps, they started actually using this transformation matrix for a couple objects, so we actually
                // have to make sure to handle it properly now, but most still use an identity matrix, although this might change moving forward

                if(version >= 6) {
                    mapgeoFile.ReadByte();  // necessary to fix version 5/6 differences, signifcance unknown, only known difference between versions
                }

                for(int j = 0; j < 6; j++) {  // appears to be an AABB
                    mapgeoFile.ReadFloat();
                }


                // here is the identity matrix section
                // 
                // note:  the matrix actually reads in column-major order, so the 5th element read is actually in
                // the 2nd column 1st row rather than the 1st column 2nd row
                // 
                // it also follows the usual "negate X" rule

                /*for(int j = 0; j < 64; j++) {  // 16 floats
                    mapgeoFile.ReadByte();
                }*/

                float[] matrix = new float[16];
                objectBlock.transformationMatrix = matrix;
                for(int j = 0; j < matrix.Length; j++) {
                    matrix[j] = mapgeoFile.ReadFloat();
                }


                // just going to keep this warning here for now

                for(int j = 0; j < matrix.Length; j++) {
                    if(matrix[j] != MapGeoFileReader.identityMatrix[j]) {
                        Console.WriteLine("non-identity matrix at " + mapgeoFile.GetFilePosition() + ":  ");

                        for(int k = 0; k < 4; k++) {
                            Console.Write(" ");
                            for(int m = 0; m < 4; m++) {
                                Console.Write(" " + matrix[m * 4 + k]);  // need to print in row-major order despite being stored in column-major order
                            }
                            Console.WriteLine();
                        }
                        Console.WriteLine();
                        Console.WriteLine("this shouldn't be any issue");
                        Program.Pause();

                        break;
                    }
                }


                // not sure what this is exactly, some sort of object flag?
                // 
                // objects with similar transparency effects tend to have the same value here, see the Project map for a good example
                // 
                // 0x1c - fuzzy circle lights, uses transparency, camera looks through these textures planes to project the light onto objects behind
                //      - also used for window lights on buildings
                // 0x1e - also transparency, but seems to also be connected to animated/scrolling textures?

                int unknownByte1 = mapgeoFile.ReadByte();
                if(unknownByte1 != 0x1f && unknownByte1 != 0x1c && unknownByte1 != 0x1e) {
                    Console.WriteLine("unrecognized unknownByte1:  0x" + unknownByte1.ToString("X2"));
                    Program.Pause();
                }

                /*if(unknownByte1 != 0x1f) {
                    Console.WriteLine("  non-0x1f unknownByte1:  0x" + unknownByte1.ToString("X2"));
                    Program.Pause();
                }*/

                objectBlock.unknownByte1 = unknownByte1;


                if(version >= 7) {
                    int layerBitmask = mapgeoFile.ReadByte();
                    objectBlock.layerBitmask = layerBitmask;

                    if(layerBitmaskObjectCounts.ContainsKey(layerBitmask) == false) {
                        layerBitmaskObjectCounts[layerBitmask] = 1;
                    } else {
                        layerBitmaskObjectCounts[layerBitmask]++;
                    }

                    if(layerBitmask != globalLayerBitmaskValue) {
                        foundLayersBitmask |= layerBitmask;
                    }


                    for(int j = 0; j < maxLayerCount; j++) {
                        int layerBitmaskFlag = (1 << j);

                        if((layerBitmask & layerBitmaskFlag) != layerBitmaskFlag) {
                            layerObjectChars[j][i] = layerObjectStringCharAbsent;
                        } else {
                            layerObjectChars[j][i] = layerObjectStringCharPresent;
                        }
                    }
                }


                
                if(version < 8) {
                    // 27 floats (might be 9x Vector3?), all really small values, first six appear to be UV range but the rest are really small
                    // 
                    // sets of threes also seem to have similar value ranges, however this is likely just coincidence
                    // 
                    // sample taken from the Project map's 0th object:
                    //   0.4532 0.5010 0.6329
                    //   0.3848 0.4264 0.5898
                    //   0.009715 0.01165 0.01663
                    //   -0.004104 -0.004169 -0.004142
                    //   -0.005723 -0.005705 -0.005619
                    //   0.1282 0.01433 0.02365
                    //   -0.1397 -0.1536 -0.2135
                    //   -0.0001638 -0.0003975 -0.0002642
                    //   -0.2823 -0.3129 -0.4322

                    for(int j = 0; j < 27; j++) {
                        mapgeoFile.ReadFloat();
                    }
                } else {
                    // version 8 removed this part
                }


                if(version >= 11) {
                    int unknownByte = mapgeoFile.ReadByte();  // version 11 added this, significance unknown, so far only found zeros, based on proximity to the layer bitmask, it could be related to that somehow?  but if it was just a simple extension from 8 layers to 16 layers then it shouldn't it have been assigned 0xff?  maybe not if they were not going to update every previous map to use the new layers...  might also be related to the extra header data that was added in version 10?

                    if(unknownByte != 0) {
                        Console.WriteLine("\nunknown v11 object block data is non-zero:  0x" + unknownByte.ToString("X2"));
                        Program.Pause();
                    }
                }



                // for some reason, lightmap texture names are provided directly in mapgeo files, while color textures are in the material bin files

                int lightmapTextureNameLength = mapgeoFile.ReadInt();
                objectBlock.lightmapTextureName = mapgeoFile.ReadString(lightmapTextureNameLength);


                for(int j = 0; j < 16; j++) {  // 4 floats, possibly something with lightmap data
                    mapgeoFile.ReadByte();
                }



                if(version >= 9) {
                    for(int j = 0; j < 20; j++) {  // added in version 9, significance unknown, so far only found values of all zero, possibly related to the header flag value that was also added for this version
                        int value = mapgeoFile.ReadByte();

                        if(value != 0) {
                            Console.WriteLine("\nunknown v9 object block data is non-zero:  0x" + value.ToString("X2"));
                            Program.Pause();
                        }
                    }
                }
            }


            for(int i = 0; i < maxLayerCount; i++) {
                string thisLayerString = new string(layerObjectChars[i]);  // so that we can just use string equality to compare the entire thing
                layerObjectStrings[i] = thisLayerString;

                for(int j = 0; j < i; j++) {
                    string otherLayerString = layerObjectStrings[j];

                    if(thisLayerString == otherLayerString) {
                        layerDuplicateReferences[i] = j;
                        break;
                    }
                }
            }


            Console.WriteLine("\n\nlayer bitmask object counts:");
            foreach(KeyValuePair<int, int> pair in layerBitmaskObjectCounts) {
                Console.WriteLine("  0x" + pair.Key.ToString("X2") + ":  " + pair.Value);
            }
            Console.WriteLine();
            Program.Pause();
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
                        string samplerName = MapGeoFileReader.knownSamplerNames[i];
                        //string searchString = "\x4c\x4f\xe7\x02\x10" + (char) (samplerName.Length % 256) + (char) (samplerName.Length / 256) + samplerName;

                        //samplerNameStartIndex = stringSplit.IndexOf(MapGeoFileReader.knownSamplerNames[i]);
                        //samplerNameStartIndex = stringSplit.IndexOf(searchString);
                        samplerNameStartIndex = stringSplit.IndexOf(samplerName);

                        if(samplerNameStartIndex >= 0) {
                            // found a sampler, so just use its texture

                            if(samplerName == "FlipBook_Texture") {
                                Console.WriteLine("\n\n  \"FlipBook_Texture\" is in use for material \"" + material.materialName + "\"");
                                Program.Pause();
                            }

                            if(samplerName == "GlowTexture") {
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
                        string stringSplitLower = stringSplit.ToLower();  // want to preserve texture path case for readability
                        int startIndex = stringSplitLower.IndexOf("assets/", samplerNameStartIndex);
                        int endIndex = stringSplitLower.IndexOf(".dds", startIndex) + 3;  // adding `+3` for the length of "dds" itself ('.' is already accounted)
                        int count = endIndex - startIndex + 1;

                        material.textureName = stringSplit.Substring(startIndex, count);
                    }



                    int emissiveColorStartIndex = -1;
                    string emissiveColorName = "";
                    for(int i = 0; i < MapGeoFileReader.knownEmissiveColorNames.Length; i++) {
                        string colorName = MapGeoFileReader.knownEmissiveColorNames[i];
                        //string searchString = "\x4c\x4f\xe7\x02\x10" + (char) (colorName.Length % 256) + (char) (colorName.Length / 256) + colorName;

                        //emissiveColorStartIndex = stringSplit.IndexOf(MapGeoFileReader.knownEmissiveColorNames[i]);
                        //emissiveColorStartIndex = stringSplit.IndexOf(searchString);

                        // need to make sure we find the actual sampler param key followed by its value key hash and not
                        // just another random string that happens to contain "Color", since then we might try to read
                        // the rest of that other string as bad data
                        emissiveColorStartIndex = stringSplit.IndexOf(MapGeoFileReader.knownEmissiveColorNames[i] + MapGeoFileReader.valueKeyHash);


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
                            Console.WriteLine("\n  still couldn't find emissive color keys for material \"" + material.materialName + "\" (will replace the material with a bright pink color)\n");
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

                        if(stringSplit[valueTypeIndex] != 0x0d) {  // corresponds to a value type of "vector4" (RGBA float color)
                            Console.WriteLine("\n\n  emissive color has unrecognized value type for material \"" + material.materialName + "\":  0x" + ((int) stringSplit[valueTypeIndex]).ToString("X2"));
                            Program.Pause();
                        } else {
                            if(samplerNameStartIndex < 0) {  // only need to report this if we gave a warning for it earlier
                                Console.WriteLine("\n  found emissive color for missing sampler material \"" + material.materialName + "\" (material might only be just a color with no texture, but should still check to make sure we aren't missing a new sampler key)\n");
                                Program.Pause();
                            }

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


        #region BuildVertexBlock()

        private MapGeoVertexBlock BuildVertexBlock(MapGeoObjectBlock objectBlock) {
            if(objectBlock.vertexBlock != null) {
                // multi-layer objects can save some time by reusing their block from last time
                return objectBlock.vertexBlock;
            }


            MapGeoVertexBlock vertexBlock = new MapGeoVertexBlock();
            objectBlock.vertexBlock = vertexBlock;  // save for next time, if any


            List<MapGeoVertexPropertyName> allPropertyNames = new List<MapGeoVertexPropertyName>();
            List<MapGeoVertexProperty[]> allVertexProperties = new List<MapGeoVertexProperty[]>();
            List<int> vertexByteSizes = new List<int>();

            for(int i = 0; i < objectBlock.floatDataBlockIndices.Length; i++) {
                int formatBlockIndex = objectBlock.vertexFormatBlockIndex + i;
                MapGeoVertexFormatBlock formatBlock = vertexFormatBlocks[formatBlockIndex];

                allVertexProperties.Add(formatBlock.properties);
                vertexByteSizes.Add(0);

                for(int j = 0; j < formatBlock.properties.Length; j++) {
                    MapGeoVertexProperty property = formatBlock.properties[j];

                    if(allPropertyNames.Contains(property.name) == true) {
                        Console.WriteLine("Warning:  multiple definition of vertex property " + property.name + " on object block \"" + objectBlock.objectName + "\"");
                        Program.Pause();
                    }

                    allPropertyNames.Add(property.name);

                    vertexByteSizes[i] += property.format.GetByteSize();
                }
            }


            int vertexCount = -1;

            for(int i = 0; i < objectBlock.floatDataBlockIndices.Length; i++) {
                int dataBlockIndex = objectBlock.floatDataBlockIndices[i];
                MapGeoFloatDataBlock dataBlock = floatDataBlocks[dataBlockIndex];

                int dataBlockByteSize = dataBlock.data.Length * 4;

                if((dataBlockByteSize % vertexByteSizes[i]) != 0) {
                    Console.WriteLine("Error:  float data block " + dataBlockIndex + " of length " + dataBlockByteSize + " did not match predicted vertex byte size of " + vertexByteSizes[i]);
                    Program.Pause();
                }

                int blockVertexCount = dataBlockByteSize / vertexByteSizes[i];
                if(i == 0) {
                    vertexCount = blockVertexCount;

                    vertexBlock.vertices = new MapGeoVertex[vertexCount];
                    for(int j = 0; j < vertexCount; j++) {
                        MapGeoVertex vertex = new MapGeoVertex();
                        vertexBlock.vertices[j] = vertex;
                    }
                } else {
                    if(blockVertexCount != vertexCount) {
                        Console.WriteLine("Error:  float data block for object \"" + objectBlock.objectName + "\" did not match the predicted vertex byte size of the previous blocks (local index " + i + ", global index " + dataBlockIndex + ")");
                        Program.Pause();
                    }
                }


                MapGeoVertexProperty[] blockVertexProperties = allVertexProperties[i];

                //int offset = 0;
                for(int j = 0; j < vertexCount; j++) {
                    MapGeoVertex vertex = vertexBlock.vertices[j];
                    int offset = vertexByteSizes[i] * j / 4;

                    for(int k = 0; k < blockVertexProperties.Length; k++) {
                        MapGeoVertexProperty property = blockVertexProperties[k];

                        int propertyByteSize = property.format.GetByteSize();
                        int floatCount = propertyByteSize / 4;

                        float[] floatValues = new float[floatCount];
                        for(int m = 0; m < floatCount; m++) {
                            floatValues[m] = dataBlock.data[offset + m];
                        }

                        offset += floatCount;


                        switch(property.name) {
                            default:
                                throw new System.Exception("unrecognized MapGeoVertexPropertyName " + property.name);
                                break;

                            case MapGeoVertexPropertyName.Position:
                                vertex.position = floatValues;
                                break;
                            case MapGeoVertexPropertyName.NormalDirection:
                                vertex.normalDirection = floatValues;
                                break;
                            case MapGeoVertexPropertyName.SecondaryColor:
                                // don't feel like handling this at the moment
                                break;
                            case MapGeoVertexPropertyName.ColorUV:
                                vertex.colorUV = floatValues;
                                break;
                            case MapGeoVertexPropertyName.LightmapUV:
                                vertex.lightmapUV = floatValues;
                                break;
                        }
                    }
                }
            }


            return vertexBlock;
        }

        #endregion


        #region ConvertFiles()

        public void ConvertFiles() {
            Console.WriteLine("\n\nwriting .obj file(s)");

            string baseFileName = this.mapgeoFile.GetName();

            if(createMergedLayerFile == true) {
                // create a single file of all layers combined, but also output a data file to allow for custom processing of layer data

                FileWrapper layerDataFileWrapper = new FileWrapper(this.mapgeoFile.GetFolderPath() + baseFileName + ".mapgeolayer");
                layerDataFileWrapper.Clear();

                string magic = "MGLAYERS";
                layerDataFileWrapper.WriteChars(magic.ToCharArray());

                int version = 1;
                layerDataFileWrapper.WriteInt(version);

                int objectCount = objectBlocks.Length;
                layerDataFileWrapper.WriteInt(objectCount);

                for(int i = 0; i < objectCount; i++) {
                    MapGeoObjectBlock objectBlock = objectBlocks[i];

                    int layerMask = objectBlock.layerBitmask;
                    layerDataFileWrapper.WriteByte(layerMask);
                }

                layerDataFileWrapper.Close();



                ConvertFilesForLayer(-1, baseFileName);

                Console.WriteLine("\n\nwrote merged layer .obj file along with layer data file");
            } else if(foundLayersBitmask == 0) {
                // a layer bitmask of 0xff means that object is present on every layer
                // 
                // we don't keep track of 0xff layer objects because of this fact, so
                // if we have a global bitmask of 0 then we have only found 0xff objects
                // 
                // this means that we don't really have to do anything with layers because
                // there really aren't any (technically a layer that always has everything is
                // still a layer, but there aren't multiple *distinct* layers, so we don't have to
                // make any distinguishments between the layers for our output files)

                ConvertFilesForLayer(-1, baseFileName);

                Console.WriteLine("\n\nmapgeo did not contain multiple layers");
            } else {


                for(int i = 0; i < maxLayerCount; i++) {
                //for(int i = 1; i >= 0; i--) {
                    
                    if(layerDuplicateReferences[i] != -1) {
                        // duplicate of another layer, so skip it
                        continue;
                    } else {
                        // unique layer, so allow us to write it
                    }


                    int layerBitmask = (1 << i);

                    //if((foundLayersBitmask | layerBitmask) == foundLayersBitmask) {
                        string layerFileName = baseFileName + ".Layer" + i;
                        ConvertFilesForLayer(i, layerFileName);
                    //} else {
                        // layer was not found in the file, so we don't need to write a file for it
                    //}

                    // going to just allow for empty files to be written for empty layers so that we can verify that we didn't lose anything somehow
                }


                // going to check and report for layers that *appear* to be unused (see ConvertFilesForLayer() on why we can't just write empty files for
                // seemingly-unused layers)
                Console.WriteLine("\n\nunused layer warnings (these layers contain no unique objects and may be unused):\n");
                for(int i = 0; i < maxLayerCount; i++) {
                    if(layerDuplicateReferences[i] != -1) {
                        // these layers will get reported as duplicates anyways, no need to report them twice since them being duplicates of something unused inherently implies that they are also unused
                        continue;
                    }


                    int layerBitmask = (1 << i);

                    if((foundLayersBitmask | layerBitmask) != foundLayersBitmask) {
                        Console.WriteLine("layer " + i + " might be unused");
                    }
                }


                Console.WriteLine("\n\nduplicate layer warnings (these layers contain the exact same objects as a previous layer with no changes):\n");
                
                for(int i = 0; i < maxLayerCount; i++) {
                    int duplicateLayerReferenceIndex = layerDuplicateReferences[i];

                    if(duplicateLayerReferenceIndex != -1) {
                        Console.WriteLine("layer " + i + " was a duplicate of layer " + duplicateLayerReferenceIndex + " and was not converted");
                    }
                }


                // also note that we did end up running into an issue with "unused" layers where the only difference between a used or unused layer was a single added object
                // 
                // may need to add extra checks to always consider Layer 0 as being used, that combined with the duplicate checks should be good enough to tell what is used or not
                // 
                // duplicate layers could even be skipped for writing entirely, which would be nice when dealing with some maps with only a couple layers and then
                // having to keep deleting the other unused layers each execution in order to save space (both harddrive space and clutter of visual space in the file explorer)
                //Console.WriteLine("\n\nreminder:  change this to check for *duplicate* layers rather than just trying to guess unused ones (e.g. \"Layer 6 is a duplicate of Layer 5, Layer 7 is a duplicate of Layer 5, Layer 5 appears to be unused\"");



                // note:  there *may* be a definitive layer listing inside of the map's data bin file (*not* materials.bin)
                // 
                // 738b7962c4e58140.bin / 3751997361 / 2201161822 / 2650904341 1484706743 / 1309176603
                // 
                // this seems like it may be listing different mapgeo layers, given how there's five of them and each has a "name" property
                // 
                // would need to see some other example of mapgeo layers in use in order to feel comfortable confirming this, however
            }
        }

        #endregion

        #region ConvertFilesForLayer()

        private void ConvertFilesForLayer(int layerIndex, string baseFileName) {
            Console.WriteLine("\n\nwriting .obj file");

            string folderPath = this.mapgeoFile.GetFolderPath();

            string objFileName = baseFileName + ".obj";
            string mtlFileName = baseFileName + ".mtl";  // storing this for later to use in the .obj file


            FileWrapper objFileWrapper = new FileWrapper(folderPath + objFileName);
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



            //int layerBitmaskFlag = (1 << maxLayerCount) - 1;
            int layerBitmaskFlag = 0;
            if(layerIndex != -1) {
                layerBitmaskFlag = (1 << layerIndex);

                if((foundLayersBitmask & layerBitmaskFlag) != layerBitmaskFlag) {
                    // this layer was never encountered outside of a 0xff value
                    // 
                    // original idea was to just write empty files for unused layers, however there was concern that
                    // there could be a single base layer that had zero delta objects of its own and would actually just
                    // represent the absence of all delta objects, which would mean that all of this layer's objects would
                    // show up flagged as 0xff, which would result in us assuming that this layer is actually unused since
                    // it never listed any objects of its own
                    // 
                    // we would end up having to depend on Riot never doing this and always having at least a single delta
                    // object for each layer, which simply doesn't seem like a very safe expectation at all, so we'll just
                    // deal with duplicate output files and try to write every single layer available until a more explicit
                    // solution can be found (materials.bin file does not appear to be very helpful, overall seems like Riot
                    // just allows all 8 layers to be fully defined at all times)

                    //layerBitmaskFlag = 0;  // would result in an empty file
                }
            }


            // .obj files are 1-indexed, but normal people use 0-indexed arrays, so this value is
            // initialized to `1` to counter this offset
            int currentVertexTotal = 1;

            for(int i = 0; i < objectBlocks.Length; i++) {
                string progressString = "";
                if(layerIndex != -1) {
                    progressString += "layer " + (layerIndex + 1) + "/" + maxLayerCount + ":  ";
                }
                progressString += "writing .obj file entry " + (i + 1) + "/" + objectBlocks.Length + ", " + ((i + 1f) / objectBlocks.Length * 100) + "% complete";
                Console.WriteLine(progressString);

                MapGeoObjectBlock objectBlock = objectBlocks[i];
                //bool hasLightmap = (objectBlock.lightmapTextureName.Length > 0);

                /*if(objectBlock.unknownByte1 != 0x1e) {
                    continue;
                }*/

                if((objectBlock.layerBitmask & layerBitmaskFlag) != layerBitmaskFlag) {
                    continue;
                }

                /*if(i != 37) {
                    continue;
                }*/


                MapGeoVertexBlock vertexBlock = BuildVertexBlock(objectBlock);
                MapGeoTriBlock triBlock = triBlocks[objectBlock.triBlockIndex];

                int totalVertexCount = vertexBlock.vertices.Length;
                int totalTriCount = triBlock.tris.Length;

                try {
                    objFile.WriteBlankLines(4);

                    objFile.WriteLine("g default");
                    objFile.WriteBlankLine();

                    for(int j = 0; j < totalVertexCount; j++) {
                        // going to bake the transformation matrix into the .obj vertex data since
                        // we don't really have any other option in the .obj format

                        MapGeoVertex vertex = vertexBlock.vertices[j];
                        float[] transformedVertex = objectBlock.ApplyTransformationMatrix(vertex.position, false);

                        // have to negate X-coordinates due to how Maya's coordinate axes work
                        objFile.WriteLine("v " + (-1 * transformedVertex[0]) + " " + transformedVertex[1] + " " + transformedVertex[2]);
                    }

                    objFile.WriteBlankLine();


                    for(int j = 0; j < totalVertexCount; j++) {
                        MapGeoVertex vertex = vertexBlock.vertices[j];

                        // have to invert the V-coordinate due to how Riot handles their UV coordinates (flipped upside-down about the Y=0.5 axis)
                        objFile.WriteLine("vt " + vertex.colorUV[0] + " " + (1f - vertex.colorUV[1]));

                        // these are not interchangeable, you'll end up with bad textures if you swap the UV sets
                        bool hasLightmap = (vertex.lightmapUV != null);
                        /*if(hasLightmap == false) {
                            objFile.WriteLine("vt " + uv.colorUV[0] + " " + (1f - uv.colorUV[1]));
                        } else {
                            objFile.WriteLine("vt " + uv.lightmapUV[0] + " " + (1f - uv.lightmapUV[1]));
                        }*/
                    }

                    objFile.WriteBlankLine();


                    for(int j = 0; j < totalVertexCount; j++) {
                        // also going to apply the transformation matrix to the vertex normal directions

                        MapGeoVertex vertex = vertexBlock.vertices[j];
                        float[] transformedNormal = objectBlock.ApplyTransformationMatrix(vertex.normalDirection, true);


                        // have to negate X-coordinates due to how Maya's coordinate axes work
                        objFile.WriteLine("vn " + (-1 * transformedNormal[0]) + " " + transformedNormal[1] + " " + transformedNormal[2]);
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
                            MapGeoMaterial material = materialTextureMap[submesh.materialName];


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


                            // missing:  some materials want what *should* be a map_Ka texture (defined under a "mask" material param)
                            // 
                            // problem with this however is that Maya does not seem to allow importing such a material, and will create something untextured instead
                            // 
                            // keeping the color in without a mask texture however is completely fine
                            // 
                            // we will just output the material without the mask texture but with the Ka value, so that there is at least data present for
                            // this color, and if someone needs to apply a mask to it then they can do that on their own
                            // 
                            // 
                            // what seems to work:
                            //  - Maya imports the Ka value onto the "Ambient Color" shader property
                            //  - remember this ambient color value
                            //  - override the shader property to take a file, and give it the mask texture listed in the materials.bin (note that some ambient color materials do not have mask texture, so Maya's default import is actually correct)
                            //  - under the "Color Balance" section, set "Color Gain" to the original ambient color value,
                            //  - set "Color Offset" to 128 gray
                            //  - leave all other settings as they are
                            //  - the result should be a decent enough approximation for what the live game intends

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
