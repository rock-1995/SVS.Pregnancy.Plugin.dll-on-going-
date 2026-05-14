using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using System.Threading.Tasks;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime;
using ADV.Commands.Base;

//using Il2CppSystem.Collections.Generic;

namespace SVSPregnancy
{

    public static class Util
    {
        public static Texture2D LoadTexture(this byte[] texBytes, TextureFormat format = TextureFormat.ARGB32, bool mipMaps = false)//Texture2D*
        {
            if (texBytes == null) throw new ArgumentNullException(nameof(texBytes));

            var tex = new Texture2D(4, 4, format, mipMaps);
            ImageConversion.LoadImage(tex, texBytes);
            return tex;
        }

        public static List<byte> ReadAllBytes(this Stream input)
        {
            var buffer = new byte[16 * 1024];
            using (var ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);
                return ms.ToArray().ToList();
            }
        }
        public interface IVector<T>
        {
            T Add(T other);


        }

        public static BoneWeight Normallize(this BoneWeight bw)
        {
            Vector4 vector4 = new Vector4(bw.weight0, bw.weight1, bw.weight2, bw.weight3);
            float totalweight = vector4.x + vector4.y + vector4.z + vector4.w;
            if (totalweight > 0)
            {
                vector4 = vector4 / totalweight;
                bw.weight0 = vector4.x;
                bw.weight1 = vector4.y;
                bw.weight2 = vector4.z;
                bw.weight3 = vector4.w;
            }
            return bw;
        }
        public static BoneWeight SimpleMul(this BoneWeight bw, float m)
        {

            bw.weight0 *= m;
            bw.weight1 *= m;
            bw.weight2 *= m;
            bw.weight3 *= m;
           
            return bw;
        }

        public static BoneWeight SimplePlus(this BoneWeight bw, BoneWeight bw1)
        {
            List<BoneWeight> parameters = new List<BoneWeight>() { bw, bw1 };
            BoneWeight sum = parameters.Sum();

            return sum;
        }
        public static BoneWeight Sum(this List<BoneWeight> container)
        {
            BoneWeight sum=new BoneWeight();
            List<(int, float)> bws=new List<(int, float)> ();
            foreach (BoneWeight bw in container)
            {
                List<int> boneIndicies = new List<int> { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
                List<float> boneWeights = new List<float> { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
                for (int i = 0; i < 4; i++)
                {
                    if (boneWeights[i] > 0)
                    {
                        if (bws.Exists(bwx => bwx.Item1 == boneIndicies[i]))
                        {
                            int indexx = bws.IndexOf(bws.Where(bwx => bwx.Item1 == boneIndicies[i]).First());
                            var bwx = bws[indexx];
                            bwx = (bwx.Item1, bwx.Item2 + boneWeights[i]);
                            bws[indexx] = bwx;

                        }
                        else
                        {
                            bws.Add((boneIndicies[i], boneWeights[i]));
                        }
                    }
                    
                }

            }

            bws.Sort((bwx,bwx1)=>bwx1.Item2.CompareTo(bwx.Item2));//desc
            while (bws.Count < 4)
            {
                bws.Add((0, 0));
            }
            
           // Vector4 vector4 = new Vector4(bws[0].Item2, bws[1].Item2, bws[2].Item2, bws[3].Item2);
           // vector4.Normalize();
            sum.boneIndex0 = bws[0].Item1;
            sum.boneIndex1 = bws[1].Item1;
            sum.boneIndex2 = bws[2].Item1;
            sum.boneIndex3 = bws[3].Item1;
            sum.weight0 = bws[0].Item2;
            sum.weight1 = bws[1].Item2;
            sum.weight2 = bws[2].Item2;
            sum.weight3 = bws[3].Item2;
            sum=sum.Normallize();
            return sum;


        }

        public static Vector2 Sum(this List<Vector2> container) 
        {
            

            Vector2 sum = Vector2.zero;
            foreach (Vector2 t in container)
            {
                sum += t; 
            }
            return sum;
        }


        public static Vector3 Sum(this List<Vector3> container)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 t in container)
            {
                sum += t;
            }
            return sum;
        }

