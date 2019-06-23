using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoLMapGeoConverter {
    public class TextFileWriter {

        private FileWrapper file;


        public TextFileWriter(FileWrapper file) {
            this.file = file;

            file.Clear();  // need to get rid of anything already there from a previous execution
        }

        public void Close() {
            file.Close();
        }


        public void WriteLine(string line) {
            line += "\r\n";
            file.WriteChars(line.ToCharArray());
        }

        public void WriteBlankLine() {
            WriteBlankLines(1);
        }

        public void WriteBlankLines(int lineCount) {
            for(int i = 0; i < lineCount; i++) {
                WriteLine("");
            }
        }
    }
}
