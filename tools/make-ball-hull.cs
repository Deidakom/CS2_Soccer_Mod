// Generates a symmetric geodesic ball collision hull as a Source 2 KV2 DMX.
//
// The original XSL B1 hull is a 160-vertex / 316-face polyhedron. The Source 2
// hull cooker cannot represent that face count and silently simplifies it into
// a lopsided shape (volume -18%, bounds off-centre by up to 3 units in Z).
// This tool emits a hull the cooker preserves exactly, keeping the XSL volume
// and the faceted character that gives the ball its CS:S feel.
//
//   dotnet run tools/make-ball-hull.cs -- --frequency 3 --volume 11951.8 --out hull.dmx
//
// Class-I geodesic frequency n yields 20*n^2 faces and 10*n^2+2 vertices.

using System.Globalization;

var ci = CultureInfo.InvariantCulture;
int frequency = 2;
string baseSolid = "ico";
double radius = 14.48;
double targetVolume = double.NaN;
string outPath = "hull.dmx";

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--frequency": frequency = int.Parse(args[i + 1], ci); break;
        case "--base": baseSolid = args[i + 1]; break;
        case "--radius": radius = double.Parse(args[i + 1], ci); break;
        case "--volume": targetVolume = double.Parse(args[i + 1], ci); break;
        case "--out": outPath = args[i + 1]; break;
    }
}

var (verts, faces) = BuildGeodesic(baseSolid, frequency);

if (!double.IsNaN(targetVolume))
{
    // Volume scales with r^3, so one closed-form step lands exactly on target.
    Project(verts, 1.0);
    var unitVolume = SignedVolume(verts, faces);
    radius = Math.Cbrt(targetVolume / unitVolume);
}

Project(verts, radius);

var volume = SignedVolume(verts, faces);
var area = SurfaceArea(verts, faces);
double mnx = verts.Min(v => v.X), mxx = verts.Max(v => v.X);
double mny = verts.Min(v => v.Y), mxy = verts.Max(v => v.Y);
double mnz = verts.Min(v => v.Z), mxz = verts.Max(v => v.Z);
var inradius = faces.Min(f => PlaneDistance(verts[f.A], verts[f.B], verts[f.C]));

Console.WriteLine($"base={baseSolid} frequency={frequency} radius={radius.ToString("F6", ci)} verts={verts.Count} faces={faces.Count} edges={3 * verts.Count - 6}");
Console.WriteLine($"volume={volume.ToString("F4", ci)} surfaceArea={area.ToString("F4", ci)}");
Console.WriteLine($"bounds X {mnx:F4}..{mxx:F4}  Y {mny:F4}..{mxy:F4}  Z {mnz:F4}..{mxz:F4}");
Console.WriteLine($"inradius={inradius.ToString("F4", ci)} roundness={(inradius / radius).ToString("F4", ci)}");

File.WriteAllText(outPath, BuildDmx(verts, faces, ci));
Console.WriteLine($"wrote {outPath}");

