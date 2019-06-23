using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace LoLMapGeoConverter {
    public class FileWrapper {

        private const int bufferSize = 8;

        public string folderPath { get; private set; }
        public string name { get; private set; }
        public string fileExtension { get; private set; }

        private FileStream fileStream;
        private byte[] readBuffer;


        public FileWrapper(string filePath) {
            filePath = filePath.Replace('\\', '/');

            folderPath = filePath.Substring(0, filePath.LastIndexOf('/') + 1);
            name = filePath.Substring(filePath.LastIndexOf('/') + 1);
            name = name.Substring(0, name.ToLower().LastIndexOf('.'));
            fileExtension = filePath.Substring(filePath.LastIndexOf('.'));

            FileMode fileMode;
            if(File.Exists(filePath) == true) {
                fileMode = FileMode.Open;
            } else {
                fileMode = FileMode.Create;

                Directory.CreateDirectory(folderPath);
            }
            fileStream = File.Open(filePath, fileMode, FileAccess.ReadWrite, FileShare.ReadWrite);

            readBuffer = new byte[bufferSize];
        }

        ~FileWrapper() {
            this.Close();
        }

        public override string ToString() {
            string result = name + fileExtension;

            return result;
        }

        #region misc methods

        /// <summary>
        /// Seeks to the specified offset.
        /// If this method is passed using only default parameters (attempting to seek to offset -1), then it is assumed that the seek was meant to be skipped.
        /// This is because all read/write methods support seeking prior to reading or writing, however the default parameters are used to show that this seek was not requested.
        /// </summary>
        /// <param name="offset">The offset to seek to, relative to seekOrigin.</param>
        /// <param name="seekOrigin">The SeekOrigin setting to use.</param>
        public void Seek(int offset = -1, SeekOrigin seekOrigin = SeekOrigin.Begin) {
            if(offset != -1 || seekOrigin != SeekOrigin.Begin) {
                fileStream.Seek(offset, seekOrigin);
            } else {
                // we are only using default parameters, both of which combined are invalid, signalling that the seek was unintended
            }
        }

        public int GetFilePosition() {
            return (int) fileStream.Position;
        }

        public void Open() {
            fileStream = File.Open(this.GetFullFilePath(), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        }

        public void Close() {
            fileStream.Close();
        }

        public string GetFolderPath() {
            return folderPath;
        }

        public string GetFileExtension() {
            return fileExtension;
        }

        public string GetName() {
            return name;
        }

        public string GetFullFilePath() {
            return (folderPath + name + fileExtension);  // 'folderPath' already includes a trailing '/', and 'fileExtension' already includes a leading '.'
        }

        public string GetShortFilePath() {
            return name + fileExtension;
        }

        public virtual void SetName(string newName) {
            // removes all characters banned by windows for use in file names (except \", see below)
            newName = System.Text.RegularExpressions.Regex.Replace(newName, @"[\\/:*?<>|]", " ");

            newName = newName.Replace("\"", " ");  // couldn't get the \" character to work in the regex pattern


            if(fileStream != null) {
                fileStream.Close();

                string oldFilePath = folderPath + name + fileExtension;
                string newFilePath = folderPath + newName + fileExtension;

                File.Move(oldFilePath, newFilePath);

                fileStream = File.Open(newFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }

            name = newName;
        }

        public int GetLength() {
            return (int) fileStream.Length;
        }

        public bool EndOfFile() {
            return fileStream.Position >= fileStream.Length;
        }

        public void Clear() {
            fileStream.SetLength(0);
        }

        public void Rewind() {
            this.Seek(0);
        }

        #endregion

        #region write methods

        public void WriteString(string s, int offset = -1) {
            s += "\x00";
            WriteChars(s.ToCharArray(), offset);
        }

        public void WriteChars(char[] chars, int offset = -1) {
            byte[] bytes = new byte[chars.Length];
            for(int i = 0; i < bytes.Length; i++) {
                bytes[i] = (byte) chars[i];
            }

            WriteBytes(bytes, offset);
        }

        public void WriteInt(int n, int offset = -1) {
            byte[] bytes = new byte[4];

            // as per Riot standards, use little-endian format
            for(int i = 0; i < 4; i++) {
                bytes[i] = (byte) (n & 255);
                n = n >> 8;
            }

            WriteBytes(bytes, offset);
        }

        public void WriteFloat(float f, int offset = -1) {
            byte[] bytes = System.BitConverter.GetBytes(f);

            // endianness cannot be set directly and is dependent on the computer's system architecture
            if(System.BitConverter.IsLittleEndian == false) {
                // bytes were returned in big-endian format, so reverse the array

                byte temp = bytes[3];
                bytes[3] = bytes[0];
                bytes[0] = temp;

                temp = bytes[2];
                bytes[2] = bytes[1];
                bytes[1] = temp;
            }

            WriteBytes(bytes, offset);
        }

        public void WriteShort(int n, int offset = -1) {
            byte[] bytes = new byte[2];

            for(int i = 0; i < 2; i++) {
                bytes[i] = (byte) (n & 255);
                n = n >> 8;
            }

            WriteBytes(bytes, offset);
        }

        public void WriteByte(int n, int offset = -1) {
            byte[] bytes = new byte[] { (byte) n };
            WriteBytes(bytes, offset);
        }

        public void WriteBytes(byte[] bytes, int offset = -1) {
            try {
                int oldPosition = GetFilePosition();
                Seek(offset);
                fileStream.Write(bytes, 0, bytes.Length);

                if(offset != -1) {
                    Seek(oldPosition);
                }
            } catch(System.Exception e) {
                string errorString = "Error writing to RunePageFile:  ";

                for(int i = 0; i < bytes.Length; i++) {
                    errorString += bytes[i] + " ";
                }

                errorString += "\n" + e.GetType() + "\n" + e.StackTrace;

                Console.WriteLine(errorString);
            }
        }

        public void WriteBool(bool b, int offset = -1) {
            if(b == true) {
                WriteByte(1, offset);
            } else {
                WriteByte(0, offset);
            }
        }

        public void WriteLine(string s = "", int offset = -1) {
            s += "\r\n";  // end-line characters formatted so that they look correct in notepad
            WriteChars(s.ToCharArray(), offset);
        }

        #endregion

        #region read methods

        public string ReadString(int length = -1, int offset = -1) {
            try {
                Seek(offset);

                System.Text.StringBuilder stringBuilder = new StringBuilder();
                if(length < 0) {
                    char c = ReadChar();

                    while(c != '\x00') {
                        stringBuilder.Append(c);
                        c = ReadChar();
                    }
                } else {
                    for(int i = 0; i < length; i++) {
                        char c = ReadChar();
                        stringBuilder.Append(c);
                    }
                }

                return stringBuilder.ToString();
            } catch(System.Exception e) {
                Console.WriteLine("Error reading string:  " + e.GetType() + "\n" + e.StackTrace);
                return "";
            }
        }

        public string ReadAllText() {
            Seek(0);
            return ReadString(this.GetLength());
        }

        public char ReadChar(int offset = -1) {
            try {
                return (char) ReadByte(offset);
            } catch(System.Exception e) {
                Console.WriteLine("Error reading char:  " + e.GetType() + "\n" + e.StackTrace);
                return '\x00';
            }
        }

        public int ReadInt(int offset = -1) {
            try {
                Seek(offset);

                fileStream.Read(readBuffer, 0, 4);

                int n = 0;
                for(int i = 0; i < 4; i++) {
                    n = n << 8;
                    n += readBuffer[3 - i];
                }

                return n;
            } catch(System.Exception e) {
                Console.WriteLine("Error reading int:  " + e.GetType() + "\n" + e.StackTrace);
                return 0;
            }
        }

        public float ReadFloat(int offset = -1) {
            try {
                Seek(offset);

                fileStream.Read(readBuffer, 0, 4);

                // endianness cannot be set directly and is dependent on the computer's system architecture
                if(System.BitConverter.IsLittleEndian == false) {
                    // bytes are to be read in big-endian format, so reverse the array

                    byte temp = readBuffer[3];
                    readBuffer[3] = readBuffer[0];
                    readBuffer[0] = temp;

                    temp = readBuffer[2];
                    readBuffer[2] = readBuffer[1];
                    readBuffer[1] = temp;
                }

                float f = System.BitConverter.ToSingle(readBuffer, 0);

                return f;
            } catch(System.Exception e) {
                Console.WriteLine("Error reading float:  " + e.GetType() + "\n" + e.StackTrace);
                return 0f;
            }
        }

        public ushort ReadShort(int offset = -1) {
            try {
                Seek(offset);

                fileStream.Read(readBuffer, 0, 2);

                int n = 0;
                for(int i = 0; i < 2; i++) {
                    n = n << 8;
                    n += readBuffer[1 - i];
                }

                return (ushort) n;
            } catch(System.Exception e) {
                Console.WriteLine("Error reading short:  " + e.GetType() + "\n" + e.StackTrace);
                return 0;
            }
        }

        public byte ReadByte(int offset = -1) {
            try {
                Seek(offset);
                return (byte) fileStream.ReadByte();
            } catch(System.Exception e) {
                Console.WriteLine("Error reading byte:  " + e.GetType() + "\n" + e.StackTrace);
                return 0;
            }
        }

        public bool ReadBool(int offset = -1) {
            int n = ReadInt(offset);
            if(n != 0) {
                return true;
            } else {
                return false;
            }
        }

        public string ReadLine(int offset = -1) {
            try {
                Seek(offset);

                string s = "";

                char c = ReadChar();

                while(c != '\r') {
                    s += c;
                    c = ReadChar();
                }

                // end-lines are formatted as "\r\n" so that they appear correct in notepad,
                // and this line is needed to read the additional '\n' character that makes up an end-line
                ReadChar();

                return s;
            } catch(System.Exception e) {
                Console.WriteLine("Error reading line:  " + e.GetType() + "\n" + e.StackTrace);
                return "";
            }
        }

        public string ReadLineAssignment(int offset = -1) {
            string s = ReadLine(offset);
            s = s.Substring(s.IndexOf('=') + 1);
            return s;
        }

        #endregion

    }
}
