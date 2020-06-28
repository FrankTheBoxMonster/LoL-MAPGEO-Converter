using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LoLMapGeoConverter {
    public struct Vector3 {
        public static readonly Vector3 zero = new Vector3(0, 0, 0);


        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float[] components) : this(0, 0, 0) {  // need to be able to guarantee that all components are initialized to prevent a compiler error
            for(int i = 0; i < 3 && i < components.Length; i++) {
                this[i] = components[i];
            }
        }


        public float this[int i] { get { return this.GetComponent(i); } set { this.SetComponent(i, value); } }

        private float GetComponent(int i) {
            switch(i) {
                default:
                    throw new System.Exception("invalid Vector3 component index " + i);

                case 0:
                    return this.x;
                case 1:
                    return this.y;
                case 2:
                    return this.z;
            }
        }

        private void SetComponent(int i, float value) {
            switch(i) {
                default:
                    throw new System.Exception("invalid Vector3 component index " + i);

                case 0:
                    this.x = value;
                    break;
                case 1:
                    this.y = value;
                    break;
                case 2:
                    this.z = value;
                    break;
            }
        }



        public float Magnitude { get { return this.GetMagnitude(); } }

        private float GetMagnitude() {
            return (float) System.Math.Sqrt(this.GetSquareMagnitude());
        }


        public float SquareMagnitude { get { return this.GetSquareMagnitude(); } }

        private float GetSquareMagnitude() {
            return ((this.x * this.x) + (this.y * this.y) + (this.z * this.z));
        }


        public Vector3 Normalized { get { return this.GetNormalized(); } }

        private Vector3 GetNormalized() {
            return this / this.Magnitude;
        }



        public static Vector3 operator+(Vector3 lhs, Vector3 rhs) {
            Vector3 result = new Vector3();

            for(int i = 0; i < 3; i++) {
                result[i] = lhs[i] + rhs[i];
            }

            return result;
        }

        public static Vector3 operator-(Vector3 lhs, Vector3 rhs) {
            Vector3 result = new Vector3();

            for(int i = 0; i < 3; i++) {
                result[i] = lhs[i] - rhs[i];
            }

            return result;
        }

        public static Vector3 operator*(Vector3 lhs, Vector3 rhs) {
            Vector3 result = new Vector3();

            for(int i = 0; i < 3; i++) {
                result[i] = lhs[i] * rhs[i];
            }

            return result;
        }

        public static Vector3 operator/(Vector3 lhs, Vector3 rhs) {
            Vector3 result = new Vector3();

            for(int i = 0; i < 3; i++) {
                result[i] = lhs[i] / rhs[i];
            }

            return result;
        }

        public static Vector3 operator *(Vector3 lhs, float rhs) {
            Vector3 result = new Vector3();

            for(int i = 0; i < 3; i++) {
                result[i] = lhs[i] * rhs;
            }

            return result;
        }

        public static Vector3 operator/(Vector3 lhs, float rhs) {
            Vector3 result = new Vector3();

            for(int i = 0; i < 3; i++) {
                result[i] = lhs[i] / rhs;
            }

            return result;
        }

        public static bool operator==(Vector3 lhs, Vector3 rhs) {
            return (lhs.x == rhs.x && lhs.y == rhs.y && lhs.z == rhs.z);
        }

        public static bool operator!=(Vector3 lhs, Vector3 rhs) {
            return ((lhs == rhs) == false);
        }



        public static float DotProduct(Vector3 lhs, Vector3 rhs) {
            Vector3 componentProduct = lhs * rhs;

            float result = 0;

            for(int i = 0; i < 3; i++) {
                result += componentProduct[i];
            }

            return result;
        }

        public static Vector3 CrossProduct(Vector3 lhs, Vector3 rhs) {
            Vector3 result = new Vector3();

            result.x = (lhs.y * rhs.z) - (lhs.z * rhs.y);
            result.y = (lhs.z * rhs.x) - (lhs.x * rhs.z);
            result.z = (lhs.x * rhs.y) - (lhs.y * rhs.x);

            return result;
        }



        
        // in order of bytes read, the matrix looks like this:
        // 
        // [  0  4  8 12 
        //    1  5  9 13
        //    2  6 10 14 
        //    3  7 11 15 ]
        // 
        // X-coords are also negated like everything else, but this should still only be handled at the very end
        public static Vector3 ApplyTransformationMatrix(Vector3 originalVector, float[] transformationMatrix, bool isDirection) {
            // this is the only vector math we really do, so we can get away with not needing a proper Vector3 implementation


            Vector3 result = new Vector3();

            result[0] = (transformationMatrix[0] * originalVector[0]) + (transformationMatrix[4] * originalVector[1]) + (transformationMatrix[8] * originalVector[2]);
            result[1] = (transformationMatrix[1] * originalVector[0]) + (transformationMatrix[5] * originalVector[1]) + (transformationMatrix[9] * originalVector[2]);
            result[2] = (transformationMatrix[2] * originalVector[0]) + (transformationMatrix[6] * originalVector[1]) + (transformationMatrix[10] * originalVector[2]);

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

        public static float[] ApplyTransformationMatrix(float[] originalVector, float[] transformationMatrix, bool isDirection) {
            float[] result = new float[3];
            Vector3 newVector = Vector3.ApplyTransformationMatrix(new Vector3(originalVector), transformationMatrix, isDirection);

            for(int i = 0; i < 3; i++) {
                result[i] = newVector[i];
            }

            return result;
        }



        public static float[] InvertMatrix(float[] originalMatrix) {
            float[] result = new float[16];

            Vector3 u = new Vector3(originalMatrix[0], originalMatrix[1], originalMatrix[2]);
            Vector3 v = new Vector3(originalMatrix[4], originalMatrix[5], originalMatrix[6]);
            Vector3 w = new Vector3(originalMatrix[8], originalMatrix[9], originalMatrix[10]);
            Vector3 t = new Vector3(originalMatrix[12], originalMatrix[13], originalMatrix[14]);

            result[0] = u.x;
            result[4] = u.y;
            result[8] = u.z;

            result[1] = v.x;
            result[5] = v.y;
            result[9] = v.z;

            result[2] = w.x;
            result[6] = w.y;
            result[10] = w.z;

            result[12] = Vector3.DotProduct(u, t) * -1;
            result[13] = Vector3.DotProduct(v, t) * -1;
            result[14] = Vector3.DotProduct(w, t) * -1;

            result[3] = 0;
            result[7] = 0;
            result[11] = 0;
            result[15] = 1;

            return result;
        }
    }
}
