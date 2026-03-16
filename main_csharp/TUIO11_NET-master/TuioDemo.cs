/*
	TUIO C# Demo - part of the reacTIVision project
	Copyright (c) 2005-2016 Martin Kaltenbrunner <martin@tuio.org>

	This program is free software; you can redistribute it and/or modify
	it under the terms of the GNU General Public License as published by
	the Free Software Foundation; either version 2 of the License, or
	(at your option) any later version.

	This program is distributed in the hope that it will be useful,
	but WITHOUT ANY WARRANTY; without even the implied warranty of
	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
	GNU General Public License for more details.

	You should have received a copy of the GNU General Public License
	along with this program; if not, write to the Free Software
	Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
*/

using System;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TUIO;
using System.Net.Sockets;
using System.Text;

	public class TuioDemo : Form , TuioListener
	{
		private TuioClient client;
		private Dictionary<long,TuioObject> objectList;
		private Dictionary<long,TuioCursor> cursorList;
		private Dictionary<long,TuioBlob> blobList;

		public static int width, height;
		private int window_width =  640;
		private int window_height = 480;
		private int window_left = 0;
		private int window_top = 0;
		private int screen_width = Screen.PrimaryScreen.Bounds.Width;
		private int screen_height = Screen.PrimaryScreen.Bounds.Height;

		private bool fullscreen;
		private bool verbose;

		// Face detection status
		private bool faceDetected = false;
		private string lastMessage = "";
		private DateTime lastFaceDetectionTime = DateTime.Now;
		
		// UI Components
		private Label statusLabel;
		private Label markerInfoLabel;
		private Label faceStatusLabel;
		private Label pythonStatusLabel;
        private readonly Dictionary<int, ObjModel> modelCache = new Dictionary<int, ObjModel>();
        private int persistentSymbolID = -1;
        private float persistentAngle = 0.0f;

        Font font = new Font("Segoe UI", 10.0f, FontStyle.Regular);
        Font titleFont = new Font("Segoe UI Semibold", 16.0f, FontStyle.Bold);
        Font statusFont = new Font("Segoe UI Semibold", 12.0f, FontStyle.Bold);
		SolidBrush fntBrush = new SolidBrush(Color.White);
		SolidBrush bgrBrush = new SolidBrush(Color.FromArgb(0,0,64));
		SolidBrush curBrush = new SolidBrush(Color.FromArgb(192, 0, 192));
		SolidBrush objBrush = new SolidBrush(Color.FromArgb(64, 0, 0));
		SolidBrush blbBrush = new SolidBrush(Color.FromArgb(64, 64, 64));
		SolidBrush faceDetectedBrush = new SolidBrush(Color.FromArgb(0, 255, 0));
		SolidBrush faceNotDetectedBrush = new SolidBrush(Color.FromArgb(255, 100, 100));
		Pen curPen = new Pen(new SolidBrush(Color.Blue), 1);

		public TuioDemo(int port) {
		
			verbose = false;
			fullscreen = false;
            width = window_width;
            height = window_height;

			this.ClientSize = new System.Drawing.Size(width, height);
			this.Name = "TuioDemo";
			this.Text = "MuseSense - Interactive Museum Guide";
			
			this.Closing+=new CancelEventHandler(Form_Closing);
			this.KeyDown+=new KeyEventHandler(Form_KeyDown);
            this.Resize+=new EventHandler(Form_Resized);

			this.SetStyle( ControlStyles.AllPaintingInWmPaint |
							ControlStyles.UserPaint |
							ControlStyles.DoubleBuffer, true);

			// Initialize UI labels
			InitializeLabels();
            this.WindowState = FormWindowState.Maximized;
            UpdateLayoutMetrics();

			objectList = new Dictionary<long,TuioObject>(128);
			cursorList = new Dictionary<long,TuioCursor>(128);
			blobList   = new Dictionary<long,TuioBlob>(128);
			
			client = new TuioClient(port);
			client.addTuioListener(this);

			client.connect();
			
			// Start socket thread for Python communication (face detection, gestures)
			Thread socketThread = new Thread(stream);
			socketThread.IsBackground = true;
			socketThread.Start();
    }

        private void Form_Resized(object sender, EventArgs e)
        {
            UpdateLayoutMetrics();
        }

        private void UpdateLayoutMetrics()
        {
            width = this.ClientSize.Width;
            height = this.ClientSize.Height;

            if (faceStatusLabel != null)
                faceStatusLabel.Location = new Point(width - 270, 10);

            if (pythonStatusLabel != null)
                pythonStatusLabel.Location = new Point(width - 270, 42);

            if (markerInfoLabel != null)
            {
                markerInfoLabel.Location = new Point(10, 8);
                markerInfoLabel.Size = new Size(Math.Max(300, width - 20), 28);
            }

            if (statusLabel != null)
                statusLabel.Location = new Point((width - 400) / 2, height - 35);
        }

        private class Vec3
        {
            public float X;
            public float Y;
            public float Z;

            public Vec3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        private class Vec2
        {
            public float U;
            public float V;

            public Vec2(float u, float v)
            {
                U = u;
                V = v;
            }
        }

        private class Face3
        {
            public int A;
            public int B;
            public int C;
            public int TA;
            public int TB;
            public int TC;
            public string MaterialName;

            public Face3(int a, int b, int c, int ta, int tb, int tc, string materialName)
            {
                A = a;
                B = b;
                C = c;
                TA = ta;
                TB = tb;
                TC = tc;
                MaterialName = materialName;
            }
        }

        private class MaterialInfo
        {
            public Color DiffuseColor = Color.LightGray;
            public string TexturePath;
            public Bitmap TextureBitmap;
        }

        private class ObjModel
        {
            public List<Vec3> Vertices = new List<Vec3>();
            public List<Vec2> UVs = new List<Vec2>();
            public List<Face3> Faces = new List<Face3>();
            public Dictionary<string, Color> MaterialColors = new Dictionary<string, Color>();
            public Dictionary<string, Bitmap> MaterialTextures = new Dictionary<string, Bitmap>();
            public float Radius = 1.0f;
        }

        private class FaceRender
        {
            public PointF P1;
            public PointF P2;
            public PointF P3;
            public float Depth;
            public float Shade;
            public Color BaseColor;
        }

        private string GetArtifactName(int symbolID)
        {
            switch (symbolID)
            {
                case 0:
                    return "Mask of Tutankhamun";
                case 1:
                    return "Ramses II statue at the Grand Egyptian Museum";
                case 2:
                    return "King Senwosret III (1836-1818 BC)";
                default:
                    return "Unknown Artifact";
            }
        }

        private bool IsRenderableSymbol(int symbolID)
        {
            return symbolID >= 0 && symbolID <= 2;
        }

        private int GetMarkerModelSize()
        {
            int minDim = Math.Min(width, height);
            return Math.Max(210, (int)(minDim * 0.42f));
        }

        private int GetPersistentModelSize()
        {
            int minDim = Math.Min(width, height);
            return Math.Max(280, (int)(minDim * 0.62f));
        }

        private void PinModel(int symbolID, float angle)
        {
            if (!IsRenderableSymbol(symbolID)) return;
            persistentSymbolID = symbolID;
            persistentAngle = angle;
        }

        private Point GetDisplayCenter()
        {
            return new Point(width / 2, height / 2 + 18);
        }

        private string GetModelObjPath(int symbolID)
        {
            switch (symbolID)
            {
                case 0:
                    return Path.Combine("3d models", "Mask of Tutankhamun", "Mask of Tutankhamun.obj");
                case 1:
                    return Path.Combine("3d models", "Ramses II statue at the Grand Egyptian Museum", "Ramses II statue at the Grand Egyptian Museum .obj");
                case 2:
                    return Path.Combine("3d models", "King Senwosret III (1836-1818 BC)", "King Senwosret III (1836-1818 BC).obj");
                default:
                    return null;
            }
        }

        private int ParseObjIndex(string token, int vertexCount)
        {
            string[] chunks = token.Split('/');
            int idx;
            if (!int.TryParse(chunks[0], out idx)) return -1;

            if (idx > 0) return idx - 1;
            if (idx < 0) return vertexCount + idx;
            return -1;
        }

        private int ParseObjTexIndex(string token, int uvCount)
        {
            string[] chunks = token.Split('/');
            if (chunks.Length < 2 || string.IsNullOrEmpty(chunks[1])) return -1;

            int idx;
            if (!int.TryParse(chunks[1], out idx)) return -1;

            if (idx > 0) return idx - 1;
            if (idx < 0) return uvCount + idx;
            return -1;
        }

        private string ResolveAssetPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;

            string[] candidates = new string[]
            {
                relativePath,
                Path.Combine(Application.StartupPath, relativePath),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }

            return null;
        }

        private ObjModel LoadObjModel(string relativePath)
        {
            string objPath = ResolveAssetPath(relativePath);
            if (string.IsNullOrEmpty(objPath)) return null;

            ObjModel model = new ObjModel();
            string objDirectory = Path.GetDirectoryName(objPath);
            List<string> materialLibraries = new List<string>();
            string currentMaterial = null;

            foreach (string raw in File.ReadAllLines(objPath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.Trim();

                if (line.StartsWith("mtllib "))
                {
                    string libName = line.Substring(7).Trim();
                    if (!string.IsNullOrEmpty(libName)) materialLibraries.Add(libName);
                    continue;
                }

                if (line.StartsWith("usemtl "))
                {
                    currentMaterial = line.Substring(7).Trim();
                    continue;
                }

                if (line.StartsWith("v "))
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        float x;
                        float y;
                        float z;
                        if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) &&
                            float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z))
                        {
                            model.Vertices.Add(new Vec3(x, y, z));
                        }
                    }
                }
                else if (line.StartsWith("vt "))
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        float u;
                        float v;
                        if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out u) &&
                            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        {
                            model.UVs.Add(new Vec2(u, v));
                        }
                    }
                }
                else if (line.StartsWith("f "))
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        int[] indices = new int[parts.Length - 1];
                        int[] texIndices = new int[parts.Length - 1];
                        bool allValid = true;
                        for (int i = 1; i < parts.Length; i++)
                        {
                            int parsed = ParseObjIndex(parts[i], model.Vertices.Count);
                            int parsedTex = ParseObjTexIndex(parts[i], model.UVs.Count);
                            if (parsed < 0 || parsed >= model.Vertices.Count)
                            {
                                allValid = false;
                                break;
                            }
                            indices[i - 1] = parsed;
                            texIndices[i - 1] = parsedTex;
                        }

                        if (!allValid) continue;

                        // Triangulate polygon faces using a fan.
                        for (int i = 1; i < indices.Length - 1; i++)
                        {
                            model.Faces.Add(new Face3(
                                indices[0], indices[i], indices[i + 1],
                                texIndices[0], texIndices[i], texIndices[i + 1],
                                currentMaterial));
                        }
                    }
                }
            }

            Dictionary<string, MaterialInfo> materialInfos = LoadMaterialInfos(objDirectory, materialLibraries);
            foreach (KeyValuePair<string, MaterialInfo> entry in materialInfos)
            {
                Color finalColor = entry.Value.DiffuseColor;
                if (!string.IsNullOrEmpty(entry.Value.TexturePath) && File.Exists(entry.Value.TexturePath))
                    finalColor = GetAverageImageColor(entry.Value.TexturePath, entry.Value.DiffuseColor);

                model.MaterialColors[entry.Key] = finalColor;
                if (entry.Value.TextureBitmap != null)
                    model.MaterialTextures[entry.Key] = entry.Value.TextureBitmap;
            }

            if (model.Vertices.Count == 0 || model.Faces.Count == 0) return null;

            float cx = 0;
            float cy = 0;
            float cz = 0;
            for (int i = 0; i < model.Vertices.Count; i++)
            {
                cx += model.Vertices[i].X;
                cy += model.Vertices[i].Y;
                cz += model.Vertices[i].Z;
            }
            cx /= model.Vertices.Count;
            cy /= model.Vertices.Count;
            cz /= model.Vertices.Count;

            float maxRadius = 0.0001f;
            for (int i = 0; i < model.Vertices.Count; i++)
            {
                model.Vertices[i].X -= cx;
                model.Vertices[i].Y -= cy;
                model.Vertices[i].Z -= cz;

                float r = (float)Math.Sqrt(
                    model.Vertices[i].X * model.Vertices[i].X +
                    model.Vertices[i].Y * model.Vertices[i].Y +
                    model.Vertices[i].Z * model.Vertices[i].Z);

                if (r > maxRadius) maxRadius = r;
            }

            model.Radius = maxRadius;
            return model;
        }

        private Dictionary<string, MaterialInfo> LoadMaterialInfos(string objDirectory, List<string> materialLibraries)
        {
            Dictionary<string, MaterialInfo> result = new Dictionary<string, MaterialInfo>();
            if (string.IsNullOrEmpty(objDirectory) || materialLibraries == null) return result;

            for (int m = 0; m < materialLibraries.Count; m++)
            {
                string mtlPath = Path.Combine(objDirectory, materialLibraries[m]);
                if (!File.Exists(mtlPath)) continue;

                Dictionary<string, MaterialInfo> materialInfos = ParseMtlFile(mtlPath);
                foreach (KeyValuePair<string, MaterialInfo> entry in materialInfos)
                {
                    if (!string.IsNullOrEmpty(entry.Value.TexturePath) && File.Exists(entry.Value.TexturePath))
                    {
                        try
                        {
                            entry.Value.TextureBitmap = new Bitmap(entry.Value.TexturePath);
                        }
                        catch
                        {
                            entry.Value.TextureBitmap = null;
                        }
                    }
                    result[entry.Key] = entry.Value;
                }
            }

            return result;
        }

        private Dictionary<string, MaterialInfo> ParseMtlFile(string mtlPath)
        {
            Dictionary<string, MaterialInfo> materials = new Dictionary<string, MaterialInfo>();
            string mtlDirectory = Path.GetDirectoryName(mtlPath);
            string currentName = null;

            foreach (string raw in File.ReadAllLines(mtlPath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.Trim();
                if (line.StartsWith("#")) continue;

                if (line.StartsWith("newmtl "))
                {
                    currentName = line.Substring(7).Trim();
                    if (!materials.ContainsKey(currentName))
                        materials[currentName] = new MaterialInfo();
                    continue;
                }

                if (string.IsNullOrEmpty(currentName) || !materials.ContainsKey(currentName)) continue;

                if (line.StartsWith("Kd "))
                {
                    string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        float r;
                        float g;
                        float b;
                        if (float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) &&
                            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out g) &&
                            float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b))
                        {
                            int rr = Math.Max(0, Math.Min(255, (int)(r * 255f)));
                            int gg = Math.Max(0, Math.Min(255, (int)(g * 255f)));
                            int bb = Math.Max(0, Math.Min(255, (int)(b * 255f)));
                            materials[currentName].DiffuseColor = Color.FromArgb(220, rr, gg, bb);
                        }
                    }
                    continue;
                }

                if (line.StartsWith("map_Kd "))
                {
                    string textureRel = line.Substring(7).Trim();
                    if (!string.IsNullOrEmpty(textureRel))
                        materials[currentName].TexturePath = Path.Combine(mtlDirectory, textureRel);
                }
            }

            return materials;
        }

        private Color GetAverageImageColor(string imagePath, Color fallback)
        {
            try
            {
                using (Bitmap bitmap = new Bitmap(imagePath))
                {
                    int stepX = Math.Max(1, bitmap.Width / 40);
                    int stepY = Math.Max(1, bitmap.Height / 40);

                    long sumR = 0;
                    long sumG = 0;
                    long sumB = 0;
                    long count = 0;

                    for (int y = 0; y < bitmap.Height; y += stepY)
                    {
                        for (int x = 0; x < bitmap.Width; x += stepX)
                        {
                            Color c = bitmap.GetPixel(x, y);
                            sumR += c.R;
                            sumG += c.G;
                            sumB += c.B;
                            count++;
                        }
                    }

                    if (count == 0) return fallback;
                    int r = (int)(sumR / count);
                    int g = (int)(sumG / count);
                    int b = (int)(sumB / count);
                    return Color.FromArgb(220, r, g, b);
                }
            }
            catch
            {
                return fallback;
            }
        }

        private ObjModel TryGetModel(int symbolID)
        {
            if (modelCache.ContainsKey(symbolID)) return modelCache[symbolID];

            string objPath = GetModelObjPath(symbolID);
            ObjModel loaded = LoadObjModel(objPath);
            modelCache[symbolID] = loaded;
            return loaded;
        }

        private Color GetArtifactColor(int symbolID)
        {
            switch (symbolID)
            {
                case 0:
                    return Color.FromArgb(220, 191, 138, 42);
                case 1:
                    return Color.FromArgb(220, 160, 160, 160);
                case 2:
                    return Color.FromArgb(220, 205, 170, 125);
                default:
                    return Color.LightGray;
            }
        }

        private Vec3 RotateModelVertex(Vec3 v, float yaw, float pitch)
        {
            float cy = (float)Math.Cos(yaw);
            float sy = (float)Math.Sin(yaw);
            float cp = (float)Math.Cos(pitch);
            float sp = (float)Math.Sin(pitch);

            // Rotate around Y (yaw)
            float x1 = v.X * cy + v.Z * sy;
            float z1 = -v.X * sy + v.Z * cy;
            float y1 = v.Y;

            // Rotate around X (pitch)
            float y2 = y1 * cp - z1 * sp;
            float z2 = y1 * sp + z1 * cp;

            return new Vec3(x1, y2, z2);
        }

        private Vec3 RotateFaceNormal(Vec3 normal, float yaw, float pitch)
        {
            return RotateModelVertex(normal, yaw, pitch);
        }

        private void RasterizeTriangle(
            int[] pixels,
            float[] zBuffer,
            int bufferWidth,
            int bufferHeight,
            PointF p1, PointF p2, PointF p3,
            float z1, float z2, float z3,
            Vec2 uv1, Vec2 uv2, Vec2 uv3,
            Bitmap texture,
            Color fallbackColor,
            float lightFactor)
        {
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(p1.X, Math.Min(p2.X, p3.X))));
            int maxX = Math.Min(bufferWidth - 1, (int)Math.Ceiling(Math.Max(p1.X, Math.Max(p2.X, p3.X))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(p1.Y, Math.Min(p2.Y, p3.Y))));
            int maxY = Math.Min(bufferHeight - 1, (int)Math.Ceiling(Math.Max(p1.Y, Math.Max(p2.Y, p3.Y))));

            float denom = ((p2.Y - p3.Y) * (p1.X - p3.X) + (p3.X - p2.X) * (p1.Y - p3.Y));
            if (Math.Abs(denom) < 0.00001f) return;

            bool useTexture = texture != null && uv1 != null && uv2 != null && uv3 != null;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float w1 = ((p2.Y - p3.Y) * (x - p3.X) + (p3.X - p2.X) * (y - p3.Y)) / denom;
                    float w2 = ((p3.Y - p1.Y) * (x - p3.X) + (p1.X - p3.X) * (y - p3.Y)) / denom;
                    float w3 = 1.0f - w1 - w2;

                    if (w1 < 0 || w2 < 0 || w3 < 0) continue;

                    float z = w1 * z1 + w2 * z2 + w3 * z3;
                    int idx = y * bufferWidth + x;
                    if (z <= zBuffer[idx]) continue;
                    zBuffer[idx] = z;

                    Color sourceColor = fallbackColor;
                    if (useTexture)
                    {
                        float u = w1 * uv1.U + w2 * uv2.U + w3 * uv3.U;
                        float v = w1 * uv1.V + w2 * uv2.V + w3 * uv3.V;
                        u = Math.Max(0.0f, Math.Min(1.0f, u));
                        v = Math.Max(0.0f, Math.Min(1.0f, v));

                        int tx = Math.Max(0, Math.Min(texture.Width - 1, (int)(u * (texture.Width - 1))));
                        int ty = Math.Max(0, Math.Min(texture.Height - 1, (int)((1.0f - v) * (texture.Height - 1))));
                        sourceColor = texture.GetPixel(tx, ty);
                    }

                    float exposure = 1.12f;
                    int r = Math.Max(0, Math.Min(255, (int)(sourceColor.R * lightFactor * exposure + 8)));
                    int gr = Math.Max(0, Math.Min(255, (int)(sourceColor.G * lightFactor * exposure + 8)));
                    int b = Math.Max(0, Math.Min(255, (int)(sourceColor.B * lightFactor * exposure + 8)));
                    pixels[idx] = Color.FromArgb(255, r, gr, b).ToArgb();
                }
            }
        }

        private void RenderObjModel(Graphics g, ObjModel model, int centerX, int centerY, float markerAngle, int size, Color baseColor)
        {
            if (model == null) return;

            int renderWidth = size;
            int renderHeight = size;
            int[] pixels = new int[renderWidth * renderHeight];
            float[] zBuffer = new float[renderWidth * renderHeight];
            for (int i = 0; i < zBuffer.Length; i++) zBuffer[i] = float.NegativeInfinity;

            float scale = (size * 0.95f) / model.Radius;
            float yaw = markerAngle + (float)(DateTime.Now.TimeOfDay.TotalSeconds * 0.4);
            float pitch = -0.35f;
            float cameraZ = size * 3.2f;
            float projection = size * 1.7f;

            Vec3[] transformed = new Vec3[model.Vertices.Count];
            PointF[] projected = new PointF[model.Vertices.Count];
            float[] depthValues = new float[model.Vertices.Count];

            for (int i = 0; i < model.Vertices.Count; i++)
            {
                Vec3 tv = RotateModelVertex(model.Vertices[i], yaw, pitch);
                tv.X *= scale;
                tv.Y *= scale;
                tv.Z *= scale;
                transformed[i] = tv;

                float z = tv.Z + cameraZ;
                if (z < 1f) z = 1f;
                depthValues[i] = tv.Z;

                float sx = renderWidth * 0.5f + (tv.X * projection / z);
                float sy = renderHeight * 0.5f - (tv.Y * projection / z);
                projected[i] = new PointF(sx, sy);
            }
            Vec3 lightDir = new Vec3(0.2f, -0.5f, 1.0f);
            float lightLen = (float)Math.Sqrt(lightDir.X * lightDir.X + lightDir.Y * lightDir.Y + lightDir.Z * lightDir.Z);
            lightDir.X /= lightLen;
            lightDir.Y /= lightLen;
            lightDir.Z /= lightLen;

            for (int i = 0; i < model.Faces.Count; i++)
            {
                Face3 f = model.Faces[i];
                Vec3 a = transformed[f.A];
                Vec3 b = transformed[f.B];
                Vec3 c = transformed[f.C];

                float ux = b.X - a.X;
                float uy = b.Y - a.Y;
                float uz = b.Z - a.Z;
                float vx = c.X - a.X;
                float vy = c.Y - a.Y;
                float vz = c.Z - a.Z;

                float nx = uy * vz - uz * vy;
                float ny = uz * vx - ux * vz;
                float nz = ux * vy - uy * vx;

                float nLen = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nLen < 0.00001f) continue;
                nx /= nLen;
                ny /= nLen;
                nz /= nLen;

                Vec3 rotatedNormal = RotateFaceNormal(new Vec3(nx, ny, nz), yaw, pitch);

                float diffuse = Math.Abs(nx * lightDir.X + ny * lightDir.Y + nz * lightDir.Z);
                // Strong ambient term keeps original texture colors from collapsing into dark tones.
                float lightFactor = 0.68f + 0.42f * diffuse;
                if (lightFactor > 1.18f) lightFactor = 1.18f;

                Color faceBaseColor = !string.IsNullOrEmpty(f.MaterialName) && model.MaterialColors.ContainsKey(f.MaterialName)
                    ? model.MaterialColors[f.MaterialName]
                    : baseColor;
                Bitmap faceTexture = null;
                if (!string.IsNullOrEmpty(f.MaterialName) && model.MaterialTextures.ContainsKey(f.MaterialName))
                    faceTexture = model.MaterialTextures[f.MaterialName];

                Vec2 uv1 = (f.TA >= 0 && f.TA < model.UVs.Count) ? model.UVs[f.TA] : null;
                Vec2 uv2 = (f.TB >= 0 && f.TB < model.UVs.Count) ? model.UVs[f.TB] : null;
                Vec2 uv3 = (f.TC >= 0 && f.TC < model.UVs.Count) ? model.UVs[f.TC] : null;

                RasterizeTriangle(
                    pixels,
                    zBuffer,
                    renderWidth,
                    renderHeight,
                    projected[f.A], projected[f.B], projected[f.C],
                    depthValues[f.A], depthValues[f.B], depthValues[f.C],
                    uv1, uv2, uv3,
                    faceTexture,
                    faceBaseColor,
                        lightFactor);
            }

            using (Bitmap frame = new Bitmap(renderWidth, renderHeight, PixelFormat.Format32bppArgb))
            {
                Rectangle rect = new Rectangle(0, 0, renderWidth, renderHeight);
                BitmapData data = frame.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                frame.UnlockBits(data);

                g.DrawImage(frame, centerX - renderWidth / 2, centerY - renderHeight / 2, renderWidth, renderHeight);
            }
        }


    private void InitializeLabels()
    {
        // Face status label (top-right corner)
        faceStatusLabel = new Label();
        faceStatusLabel.AutoSize = false;
        faceStatusLabel.Size = new Size(260, 30);
        faceStatusLabel.Location = new Point(width - 270, 10);
        faceStatusLabel.BackColor = Color.Transparent;
        faceStatusLabel.ForeColor = Color.White;
        faceStatusLabel.Font = statusFont;
        faceStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        faceStatusLabel.Text = "Face: Not Detected";
        this.Controls.Add(faceStatusLabel);

        // Python connection status label (top-right, below face label)
        pythonStatusLabel = new Label();
        pythonStatusLabel.AutoSize = false;
        pythonStatusLabel.Size = new Size(260, 24);
        pythonStatusLabel.Location = new Point(width - 270, 42);
        pythonStatusLabel.BackColor = Color.Transparent;
        pythonStatusLabel.ForeColor = Color.OrangeRed;
        pythonStatusLabel.Font = font;
        pythonStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        pythonStatusLabel.Text = "⬤ Python: Disconnected";
        this.Controls.Add(pythonStatusLabel);
        
        // Marker info label (top-left corner)
        markerInfoLabel = new Label();
        markerInfoLabel.AutoSize = false;
        markerInfoLabel.Size = new Size(620, 28);
        markerInfoLabel.Location = new Point(10, 8);
        markerInfoLabel.BackColor = Color.Transparent;
        markerInfoLabel.ForeColor = Color.White;
        markerInfoLabel.Font = statusFont;
        markerInfoLabel.TextAlign = ContentAlignment.MiddleLeft;
        markerInfoLabel.Text = "Place a marker to begin";
        this.Controls.Add(markerInfoLabel);
        
        // Status label (bottom center)
        statusLabel = new Label();
        statusLabel.AutoSize = false;
        statusLabel.Size = new Size(400, 25);
        statusLabel.Location = new Point((width - 400) / 2, height - 35);
        statusLabel.BackColor = Color.Transparent;
        statusLabel.ForeColor = Color.LightGray;
        statusLabel.Font = font;
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.Text = "Press F1 for fullscreen | ESC to exit | V for verbose";
        this.Controls.Add(statusLabel);
    }

		private void Form_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e) {

 			if ( e.KeyData == Keys.F1) {
	 			if (fullscreen == false) {

					width = screen_width;
					height = screen_height;

					window_left = this.Left;
					window_top = this.Top;

					this.FormBorderStyle = FormBorderStyle.None;
		 			this.Left = 0;
		 			this.Top = 0;
		 			this.Width = screen_width;
		 			this.Height = screen_height;

		 			fullscreen = true;
	 			} else {

					width = window_width;
					height = window_height;

		 			this.FormBorderStyle = FormBorderStyle.Sizable;
		 			this.Left = window_left;
		 			this.Top = window_top;
		 			this.Width = window_width;
		 			this.Height = window_height;

		 			fullscreen = false;
	 			}
 			} else if ( e.KeyData == Keys.Escape) {
				this.Close();

 			} else if ( e.KeyData == Keys.V ) {
 				verbose=!verbose;
 			}

 		}

		private void Form_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
            foreach (ObjModel model in modelCache.Values)
            {
                if (model == null) continue;
                foreach (Bitmap tex in model.MaterialTextures.Values)
                {
                    if (tex != null) tex.Dispose();
                }
            }
            modelCache.Clear();

			client.removeTuioListener(this);

			client.disconnect();
			System.Environment.Exit(0);
		}

		public void addTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Add(o.SessionID,o);
			} 
            PinModel(o.SymbolID, (float)o.Angle);
			if (verbose) Console.WriteLine("add obj "+o.SymbolID+" ("+o.SessionID+") "+o.X+" "+o.Y+" "+o.Angle);
			
			// Update marker info on UI thread
			UpdateMarkerInfo(o.SymbolID, true);
		}

		public void updateTuioObject(TuioObject o) {

			if (verbose) Console.WriteLine("set obj "+o.SymbolID+" "+o.SessionID+" "+o.X+" "+o.Y+" "+o.Angle+" "+o.MotionSpeed+" "+o.RotationSpeed+" "+o.MotionAccel+" "+o.RotationAccel);
            PinModel(o.SymbolID, (float)o.Angle);
            UpdateMarkerInfo(o.SymbolID, true);
		}

		public void removeTuioObject(TuioObject o) {
			lock(objectList) {
				objectList.Remove(o.SessionID);
			}
			if (verbose) Console.WriteLine("del obj "+o.SymbolID+" ("+o.SessionID+")");
			
			// Update marker info on UI thread
			if (objectList.Count == 0) {
                if (persistentSymbolID >= 0)
                    UpdateMarkerInfo(persistentSymbolID, true);
                else
				    UpdateMarkerInfo(-1, false);
            } else {
                int firstSymbol = -1;
                lock(objectList) {
                    foreach (TuioObject remaining in objectList.Values) {
                        firstSymbol = remaining.SymbolID;
                        break;
                    }
                }
                if (firstSymbol >= 0) UpdateMarkerInfo(firstSymbol, true);
			}
		}

		public void addTuioCursor(TuioCursor c) {
			lock(cursorList) {
				cursorList.Add(c.SessionID,c);
			}
			if (verbose) Console.WriteLine("add cur "+c.CursorID + " ("+c.SessionID+") "+c.X+" "+c.Y);
		}

		public void updateTuioCursor(TuioCursor c) {
			if (verbose) Console.WriteLine("set cur "+c.CursorID + " ("+c.SessionID+") "+c.X+" "+c.Y+" "+c.MotionSpeed+" "+c.MotionAccel);
		}

		public void removeTuioCursor(TuioCursor c) {
			lock(cursorList) {
				cursorList.Remove(c.SessionID);
			}
			if (verbose) Console.WriteLine("del cur "+c.CursorID + " ("+c.SessionID+")");
 		}

		public void addTuioBlob(TuioBlob b) {
			lock(blobList) {
				blobList.Add(b.SessionID,b);
			}
			if (verbose) Console.WriteLine("add blb "+b.BlobID + " ("+b.SessionID+") "+b.X+" "+b.Y+" "+b.Angle+" "+b.Width+" "+b.Height+" "+b.Area);
		}

		public void updateTuioBlob(TuioBlob b) {
		
			if (verbose) Console.WriteLine("set blb "+b.BlobID + " ("+b.SessionID+") "+b.X+" "+b.Y+" "+b.Angle+" "+b.Width+" "+b.Height+" "+b.Area+" "+b.MotionSpeed+" "+b.RotationSpeed+" "+b.MotionAccel+" "+b.RotationAccel);
		}

		public void removeTuioBlob(TuioBlob b) {
			lock(blobList) {
				blobList.Remove(b.SessionID);
			}
			if (verbose) Console.WriteLine("del blb "+b.BlobID + " ("+b.SessionID+")");
		}

		public void refresh(TuioTime frameTime) {
			Invalidate();
		}
    class Client
    {
        int byteCT;
        public NetworkStream stream;
        byte[] sendData;
        public TcpClient client;

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
                Console.WriteLine("connection made ! with " + host);
                return true;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Console.WriteLine("Connection Failed: " + e.Message);
                return false;
            }
        }

        public string recieveMessage()
        {
            try
            {
                if (stream == null) return null;
                byte[] receiveBuffer = new byte[1024];
                int bytesReceived = stream.Read(receiveBuffer, 0, 1024);
                Console.WriteLine(bytesReceived);
                string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived);
                Console.WriteLine(data);
                return data;
            }
            catch (System.Exception e)
            {

            }

            return null;
        }

    }
	string msg = "";
	string oldmsg = "";
    public void stream()
    {
        Client c = new Client();
        // Retry connecting until Python server is ready
        while (!c.connectToSocket("localhost", 5000))
        {
            Console.WriteLine("[SOCKET] Retrying connection to Python in 2s...");
            Thread.Sleep(2000);
        }
        UpdatePythonStatus(true);

        while (true)
        {
            msg = c.recieveMessage();
            if (msg == null) {
                // Connection lost — try to reconnect
                Console.WriteLine("[SOCKET] Connection lost. Reconnecting...");
                UpdatePythonStatus(false);
                c = new Client();
                while (!c.connectToSocket("localhost", 5000))
                {
                    Console.WriteLine("[SOCKET] Retrying in 2s...");
                    Thread.Sleep(2000);
                }
                Console.WriteLine("[SOCKET] Reconnected.");
                UpdatePythonStatus(true);
                continue;
            }

            if (msg == "q")
            {
                c.stream.Close();
                c.client.Close();
                Console.WriteLine("Connection Terminated !");
                break;
            }
        


            // Process the message from Python
            Console.WriteLine("Received from Python: " + msg);

            if (msg.Contains("face:detected") || msg.Contains("face detected"))
            {
                faceDetected = true;
                lastFaceDetectionTime = DateTime.Now;
                UpdateFaceStatus(true, msg);
            }
            else if (msg.Contains("face:lost") || msg.Contains("face lost"))
            {
                faceDetected = false;
                UpdateFaceStatus(false);
            }
            else if (msg.Contains("gesture"))
            {
                UpdateStatus("Gesture detected: " + msg);
            }

            if (msg != oldmsg)
            {
                lastMessage = msg;
                oldmsg = msg;
            }

            // Request UI refresh
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(delegate { this.Invalidate(); }));
            }
            else
            {
                this.Invalidate();
            }

            Thread.Sleep(50); // Small delay to prevent CPU overload
        }
    }
    
    private void UpdatePythonStatus(bool connected)
    {
        string text = connected ? "⬤ Python: Connected" : "⬤ Python: Disconnected";
        Color color = connected ? Color.LimeGreen : Color.OrangeRed;
        if (pythonStatusLabel.InvokeRequired)
        {
            pythonStatusLabel.BeginInvoke(new MethodInvoker(delegate {
                pythonStatusLabel.Text = text;
                pythonStatusLabel.ForeColor = color;
            }));
        }
        else
        {
            pythonStatusLabel.Text = text;
            pythonStatusLabel.ForeColor = color;
        }
    }

    private void UpdateFaceStatus(bool detected, string rawMsg = "")
    {
        string label = detected ? "✓ Face Detected" : "Face: Not Detected";
        if (detected && rawMsg.Contains("face:detected:"))
        {
            string personName = rawMsg.Substring(rawMsg.IndexOf("face:detected:") + "face:detected:".Length).Trim();
            if (!string.IsNullOrEmpty(personName))
                label = "✓ " + personName;
        }
        if (faceStatusLabel.InvokeRequired)
        {
            faceStatusLabel.BeginInvoke(new MethodInvoker(delegate {
                faceStatusLabel.Text = label;
                faceStatusLabel.ForeColor = detected ? Color.LightGreen : Color.LightCoral;
            }));
        }
        else
        {
            faceStatusLabel.Text = label;
            faceStatusLabel.ForeColor = detected ? Color.LightGreen : Color.LightCoral;
        }
    }
    
    private void UpdateStatus(string message)
    {
        if (statusLabel.InvokeRequired)
        {
            statusLabel.BeginInvoke(new MethodInvoker(delegate {
                statusLabel.Text = message;
            }));
        }
        else
        {
            statusLabel.Text = message;
        }
    }
    
    private void UpdateMarkerInfo(int symbolID, bool added)
    {
        if (markerInfoLabel.InvokeRequired)
        {
            markerInfoLabel.BeginInvoke(new MethodInvoker(delegate {
                UpdateMarkerInfoInternal(symbolID, added);
            }));
        }
        else
        {
            UpdateMarkerInfoInternal(symbolID, added);
        }
    }
    
    private void UpdateMarkerInfoInternal(int symbolID, bool added)
    {
        if (!added)
        {
            markerInfoLabel.Text = "Place a marker to begin";
            markerInfoLabel.ForeColor = Color.White;
            return;
        }
        
        string artifactName = symbolID >= 0 && symbolID <= 2
            ? GetArtifactName(symbolID)
            : $"Marker #{symbolID}";

        markerInfoLabel.Text = $"Current Artifact  |  ID: {symbolID}  |  {artifactName}";
        markerInfoLabel.ForeColor = Color.Yellow;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
		{
			// Getting the graphics object
			Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (LinearGradientBrush bg = new LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                Color.FromArgb(10, 22, 48),
                Color.FromArgb(2, 8, 22),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, new Rectangle(0,0,width,height));
            }

            // Soft highlight area to make the model stand out.
            using (SolidBrush spotlight = new SolidBrush(Color.FromArgb(38, 120, 180, 230)))
            {
                int s = (int)(Math.Min(width, height) * 0.85f);
                g.FillEllipse(spotlight, width / 2 - s / 2, height / 2 - s / 2 + 20, s, s);
            }

            // Top and bottom translucent bars for better text readability.
            using (SolidBrush topBar = new SolidBrush(Color.FromArgb(120, 5, 12, 28)))
            using (SolidBrush bottomBar = new SolidBrush(Color.FromArgb(110, 5, 12, 28)))
            {
                g.FillRectangle(topBar, new Rectangle(0, 0, width, 54));
                g.FillRectangle(bottomBar, new Rectangle(0, height - 44, width, 44));
            }

			// Draw face detection indicator in top-right corner
			int indicatorSize = 15;
			int indicatorX = width - 280;
			int indicatorY = 45;
			if (faceDetected && (DateTime.Now - lastFaceDetectionTime).TotalSeconds < 5)
			{
				g.FillEllipse(faceDetectedBrush, indicatorX, indicatorY, indicatorSize, indicatorSize);
			}
			else
			{
				g.FillEllipse(faceNotDetectedBrush, indicatorX, indicatorY, indicatorSize, indicatorSize);
			}

			// draw the cursor path
			if (cursorList.Count > 0) {
 			 lock(cursorList) {
			 foreach (TuioCursor tcur in cursorList.Values) {
					List<TuioPoint> path = tcur.Path;
					TuioPoint current_point = path[0];

					for (int i = 0; i < path.Count; i++) {
						TuioPoint next_point = path[i];
						g.DrawLine(curPen, current_point.getScreenX(width), current_point.getScreenY(height), next_point.getScreenX(width), next_point.getScreenY(height));
						current_point = next_point;
					}
					g.FillEllipse(curBrush, current_point.getScreenX(width) - height / 100, current_point.getScreenY(height) - height / 100, height / 50, height / 50);
					g.DrawString(tcur.CursorID + "", font, fntBrush, new PointF(tcur.getScreenX(width) - 10, tcur.getScreenY(height) - 10));
				}
			}
		 }

			// draw the objects
            Point displayCenter = GetDisplayCenter();
            int displaySize = GetPersistentModelSize();

            if (objectList.Count > 0) {
 				lock(objectList) {
					foreach (TuioObject tobj in objectList.Values) {
                    int size = GetMarkerModelSize();
					int ox=0;
					int oy=0;
                    
                    // Draw glow effect around marker to show it's detected
                    ox = tobj.getScreenX(width);
                    oy = tobj.getScreenY(height);
                    

					if (IsRenderableSymbol(tobj.SymbolID))
                    {
                        // Pin selected model/angle, but do not render at marker position.
                        PinModel(tobj.SymbolID, (float)tobj.Angle);
						continue;
                    }
					else //default marker
					{
                        size = GetMarkerModelSize();

                        g.TranslateTransform(ox, oy);
                        g.RotateTransform((float)(tobj.Angle / Math.PI * 180.0f));
                        g.TranslateTransform(-ox, -oy);

                        // Draw colored rectangle based on symbol ID
                        Color markerColor = Color.FromArgb(
                            (tobj.SymbolID * 40) % 255, 
                            (tobj.SymbolID * 80) % 255, 
                            (tobj.SymbolID * 120) % 255
                        );
                        g.FillRectangle(new SolidBrush(markerColor), new Rectangle(ox - size / 2, oy - size / 2, size, size));
                    }

                    // Reset transform so subsequent drawing stays in screen space.
                    g.ResetTransform();
					}
				}
			}

            // Always render selected model at the static display position.
            if (persistentSymbolID >= 0)
            {
                ObjModel persistentModel = TryGetModel(persistentSymbolID);
                if (persistentModel != null)
                {
                    Color persistentColor = GetArtifactColor(persistentSymbolID);
                    RenderObjModel(g, persistentModel, displayCenter.X, displayCenter.Y, persistentAngle, displaySize, persistentColor);
                }
            }

			// draw the blobs
			if (blobList.Count > 0) {
				lock(blobList) {
					foreach (TuioBlob tblb in blobList.Values) {
						int bx = tblb.getScreenX(width);
						int by = tblb.getScreenY(height);
						float bw = tblb.Width*width;
						float bh = tblb.Height*height;

						g.TranslateTransform(bx, by);
						g.RotateTransform((float)(tblb.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-bx, -by);

						g.FillEllipse(blbBrush, bx - bw / 2, by - bh / 2, bw, bh);

						g.TranslateTransform(bx, by);
						g.RotateTransform(-1 * (float)(tblb.Angle / Math.PI * 180.0f));
						g.TranslateTransform(-bx, -by);
						
						g.DrawString(tblb.BlobID + "", font, fntBrush, new PointF(bx, by));
					}
				}
			}
		}

    private void InitializeComponent()
    {
            this.SuspendLayout();
            // 
            // TuioDemo
            // 
            this.ClientSize = new System.Drawing.Size(282, 253);
            this.Name = "TuioDemo";
            this.Load += new System.EventHandler(this.TuioDemo_Load);
            this.ResumeLayout(false);

    }
	 
    private void TuioDemo_Load(object sender, EventArgs e)
    {
       
    }

    public static void Main(String[] argv) {
	 		int port = 0;
			switch (argv.Length) {
				case 1:
					port = int.Parse(argv[0],null);
					if(port==0) goto default;
					break;
				case 0:
					port = 3333;
					break;
				default:
					Console.WriteLine("usage: mono TuioDemo [port]");
					System.Environment.Exit(0);
					break;
			}
			
			TuioDemo app = new TuioDemo(port);
			Application.Run(app);
		}
	}
