using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class MapGeoUV {

        public float[] colorUV;

        // we never do things with lighting so not sure how we found this name, probably just guessed based on
        // the range of the values, proximity with the color UV, and the existance of lightmap texture names in the file
        public float[] lightmapUV;


        public MapGeoUV() {

        }
    }
}