static (List<V> verts, List<F> faces) BuildGeodesic(string baseSolid, int frequency)
{
    var t = (1 + Math.Sqrt(5)) / 2;
    List<V> baseVerts;
    List<F> baseFaces;

    switch (baseSolid)
    {
        case "octa":
            baseVerts = new List<V>
            {
                new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
                new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
            };
            baseFaces = new List<F>
            {
                new(0, 2, 4), new(2, 1, 4), new(1, 3, 4), new(3, 0, 4),
                new(2, 0, 5), new(1, 2, 5), new(3, 1, 5), new(0, 3, 5),
            };
            break;

        case "tetra":
            baseVerts = new List<V>
            {
                new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1),
            };
            baseFaces = new List<F>
            {
                new(0, 1, 2), new(0, 3, 1), new(0, 2, 3), new(1, 3, 2),
            };
            break;

        default:
            baseVerts = new List<V>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            baseFaces = new List<F>
            {
                new(0, 11, 5), new(0, 5, 1), new(0, 1, 7), new(0, 7, 10), new(0, 10, 11),
                new(1, 5, 9), new(5, 11, 4), new(11, 10, 2), new(10, 7, 6), new(7, 1, 8),
                new(3, 9, 4), new(3, 4, 2), new(3, 2, 6), new(3, 6, 8), new(3, 8, 9),
                new(4, 9, 5), new(2, 4, 11), new(6, 2, 10), new(8, 6, 7), new(9, 8, 1),
            };
            break;
    }

    // Class-I geodesic: split every icosahedron edge into `frequency` parts and
    // tile each face with the resulting lattice, welding shared lattice points.
    var verts = new List<V>();
    var faces = new List<F>();
    var weld = new Dictionary<(long, long, long), int>();

    int Weld(V v)
    {
        var key = ((long)Math.Round(v.X * 1e6), (long)Math.Round(v.Y * 1e6), (long)Math.Round(v.Z * 1e6));
        if (weld.TryGetValue(key, out var hit))
        {
            return hit;
        }

        verts.Add(v);
        weld[key] = verts.Count - 1;
        return verts.Count - 1;
    }

    foreach (var f in baseFaces)
    {
        var a = baseVerts[f.A];
        var b = baseVerts[f.B];
        var c = baseVerts[f.C];
        var lattice = new int[frequency + 1][];

        for (var i = 0; i <= frequency; i++)
        {
            lattice[i] = new int[frequency - i + 1];
            for (var j = 0; j <= frequency - i; j++)
            {
                var u = (double)i / frequency;
                var w = (double)j / frequency;
                lattice[i][j] = Weld(new V(
                    a.X + (b.X - a.X) * u + (c.X - a.X) * w,
                    a.Y + (b.Y - a.Y) * u + (c.Y - a.Y) * w,
                    a.Z + (b.Z - a.Z) * u + (c.Z - a.Z) * w));
            }
        }

        for (var i = 0; i < frequency; i++)
        {
            for (var j = 0; j < frequency - i; j++)
            {
                faces.Add(new F(lattice[i][j], lattice[i + 1][j], lattice[i][j + 1]));
                if (j < frequency - i - 1)
                {
                    faces.Add(new F(lattice[i + 1][j], lattice[i + 1][j + 1], lattice[i][j + 1]));
                }
            }
        }
    }

    return (verts, faces);
}

static void Project(List<V> verts, double radius)
{
    for (var i = 0; i < verts.Count; i++)
    {
        var v = verts[i];
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        verts[i] = new V(v.X / len * radius, v.Y / len * radius, v.Z / len * radius);
    }
}

static double SignedVolume(List<V> verts, List<F> faces)
{
    var sum = 0.0;
    foreach (var f in faces)
    {
        var a = verts[f.A];
        var b = verts[f.B];
        var c = verts[f.C];
        sum += (a.X * (b.Y * c.Z - b.Z * c.Y)
              - a.Y * (b.X * c.Z - b.Z * c.X)
              + a.Z * (b.X * c.Y - b.Y * c.X)) / 6.0;
    }

    return sum;
}

static double SurfaceArea(List<V> verts, List<F> faces)
{
    var sum = 0.0;
    foreach (var f in faces)
    {
        var a = verts[f.A];
        var b = verts[f.B];
        var c = verts[f.C];
        double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
        double wx = c.X - a.X, wy = c.Y - a.Y, wz = c.Z - a.Z;
        double cx = uy * wz - uz * wy, cy = uz * wx - ux * wz, cz = ux * wy - uy * wx;
        sum += Math.Sqrt(cx * cx + cy * cy + cz * cz) / 2.0;
    }

    return sum;
}

static double PlaneDistance(V a, V b, V c)
{
    double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
    double wx = c.X - a.X, wy = c.Y - a.Y, wz = c.Z - a.Z;
    double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
    var len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
    return Math.Abs(a.X * nx + a.Y * ny + a.Z * nz) / len;
}