        public static Vector4 Sum(this List<Vector4> container)
        {
            Vector4 sum = Vector4.zero;
            foreach (Vector4 t in container)
            {
                sum += t;
            }
            return sum;
        }

        public static Color Sum(this List<Color> container)
        {
            Color sum = new Color(0,0,0,0);
            foreach (Color t in container)
            {
                sum += t;
            }
            return sum;
        }
        public static T GetOrAddComponent<T>(this GameObject __instance) where T : Component
        {
            T ctrlx;
            UnityEngine.Component? tempctrlx;
            __instance.TryGetComponent(Il2CppType.Of<T>(), out tempctrlx);
            if (tempctrlx == null)
            {
                ctrlx = __instance.AddComponent(Il2CppType.Of<T>()).Cast<T>();

            }
            else
            {
                ctrlx = tempctrlx.Cast<T>();
            }
            return ctrlx;
        }

        public static System.Collections.Generic.List<T> ToList<T>(this Il2CppSystem.Collections.Generic.List<T> cpplist)
        {
            System.Collections.Generic.List<T> list = new System.Collections.Generic.List<T>();
            for (int i = 0; i < cpplist.Count; i++)
            {
                list.Add(cpplist[i]);
            }
            return list;
        }

        public static Il2CppSystem.Collections.Generic.List<T> ToCppList<T>(this System.Collections.Generic.List<T> list)
        {
            Il2CppSystem.Collections.Generic.List<T> cpplist = new Il2CppSystem.Collections.Generic.List<T>();
            for (int i = 0; i < list.Count; i++)
            {
                cpplist.Add(list[i]);
            }
            return cpplist;
        }

        public static System.Collections.Generic.Dictionary<T0, T1> ToDict<T0, T1>(this Il2CppSystem.Collections.Generic.Dictionary<T0, T1> cppdict)
        {
            System.Collections.Generic.Dictionary < T0, T1 > dict= new System.Collections.Generic.Dictionary < T0, T1 >();
            foreach(var p in cppdict)
            {
                dict[p.Key] =p.Value;
            }
            return dict; 
        }

        public static Il2CppSystem.Collections.Generic.Dictionary<T0, T1> ToCppDict<T0, T1>(this System.Collections.Generic.Dictionary<T0, T1> dict)
        {
            Il2CppSystem.Collections.Generic.Dictionary<T0, T1> cppdict = new Il2CppSystem.Collections.Generic.Dictionary<T0, T1>();
            foreach (var p in dict)
            {
                cppdict[p.Key] = p.Value;
            }
            return cppdict;
        }

