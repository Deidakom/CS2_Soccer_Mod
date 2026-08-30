param(
    [Parameter(Mandatory = $true)]
    [string] $CssBinDirectory,

    [Parameter(Mandatory = $true)]
    [string] $CollisionData,

    [Parameter(Mandatory = $true)]
    [string] $OutputJson
)

$ErrorActionPreference = 'Stop'

if ([IntPtr]::Size -ne 4) {
    throw 'Run this script with 32-bit Windows PowerShell (SysWOW64); the installed CSS vphysics.dll is 32-bit.'
}

$source = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class Source1VPhysicsDump
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vec3
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VCollide
    {
        public ushort SolidCount;
        public ushort IsPacked;
        public IntPtr Solids;
        public IntPtr KeyValues;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate IntPtr CreateInterfaceDelegate(string name, IntPtr returnCode);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void VCollideLoadDelegate(
        IntPtr self,
        out VCollide output,
        int solidCount,
        IntPtr buffer,
        int size,
        [MarshalAs(UnmanagedType.I1)] bool swap);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void VCollideUnloadDelegate(IntPtr self, ref VCollide collide);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate float CollideScalarDelegate(IntPtr self, IntPtr collide);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void CollideGetAabbDelegate(
        IntPtr self,
        out Vec3 mins,
        out Vec3 maxs,
        IntPtr collide,
        ref Vec3 origin,
        ref Vec3 angles);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void CollideGetMassCenterDelegate(
        IntPtr self, IntPtr collide, out Vec3 massCenter);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate int CreateDebugMeshDelegate(
        IntPtr self, IntPtr collide, out IntPtr vertices);

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void DestroyDebugMeshDelegate(
        IntPtr self, int vertexCount, IntPtr vertices);

    private static T VFunc<T>(IntPtr instance, int index) where T : class
    {
        IntPtr table = Marshal.ReadIntPtr(instance);
        IntPtr function = Marshal.ReadIntPtr(table, index * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer(function, typeof(T)) as T;
    }

    private static string F(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string VecJson(Vec3 value)
    {
        return "[" + F(value.X) + "," + F(value.Y) + "," + F(value.Z) + "]";
    }

    private static string VertexKey(float x, float y, float z)
    {
        return Math.Round(x, 6).ToString("F6", CultureInfo.InvariantCulture) + "," +
               Math.Round(y, 6).ToString("F6", CultureInfo.InvariantCulture) + "," +
               Math.Round(z, 6).ToString("F6", CultureInfo.InvariantCulture);
    }

    public static string Dump(string cssBinDirectory, string collisionData, string outputJson)
    {
        string bin = Path.GetFullPath(cssBinDirectory);
        string input = Path.GetFullPath(collisionData);
        string output = Path.GetFullPath(outputJson);
        if (!SetDllDirectory(bin))
            throw new InvalidOperationException("SetDllDirectory failed: " + Marshal.GetLastWin32Error());

        IntPtr module = LoadLibrary(Path.Combine(bin, "vphysics.dll"));
        if (module == IntPtr.Zero)
            throw new InvalidOperationException("LoadLibrary(vphysics.dll) failed: " + Marshal.GetLastWin32Error());
        IntPtr createAddress = GetProcAddress(module, "CreateInterface");
        if (createAddress == IntPtr.Zero)
            throw new InvalidOperationException("vphysics.dll has no CreateInterface export");

        CreateInterfaceDelegate create = (CreateInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(
            createAddress, typeof(CreateInterfaceDelegate));
        IntPtr physics = create("VPhysicsCollision007", IntPtr.Zero);
        if (physics == IntPtr.Zero)
            throw new InvalidOperationException("VPhysicsCollision007 is unavailable");

        byte[] payload = File.ReadAllBytes(input);
        IntPtr payloadMemory = Marshal.AllocHGlobal(payload.Length);
        IntPtr collide = IntPtr.Zero;
        VCollide loaded = new VCollide();
        IntPtr debugMemory = IntPtr.Zero;
        int debugVertexCount = 0;
        try
        {
            Marshal.Copy(payload, 0, payloadMemory, payload.Length);
            VCollideLoadDelegate load = VFunc<VCollideLoadDelegate>(physics, 36);
            VCollideUnloadDelegate unload = VFunc<VCollideUnloadDelegate>(physics, 37);
            load(physics, out loaded, 1, payloadMemory, payload.Length, false);
            if (loaded.SolidCount != 1 || loaded.Solids == IntPtr.Zero)
                throw new InvalidOperationException("VCollideLoad did not return one solid");
            collide = Marshal.ReadIntPtr(loaded.Solids);
            if (collide == IntPtr.Zero)
                throw new InvalidOperationException("VCollideLoad returned a null solid");

            CollideScalarDelegate volumeCall = VFunc<CollideScalarDelegate>(physics, 20);
            CollideScalarDelegate areaCall = VFunc<CollideScalarDelegate>(physics, 21);
            CollideGetAabbDelegate aabbCall = VFunc<CollideGetAabbDelegate>(physics, 23);
            CollideGetMassCenterDelegate centerCall = VFunc<CollideGetMassCenterDelegate>(physics, 24);
            CreateDebugMeshDelegate debugCall = VFunc<CreateDebugMeshDelegate>(physics, 40);
            DestroyDebugMeshDelegate destroyDebug = VFunc<DestroyDebugMeshDelegate>(physics, 41);

            float volume = volumeCall(physics, collide);
            float surfaceArea = areaCall(physics, collide);
            Vec3 origin = new Vec3();
            Vec3 angles = new Vec3();
            Vec3 mins;
            Vec3 maxs;
            Vec3 massCenter;
            aabbCall(physics, out mins, out maxs, collide, ref origin, ref angles);
            centerCall(physics, collide, out massCenter);
            debugVertexCount = debugCall(physics, collide, out debugMemory);
            if (debugVertexCount <= 0 || debugMemory == IntPtr.Zero || debugVertexCount % 3 != 0)
                throw new InvalidOperationException("CreateDebugMesh returned an invalid triangle list");

            float[] raw = new float[debugVertexCount * 3];
            Marshal.Copy(debugMemory, raw, 0, raw.Length);
            List<Vec3> unique = new List<Vec3>();
            Dictionary<string, int> indices = new Dictionary<string, int>();
            int[] triangleIndices = new int[debugVertexCount];
            for (int vertex = 0; vertex < debugVertexCount; ++vertex)
            {
                float x = raw[vertex * 3];
                float y = raw[vertex * 3 + 1];
                float z = raw[vertex * 3 + 2];
                string key = VertexKey(x, y, z);
                int index;
                if (!indices.TryGetValue(key, out index))
                {
                    index = unique.Count;
                    indices.Add(key, index);
                    unique.Add(new Vec3 { X = x, Y = y, Z = z });
                }
                triangleIndices[vertex] = index;
            }

            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"schema\": \"cs2-soccermod.source1-vphysics-debug-mesh/1\",");
            json.AppendLine("  \"interface\": \"VPhysicsCollision007\",");
            json.AppendLine("  \"volume\": " + F(volume) + ",");
            json.AppendLine("  \"surfaceArea\": " + F(surfaceArea) + ",");
            json.AppendLine("  \"mins\": " + VecJson(mins) + ",");
            json.AppendLine("  \"maxs\": " + VecJson(maxs) + ",");
            json.AppendLine("  \"massCenter\": " + VecJson(massCenter) + ",");
            json.AppendLine("  \"debugVertexCount\": " + debugVertexCount + ",");
            json.AppendLine("  \"triangleCount\": " + (debugVertexCount / 3) + ",");
            json.AppendLine("  \"uniqueVertexCount\": " + unique.Count + ",");
            json.AppendLine("  \"vertices\": [");
            for (int i = 0; i < unique.Count; ++i)
            {
                json.Append("    " + VecJson(unique[i]));
                json.AppendLine(i + 1 == unique.Count ? "" : ",");
            }
            json.AppendLine("  ],");
            json.AppendLine("  \"triangles\": [");
            for (int i = 0; i < triangleIndices.Length; i += 3)
            {
                json.Append("    [" + triangleIndices[i] + "," + triangleIndices[i + 1] + "," + triangleIndices[i + 2] + "]");
                json.AppendLine(i + 3 == triangleIndices.Length ? "" : ",");
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, json.ToString(), new UTF8Encoding(false));

            destroyDebug(physics, debugVertexCount, debugMemory);
            debugMemory = IntPtr.Zero;
            collide = IntPtr.Zero;
            unload(physics, ref loaded);
            return json.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(payloadMemory);
        }
    }
}
'@

Add-Type -TypeDefinition $source -Language CSharp
[Source1VPhysicsDump]::Dump($CssBinDirectory, $CollisionData, $OutputJson)