static string BuildDmx(List<V> verts, List<F> faces, CultureInfo ci)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("<!-- dmx encoding keyvalues2 4 format model 22 -->");
    sb.AppendLine("\"DmElement\"");
    sb.AppendLine("{");
    sb.AppendLine("    \"id\" \"elementid\" \"38e8d536-992f-529a-a804-f4843d1891c9\"");
    sb.AppendLine("    \"name\" \"string\" \"root\"");
    sb.AppendLine("    \"skeleton\" \"element\" \"cf6fe265-5f27-5ffd-be68-794573a09640\"");
    sb.AppendLine("    \"model\" \"element\" \"cf6fe265-5f27-5ffd-be68-794573a09640\"");
    sb.AppendLine("}");
    sb.AppendLine();
    sb.AppendLine("\"DmeModel\"");
    sb.AppendLine("{");
    sb.AppendLine("    \"id\" \"elementid\" \"cf6fe265-5f27-5ffd-be68-794573a09640\"");
    sb.AppendLine("    \"transform\" \"DmeTransform\"");
    sb.AppendLine("    {");
    sb.AppendLine("        \"id\" \"elementid\" \"2a82d0d8-f01c-5295-8ae5-0ff3095d4173\"");
    sb.AppendLine("        \"position\" \"vector3\" \"0 0 0\"");
    sb.AppendLine("        \"orientation\" \"quaternion\" \"0 0 0 1\"");
    sb.AppendLine("    }");
    sb.AppendLine("    \"shape\" \"element\" \"\"");
    sb.AppendLine("    \"visible\" \"bool\" \"1\"");
    sb.AppendLine("    \"children\" \"element_array\"");
    sb.AppendLine("    [");
    sb.AppendLine("        \"element\" \"8f07c0cd-9d29-5589-983a-dd0a45e6565c\"");
    sb.AppendLine("    ]");
    sb.AppendLine("    \"jointList\" \"element_array\"");
    sb.AppendLine("    [");
    sb.AppendLine("        \"element\" \"8f07c0cd-9d29-5589-983a-dd0a45e6565c\"");
    sb.AppendLine("    ]");
    sb.AppendLine("    \"baseStates\" \"element_array\"");
    sb.AppendLine("    [");
    sb.AppendLine("        \"DmeTransformsList\"");
    sb.AppendLine("        {");
    sb.AppendLine("            \"id\" \"elementid\" \"8bb88d05-347a-55f4-9157-505c71f3893c\"");
    sb.AppendLine("            \"transforms\" \"element_array\"");
    sb.AppendLine("            [");
    sb.AppendLine("                \"DmeTransform\"");
    sb.AppendLine("                {");
    sb.AppendLine("                    \"id\" \"elementid\" \"d4d9bc6c-466d-5b36-81f6-29b71a289426\"");
    sb.AppendLine("                    \"position\" \"vector3\" \"0 0 0\"");
    sb.AppendLine("                    \"orientation\" \"quaternion\" \"0 0 0 1\"");
    sb.AppendLine("                }");
    sb.AppendLine("            ]");
    sb.AppendLine("        }");
    sb.AppendLine("    ]");
    sb.AppendLine("    \"axisSystem\" \"DmeAxisSystem\"");
    sb.AppendLine("    {");
    sb.AppendLine("        \"id\" \"elementid\" \"16b46788-cc8b-51bf-8a1f-79c1c0639a92\"");
    sb.AppendLine("        \"upAxis\" \"int\" \"3\"");
    sb.AppendLine("        \"forwardParity\" \"int\" \"1\"");
    sb.AppendLine("        \"coordSys\" \"int\" \"0\"");
    sb.AppendLine("    }");
    sb.AppendLine("}");
    sb.AppendLine();
    sb.AppendLine("\"DmeDag\"");
    sb.AppendLine("{");
    sb.AppendLine("    \"id\" \"elementid\" \"8f07c0cd-9d29-5589-983a-dd0a45e6565c\"");
    sb.AppendLine("    \"transform\" \"DmeTransform\"");
    sb.AppendLine("    {");
    sb.AppendLine("        \"id\" \"elementid\" \"31198c50-1b08-5f29-893a-f4e3de228be7\"");
    sb.AppendLine("        \"position\" \"vector3\" \"0 0 0\"");
    sb.AppendLine("        \"orientation\" \"quaternion\" \"0 0 0 1\"");
    sb.AppendLine("    }");
    sb.AppendLine("    \"shape\" \"DmeMesh\"");
    sb.AppendLine("    {");
    sb.AppendLine("        \"id\" \"elementid\" \"9d3cecd4-7bb9-5b69-950f-01adf25b2fd4\"");
    sb.AppendLine("        \"bindState\" \"element\" \"\"");
    sb.AppendLine("        \"currentState\" \"element\" \"e440423a-f1f1-5ba0-88b9-d1f48f05fe7e\"");
    sb.AppendLine("        \"baseStates\" \"element_array\"");
    sb.AppendLine("        [");
    sb.AppendLine("            \"element\" \"e440423a-f1f1-5ba0-88b9-d1f48f05fe7e\"");
    sb.AppendLine("        ]");
    sb.AppendLine("        \"deltaStates\" \"element_array\"");
    sb.AppendLine("        [");
    sb.AppendLine("        ]");
    sb.AppendLine("        \"faceSets\" \"element_array\"");
    sb.AppendLine("        [");
    sb.AppendLine("            \"DmeFaceSet\"");
    sb.AppendLine("            {");
    sb.AppendLine("                \"id\" \"elementid\" \"17f84df0-fc8f-5aa4-8dbd-e924451ee40f\"");
    sb.AppendLine("                \"name\" \"string\" \"ball hull faces\"");
    sb.AppendLine("                \"faces\" \"int_array\"");
    sb.AppendLine("                [");
    var flat = new List<int>(faces.Count * 4);
    foreach (var f in faces)
    {
        flat.Add(f.A);
        flat.Add(f.B);
        flat.Add(f.C);
        flat.Add(-1);
    }

    for (var i = 0; i < flat.Count; i++)
    {
        sb.AppendLine($"                    \"{flat[i].ToString(ci)}\"{(i == flat.Count - 1 ? "" : ",")}");
    }

    sb.AppendLine("                ]");
    sb.AppendLine("                \"material\" \"element\" \"\"");
    sb.AppendLine("            }");
    sb.AppendLine("        ]");
    sb.AppendLine("        \"visible\" \"bool\" \"1\"");
    sb.AppendLine("    }");
    sb.AppendLine("    \"visible\" \"bool\" \"1\"");
    sb.AppendLine("    \"children\" \"element_array\"");
    sb.AppendLine("    [");
    sb.AppendLine("    ]");
    sb.AppendLine("}");
    sb.AppendLine();
    sb.AppendLine("\"DmeVertexData\"");
    sb.AppendLine("{");
    sb.AppendLine("    \"id\" \"elementid\" \"e440423a-f1f1-5ba0-88b9-d1f48f05fe7e\"");
    sb.AppendLine("    \"name\" \"string\" \"bind\"");
    sb.AppendLine("    \"vertexFormat\" \"string_array\"");
    sb.AppendLine("    [");
    sb.AppendLine("        \"position$0\"");
    sb.AppendLine("    ]");
    sb.AppendLine("    \"jointCount\" \"int\" \"0\"");
    sb.AppendLine("    \"flipVCoordinates\" \"bool\" \"0\"");
    sb.AppendLine("    \"position$0\" \"vector3_array\"");
    sb.AppendLine("    [");
    for (var i = 0; i < verts.Count; i++)
    {
        var v = verts[i];
        var s = $"{v.X.ToString("R", ci)} {v.Y.ToString("R", ci)} {v.Z.ToString("R", ci)}";
        sb.AppendLine($"        \"{s}\"{(i == verts.Count - 1 ? "" : ",")}");
    }

    sb.AppendLine("    ]");
    sb.AppendLine("    \"position$0Indices\" \"int_array\"");
    sb.AppendLine("    [");
    for (var i = 0; i < verts.Count; i++)
    {
        sb.AppendLine($"        \"{i.ToString(ci)}\"{(i == verts.Count - 1 ? "" : ",")}");
    }

    sb.AppendLine("    ]");
    sb.AppendLine("}");
    return sb.ToString();
}

record struct V(double X, double Y, double Z);

record struct F(int A, int B, int C);
