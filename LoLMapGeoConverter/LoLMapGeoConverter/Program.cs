using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace LoLMapGeoConverter {
    public class Program {

        public static void Main(string[] args) {
            Console.WriteLine("LoL MapGeo Converter by FrankTheBoxMonster");

            if(args.Length < 1) {
                Console.WriteLine("Error:  must provide a file (you can drag-and-drop a file onto the .exe)");
                Pause();
                System.Environment.Exit(1);
            }



            string mapgeoFilePath = "";
            string binFilePath = "";

            for(int i = 0; i < args.Length; i++) {
                if(args[i].EndsWith(".mapgeo") == true) {
                    if(mapgeoFilePath == "") {
                        mapgeoFilePath = args[i];
                    } else {
                        Console.WriteLine("Found multiple .mapgeo files, only the first will be converted");
                    }
                }

                if(args[i].EndsWith(".bin") == true) {
                    if(binFilePath == "") {
                        binFilePath = args[i];
                    } else {
                        Console.WriteLine("Found multiple .bin files, only the first will be converted");
                    }
                }
            }


            if(mapgeoFilePath == "") {
                Console.WriteLine("Error:  must provide a .mapgeo file (you can drag-and-drop a file onto the .exe)");
                Pause();
                System.Environment.Exit(1);
            }

            if(binFilePath == "") {
                Console.WriteLine("No .bin file was provided (no textures will be read)");
                Pause();
            }


            try {
                string mapGeoShortPath = mapgeoFilePath.Substring(mapgeoFilePath.LastIndexOf('\\') + 1);
                string binShortPath = binFilePath.Substring(binFilePath.LastIndexOf('\\') + 1);
                Console.WriteLine("\nConverting files:  \n  " + mapgeoFilePath + "\n  " + binFilePath);

                TryReadFile(mapgeoFilePath, binFilePath);
            } catch(System.Exception e) {
                Console.WriteLine("\n\nError:  " + e.ToString());
            }


            Console.WriteLine("\n\nDone");
            Pause();
        }


        public static void Pause() {
            Console.WriteLine("Press any key to continue . . .");
            Console.ReadKey(true);
        }


        private static void TryReadFile(string mapGeoFilePath, string binFilePath) {
            FileWrapper mapgeoFile = new FileWrapper(mapGeoFilePath);


            string magic = mapgeoFile.ReadString(4);
            if(magic != "OEGM") {  // 'MGEO' backwards
                Console.WriteLine("Error:  unrecognized magic value:  " + magic);
                return;
            }


            int version = mapgeoFile.ReadInt();  // moon said this was an int
            Console.WriteLine("\nversion = " + version);


            if(version != 6 && version != 5) {
                Console.WriteLine("Error:  unrecognized version number:  " + version);

                return;
            }


            FileWrapper binFile = null;
            if(binFilePath != "") {
                binFile = new FileWrapper(binFilePath);
            }

            MapGeoFileReader mapgeo = new MapGeoFileReader(mapgeoFile, version, binFile);


            mapgeo.ConvertFiles();
        }
    }
}