        /*
        public static Il2CppSystem.Type ToIl2CppType(this System.Type type)
        {
            return Il2CppSystem.Type.GetType(type.FullName);
        }
        public static System.Type ToRegularType(this Il2CppSystem.Type type)
        {
            return System.Type.GetType(type.FullName);
        }*/
        public static T ToObject<T>(this IntPtr ptr)
        {
            return ptr != IntPtr.Zero ? Il2CppObjectPool.Get<T>(ptr) : default(T);
        }
        public static IntPtr ToIl2CppTypePtr(this System.Type type)
        {
            return Il2CppType.Of<System.Type>().Pointer;
        }
        public static IntPtr ToPtr(this object obj)
        {
            return obj!=null?IL2CPP.Il2CppObjectBaseToPtr((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)obj):IntPtr.Zero;
        }
        public static object GetFieldRefl(this object obj, string name)
        {
            return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj);
        }

        public static T GetFieldRefl<T>(this object obj, string name)
        {
            return (T)((object)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj));
        }

        public static void SetFieldRefl<T>(this object obj, string name, T value)
        {
            obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(obj, value);
        }

        public static T GetPropertyRefl<T>(this object obj, string name)
        {
            PropertyInfo pi = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.CanRead)
            {
                return (T)pi.GetValue(obj, null);
            }
            else
            {
                throw new Exception($"{pi.Name} does not exist or does not have get method");
            }
        }

        public static void SetPropertyRefl<T>(this object obj, string name, T value)
        {
            PropertyInfo pi = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.CanWrite)
            {
                pi.SetValue(obj, value, null);
            }
            else
            {
                throw new Exception($"{pi.Name} does not exist or does not have set method");
            }
        }

        public static T GetFieldDirect<T>(this object obj, string name)
        {
            Type objtype = obj.GetType();
            FieldInfo fi = objtype.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            TypedReference reference = __makeref(obj);
            T result = (T)fi.GetValueDirect(reference);
            return result;
        }

        public static void SetFieldDirect<T>(this object obj, string name, T x)
        {

            Type objtype = obj.GetType();
            FieldInfo fi = objtype.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            TypedReference reference = __makeref(obj);
            fi.SetValueDirect(reference, x);

        }

        public static T GetFieldDirectly<T>(object __instance, string name)
        {
            Type objtype = __instance.GetType();
            FieldInfo fi = objtype.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            TypedReference reference = __makeref(__instance);
            T result = (T)fi.GetValueDirect(reference);
            return result;
        }
        public static void SetFieldDirectly<T>(object __instance, string name, T x)
        {
            Type objtype = __instance.GetType();
            FieldInfo fi = objtype.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            TypedReference reference = __makeref(__instance);
            fi.SetValueDirect(reference, x);
        }


        public static MethodInfo GetMethodInfo(Type classtype, string methodname, BindingFlags attrs = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        {
            var tempmethod = classtype.GetMethod(methodname, attrs);
            VerifyNullity<MethodInfo>(tempmethod);
            return tempmethod;
        }

        public static FieldInfo GetFieldInfo(Type classtype, string fieldname, BindingFlags attrs = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        {
            var tempfield = classtype.GetField(fieldname, attrs);
            VerifyNullity<FieldInfo>(tempfield);
            return tempfield;
        }


        public static PropertyInfo GetPropertyInfo(Type classtype, string fieldname, BindingFlags attrs = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        {
            var tempfield = classtype.GetProperty(fieldname, attrs);
            VerifyNullity<PropertyInfo>(tempfield);
            return tempfield;
        }
        public static void VerifyNullity<T>(T s)
        {
            if (s == null)
            {
                throw new ArgumentNullException(paramName: nameof(s), message: "'s info has not been found.");
            }
        }
        public static void PrintMethod(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions.ToList())
            {
                SaveLog(instruction.ToString());
            }
        }
        public static void PrintComponents<T>(GameObject obj)
        {
            List<T> components = new List<T>(obj.GetComponents<T>().ToList());
            foreach (var component in components)
            {
                Util.SaveLog(component.ToString());
            }
        }

        public static void PrintComponentsInChildren<T>(GameObject obj)
        {
            List<T> components = new List<T>(obj.GetComponentsInChildren<T>().ToList());
            foreach (var component in components)
            {
                Util.SaveLog(component.ToString());
            }
        }

        public static bool ToLog(this object obj)
        {
            return obj != null ? SaveLog(obj.ToString()) : false;
        }
        public static bool SaveLog(String s)    
        {
            String logpath = $"{Path.GetTempPath()}{"SV_Preg\\"}" + "debuglog.txt";
            var tmp = s;
            StreamWriter logfile;
            String path = Path.GetDirectoryName(logpath);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }



            logfile = new(logpath, append: true);



            for (int i = 0; i < 3; ++i)
                try
                {

                    logfile.WriteLineAsync(tmp);

                    logfile.Close();
                    logfile.Dispose();
                    break;
                }
                catch (IOException)
                {

                    System.Threading.Tasks.Task.Delay(500);


                }
            return true;

        }

    }
}
